using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using TelaRec.Core.Contracts;
using TelaRec.Core.Recording;
using TelaRec.Core.Reliability;

namespace TelaRec.Infrastructure.Recording;

public sealed class RecordingService : IRecordingService, IAsyncDisposable
{
    private sealed record Packet(TimeSpan Timestamp, VideoFrame? Video = null, AudioFrame? Audio = null);
    private readonly IVideoCaptureService _capture;
    private readonly IVideoEncoderService _encoder;
    private readonly IAudioCaptureService _audio;
    private readonly ILogger<RecordingService> _logger;
    private readonly IRecordingExporterFactory? _exporters;
    private readonly IFrameScaler _scaler;
    private readonly IWebcamCaptureService? _webcam;
    private readonly IVideoFrameComposer _composer;
    private readonly IRecordingSessionJournal? _journal;
    private readonly IRecordingDiagnostics? _diagnostics;
    private readonly RecordingStateMachine _machine = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _disposeSync = new();
    private Task? _disposeTask;
    private Channel<Packet>? _queue;
    private Task? _encodeTask;
    private Task? _failedCleanupTask;
    private Exception? _encodeError;
    private string? _temporaryPath;
    private string? _outputPath;
    private long _originTicks;
    private long _pauseStartedTicks;
    private long _pausedTicks;
    private bool _paused;
    private bool _audioEnabled;
    private int _fps;
    private RecordingProfile _profile = RecordingProfile.Mvp;
    private Guid _sessionId;
    private RecordingStatus _status = RecordingStatus.Initial;
    public RecordingStatus Status => _status;
    public event EventHandler<RecordingStatus>? StatusChanged;

    public RecordingService(IVideoCaptureService capture, IVideoEncoderService encoder, IAudioCaptureService audio, ILogger<RecordingService> logger, IRecordingExporterFactory? exporters = null, IFrameScaler? scaler = null, IWebcamCaptureService? webcam = null, IVideoFrameComposer? composer = null, IRecordingSessionJournal? journal = null, IRecordingDiagnostics? diagnostics = null)
    {
        _capture = capture; _encoder = encoder; _audio = audio; _logger = logger; _exporters = exporters; _scaler = scaler ?? new BgraFrameScaler(); _webcam = webcam; _composer = composer ?? new VideoFrameComposer(); _journal = journal; _diagnostics = diagnostics;
        _capture.FrameCaptured += OnFrameCaptured;
        _capture.CaptureFailed += OnVideoFailed;
        _audio.AudioCaptured += OnAudioCaptured;
        _audio.CaptureFailed += OnAudioFailed;
        if (_webcam is not null) { _webcam.FrameCaptured += OnWebcamFrame; _webcam.CaptureFailed += OnWebcamFailed; }
    }
    private void OnWebcamFrame(object? sender, VideoFrame frame)
    {
        if (_machine.State is not (RecordingState.Starting or RecordingState.Recording)) return;
        _composer.UpdateWebcamFrame(frame with { Timestamp = Normalize(frame.Timestamp) });
    }
    private void OnWebcamFailed(object? sender, Exception exception)
    {
        _logger.LogWarning(exception, "Webcam indisponível; continuando somente com captura de tela");
        if (_machine.State is RecordingState.Recording or RecordingState.Paused)
            Publish(_machine.State, "Webcam indisponível; gravação de tela continua.");
    }
    private void OnAudioFailed(object? sender, Exception exception)
    {
        _logger.LogWarning(exception, "Áudio indisponível; o vídeo continuará");
        if (_machine.State is RecordingState.Recording or RecordingState.Paused)
            Publish(_machine.State, "Áudio indisponível; gravação de vídeo continua.");
    }
    private void OnVideoFailed(object? sender, Exception exception)
    {
        _logger.LogError(exception, "Captura de vídeo interrompida");
        var wasActive = _machine.State is RecordingState.Recording or RecordingState.Paused;
        if (_machine.State is RecordingState.Starting or RecordingState.Recording or RecordingState.Paused or RecordingState.Stopping)
            Fail(exception.Message);
        if (wasActive) ScheduleFailedCleanup();
    }

    public async Task StartAsync(RecordingRequest request, CancellationToken cancellationToken = default)
    {
        if (_failedCleanupTask is { } failedCleanup) await failedCleanup.ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var started = false;
        try
        {
            if (_machine.State is not (RecordingState.Idle or RecordingState.Completed or RecordingState.Failed))
                throw new InvalidOperationException("Já existe uma gravação em andamento.");
            _profile = request.EffectiveProfile;
            if (!_profile.IsValid) throw new ArgumentException("Perfil de gravação inválido.", nameof(request));
            if (!string.Equals(Path.GetExtension(request.OutputPath), _profile.FileExtension, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"A saída deve usar a extensão {_profile.FileExtension}.", nameof(request));
            if (_profile.Container != RecordingContainer.Mp4 && _exporters is null)
                throw new NotSupportedException("Nenhum backend de pós-processamento foi configurado.");
            if (_machine.State == RecordingState.Failed && _queue is not null)
                await CleanupAsync().ConfigureAwait(false);
            _failedCleanupTask = null;
            _machine.MoveTo(RecordingState.Starting);
            started = true;
            Publish(RecordingState.Starting);
            _outputPath = Path.GetFullPath(request.OutputPath);
            _temporaryPath = Path.Combine(Path.GetDirectoryName(_outputPath)!, $".{Path.GetFileNameWithoutExtension(_outputPath)}.{Guid.NewGuid():N}.partial.mp4");
            _sessionId = Guid.NewGuid();
            if (_journal is not null) await _journal.CreateAsync(new RecordingJournalEntry(_sessionId, _temporaryPath, _outputPath, DateTimeOffset.UtcNow, RecordingState.Starting, null), cancellationToken).ConfigureAwait(false);
            _queue = Channel.CreateBounded<Packet>(new BoundedChannelOptions(32) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = false });
            _encodeError = null; _paused = false; _pausedTicks = 0; _pauseStartedTicks = 0;
            _audioEnabled = request.SystemAudioEnabled && _profile.Container != RecordingContainer.Gif; _fps = _profile.FramesPerSecond;
            _originTicks = NowTicks();
            _composer.Configure(request.EffectiveOverlays);
            await _encoder.StartAsync(request with { OutputPath = _temporaryPath, FramesPerSecond = _profile.FramesPerSecond, Profile = RecordingProfile.Mvp with { FramesPerSecond = _profile.FramesPerSecond, Bitrate = _profile.Bitrate, Width = _profile.Width, Height = _profile.Height } }, cancellationToken).ConfigureAwait(false);
            _encodeTask = Task.Run(() => EncodeLoopAsync(_queue.Reader), CancellationToken.None);
            await _capture.StartAsync(request, cancellationToken).ConfigureAwait(false);
            if (request.EffectiveOverlays.Webcam.Enabled && _webcam is not null)
            {
                try { await _webcam.StartAsync(request.EffectiveOverlays.Webcam, cancellationToken).ConfigureAwait(false); }
                catch (Exception exception) { _logger.LogWarning(exception, "Webcam não iniciada; continuando somente com tela"); }
            }
            string? audioWarning = null;
            if (_audioEnabled)
            {
                try { await _audio.StartAsync(cancellationToken).ConfigureAwait(false); }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, "Áudio não iniciado; continuando somente com vídeo");
                    audioWarning = "Áudio indisponível; gravação de vídeo continua.";
                }
            }
            if (_encodeError is not null) throw new InvalidOperationException("Encoder falhou ao iniciar.", _encodeError);
            _machine.MoveTo(RecordingState.Recording);
            Publish(RecordingState.Recording, audioWarning);
            _logger.LogInformation("Gravação iniciada: {OutputPath}", _outputPath);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Não foi possível iniciar a gravação");
            if (started)
            {
                Fail(exception.Message);
                await CleanupAsync().ConfigureAwait(false);
            }
            throw;
        }
        finally { _gate.Release(); }
    }
    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { _machine.MoveTo(RecordingState.Paused); _pauseStartedTicks = NowTicks(); _paused = true; Publish(RecordingState.Paused); }
        finally { _gate.Release(); }
    }
    public async Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { _machine.MoveTo(RecordingState.Recording); _pausedTicks += NowTicks() - _pauseStartedTicks; _paused = false; Publish(RecordingState.Recording); }
        finally { _gate.Release(); }
    }
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_machine.State is not (RecordingState.Recording or RecordingState.Paused or RecordingState.Starting or RecordingState.Failed)) return;
            if (_machine.State != RecordingState.Failed) { _machine.MoveTo(RecordingState.Stopping); Publish(RecordingState.Stopping); }
            _logger.LogDebug("Parando WASAPI");
            await _audio.StopAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("Parando WGC");
            await _capture.StopAsync(cancellationToken).ConfigureAwait(false);
            if (_webcam is not null) await _webcam.StopAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("Esvaziando fila A/V");
            _queue?.Writer.TryComplete();
            if (_encodeTask is not null) await _encodeTask.ConfigureAwait(false);
            _logger.LogDebug("Finalizando Media Foundation");
            if (_encodeError is not null) throw new InvalidOperationException("Falha no encoder.", _encodeError);
            await _encoder.CompleteAsync(cancellationToken).ConfigureAwait(false);
            if (_temporaryPath is null || _outputPath is null || !File.Exists(_temporaryPath))
                throw new InvalidOperationException("Arquivo MP4 temporário não foi produzido.");
            if (_profile.Container == RecordingContainer.Mp4)
                File.Move(_temporaryPath, _outputPath, true);
            else
                await _exporters!.GetExporter(_profile).ExportAsync(_temporaryPath, _outputPath, _profile, cancellationToken).ConfigureAwait(false);
            if (_journal is not null) await _journal.CompleteAsync(_sessionId, cancellationToken).ConfigureAwait(false);
            _machine.MoveTo(RecordingState.Completed);
            Publish(RecordingState.Completed);
            _logger.LogInformation("Gravação concluída: {OutputPath}", _outputPath);
        }
        catch (Exception exception) { _logger.LogError(exception, "Falha ao finalizar gravação"); Fail(exception.Message); throw; }
        finally { await CleanupAsync().ConfigureAwait(false); _gate.Release(); }
    }
    private void OnFrameCaptured(object? sender, VideoFrame frame)
    {
        if (_paused || _machine.State is not (RecordingState.Recording or RecordingState.Starting)) return;
        var timestamp = Normalize(frame.Timestamp);
        var scaled = _scaler.Scale(frame with { Timestamp = timestamp }, _profile.Width, _profile.Height);
        _queue?.Writer.TryWrite(new Packet(timestamp, Video: _composer.Compose(scaled)));
    }
    private void OnAudioCaptured(object? sender, AudioFrame frame)
    {
        if (_paused || _machine.State != RecordingState.Recording) return;
        var timestamp = Normalize(frame.Timestamp);
        _queue?.Writer.TryWrite(new Packet(timestamp, Audio: frame with { Timestamp = timestamp }));
    }
    private TimeSpan Normalize(TimeSpan absolute) => TimeSpan.FromTicks(Math.Max(0, absolute.Ticks - _originTicks - Interlocked.Read(ref _pausedTicks)));
    private async Task EncodeLoopAsync(ChannelReader<Packet> reader)
    {
        var pending = new PriorityQueue<Packet, long>();
        var videoStarted = false;
        long audioCursor = 0;
        long videoEnd = 0;
        try
        {
            await foreach (var packet in reader.ReadAllAsync().ConfigureAwait(false))
            {
                if (!videoStarted)
                {
                    if (packet.Video is null) { pending.Enqueue(packet, packet.Timestamp.Ticks); if (pending.Count > 32) pending.Dequeue(); continue; }
                    var began = Stopwatch.GetTimestamp();
                    await _encoder.EncodeAsync(packet.Video).ConfigureAwait(false);
                    LogSlowEncode(began, packet.Video.Timestamp);
                    videoEnd = packet.Video.Timestamp.Ticks + TimeSpan.TicksPerSecond / _fps;
                    videoStarted = true;
                    pending.Clear();
                    continue;
                }
                pending.Enqueue(packet, packet.Timestamp.Ticks);
                var watermark = Math.Max(0, NowTicks() - _originTicks - _pausedTicks - TimeSpan.TicksPerSecond / 5);
                while (pending.TryPeek(out _, out var timestamp) && (timestamp <= watermark || pending.Count > 64))
                    await WriteAsync(pending.Dequeue()).ConfigureAwait(false);
            }
            if (!videoStarted) throw new InvalidOperationException("Nenhum quadro de vídeo foi capturado.");
            while (pending.Count > 0) await WriteAsync(pending.Dequeue()).ConfigureAwait(false);
            _logger.LogDebug("Fila drenada: vídeo {VideoEnd} ticks, áudio {AudioCursor} ticks", videoEnd, audioCursor);
            if (_audioEnabled) await FillSilenceAsync(videoEnd).ConfigureAwait(false);
            _logger.LogDebug("Silêncio preenchido até {AudioCursor} ticks", audioCursor);
        }
        catch (Exception exception)
        {
            _encodeError = exception;
            _logger.LogError(exception, "Falha na fila A/V");
            var wasActive = _machine.State is RecordingState.Recording or RecordingState.Paused;
            Fail(exception.Message);
            if (wasActive) ScheduleFailedCleanup();
        }
        async Task WriteAsync(Packet packet)
        {
            if (packet.Video is { } video)
            {
                var began = Stopwatch.GetTimestamp();
                await _encoder.EncodeAsync(video).ConfigureAwait(false);
                LogSlowEncode(began, video.Timestamp);
                videoEnd = Math.Max(videoEnd, video.Timestamp.Ticks + TimeSpan.TicksPerSecond / _fps);
                return;
            }
            if (packet.Audio is not { } audio || !_audioEnabled) return;
            await FillSilenceAsync(packet.Timestamp.Ticks).ConfigureAwait(false);
            var timestamp = Math.Max(packet.Timestamp.Ticks, audioCursor);
            await _encoder.EncodeAudioAsync(audio with { Timestamp = TimeSpan.FromTicks(timestamp) }).ConfigureAwait(false);
            audioCursor = timestamp + audio.Pcm16.Length * TimeSpan.TicksPerSecond / (48_000 * 4);
        }
        async Task FillSilenceAsync(long untilTicks)
        {
            while (audioCursor + TimeSpan.TicksPerSecond / 100 < untilTicks)
            {
                var frames = (int)Math.Min(2048, (untilTicks - audioCursor) * 48_000 / TimeSpan.TicksPerSecond);
                if (frames <= 0) break;
                await _encoder.EncodeAudioAsync(new AudioFrame(new byte[frames * 4], 48_000, 2, TimeSpan.FromTicks(audioCursor))).ConfigureAwait(false);
                audioCursor += frames * TimeSpan.TicksPerSecond / 48_000;
            }
        }
    }
    private void ScheduleFailedCleanup()
    {
        if (_failedCleanupTask is not null) return;
        _failedCleanupTask = Task.Run(async () =>
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try { if (_machine.State == RecordingState.Failed && _queue is not null) await CleanupAsync().ConfigureAwait(false); }
            catch (Exception cleanupError) { _logger.LogError(cleanupError, "Falha ao limpar gravação interrompida"); }
            finally { _gate.Release(); }
        });
    }
    private void LogSlowEncode(long began, TimeSpan timestamp)
    {
        var elapsed = Stopwatch.GetElapsedTime(began);
        if (elapsed > TimeSpan.FromMilliseconds(200))
            _logger.LogWarning("Encoder lento no quadro {Timestamp}: {Elapsed}", timestamp, elapsed);
    }
    private void Publish(RecordingState state, string? error = null)
    {
        var elapsed = _originTicks == 0 ? TimeSpan.Zero : TimeSpan.FromTicks(Math.Max(0, NowTicks() - _originTicks - _pausedTicks - (_paused ? NowTicks() - _pauseStartedTicks : 0)));
        _status = new RecordingStatus(state, elapsed, error);
        StatusChanged?.Invoke(this, _status);
        if (error is not null) _diagnostics?.Report(state == RecordingState.Failed ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning, $"recording.{state}".ToLowerInvariant(), error);
        if (_journal is not null && _sessionId != Guid.Empty && state is not RecordingState.Completed)
            _ = PersistJournalStateAsync(state, error);
    }
    private void Fail(string error)
    {
        if (_machine.State != RecordingState.Failed) { _machine.MoveTo(RecordingState.Failed); Publish(RecordingState.Failed, error); }
    }
    private async Task PersistJournalStateAsync(RecordingState state, string? error)
    {
        try { await _journal!.UpdateAsync(_sessionId, state, error).ConfigureAwait(false); }
        catch (Exception exception) { _logger.LogWarning(exception, "Não foi possível atualizar journal da sessão {SessionId}", _sessionId); }
    }
    private async Task CleanupAsync()
    {
        _queue?.Writer.TryComplete();
        try { await _capture.StopAsync().ConfigureAwait(false); } catch (Exception e) { _logger.LogWarning(e, "Falha ao liberar captura"); }
        try { if (_webcam is not null) await _webcam.StopAsync().ConfigureAwait(false); } catch (Exception e) { _logger.LogWarning(e, "Falha ao liberar webcam"); }
        try { await _audio.StopAsync().ConfigureAwait(false); } catch (Exception e) { _logger.LogWarning(e, "Falha ao liberar áudio"); }
        if (_encodeTask is not null) await _encodeTask.ConfigureAwait(false);
        await _encoder.DisposeAsync().ConfigureAwait(false);
        if (_temporaryPath is not null && File.Exists(_temporaryPath)) File.Delete(_temporaryPath);
        _queue = null; _encodeTask = null; _temporaryPath = null; _outputPath = null;
    }
    private static long NowTicks() => (long)(Stopwatch.GetTimestamp() * (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency);
    public ValueTask DisposeAsync()
    {
        lock (_disposeSync) _disposeTask ??= DisposeCoreAsync();
        return new ValueTask(_disposeTask);
    }

    private async Task DisposeCoreAsync()
    {
        if (_failedCleanupTask is not null) await _failedCleanupTask.ConfigureAwait(false);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await CleanupAsync().ConfigureAwait(false);
            _capture.FrameCaptured -= OnFrameCaptured;
            _capture.CaptureFailed -= OnVideoFailed;
            _audio.AudioCaptured -= OnAudioCaptured;
            _audio.CaptureFailed -= OnAudioFailed;
            if (_webcam is not null) { _webcam.FrameCaptured -= OnWebcamFrame; _webcam.CaptureFailed -= OnWebcamFailed; await _webcam.DisposeAsync().ConfigureAwait(false); }
            await _capture.DisposeAsync().ConfigureAwait(false);
            await _audio.DisposeAsync().ConfigureAwait(false);
            if (_composer is IDisposable disposableComposer) disposableComposer.Dispose();
        }
        finally { _gate.Release(); _gate.Dispose(); }
    }
}
