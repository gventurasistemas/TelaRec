namespace TelaRec.Core.Recording;

public sealed class RecordingStateMachine
{
    public RecordingState State { get; private set; } = RecordingState.Idle;

    public void MoveTo(RecordingState next)
    {
        if (!IsAllowed(State, next))
            throw new InvalidOperationException($"Transição inválida: {State} -> {next}.");
        State = next;
    }

    private static bool IsAllowed(RecordingState from, RecordingState to) => (from, to) switch
    {
        (RecordingState.Idle, RecordingState.Starting) => true,
        (RecordingState.Completed, RecordingState.Starting) => true,
        (RecordingState.Failed, RecordingState.Starting) => true,
        (RecordingState.Starting, RecordingState.Recording) => true,
        (RecordingState.Starting, RecordingState.Failed) => true,
        (RecordingState.Starting, RecordingState.Stopping) => true,
        (RecordingState.Recording, RecordingState.Paused) => true,
        (RecordingState.Recording, RecordingState.Stopping) => true,
        (RecordingState.Recording, RecordingState.Failed) => true,
        (RecordingState.Paused, RecordingState.Recording) => true,
        (RecordingState.Paused, RecordingState.Stopping) => true,
        (RecordingState.Paused, RecordingState.Failed) => true,
        (RecordingState.Stopping, RecordingState.Completed) => true,
        (RecordingState.Stopping, RecordingState.Failed) => true,
        _ => false
    };
}
