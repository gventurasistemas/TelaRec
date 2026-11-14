namespace TelaRec.Core.Contracts;
public interface IScreenshotService
{
    Task<string> CapturePngAsync(string outputDirectory, CancellationToken cancellationToken = default);
}
