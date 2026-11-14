using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using TelaRec.Core.Contracts;
using TelaRec.Core.Recording;

namespace TelaRec.Infrastructure.Recording;

/// <summary>Captura WASAPI de um microfone, normalizada para PCM16/48 kHz estéreo.</summary>
public sealed class WasapiMicrophoneCaptureService(ILogger<WasapiMicrophoneCaptureService> logger) : IMicrophoneCaptureService
{
    private readonly Pcm16Resampler _resampler = new();
    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _device;
    private WasapiCapture? _capture;

    public event EventHandler<AudioFrame>? AudioCaptured;
    public event EventHandler<Exception>? CaptureFailed;

    public IReadOnlyList<AudioInputDevice> GetDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        using var defaultDevice = TryGetDefault(enumerator);
        return enumerator
            .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .Select(device => new AudioInputDevice(device.ID, device.FriendlyName, device.ID == defaultDevice?.ID))
            .ToArray();
    }

    public Task StartAsync(string? deviceId = null, CancellationToken cancellationToken = default)
    {
        if (_capture is not null) throw new InvalidOperationException("Microfone já iniciado.");
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            _enumerator = new MMDeviceEnumerator();
            _device = string.IsNullOrWhiteSpace(deviceId)
                ? _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia)
                : _enumerator.GetDevice(deviceId);
            _capture = new WasapiCapture(_device);
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            _resampler.Reset();
            _capture.StartRecording();
            logger.LogInformation("Microfone WASAPI iniciado: {Device} ({Format})", _device.FriendlyName, _capture.WaveFormat);
            return Task.CompletedTask;
        }
        catch (Exception exception)
        {
            CaptureFailed?.Invoke(this, exception);
            _ = StopAsync();
            throw;
        }
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
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Falha ao processar dados do microfone");
            CaptureFailed?.Invoke(this, exception);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is null) return;
        logger.LogWarning(e.Exception, "Microfone WASAPI interrompido");
        CaptureFailed?.Invoke(this, e.Exception);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_capture is { } capture)
        {
            _capture = null;
            capture.DataAvailable -= OnDataAvailable;
            capture.RecordingStopped -= OnRecordingStopped;
            try { capture.StopRecording(); }
            finally { capture.Dispose(); }
        }
        _enumerator?.Dispose();
        _enumerator = null;
        _device?.Dispose();
        _device = null;
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private static MMDevice? TryGetDefault(MMDeviceEnumerator enumerator)
    {
        try { return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia); }
        catch { return null; }
    }
}
