using Arcanum.Presentation.Hud;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-2305, ARC-UI-2306: the keys of a bottom strip text entry (the original's 0x566F00, shared by bar modes 6 and 7): Backspace, Delete, Home, the arrows and Insert edit,
/// Tab does nothing, a printable character is typed. Enter (the entry's accept) and Escape are the owner's.
/// </summary>
internal static class AreaStripEntryKeys
{
    /// <summary>Applies a key press (or echo) of the entry's editing keys through <paramref name="type"/> and <paramref name="press"/>. Returns true: every key but the
    /// owner's Enter and Escape belongs to the entry while it is bound (0x54C8E0 skips the hotkeys).</summary>
    internal static bool Edit(InputEventKey key, Func<char, bool> type, Func<StripEntryKey, bool> press)
    {
        var code = key.Keycode != Key.None ? key.Keycode : key.PhysicalKeycode;
        switch (code)
        {
            case Key.Backspace: type('\b'); return true;
            case Key.Delete: press(StripEntryKey.Delete); return true;
            case Key.Home: press(StripEntryKey.Home); return true;
            case Key.Left: press(StripEntryKey.Left); return true;
            case Key.Right: press(StripEntryKey.Right); return true;
            case Key.Up: press(StripEntryKey.Up); return true;
            case Key.Down: press(StripEntryKey.Down); return true;
            case Key.Insert: if (!key.Echo) press(StripEntryKey.Insert); return true;
            case Key.Tab: return true;
        }
        if (key.Unicode >= ' ' && key.Unicode <= 0xFF && !key.CtrlPressed && !key.AltPressed && !key.MetaPressed) type((char)key.Unicode);
        return true;
    }
}
