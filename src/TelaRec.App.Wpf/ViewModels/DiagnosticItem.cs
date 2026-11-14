using TelaRec.App.Wpf.Resources;
using TelaRec.Core.Reliability;

namespace TelaRec.App.Wpf.ViewModels;

/// <summary>Item exibido na lista de diagnósticos, com textos já localizados.</summary>
public sealed record DiagnosticItem(
    DateTimeOffset Timestamp,
    DiagnosticSeverity Severity,
    string Code,
    string Message)
{
    public string TimestampText => Timestamp.ToLocalTime().ToString("HH:mm:ss");

    public string SeverityLabel => Severity switch
    {
        DiagnosticSeverity.Error => Strings.SeverityError,
        DiagnosticSeverity.Warning => Strings.SeverityWarning,
        _ => Strings.SeverityInformation
    };
}
