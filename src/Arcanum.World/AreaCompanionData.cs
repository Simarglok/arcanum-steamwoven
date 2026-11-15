using Arcanum.Data.Area;
using Arcanum.Data.Characters;
using Arcanum.Data.Combat;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Party;

namespace Arcanum.World;

public enum AreaCompanionDataStatus { Ready, ModuleOutsideGameRoot, MountFailed, StackFailed, AiTableUnavailable, EffectsUnavailable }

/// <summary>
/// ARC-WORLD-037: what the follow check needs of any NPC, read from the game's own data instead of the measured roster:
/// the AI parameter table (ARC-DATA-033) and the effect tables, opened once from the game folder that holds the module
/// archive (<see cref="AreaContentRoot.Discover"/>), each table from the sources of its load phase (ARC-DATA-027).
/// </summary>
public sealed class AreaCompanionData
{
    public const int LevelStat = 17, AlignmentStat = 19;
    /// <summary>The stat getter's clamps (0x4B0FC0, 0x4B0FD0 tables; ledger AP5): level 0..51, alignment −1000..1000.</summary>
    public const int MinLevel = 0, MaxLevel = 51, MinAlignment = -1000, MaxAlignment = 1000;

    private readonly Func<int, EffectRule?> _effect;

    public AreaCompanionData(AiParameterTable table, Func<int, EffectRule?> effect)
    {
        Table = table ?? throw new ArgumentNullException(nameof(table));
        _effect = effect ?? throw new ArgumentNullException(nameof(effect));
    }

    public AiParameterTable Table { get; }

    /// <summary>Opens the tables for the module at <paramref name="modulePath"/>; a typed status, never an exception for data.</summary>
    public static (AreaCompanionDataStatus Status, AreaCompanionData? Data, string Detail) Open(string modulePath)
    {
        ArgumentNullException.ThrowIfNull(modulePath);
        var found = AreaContentRoot.Discover(modulePath);
        if (found.Status != AreaContentRootStatus.Ready)
            return (found.Status == AreaContentRootStatus.MountFailed ? AreaCompanionDataStatus.MountFailed : AreaCompanionDataStatus.ModuleOutsideGameRoot, null, found.Detail);
        var opened = ContentStack.OpenShared(found.Plan!);
        if (opened.Status != ContentStackStatus.Ready) return (AreaCompanionDataStatus.StackFailed, null, opened.Status.ToString());
        using var stack = opened.Stack!;
        var table = AiParameterTable.Open(stack);
        if (table.Table is null) return (AreaCompanionDataStatus.AiTableUnavailable, null, $"{table.Status} {table.Detail}");
        var effects = AreaStartingHero.Table(stack, AreaStartingHero.EffectsPath, ContentLoadPhase.GameStart, required: true, out var detail);
        var module = effects is null ? null : AreaStartingHero.Table(stack, AreaStartingHero.ModuleEffectsPath, ContentLoadPhase.ModuleLoaded, required: false, out detail);
        if (effects is null || detail is not null) return (AreaCompanionDataStatus.EffectsUnavailable, null, detail ?? "effects");
        return (AreaCompanionDataStatus.Ready, new AreaCompanionData(table.Table, number => Effect(effects, module, number)), table.Detail);
    }

    private static EffectRule? Effect(MesFile effects, MesFile? module, int number)
    {
        if (number >= EffectRules.FirstNumber) return EffectRules.Read(effects, number).Rule;
        if (module?.Find(number) is not { } message || EffectRules.Parse(message.Bytes.Span, out var changes) != EffectRuleStatus.Parsed) return null;
        return new(number, true, changes);
    }

    /// <summary>
    /// The follow facts of a dialog NPC from its effective fields (instance over prototype): the AI row its AI data field
    /// names (fields 5..8), and its level and alignment as the stat getter reads them (ledger AP5): the stored stat, then
    /// every effect of its effect list that changes it (the source's aggregation, <see cref="EffectModifiers"/>), then the
    /// clamp; an effect number without a definition changes nothing, as in the original. Null with <paramref name="refusal"/>
    /// when the fields do not read or the row is outside the table. ARC-MAGIC-1551: with a <paramref name="session"/> the level and alignment a
    /// TraitIdx spell wrote into the NPC lie over the stored ones (<see cref="AreaWrittenCritterFields"/>).
    /// </summary>
    public AreaCompanionFacts? Facts(AreaSpeaker speaker, out string? refusal, GameSessionOwner? session = null)
    {
        ArgumentNullException.ThrowIfNull(speaker);
        var read = CombatFieldReader.Read(speaker.Row);
        if (AreaWrittenCritterFields.Over(session, speaker.Row, read.Fields) is not { Critter: { } critter, Npc: { } npc }) { refusal = $"fields {read.Failure}"; return null; }
        if (Table.Lookup(npc) is not { Status: AiParameterLookupStatus.Found, Row: { } row }) { refusal = $"AI row {npc.AiData} outside the table"; return null; }
        var level = new List<EffectModifier>(); var alignment = new List<EffectModifier>();
        foreach (var entry in critter.Effects ?? [])
        {
            if (_effect(entry.Number) is not { } rule) continue;
            foreach (var change in rule.Changes.Where(change => change.Kind == EffectAttributeKind.Stat))
            {
                var modifier = new EffectModifier(Operator(change.Operator), change.Value);
                if (change.Attribute == LevelStat) level.Add(modifier);
                else if (change.Attribute == AlignmentStat) alignment.Add(modifier);
            }
        }
        refusal = null;
        return new(speaker.Row.PrototypeNumber, speaker.ScriptId, new FollowThresholds(row.FollowReaction, row.FollowAlignmentAbove, row.FollowAlignmentBelow, row.JoinLevelGap),
            Math.Clamp(EffectModifiers.Apply(critter.BaseStats[LevelStat], level), MinLevel, MaxLevel),
            Math.Clamp(EffectModifiers.Apply(critter.BaseStats[AlignmentStat], alignment), MinAlignment, MaxAlignment), npc.Wait);
    }

    internal static StatModifierOperator Operator(EffectChangeOperator op) => op switch
    {
        EffectChangeOperator.Add => StatModifierOperator.Add, EffectChangeOperator.Percent => StatModifierOperator.Percent,
        EffectChangeOperator.Minimum => StatModifierOperator.Minimum, EffectChangeOperator.Maximum => StatModifierOperator.Maximum,
        EffectChangeOperator.Multiply => StatModifierOperator.Multiply, EffectChangeOperator.Divide => StatModifierOperator.Divide,
        _ => throw new ArgumentOutOfRangeException(nameof(op)),
    };
}
