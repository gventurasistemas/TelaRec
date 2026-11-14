using TelaRec.Core.Recording;

namespace TelaRec.Tests;

public sealed class RecordingStatusTests
{
    [Fact]
    public void Initial_status_is_idle_with_no_elapsed_time()
    {
        var status = RecordingStatus.Initial;
        Assert.Equal(RecordingState.Idle, status.State);
        Assert.Equal(TimeSpan.Zero, status.Elapsed);
        Assert.Null(status.ErrorMessage);
    }
}
