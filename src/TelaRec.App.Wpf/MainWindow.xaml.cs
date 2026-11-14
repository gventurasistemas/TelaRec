using System.ComponentModel;
using System.Windows;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using TelaRec.App.Wpf.Infrastructure;
using TelaRec.App.Wpf.Resources;
using TelaRec.App.Wpf.ViewModels;
using TelaRec.Core.Capture;
using TelaRec.Core.Input;

namespace TelaRec.App.Wpf;

public partial class MainWindow : Window
{
    private const int HotkeyToggleRecording = 1;
    private const int HotkeyTogglePause = 2;
    private const int HotkeyScreenshot = 3;

    private readonly MainWindowViewModel _viewModel;
    private readonly ILogger<MainWindow> _logger;
    private readonly Dictionary<int, string> _appliedHotkeys = new();
    private NotifyIcon? _tray;
    private GlobalHotkeyService? _hotkeys;
    private bool _allowClose;
    private bool _trayHintShown;

    public MainWindow(MainWindowViewModel viewModel, ILogger<MainWindow> logger)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _logger = logger;
        DataContext = _viewModel;
        ContentRendered += async (_, _) =>
        {
            await _viewModel.LoadAsync();
            ApplyHotkeys();
        };
        SourceInitialized += (_, _) => SetupShellIntegration();
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) Hide(); };
        Closing += OnClosing;
        Closed += OnClosed;
        _viewModel.NotificationRequested += OnNotificationRequested;
    }

    private void SetupShellIntegration()
    {
        _hotkeys = new GlobalHotkeyService(this);
        _hotkeys.Pressed += OnHotkeyPressed;
        _viewModel.HotkeysChanged += OnHotkeysChanged;

        var menu = new ContextMenuStrip();
        menu.Items.Add(Strings.TrayShow, null, (_, _) => RestoreWindow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Strings.Start, null, (_, _) => _viewModel.ToggleRecordingCommand.Execute(null));
        menu.Items.Add(Strings.Pause, null, (_, _) => _viewModel.TogglePauseCommand.Execute(null));
        menu.Items.Add(Strings.Stop, null, (_, _) => _viewModel.StopRecordingCommand.Execute(null));
        menu.Items.Add(Strings.Screenshot, null, (_, _) => _viewModel.ScreenshotCommand.Execute(null));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Strings.TrayExit, null, (_, _) => ExitApplication());

        _tray = new NotifyIcon
        {
            Text = "TelaRec",
            Visible = true,
            ContextMenuStrip = menu,
            Icon = System.Drawing.SystemIcons.Application
        };
        _tray.DoubleClick += (_, _) => RestoreWindow();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose || !_viewModel.CloseToTray) return;

        e.Cancel = true;
        Hide();
        if (_trayHintShown) return;
        _trayHintShown = true;
        ShowBalloon(Strings.AppTitle, Strings.TrayBalloonHint, ToolTipIcon.Info);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.HotkeysChanged -= OnHotkeysChanged;
        _viewModel.NotificationRequested -= OnNotificationRequested;
        _tray?.Dispose();
        _tray = null;
        _hotkeys?.Dispose();
        _hotkeys = null;
    }

    private void ExitApplication()
    {
        _allowClose = true;
        Close();
        System.Windows.Application.Current?.Shutdown();
    }

    /// <summary>Mostra e ativa a janela, inclusive quando estava oculta na bandeja.</summary>
    private void RestoreWindow()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void OnNotificationRequested(object? sender, NotificationMessage message)
        => ShowBalloon(message.Title, message.Body, message.IsError ? ToolTipIcon.Error : ToolTipIcon.Info);

    private void ShowBalloon(string title, string body, ToolTipIcon icon)
    {
        try
        {
            _tray?.ShowBalloonTip(3000, title, body, icon);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Falha ao exibir notificação da bandeja");
        }
    }

    private void OnHotkeysChanged(object? sender, EventArgs e) => ApplyHotkeys();

    /// <summary>Re-registra os atalhos configurados sem reiniciar o aplicativo.</summary>
    private void ApplyHotkeys()
    {
        if (_hotkeys is null) return;
        RegisterHotkey(HotkeyToggleRecording, _viewModel.ToggleRecordingHotkey, Strings.ToggleRecordingHotkey);
        RegisterHotkey(HotkeyTogglePause, _viewModel.TogglePauseHotkey, Strings.TogglePauseHotkey);
        RegisterHotkey(HotkeyScreenshot, _viewModel.ScreenshotHotkey, Strings.ScreenshotHotkey);
    }

    private void RegisterHotkey(int id, string text, string label)
    {
        // Evita re-registrar a cada salvamento (inclusive durante a gravação),
        // que poderia abrir uma janela curta sem o atalho ativo.
        if (_appliedHotkeys.TryGetValue(id, out var applied) && applied == text && _hotkeys!.Registered.ContainsKey(id))
            return;

        _hotkeys!.Unregister(id);
        _appliedHotkeys.Remove(id);
        if (!HotkeyGesture.TryParse(text, out var gesture))
        {
            _viewModel.ReportHotkeyIssue($"{label}: {string.Format(Strings.HotkeyInvalid, text)}");
            return;
        }

        var result = _hotkeys.Register(id, gesture);
        if (!result.Success)
        {
            _viewModel.ReportHotkeyIssue($"{label}: {result.Error}");
            return;
        }

        _appliedHotkeys[id] = text;
        _logger.LogInformation("Atalho {Label} registrado como {Gesture}.", label, gesture);
    }

    private void OnHotkeyPressed(int id)
    {
        Dispatcher.Invoke(() =>
        {
            // Atalho acionado com a janela oculta na bandeja restaura a UI.
            if (!IsVisible || WindowState == WindowState.Minimized) RestoreWindow();

            var command = id switch
            {
                HotkeyToggleRecording => _viewModel.ToggleRecordingCommand,
                HotkeyTogglePause => _viewModel.TogglePauseCommand,
                HotkeyScreenshot => _viewModel.ScreenshotCommand,
                _ => null
            };
            _logger.LogInformation("Atalho global {Id} pressionado; estado {State}; pode executar {CanExecute}.",
                id, _viewModel.CurrentRecordingState, command?.CanExecute(null));
            if (command?.CanExecute(null) == true) command.Execute(null);
        });
    }

    private void SelectRegion(object sender, RoutedEventArgs e)
    {
        var screen = Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        var overlay = new RegionOverlayWindow(screen, dpi.DpiScaleX, dpi.DpiScaleY) { Owner = this };
        Hide();
        overlay.ShowDialog();
        Show();
        Activate();
        if (overlay.Result is not null) _viewModel.SelectTarget(overlay.Result);
    }

    private void SelectWindow(object sender, RoutedEventArgs e)
    {
        var windows = WindowEnumerator.GetVisibleWindows()
            .Where(window => window.Handle != new System.Windows.Interop.WindowInteropHelper(this).Handle)
            .ToList();
        var list = new System.Windows.Controls.ListBox
        {
            ItemsSource = windows,
            DisplayMemberPath = nameof(DesktopWindow.Title),
            Margin = new Thickness(12)
        };
        var panel = new System.Windows.Controls.DockPanel();
        var button = new System.Windows.Controls.Button
        {
            Content = Strings.UseWindow,
            Margin = new Thickness(12),
            Padding = new Thickness(12, 6, 12, 6),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        System.Windows.Controls.DockPanel.SetDock(button, System.Windows.Controls.Dock.Bottom);
        panel.Children.Add(button);
        panel.Children.Add(list);
        var dialog = new Window
        {
            Title = Strings.SelectWindow,
            Owner = this,
            Width = 480,
            Height = 380,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel
        };
        button.Click += (_, _) => { if (list.SelectedItem is not null) dialog.DialogResult = true; };
        list.MouseDoubleClick += (_, _) => { if (list.SelectedItem is not null) dialog.DialogResult = true; };
        if (dialog.ShowDialog() == true && list.SelectedItem is DesktopWindow selected)
            _viewModel.SelectTarget(new CaptureTarget.Window(selected.Handle, selected.Title));
    }
}
