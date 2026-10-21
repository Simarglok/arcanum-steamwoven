using Arcanum.Data.Area;
using Arcanum.Input;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.TerrainViewer.Core;
using GamepadLab.Core;
using Godot;

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
    // A pickup or loot clicked from too far: the click route walks within reach first, then the same action runs once.
    private (AreaObjectKey Key, AreaInventoryTargetKind Kind)? _inventoryAfterRoute;
    private (GameSessionOwner Session, long Revision, int Map)? _inventoryHiddenStamp;
    private IReadOnlySet<AreaObjectKey>? _inventoryHidden;
    private bool InventoryOpen => _inventoryPanel?.IsOpen == true;
    private bool InventoryActive => _inventoryHost is not null && _areaSession is not null && _readyMode == TerrainViewerMode.CrashSiteArea;

    /// <summary>Builds the seeds of the exterior and (when loaded) the cave and attaches them to the new area session.
    /// A refused build leaves the inventory unavailable; the rest of the area runs as before. ARC-SCRIPT-013: the game's
    /// script content (shared with the conversations) runs an item's insert-item script on a transfer.</summary>
    private void BindAreaInventory(TerrainViewerArguments arguments, TerrainLoadResult result)
    {
        UnbindAreaInventory();
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
        AdoptAreaInventory(new AreaInventoryHost(maps, scripts: content.Content));
    }

    // The session has no detach: when a later map's seed is refused, an earlier one stays attached but unused (seeds are
    // never saved, and the dialog port and clicks need _inventoryHost).
    private void AdoptAreaInventory(AreaInventoryHost host)
    {
        var failure = host.Attach(_areaSession!);
        if (failure != GameSessionFailure.None) { _inventoryStatus = $"inventory: unavailable (seed {failure})"; return; }
        _inventoryHost = host;
        _inventoryStatus = "inventory: left-click an item to pick it up or a dead NPC to loot; I opens the window";
        // ARC-COMBAT-005: the combat targets are the seeds' NPCs.
        BindAreaCombat(host);
    }

    private void UnbindAreaInventory()
    {
        EndInventory();
        UnbindAreaCombat();
        _inventoryHost = null; _inventoryAfterRoute = null; _inventoryHiddenStamp = null; _inventoryHidden = null;
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
        }
        if (!ReferenceEquals(_canvas.AreaHidden, _inventoryHidden)) _canvas.SetAreaHidden(_inventoryHidden);
    }

    private bool AreaTakenByPc(AreaDrawCommand command) => command.Identity is { } key && _inventoryHidden?.Contains(key) == true;

    /// <summary>The pickup or loot target that is the frontmost pickable object under the screen point, if any.</summary>
    private AreaInventoryTarget? InventoryTargetAt(Vector2 screen)
    {
        if (!InventoryActive || AreaObjectAtScreen(screen) is not { } key) return null;
        return _inventoryHost!.Target(_areaSession!, CurrentAreaMap, key);
    }

    private static long Chebyshev(AreaObjectRecord row, (long X, long Y) cell) =>
        Math.Max(Math.Abs(row.WorldX!.Value - cell.X), Math.Abs(row.WorldY!.Value - cell.Y));

    /// <summary>A guarded left press (see <c>WorldRouteClick</c>) on a ground item or a dead NPC: true when it was one.</summary>
    private bool TryAreaInventoryClick(Vector2 screen)
    {
        if (InventoryOpen || ActorCell() is not { } cell || InventoryTargetAt(screen) is not { } target) return false;
        CancelTalkApproach("an item click replaced it"); CancelAreaUseApproach("an item click replaced it"); CancelInventoryApproach(null);
        var distance = Chebyshev(target.Row, cell);
        var verb = target.Kind == AreaInventoryTargetKind.GroundItem ? "pick up" : "loot";
        if (distance <= AreaInventoryHost.Reach)
        {
            StopWorldRoute(target.Kind == AreaInventoryTargetKind.GroundItem ? "ROUTE_REPLACED_BY_PICKUP" : "ROUTE_REPLACED_BY_LOOT");
            ActOnInventoryTarget(target);
            return true;
        }
        if (_worldActor.Session is AreaGridSession session && WorldRoute(session) is { } route
            && ApproachCell(session, target.Row.WorldX!.Value, target.Row.WorldY!.Value, AreaInventoryHost.Reach) is { } goal)
        {
            StopWorldRoute(target.Kind == AreaInventoryTargetKind.GroundItem ? "ROUTE_REPLACED_BY_PICKUP" : "ROUTE_REPLACED_BY_LOOT");
            route.SetTarget(goal, WorldTime); _inventoryAfterRoute = (target.Row.Key, target.Kind);
            _worldRouteDiagnostic = DescribeWorldRoute(route); _worldRouteClick = $"{goal.X},{goal.Y} walk to {verb}";
            _inventoryStatus = $"walking to {verb}: {distance} cells away, goal {goal.X},{goal.Y}";
            GD.Print($"ARC-UI-004 {verb.Replace(' ', '-')}-approach distance={distance} goal={goal}");
        }
        else _inventoryStatus = $"cannot {verb}: {distance} cells away and no standable cell within {AreaInventoryHost.Reach}";
        RefreshAreaStatus();
        return true;
    }

    private void ActOnInventoryTarget(AreaInventoryTarget target)
    {
        if (target.Kind == AreaInventoryTargetKind.DeadNpc) { OpenInventory(target.Entity); return; }
        var result = _inventoryHost!.PickUp(_areaSession!, target.Entity);
        var name = _inventoryHost.Name(target.Entity, _hoverNames?.Catalog);
        _inventoryStatus = result.Disposition == GameSessionDisposition.Changed
            ? $"picked up {name}{AreaInventoryHost.Printed(result)}" : $"pickup refused: {AreaInventoryHost.Refusal(result.Failure)}";
        if (result.Disposition == GameSessionDisposition.Changed) PlayPickupSound(target.Entity);
        GD.Print($"ARC-UI-004 pickup disposition={result.Disposition} failure={result.Failure} pc={_areaSession!.QueryPcInventory().Count}{InsertScriptLog(result)}");
        SyncAreaInventory(); RefreshAreaStatus();
    }

    // ARC-SCRIPT-013: how an item's insert-item script ended and how many lines it printed (the text stays on screen only).
    private static string InsertScriptLog(InventoryMutationResult result) =>
        result.InsertScript is { } run ? $" insert-script={run.Script} stop={run.Stop} lines={result.Messages.Count}" : string.Empty;

    private void CancelInventoryApproach(string? why)
    {
        if (_inventoryAfterRoute is null) return;
        _inventoryAfterRoute = null;
        if (why is not null) _inventoryStatus = "walk to pick up/loot cancelled: " + why;
    }

    /// <summary>Runs the pickup or loot once a walk to it has ended settled; dropped under the click guards.</summary>
    private void ProcessInventoryAfterRoute()
    {
        if (_inventoryAfterRoute is not { } pending || _worldActor.Session is not AreaGridSession walking || walking.State.Phase != MovementPhase.Settled
            || !ReferenceEquals(_worldRouteSession, walking) || _worldRoute is not { } route || route.Status == RouteFollowStatus.Following) return;
        _inventoryAfterRoute = null;
        var cell = ActorCell()!.Value;
        var target = InventoryActive ? _inventoryHost!.Target(_areaSession!, CurrentAreaMap, pending.Key) : null;
        if (_worldLifecycleSession is not null || _worldResetPending || _worldReloadPending) _inventoryStatus = "walk to pick up/loot cancelled: reset/reload";
        else if (route.Status != RouteFollowStatus.Arrived) _inventoryStatus = "walk to pick up/loot stopped: " + (route.StopReason ?? route.Status.ToString());
        else if (!_worldFocused || WorldModal || WorldUiFocus) _inventoryStatus = "walk to pick up/loot dropped: input quarantined/modal";
        else if (target is null || target.Kind != pending.Kind) _inventoryStatus = "walk to pick up/loot ended: the target is gone";
        else if (Chebyshev(target.Row, cell) > AreaInventoryHost.Reach) _inventoryStatus = "walk to pick up/loot ended out of reach";
        else { ActOnInventoryTarget(target); return; }
        RefreshAreaStatus();
    }

    /// <summary>Opens the window on the PC's list, or (with <paramref name="holder"/>) on a dead NPC to loot.</summary>
    private void OpenInventory(string? holder = null)
    {
        if (!InventoryActive || InventoryOpen) return;
        if (_inventoryPanel is null)
        {
            _inventoryPanel = new AreaInventoryPanel(); AddChild(_inventoryPanel);
            _inventoryPanel.CloseRequested += EndInventory;
            _inventoryPanel.TakeRequested += TakeFromBody;
            _inventoryPanel.TakeAllRequested += TakeAllFromBody;
            _inventoryPanel.UseRequested += HealSelf;
            _inventoryPanel.ReadRequested += ReadItem;
            _inventoryPanel.PickRequested += ArmLockpick;
            _inventoryPanel.RepairRequested += RepairItem;
        }
        // A quick key held across the window must not act on its release afterwards (its press predates the window).
        QuarantineWorld(); _quickKeys.Reset();
        _inventoryPanel.Open(InventoryView(holder));
        GD.Print($"ARC-UI-004 inventory open holder={(holder is null ? "none" : "dead-npc")} pc={_areaSession!.QueryPcInventory().Count} loot={_inventoryPanel.ViewForSelfTest!.Loot.Count}");
        RefreshReadyStatus();
    }

    private AreaInventoryView InventoryView(string? holder) => _inventoryHost!.View(_areaSession!, _hoverNames?.Catalog, CurrentAreaMap, holder, _areaKitHealing, _areaKitLockpicks, _areaKitDurability);

    /// <summary>ARC-SKILLS-005: the hero uses a healing item on itself (the inventory use passes the user as the target, as the
    /// original's inventory window does). Outside a turn-based round, so no action points are charged.</summary>
    private void HealSelf(string item)
    {
        if (!InventoryOpen || !InventoryActive) return;
        var holder = _inventoryPanel!.Holder;
        var healing = _inventoryHost!.HealingItem(_areaSession!, item, _areaKitHealing);
        string message;
        if (healing is null) message = "not used: not a healing item the hero holds";
        else
        {
            var result = _areaSession!.HealWithItem(_areaSession.Version, SessionLifeTarget.Hero, healing);
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
        EndInventory();
        CancelPendingWalkOrder("lockpicks armed");
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

    private void EndInventory()
    {
        if (_inventoryPanel is null || !_inventoryPanel.IsOpen) return;
        GD.Print($"ARC-UI-004 inventory closed pc={_areaSession?.QueryPcInventory().Count}");
        _inventoryPanel.CloseView();
        QuarantineWorld(); RefreshReadyStatus();
    }

    private void TakeFromBody(string item)
    {
        if (!InventoryOpen || !InventoryActive || _inventoryPanel!.Holder is not { } holder) return;
        var result = _inventoryHost!.Loot(_areaSession!, holder, item);
        if (result.Disposition == GameSessionDisposition.Changed) PlayTakenItemSound(item);
        var message = result.Disposition == GameSessionDisposition.Changed
            ? $"took {_inventoryHost.Name(item, _hoverNames?.Catalog)}{AreaInventoryHost.Printed(result)}" : $"not taken: {AreaInventoryHost.Refusal(result.Failure)}";
        GD.Print($"ARC-UI-004 loot disposition={result.Disposition} failure={result.Failure} pc={_areaSession!.QueryPcInventory().Count}{InsertScriptLog(result)}");
        _inventoryStatus = message;
        _inventoryPanel.Render(InventoryView(holder), message);
        SyncAreaInventory(); RefreshReadyStatus();
    }

    /// <summary>Takes every item in list order; a refused item stays and is named in the message.</summary>
    private void TakeAllFromBody()
    {
        if (!InventoryOpen || !InventoryActive || _inventoryPanel!.Holder is not { } holder) return;
        var taken = 0; var refused = new List<string>(); var printed = new System.Text.StringBuilder();
        foreach (var line in InventoryView(holder).Loot)
        {
            var result = _inventoryHost!.Loot(_areaSession!, holder, line.Entity);
            GD.Print($"ARC-UI-004 loot-all-item disposition={result.Disposition} failure={result.Failure}{InsertScriptLog(result)}");
            // One pickup sound for the whole take (the first item's), not one voice per item in the same frame.
            if (result.Disposition == GameSessionDisposition.Changed) { if (taken++ == 0) PlayTakenItemSound(line.Entity); printed.Append(AreaInventoryHost.Printed(result)); }
            else refused.Add($"{line.Text}: {AreaInventoryHost.Refusal(result.Failure)}");
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
        var routed = key.Keycode != Key.None ? key.Keycode : key.PhysicalKeycode;
        if (key.Pressed && !key.Echo && routed is Key.I or Key.Escape) EndInventory();
    }

    private void HandleInventoryPulse(IntentFrame intent)
    {
        if (intent.Cancel) EndInventory();
        else if (intent.MenuPressedDirection != 0) _inventoryPanel!.Navigate(intent.MenuPressedDirection);
        else if (intent.Confirm) _inventoryPanel!.Confirm();
    }

    // The status line is rebuilt every frame: the PC's count (ARC-WORLD-027: starting kit included) is read once per
    // session revision.
    private (GameSessionOwner Session, long Revision, int Count)? _inventoryCount;

    private string AreaInventoryStatus()
    {
        if (!InventoryActive) return " | " + _inventoryStatus;
        if (InventoryOpen) return " | inventory open: I or Escape closes";
        var session = _areaSession!;
        if (_inventoryCount is not { } cached || !ReferenceEquals(cached.Session, session) || cached.Revision != session.Version.Revision)
            _inventoryCount = cached = (session, session.Version.Revision, session.QueryHeroKit().Count + session.QueryPcInventory().Count);
        return $" | PC items {cached.Count} | {_inventoryStatus}";
    }
}
