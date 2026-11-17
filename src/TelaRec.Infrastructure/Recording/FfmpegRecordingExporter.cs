using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using TelaRec.Core.Contracts;
using TelaRec.Core.Recording;

namespace TelaRec.Infrastructure.Recording;

/// <summary>Backend de pós-processamento isolado para MKV, MOV e GIF.</summary>
public sealed class FfmpegRecordingExporter : IRecordingExporter, IRecordingExportPreflight
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ExportTimeout = TimeSpan.FromMinutes(15);
    private readonly ILogger<FfmpegRecordingExporter> _logger;
    private readonly IFfmpegProcessRunner _processRunner;
    private readonly Func<string?> _configuredPath;
    private readonly Func<string> _baseDirectory;

    public FfmpegRecordingExporter(ILogger<FfmpegRecordingExporter> logger)
        : this(logger, new FfmpegProcessRunner(), () => Environment.GetEnvironmentVariable("TELAREC_FFMPEG_PATH"), () => AppContext.BaseDirectory)
    {
    }

    internal FfmpegRecordingExporter(ILogger<FfmpegRecordingExporter> logger, IFfmpegProcessRunner processRunner, Func<string?> configuredPath, Func<string> baseDirectory)
    {
        _logger = logger;
        _processRunner = processRunner;
        _configuredPath = configuredPath;
        _baseDirectory = baseDirectory;
    }

    public bool CanExport(RecordingProfile profile) => profile.Container is RecordingContainer.Mkv or RecordingContainer.Mov or RecordingContainer.Gif;

    public async Task<RecordingExportPreflightResult> CheckAvailabilityAsync(RecordingProfile profile, CancellationToken cancellationToken = default)
    {
        if (!CanExport(profile)) return RecordingExportPreflightResult.Unavailable($"Perfil {profile.Container} não é exportado por FFmpeg.");
        var resolved = ResolveFfmpeg();
        if (resolved.Path is null) return RecordingExportPreflightResult.Unavailable(resolved.Message);

        try
        {
            var version = await _processRunner.RunAsync(resolved.Path, ["-hide_banner", "-version"], ProbeTimeout, cancellationToken).ConfigureAwait(false);
            if (version.ExitCode != 0) return RecordingExportPreflightResult.Unavailable($"FFmpeg em '{resolved.Path}' não respondeu à verificação (código {version.ExitCode}): {version.Diagnostic}");
            if (!version.StandardOutput.Contains("ffmpeg version", StringComparison.OrdinalIgnoreCase)) return RecordingExportPreflightResult.Unavailable($"O arquivo configurado em '{resolved.Path}' não se identificou como FFmpeg.");

            if (profile.Container == RecordingContainer.Gif)
            {
                var filters = await _processRunner.RunAsync(resolved.Path, ["-hide_banner", "-filters"], ProbeTimeout, cancellationToken).ConfigureAwait(false);
                if (filters.ExitCode != 0 || !filters.StandardOutput.Contains("palettegen", StringComparison.OrdinalIgnoreCase) || !filters.StandardOutput.Contains("paletteuse", StringComparison.OrdinalIgnoreCase))
                    return RecordingExportPreflightResult.Unavailable($"FFmpeg em '{resolved.Path}' não oferece os filtros palettegen e paletteuse exigidos para GIF.");
            }
            else
            {
                var encoders = await _processRunner.RunAsync(resolved.Path, ["-hide_banner", "-encoders"], ProbeTimeout, cancellationToken).ConfigureAwait(false);
                var videoEncoder = profile.VideoCodec == VideoCodec.Hevc ? "libx265" : "libx264";
                if (encoders.ExitCode != 0 || !encoders.StandardOutput.Contains(videoEncoder, StringComparison.OrdinalIgnoreCase) || !encoders.StandardOutput.Contains("aac", StringComparison.OrdinalIgnoreCase))
                    return RecordingExportPreflightResult.Unavailable($"FFmpeg em '{resolved.Path}' não oferece {videoEncoder} e AAC exigidos para {profile.Container}. Instale uma versão compatível ou selecione MP4 nativo.");
            }

            var message = $"FFmpeg disponível em '{resolved.Path}' para {profile.Container}.";
            _logger.LogInformation("{Message}", message);
            return RecordingExportPreflightResult.Available(message);
        }
        catch (TimeoutException exception)
        {
            _logger.LogWarning(exception, "Tempo esgotado ao verificar FFmpeg em {FfmpegPath}", resolved.Path);
            return RecordingExportPreflightResult.Unavailable($"FFmpeg em '{resolved.Path}' excedeu o tempo de verificação. Verifique o executável ou selecione MP4 nativo.");
        }
        catch (Win32Exception exception)
        {
            _logger.LogWarning(exception, "Não foi possível executar FFmpeg em {FfmpegPath}", resolved.Path);
            return RecordingExportPreflightResult.Unavailable($"Não foi possível executar FFmpeg em '{resolved.Path}': {exception.Message}");
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "Falha de E/S ao verificar FFmpeg em {FfmpegPath}", resolved.Path);
            return RecordingExportPreflightResult.Unavailable($"Falha ao verificar FFmpeg em '{resolved.Path}': {exception.Message}");
        }
    }

    public async Task ExportAsync(string sourcePath, string destinationPath, RecordingProfile profile, CancellationToken cancellationToken = default)
    {
        if (!CanExport(profile)) throw new NotSupportedException($"Perfil {profile.Container} não é exportado por FFmpeg.");
        var availability = await CheckAvailabilityAsync(profile, cancellationToken).ConfigureAwait(false);
        if (!availability.IsAvailable) throw new NotSupportedException(availability.Message);

        var ffmpeg = ResolveFfmpeg().Path!;
        var output = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        if (File.Exists(output)) throw new IOException($"Já existe um arquivo no destino: {output}");
        var sourceStem = Path.GetFileNameWithoutExtension(Path.GetFullPath(sourcePath));
        var temporaryOutput = Path.Combine(Path.GetDirectoryName(output)!, $"{sourceStem}.export.partial{Path.GetExtension(output)}");
        var exportCompleted = false;
        try
        {
            var result = await _processRunner.RunAsync(ffmpeg, BuildArguments(sourcePath, temporaryOutput, profile), ExportTimeout, cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0) throw new InvalidOperationException($"FFmpeg não conseguiu gerar {profile.Container} (código {result.ExitCode}): {result.Diagnostic}");
            if (!File.Exists(temporaryOutput)) throw new InvalidOperationException("FFmpeg terminou sem produzir o arquivo temporário esperado.");
            exportCompleted = true;
            try { File.Move(temporaryOutput, output); }
            catch (IOException exception)
            {
                var recovered = PreserveCompletedOutput(temporaryOutput, output);
                throw new IOException($"FFmpeg concluiu a conversão, mas não publicou o destino. A saída recuperável foi preservada em {recovered}.", exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                var recovered = PreserveCompletedOutput(temporaryOutput, output);
                throw new UnauthorizedAccessException($"FFmpeg concluiu a conversão, mas não publicou o destino. A saída recuperável foi preservada em {recovered}.", exception);
            }
            _logger.LogInformation("Exportação FFmpeg concluída: {Container} para {Path}", profile.Container, output);
        }
        finally
        {
            // Uma conversão cancelada, expirada ou com erro só pode deixar saída incompleta.
            // Após êxito do encoder, não apagamos a única cópia válida em caso de falha no move.
            if (!exportCompleted && File.Exists(temporaryOutput))
            {
                try { File.Delete(temporaryOutput); }
                catch (IOException exception) { _logger.LogWarning(exception, "Não foi possível remover temporário incompleto do FFmpeg: {Path}", temporaryOutput); }
                catch (UnauthorizedAccessException exception) { _logger.LogWarning(exception, "Sem permissão para remover temporário incompleto do FFmpeg: {Path}", temporaryOutput); }
            }
        }
    }

    private static IReadOnlyList<string> BuildArguments(string sourcePath, string temporaryOutput, RecordingProfile profile)
    {
        if (profile.Container == RecordingContainer.Gif)
        {
            var filter = $"fps={Math.Min(profile.FramesPerSecond, 30)},scale={profile.Width}:{profile.Height}:force_original_aspect_ratio=decrease:flags=lanczos,pad={profile.Width}:{profile.Height}:(ow-iw)/2:(oh-ih)/2:color=black,split[s0][s1];[s0]palettegen=max_colors=256:stats_mode=diff[p];[s1][p]paletteuse=dither=sierra2_4a";
            return ["-y", "-i", sourcePath, "-vf", filter, "-loop", "0", temporaryOutput];
        }

        var videoCodec = profile.VideoCodec == VideoCodec.Hevc ? "libx265" : "libx264";
        var filterGraph = $"scale={profile.Width}:{profile.Height}:force_original_aspect_ratio=decrease:flags=lanczos,pad={profile.Width}:{profile.Height}:(ow-iw)/2:(oh-ih)/2:color=black";
        return ["-y", "-i", sourcePath, "-map", "0:v:0", "-map", "0:a?", "-vf", filterGraph, "-r", profile.FramesPerSecond.ToString(System.Globalization.CultureInfo.InvariantCulture), "-c:v", videoCodec, "-b:v", profile.Bitrate.ToString(System.Globalization.CultureInfo.InvariantCulture), "-c:a", "aac", "-b:a", "128k", temporaryOutput];
    }

    private static string PreserveCompletedOutput(string temporaryOutput, string output)
    {
        var directory = Path.GetDirectoryName(output)!;
        var extension = Path.GetExtension(output);
        var name = Path.GetFileNameWithoutExtension(output);
        var recovered = Path.Combine(directory, $"{name}-export-recuperavel-{Guid.NewGuid():N}{extension}");
        File.Move(temporaryOutput, recovered);
        return recovered;
    }

    private FfmpegResolution ResolveFfmpeg()
    {
        var configured = _configuredPath();
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!Path.IsPathFullyQualified(configured)) return new(null, "TELAREC_FFMPEG_PATH deve apontar para o caminho absoluto de ffmpeg.exe; não usamos PATH para evitar executável ambíguo.");
            var fullPath = Path.GetFullPath(configured);
            return File.Exists(fullPath)
                ? new(fullPath, $"FFmpeg configurado em '{fullPath}'.")
                : new(null, $"FFmpeg configurado não foi encontrado em '{fullPath}'. Corrija TELAREC_FFMPEG_PATH ou selecione MP4 nativo.");
        }

        var baseDirectory = _baseDirectory();
        foreach (var candidate in new[] { Path.Combine(baseDirectory, "tools", "ffmpeg.exe"), Path.Combine(baseDirectory, "ffmpeg.exe") })
        {
            if (File.Exists(candidate)) return new(candidate, $"FFmpeg localizado em '{candidate}'.");
        }
        return new(null, "FFmpeg não encontrado. MKV, MOV e GIF requerem tools\\ffmpeg.exe junto ao TelaRec ou TELAREC_FFMPEG_PATH absoluto; selecione MP4 para gravação nativa.");
    }

    private sealed record FfmpegResolution(string? Path, string Message);
}

internal interface IFfmpegProcessRunner
{
    Task<FfmpegProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken);
}

internal sealed record FfmpegProcessResult(int ExitCode, string StandardOutput, string StandardError, bool OutputTruncated)
{
    public string Diagnostic
    {
        get
        {
            var diagnostic = string.IsNullOrWhiteSpace(StandardError) ? StandardOutput : StandardError;
            return OutputTruncated ? $"{diagnostic} [saída limitada]" : diagnostic;
        }
    }
}

internal sealed class FfmpegProcessRunner : IFfmpegProcessRunner
{
    internal const int MaximumDiagnosticCharacters = 16 * 1024;

    public async Task<FfmpegProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);

        process.Start();
        var outputTask = ReadLimitedAsync(process.StandardOutput);
        var errorTask = ReadLimitedAsync(process.StandardError);
        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        try
        {
            await process.WaitForExitAsync(linkedCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await StopProcessTreeAsync(process).ConfigureAwait(false);
            await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
            throw;
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            await StopProcessTreeAsync(process).ConfigureAwait(false);
            await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
            throw new TimeoutException($"FFmpeg excedeu o limite de {timeout.TotalSeconds:0} segundos.");
        }
        finally
        {
            if (!process.HasExited) await StopProcessTreeAsync(process).ConfigureAwait(false);
        }

        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);
        return new(process.ExitCode, output.Text, error.Text, output.Truncated || error.Truncated);
    }

    private static async Task StopProcessTreeAsync(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // O processo terminou entre a verificação e o Kill.
        }
        if (!process.HasExited) await process.WaitForExitAsync().ConfigureAwait(false);
    }

    private static async Task<(string Text, bool Truncated)> ReadLimitedAsync(StreamReader reader)
    {
        var buffer = new char[1024];
        var builder = new StringBuilder(MaximumDiagnosticCharacters);
        var truncated = false;
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length)).ConfigureAwait(false)) > 0)
        {
            var remaining = MaximumDiagnosticCharacters - builder.Length;
            if (remaining <= 0) { truncated = true; continue; }
            builder.Append(buffer, 0, Math.Min(read, remaining));
            if (read > remaining) truncated = true;
        }
        return (builder.ToString(), truncated);
    }
}
