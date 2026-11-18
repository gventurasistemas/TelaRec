using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Input;
using TelaRec.App.Wpf;
using TelaRec.Core.Capture;

namespace TelaRec.Tests;

public sealed class RegionOverlayWindowTests
{
    [Fact]
    public void Captured_mouse_routes_drag_and_release_to_the_selection_surface()
    {
        RunInSta(() =>
        {
            var screen = System.Windows.Forms.Screen.PrimaryScreen
                ?? throw new InvalidOperationException("Nenhum monitor disponível para o teste.");
            var overlay = new RegionOverlayWindow(screen, 1, 1) { Topmost = false, ShowActivated = false };
            try
            {
                overlay.Show();
                var surface = (UIElement)overlay.FindName("Surface");
                var pointer = Mouse.GetPosition(surface);
                var start = new Point(pointer.X < screen.Bounds.Width / 2 ? screen.Bounds.Width : 0, 0);
                overlay.BeginSelection(start, isOverResizeHandle: false);
                var captured = Assert.IsAssignableFrom<UIElement>(Mouse.Captured);
                var initialSelection = overlay.CurrentSelection;

                captured.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
                {
                    RoutedEvent = Mouse.MouseMoveEvent
                });

                Assert.NotEqual(initialSelection, overlay.CurrentSelection);

                captured.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                {
                    RoutedEvent = Mouse.MouseUpEvent
                });

                Assert.False(overlay.IsDrawing);
                Assert.Null(Mouse.Captured);
            }
            finally
            {
                overlay.Close();
            }
        });
    }

    [Fact]
    public void Existing_selection_accepts_a_new_drag()
    {
        RunInSta(() =>
        {
            var screen = System.Windows.Forms.Screen.PrimaryScreen
                ?? throw new InvalidOperationException("Nenhum monitor disponível para o teste.");
            var overlay = new RegionOverlayWindow(screen, 1, 1);

            overlay.UpdateSelectionFromDip(new Point(100, 120), new Point(600, 520));
            Assert.Equal(
                new CaptureTarget.Region(screen.Bounds.Left + 100, screen.Bounds.Top + 120, 500, 400),
                overlay.CurrentSelection);

            overlay.BeginSelection(new Point(200, 220), isOverResizeHandle: false);

            Assert.True(overlay.IsDrawing);
            Assert.Equal(
                new CaptureTarget.Region(screen.Bounds.Left + 200, screen.Bounds.Top + 220, 0, 0),
                overlay.CurrentSelection);
            overlay.Close();
        });
    }

    private static void RunInSta(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "O teste WPF não terminou no tempo esperado.");
        failure?.Throw();
    }
}
