using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using TelaRec.Core.Contracts;
using TelaRec.Core.Recording;
using TelaRec.Core.Reliability;
using TelaRec.Infrastructure.Recording;

namespace TelaRec.Tests;

public class RecordingServiceTests
{
    [Fact]
    public async Task StartPauseResumeStopAndRestartProduceOnlyFinalFiles()
    {
        var folder = Path.Combine(Path.GetTempPath(), "TelaRecTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var capture = new FakeCapture();
        var encoder = new FakeEncoder();
        await using var service = new RecordingService(capture, encoder, new FakeAudio(), NullLogger<RecordingService>.Instance);
        try
        {
            foreach (var index in new[] { 1, 2 })
            {
                var path = Path.Combine(folder, $"{index}.mp4");
                await service.StartAsync(new RecordingRequest(path));
                Assert.Equal(RecordingState.Recording, service.Status.State);
                await service.PauseAsync();
                Assert.Equal(RecordingState.Paused, service.Status.State);
                await service.ResumeAsync();
                Assert.Equal(RecordingState.Recording, service.Status.State);
                await service.StopAsync();
                Assert.Equal(RecordingState.Completed, service.Status.State);
                Assert.True(File.Exists(path));
                Assert.Empty(Directory.GetFiles(folder, "*.partial.mp4"));
            }
            Assert.Equal(2, encoder.Completed);
            Assert.Equal(2, encoder.VideoFrames);
        }
        finally { Directory.Delete(folder, true); }
    }
    [Fact]
    public async Task DuplicateStartKeepsCurrentRecordingActive()
    {
        var path = Path.Combine(Path.GetTempPath(), $"TelaRec-{Guid.NewGuid():N}.mp4");
        await using var service = new RecordingService(new FakeCapture(), new FakeEncoder(), new FakeAudio(), NullLogger<RecordingService>.Instance);
        try
        {
            await service.StartAsync(new RecordingRequest(path));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(new RecordingRequest(path)));
            Assert.Equal(RecordingState.Recording, service.Status.State);
            await service.StopAsync();
            Assert.Equal(RecordingState.Completed, service.Status.State);
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public async Task AudioFailureDoesNotStopVideo()
    {
        var path = Path.Combine(Path.GetTempPath(), $"TelaRec-{Guid.NewGuid():N}.mp4");
        var audio = new FakeAudio { FailOnStart = true };
        await using var service = new RecordingService(new FakeCapture(), new FakeEncoder(), audio, NullLogger<RecordingService>.Instance);
        try
        {
            await service.StartAsync(new RecordingRequest(path, SystemAudioEnabled: true));
            Assert.Contains("Áudio indisponível", service.Status.ErrorMessage);
            await service.StopAsync();
            Assert.Equal(RecordingState.Completed, service.Status.State);
            Assert.True(File.Exists(path));
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public async Task VideoCaptureFailureEntersFailedState()
    {
        var path = Path.Combine(Path.GetTempPath(), $"TelaRec-{Guid.NewGuid():N}.mp4");
        var capture = new FakeCapture();
        await using var service = new RecordingService(capture, new FakeEncoder(), new FakeAudio(), NullLogger<RecordingService>.Instance);
        await service.StartAsync(new RecordingRequest(path));
        capture.Fail();
        Assert.Equal(RecordingState.Failed, service.Status.State);
        Assert.False(File.Exists(path));
    }
    [Fact]
    public async Task EncoderStartupFailureEntersFailedState()
    {
        var path = Path.Combine(Path.GetTempPath(), $"TelaRec-{Guid.NewGuid():N}.mp4");
        await using var service = new RecordingService(new FakeCapture(), new FakeEncoder { FailOnStart = true }, new FakeAudio(), NullLogger<RecordingService>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(new RecordingRequest(path)));
        Assert.Equal(RecordingState.Failed, service.Status.State);
        Assert.False(File.Exists(path));
    }
    [Fact]
    public async Task DisposeAsync_is_idempotent()
    {
        var service = new RecordingService(new FakeCapture(), new FakeEncoder(), new FakeAudio(), NullLogger<RecordingService>.Instance);
        await service.DisposeAsync();
        await service.DisposeAsync();
    }
    [Fact]
    public async Task DisposeAsync_during_active_recording_removes_temporary_file()
    {
        var folder = Path.Combine(Path.GetTempPath(), "TelaRecTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var service = new RecordingService(new FakeCapture(), new FakeEncoder { CreateFileOnStart = true }, new FakeAudio(), NullLogger<RecordingService>.Instance);
        try
        {
            await service.StartAsync(new RecordingRequest(Path.Combine(folder, "active.mp4")));
            Assert.Equal(RecordingState.Recording, service.Status.State);
            Assert.Single(Directory.GetFiles(folder, "*.partial.mp4"));
            await service.DisposeAsync();
            Assert.Empty(Directory.GetFiles(folder, "*.partial.mp4"));
        }
        finally { Directory.Delete(folder, true); }
    }
    [Fact]
    public async Task Cursor_preference_is_forwarded_to_capture()
    {
        var folder = Path.Combine(Path.GetTempPath(), "TelaRecTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var capture = new FakeCapture();
        await using var service = new RecordingService(capture, new FakeEncoder(), new FakeAudio(), NullLogger<RecordingService>.Instance);
        try
        {
            await service.StartAsync(new RecordingRequest(Path.Combine(folder, "cursor.mp4"), ShowCursor: false));
            Assert.NotNull(capture.LastRequest);
            Assert.False(capture.LastRequest!.ShowCursor);
            await service.StopAsync();
        }
        finally { Directory.Delete(folder, true); }
    }

    [Theory]
    [InlineData(VideoCodec.H264)]
    [InlineData(VideoCodec.Hevc)]
    public async Task Selected_profile_is_forwarded_unchanged_to_encoder(VideoCodec codec)
    {
        var folder = Path.Combine(Path.GetTempPath(), "TelaRecTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var encoder = new FakeEncoder();
        var profile = new RecordingProfile(RecordingContainer.Mp4, codec, 5_500_000, 24, 1280, 720);
        await using var service = new RecordingService(new FakeCapture(), encoder, new FakeAudio(), NullLogger<RecordingService>.Instance);
        try
        {
            await service.StartAsync(new RecordingRequest(Path.Combine(folder, $"{codec}.mp4"), Profile: profile));

            Assert.NotNull(encoder.LastRequest);
            Assert.Equal(profile, encoder.LastRequest!.EffectiveProfile);

            await service.StopAsync();
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task Unsupported_hevc_fails_before_capture_audio_or_partial_file()
    {
        var folder = Path.Combine(Path.GetTempPath(), "TelaRecTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var capture = new FakeCapture();
        var audio = new FakeAudio();
        var encoder = new FakeEncoder { UnsupportedCodec = VideoCodec.Hevc };
        var profile = new RecordingProfile(RecordingContainer.Mp4, VideoCodec.Hevc, 5_500_000, 30, 1280, 720);
        await using var service = new RecordingService(capture, encoder, audio, NullLogger<RecordingService>.Instance);
        try
        {
            var error = await Assert.ThrowsAsync<NotSupportedException>(() =>
                service.StartAsync(new RecordingRequest(Path.Combine(folder, "hevc.mp4"), Profile: profile, SystemAudioEnabled: true)));

            Assert.Contains("H.264", error.Message);
            Assert.Equal(profile, encoder.LastRequest?.EffectiveProfile);
            Assert.Equal(0, capture.StartCalls);
            Assert.Equal(0, audio.StartCalls);
            Assert.Empty(Directory.GetFiles(folder, "*.partial.mp4"));
            Assert.Equal(RecordingState.Failed, service.Status.State);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task Complete_failure_preserves_recoverable_file_and_journal_diagnostic()
    {
        var folder = Path.Combine(Path.GetTempPath(), "TelaRecTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var journal = new FakeJournal();
        var encoder = new FakeEncoder { CreateFileOnStart = true, FailOnComplete = true };
        await using var service = new RecordingService(new FakeCapture(), encoder, new FakeAudio(), NullLogger<RecordingService>.Instance, journal: journal);
        try
        {
            await service.StartAsync(new RecordingRequest(Path.Combine(folder, "complete-failure.mp4")));
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.StopAsync());

            var recovered = Assert.Single(Directory.GetFiles(folder, "complete-failure-recuperavel-*.mp4"));
            Assert.Contains(recovered, error.Message);
            Assert.Empty(Directory.GetFiles(folder, "*.partial.mp4"));
            Assert.Equal(0, journal.Completed);
            Assert.Contains(journal.Updates, update => update.Error?.Contains(recovered, StringComparison.Ordinal) == true);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task Final_output_collision_preserves_recoverable_file()
    {
        var folder = Path.Combine(Path.GetTempPath(), "TelaRecTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var output = Path.Combine(folder, "move-failure.mp4");
        var encoder = new FakeEncoder { BeforeComplete = () => File.WriteAllBytes(output, [9, 8, 7]) };
        await using var service = new RecordingService(new FakeCapture(), encoder, new FakeAudio(), NullLogger<RecordingService>.Instance);
        try
        {
            await service.StartAsync(new RecordingRequest(output));
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.StopAsync());

            var recovered = Assert.Single(Directory.GetFiles(folder, "move-failure-recuperavel-*.mp4"));
            Assert.Contains(recovered, error.Message);
            Assert.Equal(new byte[] { 9, 8, 7 }, await File.ReadAllBytesAsync(output));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task Existing_output_is_never_overwritten()
    {
        var path = Path.Combine(Path.GetTempPath(), $"TelaRec-{Guid.NewGuid():N}.mp4");
        await File.WriteAllBytesAsync(path, [9, 8, 7]);
        await using var service = new RecordingService(new FakeCapture(), new FakeEncoder(), new FakeAudio(), NullLogger<RecordingService>.Instance);
        try
        {
            await Assert.ThrowsAsync<IOException>(() => service.StartAsync(new RecordingRequest(path)));
            Assert.Equal(new byte[] { 9, 8, 7 }, await File.ReadAllBytesAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Failed_external_export_preserves_the_valid_mp4_intermediate()
    {
        var folder = Path.Combine(Path.GetTempPath(), "TelaRecTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var profile = new RecordingProfile(RecordingContainer.Mkv, VideoCodec.H264, 1_000_000, 30, 2, 2);
        await using var service = new RecordingService(
            new FakeCapture(),
            new FakeEncoder(),
            new FakeAudio(),
            NullLogger<RecordingService>.Instance,
            new ThrowingExporterFactory());
        try
        {
            await service.StartAsync(new RecordingRequest(Path.Combine(folder, "clip.mkv"), Profile: profile));
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.StopAsync());

            Assert.Contains("MP4 intermediário", error.Message);
            Assert.False(File.Exists(Path.Combine(folder, "clip.mkv")));
            Assert.Single(Directory.GetFiles(folder, "clip-intermediario-*.mp4"));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task Microphone_and_system_are_mixed_before_encoding()
    {
        var folder = Path.Combine(Path.GetTempPath(), "TelaRecTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var system = new FakeAudio();
        var microphone = new FakeMicrophone();
        var encoder = new FakeEncoder();
        await using var service = new RecordingService(new FakeCapture(), encoder, system, NullLogger<RecordingService>.Instance, microphone: microphone);
        try
        {
            await service.StartAsync(new RecordingRequest(
                Path.Combine(folder, "mixed.mp4"),
                AudioMix: new AudioMixSettings(true, true)));
            var ticks = (long)(Stopwatch.GetTimestamp() * (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency);
            system.Emit(new AudioFrame(Samples(10_000, 10_000), 48_000, 2, TimeSpan.FromTicks(ticks)));
            microphone.Emit(new AudioFrame(Samples(10_000, 10_000), 48_000, 2, TimeSpan.FromTicks(ticks)));
            await service.StopAsync();

            Assert.Contains(encoder.AudioFrames, frame => BitConverter.ToInt16(frame.Pcm16, 0) == 20_000);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task Microphone_failure_keeps_video_and_system_audio_running()
    {
        var folder = Path.Combine(Path.GetTempPath(), "TelaRecTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var system = new FakeAudio();
        var microphone = new FakeMicrophone();
        var encoder = new FakeEncoder();
        await using var service = new RecordingService(new FakeCapture(), encoder, system, NullLogger<RecordingService>.Instance, microphone: microphone);
        try
        {
            await service.StartAsync(new RecordingRequest(Path.Combine(folder, "fallback.mp4"), AudioMix: new AudioMixSettings(true, true)));
            microphone.Fail();
            var ticks = (long)(Stopwatch.GetTimestamp() * (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency);
            system.Emit(new AudioFrame(Samples(8000, 8000), 48_000, 2, TimeSpan.FromTicks(ticks)));
            await service.StopAsync();

            Assert.Equal(RecordingState.Completed, service.Status.State);
            Assert.True(File.Exists(Path.Combine(folder, "fallback.mp4")));
            Assert.NotEmpty(encoder.AudioFrames);
        }
        finally { Directory.Delete(folder, true); }
    }

    private sealed class FakeCapture : IVideoCaptureService
    {
        public event EventHandler<VideoFrame>? FrameCaptured;
        public event EventHandler<Exception>? CaptureFailed;
        public RecordingRequest? LastRequest { get; private set; }
        public int StartCalls { get; private set; }
        public CaptureFrameBounds? CaptureBounds { get; private set; } = new(0, 0, 1920, 1080);
        public Task StartAsync(RecordingRequest request, CancellationToken cancellationToken = default)
        {
            StartCalls++;
            LastRequest = request;
            var ticks = (long)(Stopwatch.GetTimestamp() * (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency);
            FrameCaptured?.Invoke(this, new VideoFrame(new byte[16], 2, 2, 8, TimeSpan.FromTicks(ticks)));
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Fail() => CaptureFailed?.Invoke(this, new InvalidOperationException("Falha WGC"));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class FakeAudio : IAudioCaptureService
    {
        public bool FailOnStart;
        public int StartCalls { get; private set; }
        public event EventHandler<AudioFrame>? AudioCaptured;
        public event EventHandler<Exception>? CaptureFailed;
        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            StartCalls++;
            if (FailOnStart) { var error = new InvalidOperationException("Sem dispositivo"); CaptureFailed?.Invoke(this, error); throw error; }
            var ticks = (long)(Stopwatch.GetTimestamp() * (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency);
            AudioCaptured?.Invoke(this, new AudioFrame(new byte[4], 48_000, 2, TimeSpan.FromTicks(ticks)));
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Emit(AudioFrame frame) => AudioCaptured?.Invoke(this, frame);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class FakeMicrophone : IMicrophoneCaptureService
    {
        public event EventHandler<AudioFrame>? AudioCaptured;
        public event EventHandler<Exception>? CaptureFailed;
        public IReadOnlyList<AudioInputDevice> GetDevices() => [new("default", "Microfone de teste", true)];
        public Task StartAsync(string? deviceId = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Emit(AudioFrame frame) => AudioCaptured?.Invoke(this, frame);
        public void Fail() => CaptureFailed?.Invoke(this, new InvalidOperationException("Microfone removido"));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class FakeEncoder : IVideoEncoderService
    {
        public bool FailOnStart;
        public bool FailOnComplete;
        public bool CreateFileOnStart;
        public VideoCodec? UnsupportedCodec;
        public Action? BeforeComplete;
        public int VideoFrames;
        public int Completed;
        public RecordingRequest? LastRequest { get; private set; }
        public List<AudioFrame> AudioFrames { get; } = [];
        private string? _path;
        public Task StartAsync(RecordingRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            if (request.EffectiveProfile.VideoCodec == UnsupportedCodec)
                throw new NotSupportedException("HEVC não está disponível. Selecione H.264.");
            if (FailOnStart) throw new InvalidOperationException("Encoder indisponível");
            _path = request.OutputPath;
            if (CreateFileOnStart) File.WriteAllBytes(_path, [1, 2, 3]);
            return Task.CompletedTask;
        }
        public Task EncodeAsync(VideoFrame frame, CancellationToken cancellationToken = default) { VideoFrames++; return Task.CompletedTask; }
        public Task EncodeAudioAsync(AudioFrame frame, CancellationToken cancellationToken = default) { AudioFrames.Add(frame); return Task.CompletedTask; }
        public Task CompleteAsync(CancellationToken cancellationToken = default)
        {
            BeforeComplete?.Invoke();
            if (FailOnComplete) throw new InvalidOperationException("Falha ao finalizar o encoder");
            File.WriteAllBytes(_path!, [1, 2, 3]); Completed++; return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ThrowingExporterFactory : IRecordingExporterFactory
    {
        public IRecordingExporter GetExporter(RecordingProfile profile) => new ThrowingExporter();
    }

    private sealed class FakeJournal : IRecordingSessionJournal
    {
        public List<(RecordingState State, string? Error)> Updates { get; } = [];
        public int Completed { get; private set; }
        public Task CreateAsync(RecordingJournalEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateAsync(Guid sessionId, RecordingState state, string? error = null, CancellationToken cancellationToken = default)
        {
            Updates.Add((state, error));
            return Task.CompletedTask;
        }
        public Task CompleteAsync(Guid sessionId, CancellationToken cancellationToken = default)
        {
            Completed++;
            return Task.CompletedTask;
        }
        public Task<RecoveryResult> RecoverAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new RecoveryResult(0, 0, []));
    }

    private sealed class ThrowingExporter : IRecordingExporter
    {
        public bool CanExport(RecordingProfile profile) => true;
        public Task ExportAsync(string sourcePath, string destinationPath, RecordingProfile profile, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("FFmpeg simulado indisponível");
    }

    private static byte[] Samples(params short[] values)
    {
        var bytes = new byte[values.Length * sizeof(short)];
        for (var index = 0; index < values.Length; index++) BitConverter.TryWriteBytes(bytes.AsSpan(index * 2, 2), values[index]);
        return bytes;
    }
}
