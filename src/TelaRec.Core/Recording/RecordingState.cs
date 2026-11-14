namespace TelaRec.Core.Recording;

public enum RecordingState
{
    Idle,
    Starting,
    Recording,
    Paused,
    Stopping,
    Completed,
    Failed
}
