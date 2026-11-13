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
/// entry a level is added with the item cause, eight at the heaviest). A new level from 4 up also schedules the NPC's encumbrance fatigue timer (ARC-MAGIC-1568, <see cref="SessionEventKinds.NpcEncumbrance"/>: 0x45E820 mode 1, one per NPC; it gives an NPC without a life record the record of its loaded state). Ready for an NPC the map's inventory seed lists; not ready when the NPC is no holder of it, when effect 158 has no rule to add, when that timer cannot be scheduled
/// (the event queue or the life record budget is full) or the old and new level are not both readable; nothing is written then. The level message is the local PC's and the animation speed
/// (0x436FA0) the host's. <b>Poison</b> (stat 24, ARC-MAGIC-1508) is the life state's level (<see cref="PoisonStat"/>). <b>Gender and race</b> (ARC-MAGIC-1509, cases 2 and 3 of the setter) remove the first entry of the gender (9) or race (0) cause from the NPC's effect list before the store (0x4EA2E0) and add the innate effect after it
/// (0x4E9F70: race 0x40 + race with the race cause, gender 0 effect 0x14A with the gender cause), from the rules the host attached (<see cref="GameSessionOwner.AttachInnateEffects"/>; without the rule of the effect to add nothing is written);
/// the race a store reaches (11, the table's last, a race the character rules do not have) is not ready. <b>Not ready</b> (issues name them): an NPC whose stats the follower's advancement record
/// holds, and a hero target (the hero's stats are the character owner's).</item>
/// </list>
/// </summary>
public sealed partial class SessionSpellCaster
{
    /// <summary>The object fields TraitIdx writes: the indexed base stats (<c>obj_f_critter_stat_base_idx</c>) and the indexed resistances (<c>obj_f_resistance_idx</c>).</summary>
    public const int StatIndexField = 220, ResistanceIndexField = 31;

    // The stats the host runs as the setter does for an NPC: the primary stats (strength with its encumbrance recalculation, see below; the guards for intelligence and willpower included) and the stats the setter stores after the clamp alone (the others are not ready, see above).
    private static bool ReadyStat(int index) => index is 0 or 1 or 2 or 3 or 4 or 5 or 6 or 7 or 17 or 18 or 19 or 20 or 21 or 22 or 23 or 24 or 25 or 26 or 27;

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
        // ARC-MAGIC-1508: poison is the life state's (the setter's case 0x18: the constitution test 0x4B0EE0, the timer 0x4B1350), not a stored entry.
        if (data.Index == 24) return PoisonStat(run, obj.Id, data, critter.Combat.Target);
        // Magick and tech points run the rewield (0x465530) after the store: with a worn item that carries a wield script the session cannot run it for an NPC, so nothing is written.
        if (data.Index is 22 or 23 && !RewieldReady(obj.Id, map)) return null;
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
        // ARC-MAGIC-1506 (case 0 of the setter): strength reads the encumbrance level before the store and recalculates after it; the recalculation 0x45F820 acts only when the level changed.
        SessionEffectDefinition? encumbrance = null;
        var level = -1;
        if (data.Index == 0 && next != current)
        {
            if (_owner.QueryNpcCarry(map, critter.Combat.Target, stats, current) is not { } before || _owner.QueryNpcCarry(map, critter.Combat.Target, stats, next) is not { } after) return null;
            if (before.Level != after.Level)
            {
                // Not ready: a level without the rule of effect 158 to add, and a level from 4 whose fatigue timer (ARC-MAGIC-1568) cannot be scheduled (the queue or the life record budget is full, or the NPC's other timers lie on another map).
                if (_owner.EncumbranceRule() is not { } rule || CarryRules.DrainIntervalMs(after.Level) is not null && !_owner.CanScheduleNpcEncumbrance(critter.Combat.Target, map)) return null;
                (encumbrance, level) = (rule, after.Level);
            }
        }
        // ARC-MAGIC-1509 (cases 2 and 3 of the setter, 0x4B0E74): gender and race remove the first innate entry of their cause before the store (0x4EA2E0) and add the new innate effect after it (0x4E9F70: race
        // 0x40 + race with the race cause; gender 0 effect 0x14A with the gender cause, gender 1 adds nothing). The rule of the effect to add is the host's: without it, nothing is written. A race beyond the character
        // rules' ten (11, the table's last) is not ready: the consumers of an NPC's stored fields read none for it.
        SessionEffectDefinition? innate = null;
        var swaps = data.Index is 26 or 27;
        if (swaps)
        {
            if (data.Index == 27 && next > (int)CharacterRace.Orc) return null;
            if (data.Index == 27 || next == 0)
            {
                innate = _owner.InnateRule(data.Index == 27 ? GameSessionOwner.RaceEffectBase + next : GameSessionOwner.FemaleEffectNumber);
                if (innate is null) return null;
            }
            if (_owner.RemoveNpcInnate(critter.Combat.Target, data.Index == 26) is { Disposition: GameSessionDisposition.Rejected } removal)
            {
                _log.Add(new(SpellCastEventKind.TraitIndexNotSaved, run.Id, (int)removal.Failure, obj.Id));
                return SpellComponentOutcome.None;
            }
        }
        var written = _owner.WriteCritterStat(_owner.Version, map, obj.Id, data.Index, next);
        var outcome = Report(run, obj.Id, data, written);
        if (innate is not null && written.Disposition != GameSessionDisposition.Rejected && _owner.AddNpcInnate(critter.Combat.Target, innate, data.Index == 26) is { Disposition: GameSessionDisposition.Rejected } added)
            _log.Add(new(SpellCastEventKind.TraitIndexNotSaved, run.Id, (int)added.Failure, obj.Id));
        if (encumbrance is not null && written.Disposition != GameSessionDisposition.Rejected && _owner.RecalculateNpcEncumbrance(critter.Combat.Target, map, encumbrance, level) is { Disposition: GameSessionDisposition.Rejected } failed)
            _log.Add(new(SpellCastEventKind.TraitIndexNotSaved, run.Id, (int)failed.Failure, obj.Id));
        return outcome;
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

    /// <summary>
    /// ARC-MAGIC-1508 (ledger RM3b, the setter's poison case; the addresses are in RM3b): stat 24 is the poison level, which the original stores in the stat array and the session keeps in the NPC's life state (the map data's entry is only what it loads
    /// with: <c>LifeState.PoisonLevel</c> of the loaded state, then the life record wins). The executor's current value (the raw entry) is therefore the life state's level; the sum wraps in 32 bits, is clamped to 0..1000,
    /// becomes 0 for an effective constitution of 20 or more (the setter's constitution test), and is written through <see cref="GameSessionOwner.SetPoisonLevel"/>, which sets the poison timers of a positive level and gives the NPC a life
    /// record. The interface callback is not the session's. Not ready when the life state cannot be read.
    /// </summary>
    private SpellComponentOutcome? PoisonStat(SpellRun run, string entity, TraitIndexData data, SessionLifeTarget life)
    {
        if (_owner.QueryLife(life).View is not { } view) return null;
        var written = _owner.SetPoisonLevel(_owner.Version, life, unchecked(view.Record.State.PoisonLevel + data.Value));
        return Report(run, entity, data, written.Disposition, written.Failure);
    }

    private SpellComponentOutcome Report(SpellRun run, string entity, TraitIndexData data, GameSessionMutationResult written) => Report(run, entity, data, written.Disposition, written.Failure);

    private SpellComponentOutcome Report(SpellRun run, string entity, TraitIndexData data, GameSessionDisposition disposition, GameSessionFailure failure)
    {
        _log.Add(disposition == GameSessionDisposition.Rejected ? new(SpellCastEventKind.TraitIndexNotSaved, run.Id, (int)failure, entity)
            : new(SpellCastEventKind.TraitIndexWritten, run.Id, data.Field, entity, data.Index));
        return SpellComponentOutcome.None;
    }
}
