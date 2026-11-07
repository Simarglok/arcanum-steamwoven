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
    /// is none (owner decision 2026-10-21).
    /// </summary>
    public bool GeneratedLinePickSpansGaps { get; init; }

    /// <summary>
    /// GM3 (ARC-DEV-179, <c>$$ −n</c>, 0x415BA0 case 0 with the transfer 0x464830): the action moves the PC's gold to the NPC in one call that does
    /// nothing when the PC holds less than the whole amount, yet it lowers the amount still owed by the PC's whole gold before the followers are
    /// asked, and asks each follower the same way for what is left. So a PC with less than the debt keeps all its gold and the NPC gets only what
    /// the followers pay (the debt less the PC's gold): a hole that lets the PC off; and a payer with no gold stack at all is not refused but credits the NPC the whole
    /// amount owed from nothing. Steamwoven makes each payer pay the lesser of its gold and what is owed, in the same order, a payer without gold nothing.
    /// </summary>
    public bool ShortPayerPaysNothing { get; init; }

    /// <summary>Recorded fixes; the default setting.</summary>
    public static readonly DialogRuleProfile Steamwoven = new() { Name = "steamwoven" };

    /// <summary>The retail behaviour.</summary>
    public static readonly DialogRuleProfile Original = new() { Name = "original", GeneratedLinePickSpansGaps = true, ShortPayerPaysNothing = true };

    /// <summary>Both settings, default first.</summary>
    public static IReadOnlyList<DialogRuleProfile> All { get; } = new[] { Steamwoven, Original };

    /// <summary>The setting with this name, or null when there is none.</summary>
    public static DialogRuleProfile? FromName(string? name) => All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
}
