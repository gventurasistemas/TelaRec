using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using TelaRec.Core.Contracts;
using TelaRec.Core.Recording;

namespace TelaRec.Infrastructure.Recording;

public sealed class VideoFrameComposer : IVideoFrameComposer, IDisposable
{
    private readonly object _sync = new();
    private RecordingOverlaySettings _settings = RecordingOverlaySettings.None;
    private byte[]? _watermark;
    private int _watermarkWidth, _watermarkHeight;
    private VideoFrame? _webcam;

    public void Configure(RecordingOverlaySettings settings)
    {
        lock (_sync)
        {
            _settings = settings;
            _webcam = null;
            (_watermark, _watermarkWidth, _watermarkHeight) = BuildWatermark(settings.Watermark);
        }
    }
    public void UpdateWebcamFrame(VideoFrame frame) { lock (_sync) _webcam = frame; }
    public VideoFrame Compose(VideoFrame frame)
    {
        lock (_sync)
        {
            var output = frame.Pixels.ToArray();
            if (_watermark is not null) Blend(output, frame.Width, frame.Height, frame.Stride, _watermark, _watermarkWidth, _watermarkHeight, _settings.Watermark.Position, _settings.Watermark.Opacity, _settings.Watermark.Scale);
            if (_settings.Webcam.Enabled && _webcam is { } webcam)
                Blend(output, frame.Width, frame.Height, frame.Stride, webcam.Pixels, webcam.Width, webcam.Height, _settings.Webcam.Position, _settings.Webcam.Opacity, _settings.Webcam.Scale);
            return frame with { Pixels = output };
        }
    }
    private static void Blend(byte[] destination, int width, int height, int stride, byte[] source, int sourceWidth, int sourceHeight, OverlayPosition position, double opacity, double scale)
    {
        opacity = Math.Clamp(opacity, 0, 1); scale = Math.Clamp(scale, 0.02, 1);
        var targetWidth = Math.Max(1, Math.Min(width, (int)Math.Round(sourceWidth * scale)));
        var targetHeight = Math.Max(1, Math.Min(height, (int)Math.Round(sourceHeight * scale)));
        var margin = Math.Max(8, Math.Min(width, height) / 100);
        var x = position is OverlayPosition.TopRight or OverlayPosition.BottomRight ? width - targetWidth - margin : margin;
        var y = position is OverlayPosition.BottomLeft or OverlayPosition.BottomRight ? height - targetHeight - margin : margin;
        x = Math.Clamp(x, 0, width - targetWidth); y = Math.Clamp(y, 0, height - targetHeight);
        for (var ty = 0; ty < targetHeight; ty++)
        for (var tx = 0; tx < targetWidth; tx++)
        {
            var sx = tx * sourceWidth / targetWidth; var sy = ty * sourceHeight / targetHeight;
            var sourceIndex = (sy * sourceWidth + sx) * 4;
            var alpha = source[sourceIndex + 3] / 255d * opacity;
            if (alpha <= 0) continue;
            var targetIndex = (y + ty) * stride + (x + tx) * 4;
            for (var channel = 0; channel < 3; channel++)
                destination[targetIndex + channel] = (byte)Math.Round(source[sourceIndex + channel] * alpha + destination[targetIndex + channel] * (1 - alpha));
        }
    }
    private static (byte[]? Pixels, int Width, int Height) BuildWatermark(WatermarkSettings settings)
    {
        if (settings.Kind == WatermarkKind.None) return (null, 0, 0);
        using var bitmap = settings.Kind == WatermarkKind.Image
            ? LoadImage(settings.ImagePath)
            : RenderText(settings.Text);
        if (bitmap is null) return (null, 0, 0);
        using var converted = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(converted)) graphics.DrawImageUnscaled(bitmap, 0, 0);
        var data = converted.LockBits(new Rectangle(0, 0, converted.Width, converted.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try { var pixels = new byte[Math.Abs(data.Stride) * data.Height]; Marshal.Copy(data.Scan0, pixels, 0, pixels.Length); return (pixels, converted.Width, converted.Height); }
        finally { converted.UnlockBits(data); }
    }
    private static Bitmap? LoadImage(string? path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? new Bitmap(path) : null;
    private static Bitmap RenderText(string? text)
    {
        var value = string.IsNullOrWhiteSpace(text) ? "TelaRec" : text;
        using var font = new Font(FontFamily.GenericSansSerif, 32, FontStyle.Bold, GraphicsUnit.Pixel);
        using var measure = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(measure);
        var size = graphics.MeasureString(value, font);
        var bitmap = new Bitmap((int)Math.Ceiling(size.Width) + 24, (int)Math.Ceiling(size.Height) + 20, PixelFormat.Format32bppArgb);
        using var target = Graphics.FromImage(bitmap);
        target.DrawString(value, font, Brushes.White, 12, 8);
        return bitmap;
    }
    public void Dispose() { lock (_sync) { _watermark = null; _webcam = null; } }
}
