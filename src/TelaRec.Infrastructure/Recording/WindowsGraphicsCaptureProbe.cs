using Windows.Graphics.Capture;

namespace TelaRec.Infrastructure.Recording;

internal static class WindowsGraphicsCaptureProbe
{
    public static bool IsSupported => GraphicsCaptureSession.IsSupported();
}
