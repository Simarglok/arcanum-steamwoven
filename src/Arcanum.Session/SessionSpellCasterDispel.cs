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
/// <item>the **object's**, for a PC or NPC: the stoned spell flag (0x10000) cleared by a plain write with no reference count, and then (ARC-MAGIC-039; the first read of 0x451C40 missed it) the
/// object flag stoned (0x80000) cleared through the object flag clear 0x43D280 when it is set (that function also clears the render bit 0x2000000 of field 0x147, which the session does not keep: the viewer
/// reads the object flags), the object **destroyed when its spell flags held the summoned bit** (0x4000, read before the clear), and (ARC-MAGIC-047) a perma-polymorph undone (critter flags 2
/// bit 0x400000: the Trait restore, then the bit cleared by a plain write; the critter flags 2 are the session's since schema 90). A portal's or container's magically held bit
/// (portal flags bit 4, container flags bit 4) is cleared by a plain write (ARC-MAGIC-037, MA-6: <c>SessionSpellCasterLocks.cs</c>).</item>
/// </list>
/// </summary>
public sealed partial class SessionSpellCaster
{
    /// <summary>The detail bits of a <see cref="SpellCastEventKind.Dispelled"/> line: the stoned spell flag cleared, the summoned critter destroyed, the stoned object flag cleared (ARC-MAGIC-039), the perma-polymorph undone (ARC-MAGIC-047).</summary>
    public const int DispelStonedCleared = 1, DispelDestroyed = 2, DispelObjectStonedCleared = 4, DispelPermaPolymorphUndone = 8;

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
    // (its magically held bit); the perma-polymorph is the last step (ARC-MAGIC-047).
    internal void DispelObject(SpellRun run, string id)
    {
        // A portal or container: only its magically held bit (MA-6).
        if (DispelOpenable(run, id)) return;
        var created = SessionCreatedObject.KindOf(id) is SessionCreatedKind.Critter ? _owner.QueryCreatedObject(id) : null;
        if (created is null && id != Hero && !CritterKnown(id)) return;
        // The spell flags are read once: the summoned bit is tested on that value after the stoned clear's write.
        var spell = created is { } made ? unchecked((int)made.SpellFlags) : CurrentFlags(id, SessionObjectFlagField.Spell);
        var detail = 0;
        if (created is null && (spell & StonedSpellFlag) != 0 && WriteFlag(run, id, SessionObjectFlagField.Spell, StonedSpellFlag, false))
        {
            detail |= DispelStonedCleared;
            // 0x43D280 with 0x80000: the object flag stoned goes too when it is set (a plain flag write: the original's clear has no reference count), whatever the spell flag's own write did.
            if ((CurrentFlags(id, SessionObjectFlagField.Object) & ObjectStoned) != 0 && WriteFlag(run, id, SessionObjectFlagField.Object, ObjectStoned, false, sideEffect: true)) detail |= DispelObjectStonedCleared;
        }
        if ((spell & SpellTargetObject.Summoned) != 0 && id != Hero)
        {
            // The destroy waits for the runs' half (it reads the runs' summoned lists first, DP2).
            if (created is not null) { _dispelDestroys.Add((run.Id, id)); detail |= DispelDestroyed; }
            else _log.Add(new(SpellCastEventKind.DispelDestroyNotReady, run.Id, SpellTargetObject.Npc, id));
        }
        // The last step of the object half (DP2): a set perma-polymorph bit (critter flags 2, 0x400000) calls the Trait restore with the object's current art, then clears the bit by a plain write. A critter the
        // summoned bit destroys is not read again here; the viewer's redraw of a map NPC whose data body is the polymorph (the stored art, field 24) is not built.
        var destroyed = (spell & SpellTargetObject.Summoned) != 0 && id != Hero;
        if (created is null && !destroyed && (CurrentFlags(id, SessionObjectFlagField.Critter2) & PermaPolymorphCritterFlag2) != 0)
        {
            TraitRestore(run, id, _owner.QueryTraitArt(id));
            if (WriteFlag(run, id, SessionObjectFlagField.Critter2, PermaPolymorphCritterFlag2, false)) detail |= DispelPermaPolymorphUndone;
        }
        if (detail != 0) _log.Add(new(SpellCastEventKind.Dispelled, run.Id, 0, id, detail));
    }

    // SUM7 (0x4554B0, 0x459740): whether the run's summoned list (+0x134) holds the object. The list is the objects this host's summon map made for the run, dead or alive: the original's list keeps the handle of a
    // creature that was killed (the kill is what tells the runs, ObjectGone), so the answer does not ask the cast map for the object, which has no body for a dead created critter (ARC-MAGIC-053).
    bool ISpellRunHost.SummonedListHolds(SpellRun run, string obj) =>
        SummonMap is { } map && _owner.QueryCreatedObjects(map).Any(made => made.Run == run.Id && made.Entity == obj);
}
