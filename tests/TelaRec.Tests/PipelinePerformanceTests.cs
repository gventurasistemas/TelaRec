using System.Buffers;
using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using TelaRec.App.Wpf.Infrastructure;
using TelaRec.Core.Contracts;
using TelaRec.Core.Recording;
using TelaRec.Infrastructure.Recording;

namespace TelaRec.Tests;

public sealed class PipelinePerformanceTests
{
    [Fact]
    public void Composer_reuses_input_pixels_when_no_overlay_is_active()
    {
        using var composer = new VideoFrameComposer();
        composer.Configure(RecordingOverlaySettings.None);
        var input = new VideoFrame(new byte[16], 2, 2, 8, TimeSpan.Zero);

        var output = composer.Compose(input);

        Assert.Same(input, output);
        Assert.Same(input.Pixels, output.Pixels);
    }

    [Fact]
    public void Latest_preview_frame_coalescer_keeps_only_the_latest_pending_frame()
    {
        var coalescer = new LatestFrameCoalescer<VideoFrame>();
        var first = Frame(1);
        var latest = Frame(2);

        Assert.True(coalescer.Offer(first));
        Assert.False(coalescer.Offer(latest));
        Assert.Equal(1, coalescer.DroppedItems);
        Assert.True(coalescer.TryTake(out var consumed));
        Assert.Same(latest, consumed);
        Assert.False(coalescer.CompleteRender());
    }

    [Fact]
    public void Latest_preview_frame_coalescer_requests_one_follow_up_callback_when_a_frame_arrives_during_render()
    {
        var coalescer = new LatestFrameCoalescer<VideoFrame>();
        Assert.True(coalescer.Offer(Frame(1)));
        Assert.True(coalescer.TryTake(out _));

        Assert.False(coalescer.Offer(Frame(2)));

        Assert.True(coalescer.CompleteRender());
        Assert.True(coalescer.TryTake(out var latest));
        Assert.Equal((byte)2, latest!.Pixels[0]);
        Assert.False(coalescer.CompleteRender());
    }

    [Fact]
    public void Pooled_nv12_buffer_is_returned_once_and_cannot_be_used_after_return()
    {
        var pool = new TrackingArrayPool();
        var destination = System.Runtime.InteropServices.Marshal.AllocHGlobal(6);
        try
        {
            var rented = PooledNv12Buffer.Rent(6, pool);
            rented.Span.Fill(37);
            rented.CopyTo(destination);
            rented.Dispose();

            Assert.Equal(1, pool.ReturnCount);
            Assert.Throws<ObjectDisposedException>(() => _ = rented.Span[0]);
            var copied = new byte[6];
            System.Runtime.InteropServices.Marshal.Copy(destination, copied, 0, copied.Length);
            Assert.All(copied, value => Assert.Equal((byte)37, value));
        }
        finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(destination); }
    }

    [Fact]
    public void Nv12_conversion_overwrites_a_reused_buffer_without_cross_frame_corruption()
    {
        var pool = new TrackingArrayPool();
        var firstFrame = new VideoFrame(Enumerable.Repeat((byte)0, 16).ToArray(), 2, 2, 8, TimeSpan.Zero);
        var secondPixels = Enumerable.Repeat((byte)0, 16).ToArray();
        secondPixels[2] = 255;
        var secondFrame = new VideoFrame(secondPixels, 2, 2, 8, TimeSpan.Zero);

        byte[] first;
        using (var buffer = PooledNv12Buffer.Rent(6, pool))
        {
            MediaFoundationVideoEncoderService.ConvertToNv12(firstFrame, buffer.Span);
            first = buffer.Span.ToArray();
        }
        using (var buffer = PooledNv12Buffer.Rent(6, pool))
        {
            MediaFoundationVideoEncoderService.ConvertToNv12(secondFrame, buffer.Span);
            Assert.NotEqual(first, buffer.Span.ToArray());
        }
        Assert.Equal(2, pool.ReturnCount);
    }

    [Fact]
    public async Task Full_video_queue_drops_new_video_and_stop_drains_in_bounded_time()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TelaRecTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var capture = new BurstCapture();
        var encoder = new BlockingEncoder();
        await using var service = new RecordingService(capture, encoder, new SilentAudio(), NullLogger<RecordingService>.Instance);
        try
        {
            var profile = new RecordingProfile(RecordingContainer.Mp4, VideoCodec.H264, 1_000_000, 30, 2, 2);
            await service.StartAsync(new RecordingRequest(Path.Combine(directory, "pressure.mp4"), Profile: profile));
            await encoder.FirstEncodeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            capture.EmitMany(RecordingService.QueueCapacity * 3);
            var pressured = service.CurrentTelemetry;
            Assert.True(pressured.QueueDroppedVideoFrames > 0);
            Assert.InRange(pressured.PeakQueueDepth, 1, RecordingService.QueueCapacity);
            Assert.InRange(pressured.PeakReorderQueueDepth, 0, RecordingService.QueueCapacity);
            Assert.Equal(RecordingService.QueueCapacity, pressured.QueueCapacity);
            Assert.InRange(pressured.PeakQueuedVideoBytes, 1, pressured.MaximumQueuedVideoBytes);

            var stop = service.StopAsync();
            encoder.Release();
            await stop.WaitAsync(TimeSpan.FromSeconds(5));

            var completed = service.CurrentTelemetry;
            Assert.Equal(RecordingState.Completed, service.Status.State);
            Assert.Equal(0, completed.QueueDepth);
            Assert.Equal(0, completed.QueuedVideoBytes);
            Assert.True(completed.EncodedFrames > 0);
            Assert.True(completed.ManagedAllocatedBytes >= 0);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Audio_burst_does_not_consume_the_reserved_video_queue()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TelaRecTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var capture = new BurstCapture();
        var audio = new BurstAudio();
        var encoder = new BlockingEncoder();
        await using var service = new RecordingService(capture, encoder, audio, NullLogger<RecordingService>.Instance);
        try
        {
            var profile = new RecordingProfile(RecordingContainer.Mp4, VideoCodec.H264, 1_000_000, 30, 2, 2);
            await service.StartAsync(new RecordingRequest(Path.Combine(directory, "audio-pressure.mp4"), SystemAudioEnabled: true, Profile: profile));
            await encoder.FirstEncodeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            audio.EmitMany(20);
            capture.EmitMany(6);
            var pressured = service.CurrentTelemetry;

            Assert.InRange(pressured.QueueDroppedVideoFrames, 0, 1);
            Assert.True(pressured.QueuedVideoBytes >= 5 * 16);
            Assert.InRange(pressured.QueueDepth, 20, RecordingService.QueueCapacity);

            var stop = service.StopAsync();
            encoder.Release();
            await stop.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Consumer_exception_transitions_to_failed_and_dispose_does_not_deadlock()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TelaRecTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var capture = new BurstCapture();
        var encoder = new ThrowingEncoder();
        var service = new RecordingService(capture, encoder, new SilentAudio(), NullLogger<RecordingService>.Instance);
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.StatusChanged += (_, status) =>
        {
            if (status.State == RecordingState.Failed) failed.TrySetResult();
        };
        try
        {
            var profile = new RecordingProfile(RecordingContainer.Mp4, VideoCodec.H264, 1_000_000, 30, 2, 2);
            await service.StartAsync(new RecordingRequest(Path.Combine(directory, "failure.mp4"), Profile: profile));
            await encoder.FailureObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await failed.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(RecordingState.Failed, service.Status.State);
            await service.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await service.DisposeAsync();
            Directory.Delete(directory, true);
        }
    }

    private static VideoFrame Frame(byte value) => new([value, 0, 0, 255], 1, 1, 4, TimeSpan.Zero);

    private sealed class TrackingArrayPool : ArrayPool<byte>
    {
        private byte[]? _buffer;
        public int ReturnCount { get; private set; }
        public override byte[] Rent(int minimumLength)
        {
            if (_buffer is { } existing && existing.Length >= minimumLength) return existing;
            return _buffer = new byte[minimumLength];
        }
        public override void Return(byte[] array, bool clearArray = false)
        {
            Assert.Same(_buffer, array);
            ReturnCount++;
        }
    }

    private sealed class BurstCapture : IVideoCaptureService
    {
        public event EventHandler<VideoFrame>? FrameCaptured;
        public event EventHandler<Exception>? CaptureFailed { add { } remove { } }
        public CaptureFrameBounds? CaptureBounds { get; } = new(0, 0, 2, 2);
        public Task StartAsync(RecordingRequest request, CancellationToken cancellationToken = default)
        {
            EmitMany(1);
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void EmitMany(int count)
        {
            for (var index = 0; index < count; index++)
            {
                var ticks = (long)(Stopwatch.GetTimestamp() * (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency) + index;
                FrameCaptured?.Invoke(this, new VideoFrame([1, 2, 3, 255, 4, 5, 6, 255, 7, 8, 9, 255, 10, 11, 12, 255], 2, 2, 8, TimeSpan.FromTicks(ticks)));
            }
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class SilentAudio : IAudioCaptureService
    {
        public event EventHandler<AudioFrame>? AudioCaptured { add { } remove { } }
        public event EventHandler<Exception>? CaptureFailed { add { } remove { } }
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class BurstAudio : IAudioCaptureService
    {
        public event EventHandler<AudioFrame>? AudioCaptured;
        public event EventHandler<Exception>? CaptureFailed { add { } remove { } }
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void EmitMany(int count)
        {
            var origin = (long)(Stopwatch.GetTimestamp() * (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency);
            for (var index = 0; index < count; index++)
                AudioCaptured?.Invoke(this, new AudioFrame(new byte[1_920], 48_000, 2, TimeSpan.FromTicks(origin + index * TimeSpan.TicksPerSecond / 100)));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class BlockingEncoder : IVideoEncoderService
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FirstEncodeStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private string? _path;
        public Task StartAsync(RecordingRequest request, CancellationToken cancellationToken = default) { _path = request.OutputPath; return Task.CompletedTask; }
        public async Task EncodeAsync(VideoFrame frame, CancellationToken cancellationToken = default)
        {
            FirstEncodeStarted.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
        }
        public Task EncodeAudioAsync(AudioFrame frame, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CompleteAsync(CancellationToken cancellationToken = default) { File.WriteAllBytes(_path!, [1]); return Task.CompletedTask; }
        public void Release() => _release.TrySetResult();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ThrowingEncoder : IVideoEncoderService
    {
        public TaskCompletionSource FailureObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task StartAsync(RecordingRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EncodeAsync(VideoFrame frame, CancellationToken cancellationToken = default)
        {
            FailureObserved.TrySetResult();
            throw new InvalidOperationException("Falha injetada no consumidor");
        }
        public Task EncodeAudioAsync(AudioFrame frame, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CompleteAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
