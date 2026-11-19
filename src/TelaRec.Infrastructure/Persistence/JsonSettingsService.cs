using System.Text.Json;
using Microsoft.Extensions.Logging;
using TelaRec.Core.Configuration;
using TelaRec.Core.Contracts;
using TelaRec.Core.Reliability;

namespace TelaRec.Infrastructure.Persistence;

public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<JsonSettingsService> _logger;
    private readonly IRecordingDiagnostics? _diagnostics;
    private readonly string _settingsPath;
    private readonly string _backupPath;
    private sealed record LoadedSettings(TelaRecSettings Settings, bool Migrated);
    private sealed class UnsupportedSettingsSchemaException(string message) : Exception(message);

    public JsonSettingsService(ILogger<JsonSettingsService> logger, IRecordingDiagnostics? diagnostics = null, string? settingsPath = null)
    {
        _logger = logger;
        _diagnostics = diagnostics;
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TelaRec",
            "settings.json");
        _backupPath = _settingsPath + ".bak";
    }

    public async Task<TelaRecSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath)) return new TelaRecSettings();

        LoadedSettings? settings;
        try { settings = await TryLoadAsync(_settingsPath, cancellationToken).ConfigureAwait(false); }
        catch (UnsupportedSettingsSchemaException exception)
        {
            _logger.LogWarning(exception, "Schema de configurações não suportado em {SettingsPath}", _settingsPath);
            _diagnostics?.Report(DiagnosticSeverity.Warning, "settings.schema_unsupported", "As configurações foram criadas por uma versão mais nova e foram preservadas. Tentando o backup compatível.");
            settings = null;
        }
        if (settings is not null)
        {
            if (settings.Migrated)
            {
                await SaveAsync(settings.Settings, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Configurações migradas para schema {SchemaVersion}", PersistenceSchemas.Settings);
                _diagnostics?.Report(DiagnosticSeverity.Information, "settings.migrated", $"Configurações migradas para o schema {PersistenceSchemas.Settings}.");
            }
            return settings.Settings;
        }

        _logger.LogWarning("Configurações inválidas em {SettingsPath}; tentando o backup", _settingsPath);
        _diagnostics?.Report(DiagnosticSeverity.Warning, "settings.load", "O arquivo de configurações estava inválido. Tentando restaurar o backup.");

        if (File.Exists(_backupPath))
        {
            LoadedSettings? restored;
            try { restored = await TryLoadAsync(_backupPath, cancellationToken).ConfigureAwait(false); }
            catch (UnsupportedSettingsSchemaException exception)
            {
                _logger.LogWarning(exception, "Schema do backup de configurações não suportado em {BackupPath}", _backupPath);
                restored = null;
            }
            if (restored is not null)
            {
                _logger.LogInformation("Configurações restauradas do backup {BackupPath}", _backupPath);
                _diagnostics?.Report(DiagnosticSeverity.Information, "settings.recovery", "Configurações restauradas a partir do backup.");
                return restored.Settings;
            }
        }

        _logger.LogWarning("Backup ausente ou inválido; usando configurações padrão");
        _diagnostics?.Report(DiagnosticSeverity.Warning, "settings.defaults", "Não foi possível ler as configurações; usando os valores padrão.");
        return new TelaRecSettings();
    }

    public async Task SaveAsync(TelaRecSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var directory = Path.GetDirectoryName(_settingsPath);
        if (string.IsNullOrWhiteSpace(directory)) throw new InvalidOperationException("Caminho de configurações inválido.");
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(settings.OutputDirectory);

        var temporaryPath = _settingsPath + ".tmp";
        try
        {
            await using (var stream = File.Create(temporaryPath))
                await JsonSerializer.SerializeAsync(stream, settings, SerializerOptions, cancellationToken).ConfigureAwait(false);

            // Confirma que o arquivo gravado pode ser lido antes de substituir o atual.
            if (await TryLoadAsync(temporaryPath, cancellationToken).ConfigureAwait(false) is null)
                throw new InvalidOperationException("As configurações gravadas não puderam ser lidas de volta.");

            if (File.Exists(_settingsPath))
                File.Replace(temporaryPath, _settingsPath, _backupPath, ignoreMetadataErrors: true);
            else
                File.Move(temporaryPath, _settingsPath);

            _logger.LogInformation("Configurações do TelaRec salvas em {SettingsPath}", _settingsPath);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private async Task<LoadedSettings?> TryLoadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var settings = document.RootElement.Deserialize<TelaRecSettings>(SerializerOptions);
            if (settings is null) return null;

            var persistedSchema = document.RootElement.TryGetProperty(nameof(TelaRecSettings.SchemaVersion), out var schemaElement) && schemaElement.TryGetInt32(out var schema)
                ? schema
                : 0;
            if (persistedSchema > PersistenceSchemas.Settings)
                throw new UnsupportedSettingsSchemaException($"Schema de configurações {persistedSchema} é mais novo que o suportado ({PersistenceSchemas.Settings}). Restaure o backup com uma versão compatível do TelaRec.");

            var needsPresetMigration = settings.PresetSchemaVersion < PersistenceSchemas.Presets;
            if (needsPresetMigration) UpgradeLegacyDefaultVideoProfile(settings);
            var migrated = persistedSchema < PersistenceSchemas.Settings || needsPresetMigration;
            settings.SchemaVersion = PersistenceSchemas.Settings;
            settings.PresetSchemaVersion = PersistenceSchemas.Presets;
            return new LoadedSettings(settings, migrated);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnsupportedSettingsSchemaException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Falha ao ler configurações de {SettingsPath}", path);
            return null;
        }
    }

    /// <summary>
    /// Atualiza somente o perfil que correspondia exatamente ao padrão antigo. Configurações
    /// personalizadas, inclusive 1080p/30 FPS escolhidas conscientemente, continuam intactas.
    /// </summary>
    private static void UpgradeLegacyDefaultVideoProfile(TelaRecSettings settings)
    {
        if (!string.Equals(settings.SelectedPreset, "tutorial-1080p", StringComparison.Ordinal) ||
            settings.OutputWidth != 1920 || settings.OutputHeight != 1080 ||
            settings.FramesPerSecond != 30 || settings.VideoBitrate != 8_000_000 ||
            !string.Equals(settings.Quality, "Alta", StringComparison.OrdinalIgnoreCase))
            return;

        settings.SelectedPreset = "high-quality-60";
        settings.FramesPerSecond = 60;
        settings.VideoBitrate = 15_000_000;
    }
}
