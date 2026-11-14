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

/// <summary>
/// Código do motivo associado a um estado de agendamento. A mensagem textual
/// continua disponível para diagnóstico, mas a UI usa o código para traduzir.
/// </summary>
public enum ScheduledRecordingMessageCode
{
    None,
    Conflict,
    ActiveRecording,
    Unavailable,
    StartFailed,
    StopFailed
}

public sealed record ScheduledRecordingStatus(
    Guid Id,
    ScheduledRecordingState State,
    string? Message = null,
    ScheduledRecordingMessageCode MessageCode = ScheduledRecordingMessageCode.None);

public interface IRecordingScheduler : IAsyncDisposable
{
    IReadOnlyList<ScheduledRecordingStatus> Scheduled { get; }
    event EventHandler<ScheduledRecordingStatus>? StatusChanged;
    Task ScheduleAsync(ScheduledRecording schedule, CancellationToken cancellationToken = default);
    Task CancelAsync(Guid scheduleId, CancellationToken cancellationToken = default);
}
