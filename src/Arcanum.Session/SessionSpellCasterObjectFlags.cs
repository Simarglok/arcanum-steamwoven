using Arcanum.Data.Magic;
using Arcanum.Simulation;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;

namespace Arcanum.Session;

/// <summary>ARC-DEV-302: an ObjFlag component's data (its object field number, the one flag bit and on or off).</summary>
public sealed record SpellObjectFlagComponent(int Field, uint Flag, bool On);

/// <summary>
/// ARC-DEV-302 (magic plan MT13, the hold part; the S13b read GT145–GT153): the ObjFlag component (0x454A10) on a critter for the three flags that hold it: entangled (spell flag
/// 0x1000000) and stoned (spell flag 0x10000) in <c>obj_f_spell_flags</c>, paralysed (critter flag 0x40) in <c>obj_f_critter_flags</c>. ARC-DEV-304 (B24, GT216): the spoken-with-dead
/// spell flag (<c>OSF_SPOKEN_WITH_DEAD</c>, 0x2000000, spell 56) is a fourth bit the component takes; the original's component has no side effect for it, only the write with the reference
/// count. The session keeps what it writes (<see cref="GameSessionOwner.WriteObjectFlag"/>); every reader of this host sees the critters' flags with it (<see cref="FlagCastMap"/>).
/// ARC-DEV-358 (magic audit MA-1; the side effects are XR5 of the executable read, 0x454A10 re-read for this slice): the component takes **every bit of the spell flags** (field 20), of
/// the object flags (field 19, <c>obj_f_flags</c>) and of the critter flags (field 218) on the hero and a mobile critter, with the reference count; the side effects the original has are
/// done: invisible, shrunk, water walking and stoned write their object flag, don't-draw writes the paralysed bit of a critter. What stays not ready is named in
/// the not-ready cases (a field this host does not model: the portal and container locks of MA-6, items and the rest of the object types). ARC-MAGIC-035 (MA-4): the social and AI flags (charmed, mind controlled, undead, fleeing, blinded)
/// are written with the original's side effects (<c>SessionSpellCasterSocial.cs</c>).
/// </summary>
public sealed partial class SessionSpellCaster
{
    /// <summary>The object field numbers of the object flags (19, 0x13), the spell flags (20, 0x14) and the critter flags (218, 0xDA).</summary>
    public const int ObjectFlagsField = 19, SpellFlagsField = SpellComponentTables.SpellFlagsField, CritterFlagsField = 218;

    /// <summary>The hold flags (GT145): entangled and stoned among the spell flags, paralysed among the critter flags.</summary>
    public const int EntangledSpellFlag = 0x1000000, StonedSpellFlag = 0x10000, ParalysedCritterFlag = 0x40;

    /// <summary>ARC-DEV-304 (GT216): <c>OSF_SPOKEN_WITH_DEAD</c>, the spell flag spell 56 sets on a dead critter (and removes at its END): the corpse can be spoken with.</summary>
    public const int SpokenWithDeadSpellFlag = 0x2000000;

    /// <summary>ARC-DEV-358 (XR5): the spell flags with a side effect in 0x454A10: invisible (<c>OSF_INVISIBLE</c>) writes the object flags invisible and translucent, shrunk the object flag shrunk,
    /// water walking the object flag water walking, stoned the object flag stoned; the four detection bits (0x3C0: magic, alignment, traps, invisible) call the screen refresh for the local player.</summary>
    public const int InvisibleSpellFlag = 0x1, ShrunkSpellFlag = 0x80000, WaterWalkingSpellFlag = 0x200000, DetectionSpellFlags = 0x3C0;

    /// <summary>ARC-DEV-358: the spell flags whose side effects are social or AI ones: charmed (+30 reaction toward a PC caster) and mind controlled (follow, no flee, −50, disband at the END); ARC-MAGIC-035 (MA-4) writes them (<c>SessionSpellCasterSocial.cs</c>).</summary>
    public const int CharmedSpellFlag = 0x800000, MindControlledSpellFlag = 0x8000000;

    /// <summary>ARC-DEV-358: the critter flags with a function of their own: undead, fleeing, blinded (ARC-MAGIC-035, MA-4). Paralysed is the hold above.</summary>
    public const int UndeadCritterFlag = 0x4, FleeingCritterFlag = 0x10, BlindedCritterFlag = 0x80;

    /// <summary>ARC-DEV-358 (XR5): the object flags the side effects write: invisible (0x200), translucent (0x40), shrunk (0x80), water walking (0x40000), stoned (0x80000) and don't-draw (0x100).</summary>
    public const int ObjectInvisible = 0x200, ObjectTranslucent = 0x40, ObjectShrunk = 0x80, ObjectWaterWalking = 0x40000, ObjectStoned = 0x80000, ObjectDontDraw = 0x100;

    // The ObjFlag component on a critter: true when it was taken (written, or left as the reference count says); false when it is not ready (a bit or field that needs another slice, a
    // target that is no critter), which the caller logs.
    [OriginalFunction(0x452380, OriginalFunctionScope.Partial)]
    private bool ObjectFlag(SpellRun run, SessionSpellComponent component, SpellRunTarget target)
    {
        if (component.ObjectFlag is not { } data || target.Object is not { } obj) return false;
        var mask = unchecked((int)data.Flag);
        if (FieldOf(data.Field) is not { } which) return false;
        var id = obj.Id;
        if (SocialHostMissing(which, mask, id) && IsSocialFlag(which, mask)) return false;
        // A set needs the critter here; a clear needs it here or a change the session already holds for it (the change is kept by the critter's identity, and a run's END may come after
        // its critter left the map; an object the session wrote nothing on and the map does not know is no critter this host may clear a bit on).
        // ARC-MAGIC-035: the blinded condition is the life record's, so an END after its critter left the map still clears it when the session holds the record.
        var recordedBlind = !data.On && which == SessionObjectFlagField.Critter && (mask & BlindedCritterFlag) != 0 && id != Hero && _owner.QueryLifeRecord(id) is not null;
        if (id == Hero ? _owner.QueryHero() is null : (data.On ? _dataMap.Critter(id) is null : !CritterKnown(id) && !recordedBlind)) return false;
        if (id != Hero && !id.StartsWith(ObjectStateVocabulary.EntityPrefix, StringComparison.Ordinal)) return false;
        var count = ProvidingRuns(id, which, mask, data.On);
        // 0x455100 first: setting a bit another active run already provides does nothing; clearing keeps a bit more than one run provides.
        if (data.On ? count > 0 : count > 1)
        {
            _log.Add(new(SpellCastEventKind.ObjectFlagKept, run.Id, mask, id, FlagDetail(which, data.On)));
            return true;
        }
        // ARC-MAGIC-035 (MA-4): the social and AI flags run the original's own order (side effects first, the closing write only where the function does not return before it).
        if (IsSocialFlag(which, mask)) return SocialFlag(run, id, which, mask, data.On);
        // The side effects come first in the original and the field's own write last (0x4550C6); the order is not observable here (one revision per write).
        if (!WriteFlag(run, id, which, mask, data.On)) return true;
        SideEffects(run, id, which, mask, data.On);
        return true;
    }

    // A critter this host knows: on its map, or one the session holds written flags for (it left the map after a spell wrote them).
    private bool CritterKnown(string id) => _dataMap.Critter(id) is not null || !_owner.QueryObjectFlags(id).IsEmpty;

    private static SessionObjectFlagField? FieldOf(int field) => field switch
    {
        SpellFlagsField => SessionObjectFlagField.Spell,
        CritterFlagsField => SessionObjectFlagField.Critter,
        ObjectFlagsField => SessionObjectFlagField.Object,
        _ => null,
    };

    // One write of the session's flag change with its log line; false when the session refused it.
    private bool WriteFlag(SpellRun run, string id, SessionObjectFlagField field, int mask, bool on, bool sideEffect = false)
    {
        var written = _owner.WriteObjectFlag(_owner.Version, id, field, mask, on, DataHas(id, field, mask));
        var rejected = written.Disposition == GameSessionDisposition.Rejected;
        var detail = FlagDetail(field, on) | (sideEffect ? SideEffectDetail : 0);
        _log.Add(rejected
            ? new(SpellCastEventKind.ObjectFlagNotSaved, run.Id, (int)written.Failure, id, detail)
            : new(SpellCastEventKind.ObjectFlagWritten, run.Id, mask, id, detail));
        return !rejected;
    }

    // XR5 (0x454A10, set and clear branches): what a written bit does besides its own write. The object flag writes (0x43D0E0 set, 0x43D280 clear, 0x43EF10 and 0x43EFA0 with the redraw)
    // are the session's object-flags change; the redraw is the viewer's, which reads the flags (the render of a critter with them is MA-8's and the viewer's).
    private void SideEffects(SpellRun run, string id, SessionObjectFlagField field, int mask, bool on)
    {
        if (field == SessionObjectFlagField.Spell)
        {
            if ((mask & InvisibleSpellFlag) != 0)
            {
                // The local player gets the translucent flag only; any other critter the invisible object flag too.
                if (id != Hero) WriteFlag(run, id, SessionObjectFlagField.Object, ObjectInvisible, on, sideEffect: true);
                WriteFlag(run, id, SessionObjectFlagField.Object, ObjectTranslucent, on, sideEffect: true);
            }
            else if ((mask & ShrunkSpellFlag) != 0) WriteFlag(run, id, SessionObjectFlagField.Object, ObjectShrunk, on, sideEffect: true);
            else if ((mask & WaterWalkingSpellFlag) != 0) WriteFlag(run, id, SessionObjectFlagField.Object, ObjectWaterWalking, on, sideEffect: true);
            else if ((mask & StonedSpellFlag) != 0) WriteFlag(run, id, SessionObjectFlagField.Object, ObjectStoned, on, sideEffect: true);
            // GT146: setting entangled interrupts, at level 6, the critter's runs whose bottom or current goal is 3 or 61 (0x44E4D0); the host does it. Stone interrupts nothing.
            else if (on && (mask & EntangledSpellFlag) != 0) _log.Add(new(SpellCastEventKind.HoldStarted, run.Id, mask, id, FlagDetail(field, true)));
        }
        else if (field == SessionObjectFlagField.Critter)
        {
            if (on && (mask & ParalysedCritterFlag) != 0) _log.Add(new(SpellCastEventKind.HoldStarted, run.Id, mask, id, FlagDetail(field, true)));
        }
        else if ((mask & ObjectDontDraw) != 0 && (id == Hero || CritterKnown(id)))
        {
            // Don't-draw on a PC or NPC also sets (or clears) the paralysed critter flag by a plain write: the hidden body does not act (Disintegrate's vanishing body). The original's
            // clear goes on to a function (0x435D70) when the critter's second critter flags have bit 0x2; what it does is not read (F0, MA-1 left-over).
            WriteFlag(run, id, SessionObjectFlagField.Critter, ParalysedCritterFlag, on, sideEffect: true);
        }
    }

    /// <summary>The detail bit of an <see cref="SpellCastEventKind.ObjectFlagWritten"/> line a side effect wrote (the object flags and the paralysed bit of don't-draw), not the component's own flag.</summary>
    public const int SideEffectDetail = 8;

    private static int FlagDetail(SessionObjectFlagField field, bool on) =>
        (field switch { SessionObjectFlagField.Critter => 2, SessionObjectFlagField.Object => 4, _ => 0 }) | (on ? 1 : 0);

    /// <summary>
    /// The reference count 0x455100: for a set, 0 while the critter does not have the bit now; otherwise, for the spell flags only, the active runs whose object is the critter and whose
    /// record sets the bit (its ObjFlag components that turn a spell flag on, +0x114) — the run being processed among them. The critter flags count no run, so paralysis is not counted:
    /// the first of two paralysing runs to end clears the bit (the original's own behaviour). The original also looks for the critter in a run's two object lists (+0x130, +0x134),
    /// which are not modelled: the run's target only.
    /// </summary>
    [OriginalFunction(0x455100, OriginalFunctionScope.Partial)]
    private int ProvidingRuns(string id, SessionObjectFlagField field, int mask, bool setting)
    {
        if (setting && (CurrentFlags(id, field) & mask) == 0) return 0;
        if (field != SessionObjectFlagField.Spell) return 0;
        return _owner.LiveSpellRuns().Runs.Count(r => (r.Flags & SpellRunFlags.Active) != 0 && r.Target?.Id == id
            && _records(r.Spell) is { } record && (unchecked((int)record.Data.SpellFlagsSet) & mask) != 0);
    }

    // Whether the critter's data has the bit (null when the critter is not on this map: its data is unknown here).
    private bool? DataHas(string id, SessionObjectFlagField field, int mask)
    {
        if (id == Hero) return false;
        // Not on this map: a change that sets the bit says the data lacks it (a set equal to the data's value keeps no change); else unknown.
        if (_dataMap.Critter(id) is not { } critter)
        {
            var written = _owner.QueryObjectFlags(id);
            return ((field switch { SessionObjectFlagField.Spell => written.SpellSet, SessionObjectFlagField.Object => written.ObjectSet, _ => written.CritterSet }) & mask) != 0 ? false : null;
        }
        return ((field switch { SessionObjectFlagField.Spell => critter.SpellFlags, SessionObjectFlagField.Object => critter.ObjectFlags, _ => critter.CritterFlags }) & mask) != 0;
    }

    // A critter's flag field now: its data's value with the session's writes.
    private int CurrentFlags(string id, SessionObjectFlagField field)
    {
        var written = _owner.QueryObjectFlags(id);
        var critter = id == Hero ? null : _dataMap.Critter(id);
        return field switch
        {
            SessionObjectFlagField.Spell => written.Spell(critter?.SpellFlags ?? 0),
            SessionObjectFlagField.Object => written.Object(critter?.ObjectFlags ?? 0),
            _ => written.Critter(critter?.CritterFlags ?? 0),
        };
    }

    /// <summary>ARC-DEV-302: whether a critter is held now (the session's writes over its data): entangled, stoned or paralysed.</summary>
    public (bool Entangled, bool Stoned, bool Paralysed) Holds(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var spell = CurrentFlags(id, SessionObjectFlagField.Spell);
        return ((spell & EntangledSpellFlag) != 0, (spell & StonedSpellFlag) != 0, (CurrentFlags(id, SessionObjectFlagField.Critter) & ParalysedCritterFlag) != 0);
    }

    /// <summary>ARC-DEV-304 (GT216): whether a critter carries the spoken-with-dead spell flag now (the session's writes over its data): a dead critter with it is talked to, not looted.</summary>
    public bool SpokenWithDead(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return (CurrentFlags(id, SessionObjectFlagField.Spell) & SpokenWithDeadSpellFlag) != 0;
    }

    /// <summary>
    /// ARC-DEV-302: the map with the critters' flags as the session wrote them: the objects and critters it answers carry their spell and critter flags over the data's. So the
    /// run rules (the forbidden flags at BEGIN, a component area's required and excluded flags, the cancels) and the host's own facts read what a spell wrote.
    /// </summary>
    private sealed class FlagCastMap(ISpellCastMap map, GameSessionOwner owner) : ISpellCastMap
    {
        private SpellTargetObject? Over(SpellTargetObject? o)
        {
            if (o is null) return null;
            var written = owner.QueryObjectFlags(o.Id);
            return written.IsEmpty ? o : o with { ObjectFlags = written.Object(o.ObjectFlags), SpellFlags = written.Spell(o.SpellFlags), CritterFlags = written.Critter(o.CritterFlags) };
        }

        private IReadOnlyList<SpellTargetObject> Over(IReadOnlyList<SpellTargetObject> list) => list.Count == 0 ? list : [.. list.Select(o => Over(o)!)];

        public SpellTargetObject? Object(string id) => Over(map.Object(id));
        public IReadOnlyList<SpellTargetObject> ObjectsInSquare(SpellLocation centre, int radius, int typeMask) => Over(map.ObjectsInSquare(centre, radius, typeMask));
        public IReadOnlyList<SpellTargetObject> ObjectsAt(SpellLocation tile, int typeMask) => Over(map.ObjectsAt(tile, typeMask));
        public bool TileBlocked(SpellLocation tile) => map.TileBlocked(tile);
        public bool PathExists(SpellTargetObject source, SpellLocation tile) => map.PathExists(source, tile);
        public int SectorKind(SpellLocation location) => map.SectorKind(location);
        public int Direction(SpellLocation from, SpellLocation to) => map.Direction(from, to);
        public IReadOnlyList<SpellTargetObject> Party(SpellTargetObject source) => Over(map.Party(source));
        public SpellLineOfFire LineOfFire(SpellObjectFacts parent, SpellLocation tile) => map.LineOfFire(parent, tile);

        public SpellCastCritter? Critter(string entity)
        {
            if (map.Critter(entity) is not { } critter) return null;
            var written = owner.QueryObjectFlags(entity);
            return written.IsEmpty ? critter : critter with { ObjectFlags = written.Object(critter.ObjectFlags), SpellFlags = written.Spell(critter.SpellFlags), CritterFlags = written.Critter(critter.CritterFlags) };
        }

        public CombatWeapon WieldedWeapon(string entity) => map.WieldedWeapon(entity);
        public IReadOnlyList<SpellAcidItem> WornItems(string entity) => map.WornItems(entity);
        public (Combatant Combatant, AttackTargetKind Kind)? ObjectCombat(string id) => map.ObjectCombat(id);
        public bool DamageObject(string id, AttackDamage damage) => map.DamageObject(id, damage);
        public SpellOpenable? Openable(string id) => map.Openable(id);
    }
}
