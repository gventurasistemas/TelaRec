using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using TelaRec.Core.Contracts;
using TelaRec.Core.Recording;

namespace TelaRec.Infrastructure.Recording;

public sealed class OpenCvWebcamCaptureService(ILogger<OpenCvWebcamCaptureService> logger) : IWebcamCaptureService
{
    private VideoCapture? _capture;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    public event EventHandler<VideoFrame>? FrameCaptured;
    public event EventHandler<Exception>? CaptureFailed;

    public Task StartAsync(WebcamOverlaySettings settings, CancellationToken cancellationToken = default)
    {
        if (!settings.Enabled || _loop is not null) return Task.CompletedTask;
        _capture = new VideoCapture(settings.DeviceIndex, VideoCaptureAPIs.DSHOW);
        if (!_capture.IsOpened()) { _capture.Dispose(); _capture = null; throw new InvalidOperationException("Não foi possível abrir a webcam selecionada."); }
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loop = Task.Run(() => LoopAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }
    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var bgr = new Mat();
            using var bgra = new Mat();
            while (!cancellationToken.IsCancellationRequested)
            {
                if (_capture is null || !_capture.Read(bgr) || bgr.Empty()) { await Task.Delay(10, cancellationToken).ConfigureAwait(false); continue; }
                Cv2.CvtColor(bgr, bgra, ColorConversionCodes.BGR2BGRA);
                var bytes = new byte[bgra.Rows * bgra.Cols * 4];
                Marshal.Copy(bgra.Data, bytes, 0, bytes.Length);
                var ticks = (long)(Stopwatch.GetTimestamp() * (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency);
                FrameCaptured?.Invoke(this, new VideoFrame(bytes, bgra.Cols, bgra.Rows, bgra.Cols * 4, TimeSpan.FromTicks(ticks)));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) { logger.LogWarning(exception, "Captura de webcam interrompida"); CaptureFailed?.Invoke(this, exception); }
    }
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _cts?.Cancel();
        if (_loop is not null) try { await _loop.ConfigureAwait(false); } catch (OperationCanceledException) { }
        _loop = null; _cts?.Dispose(); _cts = null;
        _capture?.Release(); _capture?.Dispose(); _capture = null;
    }
    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
