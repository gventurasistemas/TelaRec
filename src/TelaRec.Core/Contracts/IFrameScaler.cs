using TelaRec.Core.Recording;

namespace TelaRec.Core.Contracts;

public interface IFrameScaler
{
    VideoFrame Scale(VideoFrame source, int width, int height);
}
