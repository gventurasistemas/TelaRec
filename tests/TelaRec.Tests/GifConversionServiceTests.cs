using TelaRec.Core.Contracts;
using TelaRec.Core.Recording;
using TelaRec.Infrastructure.Recording;

namespace TelaRec.Tests;

public sealed class GifConversionServiceTests
{
    [Fact]
    public async Task Converts_video_and_publishes_gif_below_configured_limit()
    {
        var directory = NewDirectory();
        try
        {
            var source = Path.Combine(directory, "clip.mp4");
            await File.WriteAllBytesAsync(source, [1]);
            var service = new GifConversionService(new FakeFactory(1024));

            var result = await service.ConvertAsync(new GifConversionRequest(source, 2048));

            Assert.True(File.Exists(result.OutputPath));
            Assert.Equal(1024, result.SizeBytes);
            Assert.EndsWith("clip-gif.gif", result.OutputPath, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Deletes_gif_when_it_exceeds_configured_limit()
    {
        var directory = NewDirectory();
        try
        {
            var source = Path.Combine(directory, "clip.mp4");
            await File.WriteAllBytesAsync(source, [1]);
            var service = new GifConversionService(new FakeFactory(2049));

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ConvertAsync(new GifConversionRequest(source, 2048)));

            Assert.Contains("excede", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(Directory.GetFiles(directory, "*.gif"));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Rejects_non_video_input_before_export()
    {
        var directory = NewDirectory();
        try
        {
            var source = Path.Combine(directory, "note.txt");
            await File.WriteAllTextAsync(source, "x");
            var factory = new FakeFactory(1);

            await Assert.ThrowsAsync<NotSupportedException>(() => new GifConversionService(factory).ConvertAsync(new GifConversionRequest(source, 1024)));
            Assert.False(factory.Exporter.Called);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static string NewDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TelaRecGifConversionTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class FakeFactory(long outputLength) : IRecordingExporterFactory
    {
        public FakeExporter Exporter { get; } = new(outputLength);
        public IRecordingExporter GetExporter(RecordingProfile profile) => Exporter;
    }

    private sealed class FakeExporter(long outputLength) : IRecordingExporter
    {
        public bool Called { get; private set; }
        public bool CanExport(RecordingProfile profile) => true;

        public async Task ExportAsync(string sourcePath, string destinationPath, RecordingProfile profile, CancellationToken cancellationToken = default)
        {
            Called = true;
            await File.WriteAllBytesAsync(destinationPath, new byte[outputLength], cancellationToken);
        }
    }
}
