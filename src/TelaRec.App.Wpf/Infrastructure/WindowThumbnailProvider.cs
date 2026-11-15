using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using ImageSource = System.Windows.Media.ImageSource;

namespace TelaRec.App.Wpf.Infrastructure;

internal static class WindowThumbnailProvider
{
    private const int ExtendedFrameBounds = 9;
    private const uint RenderFullContent = 2;
    private const int MaximumCaptureDimension = 8192;

    public static ImageSource? Capture(nint handle, int thumbnailWidth, int thumbnailHeight)
    {
        if (!TryGetWindowBounds(handle, out var bounds)) return null;

        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        if (width <= 0 || height <= 0 || width > MaximumCaptureDimension || height > MaximumCaptureDimension)
            return null;

        try
        {
            using var captured = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(captured))
            {
                graphics.Clear(Color.FromArgb(16, 19, 26));
                var deviceContext = graphics.GetHdc();
                bool rendered;
                try
                {
                    rendered = PrintWindow(handle, deviceContext, RenderFullContent);
                }
                finally
                {
                    graphics.ReleaseHdc(deviceContext);
                }

                if (!rendered && !IsIconic(handle))
                {
                    graphics.CopyFromScreen(
                        bounds.Left,
                        bounds.Top,
                        0,
                        0,
                        new System.Drawing.Size(width, height),
                        CopyPixelOperation.SourceCopy);
                }
            }

            using var thumbnail = ScaleToFit(captured, thumbnailWidth, thumbnailHeight);
            var bitmapHandle = thumbnail.GetHbitmap(Color.FromArgb(0));
            try
            {
                var source = Imaging.CreateBitmapSourceFromHBitmap(
                    bitmapHandle,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                DeleteObject(bitmapHandle);
            }
        }
        catch (ExternalException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static Bitmap ScaleToFit(Bitmap source, int width, int height)
    {
        var thumbnail = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(thumbnail);
        graphics.Clear(Color.FromArgb(16, 19, 26));
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.HighQuality;

        var scale = Math.Min(width / (double)source.Width, height / (double)source.Height);
        var scaledWidth = Math.Max(1, (int)Math.Round(source.Width * scale));
        var scaledHeight = Math.Max(1, (int)Math.Round(source.Height * scale));
        var x = (width - scaledWidth) / 2;
        var y = (height - scaledHeight) / 2;
        graphics.DrawImage(source, x, y, scaledWidth, scaledHeight);
        return thumbnail;
    }

    private static bool TryGetWindowBounds(nint handle, out NativeRect bounds)
    {
        if (DwmGetWindowAttribute(handle, ExtendedFrameBounds, out bounds, Marshal.SizeOf<NativeRect>()) == 0)
            return true;

        return GetWindowRect(handle, out bounds);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint handle, int attribute, out NativeRect value, int valueSize);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint handle, out NativeRect rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(nint handle, nint deviceContext, uint flags);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint handle);
}
