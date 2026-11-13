using Arcanum.Data.Magic;
using Arcanum.Simulation;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Skills;

namespace Arcanum.Session;

/// <summary>
/// ARC-MAGIC-1295 (the TraitIdx component, type 22, 0x452D80; ledger RM3): the component adds its value to an indexed field of a PC or NPC target. The parser fixes the multiplier and the divisor at 1 and the flag at 0, so the
/// executor writes <c>current × 1 / 1 + value</c> (wrapping 32-bit arithmetic). <c>current</c> is the <b>stored</b> entry, not the effective stat: for field 220 (<c>obj_f_critter_stat_base_idx</c>) the base stat getter
/// 0x4B0740 (the raw array entry for the stats below 8 and above 16; 8 to 16 are derived from the effective stats), for field 31 (<c>obj_f_resistance_idx</c>) the indexed read 0x4072D0. The stored values of the NPC are the data's
/// with what the session wrote over them (<see cref="SessionCritterIndexedWrites"/>).
/// <list type="bullet">
/// <item><b>Resistance</b> (field 31, index 0..4 normal, fire, electrical, poison, magic): the indexed write 0x407340 stores the sum unclamped.</item>
/// <item><b>Base stat</b> (field 220): the stat setter 0x4B0980. A derived stat (8 to 16) is read but never written. The new value is clamped to the stat's range (0x4B0FC0, 0x4B0FD0: for a primary stat 1 to the race's
/// maximum, <see cref="CharacterStatRules.Range"/>; for the others <see cref="CharacterStatRules.StoredRange"/>); a lowering is refused, and nothing written, when a skill keyed on the stat needs the higher value (0x4C6B50,
/// <see cref="SkillRules.CheckBaseChange"/>, read with the effective stat the lower base gives, the NPC's effect list laid over it) and, for intelligence, when a known spell rank (0x4B1B90) or a tech degree (0x4B02B0)
/// needs the higher value, for willpower when a known spell rank does (0x4B1C00). Ready for an NPC: the primary stats dexterity, constitution, beauty, intelligence, perception, willpower and charisma, and the stats the
/// setter stores after the clamp alone for an NPC: level (17), experience (18, the level-up 0x4A69C0 is a PC's), alignment (19, the followers' re-check is a PC's), fate points (20), unspent points (21) and 25; the
/// callbacks after the write (0x460860, 0x4601C0) are the user interface's. <b>Not ready</b> (issues name them): strength (the encumbrance recalculation 0x45F820 needs the NPC's carried weight), magick and tech points
/// (the rewield 0x465530 runs the worn items' scripts), poison (the poison timer 0x4B1350, the life state), gender and race (the innate effect removal 0x4EA2E0 and add), an NPC whose stats the follower's advancement record
/// holds, and a hero target (the hero's stats are the character owner's).</item>
/// </list>
/// </summary>
public sealed partial class SessionSpellCaster
{
    /// <summary>The object fields TraitIdx writes: the indexed base stats (<c>obj_f_critter_stat_base_idx</c>) and the indexed resistances (<c>obj_f_resistance_idx</c>).</summary>
    public const int StatIndexField = 220, ResistanceIndexField = 31;

    // The stats the host runs as the setter does for an NPC: the primary stats but strength (the guards for intelligence and willpower included) and the stats the setter stores after the clamp alone (the others are not ready, see above).
    private static bool ReadyStat(int index) => index is 1 or 2 or 3 or 4 or 5 or 6 or 7 or 17 or 18 or 19 or 20 or 21 or 25;

    // The reasons of a refused lowering (the event's value).
    private const int RefusedBySkill = 0, RefusedBySpell = 1, RefusedByDegree = 2;

    // 0x452D80: a target that is no PC or NPC does nothing (the executor tests the cached type); the hero, a stat the setter does more with and an NPC without stored values are not ready.
    [OriginalFunction(0x452D80, OriginalFunctionScope.Partial)]
    private SpellComponentOutcome? TraitIndexComponent(SpellRun run, SessionSpellComponent component, SpellRunTarget target)
    {
        if (component.TraitIdx is not { } data) return null;
        if (target.Object is not { } obj || obj.Type is not (SpellObjectFacts.PcType or SpellObjectFacts.NpcType)) return SpellComponentOutcome.None;
        if (obj.Type != SpellObjectFacts.NpcType || !obj.Id.StartsWith(ObjectStateVocabulary.EntityPrefix, StringComparison.Ordinal)) return null;
        if (_map.Critter(obj.Id) is not { StoredStats: { Count: SessionCritterIndexedWrites.StatCount } stats, StoredResistances: { Count: SessionCritterIndexedWrites.ResistanceCount } resistances } critter) return null;
        var map = _owner.QueryActor().Map;
        if (data.Field == ResistanceIndexField)
        {
            if (data.Index is < 0 or >= SessionCritterIndexedWrites.ResistanceCount) return null;
            return Report(run, obj.Id, data, _owner.WriteCritterResistance(_owner.Version, map, obj.Id, data.Index, unchecked(resistances[data.Index] + data.Value)));
        }
        if (data.Field != StatIndexField || data.Index is < 0 or >= SessionCritterIndexedWrites.StatCount) return null;
        // The setter returns the derived stats (8 to 16) unwritten.
        if (data.Index is >= 8 and <= 16) return SpellComponentOutcome.None;
        if (!ReadyStat(data.Index) || _owner.QueryNpcAdvancement(obj.Id) is not null) return null;
        if (stats[27] is < 0 or > 10) return null;
        if (CharacterStatRules.StoredRange((CharacterRace)stats[27], data.Index) is not { } range) return null;
        var current = stats[data.Index];
        var next = Math.Clamp(unchecked(current + data.Value), range.Minimum, range.Maximum);
        // Only the primary stats have skills keyed on them, and spells and degrees ask for intelligence and willpower alone.
        if (next < current && data.Index <= 7)
        {
            if (critter.StoredBasicSkills is not { Count: SkillRules.BasicCount } basic || critter.StoredTechSkills is not { Count: SkillRules.TechCount } tech) return null;
            var stat = (CharacterStat)data.Index;
            // The guards read the stat after the write: the stored value of the others, the lower base, the NPC's effects.
            if (_owner.QueryNpcEffectiveStat(critter.Combat.Target, stats, stat, next) is not { } effective) return null;
            var check = SkillRules.CheckBaseChange(stat, current, next, effective, basic, tech);
            if (check.Status == SkillKeyStatStatus.Invalid) return null;
            var refused = check.Status != SkillKeyStatStatus.Allowed ? RefusedBySkill : -1;
            if (refused < 0 && stat is CharacterStat.Intelligence or CharacterStat.Willpower)
            {
                // The spell ranks the NPC knows (the session's book over its fields'), then for intelligence its tech degrees.
                if ((_owner.QueryNpcSpellBook(obj.Id) ?? critter.Book) is not { } book || !SpellBookRules.IsValid(book)) return null;
                var willpower = _owner.AutoLevel?.SpellWillpower ?? AutoLevelRules.DefaultSpellWillpower;
                if (willpower.Length != SpellBookRules.Spells) return null;
                if (!SpellBookRules.RanksHold(book.Ranks, stat == CharacterStat.Intelligence ? _ => SpellBookRules.MinimumIntelligence : spell => willpower[spell], effective)) refused = RefusedBySpell;
                else if (stat == CharacterStat.Intelligence)
                {
                    if (critter.StoredDegrees is not { Count: AutoLevelRules.TechDisciplines } degrees || degrees.Any(degree => degree is < 0 or > AutoLevelRules.MaxDegree)) return null;
                    if (degrees.Any(degree => AutoLevelRules.DegreeMinimumIntelligence(degree) > effective)) refused = RefusedByDegree;
                }
            }
            if (refused >= 0)
            {
                _log.Add(new(SpellCastEventKind.TraitIndexRefused, run.Id, refused, obj.Id, data.Index));
                return SpellComponentOutcome.None;
            }
        }
        return Report(run, obj.Id, data, _owner.WriteCritterStat(_owner.Version, map, obj.Id, data.Index, next));
    }

    private SpellComponentOutcome Report(SpellRun run, string entity, TraitIndexData data, GameSessionMutationResult written)
    {
        _log.Add(written.Disposition == GameSessionDisposition.Rejected ? new(SpellCastEventKind.TraitIndexNotSaved, run.Id, (int)written.Failure, entity)
            : new(SpellCastEventKind.TraitIndexWritten, run.Id, data.Field, entity, data.Index));
        return SpellComponentOutcome.None;
    }
}
