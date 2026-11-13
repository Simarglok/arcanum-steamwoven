using Arcanum.Data.Magic;
using Arcanum.Simulation;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Items;
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
/// setter stores after the clamp alone for an NPC (ARC-MAGIC-1507 adds magick and tech points, 22 and 23, whose rewield <see cref="RewieldReady"/> accepts when no worn item has a wield script): level (17), experience (18, the level-up 0x4A69C0 is a PC's), alignment (19, the followers' re-check is a PC's), fate points (20), unspent points (21) and 25; the
/// callbacks after the write (0x460860, 0x4601C0) are the user interface's. <b>Strength</b> (ARC-MAGIC-1506, case 0 of the setter) reads the encumbrance level before the store (0x45F790: the total weight of the NPC's list, 0x461410, and its carry weight, stat 8, which the new strength gives:
/// <see cref="GameSessionOwner.QueryNpcCarry"/>) and, when the level changed, runs the recalculation 0x45F820 after it (<see cref="GameSessionOwner.RecalculateNpcEncumbrance"/>: every entry of effect 158 leaves the NPC's list, then one
/// entry a level is added with the item cause, eight at the heaviest). Ready for an NPC the map's inventory seed lists; not ready when the NPC is no holder of it, when effect 158 has no rule to add, and when the new level has the
/// fatigue timer (level 4 and above: 0x45E820 mode 1 is kept for the hero alone) or the old and new level are not both readable; nothing is written then. The level message is the local PC's and the animation speed
/// (0x436FA0) the host's. <b>Not ready</b> (issues name them): poison (the poison timer 0x4B1350, the life state) and gender and race (the innate effect removal 0x4EA2E0 and add).
/// ARC-MAGIC-1510: an NPC whose stats the follower's advancement record holds reads and writes its sheet (<see cref="GameSessionOwner.WriteNpcAdvancementStat"/>), and the hero is written in its state (<c>TraitIndexHero</c>; the hero's resistance, magick and
/// tech points, stat 25 and a level-up stay not ready, #1628).</item>
/// </list>
/// </summary>
public sealed partial class SessionSpellCaster
{
    /// <summary>The object fields TraitIdx writes: the indexed base stats (<c>obj_f_critter_stat_base_idx</c>) and the indexed resistances (<c>obj_f_resistance_idx</c>).</summary>
    public const int StatIndexField = 220, ResistanceIndexField = 31;

    // The stats the host runs as the setter does for an NPC: the primary stats (strength with its encumbrance recalculation, see below; the guards for intelligence and willpower included) and the stats the setter stores after the clamp alone (the others are not ready, see above).
    private static bool ReadyStat(int index) => index is 0 or 1 or 2 or 3 or 4 or 5 or 6 or 7 or 17 or 18 or 19 or 20 or 21 or 22 or 23 or 25;

    // The reasons of a refused lowering (the event's value).
    private const int RefusedBySkill = 0, RefusedBySpell = 1, RefusedByDegree = 2;

    // 0x452D80: a target that is no PC or NPC does nothing (the executor tests the cached type); the hero, a stat the setter does more with and an NPC without stored values are not ready.
    [OriginalFunction(0x452D80, OriginalFunctionScope.Partial)]
    private SpellComponentOutcome? TraitIndexComponent(SpellRun run, SessionSpellComponent component, SpellRunTarget target)
    {
        if (component.TraitIdx is not { } data) return null;
        if (target.Object is not { } obj || obj.Type is not (SpellObjectFacts.PcType or SpellObjectFacts.NpcType)) return SpellComponentOutcome.None;
        if (obj.Type == SpellObjectFacts.PcType) return obj.Id == Hero ? TraitIndexHero(run, data) : null;
        if (!obj.Id.StartsWith(ObjectStateVocabulary.EntityPrefix, StringComparison.Ordinal)) return null;
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
        if (!ReadyStat(data.Index)) return null;
        // ARC-MAGIC-1510: a follower with an advancement record holds its primary stats, level, unspent points and magick and tech points in the record's sheet, which then gives the current value and the guards' skills and degrees, and takes the write.
        var recorded = _owner.QueryNpcAdvancement(obj.Id) is { } held && GameSessionOwner.SheetHoldsStat(data.Index) ? held.Sheet : null;
        if (recorded is not null) stats = SheetOver(stats, recorded);
        // Magick and tech points run the rewield (0x465530) after the store: with a worn item that carries a wield script the session cannot run it for an NPC, so nothing is written.
        if (data.Index is 22 or 23 && !RewieldReady(obj.Id, map)) return null;
        if (stats[27] is < 0 or > 10) return null;
        if (CharacterStatRules.StoredRange((CharacterRace)stats[27], data.Index) is not { } range) return null;
        var current = stats[data.Index];
        var next = Math.Clamp(unchecked(current + data.Value), range.Minimum, range.Maximum);
        // Only the primary stats have skills keyed on them, and spells and degrees ask for intelligence and willpower alone.
        if (next < current && data.Index <= 7)
        {
            var stat = (CharacterStat)data.Index;
            // The skills and degrees the guards read are the record's when it holds the stat, else the stored fields'.
            IReadOnlyList<int>? basic = recorded?.BasicSkills ?? critter.StoredBasicSkills, tech = recorded?.TechSkills ?? critter.StoredTechSkills, degrees = recorded?.TechDegrees ?? critter.StoredDegrees;
            // The guards read the stat after the write: the stored value of the others, the lower base, the NPC's effects.
            if (_owner.QueryNpcEffectiveStat(critter.Combat.Target, stats, stat, next) is not { } effective) return null;
            // The spell ranks the NPC knows: the session's book over its fields'.
            if (LoweringRefusal(stat, current, next, effective, basic, tech, _owner.QueryNpcSpellBook(obj.Id) ?? critter.Book, degrees) is not { } refused) return null;
            if (refused >= 0)
            {
                _log.Add(new(SpellCastEventKind.TraitIndexRefused, run.Id, refused, obj.Id, data.Index));
                return SpellComponentOutcome.None;
            }
        }
        // ARC-MAGIC-1506 (case 0 of the setter): strength reads the encumbrance level before the store and recalculates after it; the recalculation 0x45F820 acts only when the level changed.
        SessionEffectDefinition? encumbrance = null;
        var level = -1;
        if (data.Index == 0 && next != current)
        {
            if (_owner.QueryNpcCarry(map, critter.Combat.Target, stats, current) is not { } before || _owner.QueryNpcCarry(map, critter.Combat.Target, stats, next) is not { } after) return null;
            if (before.Level != after.Level)
            {
                // Not ready: the fatigue timer of a level from 4 (0x45E820 mode 1 is not kept for an NPC), and a level without the rule of effect 158 to add.
                if (CarryRules.DrainIntervalMs(after.Level) is not null || _owner.EncumbranceRule() is not { } rule) return null;
                (encumbrance, level) = (rule, after.Level);
            }
        }
        var written = recorded is not null ? _owner.WriteNpcAdvancementStat(_owner.Version, map, obj.Id, data.Index, next) : _owner.WriteCritterStat(_owner.Version, map, obj.Id, data.Index, next);
        var outcome = Report(run, obj.Id, data, written);
        if (encumbrance is not null && written.Disposition != GameSessionDisposition.Rejected && _owner.RecalculateNpcEncumbrance(critter.Combat.Target, encumbrance, level) is { Disposition: GameSessionDisposition.Rejected } failed)
            _log.Add(new(SpellCastEventKind.TraitIndexNotSaved, run.Id, (int)failed.Failure, obj.Id));
        return outcome;
    }

    // ARC-MAGIC-1510: the stored stats with the advancement record's sheet laid over the ones it holds (primary stats, level, unspent points, magick and tech points).
    private static IReadOnlyList<int> SheetOver(IReadOnlyList<int> stats, AdvancementSheet sheet)
    {
        var values = stats.ToArray();
        foreach (var index in new[] { 0, 1, 2, 3, 4, 5, 6, 7, 17, 21, 22, 23 }) values[index] = GameSessionOwner.SheetStat(sheet, index)!.Value;
        return Array.AsReadOnly(values);
    }

    // The refusal of a lowering by the setter's guards (0x4C6B50, then for intelligence 0x4B1B90 and 0x4B02B0, for willpower 0x4B1C00): -1 when none refuses, the refusal's reason otherwise, null when a fact the guards read is not there or not valid (not ready).
    private int? LoweringRefusal(CharacterStat stat, int current, int next, int effective, IReadOnlyList<int>? basic, IReadOnlyList<int>? tech, SpellBook? book, IReadOnlyList<int>? degrees)
    {
        if (basic is not { Count: SkillRules.BasicCount } || tech is not { Count: SkillRules.TechCount }) return null;
        var check = SkillRules.CheckBaseChange(stat, current, next, effective, basic, tech);
        if (check.Status == SkillKeyStatStatus.Invalid) return null;
        if (check.Status != SkillKeyStatStatus.Allowed) return RefusedBySkill;
        if (stat is not (CharacterStat.Intelligence or CharacterStat.Willpower)) return -1;
        if (book is not { } known || !SpellBookRules.IsValid(known)) return null;
        var willpower = _owner.AutoLevel?.SpellWillpower ?? AutoLevelRules.DefaultSpellWillpower;
        if (willpower.Length != SpellBookRules.Spells) return null;
        if (!SpellBookRules.RanksHold(known.Ranks, stat == CharacterStat.Intelligence ? _ => SpellBookRules.MinimumIntelligence : spell => willpower[spell], effective)) return RefusedBySpell;
        if (stat != CharacterStat.Intelligence) return -1;
        if (degrees is not { Count: AutoLevelRules.TechDisciplines } || degrees.Any(degree => degree is < 0 or > AutoLevelRules.MaxDegree)) return null;
        return degrees.Any(degree => AutoLevelRules.DegreeMinimumIntelligence(degree) > effective) ? RefusedByDegree : -1;
    }

    /// <summary>The TraitIdx component's write for the hero.</summary>
    /// <remarks>
    /// ARC-MAGIC-1510 (0x452D80 and the setter 0x4B0980 for the PC): the hero's stored base stats are the hero state's (the character owner's primary stats, level, experience, alignment, fate points, unspent points). The component adds its value
    /// to the stored one, clamps it, asks the same guards for a lowering (with the hero's skills, college ranks and tech degrees), stores it and leaves the callbacks to the interface. Strength moves the encumbrance level as every revision compares it
    /// (0x45F820); alignment arms the followers' re-check (OL17). A derived stat (8 to 16) is read and not written. <b>Not ready</b>: a resistance (the hero stores none), magick and tech points (the rewield 0x465530 runs the worn items' scripts), stat 25, poison, gender
    /// and race, and a level or experience that reaches a level (the recalculation 0x4A69C0 runs the experience award's messages, scheme and followers' step), or without the experience table to tell.
    /// </remarks>
    private SpellComponentOutcome? TraitIndexHero(SpellRun run, TraitIndexData data)
    {
        if (_owner.QueryHero() is not { } hero || data.Field != StatIndexField || data.Index is < 0 or >= SessionCritterIndexedWrites.StatCount) return null;
        if (data.Index is >= 8 and <= 16) return SpellComponentOutcome.None;
        if (_owner.QueryHeroStoredStat(data.Index) is not { } current || CharacterStatRules.StoredRange(hero.Race, data.Index) is not { } range) return null;
        var next = Math.Clamp(unchecked(current + data.Value), range.Minimum, range.Maximum);
        if (data.Index is 17 or 18 && _owner.HeroLevelUpPending(data.Index == 17 ? next : hero.Level, data.Index == 18 ? next : hero.Experience) is not false) return null;
        if (next < current && data.Index <= 7)
        {
            var stat = (CharacterStat)data.Index;
            if (_owner.QueryHeroEffectiveStatAt(stat, next) is not { } effective) return null;
            if (LoweringRefusal(stat, current, next, effective, hero.Advancement.BasicSkills, hero.Advancement.TechSkills, _owner.QueryHeroSpellBook(), hero.Advancement.TechDegrees) is not { } refused) return null;
            if (refused >= 0)
            {
                _log.Add(new(SpellCastEventKind.TraitIndexRefused, run.Id, refused, Hero, data.Index));
                return SpellComponentOutcome.None;
            }
        }
        return Report(run, Hero, data, _owner.WriteHeroStat(_owner.Version, data.Index, next));
    }

    /// <summary>
    /// ARC-MAGIC-1507 (ledger RM3b): the rewield 0x465530 the setter runs after storing magick points (22) or tech points (23) walks the wear slots 1000 to 1008; for each worn item it recalculates the wearer's light (0x467E80)
    /// and runs the item's script points 25 (wield off) and 24 (wield on) with the wearer as the initiator. Ready when the session knows the NPC's list (the holder of its map's inventory view, a dressed NPC's wear record laid
    /// over the map data) and no worn item has script point 24 or 25: the runs then do nothing (a script run of a point with no script returns at once), and the light stays as saved, as it does after the session dresses an NPC (exact for an NPC wearing no light item; with a light item the original's size follows the item's power against the holder's derived stat 16, #1561)
    /// (the carried light is not recalculated for an NPC yet, <c>docs/rendering/area-lighting.md</c>). With a worn wield script, or an NPC the session does not list, it is not ready.
    /// </summary>
    private bool RewieldReady(string npc, SessionMapId map) =>
        _owner.QueryMapInventory(map)?.Holders.FirstOrDefault(holder => holder.Entity == npc && holder.Kind == SessionHolderKind.Npc) is { } listed &&
        !listed.Items.Any(item => item.Facts.Worn && (item.Facts.HasScript(SessionScriptPoint.WieldOn) || item.Facts.HasScript(SessionScriptPoint.WieldOff)));

    private SpellComponentOutcome Report(SpellRun run, string entity, TraitIndexData data, GameSessionMutationResult written)
    {
        _log.Add(written.Disposition == GameSessionDisposition.Rejected ? new(SpellCastEventKind.TraitIndexNotSaved, run.Id, (int)written.Failure, entity)
            : new(SpellCastEventKind.TraitIndexWritten, run.Id, data.Field, entity, data.Index));
        return SpellComponentOutcome.None;
    }
}
