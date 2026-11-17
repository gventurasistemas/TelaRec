using TelaRec.Core.Recording;

namespace TelaRec.Core.Contracts;

/// <summary>Verifica se um exportador opcional pode atender um perfil antes da captura.</summary>
public interface IRecordingExportPreflight
{
    Task<RecordingExportPreflightResult> CheckAvailabilityAsync(RecordingProfile profile, CancellationToken cancellationToken = default);
}

public sealed record RecordingExportPreflightResult(bool IsAvailable, string Message)
{
    public static RecordingExportPreflightResult Available(string message) => new(true, message);
    public static RecordingExportPreflightResult Unavailable(string message) => new(false, message);
}
