using System.Reflection;
using TelaRec.App.Wpf;
using TelaRec.App.Wpf.Infrastructure;

namespace TelaRec.Tests;

public sealed class ReleaseIdentityTests
{
    [Theory]
    [InlineData("1.0.0")]
    [InlineData("1.2.3-dev.local+abc123")]
    [InlineData("12.0.1-rc.1")]
    public void Semver_accepts_supported_versions(string version) => Assert.True(ReleaseIdentity.IsValidSemVer(version));

    [Theory]
    [InlineData("v1.0.0")]
    [InlineData("1.0")]
    [InlineData("1.0.0-")]
    public void Semver_rejects_invalid_versions(string version) => Assert.False(ReleaseIdentity.IsValidSemVer(version));

    [Fact]
    public void Assembly_identity_uses_informational_version_and_redacted_commit()
    {
        var assembly = typeof(TelaRec.App.Wpf.App).Assembly;
        var expected = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        var identity = ReleaseIdentity.FromAssembly(assembly);

        Assert.Equal(expected, identity.InformationalVersion);
        Assert.True(ReleaseIdentity.IsValidSemVer(identity.InformationalVersion.Split('+')[0]));
        Assert.DoesNotContain("\\", identity.DisplayVersion);
        Assert.DoesNotContain(":", identity.DisplayVersion);
    }

    [Fact]
    public void Assembly_file_properties_are_centrally_identified()
    {
        var assembly = typeof(TelaRec.App.Wpf.App).Assembly;

        Assert.Equal("TelaRec", assembly.GetCustomAttribute<AssemblyProductAttribute>()!.Product);
        Assert.Equal("Gelson Ventura Gomes", assembly.GetCustomAttribute<AssemblyCompanyAttribute>()!.Company);
        Assert.Equal("1.0.0.0", assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()!.Version);
        Assert.Contains("Gelson Ventura Gomes", assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()!.Copyright);
    }
}
