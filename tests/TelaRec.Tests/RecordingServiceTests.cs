using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using TelaRec.Core.Contracts;
using TelaRec.Core.Recording;
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
    private sealed class FakeCapture : IVideoCaptureService
    {
        public event EventHandler<VideoFrame>? FrameCaptured;
        public event EventHandler<Exception>? CaptureFailed;
        public Task StartAsync(RecordingRequest request, CancellationToken cancellationToken = default)
        {
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
        public event EventHandler<AudioFrame>? AudioCaptured;
        public event EventHandler<Exception>? CaptureFailed;
        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (FailOnStart) { var error = new InvalidOperationException("Sem dispositivo"); CaptureFailed?.Invoke(this, error); throw error; }
            var ticks = (long)(Stopwatch.GetTimestamp() * (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency);
            AudioCaptured?.Invoke(this, new AudioFrame(new byte[4], 48_000, 2, TimeSpan.FromTicks(ticks)));
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class FakeEncoder : IVideoEncoderService
    {
        public bool FailOnStart;
        public int VideoFrames;
        public int Completed;
        private string? _path;
        public Task StartAsync(RecordingRequest request, CancellationToken cancellationToken = default)
        {
            if (FailOnStart) throw new InvalidOperationException("Encoder indisponível");
            _path = request.OutputPath;
            return Task.CompletedTask;
        }
        public Task EncodeAsync(VideoFrame frame, CancellationToken cancellationToken = default) { VideoFrames++; return Task.CompletedTask; }
        public Task EncodeAudioAsync(AudioFrame frame, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CompleteAsync(CancellationToken cancellationToken = default)
        {
            File.WriteAllBytes(_path!, [1, 2, 3]); Completed++; return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
