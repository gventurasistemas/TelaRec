using TelaRec.Core.Reliability;

namespace TelaRec.Infrastructure.Reliability;

public sealed class RecordingDiagnosticsService : IRecordingDiagnostics
{
    private readonly List<RecordingDiagnostic> _items = [];
    public IReadOnlyList<RecordingDiagnostic> Current { get { lock (_items) return _items.ToArray(); } }
    public event EventHandler<RecordingDiagnostic>? Reported;
    public void Report(DiagnosticSeverity severity, string code, string message)
    {
        var item = new RecordingDiagnostic(DateTimeOffset.Now, severity, code, message);
        lock (_items) { _items.Add(item); if (_items.Count > 50) _items.RemoveAt(0); }
        Reported?.Invoke(this, item);
    }
}
