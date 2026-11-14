using TelaRec.Core.Recording;

namespace TelaRec.Core.Contracts;

public interface IRecordingService
{
    RecordingStatus Status { get; }

    event EventHandler<RecordingStatus>? StatusChanged;

    Task StartAsync(RecordingRequest request, CancellationToken cancellationToken = default);
    Task PauseAsync(CancellationToken cancellationToken = default);
    Task ResumeAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
