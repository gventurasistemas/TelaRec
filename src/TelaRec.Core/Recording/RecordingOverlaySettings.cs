namespace TelaRec.Core.Recording;

public enum OverlayPosition { TopLeft, TopRight, BottomLeft, BottomRight }
public enum WatermarkKind { None, Text, Image }

public sealed record WatermarkSettings(
    WatermarkKind Kind = WatermarkKind.None,
    string? Text = null,
    string? ImagePath = null,
    OverlayPosition Position = OverlayPosition.BottomRight,
    double Opacity = 0.75,
    double Scale = 1.0);

public sealed record WebcamOverlaySettings(
    bool Enabled = false,
    int DeviceIndex = 0,
    OverlayPosition Position = OverlayPosition.BottomRight,
    double Opacity = 1.0,
    double Scale = 0.25);

public sealed record RecordingOverlaySettings(
    WatermarkSettings Watermark,
    WebcamOverlaySettings Webcam,
    ClickEffectSettings? ClickEffects = null)
{
    public ClickEffectSettings EffectiveClickEffects => ClickEffects ?? ClickEffectSettings.None;

    public static RecordingOverlaySettings None { get; } = new(new WatermarkSettings(), new WebcamOverlaySettings());
}
