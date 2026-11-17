using TelaRec.Core.Recording;

namespace TelaRec.Infrastructure.Recording;

/// <summary>Metadados observados por um decodificador de mídia durante o smoke de integração.</summary>
internal sealed record MediaInspection(
    long FileBytes,
    bool HasMp4Signature,
    VideoCodec? VideoCodec,
    int Width,
    int Height,
    double FramesPerSecond,
    TimeSpan VideoDuration,
    int DecodedVideoSamples,
    int NonBlackVideoSamples,
    TimeSpan? AudioDuration,
    int DecodedAudioSamples);

internal sealed record MediaSmokeExpectation(
    VideoCodec VideoCodec,
    int Width,
    int Height,
    int FramesPerSecond,
    TimeSpan MinimumDuration,
    TimeSpan MaximumDuration,
    bool AudioRequired);

/// <summary>Regras puras para que falhas de mídia sejam determinísticas e testáveis sem desktop interativo.</summary>
internal static class MediaSmokeValidator
{
    public static IReadOnlyList<string> Validate(MediaInspection observed, MediaSmokeExpectation expected)
    {
        var failures = new List<string>();
        if (observed.FileBytes <= 0) failures.Add("arquivo vazio");
        if (!observed.HasMp4Signature) failures.Add("contêiner MP4 inválido ou truncado (caixa ftyp ausente)");
        if (observed.VideoCodec != expected.VideoCodec) failures.Add($"codec de vídeo observado={observed.VideoCodec?.ToString() ?? "ausente"}, esperado={expected.VideoCodec}");
        if (observed.Width != expected.Width || observed.Height != expected.Height)
            failures.Add($"dimensões observadas={observed.Width}x{observed.Height}, esperadas={expected.Width}x{expected.Height}");
        var minimumFps = expected.FramesPerSecond * 0.60;
        var maximumFps = expected.FramesPerSecond * 1.25;
        if (observed.FramesPerSecond < minimumFps || observed.FramesPerSecond > maximumFps)
            failures.Add($"FPS observado={observed.FramesPerSecond:F2}, esperado entre {minimumFps:F2} e {maximumFps:F2}");
        if (observed.VideoDuration < expected.MinimumDuration || observed.VideoDuration > expected.MaximumDuration)
            failures.Add($"duração de vídeo observada={observed.VideoDuration.TotalSeconds:F2}s, esperada entre {expected.MinimumDuration.TotalSeconds:F2}s e {expected.MaximumDuration.TotalSeconds:F2}s");
        if (observed.DecodedVideoSamples == 0) failures.Add("nenhum frame de vídeo foi decodificado");
        if (observed.NonBlackVideoSamples == 0) failures.Add("todos os frames de vídeo decodificados estão vazios ou pretos");
        if (expected.AudioRequired)
        {
            if (observed.DecodedAudioSamples == 0 || observed.AudioDuration is null)
                failures.Add("fluxo de áudio solicitado não foi decodificado");
            else if (Math.Abs((observed.AudioDuration.Value - observed.VideoDuration).TotalMilliseconds) > 250)
                failures.Add($"deriva A/V observada={Math.Abs((observed.AudioDuration.Value - observed.VideoDuration).TotalMilliseconds):F0} ms, máximo=250 ms");
        }
        return failures;
    }
}
