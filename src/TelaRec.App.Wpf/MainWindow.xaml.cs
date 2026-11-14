using System.Windows;
using System.Windows.Forms;
using TelaRec.App.Wpf.Infrastructure;
using TelaRec.App.Wpf.ViewModels;
using TelaRec.Core.Capture;

namespace TelaRec.App.Wpf;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private NotifyIcon? _tray;
    private GlobalHotkeyService? _hotkeys;

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
        ContentRendered += async (_, _) => await _viewModel.LoadAsync();
        SourceInitialized += (_, _) => SetupShellIntegration();
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) Hide(); };
        Closed += (_, _) => { _tray?.Dispose(); _hotkeys?.Dispose(); };
    }

    private void SetupShellIntegration()
    {
        _hotkeys = new GlobalHotkeyService(this);
        _hotkeys.Register(1, 0x0003, (uint)Keys.R); // Ctrl+Alt+R
        _hotkeys.Register(2, 0x0003, (uint)Keys.P); // Ctrl+Alt+P
        _hotkeys.Pressed += id => Dispatcher.Invoke(() =>
        {
            var command = id == 1 ? _viewModel.StartRecordingCommand : _viewModel.PauseRecordingCommand;
            if (command.CanExecute(null)) command.Execute(null);
        });
        var menu = new ContextMenuStrip();
        menu.Items.Add("Gravar", null, (_, _) => _viewModel.StartRecordingCommand.Execute(null));
        menu.Items.Add("Pausar", null, (_, _) => _viewModel.PauseRecordingCommand.Execute(null));
        menu.Items.Add("Parar", null, (_, _) => _viewModel.StopRecordingCommand.Execute(null));
        menu.Items.Add("Sair", null, (_, _) => Close());
        _tray = new NotifyIcon { Text = "TelaRec", Visible = true, ContextMenuStrip = menu, Icon = System.Drawing.SystemIcons.Application };
        _tray.DoubleClick += (_, _) => { Show(); WindowState = WindowState.Normal; Activate(); };
    }
    private void SelectRegion(object sender, RoutedEventArgs e)
    {
        var screen = Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        var overlay = new RegionOverlayWindow(screen, dpi.DpiScaleX, dpi.DpiScaleY) { Owner = this };
        Hide();
        overlay.ShowDialog();
        Show(); Activate();
        if (overlay.Result is not null) _viewModel.SelectTarget(overlay.Result);
    }
    private void SelectWindow(object sender, RoutedEventArgs e)
    {
        var windows = WindowEnumerator.GetVisibleWindows().Where(w => w.Handle != new System.Windows.Interop.WindowInteropHelper(this).Handle).ToList();
        var list = new System.Windows.Controls.ListBox { ItemsSource = windows, DisplayMemberPath = nameof(DesktopWindow.Title), Margin = new Thickness(12) };
        var panel = new System.Windows.Controls.DockPanel();
        var button = new System.Windows.Controls.Button { Content = "Usar janela", Margin = new Thickness(12), Padding = new Thickness(12, 6, 12, 6), HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        System.Windows.Controls.DockPanel.SetDock(button, System.Windows.Controls.Dock.Bottom);
        panel.Children.Add(button); panel.Children.Add(list);
        var dialog = new Window { Title = "Selecionar janela", Owner = this, Width = 480, Height = 380, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel };
        button.Click += (_, _) => { if (list.SelectedItem is not null) dialog.DialogResult = true; };
        list.MouseDoubleClick += (_, _) => { if (list.SelectedItem is not null) dialog.DialogResult = true; };
        if (dialog.ShowDialog() == true && list.SelectedItem is DesktopWindow selected)
            _viewModel.SelectTarget(new CaptureTarget.Window(selected.Handle, selected.Title));
    }
}
