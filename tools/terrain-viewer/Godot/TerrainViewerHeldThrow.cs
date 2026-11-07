using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
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
    /// The press of the held item at <paramref name="tile"/> (TT2, TT6; ARC-DEV-182, MIG13 S12b): the refusals that leave the item on the cursor (a companion on the tile, a boomerang still away, a hero that cannot act), then
    /// the throw goal 37 of the goal table starts through <see cref="ThrowRequest"/> (no approach: the hero throws from where it stands). The goal's nodes begin the throw (the points, the throw art, the turn), and
    /// at the action frame <see cref="HeroThrowItem"/> throws through the session (<see cref="PerformHeroThrow"/>). Without a hero-runs host (the synthetic fixtures) the throw is made at once. True when the throw started or was made.
    /// </summary>
    internal bool ThrowGameCursorItem(CellCoord tile)
    {
        if (_gameCursorItem is not { } held || _areaSession is not { } owner || _worldActor.Session is not AreaGridSession grid || _combatHost is null) return false;
        var struck = HeldThrowStruck(owner, HeroCellNow(grid), tile);
        if (struck is { Companion: true })
        {
            SetHeldThrowNotice("throwing at a companion is NOT READY (the item stays on the cursor)");
            RefreshAreaStatus();
            return false;
        }
        if (_inventoryHost!.GridMoveItem(owner, held.Item, _areaKitGridMove) is null) { PutBackGameCursorItem("its facts do not read here"); return false; }
        // The session's refusals a throw can meet after the lift, checked before the round's points are charged: a wielded boomerang
        // still away, a hero that cannot act.
        if (owner.QueryBoomerangInFlight() && owner.QueryEquipment().Worn.Any(w => w.Slot == Arcanum.Simulation.Equipment.EquipmentSlot.Weapon && w.Entity == held.Item))
        { PutBackGameCursorItem("it is still in flight"); return false; }
        if (owner.QueryHeroLife() is not { Status.IsActive: true }) { PutBackGameCursorItem("the hero cannot act"); return false; }
        CancelAreaAttack("a throw replaced it");
        CancelTalkApproach("a throw replaced it"); CancelAreaUseApproach("a throw replaced it"); CancelInventoryApproach("a throw replaced it");
        // The throw goal replaces the walk (the hero throws from the cell it stands on).
        StopWorldRoute("ROUTE_REPLACED_BY_THROW");
        if (HeroRunsHost() is null)
        {
            if (!ChargeHeroAction(owner.QueryHeroThrowCost(), "throw")) { SetHeldThrowNotice(_turnBasedNotice ?? "the throw was refused"); RefreshAreaStatus(); return false; }
            _gameCursorItem = null;
            return PerformHeroThrow(held, tile) is not null;
        }
        var result = HeroRequests.Apply(new ThrowRequest(held.Item, tile));
        if (result.Outcome != HeroRequestOutcome.Done) { SetHeldThrowNotice("the throw was refused"); RefreshAreaStatus(); return false; }
        SetHeldThrowNotice($"{held.Name}: the throw starts");
        return true;
    }

    // The throw run in progress: the item, its tile and name while the hero's swing plays (the cursor is empty from the swing's start, TT2).
    private sealed record HeroThrowSwing(GameCursorItem Held, CellCoord Tile, AreaAttackPlayer Player, ActorDiagnosticAction Action, int Octant);
    private HeroThrowSwing? _throwSwing;

    /// <summary>The throw swing plays (the combat-mode exit gate's attempt-attack test reads it, with the attack's).</summary>
    private bool ThrowPlaying => _throwSwing?.Player is { Playing: true };

    /// <summary>
    /// The throw goal's first node (ARC-DEV-182, 0x42BD40 → 0x42C390 → 0x42CB10): the points of the throw (a turn-based round charges them; TT4 the Original setting nothing), the throw art (animation 14) and the turn to the tile.
    /// The cursor empties here (TT2). Null when the throw cannot begin: the item stays on the cursor and the status line says why.
    /// </summary>
    internal Arcanum.Simulation.Ai.AiAttackSwing? HeroBeginThrow(string item, CellCoord tile)
    {
        if (_gameCursorItem is not { } held || held.Item != item || _areaSession is not { } owner || _worldActor.Session is not AreaGridSession session) return null;
        var animation = Arcanum.Simulation.Equipment.WeaponArtRules.ThrowAnimation;
        if (HeroAttackArt.Clip(_worldActor.Profile, animation) is not var (action, art, rateTable))
        { SetHeldThrowNotice($"not thrown: the hero's body has no ART for animation {animation}"); RefreshAreaStatus(); return null; }
        if (!ChargeHeroAction(owner.QueryHeroThrowCost(), "throw")) { SetHeldThrowNotice(_turnBasedNotice ?? "the throw was refused"); RefreshAreaStatus(); return null; }
        var attack = owner.QueryHeroAttack();
        var speed = Math.Clamp(attack?.Speed ?? RealTimeCombatRules.MaxStat / 2, 1, RealTimeCombatRules.MaxStat);
        var weaponSpeed = attack?.WeaponSpeed ?? 0;
        var weapon = (AttackArtWeapon)Arcanum.Simulation.Equipment.WeaponArtRules.Weapon(_worldActor.Profile.Attack!.Selector);
        var rate = RealTimeCombatRules.AttackFramesPerSecond(rateTable, weapon, speed, art.RawFramesPerSecond);
        var clip = new AttackClip(art.FramesPerGroup, art.ActionFrame, RealTimeCombatRules.AttackFramePauseMs(rate, weaponSpeed), weapon);
        var target = session.Chart.ToChart(new(tile.X, tile.Y));
        var octant = AreaCombatHost.Facing(session.State.CommittedCell, target) is { } facing ? DiagnosticGridActor.Octant(facing)
            : DiagnosticGridActor.RenderState(session.State).Facing;
        _gameCursorItem = null;
        _throwSwing = new HeroThrowSwing(held, tile, new AreaAttackPlayer(clip, WorldMilliseconds()), action, octant);
        _worldActor.AttackPose = (action, octant, 0);
        GD.Print($"ARC-DEV-182 throw-start item={item} tile={tile.X},{tile.Y} animation={animation} drawn={action} frames={clip.Frames} action={clip.ActionFrame} pause={clip.PauseMs}");
        PublishWorldSelection();
        var moments = RealTimeCombatRules.Schedule(clip);
        var hit = moments.FirstOrDefault(m => m.Kind == AttackUpdateKind.Hit);
        var end = moments[^1];
        var actionMs = (int)Math.Max(1, hit.Kind == AttackUpdateKind.Hit ? hit.AtMs : end.AtMs);
        return new Arcanum.Simulation.Ai.AiAttackSwing(actionMs, (int)Math.Max(1, end.AtMs - actionMs));
    }

    /// <summary>The throw goal's action frame (0x42AB90 → 0x4B3C00): the item is thrown through the session. False when the swing is gone or the session refuses.</summary>
    internal bool HeroThrowItem(string item, CellCoord tile)
    {
        if (_throwSwing is not { } swing || swing.Held.Item != item) return false;
        return PerformHeroThrow(swing.Held, tile) is not null;
    }

    /// <summary>The throw goal ends (it threw, or refused, or was interrupted): the pose is cleared.</summary>
    internal void HeroThrowEnds(string item, Arcanum.Simulation.Ai.AiExecutorReason reason)
    {
        if (_throwSwing is { } swing && swing.Held.Item == item) { _throwSwing = null; _worldActor.AttackPose = null; PublishWorldSelection(); }
        if (reason == Arcanum.Simulation.Ai.AiExecutorReason.ActionRefused) { SetHeldThrowNotice("the throw was stopped (the item stays in the pack)"); RefreshAreaStatus(); }
    }

    /// <summary>Per frame: the throw swing's pose (the throw itself is the goal's); the guards of the attack apply (a window, the hero's state, the run gone).</summary>
    private void ProcessHeroThrow()
    {
        if (_throwSwing is not { } swing) return;
        if (_areaSession is not { } owner || _worldActor.Session is not AreaGridSession || !ReferenceEquals(swing.Held.Owner, owner) || swing.Held.Map != CurrentAreaMap || WorldModal || ConversationOpen
            || owner.QueryHeroLife() is not { Status.IsActive: true } || HeroRunsHost() is { ThrowRun: null })
        {
            EndHeroThrow("the throw was interrupted");
            return;
        }
        var changed = false;
        foreach (var (_, update) in swing.Player.Advance(WorldMilliseconds(), () => owner.QueryHeroLife() is { Status.IsActive: true }))
        {
            changed = true;
            if (update.Kind == AttackUpdateKind.Ended || !swing.Player.Playing) break;
        }
        if (_throwSwing == swing && swing.Player.Playing && changed) { _worldActor.AttackPose = (swing.Action, swing.Octant, swing.Player.Playback.Frame); PublishWorldSelection(); }
        else if (_throwSwing == swing && !swing.Player.Playing) { _worldActor.AttackPose = null; PublishWorldSelection(); }
    }

    /// <summary>The throw run is cut (a cancel): the run ends and the pose goes.</summary>
    private void EndHeroThrow(string why)
    {
        try { _heroRunsHost?.EndThrow("ROUTE_STOPPED_BY_THROW_END"); }
        catch (ObjectDisposedException) { /* a replaced session */ }
        if (_throwSwing is not null) { _throwSwing = null; _worldActor.AttackPose = null; GD.Print("ARC-DEV-182 throw-cancel " + why); PublishWorldSelection(); }
    }

    /// <summary>
    /// The session's throw of <paramref name="held"/> at <paramref name="tile"/> (TT2 → <see cref="GameSessionOwner.ThrowHeroItem"/>): the struck critter is the
    /// line of fire's living NPC blocker, else the living NPC on the tile (TW4); a companion there is NOT READY (the item stays). A dead struck critter's refusal throws again at the location. On success each shot flies.
    /// </summary>
    private SessionHeroThrowResult? PerformHeroThrow(GameCursorItem held, CellCoord tile)
    {
        if (_areaSession is not { } owner || _worldActor.Session is not AreaGridSession grid || _combatHost is null) return null;
        var from = HeroCellNow(grid);
        var struck = HeldThrowStruck(owner, from, tile);
        if (struck is { Companion: true })
        {
            SetHeldThrowNotice("throwing at a companion is NOT READY (the item stays in the pack)");
            RefreshAreaStatus();
            return null;
        }
        if (_inventoryHost!.GridMoveItem(owner, held.Item, _areaKitGridMove) is not { } facts) { SetHeldThrowNotice("not thrown: its facts do not read here"); return null; }
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
            SetHeldThrowNotice(AreaHeldThrow.Refusal(result.Failure, held.Name) + " (it stays in the pack)");
            RefreshAreaStatus();
            return null;
        }
        if (result.Disposition == GameSessionDisposition.NoChange) { SetHeldThrowNotice("the hero cannot act (the item stays in the pack)"); RefreshAreaStatus(); return null; }
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
