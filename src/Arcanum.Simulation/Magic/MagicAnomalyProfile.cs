namespace Arcanum.Simulation.Magic;

/// <summary>
/// The magic rule settings of owner decision D8 (ARC-MAGIC-005, the first magic slice with a switch): <see cref="Steamwoven"/>
/// (the original with the fixes the owner accepted on 2026-10-20, the default) and <see cref="Original"/> (the retail 1.0.7.4
/// behaviour). Each switch is one bug of the original confirmed in the executable and decided in the magic plan's anomaly
/// decisions (docs/research/magic-implementation-plan.md). The profile holds no state, so it may change between casts.
/// </summary>
public sealed record MagicAnomalyProfile
{
    /// <summary>Stable setting name, <c>steamwoven</c> or <c>original</c>; a profile changed with <c>with</c> is a custom mix.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// MG4: the resistance term (a tenth of the margin the d100 gave, or of the resistance in the predictor) is subtracted from
    /// the target's saving throw, so resisting the magic first makes the save harder. Steamwoven adds it, as the manual says
    /// (+1 per 10 %).
    /// </summary>
    public bool SaveSubtractsResistance { get; init; }

    /// <summary>
    /// MG25 (ARC-MAGIC-007): the area test's summoned bit (<c>Tgt_Summoned</c>) checks bit 0x4000 of the flags word the test read
    /// last — the critter flags (0x4000 "surrendered") when the area has the critter bit, a door's or chest's flags after a lock
    /// test — instead of the spell flag "summoned". Steamwoven always checks the spell flag. No installed record reaches it: the
    /// two areas that join the bits (records 183 and 217) are action areas the collection does not test.
    /// </summary>
    public bool SummonedTestReadsLastFlags { get; init; }

    /// <summary>
    /// MG3 (ARC-MAGIC-009): a maintenance tick of a run with a resistance margin charges the plain maintenance and then the
    /// surcharged one. Steamwoven charges only the surcharged maintenance.
    /// </summary>
    public bool MaintenancePaysBoth { get; init; }

    /// <summary>
    /// MG29 (ARC-MAGIC-009, owner decision 2026-10-21, ARC-MAGIC-010): with a resistance margin a maintenance tick charges the
    /// surcharged maintenance even for a free run, after a failed check or payment, and — at least 1 — for a timed spell whose
    /// maintenance cost is 0. Steamwoven takes no surcharge in those three cases.
    /// </summary>
    public bool SurchargeAlways { get; init; }

    /// <summary>
    /// MG15 (ARC-MAGIC-011, owner decision 2026-10-20): a target's full reflection charges the reflecting caster the global "last
    /// cost" any payment charged (0x5E762C), stale for a free run, which paid nothing. Steamwoven charges what the reflected run's
    /// own stage asked in that pass: its BEGIN cost at BEGIN, its maintenance at MAINTAIN, nothing for a free or technology cast
    /// or another action.
    /// </summary>
    public bool ReflectionPaysLastCost { get; init; }

    /// <summary>
    /// MG30 (ARC-MAGIC-011; pending the owner, recommended fix): the runs a starting run's cancels interrupt are refused by the
    /// processing's re-entrancy guard and left without a tick or an END. Steamwoven processes them right after the current pass.
    /// </summary>
    public bool ReEntryDropsRuns { get; init; }

    /// <summary>
    /// MG32 (ARC-MAGIC-011; pending the owner, recommended fix): a run's free removes only its tick events, so an interrupt or
    /// process event outlives it and can hit a new run in the reused slot. Steamwoven removes every event of the run.
    /// </summary>
    public bool FreeKeepsOtherEvents { get; init; }

    /// <summary>
    /// MG11 (ARC-MAGIC-009): the count of a critter's maintained runs (0x450AC0) walks the run table and answers 0, so its free
    /// maintenance slots are always intelligence / 4 (the AI's "no free slot" test). Steamwoven counts the runs that hold its slots.
    /// </summary>
    public bool MaintainedCountIsZero { get; init; }

    /// <summary>
    /// MG19 (ARC-MAGIC-009): one run flag (0x10) means both "unresistable" (<c>No_Resist:</c>, a script's unresistable cast) and
    /// "final END scheduled", so an unresistable spell ends right after BEGIN and a run whose END is scheduled is unresistable.
    /// Steamwoven keeps the two apart.
    /// </summary>
    public bool UnresistableIsFinalEnd { get; init; }

    /// <summary>
    /// MG35 (ARC-MAGIC-012; pending the owner, recommended fix): the damage step takes a full or death hit's amount — the target's
    /// current hit points — and only then applies a PC attacker's difficulty share, so on hard a PC's death spell deals three
    /// quarters of the hit points and the target lives (and on easy one and a half). Steamwoven leaves that amount alone.
    /// </summary>
    public bool DifficultyScalesKillDamage { get; init; }

    /// <summary>
    /// MG36 (ARC-MAGIC-012; pending the owner, recommended fix): the spell Damage component's combat context carries the caster's
    /// wielded weapon, so its ignore-resistance flag makes the spell ignore resistances, its "uber" flag lets the spell hurt a
    /// nigh-invulnerable critter, and its damage-armour flag wears the target's armour. Steamwoven gives the spell no weapon.
    /// </summary>
    public bool SpellDamageUsesCastersWeapon { get; init; }

    /// <summary>
    /// MG37 (ARC-MAGIC-012; pending the owner, recommended fix): a resistance margin takes at least 1 from a Damage component's
    /// amount even when the amount is 0, so a resisted stun-only spell (0–0 damage) deals −1: it heals a hit point (or lowers the
    /// poison level by 1). Steamwoven takes nothing from an amount of 0 or less.
    /// </summary>
    public bool MarginTakesFromNoDamage { get; init; }

    /// <summary>
    /// MG38 (ARC-MAGIC-012; pending the owner, recommended fix): the acid damage's body-armour test compares the slot loop's index
    /// (0..8) with the body-armour slot 1006, so it never holds and the wearer always takes a third of the acid after its worn
    /// items. Steamwoven spares a wearer whose body armour took the acid.
    /// </summary>
    public bool AcidIgnoresBodyArmour { get; init; }

    /// <summary>The original with every recorded fix; the default setting.</summary>
    public static readonly MagicAnomalyProfile Steamwoven = new() { Name = "steamwoven" };

    /// <summary>The retail 1.0.7.4 behaviour.</summary>
    public static readonly MagicAnomalyProfile Original = new()
    {
        Name = "original", SaveSubtractsResistance = true, SummonedTestReadsLastFlags = true, MaintenancePaysBoth = true,
        SurchargeAlways = true, MaintainedCountIsZero = true, UnresistableIsFinalEnd = true, ReflectionPaysLastCost = true, ReEntryDropsRuns = true,
        FreeKeepsOtherEvents = true, DifficultyScalesKillDamage = true, SpellDamageUsesCastersWeapon = true, MarginTakesFromNoDamage = true,
        AcidIgnoresBodyArmour = true,
    };

    /// <summary>Both settings, default first.</summary>
    public static IReadOnlyList<MagicAnomalyProfile> All { get; } = new[] { Steamwoven, Original };

    /// <summary>The setting with this name, or null when there is none.</summary>
    public static MagicAnomalyProfile? FromName(string? name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
}
