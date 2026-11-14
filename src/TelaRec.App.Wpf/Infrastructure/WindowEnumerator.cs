using System.Runtime.InteropServices;

namespace TelaRec.App.Wpf.Infrastructure;

public sealed record DesktopWindow(nint Handle, string Title);
public static class WindowEnumerator
{
    public static IReadOnlyList<DesktopWindow> GetVisibleWindows()
    {
        var result = new List<DesktopWindow>();
        EnumWindows((handle, _) =>
        {
            if (IsWindowVisible(handle) && GetWindowTextLength(handle) > 0)
            {
                var buffer = new System.Text.StringBuilder(GetWindowTextLength(handle) + 1);
                GetWindowText(handle, buffer, buffer.Capacity);
                result.Add(new DesktopWindow(handle, buffer.ToString()));
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }
    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hWnd, System.Text.StringBuilder text, int maxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(nint hWnd);
}
