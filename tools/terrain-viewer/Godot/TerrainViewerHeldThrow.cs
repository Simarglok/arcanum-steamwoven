using Arcanum.Data.Area;
using Arcanum.Presentation.Hud;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
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
    // ARC-UI-086: an ammunition stack on the cursor carries the units to throw (the lift's quantity question) and the kind the split makes.
    private sealed record GameCursorItem(string Item, string Name, GameSessionOwner Owner, int Map, int? Count = null, SessionItemKind? StackKind = null);

    private GameCursorItem? _gameCursorItem;
    private string? _heldThrowNotice;
    // When the notice was set: it shows while an item is held and for a while after.
    private ulong _heldThrowNoticeAt;
    private const ulong HeldThrowNoticeMs = 8000;
    private SessionHeroThrowResult? _lastHeldThrowForSelfTest;
    internal string? GameCursorItemForSelfTest => _gameCursorItem?.Item;
    internal int? GameCursorCountForSelfTest => _gameCursorItem?.Count;
    internal SessionHeroThrowResult? LastHeldThrowForSelfTest => _lastHeldThrowForSelfTest;
    internal string? HeldThrowNoticeForSelfTest => _heldThrowNotice;

    /// <summary>
    /// The map drop with flag 4 of a held item of the own window (IW18; the use icon's for <c>OIF_USE_IS_THROW</c>, IW6): the lift's
    /// checks (<see cref="AreaHeldThrow.Lift"/>: the original's remove check refuses a no-drop item, a few script mixes are NOT READY) leave a refused
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
        else if (AreaHeldThrow.Lift(facts) is var refused && refused != GameSessionFailure.None) message = AreaHeldThrow.Refusal(refused, name);
        else if (item == SessionHeroGrid.GoldEntity)
        {
            // ARC-UI-094 (QW1): the gold pile's coins are lifted like a stack: one coin lifts at once, more ask how many.
            var coins = session.QueryGold();
            var move = new AreaQuantityMove(item, name, null, GoldPileKind(), coins) { ToCursor = true };
            if (coins < 1) message = "not thrown: the hero holds no coin";
            else
            {
                if (QuantityBar.Ask((int)SessionItemType.Gold, coins, intoLootedHolder: false, takeAll: false) is { } count) FinishStackLift(move, count);
                else AskQuantity(move);
                return;
            }
        }
        else if (facts.Kind.Type == SessionItemType.Ammo)
        {
            // ARC-UI-086 (QW1): an ammunition stack is put back and its quantity read: one unit lifts at once, more ask how many.
            if (InventoryView(null).Pc.FirstOrDefault(line => line.Entity == item) is not { StackKind: { } kind } line)
                message = $"not thrown: {name}'s stack facts do not read here";
            else
            {
                var move = new AreaQuantityMove(item, name, null, kind, line.Units) { ToCursor = true };
                if (QuantityBar.Ask((int)kind.Type, line.Units, intoLootedHolder: false, takeAll: false) is { } count) FinishStackLift(move, count);
                else AskQuantity(move);
                return;
            }
        }
        else
        {
            PutOnGameCursor(new(item, name, session, CurrentAreaMap), source, worn);
            return;
        }
        GD.Print($"ARC-UI-063 lift item={item} source={source} refused: {message}");
        _inventoryStatus = message;
        _inventoryPanel.Render(InventoryView(null), message);
        RefreshReadyStatus();
    }

    /// <summary>
    /// ARC-UI-087: an item of the hero's put on the game cursor from the hotkey bar, without the inventory window: the activation's Hold (0x54FCF0 →
    /// 0x573740, which runs the move with flag 4, the same lift as the map drop of IW18, so a pack item with the throw use is lifted by its key or click),
    /// and the right-drag of an item entry that ends off the bar (HK15: 0x573630 puts the item on the cursor and it stays there). An item held before is
    /// put back (0x575770) and the inventory window closes (0x573440). The lift's refusals are <see cref="AreaHeldThrow.Lift"/>'s; an ammunition stack
    /// goes whole (the quantity question belongs to the window's moves, QW1; the activation's own flag 0x20 asks none). Null when the item is on the
    /// cursor, else why not (the item stays where it was).
    /// </summary>
    internal string? HoldItemOnGameCursor(string item, string source)
    {
        if (_areaSession is not { } session || _inventoryHost is null || _worldActor.Session is not AreaGridSession) return "the cursor is not ready here";
        if (session.QueryHero() is null) return "not thrown: needs a hero (start with --hero)";
        var name = _inventoryHost.ItemName(session, item, _hoverNames?.Catalog);
        if (_inventoryHost.GridMoveItem(session, item, _areaKitGridMove) is not { } facts) return $"not thrown: {name}'s facts do not read here";
        var worn = session.QueryEquipment().Worn.Any(entry => entry.Entity == item);
        if (AreaHeldThrow.Lift(facts) is var refused && refused != GameSessionFailure.None) return AreaHeldThrow.Refusal(refused, name);
        int? count = null;
        if (facts.Kind.Type == SessionItemType.Ammo && (count = session.QueryAmmo(item)?.Units) is not > 0) return $"not thrown: {name} has no units";
        if (item == SessionHeroGrid.GoldEntity && (count = session.QueryGold()) is not > 0) return "not thrown: the hero holds no coin"; // ARC-UI-094: the whole pile
        if (ThrowPlaying) return "not thrown: a throw is playing";
        CancelTargetModes("an item was taken");
        PutBackGameCursorItem("another item was taken");
        if (InventoryOpen) EndInventory();
        PutOnGameCursor(new(item, name, session, CurrentAreaMap, count, count is null ? null : item == SessionHeroGrid.GoldEntity ? GoldPileKind() : facts.Kind), source, worn);
        return null;
    }

    // The item is on the game cursor: the window closes (IW18).
    private void PutOnGameCursor(GameCursorItem held, string source, bool worn)
    {
        _gameCursorItem = held;
        var range = held.Owner.QueryHeroThrowRange(held.Item, held.Count);
        var units = held.Count is { } n ? $"{n} of " : "";
        SetHeldThrowNotice($"{units}{held.Name} on the cursor (thrown range {range?.ToString() ?? "-"}): left-click a tile to throw it, right-click puts it back");
        GD.Print($"ARC-UI-063 lift item={held.Item} source={source} worn={worn} range={range?.ToString() ?? "-"}" + (held.Count is { } c ? $" count={c}" : ""));
        if (InventoryOpen) EndInventory();
        RefreshReadyStatus();
    }

    /// <summary>
    /// ARC-UI-086 (QW1, QW5, IW18): the quantity of an ammunition stack's lift is known (one unit, or the question's accepted count): 0 lifts nothing
    /// (the request returns before any move, the stack stays); otherwise the units are on the game cursor and the window closes. Steamwoven splits
    /// them off at the throw (the original at the lift), so a put back only empties the cursor.
    /// </summary>
    private void FinishStackLift(AreaQuantityMove move, int count)
    {
        var session = _areaSession!;
        if (count <= 0)
        {
            _inventoryStatus = $"nothing lifted: {move.Name} stays";
            GD.Print($"ARC-UI-086 lift item={move.Item} count=0 nothing");
            _inventoryPanel!.Render(InventoryView(null), _inventoryStatus);
            RefreshReadyStatus();
            return;
        }
        PutOnGameCursor(new(move.Item, move.Name, session, CurrentAreaMap, count, move.Kind), "round-view", false);
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
        else if (_areaSession!.QueryHeroThrowRange(held.Item, held.Count) is null) PutBackGameCursorItem("the hero no longer holds it");
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
        // ARC-COMBAT-1943 (0x54ED30): a polymorphed hero's press with an item on the cursor is refused silently (the item stays held).
        if (HeroClickRules.ThrowRefused(HeroClickSpells().Polymorphed)) return;
        CellCoord tile;
        AreaObjectKey? pressed = null;
        if (AreaObjectAtScreen(screen) is { } key && _readyArea?.Snapshot.Objects.FirstOrDefault(row => row.Key == key) is { } row
            && (row.Type is 15 or 16 && !IsAreaCompanion(row.Key) && row.WorldX is not null && row.WorldY is not null ? NpcCell(row) : LiveCell(_areaSession, row)) is { } at)
        {
            tile = at;
            // TT6 (ARC-COMBAT-2425): the press on an object stores the object as the throw goal's target.
            if (row.Type is 15 or 16) pressed = key;
        }
        else
        {
            var chart = _canvas.GetGlobalTransformWithCanvas().AffineInverse() * screen;
            if (AreaCellPicking.Pick(new(chart.X, chart.Y)) is not { } cell) return;
            var world = grid.Chart.ToWorld(cell);
            tile = new(checked((int)world.X), checked((int)world.Y));
        }
        ThrowGameCursorItem(tile, pressed);
    }

    /// <summary>
    /// The press of the held item at <paramref name="tile"/> (TT2, TT6; ARC-DEV-182, MIG13 S12b): the refusals that leave the item on the cursor (a companion on the tile, a boomerang still away, a hero that cannot act), then
    /// the throw goal 37 of the goal table starts through <see cref="ThrowRequest"/> (no approach: the hero throws from where it stands). The goal's nodes begin the throw (the points, the throw art, the turn), and
    /// at the action frame <see cref="HeroThrowItem"/> throws through the session (<see cref="PerformHeroThrow"/>). Without a hero-runs host (the synthetic fixtures) the throw is made at once. True when the throw started or was made.
    /// </summary>
    internal bool ThrowGameCursorItem(CellCoord tile, AreaObjectKey? pressed = null)
    {
        if (_gameCursorItem is not { } held || _areaSession is not { } owner || _worldActor.Session is not AreaGridSession grid || _combatHost is null) return false;
        var struck = HeldThrowStruck(owner, HeroCellNow(grid), tile, pressed);
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
            return PerformHeroThrow(held, tile, pressed) is not null;
        }
        _throwPressed = pressed;
        var result = HeroRequests.Apply(new ThrowRequest(held.Item, tile));
        if (result.Outcome != HeroRequestOutcome.Done) { _throwPressed = null; SetHeldThrowNotice("the throw was refused"); RefreshAreaStatus(); return false; }
        return true;
    }

    /// <summary>
    /// ARC-COMBAT-2524 (ledger RH2, TW1, TW11; 0x434B00, 0x434F80): a combat click on a critter with the hero's wielded weapon "default throwing" starts the throw goal 37 at the critter's <b>tile</b>, never at the critter itself (the
    /// builder writes the item and a location only), so the throw rolls as a throw at a location: the line of fire names only a living, non-shoot-through blocker before the tile, and a hit lands on the tile striking nothing. The item
    /// is the session's choice (<see cref="GameSessionOwner.QueryDefaultThrowItem"/>: a spare of the weapon's prototype, else the wielded one); the throw goal needs no walk and takes the throw's points (<see cref="BeginThrowSwing"/>). True
    /// when the click was taken (a throw started, or refused with its notice); false when the weapon stays in the hand and the attack goal starts as without the flag.
    /// </summary>
    private bool TryDefaultThrowClick(AreaCombatTarget target)
    {
        if (_gameCursorItem is not null || _areaSession is not { } owner || _inventoryHost is null || HeroRunsHost() is null || _worldActor.Session is not AreaGridSession) return false;
        if (HeroDefaultThrowWeight() is not { } weight || owner.QueryDefaultThrowItem(new(weight)) is not { } item) return false;
        var tile = NpcCell(target.Row);
        _gameCursorItem = new GameCursorItem(item, _inventoryHost.ItemName(owner, item, _hoverNames?.Catalog), owner, CurrentAreaMap);
        var started = ThrowGameCursorItem(tile);
        // A throw that did not start leaves the weapon where it is: the cursor holds it only for the throw goal's first node.
        if (!started) _gameCursorItem = null;
        GD.Print($"ARC-COMBAT-2524 default-throw-click item={item} target={target.Entity} tile={tile.X},{tile.Y} started={started}");
        return true;
    }

    // The throw run in progress: the item, its tile and name while the hero's swing plays (the cursor is empty from the swing's start, TT2).
    private sealed record HeroThrowSwing(GameCursorItem Held, CellCoord Tile, AreaAttackPlayer Player, ActorDiagnosticAction Action, int Octant)
    {
        // The item left the hero at the action frame: a cut after it leaves nothing in the pack to say.
        public bool Thrown { get; set; }
    }
    private HeroThrowSwing? _throwSwing;

    // ARC-COMBAT-2425 (TT6): the critter the press named, kept for the throw goal's action frame (the original keeps the object in the goal's frame). It is not saved: a throw run a load restored throws at the location.
    private AreaObjectKey? _throwPressed;

    /// <summary>The throw swing plays (the combat-mode exit gate's attempt-attack test reads it, with the attack's).</summary>
    private bool ThrowPlaying => _throwSwing?.Player is { Playing: true };

    /// <summary>
    /// The throw goal's first node (ARC-DEV-182, 0x42BD40 → 0x42C390 → 0x42CB10): the points of the throw (a turn-based round charges them; TT4 the Original setting nothing), the throw art (animation 14) and the hero's pose turned toward the tile (the pose only: the idle facing is not changed).
    /// The cursor empties here (TT2). Null when the throw cannot begin: the item stays on the cursor and the status line says why.
    /// </summary>
    internal Arcanum.Simulation.Ai.AiAttackSwing? HeroBeginThrow(string item, CellCoord tile)
    {
        // ARC-DEV-317: a throw run a load restored before its start finds the cursor empty (the cursor is not saved): the item it names goes back on it.
        if (_gameCursorItem is null && _areaSession is { } restored && RestoredThrow(restored, item) && RestoredCursorItem(restored, item) is { } back)
        {
            _gameCursorItem = back;
            GD.Print($"ARC-DEV-317 throw-adopt item={item} node=1 count={back.Count?.ToString() ?? "-"}");
        }
        if (_gameCursorItem is not { } held || held.Item != item || _areaSession is not { } owner || _worldActor.Session is not AreaGridSession) return null;
        _throwOwner = owner;
        return BeginThrowSwing(held, tile, replay: false);
    }

    // ARC-DEV-317 (MIG14 S9b): the session whose throws the viewer has seen (set at the press and at the swing's start); a throw run of another session is one a load restored (the cursor and the swing are not saved).
    private GameSessionOwner? _throwOwner;

    private bool RestoredThrow(GameSessionOwner owner, string item)
    {
        if (ReferenceEquals(_throwOwner, owner) || HeroRunsHost()?.ThrowRun is not { } run || run.Bottom.Item != item) return false;
        _throwOwner = owner;
        _throwPressed = null; // a throw run a load restored holds no pressed object (TT6: not saved)
        return true;
    }

    // The item as the lift put it on the cursor: the units the throw's frame carries (a partial lift of a stack), or the whole pile of ammunition or coin, or null when its facts do not read (the throw then refuses and says so).
    private GameCursorItem? RestoredCursorItem(GameSessionOwner owner, string item)
    {
        if (_inventoryHost is null || owner.QueryHero() is null || _inventoryHost.GridMoveItem(owner, item, _areaKitGridMove) is not { } facts)
        {
            SetHeldThrowNotice("the loaded throw stopped: the item's facts do not read here (it stays in the pack)"); RefreshAreaStatus();
            GD.Print($"ARC-DEV-317 throw-adopt item={item} refused (facts)");
            return null;
        }
        var units = HeroRunsHost()?.ThrowRun?.Bottom.Count is > 0 and var c ? c : (int?)null;
        int? count = units ?? (item == SessionHeroGrid.GoldEntity ? owner.QueryGold() : facts.Kind.Type == SessionItemType.Ammo ? owner.QueryAmmo(item)?.Units : null);
        return new(item, _inventoryHost.ItemName(owner, item, _hoverNames?.Catalog), owner, CurrentAreaMap, count, count is null ? null : item == SessionHeroGrid.GoldEntity ? GoldPileKind() : facts.Kind);
    }

    // The throw's swing: the throw art, the hero's pose turned to the tile, the cursor emptied. <paramref name="replay"/> (ARC-DEV-317): a restored swing that was paid before the save plays again from its first frame, charging nothing.
    private Arcanum.Simulation.Ai.AiAttackSwing? BeginThrowSwing(GameCursorItem held, CellCoord tile, bool replay)
    {
        if (_areaSession is not { } owner || _worldActor.Session is not AreaGridSession session) return null;
        var item = held.Item;
        var animation = Arcanum.Simulation.Equipment.WeaponArtRules.ThrowAnimation;
        if (HeroAttackArt.Clip(_worldActor.Profile, animation) is not var (action, art, rateTable))
        { SetHeldThrowNotice($"not thrown: the hero's body has no ART for animation {animation}"); RefreshAreaStatus(); return null; }
        if (!replay && !ChargeHeroAction(owner.QueryHeroThrowCost(), "throw")) { SetHeldThrowNotice(_turnBasedNotice ?? "the throw was refused"); RefreshAreaStatus(); return null; }
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
        GD.Print($"ARC-DEV-182 throw-start item={item} tile={tile.X},{tile.Y} animation={animation} drawn={action} frames={clip.Frames} action={clip.ActionFrame} pause={clip.PauseMs}" + (replay ? " replay" : ""));
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
        // ARC-DEV-317: a throw a load restored between its start and its action frame: the swing is rebuilt (the item is still in the pack) and plays again; the item leaves now.
        if (_throwSwing is null && _areaSession is { } restored && RestoredThrow(restored, item) && RestoredCursorItem(restored, item) is { } held)
        {
            BeginThrowSwing(held, tile, replay: true);
            GD.Print($"ARC-DEV-317 throw-adopt item={item} node=2 swing={_throwSwing is not null}");
        }
        if (_throwSwing is not { } swing || swing.Held.Item != item) return false;
        // A window that opened this frame cuts the throw before the item leaves (the guards of ProcessHeroThrow run a frame later).
        if (WorldModal || ConversationOpen) return false;
        var pressed = _throwPressed;
        _throwPressed = null;
        swing.Thrown = PerformHeroThrow(swing.Held, tile, pressed) is not null;
        return swing.Thrown;
    }

    /// <summary>The throw goal ends (it threw, or refused, or was interrupted): the pose is cleared.</summary>
    internal void HeroThrowEnds(string item, Arcanum.Simulation.Ai.AiExecutorReason reason)
    {
        _throwPressed = null;
        // A throw that never began (unpaid, no ART) has said why; one cut after the item left has nothing to say; only a swing cut before it left leaves the item in the pack.
        if (_throwSwing is { } swing && swing.Held.Item == item)
        {
            _throwSwing = null; _worldActor.AttackPose = null; PublishWorldSelection();
            if (reason == Arcanum.Simulation.Ai.AiExecutorReason.ActionRefused && !swing.Thrown) { SetHeldThrowNotice("the throw was stopped (the item stays in the pack)"); RefreshAreaStatus(); }
        }
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
        _throwPressed = null;
        try { _heroRunsHost?.EndThrow("ROUTE_STOPPED_BY_THROW_END"); }
        catch (ObjectDisposedException) { /* a replaced session */ }
        if (_throwSwing is not null) { _throwSwing = null; _worldActor.AttackPose = null; GD.Print("ARC-DEV-182 throw-cancel " + why); PublishWorldSelection(); }
    }

    /// <summary>
    /// The session's throw of <paramref name="held"/> at <paramref name="tile"/> (TT2 → <see cref="GameSessionOwner.ThrowHeroItem"/>): the struck critter is the
    /// line of fire's living NPC blocker, else the critter the press named (TT6, TW11), else none: a tile press names no critter (the line of fire skips the target tile's own objects, TW10); a companion named is NOT READY (the item stays). <see cref="HeldThrowStruck"/> never names a dead critter as the struck object (the line of fire passes it and the tile fallback skips it, so the item flies to the location), as the original's line of fire never does (ARC-COMBAT-2416, TW9); the press itself still takes an object's tile with no dead filter, and the original's click-through of corpses in combat mode is not reproduced. The session rolls against a corpse only for a struck critter that died before the roll (ARC-COMBAT-2392). ARC-COMBAT-2425 (TW11): the critter standing on a pressed tile is not named; the item lands there striking nothing (the original's hit with no struck object), a hit roll with no defender and the miss's scatter strikes of such a throw are NOT READY (#2435). On success each shot flies.
    /// </summary>
    private SessionHeroThrowResult? PerformHeroThrow(GameCursorItem held, CellCoord tile, AreaObjectKey? pressed = null)
    {
        if (_areaSession is not { } owner || _worldActor.Session is not AreaGridSession grid || _combatHost is null) return null;
        var from = HeroCellNow(grid);
        var struck = HeldThrowStruck(owner, from, tile, pressed);
        if (struck is { Companion: true })
        {
            SetHeldThrowNotice("throwing at a companion is NOT READY (the item stays in the pack)");
            RefreshAreaStatus();
            return null;
        }
        if (_inventoryHost!.GridMoveItem(owner, held.Item, _areaKitGridMove) is not { } facts) { SetHeldThrowNotice("not thrown: its facts do not read here"); return null; }
        var range = owner.QueryHeroThrowRange(held.Item, held.Count) ?? 1;
        var wearable = _inventoryHost.Wearable(owner, held.Item, _areaKitWear);
        var request = new SessionHeroThrow(held.Item, facts, tile)
        {
            Count = held.Count, SplitKind = held.StackKind,
            Combat = wearable?.Item, Throwable = wearable?.Throwable == true,
            HeroWorn = AreaCombatHost.HeroWorn(owner, _areaKitDurability), DropMap = AreaDropMapNow(), ItemTriggers = ItemTriggersNow(),
            // ARC-COMBAT-1955: the removal's and the throw script (point 6) of the item run through the shown map's item script host.
            Scripts = _inventoryHost.ItemScripts(owner),
        };
        var aimed = struck?.Goal;
        if (aimed is not null)
        {
            var cell = NpcCell(aimed.Row);
            request = request with
            {
                Struck = aimed.Combat, StruckCell = cell, StruckLight = AreaRowLightLevel(aimed.Row), StruckWorn = _combatHost.WornItems(owner, aimed.Entity),
                LineOfFire = ShotLine(TraceLineOfFire(from, aimed.Row), aimed.Row),
            };
        }
        // ARC-COMBAT-2435 (TW11): a throw that names no critter rolls as a throw at the location: the light of the tile and the line of fire's cost are its terms (the blocker, if any, is no living critter, or it would be the struck one); the light
        // also serves a throw that falls back to the location after its scripts ran.
        var locationLine = new SessionLineOfFire(from != tile && AreaLineOfFireNow() is { } lineMap ? Math.Min(LineOfFireRules.Trace(from, 0, 0, tile, lineMap, owner.Rules.Combat).Cost, AttackResolver.MaxMagnitude) : 0, false);
        request = request with { TargetLight = AreaCellLightLevel(tile.X, tile.Y) };
        if (aimed is null) request = request with { LineOfFire = locationLine };
        // ARC-COMBAT-2435 smoke seam: a smoke that needs its throw at a tile to hit sets the session's random stream at the action frame (the stream's draws before it are not the smoke's to count).
        if (_heldThrowSeedForSelfTest is { } seed) { owner.RestoreRandom(new SessionRandomState(seed, 0)); _heldThrowSeedForSelfTest = null; }
        // ARC-COMBAT-2698: the refused weapon's sound fields are read while it is still the hero's (the refusal's sound plays after it left the list).
        var thrownSounds = EntitySounds(held.Item);
        var result = owner.ThrowHeroItem(owner.Version, request);
        // A struck critter that died meanwhile refuses the attack: the original's press throws at the location anyway.
        if (aimed is not null && result.Disposition == GameSessionDisposition.NoChange && owner.QueryHeroLife() is { Status.IsActive: true })
        {
            aimed = null;
            result = owner.ThrowHeroItem(owner.Version, request with { Struck = null, StruckCell = null, StruckWorn = null, LineOfFire = locationLine });
        }
        // ARC-COMBAT-1955: the scripts ran, the session threw at the location itself because the struck critter refused: no struck critter is left.
        if (aimed is not null && result.AtLocation) aimed = null;
        _lastHeldThrowForSelfTest = result;
        GD.Print($"ARC-UI-063 throw item={held.Item} tile={tile.X},{tile.Y} struck={aimed?.Entity ?? "none"} disposition={result.Disposition}"
            + $" failure={result.Failure} outcome={result.Attack?.Resolution?.Outcome.ToString() ?? "-"} shots={result.Shots.Count}"
            + $" destination={(result.Destination is { } d ? $"{d.X},{d.Y}" : "-")} cost={owner.QueryHeroThrowCost()}"
            + (result.ScriptDeclined ? " script-declined" : "") + (result.AmmunitionShort ? " out-of-ammunition" : "") + (result.Scripts.Count > 0 ? $" scripts={result.Scripts.Count}" : "") + (result.Messages.Count > 0 ? $" lines={result.Messages.Count}" : ""));
        // ARC-COMBAT-1955: a line the item's removal or throw script printed goes to the message window, and the eye candy they made plays (a refused throw whose scripts changed nothing shows none).
        PostScriptLines(result.Messages);
        QueueScriptEyeCandies(result.EyeCandies);
        if (result.Disposition == GameSessionDisposition.Rejected || result.Failure != GameSessionFailure.None)
        {
            // A refusal after the scripts took effect is a changed session with the failure named: the item did not leave.
            SetHeldThrowNotice(AreaHeldThrow.Refusal(result.Failure, held.Name) + " (it stays in the pack)");
            if (result.Disposition != GameSessionDisposition.Rejected) SyncAreaInventory();
            RefreshAreaStatus();
            // A refusal after the scripts took effect keeps its own notice: the throw goal's end must not replace it with "stopped".
            return result.Disposition == GameSessionDisposition.Rejected ? null : result;
        }
        // ARC-COMBAT-2415 (TW8, 0x4B65D0): the thrown weapon's ammunition check refused: the item left the pack and lies on the hero's tile switched off (the original plays its out-of-ammunition sound).
        if (result.AmmunitionShort)
        {
            SetHeldThrowNotice($"{held.Name} not thrown: no ammunition for it (it lies switched off on the hero's tile)");
            // ARC-COMBAT-2698 (0x4B3170): the out-of-ammunition sound on the hero, then the auto switch (a PC thrower only; 0x4B83E0).
            if (ActorCell() is { } sounded) PlayAmmunitionRefusalSound(thrownSounds, sounded.X, sounded.Y);
            HeroThrowOutOfAmmunition(result, aimed?.Row);
            SyncAreaInventory(); RefreshAreaStatus();
            return result;
        }
        if (result.ScriptDeclined)
        {
            SetHeldThrowNotice($"{held.Name} stays: its throw script kept it (it is back in the pack)");
            SyncAreaInventory(); RefreshAreaStatus();
            return result;
        }
        if (result.Disposition == GameSessionDisposition.NoChange) { SetHeldThrowNotice("the hero cannot act (the item stays in the pack)"); RefreshAreaStatus(); return null; }
        var (art, unfaced) = HeldThrowArt(held.Item, wearable);
        var hit = result.Attack?.Resolution?.Outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit;
        // ARC-COMBAT-1930 (0x4B3170, PF1, PF5, VCG9): a throw sends no attack event at the throw; the projectile's end alerts only the object it strikes. A throw decided a hit raises it here for the aimed critter (the arrival's
        // time is the accepted deviation A) with the thrown item's loudness as a weapon (0x4B2810); a throw decided a miss never strikes its aimed target and alerts none.
        if (aimed is not null && hit && result.Attack?.Resolution?.Refusal == AttackRefusal.None) _aiHost?.HeroAttacked(aimed.Entity, KitItemNoise(held.Item));
        foreach (var shot in result.Shots)
            // ARC-COMBAT-2435: a hit with no struck critter flies to its tile and strikes nothing (the targetless hit shot, ProjectileRules.Cross); a miss flies toward its scattered tile and strikes what it passes.
            FlyShot(shot, result.Destination ?? tile, art, unfaced, range, hit, aimed?.Row ?? HeroRow, aimed, null, result.Destination is not null,
                sizeArc: facts.Kind.Type != SessionItemType.Weapon);
        SetHeldThrowNotice($"{held.Name} thrown at {tile.X},{tile.Y}" + (aimed is null ? (result.Attack?.Resolution is null ? "" : hit ? ": it lands there" : result.Destination is { } scattered ? $": it missed and flies to {scattered.X},{scattered.Y}" : ": it missed") : hit ? $": it will hit {aimed.Entity}" : ": it missed"));
        // A critical miss turned on the hero at once (its result is the attack's); a strike waits for the flight.
        if (aimed is not null && result.Attack is { } attack && (result.Shots.Count == 0 || attack.Life is not null)) FinishCritterHit(aimed, attack, selfHitReachesTaunt: false, thrownItem: held.Item); // ARC-AI-1639: a held throw has weapon type 3 (0x465D80), which skips the attack end's taunt call
        // ARC-COMBAT-2435: a critical miss of a throw at a location turns on the hero (its damage was applied by the session): the hero's lines, its worn-out weapon and its stun.
        if (aimed is null && result.Attack is { Recipient: Arcanum.Simulation.Combat.DamageRecipient.Attacker } turned) FinishLocationThrow(owner, turned);
        SyncAreaInventory(); RefreshAreaStatus();
        return result;
    }

    // ARC-COMBAT-2435: the reaction to a critical miss of a throw at a location: the self-hit's floaters, the hero's weapon that wore out and its stun (the attacker is the victim, no struck critter).
    private void FinishLocationThrow(GameSessionOwner owner, SessionAttackResult result)
    {
        HeroWeaponWornOut(result.Wear, HeroRow, false);
        FloatCritterAttack(result, HeroFloater, HeroFloater);
        _attackNotice = "critical miss: the throw turned on the hero";
        GD.Print($"ARC-COMBAT-2435 location-throw-critical-miss damage={result.Life?.HitPointDamage} events={result.Life?.Events} step={result.StepFailure}"
            + $" wear={string.Join(",", result.Wear?.Steps.Select(s => $"{s.Kind}:{s.Entity}:{s.Amount}:{s.Skip}") ?? [])}");
        if (result.Life is { } took && took.Events.HasFlag(LifeEvents.Stunned)) HeroStunned(owner);
        SyncAreaCombatBodies();
    }

    // The struck critter of a throw at the tile (ARC-COMBAT-2425, TW4 as corrected by TW10 and TW11): the line of fire's blocker when it is a living NPC with combat values (a blocker replaces the target, RA4),
    // else the critter the press named (TT6: the object press stores the object as the goal's target), else none: a press on a tile stores a location only, and the line of fire never names the critter on
    // the target's own tile. A companion is named so the throw can refuse it (NOT READY).
    private (AttackGoal? Goal, bool Companion)? HeldThrowStruck(GameSessionOwner owner, CellCoord from, CellCoord tile, AreaObjectKey? pressed)
    {
        if (_readyArea is not { } area) return null;
        (AttackGoal? Goal, bool Companion)? Critter(AreaObjectRecord row)
        {
            if (IsAreaCompanion(row.Key)) return (null, true);
            return _combatHost!.Target(owner, row.Key) is { Dead: false, Build.Combat: { } combat } npc ? (new AttackGoal(npc, combat, null), false) : null;
        }
        if (from != tile && AreaLineOfFireNow() is { } map && LineOfFireRules.Trace(from, 0, 0, tile, map, owner.Rules.Combat).Blocker is { } blocker)
            return blocker.Type is 15 or 16 ? Critter(blocker) : null;
        if (pressed is { } key && area.Snapshot.Objects.FirstOrDefault(row => row.Key == key) is { Type: 15 or 16 } named && !AreaCritterDead(owner, CurrentAreaMap, named))
            return Critter(named);
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
            var missile = kit ?? (InventoryItemRow(item) ?? MadeItemFields(item))?.Weapon?.MissileArt;
            if (missile is { } m && m != Arcanum.Data.Combat.CombatWeaponFields.NoMissileArt) return (unchecked((uint)m), false);
        }
        if (item == SessionHeroGrid.GoldEntity) return (PrototypeItems()?.Item(AreaBarterData.GoldPrototype)?.Art, true); // ARC-UI-094: the coins fly as the gold prototype's art
        // ARC-SCRIPT-1779: the ground art a script's change art number wrote for the item is its art in flight.
        if (_areaSession?.QueryObjectArt(new SessionMapId(CurrentAreaMap), item) is { } written) return (written, true);
        foreach (var map in _inventoryHost?.Maps ?? [])
            if (map.TryGetRow(item, out var row) && Number(row, CurrentArtSlot) is { } own) return (own, true);
        // ARC-DEV-315: an item the session made has no row: its prototype's current art.
        if (_areaSession is { } session && AreaMadeItems.Art(session, item, prototype => PrototypeItems()?.Item(prototype)?.Art) is { } madeArt) return (madeArt, true);
        return (KitArt(item), true);
    }

    /// <summary>ARC-DEV-315: the stored fields of an item the session made (no row in any map: a copy of its prototype), or null for any other item.</summary>
    private Arcanum.Data.Combat.CombatFields? MadeItemFields(string item) =>
        _areaSession is { } session && AreaMadeItems.Prototype(session, item) is { } prototype ? PrototypeItems()?.Fields(prototype) : null;

    /// <summary>ARC-COMBAT-2435: the seed the next held throw's roll starts from (a smoke whose throw at a tile must hit: the hit roll of seed 727 is 1); cleared by that throw.</summary>
    private ulong? _heldThrowSeedForSelfTest;

    private void SetHeldThrowNotice(string notice) { _heldThrowNotice = notice; _heldThrowNoticeAt = Time.GetTicksMsec(); }

    private string HeldThrowStatus() => _heldThrowNotice is null || !InventoryActive
        || _gameCursorItem is null && Time.GetTicksMsec() - _heldThrowNoticeAt > HeldThrowNoticeMs ? string.Empty : $" | throw: {_heldThrowNotice}";
}
