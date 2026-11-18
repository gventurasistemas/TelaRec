using System.Text.Json;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using TelaRec.Core.Configuration;
using TelaRec.Core.Reliability;
using TelaRec.Core.Recording;

namespace TelaRec.Infrastructure.Reliability;

public sealed class JsonRecordingSessionJournal : IRecordingSessionJournal
{
    private readonly ILogger<JsonRecordingSessionJournal> _logger;
    private readonly string _directory;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _sessionGates = new();
    private readonly ConcurrentDictionary<Guid, byte> _completedSessions = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public JsonRecordingSessionJournal(ILogger<JsonRecordingSessionJournal> logger, string? directory = null)
    {
        _logger = logger;
        _directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TelaRec", "sessions");
    }
    public async Task CreateAsync(RecordingJournalEntry entry, CancellationToken cancellationToken = default)
    {
        var gate = GateFor(entry.SessionId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_directory);
            _completedSessions.TryRemove(entry.SessionId, out _);
            await WriteAsync(entry, cancellationToken).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
    public async Task UpdateAsync(Guid sessionId, RecordingState state, string? error = null, CancellationToken cancellationToken = default)
    {
        var gate = GateFor(sessionId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_completedSessions.ContainsKey(sessionId)) return;
            var entry = await ReadAsync(sessionId, cancellationToken).ConfigureAwait(false);
            if (entry is not null && !_completedSessions.ContainsKey(sessionId))
                await WriteAsync(entry with { State = state, Error = error }, cancellationToken).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
    public async Task CompleteAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var gate = GateFor(sessionId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _completedSessions[sessionId] = 0;
            var path = JournalPath(sessionId);
            if (File.Exists(path)) File.Delete(path);
        }
        finally { gate.Release(); }
    }
    public async Task<RecoveryResult> RecoverAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_directory)) return new RecoveryResult(0, 0, []);
        var diagnostics = new List<string>(); var removed = 0;

        // Escritas de journal são atômicas (.json.tmp -> .json). Um encerramento
        // entre as duas operações só pode deixar um arquivo nesta pasta privada;
        // ele nunca contém mídia nem dados do usuário.
        foreach (var abandonedWrite in Directory.GetFiles(_directory, "*.json.tmp"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                File.Delete(abandonedWrite);
                removed++;
                diagnostics.Add("Uma escrita incompleta do journal foi removida.");
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Não foi possível remover escrita temporária do journal {JournalPath}", abandonedWrite);
            }
        }

        var entries = Directory.GetFiles(_directory, "*.json");
        foreach (var path in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var entry = await ReadEntryAsync(path, cancellationToken).ConfigureAwait(false);
                var preservationFailed = false;
                if (entry is not null && IsOwnedTemporary(entry))
                {
                    foreach (var temporaryPath in OwnedTemporaryPaths(entry))
                    {
                        if (!File.Exists(temporaryPath)) continue;
                        preservationFailed |= !TryPreserveTemporary(entry, temporaryPath, diagnostics);
                    }
                    if (preservationFailed)
                    {
                        diagnostics.Add($"A mídia da sessão interrompida {entry.SessionId:N} permaneceu no caminho original; o journal foi mantido para nova tentativa.");
                        continue;
                    }

                    diagnostics.Add($"Sessão interrompida {entry.SessionId:N} recuperada sem apagar mídia.");
                    if (!string.IsNullOrWhiteSpace(entry.Error)) diagnostics.Add(entry.Error);
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

    private static IEnumerable<string> OwnedTemporaryPaths(RecordingJournalEntry entry)
    {
        yield return entry.TemporaryPath;

        // FfmpegRecordingExporter cria este arquivo a partir do nome do MP4
        // temporário. A extensão vem exclusivamente do destino registrado.
        var directory = Path.GetDirectoryName(Path.GetFullPath(entry.TemporaryPath))!;
        var sourceStem = Path.GetFileNameWithoutExtension(entry.TemporaryPath);
        var extension = Path.GetExtension(entry.OutputPath);
        if (!string.IsNullOrEmpty(extension))
            yield return Path.Combine(directory, $"{sourceStem}.export.partial{extension}");
    }

    private bool TryPreserveTemporary(RecordingJournalEntry entry, string temporaryPath, List<string> diagnostics)
    {
        var recoveryPath = GetRecoveryPath(entry, temporaryPath);
        try
        {
            File.Move(temporaryPath, recoveryPath);
            diagnostics.Add($"Mídia recuperável da sessão {entry.SessionId:N} preservada em {recoveryPath}.");
            _logger.LogWarning("Sessão interrompida {SessionId} preservou mídia recuperável em {Path}", entry.SessionId, recoveryPath);
            return true;
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "Não foi possível mover mídia recuperável da sessão {SessionId}; preservando em {Path}", entry.SessionId, temporaryPath);
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogWarning(exception, "Sem permissão para mover mídia recuperável da sessão {SessionId}; preservando em {Path}", entry.SessionId, temporaryPath);
            return false;
        }
    }

    private static string GetRecoveryPath(RecordingJournalEntry entry, string temporaryPath)
    {
        var directory = Path.GetDirectoryName(temporaryPath)!;
        var name = Path.GetFileNameWithoutExtension(entry.OutputPath);
        var extension = Path.GetExtension(temporaryPath);
        var kind = string.Equals(temporaryPath, entry.TemporaryPath, StringComparison.OrdinalIgnoreCase)
            ? "recuperavel"
            : "export-recuperavel";
        var basePath = Path.Combine(directory, $"{name}-{kind}-{entry.SessionId:N}");
        var candidate = basePath + extension;
        var suffix = 1;
        while (File.Exists(candidate)) candidate = $"{basePath}-{suffix++}{extension}";
        return candidate;
    }
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
        return await ReadEntryAsync(path, token).ConfigureAwait(false);
    }

    private static async Task<RecordingJournalEntry?> ReadEntryAsync(string path, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token).ConfigureAwait(false);
        var entry = document.RootElement.Deserialize<RecordingJournalEntry>(JsonOptions);
        if (entry is null) return null;
        var schema = document.RootElement.TryGetProperty(nameof(RecordingJournalEntry.SchemaVersion), out var element) && element.TryGetInt32(out var persisted)
            ? persisted
            : 0;
        if (schema > PersistenceSchemas.Journal)
            throw new InvalidDataException($"Schema de journal {schema} não é suportado.");
        return entry with { SchemaVersion = PersistenceSchemas.Journal };
    }
    private string JournalPath(Guid id) => Path.Combine(_directory, $"{id:N}.json");
    private SemaphoreSlim GateFor(Guid sessionId) => _sessionGates.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));
}
