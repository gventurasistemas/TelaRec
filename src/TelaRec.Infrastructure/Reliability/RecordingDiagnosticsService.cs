using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TelaRec.Core.Reliability;

namespace TelaRec.Infrastructure.Reliability;

/// <summary>
/// Mantém os últimos diagnósticos em memória e replica cada um no log de arquivo
/// (provider de P01), para que a lista da UI e o arquivo tenham o mesmo conteúdo.
/// </summary>
public sealed class RecordingDiagnosticsService : IRecordingDiagnostics
{
    private const int MaximumItems = 50;
    private readonly List<RecordingDiagnostic> _items = [];
    private readonly ILogger _logger;

    public RecordingDiagnosticsService(ILogger<RecordingDiagnosticsService>? logger = null)
        => _logger = logger ?? NullLogger<RecordingDiagnosticsService>.Instance;

    public IReadOnlyList<RecordingDiagnostic> Current { get { lock (_items) return _items.ToArray(); } }
    public event EventHandler<RecordingDiagnostic>? Reported;

    public void Report(DiagnosticSeverity severity, string code, string message)
    {
        var item = new RecordingDiagnostic(DateTimeOffset.Now, severity, code, message);
        lock (_items) { _items.Add(item); if (_items.Count > MaximumItems) _items.RemoveAt(0); }
        Log(severity, code, message);
        Reported?.Invoke(this, item);
    }

    private void Log(DiagnosticSeverity severity, string code, string message)
    {
        var level = severity switch
        {
            DiagnosticSeverity.Error => LogLevel.Error,
            DiagnosticSeverity.Warning => LogLevel.Warning,
            _ => LogLevel.Information
        };
        _logger.Log(level, "[{Code}] {Message}", code, message);
    }
}
