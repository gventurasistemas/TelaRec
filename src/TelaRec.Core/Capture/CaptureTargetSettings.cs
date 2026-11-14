namespace TelaRec.Core.Capture;

/// <summary>
/// Representação serializável de um alvo de captura. Handles de janela são
/// revalidados pela camada Windows antes de serem reutilizados.
/// </summary>
public sealed class CaptureTargetSettings
{
    public string Kind { get; set; } = "display";
    public int DisplayIndex { get; set; }
    public long WindowHandle { get; set; }
    public string? WindowTitle { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public static CaptureTargetSettings FromTarget(CaptureTarget target) => target switch
    {
        CaptureTarget.Display display => new CaptureTargetSettings { Kind = "display", DisplayIndex = display.DisplayIndex },
        CaptureTarget.Window window => new CaptureTargetSettings
        {
            Kind = "window",
            WindowHandle = window.Handle.ToInt64(),
            WindowTitle = window.Title
        },
        CaptureTarget.Region region => new CaptureTargetSettings
        {
            Kind = "region",
            X = region.X,
            Y = region.Y,
            Width = region.Width,
            Height = region.Height
        },
        _ => throw new ArgumentOutOfRangeException(nameof(target))
    };

    public bool TryCreateNonWindowTarget(out CaptureTarget? target)
    {
        target = (Kind ?? string.Empty).ToLowerInvariant() switch
        {
            "display" when DisplayIndex >= 0 => new CaptureTarget.Display(DisplayIndex),
            "region" when Width > 0 && Height > 0 => new CaptureTarget.Region(X, Y, Width, Height),
            _ => null
        };
        return target is not null;
    }
}
