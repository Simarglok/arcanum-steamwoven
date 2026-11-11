using Arcanum.Simulation.Magic;

namespace Arcanum.Session;

/// <summary>
/// ARC-MAGIC-034 (magic audit MA-3, owner decisions D1–D6 of 2026-11-11; the executable read DP1–DP3, ledger
/// <see href="../../docs/research/ledger/spell-runs.md">spell-runs</see>): the Dispel component (type 8, 0x451B90 → the wrapper 0x451BB0 → 0x451C40) of Disperse Magick (41) and Dweomer Shield (42).
/// It has no data and takes the object the per-target stage handed it (none, as for a tile target: nothing). Its two halves:
/// <list type="bullet">
/// <item>the **runs'**: every run but the one being processed whose source, target or summoned list is the object ends by event, but the runs of a technological record
/// (<see cref="SpellRunSystem.Dispel"/>). The component runs inside the dispelling run's BEGIN, so the endings are told after the outer action, as the destroyed objects' are
/// (<c>_goneObjects</c>); they are events 1 ms later in the original as well, so only the order against the component's own object half differs. A summoned critter the Dispel destroys
/// is destroyed after those endings are told, because the original's loop reads the runs' summoned lists before the destroy (<c>_dispelDestroys</c>);</item>
/// <item>the **object's**, for a PC or NPC: the stoned spell flag (0x10000) cleared by a plain write (no reference count and no side effect: the object flag stoned that the ObjFlag
/// component writes stays as it is), the object **destroyed when its spell flags held the summoned bit** (0x4000, read before the clear), and a perma-polymorph undone (critter flags 2
/// bit 0x400000, with the restore of its art; not modelled: the session keeps no second critter flags field and the Trait restore is MA-8's). A portal's or container's magically held bit
/// (portal flags bit 4, container flags bit 4) is cleared by a plain write (ARC-MAGIC-037, MA-6: <c>SessionSpellCasterLocks.cs</c>).</item>
/// </list>
/// </summary>
public sealed partial class SessionSpellCaster
{
    /// <summary>The detail bits of a <see cref="SpellCastEventKind.Dispelled"/> line: the stoned spell flag cleared, the summoned critter destroyed.</summary>
    public const int DispelStonedCleared = 1, DispelDestroyed = 2;

    // The Dispel component on its target object.
    private SpellComponentOutcome? DispelComponent(SpellRun run, SpellRunTarget target)
    {
        if (target.Object is not { } obj) return SpellComponentOutcome.None;
        // The original's guard (0x5B0BA4) is the run being processed: it is the one run the endings leave alone (Dweomer Shield, sourced by the very object it dispels, must not end itself).
        if (_goneObjects.Count < MaxPendingRunEnds) _goneObjects.Add((SessionSpellRunActionKind.Dispel, obj.Id, run.Id));
        DispelObject(run, obj.Id);
        return SpellComponentOutcome.None;
    }

    // The object's own half (0x451C40, after the runs' loops), for the hero, a critter this host knows, a critter the session created and (ARC-MAGIC-037, MA-6) a portal or container
    // (its magically held bit); the perma-polymorph (MA-8) is not modelled.
    internal void DispelObject(SpellRun run, string id)
    {
        // A portal or container: only its magically held bit (MA-6).
        if (DispelOpenable(run, id)) return;
        var created = SessionCreatedObject.KindOf(id) is SessionCreatedKind.Critter ? _owner.QueryCreatedObject(id) : null;
        if (created is null && id != Hero && !CritterKnown(id)) return;
        // The spell flags are read once: the summoned bit is tested on that value after the stoned clear's write.
        var spell = created is { } made ? unchecked((int)made.SpellFlags) : CurrentFlags(id, SessionObjectFlagField.Spell);
        var detail = 0;
        if (created is null && (spell & StonedSpellFlag) != 0 && WriteFlag(run, id, SessionObjectFlagField.Spell, StonedSpellFlag, false)) detail |= DispelStonedCleared;
        if ((spell & SpellTargetObject.Summoned) != 0 && id != Hero)
        {
            // The destroy waits for the runs' half (it reads the runs' summoned lists first, DP2).
            if (created is not null) { _dispelDestroys.Add((run.Id, id)); detail |= DispelDestroyed; }
            else _log.Add(new(SpellCastEventKind.DispelDestroyNotReady, run.Id, SpellTargetObject.Npc, id));
        }
        if (detail != 0) _log.Add(new(SpellCastEventKind.Dispelled, run.Id, 0, id, detail));
    }

    // SUM7 (0x4554B0): the run's summoned list as the runs' Dispel reads it (+0x134), through the objects this host's summon map holds for the run.
    bool ISpellRunHost.SummonedListHolds(SpellRun run, string obj) => SummonedOf(run).Any(summoned => summoned.Id == obj);
}
