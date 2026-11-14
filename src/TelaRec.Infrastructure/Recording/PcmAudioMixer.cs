using TelaRec.Core.Recording;

namespace TelaRec.Infrastructure.Recording;

/// <summary>Mixagem PCM16 intercalada com ganho, mute e saturação contra clipping.</summary>
public static class PcmAudioMixer
{
    public static byte[] MixPcm16(byte[]? system, byte[]? microphone, AudioMixSettings settings)
    {
        var systemSamples = system ?? [];
        var microphoneSamples = microphone ?? [];
        var length = Math.Max(systemSamples.Length, microphoneSamples.Length);
        if ((length & 1) != 0) length--;
        var output = new byte[length];
        var systemGain = settings.SystemMuted ? 0 : Math.Clamp(settings.SystemGain, 0, 4);
        var microphoneGain = settings.MicrophoneMuted ? 0 : Math.Clamp(settings.MicrophoneGain, 0, 4);
        for (var index = 0; index < length; index += sizeof(short))
        {
            var systemValue = index + 1 < systemSamples.Length ? BitConverter.ToInt16(systemSamples, index) * systemGain : 0;
            var microphoneValue = index + 1 < microphoneSamples.Length ? BitConverter.ToInt16(microphoneSamples, index) * microphoneGain : 0;
            var mixed = (short)Math.Clamp(Math.Round(systemValue + microphoneValue), short.MinValue, short.MaxValue);
            BitConverter.TryWriteBytes(output.AsSpan(index, sizeof(short)), mixed);
        }
        return output;
    }

    public static byte[] ApplyGain(byte[] pcm16, float gain, bool muted)
        => MixPcm16(pcm16, null, new AudioMixSettings(true, false, gain, 0, muted));
}
