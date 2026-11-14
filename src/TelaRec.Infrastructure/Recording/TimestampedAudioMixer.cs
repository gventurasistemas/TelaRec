using TelaRec.Core.Recording;

namespace TelaRec.Infrastructure.Recording;

public enum AudioSource { System, Microphone }

/// <summary>Alinha blocos das fontes em janelas de 10 ms e preenche a fonte ausente com silêncio.</summary>
public sealed class TimestampedAudioMixer
{
    private const long BucketTicks = TimeSpan.TicksPerSecond / 100;
    private const long HoldTicks = TimeSpan.TicksPerSecond / 10;
    private sealed class Pending { public AudioFrame? System; public AudioFrame? Microphone; }
    private readonly SortedDictionary<long, Pending> _pending = [];
    private readonly AudioMixSettings _settings;
    private long _latestTicks;
    private TimeSpan? _systemOrigin;
    private TimeSpan? _microphoneOrigin;
    private TimeSpan? _timelineOrigin;
    private TimeSpan? _lastSystemElapsed;
    private TimeSpan? _lastMicrophoneElapsed;

    public TimestampedAudioMixer(AudioMixSettings settings) => _settings = settings;
    public TimeSpan MaxSourceDrift { get; private set; }

    public void Add(AudioSource source, AudioFrame frame)
    {
        if (frame.SampleRate != 48_000 || frame.Channels != 2 || frame.Pcm16.Length == 0) return;
        var timestamp = NormalizeSourceTimestamp(source, frame.Timestamp);
        frame = frame with { Timestamp = timestamp };
        var bucket = Math.Max(0, timestamp.Ticks / BucketTicks * BucketTicks);
        if (!_pending.TryGetValue(bucket, out var item)) _pending[bucket] = item = new Pending();
        if (source == AudioSource.System)
        {
            item.System = frame;
            _lastSystemElapsed = frame.Timestamp - _timelineOrigin!.Value;
        }
        else
        {
            item.Microphone = frame;
            _lastMicrophoneElapsed = frame.Timestamp - _timelineOrigin!.Value;
        }
        _latestTicks = Math.Max(_latestTicks, frame.Timestamp.Ticks);
        if (_lastSystemElapsed is { } system && _lastMicrophoneElapsed is { } microphone)
        {
            // O callback da outra fonte pode ser do bloco anterior; só compara
            // blocos temporalmente vizinhos para não confundir cadência com drift.
            var drift = TimeSpan.FromTicks(Math.Abs((system - microphone).Ticks));
            if (drift <= TimeSpan.FromMilliseconds(250))
                MaxSourceDrift = TimeSpan.FromTicks(Math.Max(MaxSourceDrift.Ticks, drift.Ticks));
        }
    }

    private TimeSpan NormalizeSourceTimestamp(AudioSource source, TimeSpan timestamp)
    {
        _timelineOrigin ??= timestamp;
        if (source == AudioSource.System)
        {
            _systemOrigin ??= timestamp;
            return _timelineOrigin.Value + (timestamp - _systemOrigin.Value);
        }
        _microphoneOrigin ??= timestamp;
        return _timelineOrigin.Value + (timestamp - _microphoneOrigin.Value);
    }

    public IEnumerable<AudioFrame> DrainReady(bool final = false)
    {
        while (_pending.Count > 0)
        {
            var pair = _pending.First();
            var hasBoth = pair.Value.System is not null && pair.Value.Microphone is not null;
            var waitingForBoth = _settings.SystemEnabled && _settings.MicrophoneEnabled && !hasBoth;
            if (waitingForBoth && !final && _latestTicks < pair.Key + HoldTicks) yield break;

            _pending.Remove(pair.Key);
            var system = _settings.SystemEnabled ? pair.Value.System?.Pcm16 : null;
            var microphone = _settings.MicrophoneEnabled ? pair.Value.Microphone?.Pcm16 : null;
            var bytes = PcmAudioMixer.MixPcm16(system, microphone, _settings);
            if (bytes.Length > 0) yield return new AudioFrame(bytes, 48_000, 2, TimeSpan.FromTicks(pair.Key));
        }
    }
}
