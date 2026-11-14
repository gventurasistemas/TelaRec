using TelaRec.Core.Configuration;
using TelaRec.Core.Recording;

namespace TelaRec.Tests;

public sealed class SettingsValidatorTests
{
    [Fact]
    public void Accepts_valid_settings() =>
        Assert.Empty(SettingsValidator.Validate(Valid(), verifyOutputDirectory: false));

    [Theory]
    [InlineData(0)]
    [InlineData(121)]
    public void Rejects_fps_out_of_range(int fps)
    {
        var settings = Valid();
        settings.FramesPerSecond = fps;
        Assert.Contains(SettingsValidator.Validate(settings, verifyOutputDirectory: false),
            issue => issue.Code == SettingsValidationCode.FramesPerSecondOutOfRange);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100)]
    [InlineData(100_000_000)]
    public void Rejects_bitrate_out_of_range(int bitrate)
    {
        var settings = Valid();
        settings.VideoBitrate = bitrate;
        Assert.Contains(SettingsValidator.Validate(settings, verifyOutputDirectory: false),
            issue => issue.Code == SettingsValidationCode.VideoBitrateOutOfRange);
    }

    [Fact]
    public void Rejects_odd_dimensions_for_mp4()
    {
        var settings = Valid();
        settings.OutputWidth = 1281;
        Assert.Contains(SettingsValidator.Validate(settings, verifyOutputDirectory: false),
            issue => issue.Code == SettingsValidationCode.ResolutionMustBeEven);
    }

    [Fact]
    public void Accepts_odd_dimensions_for_gif()
    {
        var settings = Valid();
        settings.RecordingContainer = RecordingContainer.Gif;
        settings.VideoCodec = VideoCodec.Gif;
        settings.OutputWidth = 641;
        settings.OutputHeight = 481;
        Assert.Empty(SettingsValidator.Validate(settings, verifyOutputDirectory: false));
    }

    [Fact]
    public void Rejects_relative_output_directory()
    {
        var settings = Valid();
        settings.OutputDirectory = "videos";
        Assert.Contains(SettingsValidator.Validate(settings, verifyOutputDirectory: false),
            issue => issue.Code == SettingsValidationCode.OutputDirectoryNotAbsolute);
    }

    [Fact]
    public void Allows_mp4_with_hevc_and_defers_capability_check_to_encoder()
    {
        var settings = Valid();
        settings.VideoCodec = VideoCodec.Hevc;
        Assert.DoesNotContain(SettingsValidator.Validate(settings, verifyOutputDirectory: false),
            issue => issue.Code == SettingsValidationCode.ProfileNotSupported);
    }

    [Fact]
    public void Verifies_that_output_directory_is_writable()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TelaRecSettingsTests", Guid.NewGuid().ToString("N"));
        var settings = Valid();
        settings.OutputDirectory = directory;
        try
        {
            Assert.Empty(SettingsValidator.Validate(settings));
            Assert.True(Directory.Exists(directory));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static TelaRecSettings Valid() => new()
    {
        OutputDirectory = Path.Combine(Path.GetTempPath(), "TelaRecSettingsTests"),
        FramesPerSecond = 30,
        VideoBitrate = 8_000_000,
        OutputWidth = 1920,
        OutputHeight = 1080,
        RecordingContainer = RecordingContainer.Mp4,
        VideoCodec = VideoCodec.H264
    };
}
