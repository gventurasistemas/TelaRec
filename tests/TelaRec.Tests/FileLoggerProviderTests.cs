using Microsoft.Extensions.Logging;
using TelaRec.Infrastructure.Reliability;

namespace TelaRec.Tests;

public sealed class FileLoggerProviderTests
{
    [Fact]
    public void Writes_information_and_ignores_debug()
    {
        var directory = NewDirectory();
        try
        {
            using var provider = new FileLoggerProvider(directory, LogLevel.Information);
            var logger = provider.CreateLogger("TelaRec.Teste");

            logger.LogInformation("mensagem visível");
            logger.LogDebug("mensagem oculta");

            var content = ReadAll(directory);
            Assert.Contains("mensagem visível", content);
            Assert.DoesNotContain("mensagem oculta", content);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void Retention_removes_logs_older_than_configured_days()
    {
        var directory = NewDirectory();
        try
        {
            var old = Path.Combine(directory, "telarec-20200101.log");
            File.WriteAllText(old, "antigo");
            File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-30));
            var recent = Path.Combine(directory, "telarec-20260101.log");
            File.WriteAllText(recent, "recente");

            using var provider = new FileLoggerProvider(directory, LogLevel.Information, retentionDays: 14);

            Assert.False(File.Exists(old));
            Assert.True(File.Exists(recent));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void Logging_to_a_missing_directory_does_not_throw()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TelaRecTests", $"invalido-{Guid.NewGuid():N}");
        using var provider = new FileLoggerProvider(directory, LogLevel.Information);
        var logger = provider.CreateLogger("TelaRec.Teste");

        logger.LogError(new InvalidOperationException("falha"), "teste");
        Directory.Delete(directory, true);
        logger.LogError("depois da remoção");
    }

    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "TelaRecLogTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ReadAll(string directory) =>
        string.Join(Environment.NewLine, Directory.GetFiles(directory, "*.log").Select(File.ReadAllText));
}
