namespace TelaRec.App.Wpf.Infrastructure;

/// <summary>
/// Mantém apenas o item visual mais recente e sinaliza quando o consumidor deve
/// agendar um único callback. O produtor pode substituir um frame pendente sem
/// enfileirar trabalho adicional na UI.
/// </summary>
internal sealed class LatestFrameCoalescer<T> where T : class
{
    private readonly object _sync = new();
    private T? _latest;
    private bool _renderScheduled;
    private long _droppedItems;

    public long DroppedItems => Interlocked.Read(ref _droppedItems);

    /// <returns>True somente quando o consumidor precisa agendar um callback.</returns>
    public bool Offer(T item)
    {
        ArgumentNullException.ThrowIfNull(item);
        lock (_sync)
        {
            if (_latest is not null) Interlocked.Increment(ref _droppedItems);
            _latest = item;
            if (_renderScheduled) return false;
            _renderScheduled = true;
            return true;
        }
    }

    public bool TryTake(out T? item)
    {
        lock (_sync)
        {
            item = _latest;
            _latest = null;
            return item is not null;
        }
    }

    /// <returns>True quando existe um novo item e o consumidor deve agendar o próximo callback.</returns>
    public bool CompleteRender()
    {
        lock (_sync)
        {
            if (_latest is not null) return true;
            _renderScheduled = false;
            return false;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _latest = null;
            _renderScheduled = false;
        }
    }
}
