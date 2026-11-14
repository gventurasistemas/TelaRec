namespace TelaRec.Core.Recording;

public sealed record RecordingRequest(string OutputPath, int FramesPerSecond = 30, bool SystemAudioEnabled = false, Capture.CaptureTarget? Target = null, RecordingProfile? Profile = null, RecordingOverlaySettings? Overlays = null, bool ShowCursor = true, AudioMixSettings? AudioMix = null, string? MicrophoneDeviceId = null, VideoEncoderAcceleration EncoderAcceleration = VideoEncoderAcceleration.Auto)
{
    public RecordingProfile EffectiveProfile => Profile ?? RecordingProfile.Mvp with { FramesPerSecond = FramesPerSecond };
    public RecordingOverlaySettings EffectiveOverlays => Overlays ?? RecordingOverlaySettings.None;
    public AudioMixSettings EffectiveAudioMix => AudioMix ?? (SystemAudioEnabled ? AudioMixSettings.SystemOnly : AudioMixSettings.None);
}
