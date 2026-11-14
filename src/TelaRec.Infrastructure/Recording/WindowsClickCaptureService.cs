using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using TelaRec.Core.Contracts;
using TelaRec.Core.Recording;

namespace TelaRec.Infrastructure.Recording;

/// <summary>
/// Hook global passivo de cliques (WH_MOUSE_LL). Roda em uma thread própria com
/// message loop e sempre repassa o evento ao próximo hook, sem bloquear cliques.
/// </summary>
public sealed class WindowsClickCaptureService : IClickCaptureService, IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmLeftButtonDown = 0x0201;
    private const int WmRightButtonDown = 0x0204;
    private const int WmMiddleButtonDown = 0x0207;
    private const uint WmQuit = 0x0012;

    private readonly ILogger<WindowsClickCaptureService> _logger;
    private readonly LowLevelMouseProc _hookProc;
    private readonly object _sync = new();
    private Thread? _thread;
    private uint _threadId;
    private nint _hook;
    private bool _capturing;

    public WindowsClickCaptureService(ILogger<WindowsClickCaptureService> logger)
    {
        _logger = logger;
        _hookProc = OnMouseEvent;
    }

    public event EventHandler<ClickEvent>? ClickCaptured;
    public bool IsCapturing { get { lock (_sync) return _capturing; } }

    public void Start()
    {
        var ready = new ManualResetEventSlim(false);
        lock (_sync)
        {
            if (_capturing)
            {
                ready.Dispose();
                return;
            }

            _capturing = true;
            _thread = new Thread(() => RunHook(ready))
            {
                IsBackground = true,
                Name = "TelaRec.ClickHook"
            };
            _thread.Start();
        }

        // Não segura o lock enquanto a thread instala o hook.
        ready.Wait(TimeSpan.FromSeconds(3));
    }

    private void RunHook(ManualResetEventSlim ready)
    {
        try
        {
            lock (_sync) _threadId = GetCurrentThreadId();
            _hook = SetWindowsHookEx(WhMouseLl, _hookProc, GetModuleHandle(null), 0);
            if (_hook == nint.Zero)
            {
                _logger.LogWarning("Não foi possível instalar o hook de cliques (erro {Error})", Marshal.GetLastWin32Error());
                ready.Set();
                return;
            }

            ready.Set();
            _logger.LogInformation("Hook de cliques instalado");
            while (GetMessage(out var message, nint.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Hook de cliques encerrado com falha");
            ready.Set();
        }
        finally
        {
            if (_hook != nint.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = nint.Zero;
            }
            lock (_sync)
            {
                _threadId = 0;
                _capturing = false;
            }
        }
    }

    private nint OnMouseEvent(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && IsButtonDown(wParam))
        {
            try
            {
                var data = Marshal.PtrToStructure<MouseHookStruct>(lParam);
                var ticks = (long)(Stopwatch.GetTimestamp() * (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency);
                _logger.LogDebug("Clique capturado em {X},{Y}", data.Point.X, data.Point.Y);
                ClickCaptured?.Invoke(this, new ClickEvent(data.Point.X, data.Point.Y, TimeSpan.FromTicks(ticks)));
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Falha ao processar clique");
            }
        }

        // Nunca engole o clique: sempre delega ao próximo hook.
        return CallNextHookEx(nint.Zero, code, wParam, lParam);
    }

    private static bool IsButtonDown(nint wParam) =>
        wParam == WmLeftButtonDown || wParam == WmRightButtonDown || wParam == WmMiddleButtonDown;

    public void Stop()
    {
        Thread? thread;
        lock (_sync)
        {
            if (!_capturing && _thread is null) return;
            _capturing = false;
            if (_threadId != 0) PostThreadMessage(_threadId, WmQuit, nint.Zero, nint.Zero);
            thread = _thread;
        }

        if (thread is not null && thread != Thread.CurrentThread)
            thread.Join(TimeSpan.FromSeconds(2));

        lock (_sync) _thread = null;
    }

    public void Dispose() => Stop();

    private delegate nint LowLevelMouseProc(int code, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct MousePoint { public readonly int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct MouseHookStruct
    {
        public readonly MousePoint Point;
        public readonly uint MouseData;
        public readonly uint Flags;
        public readonly uint Time;
        public readonly nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint HWnd;
        public uint Value;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public MousePoint Point;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, LowLevelMouseProc hookProc, nint module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Message message, nint hWnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(ref Message message);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint threadId, uint message, nint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);
}
