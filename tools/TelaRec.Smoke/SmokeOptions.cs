using TelaRec.Core.Recording;

internal enum SmokeTargetMode { Window, Display, Region }

internal sealed record SmokeOptions(
    string OutputPath,
    bool Integration,
    bool SystemAudio,
    bool Microphone,
    bool Silent,
    bool Short,
    bool ClickEffects,
    bool Cursor,
    SmokeTargetMode TargetMode,
    VideoCodec Codec,
    TimeSpan Timeout)
{
    public TimeSpan Duration => Short ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(5);

    public static SmokeOptions Parse(string[] args)
    {
        string? output = null;
        for (var index = 0; index < args.Length; index++)
        {
            if (string.Equals(args[index], "--codec", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[index], "--timeout-seconds", StringComparison.OrdinalIgnoreCase))
            {
                index++;
                continue;
            }
            if (!args[index].StartsWith("--", StringComparison.Ordinal))
            {
                output = args[index];
                break;
            }
        }
        output ??= Path.Combine(Path.GetTempPath(), "TelaRec-smoke.mp4");
        var codec = ReadEnum(args, "--codec", VideoCodec.H264);
        var timeoutSeconds = ReadInt(args, "--timeout-seconds", 30, 5, 300);
        var target = args.Contains("--window", StringComparer.OrdinalIgnoreCase) ? SmokeTargetMode.Window
            : args.Contains("--display", StringComparer.OrdinalIgnoreCase) ? SmokeTargetMode.Display
            : SmokeTargetMode.Region;
        return new SmokeOptions(
            Path.GetFullPath(output),
            args.Contains("--integration", StringComparer.OrdinalIgnoreCase),
            args.Contains("--audio", StringComparer.OrdinalIgnoreCase),
            args.Contains("--microphone", StringComparer.OrdinalIgnoreCase),
            args.Contains("--silent", StringComparer.OrdinalIgnoreCase),
            args.Contains("--short", StringComparer.OrdinalIgnoreCase),
            args.Contains("--click-effects", StringComparer.OrdinalIgnoreCase),
            !args.Contains("--cursor-off", StringComparer.OrdinalIgnoreCase),
            target,
            codec,
            TimeSpan.FromSeconds(timeoutSeconds));
    }

    private static T ReadEnum<T>(string[] args, string option, T fallback) where T : struct, Enum
    {
        var index = Array.FindIndex(args, argument => string.Equals(argument, option, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length && Enum.TryParse<T>(args[index + 1], true, out var parsed) ? parsed : fallback;
    }

    private static int ReadInt(string[] args, string option, int fallback, int minimum, int maximum)
    {
        var index = Array.FindIndex(args, argument => string.Equals(argument, option, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var parsed)
            ? Math.Clamp(parsed, minimum, maximum)
            : fallback;
    }
}
