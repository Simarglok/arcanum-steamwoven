namespace Arcanum.Simulation.Magic;

/// <summary>
/// The two magic rule settings of owner decision D8: <see cref="Steamwoven"/> (the original with the recorded D2 fixes, the
/// default) and <see cref="Original"/> (the retail 1.0.7.4 behaviour, bugs included). Each switch is one bug of the original
/// game confirmed in its executable and decided in docs/research/npc-combat-ai-plan.md#anomaly-decisions (the magic plan's
/// accepted decisions). The profile holds no state, so it may change between casts.
/// </summary>
public sealed record MagicAnomalyProfile
{
    /// <summary>
    /// Stable setting name, <c>steamwoven</c> or <c>original</c>. A profile changed with <c>with</c> is a custom mix and
    /// should get its own name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// MG18: the target name <c>Tgt_Obj_No_ST_Critter_Dead</c> lacks the critter type bit its siblings carry, so an area that
    /// names it does not require its targets to be critters (see <see cref="SpellTargetRules.Targets"/>).
    /// </summary>
    public bool NotDeadCritterTargetLacksCritterBit { get; init; }

    /// <summary>The original with every recorded D2 fix; the default setting.</summary>
    public static readonly MagicAnomalyProfile Steamwoven = new() { Name = "steamwoven" };

    /// <summary>The retail 1.0.7.4 behaviour: every switch keeps the original game's bug.</summary>
    public static readonly MagicAnomalyProfile Original = new() { Name = "original", NotDeadCritterTargetLacksCritterBit = true };

    /// <summary>Both settings, default first.</summary>
    public static IReadOnlyList<MagicAnomalyProfile> All { get; } = new[] { Steamwoven, Original };

    /// <summary>The setting with this name, or null when there is none.</summary>
    public static MagicAnomalyProfile? FromName(string? name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
}

/// <summary>ARC-MAGIC-002: the target masks of spell areas under a rule setting. The data keeps the original's masks
/// (<c>Arcanum.Data.Magic.SpellArea</c>); the setting applies when an area is used, so no state depends on it.</summary>
public static class SpellTargetRules
{
    /// <summary>The value bit only <c>Tgt_Obj_No_ST_Critter_Dead</c> carries (its low word's 0x80000000).</summary>
    public const ulong NotDeadCritterBit = 0x80000000UL;
    /// <summary>The critter type bit (<c>Tgt_Obj_T_Critter</c> is the object bit 0x4 with it).</summary>
    public const ulong CritterBit = 0x40UL;

    /// <summary>The mask an area tests: with MG18 fixed (Steamwoven), a mask naming <c>Tgt_Obj_No_ST_Critter_Dead</c> also
    /// carries the critter bit, as its siblings do; the Original setting keeps the mask as read.</summary>
    public static ulong Targets(ulong mask, MagicAnomalyProfile rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return !rules.NotDeadCritterTargetLacksCritterBit && (mask & NotDeadCritterBit) != 0 ? mask | CritterBit : mask;
    }
}
