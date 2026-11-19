using TelaRec.Core.Recording;

namespace TelaRec.Core.Configuration;

/// <summary>Configuração equilibrada de resolução, fluidez e espaço em disco para gravações novas.</summary>
public sealed record RecordingQualityProfile(string Quality, int Width, int Height, int FramesPerSecond)
{
    public int SuggestedBitrate => QualityBitrateCalculator.Suggest(Quality, Width, Height, FramesPerSecond);
}

public static class RecordingQualityProfiles
{
    public static IReadOnlyList<RecordingQualityProfile> All { get; } =
    [
        new("Muito Alta", 1920, 1080, 60),
        new("Alta", 1920, 1080, 60),
        new("Média", 1920, 1080, 30),
        new("Baixa", 1280, 720, 30),
        new("Econômica", 1280, 720, 24)
    ];

    public static bool TryGet(string? quality, out RecordingQualityProfile profile)
    {
        profile = All.FirstOrDefault(candidate => string.Equals(candidate.Quality, quality, StringComparison.OrdinalIgnoreCase))!;
        return profile is not null;
    }
}
