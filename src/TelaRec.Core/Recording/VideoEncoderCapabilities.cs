namespace TelaRec.Core.Recording;

/// <summary>Preferência do usuário para a seleção de MFTs do Media Foundation.</summary>
public enum VideoEncoderAcceleration
{
    Auto,
    PreferHardware,
    SoftwareOnly
}

/// <summary>Capacidades efetivamente descobertas no Windows atual.</summary>
public sealed record VideoEncoderCapabilities(
    bool H264HardwareSupported,
    bool HevcHardwareSupported,
    bool HevcSoftwareSupported = false)
{
    public bool HevcSupported => HevcHardwareSupported || HevcSoftwareSupported;
    public static VideoEncoderCapabilities SafeDefault { get; } = new(false, false, false);
}

/// <summary>Consulta isolada para que regras de UI não dependam de Media Foundation.</summary>
public interface IVideoEncoderCapabilitiesService
{
    VideoEncoderCapabilities GetCapabilities();
}

/// <summary>Contadores de uma sessão, voltados a diagnóstico e comparação de hardware.</summary>
public sealed record RecordingPerformanceTelemetry(
    long CapturedFrames,
    long RateLimitedFrames,
    long RepeatedFrames,
    long QueueDroppedVideoFrames,
    long QueueDroppedAudioPackets,
    long EncodedFrames,
    TimeSpan AverageEncodeTime,
    bool UsingHardwareEncoder)
{
    public static RecordingPerformanceTelemetry Empty { get; } = new(0, 0, 0, 0, 0, 0, TimeSpan.Zero, false);
    public long DroppedFrames => RateLimitedFrames + QueueDroppedVideoFrames;
}

public interface IRecordingTelemetrySource
{
    RecordingPerformanceTelemetry CurrentTelemetry { get; }
}

public interface IVideoEncoderTelemetrySource
{
    bool UsingHardwareEncoder { get; }
}

public interface IVideoCaptureTelemetrySource
{
    long CapturedFrames { get; }
    long RateLimitedFrames { get; }
    long RepeatedFrames { get; }
}
