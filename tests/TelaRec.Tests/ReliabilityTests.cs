using Microsoft.Extensions.Logging.Abstractions;
using TelaRec.Core.Reliability;
using TelaRec.Core.Recording;
using TelaRec.Core.Contracts;
using TelaRec.Core.Scheduling;
using TelaRec.Infrastructure.Reliability;

namespace TelaRec.Tests;

public sealed class ReliabilityTests
{
    [Fact]
    public async Task Journal_recovers_only_registered_temporary_file()
    {
        var root = Path.Combine(Path.GetTempPath(), "TelaRecJournalTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var temporary = Path.Combine(root, ".recording.partial.mp4");
        var output = Path.Combine(root, "recording.mp4");
        await File.WriteAllBytesAsync(temporary, [1, 2, 3]);
        var journal = new JsonRecordingSessionJournal(NullLogger<JsonRecordingSessionJournal>.Instance, Path.Combine(root, "sessions"));
        try
        {
            await journal.CreateAsync(new RecordingJournalEntry(Guid.NewGuid(), temporary, output, DateTimeOffset.UtcNow, RecordingState.Recording, null));
            var result = await journal.RecoverAsync();
            Assert.Equal(1, result.TemporaryFilesRemoved);
            Assert.False(File.Exists(temporary));
            Assert.Contains(result.Diagnostics, text => text.Contains("recuperada"));
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task Journal_preserves_unowned_path()
    {
        var root = Path.Combine(Path.GetTempPath(), "TelaRecJournalTests", Guid.NewGuid().ToString("N"));
        var outside = Path.Combine(Path.GetTempPath(), $"unowned-{Guid.NewGuid():N}.mp4");
        Directory.CreateDirectory(root);
        await File.WriteAllBytesAsync(outside, [1]);
        var journal = new JsonRecordingSessionJournal(NullLogger<JsonRecordingSessionJournal>.Instance, Path.Combine(root, "sessions"));
        try
        {
            await journal.CreateAsync(new RecordingJournalEntry(Guid.NewGuid(), outside, Path.Combine(root, "output.mp4"), DateTimeOffset.UtcNow, RecordingState.Failed, null));
            await journal.RecoverAsync();
            Assert.True(File.Exists(outside));
        }
        finally { File.Delete(outside); Directory.Delete(root, true); }
    }
    [Fact]
    public async Task Scheduler_rejects_overlapping_rules()
    {
        await using var scheduler = new RecordingScheduler(new FakeRecordingService(), new AvailableProbe(), new RecordingDiagnosticsService(), NullLogger<RecordingScheduler>.Instance);
        var now = DateTimeOffset.UtcNow.AddHours(1);
        var request = new RecordingRequest(Path.Combine(Path.GetTempPath(), "scheduled.mp4"));
        await scheduler.ScheduleAsync(new ScheduledRecording(Guid.NewGuid(), now, now.AddMinutes(10), request));
        await Assert.ThrowsAsync<InvalidOperationException>(() => scheduler.ScheduleAsync(new ScheduledRecording(Guid.NewGuid(), now.AddMinutes(5), now.AddMinutes(15), request)));
    }
    private sealed class AvailableProbe : IRecordingAvailabilityProbe { public RecordingAvailability Check() => new(true); }
    private sealed class FakeRecordingService : IRecordingService
    {
        public RecordingStatus Status { get; } = RecordingStatus.Initial;
        public event EventHandler<RecordingStatus>? StatusChanged { add { } remove { } }
        public Task StartAsync(RecordingRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ResumeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
