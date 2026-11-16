using Arcanum.Data.Characters;
using Arcanum.Data.Combat;
using Arcanum.Session;
using Arcanum.Simulation;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using GamepadLab.Core;

namespace Arcanum.World;

/// <summary>ARC-WORLD-096: a rectangle in area pixels (the AREA plan's drawing space).</summary>
public readonly record struct AreaPixelRect(double X, double Y, double Width, double Height)
{
    /// <summary>The restock's screen test (0x463CC0..0x463D1B): the rectangles overlap, strictly on every side.</summary>
    public bool Meets(AreaPixelRect other) =>
        X < other.X + other.Width && Y < other.Y + other.Height && other.X < X + Width && other.Y < Y + Height;
}

/// <summary>ARC-WORLD-096: what the viewer shows, for the restock host: the shown map, the game window in area pixels at the viewer's
/// zoom (null when no map is shown; the screen test), the window's size at zoom 1 (the merchant search's area, which a display zoom
/// must not change) and an object's drawn rectangle on the shown map.</summary>
public interface IAreaRestockScreen
{
    int? ShownMap { get; }
    AreaPixelRect? GameWindow { get; }
    (double Width, double Height) WindowSize { get; }
    /// <summary>The union of the drawn rectangles of the object's T0 body on the shown map, or null when it draws none.</summary>
    AreaPixelRect? BodyRect(Arcanum.Data.Area.AreaObjectKey key);
}

/// <summary>
/// ARC-WORLD-096: the AREA viewer's restock host (the original's live objects for the restock event, ledger RK1–RK9; contract
/// docs/world/barter-runtime.md). It reads the holders' T0 restock fields and persistent items from the inventory maps, death and the PC
/// leader from the session, the screen from the viewer, the inventory sources and item prototypes from the barter data (null when they
/// failed to load: every set is <see cref="SessionRestockSetStatus.Unsupported"/>, so a restock keeps the lists, and no item is known); the
/// <see cref="AreaEventWorld"/> answers <see cref="ISessionRestockWorld"/> through it. NPC positions are the cells the AI host has them on
/// (<see cref="AreaInventoryHost.NpcLiveCell"/>, ARC-WORLD-2357), else their T0 cells.
/// </summary>
public sealed class AreaRestockHost
{
    private readonly GameSessionOwner _session;
    private readonly AreaInventoryHost _inventory;
    private readonly AreaBarterData? _data;
    private readonly IAreaRestockScreen _screen;

    public AreaRestockHost(GameSessionOwner session, AreaInventoryHost inventory, AreaBarterData? data, IAreaRestockScreen screen)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _data = data;
        _screen = screen ?? throw new ArgumentNullException(nameof(screen));
    }

    public GameSessionOwner Session => _session;
    public AreaInventoryHost Inventory => _inventory;
    public AreaBarterData? Data => _data;

    /// <summary>
    /// The restock schedule at an inventory window open (0x5733A9 in 0x5727B0, ledger RK2): the window's shown object (in the barter
    /// mode the merchant's substitute container when it has one, else the merchant, RK9; in the loot mode the looted holder) gets a new
    /// restock event when its T0 inventory source is not 0. Null when the object is no seed holder of the actor's map (a junk pile, the
    /// PC): nothing is asked of the session.
    /// </summary>
    public static GameSessionMutationResult? Schedule(GameSessionOwner session, AreaInventoryHost inventory, string shown)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(shown);
        var runtimeMap = session.QueryActor().Map.RuntimeMap;
        var map = inventory.Maps.FirstOrDefault(found => found.RuntimeMap == runtimeMap);
        // ARC-DEV-386 (E1-c4): a critter the session created on that map is a holder of its one-row map.
        if (map is null || !map.Seed.Holders.Any(holder => holder.Entity == shown))
            map = inventory.Resolver.CreatedMap(shown) is { } created && created.RuntimeMap == runtimeMap ? created : null;
        if (map is null) return null;
        return session.ScheduleRestock(session.Version, shown, map.RestockSeed(shown).InventorySource != 0);
    }

    /// <summary>The holder's facts (RK1, RK3): its seed kind and T0 restock fields, death and a PC leader from the session (an NPC), and
    /// whether its drawn body meets the game window (a container on the shown map; anything else is off screen). Null for an object that
    /// is no seed holder of that map.</summary>
    public RestockHolder? Facts(SessionMapId map, string entity)
    {
        if (Holder(map.RuntimeMap, entity) is not { } found) return null;
        var (inventoryMap, holder) = found;
        var seed = inventoryMap.RestockSeed(entity);
        if (holder.Kind == SessionHolderKind.Npc)
            return new(RestockHolderKind.Npc, seed.InventorySource, _session.QueryNpcDead(entity), _session.QueryPcLeader(entity) is not null, 0, false);
        return new(RestockHolderKind.Container, seed.InventorySource, false, false, seed.ContainerFlags, OnScreen(inventoryMap, entity));
    }

    /// <summary>
    /// ARC-DEV-386 (RK16, RK17): whether the restock body 0x463C60 that an object's creation runs does anything for <paramref name="entity"/>: a living NPC of the map with an inventory source and no PC leader
    /// (<see cref="RestockRules.Fire"/>). Otherwise it returns before the clear (a source of 0 is not cleared or dressed). <paramref name="beforeLeader"/> (ARC-MAGIC-053): the creation's restock runs
    /// before the summon effect makes the creature the summoner's follower (0x442260 inside 0x450C40), so a summoned creature's leader is not read.
    /// </summary>
    public bool GeneratesAtCreation(SessionMapId map, string entity, bool beforeLeader = false) =>
        Facts(map, entity) is { Kind: RestockHolderKind.Npc } facts && RestockRules.Fire(beforeLeader ? facts with { PcLeader = false } : facts, null).Step == RestockStep.Regenerate;

    /// <summary>Whether the container's drawn body meets the game window (0x43DFD0 against 0x5E8808..0x5E8814): only on the shown map.</summary>
    [OriginalFunction(0x43DFD0, OriginalFunctionScope.Partial)]
    public bool OnScreen(SessionMapId map, string entity) =>
        Holder(map.RuntimeMap, entity) is { } found && OnScreen(found.Map, entity);

    private bool OnScreen(AreaInventoryMap map, string entity) =>
        _screen.ShownMap == map.RuntimeMap && _screen.GameWindow is { } window && _screen.BodyRect(map.Row(entity).Key) is { } body && body.Meets(window);

    /// <summary>
    /// The merchant search of an off-screen container (0x463D2F..0x463D72, RK3): the NPCs of the game-window-sized area around it (0x440FC0
    /// with the NPC mask: the tile rectangle that holds the four corners of a window-sized screen rectangle centred on the container's
    /// tile, RK9), in the seed's order, the first whose substitute container (0x45F650: no leader at T0 or in the session, field 0x126,
    /// within 20 tiles) it is; null when none is. ARC-WORLD-2357: an NPC stands where the AI host has it now (<see cref="AreaInventoryHost.NpcLiveCell"/>), else on its T0 cell; the container on its T0 cell.
    /// </summary>
    public string? Merchant(SessionMapId map, string container)
    {
        if (Holder(map.RuntimeMap, container) is not { Holder.Kind: SessionHolderKind.Container } found || Cell(found.Map, container) is not { } at)
            return null;
        var inventoryMap = found.Map;
        var (minX, minY, maxX, maxY) = Vicinity(at, _screen.WindowSize);
        foreach (var npc in inventoryMap.Seed.Holders.Where(holder => holder.Kind == SessionHolderKind.Npc))
            if (_inventory.LiveCell(inventoryMap, npc.Entity) is { } cell && cell.X >= minX && cell.X <= maxX && cell.Y >= minY && cell.Y <= maxY &&
                inventoryMap.Substitute(npc.Entity) is { } substitute && substitute.Entity == container && _session.QueryLeader(npc.Entity) is null &&
                Arcanum.Simulation.Ai.AiExecutorRules.Distance(cell, substitute.Cell) <= AreaBarterMerchant.SubstituteRange)
                return npc.Entity;
        return null;
    }

    /// <summary>The tile rectangle (inclusive) of 0x440FC0 around <paramref name="center"/>: the tiles under the four corners of a
    /// <paramref name="window"/>-sized screen rectangle centred on the tile's centre, and every tile between them.</summary>
    public static (int MinX, int MinY, int MaxX, int MaxY) Vicinity(CellCoord center, (double Width, double Height) window)
    {
        var middle = AreaCellPicking.Center(center);
        var corners = new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) }
            .Select(corner => AreaCellPicking.Pick(new Vec2(middle.X + corner.Item1 * window.Width / 2, middle.Y + corner.Item2 * window.Height / 2)) ?? center)
            .ToArray();
        return (corners.Min(cell => cell.X), corners.Min(cell => cell.Y), corners.Max(cell => cell.X), corners.Max(cell => cell.Y));
    }

    /// <summary>
    /// ARC-WORLD-101: the host's part of a generated-once container's seed (0x463E20 with flag 0x200, RK10): the container's T0 tile x (the
    /// low 32 bits of its location) and the session hero's player name bytes (0x441B60 on field 0x10E: <see cref="SessionHeroState.PlayerName"/>,
    /// empty when unset). A session without a hero seeds from the tile x alone, so its name is empty. Null for an object that is no container
    /// of that map's seed or a tile outside 32 bits.
    /// </summary>
    public SessionGeneratedOnceFacts? GeneratedOnceFacts(SessionMapId map, string container)
    {
        if (Holder(map.RuntimeMap, container) is not { Holder.Kind: SessionHolderKind.Container } found || Cell(found.Map, container) is not { } at) return null;
        return new(at.X, _session.QueryHero() is { } hero ? hero.PlayerName : []);
    }

    /// <summary>
    /// ARC-WORLD-103: the facts of an NPC's best-items wield after its regeneration (0x4654F0, RK15): the NPC as a wearer and as the choice
    /// scores it from its combat values (<paramref name="combat"/>, its fields on that map) in its session life state (the map data's when
    /// the session has none); an item's wear facts from its object row on that map, or (an item the session made, a kit item the hero gave)
    /// from its prototype's stored fields. The viewer has one diagnostic body art, so it answers the art lookup with yes, as at the hero's
    /// start. Null for an object that is no NPC of that map's seed or without combat values.
    /// </summary>
    public SessionNpcWielder? Wielder(SessionMapId map, string npc, SessionCritterCombat? combat)
    {
        if (combat is null || Holder(map.RuntimeMap, npc) is not { Holder.Kind: SessionHolderKind.Npc } found) return null;
        var state = _session.QueryLife(combat.Target).View?.Record.State ?? combat.Loaded;
        var inventoryMap = found.Map;
        SessionWearableItem? Item(SessionInventoryItem item)
        {
            if (inventoryMap.TryGetRow(item.Entity, out var row))
                return CombatFieldReader.Read(row).Fields is { } fields ? SessionEquipmentFacts.Wearable(item.Facts.Kind, fields) : null;
            return _data?.Prototype(item.Facts.Kind.Prototype) is { } prototype ? SessionEquipmentFacts.Wearable(item.Facts.Kind, prototype) : null;
        }
        return new(combat.Wearer(state.Conditions), combat.Chooser(state), Item, (_, _) => true);
    }

    /// <summary>
    /// ARC-MAGIC-1561 (ledger RM3c, IE5, EQ12): the carried light of an NPC whose worn items the session recalculated, as the light recalculation 0x467E80 leaves it: the largest size of its worn items' light flags, each shrunk by its power ratio against the
    /// NPC's aptitude (<see cref="Arcanum.Simulation.Equipment.EquipmentRules.Light"/>); <see cref="Arcanum.Data.Lighting.CarriedLightSize.None"/> when no worn item gives any. The original recalculates when an item with light flags is wielded or unwielded
    /// and in the rewield a magick or tech point write runs (0x465530), and leaves the light as saved for any other item. Steamwoven reads the NPC as recalculated when the session dressed it (its worn record: the regeneration's and the AI gate's wields)
    /// or wrote its magick or tech points (TraitIdx stat 22 or 23), and then only when a worn item has light flags now, or one worn at load had (a light that comes from no worn item stays as saved). The light is derived at the question, with the NPC's aptitude now (the original
    /// stores the size it computed at the event: the difference is an aptitude that moved since, which the hero's light follows as well). Null: the saved light stands (the NPC was not recalculated, or has no light item now or at load), and for an NPC the
    /// restock does not know (no wielder).
    /// </summary>
    public Arcanum.Data.Lighting.CarriedLightSize? NpcLight(SessionMapId map, string npc, IAreaEventObjects? objects) => NpcLight(map, npc, objects, new(() => _session.QueryMapInventory(map)));

    /// <summary>ARC-MAGIC-1561: <see cref="NpcLight(SessionMapId, string, IAreaEventObjects?)"/> for each of <paramref name="npcs"/> with the map's inventory view read once, and only when an NPC passes the cheap test.</summary>
    public IReadOnlyDictionary<string, Arcanum.Data.Lighting.CarriedLightSize> NpcLights(SessionMapId map, IEnumerable<string> npcs, IAreaEventObjects? objects)
    {
        ArgumentNullException.ThrowIfNull(npcs);
        var lights = new Dictionary<string, Arcanum.Data.Lighting.CarriedLightSize>(StringComparer.Ordinal);
        var view = new Lazy<SessionMapInventory?>(() => _session.QueryMapInventory(map));
        foreach (var npc in npcs)
            if (NpcLight(map, npc, objects, view) is { } size) lights[npc] = size;
        return lights;
    }

    private Arcanum.Data.Lighting.CarriedLightSize? NpcLight(SessionMapId map, string npc, IAreaEventObjects? objects, Lazy<SessionMapInventory?> view)
    {
        if (objects is null) return null;
        // The cheap test first: the viewer asks for every NPC of the shown map each time the session changes.
        var recalculated = _session.QueryNpcWorn(npc) is not null || _session.QueryCritterIndexedWrites(npc).Stats.Keys.Any(index => index is 22 or 23);
        if (!recalculated) return null;
        if (Wielder(map, npc, objects.Critter(npc)) is not { } wielder || Holder(map.RuntimeMap, npc) is not { } found) return null;
        if (view.Value?.Holders.FirstOrDefault(holder => holder.Entity == npc && holder.Kind == SessionHolderKind.Npc) is not { } listed) return null;
        // What it wears now: the host's worn items (a broken or destroyed item is taken off), each with the wear facts of its list entry.
        if (objects.Worn(_session, map.RuntimeMap, npc) is not { } wornNow) return null;
        var worn = new List<Arcanum.Simulation.Equipment.EquipmentItem>();
        foreach (var item in wornNow)
        {
            // A worn item the host cannot describe leaves the light as saved: the recalculation would not know its flags.
            if (listed.Items.FirstOrDefault(entry => entry.Entity == item.Entity) is not { } entry || wielder.Items(entry) is not { } facts) return null;
            worn.Add(facts.Item);
        }
        var carries = worn.Any(item => item.Light != 0);
        if (!carries)
        {
            // The light the load gave from a worn item (the seed's worn rows) is cleared when that item left; a light of no worn item stays.
            var atLoad = found.Holder.Items.Any(entity => found.Map.Seed.Items.FirstOrDefault(row => row.Entity == entity) is { Facts.Worn: true } row &&
                wielder.Items(new(row.Entity, row.Facts)) is { } facts && facts.Item.Light != 0);
            if (!atLoad) return null;
        }
        return Size(Arcanum.Simulation.Equipment.EquipmentRules.Light(worn, wielder.Chooser.MagickTechAptitude));
    }

    private static Arcanum.Data.Lighting.CarriedLightSize Size(Arcanum.Simulation.Combat.LightEmission light) => light switch
    {
        Arcanum.Simulation.Combat.LightEmission.Small => Arcanum.Data.Lighting.CarriedLightSize.Small,
        Arcanum.Simulation.Combat.LightEmission.Medium => Arcanum.Data.Lighting.CarriedLightSize.Medium,
        Arcanum.Simulation.Combat.LightEmission.Large => Arcanum.Data.Lighting.CarriedLightSize.Large,
        Arcanum.Simulation.Combat.LightEmission.ExtraLarge => Arcanum.Data.Lighting.CarriedLightSize.ExtraLarge,
        _ => Arcanum.Data.Lighting.CarriedLightSize.None,
    };

    /// <summary>Whether a seed item carries the persistent flag 0x2000 (RK5); an item the session made carries none.</summary>
    public bool Persistent(SessionMapId map, string item) =>
        _inventory.Maps.FirstOrDefault(found => found.RuntimeMap == map.RuntimeMap)?.Persistent(item) == true;

    /// <summary>
    /// The set of an inventory source as the regeneration reads it (0x4BF5D0, RK6, RK7): a parsed set generates; a set the load emptied
    /// generates the coins its loot line read before the failure (unreachable when that pair did not read); an unreachable set is
    /// <see cref="SessionRestockSetStatus.Unreachable"/>; the shapes the project refuses are <see cref="SessionRestockSetStatus.Unsupported"/>.
    /// </summary>
    public SessionRestockSet Set(int source)
    {
        if (_data is null) return new(SessionRestockSetStatus.Unsupported, null);
        var read = InventorySourceRules.Read(_data.Sources, _data.Buy, source, _data.Existing.Contains);
        return read.Status switch
        {
            InventorySourceStatus.Parsed when read.Set is { } set => new(SessionRestockSetStatus.Read,
                new(set.MinCoins, set.MaxCoins, Array.AsReadOnly(set.Items.Select(line => new RestockLine(line.Rate, line.BasicPrototype)).ToArray()))),
            InventorySourceStatus.Emptied => InventorySourceRules.LootCoins(_data.Sources, source) switch
            {
                { Status: InventorySourceCoinsStatus.Read } coins => new(SessionRestockSetStatus.Read, new(coins.Min, coins.Max, Array.Empty<RestockLine>())),
                { Status: InventorySourceCoinsStatus.Undefined } => new(SessionRestockSetStatus.Unreachable, null),
                _ => new(SessionRestockSetStatus.Unsupported, null),
            },
            InventorySourceStatus.Unreachable => new(SessionRestockSetStatus.Unreachable, null),
            _ => new(SessionRestockSetStatus.Unsupported, null),
        };
    }

    /// <summary>What a line's basic prototype creates (0x4ED8B0 on prototype number + 20): its object type, number, name and description
    /// from the prototype's stored fields, with an ammunition prototype's type and units; null for no item prototype the data holds.</summary>
    public SessionRestockItem? Item(int basicPrototype)
    {
        if (basicPrototype > int.MaxValue - 20 || _data?.Prototype(basicPrototype + 20) is not { } fields) return null;
        var type = (SessionItemType)fields.Object.Type;
        if (!Enum.IsDefined(type)) return null;
        var kind = new SessionItemKind(type, basicPrototype + 20, fields.Object.Name, fields.Object.Description);
        return type == SessionItemType.Ammo ? new(kind, fields.Ammo?.AmmoType, fields.Ammo?.Quantity) : new(kind);
    }

    /// <summary>The kind of a new gold pile (prototype 9076).</summary>
    public SessionItemKind PileKind => _data?.GoldKind() ?? new(SessionItemType.Gold, AreaBarterData.GoldPrototype, 0, 0);

    // ARC-DEV-386 (E1-c4): a critter the session created on the map is a holder of its one-row map the resolver was given (RecordResolver.SetCreated), which is no map of the host's own.
    private (AreaInventoryMap Map, SessionSeedHolder Holder)? Holder(int runtimeMap, string entity) =>
        _inventory.Maps.FirstOrDefault(map => map.RuntimeMap == runtimeMap) is { } map && map.Seed.Holders.FirstOrDefault(holder => holder.Entity == entity) is { } holder
            ? (map, holder)
            : _inventory.Resolver.CreatedMap(entity) is { } created && created.RuntimeMap == runtimeMap && created.Seed.Holders.FirstOrDefault(holder => holder.Entity == entity) is { } made
                ? (created, made) : null;

    private static CellCoord? Cell(AreaInventoryMap map, string entity) =>
        map.Row(entity) is { WorldX: { } x, WorldY: { } y } && x is >= int.MinValue and <= int.MaxValue && y is >= int.MinValue and <= int.MaxValue
            ? new((int)x, (int)y) : null;
}
