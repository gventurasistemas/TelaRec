using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using TelaRec.Core.Contracts;
using TelaRec.Core.Recording;

namespace TelaRec.Infrastructure.Recording;

public sealed class WasapiLoopbackAudioCaptureService(ILogger<WasapiLoopbackAudioCaptureService> logger) : IAudioCaptureService
{
    private WasapiLoopbackCapture? _capture;
    private readonly Pcm16Resampler _resampler = new();
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
            _resampler.Reset();
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
            var now = (long)(Stopwatch.GetTimestamp() * (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency);
            var frame = _resampler.Convert(e.Buffer, e.BytesRecorded, _capture.WaveFormat, now);
            if (frame is not null) AudioCaptured?.Invoke(this, frame);
        }
        catch (Exception exception) { logger.LogWarning(exception, "Falha ao processar áudio loopback"); CaptureFailed?.Invoke(this, exception); }
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
