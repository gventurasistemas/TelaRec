namespace TelaRec.Core.Recording;

public sealed record RecordingRequest(string OutputPath, int FramesPerSecond = 30, bool SystemAudioEnabled = false, Capture.CaptureTarget? Target = null, RecordingProfile? Profile = null, RecordingOverlaySettings? Overlays = null)
{
    public RecordingProfile EffectiveProfile => Profile ?? RecordingProfile.Mvp with { FramesPerSecond = FramesPerSecond };
    public RecordingOverlaySettings EffectiveOverlays => Overlays ?? RecordingOverlaySettings.None;
}
