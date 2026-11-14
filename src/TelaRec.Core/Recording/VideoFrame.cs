namespace TelaRec.Core.Recording;

/// <summary>Quadro BGRA copiado para memória gerenciada; Timestamp usa o relógio monotônico QPC.</summary>
public sealed record VideoFrame(byte[] Pixels, int Width, int Height, int Stride, TimeSpan Timestamp);
