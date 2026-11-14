using TelaRec.Core.Capture;

namespace TelaRec.Tests;

public sealed class CaptureTargetTests
{
    [Fact] public void Display_defaults_to_primary() => Assert.Equal(0, new CaptureTarget.Display().DisplayIndex);
    [Fact] public void Region_requires_positive_size() => Assert.False(new CaptureTarget.Region(0, 0, 0, 10).IsValid);
    [Fact] public void Valid_region_is_accepted() => Assert.True(new CaptureTarget.Region(10, 20, 640, 480).IsValid);
}
