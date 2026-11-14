using TelaRec.Core.Recording;
using TelaRec.Infrastructure.Recording;

namespace TelaRec.Tests;

public sealed class BgraFrameScalerTests
{
    [Fact]
    public void Scale_letterboxes_and_preserves_source_pixels()
    {
        var source = new VideoFrame(
            [10, 20, 30, 255, 40, 50, 60, 255],
            2, 1, 8, TimeSpan.Zero);

        var result = new BgraFrameScaler().Scale(source, 4, 4);

        Assert.Equal((4, 4, 16), (result.Width, result.Height, result.Stride));
        Assert.Equal(0, result.Pixels[0]);
        Assert.Equal(10, result.Pixels[16]);
        Assert.Equal(40, result.Pixels[24]);
    }
}
