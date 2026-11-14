using TelaRec.Core.Recording;

namespace TelaRec.Core.Contracts;

public interface IVideoFrameComposer
{
    void Configure(RecordingOverlaySettings settings);

    /// <summary>Define a área física capturada para projetar cliques no quadro de saída.</summary>
    void SetClickEffectContext(CaptureFrameBounds? source);

    /// <summary>Registra um clique com timestamp já normalizado no relógio da gravação.</summary>
    void AddClickEffect(ClickEvent click);

    void UpdateWebcamFrame(VideoFrame frame);
    VideoFrame Compose(VideoFrame frame);
}
