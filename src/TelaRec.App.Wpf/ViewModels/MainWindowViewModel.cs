using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using TelaRec.App.Wpf.Infrastructure;
using TelaRec.App.Wpf.Resources;
using TelaRec.Core.Configuration;
using TelaRec.Core.Capture;
using TelaRec.Core.Contracts;
using TelaRec.Core.Recording;
using TelaRec.Core.Reliability;
using TelaRec.Core.Scheduling;

namespace TelaRec.App.Wpf.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly ISettingsService _settingsService;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly IRecordingService _recordingService;
    private readonly IRecordingScheduler _scheduler;
    private readonly IRecordingDiagnostics _diagnostics;
    private readonly IRecordingAvailabilityProbe _availability;
    private TelaRecSettings _settings = new();
    private string _statusMessage = Strings.Get("Ready");
    private string _outputDirectory = string.Empty;
    private string _recordingDuration = "00:00:00";
    private string _captureMode = "Tela inteira";
    private CaptureTarget? _selectedTarget;
    private readonly Stopwatch _durationClock = new();
    private readonly DispatcherTimer _durationTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private TimeSpan _durationBeforeCurrent;
    private string? _audioWarning;
    private string _diagnosticsText = string.Empty;
    private DateTime _scheduledStart = DateTime.Now.AddMinutes(5);
    private int _scheduledMinutes = 10;

    public MainWindowViewModel(ISettingsService settingsService, IRecordingService recordingService, IRecordingScheduler scheduler, IRecordingDiagnostics diagnostics, IRecordingAvailabilityProbe availability, ILogger<MainWindowViewModel> logger)
    {
        _settingsService = settingsService;
        _recordingService = recordingService;
        _logger = logger; _scheduler = scheduler; _diagnostics = diagnostics; _availability = availability;
        SaveSettingsCommand = new RelayCommand(async () => await SaveSettingsAsync());
        OpenOutputDirectoryCommand = new RelayCommand(OpenOutputDirectory, () => Directory.Exists(OutputDirectory));
        StartRecordingCommand = new RelayCommand(async () => await StartRecordingAsync(), () => _recordingService.Status.State is RecordingState.Idle or RecordingState.Completed or RecordingState.Failed);
        PauseRecordingCommand = new RelayCommand(async () => await PauseRecordingAsync(), () => _recordingService.Status.State == RecordingState.Recording);
        ResumeRecordingCommand = new RelayCommand(async () => await ResumeRecordingAsync(), () => _recordingService.Status.State == RecordingState.Paused);
        StopRecordingCommand = new RelayCommand(async () => await StopRecordingAsync(), () => _recordingService.Status.State is RecordingState.Recording or RecordingState.Paused);
        ScheduleRecordingCommand = new RelayCommand(async () => await ScheduleRecordingAsync());
        _recordingService.StatusChanged += (_, status) => System.Windows.Application.Current.Dispatcher.BeginInvoke(() => UpdateRecordingStatus(status));
        _durationTimer.Tick += (_, _) => UpdateLiveDuration();
        _diagnostics.Reported += (_, diagnostic) => System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            DiagnosticsText = $"{diagnostic.Timestamp:HH:mm:ss} — {diagnostic.Message}");
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string RecordingDuration { get => _recordingDuration; private set => SetField(ref _recordingDuration, value); }
    public string CaptureMode { get => _captureMode; set { if (SetField(ref _captureMode, value)) { _settings.LastCaptureMode = value; _selectedTarget = null; } } }
    public void SelectTarget(CaptureTarget target)
    {
        CaptureMode = target switch { CaptureTarget.Window => "Janela selecionada", CaptureTarget.Region => "Região personalizada", _ => "Tela inteira" };
        _selectedTarget = target;
        StatusMessage = target switch { CaptureTarget.Window window => $"Janela: {window.Title}", CaptureTarget.Region => "Região selecionada.", _ => "Tela inteira selecionada." };
    }
    public string StatusMessage { get => _statusMessage; private set => SetField(ref _statusMessage, value); }
    public string DiagnosticsText { get => _diagnosticsText; private set => SetField(ref _diagnosticsText, value); }
    public DateTime ScheduledStart { get => _scheduledStart; set => SetField(ref _scheduledStart, value); }
    public int ScheduledMinutes { get => _scheduledMinutes; set => SetField(ref _scheduledMinutes, value); }
    public string OutputDirectory
    {
        get => _outputDirectory;
        set { if (SetField(ref _outputDirectory, value)) ((RelayCommand)OpenOutputDirectoryCommand).RaiseCanExecuteChanged(); }
    }
    public int FramesPerSecond
    {
        get => _settings.FramesPerSecond;
        set { if (_settings.FramesPerSecond != value) { _settings.FramesPerSecond = value; OnPropertyChanged(); } }
    }
    public bool SystemAudioEnabled
    {
        get => _settings.SystemAudioEnabled;
        set { if (_settings.SystemAudioEnabled != value) { _settings.SystemAudioEnabled = value; OnPropertyChanged(); OnPropertyChanged(nameof(AudioStatusText)); } }
    }
    public bool ShowCursor
    {
        get => _settings.ShowCursor;
        set { if (_settings.ShowCursor != value) { _settings.ShowCursor = value; OnPropertyChanged(); } }
    }
    public bool ShowClickEffects
    {
        get => _settings.ShowClickEffects;
        set { if (_settings.ShowClickEffects != value) { _settings.ShowClickEffects = value; OnPropertyChanged(); } }
    }
    public string AudioStatusText => SystemAudioEnabled ? "🔊 Áudio ligado" : "🔇 Áudio desligado";
    public string OutputFormat
    {
        get => _settings.RecordingContainer.ToString().ToUpperInvariant();
        set
        {
            if (Enum.TryParse<RecordingContainer>(value, true, out var container)) { _settings.RecordingContainer = container; OnPropertyChanged(); }
        }
    }
    public string OutputResolution
    {
        get => $"{_settings.OutputWidth}×{_settings.OutputHeight}";
        set
        {
            var parts = value.Split('×', 'x', 'X');
            if (parts.Length == 2 && int.TryParse(parts[0], out var width) && int.TryParse(parts[1], out var height))
            {
                _settings.OutputWidth = width; _settings.OutputHeight = height; OnPropertyChanged();
            }
        }
    }
    public int VideoBitrate
    {
        get => _settings.VideoBitrate;
        set { if (_settings.VideoBitrate != value) { _settings.VideoBitrate = value; OnPropertyChanged(); } }
    }
    public bool WebcamEnabled { get => _settings.Webcam.Enabled; set { if (_settings.Webcam.Enabled != value) { _settings.Webcam = _settings.Webcam with { Enabled = value }; OnPropertyChanged(); } } }
    public int WebcamDeviceIndex { get => _settings.Webcam.DeviceIndex; set { if (_settings.Webcam.DeviceIndex != value) { _settings.Webcam = _settings.Webcam with { DeviceIndex = value }; OnPropertyChanged(); } } }
    public double WebcamScale { get => _settings.Webcam.Scale; set { _settings.Webcam = _settings.Webcam with { Scale = value }; OnPropertyChanged(); } }
    public string WatermarkText { get => _settings.Watermark.Text ?? string.Empty; set { _settings.Watermark = _settings.Watermark with { Kind = string.IsNullOrWhiteSpace(value) ? WatermarkKind.None : WatermarkKind.Text, Text = value }; OnPropertyChanged(); } }
    public string WatermarkImagePath { get => _settings.Watermark.ImagePath ?? string.Empty; set { _settings.Watermark = _settings.Watermark with { Kind = string.IsNullOrWhiteSpace(value) ? WatermarkKind.None : WatermarkKind.Image, ImagePath = value }; OnPropertyChanged(); } }
    public double WatermarkOpacity { get => _settings.Watermark.Opacity; set { _settings.Watermark = _settings.Watermark with { Opacity = value }; OnPropertyChanged(); } }
    public double WatermarkScale { get => _settings.Watermark.Scale; set { _settings.Watermark = _settings.Watermark with { Scale = value }; OnPropertyChanged(); } }
    public string WatermarkPosition { get => _settings.Watermark.Position.ToString(); set { if (Enum.TryParse<OverlayPosition>(value, true, out var position)) { _settings.Watermark = _settings.Watermark with { Position = position }; OnPropertyChanged(); } } }
    public string WebcamPosition { get => _settings.Webcam.Position.ToString(); set { if (Enum.TryParse<OverlayPosition>(value, true, out var position)) { _settings.Webcam = _settings.Webcam with { Position = position }; OnPropertyChanged(); } } }
    public double WebcamOpacity { get => _settings.Webcam.Opacity; set { _settings.Webcam = _settings.Webcam with { Opacity = value }; OnPropertyChanged(); } }
    public ICommand SaveSettingsCommand { get; }
    public ICommand OpenOutputDirectoryCommand { get; }
    public ICommand StartRecordingCommand { get; }
    public ICommand PauseRecordingCommand { get; }
    public ICommand ResumeRecordingCommand { get; }
    public ICommand StopRecordingCommand { get; }
    public ICommand ScheduleRecordingCommand { get; }

    public async Task LoadAsync()
    {
        _settings = await _settingsService.LoadAsync();
        OutputDirectory = _settings.OutputDirectory;
        OnPropertyChanged(nameof(FramesPerSecond));
        OnPropertyChanged(nameof(SystemAudioEnabled));
        OnPropertyChanged(nameof(AudioStatusText));
        OnPropertyChanged(nameof(ShowCursor));
        OnPropertyChanged(nameof(ShowClickEffects));
        OnPropertyChanged(nameof(OutputFormat));
        OnPropertyChanged(nameof(OutputResolution));
        OnPropertyChanged(nameof(VideoBitrate));
        OnPropertyChanged(nameof(WebcamEnabled)); OnPropertyChanged(nameof(WebcamDeviceIndex)); OnPropertyChanged(nameof(WebcamScale));
        OnPropertyChanged(nameof(WatermarkText)); OnPropertyChanged(nameof(WatermarkImagePath)); OnPropertyChanged(nameof(WatermarkOpacity)); OnPropertyChanged(nameof(WatermarkScale));
        OnPropertyChanged(nameof(WatermarkPosition)); OnPropertyChanged(nameof(WebcamPosition)); OnPropertyChanged(nameof(WebcamOpacity));
        CaptureMode = _settings.LastCaptureMode;
    }

    private async Task<bool> SaveSettingsAsync()
    {
        SyncSettingsFromView();
        try
        {
            await _settingsService.SaveAsync(_settings);
            StatusMessage = "Configurações salvas.";
            ((RelayCommand)OpenOutputDirectoryCommand).RaiseCanExecuteChanged();
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Falha ao salvar configurações");
            StatusMessage = "Não foi possível salvar as configurações.";
            return false;
        }
    }

    private void SyncSettingsFromView()
    {
        _settings.OutputDirectory = OutputDirectory.Trim();
        _settings.LastCaptureMode = CaptureMode;
    }

    private void OpenOutputDirectory()
    {
        if (Directory.Exists(OutputDirectory)) Process.Start(new ProcessStartInfo(OutputDirectory) { UseShellExecute = true });
    }

    internal async Task StartRecordingAsync()
    {
        try
        {
            SyncSettingsFromView();
            if (!EnsureSettingsValid()) return;
            if (!await SaveSettingsAsync()) return;

            var availability = _availability.Check();
            if (!availability.CanRecord)
            {
                var reason = string.IsNullOrWhiteSpace(availability.Reason)
                    ? "A captura de tela não está disponível."
                    : availability.Reason;
                StatusMessage = reason;
                _diagnostics.Report(DiagnosticSeverity.Warning, "recording.unavailable", reason);
                return;
            }

            await _recordingService.StartAsync(CreateRequest());
        }
        catch (Exception exception) { _logger.LogError(exception, "Falha ao iniciar a gravação"); StatusMessage = exception.Message; }
    }
    private bool EnsureSettingsValid()
    {
        var issues = SettingsValidator.Validate(_settings);
        if (issues.Count == 0) return true;
        var issue = issues[0];
        StatusMessage = issue.Message;
        _diagnostics.Report(DiagnosticSeverity.Warning, $"settings.{issue.Code}", issue.Message);
        return false;
    }
    private async Task ScheduleRecordingAsync()
    {
        try
        {
            SyncSettingsFromView();
            if (!EnsureSettingsValid()) return;
            if (!await SaveSettingsAsync()) return;
            var request = CreateRequest();
            var start = new DateTimeOffset(ScheduledStart);
            await _scheduler.ScheduleAsync(new ScheduledRecording(Guid.NewGuid(), start, start.AddMinutes(ScheduledMinutes), request));
            DiagnosticsText = $"Gravação agendada para {start:dd/MM HH:mm}.";
        }
        catch (Exception exception) { _logger.LogWarning(exception, "Não foi possível agendar gravação"); DiagnosticsText = exception.Message; }
    }
    private RecordingRequest CreateRequest()
    {
        var profile = new RecordingProfile(_settings.RecordingContainer,
            _settings.RecordingContainer == RecordingContainer.Gif ? VideoCodec.Gif : _settings.VideoCodec,
            VideoBitrate, FramesPerSecond, _settings.OutputWidth, _settings.OutputHeight);
        if (!profile.IsValid) throw new InvalidOperationException("Formato, resolução ou bitrate inválidos.");
        var name = $"TelaRec_{DateTime.Now:yyyyMMdd_HHmmss}{profile.FileExtension}";
        CaptureTarget target = CaptureMode switch
        {
            "Tela inteira" => new CaptureTarget.Display(),
            "Janela selecionada" when _selectedTarget is CaptureTarget.Window window => window,
            "Região personalizada" when _selectedTarget is CaptureTarget.Region region => region,
            _ => throw new InvalidOperationException("Selecione a janela ou região antes de gravar.")
        };
        return new RecordingRequest(Path.Combine(OutputDirectory, name), FramesPerSecond, SystemAudioEnabled, target, profile, new RecordingOverlaySettings(_settings.Watermark, _settings.Webcam, BuildClickEffects()), _settings.ShowCursor);
    }
    private ClickEffectSettings BuildClickEffects() => _settings.ShowClickEffects
        ? new ClickEffectSettings(
            Enabled: true,
            Radius: _settings.ClickEffectRadius,
            DurationSeconds: _settings.ClickEffectDurationSeconds,
            Color: _settings.ClickEffectColor)
        : ClickEffectSettings.None;
    private async Task PauseRecordingAsync() { try { await _recordingService.PauseAsync(); } catch (Exception exception) { _logger.LogError(exception, "Falha ao pausar"); } }
    private async Task ResumeRecordingAsync() { try { await _recordingService.ResumeAsync(); } catch (Exception exception) { _logger.LogError(exception, "Falha ao retomar"); } }
    private async Task StopRecordingAsync() { try { await _recordingService.StopAsync(); } catch (Exception exception) { _logger.LogError(exception, "Falha ao parar"); } }
    private void UpdateRecordingStatus(RecordingStatus status)
    {
        if (status.State == RecordingState.Starting)
        {
            _durationTimer.Stop(); _durationClock.Reset(); _durationBeforeCurrent = TimeSpan.Zero; _audioWarning = null;
            RecordingDuration = "00:00:00";
        }
        if (status.State == RecordingState.Recording)
        {
            _audioWarning = status.ErrorMessage ?? _audioWarning;
            if (!_durationClock.IsRunning) _durationClock.Start();
            _durationTimer.Start();
        }
        if (status.State is RecordingState.Paused or RecordingState.Stopping or RecordingState.Completed or RecordingState.Failed)
        {
            _durationBeforeCurrent += _durationClock.Elapsed;
            _durationClock.Reset(); _durationTimer.Stop();
            RecordingDuration = _durationBeforeCurrent.ToString(@"hh\:mm\:ss");
        }
        StatusMessage = status.State switch
        {
            RecordingState.Recording => $"Gravando — {status.Elapsed:mm\\:ss}",
            RecordingState.Paused => Strings.Get("RecordingPaused"),
            RecordingState.Completed => Strings.Get("RecordingComplete"),
            RecordingState.Failed => $"Falha: {status.ErrorMessage}",
            _ => status.State.ToString()
        };
        if (status.State == RecordingState.Recording) UpdateLiveDuration();
        foreach (var command in new[] { StartRecordingCommand, PauseRecordingCommand, ResumeRecordingCommand, StopRecordingCommand }) ((RelayCommand)command).RaiseCanExecuteChanged();
    }
    private void UpdateLiveDuration()
    {
        RecordingDuration = (_durationBeforeCurrent + _durationClock.Elapsed).ToString(@"hh\:mm\:ss");
        StatusMessage = _audioWarning ?? $"Gravando — {RecordingDuration}";
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
