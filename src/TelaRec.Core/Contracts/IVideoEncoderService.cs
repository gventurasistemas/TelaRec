using TelaRec.Core.Recording;

namespace TelaRec.Core.Contracts;

public interface IVideoEncoderService : IAsyncDisposable
{
    Task StartAsync(RecordingRequest request, CancellationToken cancellationToken = default);
    Task EncodeAsync(VideoFrame frame, CancellationToken cancellationToken = default);
    Task EncodeAudioAsync(AudioFrame frame, CancellationToken cancellationToken = default);
    Task CompleteAsync(CancellationToken cancellationToken = default);
}
