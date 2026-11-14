using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TelaRec.Core.Configuration;
using TelaRec.Core.Recording;
using TelaRec.Infrastructure.Persistence;
using TelaRec.Infrastructure.Reliability;

namespace TelaRec.Tests;

public sealed class JsonSettingsServiceTests
{
    [Fact]
    public async Task Round_trip_preserves_settings_and_leaves_no_temporary_file()
    {
        var directory = NewDirectory();
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var service = CreateService(path);
            await service.SaveAsync(new TelaRecSettings
            {
                OutputDirectory = Path.Combine(directory, "out"),
                FramesPerSecond = 60,
                VideoBitrate = 6_000_000,
                OutputWidth = 1280,
                OutputHeight = 720,
                RecordingContainer = RecordingContainer.Mkv,
                VideoCodec = VideoCodec.H264
            });

            var loaded = await CreateService(path).LoadAsync();
            Assert.Equal(60, loaded.FramesPerSecond);
            Assert.Equal(6_000_000, loaded.VideoBitrate);
            Assert.Equal(1280, loaded.OutputWidth);
            Assert.Equal(RecordingContainer.Mkv, loaded.RecordingContainer);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Save_keeps_backup_of_previous_settings()
    {
        var directory = NewDirectory();
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var service = CreateService(path);
            await service.SaveAsync(new TelaRecSettings
            {
                OutputDirectory = Path.Combine(directory, "out"),
                FramesPerSecond = 30
            });
            await service.SaveAsync(new TelaRecSettings
            {
                OutputDirectory = Path.Combine(directory, "out"),
                FramesPerSecond = 60
            });

            Assert.True(File.Exists(path + ".bak"));
            var backup = await File.ReadAllTextAsync(path + ".bak");
            Assert.Contains("\"FramesPerSecond\": 30", backup);
            Assert.Equal(60, (await CreateService(path).LoadAsync()).FramesPerSecond);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ \"FramesPerSecond\": ")]
    public async Task Corrupted_or_empty_file_falls_back_to_backup(string corruptedContent)
    {
        var directory = NewDirectory();
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var expected = new TelaRecSettings
            {
                OutputDirectory = Path.Combine(directory, "out"),
                FramesPerSecond = 45
            };
            await File.WriteAllTextAsync(path, corruptedContent);
            await File.WriteAllTextAsync(path + ".bak", JsonSerializer.Serialize(expected));

            var diagnostics = new RecordingDiagnosticsService();
            var loaded = await new JsonSettingsService(
                NullLogger<JsonSettingsService>.Instance, diagnostics, path).LoadAsync();

            Assert.Equal(45, loaded.FramesPerSecond);
            Assert.Contains(diagnostics.Current, item => item.Code == "settings.recovery");
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Corrupted_file_without_backup_returns_defaults_and_reports()
    {
        var directory = NewDirectory();
        var path = Path.Combine(directory, "settings.json");
        try
        {
            await File.WriteAllTextAsync(path, "{ inválido");
            var diagnostics = new RecordingDiagnosticsService();
            var loaded = await new JsonSettingsService(
                NullLogger<JsonSettingsService>.Instance, diagnostics, path).LoadAsync();

            Assert.Equal(new TelaRecSettings().FramesPerSecond, loaded.FramesPerSecond);
            Assert.Contains(diagnostics.Current, item => item.Code == "settings.defaults");
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Failed_save_does_not_corrupt_existing_settings()
    {
        var directory = NewDirectory();
        var path = Path.Combine(directory, "settings.json");
        try
        {
            var service = CreateService(path);
            await service.SaveAsync(new TelaRecSettings
            {
                OutputDirectory = Path.Combine(directory, "out"),
                FramesPerSecond = 30
            });
            var before = await File.ReadAllTextAsync(path);

            await Assert.ThrowsAnyAsync<Exception>(() => service.SaveAsync(new TelaRecSettings
            {
                OutputDirectory = "C:\\invalid\0path"
            }));

            Assert.Equal(before, await File.ReadAllTextAsync(path));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally { Directory.Delete(directory, true); }
    }

    private static JsonSettingsService CreateService(string path) =>
        new(NullLogger<JsonSettingsService>.Instance, new RecordingDiagnosticsService(), path);

    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "TelaRecSettingsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
