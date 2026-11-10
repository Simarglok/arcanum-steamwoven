namespace Arcanum.Session;

/// <summary>
/// The interface settings of owner decision D8 (docs/world/rule-modes.md): what the game shows the player where the original
/// shows something that does not match what it does.
/// </summary>
public sealed record InterfaceRuleProfile
{
    /// <summary>Stable setting name, <c>steamwoven</c> or <c>original</c>; a profile changed with <c>with</c> is a custom mix
    /// and should get its own name.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Ledger CUA1 (ARC-UI-056): in combat mode the original's cursor refresh (0x553990) shows the called-shot cursor by the held
    /// key, <c>,</c> the head (art 818), <c>.</c> the leg picture (819) and <c>/</c> the arm picture (820), while the attack aims
    /// <c>.</c> at the arm (location 2) and <c>/</c> at the leg (location 3, ledger FA4). Steamwoven shows the picture of the
    /// location the attack aims at.
    /// </summary>
    public bool CalledShotCursorsSwapped { get; init; }

    /// <summary>The original with every recorded fix and addition; the default setting.</summary>
    public static readonly InterfaceRuleProfile Steamwoven = new() { Name = "steamwoven" };

    /// <summary>The retail 1.0.7.4 behaviour.</summary>
    public static readonly InterfaceRuleProfile Original = new() { Name = "original", CalledShotCursorsSwapped = true };

    /// <summary>Both settings, default first.</summary>
    public static IReadOnlyList<InterfaceRuleProfile> All { get; } = new[] { Steamwoven, Original };

    /// <summary>The setting with this name, or null when there is none.</summary>
    public static InterfaceRuleProfile? FromName(string? name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
}
