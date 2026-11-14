namespace TelaRec.Core.Reliability;

public enum DiagnosticSeverity { Information, Warning, Error }
public sealed record RecordingDiagnostic(DateTimeOffset Timestamp, DiagnosticSeverity Severity, string Code, string Message);

public interface IRecordingDiagnostics
{
    IReadOnlyList<RecordingDiagnostic> Current { get; }
    event EventHandler<RecordingDiagnostic>? Reported;
    void Report(DiagnosticSeverity severity, string code, string message);
}
