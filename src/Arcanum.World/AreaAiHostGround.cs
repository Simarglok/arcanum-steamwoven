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

    // The items an NPC's pickup goal is on its way to: Tile answers their cell (the item is no critter the host walks).
    private readonly HashSet<string> _pickupItems = new(StringComparer.Ordinal);

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

    private static bool InSquare(CellCoord center, CellCoord cell, int halfSize) =>
        Math.Abs((long)cell.X - center.X) <= halfSize && Math.Abs((long)cell.Y - center.Y) <= halfSize;

    /// <summary>ARC-AI-1309 (0x4AE4E0, 0x4A8F90): the loose items of the kind in the square of half-size <paramref name="halfSize"/> around the NPC, in the session's ground order (the original's object list order was not read):
    /// a map seed's loose item, a spilled or dropped one, the hero's placed ones; none in a junk pile or in flight. Each with the item flags and worth the search tests (<see cref="Describe"/>).</summary>
    public IReadOnlyList<AiGroundItem> LooseItems(string npc, AiGroundKind kind, int halfSize)
    {
        if (GroundView() is not { } view || Tile(npc) is not { } at) return [];
        var found = new List<AiGroundItem>();
        foreach (var ground in view.Ground)
            if (KindOf(ground.Item.Facts.Kind.Type) == kind && InSquare(at, ground.Cell, halfSize) && !_session.QueryItemInFlight(ground.Item.Entity))
                found.Add(Describe(npc, view, ground.Item, AiExecutorRules.Distance(at, ground.Cell), off: false));
        return found;
    }

    /// <summary>ARC-AI-1309: the junk piles in the square, in the order the session made them (the containers named 3023; the map's own piles are not read here).</summary>
    public IReadOnlyList<string> JunkPiles(string npc, int halfSize) =>
        GroundView() is { } view && Tile(npc) is { } at ? view.JunkPiles.Where(pile => InSquare(at, pile.Cell, halfSize)).Select(pile => pile.Entity).ToArray() : [];

    /// <summary>ARC-AI-1309 (0x4626B0): the first item of the kind in the pile's list, or null. Its object flag OFF is an item in flight (hidden until it lands).</summary>
    [OriginalFunction(0x4626B0, OriginalFunctionScope.Partial)]
    public AiGroundItem? FirstInPile(string pile, AiGroundKind kind)
    {
        if (GroundView() is not { } view || view.JunkPiles.FirstOrDefault(listed => listed.Entity == pile) is not { } found) return null;
        foreach (var item in found.Items)
            if (KindOf(item.Facts.Kind.Type) == kind)
                return Describe(_searchNpc, view, item, 0, off: _session.QueryItemInFlight(item.Entity));
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

    // The insert check for an NPC (0x466510): the item's weight with the NPC as holder against its carry weight (the room test and the PC-only Technophobia do not apply to an NPC's list).
    private SessionInsertVerdict NpcInsertVerdict(string npc, SessionMapInventory? view, SessionInventoryItem item)
    {
        if (Build(npc) is not { } build || LifeState(npc) is not { } state) return SessionInsertVerdict.TooHeavy;
        var aptitude = Math.Clamp(build.EffectiveStat(CharacterStat.MagickTechAptitude, state), EquipmentLimits.MinAptitude, EquipmentLimits.MaxAptitude);
        var carry = build.EffectiveStat(CharacterStat.CarryWeight, state);
        var held = view?.Holders.FirstOrDefault(listed => listed.Entity == npc)?.Items ?? [];
        var total = CarryRules.TotalWeight(held.Select(CarriedBy), CarryHolder.Critter, aptitude);
        return CarryRules.Fits(total, CarryRules.ItemWeight(CarriedBy(item), CarryHolder.Critter, aptitude), carry) ? SessionInsertVerdict.Accepted : SessionInsertVerdict.TooHeavy;
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
        if (!Walks(npc)) return false;
        _pickupItems.Add(item);
        var current = RunOf(npc);
        if (Started(npc, current, AiExecutorRules.StartPickup(this, npc, item, current))) return true;
        _pickupItems.Remove(item);
        return false;
    }

    /// <summary>The pickup switch of the item system (0x4681A0, global 0x5E8800): 1 from the item module's start, lowered only while an inventory window is open in loot mode on a container (0x468190, ID6), which this host
    /// does not know (<see cref="InterfaceHoldsObject"/> answers false as well): always on.</summary>
    public bool NpcPickupsOn => true;

    // The cell the pickup goal's item lies on: the shown map's loose item's, or the cell of the junk pile that holds it; null (the goal ends, "object gone") once it lies nowhere.
    private CellCoord? PickupCell(string id)
    {
        if (!_pickupItems.Contains(id)) return null;
        if (_session.QueryGroundItem(id) is { } lying && lying.Map.RuntimeMap == _scene.RuntimeMap) return lying.Cell;
        if (_session.QueryJunkPiles().FirstOrDefault(pile => pile.Map.RuntimeMap == _scene.RuntimeMap && pile.Items.Any(item => item.Entity == id)) is { } pile) return pile.Cell;
        _pickupItems.Remove(id);
        return null;
    }

    /// <summary>
    /// ARC-AI-1309 (goal 16 → 0x42A9B0 → 0x42AA70): the pickup. A target with object flags OFF or DESTROYED (0x13 &amp; 3) is not taken (a refusal of the session: an item that lies nowhere); an item whose holder is not a
    /// junk pile is not taken either (the session takes an item of the ground or of a pile). In a round the actor's 1 action point is spent first (0x4B7CD0: none left ends its subturn and the pickup fails); then the item moves
    /// into the NPC's list (<see cref="GameSessionOwner.PickUpItemByNpc"/>, the insert check the carry weight of 0x466510) and a <see cref="AreaAiNoticeKind.PickUp"/> notice tells the viewer. NOT READY: the item's get and insert-item
    /// scripts (an item with one is refused), the item sound of a local picker (0x4F0BF0) and the network round's flag.
    /// </summary>
    [OriginalFunction(0x42AA70, OriginalFunctionScope.Partial)]
    public void PickUp(string id, string item)
    {
        _pickupItems.Remove(item);
        if (!Walks(id)) return;
        if (InRound && RoundActor == id)
        {
            if (ActionPoints(id) < 1) { EndSubturn(id); Trace(id).Item = $"pickup {item}: no action point left"; return; }
            Consume(id, 1);
        }
        var view = GroundView();
        var result = _session.PickUpItemByNpc(_session.Version, id, item, moving => NpcInsertVerdict(id, view, moving));
        Trace(id).Item = $"pickup {item} {result.Disposition} {result.Failure}";
        if (result.Disposition == GameSessionDisposition.Changed) _notices.Add(new(AreaAiNoticeKind.PickUp, id, null) { Item = result.Item ?? item });
    }
}
