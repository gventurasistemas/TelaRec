using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TelaRec.Core.Contracts;
using TelaRec.Core.Recording;

namespace TelaRec.Infrastructure.Recording;

/// <summary>Backend de pós-processamento isolado para MKV, MOV e GIF.</summary>
public sealed class FfmpegRecordingExporter(ILogger<FfmpegRecordingExporter> logger) : IRecordingExporter
{
    public bool CanExport(RecordingProfile profile) => profile.Container is RecordingContainer.Mkv or RecordingContainer.Mov or RecordingContainer.Gif;

    public async Task ExportAsync(string sourcePath, string destinationPath, RecordingProfile profile, CancellationToken cancellationToken = default)
    {
        if (!CanExport(profile)) throw new NotSupportedException($"Perfil {profile.Container} não é exportado por FFmpeg.");
        var ffmpeg = FindFfmpeg();
        var output = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        if (File.Exists(output)) throw new IOException($"Já existe um arquivo no destino: {output}");
        // O intermediário de Media Foundation já possui um id de sessão único.
        // Derivar o nome do temporário de exportação dele permite ao journal
        // removê-lo com segurança caso o processo seja encerrado durante o FFmpeg.
        var sourceStem = Path.GetFileNameWithoutExtension(Path.GetFullPath(sourcePath));
        var temporaryOutput = Path.Combine(
            Path.GetDirectoryName(output)!,
            $"{sourceStem}.export.partial{Path.GetExtension(output)}");
        var videoCodec = profile.VideoCodec == VideoCodec.Hevc ? "libx265" : "libx264";
        var arguments = profile.Container == RecordingContainer.Gif
            ? $"-y -i {Q(sourcePath)} -vf {Q($"fps={Math.Min(profile.FramesPerSecond, 30)},scale={profile.Width}:{profile.Height}:force_original_aspect_ratio=decrease:flags=lanczos,pad={profile.Width}:{profile.Height}:(ow-iw)/2:(oh-ih)/2:color=black,split[s0][s1];[s0]palettegen=max_colors=256:stats_mode=diff[p];[s1][p]paletteuse=dither=sierra2_4a")} -loop 0 {Q(temporaryOutput)}"
            : $"-y -i {Q(sourcePath)} -map 0:v:0 -map 0:a? -vf {Q($"scale={profile.Width}:{profile.Height}:force_original_aspect_ratio=decrease:flags=lanczos,pad={profile.Width}:{profile.Height}:(ow-iw)/2:(oh-ih)/2:color=black")} -r {profile.FramesPerSecond} -c:v {videoCodec} -b:v {profile.Bitrate} -c:a aac -b:a 128k {Q(temporaryOutput)}";
        logger.LogInformation("Exportando {Container} para {Path}", profile.Container, output);
        try
        {
            using var process = new Process { StartInfo = new ProcessStartInfo(ffmpeg, arguments) { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true } };
            process.Start();
            var error = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (process.ExitCode != 0) throw new InvalidOperationException($"FFmpeg não conseguiu gerar {profile.Container}: {error}");
            if (!File.Exists(temporaryOutput)) throw new InvalidOperationException("FFmpeg terminou sem produzir o arquivo temporário esperado.");
            File.Move(temporaryOutput, output);
        }
        finally
        {
            if (File.Exists(temporaryOutput))
            {
                try { File.Delete(temporaryOutput); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
    private static string FindFfmpeg()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("TELAREC_FFMPEG_PATH"),
            Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg.exe"),
            Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "ffmpeg.exe")
        }.Where(x => !string.IsNullOrWhiteSpace(x) && File.Exists(x)).ToList();
        if (candidates.Count > 0) return candidates[0]!;
        var path = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [];
        var found = path.Select(p => Path.Combine(p, "ffmpeg.exe")).FirstOrDefault(File.Exists);
        return found ?? throw new FileNotFoundException("FFmpeg não encontrado. Distribua tools\\ffmpeg.exe ou defina TELAREC_FFMPEG_PATH.");
    }
    private static string Q(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
}
