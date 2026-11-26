using Arcanum.Data.Area;
using Arcanum.Input;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Locks;
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
        // ARC-DEV-1151: taken (and cleared) before any early return, so a scene that binds no host does not leave the list to a later one.
        var unreadLeft = keepLeft ? _unreadCarriedLeft : [];
        _unreadCarriedLeft = [];
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
        var heldLeft = keepLeft ? _heldRowsLeft : new Dictionary<string, AreaHeldRow>();
        _heldRowsLeft = new Dictionary<string, AreaHeldRow>();
        // The map just loaded is remembered, so a companion carried from it needs no read. A map the resolver reads on demand is read through
        // the same directory (a window of the main map around a stored cell the session recorded).
        if (arguments.AreaMapName is not null && maps.Count == 1) _homes.Remember(AreaHomeRead.Of(maps[0], _ownCompanionTemplates, area.Frames, area.Snapshot));
        var host = new AreaInventoryHost(maps, scripts: content.Content, records: new AreaRememberedRecords(left,
            new AreaMapRecords(arguments, _homes, map => _areaSession is { } owner ? AreaVisitors.HomeWindow(owner, map) : null,
                // ARC-DEV-337 (B52): the window of one companion is read around his own stored cell.
                (map, entity) => _areaSession is { } holder ? AreaCompanionPlacement.HomeCell(holder, new(map), entity) : null)),
            // ARC-DEV-305 (B36, B01): the facts of an item the session made come from its prototype in the same content stack.
            prototypeFields: prototype => PrototypeItems()?.Fields(prototype));
        // ARC-DEV-314 (B01): the rows of the items the hero holds, read from the host of the window the run left (a window of the main map is runtime map 1 each time).
        host.Retain(heldLeft);
        // ARC-DEV-1151: and the items whose row the last host could not read, so a map change does not ask for them again.
        host.RetainUnread(unreadLeft);
        AdoptAreaInventory(host);
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
        // ARC-DEV-366: an item script's add-effect action (the tech books' effects 168 to 175) takes its rule from the hero's effect tables; the
        // field is read at each use, so the tables the character start loads later still answer.
        host.EffectRules = number => _areaEffectRules?.Invoke(number);
        // ARC-UI-2444: the item windows' portrait of an NPC that stores the PC's own one takes the catalogue's next or previous (0x4CEB80).
        host.PortraitCatalogueSource = PortraitCatalogueNow;
        // ARC-SCRIPT-2335: an insert-item script's container vicinity, name test and change art number read the shown map's use-script host.
        host.ShownScripts = () => _readyArea is { } shownArea ? UseScriptHost(shownArea.Snapshot) : null;
        // ARC-WORLD-2357: the substitute container reach (the dialog item hold, the restock merchant search) measures where the shown map's AI host has the NPC now.
        // It reads the built host without building it again (the restock event reaches it mid-advance, as AreaEvents' own host reader does); a group companion not following stands on its own cell (AreaAiHost.Tile).
        host.NpcLiveCell = npc => _aiHost is { } ai && ReferenceEquals(ai.Session, _areaSession) ? ai.Tile(npc) : null;
        host.NpcWearer = (map, npc) => AreaEvents.NpcWieldScripts(map, npc);
        host.NpcWearerFacts = (map, npc) => AreaEvents.RestockWielder(map, npc)?.Wearer;
        // ARC-UI-2159: the weight test of a sale onto a slot of a merchant's doll reads the merchant's build and life state from the shown map's AI host.
        host.NpcSlotInsertCheck = npc => _aiHost is { } ai && ReferenceEquals(ai.Session, _areaSession) ? ai.SlotInsertCheck(npc) : null;
        _inventoryHost = host;
        // ARC-DEV-354: the carried items whose row neither the new host's maps nor the rows handed over from the last one give (a fresh run, a load
        // into another window or map) are read by identity from their origin map.
        host.ReadCarriedRows(_areaSession!);
        _inventoryStatus = "inventory: left-click an item to pick it up or a dead NPC to loot; I opens the window";
        // ARC-COMBAT-005: the combat targets are the seeds' NPCs.
        BindAreaCombat(host);
    }

    // ARC-DEV-075: the record maps of the host a map change tore down, for the next scene of the same run (a new run starts with none).
    private IReadOnlyList<AreaInventoryMap> _recordMapsLeft = [];
    // ARC-DEV-314: the rows and weights of the items the hero held when a map change tore the host down.
    private IReadOnlyDictionary<string, AreaHeldRow> _heldRowsLeft = new Dictionary<string, AreaHeldRow>();
    // ARC-DEV-1151: the carried items (and the origin map asked) whose row the host a map change tore down could not read.
    private IReadOnlyCollection<(string Entity, int Origin)> _unreadCarriedLeft = [];

    private void UnbindAreaInventory()
    {
        if (_inventoryHost is { } leaving)
        {
            _recordMapsLeft = leaving.Resolver.Maps;
            _heldRowsLeft = _areaSession is { } holding ? leaving.HeldRows(holding) : new Dictionary<string, AreaHeldRow>();
            _unreadCarriedLeft = _areaSession is { } asking ? leaving.UnreadCarried(asking) : [];
        }
        EndInventory();
        // ARC-DEV-323: the window no longer clears a theft in progress, so a replaced session drops its "trying" note here.
        _pickPocketTrying = false;
        UnbindAreaCombat();
        _inventoryHost = null; _inventoryHiddenStamp = null; _inventoryHidden = null;
        _canvas?.SetAreaHidden(null);
        _inventoryStatus = "inventory: not loaded";
    }

    /// <summary>Per frame: the taken items of the current map are neither drawn nor picked.</summary>
    private (GameSessionOwner Session, long Revision, int Map, SessionMapInventory View)? _mapInventoryNow;

    /// <summary>
    /// ARC-RENDER-3362: the shown map's inventory at the session's revision, made once for the frame's readers that are keyed on it (the hidden set and the ground bodies): every AI tick moves the
    /// revision, and each of them made the whole map's lists again. A map not attached yet is asked every time.
    /// </summary>
    private SessionMapInventory? AreaMapInventoryNow(GameSessionOwner session, int map)
    {
        var revision = session.Version.Revision;
        if (_mapInventoryNow is { } known && ReferenceEquals(known.Session, session) && known.Revision == revision && known.Map == map) return known.View;
        var view = session.QueryMapInventory(new SessionMapId(map));
        _mapInventoryNow = view is null ? null : (session, revision, map, view);
        return view;
    }

    private void SyncAreaInventory()
    {
        if (_canvas is null) return;
        if (!InventoryActive || _readyArea is null) { SyncAreaHiddenWithoutInventory(); return; }
        var stamp = (_areaSession!, _areaSession!.Version.Revision, CurrentAreaMap);
        if (!Equals(stamp, _inventoryHiddenStamp))
        {
            _inventoryHiddenStamp = stamp;
            var shown = _inventoryHidden;
            var current = AreaMapInventoryNow(_areaSession, CurrentAreaMap);
            _inventoryHidden = _inventoryHost!.Taken(_areaSession, CurrentAreaMap, current);
            // ARC-COMBAT-008: the objects an attack destroyed or switched off are not drawn either.
            if (ObjectTargets()?.Gone(_areaSession) is { Count: > 0 } gone) _inventoryHidden = _inventoryHidden.Concat(gone).ToHashSet();
            // ARC-WORLD-063: a map item swept into a junk pile is drawn as the pile, not where it lay.
            if (_inventoryHost.Piled(CurrentAreaMap, current) is { Count: > 0 } piled) _inventoryHidden = _inventoryHidden.Concat(piled).ToHashSet();
            // ARC-RENDER-3126: the session's revision moves with every AI tick; an unchanged hidden set keeps its reference, so the canvas does not submit the view again for it.
            if (shown is not null && shown.SetEquals(_inventoryHidden)) _inventoryHidden = shown;
        }
        if (!ReferenceEquals(_canvas.AreaHidden, _inventoryHidden)) _canvas.SetAreaHidden(_inventoryHidden);
        // ARC-RENDER-041: a death seen since the last sync leaves its pool of blood first.
        LeaveBloodPoolsForNewDeaths();
        // ARC-RENDER-033: an item picked up or dropped leaves or joins the drawn ground at once.
        if (SyncAreaGroundBodies()) _canvas.SetAreaMovingBodies(AreaMovingCommands());
    }

    private (GameSessionOwner Session, long Revision, object Area)? _goneOnlyStamp;
    private (object Area, AreaObjectTargets Targets)? _goneOnlyTargets;
    private IReadOnlySet<AreaObjectKey>? _goneOnlyHidden;

    /// <summary>
    /// ARC-WORLD-1436: the frame's hidden set when the shown map has no inventory host (a refused seed): the NPCs its data stores off, or that a script switched off or destroyed, are still not drawn. The scene plan keeps
    /// the body of a data-off NPC (ARC-SCRIPT-1360), so the canvas must hide it; the targets are built without an inventory (no container entity then, which the NPC set does not read).
    /// </summary>
    private void SyncAreaHiddenWithoutInventory()
    {
        if (_canvas is null) return;
        if (_readyArea is not { } area || _areaSession is not { } session || _readyMode != TerrainViewerMode.Area)
        {
            _goneOnlyStamp = null; _goneOnlyHidden = null;
            if (_canvas.AreaHidden is not null) _canvas.SetAreaHidden(null);
            return;
        }
        var stamp = (session, session.Version.Revision, (object)area);
        if (!Equals(stamp, _goneOnlyStamp))
        {
            _goneOnlyStamp = stamp;
            if (_goneOnlyTargets is not { } held || !ReferenceEquals(held.Area, area))
                _goneOnlyTargets = held = (area, new AreaObjectTargets(area.Snapshot.RuntimeMap, area.Snapshot.Objects, new Dictionary<AreaObjectKey, (string, SessionOpenableBaseline)>(), null));
            _goneOnlyHidden = held.Targets.Gone(session) is { Count: > 0 } gone ? gone : null;
        }
        if (!ReferenceEquals(_canvas.AreaHidden, _goneOnlyHidden)) _canvas.SetAreaHidden(_goneOnlyHidden);
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

    /// <summary>ARC-WORLD-092: where a map row stands now: a companion on its own actor's committed cell (ARC-COMBAT-1945), an NPC the AI walked on its cell, anything else on its map data's.</summary>
    private (long X, long Y) LiveWorldCell(AreaObjectRecord row) =>
        _areaCompanions?.Companions.FirstOrDefault(c => c.Speaker.Row.Key == row.Key) is { } companion ? _areaCompanions.WorldCell(companion.Speaker) :
        _aiHost?.Scene.Npcs.FirstOrDefault(n => n.Row.Key == row.Key) is { } npc && _aiHost.Movers.IsMoved(npc.Entity) && _aiHost.Movers.Of(npc.Entity) is { } mover
            ? (mover.Cell.X, mover.Cell.Y) : (row.WorldX!.Value, row.WorldY!.Value);

    /// <summary>
    /// A guarded left press (see <c>WorldRouteClick</c>) on a ground item, a container, a junk pile or a dead NPC: true when it was one. ARC-DEV-175 (MIG13 S11c, GS26 and GS27): the click is an <see cref="ObjectClickRequest"/>; the click
    /// rules make it the pickup goal 15 for an item (the hero walks onto its tile through goal 8 with range 0, then goal 16 takes it) or the use goal 81 for a container or a body (S11b: range 1, then the loot window). There is no walk-to-pick-up
    /// intent any more; the act is applied at the frame's next safe point (<see cref="ProcessUsePending"/>). ARC-INPUT-1946: Alt is passed as pressed; out of combat mode it is the immediate action 0x4445A0 (<see cref="AreaHeroPull"/>), which builds no goal and replaces nothing the hero does.
    /// </summary>
    private bool TryAreaInventoryClick(Vector2 screen, bool alt, bool shift, out bool immediate)
    {
        immediate = false;
        if (InventoryOpen || ActorCell() is null || InventoryTargetAt(screen) is not { } target) return false;
        // ARC-INPUT-1946 (0x4445A0): the Alt action builds no goal, so it cancels no approach and starts no run; it moves the item or body at once.
        if (alt && HeroRequests.ClickGoalOf(InventoryTargetOf(target.Row.Key), alt, false) == Arcanum.Simulation.Goals.HeroClickGoal.Immediate)
        {
            var moved = HeroRequests.Apply(new ObjectClickRequest(InventoryTargetOf(target.Row.Key), Alt: true));
            _inventoryStatus = moved.Outcome == HeroRequestOutcome.Done ? "moved onto the hero's tile" : "nothing moved: out of reach (one tile) or not takable";
            GD.Print($"ARC-INPUT-1946 alt-click kind={target.Kind} outcome={moved.Outcome}");
            SyncAreaInventory(); RefreshAreaStatus();
            immediate = true;
            return true;
        }
        CancelPickPocket("an item click replaced it");
        CancelTalkApproach("an item click replaced it"); CancelAreaUseApproach("an item click replaced it"); CancelInventoryApproach(null);
        _useOpenPending = null;
        var verb = target.Kind == AreaInventoryTargetKind.GroundItem ? "pick up" : "loot";
        var request = HeroRequests.Apply(new ObjectClickRequest(InventoryTargetOf(target.Row.Key), Alt: alt, Shift: shift));
        // ARC-DEV-304 (GT115, GT120): a body with the spoken-with-dead spell flag is talked to (goal 21); a body that is no dialog NPC has no window to open (the generic greeting line is not modelled).
        _inventoryStatus = request.Click == Arcanum.Simulation.Goals.HeroClickGoal.Talk ? $"talking to a body spoken with by spell: goal 21 {(request.Outcome == HeroRequestOutcome.Done ? "started" : "refused")} (no dialog NPC, so no window)"
            : request.Click is not (Arcanum.Simulation.Goals.HeroClickGoal.Pickup or Arcanum.Simulation.Goals.HeroClickGoal.UseContainer) ? $"the click does nothing here ({request.Click})"
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
        if (!InventoryActive || InventoryOpen || !WindowCanOpen(mode)) { _containerLootSession = null; _contentsHolder = null; return; }
        if (holder is null || SessionJunkPile.Number(holder) is not null) _containerLootSession = null;
        if (_inventoryPanel is null)
        {
            _inventoryPanel = new AreaInventoryPanel(); AddChild(_inventoryPanel); _inventoryPanel.DiagnosticSwitch = DebugMode;
            _inventoryPanel.CloseRequested += EndInventory;
            _inventoryPanel.TakeRequested += item => { if (!ContentsWindow) TakeFromBody(item); };
            _inventoryPanel.TakeAllRequested += () => { if (!ContentsWindow) TakeAllFromBody(); };
            _inventoryPanel.ArrangeRequested += ArrangeFromWindow;
            _inventoryPanel.StatRequested += ShowStatLines;
            _inventoryPanel.SoundRequested += id => PlayInterfaceSound(id, "inventory arrange button");
            _inventoryPanel.PlantRequested += item => { if (!ContentsWindow) PickPocketAttempt(Arcanum.Simulation.Skills.PickPocketDirection.Plant, item); };
            _inventoryPanel.UseRequested += item => { if (!ContentsWindow) UseItemFromWindow(item); };
            _inventoryPanel.ItemPicked += InventoryItemPicked;
            _inventoryPanel.PickCancelled += () => { if (_itemAiming is not null) CancelArmedLockpicks("right click"); else CancelSpellTargeting("right click"); };
            _inventoryPanel.PointerStill += InventoryPointerStill;
            _inventoryPanel.ReadRequested += item => { if (!ContentsWindow) ReadItem(item); };
            _inventoryPanel.PickRequested += item => { if (!ContentsWindow) ArmLockpick(item); };
            _inventoryPanel.RepairRequested += item => { if (!ContentsWindow) RepairItem(item); };
            _inventoryPanel.WearRequested += (item, slot) => { if (!ContentsWindow) WearItem(item, slot); };
            _inventoryPanel.TakeOffRequested += slot => { if (!ContentsWindow) TakeOffItem(slot); };
            _inventoryPanel.DropRequested += drop => { if (!ContentsWindow) DropItem(drop); };
            _inventoryPanel.MapDropRequested += held => { if (!ContentsWindow) LiftToGameCursor(held.Entity, "round-view"); };
            _inventoryPanel.QuantityAccepted += AcceptQuantity;
            _inventoryPanel.QuantityCancelled += CancelQuantity;
            _inventoryPanel.HolderView = InventoryHolderView;
            _inventoryPanel.PortraitTextures = HolderPortraitTexture;
        }
        // A quick key held across the window must not act on its release afterwards (its press predates the window).
        QuarantineWorld(); _quickKeys.Reset();
        // ARC-DEV-339: the arrange button's layout flag is cleared at every open (0x57341F, ledger IW37): the first press lays out row by row.
        _arrangeLayout = InventoryArrangeLayout.Horizontal;
        // ARC-WORLD-063: a junk pile's window closes against the session it was opened on.
        _junkPileSession = SessionJunkPile.Number(holder) is not null ? _areaSession : null;
        // ARC-UI-051: the player's own window shows the original look when the game's art read.
        _inventoryPanel.SetArtLook(InventoryArtLook());
        // ARC-UI-054: the open centres the game view on the window's holder, the PC (0x5732D5 → 0x4B8CE0, IW33).
        CentreGameViewOnHolder();
        // ARC-RES-1410 (ledger XR16 (g), IW39): the open of the loot, steal and See Contents windows lays the holder's grid out again (0x5727B0 calls
        // 0x4670A0 near its end, after its early returns) before the window draws; the stack's enter step is that point here. A refusal (no room) writes nothing.
        void Show()
        {
            if (holder is not null && _areaSession is { } arranging)
            {
                var arranged = arranging.ArrangeHolderGrid(arranging.Version, holder);
                GD.Print($"ARC-RES-1410 arrange at open holder={holder} disposition={arranged.Disposition} failure={arranged.Failure} no-room={arranged.NoRoom} moved={arranged.Moved}");
            }
            _inventoryPanel!.Open(InventoryView(holder));
        }
        if (!OpenWindowMode(mode, Show, () => _inventoryPanel!.CloseView()))
        {
            _junkPileSession = null; _contentsHolder = null;
            return;
        }
        // ARC-WORLD-096: a loot open schedules the looted holder's restock (RK2; a junk pile is no seed holder and has none).
        if (holder is not null) ScheduleRestock(holder, "loot");
        GD.Print($"ARC-UI-004 inventory open holder={(holder is null ? "none" : SessionJunkPile.Number(holder) is not null ? "junk-pile" : ReferenceEquals(_containerLootSession, _areaSession) ? "container" : holder == _pickPocketVictim ? "steal" : "dead-npc")} pc={_areaSession!.QueryPcInventory().Count} loot={_inventoryPanel.ViewForSelfTest!.Loot.Count}");
        RefreshReadyStatus();
    }

    /// <summary>ARC-UI-054, ARC-UI-098: a window with a round view (the inventory, the barter window, the logbook, IW33, LB23) centres the game view on its
    /// holder, the PC, when it opens, at the current zoom; the palette's Fit view ends there at the preferred zoom, as at the other centrings.</summary>
    private void CentreGameViewOnHolder()
    {
        if (_camera is null) return;
        var zoom = _worldFitView ? _worldPreferredZoom : _camera.State.Zoom;
        _worldFitView = false; _camera.ResetWorld(WorldAnchor, WorldViewport, zoom);
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
        // ARC-UI-1121 (B30, IW29): the body's doll view prints the damage and armour ratings the combat host reads for it and fills a too heavy weapon's slot.
        with { Steal = holder is not null && holder == _pickPocketVictim,
               // ARC-RES-1327 (XR3, XR16): the container window See Contents opened (window mode 4) moves nothing.
               Contents = holder is not null && holder == _contentsHolder,
               BodyRatings = holder is not null && _combatHost is { } combat && combat.Covers(CurrentAreaMap) ? combat.DollRatings(_areaSession!, holder) : null,
               // ARC-UI-1267 (IW25): the hero's own window prints its damage and armour ratings and fills a too heavy weapon's slot. ARC-UI-1918 (IW26): so do the windows of modes 1 to 4 once
               // the hero's doll toggle is set.
               HeroRatings = _combatHost?.HeroDollRatings(_areaSession!) };

    // The last heal's message, for the item cursor's notice (the cursor reads it after the use returns; no window is open then).
    private string? _healMessage;
    /// <summary>The last heal's target and the session's answer, for the smoke.</summary>
    internal (string Target, SessionHealResult Result)? LastHealForSelfTest { get; private set; }

    /// <summary>ARC-SKILLS-005: the hero uses a healing item on a critter: itself (the inventory use passes the user as the target, as the
    /// original's inventory window does) or, since ARC-UI-1127 (E8-g), another critter the item cursor picked (a follower, an NPC). ARC-UI-025: the item use (0x462CC0) reaches it as the heal skill with the item and has charged
    /// the use's action points already, so the heal charges none (<paramref name="charged"/>). The answer is the HUD message; an open inventory window shows it too.</summary>
    private void HealCritter(string item, string target, bool charged = false)
    {
        if (_areaSession is not { } session) return;
        var shown = InventoryOpen && InventoryActive;
        var holder = shown ? _inventoryPanel!.Holder : null;
        var healing = _inventoryHost!.HealingItem(session, item, _areaKitHealing);
        var life = target == AiSchedulerRules.Pc ? SessionLifeTarget.Hero : SpellCasterNow()?.LifeTarget(target);
        string message;
        if (healing is null) message = "not used: not a healing item the hero holds";
        else if (life is null) message = "not used: the target has no life record here";
        else
        {
            ResumeLoadedRound();
            var result = session.HealWithItem(session.Version, life, healing, charged ? null : CombatWorldOrNull());
            LastHealForSelfTest = (target, result);
            message = result.Disposition == GameSessionDisposition.Rejected ? $"not used: {result.Failure}"
                : (result.Check!.Success ? $"healed {result.Heal!.Value.HitPoints} hit point(s){(result.Heal.Value.Cure ? " and cured an injury" : string.Empty)}" : "the heal failed")
                + (result.ItemDestroyed ? "; the item is used up" : $"; {result.UsesLeft} use(s) left") + (result.FateUsed ? " (fate point)" : string.Empty);
            GD.Print($"ARC-SKILLS-005 heal target={target} disposition={result.Disposition} failure={result.Failure} success={result.Check?.Success} critical={result.Check?.Critical} " +
                $"healed={result.Heal?.HitPoints} cure={result.Heal?.Cure} uses-left={result.UsesLeft?.ToString() ?? "-"} destroyed={result.ItemDestroyed} " +
                $"hp-damage={session.QueryHeroLife()?.Record.State.HitPointDamage}");
            if (!life.IsHero && result.Disposition != GameSessionDisposition.Rejected) message = $"{target}: {message}";
        }
        _healMessage = message;
        if (shown) { _inventoryStatus = message; _inventoryPanel!.Render(InventoryView(holder), message); }
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
        // ARC-INPUT-1123 (IA4, SA52): the opener asks the action points (0x4B7790, cost 4: only in a turn-based round and only on the hero's own
        // subturn) after the window closed and the mode below was popped, and pushes mode 16 only when they allow it; the lockpicks' mask is
        // the item mask of an item with no spell (IA3, any object), so the begin is the item cursor's. A refusal leaves the interface on the base.
        var mask = AreaItemAiming.Mask(false, false, null);
        if (AreaItemAiming.Begin(mask, true, false, _areaSession!.QueryActionPointCheck(SessionCombatant.Hero, TurnBasedRules.UseItemCost)) == AreaItemAimBegin.NoActionPoints)
        {
            _portalNotice = "pick a lock: not enough action points";
            GD.Print("ARC-INPUT-1123 lockpicks refused: not enough action points");
            RefreshAreaStatus();
            return;
        }
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
        // ARC-UI-090: the hero's gold pile moves in the grid like an item (ledger HG1). ARC-UI-091: the drop icon drops coins (the quantity window
        // first); a release anywhere else puts it back (a throw and a sale of coins are NOT READY).
        if (item == SessionHeroGrid.GoldEntity && drop.Kind == AreaInventoryDropKind.DropToGround) { DropToGround(item); return; }
        if (item == SessionHeroGrid.GoldEntity && drop.Kind != AreaInventoryDropKind.GridMove)
        {
            _inventoryStatus = $"{name} went back to its place (a sale of coins is NOT READY)";
            _inventoryPanel.Render(InventoryView(holder), _inventoryStatus);
            return;
        }
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
            case AreaInventoryDropKind.Give:
                GiveToHolder(drop);
                return;
            case AreaInventoryDropKind.TakeWear:
                TakeWearFromHolder(drop);
                return;
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
        FinishInventoryClose(holder);
    }

    /// <summary>
    /// The close of the inventory family's window after the stack took it off: the theft goes on, a junk pile dissolves, the container's lid closes, the input gate closes. ARC-MAGIC-1336: also the tail of a
    /// loot window a spell's cursor took with it when it was cancelled (SA38: the leave case of mode 1 pops the window below it too, and the stack's hide step only hides the panel).
    /// </summary>
    private void FinishInventoryClose(string? holder)
    {
        // ARC-DEV-323 (SL13): the window's close ends no goal. The original's close (0x573440, the steal mode's leave case) calls nothing of the goal table, so a Steal or Plant
        // that walks to the victim (goal 33) or waits for its attempt (86) goes on without the window and makes its attempt as it does after a load (ProcessPickPocketPending);
        // ARC-DEV-184 ended it here. What ends such a run is a new order, a cancel or its own end.
        var keepsTheft = _pickPocketVictim is not null;
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
        // The viewer's input gate closes with the window (its own click must not walk the hero), but its route stop would interrupt the hero's runs: a theft that walks keeps them (ARC-DEV-323).
        if (keepsTheft) { _quarantineKeepsRuns = true; try { QuarantineWorld(); } finally { _quarantineKeepsRuns = false; } }
        else QuarantineWorld();
        RefreshReadyStatus();
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

    // ARC-DEV-339 (B54): the layout the arrange button's next press uses (the original's flag 0x6814F0: 0 row by row, 1 column by column).
    private InventoryArrangeLayout _arrangeLayout;

    /// <summary>ARC-DEV-339 (B54): the arrange button's release (0x573AA5..0x573B2D, ledger IW37): the hero's items are laid out again in the
    /// current layout (<see cref="GameSessionOwner.ArrangeHeroGrid"/>: sorted by size, no insert check, no event, no action points), then the flag
    /// flips whatever came of it. The original prints nothing; the status line is PROJECT. ARC-UI-3198 (IW38): with <paramref name="companion"/> (the barter
    /// partner when it is the hero's follower, flag 0x6810FC) the same layout also arranges that holder's grid (<see cref="GameSessionOwner.ArrangeHolderGrid"/>, the
    /// same rules, no checks, no events, no action points) before the one flip.</summary>
    private string ArrangeHero(GameSessionOwner session, string? companion = null)
    {
        var layout = _arrangeLayout;
        var result = session.ArrangeHeroGrid(session.Version, layout);
        var partner = companion is null ? null : session.ArrangeHolderGrid(session.Version, companion, layout);
        _arrangeLayout = layout == InventoryArrangeLayout.Horizontal ? InventoryArrangeLayout.Vertical : InventoryArrangeLayout.Horizontal;
        GD.Print($"ARC-DEV-339 arrange layout={layout} disposition={result.Disposition} failure={result.Failure} no-room={result.NoRoom} moved={result.Moved} " +
            $"next={_arrangeLayout} grid={string.Join(' ', session.QueryHeroGrid().Items.Select(placed => $"{placed.Entity}@{placed.Slot}:{placed.Width}x{placed.Height}"))}");
        if (partner is not null)
            GD.Print($"ARC-UI-3198 arrange companion={companion} layout={layout} disposition={partner.Disposition} failure={partner.Failure} no-room={partner.NoRoom} moved={partner.Moved} " +
                $"grid={string.Join(' ', (session.QueryHolderItems(companion!) ?? []).Select(item => $"{item.Entity}@{item.Facts.InventoryLocation}"))}");
        var how = layout == InventoryArrangeLayout.Horizontal ? "row by row" : "column by column";
        var text = result.Disposition == GameSessionDisposition.Rejected ? $"not arranged: {result.Failure}" : result.NoRoom ? "not arranged: an item finds no place"
            : result.Moved == 0 ? $"items already lie {how}" : $"arranged {result.Moved} item(s) {how}";
        return partner is null ? text : text + (partner.Disposition == GameSessionDisposition.Rejected ? $"; companion not arranged: {partner.Failure}" : partner.NoRoom ? "; companion: an item finds no place"
            : partner.Moved == 0 ? "; companion's items already lie so" : $"; companion: {partner.Moved} item(s) arranged");
    }

    private void ArrangeFromWindow()
    {
        if (!InventoryOpen || !InventoryActive || _inventoryPanel!.Held is not null) return;
        _inventoryStatus = ArrangeHero(_areaSession!);
        _inventoryPanel.Render(InventoryView(_inventoryPanel.Holder), _inventoryStatus);
        RefreshReadyStatus();
    }

    /// <summary>Takes every item in list order; a refused item stays and is named in the message.</summary>
    private void TakeAllFromBody()
    {
        if (!InventoryOpen || !InventoryActive || _inventoryPanel!.Holder is not { } holder || holder == _pickPocketVictim) return;
        var taken = 0; var refused = new List<string>(); var printed = new System.Text.StringBuilder();
        foreach (var line in InventoryView(holder).Loot)
        {
            // ARC-UI-029: Take all moves a dead NPC's (ARC-WORLD-101: or a container's; ARC-UI-1328: or a junk pile's) gold pile whole without asking (the original's flag 0x20, QW1).
            var result = line.StackKind is { Type: SessionItemType.Gold }
                ? SessionJunkPile.Number(holder) is not null ? _areaSession!.TakeGoldFromJunkPile(_areaSession.Version, holder, line.Entity, line.Units, _inventoryHost!.ItemScripts(_areaSession))
                : _areaSession!.TakeGoldFromNpc(_areaSession.Version, holder, line.Units, line.Entity, _inventoryHost!.ItemScripts(_areaSession))
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
