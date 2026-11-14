using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using TelaRec.Core.Contracts;
using TelaRec.Core.Recording;

namespace TelaRec.Infrastructure.Recording;

public sealed class WasapiLoopbackAudioCaptureService(ILogger<WasapiLoopbackAudioCaptureService> logger) : IAudioCaptureService
{
    private WasapiLoopbackCapture? _capture;
    private long _sourceFrames;
    private long _outputFrames;
    private long _originTicks;
    private double _nextSourcePosition;
    private float _previousLeft;
    private float _previousRight;
    public event EventHandler<AudioFrame>? AudioCaptured;
    public event EventHandler<Exception>? CaptureFailed;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_capture is not null) throw new InvalidOperationException("Áudio já iniciado.");
        try
        {
            _capture = new WasapiLoopbackCapture();
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            _sourceFrames = _outputFrames = 0;
            _nextSourcePosition = 0;
            _originTicks = 0;
            _capture.StartRecording();
            logger.LogInformation("WASAPI loopback iniciado: {Format}", _capture.WaveFormat);
            return Task.CompletedTask;
        }
        catch (Exception exception) { CaptureFailed?.Invoke(this, exception); StopAsync(); throw; }
    }
    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is null) return;
        logger.LogWarning(e.Exception, "WASAPI loopback interrompido");
        CaptureFailed?.Invoke(this, e.Exception);
    }
    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        try
        {
            if (_capture is null) return;
            var format = _capture.WaveFormat;
            var frames = e.BytesRecorded / format.BlockAlign;
            if (frames == 0) return;
            if (_originTicks == 0)
            {
                var now = (long)(Stopwatch.GetTimestamp() * (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency);
                _originTicks = now - (long)(frames * (double)TimeSpan.TicksPerSecond / format.SampleRate);
            }
            var left = new float[frames];
            var right = new float[frames];
            for (var i = 0; i < frames; i++)
            {
                var offset = i * format.BlockAlign;
                left[i] = ReadSample(e.Buffer, offset, format);
                right[i] = format.Channels > 1 ? ReadSample(e.Buffer, offset + format.BitsPerSample / 8, format) : left[i];
            }
            var firstOutput = _outputFrames;
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            var step = format.SampleRate / 48_000.0;
            while (_nextSourcePosition < _sourceFrames + frames)
            {
                var local = _nextSourcePosition - _sourceFrames;
                var index = (int)Math.Floor(local);
                if (index >= frames - 1) break;
                var fraction = (float)(local - index);
                var l0 = index < 0 ? _previousLeft : left[index];
                var r0 = index < 0 ? _previousRight : right[index];
                var l1 = left[index + 1];
                var r1 = right[index + 1];
                writer.Write((short)Math.Clamp(Math.Round((l0 + (l1 - l0) * fraction) * short.MaxValue), short.MinValue, short.MaxValue));
                writer.Write((short)Math.Clamp(Math.Round((r0 + (r1 - r0) * fraction) * short.MaxValue), short.MinValue, short.MaxValue));
                _nextSourcePosition += step;
                _outputFrames++;
            }
            _previousLeft = left[^1]; _previousRight = right[^1];
            _sourceFrames += frames;
            if (stream.Length > 0)
                AudioCaptured?.Invoke(this, new AudioFrame(stream.ToArray(), 48_000, 2,
                    TimeSpan.FromTicks(_originTicks + firstOutput * TimeSpan.TicksPerSecond / 48_000)));
        }
        catch (Exception exception) { logger.LogWarning(exception, "Falha ao processar áudio loopback"); CaptureFailed?.Invoke(this, exception); }
    }
    private static float ReadSample(byte[] buffer, int offset, WaveFormat format)
    {
        if ((format.Encoding == WaveFormatEncoding.IeeeFloat ||
             format is WaveFormatExtensible extensible && extensible.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71"))
            && format.BitsPerSample == 32) return BitConverter.ToSingle(buffer, offset);
        if (format.BitsPerSample == 16) return BitConverter.ToInt16(buffer, offset) / 32768f;
        if (format.BitsPerSample == 32) return BitConverter.ToInt32(buffer, offset) / 2147483648f;
        if (format.BitsPerSample == 24)
        {
            var value = buffer[offset] | buffer[offset + 1] << 8 | buffer[offset + 2] << 16;
            if ((value & 0x800000) != 0) value |= unchecked((int)0xff000000);
            return value / 8388608f;
        }
        throw new NotSupportedException($"Formato WASAPI não suportado: {format}.");
    }
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_capture is { } capture)
        {
            _capture = null;
            capture.DataAvailable -= OnDataAvailable;
            capture.RecordingStopped -= OnRecordingStopped;
            try { capture.StopRecording(); } finally { capture.Dispose(); }
        }
        return Task.CompletedTask;
    }
    public async ValueTask DisposeAsync() => await StopAsync();
}
