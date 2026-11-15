using Microsoft.Extensions.Logging.Abstractions;
using TelaRec.App.Wpf.Resources;
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
                new FakeAvailabilityProbe(new RecordingAvailability(false, null, RecordingUnavailableReason.DesktopLocked)));

            await viewModel.LoadAsync();
            await viewModel.StartRecordingAsync();

            Assert.False(recording.StartCalled);
            Assert.Equal(Strings.DesktopLocked, viewModel.StatusMessage);
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
            Assert.Equal(Strings.ValidationSummary, viewModel.StatusMessage);
            Assert.NotEmpty(viewModel.GetErrors(nameof(viewModel.FramesPerSecond)).Cast<string>());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Preset_selection_updates_profile_and_persists()
    {
        var directory = NewDirectory();
        try
        {
            var settings = new FakeSettingsService(directory);
            var viewModel = CreateViewModel(settings, new FakeRecordingService(), new FakeAvailabilityProbe(new RecordingAvailability(true)));

            await viewModel.LoadAsync();
            viewModel.SelectedPreset = "light-720p";

            Assert.Equal("1280×720", viewModel.OutputResolution);
            Assert.Equal(30, viewModel.FramesPerSecond);
            Assert.Equal(QualityBitrateCalculator.Suggest("Média", 1280, 720, 30), viewModel.VideoBitrate);
            Assert.True(await viewModel.SaveSettingsAsync());
            Assert.Equal("light-720p", settings.Settings.SelectedPreset);
            Assert.Equal(1280, settings.Settings.OutputWidth);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Manual_change_switches_preset_to_custom()
    {
        var directory = NewDirectory();
        try
        {
            var viewModel = CreateViewModel(new FakeSettingsService(directory), new FakeRecordingService(), new FakeAvailabilityProbe(new RecordingAvailability(true)));
            await viewModel.LoadAsync();

            viewModel.SelectedPreset = "tutorial-1080p";
            viewModel.VideoBitrate = 3_000_000;

            Assert.Equal(MainWindowViewModel.CustomPresetId, viewModel.SelectedPreset);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Changing_quality_recalculates_the_suggested_bitrate()
    {
        var directory = NewDirectory();
        try
        {
            var viewModel = CreateViewModel(new FakeSettingsService(directory), new FakeRecordingService(), new FakeAvailabilityProbe(new RecordingAvailability(true)));
            await viewModel.LoadAsync();

            viewModel.Quality = "Baixa";

            Assert.Equal(QualityBitrateCalculator.Suggest("Baixa", 1920, 1080, 30), viewModel.VideoBitrate);
            Assert.Equal(MainWindowViewModel.CustomPresetId, viewModel.SelectedPreset);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Toggle_recording_starts_then_stops()
    {
        var directory = NewDirectory();
        try
        {
            var recording = new FakeRecordingService();
            var viewModel = CreateViewModel(new FakeSettingsService(directory), recording, new FakeAvailabilityProbe(new RecordingAvailability(true)));
            await viewModel.LoadAsync();

            await viewModel.ToggleRecordingAsync();
            Assert.True(recording.StartCalled);
            Assert.Equal(RecordingState.Recording, recording.Status.State);

            await viewModel.ToggleRecordingAsync();
            Assert.True(recording.StopCalled);
            Assert.Equal(RecordingState.Completed, recording.Status.State);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Toggle_pause_alternates_between_pause_and_resume()
    {
        var directory = NewDirectory();
        try
        {
            var recording = new FakeRecordingService();
            var viewModel = CreateViewModel(new FakeSettingsService(directory), recording, new FakeAvailabilityProbe(new RecordingAvailability(true)));
            await viewModel.LoadAsync();
            await viewModel.ToggleRecordingAsync();

            await viewModel.TogglePauseAsync();
            Assert.Equal(1, recording.PauseCalls);
            Assert.Equal(RecordingState.Paused, recording.Status.State);

            await viewModel.TogglePauseAsync();
            Assert.Equal(1, recording.ResumeCalls);
            Assert.Equal(RecordingState.Recording, recording.Status.State);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Screenshot_command_saves_and_reports_the_file()
    {
        var directory = NewDirectory();
        try
        {
            var screenshot = new FakeScreenshotService();
            var viewModel = CreateViewModel(new FakeSettingsService(directory), new FakeRecordingService(), new FakeAvailabilityProbe(new RecordingAvailability(true)), screenshot);
            await viewModel.LoadAsync();

            await viewModel.CaptureScreenshotAsync();

            Assert.Equal(1, screenshot.Calls);
            Assert.Equal(directory, screenshot.LastDirectory);
            Assert.Equal(ScreenshotScope.MonitorUnderCursor, screenshot.LastScope);
            Assert.Contains("shot.png", viewModel.StatusMessage);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Header_tracks_container_codec_resolution_and_fps()
    {
        var directory = NewDirectory();
        try
        {
            var viewModel = CreateViewModel(new FakeSettingsService(directory), new FakeRecordingService(), new FakeAvailabilityProbe(new RecordingAvailability(true)));
            await viewModel.LoadAsync();

            Assert.Contains("MP4 • H.264 • 1920×1080 • 30 FPS", viewModel.FormatSummary);

            viewModel.OutputFormat = "GIF";
            viewModel.FramesPerSecond = 60;
            Assert.Contains("GIF • GIF • 1920×1080 • 60 FPS", viewModel.FormatSummary);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Invalid_hotkey_blocks_saving()
    {
        var directory = NewDirectory();
        try
        {
            var viewModel = CreateViewModel(new FakeSettingsService(directory), new FakeRecordingService(), new FakeAvailabilityProbe(new RecordingAvailability(true)));
            await viewModel.LoadAsync();
            viewModel.ToggleRecordingHotkey = "Ctrl+Alt";

            Assert.False(await viewModel.SaveSettingsAsync());
            Assert.Equal(Strings.ValidationSummary, viewModel.StatusMessage);
            Assert.Contains(viewModel.GetErrors(nameof(viewModel.ToggleRecordingHotkey)).Cast<string>(),
                error => error.Contains("Atalho inválido"));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Saved_window_or_region_mode_without_target_falls_back_to_full_screen()
    {
        var directory = NewDirectory();
        try
        {
            var settings = new FakeSettingsService(directory)
            {
                Settings = new TelaRecSettings { OutputDirectory = directory, LastCaptureMode = "Janela selecionada" }
            };
            var viewModel = CreateViewModel(settings, new FakeRecordingService(), new FakeAvailabilityProbe(new RecordingAvailability(true)));

            await viewModel.LoadAsync();

            Assert.Equal("fullscreen", viewModel.CaptureMode);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Saved_region_target_is_restored_and_used_by_the_next_request()
    {
        var directory = NewDirectory();
        try
        {
            var settings = new FakeSettingsService(directory)
            {
                Settings = new TelaRecSettings
                {
                    OutputDirectory = directory,
                    LastCaptureMode = "region",
                    LastCaptureTarget = new TelaRec.Core.Capture.CaptureTargetSettings
                    {
                        Kind = "region", X = -400, Y = 30, Width = 800, Height = 600
                    }
                }
            };
            var viewModel = CreateViewModel(settings, new FakeRecordingService(), new FakeAvailabilityProbe(new RecordingAvailability(true)));

            await viewModel.LoadAsync();

            Assert.Equal(MainWindowViewModel.CaptureModeRegion, viewModel.CaptureMode);
            Assert.Equal(new TelaRec.Core.Capture.CaptureTarget.Region(-400, 30, 800, 600), viewModel.BuildCaptureTarget());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Microphone_source_and_selected_device_are_applied_to_recording_request()
    {
        var directory = NewDirectory();
        try
        {
            var recording = new FakeRecordingService();
            var microphone = new FakeMicrophoneCapture();
            var settings = new FakeSettingsService(directory)
            {
                Settings = new TelaRecSettings
                {
                    OutputDirectory = directory,
                    MicrophoneEnabled = true,
                    MicrophoneDeviceId = "mic-2"
                }
            };
            var viewModel = CreateViewModel(settings, recording, new FakeAvailabilityProbe(new RecordingAvailability(true)), microphone: microphone);

            await viewModel.LoadAsync();
            Assert.Equal("both", viewModel.AudioSourceMode);
            Assert.Equal(2, viewModel.MicrophoneDevices.Count);
            viewModel.AudioSourceMode = "microphone";
            await viewModel.ToggleRecordingAsync();

            Assert.NotNull(recording.LastRequest);
            Assert.False(recording.LastRequest!.EffectiveAudioMix.SystemEnabled);
            Assert.True(recording.LastRequest.EffectiveAudioMix.MicrophoneEnabled);
            Assert.Equal("mic-2", recording.LastRequest.MicrophoneDeviceId);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Diagnostics_are_listed_newest_first_and_can_be_cleared()
    {
        var directory = NewDirectory();
        try
        {
            var diagnostics = new FakeDiagnostics();
            var viewModel = CreateViewModel(
                new FakeSettingsService(directory),
                new FakeRecordingService(),
                new FakeAvailabilityProbe(new RecordingAvailability(true)),
                diagnostics: diagnostics);
            await viewModel.LoadAsync();

            diagnostics.Report(DiagnosticSeverity.Information, "first", "Primeiro evento");
            diagnostics.Report(DiagnosticSeverity.Warning, "second", "Segundo evento");

            Assert.Equal(2, viewModel.Diagnostics.Count);
            Assert.Equal("second", viewModel.Diagnostics[0].Code);
            Assert.True(viewModel.HasDiagnostics);

            viewModel.ClearDiagnosticsCommand.Execute(null);

            Assert.Empty(viewModel.Diagnostics);
            Assert.False(viewModel.HasDiagnostics);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Scheduled_recording_updates_state_shows_rejection_and_can_be_cancelled()
    {
        var directory = NewDirectory();
        try
        {
            var scheduler = new FakeScheduler();
            var viewModel = CreateViewModel(
                new FakeSettingsService(directory),
                new FakeRecordingService(),
                new FakeAvailabilityProbe(new RecordingAvailability(true)),
                scheduler: scheduler);
            await viewModel.LoadAsync();
            viewModel.ScheduledStart = DateTime.Now.AddMinutes(5);
            viewModel.ScheduledMinutes = 10;

            await viewModel.ScheduleRecordingAsync();

            var item = Assert.Single(viewModel.Schedules);
            Assert.Equal(ScheduledRecordingState.Pending, item.State);
            Assert.Equal(Strings.ScheduledStatePending, item.StateLabel);
            Assert.True(item.CanCancel);

            scheduler.Report(item.Id, ScheduledRecordingState.Rejected, ScheduledRecordingMessageCode.Conflict);

            Assert.Equal(ScheduledRecordingState.Rejected, item.State);
            Assert.Equal(Strings.ScheduledStateRejected, item.StateLabel);
            Assert.Equal(Strings.ScheduleConflict, item.Message);
            Assert.False(item.CanCancel);

            await viewModel.CancelScheduleAsync(item.Id);

            Assert.Empty(viewModel.Schedules);
            Assert.Equal(Strings.ScheduleCancelled, viewModel.StatusMessage);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Invalid_bitrate_blocks_start()
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

            viewModel.VideoBitrate = 100;
            await viewModel.StartRecordingAsync();

            Assert.False(recording.StartCalled);
            Assert.True(viewModel.HasErrors);
            Assert.NotEmpty(viewModel.GetErrors(nameof(viewModel.VideoBitrate)).Cast<string>());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Relative_output_directory_blocks_start()
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

            viewModel.OutputDirectory = "pasta-relativa";
            await viewModel.StartRecordingAsync();

            Assert.False(recording.StartCalled);
            Assert.NotEmpty(viewModel.GetErrors(nameof(viewModel.OutputDirectory)).Cast<string>());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Recording_state_changes_update_status_and_notifications()
    {
        var directory = NewDirectory();
        try
        {
            var notifications = new List<NotificationMessage>();
            var viewModel = CreateViewModel(
                new FakeSettingsService(directory),
                new FakeRecordingService(),
                new FakeAvailabilityProbe(new RecordingAvailability(true)));
            viewModel.NotificationRequested += (_, message) => notifications.Add(message);
            await viewModel.LoadAsync();

            await viewModel.ToggleRecordingAsync();
            Assert.Equal(RecordingState.Recording, viewModel.CurrentRecordingState);
            Assert.True(viewModel.StopRecordingCommand.CanExecute(null));
            Assert.NotEqual(Strings.Ready, viewModel.StatusMessage);

            await viewModel.ToggleRecordingAsync();
            Assert.Equal(RecordingState.Completed, viewModel.CurrentRecordingState);
            Assert.Contains(notifications, message => message.Title == Strings.NotificationStartedTitle);
            Assert.Contains(notifications, message => message.Title == Strings.NotificationCompletedTitle);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static MainWindowViewModel CreateViewModel(
        ISettingsService settings,
        IRecordingService recording,
        IRecordingAvailabilityProbe availability,
        IScreenshotService? screenshot = null,
        IRecordingDiagnostics? diagnostics = null,
        IRecordingScheduler? scheduler = null,
        IMicrophoneCaptureService? microphone = null) =>
        new(settings, recording, scheduler ?? new FakeScheduler(), diagnostics ?? new SilentDiagnostics(), availability,
            screenshot ?? new FakeScreenshotService(), NullLogger<MainWindowViewModel>.Instance, microphone);

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

    [Fact]
    public void Audio_status_identifies_each_enabled_source()
    {
        var directory = NewDirectory();
        try
        {
            var settings = new FakeSettingsService(directory)
            {
                Settings = new TelaRecSettings { OutputDirectory = directory }
            };
            var viewModel = CreateViewModel(
                settings,
                new FakeRecordingService(),
                new FakeAvailabilityProbe(new RecordingAvailability(true)));

            viewModel.SystemAudioEnabled = true;
            Assert.Contains(Strings.AudioSourceSystem, viewModel.AudioStatusText);
            Assert.DoesNotContain(Strings.AudioSourceMicrophone, viewModel.AudioStatusText);

            viewModel.MicrophoneEnabled = true;
            Assert.Contains(Strings.AudioSourceBoth, viewModel.AudioStatusText);

            viewModel.SystemAudioEnabled = false;
            Assert.Contains(Strings.AudioSourceMicrophone, viewModel.AudioStatusText);

            viewModel.MicrophoneEnabled = false;
            Assert.Equal(Strings.AudioOff, viewModel.AudioStatusText);
        }
        finally { Directory.Delete(directory, true); }
    }

    private sealed class FakeRecordingService : IRecordingService
    {
        public bool StartCalled { get; private set; }
        public bool StopCalled { get; private set; }
        public int PauseCalls { get; private set; }
        public int ResumeCalls { get; private set; }
        public RecordingRequest? LastRequest { get; private set; }
        public RecordingStatus Status { get; private set; } = RecordingStatus.Initial;
        public event EventHandler<RecordingStatus>? StatusChanged;

        public Task StartAsync(RecordingRequest request, CancellationToken cancellationToken = default)
        {
            StartCalled = true;
            LastRequest = request;
            SetStatus(new RecordingStatus(RecordingState.Recording, TimeSpan.Zero));
            return Task.CompletedTask;
        }

        public Task PauseAsync(CancellationToken cancellationToken = default)
        {
            PauseCalls++;
            SetStatus(new RecordingStatus(RecordingState.Paused, Status.Elapsed));
            return Task.CompletedTask;
        }

        public Task ResumeAsync(CancellationToken cancellationToken = default)
        {
            ResumeCalls++;
            SetStatus(new RecordingStatus(RecordingState.Recording, Status.Elapsed));
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCalled = true;
            SetStatus(new RecordingStatus(RecordingState.Completed, Status.Elapsed));
            return Task.CompletedTask;
        }

        private void SetStatus(RecordingStatus status)
        {
            Status = status;
            StatusChanged?.Invoke(this, status);
        }
    }

    private sealed class FakeMicrophoneCapture : IMicrophoneCaptureService
    {
        public event EventHandler<AudioFrame>? AudioCaptured { add { } remove { } }
        public event EventHandler<Exception>? CaptureFailed { add { } remove { } }
        public IReadOnlyList<AudioInputDevice> GetDevices() =>
        [
            new("mic-1", "Microfone 1", true),
            new("mic-2", "Microfone 2")
        ];
        public Task StartAsync(string? deviceId = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeScreenshotService : IScreenshotService
    {
        public int Calls { get; private set; }
        public string? LastDirectory { get; private set; }
        public ScreenshotScope? LastScope { get; private set; }

        public Task<IReadOnlyList<string>> CapturePngAsync(string outputDirectory, ScreenshotScope scope = ScreenshotScope.MonitorUnderCursor, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastDirectory = outputDirectory;
            LastScope = scope;
            IReadOnlyList<string> paths = [Path.Combine(outputDirectory, "shot.png")];
            return Task.FromResult(paths);
        }
    }

    private sealed class FakeAvailabilityProbe(RecordingAvailability availability) : IRecordingAvailabilityProbe
    {
        public RecordingAvailability Check() => availability;
    }

    private sealed class FakeScheduler : IRecordingScheduler
    {
        private readonly List<ScheduledRecordingStatus> _statuses = [];

        public IReadOnlyList<ScheduledRecordingStatus> Scheduled => _statuses;
        public event EventHandler<ScheduledRecordingStatus>? StatusChanged;

        public Task ScheduleAsync(ScheduledRecording schedule, CancellationToken cancellationToken = default)
        {
            var status = new ScheduledRecordingStatus(schedule.Id, ScheduledRecordingState.Pending);
            _statuses.Add(status);
            StatusChanged?.Invoke(this, status);
            return Task.CompletedTask;
        }

        public Task CancelAsync(Guid scheduleId, CancellationToken cancellationToken = default)
        {
            _statuses.RemoveAll(status => status.Id == scheduleId);
            return Task.CompletedTask;
        }

        public void Report(Guid id, ScheduledRecordingState state, ScheduledRecordingMessageCode code = ScheduledRecordingMessageCode.None)
        {
            var status = new ScheduledRecordingStatus(id, state, null, code);
            _statuses.RemoveAll(item => item.Id == id);
            _statuses.Add(status);
            StatusChanged?.Invoke(this, status);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeDiagnostics : IRecordingDiagnostics
    {
        private readonly List<RecordingDiagnostic> _items = [];
        public IReadOnlyList<RecordingDiagnostic> Current => _items;
        public event EventHandler<RecordingDiagnostic>? Reported;

        public void Report(DiagnosticSeverity severity, string code, string message)
        {
            var item = new RecordingDiagnostic(DateTimeOffset.Now, severity, code, message);
            _items.Add(item);
            Reported?.Invoke(this, item);
        }
    }

    private sealed class SilentDiagnostics : IRecordingDiagnostics
    {
        public IReadOnlyList<RecordingDiagnostic> Current => [];
        public event EventHandler<RecordingDiagnostic>? Reported { add { } remove { } }
        public void Report(DiagnosticSeverity severity, string code, string message) { }
    }
}
