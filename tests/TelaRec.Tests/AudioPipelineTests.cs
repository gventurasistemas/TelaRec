using NAudio.Wave;
using TelaRec.Core.Recording;
using TelaRec.Infrastructure.Recording;

namespace TelaRec.Tests;

public sealed class AudioPipelineTests
{
    [Fact]
    public void Mixer_applies_gain_mute_and_clipping_protection()
    {
        var system = Samples(30_000, 30_000);
        var microphone = Samples(30_000, 30_000);
        var clipped = PcmAudioMixer.MixPcm16(system, microphone, new AudioMixSettings(true, true));
        var muted = PcmAudioMixer.MixPcm16(system, microphone, new AudioMixSettings(true, true, 1, 1, false, true));

        Assert.Equal(short.MaxValue, BitConverter.ToInt16(clipped, 0));
        Assert.Equal((short)30_000, BitConverter.ToInt16(muted, 0));
    }

    [Fact]
    public void Resampler_downmixes_six_channels_to_stereo()
    {
        var format = new WaveFormat(48_000, 16, 6);
        var buffer = Samples(10_000, -10_000, 0, 0, 10_000, -10_000);

        var (left, right) = Pcm16Resampler.ReadStereo(buffer, 0, format);

        Assert.True(left > 0.05f);
        Assert.True(right < -0.05f);
    }

    [Fact]
    public void Resampler_keeps_timestamps_continuous_between_blocks()
    {
        var format = new WaveFormat(48_000, 16, 1);
        var resampler = new Pcm16Resampler();
        var first = resampler.Convert(Samples(1000, 1000, 1000, 1000), 8, format, TimeSpan.TicksPerSecond)!;
        var second = resampler.Convert(Samples(1000, 1000, 1000, 1000), 8, format, TimeSpan.TicksPerSecond + 4 * TimeSpan.TicksPerSecond / 48_000)!;

        Assert.Equal(first.Timestamp.Ticks + first.Pcm16.Length / 4 * TimeSpan.TicksPerSecond / 48_000, second.Timestamp.Ticks);
    }

    [Fact]
    public void Timestamped_mixer_keeps_simulated_thirty_minute_drift_under_one_hundred_ms()
    {
        var mixer = new TimestampedAudioMixer(new AudioMixSettings(true, true));
        var pcm = Samples(1200, 1200);
        for (var second = 0; second < 30 * 60; second++)
        {
            mixer.Add(AudioSource.System, new AudioFrame(pcm, 48_000, 2, TimeSpan.FromSeconds(second)));
            mixer.Add(AudioSource.Microphone, new AudioFrame(pcm, 48_000, 2, TimeSpan.FromSeconds(second) + TimeSpan.FromMilliseconds(50)));
            _ = mixer.DrainReady().ToArray();
        }

        Assert.True(mixer.MaxSourceDrift < TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public void Timestamped_mixer_aligns_initial_device_latency_before_mixing()
    {
        var mixer = new TimestampedAudioMixer(new AudioMixSettings(true, true));
        mixer.Add(AudioSource.System, new AudioFrame(Samples(10_000, 10_000), 48_000, 2, TimeSpan.FromSeconds(10)));
        mixer.Add(AudioSource.Microphone, new AudioFrame(Samples(10_000, 10_000), 48_000, 2, TimeSpan.FromSeconds(10) + TimeSpan.FromMilliseconds(50)));

        var mixed = Assert.Single(mixer.DrainReady(final: true));
        Assert.Equal((short)20_000, BitConverter.ToInt16(mixed.Pcm16, 0));
    }

    private static byte[] Samples(params short[] values)
    {
        var bytes = new byte[values.Length * sizeof(short)];
        for (var index = 0; index < values.Length; index++) BitConverter.TryWriteBytes(bytes.AsSpan(index * 2, 2), values[index]);
        return bytes;
    }
}
