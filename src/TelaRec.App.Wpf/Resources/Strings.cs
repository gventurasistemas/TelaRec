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
}
