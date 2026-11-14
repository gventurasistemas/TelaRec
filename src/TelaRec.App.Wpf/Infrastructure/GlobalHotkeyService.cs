using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows;
using TelaRec.Core.Input;
using TelaRec.App.Wpf.Resources;

namespace TelaRec.App.Wpf.Infrastructure;

public sealed record HotkeyRegistrationResult(bool Success, string? Error)
{
    public static HotkeyRegistrationResult Ok { get; } = new(true, null);
    public static HotkeyRegistrationResult Failed(string error) => new(false, error);
}

/// <summary>
/// Registro dinâmico de atalhos globais. Cada atalho pode ser re-registrado sem
/// reiniciar o app e o conflito com outro programa é devolvido ao chamador.
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private readonly HwndSource _source;
    private readonly Dictionary<int, HotkeyGesture> _registered = new();
    private bool _disposed;

    public event Action<int>? Pressed;

    public GlobalHotkeyService(Window window)
    {
        _source = (HwndSource)PresentationSource.FromVisual(window)!;
        _source.AddHook(WndProc);
    }

    public IReadOnlyDictionary<int, HotkeyGesture> Registered => _registered;

    public HotkeyRegistrationResult Register(int id, HotkeyGesture gesture)
    {
        if (_disposed) return HotkeyRegistrationResult.Failed(Strings.Get("HotkeyServiceUnavailable"));
        if (!gesture.IsValid) return HotkeyRegistrationResult.Failed(Strings.Get("HotkeyInvalidFormat"));

        Unregister(id);
        if (_registered.Values.Any(existing => existing == gesture))
        {
            var duplicate = string.Format(Strings.Get("HotkeyDuplicate"), gesture);
            return HotkeyRegistrationResult.Failed(duplicate);
        }

        if (!RegisterHotKey(_source.Handle, id, (uint)gesture.Modifiers, (uint)gesture.VirtualKey))
        {
            var conflict = string.Format(Strings.Get("HotkeyInUse"), gesture);
            return HotkeyRegistrationResult.Failed(conflict);
        }

        _registered[id] = gesture;
        return HotkeyRegistrationResult.Ok;
    }

    public void Unregister(int id)
    {
        if (!_registered.Remove(id)) return;
        UnregisterHotKey(_source.Handle, id);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmHotkey) return IntPtr.Zero;
        Pressed?.Invoke(wParam.ToInt32());
        handled = true;
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var id in _registered.Keys.ToArray()) UnregisterHotKey(_source.Handle, id);
        _registered.Clear();
        _source.RemoveHook(WndProc);
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
