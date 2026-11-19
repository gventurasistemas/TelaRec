namespace TelaRec.Core.Recording;

/// <summary>
/// Traduz os níveis de qualidade em um bitrate sugerido, considerando resolução e FPS.
/// A edição manual do bitrate continua permitida; esta função apenas sugere o valor.
/// </summary>
public static class QualityBitrateCalculator
{
    public const int MinimumBitrate = 500_000;
    public const int MaximumBitrate = 80_000_000;

    public static int Suggest(string? quality, int width, int height, int framesPerSecond)
    {
        if (width <= 0 || height <= 0) return MinimumBitrate;
        var fps = Math.Clamp(framesPerSecond, 1, 120);
        var bitsPerPixel = Normalize(quality) switch
        {
            "muito alta" or "very high" => 0.18,
            "alta" or "high" => 0.12,
            "media" or "medium" => 0.07,
            "baixa" or "low" => 0.04,
            "economica" or "economy" => 0.025,
            _ => 0.07
        };

        var raw = width * (double)height * fps * bitsPerPixel;
        var clamped = Math.Clamp(raw, MinimumBitrate, MaximumBitrate);
        // Arredonda para 100 kbps para manter os valores amigáveis.
        return (int)(Math.Round(clamped / 100_000d) * 100_000d);
    }

    private static string Normalize(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant()
            .Replace('á', 'a').Replace('à', 'a').Replace('ã', 'a')
            .Replace('é', 'e').Replace('ê', 'e')
            .Replace('í', 'i')
            .Replace('ó', 'o').Replace('ô', 'o').Replace('õ', 'o')
            .Replace('ú', 'u').Replace('ç', 'c');
}
