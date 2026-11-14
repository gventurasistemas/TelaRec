namespace TelaRec.Core.Scheduling;

public sealed record PersistedScheduledRecording(
    ScheduledRecording Rule,
    ScheduledRecordingStatus Status);

public interface IScheduledRecordingStore
{
    Task<IReadOnlyList<PersistedScheduledRecording>> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(IReadOnlyCollection<PersistedScheduledRecording> schedules, CancellationToken cancellationToken = default);
}
