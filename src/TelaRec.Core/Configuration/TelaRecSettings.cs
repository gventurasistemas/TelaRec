using TelaRec.Core.Recording;

namespace TelaRec.Core.Configuration;

public sealed class TelaRecSettings
{
    /// <summary>Versão do documento settings.json; ausente em instalações anteriores à P23.</summary>
    public int SchemaVersion { get; set; } = PersistenceSchemas.Settings;
    /// <summary>Versão dos IDs de preset persistidos em <see cref="SelectedPreset"/>.</summary>
    public int PresetSchemaVersion { get; set; } = PersistenceSchemas.Presets;

    public string OutputDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        "TelaRec");

    public int FramesPerSecond { get; set; } = 60;

    public bool SystemAudioEnabled { get; set; } = true;
    public bool MicrophoneEnabled { get; set; }
    public string? MicrophoneDeviceId { get; set; }
    public float SystemAudioGain { get; set; } = 1;
    public float MicrophoneGain { get; set; } = 1;
    public bool SystemAudioMuted { get; set; }
    public bool MicrophoneMuted { get; set; }

    public string Quality { get; set; } = "Alta";
    public string ToggleRecordingHotkey { get; set; } = "Ctrl+Alt+R";
    public string TogglePauseHotkey { get; set; } = "Ctrl+Alt+P";
    public string ScreenshotHotkey { get; set; } = "Ctrl+Alt+S";
    public string LastCaptureMode { get; set; } = "fullscreen";
    public Capture.CaptureTargetSettings LastCaptureTarget { get; set; } = new();
    public bool CloseToTray { get; set; } = true;
    public bool ShowCursor { get; set; } = true;
    public bool ShowClickEffects { get; set; } = false;
    public double ClickEffectDurationSeconds { get; set; } = 0.6;
    public double ClickEffectRadius { get; set; } = 28;
    public string ClickEffectColor { get; set; } = "#FF3B30";
    public string SelectedPreset { get; set; } = "high-quality-60";
    public RecordingContainer RecordingContainer { get; set; } = RecordingContainer.Mp4;
    public VideoCodec VideoCodec { get; set; } = VideoCodec.H264;
    public VideoEncoderAcceleration EncoderAcceleration { get; set; } = VideoEncoderAcceleration.Auto;
    public int VideoBitrate { get; set; } = 15_000_000;
    public int OutputWidth { get; set; } = 1920;
    public int OutputHeight { get; set; } = 1080;
    public int MaximumGifSizeMegabytes { get; set; } = 25;
    public WatermarkSettings Watermark { get; set; } = new();
    public WebcamOverlaySettings Webcam { get; set; } = new();
}
