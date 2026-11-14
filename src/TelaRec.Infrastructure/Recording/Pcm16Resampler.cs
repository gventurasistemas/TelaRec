using NAudio.Wave;
using TelaRec.Core.Recording;

namespace TelaRec.Infrastructure.Recording;

/// <summary>
/// Normaliza PCM/float para PCM16 estéreo a 48 kHz. A interpolação conserva o
/// último quadro da chamada anterior para não introduzir descontinuidade entre blocos.
/// </summary>
public sealed class Pcm16Resampler
{
    private WaveFormat? _format;
    private long _sourceFrames;
    private long _outputFrames;
    private double _nextSourcePosition;
    private float _previousLeft;
    private float _previousRight;
    private long _originTicks;

    public void Reset()
    {
        _format = null;
        _sourceFrames = _outputFrames = 0;
        _nextSourcePosition = 0;
        _previousLeft = _previousRight = 0;
        _originTicks = 0;
    }

    public AudioFrame? Convert(byte[] buffer, int bytesRecorded, WaveFormat format, long captureEndTicks)
    {
        if (bytesRecorded <= 0 || format.BlockAlign <= 0 || format.SampleRate <= 0 || format.Channels <= 0) return null;
        if (!SameFormat(_format, format))
        {
            Reset();
            _format = format;
        }

        var frames = bytesRecorded / format.BlockAlign;
        if (frames <= 0) return null;
        if (_originTicks == 0)
            _originTicks = captureEndTicks - (long)(frames * (double)TimeSpan.TicksPerSecond / format.SampleRate);

        var left = new float[frames];
        var right = new float[frames];
        for (var index = 0; index < frames; index++)
            (left[index], right[index]) = ReadStereo(buffer, index * format.BlockAlign, format);

        var firstOutput = _outputFrames;
        var step = format.SampleRate / 48_000d;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        while (_nextSourcePosition < _sourceFrames + frames)
        {
            var local = _nextSourcePosition - _sourceFrames;
            var index = (int)Math.Floor(local);
            if (index >= frames - 1) break;
            var fraction = (float)(local - index);
            var l0 = index < 0 ? _previousLeft : left[index];
            var r0 = index < 0 ? _previousRight : right[index];
            writer.Write(ToPcm16(l0 + (left[index + 1] - l0) * fraction));
            writer.Write(ToPcm16(r0 + (right[index + 1] - r0) * fraction));
            _nextSourcePosition += step;
            _outputFrames++;
        }

        _previousLeft = left[^1];
        _previousRight = right[^1];
        _sourceFrames += frames;
        return stream.Length == 0
            ? null
            : new AudioFrame(stream.ToArray(), 48_000, 2,
                TimeSpan.FromTicks(_originTicks + firstOutput * TimeSpan.TicksPerSecond / 48_000));
    }

    public static (float Left, float Right) ReadStereo(byte[] buffer, int frameOffset, WaveFormat format)
    {
        var bytesPerSample = Math.Max(1, format.BitsPerSample / 8);
        if (format.Channels == 1)
        {
            var mono = ReadSample(buffer, frameOffset, format);
            return (mono, mono);
        }

        var samples = new float[format.Channels];
        for (var channel = 0; channel < format.Channels; channel++)
            samples[channel] = ReadSample(buffer, frameOffset + channel * bytesPerSample, format);

        if (format.Channels == 2) return (samples[0], samples[1]);

        // Downmix ITU/EBU aproximado: L/R diretos, centro e surrounds a -3 dB,
        // LFE a -6 dB. Canais excedentes são distribuídos com ganho conservador.
        var left = samples[0];
        var right = samples[1];
        var leftWeight = 1d;
        var rightWeight = 1d;
        if (samples.Length > 2)
        {
            left += samples[2] * 0.7071f;
            right += samples[2] * 0.7071f;
            leftWeight += .7071; rightWeight += .7071;
        }
        if (samples.Length > 3)
        {
            left += samples[3] * .5f;
            right += samples[3] * .5f;
            leftWeight += .5; rightWeight += .5;
        }
        for (var channel = 4; channel < samples.Length; channel++)
        {
            var gain = .7071f;
            if ((channel & 1) == 0) { left += samples[channel] * gain; leftWeight += gain; }
            else { right += samples[channel] * gain; rightWeight += gain; }
        }
        return ((float)(left / leftWeight), (float)(right / rightWeight));
    }

    private static short ToPcm16(float sample) => (short)Math.Clamp(Math.Round(sample * short.MaxValue), short.MinValue, short.MaxValue);

    private static float ReadSample(byte[] buffer, int offset, WaveFormat format)
    {
        if ((format.Encoding == WaveFormatEncoding.IeeeFloat ||
             format is WaveFormatExtensible extensible && extensible.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71")) &&
            format.BitsPerSample == 32)
            return BitConverter.ToSingle(buffer, offset);
        return format.BitsPerSample switch
        {
            16 => BitConverter.ToInt16(buffer, offset) / 32768f,
            24 => ReadPcm24(buffer, offset) / 8388608f,
            32 => BitConverter.ToInt32(buffer, offset) / 2147483648f,
            _ => throw new NotSupportedException($"Formato WASAPI não suportado: {format}.")
        };
    }

    private static int ReadPcm24(byte[] buffer, int offset)
    {
        var value = buffer[offset] | buffer[offset + 1] << 8 | buffer[offset + 2] << 16;
        return (value & 0x800000) != 0 ? value | unchecked((int)0xff000000) : value;
    }

    private static bool SameFormat(WaveFormat? previous, WaveFormat next) => previous is not null &&
        previous.SampleRate == next.SampleRate && previous.Channels == next.Channels &&
        previous.BitsPerSample == next.BitsPerSample && previous.Encoding == next.Encoding;
}
