using Arcanum.Data.Area;
using Arcanum.Input;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.TerrainViewer.Core;
using GamepadLab.Core;
using Godot;
using Arcanum.World;

namespace Arcanum.TerrainViewer;

// ARC-UI-004: the session inventory (ARC-WORLD-014) in crash-site-area. The loaded maps' T0 seeds are attached to the
// area session when it is created and again on every quick load. A left click on an item lying on the ground picks it
// up, a left click on a dead NPC opens it in the inventory window to loot; both arrive through the guarded ground-click
// path (WorldRouteClick) after talk and use, act within reach 1 at once and otherwise walk within reach first (the
// source goals' move-near step), like walk-to-use. A taken item is neither drawn nor picked. The original Inventory key
// (I, logical key down) toggles the window; the window is modal (gate closed, clock paused) and Escape, I or a
// controller cancel closes it. Virgil's negative IN goes to the same session inventory through the dialog port.
public partial class TerrainViewerApp
{
    private AreaInventoryHost? _inventoryHost;
    private string _inventoryStatus = "inventory: not loaded";
    private AreaInventoryPanel? _inventoryPanel;
    // ARC-SKILLS-005: the hero's kit healing items from its load (null without --hero).
    private IReadOnlyList<SessionHealingItem?>? _areaKitHealing;
    private IReadOnlyList<SessionLockpick?>? _areaKitLockpicks;
    // ARC-COMBAT-008: the kit's durability values for the attack's wear.
    private IReadOnlyList<SessionWearItem?>? _areaKitDurability;
    // ARC-UI-016: the kit's item spell facts (the cast host's item lookup).
    private IReadOnlyList<SessionItemSpells?>? _areaKitItemSpells;
    // ARC-UI-015: the kit's wear facts (the best-items start's), for wearing a kit item from the window.
    private IReadOnlyList<SessionWearableItem?>? _areaKitWear;
    // ARC-UI-019: the kit's facts for a move in the hero's grid.
    private IReadOnlyList<SessionGridMoveItem?>? _areaKitGridMove;
    // ARC-UI-025: the kit's use facts (the window's use paths).
    private IReadOnlyList<AreaItemUseFacts?>? _areaKitUse;
    private (GameSessionOwner Session, long Revision, int Map)? _inventoryHiddenStamp;
    private IReadOnlySet<AreaObjectKey>? _inventoryHidden;
    // ARC-DEV-109: the one inventory panel is three modes of the stack, as the original's inventory opener has them: the hero's own list
    // (8), the loot window of a body, container or junk pile (10) and the steal window of a pocket being picked (11).
    private bool InventoryOpen => OpenInventoryMode is not null;
    private InterfaceMode? OpenInventoryMode => WindowOpen(InterfaceMode.Inventory) ? InterfaceMode.Inventory
        : WindowOpen(InterfaceMode.Loot) ? InterfaceMode.Loot : WindowOpen(InterfaceMode.Steal) ? InterfaceMode.Steal : null;
    private InterfaceMode InventoryModeOf(string? holder) => holder is null ? InterfaceMode.Inventory
        : holder == _pickPocketVictim ? InterfaceMode.Steal : InterfaceMode.Loot;
    private bool InventoryActive => _inventoryHost is not null && _areaSession is not null && _readyMode == TerrainViewerMode.Area;

    /// <summary>Builds the seeds of the exterior and (when loaded) the cave and attaches them to the new area session.
    /// A refused build leaves the inventory unavailable; the rest of the area runs as before. ARC-SCRIPT-013: the game's
    /// script content (shared with the conversations) runs an item's insert-item script on a transfer.</summary>
    private void BindAreaInventory(TerrainViewerArguments arguments, TerrainLoadResult result, bool keepLeft = false)
    {
        UnbindAreaInventory();
        // ARC-DEV-079 (MIG4): the home maps' data (rows, bodies, frames) the viewer has read or loaded is kept for the next scene of the same
        // run (a quick load or a fresh run reads again, and forgets a failed read); set before any early return so no scene keeps the last one's.
        var kept = keepLeft ? _homes?.Reads ?? [] : [];
        _homes = new AreaHomeDirectory(new AreaMapHomes(arguments, MapCompanionFacts));
        foreach (var read in kept) _homes.Remember(read);
        if (_areaSession is not { } session || result.Area is not { } area || result.AreaStepSnapshot is not { } steps) return;
        var sources = new List<(string Name, AreaSnapshot Snapshot, Arcanum.Data.Traversal.AreaStepSnapshot Steps)> { ("exterior", area.Snapshot, steps) };
        if (result.Cave is { } cave) sources.Add(("cave", cave.Area.Snapshot, cave.Steps));
        var maps = new List<AreaInventoryMap>();
        foreach (var (name, snapshot, mapSteps) in sources)
        {
            var build = AreaInventoryMap.Build(snapshot, mapSteps);
            GD.Print($"ARC-UI-004 inventory-seed map={snapshot.RuntimeMap} status={build.Status} detail={build.Detail}");
            if (build.Map is null) { _inventoryStatus = $"inventory: unavailable ({name} {build.Status})"; return; }
            maps.Add(build.Map);
        }
        var content = ConversationContent(arguments.ModulePath, area.Snapshot.Inputs.SingleOrDefault(input => input.Key.Role == AreaSourceRole.Module)?.Sha256);
        // ARC-DEV-072 (MIG1): an NPC whose home map this run did not load resolves through the resolver, which reads the home map's rows on
        // demand from the game folder. Only maps a whole-map plan admits are read: a home on the main world map (a window) is not (NOT READY).
        // ARC-DEV-075 (MIG2): the maps the run left at its last map change answer first (a window of the main map included), so a companion
        // carried from the main map into the cave has its home record there.
        var left = keepLeft ? _recordMapsLeft : [];
        _recordMapsLeft = [];
        // The map just loaded is remembered, so a companion carried from it needs no read. A map the resolver reads on demand is read through
        // the same directory (a window of the main map around a stored cell the session recorded).
        if (arguments.AreaMapName is not null && maps.Count == 1) _homes.Remember(AreaHomeRead.Of(maps[0], _ownCompanionTemplates, area.Frames, area.Snapshot));
        AdoptAreaInventory(new AreaInventoryHost(maps, scripts: content.Content, records: new AreaRememberedRecords(left,
            new AreaMapRecords(arguments, _homes, map => _areaSession is { } owner ? AreaVisitors.HomeWindow(owner, map) : null))));
    }

    // The session has no detach: when a later map's seed is refused, an earlier one stays attached but unused (seeds are
    // never saved, and the dialog port and clicks need _inventoryHost).
    private void AdoptAreaInventory(AreaInventoryHost host)
    {
        var failure = host.Attach(_areaSession!);
        if (failure != GameSessionFailure.None)
        {
            // ARC-COMBAT-024: without a host the map has no loot, combat or fallen bodies: the log says why.
            GD.Print($"ARC-UI-004 inventory-attach refused failure={failure}");
            _inventoryStatus = $"inventory: unavailable (seed {failure})"; return;
        }
        _inventoryHost = host;
        _inventoryStatus = "inventory: left-click an item to pick it up or a dead NPC to loot; I opens the window";
        // ARC-COMBAT-005: the combat targets are the seeds' NPCs.
        BindAreaCombat(host);
    }

    // ARC-DEV-075: the record maps of the host a map change tore down, for the next scene of the same run (a new run starts with none).
    private IReadOnlyList<AreaInventoryMap> _recordMapsLeft = [];

    private void UnbindAreaInventory()
    {
        if (_inventoryHost is { } leaving) _recordMapsLeft = leaving.Resolver.Maps;
        EndInventory();
        UnbindAreaCombat();
        _inventoryHost = null; _inventoryHiddenStamp = null; _inventoryHidden = null;
        _canvas?.SetAreaHidden(null);
        _inventoryStatus = "inventory: not loaded";
    }

    /// <summary>Per frame: the taken items of the current map are neither drawn nor picked.</summary>
    private void SyncAreaInventory()
    {
        if (_canvas is null) return;
        if (!InventoryActive || _readyArea is null) { if (_canvas.AreaHidden is not null) _canvas.SetAreaHidden(null); return; }
        var stamp = (_areaSession!, _areaSession!.Version.Revision, CurrentAreaMap);
        if (!Equals(stamp, _inventoryHiddenStamp))
        {
            _inventoryHiddenStamp = stamp;
            _inventoryHidden = _inventoryHost!.Taken(_areaSession, CurrentAreaMap);
            // ARC-COMBAT-008: the objects an attack destroyed or switched off are not drawn either.
            if (ObjectTargets()?.Gone(_areaSession) is { Count: > 0 } gone) _inventoryHidden = _inventoryHidden.Concat(gone).ToHashSet();
            // ARC-WORLD-063: a map item swept into a junk pile is drawn as the pile, not where it lay.
            if (_inventoryHost.Piled(_areaSession, CurrentAreaMap) is { Count: > 0 } piled) _inventoryHidden = _inventoryHidden.Concat(piled).ToHashSet();
        }
        if (!ReferenceEquals(_canvas.AreaHidden, _inventoryHidden)) _canvas.SetAreaHidden(_inventoryHidden);
        // ARC-RENDER-041: a death seen since the last sync leaves its pool of blood first.
        LeaveBloodPoolsForNewDeaths();
        // ARC-RENDER-033: an item picked up or dropped leaves or joins the drawn ground at once.
        if (SyncAreaGroundBodies()) _canvas.SetAreaMovingBodies(AreaMovingCommands());
    }

    private bool AreaTakenByPc(AreaDrawCommand command) => command.Identity is { } key && _inventoryHidden?.Contains(key) == true;

    /// <summary>The pickup or loot target that is the frontmost pickable object under the screen point, if any.</summary>
    private AreaInventoryTarget? InventoryTargetAt(Vector2 screen)
    {
        if (!InventoryActive || AreaObjectAtScreen(screen) is not { } key) return null;
        return AreaInventoryTargetOf(key);
    }

    private long Chebyshev(AreaObjectRecord row, (long X, long Y) cell)
    {
        var at = LiveWorldCell(row);
        return Math.Max(Math.Abs(at.X - cell.X), Math.Abs(at.Y - cell.Y));
    }

    /// <summary>ARC-WORLD-092: where a map row stands now: an NPC the AI walked on its cell, anything else on its map data's.</summary>
    private (long X, long Y) LiveWorldCell(AreaObjectRecord row) =>
        _aiHost?.Scene.Npcs.FirstOrDefault(n => n.Row.Key == row.Key) is { } npc && _aiHost.Movers.IsMoved(npc.Entity) && _aiHost.Movers.Of(npc.Entity) is { } mover
            ? (mover.Cell.X, mover.Cell.Y) : (row.WorldX!.Value, row.WorldY!.Value);

    /// <summary>
    /// A guarded left press (see <c>WorldRouteClick</c>) on a ground item, a container, a junk pile or a dead NPC: true when it was one. ARC-DEV-175 (MIG13 S11c, GS26 and GS27): the click is an <see cref="ObjectClickRequest"/>; the click
    /// rules make it the pickup goal 15 for an item (the hero walks onto its tile through goal 8 with range 0, then goal 16 takes it) or the use goal 81 for a container or a body (S11b: range 1, then the loot window). There is no walk-to-pick-up
    /// intent any more; the act is applied at the frame's next safe point (<see cref="ProcessUsePending"/>). Alt is passed in combat mode only (out of it Alt is the immediate action 0x4445A0, not read).
    /// </summary>
    private bool TryAreaInventoryClick(Vector2 screen, bool alt)
    {
        if (InventoryOpen || ActorCell() is null || InventoryTargetAt(screen) is not { } target) return false;
        CancelPickPocket("an item click replaced it");
        CancelTalkApproach("an item click replaced it"); CancelAreaUseApproach("an item click replaced it"); CancelInventoryApproach(null);
        _useOpenPending = null;
        var verb = target.Kind == AreaInventoryTargetKind.GroundItem ? "pick up" : "loot";
        var request = HeroRequests.Apply(new ObjectClickRequest(InventoryTargetOf(target.Row.Key), Alt: alt && HeroInCombatMode));
        _inventoryStatus = request.Click is not (Arcanum.Simulation.Goals.HeroClickGoal.Pickup or Arcanum.Simulation.Goals.HeroClickGoal.UseContainer) ? $"the click does nothing here ({request.Click})"
            : request.Outcome == HeroRequestOutcome.Done ? $"walking to {verb}: goal {(request.Click == Arcanum.Simulation.Goals.HeroClickGoal.Pickup ? 15 : 81)} started" : $"cannot {verb}: the order was refused";
        GD.Print($"ARC-DEV-175 {verb.Replace(' ', '-')}-click outcome={request.Outcome} goal={request.Click}");
        RefreshAreaStatus();
        return true;
    }

    private void ActOnInventoryTarget(AreaInventoryTarget target)
    {
        // ARC-COMBAT-020: in a turn-based round a pickup costs 1 action point and a body's loot (an object's use) 2 (ledger CT3).
        var pickup = target.Kind == AreaInventoryTargetKind.GroundItem;
        if (!ChargeHeroAction(pickup ? TurnBasedRules.PickUpCost : TurnBasedRules.UseObjectCost, pickup ? "pickup" : "loot"))
        { _inventoryStatus = _turnBasedNotice; RefreshAreaStatus(); return; }
        // ARC-WORLD-101: a container's window first passes its admission (lock, scripts) and generates it once.
        if (target.Kind == AreaInventoryTargetKind.Container) { OpenContainer(target); return; }
        if (!pickup) { _containerLootSession = null; OpenInventory(target.Entity); return; }
        var result = _inventoryHost!.PickUp(_areaSession!, target.Entity);
        var name = _inventoryHost.Name(target.Entity, _hoverNames?.Catalog);
        _inventoryStatus = AreaInventoryHost.Moved(result)
            ? $"picked up {name}{AreaInventoryHost.Printed(result)}" : $"pickup refused: {AreaInventoryHost.NotMoved(result)}";
        if (AreaInventoryHost.Moved(result)) PlayPickupSound(target.Entity);
        GD.Print($"ARC-UI-004 pickup disposition={result.Disposition} failure={result.Failure} pc={_areaSession!.QueryPcInventory().Count}{InsertScriptLog(result)}");
        SyncAreaInventory(); RefreshAreaStatus();
    }

    // ARC-SCRIPT-013: how an item's insert-item script ended and how many lines it printed (the text stays on screen only).
    // ARC-SCRIPT-041: every item script run of the transfer (point:script:stop) and a gate's decline.
    private static string InsertScriptLog(InventoryMutationResult result) =>
        (result.InsertScript is { } run ? $" insert-script={run.Script} stop={run.Stop} lines={result.Messages.Count}" : string.Empty) +
        (result.Scripts.Count == 0 ? string.Empty : " scripts=" + string.Join(",", result.Scripts.Select(s => $"{s.Point}:{s.Run.Script}:{s.Run.Stop}"))) +
        (result.DeclinedBy is { } point ? $" declined-by={point}" : string.Empty);

    private void CancelInventoryApproach(string? why)
    {
        // ARC-SKILLS-010: what replaces a walk to pick up or loot replaces an armed or walking pick pocket too.
        if (why is not null) CancelPickPocket(why);
        // ARC-DEV-175: the walk to pick up or loot is the hero's pickup run (goal 15) or use run (goal 81 for a body or a container); a use run for a trigger object ends with the same call (CancelAreaUseApproach).
        var ended = ShownHeroRunsHost is { } host && (host.EndPickup(why ?? "cancelled") | host.EndUse(why ?? "cancelled"));
        if (!ended && _useOpenPending is null) return;
        _useOpenPending = null;
        if (why is not null) _inventoryStatus = "walk to pick up/loot cancelled: " + why;
    }

    private GameSessionOwner? _junkPileSession;

    /// <summary>Opens the window on the PC's list, or (with <paramref name="holder"/>) on a dead NPC, junk pile or (ARC-WORLD-101, through
    /// <c>OpenContainer</c>) container to loot.</summary>
    private void OpenInventory(string? holder = null)
    {
        var mode = InventoryModeOf(holder);
        if (!InventoryActive || InventoryOpen || !WindowCanOpen(mode)) { _containerLootSession = null; return; }
        if (holder is null || SessionJunkPile.Number(holder) is not null) _containerLootSession = null;
        if (_inventoryPanel is null)
        {
            _inventoryPanel = new AreaInventoryPanel(); AddChild(_inventoryPanel);
            _inventoryPanel.CloseRequested += EndInventory;
            _inventoryPanel.TakeRequested += TakeFromBody;
            _inventoryPanel.TakeAllRequested += TakeAllFromBody;
            _inventoryPanel.PlantRequested += item => PickPocketAttempt(Arcanum.Simulation.Skills.PickPocketDirection.Plant, item);
            _inventoryPanel.UseRequested += UseItemFromWindow;
            _inventoryPanel.PointerStill += InventoryPointerStill;
            _inventoryPanel.ReadRequested += ReadItem;
            _inventoryPanel.PickRequested += ArmLockpick;
            _inventoryPanel.RepairRequested += RepairItem;
            _inventoryPanel.WearRequested += WearItem;
            _inventoryPanel.TakeOffRequested += TakeOffItem;
            _inventoryPanel.DropRequested += DropItem;
            _inventoryPanel.MapDropRequested += held => LiftToGameCursor(held.Entity, "round-view");
            _inventoryPanel.QuantityAccepted += AcceptQuantity;
            _inventoryPanel.QuantityCancelled += CancelQuantity;
            _inventoryPanel.HolderView = InventoryHolderView;
        }
        // A quick key held across the window must not act on its release afterwards (its press predates the window).
        QuarantineWorld(); _quickKeys.Reset();
        // ARC-WORLD-063: a junk pile's window closes against the session it was opened on.
        _junkPileSession = SessionJunkPile.Number(holder) is not null ? _areaSession : null;
        // ARC-UI-051: the player's own window shows the original look when the game's art read.
        _inventoryPanel.SetArtLook(InventoryArtLook());
        // ARC-UI-054: the open centres the game view on the window's holder, the PC (0x5732D5 → 0x4B8CE0, IW33), at the current zoom;
        // the palette's Fit view ends there at the preferred zoom, as at the other centrings.
        if (_camera is not null)
        {
            var zoom = _worldFitView ? _worldPreferredZoom : _camera.State.Zoom;
            _worldFitView = false; _camera.ResetWorld(WorldAnchor, WorldViewport, zoom);
        }
        var view = InventoryView(holder);
        if (!OpenWindowMode(mode, () => _inventoryPanel!.Open(view), () => _inventoryPanel!.CloseView()))
        {
            _junkPileSession = null;
            return;
        }
        // ARC-WORLD-096: a loot open schedules the looted holder's restock (RK2; a junk pile is no seed holder and has none).
        if (holder is not null) ScheduleRestock(holder, "loot");
        GD.Print($"ARC-UI-004 inventory open holder={(holder is null ? "none" : SessionJunkPile.Number(holder) is not null ? "junk-pile" : ReferenceEquals(_containerLootSession, _areaSession) ? "container" : holder == _pickPocketVictim ? "steal" : "dead-npc")} pc={_areaSession!.QueryPcInventory().Count} loot={_inventoryPanel.ViewForSelfTest!.Loot.Count}");
        RefreshReadyStatus();
    }

    /// <summary>ARC-UI-054: the PC's foot point in canvas units and the game view's zoom, for the window's round view (IW33).</summary>
    private (Vector2 Foot, float Zoom)? InventoryHolderView()
    {
        if (_areaSession is null || _camera is null) return null;
        var canvas = GetViewport().GetCanvasTransform();
        var anchor = WorldAnchor;
        return (canvas * new Vector2((float)anchor.X, (float)anchor.Y), canvas.Scale.X);
    }

    private AreaInventoryView InventoryView(string? holder) => _inventoryHost!.View(_areaSession!, _hoverNames?.Catalog, CurrentAreaMap, holder, _areaKitHealing, _areaKitLockpicks, _areaKitDurability, _areaKitWear, _areaKitUse)
        // ARC-SKILLS-010: the window on the NPC whose pocket the hero picks is in steal mode.
        with { Steal = holder is not null && holder == _pickPocketVictim };

    /// <summary>ARC-SKILLS-005: the hero uses a healing item on itself (the inventory use passes the user as the target, as the
    /// original's inventory window does). ARC-UI-025: the item use (0x462CC0) reaches it as the heal skill with the item and has charged
    /// the use's action points already, so the heal charges none (<paramref name="charged"/>).</summary>
    private void HealSelf(string item, bool charged = false)
    {
        if (!InventoryOpen || !InventoryActive) return;
        var holder = _inventoryPanel!.Holder;
        var healing = _inventoryHost!.HealingItem(_areaSession!, item, _areaKitHealing);
        string message;
        if (healing is null) message = "not used: not a healing item the hero holds";
        else
        {
            ResumeLoadedRound();
            var result = _areaSession!.HealWithItem(_areaSession.Version, SessionLifeTarget.Hero, healing, charged ? null : CombatWorldOrNull());
            var life = _areaSession.QueryHeroLife()?.Record.State;
            message = result.Disposition == GameSessionDisposition.Rejected ? $"not used: {result.Failure}"
                : (result.Check!.Success ? $"healed {result.Heal!.Value.HitPoints} hit point(s){(result.Heal.Value.Cure ? " and cured an injury" : string.Empty)}" : "the heal failed")
                + (result.ItemDestroyed ? "; the item is used up" : $"; {result.UsesLeft} use(s) left") + (result.FateUsed ? " (fate point)" : string.Empty);
            GD.Print($"ARC-SKILLS-005 heal disposition={result.Disposition} failure={result.Failure} success={result.Check?.Success} critical={result.Check?.Critical} " +
                $"healed={result.Heal?.HitPoints} cure={result.Heal?.Cure} uses-left={result.UsesLeft?.ToString() ?? "-"} destroyed={result.ItemDestroyed} " +
                $"hp-damage={life?.HitPointDamage}");
        }
        _inventoryStatus = message;
        _inventoryPanel.Render(InventoryView(holder), message);
        RefreshReadyStatus();
    }

    /// <summary>ARC-SKILLS-006: lockpicks chosen in the window: it closes and the next left press on a door or window picks its
    /// lock (the original's use of lockpicks on an object, 0x571CB0); a press elsewhere cancels.</summary>
    private void ArmLockpick(string item)
    {
        if (!InventoryOpen || !InventoryActive) return;
        if (_inventoryHost!.Lockpick(_areaSession!, item, _areaKitLockpicks) is null)
        {
            _inventoryStatus = "not used: not lockpicks the hero holds";
            _inventoryPanel!.Render(InventoryView(_inventoryPanel.Holder), _inventoryStatus);
            return;
        }
        // ARC-DEV-110 (MIG16 S3): the lockpicks are the item target mode (16). The original's opener (0x571AA0) closes the inventory
        // and pops it when the item's target mask has no inventory bit (a lock is not an inventory item): the window closes through its
        // End routine, then the mode takes the base; a target or pick pocket armed before it is left first (the mode replaces it).
        EndInventory();
        CancelPendingOrder("lockpicks armed");
        CancelSpellTargeting("lockpicks armed");
        if (!OpenTargetMode(InterfaceMode.ItemTarget, _ => _lockpickArmed = null))
        {
            _portalNotice = "pick a lock: not ready (the interface mode did not open)";
            RefreshAreaStatus();
            return;
        }
        _lockpickArmed = item;
        _portalNotice = "pick a lock: press a door or window";
        GD.Print($"ARC-SKILLS-006 lockpicks armed item={item}");
        RefreshAreaStatus();
    }

    /// <summary>ARC-SKILLS-007: the hero repairs one of its items with the Repair skill (the original's skill-bar repair, which
    /// charges no action points and makes the hero the repairer).</summary>
    private SessionRepairResult? _lastRepair;

    private void RepairItem(string item)
    {
        _lastRepair = null;
        if (!InventoryOpen || !InventoryActive) return;
        var holder = _inventoryPanel!.Holder;
        string message;
        if (_inventoryHost!.RepairBaseline(_areaSession!, item, _areaKitDurability) is not { } baseline) message = "not repaired: no condition for this item";
        else
        {
            var result = _areaSession!.RepairWithSkill(_areaSession.Version, item, baseline);
            _lastRepair = result;
            message = result.Disposition == GameSessionDisposition.Rejected ? $"not repaired: {result.Failure}"
                : (result.Repair == Arcanum.Simulation.Items.ItemRepairStatus.NothingToRepair ? "nothing to repair"
                    : result.Amount > 0 ? $"repaired {result.Amount} hit point(s)" : "the repair restored nothing")
                + (result.FateUsed ? " (fate point)" : string.Empty);
            GD.Print($"ARC-SKILLS-007 repair item={item} disposition={result.Disposition} failure={result.Failure} status={result.Repair} amount={result.Amount} " +
                $"damage={result.Condition?.Damage} broken={result.Condition?.Broken}");
        }
        _inventoryStatus = message;
        _inventoryPanel.Render(InventoryView(holder), message);
        RefreshReadyStatus();
    }

    /// <summary>
    /// ARC-UI-015: puts a PC item into a wear slot through the session (the original's drop of a held item on a doll slot, ledger IW3):
    /// the wear check and the occupant's no-drop flag and insert check refuse with a message, an occupant goes back to the pack. The viewer has one
    /// diagnostic body art, so it answers the art lookup with yes (as the best-items start does). Moving, wearing and taking off cost
    /// no action points, in a turn-based round too (IW8). The item's scripts run through the item script host; its item spells through
    /// the cast host's triggers when the map has one.
    /// </summary>
    private void WearItem(string item, EquipmentSlot slot)
    {
        if (!InventoryOpen || !InventoryActive) return;
        var holder = _inventoryPanel!.Holder;
        var session = _areaSession!;
        var name = _inventoryHost!.ItemName(session, item, _hoverNames?.Catalog);
        string message;
        if (_inventoryHost.Wearable(session, item, _areaKitWear) is not { } wearable) message = $"not worn: {name} has no wear values here";
        else
        {
            // The triggers first: building them may rebuild the cast host, which can end a spell goal and change the session.
            var triggers = ItemTriggersNow();
            var result = session.Equip(session.Version, item, slot, wearable, (_, _) => true, _inventoryHost.InsertCheck, _inventoryHost.ItemScripts(session), triggers);
            message = AreaInventoryHost.WearText(result, name, slot, false, entity => _inventoryHost.ItemName(session, entity, _hoverNames?.Catalog));
            GD.Print($"ARC-UI-015 wear item={item} slot={slot} disposition={result.Disposition} failure={result.Failure} refusal={result.Refusal} " +
                $"displaced={result.Displaced ?? "-"} rolled-back={result.RolledBack} scripts={result.Scripts.Count} spells={result.ItemSpells.Count} worn={session.QueryEquipment().Worn.Count}");
        }
        _inventoryStatus = message;
        _inventoryPanel.Render(InventoryView(holder), message);
        RefreshReadyStatus();
    }

    /// <summary>ARC-UI-015: takes a worn item off into the pack (the original's drop of a worn item on the grid, ledger IW4); the insert
    /// check (a Technophobia hero's technological item) and the item's no-drop flag refuse with a message.</summary>
    private void TakeOffItem(EquipmentSlot slot)
    {
        if (!InventoryOpen || !InventoryActive) return;
        var holder = _inventoryPanel!.Holder;
        var session = _areaSession!;
        var worn = session.QueryEquipment().Worn.FirstOrDefault(item => item.Slot == slot);
        var name = worn is null ? AreaInventoryHost.SlotName(slot) : _inventoryHost!.ItemName(session, worn.Entity, _hoverNames?.Catalog);
        var triggers = ItemTriggersNow();
        var result = session.Unequip(session.Version, slot, _inventoryHost!.InsertCheck, _inventoryHost.ItemScripts(session), triggers);
        var message = AreaInventoryHost.WearText(result, name, slot, true, entity => _inventoryHost.ItemName(session, entity, _hoverNames?.Catalog));
        GD.Print($"ARC-UI-015 take-off slot={slot} disposition={result.Disposition} failure={result.Failure} refusal={result.Refusal} " +
            $"scripts={result.Scripts.Count} spells={result.ItemSpells.Count} worn={session.QueryEquipment().Worn.Count}");
        _inventoryStatus = message;
        _inventoryPanel.Render(InventoryView(holder), message);
        RefreshReadyStatus();
    }

    /// <summary>
    /// ARC-UI-019: the held item's release on the doll or the grid (ledger IW10–IW13): a grid item moves to the cell under its grab (the session's
    /// <see cref="GameSessionOwner.MoveHeroItem"/>), a worn item comes off into that cell (<see cref="GameSessionOwner.Unequip"/> with the slot),
    /// either goes into a wear slot (<see cref="GameSessionOwner.Equip"/>, after which the occupant it took out is on the cursor), and a release
    /// anywhere else puts it back. Every refusal leaves the item where it was and the cursor empty. No action points (IW8).
    /// </summary>
    private void DropItem(AreaInventoryDrop drop)
    {
        if (!InventoryOpen || !InventoryActive) return;
        var holder = _inventoryPanel!.Holder;
        var session = _areaSession!;
        var item = drop.Held.Entity;
        var name = _inventoryHost!.ItemName(session, item, _hoverNames?.Catalog);
        string NameOf(string entity) => _inventoryHost.ItemName(session, entity, _hoverNames?.Catalog);
        string message;
        EquipmentMutationResult? result = null;
        switch (drop.Kind)
        {
            case AreaInventoryDropKind.GridMove:
                if (_inventoryHost.GridMoveItem(session, item, _areaKitGridMove) is not { } facts) { message = $"not moved: {name}'s facts do not read here"; break; }
                result = session.MoveHeroItem(session.Version, item, drop.GridSlot!.Value, facts, _inventoryHost.InsertCheck, _inventoryHost.ItemScripts(session));
                message = AreaInventoryHost.GridText(result.Value, name, drop.GridSlot.Value);
                break;
            case AreaInventoryDropKind.TakeOffTo:
                var triggers = ItemTriggersNow();
                result = session.Unequip(session.Version, drop.Held.Worn!.Value, _inventoryHost.InsertCheck, _inventoryHost.ItemScripts(session), triggers, drop.GridSlot);
                message = AreaInventoryHost.GridText(result.Value, name, drop.GridSlot!.Value);
                break;
            case AreaInventoryDropKind.Wear:
                if (_inventoryHost.Wearable(session, item, _areaKitWear) is not { } wearable) { message = $"not worn: {name} has no wear values here"; break; }
                var wearTriggers = ItemTriggersNow();
                result = session.Equip(session.Version, item, drop.Slot!.Value, wearable, (_, _) => true, _inventoryHost.InsertCheck, _inventoryHost.ItemScripts(session), wearTriggers);
                message = AreaInventoryHost.WearText(result.Value, name, drop.Slot.Value, false, NameOf);
                break;
            case AreaInventoryDropKind.DropToGround:
                DropToGround(item);
                return;
            case AreaInventoryDropKind.Take:
                // ARC-UI-052: the holder's item released on the hero's grid is taken as its Take button takes it (PROJECT: into the hero's
                // first free cells, not the cell it was released on; a stack asks how many first, QW1).
                GD.Print($"ARC-UI-052 loot drag take item={item} cell={drop.GridSlot?.ToString() ?? "-"}");
                TakeFromBody(item);
                return;
            case AreaInventoryDropKind.Plant:
                // ARC-UI-065: the hero's item released on the victim's grid in the steal window, or a right release on the hero's item, is one plant
                // attempt (0x575100, IW35; PROJECT: a planted item goes to the victim's first free place, not the released cell).
                GD.Print($"ARC-UI-065 steal drag plant item={item} location={drop.GridSlot?.ToString() ?? "-"}");
                if (holder is not null && holder == _pickPocketVictim) { PickPocketAttempt(Arcanum.Simulation.Skills.PickPocketDirection.Plant, item); return; }
                message = $"{name} went back to its place";
                break;
            case AreaInventoryDropKind.UseIcon:
                // The item goes back to its place and is used: at the hero with a Shift key held, with no target otherwise (IW6).
                UseHeroItem(item, AreaItemUseSource.UseIcon, Godot.Input.IsKeyPressed(Key.Shift));
                return;
            default:
                message = $"{name} went back to its place";
                break;
        }
        GD.Print($"ARC-UI-019 drop kind={drop.Kind} item={item} from={drop.Held.Worn?.ToString() ?? "grid"} grid-slot={drop.GridSlot?.ToString() ?? "-"} " +
            $"slot={drop.Slot?.ToString() ?? "-"} disposition={result?.Disposition.ToString() ?? "-"} failure={result?.Failure.ToString() ?? "-"} " +
            $"verdict={result?.Verdict?.ToString() ?? "-"} displaced={result?.Displaced ?? "-"} grid={session.QueryHeroGrid().Items.Count} worn={session.QueryEquipment().Worn.Count}");
        _inventoryStatus = message;
        _inventoryPanel.Render(InventoryView(holder), message);
        // A wear move's occupant ends up held, in the cell the session gave it (IW3).
        if (drop.Kind == AreaInventoryDropKind.Wear && result is { Disposition: GameSessionDisposition.Changed, Displaced: { } displaced })
            _inventoryPanel.Hold(new(displaced, null, 0));
        RefreshReadyStatus();
    }

    /// <summary>ARC-MAGIC-020: the item spell executors for a wear move, over the shown map's cast host (none without spell data); its item
    /// lookup is the hero's items (ARC-UI-016) and the shown map's NPCs' (ARC-UI-031). ARC-UI-025: the window's item use casts through
    /// it too. ARC-UI-031: the same executors the cast host fires its own life steps' item events through.</summary>
    private ISessionItemTriggers? ItemTriggersNow() =>
        _combatHost is not null && _worldActor.Session is AreaGridSession && SpellCasterNow() is { } caster ? caster.ItemTriggers : null;

    private void EndInventory()
    {
        if (_inventoryPanel is null || OpenInventoryMode is not { } mode) return;
        GD.Print($"ARC-UI-004 inventory closed pc={_areaSession?.QueryPcInventory().Count}");
        var holder = _inventoryPanel.Holder;
        // ARC-UI-029: a question still up goes with the window; nothing moves (ARC-DEV-110: the window's close pops the quantity picker
        // above it, mode 13, and its leave step drops the question).
        CloseWindowMode(mode);
        // ARC-DEV-184: the window's Steal and Plant goals go with it (a skill run walking or waiting, an attempt not yet made).
        if (_pickPocketVictim is not null) { try { _heroRunsHost?.EndSkill("ROUTE_STOPPED_BY_SKILL_END"); } catch (ObjectDisposedException) { /* a replaced session */ } _pickPocketPending = null; _pickPocketTrying = false; }
        _pickPocketVictim = null;
        // ARC-WORLD-063: closing the window on a junk pile runs its dissolve check (0x573440 → 0x463540): one item or none left, it goes.
        var pileSession = _junkPileSession;
        _junkPileSession = null;
        if (SessionJunkPile.Number(holder) is not null && _areaSession is { } session && ReferenceEquals(session, pileSession))
        {
            var closed = session.CloseJunkPile(session.Version, holder!);
            GD.Print($"ARC-WORLD-063 junk-pile close entity={holder} disposition={closed.Disposition} failure={closed.Failure} piles={session.QueryJunkPiles().Count}");
            SyncAreaInventory();
        }
        CloseContainer(holder);
        QuarantineWorld(); RefreshReadyStatus();
    }

    private void TakeFromBody(string item)
    {
        if (!InventoryOpen || !InventoryActive || _inventoryPanel!.Holder is not { } holder) return;
        // ARC-SKILLS-010: in steal mode a take is a theft of the whole item (no quantity window: the drag runs the steal at once).
        if (holder == _pickPocketVictim) { PickPocketAttempt(Arcanum.Simulation.Skills.PickPocketDirection.Steal, item); return; }
        // ARC-UI-029: a gold pile or an ammunition stack of more than one unit asks how many first.
        if (TakeStackFromBody(holder, item)) return;
        var result = _inventoryHost!.Loot(_areaSession!, holder, item);
        if (AreaInventoryHost.Moved(result)) PlayTakenItemSound(item);
        var message = AreaInventoryHost.Moved(result)
            ? $"took {_inventoryHost.Name(item, _hoverNames?.Catalog)}{AreaInventoryHost.Printed(result)}" : $"not taken: {AreaInventoryHost.NotMoved(result)}";
        GD.Print($"ARC-UI-004 loot disposition={result.Disposition} failure={result.Failure} pc={_areaSession!.QueryPcInventory().Count}{InsertScriptLog(result)}");
        _inventoryStatus = message;
        _inventoryPanel.Render(InventoryView(holder), message);
        SyncAreaInventory(); RefreshReadyStatus();
    }

    /// <summary>Takes every item in list order; a refused item stays and is named in the message.</summary>
    private void TakeAllFromBody()
    {
        if (!InventoryOpen || !InventoryActive || _inventoryPanel!.Holder is not { } holder || holder == _pickPocketVictim) return;
        var taken = 0; var refused = new List<string>(); var printed = new System.Text.StringBuilder();
        foreach (var line in InventoryView(holder).Loot)
        {
            // ARC-UI-029: Take all moves a dead NPC's (ARC-WORLD-101: or a container's) gold pile whole without asking (the original's flag 0x20, QW1).
            var result = line.StackKind is { Type: SessionItemType.Gold } && SessionJunkPile.Number(holder) is null
                ? _areaSession!.TakeGoldFromNpc(_areaSession.Version, holder, line.Units, line.Entity)
                : _inventoryHost!.Loot(_areaSession!, holder, line.Entity);
            GD.Print($"ARC-UI-004 loot-all-item disposition={result.Disposition} failure={result.Failure}{InsertScriptLog(result)}");
            // One pickup sound for the whole take (the first item's), not one voice per item in the same frame.
            if (AreaInventoryHost.Moved(result)) { if (taken++ == 0) PlayTakenItemSound(line.Entity); printed.Append(AreaInventoryHost.Printed(result)); }
            else refused.Add($"{line.Text}: {AreaInventoryHost.NotMoved(result)}");
        }
        var message = $"took {taken} item(s){printed}" + (refused.Count == 0 ? string.Empty : "; not taken: " + string.Join("; ", refused));
        GD.Print($"ARC-UI-004 loot-all taken={taken} refused={refused.Count} pc={_areaSession!.QueryPcInventory().Count}");
        _inventoryStatus = message;
        _inventoryPanel.Render(InventoryView(holder), message);
        SyncAreaInventory(); RefreshReadyStatus();
    }

    /// <summary>The original Inventory key (I) acts on its logical key down while an area inventory exists. Returns true
    /// when the key belongs to it, so it is not reported as a reserved, unimplemented original key.</summary>
    private bool RouteInventoryKey(WorldKeyDecision decision)
    {
        if (!InventoryActive || decision.Key != WorldKey.I) return false;
        if (!decision.Pressed || decision.Echo) return true;
        if (!_worldFocused || WorldModal || WorldUiFocus) { _inventoryStatus = "I ignored: palette, dialog or UI focus owns input"; RefreshAreaStatus(); return true; }
        OpenInventory();
        return true;
    }

    /// <summary>While the window is open it owns every key: I and Escape close it on a fresh press.</summary>
    private void HandleInventoryKey(InputEventKey key)
    {
        // ARC-UI-029: while the quantity window asks it owns the keys.
        if (_inventoryPanel?.Asking == true) { HandleQuantityKey(key); return; }
        // ARC-UI-022: a key 1..0 drops the held item on that hotkey entry.
        if (DropHeldOnHotkey(key)) return;
        var routed = key.Keycode != Key.None ? key.Keycode : key.PhysicalKeycode;
        if (key.Pressed && !key.Echo && routed == Key.I && !CharacterKeySilenced(key, routed)) EndInventory();
    }

    private void HandleInventoryPulse(IntentFrame intent)
    {
        if (intent.Cancel && _inventoryPanel!.Asking) _inventoryPanel.CancelQuantity();
        else if (intent.Cancel) EndInventory();
        else if (intent.MenuPressedDirection != 0) _inventoryPanel!.Navigate(intent.MenuPressedDirection);
        else if (intent.Confirm) _inventoryPanel!.Confirm();
    }

    // The status line is rebuilt every frame: the PC's count (ARC-WORLD-027: starting kit included) is read once per
    // session revision.
    private (GameSessionOwner Session, long Revision, int Count)? _inventoryCount;

    private string AreaInventoryStatus()
    {
        if (!InventoryActive) return " | " + _inventoryStatus;
        if (InventoryOpen) return " | inventory open: I closes";
        var session = _areaSession!;
        if (_inventoryCount is not { } cached || !ReferenceEquals(cached.Session, session) || cached.Revision != session.Version.Revision)
            _inventoryCount = cached = (session, session.Version.Revision, session.QueryHeroKit().Count + session.QueryPcInventory().Count);
        return $" | PC items {cached.Count} | {_inventoryStatus}";
    }
}
