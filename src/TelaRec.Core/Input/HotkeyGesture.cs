using System.Globalization;

namespace TelaRec.Core.Input;

/// <summary>Modificadores de um atalho global, com os mesmos valores usados por RegisterHotKey.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8
}

/// <summary>
/// Atalho global independente de UI. Faz o parsing/formatação de textos como
/// "Ctrl+Alt+R" e expõe o código virtual usado pelo sistema operacional.
/// </summary>
public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, int VirtualKey)
{
    private static readonly IReadOnlyDictionary<string, int> NamedKeys =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["PrintScreen"] = 0x2C,
            ["Pause"] = 0x13,
            ["Insert"] = 0x2D,
            ["Delete"] = 0x2E,
            ["Home"] = 0x24,
            ["End"] = 0x23,
            ["PageUp"] = 0x21,
            ["PageDown"] = 0x22,
            ["Space"] = 0x20,
            ["Tab"] = 0x09,
            ["Enter"] = 0x0D,
            ["Escape"] = 0x1B,
            ["Up"] = 0x26,
            ["Down"] = 0x28,
            ["Left"] = 0x25,
            ["Right"] = 0x27
        };

    public bool IsValid => Modifiers != HotkeyModifiers.None && VirtualKey > 0;

    /// <summary>Tenta converter um texto como "Ctrl+Alt+R" em um atalho válido.</summary>
    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var modifiers = HotkeyModifiers.None;
        var virtualKey = 0;
        var tokens = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            if (TryParseModifier(token, out var modifier))
            {
                modifiers |= modifier;
                continue;
            }

            if (virtualKey != 0) return false;
            if (!TryParseKey(token, out virtualKey)) return false;
        }

        if (virtualKey == 0 || modifiers == HotkeyModifiers.None) return false;
        gesture = new HotkeyGesture(modifiers, virtualKey);
        return true;
    }

    /// <summary>Nome amigável da tecla, usado para exibição e round-trip.</summary>
    public string KeyName
    {
        get
        {
            if (VirtualKey is >= 0x41 and <= 0x5A) return ((char)VirtualKey).ToString();
            if (VirtualKey is >= 0x30 and <= 0x39) return ((char)VirtualKey).ToString();
            if (VirtualKey is >= 0x70 and <= 0x87) return $"F{VirtualKey - 0x6F}";
            foreach (var pair in NamedKeys)
                if (pair.Value == VirtualKey) return pair.Key;
            return $"0x{VirtualKey:X2}";
        }
    }

    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Windows)) parts.Add("Win");
        parts.Add(KeyName);
        return string.Join("+", parts);
    }

    private static bool TryParseModifier(string token, out HotkeyModifiers modifier)
    {
        modifier = token.ToLowerInvariant() switch
        {
            "ctrl" or "control" => HotkeyModifiers.Control,
            "alt" => HotkeyModifiers.Alt,
            "shift" => HotkeyModifiers.Shift,
            "win" or "windows" or "meta" => HotkeyModifiers.Windows,
            _ => HotkeyModifiers.None
        };
        return modifier != HotkeyModifiers.None;
    }

    private static bool TryParseKey(string token, out int virtualKey)
    {
        virtualKey = 0;
        if (token.Length == 1)
        {
            var character = char.ToUpperInvariant(token[0]);
            if (character is >= 'A' and <= 'Z') { virtualKey = character; return true; }
            if (character is >= '0' and <= '9') { virtualKey = character; return true; }
            return false;
        }

        if (token.Length is >= 2 and <= 3 && (token[0] is 'F' or 'f') &&
            int.TryParse(token.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var function) &&
            function is >= 1 and <= 24)
        {
            virtualKey = 0x6F + function;
            return true;
        }

        return NamedKeys.TryGetValue(token, out virtualKey);
    }
}
