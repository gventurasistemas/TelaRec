using TelaRec.Core.Recording;

namespace TelaRec.Tests;

public sealed class RecordingStateMachineTests
{
    [Fact]
    public void Idle_can_start_recording() { var sut = new RecordingStateMachine(); sut.MoveTo(RecordingState.Starting); sut.MoveTo(RecordingState.Recording); Assert.Equal(RecordingState.Recording, sut.State); }
    [Fact]
    public void Recording_can_pause_and_resume() { var sut = Started(); sut.MoveTo(RecordingState.Paused); sut.MoveTo(RecordingState.Recording); Assert.Equal(RecordingState.Recording, sut.State); }
    [Fact]
    public void Recording_can_stop_and_complete() { var sut = Started(); sut.MoveTo(RecordingState.Stopping); sut.MoveTo(RecordingState.Completed); Assert.Equal(RecordingState.Completed, sut.State); }
    [Fact]
    public void Failed_recording_can_be_started_again() { var sut = new RecordingStateMachine(); sut.MoveTo(RecordingState.Starting); sut.MoveTo(RecordingState.Failed); sut.MoveTo(RecordingState.Starting); Assert.Equal(RecordingState.Starting, sut.State); }
    [Fact]
    public void Idle_cannot_pause() { var sut = new RecordingStateMachine(); Assert.Throws<InvalidOperationException>(() => sut.MoveTo(RecordingState.Paused)); }
    [Fact]
    public void Completed_cannot_resume() { var sut = Started(); sut.MoveTo(RecordingState.Stopping); sut.MoveTo(RecordingState.Completed); Assert.Throws<InvalidOperationException>(() => sut.MoveTo(RecordingState.Recording)); }
    private static RecordingStateMachine Started() { var sut = new RecordingStateMachine(); sut.MoveTo(RecordingState.Starting); sut.MoveTo(RecordingState.Recording); return sut; }
}
