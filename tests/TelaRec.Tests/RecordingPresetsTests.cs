using TelaRec.Core.Configuration;
namespace TelaRec.Tests;
public sealed class RecordingPresetsTests
{
    [Fact] public void Contains_three_named_presets() => Assert.Equal(3, RecordingPresets.All.Count);
    [Theory]
    [InlineData("Tutorial 1080p", 1920, 1080, 30)]
    [InlineData("Leve 720p", 1280, 720, 30)]
    [InlineData("Alta qualidade 60 FPS", 1920, 1080, 60)]
    public void Preset_has_expected_dimensions(string name, int width, int height, int fps)
    { var preset = Assert.Single(RecordingPresets.All.Where(x => x.Name == name)); Assert.Equal((width,height,fps), (preset.Width,preset.Height,preset.FramesPerSecond)); }
}
