using System.Windows.Media;

namespace TelaRec.App.Wpf.Infrastructure;

/// <summary>Executa a captura potencialmente bloqueante fora da UI e limita sua espera.</summary>
internal sealed class WindowThumbnailLoader
{
    internal const int MaximumConcurrentCaptures = 3;
    private readonly Func<nint, int, int, ImageSource?> _capture;
    private readonly TimeSpan _timeout;

    public WindowThumbnailLoader(Func<nint, int, int, ImageSource?> capture, TimeSpan timeout)
    {
        _capture = capture;
        _timeout = timeout;
    }

    public async Task<ImageSource?> CaptureAsync(nint handle, int width, int height, CancellationToken cancellationToken)
    {
        var capture = Task.Run(() => _capture(handle, width, height));
        var timeout = Task.Delay(_timeout, cancellationToken);
        if (await Task.WhenAny(capture, timeout).ConfigureAwait(false) != capture) return null;
        return await capture.ConfigureAwait(false);
    }
}
