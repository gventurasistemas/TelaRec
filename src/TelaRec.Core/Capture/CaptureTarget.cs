namespace TelaRec.Core.Capture;

public abstract record CaptureTarget
{
    public sealed record Display(int DisplayIndex = 0) : CaptureTarget;
    public sealed record Window(nint Handle, string Title) : CaptureTarget;
    public sealed record Region(int X, int Y, int Width, int Height, int MonitorIndex = 0) : CaptureTarget
    {
        public bool IsValid => Width > 0 && Height > 0;
    }
}
