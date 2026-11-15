using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TelaRec.App.Wpf.Infrastructure;
using TelaRec.App.Wpf.Resources;
using TelaRec.Core.Scheduling;

namespace TelaRec.App.Wpf.ViewModels;

/// <summary>Agendamento exibido na UI, com estado e motivo localizados.</summary>
public sealed class ScheduledRecordingItem : INotifyPropertyChanged
{
    private ScheduledRecordingStatus _status;

    public ScheduledRecordingItem(ScheduledRecording rule, ScheduledRecordingStatus status, Action<Guid> cancel)
    {
        Id = rule.Id;
        StartsAt = rule.StartsAt.ToLocalTime();
        StopsAt = rule.StopsAt.ToLocalTime();
        _status = status;
        CancelCommand = new RelayCommand(() => cancel(Id), () => CanCancel);
    }

    public Guid Id { get; }
    public DateTimeOffset StartsAt { get; }
    public DateTimeOffset StopsAt { get; }
    public string StartsAtText => StartsAt.ToString("dd/MM HH:mm");
    public string StopsAtText => StopsAt.ToString("dd/MM HH:mm");
    public ScheduledRecordingState State => _status.State;
    public ICommand CancelCommand { get; }

    public string StateLabel => State switch
    {
        ScheduledRecordingState.Pending => Strings.ScheduledStatePending,
        ScheduledRecordingState.Starting => Strings.ScheduledStateStarting,
        ScheduledRecordingState.Recording => Strings.ScheduledStateRecording,
        ScheduledRecordingState.Cancelling => Strings.ScheduledStateCancelling,
        ScheduledRecordingState.Cancelled => Strings.ScheduledStateCancelled,
        ScheduledRecordingState.Completed => Strings.ScheduledStateCompleted,
        ScheduledRecordingState.Rejected => Strings.ScheduledStateRejected,
        _ => Strings.ScheduledStateFailed
    };

    public string Message => _status.MessageCode switch
    {
        ScheduledRecordingMessageCode.Conflict => Strings.ScheduleConflict,
        ScheduledRecordingMessageCode.ActiveRecording => Strings.ScheduleActiveRecording,
        ScheduledRecordingMessageCode.Unavailable => Strings.ScheduleUnavailable,
        ScheduledRecordingMessageCode.StartFailed => Strings.ScheduleStartFailed,
        ScheduledRecordingMessageCode.StopFailed => Strings.ScheduleStopFailed,
        _ => State == ScheduledRecordingState.Completed ? string.Empty : _status.Message ?? string.Empty
    };

    public bool CanCancel => State is ScheduledRecordingState.Pending or ScheduledRecordingState.Starting or ScheduledRecordingState.Recording;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Update(ScheduledRecordingStatus status)
    {
        _status = status;
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(StateLabel));
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(CanCancel));
        ((RelayCommand)CancelCommand).RaiseCanExecuteChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
