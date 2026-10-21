using Arcanum.Data.Area;
using Arcanum.Data.Combat;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-MAGIC-014: the AREA map as a cast reads it (<see cref="ISpellCastMap"/>), for any map the viewer opens. Objects are the map's
/// rows on their world cells — an object with a session entity (an NPC, a container, an item: the inventory seed) under that id, any
/// other under <c>row:</c> kind and source index — and the hero (<see cref="SessionSpellCaster.Hero"/>) on its live cell. An NPC's facts
/// are its combat values (<see cref="AreaCombatHost"/>) and stored fields with its life in the session; the line of fire is the
/// original's (<see cref="LineOfFireRules.Trace"/>, ARC-COMBAT-012) over the map's live <see cref="AreaLineOfFire"/>.
/// Stand-ins, each documented in the contract: the sector kind is one for the whole map, a path exists to every admitted tile, NPCs stand on
/// their map cells, an NPC wields no weapon for a spell's context, and spell damage to objects other than critters is not ready.
/// </summary>
public sealed class AreaSpellCastMap : ISpellCastMap
{
    private const int ObjectFlagsSlot = 18, SpellFlagsSlot = 19, PcType = 15, NpcType = 16;
    private const int DestroyedOrOff = 0x3, InInventory = 0x1000;

    private readonly GameSessionOwner _session;
    private readonly int _map;
    private readonly AreaStepSnapshot _steps;
    private readonly AreaCombatHost _combat;
    private readonly Func<CellCoord?> _heroCell;
    private readonly Func<AreaLineOfFire?> _lineOfFire;
    private readonly Func<AreaObjectKey, bool> _gone;
    private readonly Dictionary<string, AreaObjectRecord> _rows = new(StringComparer.Ordinal);
    // The rows by their world cell, and each row's stored fields read once.
    private readonly Dictionary<(long X, long Y), List<string>> _cells = [];
    private readonly Dictionary<string, CombatFields?> _fields = new(StringComparer.Ordinal);
    private readonly List<string> _npcs = [];

    /// <param name="session">The session (life, companions, the hero).</param>
    /// <param name="runtimeMap">The runtime map the rows belong to.</param>
    /// <param name="steps">The map's step evidence (its admitted tiles are the map).</param>
    /// <param name="rows">The map's object rows.</param>
    /// <param name="combat">The NPCs' combat values of the map.</param>
    /// <param name="heroCell">The hero's cell now (null: not on the map).</param>
    /// <param name="lineOfFire">The map's line of fire now (null: none, every line is clear).</param>
    /// <param name="gone">Whether the session destroyed or switched off a row (none when null).</param>
    public AreaSpellCastMap(GameSessionOwner session, int runtimeMap, AreaStepSnapshot steps, IEnumerable<AreaObjectRecord> rows, AreaCombatHost combat,
        Func<CellCoord?> heroCell, Func<AreaLineOfFire?> lineOfFire, Func<AreaObjectKey, bool>? gone = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _map = runtimeMap;
        _steps = steps ?? throw new ArgumentNullException(nameof(steps));
        ArgumentNullException.ThrowIfNull(rows);
        _combat = combat ?? throw new ArgumentNullException(nameof(combat));
        _heroCell = heroCell ?? throw new ArgumentNullException(nameof(heroCell));
        _lineOfFire = lineOfFire ?? throw new ArgumentNullException(nameof(lineOfFire));
        _gone = gone ?? (_ => false);
        foreach (var row in rows)
        {
            // A carried item (object flag 0x1000) stands in no sector list, whatever location it stores.
            if (row.WorldX is not { } x || row.WorldY is not { } y || (Flags(row, ObjectFlagsSlot) & InInventory) != 0) continue;
            var id = Id(row);
            _rows[id] = row;
            if (!_cells.TryGetValue((x, y), out var list)) _cells[(x, y)] = list = [];
            list.Add(id);
            if (row.Type == NpcType) _npcs.Add(id);
        }
    }

    /// <summary>The cast's id of a map row: its session entity, else <c>row:</c> kind and source index.</summary>
    public string Id(AreaObjectRecord row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return _combat.Entity(_map, row.Key) ?? $"row:{row.Key.Kind}:{row.Key.SourceIndex}";
    }

    /// <summary>The map location of a cell.</summary>
    public static SpellLocation Location(CellCoord cell) => new(checked((uint)cell.X), checked((uint)cell.Y));

    // ---- ISpellTargetWorld ----

    public SpellTargetObject? Object(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (id == SessionSpellCaster.Hero) return HeroObject();
        return _rows.TryGetValue(id, out var row) ? RowObject(id, row) : null;
    }

    public IReadOnlyList<SpellTargetObject> ObjectsInSquare(SpellLocation centre, int radius, int typeMask)
    {
        var found = new List<SpellTargetObject>();
        if (radius < 0) return found;
        if ((typeMask & (1 << PcType)) != 0 && HeroObject() is { } hero
            && Math.Max(Math.Abs((long)hero.Location.X - centre.X), Math.Abs((long)hero.Location.Y - centre.Y)) <= radius) found.Add(hero);
        for (long y = (long)centre.Y - radius; y <= (long)centre.Y + radius; y++)
            for (long x = (long)centre.X - radius; x <= (long)centre.X + radius; x++)
                Add(found, x, y, typeMask);
        return found;
    }

    public IReadOnlyList<SpellTargetObject> ObjectsAt(SpellLocation tile, int typeMask)
    {
        var found = new List<SpellTargetObject>();
        if ((typeMask & (1 << PcType)) != 0 && HeroObject() is { } hero && hero.Location == tile) found.Add(hero);
        Add(found, tile.X, tile.Y, typeMask);
        return found;
    }

    private void Add(List<SpellTargetObject> found, long x, long y, int typeMask)
    {
        if (!_cells.TryGetValue((x, y), out var ids)) return;
        foreach (var id in ids)
            if (_rows[id].Type is >= 0 and < 32 && (typeMask & (1 << _rows[id].Type)) != 0) found.Add(RowObject(id, _rows[id]));
    }

    public bool TileBlocked(SpellLocation tile) => !_steps.IsAdmitted(new(tile.X, tile.Y));

    public bool PathExists(SpellTargetObject source, SpellLocation tile) => !TileBlocked(tile);

    public int SectorKind(SpellLocation location) => 0;

    /// <summary>The original's direction of a tile offset (0x4B8D50): the octant of the line, a 2:1 slope rule deciding between a
    /// straight and a diagonal step; no offset answers 5.</summary>
    public static int Direction(long dx, long dy)
    {
        int sx = Math.Sign(dx), sy = Math.Sign(dy);
        if (Math.Abs(dx) > 2 * Math.Abs(dy)) sy = 0;
        if (Math.Abs(dy) > 2 * Math.Abs(dx)) sx = 0;
        return (sx, sy) switch
        {
            (-1, -1) => 0, (-1, 0) => 1, (-1, 1) => 2, (0, 1) => 3, (1, 1) => 4, (1, 0) => 5, (1, -1) => 6, (0, -1) => 7, _ => 5,
        };
    }

    public int Direction(SpellLocation from, SpellLocation to) => Direction((long)to.X - from.X, (long)to.Y - from.Y);

    public IReadOnlyList<SpellTargetObject> Party(SpellTargetObject source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Id != SessionSpellCaster.Hero) return [];
        return _npcs.Where(id => _session.QueryCompanion(id).LedByPc).Select(id => RowObject(id, _rows[id])).ToArray();
    }

    // ---- ISpellCastMap ----

    public SpellLineOfFire LineOfFire(SpellObjectFacts parent, SpellLocation tile)
    {
        ArgumentNullException.ThrowIfNull(parent);
        if (_lineOfFire() is not { } map) return new(0, null);
        var trace = LineOfFireRules.Trace(new(checked((int)parent.Location.X), checked((int)parent.Location.Y)), 0, 0,
            new(checked((int)tile.X), checked((int)tile.Y)), map, _session.Rules.Combat);
        var blocker = trace.Blocker is { } row && row.WorldX is { } x && row.WorldY is { } y
            ? new SpellObjectFacts(Id(row), row.Type, new(checked((uint)x), checked((uint)y)), Flags(row, ObjectFlagsSlot), Flags(row, SpellFlagsSlot))
            : null;
        return new(trace.LineFailed ? LineOfFireRules.FailedLineCost : trace.Cost, blocker);
    }

    public SpellCastCritter? Critter(string entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (entity == SessionSpellCaster.Hero || !_rows.TryGetValue(entity, out var row) || row.Type != NpcType) return null;
        if (_combat.Build(_map, entity).Combat is not { } combat || Fields(entity, row) is not { } fields) return null;
        var resistances = fields.Object.Resistances;
        return new(combat, resistances.Count > 4 ? resistances[4] : 0, unchecked((int)fields.Object.Flags), unchecked((int)fields.Object.SpellFlags),
            unchecked((int)(fields.Critter?.Flags ?? 0)), unchecked((int)(fields.Critter?.Flags2 ?? 0)));
    }

    public CombatWeapon WieldedWeapon(string entity) => CombatWeapon.Unarmed();

    public IReadOnlyList<SpellAcidItem> WornItems(string entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        // The hero's kit items carry no stored fields here (their wear facts are the viewer's), so the hero answers none while it
        // wears any; an NPC's items answer their stored acid resistance.
        var worn = entity == SessionSpellCaster.Hero ? AreaCombatHost.HeroWorn(_session, null) : _combat.WornItems(_session, _map, entity);
        if (worn is null) return [];
        return worn.OrderBy(p => (int)p.Key).Select(p => new SpellAcidItem(p.Value.Entity, (int)p.Key, false,
            _combat.Fields(_map, p.Value.Entity) is { } fields && fields.Object.Resistances.Count > 0 ? Math.Clamp(fields.Object.Resistances[0], 0, 95) : 0))
            .ToArray();
    }

    public (Combatant Combatant, AttackTargetKind Kind)? ObjectCombat(string id) => null;

    public bool DamageObject(string id, AttackDamage damage) => false;

    // ---- facts ----

    private CombatFields? Fields(string id, AreaObjectRecord row)
    {
        if (!_fields.TryGetValue(id, out var fields)) _fields[id] = fields = CombatFieldReader.Read(row).Fields;
        return fields;
    }

    private SpellTargetObject? HeroObject()
    {
        if (_heroCell() is not { } cell || _session.QueryHeroLife() is not { } life) return null;
        var state = life.Record.State;
        return new(SessionSpellCaster.Hero, PcType, Location(cell), HitPoints: life.Status.HitPoints, HitPointDamage: state.HitPointDamage,
            Alignment: _session.QueryHero()?.Alignment ?? 0, Poison: state.PoisonLevel);
    }

    private SpellTargetObject RowObject(string id, AreaObjectRecord row)
    {
        var location = new SpellLocation(checked((uint)row.WorldX!.Value), checked((uint)row.WorldY!.Value));
        var objectFlags = Flags(row, ObjectFlagsSlot) | (_gone(row.Key) ? DestroyedOrOff : 0);
        if (row.Type == NpcType && Critter(id) is { } npc && _session.QueryLife(npc.Combat.Target).View is { } life)
            return new(id, NpcType, location, objectFlags, npc.SpellFlags, npc.CritterFlags, npc.CritterFlags2, life.Status.HitPoints,
                life.Record.State.HitPointDamage, Poison: life.Record.State.PoisonLevel,
                PcLeader: _session.QueryCompanion(id).LedByPc ? SessionSpellCaster.Hero : null);
        return new(id, row.Type, location, objectFlags, Flags(row, SpellFlagsSlot), HitPoints: 1);
    }

    private static int Flags(AreaObjectRecord row, int slot) =>
        unchecked((int)(row.EffectiveFields.FirstOrDefault(f => f.Slot == slot && f.Presence == AreaPresence.Value)?.Number ?? 0));
}
