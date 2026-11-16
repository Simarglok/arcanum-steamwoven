using Arcanum.Data.Combat;
using Arcanum.Session;
using Arcanum.Simulation;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;

namespace Arcanum.World;

/// <summary>
/// ARC-AI-1309 (the item search's ground, the pickup goal 15 and its end): the shown map's loose items and junk piles as <see cref="IAiItemSearchWorld"/> reads them (0x4AE4E0 square, 0x4A8F90),
/// the pickup starter (0x4356C0, <see cref="AiExecutorRules.StartPickup"/>) and the goal's end (0x42A9B0 → 0x42AA70, the transfer 0x4617F0 into the NPC's list,
/// <see cref="GameSessionOwner.PickUpItemByNpc"/>).
/// </summary>
public sealed partial class AreaAiHost
{
    /// <summary>The item flags the search skips (field 0x57 mask 0x80010): NO_DISPLAY (0x10) and NO_NPC_PICKUP (0x80000).</summary>
    private const uint NoDisplayFlag = 0x10, NoNpcPickupFlag = 0x80000;

    // The shown map's inventory view, held for the length of one item search (one query for its three calls) and read afresh otherwise.
    private SessionMapInventory? _groundView;
    // The NPC whose search is running (FirstInPile's insert check reads its carry weight).
    private string? _searchNpc;
    private bool _groundHeld;

    // The items an NPC's pickup goal is on its way to, with the cell each lay on at the start: Tile answers it (the item is no critter the host walks) without a query of the map's inventory at every step.
    // Several NPCs may be after one item; an entry goes when the item is taken or lies nowhere, and with a reset. A load in the middle of a walk loses it (the restored run ends as the object gone).
    private readonly Dictionary<string, CellCoord> _pickupItems = new(StringComparer.Ordinal);

    private sealed class GroundHold(AreaAiHost host) : IDisposable
    {
        public void Dispose() { host._groundHeld = false; host._groundView = null; host._searchNpc = null; }
    }

    private IDisposable HoldGround(string npc)
    {
        _searchNpc = npc;
        _groundHeld = true;
        _groundView = null;
        return new GroundHold(this);
    }

    // The shown map's inventory view: the session's map inventory when this host's map is the actor's (a pickup is the actor map's transfer), else none.
    private SessionMapInventory? GroundView()
    {
        if (_groundHeld && _groundView is not null) return _groundView;
        var map = new SessionMapId(_scene.RuntimeMap);
        var view = _session.QueryActor().Map == map ? _session.QueryMapInventory(map) : null;
        if (_groundHeld) _groundView = view;
        return view;
    }

    private static AiGroundKind? KindOf(SessionItemType type) => type switch
    {
        SessionItemType.Ammo => AiGroundKind.Ammunition,
        SessionItemType.Weapon => AiGroundKind.Weapon,
        SessionItemType.Armor => AiGroundKind.Armor,
        _ => null,
    };

    /// <summary>ARC-SCRIPT-2386 (IV13, hide mask 3 of the object lists 0x4407C0 and 0x440B40): whether the item is switched off (a script's toggle, a thrown item in flight): the pickup's source lists leave it out.</summary>
    private bool ItemOff(string item) => _session.QueryItemOff(new SessionMapId(_scene.RuntimeMap), item, dataOff: false);

    private static bool InSquare(CellCoord center, CellCoord cell, int halfSize) =>
        Math.Abs((long)cell.X - center.X) <= halfSize && Math.Abs((long)cell.Y - center.Y) <= halfSize;

    /// <summary>ARC-AI-1309 (0x4AE4E0, 0x4A8F90): the loose items of the kind in the square of half-size <paramref name="halfSize"/> around the NPC, in the session's ground order (the original's object list order was not read):
    /// a map seed's loose item, a spilled or dropped one, the hero's placed ones; none in a junk pile or in flight. Each with the item flags and worth the search tests (<see cref="Describe"/>).</summary>
    public IReadOnlyList<AiGroundItem> LooseItems(string npc, AiGroundKind kind, int halfSize)
    {
        if (GroundView() is not { } view || Tile(npc) is not { } at) return [];
        var found = new List<AiGroundItem>();
        foreach (var ground in view.Ground)
            if (KindOf(ground.Item.Facts.Kind.Type) == kind && InSquare(at, ground.Cell, halfSize) && !ItemOff(ground.Item.Entity))
                found.Add(Describe(npc, view, ground.Item, AiExecutorRules.Distance(at, ground.Cell), off: false));
        return found;
    }

    /// <summary>ARC-AI-1309: the junk piles in the square, in the order the session made them (the containers named 3023); ARC-AI-1486: then the map's own standing piles in the map's order (a container holder of the
    /// map's seed of prototype 3043, such as the Crash Site cave's). The original's object list order was not read.</summary>
    public IReadOnlyList<string> JunkPiles(string npc, int halfSize)
    {
        if (GroundView() is not { } view || Tile(npc) is not { } at) return [];
        var made = view.JunkPiles.Where(pile => InSquare(at, pile.Cell, halfSize)).Select(pile => pile.Entity);
        return [.. made, .. MapPilesIn(at, halfSize).Select(pile => pile.Entity)];
    }

    // The map's own standing junk piles whose cell lies in the square (the shown map's: a pickup is the actor map's transfer).
    private IReadOnlyList<(string Entity, CellCoord Cell, SessionObjectTarget Target)> MapPilesIn(CellCoord center, int halfSize) =>
        _scene.Combat.MapJunkPiles(_session, _scene.RuntimeMap, cell => InSquare(center, cell, halfSize));

    // The standing junk pile of the map's data that lists the item now, with its cell and target; null when the item lies in none (or the shown map is not the actor's).
    private (CellCoord Cell, SessionObjectTarget Target)? MapPileOf(string item) =>
        _session.QueryActor().Map.RuntimeMap == _scene.RuntimeMap && _scene.Combat.MapJunkPileHolding(_session, _scene.RuntimeMap, item) is { } found ? (found.Cell, found.Target) : null;

    /// <summary>ARC-AI-1309 (0x4626B0): the first item of the kind in the pile's list, or null. Its object flag OFF is an item in flight (hidden until it lands).</summary>
    [OriginalFunction(0x4626B0, OriginalFunctionScope.Partial)]
    public AiGroundItem? FirstInPile(string pile, AiGroundKind kind)
    {
        if (GroundView() is not { } view) return null;
        if (view.JunkPiles.FirstOrDefault(listed => listed.Entity == pile) is not { } found)
        {
            // ARC-AI-1486: a junk pile of the map's data: the first item of the kind in its container's current list (an item of a pile is never in flight).
            if (_scene.Combat.MapJunkPileNamed(_session, _scene.RuntimeMap, pile) is not { } named) return null;
            // Only the seed's items: one a barter sold into the container is not the transfer's (the pile's pickup names seed items).
            foreach (var item in _session.QueryHolderItems(pile) ?? [])
                if (KindOf(item.Facts.Kind.Type) == kind && named.SeedItems.Contains(item.Entity, StringComparer.Ordinal)) return Describe(_searchNpc, view, item, 0, off: false);
            return null;
        }
        foreach (var item in found.Items)
            if (KindOf(item.Facts.Kind.Type) == kind)
                return Describe(_searchNpc, view, item, 0, off: ItemOff(item.Entity));
        return null;
    }

    // The search's tests on one item: the item flags (0x57), worth (0x5B: 0 is worthless, 0x461F60) and whether the NPC may take it (0x466510; asked only while a search runs for an NPC). An item with no fields to read counts as worthless.
    private AiGroundItem Describe(string? npc, SessionMapInventory view, SessionInventoryItem item, long distance, bool off)
    {
        var fields = _scene.Combat.GroundItemFields(_scene.RuntimeMap, item)?.Item;
        if (fields is null) return new(item.Entity, distance, Worthless: true, Off: off);
        return new(item.Entity, distance, NoDisplay: (fields.Flags & NoDisplayFlag) != 0, NoNpcPickup: (fields.Flags & NoNpcPickupFlag) != 0, Worthless: fields.Worth == 0,
            CannotTake: npc is not null && NpcInsertVerdict(npc, view, item) != SessionInsertVerdict.Accepted, Off: off);
    }

    // The insert check for an NPC (0x466510): the item's weight with the NPC as holder against its carry weight, then the room in its grid (ARC-AI-1654); the PC-only Technophobia does not apply to an NPC's list.
    private SessionInsertVerdict NpcInsertVerdict(string npc, SessionMapInventory? view, SessionInventoryItem item)
    {
        if (Build(npc) is not { } build || LifeState(npc) is not { } state) return SessionInsertVerdict.TooHeavy;
        var aptitude = Math.Clamp(build.EffectiveStat(CharacterStat.MagickTechAptitude, state), EquipmentLimits.MinAptitude, EquipmentLimits.MaxAptitude);
        var carry = build.EffectiveStat(CharacterStat.CarryWeight, state);
        // The NPC's whole list: the shown map's view lists it, and a companion carried here from another map lists on its home map's seed, which the session's holder query finds (ARC-AI-1486).
        var held = view?.Holders.FirstOrDefault(listed => listed.Entity == npc)?.Items ?? _session.QueryHolderItems(npc) ?? [];
        var total = CarryRules.TotalWeight(held.Select(CarriedBy), CarryHolder.Critter, aptitude);
        if (!CarryRules.Fits(total, CarryRules.ItemWeight(CarriedBy(item), CarryHolder.Critter, aptitude), carry)) return SessionInsertVerdict.TooHeavy;
        // ARC-AI-1654: then the room (code 2): a free place in the NPC's 10 x 12 grid, an ammunition stack of the same prototype needing none.
        return _session.QueryHolderRoom(npc, item) ? SessionInsertVerdict.Accepted : SessionInsertVerdict.NoRoom;
    }

    // An item as the weight reads it: the session's tracked weight facts first, else its listed facts; ammunition by its units.
    private CarriedItem CarriedBy(SessionInventoryItem item)
    {
        var weight = _session.QueryItemWeight(item.Entity) ?? item.Facts.WeightFacts;
        var units = item.Facts.Kind.Type == SessionItemType.Ammo ? _session.QueryAmmo(item.Entity)?.Units ?? item.Facts.Quantity ?? 0 : 0;
        return new((int)item.Facts.Kind.Type, weight.Weight, weight.MagicWeight, weight.Complexity, units);
    }

    /// <summary>ARC-AI-1309 (0x4356C0): the pickup starter for the search: goal 15 with the item through <see cref="AiExecutorRules.StartPickup"/> (the start gate with the item-use cost, the pickup switch, the level-3
    /// interrupt). True when the run is in the table.</summary>
    public bool StartPickup(string npc, string item)
    {
        if (!Walks(npc) || LyingCell(item) is not { } cell) return false;
        var added = _pickupItems.TryAdd(item, cell);
        var current = RunOf(npc);
        if (Started(npc, current, AiExecutorRules.StartPickup(this, npc, item, current))) return true;
        if (added) _pickupItems.Remove(item);
        return false;
    }

    /// <summary>ARC-AI-1486 (0x4681A0, global 0x5E8800, read by the starter 0x4356C0): the pickup switch of the item system, 1 from the item module's start. The window that opens in loot or See Contents mode on a
    /// container lowers it by one (0x5727B0 → 0x468190; a window on a critter's body or on the hero's own list does not: the open tests the shown object's type 2) and its close raises it again (0x573440 → 0x468180).
    /// The scene's <see cref="AreaAiScene.LootWindowOpen"/> says whether such a window is open: then no NPC starts a pickup (<see cref="AiStartRefusal.PickupsOff"/>) and the goal's end of one already under way fails
    /// (0x42AA70 returns 0 when the switch reads 0, before the action point is charged: <see cref="PickUp"/>).</summary>
    public bool NpcPickupsOn => _scene.LootWindowOpen?.Invoke() != true;

    private CellCoord? PickupCell(string id) => _pickupItems.TryGetValue(id, out var cell) ? cell : null;

    // The cell the item lies on now: the shown map's loose item's, or the cell of the junk pile that holds it; null when it lies nowhere (one query of the map's inventory, so not for every step).
    private CellCoord? LyingCell(string id)
    {
        if (_session.QueryGroundItem(id) is { } lying && lying.Map.RuntimeMap == _scene.RuntimeMap) return lying.Cell;
        return _session.QueryJunkPiles().FirstOrDefault(pile => pile.Map.RuntimeMap == _scene.RuntimeMap && pile.Items.Any(item => item.Entity == id))?.Cell ?? MapPileOf(id)?.Cell;
    }

    /// <summary>
    /// ARC-AI-1309 (goal 16 → 0x42A9B0 → 0x42AA70): the pickup. A target with object flags OFF or DESTROYED (0x13 &amp; 3) is not taken and neither is an item whose holder is no junk pile (an item that lies nowhere: no action point is charged). In a round the actor's 1 action point is spent first (0x4B7CD0: none left ends its subturn and the pickup fails); then the item moves
    /// into the NPC's list (<see cref="GameSessionOwner.PickUpItemByNpc"/>, the insert check the carry weight of 0x466510) and a <see cref="AreaAiNoticeKind.PickUp"/> notice tells the viewer. The goal's end reads the item switch first (0x4681A0: a loot window open on a container, <see cref="NpcPickupsOn"/>, fails it before any action point), and an item of a standing junk pile of the map's data leaves its container (ARC-AI-1486, <see cref="MapPileOf"/>). ARC-AI-1655: the item's get (before the insert check, skip default leaves the item) and insert-item scripts run with the NPC as the triggerer and their lines go out in the notice. NOT READY: the item sound of a local picker (0x4F0BF0) and the network round's flag.
    /// </summary>
    [OriginalFunction(0x42AA70, OriginalFunctionScope.Partial)]
    public void PickUp(string id, string item)
    {
        if (!Walks(id)) return;
        // ARC-AI-1486 (0x42AA70 reads the switch 0x4681A0 first): a loot window opened on a container since the walk started fails the pickup before anything is charged or moved.
        if (!NpcPickupsOn)
        {
            _pickupItems.Remove(item);
            Trace(id).Item = $"pickup {item}: loot window open";
            return;
        }
        // 0x42A9B0 and 0x42AA70 refuse an item that is gone or held by something that is no junk pile before they charge the action point.
        if (LyingCell(item) is null)
        {
            _pickupItems.Remove(item);
            Trace(id).Item = $"pickup {item}: not there";
            return;
        }
        if (InRound && RoundActor == id)
        {
            if (ActionPoints(id) < 1) { EndSubturn(id); Trace(id).Item = $"pickup {item}: no action point left"; return; }
            Consume(id, 1);
        }
        var view = GroundView();
        // ARC-AI-1486: an item of a junk pile of the map's data leaves its container (the pile is named by its target).
        // ARC-AI-1655: the item's get and insert-item scripts run through the shown map's item script host, with the NPC as the triggerer.
        var result = _session.PickUpItemByNpc(_session.Version, id, item, moving => NpcInsertVerdict(id, view, moving), MapPileOf(item)?.Target, _scene.ItemScripts?.Invoke());
        Trace(id).Item = $"pickup {item} {result.Disposition} {result.Failure}" + (result.DeclinedBy is { } point ? $" declined by point {point}" : "");
        // A get script that answered skip default leaves the item where it lies (its flag writes and lines still happen, so the disposition can be Changed).
        var picked = result.Disposition == GameSessionDisposition.Changed && result.DeclinedBy is null;
        if (picked || LyingCell(item) is null) _pickupItems.Remove(item);
        if (picked) _notices.Add(new(AreaAiNoticeKind.PickUp, id, null) { Item = result.Item ?? item, Messages = result.Messages });
        else if (result.DeclinedBy is not null) _notices.Add(new(AreaAiNoticeKind.PickUpDeclined, id, null) { Item = item, Messages = result.Messages });
    }
}
