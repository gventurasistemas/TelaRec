using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using TelaRec.Core.Contracts;
using TelaRec.Core.Recording;
using TelaRec.Core.Reliability;

namespace TelaRec.Infrastructure.Recording;

public sealed class RecordingService : IRecordingService, IRecordingTelemetrySource, IAsyncDisposable
{
    /// <summary>
    /// A fila comporta a janela de reordenação de áudio sem permitir que ela ocupe a
    /// reserva de vídeo. Quadros de vídeo continuam limitados por quantidade e bytes.
    /// </summary>
    internal const int QueueCapacity = 64;
    internal const int VideoQueueHighWatermark = 6;
    internal const long MaximumQueuedVideoBytes = 64L * 1024 * 1024;

    private sealed record Packet(
        TimeSpan Timestamp,
        VideoFrame? Video = null,
        AudioFrame? Audio = null,
        AudioSource? AudioSource = null,
        long EnqueuedAtTimestamp = 0);
    private readonly IVideoCaptureService _capture;
    private readonly IVideoEncoderService _encoder;
    private readonly IAudioCaptureService _audio;
    private readonly IMicrophoneCaptureService? _microphone;
    private readonly ILogger<RecordingService> _logger;
    private readonly IRecordingExporterFactory? _exporters;
    private readonly IFrameScaler _scaler;
    private readonly IWebcamCaptureService? _webcam;
    private readonly IVideoFrameComposer _composer;
    private readonly IRecordingSessionJournal? _journal;
    private readonly IRecordingDiagnostics? _diagnostics;
    private readonly IClickCaptureService? _clickCapture;
    private readonly RecordingStateMachine _machine = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _disposeSync = new();
    private readonly object _journalSync = new();
    private readonly object _queueMetricsSync = new();
    private Task? _disposeTask;
    private Channel<Packet>? _queue;
    private Task? _encodeTask;
    private Task? _failedCleanupTask;
    private Task _journalUpdateTail = Task.CompletedTask;
    private Exception? _encodeError;
    private string? _temporaryPath;
    private string? _preservedPartialPath;
    private string? _outputPath;
    private long _originTicks;
    private long _pauseStartedTicks;
    private long _pausedTicks;
    private bool _paused;
    private bool _audioEnabled;
    private bool _systemAudioActive;
    private bool _microphoneAudioActive;
    private AudioMixSettings _audioMix = AudioMixSettings.None;
    private int _fps;
    private RecordingProfile _profile = RecordingProfile.Mvp;
    private Guid _sessionId;
    private bool _journalCreated;
    private long _queueDroppedVideoFrames;
    private long _queueDroppedAudioPackets;
    private long _encodedFrames;
    private long _encodeDurationTicks;
    private long _receivedVideoFrames;
    private long _queueLatencyTicks;
    private long _queueLatencySamples;
    private long _managedAllocationStart;
    private int _queueDepth;
    private int _queuedVideoPackets;
    private int _peakQueueDepth;
    private int _peakReorderQueueDepth;
    private long _queuedVideoBytes;
    private long _peakQueuedVideoBytes;
    private RecordingStatus _status = RecordingStatus.Initial;
    public RecordingStatus Status => _status;
    public RecordingPerformanceTelemetry CurrentTelemetry
    {
        get
        {
            var capture = _capture as IVideoCaptureTelemetrySource;
            var encoded = Interlocked.Read(ref _encodedFrames);
            var average = encoded == 0 ? TimeSpan.Zero : TimeSpan.FromTicks(Interlocked.Read(ref _encodeDurationTicks) / encoded);
            var latencySamples = Interlocked.Read(ref _queueLatencySamples);
            var queueLatency = latencySamples == 0 ? TimeSpan.Zero : TimeSpan.FromTicks(Interlocked.Read(ref _queueLatencyTicks) / latencySamples);
            return new RecordingPerformanceTelemetry(
                capture?.CapturedFrames ?? Interlocked.Read(ref _receivedVideoFrames),
                capture?.RateLimitedFrames ?? 0,
                capture?.RepeatedFrames ?? 0,
                Interlocked.Read(ref _queueDroppedVideoFrames),
                Interlocked.Read(ref _queueDroppedAudioPackets),
                encoded,
                average,
                (_encoder as IVideoEncoderTelemetrySource)?.UsingHardwareEncoder ?? false)
            {
                QueueCapacity = QueueCapacity,
                QueueDepth = Volatile.Read(ref _queueDepth),
                PeakQueueDepth = Volatile.Read(ref _peakQueueDepth),
                PeakReorderQueueDepth = Volatile.Read(ref _peakReorderQueueDepth),
                QueuedVideoBytes = Interlocked.Read(ref _queuedVideoBytes),
                PeakQueuedVideoBytes = Interlocked.Read(ref _peakQueuedVideoBytes),
                MaximumQueuedVideoBytes = MaximumQueuedVideoBytes,
                AverageQueueLatency = queueLatency,
                ManagedAllocatedBytes = Math.Max(0, GC.GetTotalAllocatedBytes(precise: false) - Interlocked.Read(ref _managedAllocationStart))
            };
        }
    }
    public event EventHandler<RecordingStatus>? StatusChanged;

    public RecordingService(IVideoCaptureService capture, IVideoEncoderService encoder, IAudioCaptureService audio, ILogger<RecordingService> logger, IRecordingExporterFactory? exporters = null, IFrameScaler? scaler = null, IWebcamCaptureService? webcam = null, IVideoFrameComposer? composer = null, IRecordingSessionJournal? journal = null, IRecordingDiagnostics? diagnostics = null, IClickCaptureService? clickCapture = null, IMicrophoneCaptureService? microphone = null)
    {
        _capture = capture; _encoder = encoder; _audio = audio; _logger = logger; _exporters = exporters; _scaler = scaler ?? new BgraFrameScaler(); _webcam = webcam; _composer = composer ?? new VideoFrameComposer(); _journal = journal; _diagnostics = diagnostics; _clickCapture = clickCapture; _microphone = microphone;
        _capture.FrameCaptured += OnFrameCaptured;
        _capture.CaptureFailed += OnVideoFailed;
        _audio.AudioCaptured += OnSystemAudioCaptured;
        _audio.CaptureFailed += OnSystemAudioFailed;
        if (_microphone is not null) { _microphone.AudioCaptured += OnMicrophoneAudioCaptured; _microphone.CaptureFailed += OnMicrophoneFailed; }
        if (_webcam is not null) { _webcam.FrameCaptured += OnWebcamFrame; _webcam.CaptureFailed += OnWebcamFailed; }
        if (_clickCapture is not null) _clickCapture.ClickCaptured += OnClickCaptured;
    }
    private void OnClickCaptured(object? sender, ClickEvent click)
    {
        if (_paused || _machine.State != RecordingState.Recording) return;
        var timestamp = Normalize(click.Timestamp);
        _composer.AddClickEffect(click with { Timestamp = timestamp });
        _logger.LogDebug("Efeito de clique em ({X},{Y}) no instante {Timestamp}", click.ScreenX, click.ScreenY, timestamp);
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
    private void OnSystemAudioFailed(object? sender, Exception exception)
    {
        _systemAudioActive = false;
        _logger.LogWarning(exception, "Áudio do sistema indisponível; o vídeo continuará");
        if (_machine.State is RecordingState.Recording or RecordingState.Paused)
            Publish(_machine.State, "Áudio indisponível (sistema); gravação continua.");
    }
    private void OnMicrophoneFailed(object? sender, Exception exception)
    {
        _microphoneAudioActive = false;
        _logger.LogWarning(exception, "Microfone indisponível; as demais fontes continuam");
        if (_machine.State is RecordingState.Recording or RecordingState.Paused)
            Publish(_machine.State, "Microfone indisponível; gravação continua com as demais fontes.");
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
            if (_profile.Container != RecordingContainer.Mp4)
            {
                var exporter = _exporters!.GetExporter(_profile);
                if (exporter is IRecordingExportPreflight preflight)
                {
                    var availability = await preflight.CheckAvailabilityAsync(_profile, cancellationToken).ConfigureAwait(false);
                    if (!availability.IsAvailable)
                        throw new NotSupportedException(availability.Message);
                }
            }
            if (_machine.State == RecordingState.Failed && _queue is not null)
                await CleanupAsync().ConfigureAwait(false);
            _failedCleanupTask = null;
            _machine.MoveTo(RecordingState.Starting);
            started = true;
            Publish(RecordingState.Starting);
            _outputPath = Path.GetFullPath(request.OutputPath);
            if (File.Exists(_outputPath))
                throw new IOException($"Já existe uma gravação no destino: {_outputPath}");
            _temporaryPath = Path.Combine(Path.GetDirectoryName(_outputPath)!, $".{Path.GetFileNameWithoutExtension(_outputPath)}.{Guid.NewGuid():N}.partial.mp4");
            _preservedPartialPath = null;
            _sessionId = Guid.NewGuid();
            _journalCreated = false;
            lock (_journalSync) _journalUpdateTail = Task.CompletedTask;
            if (_journal is not null)
            {
                await _journal.CreateAsync(new RecordingJournalEntry(_sessionId, _temporaryPath, _outputPath, DateTimeOffset.UtcNow, RecordingState.Starting, null), cancellationToken).ConfigureAwait(false);
                _journalCreated = true;
            }
            _queue = Channel.CreateBounded<Packet>(new BoundedChannelOptions(QueueCapacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = false });
            Interlocked.Exchange(ref _queueDroppedVideoFrames, 0);
            Interlocked.Exchange(ref _queueDroppedAudioPackets, 0);
            Interlocked.Exchange(ref _encodedFrames, 0);
            Interlocked.Exchange(ref _encodeDurationTicks, 0);
            Interlocked.Exchange(ref _receivedVideoFrames, 0);
            Interlocked.Exchange(ref _queueLatencyTicks, 0);
            Interlocked.Exchange(ref _queueLatencySamples, 0);
            Interlocked.Exchange(ref _managedAllocationStart, GC.GetTotalAllocatedBytes(precise: false));
            lock (_queueMetricsSync)
            {
                _queueDepth = 0;
                _queuedVideoPackets = 0;
                _peakQueueDepth = 0;
                _peakReorderQueueDepth = 0;
                _queuedVideoBytes = 0;
                _peakQueuedVideoBytes = 0;
            }
            _encodeError = null; _paused = false; _pausedTicks = 0; _pauseStartedTicks = 0;
            _audioMix = request.EffectiveAudioMix;
            _audioEnabled = _audioMix.HasEnabledSource && _profile.Container != RecordingContainer.Gif;
            _systemAudioActive = _audioMix.SystemEnabled;
            _microphoneAudioActive = _audioMix.MicrophoneEnabled && _microphone is not null;
            _fps = _profile.FramesPerSecond;
            _originTicks = NowTicks();
            _composer.Configure(request.EffectiveOverlays);
            await _encoder.StartAsync(request with { OutputPath = _temporaryPath, FramesPerSecond = _profile.FramesPerSecond, SystemAudioEnabled = _audioEnabled, AudioMix = _audioEnabled ? _audioMix : AudioMixSettings.None, Profile = _profile }, cancellationToken).ConfigureAwait(false);
            _encodeTask = Task.Run(() => EncodeLoopAsync(_queue.Reader), CancellationToken.None);
            await _capture.StartAsync(request, cancellationToken).ConfigureAwait(false);
            _composer.SetClickEffectContext(_capture.CaptureBounds);
            if (request.EffectiveOverlays.EffectiveClickEffects.Enabled)
                StartClickCapture();
            if (request.EffectiveOverlays.Webcam.Enabled && _webcam is not null)
            {
                try { await _webcam.StartAsync(request.EffectiveOverlays.Webcam, cancellationToken).ConfigureAwait(false); }
                catch (Exception exception) { _logger.LogWarning(exception, "Webcam não iniciada; continuando somente com tela"); }
            }
            string? audioWarning = null;
            if (_audioEnabled && _systemAudioActive)
            {
                try { await _audio.StartAsync(cancellationToken).ConfigureAwait(false); }
                catch (Exception exception)
                {
                    _systemAudioActive = false;
                    _logger.LogWarning(exception, "Áudio do sistema não iniciado; continuando com as demais fontes");
                    audioWarning = "Áudio indisponível (sistema); gravação continua.";
                }
            }
            if (_audioEnabled && _audioMix.MicrophoneEnabled)
            {
                if (_microphone is null)
                {
                    _microphoneAudioActive = false;
                    audioWarning ??= "Microfone indisponível; gravação continua.";
                    _logger.LogWarning("Nenhum serviço de microfone foi configurado; continuando com as demais fontes");
                }
                else
                {
                    try { await _microphone.StartAsync(request.MicrophoneDeviceId, cancellationToken).ConfigureAwait(false); }
                    catch (Exception exception)
                    {
                        _microphoneAudioActive = false;
                        _logger.LogWarning(exception, "Microfone não iniciado; continuando com as demais fontes");
                        audioWarning ??= "Microfone indisponível; gravação continua.";
                    }
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
            if (_microphone is not null)
            {
                _logger.LogDebug("Parando microfone WASAPI");
                await _microphone.StopAsync(cancellationToken).ConfigureAwait(false);
            }
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
                File.Move(_temporaryPath, _outputPath);
            else
            {
                try
                {
                    await _exporters!.GetExporter(_profile).ExportAsync(_temporaryPath, _outputPath, _profile, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    var preserved = PreserveIntermediateAfterExportFailure();
                    throw new InvalidOperationException(
                        preserved is null
                            ? $"A conversão falhou; o arquivo final não foi publicado. Detalhe: {exception.Message}"
                            : $"A conversão falhou; o MP4 intermediário foi preservado em {preserved}. Detalhe: {exception.Message}",
                        exception);
                }
            }
            await FlushJournalUpdatesAsync().ConfigureAwait(false);
            if (_journal is not null && _journalCreated)
            {
                await _journal.CompleteAsync(_sessionId, cancellationToken).ConfigureAwait(false);
                _journalCreated = false;
            }
            _machine.MoveTo(RecordingState.Completed);
            Publish(RecordingState.Completed);
            _logger.LogInformation("Gravação concluída: {OutputPath}", _outputPath);
        }
        catch (Exception exception)
        {
            var preserved = PreservePartialAfterFinalizationFailure();
            var message = preserved is null
                ? $"Falha ao finalizar gravação: {exception.Message}"
                : _profile.Container == RecordingContainer.Mp4
                    ? $"Falha ao finalizar gravação. O arquivo recuperável foi preservado em {preserved}."
                    : $"A conversão falhou; o MP4 intermediário foi preservado em {preserved}.";
            _logger.LogError(exception, "{Message}", message);
            Fail(message);
            throw new InvalidOperationException(message, exception);
        }
        finally { await CleanupAsync().ConfigureAwait(false); _gate.Release(); }
    }
    private void OnFrameCaptured(object? sender, VideoFrame frame)
    {
        if (_paused || _machine.State is not (RecordingState.Recording or RecordingState.Starting)) return;
        Interlocked.Increment(ref _receivedVideoFrames);
        var expectedBytes = _profile.Width * (long)_profile.Height * sizeof(uint);
        if (Volatile.Read(ref _queuedVideoPackets) >= VideoQueueHighWatermark ||
            Interlocked.Read(ref _queuedVideoBytes) + expectedBytes > MaximumQueuedVideoBytes)
        {
            Interlocked.Increment(ref _queueDroppedVideoFrames);
            return;
        }
        var timestamp = Normalize(frame.Timestamp);
        var scaled = _scaler.Scale(frame with { Timestamp = timestamp }, _profile.Width, _profile.Height);
        Enqueue(new Packet(timestamp, Video: _composer.Compose(scaled)));
    }
    private void OnSystemAudioCaptured(object? sender, AudioFrame frame)
    {
        if (!_systemAudioActive || _paused || _machine.State != RecordingState.Recording) return;
        var timestamp = Normalize(frame.Timestamp);
        Enqueue(new Packet(timestamp, Audio: frame with { Timestamp = timestamp }, AudioSource: AudioSource.System));
    }
    private void OnMicrophoneAudioCaptured(object? sender, AudioFrame frame)
    {
        if (!_microphoneAudioActive || _paused || _machine.State != RecordingState.Recording) return;
        var timestamp = Normalize(frame.Timestamp);
        Enqueue(new Packet(timestamp, Audio: frame with { Timestamp = timestamp }, AudioSource: AudioSource.Microphone));
    }
    private void Enqueue(Packet packet)
    {
        packet = packet with { EnqueuedAtTimestamp = Stopwatch.GetTimestamp() };
        lock (_queueMetricsSync)
        {
            var writer = _queue?.Writer;
            var queueFull = _queueDepth >= QueueCapacity;
            var videoExceedsBudget = packet.Video is { } candidateVideo &&
                (_queuedVideoPackets >= VideoQueueHighWatermark || _queuedVideoBytes + candidateVideo.Pixels.LongLength > MaximumQueuedVideoBytes);
            if (!queueFull && !videoExceedsBudget && writer is not null && writer.TryWrite(packet))
            {
                _queueDepth++;
                _peakQueueDepth = Math.Max(_peakQueueDepth, _queueDepth);
                if (packet.Video is { } video)
                {
                    _queuedVideoPackets++;
                    _queuedVideoBytes += video.Pixels.LongLength;
                    _peakQueuedVideoBytes = Math.Max(_peakQueuedVideoBytes, _queuedVideoBytes);
                }
                return;
            }
        }
        if (packet.Video is not null) Interlocked.Increment(ref _queueDroppedVideoFrames);
        else Interlocked.Increment(ref _queueDroppedAudioPackets);
    }
    private TimeSpan Normalize(TimeSpan absolute) => TimeSpan.FromTicks(Math.Max(0, absolute.Ticks - _originTicks - Interlocked.Read(ref _pausedTicks)));
    private async Task EncodeLoopAsync(ChannelReader<Packet> reader)
    {
        var pending = new PriorityQueue<Packet, long>();
        var videoStarted = false;
        long audioCursor = 0;
        long videoEnd = 0;
        var audioMixer = new TimestampedAudioMixer(_audioMix);
        try
        {
            await foreach (var packet in reader.ReadAllAsync().ConfigureAwait(false))
            {
                RecordPacketDequeued(packet);
                if (!videoStarted)
                {
                    if (packet.Video is null)
                    {
                        pending.Enqueue(packet, packet.Timestamp.Ticks);
                        if (pending.Count > QueueCapacity)
                        {
                            var dropped = pending.Dequeue();
                            RecordDroppedPacket(dropped);
                            ReleasePacket(dropped);
                        }
                        RecordReorderQueueDepth(pending.Count);
                        continue;
                    }
                    var began = Stopwatch.GetTimestamp();
                    try
                    {
                        await _encoder.EncodeAsync(packet.Video).ConfigureAwait(false);
                        RecordEncodeTime(began, packet.Video.Timestamp);
                    }
                    finally { ReleasePacket(packet); }
                    videoEnd = packet.Video.Timestamp.Ticks + TimeSpan.TicksPerSecond / _fps;
                    videoStarted = true;
                    while (pending.Count > 0)
                        await WriteAsync(pending.Dequeue()).ConfigureAwait(false);
                    continue;
                }
                pending.Enqueue(packet, packet.Timestamp.Ticks);
                var watermark = Math.Max(0, NowTicks() - _originTicks - _pausedTicks - TimeSpan.TicksPerSecond / 5);
                while (pending.TryPeek(out _, out var timestamp) && (timestamp <= watermark || pending.Count > QueueCapacity))
                    await WriteAsync(pending.Dequeue()).ConfigureAwait(false);
                RecordReorderQueueDepth(pending.Count);
            }
            if (!videoStarted) throw new InvalidOperationException("Nenhum quadro de vídeo foi capturado.");
            while (pending.Count > 0) await WriteAsync(pending.Dequeue()).ConfigureAwait(false);
            foreach (var mixed in audioMixer.DrainReady(final: true)) await EncodeMixedAudioAsync(mixed).ConfigureAwait(false);
            _logger.LogDebug("Fila drenada: vídeo {VideoEnd} ticks, áudio {AudioCursor} ticks", videoEnd, audioCursor);
            if (_audioMix.SystemEnabled && _audioMix.MicrophoneEnabled)
            {
                _logger.LogInformation("Drift máximo entre sistema e microfone: {Drift}", audioMixer.MaxSourceDrift);
                _logger.LogInformation(
                    "Mixagem A/V: sistema={SystemFrames} amostras; microfone={MicrophoneFrames} amostras; saída={OutputFrames} amostras; baldes mistos={MixedBuckets}; baldes de fonte única={SingleSourceBuckets}; diferença inicial={InitialSkew}",
                    audioMixer.SystemInputSampleFrames,
                    audioMixer.MicrophoneInputSampleFrames,
                    audioMixer.OutputSampleFrames,
                    audioMixer.MixedBuckets,
                    audioMixer.SingleSourceBuckets,
                    audioMixer.InitialSourceSkew);
                if (audioMixer.MaxSourceDrift >= TimeSpan.FromMilliseconds(100))
                    _diagnostics?.Report(DiagnosticSeverity.Warning, "audio.drift", $"Drift entre sistema e microfone: {audioMixer.MaxSourceDrift.TotalMilliseconds:F0} ms.");
            }
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
        finally
        {
            while (pending.Count > 0) ReleasePacket(pending.Dequeue());
            while (reader.TryRead(out var abandoned))
            {
                RecordDroppedPacket(abandoned);
                ReleasePacket(abandoned);
            }
        }
        async Task WriteAsync(Packet packet)
        {
            if (packet.Video is { } video)
            {
                var began = Stopwatch.GetTimestamp();
                try
                {
                    await _encoder.EncodeAsync(video).ConfigureAwait(false);
                    RecordEncodeTime(began, video.Timestamp);
                }
                finally { ReleasePacket(packet); }
                videoEnd = Math.Max(videoEnd, video.Timestamp.Ticks + TimeSpan.TicksPerSecond / _fps);
                return;
            }
            try
            {
                if (packet.Audio is not { } audio || !_audioEnabled) return;
                audioMixer.Add(packet.AudioSource ?? AudioSource.System, audio);
                foreach (var mixed in audioMixer.DrainReady()) await EncodeMixedAudioAsync(mixed).ConfigureAwait(false);
            }
            finally { ReleasePacket(packet); }
        }
        async Task EncodeMixedAudioAsync(AudioFrame audio)
        {
            await FillSilenceAsync(audio.Timestamp.Ticks).ConfigureAwait(false);
            var timestamp = Math.Max(audio.Timestamp.Ticks, audioCursor);
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
    private void StartClickCapture()
    {
        if (_clickCapture is null) return;
        try { _clickCapture.Start(); }
        catch (Exception exception) { _logger.LogWarning(exception, "Hook de cliques não iniciado; gravando sem efeitos de clique"); }
    }
    private void ScheduleFailedCleanup()
    {
        if (_failedCleanupTask is not null) return;
        _failedCleanupTask = Task.Run(async () =>
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_machine.State == RecordingState.Failed && _queue is not null)
                {
                    var preserved = PreservePartialAfterFinalizationFailure();
                    if (preserved is not null)
                        Publish(RecordingState.Failed, $"Falha na gravação. O arquivo recuperável foi preservado em {preserved}.");
                    await CleanupAsync().ConfigureAwait(false);
                }
            }
            catch (Exception cleanupError) { _logger.LogError(cleanupError, "Falha ao limpar gravação interrompida"); }
            finally { _gate.Release(); }
        });
    }
    private void RecordEncodeTime(long began, TimeSpan timestamp)
    {
        var elapsed = Stopwatch.GetElapsedTime(began);
        Interlocked.Increment(ref _encodedFrames);
        Interlocked.Add(ref _encodeDurationTicks, elapsed.Ticks);
        if (elapsed > TimeSpan.FromMilliseconds(200))
            _logger.LogWarning("Encoder lento no quadro {Timestamp}: {Elapsed}", timestamp, elapsed);
    }
    private void RecordPacketDequeued(Packet packet)
    {
        if (packet.EnqueuedAtTimestamp == 0) return;
        Interlocked.Increment(ref _queueLatencySamples);
        Interlocked.Add(ref _queueLatencyTicks, Stopwatch.GetElapsedTime(packet.EnqueuedAtTimestamp).Ticks);
    }
    private void ReleasePacket(Packet packet)
    {
        lock (_queueMetricsSync)
        {
            if (_queueDepth > 0) _queueDepth--;
            if (packet.Video is { } video)
            {
                if (_queuedVideoPackets > 0) _queuedVideoPackets--;
                _queuedVideoBytes = Math.Max(0, _queuedVideoBytes - video.Pixels.LongLength);
            }
        }
    }
    private void RecordReorderQueueDepth(int depth)
    {
        while (true)
        {
            var current = Volatile.Read(ref _peakReorderQueueDepth);
            if (depth <= current || Interlocked.CompareExchange(ref _peakReorderQueueDepth, depth, current) == current) return;
        }
    }
    private void RecordDroppedPacket(Packet packet)
    {
        if (packet.Video is not null) Interlocked.Increment(ref _queueDroppedVideoFrames);
        else Interlocked.Increment(ref _queueDroppedAudioPackets);
    }
    private void Publish(RecordingState state, string? error = null)
    {
        var elapsed = _originTicks == 0 ? TimeSpan.Zero : TimeSpan.FromTicks(Math.Max(0, NowTicks() - _originTicks - _pausedTicks - (_paused ? NowTicks() - _pauseStartedTicks : 0)));
        _status = new RecordingStatus(state, elapsed, error);
        StatusChanged?.Invoke(this, _status);
        if (state is RecordingState.Completed or RecordingState.Failed)
        {
            var telemetry = CurrentTelemetry;
            _logger.LogInformation("Telemetria da gravação: Captured={CapturedFrames}; Dropped={DroppedFrames}; Encoded={EncodedFrames}; Queue={QueueDepth}/{QueueCapacity} Peak={PeakQueueDepth}; ReorderPeak={ReorderPeak}; VideoBytes={QueuedVideoBytes}/{MaximumQueuedVideoBytes} PeakVideoBytes={PeakQueuedVideoBytes}; AverageQueue={AverageQueue}; AverageEncode={AverageEncode}; Allocated={AllocatedBytes}; Encoder={Encoder}",
                telemetry.CapturedFrames, telemetry.DroppedFrames, telemetry.EncodedFrames, telemetry.QueueDepth, telemetry.QueueCapacity, telemetry.PeakQueueDepth, telemetry.PeakReorderQueueDepth, telemetry.QueuedVideoBytes, telemetry.MaximumQueuedVideoBytes, telemetry.PeakQueuedVideoBytes, telemetry.AverageQueueLatency, telemetry.AverageEncodeTime, telemetry.ManagedAllocatedBytes, telemetry.UsingHardwareEncoder ? "hardware" : "software");
            if (telemetry.DroppedFrames > 0)
                _diagnostics?.Report(DiagnosticSeverity.Warning, "recording.frames_dropped", $"{telemetry.DroppedFrames} quadros foram descartados durante a gravação.");
            if (telemetry.QueueDroppedAudioPackets > 0)
                _diagnostics?.Report(DiagnosticSeverity.Warning, "recording.audio_packets_dropped", $"{telemetry.QueueDroppedAudioPackets} pacotes de áudio foram descartados durante a gravação.");
        }
        if (error is not null) _diagnostics?.Report(state == RecordingState.Failed ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning, $"recording.{state}".ToLowerInvariant(), error);
        if (_journal is not null && _journalCreated && _sessionId != Guid.Empty && state is not RecordingState.Completed)
            QueueJournalUpdate(state, error);
    }
    private void Fail(string error)
    {
        if (_machine.State != RecordingState.Failed) { _machine.MoveTo(RecordingState.Failed); Publish(RecordingState.Failed, error); }
    }
    private void QueueJournalUpdate(RecordingState state, string? error)
    {
        lock (_journalSync)
        {
            _journalUpdateTail = _journalUpdateTail.ContinueWith(
                _ => PersistJournalStateSafeAsync(state, error),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default).Unwrap();
        }
    }
    private async Task PersistJournalStateSafeAsync(RecordingState state, string? error)
    {
        try { await _journal!.UpdateAsync(_sessionId, state, error).ConfigureAwait(false); }
        catch (Exception exception) { _logger.LogWarning(exception, "Não foi possível atualizar journal da sessão {SessionId}", _sessionId); }
    }
    private async Task FlushJournalUpdatesAsync()
    {
        Task updates;
        lock (_journalSync) updates = _journalUpdateTail;
        await updates.ConfigureAwait(false);
    }
    private string? PreserveIntermediateAfterExportFailure()
    {
        if (_temporaryPath is null || !File.Exists(_temporaryPath)) return null;
        try
        {
            var directory = Path.GetDirectoryName(_temporaryPath)!;
            var stem = Path.GetFileNameWithoutExtension(_outputPath ?? "TelaRec");
            var path = Path.Combine(directory, $"{stem}-intermediario-{_sessionId:N}.mp4");
            File.Move(_temporaryPath, path);
            _temporaryPath = null;
            _preservedPartialPath = path;
            _logger.LogWarning("Conversão externa falhou; MP4 intermediário preservado em {Path}", path);
            return path;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Não foi possível preservar o MP4 intermediário após falha de exportação");
            return null;
        }
    }

    private string? PreservePartialAfterFinalizationFailure()
    {
        if (_preservedPartialPath is not null) return _preservedPartialPath;
        if (_temporaryPath is null || !File.Exists(_temporaryPath)) return null;

        var originalPath = _temporaryPath;
        var directory = Path.GetDirectoryName(originalPath)!;
        var stem = Path.GetFileNameWithoutExtension(_outputPath ?? "TelaRec");
        var recoveryPath = Path.Combine(directory, $"{stem}-recuperavel-{_sessionId:N}.mp4");
        try
        {
            File.Move(originalPath, recoveryPath);
            _temporaryPath = null;
            _preservedPartialPath = recoveryPath;
            _logger.LogWarning("Finalização falhou; arquivo recuperável preservado em {Path}", recoveryPath);
        }
        catch (Exception exception)
        {
            // O arquivo original continua sendo a única evidência recuperável.
            _preservedPartialPath = originalPath;
            _logger.LogWarning(exception, "Não foi possível renomear o arquivo parcial; preservando em {Path}", originalPath);
        }
        return _preservedPartialPath;
    }
    private async Task CleanupAsync()
    {
        try { _clickCapture?.Stop(); } catch (Exception e) { _logger.LogWarning(e, "Falha ao liberar hook de cliques"); }
        _queue?.Writer.TryComplete();
        try { await _capture.StopAsync().ConfigureAwait(false); } catch (Exception e) { _logger.LogWarning(e, "Falha ao liberar captura"); }
        try { if (_webcam is not null) await _webcam.StopAsync().ConfigureAwait(false); } catch (Exception e) { _logger.LogWarning(e, "Falha ao liberar webcam"); }
        try { await _audio.StopAsync().ConfigureAwait(false); } catch (Exception e) { _logger.LogWarning(e, "Falha ao liberar áudio do sistema"); }
        try { if (_microphone is not null) await _microphone.StopAsync().ConfigureAwait(false); } catch (Exception e) { _logger.LogWarning(e, "Falha ao liberar microfone"); }
        if (_encodeTask is not null) await _encodeTask.ConfigureAwait(false);
        await _encoder.DisposeAsync().ConfigureAwait(false);
        if (_preservedPartialPath is null && _temporaryPath is not null && File.Exists(_temporaryPath)) File.Delete(_temporaryPath);
        if (_journal is not null && _journalCreated)
        {
            await FlushJournalUpdatesAsync().ConfigureAwait(false);
            if (_preservedPartialPath is null)
                await _journal.CompleteAsync(_sessionId).ConfigureAwait(false);
            else
                _logger.LogWarning("Journal da sessão {SessionId} foi preservado para recuperação de {Path}", _sessionId, _preservedPartialPath);
            _journalCreated = false;
        }
        _queue = null; _encodeTask = null; _temporaryPath = null; _outputPath = null; _preservedPartialPath = null;
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
            _audio.AudioCaptured -= OnSystemAudioCaptured;
            _audio.CaptureFailed -= OnSystemAudioFailed;
            if (_microphone is not null) { _microphone.AudioCaptured -= OnMicrophoneAudioCaptured; _microphone.CaptureFailed -= OnMicrophoneFailed; }
            if (_webcam is not null) { _webcam.FrameCaptured -= OnWebcamFrame; _webcam.CaptureFailed -= OnWebcamFailed; await _webcam.DisposeAsync().ConfigureAwait(false); }
            if (_clickCapture is not null) _clickCapture.ClickCaptured -= OnClickCaptured;
            await _capture.DisposeAsync().ConfigureAwait(false);
            await _audio.DisposeAsync().ConfigureAwait(false);
            if (_microphone is not null) await _microphone.DisposeAsync().ConfigureAwait(false);
            if (_composer is IDisposable disposableComposer) disposableComposer.Dispose();
        }
        finally { _gate.Release(); _gate.Dispose(); }
    }
}
