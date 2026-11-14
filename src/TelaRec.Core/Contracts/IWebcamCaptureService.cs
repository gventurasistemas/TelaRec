using TelaRec.Core.Recording;

namespace TelaRec.Core.Contracts;

public interface IWebcamCaptureService : IAsyncDisposable
{
    event EventHandler<VideoFrame>? FrameCaptured;
    event EventHandler<Exception>? CaptureFailed;
    Task StartAsync(WebcamOverlaySettings settings, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
