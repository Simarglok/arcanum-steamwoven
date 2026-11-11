using System.Collections.Immutable;
using Arcanum.Data.Magic;
using Arcanum.Simulation.Magic;

namespace Arcanum.Session;

/// <summary>
/// One component of a translated record (ARC-MAGIC-014): its action and its index among that action's components (the run rules'
/// component number), its type, its own area (the gate's test, MT8), its <c>Apply_AoE:</c> (the re-aim), its item triggers, and the
/// typed data the components implemented so far read (<see cref="Damage"/> for Damage and Heal, MT12; <see cref="EyeCandy"/> for
/// EyeCandy, ARC-MAGIC-017).
/// </summary>
public sealed record SessionSpellComponent(SpellRunAction Action, int Index, SpellComponentType Type, SpellTargetArea Area, ulong ApplyTargets,
    int ItemTriggers, SpellDamageComponent? Damage)
{
    public SpellEyeCandyComponent? EyeCandy { get; init; }
    /// <summary>ARC-DEV-141: an AGoal component's animation goal number (43 the knockback, 24 follow, 44 floating, 32 destroy, 62 knock-down, 0 animate), else null.</summary>
    public int? AnimationGoal { get; init; }
    /// <summary>ARC-DEV-143: an AGoalTerminate component's animation goal number (the goal whose runs on the target end), else null.</summary>
    public int? TerminatedGoal { get; init; }
    /// <summary>ARC-DEV-302: an ObjFlag component's field number, bit and on or off, else null.</summary>
    public SpellObjectFlagComponent? ObjectFlag { get; init; }
    /// <summary>ARC-MAGIC-044: a ChargeNBranch component's <c>Cost:</c> and <c>Branch:</c>, else null.</summary>
    public ChargeBranchData? ChargeBranch { get; init; }
    /// <summary>ARC-MAGIC-044: a TestNBranch component's field, comparison, <c>TestVal:</c> and <c>Branch:</c>, else null.</summary>
    public TestBranchData? TestBranch { get; init; }
    /// <summary>ARC-DEV-303: an Effect component's effect number, add or remove, <c>Count:</c>, cause and <c>Scaled:</c>, else null.</summary>
    public EffectData? Effect { get; init; }
    /// <summary>ARC-DEV-352: a Summon component's <c>Proto:</c> (the basic prototype number, 0 for a list pick), <c>Clear_Faction:</c>, <c>Palette:</c> and <c>List:</c>, else null.</summary>
    public SummonData? Summon { get; init; }
    /// <summary>ARC-MAGIC-036: a Movement component's <c>Move_Location:</c> and <c>Tile_Radius:</c>, else null.</summary>
    public MovementData? Movement { get; init; }
    /// <summary>ARC-MAGIC-040: a Trait component's field, value (the species, −1 restores) and <c>Palette:</c>, else null.</summary>
    public TraitData? Trait { get; init; }
    /// <summary>ARC-MAGIC-049: a Cast component's <c>Spell:</c> (10000 when the key is absent), else null (an Interrupt component's <c>MagicTech:</c> is not read).</summary>
    public SpellReferenceData? Cast { get; init; }
}

/// <summary>ARC-MAGIC-017: an EyeCandy component's data: its number in the spell's eye-candy group, Add or Remove, the <c>Play:</c> flags.</summary>
public sealed record SpellEyeCandyComponent(int Number, bool Add, uint Play);

/// <summary>
/// A spell record of the data (<see cref="SpellRecord"/>, ARC-MAGIC-001/002) in the rules' own types (ARC-MAGIC-014): the run rules'
/// record (cost, maintenance, duration, flags, the invocation check's record, the components per action), the resistance's record,
/// the target area of each action (line +0) and the components in file order with their per-action index.
/// </summary>
public sealed record SessionSpellRecord(SpellRecord Data, SpellRunRecord Run, SpellResistRecord Resist, ImmutableArray<SpellTargetArea> Areas,
    ImmutableArray<SessionSpellComponent> Components)
{
    /// <summary>The components of one action, in their run-rules order.</summary>
    public IEnumerable<SessionSpellComponent> Of(SpellRunAction action) => Components.Where(c => c.Action == action);

    /// <summary>The component a run's action processes as number <paramref name="index"/>, or null.</summary>
    public SessionSpellComponent? Component(SpellRunAction action, int index) =>
        Components.FirstOrDefault(c => c.Action == action && c.Index == index);
}

/// <summary>
/// ARC-MAGIC-014: the translation of the spell data into the magic rules' types, for any record. Field by field as the original's
/// record holds them (ledger "Spell records" SR rows, RS9): the BEGIN area's self bit (+0x70 bit 0x1) makes a self spell, the
/// aggressive bit of <c>Info:</c> (0x2, aggressive and may-be-aggressive) and a Damage component (0x80) feed the resistance, and
/// <c>No_Stack:</c> as written (non-zero) turns the no-stack test on.
/// </summary>
public static class SessionSpellRecords
{
    /// <summary>The self bit of an area's target word (<c>Tgt_Self</c>).</summary>
    public const ulong SelfTarget = 0x1;

    public static SessionSpellRecord Translate(SpellRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var areas = record.Areas.Select(Area).ToImmutableArray();
        var counts = new int[SpellRecord.Actions];
        var components = ImmutableArray.CreateBuilder<SessionSpellComponent>(record.Components.Count);
        foreach (var component in record.Components)
        {
            var action = (SpellRunAction)(int)component.Action;
            var damage = component.Data is DamageData d && component.Type is SpellComponentType.Damage or SpellComponentType.Heal
                ? new SpellDamageComponent(d.DamageType, d.Minimum, d.Maximum, unchecked((int)d.Flags)) : null;
            components.Add(new(action, counts[(int)action]++, component.Type, Area(component.Area), component.ApplyTargets,
                unchecked((int)component.ItemTriggers), damage)
            { EyeCandy = component.Data is EyeCandyData e ? new(e.EyeCandy, e.Add, e.Play) : null, AnimationGoal = component.Data is AnimationGoalData a ? a.Goal : null,
                TerminatedGoal = component.Data is AnimationGoalTerminateData t ? t.Goal : null,
                ObjectFlag = component.Data is ObjectFlagData o ? new(o.Field, o.Flag, o.On) : null,
                Effect = component.Data as EffectData, Summon = component.Data as SummonData, Movement = component.Data as MovementData, Trait = component.Data as TraitData,
                ChargeBranch = component.Data as ChargeBranchData, TestBranch = component.Data as TestBranchData,
                Cast = component.Type == SpellComponentType.Cast ? component.Data as SpellReferenceData : null });
        }
        var invocation = new SpellInvocationRecord(record.IsTechnology, record.Range, unchecked((int)record.DisallowedCasterSpellFlags),
            unchecked((int)record.DisallowedTargetSpellFlags), unchecked((int)record.DisallowedTargetCritterFlags));
        var run = new SpellRunRecord(record.IsTechnology, record.NoResist, record.SkipsBeginCost, record.Cost, record.MaintainCost, record.MaintainPeriod,
            new(record.DurationSeconds, record.DurationStat, record.DurationStatLevel, record.DurationStatModifier), record.DurationTriggerCount,
            record.MinimumWillpower, record.CastingAnimation, record.NoStack != 0, record.ChargesFromCells, invocation)
        {
            ActionComponents = [.. counts],
            ItemTriggers = record.ItemTriggers != 0,
            NoReflect = record.NoReflect,
            CancelsSpellFlags = unchecked((int)record.CancelsSpellFlags),
            CancelsEnvironmentSpellFlags = unchecked((int)record.CancelsEnvironmentSpellFlags),
            SelfSpell = areas.Length > 0 && (areas[0].Targets & SelfTarget) != 0,
        };
        var resist = new SpellResistRecord(record.Number, record.IsTechnology, record.HasDamage, ((int)record.Info & (int)SpellInfo.Aggressive) != 0,
            record.MaintainPeriod, record.ResistStat, record.ResistModifier);
        return new(record, run, resist, areas, components.ToImmutable());
    }

    private static SpellTargetArea Area(SpellArea area) =>
        new(area.Targets, area.RequiredSpellFlags, area.ExcludedSpellFlags, area.Radius, area.Count);
}
