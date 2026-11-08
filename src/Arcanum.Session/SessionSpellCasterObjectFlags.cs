using Arcanum.Data.Magic;
using Arcanum.Simulation;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;

namespace Arcanum.Session;

/// <summary>ARC-DEV-302: an ObjFlag component's data (its object field number, the one flag bit and on or off).</summary>
public sealed record SpellObjectFlagComponent(int Field, uint Flag, bool On);

/// <summary>
/// ARC-DEV-302 (magic plan MT13, the hold part; the S13b read GT145–GT153): the ObjFlag component (0x454A10) on a critter for the three flags that hold it: entangled (spell flag
/// 0x1000000) and stoned (spell flag 0x10000) in <c>obj_f_spell_flags</c>, paralysed (critter flag 0x40) in <c>obj_f_critter_flags</c>. The session keeps what it writes
/// (<see cref="GameSessionOwner.WriteObjectFlag"/>); every reader of this host sees the critters' flags with it (<see cref="FlagCastMap"/>). The other bits and fields, and the
/// render side effects of stone (the object flag 0x80000), are not ready and stay logged as such.
/// </summary>
public sealed partial class SessionSpellCaster
{
    /// <summary>The object field numbers of the spell flags (20, 0x14) and the critter flags (218, 0xDA).</summary>
    public const int SpellFlagsField = SpellComponentTables.SpellFlagsField, CritterFlagsField = 218;

    /// <summary>The hold flags (GT145): entangled and stoned among the spell flags, paralysed among the critter flags.</summary>
    public const int EntangledSpellFlag = 0x1000000, StonedSpellFlag = 0x10000, ParalysedCritterFlag = 0x40;

    // The ObjFlag component on a critter for a hold flag: true when it was taken (written, or left as the reference count says); false when it is not ready (another bit or field,
    // a target that is no critter), which the caller logs.
    private bool ObjectFlag(SpellRun run, SessionSpellComponent component, SpellRunTarget target)
    {
        if (component.ObjectFlag is not { } data || target.Object is not { } obj) return false;
        var mask = unchecked((int)data.Flag);
        SessionObjectFlagField? field = data.Field == SpellFlagsField && mask is EntangledSpellFlag or StonedSpellFlag ? SessionObjectFlagField.Spell
            : data.Field == CritterFlagsField && mask == ParalysedCritterFlag ? SessionObjectFlagField.Critter : null;
        if (field is not { } which) return false;
        var id = obj.Id;
        // A set needs the critter here; a clear does not (the change is kept by the critter's identity, and a run's END may come after its critter left the map).
        if (id == Hero ? _owner.QueryHero() is null : data.On && _dataMap.Critter(id) is null) return false;
        if (id != Hero && !id.StartsWith("oid:", StringComparison.Ordinal)) return false;
        var count = ProvidingRuns(id, which, mask, data.On);
        // 0x455100 first: setting a bit another active run already provides does nothing; clearing keeps a bit more than one run provides.
        if (data.On ? count > 0 : count > 1)
        {
            _log.Add(new(SpellCastEventKind.ObjectFlagKept, run.Id, mask, id, FlagDetail(which, data.On)));
            return true;
        }
        var written = _owner.WriteObjectFlag(_owner.Version, id, which, mask, data.On, DataHas(id, which, mask));
        _log.Add(written.Disposition == GameSessionDisposition.Rejected
            ? new(SpellCastEventKind.ObjectFlagNotSaved, run.Id, (int)written.Failure, id, FlagDetail(which, data.On))
            : new(SpellCastEventKind.ObjectFlagWritten, run.Id, mask, id, FlagDetail(which, data.On)));
        // GT146: setting entangled or paralysed interrupts, at level 6, the critter's runs whose bottom or current goal is 3 or 61 (0x44E4D0); the host does it. Stone interrupts nothing.
        if (data.On && written.Disposition != GameSessionDisposition.Rejected && mask is EntangledSpellFlag or ParalysedCritterFlag)
            _log.Add(new(SpellCastEventKind.HoldStarted, run.Id, mask, id, FlagDetail(which, true)));
        return true;
    }

    private static int FlagDetail(SessionObjectFlagField field, bool on) => (field == SessionObjectFlagField.Critter ? 2 : 0) | (on ? 1 : 0);

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
        if (_dataMap.Critter(id) is not { } critter) return null;
        return ((field == SessionObjectFlagField.Spell ? critter.SpellFlags : critter.CritterFlags) & mask) != 0;
    }

    // A critter's flag field now: its data's value with the session's writes.
    private int CurrentFlags(string id, SessionObjectFlagField field)
    {
        var written = _owner.QueryObjectFlags(id);
        if (id == Hero) return field == SessionObjectFlagField.Spell ? written.Spell(0) : written.Critter(0);
        var critter = _dataMap.Critter(id);
        return field == SessionObjectFlagField.Spell ? written.Spell(critter?.SpellFlags ?? 0) : written.Critter(critter?.CritterFlags ?? 0);
    }

    /// <summary>ARC-DEV-302: whether a critter is held now (the session's writes over its data): entangled, stoned or paralysed.</summary>
    public (bool Entangled, bool Stoned, bool Paralysed) Holds(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var spell = CurrentFlags(id, SessionObjectFlagField.Spell);
        return ((spell & EntangledSpellFlag) != 0, (spell & StonedSpellFlag) != 0, (CurrentFlags(id, SessionObjectFlagField.Critter) & ParalysedCritterFlag) != 0);
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
            return written.IsEmpty ? o : o with { SpellFlags = written.Spell(o.SpellFlags), CritterFlags = written.Critter(o.CritterFlags) };
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
            return written.IsEmpty ? critter : critter with { SpellFlags = written.Spell(critter.SpellFlags), CritterFlags = written.Critter(critter.CritterFlags) };
        }

        public CombatWeapon WieldedWeapon(string entity) => map.WieldedWeapon(entity);
        public IReadOnlyList<SpellAcidItem> WornItems(string entity) => map.WornItems(entity);
        public (Combatant Combatant, AttackTargetKind Kind)? ObjectCombat(string id) => map.ObjectCombat(id);
        public bool DamageObject(string id, AttackDamage damage) => map.DamageObject(id, damage);
    }
}
