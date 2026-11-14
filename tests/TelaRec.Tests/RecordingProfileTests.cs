using TelaRec.Core.Recording;
namespace TelaRec.Tests;
public sealed class RecordingProfileTests
{
    [Fact] public void Mvp_profile_is_valid() => Assert.True(RecordingProfile.Mvp.IsValid);
    [Fact] public void Odd_dimensions_are_invalid() => Assert.False(new RecordingProfile(RecordingContainer.Mp4, VideoCodec.H264,1,30,1281,720).IsValid);
    [Fact] public void Rejects_invalid_frame_rate() => Assert.False(new RecordingProfile(RecordingContainer.Mp4,VideoCodec.H264,1,0,1280,720).IsValid);
    [Fact] public void Gif_requires_gif_codec() => Assert.False(new RecordingProfile(RecordingContainer.Gif, VideoCodec.H264, 1, 15, 641, 481).IsValid);
    [Fact] public void Gif_accepts_odd_dimensions() => Assert.True(new RecordingProfile(RecordingContainer.Gif, VideoCodec.Gif, 1, 15, 641, 481).IsValid);
    [Fact] public void Container_selects_expected_extension() => Assert.Equal(".mkv", new RecordingProfile(RecordingContainer.Mkv, VideoCodec.H264, 1, 30, 1280, 720).FileExtension);
}
