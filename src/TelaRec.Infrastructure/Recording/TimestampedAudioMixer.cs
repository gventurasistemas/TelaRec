using TelaRec.Core.Recording;

namespace TelaRec.Infrastructure.Recording;

public enum AudioSource { System, Microphone }

/// <summary>Alinha blocos das fontes em janelas de 10 ms e preenche a fonte ausente com silêncio.</summary>
public sealed class TimestampedAudioMixer
{
    private const int SampleRate = 48_000;
    private const int Channels = 2;
    private const int BytesPerSampleFrame = Channels * sizeof(short);
    private const int BucketSampleFrames = SampleRate / 100;
    private const int BucketBytes = BucketSampleFrames * BytesPerSampleFrame;
    private const long BucketTicks = TimeSpan.TicksPerSecond / 100;
    private const long HoldSampleFrames = SampleRate / 10;
    private sealed class Pending
    {
        public byte[]? System;
        public int SystemSampleFrames;
        public byte[]? Microphone;
        public int MicrophoneSampleFrames;
    }
    private readonly SortedDictionary<long, Pending> _pending = [];
    private readonly List<(AudioSource Source, AudioFrame Frame)> _startupFrames = [];
    private readonly AudioMixSettings _settings;
    private long _latestEndSampleFrame;
    private TimeSpan? _systemOrigin;
    private TimeSpan? _microphoneOrigin;
    private TimeSpan? _timelineOrigin;
    private TimeSpan? _lastSystemElapsed;
    private TimeSpan? _lastMicrophoneElapsed;

    public TimestampedAudioMixer(AudioMixSettings settings) => _settings = settings;
    public TimeSpan MaxSourceDrift { get; private set; }
    public long SystemInputSampleFrames { get; private set; }
    public long MicrophoneInputSampleFrames { get; private set; }
    public long OutputSampleFrames { get; private set; }
    public long MixedBuckets { get; private set; }
    public long SingleSourceBuckets { get; private set; }
    public TimeSpan InitialSourceSkew => _systemOrigin is { } system && _microphoneOrigin is { } microphone
        ? TimeSpan.FromTicks(Math.Abs((system - microphone).Ticks))
        : TimeSpan.Zero;

    public void Add(AudioSource source, AudioFrame frame)
    {
        if (frame.SampleRate != SampleRate || frame.Channels != Channels || frame.Pcm16.Length == 0) return;
        if (source == AudioSource.System) _systemOrigin ??= frame.Timestamp;
        else _microphoneOrigin ??= frame.Timestamp;

        if (_settings.SystemEnabled && _settings.MicrophoneEnabled &&
            (_systemOrigin is null || _microphoneOrigin is null))
        {
            _startupFrames.Add((source, frame));
            return;
        }

        _timelineOrigin ??= MinEnabledOrigin();
        if (_startupFrames.Count > 0)
        {
            _startupFrames.Add((source, frame));
            foreach (var buffered in _startupFrames) AddNormalized(buffered.Source, buffered.Frame);
            _startupFrames.Clear();
            return;
        }
        AddNormalized(source, frame);
    }

    private void AddNormalized(AudioSource source, AudioFrame frame)
    {
        var timestamp = NormalizeSourceTimestamp(source, frame.Timestamp);
        var startSampleFrame = Math.Max(0, timestamp.Ticks * SampleRate / TimeSpan.TicksPerSecond);
        var totalSampleFrames = frame.Pcm16.Length / BytesPerSampleFrame;
        if (source == AudioSource.System) SystemInputSampleFrames += totalSampleFrames;
        else MicrophoneInputSampleFrames += totalSampleFrames;
        var copiedSampleFrames = 0;
        while (copiedSampleFrames < totalSampleFrames)
        {
            var absoluteSampleFrame = startSampleFrame + copiedSampleFrames;
            var bucketIndex = absoluteSampleFrame / BucketSampleFrames;
            var bucketStartSampleFrame = bucketIndex * BucketSampleFrames;
            var offsetInBucket = (int)(absoluteSampleFrame - bucketStartSampleFrame);
            var count = Math.Min(totalSampleFrames - copiedSampleFrames, BucketSampleFrames - offsetInBucket);
            var bucketTicks = bucketStartSampleFrame * TimeSpan.TicksPerSecond / SampleRate;
            if (!_pending.TryGetValue(bucketTicks, out var item)) _pending[bucketTicks] = item = new Pending();

            ref var destination = ref source == AudioSource.System ? ref item.System : ref item.Microphone;
            destination ??= new byte[BucketBytes];
            Buffer.BlockCopy(
                frame.Pcm16,
                copiedSampleFrames * BytesPerSampleFrame,
                destination,
                offsetInBucket * BytesPerSampleFrame,
                count * BytesPerSampleFrame);
            if (source == AudioSource.System)
                item.SystemSampleFrames = Math.Max(item.SystemSampleFrames, offsetInBucket + count);
            else
                item.MicrophoneSampleFrames = Math.Max(item.MicrophoneSampleFrames, offsetInBucket + count);
            copiedSampleFrames += count;
        }

        var elapsed = timestamp - _timelineOrigin!.Value;
        if (source == AudioSource.System) _lastSystemElapsed = elapsed;
        else _lastMicrophoneElapsed = elapsed;
        _latestEndSampleFrame = Math.Max(_latestEndSampleFrame, startSampleFrame + totalSampleFrames);
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
        var sourceOrigin = source == AudioSource.System ? _systemOrigin : _microphoneOrigin;
        return _timelineOrigin!.Value + (timestamp - sourceOrigin!.Value);
    }

    private TimeSpan MinEnabledOrigin()
    {
        if (_settings.SystemEnabled && _settings.MicrophoneEnabled)
            return _systemOrigin <= _microphoneOrigin ? _systemOrigin!.Value : _microphoneOrigin!.Value;
        return _settings.SystemEnabled ? _systemOrigin!.Value : _microphoneOrigin!.Value;
    }

    public IEnumerable<AudioFrame> DrainReady(bool final = false)
    {
        while (_pending.Count > 0)
        {
            var pair = _pending.First();
            var bucketStartSampleFrame = pair.Key * SampleRate / TimeSpan.TicksPerSecond;
            var systemReady = !_settings.SystemEnabled || pair.Value.SystemSampleFrames >= BucketSampleFrames;
            var microphoneReady = !_settings.MicrophoneEnabled || pair.Value.MicrophoneSampleFrames >= BucketSampleFrames;
            var initialSkewFrames = InitialSourceSkew.Ticks * SampleRate / TimeSpan.TicksPerSecond;
            var pastHoldWindow = _latestEndSampleFrame >= bucketStartSampleFrame + BucketSampleFrames + HoldSampleFrames + initialSkewFrames;
            if (!final && !(systemReady && microphoneReady) && !pastHoldWindow) yield break;

            _pending.Remove(pair.Key);
            var sampleFrames = Math.Max(pair.Value.SystemSampleFrames, pair.Value.MicrophoneSampleFrames);
            if (sampleFrames == 0) continue;
            if (pair.Value.SystemSampleFrames > 0 && pair.Value.MicrophoneSampleFrames > 0) MixedBuckets++;
            else SingleSourceBuckets++;
            OutputSampleFrames += sampleFrames;
            var byteCount = sampleFrames * BytesPerSampleFrame;
            var system = _settings.SystemEnabled ? pair.Value.System?.AsSpan(0, byteCount).ToArray() : null;
            var microphone = _settings.MicrophoneEnabled ? pair.Value.Microphone?.AsSpan(0, byteCount).ToArray() : null;
            var bytes = PcmAudioMixer.MixPcm16(system, microphone, _settings);
            if (bytes.Length > 0) yield return new AudioFrame(bytes, SampleRate, Channels, TimeSpan.FromTicks(pair.Key));
        }
    }
}
