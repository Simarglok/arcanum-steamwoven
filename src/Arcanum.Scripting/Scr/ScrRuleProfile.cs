namespace Arcanum.Scripting.Scr;

/// <summary>
/// The SCR switches of the two rule modes (owner decision D8, contract docs/world/rule-modes.md). Each switch is a bug of
/// the original 1.0.7.4 executable confirmed in the verification ledger (ARC-RES-048) with a fix recorded by
/// ARC-SCRIPT-008 under decision D2; the original's behaviour is the <c>true</c> value. CE reconstruction slips are not
/// switches: divide stores the quotient (X7) and counter writes are masked to a byte (X8) in both modes.
/// </summary>
public sealed record ScrRuleProfile
{
    /// <summary>Stable setting name, <c>steamwoven</c> or <c>original</c>. A profile changed with <c>with</c> is a custom
    /// mix and should get its own name.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// X10 (original bug, <c>script_global_flag_set</c> 0x444D20): writing a value into a global flag clears the flag's
    /// bit and ORs the whole value shifted to that bit into the flag's 32-flag word, so 2 sets the next flag and a
    /// negative value every higher flag of the word. Steamwoven writes only the value's low bit into the flag, which is
    /// what the original stores in the flag itself.
    /// </summary>
    public bool GlobalFlagWriteSpills { get; init; }

    /// <summary>
    /// X3, tile (original bug, <c>tile_script_exec</c> 0x4C0980): after a tile script runs the unmodified record is
    /// stored back, so nothing the script changes about itself persists, "remove this script" included. Steamwoven
    /// writes the changed number, flags and counters back, as it does for objects and sectors.
    /// </summary>
    public bool TileScriptChangesDiscarded { get; init; }

    /// <summary>
    /// TE1 (original bug at the edges, ARC-RES-048): call script in turns its seconds into game milliseconds as a 32-bit
    /// <c>seconds × 1000</c> (0 raised to 1) times 8, which wraps above 536,870 seconds and turns a negative count into a
    /// delay of weeks. Steamwoven computes <c>seconds × 8000</c> without wrapping and reads a negative count as 0 (8 ms,
    /// as the original's 0 seconds).
    /// </summary>
    public bool CallDelayWraps { get; init; }

    /// <summary>Recorded fixes; the default setting.</summary>
    public static readonly ScrRuleProfile Steamwoven = new() { Name = "steamwoven" };

    /// <summary>The retail behaviour.</summary>
    public static readonly ScrRuleProfile Original = new() { Name = "original", GlobalFlagWriteSpills = true, TileScriptChangesDiscarded = true, CallDelayWraps = true };

    /// <summary>Both settings, default first.</summary>
    public static IReadOnlyList<ScrRuleProfile> All { get; } = new[] { Steamwoven, Original };

    /// <summary>The setting with this name, or null when there is none.</summary>
    public static ScrRuleProfile? FromName(string? name) => All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
}
