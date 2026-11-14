using System.Globalization;

namespace TelaRec.Core.Recording;

/// <summary>Configuração visual dos efeitos de clique desenhados antes do encoder.</summary>
public sealed record ClickEffectSettings(
    bool Enabled = false,
    double Radius = 28,
    double DurationSeconds = 0.6,
    string Color = "#FF3B30",
    double Opacity = 0.65)
{
    public static ClickEffectSettings None { get; } = new();

    public bool TryGetColor(out byte red, out byte green, out byte blue)
    {
        red = green = blue = 0;
        var value = (Color ?? string.Empty).Trim().TrimStart('#');
        if (value.Length == 8) value = value[2..];
        if (value.Length != 6 ||
            !byte.TryParse(value.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out red) ||
            !byte.TryParse(value.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out green) ||
            !byte.TryParse(value.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out blue))
        {
            red = 255; green = 59; blue = 48;
            return false;
        }

        return true;
    }
}

/// <summary>Clique físico na tela em coordenadas de pixel físico e relógio monotônico.</summary>
public readonly record struct ClickEvent(int ScreenX, int ScreenY, TimeSpan Timestamp);

/// <summary>Área capturada em coordenadas físicas da tela (origem absoluta do desktop virtual).</summary>
public readonly record struct CaptureFrameBounds(int Left, int Top, int Width, int Height)
{
    public bool IsValid => Width > 0 && Height > 0;
}

/// <summary>Matemática pura de conversão entre coordenadas da tela e do quadro de saída.</summary>
public static class ClickEffectProjection
{
    public static bool TryProject(
        ClickEvent click,
        CaptureFrameBounds source,
        int outputWidth,
        int outputHeight,
        out int x,
        out int y)
    {
        x = y = 0;
        if (!source.IsValid || outputWidth <= 0 || outputHeight <= 0) return false;

        var relativeX = click.ScreenX - source.Left;
        var relativeY = click.ScreenY - source.Top;
        if (relativeX < 0 || relativeY < 0 || relativeX >= source.Width || relativeY >= source.Height) return false;

        x = (int)Math.Round(relativeX * (double)outputWidth / source.Width);
        y = (int)Math.Round(relativeY * (double)outputHeight / source.Height);
        return x >= 0 && y >= 0 && x < outputWidth && y < outputHeight;
    }
}
