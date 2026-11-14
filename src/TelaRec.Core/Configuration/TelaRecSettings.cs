using TelaRec.Core.Recording;

namespace TelaRec.Core.Configuration;

public sealed class TelaRecSettings
{
    public string OutputDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        "TelaRec");

    public int FramesPerSecond { get; set; } = 30;

    public bool SystemAudioEnabled { get; set; } = true;

    public string Quality { get; set; } = "Alta";
    public string ToggleRecordingHotkey { get; set; } = "Ctrl+Alt+R";
    public string TogglePauseHotkey { get; set; } = "Ctrl+Alt+P";
    public string ScreenshotHotkey { get; set; } = "Ctrl+Alt+S";
    public string LastCaptureMode { get; set; } = "Tela inteira";
    public bool ShowCursor { get; set; } = true;
    public bool ShowClickEffects { get; set; } = false;
    public double ClickEffectDurationSeconds { get; set; } = 0.6;
    public double ClickEffectRadius { get; set; } = 28;
    public string ClickEffectColor { get; set; } = "#FF3B30";
    public string SelectedPreset { get; set; } = "Tutorial 1080p";
    public RecordingContainer RecordingContainer { get; set; } = RecordingContainer.Mp4;
    public VideoCodec VideoCodec { get; set; } = VideoCodec.H264;
    public int VideoBitrate { get; set; } = 8_000_000;
    public int OutputWidth { get; set; } = 1920;
    public int OutputHeight { get; set; } = 1080;
    public WatermarkSettings Watermark { get; set; } = new();
    public WebcamOverlaySettings Webcam { get; set; } = new();
}
