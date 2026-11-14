using Microsoft.Extensions.Logging;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Runtime.InteropServices;
using TelaRec.Core.Capture;
using TelaRec.Core.Recording;
using TelaRec.Infrastructure.Recording;

var path = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(Path.GetTempPath(), "TelaRec-smoke.mp4");
await using var recorder = new RecordingService(
    new WindowsGraphicsCaptureService(new TestLogger<WindowsGraphicsCaptureService>()),
    new MediaFoundationVideoEncoderService(new TestLogger<MediaFoundationVideoEncoderService>()),
    new WasapiLoopbackAudioCaptureService(new TestLogger<WasapiLoopbackAudioCaptureService>()),
    new TestLogger<RecordingService>(),
    new RecordingExporterFactory([new FfmpegRecordingExporter(new TestLogger<FfmpegRecordingExporter>())]),
    clickCapture: new WindowsClickCaptureService(new TestLogger<WindowsClickCaptureService>()));
recorder.StatusChanged += (_, status) => Console.WriteLine($"{status.State}: {status.ErrorMessage}");
try
{
CaptureTarget target = args.Contains("--region") ? new CaptureTarget.Region(100, 100, 641, 481)
        : args.Contains("--window") ? new CaptureTarget.Window(Native.GetForegroundWindow(), "Janela ativa")
        : new CaptureTarget.Display();
var formatIndex = Array.FindIndex(args, a => string.Equals(a, "--format", StringComparison.OrdinalIgnoreCase));
var format = formatIndex >= 0 && formatIndex + 1 < args.Length && Enum.TryParse<RecordingContainer>(args[formatIndex + 1], true, out var parsed)
    ? parsed : RecordingContainer.Mp4;
var profile = new RecordingProfile(format, format == RecordingContainer.Gif ? VideoCodec.Gif : VideoCodec.H264, 2_000_000, 15, 640, 480);
var clickEffects = args.Contains("--click-effects")
    ? new ClickEffectSettings(Enabled: true, Radius: 32, DurationSeconds: 1.0, Color: "#FF0000", Opacity: 1.0)
    : ClickEffectSettings.None;
var overlays = new RecordingOverlaySettings(
    args.Contains("--watermark")
        ? new WatermarkSettings(WatermarkKind.Text, "TelaRec", null, OverlayPosition.TopLeft, 0.8, 0.25)
        : new WatermarkSettings(),
    new WebcamOverlaySettings(),
    clickEffects);
    await recorder.StartAsync(new RecordingRequest(path, 15, args.Contains("--audio"), target, profile, overlays, ShowCursor: !args.Contains("--cursor-off")));
    using var output = args.Contains("--audio") && !args.Contains("--silent") ? new WaveOutEvent() : null;
    if (output is not null)
    {
        output.Init(new SignalGenerator(48000, 2) { Frequency = 440, Gain = 0.02 }.ToWaveProvider16());
        output.Play();
    }
    await Task.Delay(args.Contains("--short") ? 2000 : 5000);
    output?.Stop();
    await recorder.StopAsync();
    Console.WriteLine($"{path}: {new FileInfo(path).Length} bytes");
}
catch (Exception exception) { Console.Error.WriteLine(exception); throw; }

sealed class TestLogger<T> : ILogger<T>
{
    public IDisposable BeginScope<TState>(TState state) where TState : notnull => Scope.Instance;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Console.Error.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {level} {typeof(T).Name}: {formatter(state, exception)} {exception}");
    private sealed class Scope : IDisposable { public static readonly Scope Instance = new(); public void Dispose() { } }
}
static class Native
{
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();
}
