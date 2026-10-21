using Arcanum.Data.Area;
using Arcanum.Data.Combat;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Locks;
using Arcanum.Simulation.Movement;

namespace Arcanum.TerrainViewer.Core;

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
/// an object's use script is a trap is not read (the viewer runs no trap). It also names the objects the session destroyed or
/// switched off, which the view no longer draws.
/// </summary>
public sealed class AreaObjectTargets
{
    private const int TypeFlagsSlot = 36, KeySlot = 38;
    private readonly SessionMapId _map;
    private readonly IReadOnlyDictionary<AreaObjectKey, (string Entity, SessionOpenableBaseline Baseline)> _portals;
    private readonly AreaInventoryMap? _inventory;
    private readonly Dictionary<AreaObjectKey, (AreaObjectRecord Row, string Entity, DamageableObjectKind Kind)> _objects = [];

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

    /// <summary>The attackable object drawn as <paramref name="key"/>, or null for anything else (an NPC, a wall, an object the session
    /// destroyed or switched off, an item no longer on the ground).</summary>
    public AreaObjectAttackTarget? Target(GameSessionOwner session, AreaObjectKey key)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (_objects.TryGetValue(key, out var found))
        {
            if (session.QueryObjectCondition(_map, found.Entity, found.Kind) is SessionObjectCondition.Destroyed or SessionObjectCondition.Off) return null;
            var read = CombatFieldReader.ReadBreakable(found.Row);
            if (read.Fields is not { } fields) return new(found.Entity, found.Row, found.Kind, null, null, null, $"{read.Failure} {read.Detail}");
            var cell = new CellCoord(checked((int)found.Row.WorldX!.Value), checked((int)found.Row.WorldY!.Value));
            SessionOpenableBaseline? openable = found.Kind switch
            {
                DamageableObjectKind.Portal when _portals.TryGetValue(key, out var portal) => portal.Baseline,
                DamageableObjectKind.Container => new SessionOpenableBaseline(OpenableKind.Container, unchecked((int)Field(found.Row, TypeFlagsSlot)),
                    unchecked((int)Field(found.Row, KeySlot)), (int)((fields.Object.CurrentArt >> 14) & 31),
                    fields.Object.Prototype == SessionObjectFacts.JunkPilePrototypeFile),
                _ => null,
            };
            return SessionObjectFacts.FromCombatFields(fields, cell, openable) is { } baseline
                ? new(found.Entity, found.Row, found.Kind, new(_map, found.Entity, baseline), null, null, null)
                : new(found.Entity, found.Row, found.Kind, null, null, null, "values outside the damage rules' bounds");
        }
        // An item on the ground (the seed's, not taken or deleted).
        if (_inventory is null || !_inventory.TryGetEntity(key, out var item) || !_inventory.TryGetRow(item, out var row)) return null;
        if (session.QueryMapInventory(_map)?.Ground.Any(g => g.Item.Entity == item) != true) return null;
        if (CombatFieldReader.Read(row).Fields is not { } itemFields) return new(item, row, DamageableObjectKind.Item, null, null, null, "item fields do not read");
        return SessionWearItem.FromCombatFields(item, itemFields) is { } wear && SessionObjectFacts.ItemFacts(itemFields) is { } facts
            ? new(item, row, DamageableObjectKind.Item, null, wear, facts, null)
            : new(item, row, DamageableObjectKind.Item, null, null, null, "values outside the durability bounds");
    }

    /// <summary>The objects the session destroyed or switched off: the view no longer draws them.</summary>
    public IReadOnlySet<AreaObjectKey> Gone(GameSessionOwner session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return _objects.Where(pair => session.QueryObjectCondition(_map, pair.Value.Entity, pair.Value.Kind) is SessionObjectCondition.Destroyed
            or SessionObjectCondition.Off).Select(pair => pair.Key).ToHashSet();
    }

    /// <summary>One line for the status bar about an object attack (no original text).</summary>
    public static string Describe(SessionObjectAttackResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Disposition == GameSessionDisposition.Rejected) return $"attack refused: {result.Failure}";
        if (result.Resolution is not { } resolution) return "attack: nothing happened";
        if (resolution.Refusal != AttackRefusal.None) return $"attack: {resolution.Refusal}";
        if (resolution.Damage is { Prevented: not DamagePrevention.None } prevented) return $"hit: no damage ({prevented.Prevented})";
        if (result.Life is { } life) return $"critical miss: the hero takes {life.HitPointDamage} damage";
        if (result.Object is { } hit)
            return hit.Broken ? $"hit: {hit.Damage - hit.DamageBefore} damage, {Broke(hit.Break)}" : $"hit: {hit.Damage - hit.DamageBefore} damage";
        if (result.Item is { } item) return item.Break switch
        {
            Arcanum.Simulation.Items.ItemBreakStep.Broken => "hit: the item breaks",
            Arcanum.Simulation.Items.ItemBreakStep.Deleted => "hit: the item is destroyed",
            _ => $"hit: the item has {item.Condition?.HitPoints} hit points left",
        };
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

    private static long Field(AreaObjectRecord row, int slot) =>
        row.EffectiveFields.FirstOrDefault(f => f.Slot == slot && f.Presence == AreaPresence.Value)?.Number ?? 0;
}
