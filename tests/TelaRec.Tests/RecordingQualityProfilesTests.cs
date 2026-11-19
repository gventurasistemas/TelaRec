using TelaRec.Core.Configuration;

namespace TelaRec.Tests;

public sealed class RecordingQualityProfilesTests
{
    [Theory]
    [InlineData("Muito Alta", 1920, 1080, 60)]
    [InlineData("Alta", 1920, 1080, 60)]
    [InlineData("Média", 1920, 1080, 30)]
    [InlineData("Baixa", 1280, 720, 30)]
    [InlineData("Econômica", 1280, 720, 24)]
    public void Profiles_define_expected_video_settings(string quality, int width, int height, int fps)
    {
        Assert.True(RecordingQualityProfiles.TryGet(quality, out var profile));
        Assert.Equal((width, height, fps), (profile.Width, profile.Height, profile.FramesPerSecond));
        Assert.InRange(profile.SuggestedBitrate, 500_000, 80_000_000);
    }
}
