namespace TelaRec.Core.Recording;

public sealed record AudioFrame(byte[] Pcm16, int SampleRate, int Channels, TimeSpan Timestamp);
