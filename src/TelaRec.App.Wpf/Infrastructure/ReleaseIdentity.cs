using System.Reflection;
using System.Text.RegularExpressions;

namespace TelaRec.App.Wpf.Infrastructure;

/// <summary>Identidade de release exibida ao usuário e registrada sem dados da máquina.</summary>
public sealed record ReleaseIdentity(string Product, string InformationalVersion, string Commit)
{
    private static readonly Regex SemVer = new("^(0|[1-9]\\d*)\\.(0|[1-9]\\d*)\\.(0|[1-9]\\d*)(?:-[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?(?:\\+[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?$", RegexOptions.CultureInvariant);

    public string DisplayVersion => $"{InformationalVersion} ({Commit})";

    public static ReleaseIdentity FromAssembly(Assembly assembly)
    {
        var product = assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "TelaRec";
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString(3)
            ?? "0.0.0-dev.local";
        var commit = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Key, "SourceRevisionId", StringComparison.Ordinal))?.Value
            ?? "local";
        return new ReleaseIdentity(product, informationalVersion, commit.Length > 12 ? commit[..12] : commit);
    }

    public static bool IsValidSemVer(string value) => SemVer.IsMatch(value);
}
