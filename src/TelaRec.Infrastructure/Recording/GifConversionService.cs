using TelaRec.Core.Contracts;
using TelaRec.Core.Recording;

namespace TelaRec.Infrastructure.Recording;

/// <summary>Converte vídeos existentes em GIF e só publica resultados dentro do limite solicitado.</summary>
public sealed class GifConversionService(IRecordingExporterFactory exporters) : IGifConversionService
{
    private static readonly HashSet<string> SupportedVideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".mov", ".avi", ".webm", ".wmv"
    };

    public async Task<GifConversionResult> ConvertAsync(GifConversionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourcePath);
        if (request.MaximumOutputBytes <= 0) throw new ArgumentOutOfRangeException(nameof(request.MaximumOutputBytes));
        if (request.Width <= 0 || request.Height <= 0 || request.FramesPerSecond is < 1 or > 30)
            throw new ArgumentOutOfRangeException(nameof(request), "O perfil de conversão GIF é inválido.");

        var source = Path.GetFullPath(request.SourcePath);
        if (!File.Exists(source)) throw new FileNotFoundException("O vídeo selecionado não foi encontrado.", source);
        if (!SupportedVideoExtensions.Contains(Path.GetExtension(source)))
            throw new NotSupportedException("Selecione um arquivo de vídeo compatível (MP4, MKV, MOV, AVI, WebM ou WMV).");

        var destination = BuildAvailableOutputPath(source);
        var profile = new RecordingProfile(RecordingContainer.Gif, VideoCodec.Gif, 1_000_000, request.FramesPerSecond, request.Width, request.Height);
        var exporter = exporters.GetExporter(profile);
        await exporter.ExportAsync(source, destination, profile, cancellationToken).ConfigureAwait(false);

        var size = new FileInfo(destination).Length;
        if (size <= request.MaximumOutputBytes) return new GifConversionResult(destination, size);

        try { File.Delete(destination); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        throw new InvalidOperationException($"O GIF gerado ({FormatSize(size)}) excede o limite configurado de {FormatSize(request.MaximumOutputBytes)}. Reduza a duração ou aumente o limite.");
    }

    private static string BuildAvailableOutputPath(string source)
    {
        var directory = Path.GetDirectoryName(source)!;
        var stem = Path.GetFileNameWithoutExtension(source) + "-gif";
        var destination = Path.Combine(directory, stem + ".gif");
        for (var suffix = 1; File.Exists(destination); suffix++)
            destination = Path.Combine(directory, $"{stem}-{suffix}.gif");
        return destination;
    }

    private static string FormatSize(long bytes) => $"{bytes / (1024d * 1024d):0.##} MB";
}
