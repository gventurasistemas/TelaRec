using TelaRec.Core.Recording;

namespace TelaRec.Tests;

public sealed class ClickEffectProjectionTests
{
    [Fact]
    public void Full_display_at_native_size_maps_one_to_one()
    {
        var projected = ClickEffectProjection.TryProject(
            new ClickEvent(640, 480, TimeSpan.Zero),
            new CaptureFrameBounds(0, 0, 1920, 1080),
            1920,
            1080,
            out var x,
            out var y);

        Assert.True(projected);
        Assert.Equal(640, x);
        Assert.Equal(480, y);
    }

    [Fact]
    public void Region_crop_and_output_scale_are_applied()
    {
        var projected = ClickEffectProjection.TryProject(
            new ClickEvent(300, 150, TimeSpan.Zero),
            new CaptureFrameBounds(100, 50, 400, 200),
            800,
            400,
            out var x,
            out var y);

        Assert.True(projected);
        Assert.Equal(400, x);
        Assert.Equal(200, y);
    }

    [Fact]
    public void Secondary_monitor_offset_is_removed()
    {
        var projected = ClickEffectProjection.TryProject(
            new ClickEvent(1920, 0, TimeSpan.Zero),
            new CaptureFrameBounds(1920, 0, 1920, 1080),
            960,
            540,
            out var x,
            out var y);

        Assert.True(projected);
        Assert.Equal(0, x);
        Assert.Equal(0, y);
    }

    [Theory]
    [InlineData(99, 100)]
    [InlineData(500, 100)]
    [InlineData(300, 49)]
    [InlineData(300, 300)]
    public void Clicks_outside_capture_bounds_are_ignored(int screenX, int screenY)
    {
        var projected = ClickEffectProjection.TryProject(
            new ClickEvent(screenX, screenY, TimeSpan.Zero),
            new CaptureFrameBounds(100, 50, 400, 200),
            800,
            400,
            out _,
            out _);

        Assert.False(projected);
    }

    [Fact]
    public void Invalid_source_bounds_are_rejected()
    {
        Assert.False(ClickEffectProjection.TryProject(
            new ClickEvent(10, 10, TimeSpan.Zero),
            new CaptureFrameBounds(0, 0, 0, 0),
            100,
            100,
            out _,
            out _));
    }
}
