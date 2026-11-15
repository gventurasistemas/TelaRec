using Microsoft.Extensions.Logging;
using TelaRec.Core.Contracts;
using TelaRec.Core.Reliability;
using TelaRec.Core.Recording;
using TelaRec.Core.Scheduling;

namespace TelaRec.Infrastructure.Reliability;

/// <summary>
/// Agendador em memória. O acesso às regras usa uma única disciplina de lock;
/// início/parada de gravação ocorrem fora do lock para não bloquear cancelamentos.
/// </summary>
public sealed class RecordingScheduler : IRecordingScheduler
{
    private readonly IRecordingService _recording;
    private readonly IRecordingAvailabilityProbe _availability;
    private readonly ILogger<RecordingScheduler> _logger;
    private readonly IRecordingDiagnostics _diagnostics;
    private readonly IScheduledRecordingStore? _store;
    private readonly Dictionary<Guid, (ScheduledRecording Rule, ScheduledRecordingStatus Status)> _rules = [];
    private readonly Dictionary<Guid, TaskCompletionSource> _cancellationCompletions = [];
    private readonly TimeProvider _timeProvider;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _tickGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly System.Threading.Timer _timer;
    private volatile bool _disposed;
    private readonly TaskCompletionSource _initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim _persistGate = new(1, 1);
    private int _initializationStarted;

    public RecordingScheduler(IRecordingService recording, IRecordingAvailabilityProbe availability, IRecordingDiagnostics diagnostics, ILogger<RecordingScheduler> logger, IScheduledRecordingStore? store = null, TimeProvider? timeProvider = null)
    {
        _recording = recording;
        _availability = availability;
        _diagnostics = diagnostics;
        _logger = logger;
        _store = store;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _timer = new System.Threading.Timer(static state => ((RecordingScheduler)state!).QueueTick(), this,
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public IReadOnlyList<ScheduledRecordingStatus> Scheduled
    {
        get { lock (_sync) return _rules.Values.Select(value => value.Status).ToArray(); }
    }

    public IReadOnlyList<ScheduledRecording> ScheduledRules
    {
        get { lock (_sync) return _rules.Values.Select(value => value.Rule).ToArray(); }
    }

    public event EventHandler<ScheduledRecordingStatus>? StatusChanged;

    public Task ScheduleAsync(ScheduledRecording schedule, CancellationToken cancellationToken = default)
    {
        return ScheduleCoreAsync(schedule, cancellationToken);
    }

    private async Task ScheduleCoreAsync(ScheduledRecording schedule, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!schedule.IsValidAt(_timeProvider.GetUtcNow())) throw new ArgumentException("O período agendado deve estar no futuro e possuir fim posterior ao início.");
        ScheduledRecordingStatus status;
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_rules.Values.Any(value => value.Rule.StartsAt < schedule.StopsAt && schedule.StartsAt < value.Rule.StopsAt))
                throw new InvalidOperationException("O agendamento conflita com outra gravação.");
            status = new ScheduledRecordingStatus(schedule.Id, ScheduledRecordingState.Pending);
            _rules[schedule.Id] = (schedule, status);
        }
        Publish(status);
        await PersistAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task CancelAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        ScheduledRecordingStatus? published = null;
        Task? inFlightCancellation = null;
        TaskCompletionSource? cancellationCompletion = null;
        var mustStopRecording = false;

        lock (_sync)
        {
            if (!_rules.TryGetValue(scheduleId, out var current)) return;

            if (current.Status.State == ScheduledRecordingState.Cancelling)
            {
                inFlightCancellation = _cancellationCompletions.TryGetValue(scheduleId, out var existing)
                    ? existing.Task
                    : Task.CompletedTask;
            }
            else if (current.Status.State is ScheduledRecordingState.Starting or ScheduledRecordingState.Recording)
            {
                published = new ScheduledRecordingStatus(scheduleId, ScheduledRecordingState.Cancelling);
                _rules[scheduleId] = (current.Rule, published);
                cancellationCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _cancellationCompletions[scheduleId] = cancellationCompletion;
                mustStopRecording = true;
            }
            else
            {
                _rules.Remove(scheduleId);
                published = new ScheduledRecordingStatus(scheduleId, ScheduledRecordingState.Cancelled);
            }
        }

        if (inFlightCancellation is not null)
        {
            await inFlightCancellation.WaitAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!mustStopRecording)
        {
            if (published is not null) Publish(published);
            await PersistAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        Publish(published!);
        try
        {
            // O tick pode ter marcado o item como Starting e ainda estar dentro de
            // StartAsync. Esperar seu término evita a corrida Start -> Cancel -> Start.
            await _tickGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            _tickGate.Release();
            await _recording.StopAsync(cancellationToken).ConfigureAwait(false);

            lock (_sync)
            {
                _rules.Remove(scheduleId);
                _cancellationCompletions.Remove(scheduleId);
            }
            var cancelled = new ScheduledRecordingStatus(scheduleId, ScheduledRecordingState.Cancelled);
            Publish(cancelled);
            cancellationCompletion!.TrySetResult();
            await PersistAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            ScheduledRecordingStatus? restored = null;
            lock (_sync)
            {
                if (_rules.TryGetValue(scheduleId, out var current) && current.Status.State == ScheduledRecordingState.Cancelling)
                {
                    restored = new ScheduledRecordingStatus(scheduleId, ScheduledRecordingState.Recording, exception.Message, ScheduledRecordingMessageCode.StopFailed);
                    _rules[scheduleId] = (current.Rule, restored);
                }
                _cancellationCompletions.Remove(scheduleId);
            }
            if (restored is not null) Publish(restored);
            cancellationCompletion!.TrySetException(exception);
            throw;
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_store is null)
        {
            _initialized.TrySetResult();
            return;
        }
        if (Interlocked.Exchange(ref _initializationStarted, 1) == 0)
        {
            try
            {
                var persisted = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
                var now = _timeProvider.GetUtcNow();
                lock (_sync)
                {
                    foreach (var item in persisted)
                    {
                        if (item.Rule.StopsAt <= now || item.Status.State is ScheduledRecordingState.Completed or ScheduledRecordingState.Rejected or ScheduledRecordingState.Failed)
                            continue;
                        if (item.Rule.IsValidAt(now) || item.Status.State is ScheduledRecordingState.Starting or ScheduledRecordingState.Recording or ScheduledRecordingState.Cancelling)
                        {
                            var restoredStatus = item.Status.State is ScheduledRecordingState.Starting or ScheduledRecordingState.Recording or ScheduledRecordingState.Cancelling
                                ? new ScheduledRecordingStatus(item.Rule.Id, ScheduledRecordingState.Pending)
                                : item.Status;
                            _rules[item.Rule.Id] = (item.Rule, restoredStatus);
                        }
                    }
                }
                _initialized.TrySetResult();
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Falha ao restaurar agendamentos persistidos");
                _diagnostics.Report(DiagnosticSeverity.Warning, "schedule.restore_failed", "Não foi possível restaurar os agendamentos persistidos.");
                _initialized.TrySetResult();
            }
        }
        await _initialized.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Processa os horários vencidos uma vez; útil para hosts e testes determinísticos.</summary>
    public Task ProcessDueAsync(CancellationToken cancellationToken = default) => TickAsync(cancellationToken);

    private void QueueTick()
    {
        if (_disposed) return;
        _ = TickSafeAsync();
    }

    private async Task TickSafeAsync()
    {
        try { await TickAsync(_lifetime.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception) { _logger.LogError(exception, "Falha no ciclo do agendador"); }
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _tickGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            var now = _timeProvider.GetUtcNow();
            ScheduledRecording[] starts;
            ScheduledRecording[] stops;
            lock (_sync)
            {
                starts = _rules.Values
                    .Where(value => value.Status.State == ScheduledRecordingState.Pending && now >= value.Rule.StartsAt)
                    .Select(value => value.Rule)
                    .ToArray();
                stops = _rules.Values
                    .Where(value => value.Status.State == ScheduledRecordingState.Recording && now >= value.Rule.StopsAt)
                    .Select(value => value.Rule)
                    .ToArray();
            }

            foreach (var schedule in starts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryTransition(schedule.Id, ScheduledRecordingState.Pending, ScheduledRecordingState.Starting)) continue;
                if (now >= schedule.StopsAt)
                {
                    SetIfCurrent(schedule.Id, ScheduledRecordingState.Starting,
                        new ScheduledRecordingStatus(schedule.Id, ScheduledRecordingState.Rejected,
                            "O horário de término já passou.", ScheduledRecordingMessageCode.Unavailable));
                    continue;
                }
                if (_recording.Status.State is not (RecordingState.Idle or RecordingState.Completed or RecordingState.Failed))
                {
                    Reject(schedule.Id, ScheduledRecordingState.Starting, "Já existe uma gravação ativa.", ScheduledRecordingMessageCode.ActiveRecording);
                    continue;
                }
                var available = _availability.Check();
                if (!available.CanRecord)
                {
                    Reject(schedule.Id, ScheduledRecordingState.Starting, available.Reason ?? "Dispositivo indisponível.", ScheduledRecordingMessageCode.Unavailable);
                    continue;
                }
                try
                {
                    if (!IsCurrent(schedule.Id, ScheduledRecordingState.Starting)) continue;
                    await _recording.StartAsync(schedule.Request, cancellationToken).ConfigureAwait(false);
                    SetIfCurrent(schedule.Id, ScheduledRecordingState.Starting,
                        new ScheduledRecordingStatus(schedule.Id, ScheduledRecordingState.Recording));
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Falha ao iniciar agendamento {ScheduleId}", schedule.Id);
                    SetIfCurrent(schedule.Id, ScheduledRecordingState.Starting,
                        new ScheduledRecordingStatus(schedule.Id, ScheduledRecordingState.Failed, exception.Message, ScheduledRecordingMessageCode.StartFailed));
                }
            }

            foreach (var schedule in stops)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await _recording.StopAsync(cancellationToken).ConfigureAwait(false);
                    SetIfCurrent(schedule.Id, ScheduledRecordingState.Recording,
                        new ScheduledRecordingStatus(schedule.Id, ScheduledRecordingState.Completed));
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Falha ao encerrar agendamento {ScheduleId}", schedule.Id);
                    SetIfCurrent(schedule.Id, ScheduledRecordingState.Recording,
                        new ScheduledRecordingStatus(schedule.Id, ScheduledRecordingState.Failed, exception.Message, ScheduledRecordingMessageCode.StopFailed));
                }
            }
        }
        finally
        {
            _tickGate.Release();
            await PersistAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        if (_store is null) return;
        var snapshot = lockSnapshot();
        await _persistGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await _store.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Falha ao persistir agendamentos");
            _diagnostics.Report(DiagnosticSeverity.Warning, "schedule.persist_failed", "Não foi possível salvar os agendamentos.");
        }
        finally { _persistGate.Release(); }

        IReadOnlyCollection<PersistedScheduledRecording> lockSnapshot()
        {
            lock (_sync)
                return _rules.Values.Select(value => new PersistedScheduledRecording(value.Rule, value.Status)).ToArray();
        }
    }

    private void Reject(Guid id, ScheduledRecordingState expected, string message, ScheduledRecordingMessageCode code)
    {
        SetIfCurrent(id, expected, new ScheduledRecordingStatus(id, ScheduledRecordingState.Rejected, message, code));
        _diagnostics.Report(DiagnosticSeverity.Warning, "schedule.rejected", message);
    }

    private bool TryTransition(Guid id, ScheduledRecordingState expected, ScheduledRecordingState next)
    {
        ScheduledRecordingStatus? status = null;
        lock (_sync)
        {
            if (!_rules.TryGetValue(id, out var current) || current.Status.State != expected) return false;
            status = new ScheduledRecordingStatus(id, next);
            _rules[id] = (current.Rule, status);
        }
        Publish(status);
        return true;
    }

    private bool IsCurrent(Guid id, ScheduledRecordingState state)
    {
        lock (_sync) return _rules.TryGetValue(id, out var current) && current.Status.State == state;
    }

    private void SetIfCurrent(Guid id, ScheduledRecordingState expected, ScheduledRecordingStatus status)
    {
        var publish = false;
        lock (_sync)
        {
            if (!_rules.TryGetValue(id, out var current) || current.Status.State != expected) return;
            _rules[id] = (current.Rule, status);
            publish = true;
        }
        if (publish) Publish(status);
    }

    private void Publish(ScheduledRecordingStatus status)
    {
        try { StatusChanged?.Invoke(this, status); }
        catch (Exception exception) { _logger.LogWarning(exception, "Observador do agendador falhou para {ScheduleId}", status.Id); }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(RecordingScheduler));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Dispose();
        _lifetime.Cancel();
        try
        {
            await _tickGate.WaitAsync().ConfigureAwait(false);
            _tickGate.Release();
        }
        finally
        {
            // O semáforo permanece válido para callbacks de Timer já enfileirados;
            // eles apenas observam o token cancelado e retornam sem tocar nas regras.
        }
    }
}
