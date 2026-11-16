using Arcanum.Data.Area;
using Arcanum.Data.Combat;
using Arcanum.Session;
using Arcanum.Simulation;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Locks;
using Arcanum.Simulation.Movement;

namespace Arcanum.World;

/// <summary>
/// An object of the shown map the hero can attack (ARC-COMBAT-008): its entity, its row, and either the portal, container, scenery or
/// trap with its damage baseline (<see cref="Object"/>) or the item on the ground with its durability values and facts
/// (<see cref="Item"/>, <see cref="ItemFacts"/>); <see cref="Refusal"/> says why its fields give no baseline.
/// </summary>
public sealed record AreaObjectAttackTarget(string Entity, AreaObjectRecord Row, DamageableObjectKind Kind, SessionObjectTarget? Object,
    SessionWearItem? Item, SessionObjectBaseline? ItemFacts, string? Refusal)
{
    /// <summary>The session's attack on it from <paramref name="distance"/> cells with the hero's worn items.</summary>
    public SessionObjectAttack Attack(int distance, HitLocation? aim, IReadOnlyDictionary<EquipmentSlot, SessionWearItem>? heroWorn) =>
        new() { Target = Object, Item = Item, ItemFacts = ItemFacts, Distance = distance, Aim = aim, HeroWorn = heroWorn };
}

/// <summary>
/// ARC-COMBAT-008: the objects of one shown map an attack reaches besides NPCs: its drawn doors and windows (the map's
/// <see cref="AreaPortals"/>), containers, scenery and traps (their stored fields through <see cref="CombatFieldReader.ReadBreakable"/>
/// and <see cref="SessionObjectFacts"/>), and the items lying on its ground (<see cref="SessionWearItem"/>,
/// <see cref="SessionObjectFacts.ItemFacts"/>). A container's entity is its inventory holder's, so its spill reaches the
/// holder's items; a portal's is the lock domain's; any other static object's is <see cref="AreaUseScriptHost.EntityId"/>. Whether
/// an object's use script is a trap is the caller's (<see cref="AreaUseScriptHost.IsTrap"/>, ARC-COMBAT-011). It also names the objects the session destroyed or
/// switched off, which the view no longer draws, and since ARC-WORLD-114 the NPCs a script switched off (<see cref="AreaUseScriptHost.OffProperty"/>),
/// since ARC-SCRIPT-052 also those a script destroyed (<see cref="AreaUseScriptHost.DestroyedProperty"/>).
/// </summary>
public sealed class AreaObjectTargets
{
    private const int TypeFlagsSlot = 36, KeySlot = 38, ObjectFlagsSlot = 18;
    private readonly SessionMapId _map;
    private readonly IReadOnlyDictionary<AreaObjectKey, (string Entity, SessionOpenableBaseline Baseline)> _portals;
    private readonly AreaInventoryMap? _inventory;
    private readonly Dictionary<AreaObjectKey, (AreaObjectRecord Row, string Entity, DamageableObjectKind Kind)> _objects = [];
    // ARC-WORLD-114: the NPC rows by key, with their session entity, which a script can switch off.
    // ARC-SCRIPT-1360: with the map data's object flags, since a toggle state switches an NPC the data stores off on.
    private readonly Dictionary<AreaObjectKey, (string Entity, long Flags, CellCoord Cell)> _npcs = [];

    /// <summary>The targets of a shown map from its snapshot, its drawn portals and its inventory map (null without a seed).</summary>
    public static AreaObjectTargets From(AreaSnapshot snapshot, AreaPortals portals, AreaInventoryMap? inventory)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(portals);
        return new(snapshot.RuntimeMap, snapshot.Objects, portals.All.ToDictionary(p => p.Key, p => (p.Entity, p.Baseline)), inventory);
    }

    /// <summary>The targets of a map's rows: <paramref name="portals"/> names each drawn portal's lock entity and baseline (a portal
    /// not listed is not attackable); a map without a runtime number has none.</summary>
    public AreaObjectTargets(int runtimeMap, IEnumerable<AreaObjectRecord> rows,
        IReadOnlyDictionary<AreaObjectKey, (string Entity, SessionOpenableBaseline Baseline)> portals, AreaInventoryMap? inventory)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(portals);
        _map = new(runtimeMap);
        _portals = portals;
        _inventory = inventory;
        if (runtimeMap < 1) return;
        foreach (var row in rows)
        {
            if (row.Type == CombatFieldReader.NpcType && row.WorldX is not null && row.WorldY is not null) _npcs.TryAdd(row.Key, (AreaUseScriptHost.EntityId(row), row.EffectiveFields.FirstOrDefault(f => f.Slot == ObjectFlagsSlot) is { Presence: AreaPresence.Value, Number: { } flags } ? flags : 0,
                new(checked((int)row.WorldX.Value), checked((int)row.WorldY.Value))));
            DamageableObjectKind? kind = row.Type switch
            {
                CombatFieldReader.PortalType => DamageableObjectKind.Portal,
                CombatFieldReader.ContainerType => DamageableObjectKind.Container,
                CombatFieldReader.SceneryType => DamageableObjectKind.Scenery,
                CombatFieldReader.TrapType => DamageableObjectKind.Trap,
                _ => null,
            };
            if (kind is not { } k || row.WorldX is null || row.WorldY is null) continue;
            string entity;
            if (k == DamageableObjectKind.Portal)
            {
                // Only the drawn portals the lock domain knows.
                if (!portals.TryGetValue(row.Key, out var portal)) continue;
                entity = portal.Entity;
            }
            else entity = k == DamageableObjectKind.Container && inventory?.TryGetEntity(row.Key, out var holder) == true ? holder : AreaUseScriptHost.EntityId(row);
            _objects[row.Key] = (row, entity, k);
        }
    }

    public int Count => _objects.Count;

    /// <summary>ARC-DEV-255: the keys of the shown map's attackable portals, containers, scenery and traps, in row order (a smoke picks one).</summary>
    public IReadOnlyCollection<AreaObjectKey> Keys => _objects.Keys;

    /// <summary>ARC-DEV-255: the shown map's attackable object (a portal, container, scenery or trap) named by its session entity, the id
    /// a script's object handle and a cast hold, or null for any other entity (an NPC, an item, a wall).</summary>
    public AreaObjectKey? KeyOfEntity(string entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        foreach (var pair in _objects)
            if (pair.Value.Entity == entity) return pair.Key;
        return null;
    }

    /// <summary>ARC-DEV-261: the session entity (the id a script's object handle holds) of the shown map's attackable object <paramref name="key"/>, or null for any other row.</summary>
    public string? EntityOfKey(AreaObjectKey key) => _objects.TryGetValue(key, out var found) ? found.Entity : null;

    /// <summary>The attackable object drawn as <paramref name="key"/>, or null for anything else (an NPC, a wall, an object the session
    /// destroyed or switched off, an item no longer on the ground). <paramref name="trapped"/> says whether a row's use script is a
    /// trap (ARC-COMBAT-011); without it none is.</summary>
    public AreaObjectAttackTarget? Target(GameSessionOwner session, AreaObjectKey key, Func<AreaObjectRecord, bool>? trapped = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (_objects.TryGetValue(key, out var found))
        {
            if (session.QueryObjectCondition(_map, found.Entity, found.Kind) is SessionObjectCondition.Destroyed or SessionObjectCondition.Off) return null;
            // ARC-SCRIPT-2386 (IV13, screen pick 0x43D9F0): a switched-off item (a script's toggle) is not a target.
            if (found.Kind == DamageableObjectKind.Item && session.QueryItemOff(_map, found.Entity, dataOff: false)) return null;
            var read = CombatFieldReader.ReadBreakable(found.Row);
            if (read.Fields is not { } fields) return new(found.Entity, found.Row, found.Kind, null, null, null, $"{read.Failure} {read.Detail}");
            var cell = new CellCoord(checked((int)found.Row.WorldX!.Value), checked((int)found.Row.WorldY!.Value));
            SessionOpenableBaseline? openable = found.Kind switch
            {
                DamageableObjectKind.Portal when _portals.TryGetValue(key, out var portal) => portal.Baseline,
                DamageableObjectKind.Container => ContainerBaseline(found.Row, fields),
                _ => null,
            };
            return SessionObjectFacts.FromCombatFields(fields, cell, openable, trapped?.Invoke(found.Row) == true) is { } baseline
                ? new(found.Entity, found.Row, found.Kind, new(_map, found.Entity, baseline), null, null, null)
                : new(found.Entity, found.Row, found.Kind, null, null, null, "values outside the damage rules' bounds");
        }
        // An item on the ground (the seed's, not taken or deleted).
        if (_inventory is null || !_inventory.TryGetEntity(key, out var item) || !_inventory.TryGetRow(item, out var row)) return null;
        if (session.QueryMapInventory(_map)?.Ground.FirstOrDefault(g => g.Item.Entity == item) is not { } lying) return null;
        // ARC-SCRIPT-2386 (IV15, screen pick 0x43D9F0): a switched-off item (a script's toggle, a thrown item in flight) is not a target; ARC-SCRIPT-2397 (IV18): nor is one whose map data is off.
        if (session.QueryItemOff(_map, item, lying.Item.Facts.OffInData)) return null;
        if (CombatFieldReader.Read(row).Fields is not { } itemFields) return new(item, row, DamageableObjectKind.Item, null, null, null, "item fields do not read");
        return SessionWearItem.FromCombatFields(item, itemFields) is { } wear && SessionObjectFacts.ItemFacts(itemFields, trapped?.Invoke(row) == true) is { } facts
            ? new(item, row, DamageableObjectKind.Item, null, wear, facts, null)
            : new(item, row, DamageableObjectKind.Item, null, null, null, "values outside the durability bounds");
    }

    private (GameSessionVersion Version, IReadOnlySet<AreaObjectKey> Keys)? _gone;

    /// <summary>The objects the session destroyed or switched off (since ARC-WORLD-114 also the NPCs a script switched off, since ARC-SCRIPT-052
    /// those a script destroyed): the view no
    /// longer draws them, and since ARC-WORLD-065 they no longer block a step (the live overlay moves their T0 records). The same set is returned while the session's version is
    /// unchanged.</summary>
    public IReadOnlySet<AreaObjectKey> Gone(GameSessionOwner session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var version = session.Version;
        if (_gone is { } known && known.Version == version) return known.Keys;
        var keys = _objects.Where(pair => session.QueryObjectCondition(_map, pair.Value.Entity, pair.Value.Kind) is SessionObjectCondition.Destroyed
            or SessionObjectCondition.Off).Select(pair => pair.Key)
            .Concat(_npcs.Where(pair => AreaUseScriptHost.NpcOff(session, _map.RuntimeMap, pair.Value.Entity, pair.Value.Flags)
                || AreaUseScriptHost.Destroyed(session, _map.RuntimeMap, pair.Value.Entity)).Select(pair => pair.Key)).ToHashSet();
        // An unchanged set keeps its reference, so overlays cached on it stay valid across unrelated revisions.
        IReadOnlySet<AreaObjectKey> result = _gone is { } previous && previous.Keys.SetEquals(keys) ? previous.Keys : keys;
        _gone = (version, result);
        return result;
    }

    private (GameSessionVersion Version, IReadOnlyList<(AreaObjectKey Key, string Entity, CellCoord World)> Npcs)? _switchedOn;

    /// <summary>
    /// ARC-WORLD-1436: the NPCs the map data stores off (object flag 0x2) that the session has switched on (the quest NPCs a heartbeat switches on, ARC-SCRIPT-1360) and that are alive and
    /// not destroyed, with the world cell of their map data. The T0 step evidence leaves a data-off record out of its blockers, so such an NPC blocks no step until the live overlay holds
    /// its cell (<see cref="AreaOccupancy.WithHeld"/>); an NPC the AI host walked off its cell is the host's to hold. The same list is returned while the session's version is unchanged.
    /// </summary>
    public IReadOnlyList<(AreaObjectKey Key, string Entity, CellCoord World)> SwitchedOn(GameSessionOwner session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var version = session.Version;
        if (_switchedOn is { } known && known.Version == version) return known.Npcs;
        var list = _npcs.Where(pair => (pair.Value.Flags & AreaUseScriptHost.OffObjectFlagBit) != 0
                && !AreaUseScriptHost.NpcOff(session, _map.RuntimeMap, pair.Value.Entity, pair.Value.Flags)
                && !AreaUseScriptHost.Destroyed(session, _map.RuntimeMap, pair.Value.Entity) && !session.QueryNpcKilled(pair.Value.Entity))
            .Select(pair => (pair.Key, pair.Value.Entity, pair.Value.Cell)).ToArray();
        IReadOnlyList<(AreaObjectKey Key, string Entity, CellCoord World)> result = _switchedOn is { } previous && previous.Npcs.SequenceEqual(list) ? previous.Npcs : list;
        _switchedOn = (version, result);
        return result;
    }

    /// <summary>
    /// ARC-AI-1636 (0x43F630, the AI's busted test of a focus): whether the shown map's object <paramref name="key"/> is busted now. Not for a destroyed or switched-off object (the original's test answers 0 once object flag 0x1 is set, and
    /// the AI reads those as gone first). A portal or container reads its lock domain's busted bit 0x80 (a window the hits broke, or the map data's); a scenery or trap its busted condition (the stored flag 0x2 of its type flags
    /// at T0, or the break the session recorded). Any other row, a wall (answered 0 by the original: its validity is the wall rule's) or an object whose fields give no baseline is not busted.
    /// </summary>
    [OriginalFunction(0x43F630, OriginalFunctionScope.Partial)]
    public bool IsBusted(GameSessionOwner session, AreaObjectKey key, Func<AreaObjectRecord, bool>? trapped = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!_objects.TryGetValue(key, out var found) || Target(session, key, trapped) is not { Object: { } target }) return false;
        return found.Kind switch
        {
            DamageableObjectKind.Scenery or DamageableObjectKind.Trap => session.QueryObject(target).Condition == SessionObjectCondition.Busted,
            DamageableObjectKind.Portal or DamageableObjectKind.Container => target.Baseline.Openable is { } baseline
                && (session.QueryOpenable(new SessionOpenable(target.Map, target.Entity), baseline).Flags & LockRules.BustedBit) != 0,
            _ => false,
        };
    }

    /// <summary>ARC-RENDER-033: the scenery and traps the session busted (they take their destroyed ART).</summary>
    public IReadOnlyList<AreaObjectKey> Busted(GameSessionOwner session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return _objects.Where(pair => pair.Value.Kind is DamageableObjectKind.Scenery or DamageableObjectKind.Trap
            && session.QueryObjectCondition(_map, pair.Value.Entity, pair.Value.Kind) == SessionObjectCondition.Busted).Select(pair => pair.Key).ToArray();
    }

    /// <summary>One line for the status bar about an object attack (no original text).</summary>
    public static string Describe(SessionObjectAttackResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Disposition == GameSessionDisposition.Rejected) return $"attack refused: {result.Failure}";
        if (result.Resolution is not { } resolution) return "attack: nothing happened";
        if (resolution.Refusal != AttackRefusal.None) return $"attack: {resolution.Refusal}";
        if (resolution.Damage is { Prevented: not DamagePrevention.None } prevented) return $"hit: no damage ({prevented.Prevented})";
        if (result.Vetoed) return "hit: the object's script cancelled the damage";
        if (result.Life is { } life) return $"critical miss: the hero takes {life.HitPointDamage} damage";
        var trap = result.Host.HasFlag(SessionObjectHostSteps.TrapFires) ? " — its trap goes off" : "";
        if (result.Object is { } hit)
            return (hit.Broken ? $"hit: {hit.Damage - hit.DamageBefore} damage, {Broke(hit.Break)}" : $"hit: {hit.Damage - hit.DamageBefore} damage") + trap;
        if (result.Item is { } item) return item.Break switch
        {
            Arcanum.Simulation.Items.ItemBreakStep.Broken => "hit: the item breaks",
            Arcanum.Simulation.Items.ItemBreakStep.Deleted => "hit: the item is destroyed",
            _ => $"hit: the item has {item.Condition?.HitPoints} hit points left",
        } + trap;
        return "attack: no damage";
    }

    private static string Broke(ObjectBreak kind) => kind switch
    {
        ObjectBreak.Destroyed => "destroyed",
        ObjectBreak.WindowBusted => "the window is busted",
        ObjectBreak.Spared => "a junk pile does not break",
        ObjectBreak.ContainerSpilled => "the container breaks and spills its contents",
        ObjectBreak.SceneryRespawns => "it breaks and will come back",
        ObjectBreak.SceneryBusted or ObjectBreak.TrapBusted => "busted",
        _ => kind.ToString(),
    };

    /// <summary>A container's lock baseline: its flags (field 0x38) and lock key (field 0x3A), its current ART's frame and whether it is a
    /// junk pile (ARC-WORLD-101: the loot window's open attempt reads the same baseline as an attack).</summary>
    public static SessionOpenableBaseline ContainerBaseline(AreaObjectRecord row, CombatBreakableFields fields)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(fields);
        return new(OpenableKind.Container, unchecked((int)Field(row, TypeFlagsSlot)), unchecked((int)Field(row, KeySlot)),
            (int)((fields.Object.CurrentArt >> 14) & 31), fields.Object.Prototype == SessionObjectFacts.JunkPilePrototypeFile);
    }

    private static long Field(AreaObjectRecord row, int slot) =>
        row.EffectiveFields.FirstOrDefault(f => f.Slot == slot && f.Presence == AreaPresence.Value)?.Number ?? 0;
}
