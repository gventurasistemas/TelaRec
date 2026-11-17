using TelaRec.Core.Recording;

namespace TelaRec.Core.Reliability;

public sealed record RecordingJournalEntry(
    Guid SessionId,
    string TemporaryPath,
    string OutputPath,
    DateTimeOffset StartedAt,
    RecordingState State,
    string? Error)
{
    public int SchemaVersion { get; init; } = Configuration.PersistenceSchemas.Journal;
}

public sealed record RecoveryResult(int SessionsFound, int TemporaryFilesRemoved, IReadOnlyList<string> Diagnostics);

public interface IRecordingSessionJournal
{
    Task CreateAsync(RecordingJournalEntry entry, CancellationToken cancellationToken = default);
    Task UpdateAsync(Guid sessionId, RecordingState state, string? error = null, CancellationToken cancellationToken = default);
    Task CompleteAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<RecoveryResult> RecoverAsync(CancellationToken cancellationToken = default);
}
