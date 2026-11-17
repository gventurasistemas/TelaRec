using Microsoft.Extensions.Logging.Abstractions;
using TelaRec.Core.Contracts;
using TelaRec.Core.Recording;
using TelaRec.Infrastructure.Recording;

namespace TelaRec.Tests;

public sealed class PerformanceTelemetryTests
{
    [Fact]
    public void Dropped_frames_combine_rate_limit_and_queue_pressure()
    {
        var telemetry = new RecordingPerformanceTelemetry(100, 7, 3, 5, 2, 88, TimeSpan.FromMilliseconds(4), true);

        Assert.Equal(12, telemetry.DroppedFrames);
        Assert.True(telemetry.UsingHardwareEncoder);
    }

    [Fact]
    public void Hevc_capability_is_available_from_either_hardware_or_software()
    {
        Assert.True(new VideoEncoderCapabilities(false, true).HevcSupported);
        Assert.True(new VideoEncoderCapabilities(false, false, true).HevcSupported);
        Assert.False(VideoEncoderCapabilities.SafeDefault.HevcSupported);
    }

    [Fact]
    public void Media_foundation_capability_probe_is_safe_on_this_windows_installation()
    {
        var capabilities = new MediaFoundationVideoEncoderCapabilitiesService().GetCapabilities();
        Assert.NotNull(capabilities);
    }

    [Fact]
    public async Task Unsupported_hevc_is_rejected_before_output_directory_is_created()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TelaRecTests", Guid.NewGuid().ToString("N"), "missing");
        var profile = new RecordingProfile(RecordingContainer.Mp4, VideoCodec.Hevc, 5_500_000, 30, 1280, 720);
        await using var encoder = new MediaFoundationVideoEncoderService(
            NullLogger<MediaFoundationVideoEncoderService>.Instance,
            new FakeCapabilities(VideoEncoderCapabilities.SafeDefault));

        var error = await Assert.ThrowsAsync<NotSupportedException>(() =>
            encoder.StartAsync(new RecordingRequest(Path.Combine(directory, "clip.partial.mp4"), Profile: profile)));

        Assert.Contains("H.264", error.Message);
        Assert.False(Directory.Exists(directory));
    }

    private sealed class FakeCapabilities(VideoEncoderCapabilities capabilities) : IVideoEncoderCapabilitiesService
    {
        public VideoEncoderCapabilities GetCapabilities() => capabilities;
    }
}
