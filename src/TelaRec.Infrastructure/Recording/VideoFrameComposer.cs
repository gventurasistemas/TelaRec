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
    private ClickEffectSettings _clickSettings = ClickEffectSettings.None;
    private CaptureFrameBounds? _clickBounds;
    private readonly List<ClickEvent> _clicks = [];

    public void Configure(RecordingOverlaySettings settings)
    {
        lock (_sync)
        {
            _settings = settings;
            _webcam = null;
            _clickSettings = settings.EffectiveClickEffects;
            _clicks.Clear();
            (_watermark, _watermarkWidth, _watermarkHeight) = BuildWatermark(settings.Watermark);
        }
    }
    public void SetClickEffectContext(CaptureFrameBounds? source)
    {
        lock (_sync)
        {
            _clickBounds = source is { IsValid: true } valid ? valid : null;
            if (_clickBounds is null) _clicks.Clear();
        }
    }
    public void AddClickEffect(ClickEvent click)
    {
        lock (_sync)
        {
            if (!_clickSettings.Enabled) return;
            if (_clicks.Count >= 32) _clicks.RemoveAt(0);
            _clicks.Add(click);
        }
    }
    public void UpdateWebcamFrame(VideoFrame frame) { lock (_sync) _webcam = frame; }
    public VideoFrame Compose(VideoFrame frame)
    {
        lock (_sync)
        {
            var hasWatermark = _watermark is not null;
            var hasWebcam = _settings.Webcam.Enabled && _webcam is not null;
            var hasClickEffects = HasActiveClickEffects(frame.Timestamp);
            if (!hasWatermark && !hasWebcam && !hasClickEffects) return frame;

            var output = frame.Pixels.ToArray();
            if (hasWatermark) Blend(output, frame.Width, frame.Height, frame.Stride, _watermark!, _watermarkWidth, _watermarkHeight, _settings.Watermark.Position, _settings.Watermark.Opacity, _settings.Watermark.Scale);
            if (_settings.Webcam.Enabled && _webcam is { } webcam)
                Blend(output, frame.Width, frame.Height, frame.Stride, webcam.Pixels, webcam.Width, webcam.Height, _settings.Webcam.Position, _settings.Webcam.Opacity, _settings.Webcam.Scale);
            if (_clickSettings.Enabled && _clickBounds is { } bounds)
                RenderClickEffects(output, frame.Width, frame.Height, frame.Stride, frame.Timestamp, bounds);
            return frame with { Pixels = output };
        }
    }
    private bool HasActiveClickEffects(TimeSpan timestamp)
    {
        if (!_clickSettings.Enabled || _clickBounds is not { IsValid: true }) return false;
        var duration = TimeSpan.FromSeconds(Math.Clamp(_clickSettings.DurationSeconds, 0.05, 5.0));
        _clicks.RemoveAll(click => timestamp - click.Timestamp > duration);
        return _clicks.Any(click => timestamp >= click.Timestamp && timestamp - click.Timestamp <= duration);
    }
    private void RenderClickEffects(byte[] destination, int width, int height, int stride, TimeSpan timestamp, CaptureFrameBounds bounds)
    {
        var duration = TimeSpan.FromSeconds(Math.Clamp(_clickSettings.DurationSeconds, 0.05, 5.0));
        if (_clicks.Count == 0) return;

        _clickSettings.TryGetColor(out var red, out var green, out var blue);
        var baseRadius = Math.Clamp(_clickSettings.Radius, 2, 400);
        var opacity = Math.Clamp(_clickSettings.Opacity, 0, 1);
        foreach (var click in _clicks)
        {
            var age = timestamp - click.Timestamp;
            if (age < TimeSpan.Zero || age > duration) continue;
            if (!ClickEffectProjection.TryProject(click, bounds, width, height, out var centerX, out var centerY)) continue;

            var progress = age.TotalSeconds / duration.TotalSeconds;
            var radius = baseRadius * (0.45 + 0.85 * progress);
            var thickness = Math.Max(2.0, radius * 0.18);
            var alpha = opacity * (1 - progress);
            BlendRing(destination, width, height, stride, centerX, centerY, radius, thickness, blue, green, red, alpha);
            BlendDisc(destination, width, height, stride, centerX, centerY, Math.Max(1, baseRadius * 0.14), blue, green, red, alpha * 0.8);
        }
    }
    private static void BlendRing(byte[] destination, int width, int height, int stride, int centerX, int centerY, double radius, double thickness, byte blue, byte green, byte red, double alpha)
    {
        var inner = Math.Max(0, radius - thickness);
        var outerSquared = radius * radius;
        var innerSquared = inner * inner;
        var extent = (int)Math.Ceiling(radius);
        var minX = Math.Max(0, centerX - extent); var maxX = Math.Min(width - 1, centerX + extent);
        var minY = Math.Max(0, centerY - extent); var maxY = Math.Min(height - 1, centerY + extent);
        for (var y = minY; y <= maxY; y++)
        for (var x = minX; x <= maxX; x++)
        {
            var dx = x - centerX; var dy = y - centerY;
            var distanceSquared = dx * dx + dy * dy;
            if (distanceSquared > outerSquared || distanceSquared < innerSquared) continue;
            BlendPixel(destination, y * stride + x * 4, blue, green, red, alpha);
        }
    }
    private static void BlendDisc(byte[] destination, int width, int height, int stride, int centerX, int centerY, double radius, byte blue, byte green, byte red, double alpha)
    {
        var radiusSquared = radius * radius;
        var extent = (int)Math.Ceiling(radius);
        var minX = Math.Max(0, centerX - extent); var maxX = Math.Min(width - 1, centerX + extent);
        var minY = Math.Max(0, centerY - extent); var maxY = Math.Min(height - 1, centerY + extent);
        for (var y = minY; y <= maxY; y++)
        for (var x = minX; x <= maxX; x++)
        {
            var dx = x - centerX; var dy = y - centerY;
            if (dx * dx + dy * dy > radiusSquared) continue;
            BlendPixel(destination, y * stride + x * 4, blue, green, red, alpha);
        }
    }
    private static void BlendPixel(byte[] destination, int index, byte blue, byte green, byte red, double alpha)
    {
        alpha = Math.Clamp(alpha, 0, 1);
        if (alpha <= 0) return;
        destination[index] = (byte)Math.Round(blue * alpha + destination[index] * (1 - alpha));
        destination[index + 1] = (byte)Math.Round(green * alpha + destination[index + 1] * (1 - alpha));
        destination[index + 2] = (byte)Math.Round(red * alpha + destination[index + 2] * (1 - alpha));
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
