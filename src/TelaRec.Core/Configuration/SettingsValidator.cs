using TelaRec.Core.Recording;

namespace TelaRec.Core.Configuration;

public enum SettingsValidationCode
{
    OutputDirectoryEmpty,
    OutputDirectoryNotAbsolute,
    OutputDirectoryUnavailable,
    FramesPerSecondOutOfRange,
    VideoBitrateOutOfRange,
    ResolutionOutOfRange,
    ResolutionMustBeEven,
    ProfileNotSupported
}

public sealed record SettingsValidationIssue(SettingsValidationCode Code, string Message);

/// <summary>Validação central das configurações usadas para iniciar uma gravação.</summary>
public static class SettingsValidator
{
    public const int MinimumFps = 1;
    public const int MaximumFps = 120;
    public const int MinimumBitrate = 500_000;
    public const int MaximumBitrate = 80_000_000;

    public static IReadOnlyList<SettingsValidationIssue> Validate(TelaRecSettings settings, bool verifyOutputDirectory = true)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var issues = new List<SettingsValidationIssue>();
        ValidateOutputDirectory(settings.OutputDirectory, verifyOutputDirectory, issues);
        ValidateProfile(settings, issues);
        return issues;
    }

    private static void ValidateOutputDirectory(string? directory, bool verify, List<SettingsValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            issues.Add(new(SettingsValidationCode.OutputDirectoryEmpty, "Informe uma pasta de saída."));
            return;
        }

        if (!Path.IsPathFullyQualified(directory))
        {
            issues.Add(new(SettingsValidationCode.OutputDirectoryNotAbsolute, "A pasta de saída deve ser um caminho absoluto."));
            return;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(directory);
        }
        catch (Exception)
        {
            issues.Add(new(SettingsValidationCode.OutputDirectoryUnavailable, "A pasta de saída contém um caminho inválido."));
            return;
        }

        if (!verify) return;

        try
        {
            Directory.CreateDirectory(fullPath);
            var probe = Path.Combine(fullPath, $".telarec-write-test-{Guid.NewGuid():N}.tmp");
            File.WriteAllBytes(probe, [0]);
            File.Delete(probe);
        }
        catch (Exception)
        {
            issues.Add(new(SettingsValidationCode.OutputDirectoryUnavailable, $"A pasta de saída não pode ser criada ou escrita: {fullPath}"));
        }
    }

    private static void ValidateProfile(TelaRecSettings settings, List<SettingsValidationIssue> issues)
    {
        var profileIssues = 0;
        if (settings.FramesPerSecond is < MinimumFps or > MaximumFps)
        {
            issues.Add(new(SettingsValidationCode.FramesPerSecondOutOfRange, $"O FPS deve ficar entre {MinimumFps} e {MaximumFps}."));
            profileIssues++;
        }

        if (settings.VideoBitrate is < MinimumBitrate or > MaximumBitrate)
        {
            issues.Add(new(SettingsValidationCode.VideoBitrateOutOfRange, $"O bitrate deve ficar entre {MinimumBitrate} e {MaximumBitrate} bps."));
            profileIssues++;
        }

        if (settings.OutputWidth <= 0 || settings.OutputHeight <= 0)
        {
            issues.Add(new(SettingsValidationCode.ResolutionOutOfRange, "A resolução deve ter largura e altura positivas."));
            profileIssues++;
        }
        else if (settings.OutputWidth % 2 != 0 || settings.OutputHeight % 2 != 0)
        {
            issues.Add(new(SettingsValidationCode.ResolutionMustBeEven, "A resolução deve ter largura e altura pares para este formato."));
            profileIssues++;
        }

        if (profileIssues > 0) return;

        if (settings.RecordingContainer == RecordingContainer.Gif || settings.VideoCodec == VideoCodec.Gif)
        {
            issues.Add(new(SettingsValidationCode.ProfileNotSupported, "GIF é gerado pela conversão de um vídeo existente; selecione um formato de vídeo para gravar."));
            return;
        }

        var codec = settings.VideoCodec;
        var profile = new RecordingProfile(
            settings.RecordingContainer,
            codec,
            settings.VideoBitrate,
            settings.FramesPerSecond,
            settings.OutputWidth,
            settings.OutputHeight);
        if (!profile.IsValid)
            issues.Add(new(SettingsValidationCode.ProfileNotSupported, "A combinação de formato, codec, resolução, FPS e bitrate não é suportada."));
    }
}
