namespace Arcanum.Scripting.Dialog;

/// <summary>
/// The dialog switches of the two rule modes (owner decision D8, contract docs/world/rule-modes.md). Each switch is a behaviour of
/// the original 1.0.7.4 executable confirmed in the verification ledger; the original's behaviour is the <c>true</c> value.
/// </summary>
public sealed record DialogRuleProfile
{
    /// <summary>Stable setting name, <c>steamwoven</c> or <c>original</c>. A profile changed with <c>with</c> is a custom
    /// mix and should get its own name.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// GD-A1 (ARC-WORLD-058, 0x4D4550 with the generators 0x418480, 0x4185F0, 0x418780): a generated line is the first number of
    /// its range plus a random offset below the count of entries stored from it, so in a block with a gap it can be a missing
    /// number, shown as the original's missing-line message (here a project-worded text with the number and file), and the block's last entries are never said; without the
    /// first entry the count is 0. Steamwoven picks uniformly among the entries stored in the range and says nothing when there
    /// is none.
    /// </summary>
    public bool GeneratedLinePickSpansGaps { get; init; }

    /// <summary>Recorded fixes; the default setting.</summary>
    public static readonly DialogRuleProfile Steamwoven = new() { Name = "steamwoven" };

    /// <summary>The retail behaviour.</summary>
    public static readonly DialogRuleProfile Original = new() { Name = "original", GeneratedLinePickSpansGaps = true };

    /// <summary>Both settings, default first.</summary>
    public static IReadOnlyList<DialogRuleProfile> All { get; } = new[] { Steamwoven, Original };

    /// <summary>The setting with this name, or null when there is none.</summary>
    public static DialogRuleProfile? FromName(string? name) => All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
}
