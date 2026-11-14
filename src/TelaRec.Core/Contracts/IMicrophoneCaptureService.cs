using TelaRec.Core.Recording;
namespace TelaRec.Core.Contracts;
public interface IMicrophoneCaptureService : IAsyncDisposable
{
    event EventHandler<AudioFrame>? AudioCaptured;
    event EventHandler<Exception>? CaptureFailed;
    IReadOnlyList<AudioInputDevice> GetDevices();
    Task StartAsync(string? deviceId = null, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
