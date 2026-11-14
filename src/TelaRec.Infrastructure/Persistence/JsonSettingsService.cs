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

        var settings = await TryLoadAsync(_settingsPath, cancellationToken).ConfigureAwait(false);
        if (settings is not null) return settings;

        _logger.LogWarning("Configurações inválidas em {SettingsPath}; tentando o backup", _settingsPath);
        _diagnostics?.Report(DiagnosticSeverity.Warning, "settings.load", "O arquivo de configurações estava inválido. Tentando restaurar o backup.");

        if (File.Exists(_backupPath))
        {
            var restored = await TryLoadAsync(_backupPath, cancellationToken).ConfigureAwait(false);
            if (restored is not null)
            {
                _logger.LogInformation("Configurações restauradas do backup {BackupPath}", _backupPath);
                _diagnostics?.Report(DiagnosticSeverity.Information, "settings.recovery", "Configurações restauradas a partir do backup.");
                return restored;
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

    private async Task<TelaRecSettings?> TryLoadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<TelaRecSettings>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Falha ao ler configurações de {SettingsPath}", path);
            return null;
        }
    }
}
