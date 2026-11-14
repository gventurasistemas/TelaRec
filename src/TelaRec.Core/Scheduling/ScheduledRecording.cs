using TelaRec.Core.Recording;

namespace TelaRec.Core.Scheduling;

public sealed record ScheduledRecording(
    Guid Id,
    DateTimeOffset StartsAt,
    DateTimeOffset StopsAt,
    RecordingRequest Request)
{
    public bool IsValid => StartsAt > DateTimeOffset.UtcNow && StopsAt > StartsAt;
}

public enum ScheduledRecordingState { Pending, Recording, Completed, Rejected, Failed }
public sealed record ScheduledRecordingStatus(Guid Id, ScheduledRecordingState State, string? Message = null);

public interface IRecordingScheduler : IAsyncDisposable
{
    IReadOnlyList<ScheduledRecordingStatus> Scheduled { get; }
    event EventHandler<ScheduledRecordingStatus>? StatusChanged;
    Task ScheduleAsync(ScheduledRecording schedule, CancellationToken cancellationToken = default);
    Task CancelAsync(Guid scheduleId, CancellationToken cancellationToken = default);
}
