using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using TelaRec.Core.Capture;
using TelaRec.Core.Contracts;
using TelaRec.Core.Recording;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace TelaRec.Infrastructure.Recording;

public sealed class WindowsGraphicsCaptureService(ILogger<WindowsGraphicsCaptureService> logger) : IVideoCaptureService
{
    private readonly object _sync = new();
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDirect3DDevice? _winRtDevice;
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;
    private GraphicsCaptureItem? _item;
    private ID3D11Texture2D? _staging;
    private System.Drawing.Rectangle? _crop;
    private long _lastFrameTicks;
    private int _fps;
    private bool _running;
    private int _poolWidth, _poolHeight;
    private int _outputWidth, _outputHeight;
    private bool _resizeQueued;
    private System.Threading.Timer? _repeatTimer;
    private byte[]? _lastPixels;
    private int _lastWidth, _lastHeight, _lastStride;
    public event EventHandler<VideoFrame>? FrameCaptured;
    public event EventHandler<Exception>? CaptureFailed;

    public Task StartAsync(RecordingRequest request, CancellationToken cancellationToken = default)
    {
        if (!GraphicsCaptureSession.IsSupported()) throw new PlatformNotSupportedException("Windows.Graphics.Capture indisponível.");
        lock (_sync)
        {
            if (_running) throw new InvalidOperationException("Captura já iniciada.");
            try
            {
                _fps = Math.Max(1, request.FramesPerSecond);
                var target = request.Target ?? new CaptureTarget.Display();
                var screens = System.Windows.Forms.Screen.AllScreens.OrderByDescending(screen => screen.Primary).ToArray();
                if (screens.Length == 0) throw new InvalidOperationException("Nenhum monitor disponível.");
                if (target is CaptureTarget.Window window)
                    _item = Direct3D11Interop.CreateItemForWindow(window.Handle);
                else
                {
                    var screen = target switch
                    {
                        CaptureTarget.Display display => screens.ElementAtOrDefault(display.DisplayIndex) ?? throw new ArgumentOutOfRangeException(nameof(request), "Monitor indisponível."),
                        CaptureTarget.Region selectedRegion => screens.OrderByDescending(s => Area(System.Drawing.Rectangle.Intersect(s.Bounds, new System.Drawing.Rectangle(selectedRegion.X, selectedRegion.Y, selectedRegion.Width, selectedRegion.Height)))).First(),
                        _ => screens.FirstOrDefault(s => s.Primary) ?? screens[0]
                    };
                    if (target is CaptureTarget.Region region)
                    {
                        var selected = System.Drawing.Rectangle.Intersect(screen.Bounds, new System.Drawing.Rectangle(region.X, region.Y, region.Width, region.Height));
                        if (selected.IsEmpty) throw new ArgumentException("Região fora do monitor selecionado.");
                        _crop = new System.Drawing.Rectangle(selected.X - screen.Bounds.X, selected.Y - screen.Bounds.Y, selected.Width, selected.Height);
                    }
                    _item = Direct3D11Interop.CreateItemForMonitor(MonitorFromPoint(new Point(screen.Bounds.Left + screen.Bounds.Width / 2, screen.Bounds.Top + screen.Bounds.Height / 2), 2));
                }
                CreateDevice();
                _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_winRtDevice!, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _item!.Size);
                _poolWidth = _item.Size.Width; _poolHeight = _item.Size.Height;
                _outputWidth = (_crop?.Width ?? _poolWidth) & ~1;
                _outputHeight = (_crop?.Height ?? _poolHeight) & ~1;
                if (_outputWidth <= 0 || _outputHeight <= 0) throw new ArgumentException("Área de captura muito pequena.");
                _pool.FrameArrived += OnFrameArrived;
                _session = _pool.CreateCaptureSession(_item);
                _session.IsCursorCaptureEnabled = true;
                _running = true;
                _session.StartCapture();
                _repeatTimer = new System.Threading.Timer(RepeatLastFrame, null, 1000 / _fps, 1000 / _fps);
                logger.LogInformation("WGC iniciado: {Target}, {Width}x{Height}", target, _item.Size.Width, _item.Size.Height);
            }
            catch { Release(); throw; }
        }
        return Task.CompletedTask;
    }
    private static int Area(System.Drawing.Rectangle rectangle) => rectangle.Width * rectangle.Height;
    private void CreateDevice()
    {
        Marshal.ThrowExceptionForHR(D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7, out var device, out _, out var context));
        _device = new ID3D11Device(device);
        _context = new ID3D11DeviceContext(context);
        using var dxgi = _device.QueryInterface<IDXGIDevice>();
        _winRtDevice = Direct3D11Interop.CreateWinRtDevice(dxgi);
    }
    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        lock (_sync)
        {
            if (!_running) return;
            try
            {
                using var frame = sender.TryGetNextFrame();
                if (frame is null) return;
                var ticks = frame.SystemRelativeTime.Ticks;
                if (_lastFrameTicks != 0 && ticks - _lastFrameTicks < TimeSpan.TicksPerSecond / _fps) return;
                _lastFrameTicks = ticks;
                using var texture = Direct3D11Interop.GetTexture(frame.Surface);
                var description = texture.Description;
                if (_staging is null || _staging.Description.Width != description.Width || _staging.Description.Height != description.Height)
                {
                    _staging?.Dispose();
                    description.BindFlags = BindFlags.None;
                    description.Usage = ResourceUsage.Staging;
                    description.CPUAccessFlags = CpuAccessFlags.Read;
                    description.MiscFlags = ResourceOptionFlags.None;
                    _staging = _device!.CreateTexture2D(description);
                }
                _context!.CopyResource(_staging, texture);
                var mapped = _context.Map(_staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                try
                {
                    var wanted = _crop ?? new System.Drawing.Rectangle(0, 0, _outputWidth, _outputHeight);
                    var bounds = wanted;
                    bounds.Intersect(new System.Drawing.Rectangle(0, 0, (int)description.Width, (int)description.Height));
                    bounds.Width &= ~1; bounds.Height &= ~1;
                    if (bounds.IsEmpty) return;
                    var stride = _outputWidth * 4;
                    var pixels = new byte[stride * _outputHeight];
                    for (var y = 0; y < bounds.Height; y++)
                        Marshal.Copy(IntPtr.Add(mapped.DataPointer, (bounds.Y + y) * (int)mapped.RowPitch + bounds.X * 4),
                            pixels, (bounds.Y - wanted.Y + y) * stride + (bounds.X - wanted.X) * 4, bounds.Width * 4);
                    _lastPixels = pixels; _lastWidth = _outputWidth; _lastHeight = _outputHeight; _lastStride = stride;
                    FrameCaptured?.Invoke(this, new VideoFrame(pixels, _outputWidth, _outputHeight, stride, TimeSpan.FromTicks(ticks)));
                    var newSize = frame.ContentSize;
                    if (!_resizeQueued && newSize.Width > 0 && newSize.Height > 0 &&
                        (newSize.Width != _poolWidth || newSize.Height != _poolHeight))
                    {
                        _resizeQueued = true;
                        ThreadPool.QueueUserWorkItem(_ =>
                        {
                            lock (_sync)
                            {
                                try
                                {
                                    if (_running && ReferenceEquals(_pool, sender))
                                    {
                                        sender.Recreate(_winRtDevice!, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, newSize);
                                        _poolWidth = newSize.Width; _poolHeight = newSize.Height;
                                        logger.LogInformation("Frame pool redimensionado para {Width}x{Height}", _poolWidth, _poolHeight);
                                    }
                                }
                                catch (Exception exception) { logger.LogError(exception, "Falha ao redimensionar frame pool"); }
                                finally { _resizeQueued = false; }
                            }
                        });
                    }
                }
                finally { _context.Unmap(_staging, 0); }
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Falha ao processar quadro WGC");
                _running = false;
                CaptureFailed?.Invoke(this, exception);
            }
        }
    }
    private void RepeatLastFrame(object? state)
    {
        lock (_sync)
        {
            if (!_running || _lastPixels is null) return;
            var now = (long)(Stopwatch.GetTimestamp() * (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency);
            if (now - _lastFrameTicks < TimeSpan.TicksPerSecond / _fps) return;
            _lastFrameTicks = now;
            FrameCaptured?.Invoke(this, new VideoFrame(_lastPixels, _lastWidth, _lastHeight, _lastStride, TimeSpan.FromTicks(now)));
        }
    }
    public Task StopAsync(CancellationToken cancellationToken = default) { lock (_sync) Release(); return Task.CompletedTask; }
    private void Release()
    {
        _running = false;
        _repeatTimer?.Dispose(); _repeatTimer = null;
        if (_pool is not null) _pool.FrameArrived -= OnFrameArrived;
        _session?.Dispose(); _session = null;
        _pool?.Dispose(); _pool = null;
        _item = null;
        _staging?.Dispose(); _staging = null;
        _winRtDevice?.Dispose(); _winRtDevice = null;
        _context?.Dispose(); _context = null;
        _device?.Dispose(); _device = null;
        _crop = null; _lastFrameTicks = 0; _lastPixels = null;
        _resizeQueued = false; _poolWidth = _poolHeight = _outputWidth = _outputHeight = 0;
    }
    public ValueTask DisposeAsync() { lock (_sync) Release(); return ValueTask.CompletedTask; }
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Point(int X, int Y);
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Point point, uint flags);
    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags, IntPtr featureLevels, uint featureLevelCount, uint sdkVersion, out IntPtr device, out int featureLevel, out IntPtr context);
}
