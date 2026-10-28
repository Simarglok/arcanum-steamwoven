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
    /// MG30 (ARC-MAGIC-011, owner decision 2026-10-21: fix): the runs a starting run's cancels interrupt are refused by the
    /// processing's re-entrancy guard and left without a tick or an END. Steamwoven processes them right after the current pass.
    /// </summary>
    public bool ReEntryDropsRuns { get; init; }

    /// <summary>
    /// MG32 (ARC-MAGIC-011, owner decision 2026-10-21: fix): a run's free removes only its tick events, so an interrupt or
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
    /// MG34 (ARC-MAGIC-013, owner decision 2026-10-21: fix): a target the reflection swapped onto the caster and the resistance
    /// skipped leaves the run's source swapped — the processing jumps past the source's restore — so later targets swap with it
    /// and the next pass takes it as the run's source. Steamwoven restores the source for a resisted target too.
    /// </summary>
    public bool ResistedReflectionKeepsSwap { get; init; }

    /// <summary>
    /// MG35 (ARC-MAGIC-012, owner decision 2026-10-21: fix): the damage step takes a full or death hit's amount — the target's
    /// current hit points — and only then applies a PC attacker's difficulty share, so on hard a PC's death spell deals three
    /// quarters of the hit points and the target lives (and on easy one and a half). Steamwoven leaves that amount alone.
    /// </summary>
    public bool DifficultyScalesKillDamage { get; init; }

    /// <summary>
    /// MG36 (ARC-MAGIC-012, owner decision 2026-10-21: fix): the spell Damage component's combat context carries the caster's
    /// wielded weapon, so its ignore-resistance flag makes the spell ignore resistances, its "uber" flag lets the spell hurt a
    /// nigh-invulnerable critter, and its damage-armour flag wears the target's armour. Steamwoven gives the spell no weapon.
    /// </summary>
    public bool SpellDamageUsesCastersWeapon { get; init; }

    /// <summary>
    /// MG37 (ARC-MAGIC-012, owner decision 2026-10-21: fix): a resistance margin takes at least 1 from a Damage component's
    /// amount even when the amount is 0, so a resisted stun-only spell (0–0 damage) deals −1: it heals a hit point (or lowers the
    /// poison level by 1). Steamwoven takes nothing from an amount of 0 or less.
    /// </summary>
    public bool MarginTakesFromNoDamage { get; init; }

    /// <summary>
    /// MG38 (ARC-MAGIC-012, owner decision 2026-10-21: fix): the acid damage's body-armour test compares the slot loop's index
    /// (0..8) with the body-armour slot 1006, so it never holds and the wearer always takes a third of the acid after its worn
    /// items. Steamwoven spares a wearer who wears body armour.
    /// </summary>
    public bool AcidIgnoresBodyArmour { get; init; }

    /// <summary>
    /// MG39 (ARC-MAGIC-014, owner decision 2026-10-27: fix): a casting goal refused before its animation — the parent's action
    /// points refused in a turn-based round, the source cannot afford the cast, or the target is destroyed or off — ends through the
    /// goal's end (0x429CD0), which frees the run with its BEGIN cost charged and applied: the hero pays for a cast that never
    /// started. Steamwoven frees the run unpaid.
    /// </summary>
    public bool GoalRefusalChargesBegin { get; init; }

    /// <summary>
    /// MG40 (ARC-MAGIC-014, owner decision 2026-10-27: fix): the charge of a run freed before its BEGIN (0x456FA0 through 0x4507D0)
    /// tests neither the run's free flag, nor the record's technology flag or <c>ChargeBeginCost: 0</c>, as the BEGIN itself does, so
    /// such a run pays its cost anyway. Steamwoven charges it only when its BEGIN would have paid.
    /// </summary>
    public bool GoalEndChargesAnyRun { get; init; }

    /// <summary>
    /// MG41 (ARC-MAGIC-014, owner decision 2026-10-27: fix): when the target re-check at the release frame (0x456A90) fails, the
    /// release gate has already marked the frame handled, so the goal's end frees nothing and the run keeps its slot until the game
    /// ends. Steamwoven frees it, unpaid.
    /// </summary>
    public bool ReleaseRefusalKeepsRun { get; init; }

    /// <summary>
    /// MG46 (ARC-MAGIC-024; owner decision 2026-10-28, ARC-MAGIC-025: the narrow fix): a run an item event started takes its parent,
    /// the item's holder, as the attacker of its damage step (DH1), whose wrappers walk the worn items again (IC2, IC9), and the
    /// executor has no guard, so an item fires again on the damage of its own run or of a run it caused: a weapon whose
    /// <c>Target_Hit</c> spell damages the opponent fires it once more on its own damage, and two wearers of <c>Parent_Hit</c> armour
    /// answer each other, until the target dies or the charge runs out (installed: weapon 6130, armour 8215). Steamwoven keeps every
    /// walk but lets each item fire at most once in such a chain: a walk of a damage step of a run an item event started skips the
    /// items that started that run or a run before it in its chain (<see cref="ItemSpellTriggerRules.ChainSkips"/>), so the first
    /// answer of another item stays.
    /// </summary>
    public bool ItemRunDamageFiresHooks { get; init; }

    /// <summary>The original with every recorded fix; the default setting.</summary>
    public static readonly MagicAnomalyProfile Steamwoven = new() { Name = "steamwoven" };

    /// <summary>The retail 1.0.7.4 behaviour.</summary>
    public static readonly MagicAnomalyProfile Original = new()
    {
        Name = "original", SaveSubtractsResistance = true, SummonedTestReadsLastFlags = true, MaintenancePaysBoth = true,
        SurchargeAlways = true, MaintainedCountIsZero = true, UnresistableIsFinalEnd = true, ReflectionPaysLastCost = true, ReEntryDropsRuns = true,
        FreeKeepsOtherEvents = true, DifficultyScalesKillDamage = true, SpellDamageUsesCastersWeapon = true, MarginTakesFromNoDamage = true,
        AcidIgnoresBodyArmour = true, ResistedReflectionKeepsSwap = true, GoalRefusalChargesBegin = true, GoalEndChargesAnyRun = true,
        ReleaseRefusalKeepsRun = true, ItemRunDamageFiresHooks = true,
    };

    /// <summary>Both settings, default first.</summary>
    public static IReadOnlyList<MagicAnomalyProfile> All { get; } = new[] { Steamwoven, Original };

    /// <summary>The setting with this name, or null when there is none.</summary>
    public static MagicAnomalyProfile? FromName(string? name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
}
