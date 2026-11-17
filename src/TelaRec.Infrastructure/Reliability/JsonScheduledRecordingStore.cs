using System.Text.Json;
using Microsoft.Extensions.Logging;
using TelaRec.Core.Configuration;
using TelaRec.Core.Scheduling;

namespace TelaRec.Infrastructure.Reliability;

public sealed class JsonScheduledRecordingStore : IScheduledRecordingStore
{
    private readonly ILogger<JsonScheduledRecordingStore> _logger;
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private sealed record ScheduledRecordingsDocument(int SchemaVersion, List<PersistedScheduledRecording> Schedules);

    public JsonScheduledRecordingStore(ILogger<JsonScheduledRecordingStore> logger, string? path = null)
    {
        _logger = logger;
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TelaRec", "schedules.json");
    }

    public async Task<IReadOnlyList<PersistedScheduledRecording>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path)) return [];
            List<PersistedScheduledRecording> schedules;
            var migrated = false;
            await using (var stream = File.OpenRead(_path))
            using (var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    schedules = document.RootElement.Deserialize<List<PersistedScheduledRecording>>(Options) ?? [];
                    migrated = true;
                }
                else
                {
                    var persisted = document.RootElement.Deserialize<ScheduledRecordingsDocument>(Options);
                    if (persisted is null) return [];
                    if (persisted.SchemaVersion > PersistenceSchemas.Schedules)
                        throw new JsonException($"Schema de agendamentos {persisted.SchemaVersion} não é suportado. Restaure um backup usando uma versão compatível do TelaRec.");
                    schedules = persisted.Schedules;
                    migrated = persisted.SchemaVersion < PersistenceSchemas.Schedules;
                }
            }
            if (migrated)
            {
                await WriteAsync(schedules, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Agendamentos migrados para schema {SchemaVersion}", PersistenceSchemas.Schedules);
            }
            return schedules;
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Arquivo de agendamentos inválido: {Path}", _path);
            return [];
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(IReadOnlyCollection<PersistedScheduledRecording> schedules, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteAsync(schedules, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
            try { if (File.Exists(_path + ".tmp")) File.Delete(_path + ".tmp"); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private async Task WriteAsync(IEnumerable<PersistedScheduledRecording> schedules, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var temporary = _path + ".tmp";
        await using (var stream = File.Create(temporary))
        {
            await JsonSerializer.SerializeAsync(stream, new ScheduledRecordingsDocument(PersistenceSchemas.Schedules, schedules.ToList()), Options, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        File.Move(temporary, _path, true);
    }
}
