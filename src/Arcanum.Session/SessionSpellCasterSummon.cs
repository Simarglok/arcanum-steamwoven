using Arcanum.Simulation;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;

namespace Arcanum.Session;

/// <summary>
/// ARC-DEV-352 (E1-b): what the cast host needs of an object prototype to summon it and to answer for the object it made: the object type (3 a scenery), the object flags (<c>OF_NO_BLOCK</c> 0x400,
/// <c>OF_CLICK_THROUGH</c>, <c>OF_INVULNERABLE</c> and the rest, as the prototype file holds them), the scenery flags (<c>OSCF_IS_FIRE</c> 0x10) and the hit points. The session reads no game data:
/// the host reads them from the module's prototype file (<see cref="SessionSpellCaster.SummonPrototypes"/>).
/// </summary>
public sealed record SessionSummonPrototype(int Type, int ObjectFlags, int SceneryFlags = 0, int HitPoints = 1);

/// <summary>
/// ARC-DEV-352 (E1-b of the missing-systems plan; ARC-RES-101, ledger SUM1–SUM16): the Summon component (type 18, ProcFunc 0x452830, effect 0x450C40) for the objects it makes that are not
/// critters, and the eye-candy callbacks the summoning records wait for (EC11, EC14). Fireflash (spell 22) makes the flash (basic prototype 4050) on an empty strike tile and its CALLBACK line destroys it
/// after the eye candy's action frame; Wall of Fire (spell 21) makes five pieces (basic prototype 4031) along the wall line and its END_CALLBACK line destroys all of them. The run keeps its summoned list as
/// the created objects that name it (<see cref="SessionCreatedObject.Run"/>, newest first, SUM7); a freed run's slot lets go of the list (<see cref="GameSessionOwner"/>'s commit). <b>Not ready:</b> the
/// critter half (a prototype of another type and the list pick, E1-e), a dispel of a summoned object, the network paths.
/// </summary>
public sealed partial class SessionSpellCaster
{
    /// <summary>SC7: the <c>Proto:</c> number is a basic prototype number; the object is made from the prototype file at the number + 20.</summary>
    public const int PrototypeFileOffset = 20;

    /// <summary>The object type of a scenery.</summary>
    public const int SceneryObjectType = SpellTargetObject.Scenery;

    /// <summary>The play flags of an EyeCandy component that call the run back (EC11): <c>callback</c> (0x8) and <c>end_callback</c> (0x10).</summary>
    public const uint PlayCallback = 0x8, PlayEndCallback = 0x10;

    /// <summary>ARC-DEV-352: the facts of a prototype by its file number (null: the prototype does not read). Null: nothing is summoned and created objects are no map objects of this host.</summary>
    public Func<int, SessionSummonPrototype?>? SummonPrototypes { get; set; }

    /// <summary>ARC-DEV-352: the map this host casts on, where a summon creates its object and whose created objects are map objects here (null: none).</summary>
    public SessionMapId? SummonMap { get; set; }

    /// <summary>
    /// ARC-DEV-352 (EC9, EC11, EC14): whether the eye-candy entry of an index plays as a goal of its own (an animating entry with art and a foreground or underlay part, 0x4CD49D): played with a callback flag
    /// the run waits for the animation, which calls it back (<see cref="EyeCandyCallback"/>); not played the callback runs at once (the END callback for <c>end_callback</c>). Null: the callbacks are not
    /// ready (the run does not wait and no callback runs), as before this slice.
    /// </summary>
    public Func<int, bool>? EyeCandyPlays { get; set; }

    // SUM1: the context's "summoned" slot, which the records' Tgt_Summoned lines re-aim to; reset once per action, not per entry (a refused entry re-aims to the previous entry's object).
    private string? _lastSummoned;

    /// <summary>
    /// ARC-DEV-352 (SUM1–SUM4, SUM9): the Summon component on one list entry. Only a tile entry is summoned on (an object entry has no tile, TA9); the component's own area test (the gate) has already
    /// refused a tile that is not empty (<c>Tgt_Tile_Empty</c>: Fireflash) or holds a blocking immobile (<c>Tgt_Tile_Empty_Immobiles</c>: Wall of Fire). The object is made from the prototype file
    /// <c>Proto:</c> + 20 at the entry's tile with no offset, marked summoned (0x4000) with the run's parent as its summoner and the run as its owner, and becomes the last summoned. A component whose
    /// prototype is not a scenery (a critter) or that picks from a list is not ready (false). A creation the session refuses is logged (<see cref="SpellCastEventKind.SummonRefused"/>; the original
    /// exits, an unreachable bug, SUM2).
    /// </summary>
    [OriginalFunction(0x452830, OriginalFunctionScope.Partial)]
    private bool SummonComponent(SpellRun run, SessionSpellComponent component, SpellRunTarget target)
    {
        if (component.Summon is not { } data) return false;
        if (target.Object is not null || target.Tile.IsNone) return true;
        if (data.Prototype <= 0 || data.Prototype > SessionCreatedObject.MaxPrototype - PrototypeFileOffset || SummonMap is not { } map) return false;
        var file = data.Prototype + PrototypeFileOffset;
        if (SummonPrototypes?.Invoke(file) is not { Type: SceneryObjectType }) return false;
        if (target.Tile.X > int.MaxValue || target.Tile.Y > int.MaxValue) { _log.Add(new(SpellCastEventKind.SummonRefused, run.Id, file, null, (int)GameSessionFailure.InvalidCreatedObject)); return true; }
        var made = _owner.CreateObject(_owner.Version, SessionCreatedKind.Scenery, file, map, new CellCoord((int)target.Tile.X, (int)target.Tile.Y), 0, 0, SessionCreatedObject.SummonedFlag,
            run.Parent?.Id, run.Id);
        if (made.Object is not { } summoned) { _log.Add(new(SpellCastEventKind.SummonRefused, run.Id, file, null, (int)made.Failure)); return true; }
        _lastSummoned = summoned.Entity;
        _log.Add(new(SpellCastEventKind.Summoned, run.Id, file, summoned.Entity));
        return true;
    }

    // SUM7 / TA10: the run's summoned list, newest first (head insertion), the objects of this host's map that are still there.
    private IReadOnlyList<SpellTargetObject> SummonedOf(SpellRun run)
    {
        if (SummonMap is not { } map) return [];
        var made = _owner.QueryCreatedObjects(map);
        var list = new List<SpellTargetObject>();
        for (var i = made.Count - 1; i >= 0; i--)
            if (made[i].Run == run.Id && _map.Object(made[i].Entity) is { } summoned) list.Add(summoned);
        return list;
    }

    private SpellTargetObject? LastSummoned => _lastSummoned is { } entity ? _map.Object(entity) : null;

    /// <summary>
    /// ARC-DEV-352 (DS2, SUM8): a Destroy component on a summoned object this session created: the object goes (one revision) and the runs hear of it after the outer action (0x459740). False when the
    /// session refuses (the caller logs the component as not done).
    /// </summary>
    private bool DestroyCreated(SpellRun run, string entity)
    {
        var destroyed = _owner.DestroyCreatedObject(_owner.Version, entity);
        if (destroyed.Disposition == GameSessionDisposition.NoChange) return true;
        if (destroyed.Disposition != GameSessionDisposition.Changed) return false;
        _goneObjects.Add((SessionSpellRunActionKind.ObjectGone, entity, run.Id));
        _log.Add(new(SpellCastEventKind.SummonDestroyed, run.Id, destroyed.Object?.Prototype ?? 0, entity));
        return true;
    }

    // EC11: an Add played with a callback flag makes the run wait for the animation; one that does not play calls back at once (the END callback for end_callback alone). Without the host's answer
    // the callbacks are not ready.
    private SpellComponentOutcome EyeCandyOutcome(int index, uint play)
    {
        if ((play & (PlayCallback | PlayEndCallback)) == 0 || EyeCandyPlays is not { } plays) return SpellComponentOutcome.None;
        return plays(index) ? new(CallbackPending: true) : new(CallbackNow: true, EndCallback: (play & PlayCallback) == 0);
    }

    /// <summary>
    /// ARC-DEV-352 (EC11, EC14): the animation of an eye candy played with a callback flag reached its callback: <paramref name="phase"/> 3 is the action frame of goals 47/48 (the run's CALLBACK action),
    /// 4 the end of goals 58/59 (its END_CALLBACK action). Also the host's answer for an add it could not play after all (no free place, a duplicate, a full goal table): the callback runs at once.
    /// The run <paramref name="run"/> is acted on only while it waits for that action (its record has components there and it has not ended); otherwise nothing (a reused slot, a run already gone).
    /// </summary>
    public SessionSpellRunResult? EyeCandyCallback(int run, int phase)
    {
        if (phase is not (3 or 4) || _owner.QuerySpellRuns()[run] is not { } live) return null;
        var action = phase == 3 ? SpellRunAction.Callback : SpellRunAction.EndCallback;
        if (_records(live.Spell) is not { } record || !record.Of(action).Any()) return null;
        if (phase == 3 ? live.Action is not (SpellRunAction.Begin or SpellRunAction.Maintain) : live.Action != SpellRunAction.End) return null;
        return Act(new(phase == 3 ? SessionSpellRunActionKind.Callback : SessionSpellRunActionKind.EndCallback, run));
    }

    /// <summary>
    /// ARC-DEV-352 (GT94, SUM4): the burn's bonus for a source (a summoned fire's summoner): its stat 16 (magick/tech aptitude, F1-low) × 5 / 100 − 1 when that stat is positive, else 0.
    /// </summary>
    public int BurnBonus(string? source)
    {
        if (source is null || RunObject(source) is not { } who || Stat(who, 16) is not { } stat || stat <= 0) return 0;
        return stat * 5 / 100 - 1;
    }

    // GT94: a burn with a source spares the source and an NPC whose leader chain reaches a PC source (0x45DDA0).
    private bool BurnSpares(string victim, string? source) =>
        source is not null && (victim == source || source == Hero && _map.Object(victim)?.PcLeader == source);

    /// <summary>
    /// ARC-DEV-352 (E1-b): the created objects of the summon map as objects of the map the run rules ask: their type, flags and hit points from their prototype, the spell flags they were made with,
    /// at their tile; listed before the map's own objects on a tile (they entered their sector last). The rest of the map is <paramref name="map"/>'s.
    /// </summary>
    private sealed class CreatedCastMap(ISpellCastMap map, SessionSpellCaster host) : ISpellCastMap
    {
        private SpellTargetObject? Of(SessionCreatedObject made) =>
            made.Kind == SessionCreatedKind.Scenery && host.SummonPrototypes?.Invoke(made.Prototype) is { } facts
                ? new(made.Entity, facts.Type, new((uint)made.Cell.X, (uint)made.Cell.Y), facts.ObjectFlags, unchecked((int)made.SpellFlags), HitPoints: facts.HitPoints)
                : null;

        private IEnumerable<SpellTargetObject> Here(Func<SpellTargetObject, bool> where, int typeMask)
        {
            if (host.SummonPrototypes is null || host.SummonMap is not { } shown) yield break;
            var made = host._owner.QueryCreatedObjects(shown);
            for (var i = made.Count - 1; i >= 0; i--)
                if (Of(made[i]) is { } o && o.Type is >= 0 and < 32 && (typeMask & (1 << o.Type)) != 0 && where(o)) yield return o;
        }

        private static IReadOnlyList<SpellTargetObject> Join(IEnumerable<SpellTargetObject> created, IReadOnlyList<SpellTargetObject> rest)
        {
            var list = created.ToList();
            if (list.Count == 0) return rest;
            list.AddRange(rest);
            return list;
        }

        public SpellTargetObject? Object(string id)
        {
            if (SessionCreatedObject.KindOf(id) is null) return map.Object(id);
            return host.SummonMap is { } shown && host._owner.QueryCreatedObject(id) is { } made && made.Map == shown ? Of(made) : null;
        }

        public IReadOnlyList<SpellTargetObject> ObjectsInSquare(SpellLocation centre, int radius, int typeMask) =>
            radius < 0 ? map.ObjectsInSquare(centre, radius, typeMask)
            : Join(Here(o => Math.Max(Math.Abs((long)o.Location.X - centre.X), Math.Abs((long)o.Location.Y - centre.Y)) <= radius, typeMask), map.ObjectsInSquare(centre, radius, typeMask));

        public IReadOnlyList<SpellTargetObject> ObjectsAt(SpellLocation tile, int typeMask) => Join(Here(o => o.Location == tile, typeMask), map.ObjectsAt(tile, typeMask));
        public bool TileBlocked(SpellLocation tile) => map.TileBlocked(tile);
        public bool PathExists(SpellTargetObject source, SpellLocation tile) => map.PathExists(source, tile);
        public int SectorKind(SpellLocation location) => map.SectorKind(location);
        public int Direction(SpellLocation from, SpellLocation to) => map.Direction(from, to);
        public IReadOnlyList<SpellTargetObject> Party(SpellTargetObject source) => map.Party(source);
        public SpellLineOfFire LineOfFire(SpellObjectFacts parent, SpellLocation tile) => map.LineOfFire(parent, tile);
        public SpellCastCritter? Critter(string entity) => map.Critter(entity);
        public CombatWeapon WieldedWeapon(string entity) => map.WieldedWeapon(entity);
        public IReadOnlyList<SpellAcidItem> WornItems(string entity) => map.WornItems(entity);
        public (Combatant Combatant, AttackTargetKind Kind)? ObjectCombat(string id) => SessionCreatedObject.KindOf(id) is null ? map.ObjectCombat(id) : null;
        public bool DamageObject(string id, AttackDamage damage) => SessionCreatedObject.KindOf(id) is null && map.DamageObject(id, damage);
    }
}
