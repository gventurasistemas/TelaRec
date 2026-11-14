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
using TelaRec.Core.Input;
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
    private readonly IScreenshotService _screenshotService;
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
    private string _selectedPreset = RecordingPresets.All[0].Name;
    private bool _applyingPreset;

    public MainWindowViewModel(ISettingsService settingsService, IRecordingService recordingService, IRecordingScheduler scheduler, IRecordingDiagnostics diagnostics, IRecordingAvailabilityProbe availability, IScreenshotService screenshotService, ILogger<MainWindowViewModel> logger)
    {
        _settingsService = settingsService;
        _recordingService = recordingService;
        _logger = logger; _scheduler = scheduler; _diagnostics = diagnostics; _availability = availability; _screenshotService = screenshotService;
        PresetOptions = RecordingPresets.All.Select(preset => preset.Name).Append(Strings.CustomPreset).ToList();
        SaveSettingsCommand = new RelayCommand(async () => await SaveSettingsAsync());
        OpenOutputDirectoryCommand = new RelayCommand(OpenOutputDirectory, () => Directory.Exists(OutputDirectory));
        StartRecordingCommand = new RelayCommand(async () => await StartRecordingAsync(), () => _recordingService.Status.State is RecordingState.Idle or RecordingState.Completed or RecordingState.Failed);
        PauseRecordingCommand = new RelayCommand(async () => await PauseRecordingAsync(), () => _recordingService.Status.State == RecordingState.Recording);
        ResumeRecordingCommand = new RelayCommand(async () => await ResumeRecordingAsync(), () => _recordingService.Status.State == RecordingState.Paused);
        StopRecordingCommand = new RelayCommand(async () => await StopRecordingAsync(), () => _recordingService.Status.State is RecordingState.Recording or RecordingState.Paused);
        ScheduleRecordingCommand = new RelayCommand(async () => await ScheduleRecordingAsync());
        ScreenshotCommand = new RelayCommand(async () => await CaptureScreenshotAsync());
        ToggleRecordingCommand = new RelayCommand(async () => await ToggleRecordingAsync(),
            () => _recordingService.Status.State is not (RecordingState.Starting or RecordingState.Stopping));
        TogglePauseCommand = new RelayCommand(async () => await TogglePauseAsync(),
            () => _recordingService.Status.State is RecordingState.Recording or RecordingState.Paused);
        _recordingService.StatusChanged += (_, status) => System.Windows.Application.Current.Dispatcher.BeginInvoke(() => UpdateRecordingStatus(status));
        _durationTimer.Tick += (_, _) => UpdateLiveDuration();
        _diagnostics.Reported += (_, diagnostic) => System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            DiagnosticsText = $"{diagnostic.Timestamp:HH:mm:ss} — {diagnostic.Message}");
    }

    /// <summary>Disparado após salvar configurações para reaplicar os atalhos globais.</summary>
    public event EventHandler? HotkeysChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<string> PresetOptions { get; }
    public string RecordingDuration { get => _recordingDuration; private set => SetField(ref _recordingDuration, value); }
    public RecordingState CurrentRecordingState => _recordingService.Status.State;
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

    public string SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (!SetField(ref _selectedPreset, value)) return;
            _settings.SelectedPreset = value;
            if (_applyingPreset) return;
            if (value == Strings.CustomPreset) return;
            if (RecordingPresets.All.FirstOrDefault(preset => preset.Name == value) is { } selected)
                ApplyPreset(selected);
        }
    }

    public int FramesPerSecond
    {
        get => _settings.FramesPerSecond;
        set
        {
            if (_settings.FramesPerSecond == value) return;
            _settings.FramesPerSecond = value;
            OnPropertyChanged();
            NotifyFormatSummary();
            if (_applyingPreset) return;
            RecalculateBitrate();
            MarkCustomPreset();
        }
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
            if (Enum.TryParse<RecordingContainer>(value, true, out var container))
            {
                _settings.RecordingContainer = container;
                OnPropertyChanged();
                NotifyFormatSummary();
            }
        }
    }
    /// <summary>Resumo do cabeçalho: contêiner, codec, resolução e FPS efetivos.</summary>
    public string FormatSummary =>
        $"{OutputFormat} • {CodecSummary} • {_settings.OutputWidth}×{_settings.OutputHeight} • {_settings.FramesPerSecond} FPS";
    private string CodecSummary => _settings.RecordingContainer == RecordingContainer.Gif
        ? "GIF"
        : _settings.VideoCodec switch
        {
            VideoCodec.H264 => "H.264",
            VideoCodec.Hevc => "HEVC",
            VideoCodec.Gif => "GIF",
            _ => _settings.VideoCodec.ToString().ToUpperInvariant()
        };
    public string OutputResolution
    {
        get => $"{_settings.OutputWidth}×{_settings.OutputHeight}";
        set
        {
            var parts = value.Split('×', 'x', 'X');
            if (parts.Length != 2 || !int.TryParse(parts[0], out var width) || !int.TryParse(parts[1], out var height)) return;
            if (_settings.OutputWidth == width && _settings.OutputHeight == height) return;
            _settings.OutputWidth = width; _settings.OutputHeight = height;
            OnPropertyChanged();
            NotifyFormatSummary();
            if (_applyingPreset) return;
            RecalculateBitrate();
            MarkCustomPreset();
        }
    }
    public int VideoBitrate
    {
        get => _settings.VideoBitrate;
        set
        {
            if (_settings.VideoBitrate == value) return;
            _settings.VideoBitrate = value;
            OnPropertyChanged();
            MarkCustomPreset();
        }
    }
    public string Quality
    {
        get => _settings.Quality;
        set
        {
            if (_settings.Quality == value) return;
            _settings.Quality = value;
            OnPropertyChanged();
            if (_applyingPreset) return;
            RecalculateBitrate();
            MarkCustomPreset();
        }
    }
    public string ToggleRecordingHotkey
    {
        get => _settings.ToggleRecordingHotkey;
        set { if (_settings.ToggleRecordingHotkey != value) { _settings.ToggleRecordingHotkey = value; OnPropertyChanged(); } }
    }
    public string TogglePauseHotkey
    {
        get => _settings.TogglePauseHotkey;
        set { if (_settings.TogglePauseHotkey != value) { _settings.TogglePauseHotkey = value; OnPropertyChanged(); } }
    }
    public string ScreenshotHotkey
    {
        get => _settings.ScreenshotHotkey;
        set { if (_settings.ScreenshotHotkey != value) { _settings.ScreenshotHotkey = value; OnPropertyChanged(); } }
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
    public ICommand ScreenshotCommand { get; }
    public ICommand ToggleRecordingCommand { get; }
    public ICommand TogglePauseCommand { get; }

    public async Task LoadAsync()
    {
        _settings = await _settingsService.LoadAsync();
        OutputDirectory = _settings.OutputDirectory;
        _selectedPreset = ResolveSelectedPreset();
        OnPropertyChanged(nameof(FramesPerSecond));
        OnPropertyChanged(nameof(SystemAudioEnabled));
        OnPropertyChanged(nameof(AudioStatusText));
        OnPropertyChanged(nameof(ShowCursor));
        OnPropertyChanged(nameof(ShowClickEffects));
        OnPropertyChanged(nameof(OutputFormat));
        OnPropertyChanged(nameof(OutputResolution));
        OnPropertyChanged(nameof(FormatSummary));
        OnPropertyChanged(nameof(VideoBitrate));
        OnPropertyChanged(nameof(Quality));
        OnPropertyChanged(nameof(SelectedPreset));
        OnPropertyChanged(nameof(ToggleRecordingHotkey));
        OnPropertyChanged(nameof(TogglePauseHotkey));
        OnPropertyChanged(nameof(ScreenshotHotkey));
        OnPropertyChanged(nameof(WebcamEnabled)); OnPropertyChanged(nameof(WebcamDeviceIndex)); OnPropertyChanged(nameof(WebcamScale));
        OnPropertyChanged(nameof(WatermarkText)); OnPropertyChanged(nameof(WatermarkImagePath)); OnPropertyChanged(nameof(WatermarkOpacity)); OnPropertyChanged(nameof(WatermarkScale));
        OnPropertyChanged(nameof(WatermarkPosition)); OnPropertyChanged(nameof(WebcamPosition)); OnPropertyChanged(nameof(WebcamOpacity));
        // A janela/região escolhida não sobrevive ao reinício; volta para tela inteira
        // em vez de deixar o botão Gravar em um estado impossível de usar.
        CaptureMode = _settings.LastCaptureMode is "Janela selecionada" or "Região personalizada"
            ? "Tela inteira"
            : _settings.LastCaptureMode;
    }

    /// <summary>Aplica um preset e persiste a escolha na próxima gravação.</summary>
    internal void ApplyPreset(RecordingPreset preset)
    {
        _applyingPreset = true;
        try
        {
            _settings.OutputWidth = preset.Width;
            _settings.OutputHeight = preset.Height;
            _settings.FramesPerSecond = preset.FramesPerSecond;
            _settings.Quality = preset.Quality;
            _settings.VideoBitrate = QualityBitrateCalculator.Suggest(preset.Quality, preset.Width, preset.Height, preset.FramesPerSecond);
            _settings.SelectedPreset = preset.Name;
            OnPropertyChanged(nameof(OutputResolution));
            OnPropertyChanged(nameof(FramesPerSecond));
            OnPropertyChanged(nameof(Quality));
            OnPropertyChanged(nameof(VideoBitrate));
            NotifyFormatSummary();
        }
        finally { _applyingPreset = false; }
    }

    internal async Task<bool> SaveSettingsAsync()
    {
        SyncSettingsFromView();
        if (!ValidateHotkeys()) return false;
        try
        {
            await _settingsService.SaveAsync(_settings);
            StatusMessage = Strings.Get("SettingsSaved");
            ((RelayCommand)OpenOutputDirectoryCommand).RaiseCanExecuteChanged();
            HotkeysChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Falha ao salvar configurações");
            StatusMessage = "Não foi possível salvar as configurações.";
            return false;
        }
    }

    private bool ValidateHotkeys()
    {
        var bindings = new[]
        {
            (Label: Strings.ToggleRecordingHotkey, Value: ToggleRecordingHotkey),
            (Label: Strings.TogglePauseHotkey, Value: TogglePauseHotkey),
            (Label: Strings.ScreenshotHotkey, Value: ScreenshotHotkey)
        };
        var parsed = new List<HotkeyGesture>(bindings.Length);
        foreach (var binding in bindings)
        {
            if (!HotkeyGesture.TryParse(binding.Value, out var gesture))
            {
                ReportHotkeyIssue(string.Format(Strings.HotkeyInvalid, binding.Value));
                return false;
            }
            parsed.Add(gesture);
        }

        if (parsed.Distinct().Count() != parsed.Count)
        {
            ReportHotkeyIssue(Strings.HotkeyDuplicateConflict);
            return false;
        }
        return true;
    }

    /// <summary>Exibe no status e nos diagnósticos um problema de atalho global.</summary>
    public void ReportHotkeyIssue(string message)
    {
        StatusMessage = message;
        DiagnosticsText = message;
        _diagnostics.Report(DiagnosticSeverity.Warning, "hotkey.registration", message);
        _logger.LogWarning("Problema de atalho global: {Message}", message);
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

    /// <summary>Atalho Ctrl+Alt+R: inicia quando parado e para quando gravando/pausado.</summary>
    internal async Task ToggleRecordingAsync()
    {
        switch (_recordingService.Status.State)
        {
            case RecordingState.Idle:
            case RecordingState.Completed:
            case RecordingState.Failed:
                await StartRecordingAsync();
                break;
            case RecordingState.Recording:
            case RecordingState.Paused:
                await StopRecordingAsync();
                break;
        }
    }

    /// <summary>Atalho Ctrl+Alt+P: pausa quando gravando e retoma quando pausado.</summary>
    internal async Task TogglePauseAsync()
    {
        switch (_recordingService.Status.State)
        {
            case RecordingState.Recording:
                await PauseRecordingAsync();
                break;
            case RecordingState.Paused:
                await ResumeRecordingAsync();
                break;
        }
    }

    internal async Task CaptureScreenshotAsync()
    {
        try
        {
            SyncSettingsFromView();
            if (!EnsureSettingsValid()) return;

            var paths = await _screenshotService.CapturePngAsync(OutputDirectory, ScreenshotScope.MonitorUnderCursor);
            if (paths.Count == 0)
            {
                StatusMessage = Strings.ScreenshotFailed;
                _diagnostics.Report(DiagnosticSeverity.Warning, "screenshot.empty", Strings.ScreenshotFailed);
                return;
            }

            var names = string.Join(", ", paths.Select(Path.GetFileName));
            StatusMessage = string.Format(Strings.ScreenshotSaved, names);
            DiagnosticsText = paths[^1];
            _diagnostics.Report(DiagnosticSeverity.Information, "screenshot.saved", StatusMessage);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Falha ao capturar screenshot");
            StatusMessage = Strings.ScreenshotFailed;
            _diagnostics.Report(DiagnosticSeverity.Warning, "screenshot.failed", $"{Strings.ScreenshotFailed} {exception.Message}");
        }
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
        foreach (var command in new[] { StartRecordingCommand, PauseRecordingCommand, ResumeRecordingCommand, StopRecordingCommand, ToggleRecordingCommand, TogglePauseCommand }) ((RelayCommand)command).RaiseCanExecuteChanged();
    }
    private void UpdateLiveDuration()
    {
        RecordingDuration = (_durationBeforeCurrent + _durationClock.Elapsed).ToString(@"hh\:mm\:ss");
        StatusMessage = _audioWarning ?? $"Gravando — {RecordingDuration}";
    }

    private void RecalculateBitrate()
    {
        var suggested = QualityBitrateCalculator.Suggest(_settings.Quality, _settings.OutputWidth, _settings.OutputHeight, _settings.FramesPerSecond);
        if (_settings.VideoBitrate == suggested) return;
        _settings.VideoBitrate = suggested;
        OnPropertyChanged(nameof(VideoBitrate));
    }

    private void MarkCustomPreset()
    {
        if (_applyingPreset) return;
        _settings.SelectedPreset = Strings.CustomPreset;
        if (_selectedPreset == Strings.CustomPreset) return;
        _selectedPreset = Strings.CustomPreset;
        OnPropertyChanged(nameof(SelectedPreset));
    }

    private string ResolveSelectedPreset()
    {
        if (RecordingPresets.All.FirstOrDefault(preset =>
                preset.Name == _settings.SelectedPreset &&
                preset.Width == _settings.OutputWidth &&
                preset.Height == _settings.OutputHeight &&
                preset.FramesPerSecond == _settings.FramesPerSecond &&
                string.Equals(preset.Quality, _settings.Quality, StringComparison.OrdinalIgnoreCase)) is { } match)
            return match.Name;
        return Strings.CustomPreset;
    }

    private void NotifyFormatSummary() => OnPropertyChanged(nameof(FormatSummary));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
