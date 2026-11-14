using TelaRec.Core.Configuration;

namespace TelaRec.Tests;

public sealed class RecordingPresetsTests
{
    [Fact]
    public void Contains_three_named_presets() => Assert.Equal(3, RecordingPresets.All.Count);

    [Fact]
    public void Preset_ids_are_unique_and_stable()
    {
        var ids = RecordingPresets.All.Select(preset => preset.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("tutorial-1080p", ids);
        Assert.Contains("light-720p", ids);
        Assert.Contains("high-quality-60", ids);
    }

    [Theory]
    [InlineData("tutorial-1080p", 1920, 1080, 30)]
    [InlineData("light-720p", 1280, 720, 30)]
    [InlineData("high-quality-60", 1920, 1080, 60)]
    public void Preset_has_expected_dimensions(string id, int width, int height, int fps)
    {
        var preset = Assert.Single(RecordingPresets.All.Where(candidate => candidate.Id == id));
        Assert.Equal((width, height, fps), (preset.Width, preset.Height, preset.FramesPerSecond));
        Assert.False(string.IsNullOrWhiteSpace(preset.ResourceKey));
    }
}
