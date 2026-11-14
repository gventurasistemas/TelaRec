namespace TelaRec.Core.Recording;

public sealed record RecordingStatus(
    RecordingState State,
    TimeSpan Elapsed,
    string? ErrorMessage = null)
{
    public static RecordingStatus Initial { get; } = new(RecordingState.Idle, TimeSpan.Zero);
}
