using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-063: the item held on the game cursor and its throw (docs/rendering/terrain-viewer.md#inventory-throw-from-the-game-cursor-arc-ui-063;
// ledger IW18, TT1, TT2, TT6, TT7, CU7). A left release of a held item in the own inventory window's round view, or on its use icon for
// an item whose use is a throw, lifts it onto the game cursor and closes the window; a left press in the game view throws it at the
// pressed tile (an object's tile when one is under the pointer) through the session (ARC-COMBAT-023), and the item flies on the
// projectile host of the "default throws" dagger. Steamwoven keeps the item in the hero's pack or slot until the throw (the original
// removes it at the lift), so putting it back only empties the cursor.
public partial class TerrainViewerApp
{
    private sealed record GameCursorItem(string Item, string Name, GameSessionOwner Owner, int Map);

    private GameCursorItem? _gameCursorItem;
    private string? _heldThrowNotice;
    // When the notice was set: it shows while an item is held and for a while after.
    private ulong _heldThrowNoticeAt;
    private const ulong HeldThrowNoticeMs = 8000;
    private SessionHeroThrowResult? _lastHeldThrowForSelfTest;
    internal string? GameCursorItemForSelfTest => _gameCursorItem?.Item;
    internal SessionHeroThrowResult? LastHeldThrowForSelfTest => _lastHeldThrowForSelfTest;
    internal string? HeldThrowNoticeForSelfTest => _heldThrowNotice;

    /// <summary>
    /// The map drop with flag 4 of a held item of the own window (IW18; the use icon's for <c>OIF_USE_IS_THROW</c>, IW6): the lift's
    /// checks (<see cref="AreaHeldThrow.Lift"/>: the original's remove check refuses a no-drop item, the rest is NOT READY) leave a refused
    /// item where it was with the reason in the window; otherwise the window closes and the item is on the game cursor.
    /// </summary>
    private void LiftToGameCursor(string item, string source)
    {
        if (!InventoryOpen || !InventoryActive || _inventoryPanel!.Holder is not null) return;
        var session = _areaSession!;
        var name = _inventoryHost!.ItemName(session, item, _hoverNames?.Catalog);
        var worn = session.QueryEquipment().Worn.Any(entry => entry.Entity == item);
        string message;
        if (session.QueryHero() is null) message = "not thrown: needs a hero (start with --hero)";
        else if (_inventoryHost.GridMoveItem(session, item, _areaKitGridMove) is not { } facts) message = $"not thrown: {name}'s facts do not read here";
        else if (AreaHeldThrow.Lift(facts, worn) is var refused && refused != GameSessionFailure.None) message = AreaHeldThrow.Refusal(refused, name);
        else
        {
            _gameCursorItem = new(item, name, session, CurrentAreaMap);
            var range = session.QueryHeroThrowRange(item);
            SetHeldThrowNotice($"{name} on the cursor (thrown range {range?.ToString() ?? "-"}): left-click a tile to throw it, right-click puts it back");
            GD.Print($"ARC-UI-063 lift item={item} source={source} worn={worn} range={range?.ToString() ?? "-"}");
            EndInventory();
            RefreshReadyStatus();
            return;
        }
        GD.Print($"ARC-UI-063 lift item={item} source={source} refused: {message}");
        _inventoryStatus = message;
        _inventoryPanel.Render(InventoryView(null), message);
        RefreshReadyStatus();
    }

    /// <summary>The held item goes back (0x575770): the cursor is empty and the item stays in the hero's pack or slot.</summary>
    private void PutBackGameCursorItem(string why)
    {
        if (_gameCursorItem is not { } held) return;
        _gameCursorItem = null;
        SetHeldThrowNotice($"{held.Name} went back ({why})");
        GD.Print($"ARC-UI-063 put-back item={held.Item} why={why}");
        RefreshReadyStatus();
    }

    /// <summary>Per frame: a held item goes back when its map or session is no longer shown, a window opens, or the hero no longer holds
    /// it (PROJECT: the original keeps the item on the cursor; here it never left the hero).</summary>
    private void ProcessGameCursorItem()
    {
        if (_gameCursorItem is not { } held) return;
        if (!InventoryActive || !ReferenceEquals(held.Owner, _areaSession) || held.Map != CurrentAreaMap) PutBackGameCursorItem("the map changed");
        else if (WorldModal) PutBackGameCursorItem("a window opened");
        else if (_areaSession!.QueryHeroThrowRange(held.Item) is null) PutBackGameCursorItem("the hero no longer holds it");
    }

    /// <summary>ARC-UI-063 (TT7): a right release in the game view puts a held item back (0x54E228, after the release's own action).</summary>
    private void GameCursorRightRelease()
    {
        if (WorldClickInputOpen) PutBackGameCursorItem("a right click");
    }

    /// <summary>
    /// A guarded left press in the game view with an item on the cursor (TT2, TT6): an inactive hero puts it back (0x575770; PROJECT: the
    /// original's press on an object leaves it held then); otherwise it is thrown at the pressed tile, or at the tile of the object under
    /// the pointer (0x54ED30 throws at the object's location).
    /// </summary>
    private void GameCursorPress(Vector2 screen)
    {
        if (_worldActor.Session is not AreaGridSession grid || _canvas is null) return;
        if (_areaSession!.QueryHeroLife() is not { Status.IsActive: true }) { PutBackGameCursorItem("the hero cannot act"); return; }
        CellCoord tile;
        if (AreaObjectAtScreen(screen) is { } key && _readyArea?.Snapshot.Objects.FirstOrDefault(row => row.Key == key) is { } row
            && (row.Type is 15 or 16 && !IsAreaCompanion(row.Key) && row.WorldX is not null && row.WorldY is not null ? NpcCell(row) : LiveCell(_areaSession, row)) is { } at)
            tile = at;
        else
        {
            var chart = _canvas.GetGlobalTransformWithCanvas().AffineInverse() * screen;
            if (AreaCellPicking.Pick(new(chart.X, chart.Y)) is not { } cell) return;
            var world = grid.Chart.ToWorld(cell);
            tile = new(checked((int)world.X), checked((int)world.Y));
        }
        ThrowGameCursorItem(tile);
    }

    /// <summary>
    /// The throw of the held item at <paramref name="tile"/> (TT2 → <see cref="GameSessionOwner.ThrowHeroItem"/>): the struck critter is the
    /// line of fire's living NPC blocker, else the living NPC on the tile (TW4); a companion there is NOT READY (the item stays held). In
    /// a turn-based round the throw first charges <see cref="GameSessionOwner.QueryHeroThrowCost"/> (TT4: the Original setting nothing).
    /// A dead struck critter's refusal throws again at the location. On success the cursor is empty and each shot flies.
    /// </summary>
    internal SessionHeroThrowResult? ThrowGameCursorItem(CellCoord tile)
    {
        if (_gameCursorItem is not { } held || _areaSession is not { } owner || _worldActor.Session is not AreaGridSession grid || _combatHost is null) return null;
        var from = HeroCellNow(grid);
        var struck = HeldThrowStruck(owner, from, tile);
        if (struck is { Companion: true })
        {
            SetHeldThrowNotice("throwing at a companion is NOT READY (the item stays on the cursor)");
            RefreshAreaStatus();
            return null;
        }
        if (_inventoryHost!.GridMoveItem(owner, held.Item, _areaKitGridMove) is not { } facts) { PutBackGameCursorItem("its facts do not read here"); return null; }
        // The session's refusals a throw can meet after the lift, checked before the round's points are charged: a wielded boomerang
        // still away, a hero that cannot act.
        if (owner.QueryBoomerangInFlight() && owner.QueryEquipment().Worn.Any(w => w.Slot == Arcanum.Simulation.Equipment.EquipmentSlot.Weapon && w.Entity == held.Item))
        { PutBackGameCursorItem("it is still in flight"); return null; }
        if (owner.QueryHeroLife() is not { Status.IsActive: true }) { PutBackGameCursorItem("the hero cannot act"); return null; }
        if (!ChargeHeroAction(owner.QueryHeroThrowCost(), "throw")) { SetHeldThrowNotice(_turnBasedNotice ?? "the throw was refused"); RefreshAreaStatus(); return null; }
        CancelAreaAttack("a throw replaced it");
        CancelTalkApproach("a throw replaced it"); CancelAreaUseApproach("a throw replaced it"); CancelInventoryApproach("a throw replaced it");
        // The throw goal replaces the walk (the hero throws from the cell it stands on).
        StopWorldRoute("ROUTE_REPLACED_BY_THROW");
        PlaceHeroForDrop();
        var range = owner.QueryHeroThrowRange(held.Item) ?? 1;
        var wearable = _inventoryHost.Wearable(owner, held.Item, _areaKitWear);
        var request = new SessionHeroThrow(held.Item, facts, tile)
        {
            Combat = wearable?.Item, Throwable = wearable?.Throwable == true,
            HeroWorn = AreaCombatHost.HeroWorn(owner, _areaKitDurability), DropMap = AreaDropMapNow(), ItemTriggers = ItemTriggersNow(),
        };
        var aimed = struck?.Goal;
        if (aimed is not null)
        {
            var cell = NpcCell(aimed.Row);
            request = request with
            {
                Struck = aimed.Combat, StruckCell = cell, StruckWorn = _combatHost.WornItems(owner, aimed.Entity),
                LineOfFire = ShotLine(TraceLineOfFire(from, aimed.Row), aimed.Row),
            };
        }
        var result = owner.ThrowHeroItem(owner.Version, request);
        // A struck critter that died meanwhile refuses the attack: the original's press throws at the location anyway.
        if (aimed is not null && result.Disposition == GameSessionDisposition.NoChange && owner.QueryHeroLife() is { Status.IsActive: true })
        {
            aimed = null;
            result = owner.ThrowHeroItem(owner.Version, request with { Struck = null, StruckCell = null, StruckWorn = null, LineOfFire = null });
        }
        _lastHeldThrowForSelfTest = result;
        GD.Print($"ARC-UI-063 throw item={held.Item} tile={tile.X},{tile.Y} struck={aimed?.Entity ?? "none"} disposition={result.Disposition}"
            + $" failure={result.Failure} outcome={result.Attack?.Resolution?.Outcome.ToString() ?? "-"} shots={result.Shots.Count}"
            + $" destination={(result.Destination is { } d ? $"{d.X},{d.Y}" : "-")} cost={owner.QueryHeroThrowCost()}");
        if (result.Disposition == GameSessionDisposition.Rejected)
        {
            SetHeldThrowNotice(AreaHeldThrow.Refusal(result.Failure, held.Name) + " (it went back)");
            _gameCursorItem = null;
            RefreshAreaStatus();
            return result;
        }
        if (result.Disposition == GameSessionDisposition.NoChange) { PutBackGameCursorItem("the hero cannot act"); return result; }
        _gameCursorItem = null;
        if (aimed is not null && result.Attack?.Resolution?.Refusal == AttackRefusal.None) _aiHost?.HeroAttacked(aimed.Entity);
        var (art, unfaced) = HeldThrowArt(held.Item, wearable);
        var hit = result.Attack?.Resolution?.Outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit;
        foreach (var shot in result.Shots)
            FlyShot(shot, result.Destination ?? tile, art, unfaced, range, hit, aimed?.Row ?? HeroRow, aimed, null, result.Destination is not null);
        SetHeldThrowNotice($"{held.Name} thrown at {tile.X},{tile.Y}" + (aimed is null ? "" : hit ? $": it will hit {aimed.Entity}" : ": it missed"));
        // A critical miss turned on the hero at once (its result is the attack's); a strike waits for the flight.
        if (aimed is not null && result.Attack is { } attack && (result.Shots.Count == 0 || attack.Life is not null)) FinishCritterHit(aimed, attack);
        SyncAreaInventory(); RefreshAreaStatus();
        return result;
    }

    // The struck critter of a throw at the tile (TW4): the line of fire's blocker when it is a living NPC with combat values, else the
    // living one on the tile; a companion is named so the throw can refuse it.
    private (AttackGoal? Goal, bool Companion)? HeldThrowStruck(GameSessionOwner owner, CellCoord from, CellCoord tile)
    {
        if (_readyArea is not { } area) return null;
        (AttackGoal? Goal, bool Companion)? Critter(AreaObjectRecord row)
        {
            if (IsAreaCompanion(row.Key)) return (null, true);
            return _combatHost!.Target(owner, row.Key) is { Dead: false, Build.Combat: { } combat } npc ? (new AttackGoal(npc, combat, null), false) : null;
        }
        if (from != tile && AreaLineOfFireNow() is { } map && LineOfFireRules.Trace(from, 0, 0, tile, map, owner.Rules.Combat).Blocker is { } blocker)
            return blocker.Type is 15 or 16 ? Critter(blocker) : null;
        if (_areaCompanions is { } group)
            foreach (var companion in group.Companions)
                if (group.WorldCell(companion.Speaker) is var (x, y) && x == tile.X && y == tile.Y) return (null, true);
        foreach (var row in area.Snapshot.Objects)
            if (row.Type is 15 or 16 && row.WorldX is not null && row.WorldY is not null && NpcCell(row) == tile && !AreaCritterDead(owner, CurrentAreaMap, row)
                && Critter(row) is { } found) return found;
        return null;
    }

    // An NPC's cell now (one the AI walked stands on its own cell, ARC-WORLD-092).
    private CellCoord NpcCell(AreaObjectRecord row) => LiveWorldCell(row) is var (x, y) ? new(checked((int)x), checked((int)y)) : default;

    /// <summary>The thrown item's projectile art (PF2): a weapon's projectile art (field 127), else the item's own art (its current art,
    /// drawn unfaced); null when neither reads (the flight is not drawn).</summary>
    private (uint? Art, bool Unfaced) HeldThrowArt(string item, SessionWearableItem? wearable)
    {
        if (wearable?.Item.Weapon is not null)
        {
            var kit = item.StartsWith("kit:", StringComparison.Ordinal) && int.TryParse(item.AsSpan(4), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var number) && number >= 1 && _areaKitMissileArt is { } arts && number <= arts.Count ? arts[number - 1] : null;
            var missile = kit ?? InventoryItemRow(item)?.Weapon?.MissileArt;
            if (missile is { } m && m != Arcanum.Data.Combat.CombatWeaponFields.NoMissileArt) return (unchecked((uint)m), false);
        }
        foreach (var map in _inventoryHost?.Maps ?? [])
            if (map.TryGetRow(item, out var row) && Number(row, CurrentArtSlot) is { } own) return (own, true);
        return (KitArt(item), true);
    }

    private void SetHeldThrowNotice(string notice) { _heldThrowNotice = notice; _heldThrowNoticeAt = Time.GetTicksMsec(); }

    private string HeldThrowStatus() => _heldThrowNotice is null || !InventoryActive
        || _gameCursorItem is null && Time.GetTicksMsec() - _heldThrowNoticeAt > HeldThrowNoticeMs ? string.Empty : $" | throw: {_heldThrowNotice}";
}
