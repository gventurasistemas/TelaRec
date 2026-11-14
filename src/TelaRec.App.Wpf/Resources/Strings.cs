using System.Globalization;
using System.Resources;

namespace TelaRec.App.Wpf.Resources;

public static class Strings
{
    private static readonly ResourceManager Manager = new("TelaRec.App.Wpf.Resources.Strings", typeof(Strings).Assembly);
    public static string Get(string key) => Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
    public static string AppTitle => Get(nameof(AppTitle));
    public static string ScreenRecorder => Get(nameof(ScreenRecorder));
    public static string Recording => Get(nameof(Recording));
    public static string Settings => Get(nameof(Settings));
    public static string Start => Get(nameof(Start));
    public static string Pause => Get(nameof(Pause));
    public static string Resume => Get(nameof(Resume));
    public static string Stop => Get(nameof(Stop));
    public static string OpenFolder => Get(nameof(OpenFolder));
    public static string SaveSettings => Get(nameof(SaveSettings));
    public static string OutputFolder => Get(nameof(OutputFolder));
    public static string Audio => Get(nameof(Audio));
    public static string Capture => Get(nameof(Capture));
    public static string Watermark => Get(nameof(Watermark));
    public static string Webcam => Get(nameof(Webcam));
    public static string Enabled => Get(nameof(Enabled));
    public static string WatermarkText => Get(nameof(WatermarkText));
    public static string WatermarkImage => Get(nameof(WatermarkImage));
    public static string Position => Get(nameof(Position));
    public static string Opacity => Get(nameof(Opacity));
    public static string Scale => Get(nameof(Scale));
    public static string Cursor => Get(nameof(Cursor));
    public static string ShowCursor => Get(nameof(ShowCursor));
    public static string ClickEffects => Get(nameof(ClickEffects));
    public static string EnableClickEffects => Get(nameof(EnableClickEffects));
    public static string StartHint => Get(nameof(StartHint));
    public static string ScheduleStart => Get(nameof(ScheduleStart));
    public static string ScheduleMinutes => Get(nameof(ScheduleMinutes));
    public static string Schedule => Get(nameof(Schedule));
    public static string Screenshot => Get(nameof(Screenshot));
    public static string ScreenshotHint => Get(nameof(ScreenshotHint));
    public static string Preset => Get(nameof(Preset));
    public static string CustomPreset => Get(nameof(CustomPreset));
    public static string Quality => Get(nameof(Quality));
    public static string ToggleRecordingHotkey => Get(nameof(ToggleRecordingHotkey));
    public static string TogglePauseHotkey => Get(nameof(TogglePauseHotkey));
    public static string ScreenshotHotkey => Get(nameof(ScreenshotHotkey));
    public static string ScreenshotSaved => Get(nameof(ScreenshotSaved));
    public static string ScreenshotFailed => Get(nameof(ScreenshotFailed));
    public static string HotkeyInvalid => Get(nameof(HotkeyInvalid));
    public static string HotkeyDuplicate => Get(nameof(HotkeyDuplicate));
    public static string HotkeyDuplicateConflict => Get(nameof(HotkeyDuplicateConflict));
    public static string HotkeyInUse => Get(nameof(HotkeyInUse));
    public static string HotkeyInvalidFormat => Get(nameof(HotkeyInvalidFormat));
    public static string HotkeyServiceUnavailable => Get(nameof(HotkeyServiceUnavailable));
    public static string SettingsSaved => Get(nameof(SettingsSaved));
    public static string QualityHint => Get(nameof(QualityHint));
}
