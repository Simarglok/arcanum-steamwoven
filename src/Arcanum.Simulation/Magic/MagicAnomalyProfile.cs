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

    /// <summary>The original with every recorded fix; the default setting.</summary>
    public static readonly MagicAnomalyProfile Steamwoven = new() { Name = "steamwoven" };

    /// <summary>The retail 1.0.7.4 behaviour.</summary>
    public static readonly MagicAnomalyProfile Original = new()
    {
        Name = "original", SaveSubtractsResistance = true, SummonedTestReadsLastFlags = true, MaintenancePaysBoth = true,
        SurchargeAlways = true, MaintainedCountIsZero = true, UnresistableIsFinalEnd = true,
    };

    /// <summary>Both settings, default first.</summary>
    public static IReadOnlyList<MagicAnomalyProfile> All { get; } = new[] { Steamwoven, Original };

    /// <summary>The setting with this name, or null when there is none.</summary>
    public static MagicAnomalyProfile? FromName(string? name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
}
