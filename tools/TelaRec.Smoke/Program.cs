using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using TelaRec.Core.Capture;
using TelaRec.Core.Recording;
using TelaRec.Infrastructure.Recording;
using Windows.Graphics.Capture;

const int Success = 0;
const int Failure = 1;
const int Skipped = 2;
const int TimedOut = 3;

var options = SmokeOptions.Parse(args);
if (!options.Integration)
{
    Console.WriteLine("SKIP: smoke de mídia real é opt-in. Use --integration em uma sessão Windows interativa.");
    return Skipped;
}
if (!Environment.UserInteractive || !System.Windows.Forms.SystemInformation.UserInteractive ||
    System.Windows.Forms.Screen.AllScreens.Length == 0 || !GraphicsCaptureSession.IsSupported())
{
    Console.WriteLine("SKIP: Windows.Graphics.Capture exige uma sessão gráfica Windows interativa.");
    return Skipped;
}
if (options.Codec == VideoCodec.Hevc && !new MediaFoundationVideoEncoderCapabilitiesService().GetCapabilities().HevcSupported)
{
    Console.WriteLine("SKIP: HEVC não está disponível neste Windows; H.264 continua obrigatório.");
    return Skipped;
}

Directory.CreateDirectory(Path.GetDirectoryName(options.OutputPath) ?? AppContext.BaseDirectory);
var reportPath = options.OutputPath + ".smoke.json";
var log = new SmokeLog(options.OutputPath + ".smoke.log");
using var timeout = new CancellationTokenSource(options.Timeout);
using var visual = new DeterministicVisualSource();
await using var recorder = CreateRecorder(log);
var started = false;
var exitCode = Failure;
object? observed = null;
try
{
    log.Write($"Iniciando smoke de mídia: destino={options.OutputPath}; codec={options.Codec}; alvo={options.TargetMode}; duração={options.Duration}; timeout={options.Timeout}.");
    visual.Start(timeout.Token);
    if (options.TargetMode == SmokeTargetMode.Window)
        throw new InvalidOperationException("--window requer uma janela externa: a fonte visual determinística do smoke pertence ao próprio processo e é bloqueada para evitar auto-captura. Use --region ou --display.");
    CaptureTarget target = options.TargetMode switch
    {
        SmokeTargetMode.Display => new CaptureTarget.Display(),
        SmokeTargetMode.Region => new CaptureTarget.Region(100, 100, 641, 481),
        _ => new CaptureTarget.Window(visual.Handle, "TelaRec smoke visual")
    };
    var profile = new RecordingProfile(RecordingContainer.Mp4, options.Codec, 2_000_000, 15, 640, 480);
    var overlays = new RecordingOverlaySettings(
        new WatermarkSettings(),
        new WebcamOverlaySettings(),
        options.ClickEffects
            ? new ClickEffectSettings(Enabled: true, Radius: 32, DurationSeconds: 1.0, Color: "#FF0000", Opacity: 1.0)
            : ClickEffectSettings.None);
    await recorder.StartAsync(new RecordingRequest(
        options.OutputPath,
        profile.FramesPerSecond,
        options.SystemAudio,
        target,
        profile,
        overlays,
        ShowCursor: options.Cursor,
        AudioMix: new AudioMixSettings(options.SystemAudio, options.Microphone)), timeout.Token);
    started = true;

    using var tone = options.SystemAudio && !options.Silent ? StartTone() : null;
    await Task.Delay(options.Duration, timeout.Token);
    tone?.Stop();
    await recorder.StopAsync(CancellationToken.None);
    started = false;

    var inspection = new MediaFoundationMediaInspector().Inspect(options.OutputPath, options.SystemAudio || options.Microphone, timeout.Token);
    observed = inspection;
    var expected = new MediaSmokeExpectation(
        options.Codec,
        profile.Width,
        profile.Height,
        profile.FramesPerSecond,
        TimeSpan.FromSeconds(Math.Max(0.5, options.Duration.TotalSeconds - 1)),
        options.Duration + TimeSpan.FromSeconds(3),
        options.SystemAudio || options.Microphone);
    var failures = MediaSmokeValidator.Validate(inspection, expected);
    if (failures.Count > 0)
        throw new SmokeFailureException(string.Join("; ", failures));

    log.Write($"PASS: MP4 decodificado. codec={inspection.VideoCodec}; dimensão={inspection.Width}x{inspection.Height}; fps={inspection.FramesPerSecond:F2}; vídeo={inspection.VideoDuration.TotalSeconds:F2}s/{inspection.DecodedVideoSamples} frames; áudio={inspection.AudioDuration?.TotalSeconds:F2}s/{inspection.DecodedAudioSamples} amostras.");
    Console.WriteLine($"PASS: {options.OutputPath}");
    exitCode = Success;
}
catch (OperationCanceledException) when (timeout.IsCancellationRequested)
{
    log.Write($"TIMEOUT: etapa excedeu {options.Timeout}. Artefato preservado em {options.OutputPath}.");
    Console.Error.WriteLine($"TIMEOUT: {options.OutputPath}");
    exitCode = TimedOut;
}
catch (SmokeFailureException exception)
{
    log.Write($"FAIL: validação de mídia: {exception.Message}. Artefato preservado em {options.OutputPath}.");
    Console.Error.WriteLine($"FAIL: {exception.Message}{Environment.NewLine}Artefato: {options.OutputPath}");
    exitCode = Failure;
}
catch (Exception exception)
{
    log.Write($"FAIL: {exception.GetType().Name}: {exception.Message}{Environment.NewLine}{exception}");
    Console.Error.WriteLine($"FAIL: {exception.Message}{Environment.NewLine}Artefato: {options.OutputPath}");
    exitCode = Failure;
}
finally
{
    if (started)
    {
        try { await recorder.StopAsync(CancellationToken.None); }
        catch (Exception exception) { log.Write($"Falha ao parar após erro: {exception.Message}"); }
    }
    var report = new
    {
        ExitCode = exitCode,
        Artifact = options.OutputPath,
        Log = log.Path,
        Observed = observed
    };
    await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    }));
    log.Write($"Relatório: {reportPath}");
    log.Dispose();
}
return exitCode;

static RecordingService CreateRecorder(SmokeLog log)
{
    var recorder = new RecordingService(
        new WindowsGraphicsCaptureService(new TestLogger<WindowsGraphicsCaptureService>(log)),
        new MediaFoundationVideoEncoderService(new TestLogger<MediaFoundationVideoEncoderService>(log)),
        new WasapiLoopbackAudioCaptureService(new TestLogger<WasapiLoopbackAudioCaptureService>(log)),
        new TestLogger<RecordingService>(log),
        new RecordingExporterFactory([new FfmpegRecordingExporter(new TestLogger<FfmpegRecordingExporter>(log))]),
        clickCapture: new WindowsClickCaptureService(new TestLogger<WindowsClickCaptureService>(log)),
        microphone: new WasapiMicrophoneCaptureService(new TestLogger<WasapiMicrophoneCaptureService>(log)));
    recorder.StatusChanged += (_, status) => log.Write($"Estado={status.State}; erro={status.ErrorMessage}");
    return recorder;
}

static WaveOutEvent StartTone()
{
    var output = new WaveOutEvent();
    output.Init(new SignalGenerator(48_000, 2) { Frequency = 440, Gain = 0.02 }.ToWaveProvider16());
    output.Play();
    return output;
}

internal sealed class SmokeFailureException(string message) : Exception(message);

internal sealed class SmokeLog(string path) : IDisposable
{
    private readonly object _sync = new();
    private readonly StreamWriter _writer = new(path, append: false) { AutoFlush = true };
    public string Path { get; } = path;
    public void Write(string message)
    {
        var line = $"{DateTimeOffset.Now:O} {message}";
        lock (_sync)
        {
            Console.Error.WriteLine(line);
            _writer.WriteLine(line);
        }
    }
    public void Dispose() => _writer.Dispose();
}

internal sealed class TestLogger<T>(SmokeLog log) : ILogger<T>
{
    public IDisposable BeginScope<TState>(TState state) where TState : notnull => Scope.Instance;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => log.Write($"{level} {typeof(T).Name}: {formatter(state, exception)} {exception}");
    private sealed class Scope : IDisposable { public static readonly Scope Instance = new(); public void Dispose() { } }
}
