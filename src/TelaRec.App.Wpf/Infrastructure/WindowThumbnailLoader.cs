using System.Windows.Media;

namespace TelaRec.App.Wpf.Infrastructure;

/// <summary>Executa a captura potencialmente bloqueante fora da UI e limita sua espera.</summary>
internal sealed class WindowThumbnailLoader
{
    internal const int MaximumConcurrentCaptures = 3;
    private readonly Func<nint, int, int, ImageSource?> _capture;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _concurrency = new(MaximumConcurrentCaptures, MaximumConcurrentCaptures);

    public WindowThumbnailLoader(Func<nint, int, int, ImageSource?> capture, TimeSpan timeout)
    {
        _capture = capture;
        _timeout = timeout;
    }

    public async Task<ImageSource?> CaptureAsync(nint handle, int width, int height, CancellationToken cancellationToken)
    {
        await _concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
        var captureOwnsSlot = false;
        try
        {
            // PrintWindow pode ficar preso em um processo externo. A chamada não
            // deve ocupar workers compartilhados do ThreadPool nem ultrapassar os
            // slots mantidos até seu término real.
            var capture = Task.Factory.StartNew(
                () => _capture(handle, width, height),
                CancellationToken.None,
                TaskCreationOptions.DenyChildAttach | TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            _ = capture.ContinueWith(
                static (_, state) => ((SemaphoreSlim)state!).Release(),
                _concurrency,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            captureOwnsSlot = true;

            var timeout = Task.Delay(_timeout, cancellationToken);
            if (await Task.WhenAny(capture, timeout).ConfigureAwait(false) != capture) return null;
            return await capture.ConfigureAwait(false);
        }
        catch
        {
            if (!captureOwnsSlot) _concurrency.Release();
            throw;
        }
    }
}
