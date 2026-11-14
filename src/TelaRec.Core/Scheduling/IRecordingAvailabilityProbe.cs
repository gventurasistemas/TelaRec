using TelaRec.Core.Recording;

namespace TelaRec.Core.Scheduling;

/// <summary>Motivo padronizado de indisponibilidade, traduzido pela camada de UI.</summary>
public enum RecordingUnavailableReason
{
    None,
    CaptureUnsupported,
    DesktopLocked,
    DeviceUnavailable
}

public sealed record RecordingAvailability(
    bool CanRecord,
    string? Reason = null,
    RecordingUnavailableReason ReasonCode = RecordingUnavailableReason.None);

public interface IRecordingAvailabilityProbe
{
    RecordingAvailability Check();
}
