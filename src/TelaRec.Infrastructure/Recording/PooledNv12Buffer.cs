using System.Buffers;
using System.Runtime.InteropServices;

namespace TelaRec.Infrastructure.Recording;

/// <summary>
/// Buffer NV12 temporário com posse explícita. Ele só é devolvido ao pool depois
/// de ser copiado para o buffer nativo do Media Foundation.
/// </summary>
internal sealed class PooledNv12Buffer : IDisposable
{
    private readonly ArrayPool<byte> _pool;
    private byte[]? _buffer;

    private PooledNv12Buffer(ArrayPool<byte> pool, byte[] buffer, int length)
    {
        _pool = pool;
        _buffer = buffer;
        Length = length;
    }

    public int Length { get; }
    public Span<byte> Span => (_buffer ?? throw new ObjectDisposedException(nameof(PooledNv12Buffer))).AsSpan(0, Length);

    public static PooledNv12Buffer Rent(int length, ArrayPool<byte>? pool = null)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
        var selectedPool = pool ?? ArrayPool<byte>.Shared;
        return new PooledNv12Buffer(selectedPool, selectedPool.Rent(length), length);
    }

    public void CopyTo(IntPtr destination)
    {
        var buffer = _buffer ?? throw new ObjectDisposedException(nameof(PooledNv12Buffer));
        Marshal.Copy(buffer, 0, destination, Length);
    }

    public void Dispose()
    {
        var buffer = Interlocked.Exchange(ref _buffer, null);
        if (buffer is not null) _pool.Return(buffer);
    }
}
