namespace Arcanum.Session;

/// <summary>
/// The game-lifecycle settings of owner decision D8 (docs/world/rule-modes.md): what a load keeps and what follows the end
/// of a game. Each switch keeps the retail behaviour where Steamwoven adds to it at the owner's request.
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

    /// <summary>
    /// Gap review GI6 (PS14, PS32): the original never saves its random generator (the Random module has no save or load hook;
    /// it is seeded once from the clock at start-up), so a load goes on with the generator the running process already had
    /// and every later draw differs from the save's own continuation. Under this switch a restore takes the host's running
    /// generator (<see cref="GameSessionRestoreOptions.LoadRandom"/>) in place of the saved one. Steamwoven restores the saved
    /// seed and position, so a reload repeats the same results and save-scumming does not help (owner decision 2026-11-04,
    /// ARC-RES-088; ARC-DEV-077). The original's one saved roll, the skill module's pre-rolled gambling value, is a separate
    /// datum and stays in both modes once the session holds it.
    /// </summary>
    public bool LoadKeepsRunningRandom { get; init; }

    /// <summary>
    /// Gap review GR36 (OM30, OM31, FM7), owner decision GQ7 B (2026-11-04, ARC-RES-088; ARC-DEV-095): a link from an object of map A to an object
    /// that has since left for another map (an NPC's focus, who hit it last, who it flees from, its enemy list naming a companion who went
    /// away) becomes null in the original when A opens again, because the target's handle is not found among the objects then loaded; the group
    /// that travels with the hero arrives together and rebinds. Under this switch the session erases such a link when the hero's map opens
    /// (a change of place, a load). Steamwoven keeps the identity: the link stays, and the NPC can take up the fight when the target returns.
    /// </summary>
    public bool OpeningMapNullsLinksToLeftObjects { get; init; }

    /// <summary>
    /// Ledger CX4, CX7 (ARC-DEV-104, a bug of the game): a sector that loads again folds with the wake-up marker when it was away 3,000 ms of
    /// game time or more, but the original's elapsed time (0x45A7D0) subtracts the day and millisecond words of the clock without a borrow, so
    /// any return on another game day folds, a return across midnight after a moment too: its NPCs are put back on their waypoints or
    /// standpoints and run script point 10. Under this switch the session keeps that; Steamwoven folds after 3 s of true elapsed time
    /// (<see cref="Arcanum.Simulation.Residency.SectorCacheRules.FoldDueAfter"/>).
    /// </summary>
    public bool FoldOnDayChange { get; init; }

    /// <summary>The original with every recorded fix and addition; the default setting.</summary>
    public static readonly LifecycleRuleProfile Steamwoven = new() { Name = "steamwoven" };

    /// <summary>The retail 1.0.7.4 behaviour.</summary>
    public static readonly LifecycleRuleProfile Original = new() { Name = "original", DeathResetsToMainMenu = true, LoadKeepsRunningRandom = true, OpeningMapNullsLinksToLeftObjects = true,
        FoldOnDayChange = true };

    /// <summary>Both settings, default first.</summary>
    public static IReadOnlyList<LifecycleRuleProfile> All { get; } = new[] { Steamwoven, Original };

    /// <summary>The setting with this name, or null when there is none.</summary>
    public static LifecycleRuleProfile? FromName(string? name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
}
