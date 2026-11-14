using Microsoft.Extensions.Logging.Abstractions;
using TelaRec.App.Wpf.ViewModels;
using TelaRec.Core.Configuration;
using TelaRec.Core.Contracts;
using TelaRec.Core.Recording;
using TelaRec.Core.Reliability;
using TelaRec.Core.Scheduling;

namespace TelaRec.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public async Task Blocked_probe_prevents_start_and_shows_reason()
    {
        var directory = NewDirectory();
        try
        {
            var recording = new FakeRecordingService();
            var viewModel = CreateViewModel(
                new FakeSettingsService(directory),
                recording,
                new FakeAvailabilityProbe(new RecordingAvailability(false, "A tela está bloqueada.")));

            await viewModel.LoadAsync();
            await viewModel.StartRecordingAsync();

            Assert.False(recording.StartCalled);
            Assert.Equal("A tela está bloqueada.", viewModel.StatusMessage);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Valid_settings_and_available_capture_start_recording()
    {
        var directory = NewDirectory();
        try
        {
            var recording = new FakeRecordingService();
            var viewModel = CreateViewModel(
                new FakeSettingsService(directory),
                recording,
                new FakeAvailabilityProbe(new RecordingAvailability(true)));

            await viewModel.LoadAsync();
            await viewModel.StartRecordingAsync();

            Assert.True(recording.StartCalled);
            Assert.NotNull(recording.LastRequest);
            Assert.EndsWith(".mp4", recording.LastRequest!.OutputPath);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Invalid_settings_are_not_started()
    {
        var directory = NewDirectory();
        try
        {
            var recording = new FakeRecordingService();
            var settings = new FakeSettingsService(directory)
            {
                Settings = new TelaRecSettings
                {
                    OutputDirectory = directory,
                    FramesPerSecond = 0
                }
            };
            var viewModel = CreateViewModel(
                settings,
                recording,
                new FakeAvailabilityProbe(new RecordingAvailability(true)));

            await viewModel.LoadAsync();
            await viewModel.StartRecordingAsync();

            Assert.False(recording.StartCalled);
            Assert.Contains("FPS", viewModel.StatusMessage);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static MainWindowViewModel CreateViewModel(
        ISettingsService settings,
        IRecordingService recording,
        IRecordingAvailabilityProbe availability) =>
        new(settings, recording, new FakeScheduler(), new SilentDiagnostics(), availability,
            NullLogger<MainWindowViewModel>.Instance);

    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "TelaRecViewModelTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeSettingsService(string outputDirectory) : ISettingsService
    {
        public TelaRecSettings Settings { get; set; } = new() { OutputDirectory = outputDirectory };

        public Task<TelaRecSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Settings);

        public Task SaveAsync(TelaRecSettings settings, CancellationToken cancellationToken = default)
        {
            Settings = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRecordingService : IRecordingService
    {
        public bool StartCalled { get; private set; }
        public RecordingRequest? LastRequest { get; private set; }
        public RecordingStatus Status { get; private set; } = RecordingStatus.Initial;
        public event EventHandler<RecordingStatus>? StatusChanged { add { } remove { } }

        public Task StartAsync(RecordingRequest request, CancellationToken cancellationToken = default)
        {
            StartCalled = true;
            LastRequest = request;
            Status = new RecordingStatus(RecordingState.Recording, TimeSpan.Zero);
            return Task.CompletedTask;
        }

        public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ResumeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeAvailabilityProbe(RecordingAvailability availability) : IRecordingAvailabilityProbe
    {
        public RecordingAvailability Check() => availability;
    }

    private sealed class FakeScheduler : IRecordingScheduler
    {
        public IReadOnlyList<ScheduledRecordingStatus> Scheduled => [];
        public event EventHandler<ScheduledRecordingStatus>? StatusChanged { add { } remove { } }
        public Task ScheduleAsync(ScheduledRecording schedule, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CancelAsync(Guid scheduleId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class SilentDiagnostics : IRecordingDiagnostics
    {
        public IReadOnlyList<RecordingDiagnostic> Current => [];
        public event EventHandler<RecordingDiagnostic>? Reported { add { } remove { } }
        public void Report(DiagnosticSeverity severity, string code, string message) { }
    }
}
