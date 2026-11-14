using TelaRec.Core.Recording;

namespace TelaRec.Core.Contracts;

/// <summary>Captura cliques globais de forma passiva, apenas durante a gravação.</summary>
public interface IClickCaptureService
{
    event EventHandler<ClickEvent>? ClickCaptured;
    bool IsCapturing { get; }
    void Start();
    void Stop();
}
