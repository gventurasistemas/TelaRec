using TelaRec.Core.Capture;

namespace TelaRec.Tests;

public sealed class CaptureTargetTests
{
    [Fact] public void Display_defaults_to_primary() => Assert.Equal(0, new CaptureTarget.Display().DisplayIndex);
    [Fact] public void Region_requires_positive_size() => Assert.False(new CaptureTarget.Region(0, 0, 0, 10).IsValid);
    [Fact] public void Valid_region_is_accepted() => Assert.True(new CaptureTarget.Region(10, 20, 640, 480).IsValid);

    [Fact]
    public void Persisted_region_round_trips_without_a_window_handle()
    {
        var expected = new CaptureTarget.Region(-1200, 80, 640, 480);
        var settings = CaptureTargetSettings.FromTarget(expected);

        Assert.True(settings.TryCreateNonWindowTarget(out var restored));
        Assert.Equal(expected, restored);
    }

    [Fact]
    public void Persisted_window_requires_windows_layer_revalidation()
    {
        var settings = CaptureTargetSettings.FromTarget(new CaptureTarget.Window((nint)42, "Editor"));

        Assert.False(settings.TryCreateNonWindowTarget(out var restored));
        Assert.Null(restored);
    }
}
