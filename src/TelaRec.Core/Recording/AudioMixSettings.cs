namespace TelaRec.Core.Recording;
public sealed record AudioMixSettings(bool SystemEnabled, bool MicrophoneEnabled, float SystemGain = 1, float MicrophoneGain = 1, bool SystemMuted = false, bool MicrophoneMuted = false);
