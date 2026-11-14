using TelaRec.Core.Recording;
using TelaRec.Infrastructure.Recording;

namespace TelaRec.Tests;

public sealed class VideoFrameComposerTests
{
    [Fact]
    public void Webcam_overlay_is_composed_with_opacity()
    {
        using var composer = new VideoFrameComposer();
        composer.Configure(new RecordingOverlaySettings(new WatermarkSettings(),
            new WebcamOverlaySettings(true, 0, OverlayPosition.TopLeft, 0.5, 0.5)));
        composer.UpdateWebcamFrame(new VideoFrame(Enumerable.Repeat((byte)255, 2 * 2 * 4).ToArray(), 2, 2, 8, TimeSpan.Zero));
        var output = composer.Compose(new VideoFrame(new byte[4 * 4 * 4], 4, 4, 16, TimeSpan.Zero));

        Assert.InRange(output.Pixels[60], (byte)126, (byte)128);
        Assert.Equal(0, output.Pixels[0]);
    }
    [Fact]
    public void Text_watermark_changes_the_output_frame()
    {
        using var composer = new VideoFrameComposer();
        composer.Configure(new RecordingOverlaySettings(
            new WatermarkSettings(WatermarkKind.Text, "TelaRec", null, OverlayPosition.BottomRight, 1, 0.2),
            new WebcamOverlaySettings()));

        var output = composer.Compose(new VideoFrame(new byte[320 * 180 * 4], 320, 180, 1280, TimeSpan.Zero));

        Assert.Contains(output.Pixels, pixel => pixel != 0);
    }
}
