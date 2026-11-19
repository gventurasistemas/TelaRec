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
    public async Task Legacy_settings_are_migrated_and_original_is_preserved_as_backup()
    {
        var directory = NewDirectory();
        var path = Path.Combine(directory, "settings.json");
        try
        {
            await File.WriteAllTextAsync(path, "{ \"FramesPerSecond\": 50, \"SelectedPreset\": \"light-720p\" }");
            var diagnostics = new RecordingDiagnosticsService();

            var settings = await new JsonSettingsService(NullLogger<JsonSettingsService>.Instance, diagnostics, path).LoadAsync();

            Assert.Equal(50, settings.FramesPerSecond);
            Assert.Equal(PersistenceSchemas.Settings, settings.SchemaVersion);
            Assert.Equal(PersistenceSchemas.Presets, settings.PresetSchemaVersion);
            Assert.True(File.Exists(path + ".bak"));
            Assert.DoesNotContain("SchemaVersion", await File.ReadAllTextAsync(path + ".bak"));
            Assert.Contains(diagnostics.Current, item => item.Code == "settings.migrated");
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Previous_default_video_profile_is_upgraded_to_high_quality_60_fps()
    {
        var directory = NewDirectory();
        var path = Path.Combine(directory, "settings.json");
        try
        {
            await File.WriteAllTextAsync(path, """
                { "SchemaVersion": 1, "PresetSchemaVersion": 1, "SelectedPreset": "tutorial-1080p", "OutputWidth": 1920, "OutputHeight": 1080, "FramesPerSecond": 30, "VideoBitrate": 8000000, "Quality": "Alta" }
                """);

            var settings = await CreateService(path).LoadAsync();

            Assert.Equal("high-quality-60", settings.SelectedPreset);
            Assert.Equal(60, settings.FramesPerSecond);
            Assert.Equal(15_000_000, settings.VideoBitrate);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Previous_custom_video_profile_is_not_changed_by_preset_migration()
    {
        var directory = NewDirectory();
        var path = Path.Combine(directory, "settings.json");
        try
        {
            await File.WriteAllTextAsync(path, """
                { "SchemaVersion": 1, "PresetSchemaVersion": 1, "SelectedPreset": "tutorial-1080p", "OutputWidth": 1920, "OutputHeight": 1080, "FramesPerSecond": 30, "VideoBitrate": 12000000, "Quality": "Alta" }
                """);

            var settings = await CreateService(path).LoadAsync();

            Assert.Equal("tutorial-1080p", settings.SelectedPreset);
            Assert.Equal(30, settings.FramesPerSecond);
            Assert.Equal(12_000_000, settings.VideoBitrate);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Newer_settings_schema_is_preserved_and_compatible_backup_is_used()
    {
        var directory = NewDirectory();
        var path = Path.Combine(directory, "settings.json");
        try
        {
            await File.WriteAllTextAsync(path, "{ \"SchemaVersion\": 99, \"FramesPerSecond\": 1 }");
            await File.WriteAllTextAsync(path + ".bak", "{ \"FramesPerSecond\": 48 }");
            var diagnostics = new RecordingDiagnosticsService();

            var settings = await new JsonSettingsService(NullLogger<JsonSettingsService>.Instance, diagnostics, path).LoadAsync();

            Assert.Equal(48, settings.FramesPerSecond);
            Assert.Contains(diagnostics.Current, item => item.Code == "settings.schema_unsupported");
            Assert.Contains("\"SchemaVersion\": 99", await File.ReadAllTextAsync(path));
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
