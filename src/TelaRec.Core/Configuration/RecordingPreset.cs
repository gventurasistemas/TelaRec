namespace TelaRec.Core.Configuration;

/// <summary>
/// Preset de gravação. O <see cref="Id"/> é estável e persistido em settings.json;
/// o <see cref="ResourceKey"/> aponta para o nome localizado exibido na UI.
/// </summary>
public sealed record RecordingPreset(
    string Id,
    string ResourceKey,
    int Width,
    int Height,
    int FramesPerSecond,
    string Quality);

public static class RecordingPresets
{
    public static IReadOnlyList<RecordingPreset> All { get; } = new[]
    {
        new RecordingPreset("tutorial-1080p", "PresetTutorial1080p", 1920, 1080, 30, "Alta"),
        new RecordingPreset("light-720p", "PresetLight720p", 1280, 720, 30, "Média"),
        new RecordingPreset("high-quality-60", "PresetHighQuality60", 1920, 1080, 60, "Alta")
    };
}
