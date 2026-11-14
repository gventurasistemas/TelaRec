using TelaRec.Core.Recording;

namespace TelaRec.Core.Contracts;

public interface IAudioCaptureService : IAsyncDisposable
{
    event EventHandler<AudioFrame>? AudioCaptured;
    event EventHandler<Exception>? CaptureFailed;
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
