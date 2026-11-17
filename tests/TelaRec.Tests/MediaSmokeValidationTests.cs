using TelaRec.Core.Recording;
using TelaRec.Infrastructure.Recording;

namespace TelaRec.Tests;

public sealed class MediaSmokeValidationTests
{
    private static readonly MediaSmokeExpectation Expected = new(
        VideoCodec.H264, 640, 480, 15, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(8), AudioRequired: true);

    [Fact]
    public void Valid_media_passes_all_smoke_assertions()
    {
        var result = MediaSmokeValidator.Validate(ValidInspection(), Expected);

        Assert.Empty(result);
    }

    [Fact]
    public void Truncated_or_empty_media_is_reported_with_precise_causes()
    {
        var result = MediaSmokeValidator.Validate(ValidInspection() with { FileBytes = 0, HasMp4Signature = false }, Expected);

        Assert.Contains(result, failure => failure.Contains("arquivo vazio"));
        Assert.Contains(result, failure => failure.Contains("ftyp"));
    }

    [Fact]
    public void Incorrect_codec_missing_audio_and_black_frames_fail_the_smoke()
    {
        var result = MediaSmokeValidator.Validate(ValidInspection() with
        {
            VideoCodec = VideoCodec.Hevc,
            NonBlackVideoSamples = 0,
            DecodedAudioSamples = 0,
            AudioDuration = null
        }, Expected);

        Assert.Contains(result, failure => failure.Contains("codec"));
        Assert.Contains(result, failure => failure.Contains("pretos"));
        Assert.Contains(result, failure => failure.Contains("áudio"));
    }

    [Fact]
    public void Duration_fps_dimensions_and_av_drift_are_validated()
    {
        var result = MediaSmokeValidator.Validate(ValidInspection() with
        {
            Width = 1280,
            FramesPerSecond = 60,
            VideoDuration = TimeSpan.FromSeconds(9),
            AudioDuration = TimeSpan.FromSeconds(8)
        }, Expected);

        Assert.Contains(result, failure => failure.Contains("dimensões"));
        Assert.Contains(result, failure => failure.Contains("FPS"));
        Assert.Contains(result, failure => failure.Contains("duração"));
        Assert.Contains(result, failure => failure.Contains("deriva"));
    }

    private static MediaInspection ValidInspection() => new(
        FileBytes: 1024,
        HasMp4Signature: true,
        VideoCodec: VideoCodec.H264,
        Width: 640,
        Height: 480,
        FramesPerSecond: 15,
        VideoDuration: TimeSpan.FromSeconds(2),
        DecodedVideoSamples: 30,
        NonBlackVideoSamples: 30,
        AudioDuration: TimeSpan.FromSeconds(2.05),
        DecodedAudioSamples: 50);
}
