namespace TelaRec.Core.Configuration;
public sealed record RecordingPreset(string Name, int Width, int Height, int FramesPerSecond, string Quality);
public static class RecordingPresets
{
    public static IReadOnlyList<RecordingPreset> All { get; } = new[]
    {
        new RecordingPreset("Tutorial 1080p", 1920, 1080, 30, "Alta"),
        new RecordingPreset("Leve 720p", 1280, 720, 30, "Média"),
        new RecordingPreset("Alta qualidade 60 FPS", 1920, 1080, 60, "Alta")
    };
}
