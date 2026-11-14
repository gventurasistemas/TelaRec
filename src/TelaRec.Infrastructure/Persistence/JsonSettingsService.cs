using System.Text.Json;
using Microsoft.Extensions.Logging;
using TelaRec.Core.Configuration;
using TelaRec.Core.Contracts;

namespace TelaRec.Infrastructure.Persistence;

public sealed class JsonSettingsService(ILogger<JsonSettingsService> logger) : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TelaRec",
        "settings.json");

    public async Task<TelaRecSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath)) return new TelaRecSettings();
        await using var stream = File.OpenRead(_settingsPath);
        return await JsonSerializer.DeserializeAsync<TelaRecSettings>(stream, SerializerOptions, cancellationToken)
            ?? new TelaRecSettings();
    }

    public async Task SaveAsync(TelaRecSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        Directory.CreateDirectory(settings.OutputDirectory);
        await using var stream = File.Create(_settingsPath);
        await JsonSerializer.SerializeAsync(stream, settings, SerializerOptions, cancellationToken);
        logger.LogInformation("Configurações do TelaRec salvas em {SettingsPath}", _settingsPath);
    }
}
