namespace Arcanum.Simulation.Magic;

/// <summary>
/// What the per-target stage reads besides the run, its record and the target: the target's spell flags now (the object's
/// field; the static fields until P1 brings them into the session), the source's spell flags the shell test read (BEGIN and
/// MAINTAIN with a source; 0 otherwise), whether the parent is the local player, the caster of the full reflection on the target
/// (the parent of a run whose components set it on the target, 0x459040), the last cost any payment charged (0x5E762C, MG15) and
/// the reflected run's own cost of this action (Steamwoven: what its stage asked this pass; 0 for a free, item or technology cast).
/// </summary>
public sealed record SpellTargetStageFacts(int TargetSpellFlags, int SourceSpellFlags = 0, bool ParentLocal = false, SpellRunObject? ReflectionCaster = null,
    int LastCostCharged = 0, int ReflectedCost = 0);

/// <summary>The reflecting caster's payment: who pays and the cost (spell 10000, applied).</summary>
public readonly record struct SpellReflectionPayment(SpellRunObject Payer, int Cost);

/// <summary>
/// The per-target stage's answer: whether the target goes on to the resistance and the components, the run after it (reflected,
/// its source swapped), the current target after it, the local player's message (603 a shelled target, 602 a reflection the
/// caster reflects back) with the maintain bar refreshed, and the reflecting caster's payment.
/// </summary>
public sealed record SpellTargetStageResult(bool Passes, SpellRun Run, SpellRunTarget Target, int? Message = null, bool MaintainBar = false,
    SpellReflectionPayment? Payment = null);

/// <summary>
/// ARC-MAGIC-011 (magic plan MT10): the original's per-target stage (0x455550) between a run's target list and its components,
/// with the no-stack test (0x453410), the cancels (0x4534E0), the environment test (0x458A80) and the reflection's caster
/// (0x459040), as the 1.0.7.4 executable has them (contract docs/world/spell-target-stage.md, ledger "Spell per-target stage",
/// PT rows). Pure functions; the run system (<see cref="SpellRunSystem"/>) calls them in the original's order (O10: the shell,
/// then the reflection; the resistance after them).
/// </summary>
public static class SpellTargetStage
{
    /// <summary>The spell flags the stage tests: the anti-magic shell (OSF_ANTI_MAGIC_SHELL), full reflection
    /// (OSF_FULL_REFLECTION) and detecting invisible (OSF_DETECTING_INVISIBLE).</summary>
    public const int AntiMagicShell = 0x800, FullReflection = 0x2000, DetectingInvisible = 0x200;
    /// <summary>spell.mes: a shelled target (603), a reflection the caster reflects back (602).</summary>
    public const int MessageShelledTarget = 603, MessageReflectedBack = 602;
    /// <summary>The stat whose extraordinary value stands for detecting invisible in the environment test (perception).</summary>
    public const int Perception = 5;

    /// <summary>
    /// The per-target stage (0x455550): no target object, or the run's parent, passes. A target under an anti-magic shell is
    /// skipped unless the run itself set the shell (603 to the local player). A target with full reflection — or any target of a
    /// run already reflected — turns the run unless the record has <c>No_Reflect:</c>: the run is marked reflected; unless the run
    /// itself set the reflection, the caster of the target's reflection pays (the last cost charged in the original, MG15; the
    /// reflected cost in Steamwoven); then unless the target is the run's source or the run set the reflection, a source that
    /// reflects too makes the spell fizzle on it (602 to the local player), and otherwise the run's source and the target swap.
    /// </summary>
    [OriginalFunction(0x455550, OriginalFunctionScope.Partial)]
    public static SpellTargetStageResult Decide(SpellRun run, SpellRunRecord record, SpellRunTarget target, SpellTargetStageFacts facts,
        MagicAnomalyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(profile);
        if (target.Object is not { } obj || obj.Id == run.Parent?.Id) return new(true, run, target);
        var flags = facts.TargetSpellFlags;
        if ((flags & AntiMagicShell) != 0 && (run.SetSpellFlags & AntiMagicShell) == 0)
            return new(false, run, target, facts.ParentLocal ? MessageShelledTarget : null, facts.ParentLocal);
        if ((flags & FullReflection) == 0 && (run.Flags & SpellRunFlags.Reflected) == 0) return new(true, run, target);
        if (record.NoReflect) return new(true, run, target);
        run = run with { Flags = run.Flags | SpellRunFlags.Reflected };
        SpellReflectionPayment? payment = null;
        if ((run.SetSpellFlags & FullReflection) == 0 && (flags & FullReflection) != 0 && facts.ReflectionCaster is { } caster)
            payment = new(caster, profile.ReflectionPaysLastCost ? facts.LastCostCharged : facts.ReflectedCost);
        if (obj.Id == run.Source?.Id || (run.SetSpellFlags & FullReflection) != 0) return new(true, run, target, Payment: payment);
        if ((facts.SourceSpellFlags & FullReflection) != 0)
            return new(false, run, target, facts.ParentLocal ? MessageReflectedBack : null, facts.ParentLocal, payment);
        return new(true, run with { Source = obj }, new(run.Source, target.Tile), Payment: payment);
    }

    /// <summary>
    /// The no-stack test (0x453410, the stage at BEGIN for a record with <c>No_Stack:</c>): the first other active run (in slot
    /// order) of the same spell on <paramref name="on"/> as its target object, or — without a target object — of a self spell
    /// whose parent is <paramref name="on"/>; null for none.
    /// </summary>
    [OriginalFunction(0x453410, OriginalFunctionScope.Full)]
    public static int? Blocking(SpellRunTable table, SpellRun run, SpellRunObject on, Func<int, SpellRunRecord> records)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(on);
        ArgumentNullException.ThrowIfNull(records);
        foreach (var other in table.Runs)
        {
            if ((other.Flags & SpellRunFlags.Active) == 0) continue;
            if (other.Target?.Id == on.Id)
            {
                if (other.Spell == run.Spell && other.Id != run.Id) return other.Id;
            }
            else if (other.Target is null && records(other.Spell).SelfSpell && other.Spell == run.Spell && other.Id != run.Id && other.Parent?.Id == on.Id)
                return other.Id;
        }
        return null;
    }

    /// <summary>
    /// The runs a starting run cancels (0x4534E0), in the original's order: every other active run of the same source whose
    /// <c>Cancels_SF:</c> meets this one's; then, when this record has <c>Cancels_EnvSF:</c> and the environment allows it
    /// (<see cref="EnvironmentAllows"/>), every other active run of the same source — the second loop compares this record's
    /// <c>Cancels_SF:</c> with its own <c>Cancels_EnvSF:</c> (MG33: no installed record has <c>Cancels_EnvSF:</c>).
    /// </summary>
    [OriginalFunction(0x4534E0, OriginalFunctionScope.Full)]
    public static IReadOnlyList<int> Cancelled(SpellRunTable table, SpellRun run, Func<int, SpellRunRecord> records, Func<int, bool> environmentAllows)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(environmentAllows);
        var cancelled = new List<int>();
        if ((run.Flags & SpellRunFlags.Active) == 0) return cancelled;
        var record = records(run.Spell);
        bool SameSource(SpellRun other) => (other.Flags & SpellRunFlags.Active) != 0 && other.Source?.Id == run.Source?.Id && other.Id != run.Id;
        if (record.CancelsSpellFlags != 0)
            cancelled.AddRange(table.Runs.Where(o => SameSource(o) && (records(o.Spell).CancelsSpellFlags & record.CancelsSpellFlags) != 0).Select(o => o.Id));
        if (record.CancelsEnvironmentSpellFlags != 0 && environmentAllows(record.CancelsEnvironmentSpellFlags) &&
            (record.CancelsEnvironmentSpellFlags & record.CancelsSpellFlags) != 0)
            cancelled.AddRange(table.Runs.Where(SameSource).Select(o => o.Id));
        return cancelled;
    }

    /// <summary>The environment test (0x458A80): the local player carries every spell flag of <paramref name="mask"/>, or the mask
    /// is detecting invisible alone and the player's perception is extraordinary; false without a local player.</summary>
    [OriginalFunction(0x458A80, OriginalFunctionScope.Full)]
    public static bool EnvironmentAllows(int mask, int? playerSpellFlags, bool playerPerceptionExtraordinary) =>
        playerSpellFlags is { } flags && ((flags & mask) == mask || (mask == DetectingInvisible && playerPerceptionExtraordinary));

    /// <summary>The caster of a full reflection (or any flag set) on <paramref name="target"/> (0x459040): when the target carries
    /// any flag of <paramref name="mask"/>, the parent of the first run (in slot order) with a source whose target object it is
    /// and whose components set those flags; null otherwise. The run's object lists (+0x130, +0x134) are not modelled.</summary>
    [OriginalFunction(0x459040, OriginalFunctionScope.Partial)]
    public static SpellRunObject? FlagCaster(SpellRunTable table, SpellRunObject target, int targetSpellFlags, int mask)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(target);
        if (mask == 0 || (targetSpellFlags & mask) == 0) return null;
        return table.Runs.FirstOrDefault(r => r.Source is not null && r.Target?.Id == target.Id && (r.SetSpellFlags & mask) == mask)?.Parent;
    }
}
