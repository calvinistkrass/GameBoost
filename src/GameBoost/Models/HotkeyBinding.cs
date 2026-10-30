using System.Text.Json.Serialization;
using System.Windows.Input;
using GameBoost.Infrastructure;

namespace GameBoost.Models;

public enum HotkeyAction
{
    ToggleCrosshair = 1,
    ToggleGameMode = 2,
    NextPreset = 3
}

/// <summary>Eine globale Tastenkombination (Win32-Modifier + Virtual-Key).</summary>
public sealed class HotkeyBinding
{
    public const int ModAlt = 0x1;
    public const int ModControl = 0x2;
    public const int ModShift = 0x4;
    public const int ModWin = 0x8;

    public int Modifiers { get; set; }
    public int Key { get; set; }

    [JsonIgnore]
    public bool IsValid => Key > 0 && Key < 256;

    public static HotkeyBinding Create(int modifiers, Key key)
        => new() { Modifiers = modifiers, Key = KeyInterop.VirtualKeyFromKey(key) };

    public HotkeyBinding Clone() => new() { Modifiers = Modifiers, Key = Key };

    public bool SameAs(HotkeyBinding? other) => other != null && other.Modifiers == Modifiers && other.Key == Key;

    public override string ToString()
    {
        var parts = new List<string>();
        if ((Modifiers & ModControl) != 0) parts.Add(Loc.T("Key.Ctrl"));
        if ((Modifiers & ModAlt) != 0) parts.Add(Loc.T("Key.Alt"));
        if ((Modifiers & ModShift) != 0) parts.Add(Loc.T("Key.Shift"));
        if ((Modifiers & ModWin) != 0) parts.Add(Loc.T("Key.Win"));
        parts.Add(KeyName(Key));
        return string.Join(" + ", parts);
    }

    public static string KeyName(int virtualKey)
    {
        var key = KeyInterop.KeyFromVirtualKey(virtualKey);
        if (key >= System.Windows.Input.Key.D0 && key <= System.Windows.Input.Key.D9)
            return ((char)('0' + (key - System.Windows.Input.Key.D0))).ToString();
        if (key >= System.Windows.Input.Key.NumPad0 && key <= System.Windows.Input.Key.NumPad9)
            return "Num " + (key - System.Windows.Input.Key.NumPad0);
        if (key.ToString().StartsWith("Oem", StringComparison.Ordinal))
        {
            var ch = NativeMethods.MapVirtualKey((uint)virtualKey, NativeMethods.MAPVK_VK_TO_CHAR) & 0x7FFF;
            if (ch > 32) return ((char)ch).ToString().ToUpperInvariant();
        }
        return key switch
        {
            System.Windows.Input.Key.Space => "Space",
            System.Windows.Input.Key.Return => "Enter",
            System.Windows.Input.Key.Prior => "PageUp",
            System.Windows.Input.Key.Next => "PageDown",
            _ => key.ToString()
        };
    }
}

/// <summary>Belegung aller globalen Hotkeys.</summary>
public sealed class HotkeySettings
{
    public HotkeyBinding? ToggleCrosshair { get; set; } =
        new() { Modifiers = HotkeyBinding.ModControl | HotkeyBinding.ModAlt, Key = 0x43 }; // Strg+Alt+C

    public HotkeyBinding? ToggleGameMode { get; set; } =
        new() { Modifiers = HotkeyBinding.ModControl | HotkeyBinding.ModAlt, Key = 0x47 }; // Strg+Alt+G

    public HotkeyBinding? NextPreset { get; set; } =
        new() { Modifiers = HotkeyBinding.ModControl | HotkeyBinding.ModAlt, Key = 0x4E }; // Strg+Alt+N

    public HotkeyBinding? Get(HotkeyAction action) => action switch
    {
        HotkeyAction.ToggleCrosshair => ToggleCrosshair,
        HotkeyAction.ToggleGameMode => ToggleGameMode,
        HotkeyAction.NextPreset => NextPreset,
        _ => null
    };

    public void Set(HotkeyAction action, HotkeyBinding? binding)
    {
        switch (action)
        {
            case HotkeyAction.ToggleCrosshair: ToggleCrosshair = binding; break;
            case HotkeyAction.ToggleGameMode: ToggleGameMode = binding; break;
            case HotkeyAction.NextPreset: NextPreset = binding; break;
        }
    }
}
