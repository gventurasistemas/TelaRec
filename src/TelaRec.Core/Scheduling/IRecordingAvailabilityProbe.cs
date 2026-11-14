namespace TelaRec.Core.Scheduling;

public sealed record RecordingAvailability(bool CanRecord, string? Reason = null);
public interface IRecordingAvailabilityProbe
{
    RecordingAvailability Check();
}
