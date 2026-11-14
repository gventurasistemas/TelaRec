using System.Runtime.InteropServices;
using TelaRec.Core.Scheduling;
using Windows.Graphics.Capture;

namespace TelaRec.Infrastructure.Reliability;

public sealed class WindowsRecordingAvailabilityProbe : IRecordingAvailabilityProbe
{
    public RecordingAvailability Check()
    {
        if (!GraphicsCaptureSession.IsSupported()) return new(false, "A captura de tela não é suportada nesta versão do Windows.");
        var desktop = OpenInputDesktop(0, false, 0x0100);
        if (desktop == IntPtr.Zero) return new(false, "A tela está bloqueada ou a sessão interativa não está disponível.");
        try { return SwitchDesktop(desktop) ? new(true) : new(false, "A tela está bloqueada."); }
        finally { CloseDesktop(desktop); }
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] private static extern bool SwitchDesktop(IntPtr desktop);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr desktop);
}
