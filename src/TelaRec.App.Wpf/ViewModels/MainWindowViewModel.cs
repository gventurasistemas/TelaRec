using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using TelaRec.App.Wpf.Infrastructure;
using TelaRec.App.Wpf.Resources;
using TelaRec.Core.Capture;
using TelaRec.Core.Configuration;
using TelaRec.Core.Contracts;
using TelaRec.Core.Input;
using TelaRec.Core.Recording;
using TelaRec.Core.Reliability;
using TelaRec.Core.Scheduling;

namespace TelaRec.App.Wpf.ViewModels;

public sealed record NotificationMessage(string Title, string Body, bool IsError);

public sealed class MainWindowViewModel : INotifyPropertyChanged, INotifyDataErrorInfo
{
    public const string CustomPresetId = "custom";
    public const string CaptureModeFullScreen = "fullscreen";
    public const string CaptureModeWindow = "window";
    public const string CaptureModeRegion = "region";

    private const int DiagnosticLimit = 20;

    private readonly ISettingsService _settingsService;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly IRecordingService _recordingService;
    private readonly IRecordingScheduler _scheduler;
    private readonly IRecordingDiagnostics _diagnostics;
    private readonly IRecordingAvailabilityProbe _availability;
    private readonly IScreenshotService _screenshotService;
    private readonly IMicrophoneCaptureService? _microphoneCapture;
    private readonly IVideoEncoderCapabilitiesService? _encoderCapabilitiesService;
    private readonly IRecordingTelemetrySource? _recordingTelemetry;
    private readonly Dictionary<Guid, ScheduledRecording> _scheduleRules = new();
    private readonly Dictionary<string, List<string>> _errors = new();
    private TelaRecSettings _settings = new();
    private string _statusMessage = Strings.Ready;
    private string _outputDirectory = string.Empty;
    private string _recordingDuration = "00:00:00";
    private string _captureMode = CaptureModeFullScreen;
    private CaptureTarget? _selectedTarget;
    private int _selectedDisplayIndex;
    private readonly Stopwatch _durationClock = new();
    private readonly DispatcherTimer _durationTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private TimeSpan _durationBeforeCurrent;
    private string? _audioWarning;
    private string _diagnosticsText = string.Empty;
    private DateTime _scheduledStart = DateTime.Now.AddMinutes(5);
    private int _scheduledMinutes = 10;
    private string _selectedPreset = RecordingPresets.All[0].Id;
    private bool _applyingPreset;
    private RecordingState _lastNotifiedState = RecordingState.Idle;

    public MainWindowViewModel(
        ISettingsService settingsService,
        IRecordingService recordingService,
        IRecordingScheduler scheduler,
        IRecordingDiagnostics diagnostics,
        IRecordingAvailabilityProbe availability,
        IScreenshotService screenshotService,
        ILogger<MainWindowViewModel> logger,
        IMicrophoneCaptureService? microphoneCapture = null,
        IVideoEncoderCapabilitiesService? encoderCapabilitiesService = null,
        IRecordingTelemetrySource? recordingTelemetry = null)
    {
        _settingsService = settingsService;
        _recordingService = recordingService;
        _logger = logger;
        _scheduler = scheduler;
        _diagnostics = diagnostics;
        _availability = availability;
        _screenshotService = screenshotService;
        _microphoneCapture = microphoneCapture;
        _encoderCapabilitiesService = encoderCapabilitiesService;
        _recordingTelemetry = recordingTelemetry;

        PresetOptions = RecordingPresets.All
            .Select(preset => new LocalizedOption(preset.Id, Strings.Get(preset.ResourceKey)))
            .Append(new LocalizedOption(CustomPresetId, Strings.CustomPreset))
            .ToList();
        CaptureModeOptions =
        [
            new(CaptureModeFullScreen, Strings.CaptureFullScreen),
            new(CaptureModeWindow, Strings.CaptureWindow),
            new(CaptureModeRegion, Strings.CaptureRegion)
        ];
        QualityOptions =
        [
            new("Alta", Strings.QualityHigh),
            new("Média", Strings.QualityMedium),
            new("Baixa", Strings.QualityLow)
        ];
        AudioSourceOptions =
        [
            new("none", Strings.AudioSourceNone),
            new("system", Strings.AudioSourceSystem),
            new("microphone", Strings.AudioSourceMicrophone),
            new("both", Strings.AudioSourceBoth)
        ];
        EncoderAccelerationOptions =
        [
            new(nameof(VideoEncoderAcceleration.Auto), Strings.EncoderAccelerationAuto),
            new(nameof(VideoEncoderAcceleration.PreferHardware), Strings.EncoderAccelerationHardware),
            new(nameof(VideoEncoderAcceleration.SoftwareOnly), Strings.EncoderAccelerationSoftware)
        ];
        PositionOptions =
        [
            new(nameof(OverlayPosition.BottomRight), Strings.PositionBottomRight),
            new(nameof(OverlayPosition.BottomLeft), Strings.PositionBottomLeft),
            new(nameof(OverlayPosition.TopRight), Strings.PositionTopRight),
            new(nameof(OverlayPosition.TopLeft), Strings.PositionTopLeft)
        ];

        Diagnostics = new ObservableCollection<DiagnosticItem>();
        Schedules = new ObservableCollection<ScheduledRecordingItem>();
        DisplayOptions = new ObservableCollection<DisplayTargetOption>();
        MicrophoneDevices = new ObservableCollection<AudioInputDevice>();
        Diagnostics.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasDiagnostics));
            if (ClearDiagnosticsCommand is RelayCommand command) command.RaiseCanExecuteChanged();
        };
        Schedules.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSchedules));

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
        ClearDiagnosticsCommand = new RelayCommand(ClearDiagnostics, () => Diagnostics.Count > 0);

        _recordingService.StatusChanged += (_, status) => InvokeOnUiThread(() => UpdateRecordingStatus(status));
        _durationTimer.Tick += (_, _) => UpdateLiveDuration();
        _diagnostics.Reported += (_, diagnostic) => InvokeOnUiThread(() => AddDiagnostic(diagnostic));
        _scheduler.StatusChanged += (_, status) => InvokeOnUiThread(() => ApplyScheduledStatus(status));
    }

    /// <summary>Disparado após salvar configurações para reaplicar os atalhos globais.</summary>
    public event EventHandler? HotkeysChanged;
    public event EventHandler<NotificationMessage>? NotificationRequested;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public ObservableCollection<DiagnosticItem> Diagnostics { get; }
    public ObservableCollection<ScheduledRecordingItem> Schedules { get; }
    public ObservableCollection<DisplayTargetOption> DisplayOptions { get; }
    public ObservableCollection<AudioInputDevice> MicrophoneDevices { get; }
    public bool HasDiagnostics => Diagnostics.Count > 0;
    public bool HasSchedules => Schedules.Count > 0;

    public IReadOnlyList<LocalizedOption> PresetOptions { get; }
    public IReadOnlyList<LocalizedOption> CaptureModeOptions { get; }
    public IReadOnlyList<LocalizedOption> QualityOptions { get; }
    public IReadOnlyList<LocalizedOption> AudioSourceOptions { get; }
    public IReadOnlyList<LocalizedOption> EncoderAccelerationOptions { get; }
    public IReadOnlyList<LocalizedOption> VideoCodecOptions => EncoderCapabilities.HevcSupported
        ? [new(nameof(VideoCodec.H264), "H.264"), new(nameof(VideoCodec.Hevc), "HEVC")]
        : [new(nameof(VideoCodec.H264), "H.264")];
    public IReadOnlyList<LocalizedOption> PositionOptions { get; }

    public VideoEncoderCapabilities EncoderCapabilities
    {
        get
        {
            try { return _encoderCapabilitiesService?.GetCapabilities() ?? VideoEncoderCapabilities.SafeDefault; }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Não foi possível detectar capacidades do encoder");
                return VideoEncoderCapabilities.SafeDefault;
            }
        }
    }

    public string PerformanceSummary
    {
        get
        {
            var telemetry = _recordingTelemetry?.CurrentTelemetry ?? RecordingPerformanceTelemetry.Empty;
            var encoder = telemetry.UsingHardwareEncoder ? Strings.EncoderHardwareActive : Strings.EncoderSoftwareActive;
            return string.Format(Strings.PerformanceSummary, encoder, telemetry.DroppedFrames, telemetry.AverageEncodeTime.TotalMilliseconds);
        }
    }

    public string RecordingDuration { get => _recordingDuration; private set => SetField(ref _recordingDuration, value); }
    public RecordingState CurrentRecordingState => _recordingService.Status.State;

    public string CaptureMode
    {
        get => _captureMode;
        set
        {
            var normalized = NormalizeCaptureMode(value);
            if (SetField(ref _captureMode, normalized))
            {
                _settings.LastCaptureMode = normalized;
                _selectedTarget = normalized == CaptureModeFullScreen
                    ? new CaptureTarget.Display(_selectedDisplayIndex)
                    : null;
                PersistSelectedTarget();
            }
        }
    }

    public int SelectedDisplayIndex
    {
        get => _selectedDisplayIndex;
        set
        {
            if (!SetField(ref _selectedDisplayIndex, Math.Max(0, value))) return;
            if (CaptureMode == CaptureModeFullScreen)
            {
                _selectedTarget = new CaptureTarget.Display(_selectedDisplayIndex);
                PersistSelectedTarget();
            }
        }
    }

    /// <summary>Atualiza a lista de monitores obtida pela camada WPF/Windows.</summary>
    public void SetAvailableDisplays(IEnumerable<DisplayTargetOption> displays)
    {
        var snapshot = displays.OrderBy(display => display.Index).ToArray();
        DisplayOptions.Clear();
        foreach (var display in snapshot) DisplayOptions.Add(display);
        if (DisplayOptions.Count == 0) return;
        if (DisplayOptions.All(display => display.Index != SelectedDisplayIndex))
            SelectedDisplayIndex = DisplayOptions[0].Index;
    }

    public void SelectTarget(CaptureTarget target)
    {
        CaptureMode = target switch
        {
            CaptureTarget.Window => CaptureModeWindow,
            CaptureTarget.Region => CaptureModeRegion,
            _ => CaptureModeFullScreen
        };
        _selectedTarget = target;
        if (target is CaptureTarget.Display display)
            _selectedDisplayIndex = display.DisplayIndex;
        PersistSelectedTarget();
        OnPropertyChanged(nameof(SelectedDisplayIndex));
        StatusMessage = target switch
        {
            CaptureTarget.Window window => string.Format(Strings.WindowSelected, window.Title),
            CaptureTarget.Region => Strings.RegionSelected,
            _ => Strings.FullScreenSelected
        };
    }

    /// <summary>Restaura o alvo salvo que não depende de um handle de janela vivo.</summary>
    private void RestorePersistedNonWindowTarget()
    {
        var saved = _settings.LastCaptureTarget;
        if (saved.TryCreateNonWindowTarget(out var target) && target is not null)
        {
            _selectedTarget = target;
            _captureMode = target switch
            {
                CaptureTarget.Region => CaptureModeRegion,
                _ => CaptureModeFullScreen
            };
            if (target is CaptureTarget.Display display) _selectedDisplayIndex = display.DisplayIndex;
            OnPropertyChanged(nameof(CaptureMode));
            OnPropertyChanged(nameof(SelectedDisplayIndex));
            return;
        }

        if (string.Equals(saved.Kind, "window", StringComparison.OrdinalIgnoreCase))
        {
            // O MainWindow revalida o handle/título contra as janelas visíveis.
            _captureMode = CaptureModeWindow;
            OnPropertyChanged(nameof(CaptureMode));
            return;
        }

        SelectTarget(new CaptureTarget.Display());
    }

    /// <summary>Recebe do code-behind uma janela revalidada depois que o desktop estiver disponível.</summary>
    internal bool RestorePersistedWindowTarget(IEnumerable<CaptureTarget.Window> windows)
    {
        if (!string.Equals(_settings.LastCaptureTarget.Kind, "window", StringComparison.OrdinalIgnoreCase)) return false;
        var saved = _settings.LastCaptureTarget;
        var candidates = windows.ToArray();
        var window = candidates.FirstOrDefault(candidate => candidate.Handle.ToInt64() == saved.WindowHandle);
        if (window is null && !string.IsNullOrWhiteSpace(saved.WindowTitle))
        {
            var titleMatches = candidates
                .Where(candidate => string.Equals(candidate.Title, saved.WindowTitle, StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            if (titleMatches.Length == 1) window = titleMatches[0];
        }
        if (window is not null)
        {
            SelectTarget(window);
            return true;
        }

        SelectTarget(new CaptureTarget.Display(_selectedDisplayIndex));
        StatusMessage = Strings.FullScreenSelected;
        return false;
    }

    public string StatusMessage { get => _statusMessage; private set => SetField(ref _statusMessage, value); }
    public string DiagnosticsText { get => _diagnosticsText; private set => SetField(ref _diagnosticsText, value); }
    public DateTime ScheduledStart { get => _scheduledStart; set => SetField(ref _scheduledStart, value); }
    public int ScheduledMinutes { get => _scheduledMinutes; set => SetField(ref _scheduledMinutes, value); }

    public string OutputDirectory
    {
        get => _outputDirectory;
        set
        {
            if (!SetField(ref _outputDirectory, value)) return;
            ((RelayCommand)OpenOutputDirectoryCommand).RaiseCanExecuteChanged();
            ValidateOutputDirectory();
        }
    }

    public string SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (!SetField(ref _selectedPreset, value)) return;
            _settings.SelectedPreset = value;
            if (_applyingPreset) return;
            if (value == CustomPresetId) return;
            if (RecordingPresets.All.FirstOrDefault(preset => preset.Id == value) is { } selected)
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
            ValidateFramesPerSecond();
            if (_applyingPreset) return;
            RecalculateBitrate();
            MarkCustomPreset();
        }
    }

    public bool SystemAudioEnabled
    {
        get => _settings.SystemAudioEnabled;
        set { if (_settings.SystemAudioEnabled != value) { _settings.SystemAudioEnabled = value; OnPropertyChanged(); NotifyAudioSettingsChanged(); } }
    }

    public bool MicrophoneEnabled
    {
        get => _settings.MicrophoneEnabled;
        set { if (_settings.MicrophoneEnabled != value) { _settings.MicrophoneEnabled = value; OnPropertyChanged(); NotifyAudioSettingsChanged(); } }
    }

    public string? SelectedMicrophoneDeviceId
    {
        get => _settings.MicrophoneDeviceId;
        set { if (_settings.MicrophoneDeviceId != value) { _settings.MicrophoneDeviceId = value; OnPropertyChanged(); } }
    }

    public float SystemAudioGain
    {
        get => _settings.SystemAudioGain;
        set { if (_settings.SystemAudioGain != value) { _settings.SystemAudioGain = Math.Clamp(value, 0, 4); OnPropertyChanged(); } }
    }

    public float MicrophoneGain
    {
        get => _settings.MicrophoneGain;
        set { if (_settings.MicrophoneGain != value) { _settings.MicrophoneGain = Math.Clamp(value, 0, 4); OnPropertyChanged(); } }
    }

    public bool SystemAudioMuted
    {
        get => _settings.SystemAudioMuted;
        set { if (_settings.SystemAudioMuted != value) { _settings.SystemAudioMuted = value; OnPropertyChanged(); } }
    }

    public bool MicrophoneMuted
    {
        get => _settings.MicrophoneMuted;
        set { if (_settings.MicrophoneMuted != value) { _settings.MicrophoneMuted = value; OnPropertyChanged(); } }
    }

    public string AudioSourceMode
    {
        get => (SystemAudioEnabled, MicrophoneEnabled) switch
        {
            (true, true) => "both",
            (true, false) => "system",
            (false, true) => "microphone",
            _ => "none"
        };
        set
        {
            var (system, microphone) = value switch
            {
                "system" => (true, false),
                "microphone" => (false, true),
                "both" => (true, true),
                _ => (false, false)
            };
            var changed = _settings.SystemAudioEnabled != system || _settings.MicrophoneEnabled != microphone;
            _settings.SystemAudioEnabled = system;
            _settings.MicrophoneEnabled = microphone;
            if (changed)
            {
                OnPropertyChanged(nameof(SystemAudioEnabled));
                OnPropertyChanged(nameof(MicrophoneEnabled));
                NotifyAudioSettingsChanged();
            }
        }
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

    public bool CloseToTray
    {
        get => _settings.CloseToTray;
        set { if (_settings.CloseToTray != value) { _settings.CloseToTray = value; OnPropertyChanged(); } }
    }

    public string AudioStatusText => AudioSourceMode == "none" ? Strings.AudioOff : Strings.AudioOn;

    private void NotifyAudioSettingsChanged()
    {
        OnPropertyChanged(nameof(AudioSourceMode));
        OnPropertyChanged(nameof(AudioStatusText));
    }

    public string OutputFormat
    {
        get => _settings.RecordingContainer.ToString().ToUpperInvariant();
        set
        {
            if (!Enum.TryParse<RecordingContainer>(value, true, out var container)) return;
            if (_settings.RecordingContainer == container) return;
            _settings.RecordingContainer = container;
            OnPropertyChanged();
            NotifyFormatSummary();
            ValidateResolution();
        }
    }

    public string SelectedVideoCodec
    {
        get => _settings.VideoCodec.ToString();
        set
        {
            if (!Enum.TryParse<VideoCodec>(value, true, out var codec)) return;
            if (codec == VideoCodec.Hevc && !EncoderCapabilities.HevcSupported) return;
            if (_settings.VideoCodec == codec) return;
            _settings.VideoCodec = codec;
            OnPropertyChanged();
            NotifyFormatSummary();
            ValidateResolution();
            MarkCustomPreset();
        }
    }

    public string EncoderAcceleration
    {
        get => _settings.EncoderAcceleration.ToString();
        set
        {
            if (!Enum.TryParse<VideoEncoderAcceleration>(value, true, out var acceleration) || _settings.EncoderAcceleration == acceleration) return;
            _settings.EncoderAcceleration = acceleration;
            OnPropertyChanged();
        }
    }

    /// <summary>Resumo do cabeçalho: contêiner, codec, resolução e FPS efetivos.</summary>
    public string FormatSummary =>
        $"{OutputFormat} • {CodecSummary} • {_settings.OutputWidth}×{_settings.OutputHeight} • {_settings.FramesPerSecond} {Strings.FpsLabel}";

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
            _settings.OutputWidth = width;
            _settings.OutputHeight = height;
            OnPropertyChanged();
            NotifyFormatSummary();
            ValidateResolution();
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
            ValidateVideoBitrate();
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
        set { if (_settings.ToggleRecordingHotkey != value) { _settings.ToggleRecordingHotkey = value; OnPropertyChanged(); ValidateHotkeys(); } }
    }

    public string TogglePauseHotkey
    {
        get => _settings.TogglePauseHotkey;
        set { if (_settings.TogglePauseHotkey != value) { _settings.TogglePauseHotkey = value; OnPropertyChanged(); ValidateHotkeys(); } }
    }

    public string ScreenshotHotkey
    {
        get => _settings.ScreenshotHotkey;
        set { if (_settings.ScreenshotHotkey != value) { _settings.ScreenshotHotkey = value; OnPropertyChanged(); ValidateHotkeys(); } }
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
    public ICommand ClearDiagnosticsCommand { get; }

    // --- Validação visível (INotifyDataErrorInfo) ---

    public bool HasErrors => _errors.Values.Any(messages => messages.Count > 0);
    public string ValidationSummaryText => HasErrors ? Strings.ValidationSummary : string.Empty;

    public IEnumerable GetErrors(string? propertyName) =>
        propertyName is not null && _errors.TryGetValue(propertyName, out var messages) ? messages : Array.Empty<string>();

    private void SetErrors(string propertyName, IEnumerable<string> messages)
    {
        var list = messages.Where(message => !string.IsNullOrWhiteSpace(message)).Distinct().ToList();
        if (list.Count == 0)
        {
            if (!_errors.Remove(propertyName)) return;
        }
        else
        {
            _errors[propertyName] = list;
        }

        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(ValidationSummaryText));
    }

    private void ValidateAll()
    {
        ValidateOutputDirectory();
        ValidateFramesPerSecond();
        ValidateVideoBitrate();
        ValidateResolution();
        ValidateHotkeys();
    }

    private void ValidateOutputDirectory()
    {
        var directory = OutputDirectory?.Trim() ?? string.Empty;
        if (directory.Length == 0)
        {
            SetErrors(nameof(OutputDirectory), [Strings.ValidationOutputDirectoryEmpty]);
            return;
        }
        if (!Path.IsPathFullyQualified(directory))
        {
            SetErrors(nameof(OutputDirectory), [Strings.ValidationOutputDirectoryNotAbsolute]);
            return;
        }
        if (!Directory.Exists(directory))
        {
            SetErrors(nameof(OutputDirectory), [Strings.ValidationOutputDirectoryMissing]);
            return;
        }
        SetErrors(nameof(OutputDirectory), []);
    }

    private void ValidateFramesPerSecond()
    {
        SetErrors(nameof(FramesPerSecond), _settings.FramesPerSecond is < SettingsValidator.MinimumFps or > SettingsValidator.MaximumFps
            ? [string.Format(Strings.ValidationFpsOutOfRange, SettingsValidator.MinimumFps, SettingsValidator.MaximumFps)]
            : []);
    }

    private void ValidateVideoBitrate()
    {
        SetErrors(nameof(VideoBitrate), _settings.VideoBitrate is < SettingsValidator.MinimumBitrate or > SettingsValidator.MaximumBitrate
            ? [string.Format(Strings.ValidationBitrateOutOfRange, SettingsValidator.MinimumBitrate, SettingsValidator.MaximumBitrate)]
            : []);
    }

    private void ValidateResolution()
    {
        string[] errors;
        if (_settings.OutputWidth <= 0 || _settings.OutputHeight <= 0)
            errors = [Strings.ValidationResolutionOutOfRange];
        else if (_settings.RecordingContainer != RecordingContainer.Gif &&
                 (_settings.OutputWidth % 2 != 0 || _settings.OutputHeight % 2 != 0))
            errors = [Strings.ValidationResolutionMustBeEven];
        else if (BuildProfile().IsValid)
            errors = [];
        else
            errors = [Strings.ValidationProfileNotSupported];
        SetErrors(nameof(OutputResolution), errors);
    }

    private void ValidateHotkeys()
    {
        var bindings = new[]
        {
            (Property: nameof(ToggleRecordingHotkey), Value: ToggleRecordingHotkey),
            (Property: nameof(TogglePauseHotkey), Value: TogglePauseHotkey),
            (Property: nameof(ScreenshotHotkey), Value: ScreenshotHotkey)
        };

        var parsed = new List<HotkeyGesture>(bindings.Length);
        foreach (var binding in bindings)
        {
            if (!HotkeyGesture.TryParse(binding.Value, out var gesture))
            {
                SetErrors(binding.Property, [string.Format(Strings.ValidationHotkeyInvalid, binding.Value)]);
                continue;
            }
            if (parsed.Contains(gesture))
                SetErrors(binding.Property, [Strings.ValidationHotkeyDuplicate]);
            else
                SetErrors(binding.Property, []);
            parsed.Add(gesture);
        }
    }

    // --- Ciclo de vida ---

    public async Task LoadAsync()
    {
        _settings = await _settingsService.LoadAsync();
        await _scheduler.InitializeAsync();
        foreach (var rule in _scheduler.ScheduledRules)
        {
            _scheduleRules[rule.Id] = rule;
            var status = _scheduler.Scheduled.FirstOrDefault(item => item.Id == rule.Id)
                ?? new ScheduledRecordingStatus(rule.Id, ScheduledRecordingState.Pending);
            ApplyScheduledStatus(status);
        }
        TryCreateOutputDirectory();
        OutputDirectory = _settings.OutputDirectory;
        _selectedPreset = ResolveSelectedPreset();
        OnPropertyChanged(nameof(FramesPerSecond));
        OnPropertyChanged(nameof(SystemAudioEnabled));
        OnPropertyChanged(nameof(MicrophoneEnabled));
        OnPropertyChanged(nameof(SelectedMicrophoneDeviceId));
        OnPropertyChanged(nameof(SystemAudioGain));
        OnPropertyChanged(nameof(MicrophoneGain));
        OnPropertyChanged(nameof(SystemAudioMuted));
        OnPropertyChanged(nameof(MicrophoneMuted));
        OnPropertyChanged(nameof(AudioSourceMode));
        OnPropertyChanged(nameof(AudioStatusText));
        RefreshMicrophoneDevices();
        OnPropertyChanged(nameof(ShowCursor));
        OnPropertyChanged(nameof(ShowClickEffects));
        OnPropertyChanged(nameof(CloseToTray));
        OnPropertyChanged(nameof(OutputFormat));
        if (_settings.VideoCodec == VideoCodec.Hevc && !EncoderCapabilities.HevcSupported)
        {
            _settings.VideoCodec = VideoCodec.H264;
            _diagnostics.Report(DiagnosticSeverity.Warning, "encoder.hevc_unavailable", Strings.HevcUnavailable);
        }
        OnPropertyChanged(nameof(VideoCodecOptions));
        OnPropertyChanged(nameof(SelectedVideoCodec));
        OnPropertyChanged(nameof(EncoderAcceleration));
        OnPropertyChanged(nameof(PerformanceSummary));
        OnPropertyChanged(nameof(OutputResolution));
        OnPropertyChanged(nameof(FormatSummary));
        OnPropertyChanged(nameof(VideoBitrate));
        OnPropertyChanged(nameof(Quality));
        OnPropertyChanged(nameof(SelectedPreset));
        OnPropertyChanged(nameof(ToggleRecordingHotkey));
        OnPropertyChanged(nameof(TogglePauseHotkey));
        OnPropertyChanged(nameof(ScreenshotHotkey));
        OnPropertyChanged(nameof(WebcamEnabled));
        OnPropertyChanged(nameof(WebcamDeviceIndex));
        OnPropertyChanged(nameof(WebcamScale));
        OnPropertyChanged(nameof(WatermarkText));
        OnPropertyChanged(nameof(WatermarkImagePath));
        OnPropertyChanged(nameof(WatermarkOpacity));
        OnPropertyChanged(nameof(WatermarkScale));
        OnPropertyChanged(nameof(WatermarkPosition));
        OnPropertyChanged(nameof(WebcamPosition));
        OnPropertyChanged(nameof(WebcamOpacity));
        _selectedDisplayIndex = Math.Max(0, _settings.LastCaptureTarget.DisplayIndex);
        RestorePersistedNonWindowTarget();

        foreach (var diagnostic in _diagnostics.Current) AddDiagnostic(diagnostic);

        ValidateAll();
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
            _settings.SelectedPreset = preset.Id;
            OnPropertyChanged(nameof(OutputResolution));
            OnPropertyChanged(nameof(FramesPerSecond));
            OnPropertyChanged(nameof(Quality));
            OnPropertyChanged(nameof(VideoBitrate));
            NotifyFormatSummary();
            ValidateFramesPerSecond();
            ValidateVideoBitrate();
            ValidateResolution();
        }
        finally { _applyingPreset = false; }
    }

    internal async Task<bool> SaveSettingsAsync()
    {
        SyncSettingsFromView();
        TryCreateOutputDirectory();
        ValidateAll();
        if (HasErrors)
        {
            StatusMessage = Strings.ValidationSummary;
            _diagnostics.Report(DiagnosticSeverity.Warning, "settings.validation", FirstValidationError());
            return false;
        }

        var issues = SettingsValidator.Validate(_settings);
        if (issues.Count > 0)
        {
            var message = LocalizeValidationIssue(issues[0]);
            StatusMessage = message;
            _diagnostics.Report(DiagnosticSeverity.Warning, $"settings.{issues[0].Code}", message);
            return false;
        }

        try
        {
            await _settingsService.SaveAsync(_settings);
            StatusMessage = Strings.SettingsSaved;
            ((RelayCommand)OpenOutputDirectoryCommand).RaiseCanExecuteChanged();
            HotkeysChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Falha ao salvar configurações");
            StatusMessage = Strings.SettingsSaveFailed;
            return false;
        }
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
        PersistSelectedTarget();
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
                var reason = LocalizeAvailability(availability);
                StatusMessage = reason;
                _diagnostics.Report(DiagnosticSeverity.Warning, "recording.unavailable", reason);
                return;
            }

            await _recordingService.StartAsync(CreateRequest());
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Falha ao iniciar a gravação");
            StatusMessage = string.Format(Strings.RecordingFailed, exception.Message);
        }
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
        ValidateAll();
        if (!HasErrors) return true;
        var message = FirstValidationError();
        StatusMessage = Strings.ValidationSummary;
        _diagnostics.Report(DiagnosticSeverity.Warning, "settings.validation", message);
        return false;
    }

    internal async Task ScheduleRecordingAsync()
    {
        try
        {
            SyncSettingsFromView();
            if (!EnsureSettingsValid()) return;

            if (ScheduledMinutes <= 0 || ScheduledStart <= DateTime.Now)
            {
                StatusMessage = Strings.ScheduleMissingTimes;
                return;
            }

            if (!await SaveSettingsAsync()) return;

            var start = new DateTimeOffset(ScheduledStart);
            var schedule = new ScheduledRecording(Guid.NewGuid(), start, start.AddMinutes(ScheduledMinutes), CreateRequest());
            _scheduleRules[schedule.Id] = schedule;
            try
            {
                await _scheduler.ScheduleAsync(schedule);
                StatusMessage = string.Format(Strings.ScheduleCreated, schedule.StartsAt.LocalDateTime);
            }
            catch (InvalidOperationException)
            {
                _scheduleRules.Remove(schedule.Id);
                StatusMessage = Strings.ScheduleConflict;
                _diagnostics.Report(DiagnosticSeverity.Warning, "schedule.conflict", Strings.ScheduleConflict);
            }
            catch (ArgumentException)
            {
                _scheduleRules.Remove(schedule.Id);
                StatusMessage = Strings.ScheduleMissingTimes;
            }
            catch (Exception exception)
            {
                _scheduleRules.Remove(schedule.Id);
                _logger.LogWarning(exception, "Não foi possível agendar gravação");
                StatusMessage = string.Format(Strings.ScheduleCreateFailed, exception.Message);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Não foi possível agendar gravação");
            StatusMessage = string.Format(Strings.ScheduleCreateFailed, exception.Message);
        }
    }

    internal async Task CancelScheduleAsync(Guid scheduleId)
    {
        try
        {
            await _scheduler.CancelAsync(scheduleId);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Falha ao cancelar agendamento {ScheduleId}", scheduleId);
        }
        finally
        {
            _scheduleRules.Remove(scheduleId);
            var item = Schedules.FirstOrDefault(schedule => schedule.Id == scheduleId);
            if (item is not null) Schedules.Remove(item);
            StatusMessage = Strings.ScheduleCancelled;
        }
    }

    private void ApplyScheduledStatus(ScheduledRecordingStatus status)
    {
        var existing = Schedules.FirstOrDefault(schedule => schedule.Id == status.Id);
        if (existing is not null)
        {
            existing.Update(status);
            return;
        }

        if (!_scheduleRules.TryGetValue(status.Id, out var rule)) return;
        Schedules.Add(new ScheduledRecordingItem(rule, status, id => _ = CancelScheduleAsync(id)));
    }

    private void ClearDiagnostics()
    {
        Diagnostics.Clear();
        DiagnosticsText = string.Empty;
    }

    private void AddDiagnostic(RecordingDiagnostic diagnostic)
    {
        if (Diagnostics.Any(item => item.Timestamp == diagnostic.Timestamp && item.Code == diagnostic.Code && item.Message == diagnostic.Message))
            return;

        Diagnostics.Insert(0, new DiagnosticItem(diagnostic.Timestamp, diagnostic.Severity, diagnostic.Code, diagnostic.Message));
        while (Diagnostics.Count > DiagnosticLimit) Diagnostics.RemoveAt(Diagnostics.Count - 1);
        DiagnosticsText = $"{diagnostic.Timestamp:HH:mm:ss} — {diagnostic.Message}";
    }

    private RecordingProfile BuildProfile() => new(
        _settings.RecordingContainer,
        _settings.RecordingContainer == RecordingContainer.Gif ? VideoCodec.Gif : _settings.VideoCodec,
        _settings.VideoBitrate,
        _settings.FramesPerSecond,
        _settings.OutputWidth,
        _settings.OutputHeight);

    private RecordingRequest CreateRequest()
    {
        var profile = BuildProfile();
        if (!profile.IsValid) throw new InvalidOperationException(Strings.ValidationProfileNotSupported);
        var target = BuildCaptureTarget();
        return new RecordingRequest(
            BuildAvailableOutputPath(profile.FileExtension),
            FramesPerSecond,
            SystemAudioEnabled,
            target,
            profile,
            new RecordingOverlaySettings(_settings.Watermark, _settings.Webcam, BuildClickEffects()),
            _settings.ShowCursor,
            BuildAudioMix(),
            _settings.MicrophoneDeviceId,
            _settings.EncoderAcceleration);
    }

    internal CaptureTarget BuildCaptureTarget() => CaptureMode switch
    {
        CaptureModeFullScreen => new CaptureTarget.Display(_selectedDisplayIndex),
        CaptureModeWindow when _selectedTarget is CaptureTarget.Window window => window,
        CaptureModeRegion when _selectedTarget is CaptureTarget.Region region => region,
        _ => throw new InvalidOperationException(Strings.ValidationProfileNotSupported)
    };

    private string BuildAvailableOutputPath(string extension)
    {
        var stem = $"TelaRec_{DateTime.Now:yyyyMMdd_HHmmss}";
        var candidate = Path.Combine(OutputDirectory, stem + extension);
        for (var suffix = 1; File.Exists(candidate); suffix++)
            candidate = Path.Combine(OutputDirectory, $"{stem}-{suffix}{extension}");
        return candidate;
    }

    private AudioMixSettings BuildAudioMix() => new(
        _settings.SystemAudioEnabled,
        _settings.MicrophoneEnabled,
        _settings.SystemAudioGain,
        _settings.MicrophoneGain,
        _settings.SystemAudioMuted,
        _settings.MicrophoneMuted);

    private void RefreshMicrophoneDevices()
    {
        MicrophoneDevices.Clear();
        if (_microphoneCapture is null) return;
        try
        {
            foreach (var device in _microphoneCapture.GetDevices()) MicrophoneDevices.Add(device);
            if (!string.IsNullOrWhiteSpace(_settings.MicrophoneDeviceId) &&
                MicrophoneDevices.All(device => device.Id != _settings.MicrophoneDeviceId))
                _settings.MicrophoneDeviceId = null;
            OnPropertyChanged(nameof(SelectedMicrophoneDeviceId));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Não foi possível enumerar microfones");
            _diagnostics.Report(DiagnosticSeverity.Warning, "microphone.enumeration", "Não foi possível enumerar os microfones disponíveis.");
        }
    }

    private void PersistSelectedTarget()
    {
        var target = _selectedTarget ?? new CaptureTarget.Display(_selectedDisplayIndex);
        _settings.LastCaptureTarget = CaptureTargetSettings.FromTarget(target);
        _settings.LastCaptureMode = target switch
        {
            CaptureTarget.Window => CaptureModeWindow,
            CaptureTarget.Region => CaptureModeRegion,
            _ => CaptureModeFullScreen
        };
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
            _durationTimer.Stop();
            _durationClock.Reset();
            _durationBeforeCurrent = TimeSpan.Zero;
            _audioWarning = null;
            _lastNotifiedState = RecordingState.Starting;
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
            _durationClock.Reset();
            _durationTimer.Stop();
            RecordingDuration = _durationBeforeCurrent.ToString(@"hh\:mm\:ss");
        }

        StatusMessage = status.State switch
        {
            RecordingState.Recording => string.Format(Strings.RecordingInProgress, status.Elapsed.ToString(@"mm\:ss")),
            RecordingState.Paused => Strings.RecordingPaused,
            RecordingState.Completed => Strings.RecordingComplete,
            RecordingState.Failed => string.Format(Strings.RecordingFailed, status.ErrorMessage),
            _ => Strings.Ready
        };

        NotifyState(status);
        if (status.State == RecordingState.Recording) UpdateLiveDuration();
        foreach (var command in new[] { StartRecordingCommand, PauseRecordingCommand, ResumeRecordingCommand, StopRecordingCommand, ToggleRecordingCommand, TogglePauseCommand })
            ((RelayCommand)command).RaiseCanExecuteChanged();
    }

    private void NotifyState(RecordingStatus status)
    {
        if (_lastNotifiedState == status.State) return;
        _lastNotifiedState = status.State;
        NotificationMessage? message = status.State switch
        {
            RecordingState.Recording => new(Strings.NotificationStartedTitle, Strings.NotificationStartedBody, false),
            RecordingState.Paused => new(Strings.NotificationPausedTitle, Strings.NotificationPausedBody, false),
            RecordingState.Completed => new(Strings.NotificationCompletedTitle, Strings.NotificationCompletedBody, false),
            RecordingState.Failed => new(Strings.NotificationErrorTitle,
                string.Format(Strings.NotificationErrorBody, status.ErrorMessage ?? string.Empty), true),
            _ => null
        };
        if (message is not null) NotificationRequested?.Invoke(this, message);
    }

    private void UpdateLiveDuration()
    {
        RecordingDuration = (_durationBeforeCurrent + _durationClock.Elapsed).ToString(@"hh\:mm\:ss");
        StatusMessage = _audioWarning ?? string.Format(Strings.RecordingInProgress, RecordingDuration);
        OnPropertyChanged(nameof(PerformanceSummary));
    }

    private void RecalculateBitrate()
    {
        var suggested = QualityBitrateCalculator.Suggest(_settings.Quality, _settings.OutputWidth, _settings.OutputHeight, _settings.FramesPerSecond);
        if (_settings.VideoBitrate == suggested) return;
        _settings.VideoBitrate = suggested;
        OnPropertyChanged(nameof(VideoBitrate));
        ValidateVideoBitrate();
    }

    private void MarkCustomPreset()
    {
        if (_applyingPreset) return;
        _settings.SelectedPreset = CustomPresetId;
        if (_selectedPreset == CustomPresetId) return;
        _selectedPreset = CustomPresetId;
        OnPropertyChanged(nameof(SelectedPreset));
    }

    private string ResolveSelectedPreset()
    {
        var configured = NormalizePresetId(_settings.SelectedPreset);
        if (RecordingPresets.All.FirstOrDefault(preset =>
                preset.Id == configured &&
                preset.Width == _settings.OutputWidth &&
                preset.Height == _settings.OutputHeight &&
                preset.FramesPerSecond == _settings.FramesPerSecond &&
                string.Equals(preset.Quality, _settings.Quality, StringComparison.OrdinalIgnoreCase)) is { } match)
            return match.Id;
        return CustomPresetId;
    }

    private static string NormalizePresetId(string? value) => value switch
    {
        "Tutorial 1080p" => "tutorial-1080p",
        "Leve 720p" => "light-720p",
        "Alta qualidade 60 FPS" => "high-quality-60",
        "Personalizado" or "Custom" => CustomPresetId,
        _ => value ?? CustomPresetId
    };

    private static string NormalizeCaptureMode(string? value) => value switch
    {
        "Tela inteira" or "Full screen" => CaptureModeFullScreen,
        "Janela selecionada" or "Selected window" or CaptureModeWindow => CaptureModeWindow,
        "Região personalizada" or "Custom region" or CaptureModeRegion => CaptureModeRegion,
        _ => CaptureModeFullScreen
    };

    private void TryCreateOutputDirectory()
    {
        var directory = _settings.OutputDirectory;
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory)) return;
        try { Directory.CreateDirectory(directory); }
        catch (Exception) { /* A validação visível informa o problema ao usuário. */ }
    }

    private string FirstValidationError() =>
        _errors.Values.FirstOrDefault(messages => messages.Count > 0)?.FirstOrDefault() ?? Strings.ValidationSummary;

    private static string LocalizeAvailability(RecordingAvailability availability) => availability.ReasonCode switch
    {
        RecordingUnavailableReason.CaptureUnsupported => Strings.CaptureUnsupported,
        RecordingUnavailableReason.DesktopLocked => string.IsNullOrWhiteSpace(availability.Reason) ? Strings.DesktopLocked : availability.Reason!,
        RecordingUnavailableReason.DeviceUnavailable => Strings.DeviceUnavailable,
        _ => string.IsNullOrWhiteSpace(availability.Reason) ? Strings.CaptureUnavailable : availability.Reason
    };

    private static string LocalizeValidationIssue(SettingsValidationIssue issue) => issue.Code switch
    {
        SettingsValidationCode.OutputDirectoryEmpty => Strings.ValidationOutputDirectoryEmpty,
        SettingsValidationCode.OutputDirectoryNotAbsolute => Strings.ValidationOutputDirectoryNotAbsolute,
        SettingsValidationCode.OutputDirectoryUnavailable => Strings.ValidationOutputDirectoryUnavailable,
        SettingsValidationCode.FramesPerSecondOutOfRange => string.Format(Strings.ValidationFpsOutOfRange, SettingsValidator.MinimumFps, SettingsValidator.MaximumFps),
        SettingsValidationCode.VideoBitrateOutOfRange => string.Format(Strings.ValidationBitrateOutOfRange, SettingsValidator.MinimumBitrate, SettingsValidator.MaximumBitrate),
        SettingsValidationCode.ResolutionOutOfRange => Strings.ValidationResolutionOutOfRange,
        SettingsValidationCode.ResolutionMustBeEven => Strings.ValidationResolutionMustBeEven,
        SettingsValidationCode.ProfileNotSupported => Strings.ValidationProfileNotSupported,
        _ => Strings.ValidationSummary
    };

    private void NotifyFormatSummary() => OnPropertyChanged(nameof(FormatSummary));

    private static void InvokeOnUiThread(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }
        dispatcher.BeginInvoke(action);
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
