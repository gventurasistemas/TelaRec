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

    /// <summary>Capacidade fixa da fila A/V durante a sessão.</summary>
    public int QueueCapacity { get; init; }
    /// <summary>Quantidade de pacotes ainda retidos pelo pipeline A/V.</summary>
    public int QueueDepth { get; init; }
    /// <summary>Maior profundidade observada na fila A/V durante a sessão.</summary>
    public int PeakQueueDepth { get; init; }
    /// <summary>Maior quantidade de pacotes aguardando reordenação temporal.</summary>
    public int PeakReorderQueueDepth { get; init; }
    /// <summary>Tempo médio entre o enfileiramento e o consumo de um pacote.</summary>
    public TimeSpan AverageQueueLatency { get; init; }
    /// <summary>Alocações gerenciadas observadas no processo desde o início da sessão.</summary>
    public long ManagedAllocatedBytes { get; init; }
    /// <summary>Bytes BGRA de vídeo ainda retidos pelo pipeline de gravação.</summary>
    public long QueuedVideoBytes { get; init; }
    /// <summary>Pico de bytes BGRA de vídeo retidos pelo pipeline de gravação.</summary>
    public long PeakQueuedVideoBytes { get; init; }
    /// <summary>Orçamento máximo de bytes BGRA pendentes no pipeline.</summary>
    public long MaximumQueuedVideoBytes { get; init; }
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
