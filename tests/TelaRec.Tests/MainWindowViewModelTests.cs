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

    [Fact]
    public async Task Preset_selection_updates_profile_and_persists()
    {
        var directory = NewDirectory();
        try
        {
            var settings = new FakeSettingsService(directory);
            var viewModel = CreateViewModel(settings, new FakeRecordingService(), new FakeAvailabilityProbe(new RecordingAvailability(true)));

            await viewModel.LoadAsync();
            viewModel.SelectedPreset = "Leve 720p";

            Assert.Equal("1280×720", viewModel.OutputResolution);
            Assert.Equal(30, viewModel.FramesPerSecond);
            Assert.Equal(QualityBitrateCalculator.Suggest("Média", 1280, 720, 30), viewModel.VideoBitrate);
            Assert.True(await viewModel.SaveSettingsAsync());
            Assert.Equal("Leve 720p", settings.Settings.SelectedPreset);
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

            viewModel.SelectedPreset = "Tutorial 1080p";
            viewModel.VideoBitrate = 3_000_000;

            Assert.Equal(Strings.CustomPreset, viewModel.SelectedPreset);
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
            Assert.Equal(Strings.CustomPreset, viewModel.SelectedPreset);
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
            Assert.Contains("Atalho inválido", viewModel.StatusMessage);
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

            Assert.Equal("Tela inteira", viewModel.CaptureMode);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static MainWindowViewModel CreateViewModel(
        ISettingsService settings,
        IRecordingService recording,
        IRecordingAvailabilityProbe availability,
        IScreenshotService? screenshot = null) =>
        new(settings, recording, new FakeScheduler(), new SilentDiagnostics(), availability,
            screenshot ?? new FakeScreenshotService(), NullLogger<MainWindowViewModel>.Instance);

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
        public bool StopCalled { get; private set; }
        public int PauseCalls { get; private set; }
        public int ResumeCalls { get; private set; }
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

        public Task PauseAsync(CancellationToken cancellationToken = default)
        {
            PauseCalls++;
            Status = new RecordingStatus(RecordingState.Paused, Status.Elapsed);
            return Task.CompletedTask;
        }

        public Task ResumeAsync(CancellationToken cancellationToken = default)
        {
            ResumeCalls++;
            Status = new RecordingStatus(RecordingState.Recording, Status.Elapsed);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCalled = true;
            Status = new RecordingStatus(RecordingState.Completed, Status.Elapsed);
            return Task.CompletedTask;
        }
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
