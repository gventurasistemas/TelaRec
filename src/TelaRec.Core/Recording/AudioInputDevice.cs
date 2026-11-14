namespace TelaRec.Core.Recording;

/// <summary>Dispositivo de entrada disponível para captura de microfone.</summary>
public sealed record AudioInputDevice(string Id, string Name, bool IsDefault = false);
