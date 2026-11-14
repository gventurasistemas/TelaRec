using TelaRec.Core.Recording;

namespace TelaRec.Core.Contracts;

public interface IVideoFrameComposer
{
    void Configure(RecordingOverlaySettings settings);
    void UpdateWebcamFrame(VideoFrame frame);
    VideoFrame Compose(VideoFrame frame);
}
