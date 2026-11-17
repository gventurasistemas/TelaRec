using System.Runtime.InteropServices;
using TelaRec.Core.Recording;
using TelaRec.Infrastructure.Recording;
using Vortice.MediaFoundation;

/// <summary>Abre e decodifica a mídia produzida usando o Source Reader do Windows.</summary>
internal sealed class MediaFoundationMediaInspector
{
    private static readonly Guid HevcFormat = new("43564548-0000-0010-8000-00AA00389B71");

    public MediaInspection Inspect(string path, bool inspectAudio, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("O arquivo final não foi publicado.", path);
        MediaFactory.MFStartup();
        try
        {
            using var reader = MediaFactory.MFCreateSourceReaderFromURL(path, null);
            using var videoType = reader.GetNativeMediaType(SourceReaderIndex.FirstVideoStream, 0);
            var codec = ToCodec(videoType.GetGUID(MediaTypeAttributeKeys.Subtype));
            var (width, height) = Unpack(videoType.GetUInt64(MediaTypeAttributeKeys.FrameSize));
            var (rateNumerator, rateDenominator) = Unpack(videoType.GetUInt64(MediaTypeAttributeKeys.FrameRate));
            var fps = rateDenominator == 0 ? 0 : rateNumerator / (double)rateDenominator;
            var video = DecodeVideo(reader, videoType.GetUInt64(MediaTypeAttributeKeys.FrameSize), videoType.GetUInt64(MediaTypeAttributeKeys.FrameRate), cancellationToken);
            var audio = inspectAudio ? DecodeAudio(path, cancellationToken) : (Duration: (TimeSpan?)null, Samples: 0);
            return new MediaInspection(
                new FileInfo(path).Length,
                HasMp4Signature(path),
                codec,
                width,
                height,
                fps,
                video.Duration,
                video.Samples,
                video.NonBlackSamples,
                audio.Duration,
                audio.Samples);
        }
        finally { MediaFactory.MFShutdown(); }
    }

    private static (TimeSpan Duration, int Samples, int NonBlackSamples) DecodeVideo(IMFSourceReader reader, ulong frameSize, ulong frameRate, CancellationToken cancellationToken)
    {
        reader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
        reader.SetStreamSelection(SourceReaderIndex.FirstVideoStream, true);
        using var output = MediaFactory.MFCreateMediaType();
        output.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
        output.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.NV12);
        output.Set(MediaTypeAttributeKeys.FrameSize, frameSize);
        output.Set(MediaTypeAttributeKeys.FrameRate, frameRate);
        output.Set(MediaTypeAttributeKeys.PixelAspectRatio, ((ulong)1 << 32) | 1u);
        output.Set(MediaTypeAttributeKeys.InterlaceMode, 2u);
        reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, output);

        var duration = TimeSpan.Zero;
        var samples = 0;
        var nonBlack = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var sample = reader.ReadSample(SourceReaderIndex.FirstVideoStream, SourceReaderControlFlag.None, out _, out var flags, out var timestamp);
            if (sample is not null)
            {
                samples++;
                duration = Max(duration, TimeSpan.FromTicks(timestamp + Math.Max(0, sample.SampleDuration)));
                using var buffer = sample.ConvertToContiguousBuffer();
                buffer.Lock(out var pointer, out _, out var currentLength);
                try
                {
                    if (ContainsVisiblePixels(pointer, currentLength)) nonBlack++;
                }
                finally { buffer.Unlock(); }
            }
            if ((flags & SourceReaderFlag.EndOfStream) != 0) break;
        }
        return (duration, samples, nonBlack);
    }

    private static (TimeSpan? Duration, int Samples) DecodeAudio(string path, CancellationToken cancellationToken)
    {
        using var reader = MediaFactory.MFCreateSourceReaderFromURL(path, null);
        reader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
        reader.SetStreamSelection(SourceReaderIndex.FirstAudioStream, true);
        using var output = MediaFactory.MFCreateMediaType();
        output.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
        output.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Pcm);
        reader.SetCurrentMediaType(SourceReaderIndex.FirstAudioStream, output);

        var duration = TimeSpan.Zero;
        var samples = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var sample = reader.ReadSample(SourceReaderIndex.FirstAudioStream, SourceReaderControlFlag.None, out _, out var flags, out var timestamp);
            if (sample is not null)
            {
                samples++;
                duration = Max(duration, TimeSpan.FromTicks(timestamp + Math.Max(0, sample.SampleDuration)));
            }
            if ((flags & SourceReaderFlag.EndOfStream) != 0) break;
        }
        return (duration, samples);
    }

    private static bool ContainsVisiblePixels(IntPtr pointer, int length)
    {
        var pixels = new byte[length];
        Marshal.Copy(pointer, pixels, 0, pixels.Length);
        for (var index = 0; index < pixels.Length * 2 / 3; index += 16)
        {
            if (pixels[index] > 24) return true;
        }
        return false;
    }

    private static bool HasMp4Signature(string path)
    {
        var header = new byte[32];
        using var stream = File.OpenRead(path);
        var read = stream.Read(header, 0, header.Length);
        return read >= 8 && header[4] == (byte)'f' && header[5] == (byte)'t' && header[6] == (byte)'y' && header[7] == (byte)'p';
    }

    private static VideoCodec? ToCodec(Guid subtype) =>
        subtype == VideoFormatGuids.H264 ? VideoCodec.H264 :
        subtype == HevcFormat ? VideoCodec.Hevc : null;

    private static (int High, int Low) Unpack(ulong value) => ((int)(value >> 32), (int)value);
    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left >= right ? left : right;
}
