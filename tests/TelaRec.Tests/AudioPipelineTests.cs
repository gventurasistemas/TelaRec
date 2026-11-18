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

    [Fact]
    public void Timestamped_mixer_overlaps_sources_with_different_callback_block_sizes()
    {
        var mixer = new TimestampedAudioMixer(new AudioMixSettings(true, true));
        var output = new List<AudioFrame>();
        for (var milliseconds = 0; milliseconds < 1_000; milliseconds += 10)
        {
            if (milliseconds % 20 == 0)
                mixer.Add(AudioSource.System, Frame(10_000, 20, TimeSpan.FromMilliseconds(milliseconds)));
            mixer.Add(AudioSource.Microphone, Frame(5_000, 10, TimeSpan.FromMilliseconds(milliseconds)));
            output.AddRange(mixer.DrainReady());
        }
        output.AddRange(mixer.DrainReady(final: true));

        Assert.Equal(48_000, output.Sum(frame => frame.Pcm16.Length / 4));
        Assert.All(output, frame => Assert.Equal((short)15_000, BitConverter.ToInt16(frame.Pcm16, 0)));
        Assert.Equal(TimeSpan.Zero, output[0].Timestamp);
        Assert.Equal(TimeSpan.FromMilliseconds(990), output[^1].Timestamp);
    }

    [Fact]
    public void Timestamped_mixer_waits_for_both_device_clocks_before_releasing_audio()
    {
        var mixer = new TimestampedAudioMixer(new AudioMixSettings(true, true));
        var packets = new List<(AudioSource Source, AudioFrame Frame)>();
        for (var milliseconds = 0; milliseconds < 1_000; milliseconds += 20)
            packets.Add((AudioSource.System, Frame(10_000, 20, TimeSpan.FromMilliseconds(milliseconds))));
        for (var milliseconds = 0; milliseconds < 1_000; milliseconds += 10)
            packets.Add((AudioSource.Microphone, Frame(5_000, 10, TimeSpan.FromMilliseconds(350 + milliseconds))));

        var output = new List<AudioFrame>();
        foreach (var packet in packets.OrderBy(packet => packet.Frame.Timestamp))
        {
            mixer.Add(packet.Source, packet.Frame);
            output.AddRange(mixer.DrainReady());
        }
        output.AddRange(mixer.DrainReady(final: true));

        Assert.Equal(TimeSpan.FromMilliseconds(350), mixer.InitialSourceSkew);
        Assert.Equal(48_000, output.Sum(frame => frame.Pcm16.Length / 4));
        Assert.True(mixer.MixedBuckets >= 99);
        Assert.True(mixer.SingleSourceBuckets <= 1);
    }

    private static AudioFrame Frame(short value, int milliseconds, TimeSpan timestamp)
    {
        var samples = Enumerable.Repeat(value, 48 * milliseconds * 2).ToArray();
        return new AudioFrame(Samples(samples), 48_000, 2, timestamp);
    }

    private static byte[] Samples(params short[] values)
    {
        var bytes = new byte[values.Length * sizeof(short)];
        for (var index = 0; index < values.Length; index++) BitConverter.TryWriteBytes(bytes.AsSpan(index * 2, 2), values[index]);
        return bytes;
    }
}
