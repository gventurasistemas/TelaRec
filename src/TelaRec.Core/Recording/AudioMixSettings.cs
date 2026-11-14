namespace TelaRec.Core.Recording;
public sealed record AudioMixSettings(bool SystemEnabled, bool MicrophoneEnabled, float SystemGain = 1, float MicrophoneGain = 1, bool SystemMuted = false, bool MicrophoneMuted = false)
{
    public bool HasEnabledSource => SystemEnabled || MicrophoneEnabled;
    public static AudioMixSettings SystemOnly { get; } = new(true, false);
    public static AudioMixSettings None { get; } = new(false, false);
}
