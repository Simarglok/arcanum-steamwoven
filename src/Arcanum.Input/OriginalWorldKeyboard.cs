namespace Arcanum.Input;

/// <summary>Engine-free key identity for the WORLD keyboard route. Adapters map
/// their physical/logical key codes here; unknown keys are <see cref="Other"/>.</summary>
public enum WorldKey
{
    Other,
    Up, Down, Left, Right, Home,
    Enter, Space, Escape, Backspace, Tab, NumLock,
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    Digit0, Digit1, Digit2, Digit3, Digit4, Digit5, Digit6, Digit7, Digit8, Digit9,
    A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    Comma, Period, Slash,
}

/// <summary>What a key may do in the WORLD keyboard route (docs/input/diagnostic-palette.md).</summary>
public enum WorldKeyRole
{
    /// <summary>No binding in this context; the key creates no intent.</summary>
    Unmapped,
    /// <summary>Arrows pan the world image (KM-05), with a fixed direction.</summary>
    CameraPan,
    /// <summary>Navigation/activation inside the open diagnostic palette only.</summary>
    PaletteControl,
    /// <summary>An original Arcanum binding that is not implemented yet. It is
    /// reserved: no debug action and no redirection to another action.</summary>
    OriginalReserved,
}

[Flags]
public enum WorldKeyModifiers { None = 0, Ctrl = 1, Shift = 2, Alt = 4, Meta = 8 }

/// <summary>One routed keyboard event. <see cref="Consumed"/> events never reach
/// the shared keyboard mapping, native UI or a world/original sink.</summary>
public sealed record WorldKeyDecision(WorldKey Key, bool Pressed, bool Echo, WorldKeyRole Role,
    bool TogglePalette, bool Consumed, string? OriginalAction);

/// <summary>Static WORLD keyboard table: arrows pan; original keys from the M/Q
/// quick keys and KM-02 are reserved; the palette owns its own few controls.</summary>
public static class OriginalWorldKeyboard
{
    private static readonly Dictionary<WorldKey, string> Reserved = new()
    {
        [WorldKey.Escape] = "Game menu",
        [WorldKey.F1] = "Followers: go to point",
        [WorldKey.F2] = "Followers: attack target",
        [WorldKey.F3] = "Followers: follow closer",
        [WorldKey.F4] = "Followers: follow farther",
        [WorldKey.F5] = "Followers: stop attacking",
        [WorldKey.F6] = "Broadcast 6 (source reading only)",
        [WorldKey.F7] = "Quick save",
        [WorldKey.F8] = "Quick load",
        [WorldKey.F10] = "Compact interface (source reading only)",
        [WorldKey.F12] = "Screenshot",
        [WorldKey.NumLock] = "Always run",
        [WorldKey.Enter] = "Broadcast/chat message",
        [WorldKey.Space] = "Close interfaces / Turn-Based toggle",
        [WorldKey.Home] = "Center on PC",
        [WorldKey.A] = "Last active action",
        [WorldKey.C] = "Character sheet",
        [WorldKey.E] = "Turn-Based: end the sub-turn",
        [WorldKey.F] = "Fate points",
        [WorldKey.I] = "Inventory",
        [WorldKey.K] = "Skills pane (source reading only)",
        [WorldKey.L] = "Logbook",
        [WorldKey.M] = "Spells pane (source reading only)",
        [WorldKey.O] = "Options",
        [WorldKey.R] = "Ready/Unready combat",
        [WorldKey.S] = "Sleep/Wait",
        [WorldKey.T] = "Technical schematics",
        [WorldKey.V] = "Version information",
        [WorldKey.W] = "World map",
        [WorldKey.Comma] = "Called shot: head (held)",
        [WorldKey.Period] = "Called shot: arms (held)",
        [WorldKey.Slash] = "Called shot: legs (held)",
    };

    public static WorldKeyRole Role(WorldKey key, bool paletteOpen) => paletteOpen
        ? key is WorldKey.Up or WorldKey.Down or WorldKey.Enter or WorldKey.Space or WorldKey.Escape or WorldKey.Backspace
            ? WorldKeyRole.PaletteControl : WorldKeyRole.Unmapped
        : key is WorldKey.Up or WorldKey.Down or WorldKey.Left or WorldKey.Right ? WorldKeyRole.CameraPan
        : OriginalAction(key) is not null ? WorldKeyRole.OriginalReserved : WorldKeyRole.Unmapped;

    /// <summary>The documented original action of a reserved key, or null.</summary>
    public static string? OriginalAction(WorldKey key)
    {
        if (key is >= WorldKey.Digit0 and <= WorldKey.Digit9)
            return $"Quick slot {(key == WorldKey.Digit0 ? 10 : key - WorldKey.Digit0)}";
        return Reserved.GetValueOrDefault(key);
    }

    /// <summary>ARC-INPUT-004: true when a held modifier leaves a character letter of the original without any action. The
    /// in-game filter (0x54B5D0, with 0x57DC60 for A) reads A, C, F, I, L, R, S, T and W, in either case, from character
    /// messages, which the window procedure makes only from WM_CHAR: Shift gives the capital (the same action), Ctrl gives a
    /// control code outside the filter's table, Alt gives a system character the game never reads. Command (Meta) belongs to
    /// the system.</summary>
    public static bool CharacterKeySilenced(WorldKey key, WorldKeyModifiers modifiers)
        => key is WorldKey.A or WorldKey.C or WorldKey.F or WorldKey.I or WorldKey.L or WorldKey.R or WorldKey.S or WorldKey.T or WorldKey.W
            && (modifiers & (WorldKeyModifiers.Ctrl | WorldKeyModifiers.Alt | WorldKeyModifiers.Meta)) != 0;
}

/// <summary>Owns the diagnostic palette chord Ctrl+Shift+F9 ahead of every other
/// keyboard mapping. One activation per chord; the consumed F9 owns its whole
/// down/up pair (including echo and modifier-first release), so no late F9,
/// quick-slot or other action can leak on release.</summary>
public sealed class DiagnosticPaletteChord
{
    public const WorldKey ChordKey = WorldKey.F9;
    public const WorldKeyModifiers ChordModifiers = WorldKeyModifiers.Ctrl | WorldKeyModifiers.Shift;
    private readonly List<WorldKeyDecision> _trace = new();

    /// <summary>True between a consumed F9 press and its release.</summary>
    public bool Owned { get; private set; }
    /// <summary>Every routed decision, oldest first (bounded); a test sink, not behaviour.</summary>
    public IReadOnlyList<WorldKeyDecision> Trace => _trace;
    public int TraceLimit { get; init; } = 256;

    /// <param name="paletteOpen">The palette is the current modal surface.</param>
    /// <param name="openAllowed">The window is focused and no text entry or other
    /// UI owns focus; only then may the chord open the palette. An open palette
    /// may always be closed by its own chord.</param>
    public WorldKeyDecision Route(WorldKey key, bool pressed, bool echo, WorldKeyModifiers modifiers, bool paletteOpen, bool openAllowed)
    {
        var decision = Decide(key, pressed, echo, modifiers, paletteOpen, openAllowed);
        if (_trace.Count >= TraceLimit) _trace.RemoveAt(0);
        _trace.Add(decision);
        return decision;
    }

    /// <summary>Forget ownership at a lifecycle boundary (exit). Focus loss does
    /// not need it: a later fresh press is evaluated on its own.</summary>
    public void Reset() => Owned = false;
    public void ClearTrace() => _trace.Clear();

    private WorldKeyDecision Decide(WorldKey key, bool pressed, bool echo, WorldKeyModifiers modifiers, bool paletteOpen, bool openAllowed)
    {
        var role = OriginalWorldKeyboard.Role(key, paletteOpen);
        var action = role == WorldKeyRole.OriginalReserved ? OriginalWorldKeyboard.OriginalAction(key) : null;
        if (key != ChordKey) return new(key, pressed, echo, role, false, false, action);
        if (!pressed)
        {
            var owned = Owned; Owned = false;
            return new(key, false, echo, role, false, owned, owned ? null : action);
        }
        if (echo) return new(key, true, true, role, false, Owned, Owned ? null : action);
        // A fresh press: a lost release (focus change) never blocks a new chord.
        Owned = false;
        if (modifiers != ChordModifiers || !(paletteOpen || openAllowed)) return new(key, true, false, role, false, false, action);
        Owned = true;
        return new(key, true, false, role, true, true, null);
    }
}
