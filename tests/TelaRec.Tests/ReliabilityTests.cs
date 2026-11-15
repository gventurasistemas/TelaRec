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
    public async Task Journal_recovers_the_ffmpeg_temporary_file_owned_by_the_session()
    {
        var root = Path.Combine(Path.GetTempPath(), "TelaRecJournalTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var temporary = Path.Combine(root, ".recording.partial.mp4");
        var output = Path.Combine(root, "recording.mkv");
        var exporterTemporary = Path.Combine(root, ".recording.partial.export.partial.mkv");
        await File.WriteAllBytesAsync(temporary, [1]);
        await File.WriteAllBytesAsync(exporterTemporary, [1]);
        var journal = new JsonRecordingSessionJournal(NullLogger<JsonRecordingSessionJournal>.Instance, Path.Combine(root, "sessions"));
        try
        {
            await journal.CreateAsync(new RecordingJournalEntry(Guid.NewGuid(), temporary, output, DateTimeOffset.UtcNow, RecordingState.Stopping, null));

            var result = await journal.RecoverAsync();

            Assert.Equal(2, result.TemporaryFilesRemoved);
            Assert.False(File.Exists(temporary));
            Assert.False(File.Exists(exporterTemporary));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Journal_removes_only_its_incomplete_write_artifacts()
    {
        var root = Path.Combine(Path.GetTempPath(), "TelaRecJournalTests", Guid.NewGuid().ToString("N"));
        var sessions = Path.Combine(root, "sessions");
        Directory.CreateDirectory(sessions);
        var staleWrite = Path.Combine(sessions, $"{Guid.NewGuid():N}.json.tmp");
        await File.WriteAllTextAsync(staleWrite, "incomplete");
        var journal = new JsonRecordingSessionJournal(NullLogger<JsonRecordingSessionJournal>.Instance, sessions);
        try
        {
            var result = await journal.RecoverAsync();
            Assert.False(File.Exists(staleWrite));
            Assert.Contains(result.Diagnostics, message => message.Contains("escrita incompleta"));
        }
        finally { Directory.Delete(root, true); }
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

    [Fact]
    public async Task Scheduler_restores_pending_rules_after_restart()
    {
        var store = new InMemoryScheduleStore();
        var now = DateTimeOffset.UtcNow.AddMinutes(2);
        var rule = new ScheduledRecording(Guid.NewGuid(), now, now.AddMinutes(5),
            new RecordingRequest(Path.Combine(Path.GetTempPath(), "persisted-schedule.mp4")));
        await using (var first = new RecordingScheduler(new FakeRecordingService(), new AvailableProbe(), new RecordingDiagnosticsService(), NullLogger<RecordingScheduler>.Instance, store))
            await first.ScheduleAsync(rule);

        await using var second = new RecordingScheduler(new FakeRecordingService(), new AvailableProbe(), new RecordingDiagnosticsService(), NullLogger<RecordingScheduler>.Instance, store);
        await second.InitializeAsync();

        Assert.Contains(second.ScheduledRules, item => item.Id == rule.Id);
        Assert.Contains(second.Scheduled, item => item.Id == rule.Id && item.State == ScheduledRecordingState.Pending);
    }

    [Fact]
    public async Task Scheduler_cancellation_is_persisted()
    {
        var store = new InMemoryScheduleStore();
        var rule = new ScheduledRecording(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(2), DateTimeOffset.UtcNow.AddMinutes(5),
            new RecordingRequest(Path.Combine(Path.GetTempPath(), "cancelled-schedule.mp4")));
        await using (var scheduler = new RecordingScheduler(new FakeRecordingService(), new AvailableProbe(), new RecordingDiagnosticsService(), NullLogger<RecordingScheduler>.Instance, store))
        {
            await scheduler.ScheduleAsync(rule);
            await scheduler.CancelAsync(rule.Id);
        }

        await using var restored = new RecordingScheduler(new FakeRecordingService(), new AvailableProbe(), new RecordingDiagnosticsService(), NullLogger<RecordingScheduler>.Instance, store);
        await restored.InitializeAsync();
        Assert.DoesNotContain(restored.ScheduledRules, item => item.Id == rule.Id);
    }

    [Fact]
    public async Task Scheduler_rejects_due_rule_when_desktop_is_locked_and_reports_diagnostic()
    {
        var diagnostics = new RecordingDiagnosticsService();
        await using var scheduler = new RecordingScheduler(
            new FakeRecordingService(),
            new UnavailableProbe("A tela está bloqueada."),
            diagnostics,
            NullLogger<RecordingScheduler>.Instance);
        var now = DateTimeOffset.UtcNow.AddSeconds(1);
        var schedule = new ScheduledRecording(Guid.NewGuid(), now, now.AddMinutes(5), new RecordingRequest(Path.Combine(Path.GetTempPath(), "scheduled.mp4")));
        ScheduledRecordingStatus? reported = null;
        scheduler.StatusChanged += (_, status) => reported = status;
        await scheduler.ScheduleAsync(schedule);
        await Task.Delay(TimeSpan.FromSeconds(1.1));

        await scheduler.ProcessDueAsync();

        Assert.Equal(ScheduledRecordingState.Rejected, reported?.State);
        Assert.Equal(ScheduledRecordingMessageCode.Unavailable, reported?.MessageCode);
        Assert.Contains(diagnostics.Current, item => item.Code == "schedule.rejected" && item.Message.Contains("bloqueada"));
    }

    [Fact]
    public async Task Journal_does_not_reappear_after_completion_even_with_late_updates()
    {
        var root = Path.Combine(Path.GetTempPath(), "TelaRecJournalTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var id = Guid.NewGuid();
        var journal = new JsonRecordingSessionJournal(NullLogger<JsonRecordingSessionJournal>.Instance, Path.Combine(root, "sessions"));
        try
        {
            await journal.CreateAsync(new RecordingJournalEntry(id, Path.Combine(root, ".video.partial.mp4"), Path.Combine(root, "video.mp4"), DateTimeOffset.UtcNow, RecordingState.Recording, null));
            var updates = Enumerable.Range(0, 32)
                .Select(_ => journal.UpdateAsync(id, RecordingState.Stopping));
            await Task.WhenAll(updates.Append(journal.CompleteAsync(id)));
            await journal.UpdateAsync(id, RecordingState.Failed, "late");

            Assert.Empty(Directory.GetFiles(Path.Combine(root, "sessions"), "*.json"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Journal_recovery_reports_preserved_file_path()
    {
        var root = Path.Combine(Path.GetTempPath(), "TelaRecJournalTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var temporary = Path.Combine(root, ".recording.partial.mp4");
        var recovered = Path.Combine(root, "recording-recuperavel-session.mp4");
        var journal = new JsonRecordingSessionJournal(NullLogger<JsonRecordingSessionJournal>.Instance, Path.Combine(root, "sessions"));
        var sessionId = Guid.NewGuid();
        try
        {
            await journal.CreateAsync(new RecordingJournalEntry(sessionId, temporary, Path.Combine(root, "recording.mp4"), DateTimeOffset.UtcNow, RecordingState.Starting, null));
            await journal.UpdateAsync(sessionId, RecordingState.Failed, $"Arquivo recuperável preservado em {recovered}.");

            var result = await journal.RecoverAsync();

            Assert.Contains(result.Diagnostics, message => message.Contains(recovered, StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Cancelling_a_due_schedule_during_start_does_not_reintroduce_it()
    {
        var recording = new BlockingRecordingService();
        await using var scheduler = new RecordingScheduler(recording, new AvailableProbe(), new RecordingDiagnosticsService(), NullLogger<RecordingScheduler>.Instance);
        var start = DateTimeOffset.UtcNow.AddMilliseconds(20);
        var schedule = new ScheduledRecording(Guid.NewGuid(), start, start.AddMinutes(1), new RecordingRequest(Path.Combine(Path.GetTempPath(), "scheduled.mp4")));
        await scheduler.ScheduleAsync(schedule);
        await Task.Delay(40);

        var tick = scheduler.ProcessDueAsync();
        await recording.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var cancellation = scheduler.CancelAsync(schedule.Id);
        recording.Release.TrySetResult();
        await Task.WhenAll(tick, cancellation);

        Assert.DoesNotContain(scheduler.Scheduled, status => status.Id == schedule.Id);
    }

    [Fact]
    public async Task Cancelling_while_starting_waits_for_start_then_stops_before_removing_rule()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var recording = new BlockingRecordingService();
        await using var scheduler = new RecordingScheduler(recording, new AvailableProbe(), new RecordingDiagnosticsService(), NullLogger<RecordingScheduler>.Instance, timeProvider: clock);
        var schedule = new ScheduledRecording(Guid.NewGuid(), clock.GetUtcNow().AddMinutes(1), clock.GetUtcNow().AddMinutes(2), new RecordingRequest(Path.Combine(Path.GetTempPath(), "scheduled.mp4")));
        await scheduler.ScheduleAsync(schedule);
        clock.Advance(TimeSpan.FromMinutes(1));

        var tick = scheduler.ProcessDueAsync();
        await recording.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var cancellation = scheduler.CancelAsync(schedule.Id);
        Assert.False(cancellation.IsCompleted);

        recording.Release.TrySetResult();
        await Task.WhenAll(tick, cancellation);

        Assert.Equal(1, recording.StopCalls);
        Assert.DoesNotContain(scheduler.Scheduled, status => status.Id == schedule.Id);
    }

    [Fact]
    public async Task Concurrent_cancellation_of_active_schedule_stops_once_and_is_idempotent()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var recording = new BlockingStopRecordingService();
        await using var scheduler = new RecordingScheduler(recording, new AvailableProbe(), new RecordingDiagnosticsService(), NullLogger<RecordingScheduler>.Instance, timeProvider: clock);
        var schedule = new ScheduledRecording(Guid.NewGuid(), clock.GetUtcNow().AddMinutes(1), clock.GetUtcNow().AddMinutes(2), new RecordingRequest(Path.Combine(Path.GetTempPath(), "scheduled.mp4")));
        await scheduler.ScheduleAsync(schedule);
        clock.Advance(TimeSpan.FromMinutes(1));
        await scheduler.ProcessDueAsync();

        var first = scheduler.CancelAsync(schedule.Id);
        await recording.StopStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = scheduler.CancelAsync(schedule.Id);
        recording.ReleaseStop.TrySetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, recording.StopCalls);
        Assert.DoesNotContain(scheduler.Scheduled, status => status.Id == schedule.Id);
    }

    [Fact]
    public async Task Failed_active_cancellation_keeps_rule_recording_and_cancelable()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var recording = new ThrowingStopRecordingService();
        await using var scheduler = new RecordingScheduler(recording, new AvailableProbe(), new RecordingDiagnosticsService(), NullLogger<RecordingScheduler>.Instance, timeProvider: clock);
        var schedule = new ScheduledRecording(Guid.NewGuid(), clock.GetUtcNow().AddMinutes(1), clock.GetUtcNow().AddMinutes(2), new RecordingRequest(Path.Combine(Path.GetTempPath(), "scheduled.mp4")));
        await scheduler.ScheduleAsync(schedule);
        clock.Advance(TimeSpan.FromMinutes(1));
        await scheduler.ProcessDueAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => scheduler.CancelAsync(schedule.Id));

        Assert.Contains("parada", error.Message, StringComparison.OrdinalIgnoreCase);
        var status = Assert.Single(scheduler.Scheduled);
        Assert.Equal(ScheduledRecordingState.Recording, status.State);
        Assert.Equal(ScheduledRecordingMessageCode.StopFailed, status.MessageCode);
    }
    private sealed class AvailableProbe : IRecordingAvailabilityProbe { public RecordingAvailability Check() => new(true); }
    private sealed class InMemoryScheduleStore : IScheduledRecordingStore
    {
        private IReadOnlyList<PersistedScheduledRecording> _items = [];
        public Task<IReadOnlyList<PersistedScheduledRecording>> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(_items);
        public Task SaveAsync(IReadOnlyCollection<PersistedScheduledRecording> schedules, CancellationToken cancellationToken = default)
        {
            _items = schedules.ToArray();
            return Task.CompletedTask;
        }
    }
    private sealed class UnavailableProbe(string reason) : IRecordingAvailabilityProbe
    {
        public RecordingAvailability Check() => new(false, reason, RecordingUnavailableReason.DesktopLocked);
    }
    private sealed class FakeRecordingService : IRecordingService
    {
        public RecordingStatus Status { get; } = RecordingStatus.Initial;
        public event EventHandler<RecordingStatus>? StatusChanged { add { } remove { } }
        public Task StartAsync(RecordingRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ResumeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class BlockingRecordingService : IRecordingService
    {
        private RecordingStatus _status = RecordingStatus.Initial;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int StopCalls { get; private set; }
        public RecordingStatus Status => _status;
        public event EventHandler<RecordingStatus>? StatusChanged;
        public async Task StartAsync(RecordingRequest request, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            _status = new RecordingStatus(RecordingState.Recording, TimeSpan.Zero);
            StatusChanged?.Invoke(this, _status);
        }
        public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ResumeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCalls++;
            _status = new RecordingStatus(RecordingState.Completed, TimeSpan.Zero);
            StatusChanged?.Invoke(this, _status);
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingStopRecordingService : IRecordingService
    {
        private RecordingStatus _status = RecordingStatus.Initial;
        public TaskCompletionSource StopStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseStop { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int StopCalls { get; private set; }
        public RecordingStatus Status => _status;
        public event EventHandler<RecordingStatus>? StatusChanged;
        public Task StartAsync(RecordingRequest request, CancellationToken cancellationToken = default)
        {
            _status = new RecordingStatus(RecordingState.Recording, TimeSpan.Zero);
            StatusChanged?.Invoke(this, _status);
            return Task.CompletedTask;
        }
        public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ResumeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCalls++;
            StopStarted.TrySetResult();
            await ReleaseStop.Task.WaitAsync(cancellationToken);
            _status = new RecordingStatus(RecordingState.Completed, TimeSpan.Zero);
            StatusChanged?.Invoke(this, _status);
        }
    }

    private sealed class ThrowingStopRecordingService : IRecordingService
    {
        private RecordingStatus _status = RecordingStatus.Initial;
        public RecordingStatus Status => _status;
        public event EventHandler<RecordingStatus>? StatusChanged;
        public Task StartAsync(RecordingRequest request, CancellationToken cancellationToken = default)
        {
            _status = new RecordingStatus(RecordingState.Recording, TimeSpan.Zero);
            StatusChanged?.Invoke(this, _status);
            return Task.CompletedTask;
        }
        public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ResumeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) =>
            Task.FromException(new InvalidOperationException("Falha na parada simulada"));
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _utcNow = now;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan elapsed) => _utcNow += elapsed;
    }
}
