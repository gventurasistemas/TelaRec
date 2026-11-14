using TelaRec.Core.Recording;

namespace TelaRec.Core.Contracts;

public interface IVideoCaptureService : IAsyncDisposable
{
    event EventHandler<VideoFrame>? FrameCaptured;
    event EventHandler<Exception>? CaptureFailed;

    /// <summary>Área física capturada, disponível após <see cref="StartAsync"/>.</summary>
    CaptureFrameBounds? CaptureBounds { get; }

    Task StartAsync(RecordingRequest request, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
