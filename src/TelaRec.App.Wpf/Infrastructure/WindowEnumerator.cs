using System.Runtime.InteropServices;

namespace TelaRec.App.Wpf.Infrastructure;

public sealed record DesktopWindow(nint Handle, string Title, bool IsVisible = true, bool IsMinimized = false, uint ProcessId = 0);

internal static class WindowSelectionPolicy
{
    public static bool CanSelect(DesktopWindow window, uint currentProcessId) =>
        window.Handle != IntPtr.Zero &&
        window.IsVisible &&
        !window.IsMinimized &&
        !string.IsNullOrWhiteSpace(window.Title) &&
        window.ProcessId != currentProcessId;
}

public static class WindowEnumerator
{
    private const uint WmGetText = 0x000D;
    private const uint SmtoAbortIfHung = 0x0002;
    private const uint SelectionValidationTimeoutMilliseconds = 250;

    public static IReadOnlyList<DesktopWindow> GetVisibleWindows()
    {
        var result = new List<DesktopWindow>();
        var currentProcessId = (uint)Environment.ProcessId;
        EnumWindows((handle, _) =>
        {
            if (IsWindowVisible(handle) && GetWindowTextLength(handle) > 0)
            {
                var buffer = new System.Text.StringBuilder(GetWindowTextLength(handle) + 1);
                GetWindowText(handle, buffer, buffer.Capacity);
                GetWindowThreadProcessId(handle, out var processId);
                var window = new DesktopWindow(handle, buffer.ToString(), true, IsIconic(handle), processId);
                if (WindowSelectionPolicy.CanSelect(window, currentProcessId)) result.Add(window);
            }
            return true;
        }, IntPtr.Zero);
        return result.OrderBy(window => window.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public static bool TryGetSelectableWindow(nint handle, out DesktopWindow window)
    {
        window = default!;
        if (!IsWindow(handle) || !IsWindowVisible(handle) || IsIconic(handle)) return false;
        GetWindowThreadProcessId(handle, out var processId);
        if (!TryGetWindowTitle(handle, out var title)) return false;
        var candidate = new DesktopWindow(handle, title, true, false, processId);
        if (!WindowSelectionPolicy.CanSelect(candidate, (uint)Environment.ProcessId)) return false;
        window = candidate;
        return true;
    }

    private static bool TryGetWindowTitle(nint handle, out string title)
    {
        title = string.Empty;
        var length = GetWindowTextLength(handle);
        if (length <= 0) return false;
        var buffer = new System.Text.StringBuilder(length + 1);
        var result = SendMessageTimeout(handle, WmGetText, (nuint)buffer.Capacity, buffer, SmtoAbortIfHung, SelectionValidationTimeoutMilliseconds, out _);
        if (result == IntPtr.Zero || buffer.Length == 0) return false;
        title = buffer.ToString();
        return true;
    }

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hWnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hWnd, System.Text.StringBuilder text, int maxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(nint hWnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageTimeout(nint hWnd, uint message, nuint wParam, System.Text.StringBuilder lParam, uint flags, uint timeout, out nuint result);
}
