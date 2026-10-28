namespace Arcanum.Session;

/// <summary>
/// The game-lifecycle settings of owner decision D8 (docs/world/rule-modes.md): what follows the end of a game. Each switch
/// keeps the retail behaviour where Steamwoven adds to it at the owner's request.
/// </summary>
public sealed record LifecycleRuleProfile
{
    /// <summary>Stable setting name, <c>steamwoven</c> or <c>original</c>; a profile changed with <c>with</c> is a custom mix
    /// and should get its own name.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Ledger PD8: after the hero's death slides the original resets the game to the main menu at once, with no window and no
    /// choice (a load is the main menu's). Steamwoven first asks, in a box like the original's quit box, whether to load the newest
    /// save; No (or no save at all) resets as the original (a project addition the owner asked for, ARC-UI-028).
    /// </summary>
    public bool DeathResetsToMainMenu { get; init; }

    /// <summary>The original with every recorded fix and addition; the default setting.</summary>
    public static readonly LifecycleRuleProfile Steamwoven = new() { Name = "steamwoven" };

    /// <summary>The retail 1.0.7.4 behaviour.</summary>
    public static readonly LifecycleRuleProfile Original = new() { Name = "original", DeathResetsToMainMenu = true };

    /// <summary>Both settings, default first.</summary>
    public static IReadOnlyList<LifecycleRuleProfile> All { get; } = new[] { Steamwoven, Original };

    /// <summary>The setting with this name, or null when there is none.</summary>
    public static LifecycleRuleProfile? FromName(string? name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
}
