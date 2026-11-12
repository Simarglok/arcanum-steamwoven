namespace Arcanum.Input;

/// <summary>Button glyph families of concept §0.3. <see cref="KeyboardMouse"/> is what prompts show while the mouse or keyboard is the active device.</summary>
public enum GlyphFamily { KeyboardMouse, Xbox, PlayStation, Nintendo, SteamDeck }

/// <summary>Picks the glyph family from the pad's reported name and GUID (Godot <c>Input.GetJoyName/GetJoyGuid</c>) and the manual override.
/// A heuristic: a pad behind Steam Input reports a virtual Xbox pad, so the Deck host flag decides there; the real result needs a device check.</summary>
public static class GlyphFamilyDetector
{
    private const int VendorSony = 0x054c, VendorNintendo = 0x057e, VendorValve = 0x28de;

    public static GlyphFamily Resolve(PromptStyle style, string? joyName, string? joyGuid, bool hostIsSteamDeck = false) => style switch
    {
        PromptStyle.Xbox => GlyphFamily.Xbox,
        PromptStyle.PlayStation => GlyphFamily.PlayStation,
        PromptStyle.Nintendo => GlyphFamily.Nintendo,
        PromptStyle.SteamDeck => GlyphFamily.SteamDeck,
        _ => Detect(joyName, joyGuid, hostIsSteamDeck)
    };

    /// <summary>Order: vendor id from the GUID, then name words, then the Deck host fallback; anything unknown is Xbox, the most common layout.</summary>
    public static GlyphFamily Detect(string? joyName, string? joyGuid, bool hostIsSteamDeck = false)
    {
        var name = (joyName ?? string.Empty).ToLowerInvariant();
        var vendor = VendorOf(joyGuid);
        if (vendor == VendorValve || name.Contains("steam deck") || name.Contains("steam controller") || name.Contains("steamdeck")) return GlyphFamily.SteamDeck;
        if (vendor == VendorSony || Has(name, "dualsense", "dualshock", "playstation", "ps5", "ps4", "ps3", "sony")) return GlyphFamily.PlayStation;
        if (vendor == VendorNintendo || Has(name, "nintendo", "switch", "pro controller", "joy-con", "joycon")) return GlyphFamily.Nintendo;
        // Steam Input hands the game a virtual Xbox pad; on a Deck host that is the Deck's own pad.
        return hostIsSteamDeck ? GlyphFamily.SteamDeck : GlyphFamily.Xbox;
    }

    /// <summary>The USB vendor id of an SDL-style 32 hex digit GUID (bytes 4-5, little endian), or -1 when the text is not one.</summary>
    public static int VendorOf(string? joyGuid)
    {
        if (joyGuid is null || joyGuid.Length != 32) return -1;
        foreach (var c in joyGuid) if (!Uri.IsHexDigit(c)) return -1;
        var low = Convert.ToInt32(joyGuid.Substring(8, 2), 16);
        var high = Convert.ToInt32(joyGuid.Substring(10, 2), 16);
        return high << 8 | low;
    }

    private static bool Has(string name, params string[] words)
    {
        foreach (var word in words) if (name.Contains(word)) return true;
        return false;
    }
}

/// <summary>Short text labels of the face, shoulder and system buttons per family, for prompt strips. Face buttons follow the physical
/// position (A is the bottom button), so Nintendo's bottom button is labelled B. Text only: no art.</summary>
public static class ControllerGlyphs
{
    public static string Label(GlyphFamily family, ControllerButtons button) => button switch
    {
        ControllerButtons.A => Pick(family, "A", "Cross", "B", "A"),
        ControllerButtons.B => Pick(family, "B", "Circle", "A", "B"),
        ControllerButtons.X => Pick(family, "X", "Square", "Y", "X"),
        ControllerButtons.Y => Pick(family, "Y", "Triangle", "X", "Y"),
        ControllerButtons.LB => Pick(family, "LB", "L1", "L", "L1"),
        ControllerButtons.RB => Pick(family, "RB", "R1", "R", "R1"),
        ControllerButtons.DpadUp => "D-pad Up",
        ControllerButtons.DpadDown => "D-pad Down",
        ControllerButtons.DpadLeft => "D-pad Left",
        ControllerButtons.DpadRight => "D-pad Right",
        ControllerButtons.L3 => "L3",
        ControllerButtons.R3 => "R3",
        ControllerButtons.View => Pick(family, "View", "Create", "-", "View"),
        ControllerButtons.Menu => Pick(family, "Menu", "Options", "+", "Menu"),
        _ => string.Empty
    };

    /// <summary>Trigger label (analogue triggers are not <see cref="ControllerButtons"/>).</summary>
    public static string Trigger(GlyphFamily family, bool right) =>
        right ? Pick(family, "RT", "R2", "ZR", "R2") : Pick(family, "LT", "L2", "ZL", "L2");

    private static string Pick(GlyphFamily family, string xbox, string playStation, string nintendo, string deck) => family switch
    {
        GlyphFamily.PlayStation => playStation,
        GlyphFamily.Nintendo => nintendo,
        GlyphFamily.SteamDeck => deck,
        GlyphFamily.Xbox => xbox,
        _ => string.Empty
    };
}
