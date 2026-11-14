using System.Text.Json;
using Microsoft.Extensions.Logging;
using TelaRec.Core.Scheduling;

namespace TelaRec.Infrastructure.Reliability;

public sealed class JsonScheduledRecordingStore : IScheduledRecordingStore
{
    private readonly ILogger<JsonScheduledRecordingStore> _logger;
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

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
            await using var stream = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<List<PersistedScheduledRecording>>(stream, Options, cancellationToken).ConfigureAwait(false) ?? [];
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
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            var temporary = _path + ".tmp";
            await using (var stream = File.Create(temporary))
            {
                await JsonSerializer.SerializeAsync(stream, schedules, Options, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporary, _path, true);
        }
        finally
        {
            _gate.Release();
            try { if (File.Exists(_path + ".tmp")) File.Delete(_path + ".tmp"); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
