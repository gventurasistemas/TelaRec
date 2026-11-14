using System.Drawing;
using System.Drawing.Imaging;
using TelaRec.Core.Contracts;
namespace TelaRec.Infrastructure.Recording;
public sealed class ScreenshotService : IScreenshotService
{
    public Task<string> CapturePngAsync(string outputDirectory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);
        var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? throw new InvalidOperationException("Monitor principal indisponível.");
        var path = Path.Combine(outputDirectory, $"TelaRec_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        bitmap.Save(path, ImageFormat.Png);
        return Task.FromResult(path);
    }
}
