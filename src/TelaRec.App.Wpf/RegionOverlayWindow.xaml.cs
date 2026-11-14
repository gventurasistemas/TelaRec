using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using TelaRec.Core.Capture;

namespace TelaRec.App.Wpf;

/// <summary>Seleção física de região em um monitor, com ajuste posterior pelos cantos.</summary>
public partial class RegionOverlayWindow : Window
{
    private readonly System.Drawing.Rectangle _screenBounds;
    private readonly double _dpiScaleX;
    private readonly double _dpiScaleY;
    private Point _start;
    private bool _drawing;
    private CaptureTarget.Region? _selection;

    public CaptureTarget.Region? Result { get; private set; }

    public RegionOverlayWindow(System.Windows.Forms.Screen screen, double dpiScaleX, double dpiScaleY)
    {
        InitializeComponent();
        _screenBounds = screen.Bounds;
        _dpiScaleX = dpiScaleX;
        _dpiScaleY = dpiScaleY;
        Left = screen.Bounds.Left / dpiScaleX;
        Top = screen.Bounds.Top / dpiScaleY;
        Width = screen.Bounds.Width / dpiScaleX;
        Height = screen.Bounds.Height / dpiScaleY;
        KeyDown += OnKeyDown;
    }

    private void Start(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not Canvas) return;
        _start = e.GetPosition(Surface);
        _drawing = true;
        _selection = null;
        SetHandlesVisibility(Visibility.Collapsed);
        Selection.Visibility = Visibility.Visible;
        CaptureMouse();
        UpdateSelectionFromDip(_start, _start);
    }

    private void Move(object sender, MouseEventArgs e)
    {
        if (!_drawing || !IsMouseCaptured) return;
        UpdateSelectionFromDip(_start, e.GetPosition(Surface));
    }

    private void End(object sender, MouseButtonEventArgs e)
    {
        if (!_drawing) return;
        _drawing = false;
        ReleaseMouseCapture();
        UpdateSelectionFromDip(_start, e.GetPosition(Surface));
        SetHandlesVisibility(_selection?.IsValid == true ? Visibility.Visible : Visibility.Collapsed);
    }

    private void ResizeSelection(object sender, DragDeltaEventArgs e)
    {
        if (_selection is not { IsValid: true } current || sender is not Thumb thumb || thumb.Tag is not string edge) return;
        var dx = (int)Math.Round(e.HorizontalChange * _dpiScaleX);
        var dy = (int)Math.Round(e.VerticalChange * _dpiScaleY);
        var left = current.X;
        var top = current.Y;
        var right = current.X + current.Width;
        var bottom = current.Y + current.Height;

        if (edge.Contains("Left", StringComparison.Ordinal)) left = Math.Min(right - 2, left + dx);
        if (edge.Contains("Right", StringComparison.Ordinal)) right = Math.Max(left + 2, right + dx);
        if (edge.Contains("Top", StringComparison.Ordinal)) top = Math.Min(bottom - 2, top + dy);
        if (edge.Contains("Bottom", StringComparison.Ordinal)) bottom = Math.Max(top + 2, bottom + dy);

        left = Math.Clamp(left, _screenBounds.Left, _screenBounds.Right - 2);
        top = Math.Clamp(top, _screenBounds.Top, _screenBounds.Bottom - 2);
        right = Math.Clamp(right, left + 2, _screenBounds.Right);
        bottom = Math.Clamp(bottom, top + 2, _screenBounds.Bottom);
        _selection = new CaptureTarget.Region(left, top, right - left, bottom - top);
        RenderSelection();
    }

    private void UpdateSelectionFromDip(Point first, Point second)
    {
        var left = _screenBounds.Left + (int)Math.Round(Math.Min(first.X, second.X) * _dpiScaleX);
        var top = _screenBounds.Top + (int)Math.Round(Math.Min(first.Y, second.Y) * _dpiScaleY);
        var right = _screenBounds.Left + (int)Math.Round(Math.Max(first.X, second.X) * _dpiScaleX);
        var bottom = _screenBounds.Top + (int)Math.Round(Math.Max(first.Y, second.Y) * _dpiScaleY);
        left = Math.Clamp(left, _screenBounds.Left, _screenBounds.Right);
        top = Math.Clamp(top, _screenBounds.Top, _screenBounds.Bottom);
        right = Math.Clamp(right, _screenBounds.Left, _screenBounds.Right);
        bottom = Math.Clamp(bottom, _screenBounds.Top, _screenBounds.Bottom);
        _selection = new CaptureTarget.Region(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
        RenderSelection();
    }

    private void RenderSelection()
    {
        if (_selection is not { } region) return;
        var x = (region.X - _screenBounds.Left) / _dpiScaleX;
        var y = (region.Y - _screenBounds.Top) / _dpiScaleY;
        var width = region.Width / _dpiScaleX;
        var height = region.Height / _dpiScaleY;
        Canvas.SetLeft(Selection, x);
        Canvas.SetTop(Selection, y);
        Selection.Width = width;
        Selection.Height = height;
        PlaceHandle(TopLeft, x, y);
        PlaceHandle(TopRight, x + width, y);
        PlaceHandle(BottomLeft, x, y + height);
        PlaceHandle(BottomRight, x + width, y + height);
    }

    private static void PlaceHandle(FrameworkElement handle, double x, double y)
    {
        Canvas.SetLeft(handle, x - handle.Width / 2);
        Canvas.SetTop(handle, y - handle.Height / 2);
    }

    private void SetHandlesVisibility(Visibility visibility)
    {
        foreach (var handle in new FrameworkElement[] { TopLeft, TopRight, BottomLeft, BottomRight })
            handle.Visibility = visibility;
    }

    private void Apply(object sender, RoutedEventArgs e)
    {
        if (_selection?.IsValid != true) return;
        Result = _selection;
        DialogResult = true;
    }

    private void Cancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) DialogResult = false;
        if (e.Key == Key.Enter) Apply(this, new RoutedEventArgs());
    }
}
