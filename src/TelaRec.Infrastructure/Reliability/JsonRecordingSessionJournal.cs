using System.Text.Json;
using Microsoft.Extensions.Logging;
using TelaRec.Core.Reliability;
using TelaRec.Core.Recording;

namespace TelaRec.Infrastructure.Reliability;

public sealed class JsonRecordingSessionJournal : IRecordingSessionJournal
{
    private readonly ILogger<JsonRecordingSessionJournal> _logger;
    private readonly string _directory;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public JsonRecordingSessionJournal(ILogger<JsonRecordingSessionJournal> logger, string? directory = null)
    {
        _logger = logger;
        _directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TelaRec", "sessions");
    }
    public async Task CreateAsync(RecordingJournalEntry entry, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directory);
        await WriteAsync(entry, cancellationToken).ConfigureAwait(false);
    }
    public async Task UpdateAsync(Guid sessionId, RecordingState state, string? error = null, CancellationToken cancellationToken = default)
    {
        var entry = await ReadAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (entry is not null) await WriteAsync(entry with { State = state, Error = error }, cancellationToken).ConfigureAwait(false);
    }
    public Task CompleteAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var path = JournalPath(sessionId);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }
    public async Task<RecoveryResult> RecoverAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_directory)) return new RecoveryResult(0, 0, []);
        var diagnostics = new List<string>(); var removed = 0; var entries = Directory.GetFiles(_directory, "*.json");
        foreach (var path in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                RecordingJournalEntry? entry;
                await using (var stream = File.OpenRead(path))
                    entry = await JsonSerializer.DeserializeAsync<RecordingJournalEntry>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
                if (entry is not null && IsOwnedTemporary(entry))
                {
                    if (File.Exists(entry.TemporaryPath)) { File.Delete(entry.TemporaryPath); removed++; }
                    diagnostics.Add($"Sessão interrompida {entry.SessionId:N} recuperada.");
                }
                File.Delete(path);
            }
            catch (Exception exception) { _logger.LogWarning(exception, "Não foi possível recuperar journal {JournalPath}", path); diagnostics.Add("Um journal inválido foi preservado para análise."); }
        }
        return new RecoveryResult(entries.Length, removed, diagnostics);
    }
    private bool IsOwnedTemporary(RecordingJournalEntry entry) =>
        Path.GetFileName(entry.TemporaryPath).EndsWith(".partial.mp4", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Path.GetDirectoryName(Path.GetFullPath(entry.TemporaryPath)), Path.GetDirectoryName(Path.GetFullPath(entry.OutputPath)), StringComparison.OrdinalIgnoreCase);
    private async Task WriteAsync(RecordingJournalEntry entry, CancellationToken token)
    {
        var temporary = JournalPath(entry.SessionId) + ".tmp";
        await using (var stream = File.Create(temporary)) await JsonSerializer.SerializeAsync(stream, entry, JsonOptions, token).ConfigureAwait(false);
        File.Move(temporary, JournalPath(entry.SessionId), true);
    }
    private async Task<RecordingJournalEntry?> ReadAsync(Guid id, CancellationToken token)
    {
        var path = JournalPath(id);
        if (!File.Exists(path)) return null;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<RecordingJournalEntry>(stream, JsonOptions, token).ConfigureAwait(false);
    }
    private string JournalPath(Guid id) => Path.Combine(_directory, $"{id:N}.json");
}
