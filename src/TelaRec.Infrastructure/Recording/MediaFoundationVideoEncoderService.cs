using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using TelaRec.Core.Contracts;
using TelaRec.Core.Recording;
using Vortice.MediaFoundation;

namespace TelaRec.Infrastructure.Recording;

/// <summary>Encoder H.264/MP4 nativo do Windows Media Foundation.</summary>
public sealed class MediaFoundationVideoEncoderService(ILogger<MediaFoundationVideoEncoderService> logger) : IVideoEncoderService
{
    private IMFSinkWriter? _writer;
    private int _streamIndex = -1;
    private int? _audioStreamIndex;
    private bool _writing;
    private readonly object _writerSync = new();
    private int _fps;
    private bool _started;
    private bool _mediaFoundationStarted;
    private RecordingProfile _profile = RecordingProfile.Mvp;

    public Task StartAsync(RecordingRequest request, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(request.OutputPath) ?? throw new ArgumentException("Caminho de saída inválido.");
        Directory.CreateDirectory(directory);
        if (_started) throw new InvalidOperationException("Encoder já iniciado.");
        _profile = request.EffectiveProfile;
        if (_profile.Container != RecordingContainer.Mp4 || _profile.VideoCodec != VideoCodec.H264)
            throw new NotSupportedException("O encoder nativo aceita apenas o perfil intermediário MP4/H.264.");
        _fps = _profile.FramesPerSecond;
        MediaFactory.MFStartup();
        _mediaFoundationStarted = true;
        try
        {
            using var attributes = MediaFactory.MFCreateAttributes(1);
            attributes.Set(SinkWriterAttributeKeys.DisableThrottling, 1u);
            _writer = MediaFactory.MFCreateSinkWriterFromURL(request.OutputPath, null, attributes);
            if (request.SystemAudioEnabled) ConfigureAudio();
            _started = true;
        }
        catch { Release(); throw; }
        logger.LogInformation("Encoder Media Foundation iniciado para {OutputPath}", request.OutputPath);
        return Task.CompletedTask;
    }

    public Task EncodeAsync(VideoFrame frame, CancellationToken cancellationToken = default)
    {
        if (!_started || _writer is null) throw new InvalidOperationException("Encoder não iniciado.");

        if (_streamIndex < 0 && !Configure(frame)) throw new InvalidOperationException("Não foi possível configurar o encoder H.264.");
        var nv12 = ConvertToNv12(frame);
        using var buffer = MediaFactory.MFCreateMemoryBuffer(nv12.Length);
        buffer.Lock(out var pointer, out _, out _);
        try { Marshal.Copy(nv12, 0, pointer, nv12.Length); }
        finally { buffer.Unlock(); }
        buffer.CurrentLength = nv12.Length;
        using var sample = MediaFactory.MFCreateSample();
        sample.AddBuffer(buffer);
        sample.SampleTime = frame.Timestamp.Ticks;
        sample.SampleDuration = TimeSpan.TicksPerSecond / _fps;
        lock (_writerSync) _writer.WriteSample(_streamIndex, sample);
        return Task.CompletedTask;
    }

    public Task EncodeAudioAsync(AudioFrame frame, CancellationToken cancellationToken = default)
    {
        if (!_started || !_writing || _writer is null || _audioStreamIndex is null) return Task.CompletedTask;
        // O encoder AAC nativo recebe PCM 16-bit intercalado. Só aceitamos o formato configurado.
        if (frame.SampleRate != 48_000 || frame.Channels != 2) return Task.CompletedTask;
        using var buffer = MediaFactory.MFCreateMemoryBuffer(frame.Pcm16.Length);
        buffer.Lock(out var pointer, out _, out _);
        try { Marshal.Copy(frame.Pcm16, 0, pointer, frame.Pcm16.Length); }
        finally { buffer.Unlock(); }
        buffer.CurrentLength = frame.Pcm16.Length;
        using var sample = MediaFactory.MFCreateSample();
        sample.AddBuffer(buffer);
        sample.SampleTime = frame.Timestamp.Ticks;
        sample.SampleDuration = frame.Pcm16.Length * TimeSpan.TicksPerSecond / (frame.SampleRate * frame.Channels * sizeof(short));
        lock (_writerSync) _writer.WriteSample(_audioStreamIndex.Value, sample);
        return Task.CompletedTask;
    }

    private bool Configure(VideoFrame frame)
    {
        using var output = MediaFactory.MFCreateMediaType();
        output.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
        output.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.H264);
        output.Set(MediaTypeAttributeKeys.AvgBitrate, (uint)_profile.Bitrate);
        output.Set(MediaTypeAttributeKeys.FrameSize, Pack(frame.Width, frame.Height));
        output.Set(MediaTypeAttributeKeys.FrameRate, Pack(_fps, 1));
        output.Set(MediaTypeAttributeKeys.PixelAspectRatio, Pack(1, 1));
        output.Set(MediaTypeAttributeKeys.InterlaceMode, 2u);
        _streamIndex = _writer!.AddStream(output);

        using var input = MediaFactory.MFCreateMediaType();
        input.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
        input.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.NV12);
        input.Set(MediaTypeAttributeKeys.FrameSize, Pack(frame.Width, frame.Height));
        input.Set(MediaTypeAttributeKeys.FrameRate, Pack(_fps, 1));
        input.Set(MediaTypeAttributeKeys.PixelAspectRatio, Pack(1, 1));
        input.Set(MediaTypeAttributeKeys.InterlaceMode, 2u);
        _writer.SetInputMediaType(_streamIndex, input, null);
        _writer.BeginWriting();
        _writing = true;
        return true;
    }

    private void ConfigureAudio()
    {
        using var output = MediaFactory.MFCreateMediaType();
        output.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
        output.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Aac);
        output.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16u);
        output.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, 48_000u);
        output.Set(MediaTypeAttributeKeys.AudioNumChannels, 2u);
        output.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, 16_000u);
        _audioStreamIndex = _writer!.AddStream(output);
        using var input = MediaFactory.MFCreateMediaType();
        input.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
        input.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Pcm);
        input.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16u);
        input.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, 48_000u);
        input.Set(MediaTypeAttributeKeys.AudioNumChannels, 2u);
        input.Set(MediaTypeAttributeKeys.AudioBlockAlignment, 4u);
        input.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, 192_000u);
        _writer.SetInputMediaType(_audioStreamIndex.Value, input, null);
    }

    public Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        try { if (_writer is not null && _started && _writing) _writer.Finalize(); }
        finally { Release(); }
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Release();
        return ValueTask.CompletedTask;
    }

    private void Release()
    {
        _writer?.Dispose(); _writer = null;
        _started = false; _writing = false; _streamIndex = -1; _audioStreamIndex = null;
        if (_mediaFoundationStarted) { MediaFactory.MFShutdown(); _mediaFoundationStarted = false; }
    }

    private static ulong Pack(int high, int low) => ((ulong)(uint)high << 32) | (uint)low;
    private static byte[] ConvertToNv12(VideoFrame frame)
    {
        if ((frame.Width & 1) != 0 || (frame.Height & 1) != 0)
            throw new ArgumentException("O H.264 exige dimensões pares.");
        var result = new byte[frame.Width * frame.Height * 3 / 2];
        var uvOffset = frame.Width * frame.Height;
        for (var y = 0; y < frame.Height; y += 2)
        for (var x = 0; x < frame.Width; x += 2)
        {
            var u = 0; var v = 0;
            for (var dy = 0; dy < 2; dy++)
            for (var dx = 0; dx < 2; dx++)
            {
                var source = (y + dy) * frame.Stride + (x + dx) * 4;
                var b = frame.Pixels[source]; var g = frame.Pixels[source + 1]; var r = frame.Pixels[source + 2];
                result[(y + dy) * frame.Width + x + dx] = (byte)Math.Clamp((66 * r + 129 * g + 25 * b + 128) / 256 + 16, 0, 255);
                u += (-38 * r - 74 * g + 112 * b + 128) / 256 + 128;
                v += (112 * r - 94 * g - 18 * b + 128) / 256 + 128;
            }
            var uv = uvOffset + y / 2 * frame.Width + x;
            result[uv] = (byte)Math.Clamp(u / 4, 0, 255);
            result[uv + 1] = (byte)Math.Clamp(v / 4, 0, 255);
        }
        return result;
    }
}
