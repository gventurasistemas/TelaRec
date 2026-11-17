using System.Runtime.InteropServices;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using TelaRec.App.Wpf.Resources;
using TelaRec.App.Wpf.Infrastructure;
using TelaRec.Core.Capture;
using TelaRec.Core.Recording;
using TelaRec.Infrastructure.Recording;

namespace TelaRec.App.Wpf;

/// <summary>Prévia WGC independente, encerrada antes de uma gravação real começar.</summary>
public partial class CapturePreviewWindow : Window
{
    private readonly WindowsGraphicsCaptureService _capture;
    private readonly ILogger<WindowsGraphicsCaptureService> _logger;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly LatestFrameCoalescer<VideoFrame> _frames = new();
    private bool _closing;
    private WriteableBitmap? _bitmap;
    private Task? _startTask;
    internal long DroppedPreviewFrames => _frames.DroppedItems;

    public CapturePreviewWindow(CaptureTarget target, ILogger<WindowsGraphicsCaptureService> logger)
    {
        InitializeComponent();
        Title = Strings.PreviewTitle;
        StatusText.Text = Strings.PreviewTitle;
        _logger = logger;
        _capture = new WindowsGraphicsCaptureService(logger);
        _capture.FrameCaptured += OnFrameCaptured;
        _capture.CaptureFailed += OnCaptureFailed;
        Loaded += (_, _) => _startTask = StartAsync(target);
        Closed += async (_, _) => await StopAsync();
        SourceInitialized += (_, _) => ExcludeFromCapture();
    }

    private async Task StartAsync(CaptureTarget target)
    {
        try
        {
            await _capture.StartAsync(new RecordingRequest(
                Path.Combine(Path.GetTempPath(), "TelaRec-preview.mp4"),
                FramesPerSecond: 10,
                Target: target,
                ShowCursor: true), _lifetime.Token);
            if (_closing)
            {
                await _capture.StopAsync();
                return;
            }
            StatusText.Text = Strings.PreviewTitle;
        }
        catch (Exception exception)
        {
            StatusText.Text = string.Format(Strings.PreviewUnavailable, exception.Message);
        }
    }

    private void OnFrameCaptured(object? sender, VideoFrame frame)
    {
        if (_closing) return;
        if (_frames.Offer(frame)) ScheduleRender();
    }

    private void ScheduleRender()
    {
        _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(RenderLatestFrame));
    }

    private void RenderLatestFrame()
    {
        if (_closing) return;
        if (!_frames.TryTake(out var frame) || frame is null) return;
        if (_bitmap is null || _bitmap.PixelWidth != frame.Width || _bitmap.PixelHeight != frame.Height)
        {
            _bitmap = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null);
            PreviewImage.Source = _bitmap;
        }
        _bitmap.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height), frame.Pixels, frame.Stride, 0);
        if (!_closing && _frames.CompleteRender()) ScheduleRender();
    }

    private void OnCaptureFailed(object? sender, Exception exception)
    {
        if (_closing) return;
        _ = Dispatcher.BeginInvoke(() => StatusText.Text = string.Format(Strings.PreviewUnavailable, exception.Message));
    }

    private async Task StopAsync()
    {
        _closing = true;
        _frames.Clear();
        _lifetime.Cancel();
        _capture.FrameCaptured -= OnFrameCaptured;
        _capture.CaptureFailed -= OnCaptureFailed;
        if (_startTask is not null)
        {
            try { await _startTask.ConfigureAwait(true); }
            catch { /* O status de erro já foi mostrado pela prévia. */ }
        }
        try { await _capture.StopAsync(); }
        catch { /* A prévia nunca deve impedir o fechamento da janela. */ }
        await _capture.DisposeAsync();
        _logger.LogInformation("Prévia encerrada: frames visuais descartados por coalescimento={DroppedFrames}", DroppedPreviewFrames);
        _lifetime.Dispose();
    }

    private void ExcludeFromCapture()
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        _ = SetWindowDisplayAffinity(handle, 0x00000011); // WDA_EXCLUDEFROMCAPTURE
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(nint hWnd, uint affinity);
}
