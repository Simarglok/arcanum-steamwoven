namespace Arcanum.Simulation.Character;

/// <summary>
/// The two experience rule settings of owner decision D8 (contract docs/world/rule-modes.md): <see cref="Steamwoven"/> (the
/// original with the recorded fixes, the default) and <see cref="Original"/> (the retail 1.0.7.4 behaviour). Each switch is
/// a bug of the original game confirmed in its executable (verification ledger). The profile holds no state, so it may
/// change between any two experience decisions.
/// </summary>
public sealed record ExperienceRuleProfile
{
    /// <summary>Stable setting name, <c>steamwoven</c> or <c>original</c>; a profile changed with <c>with</c> is a custom mix
    /// and should get its own name.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Ledger XP3: the progress toward the next level multiplies <c>1000 × experience still needed</c> in 32 bits, which
    /// wraps when more than 2,147,483 experience is still needed (only a table with such a step; the installed one never
    /// has it), as the retail executable does (0x4A6900; found by the function oracle of ARC-RES-051, checked on 10,000
    /// inputs by ARC-DEV-007). Steamwoven keeps the exact product (owner decision 2026-10-17).
    /// </summary>
    public bool ProgressProductWraps { get; init; }

    /// <summary>
    /// Ledger OL28 (decision D6, ARC-UI-046): the level-up message of a PC without an auto-level scheme names the stored unspent
    /// points plus the level's points, summed before the cap of 56 (0x4A6A90–0x4A6AC4), so past the cap it names points the PC
    /// does not have (a cosmetic bug). Steamwoven names the capped value the PC holds (owner decision D6, 2026-10-21).
    /// </summary>
    public bool LevelMessagePointsBeforeCap { get; init; }

    /// <summary>
    /// Ledger OL29 (owner decision D7, 2026-10-21, ARC-WORLD-047): after the followers in its array, the PC's level step reaches only
    /// the waiting followers among the loaded objects inside a rectangle the size of the game window centred on the PC (0x4A69C0,
    /// 0x440FC0), so one waiting farther away or on another map never gets it and stays behind for good. Steamwoven gives the step
    /// to every waiting follower wherever it waits (one whose map is not loaded gets it when its map loads).
    /// </summary>
    public bool WaitingFollowersStepOnlyNearPc { get; init; }

    /// <summary>The original with every recorded fix; the default setting.</summary>
    public static readonly ExperienceRuleProfile Steamwoven = new() { Name = "steamwoven" };

    /// <summary>The retail 1.0.7.4 behaviour.</summary>
    public static readonly ExperienceRuleProfile Original = new()
    {
        Name = "original", ProgressProductWraps = true, LevelMessagePointsBeforeCap = true, WaitingFollowersStepOnlyNearPc = true,
    };

    /// <summary>Both settings, default first.</summary>
    public static IReadOnlyList<ExperienceRuleProfile> All { get; } = new[] { Steamwoven, Original };

    /// <summary>The setting with this name, or null when there is none.</summary>
    public static ExperienceRuleProfile? FromName(string? name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
}
