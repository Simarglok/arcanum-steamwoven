namespace Arcanum.Input;

/// <summary>ARC-GPAD-1374, concept §0.4: where a prompt line is shown. <see cref="Modal"/> is the bottom line of every window that is open;
/// <see cref="Target"/> is the small prompt for the world target in reach ("A Talk · X Examine"); <see cref="Play"/> is neither.</summary>
public enum PromptContext { Play, Modal, Target }

/// <summary>An analogue trigger as a prompt input (triggers are not <see cref="ControllerButtons"/>).</summary>
public enum PromptTrigger { None, Left, Right }

/// <summary>One prompt of a scheme: <paramref name="Id"/> names the semantic action (the key of a user remap), <paramref name="Text"/> says what it does
/// ("Talk"), and the input is a pad button or trigger, plus an optional touch gesture ("Tap").</summary>
public sealed record PromptEntry(string Id, PromptContext Context, string Text, ControllerButtons Button = ControllerButtons.None,
    PromptTrigger Trigger = PromptTrigger.None, string? Touch = null);

/// <summary>The prompts of one scheme, immutable. Remaps rebind the pad button of an entry by its id, so a prompt always names the button the
/// player pressed to get it (concept §0.3: "prompts follow remaps"). A scheme's behaviour slice supplies its own entries with its behaviour; the
/// foundation has none, so no prompt names an input that does nothing.</summary>
public sealed class PromptBindings
{
    public const int MaximumEntries = 64;
    public static PromptBindings Empty { get; } = new([]);

    public IReadOnlyList<PromptEntry> Entries { get; }

    public PromptBindings(IEnumerable<PromptEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Entries = Array.AsReadOnly(entries.Take(MaximumEntries).ToArray());
    }

    /// <summary>The bindings of a pad scheme. None are built yet: each scheme's slice (pointer, direct) adds its rows with its behaviour.</summary>
    public static PromptBindings ForPad(PadScheme scheme) => Empty;

    /// <summary>The bindings of the touch scheme. None are built yet (the touch slice adds them).</summary>
    public static PromptBindings ForTouch(TouchScheme scheme) => Empty;

    /// <summary>The same prompts with the pad button of each entry replaced by its remap (an id with no remap, or one with no pad button, is kept).</summary>
    public PromptBindings Remapped(IReadOnlyDictionary<string, ControllerButtons>? remap)
    {
        if (remap is null || remap.Count == 0) return this;
        return new(Entries.Select(entry => entry.Button != ControllerButtons.None && remap.TryGetValue(entry.Id, out var moved) && IsSingle(moved) ? entry with { Button = moved } : entry));
    }

    private static bool IsSingle(ControllerButtons button) => button != ControllerButtons.None && (button & (button - 1)) == 0;
}

/// <summary>Builds the prompt line. Nothing is shown while the mouse or keyboard is the active device (the original draws no prompts, KM-01), and a
/// line for the pad or touch never holds a keyboard or mouse glyph.</summary>
public static class PromptStrip
{
    public static string FamilyName(GlyphFamily family) => family switch
    {
        GlyphFamily.Xbox => "Xbox",
        GlyphFamily.PlayStation => "PlayStation",
        GlyphFamily.Nintendo => "Nintendo",
        GlyphFamily.SteamDeck => "Steam Deck",
        _ => "Keyboard and mouse"
    };

    private const string OriginalText = "original controls (Options > Controls)";

    public static string PadSchemeName(PadScheme scheme) => scheme switch { PadScheme.Direct => "A Direct", PadScheme.Pointer => "B Pointer", _ => "Off" };

    public static string TouchSchemeName(TouchScheme scheme) => scheme switch { TouchScheme.TouchPad => "C Touch+pad", _ => "Off" };

    /// <summary>The line for the device in hand; empty for the mouse and keyboard. With a scheme chosen it lists the scheme's prompts for
    /// <paramref name="context"/> (button glyphs of <paramref name="family"/>); with none to list, or none chosen, it says which device and scheme are
    /// live, so a pad that does nothing yet is never promised a button.</summary>
    public static string Compose(ActiveDevice device, GlyphFamily family, ControlSchemeSettings settings, PromptContext context,
        PromptBindings padBindings, PromptBindings touchBindings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(padBindings);
        ArgumentNullException.ThrowIfNull(touchBindings);
        switch (device)
        {
            case ActiveDevice.Pad:
            {
                var name = FamilyName(family == GlyphFamily.KeyboardMouse ? GlyphFamily.Xbox : family) + " pad";
                // Scheme C plays on the pad too (it walks and presses hotkeys with it), so the pad's line names it.
                var scheme = settings.Pad != PadScheme.Off ? PadSchemeName(settings.Pad) : settings.Touch == TouchScheme.TouchPad ? TouchSchemeName(settings.Touch) : null;
                if (scheme is null) return name + " - " + OriginalText;
                var parts = padBindings.Entries.Where(entry => entry.Context == context && (entry.Button != ControllerButtons.None || entry.Trigger != PromptTrigger.None))
                    .Select(entry => PadInputLabel(family == GlyphFamily.KeyboardMouse ? GlyphFamily.Xbox : family, entry) + " " + entry.Text).ToArray();
                return parts.Length == 0 ? name + " - " + scheme : string.Join(" | ", parts);
            }
            case ActiveDevice.Touch:
            {
                if (settings.Touch == TouchScheme.Off) return "Touch - " + OriginalText;
                var parts = touchBindings.Entries.Where(entry => entry.Context == context && entry.Touch is { Length: > 0 }).Select(entry => entry.Touch + " " + entry.Text).ToArray();
                return parts.Length == 0 ? "Touch - " + TouchSchemeName(settings.Touch) : string.Join(" | ", parts);
            }
            default:
                return string.Empty;
        }
    }

    private static string PadInputLabel(GlyphFamily family, PromptEntry entry) => entry.Trigger != PromptTrigger.None
        ? ControllerGlyphs.Trigger(family, entry.Trigger == PromptTrigger.Right)
        : ControllerGlyphs.Label(family, entry.Button);
}
