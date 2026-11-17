using Microsoft.Extensions.Logging.Abstractions;
using TelaRec.Core.Recording;
using TelaRec.Infrastructure.Recording;

namespace TelaRec.Tests;

public sealed class FfmpegRecordingExporterTests
{
    private static readonly RecordingProfile H264Mkv = new(RecordingContainer.Mkv, VideoCodec.H264, 1_000_000, 30, 640, 480);

    [Fact]
    public async Task Preflight_reports_missing_ffmpeg_without_falling_back_to_path()
    {
        var directory = NewDirectory();
        try
        {
            var exporter = CreateExporter(new FakeRunner(), null, directory);
            var result = await exporter.CheckAvailabilityAsync(H264Mkv);

            Assert.False(result.IsAvailable);
            Assert.Contains("não encontrado", result.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Preflight_rejects_invalid_configured_path_before_running_process()
    {
        var directory = NewDirectory();
        try
        {
            var runner = new FakeRunner();
            var exporter = CreateExporter(runner, Path.Combine(directory, "missing.exe"), directory);
            var result = await exporter.CheckAvailabilityAsync(H264Mkv);

            Assert.False(result.IsAvailable);
            Assert.Contains("TELAREC_FFMPEG_PATH", result.Message);
            Assert.Empty(runner.Calls);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Preflight_accepts_configured_ffmpeg_with_required_h264_capabilities()
    {
        var directory = NewDirectory();
        try
        {
            var executable = Path.Combine(directory, "ffmpeg.exe");
            await File.WriteAllBytesAsync(executable, []);
            var runner = new FakeRunner();
            var exporter = CreateExporter(runner, executable, directory);

            var result = await exporter.CheckAvailabilityAsync(H264Mkv);

            Assert.True(result.IsAvailable);
            Assert.Equal(["-hide_banner -version", "-hide_banner -encoders"], runner.Calls);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Preflight_rejects_missing_codec_capability()
    {
        var directory = NewDirectory();
        try
        {
            var executable = Path.Combine(directory, "ffmpeg.exe");
            await File.WriteAllBytesAsync(executable, []);
            var runner = new FakeRunner { Encoders = " V..... aac" };
            var exporter = CreateExporter(runner, executable, directory);

            var result = await exporter.CheckAvailabilityAsync(H264Mkv);

            Assert.False(result.IsAvailable);
            Assert.Contains("libx264", result.Message);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Cancellation_removes_only_incomplete_export_temporary()
    {
        var directory = NewDirectory();
        try
        {
            var executable = Path.Combine(directory, "ffmpeg.exe");
            var source = Path.Combine(directory, "source.mp4");
            var destination = Path.Combine(directory, "clip.mkv");
            await File.WriteAllBytesAsync(executable, []);
            await File.WriteAllBytesAsync(source, [1, 2, 3]);
            var exporter = CreateExporter(new CancellingRunner(), executable, directory);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => exporter.ExportAsync(source, destination, H264Mkv));

            Assert.False(File.Exists(destination));
            Assert.Empty(Directory.GetFiles(directory, "*.export.partial.mkv"));
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(source));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Completed_export_is_preserved_when_destination_becomes_unavailable()
    {
        var directory = NewDirectory();
        try
        {
            var executable = Path.Combine(directory, "ffmpeg.exe");
            var source = Path.Combine(directory, "source.mp4");
            var destination = Path.Combine(directory, "clip.mkv");
            await File.WriteAllBytesAsync(executable, []);
            await File.WriteAllBytesAsync(source, [1, 2, 3]);
            var exporter = CreateExporter(new CompletedExportRunner(destination), executable, directory);

            var error = await Assert.ThrowsAsync<IOException>(() => exporter.ExportAsync(source, destination, H264Mkv));

            Assert.Contains("recuperável", error.Message);
            Assert.Equal(new byte[] { 9 }, await File.ReadAllBytesAsync(destination));
            var recovered = Assert.Single(Directory.GetFiles(directory, "clip-export-recuperavel-*.mkv"));
            Assert.Equal(new byte[] { 4, 5, 6 }, await File.ReadAllBytesAsync(recovered));
            Assert.Empty(Directory.GetFiles(directory, "*.export.partial.mkv"));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Cancellation_stops_external_process_before_its_followup_command()
    {
        var directory = NewDirectory();
        var started = Path.Combine(directory, "started.txt");
        var completed = Path.Combine(directory, "completed.txt");
        using var cancellation = new CancellationTokenSource();
        try
        {
            var runner = new FfmpegProcessRunner();
            var script = await CreateStallingScriptAsync(directory, started, completed);
            var running = runner.RunAsync(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe", ["/d", "/c", script], TimeSpan.FromSeconds(30), cancellation.Token);
            await WaitForFileAsync(started);
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
            Assert.False(File.Exists(completed));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Timeout_stops_external_process_before_its_followup_command()
    {
        var directory = NewDirectory();
        var completed = Path.Combine(directory, "completed.txt");
        try
        {
            var runner = new FfmpegProcessRunner();
            var script = await CreateStallingScriptAsync(directory, null, completed);

            await Assert.ThrowsAsync<TimeoutException>(() => runner.RunAsync(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe", ["/d", "/c", script], TimeSpan.FromMilliseconds(100), CancellationToken.None));
            Assert.False(File.Exists(completed));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Process_runner_limits_captured_output()
    {
        var runner = new FfmpegProcessRunner();
        var result = await runner.RunAsync(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe", ["/d", "/c", "for /L %i in (1,1,4000) do @echo 1234567890"], TimeSpan.FromSeconds(10), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.OutputTruncated);
        Assert.True(result.StandardOutput.Length <= FfmpegProcessRunner.MaximumDiagnosticCharacters);
    }

    private static FfmpegRecordingExporter CreateExporter(IFfmpegProcessRunner runner, string? configuredPath, string baseDirectory) =>
        new(NullLogger<FfmpegRecordingExporter>.Instance, runner, () => configuredPath, () => baseDirectory);

    private static async Task WaitForFileAsync(string path)
    {
        var timeout = DateTime.UtcNow.AddSeconds(2);
        while (!File.Exists(path) && DateTime.UtcNow < timeout) await Task.Delay(10);
        Assert.True(File.Exists(path), "O processo de teste não iniciou no prazo esperado.");
    }

    private static async Task<string> CreateStallingScriptAsync(string directory, string? started, string completed)
    {
        var script = Path.Combine(directory, "stall.cmd");
        var lines = new List<string> { "@echo off" };
        if (started is not null) lines.Add($"echo started>\"{started}\"");
        lines.Add("ping -n 20 127.0.0.1 >nul");
        lines.Add($"echo completed>\"{completed}\"");
        await File.WriteAllLinesAsync(script, lines);
        return script;
    }

    private static string NewDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TelaRecTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class FakeRunner : IFfmpegProcessRunner
    {
        public List<string> Calls { get; } = [];
        public string Encoders { get; init; } = " V..... libx264\n A..... aac";

        public Task<FfmpegProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Calls.Add(string.Join(' ', arguments));
            var output = arguments.Contains("-version") ? "ffmpeg version test" : arguments.Contains("-encoders") ? Encoders : " palettegen\n paletteuse";
            return Task.FromResult(new FfmpegProcessResult(0, output, string.Empty, false));
        }
    }

    private sealed class CancellingRunner : IFfmpegProcessRunner
    {
        public Task<FfmpegProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (arguments.Contains("-version")) return Task.FromResult(new FfmpegProcessResult(0, "ffmpeg version test", string.Empty, false));
            if (arguments.Contains("-encoders")) return Task.FromResult(new FfmpegProcessResult(0, "libx264 aac", string.Empty, false));
            File.WriteAllBytes(arguments[^1], [0, 1]);
            return Task.FromCanceled<FfmpegProcessResult>(new CancellationToken(canceled: true));
        }
    }

    private sealed class CompletedExportRunner(string destination) : IFfmpegProcessRunner
    {
        public Task<FfmpegProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (arguments.Contains("-version")) return Task.FromResult(new FfmpegProcessResult(0, "ffmpeg version test", string.Empty, false));
            if (arguments.Contains("-encoders")) return Task.FromResult(new FfmpegProcessResult(0, "libx264 aac", string.Empty, false));
            File.WriteAllBytes(arguments[^1], [4, 5, 6]);
            File.WriteAllBytes(destination, [9]);
            return Task.FromResult(new FfmpegProcessResult(0, string.Empty, string.Empty, false));
        }
    }
}
