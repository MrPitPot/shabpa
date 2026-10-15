using System.Windows.Input;

namespace Shabpa.Core;

/// <summary>A keyboard shortcut such as "Ctrl+Shift+F9". It always has Ctrl or Alt in it (Shift is optional), so it
/// can never be a bare key that would fire while typing.</summary>
public sealed record Hotkey(bool Ctrl, bool Alt, bool Shift, Key Key)
{
    public const uint ModAlt = 0x1, ModCtrl = 0x2, ModShift = 0x4;

    public uint Modifiers => (Ctrl ? ModCtrl : 0u) | (Alt ? ModAlt : 0u) | (Shift ? ModShift : 0u);

    public uint VirtualKey => (uint)KeyInterop.VirtualKeyFromKey(Key);

    public override string ToString() =>
        string.Join('+', new[] { Ctrl ? "Ctrl" : null, Alt ? "Alt" : null, Shift ? "Shift" : null, Name(Key) }.Where(s => s is not null));

    private static string Name(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "Num" + ((int)key - (int)Key.NumPad0),
        Key.OemTilde => "`", Key.OemMinus => "-", Key.OemPlus => "=", Key.OemComma => ",", Key.OemPeriod => ".",
        _ => key.ToString(),
    };

    private static bool IsModifier(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
        or Key.LWin or Key.RWin or Key.System or Key.None or Key.DeadCharProcessed or Key.ImeProcessed;

    /// <summary>Null unless the key can be used: it is not only a modifier, and Ctrl or Alt is held.</summary>
    public static Hotkey? From(ModifierKeys modifiers, Key key)
    {
        if (IsModifier(key)) return null;
        var hotkey = new Hotkey(modifiers.HasFlag(ModifierKeys.Control), modifiers.HasFlag(ModifierKeys.Alt), modifiers.HasFlag(ModifierKeys.Shift), key);
        return hotkey.Ctrl || hotkey.Alt ? hotkey : null;
    }

    /// <summary>Reads "Ctrl+Alt+O". A single letter from older settings means Alt plus that letter. Null when empty or invalid.</summary>
    public static Hotkey? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        var mods = ModifierKeys.None;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= ModifierKeys.Control; break;
                case "alt": mods |= ModifierKeys.Alt; break;
                case "shift": mods |= ModifierKeys.Shift; break;
                default: return null;
            }
        }
        if (parts.Length == 1) mods = ModifierKeys.Alt; // old format: just the letter
        var last = parts[^1];
        Key? key = last switch
        {
            "`" => Key.OemTilde, "-" => Key.OemMinus, "=" => Key.OemPlus, "," => Key.OemComma, "." => Key.OemPeriod,
            _ when last.Length == 1 && char.IsDigit(last[0]) => Key.D0 + (last[0] - '0'),
            _ when last.StartsWith("Num", StringComparison.Ordinal) && last.Length == 4 && char.IsDigit(last[3]) => Key.NumPad0 + (last[3] - '0'),
            _ when Enum.TryParse<Key>(last, true, out var parsed) && Enum.IsDefined(parsed) => parsed,
            _ => null,
        };
        return key is { } k ? From(mods, k) : null;
    }
}
