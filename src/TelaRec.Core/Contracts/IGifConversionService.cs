namespace TelaRec.Core.Contracts;

/// <summary>Converte um arquivo de vídeo existente em GIF sem envolver uma sessão de gravação.</summary>
public interface IGifConversionService
{
    Task<GifConversionResult> ConvertAsync(GifConversionRequest request, CancellationToken cancellationToken = default);
}

public sealed record GifConversionRequest(
    string SourcePath,
    long MaximumOutputBytes,
    int Width = 800,
    int Height = 450,
    int FramesPerSecond = 15);

public sealed record GifConversionResult(string OutputPath, long SizeBytes);
