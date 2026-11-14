using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TelaRec.Core.Capture;

namespace TelaRec.App.Wpf;

public partial class RegionOverlayWindow : Window
{
    private Point _start;
    private NativePoint _physicalStart;
    public CaptureTarget.Region? Result { get; private set; }
    public RegionOverlayWindow(System.Windows.Forms.Screen screen, double dpiScaleX, double dpiScaleY)
    {
        InitializeComponent();
        Left = screen.Bounds.Left / dpiScaleX;
        Top = screen.Bounds.Top / dpiScaleY;
        Width = screen.Bounds.Width / dpiScaleX;
        Height = screen.Bounds.Height / dpiScaleY;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; Close(); } };
    }
    private void Start(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(this);
        GetPhysicalCursorPos(out _physicalStart);
        Selection.Visibility = Visibility.Visible;
        Canvas.SetLeft(Selection, _start.X); Canvas.SetTop(Selection, _start.Y);
        CaptureMouse();
    }
    private void Move(object sender, MouseEventArgs e)
    {
        if (!IsMouseCaptured) return;
        var point = e.GetPosition(this);
        Canvas.SetLeft(Selection, Math.Min(_start.X, point.X));
        Canvas.SetTop(Selection, Math.Min(_start.Y, point.Y));
        Selection.Width = Math.Abs(point.X - _start.X);
        Selection.Height = Math.Abs(point.Y - _start.Y);
    }
    private void End(object sender, MouseButtonEventArgs e)
    {
        ReleaseMouseCapture();
        GetPhysicalCursorPos(out var end);
        Result = new CaptureTarget.Region(Math.Min(_physicalStart.X, end.X), Math.Min(_physicalStart.Y, end.Y),
            Math.Abs(end.X - _physicalStart.X), Math.Abs(end.Y - _physicalStart.Y));
        DialogResult = Result.IsValid;
        Close();
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }
    [DllImport("user32.dll")]
    private static extern bool GetPhysicalCursorPos(out NativePoint point);
}
