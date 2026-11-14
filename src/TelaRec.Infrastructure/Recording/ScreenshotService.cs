using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.Extensions.Logging;
using TelaRec.Core.Contracts;

namespace TelaRec.Infrastructure.Recording;

/// <summary>
/// Captura PNG dos monitores. O padrão é o monitor sob o cursor, pois reflete o
/// que o usuário está vendo; todos os monitores podem ser capturados de uma vez.
/// </summary>
public sealed class ScreenshotService(ILogger<ScreenshotService> logger) : IScreenshotService
{
    public Task<IReadOnlyList<string>> CapturePngAsync(
        string outputDirectory,
        ScreenshotScope scope = ScreenshotScope.MonitorUnderCursor,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new ArgumentException("Pasta de saída não informada.", nameof(outputDirectory));

        return Task.Run<IReadOnlyList<string>>(() => Capture(outputDirectory, scope, cancellationToken), cancellationToken);
    }

    private IReadOnlyList<string> Capture(string outputDirectory, ScreenshotScope scope, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        cancellationToken.ThrowIfCancellationRequested();

        var screens = scope == ScreenshotScope.AllMonitors
            ? System.Windows.Forms.Screen.AllScreens
            : [System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position)];

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var results = new List<string>(screens.Length);
        for (var index = 0; index < screens.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bounds = screens[index].Bounds;
            using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);

            var suffix = screens.Length > 1 ? $"_{index + 1}" : string.Empty;
            var path = BuildUniquePath(outputDirectory, $"TelaRec_{timestamp}{suffix}.png");
            bitmap.Save(path, ImageFormat.Png);
            results.Add(path);
            logger.LogInformation("Screenshot salvo em {Path} ({Width}x{Height})", path, bounds.Width, bounds.Height);
        }

        return results;
    }

    private static string BuildUniquePath(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path)) return path;

        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var attempt = 1; ; attempt++)
        {
            path = Path.Combine(directory, $"{name}-{attempt}{extension}");
            if (!File.Exists(path)) return path;
        }
    }
}
