using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows;

namespace TelaRec.App.Wpf.Infrastructure;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private readonly HwndSource _source;
    public event Action<int>? Pressed;
    public GlobalHotkeyService(Window window)
    {
        _source = (HwndSource)PresentationSource.FromVisual(window)!;
        _source.AddHook(WndProc);
    }
    public bool Register(int id, uint modifiers, uint key) => RegisterHotKey(_source.Handle, id, modifiers, key);
    public void Dispose() { UnregisterHotKey(_source.Handle, 1); UnregisterHotKey(_source.Handle, 2); _source.RemoveHook(WndProc); }
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    { if (msg == WmHotkey) { Pressed?.Invoke(wParam.ToInt32()); handled = true; } return IntPtr.Zero; }
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
