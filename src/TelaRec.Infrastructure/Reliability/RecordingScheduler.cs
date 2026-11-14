using Microsoft.Extensions.Logging;
using TelaRec.Core.Contracts;
using TelaRec.Core.Scheduling;
using TelaRec.Core.Recording;
using TelaRec.Core.Reliability;

namespace TelaRec.Infrastructure.Reliability;

public sealed class RecordingScheduler : IRecordingScheduler
{
    private readonly IRecordingService _recording;
    private readonly IRecordingAvailabilityProbe _availability;
    private readonly ILogger<RecordingScheduler> _logger;
    private readonly IRecordingDiagnostics _diagnostics;
    private readonly Dictionary<Guid, (ScheduledRecording Rule, ScheduledRecordingStatus Status)> _rules = [];
    private readonly System.Threading.Timer _timer;
    private readonly SemaphoreSlim _gate = new(1, 1);
    public RecordingScheduler(IRecordingService recording, IRecordingAvailabilityProbe availability, IRecordingDiagnostics diagnostics, ILogger<RecordingScheduler> logger)
    {
        _recording = recording; _availability = availability; _diagnostics = diagnostics; _logger = logger;
        _timer = new System.Threading.Timer(_ => _ = TickAsync(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }
    public IReadOnlyList<ScheduledRecordingStatus> Scheduled { get { lock (_rules) return _rules.Values.Select(value => value.Status).ToArray(); } }
    public event EventHandler<ScheduledRecordingStatus>? StatusChanged;
    public async Task ScheduleAsync(ScheduledRecording schedule, CancellationToken cancellationToken = default)
    {
        if (!schedule.IsValid) throw new ArgumentException("O período agendado deve estar no futuro e possuir fim posterior ao início.");
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_rules.Values.Any(value => value.Rule.StartsAt < schedule.StopsAt && schedule.StartsAt < value.Rule.StopsAt))
                throw new InvalidOperationException("O agendamento conflita com outra gravação.");
            var status = new ScheduledRecordingStatus(schedule.Id, ScheduledRecordingState.Pending);
            _rules[schedule.Id] = (schedule, status);
            StatusChanged?.Invoke(this, status);
        }
        finally { _gate.Release(); }
    }
    public Task CancelAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        lock (_rules) _rules.Remove(scheduleId);
        return Task.CompletedTask;
    }
    private async Task TickAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (var item in _rules.Values.ToArray())
            {
                var now = DateTimeOffset.UtcNow;
                if (item.Status.State == ScheduledRecordingState.Pending && now >= item.Rule.StartsAt)
                {
                    if (_recording.Status.State is not (RecordingState.Idle or RecordingState.Completed or RecordingState.Failed)) { Reject(item.Rule.Id, "Já existe uma gravação ativa."); continue; }
                    var available = _availability.Check();
                    if (!available.CanRecord) { Reject(item.Rule.Id, available.Reason ?? "Dispositivo indisponível."); continue; }
                    try { await _recording.StartAsync(item.Rule.Request).ConfigureAwait(false); Set(item.Rule.Id, new(item.Rule.Id, ScheduledRecordingState.Recording)); }
                    catch (Exception exception) { _logger.LogError(exception, "Falha ao iniciar agendamento {ScheduleId}", item.Rule.Id); Set(item.Rule.Id, new(item.Rule.Id, ScheduledRecordingState.Failed, exception.Message)); }
                }
                if (item.Status.State == ScheduledRecordingState.Recording && now >= item.Rule.StopsAt)
                {
                    try { await _recording.StopAsync().ConfigureAwait(false); Set(item.Rule.Id, new(item.Rule.Id, ScheduledRecordingState.Completed)); }
                    catch (Exception exception) { Set(item.Rule.Id, new(item.Rule.Id, ScheduledRecordingState.Failed, exception.Message)); }
                }
            }
        }
        finally { _gate.Release(); }
    }
    private void Reject(Guid id, string message)
    {
        Set(id, new(id, ScheduledRecordingState.Rejected, message));
        _diagnostics.Report(DiagnosticSeverity.Warning, "schedule.rejected", message);
    }
    private void Set(Guid id, ScheduledRecordingStatus status)
    {
        var rule = _rules.TryGetValue(id, out var current) ? current.Rule : throw new InvalidOperationException();
        _rules[id] = (rule, status); StatusChanged?.Invoke(this, status);
    }
    public ValueTask DisposeAsync() { _timer.Dispose(); _gate.Dispose(); return ValueTask.CompletedTask; }
}
