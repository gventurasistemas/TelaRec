namespace TelaRec.Core.Contracts;

/// <summary>Define quais monitores serão capturados no screenshot.</summary>
public enum ScreenshotScope
{
    /// <summary>Somente o monitor onde o cursor está.</summary>
    MonitorUnderCursor,
    /// <summary>Todos os monitores, um arquivo por monitor.</summary>
    AllMonitors
}

public interface IScreenshotService
{
    /// <summary>
    /// Captura um PNG por monitor e retorna os caminhos gerados.
    /// </summary>
    Task<IReadOnlyList<string>> CapturePngAsync(
        string outputDirectory,
        ScreenshotScope scope = ScreenshotScope.MonitorUnderCursor,
        CancellationToken cancellationToken = default);
}
