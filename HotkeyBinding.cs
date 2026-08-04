namespace HonorPCHelper;

/// <summary>
/// Комбинация клавиш для глобального сочетания. Пустое значение
/// (<see cref="VirtualKey"/> == 0) означает, что действие отключено.
/// </summary>
internal readonly record struct HotkeyBinding(uint Modifiers, uint VirtualKey)
{
    internal const uint ModAlt = 0x0001;
    internal const uint ModControl = 0x0002;
    internal const uint ModShift = 0x0004;
    internal const uint ModWin = 0x0008;
    // Без MOD_NOREPEAT удержание клавиши шлёт WM_HOTKEY с частотой автоповтора.
    internal const uint ModNoRepeat = 0x4000;

    private const string DisabledToken = "None";

    internal static HotkeyBinding None => default;

    internal bool IsEmpty => VirtualKey == 0;

    /// <summary>Флаги для RegisterHotKey: сочетание плюс подавление автоповтора.</summary>
    internal uint RegistrationModifiers => Modifiers | ModNoRepeat;

    public override string ToString()
        => IsEmpty ? DisabledToken : FormatModifiers(Modifiers) + KeyName(VirtualKey);

    /// <summary>Текст удерживаемых модификаторов без основной клавиши: "Ctrl+Alt+".</summary>
    internal static string FormatModifiers(uint modifiers)
    {
        var text = new System.Text.StringBuilder(16);
        if ((modifiers & ModControl) != 0)
            text.Append("Ctrl+");
        if ((modifiers & ModAlt) != 0)
            text.Append("Alt+");
        if ((modifiers & ModShift) != 0)
            text.Append("Shift+");
        if ((modifiers & ModWin) != 0)
            text.Append("Win+");
        return text.ToString();
    }

    internal static bool TryParse(string? text, out HotkeyBinding binding)
    {
        binding = None;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        if (text.Trim().Equals(DisabledToken, StringComparison.OrdinalIgnoreCase))
            return true;

        uint modifiers = 0;
        uint virtualKey = 0;
        foreach (var part in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control":
                    modifiers |= ModControl;
                    continue;
                case "alt":
                    modifiers |= ModAlt;
                    continue;
                case "shift":
                    modifiers |= ModShift;
                    continue;
                case "win" or "windows":
                    modifiers |= ModWin;
                    continue;
            }

            if (virtualKey != 0 || !TryParseKey(part, out virtualKey))
                return false;
        }

        if (virtualKey == 0)
            return false;

        binding = new HotkeyBinding(modifiers, virtualKey);
        return true;
    }

    // Обратная к KeyName операция: разбирает как понятные имена ("Num5", "["),
    // так и названия из перечисления Keys.
    private static bool TryParseKey(string name, out uint virtualKey)
    {
        virtualKey = name.ToLowerInvariant() switch
        {
            "backspace" => 0x08,
            "tab" => 0x09,
            "enter" => 0x0D,
            "pause" => 0x13,
            "space" => 0x20,
            "pageup" => 0x21,
            "pagedown" => 0x22,
            "end" => 0x23,
            "home" => 0x24,
            "left" => 0x25,
            "up" => 0x26,
            "right" => 0x27,
            "down" => 0x28,
            "insert" => 0x2D,
            "delete" => 0x2E,
            "num*" => 0x6A,
            "numadd" => 0x6B,
            "num-" => 0x6D,
            "num." => 0x6E,
            "num/" => 0x6F,
            ";" => 0xBA,
            "=" => 0xBB,
            "," => 0xBC,
            "-" => 0xBD,
            "." => 0xBE,
            "/" => 0xBF,
            "`" => 0xC0,
            "[" => 0xDB,
            "\\" => 0xDC,
            "]" => 0xDD,
            "'" => 0xDE,
            _ => 0
        };
        if (virtualKey != 0)
            return true;

        if (name.Length == 1 && char.IsAsciiLetterOrDigit(name[0]))
            virtualKey = char.ToUpperInvariant(name[0]);
        else if (name.Length == 4 && name.StartsWith("num", StringComparison.OrdinalIgnoreCase)
            && char.IsAsciiDigit(name[3]))
            virtualKey = 0x60u + (uint)(name[3] - '0');
        else if (name.Length is 2 or 3 && (name[0] is 'f' or 'F')
            && uint.TryParse(name.AsSpan(1), out var index) && index is >= 1 and <= 24)
            virtualKey = 0x6Fu + index;
        else if (Enum.TryParse<Keys>(name, true, out var key) && key != Keys.None)
            virtualKey = (uint)key;

        return virtualKey != 0;
    }

    private static string KeyName(uint virtualKey) => virtualKey switch
    {
        >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)virtualKey).ToString(),
        >= 0x70 and <= 0x87 => $"F{virtualKey - 0x6F}",
        0x08 => "Backspace",
        0x09 => "Tab",
        0x0D => "Enter",
        0x13 => "Pause",
        0x20 => "Space",
        0x21 => "PageUp",
        0x22 => "PageDown",
        0x23 => "End",
        0x24 => "Home",
        0x25 => "Left",
        0x26 => "Up",
        0x27 => "Right",
        0x28 => "Down",
        0x2D => "Insert",
        0x2E => "Delete",
        >= 0x60 and <= 0x69 => $"Num{virtualKey - 0x60}",
        0x6A => "Num*",
        // Плюс - разделитель в записи сочетания, поэтому имя без него.
        0x6B => "NumAdd",
        0x6D => "Num-",
        0x6E => "Num.",
        0x6F => "Num/",
        0xBA => ";",
        0xBB => "=",
        0xBC => ",",
        0xBD => "-",
        0xBE => ".",
        0xBF => "/",
        0xC0 => "`",
        0xDB => "[",
        0xDC => "\\",
        0xDD => "]",
        0xDE => "'",
        _ => ((Keys)virtualKey).ToString()
    };
}
