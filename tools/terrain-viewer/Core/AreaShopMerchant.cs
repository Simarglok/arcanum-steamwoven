using Arcanum.Data.Area;
using Arcanum.Data.Combat;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

public enum AreaShopMapStatus
{
    /// <summary>The shop map and its merchant are loaded.</summary>
    Ready,
    /// <summary>The map list has no <c>SHOPPING_MAP</c> row: the original starts the game at once (OS2).</summary>
    NoShopMap,
    /// <summary>The shop map has no NPC: the original starts the game at once (OS2).</summary>
    NoMerchant,
    /// <summary>The project could not load it (the map list, the map or its inventory): the shop is the kit alone, with the reason.</summary>
    Failed,
}

/// <summary>
/// ARC-UI-038: the shop map the original's new game runs on behind the menu (the map list's <c>SHOPPING_MAP</c>, 0x40FF50 with 2): its
/// name, runtime map and start tile (where the original's game reset puts the PC, 0x4B9BE0), its inventory map (the session seed and the
/// object rows) and the merchant the opening shop barters with: the first NPC of the whole map (0x4B97E0 gives the map's size, 0x440B40
/// with the NPC mask 0x10000 lists the objects of every sector in their lists' order, leaving out the destroyed and switched-off ones,
/// ledger OS7) in the seed's order, with its stored fields.
/// </summary>
public sealed record AreaShopMap(string Name, int RuntimeMap, CellCoord Start, AreaInventoryMap Inventory, string Merchant, CombatFields MerchantFields)
{
    /// <summary>The object flags the search leaves out (0x440B40 against the mask 0x5E2F88, written 3 at 0x43A4F0 and 0x43AAC4: destroyed
    /// 0x1 and off 0x2).</summary>
    public const uint SkippedObjectFlags = 0x3;

    /// <summary>The merchant's object row.</summary>
    public AreaObjectRecord MerchantRow => Inventory.Row(Merchant);

    /// <summary>The merchant of an inventory map (the original's first NPC, OS7): the first NPC holder of the seed whose stored fields carry
    /// neither flag of <see cref="SkippedObjectFlags"/>; null when there is none. An NPC before it whose stored fields do not read stops the
    /// search (<paramref name="unreadable"/>: the project cannot tell whether the original would take it).</summary>
    /// <param name="read">The stored fields of a row (null: <see cref="CombatFieldReader.Read(AreaObjectRecord)"/>; a test's fields).</param>
    public static (string Entity, CombatFields Fields)? FirstNpc(AreaInventoryMap inventory, out string? unreadable, Func<AreaObjectRecord, CombatFields?>? read = null)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        read ??= row => CombatFieldReader.Read(row).Fields;
        unreadable = null;
        foreach (var holder in inventory.Seed.Holders.Where(holder => holder.Kind == SessionHolderKind.Npc))
        {
            if (read(inventory.Row(holder.Entity)) is not { Npc: not null } fields) { unreadable = holder.Entity; return null; }
            if ((fields.Object.Flags & SkippedObjectFlags) == 0) return (holder.Entity, fields);
        }
        return null;
    }

    /// <summary>The shop map of a map list row and its built inventory map; <see cref="AreaShopMapStatus.NoMerchant"/> without an NPC and
    /// <see cref="AreaShopMapStatus.Failed"/> when the row has no start or names another runtime map than the inventory's.</summary>
    public static AreaShopMapLoad From(MapListEntry entry, AreaInventoryMap inventory, Func<AreaObjectRecord, CombatFields?>? read = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(inventory);
        if (entry.Start is not { } start || start.X > int.MaxValue || start.Y > int.MaxValue)
            return new(AreaShopMapStatus.Failed, null, $"the shop map {entry.Name} has no start tile");
        if (entry.Runtime != inventory.RuntimeMap)
            return new(AreaShopMapStatus.Failed, null, $"the shop map {entry.Name} is runtime map {entry.Runtime}, its inventory {inventory.RuntimeMap}");
        if (FirstNpc(inventory, out var unreadable, read) is not { } merchant)
            return unreadable is not null ? new(AreaShopMapStatus.Failed, null, $"the shop map {entry.Name}: the stored fields of NPC {unreadable} do not read")
                : new(AreaShopMapStatus.NoMerchant, null, $"the shop map {entry.Name} has no NPC");
        return new(AreaShopMapStatus.Ready, new(entry.Name, entry.Runtime, new((int)start.X, (int)start.Y), inventory, merchant.Entity, merchant.Fields),
            $"{entry.Name} (runtime map {entry.Runtime}) merchant {merchant.Entity}");
    }

    /// <summary>
    /// Loads the shop map of the module of <paramref name="launch"/> (a whole map through the viewer's area load: the launch's module,
    /// archives and game folder with the shop map's name, no sector, arrival or hero) and builds its inventory map. Never throws for data:
    /// a missing map list row is <see cref="AreaShopMapStatus.NoShopMap"/>, a load or inventory the project refuses is
    /// <see cref="AreaShopMapStatus.Failed"/> with the reason.
    /// </summary>
    public static AreaShopMapLoad Load(TerrainViewerArguments launch, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(launch);
        if (launch.GameRoot is null) return new(AreaShopMapStatus.Failed, null, "the launch names no game folder (--game-root)");
        MapListEntry? entry;
        try
        {
            var found = AreaContentRoot.Discover(launch.ModulePath);
            if (found.Status != AreaContentRootStatus.Ready) return new(AreaShopMapStatus.Failed, null, "the module's content did not mount: " + found.Detail);
            var opened = ContentStack.Open(found.Plan!);
            if (opened.Status != ContentStackStatus.Ready) return new(AreaShopMapStatus.Failed, null, "the module's content did not open: " + opened.Status);
            using var stack = opened.Stack!;
            var list = MapList.Read(stack);
            if (list.Status != MapListStatus.Read) return new(AreaShopMapStatus.Failed, null, "the map list did not read: " + list.Status);
            entry = list.OfType(MapListType.Shopping);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new(AreaShopMapStatus.Failed, null, "the map list did not read: " + error.GetType().Name);
        }
        if (entry is null) return new(AreaShopMapStatus.NoShopMap, null, "the map list has no SHOPPING_MAP");
        TerrainLoadResult result;
        try
        {
            result = AreaLoadOperation.Execute(1, launch with { AreaMapName = entry.Name, AreaMapSector = null, AreaMapArrival = null, Hero = null, MainMenu = false },
                token);
        }
        catch (Exception error) when (error is InvalidOperationException or AreaException or IOException or InvalidDataException)
        {
            return new(AreaShopMapStatus.Failed, null, $"the shop map {entry.Name} did not load: {error.GetType().Name} {error.Message}");
        }
        if (result.Area is not { } area || result.AreaStepSnapshot is not { } steps)
            return new(AreaShopMapStatus.Failed, null, $"the shop map {entry.Name} loaded without its objects");
        var build = AreaInventoryMap.Build(area.Snapshot, steps, token);
        if (build.Map is null) return new(AreaShopMapStatus.Failed, null, $"the shop map {entry.Name} has no inventory ({build.Status} {build.Detail})");
        return From(entry, build.Map);
    }
}

public sealed record AreaShopMapLoad(AreaShopMapStatus Status, AreaShopMap? Map, string Detail);

/// <summary>
/// ARC-UI-038: the opening shop's barter (window 15's refresh, 0x545E80, ledger OS2, OS7) on a session made for the new character on the
/// shop map, as the original's new game runs on it behind the menu: the shop map's seed is attached, the merchant forgets the PC
/// (0x4C0F50), its inventory regenerates (0x463E20: a merchant without a source of its own keeps only its persistent items) and then its
/// substitute container's (0x45F650 → 0x463E20), and the barter window opens on the hero and the merchant (0x572240 with 1: the
/// container's gold moves to the merchant and both lists are identified, then the window's restock schedule of the container). The
/// session stays the shop's until <see cref="Take"/> hands it to the game (whose start wields the best items and teleports the PC to
/// the start map, OS5), or <see cref="Dispose"/> drops it (the shop's Back: the next opening makes the kit and the shop anew).
/// </summary>
public sealed class AreaShopSession : IDisposable
{
    private GameSessionOwner? _owner;
    private readonly AreaBarterData _data;

    private AreaShopSession(GameSessionOwner owner, AreaShopMap map, AreaBarterData data, AreaInventoryHost inventory, AreaBarter barter,
        SessionBarterOpenResult opened, SessionRegenerateResult merchant, SessionRegenerateResult? container)
    {
        _owner = owner; _data = data; Map = map; Inventory = inventory; Barter = barter; Opened = opened; MerchantRegeneration = merchant;
        ContainerRegeneration = container;
    }

    public AreaShopMap Map { get; }
    /// <summary>The inventory host of the shop map (its rows name and describe the merchant's wares).</summary>
    public AreaInventoryHost Inventory { get; }
    public AreaBarter Barter { get; }
    /// <summary>The barter open's session part (the moved coins, the identified items).</summary>
    public SessionBarterOpenResult Opened { get; }
    public SessionRegenerateResult MerchantRegeneration { get; }
    /// <summary>The substitute container's regeneration; null without a container.</summary>
    public SessionRegenerateResult? ContainerRegeneration { get; }
    /// <summary>The shop's session; null once taken or disposed.</summary>
    public GameSessionOwner? Owner => _owner;

    /// <summary>
    /// Opens the shop on <paramref name="owner"/>, a session made for the new character with its actor at the shop map's start and the
    /// window's choices and the editor's presses applied (as the start applies them). Null with the reason when a step is refused (the
    /// caller then disposes the session); the original's refused barter opening starts the game, which the caller decides.
    /// </summary>
    /// <param name="name">An item's name for the window (its entity and kind).</param>
    /// <param name="merchantName">The merchant's name.</param>
    public static AreaShopSession? Open(GameSessionOwner owner, AreaShopMap map, AreaBarterData data, Func<string, SessionItemKind, string> name,
        string merchantName, out string? refused)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(name);
        refused = null;
        var shopMap = new SessionMapId(map.RuntimeMap);
        if (owner.QueryActor().Map != shopMap) { refused = $"the session's actor is on runtime map {owner.QueryActor().Map.RuntimeMap}, not the shop map's {map.RuntimeMap}"; return null; }
        var attached = owner.AttachMapInventory(shopMap, map.Inventory.Seed);
        if (attached.Disposition == GameSessionDisposition.Rejected) { refused = $"the shop map's inventory did not attach ({attached.Failure})"; return null; }
        // 0x4C0F50 (R6): the merchant forgets meeting the PC.
        var forgot = owner.ForgetReaction(owner.Version, map.Merchant);
        if (forgot.Disposition == GameSessionDisposition.Rejected) { refused = $"the merchant did not forget the PC ({forgot.Failure})"; return null; }
        var inventory = new AreaInventoryHost([map.Inventory]);
        var restock = new AreaRestockHost(owner, inventory, data, NoScreen.Instance);
        var world = new AreaEventWorld(() => null, () => null, restock: () => restock);
        var merchantRegeneration = owner.RegenerateInventory(owner.Version, map.Merchant, world);
        if (merchantRegeneration.Disposition == GameSessionDisposition.Rejected) { refused = $"the merchant's inventory did not regenerate ({merchantRegeneration.Failure})"; return null; }
        var row = map.MerchantRow;
        if (AreaBarterMerchant.From(map.Merchant, merchantName, map.MerchantFields, data, owner.QueryCompanion(map.Merchant).LedByPc) is not { } merchant)
        {
            refused = "the shop map's merchant is not an NPC";
            return null;
        }
        if (row is { WorldX: { } x, WorldY: { } y } && x is >= int.MinValue and <= int.MaxValue && y is >= int.MinValue and <= int.MaxValue)
            merchant = merchant.WithSubstitute(map.Inventory.Substitute(map.Merchant), new((int)x, (int)y), data);
        SessionRegenerateResult? containerRegeneration = null;
        if (merchant.Container is { } container)
        {
            containerRegeneration = owner.RegenerateInventory(owner.Version, container, world);
            if (containerRegeneration.Disposition == GameSessionDisposition.Rejected)
            {
                refused = $"the merchant's container did not regenerate ({containerRegeneration.Failure})";
                return null;
            }
        }
        var state = new AreaConversationState(owner, shopMap, [map.Merchant], AreaConversationProfile.Diagnostic.InitialReaction, scopes: [AreaConversationHost.Scope(row, owner)]);
        var barter = new AreaBarter(owner, inventory, data, merchant, () => state.Reaction(0),
            delta => owner.AdjustReaction(owner.Version, map.Merchant, state.NpcFacts(), state.PcFactsFor(0), delta).Disposition == GameSessionDisposition.Changed, name);
        var opened = barter.Open();
        // ARC-WORLD-090: a substitute container that is gone leaves the merchant trading from its own list.
        if (opened is { Disposition: GameSessionDisposition.Rejected, Failure: GameSessionFailure.UnknownHolder } && barter.Merchant.Container is not null)
        {
            barter = barter.WithoutContainer();
            opened = barter.Open();
        }
        if (opened.Disposition == GameSessionDisposition.Rejected) { refused = $"the barter window did not open ({opened.Failure})"; return null; }
        // The window's open schedules the shown object's restock: the container, else the merchant (RK2, RK9).
        if (AreaRestockHost.Schedule(owner, inventory, barter.Merchant.Container ?? barter.Merchant.Entity) is { Disposition: GameSessionDisposition.Rejected } schedule)
        {
            refused = $"the restock was not scheduled ({schedule.Failure})";
            return null;
        }
        return new(owner, map, data, inventory, barter, opened, merchantRegeneration, containerRegeneration);
    }

    /// <summary>The wear facts of a ware the hero bought (not a kit item, whose facts are the kit's): from its object row's fields on the shop
    /// map, or for an item the regeneration made from its prototype's stored fields (as the restock host's NPC wield reads them); null for
    /// an item the hero does not hold or whose facts do not read.</summary>
    public SessionWearableItem? Wearable(string entity)
    {
        if (_owner is not { } owner) return null;
        // A map item the hero took, or (ARC-WORLD-055) an item the session made that the hero took from the merchant.
        SessionItemKind? kind = owner.QueryPcInventory().FirstOrDefault(item => item.Entity == entity) is { Entity: not null } taken ? taken.Kind
            : owner.QueryMadeItems().FirstOrDefault(item => item.Entity == entity) is { Entity: not null } made ? made.Kind : null;
        if (kind is not { } held) return null;
        var fields = Map.Inventory.TryGetRow(entity, out var row) ? CombatFieldReader.Read(row).Fields : _data.Prototype(held.Prototype);
        return fields is null ? null : SessionEquipmentFacts.Wearable(held, fields);
    }

    /// <summary>
    /// The game's start on this session (0x5412E0 with the new-game flag, OS5): the best weapon and body armour into empty slots, the
    /// kit's wear facts by kit number and a bought ware's from its shop row (<see cref="AreaNewCharacter.WieldStart"/>), then the PC is
    /// teleported to the start map's tile (<paramref name="startMap"/>, <paramref name="start"/>) by the session's change of place (<see cref="AreaNewGameStart"/>). Null on success, else why the session
    /// refused it: the session is not taken, but a wield done before a refused teleport stays (the caller drops the shop then).
    /// </summary>
    public string? Start(IReadOnlyList<SessionWearableItem?> kitWear, SessionMapId startMap, CellCoord start)
    {
        ArgumentNullException.ThrowIfNull(kitWear);
        if (_owner is not { } owner) return "the shop's session is gone";
        if (AreaNewCharacter.WieldStart(owner, kitWear, Wearable) is { } wield) return wield;
        // ARC-DEV-115 (MIG11 S4, RL29): the teleport is the session's change of place with the start's flags (0x45), not a bare placement.
        return AreaNewGameStart.Run(owner, startMap, start, NeighborDirection.XPlus) is { } refused ? $"the start map's teleport was refused ({refused})" : null;
    }

    /// <summary>Hands the session to the game: this shop no longer disposes it.</summary>
    public GameSessionOwner Take()
    {
        var owner = _owner ?? throw new ObjectDisposedException(nameof(AreaShopSession));
        _owner = null;
        return owner;
    }

    public void Dispose()
    {
        _owner?.Dispose();
        _owner = null;
    }

    // The shop's merchant is regenerated directly, without the screen test of the restock event.
    private sealed class NoScreen : IAreaRestockScreen
    {
        public static NoScreen Instance { get; } = new();
        public int? ShownMap => null;
        public AreaPixelRect? GameWindow => null;
        public (double Width, double Height) WindowSize => (0, 0);
        public AreaPixelRect? BodyRect(AreaObjectKey key) => null;
    }
}
