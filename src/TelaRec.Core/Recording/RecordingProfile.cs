namespace TelaRec.Core.Recording;
public enum RecordingContainer { Mp4, Mkv, Mov, Gif }
public enum VideoCodec { H264, Hevc, Gif }
public sealed record RecordingProfile(RecordingContainer Container, VideoCodec VideoCodec, int Bitrate, int FramesPerSecond, int Width, int Height)
{
    public static RecordingProfile Mvp { get; } = new(RecordingContainer.Mp4, VideoCodec.H264, 8_000_000, 30, 1920, 1080);
    public string FileExtension => Container switch { RecordingContainer.Mp4 => ".mp4", RecordingContainer.Mkv => ".mkv", RecordingContainer.Mov => ".mov", RecordingContainer.Gif => ".gif", _ => throw new ArgumentOutOfRangeException() };
    public bool IsValid =>
        Bitrate > 0 && FramesPerSecond is > 0 and <= 120 && Width > 0 && Height > 0 &&
        (Container == RecordingContainer.Gif || Width % 2 == 0 && Height % 2 == 0) &&
        (Container != RecordingContainer.Gif || VideoCodec == VideoCodec.Gif) &&
        (Container == RecordingContainer.Gif || VideoCodec is VideoCodec.H264 or VideoCodec.Hevc);
}
