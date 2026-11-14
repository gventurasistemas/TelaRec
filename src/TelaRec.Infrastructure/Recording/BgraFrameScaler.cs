using TelaRec.Core.Contracts;
using TelaRec.Core.Recording;

namespace TelaRec.Infrastructure.Recording;

/// <summary>Redimensiona BGRA antes do encoder, preservando a proporção com letterbox preto.</summary>
public sealed class BgraFrameScaler : IFrameScaler
{
    public VideoFrame Scale(VideoFrame source, int width, int height)
    {
        if (source.Width == width && source.Height == height) return source;
        var pixels = new byte[width * height * 4];
        var ratio = Math.Min(width / (double)source.Width, height / (double)source.Height);
        var scaledWidth = Math.Max(1, (int)Math.Round(source.Width * ratio));
        var scaledHeight = Math.Max(1, (int)Math.Round(source.Height * ratio));
        var offsetX = (width - scaledWidth) / 2;
        var offsetY = (height - scaledHeight) / 2;
        for (var y = 0; y < scaledHeight; y++)
        {
            var sourceY = Math.Min(source.Height - 1, (int)(y / ratio));
            for (var x = 0; x < scaledWidth; x++)
            {
                var sourceX = Math.Min(source.Width - 1, (int)(x / ratio));
                Buffer.BlockCopy(source.Pixels, sourceY * source.Stride + sourceX * 4,
                    pixels, ((y + offsetY) * width + x + offsetX) * 4, 4);
            }
        }
        return new VideoFrame(pixels, width, height, width * 4, source.Timestamp);
    }
}
