using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.Simulation.Skills;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-SKILLS-010: the pick-pocket skill in map mode (contract docs/world/pick-pocket-runtime.md). The palette's "Pick pocket" arms the
// skill (the skill bar's button stays a later task); the next left press on an NPC walks within reach 1, then the start's rules run
// (PP1): a stoned target or the hero itself is refused, a "no pickpocket" or mechanical critter floats skill_ui.mes line 525, a dead one
// is looted as any body, and a living one the hero sees opens the inventory window in steal mode. There each Steal button and each
// Plant button is one attempt through the session (4 action points in the hero's subturn); a noticed failure runs the victim's
// catching-thief script, and unless it or the victim's leadership stops it, the attack event of the hero on the victim, and closes the
// window. The original opens the window at sight distance and walks to range 1 on each drag; the viewer walks first.
public partial class TerrainViewerApp
{
    // ARC-DEV-110 (MIG16 S3): the armed skill is interface mode 2 (skill target) on the viewer's stack, read as PickPocketArmed.
    // The NPC whose pocket the window shows in steal mode, and the press waiting for its walk.
    private string? _pickPocketVictim;
    private AreaObjectKey? _pickPocketAfterRoute;

    /// <summary>The last attempt's result and consequences, for the smoke.</summary>
    private (SessionPickPocketResult Result, AreaPickPocketCaught? Caught)? _lastPickPocket;
    private AreaPickPocketStart? _lastPickPocketStart;

    /// <summary>The palette's Pick pocket: the next left press on an NPC picks its pocket.</summary>
    private void ArmPickPocket()
    {
        if (_areaSession?.QueryHero() is null || !InventoryActive) { _inventoryStatus = "pick pocket: no hero or no area inventory"; RefreshAreaStatus(); return; }
        if (AreaAiHostNow() is null) { _inventoryStatus = "pick pocket: not ready (the map's AI host is off)"; RefreshAreaStatus(); return; }
        CancelArmedLockpicks("pick pocket armed");
        CancelSpellTargeting("pick pocket armed");
        CancelPendingOrder("pick pocket armed");
        if (!OpenTargetMode(InterfaceMode.SkillTarget)) { _inventoryStatus = "pick pocket: not ready (a window is open)"; RefreshAreaStatus(); return; }
        _inventoryStatus = "pick pocket: press an NPC";
        GD.Print("ARC-SKILLS-010 pick-pocket armed");
        RefreshAreaStatus();
    }

    private void CancelPickPocket(string why)
    {
        if (!PickPocketArmed && _pickPocketAfterRoute is null) return;
        if (PickPocketArmed) CloseTargetMode(InterfaceMode.SkillTarget);
        _pickPocketAfterRoute = null;
        _inventoryStatus = "pick pocket cancelled: " + why;
    }

    /// <summary>The armed press: an NPC of the map's seed walks within reach 1 or starts at once; anything else cancels the skill.</summary>
    private void PickPocketPress(Vector2 screen)
    {
        // The press leaves the skill's target mode (SA51: pick pocket leaves it and opens the steal window).
        if (PickPocketArmed) CloseTargetMode(InterfaceMode.SkillTarget);
        if (InventoryOpen || ActorCell() is not { } cell || AreaObjectAtScreen(screen) is not { } key || _inventoryHost?.Map(CurrentAreaMap) is not { } map
            || !map.TryGetEntity(key, out var npc) || !map.Seed.Holders.Any(h => h.Entity == npc && h.Kind == SessionHolderKind.Npc))
        {
            _inventoryStatus = "pick pocket cancelled: not an NPC"; RefreshAreaStatus(); return;
        }
        CancelTalkApproach("a pick-pocket press replaced it"); CancelAreaUseApproach("a pick-pocket press replaced it"); CancelInventoryApproach(null);
        var row = map.Row(npc);
        var distance = Chebyshev(row, cell);
        if (distance <= AreaInventoryHost.Reach) { StopWorldRoute("ROUTE_REPLACED_BY_PICK_POCKET"); StartPickPocket(npc); return; }
        var (x, y) = LiveWorldCell(row);
        if (_worldActor.Session is AreaGridSession session && WorldRoute(session) is { } route && ApproachCell(session, x, y, AreaInventoryHost.Reach) is { } goal)
        {
            StopWorldRoute("ROUTE_REPLACED_BY_PICK_POCKET");
            route.SetTarget(goal, WorldTime); _pickPocketAfterRoute = key;
            _worldRouteDiagnostic = DescribeWorldRoute(route); _worldRouteClick = $"{goal.X},{goal.Y} walk to pick a pocket";
            _inventoryStatus = $"walking to pick a pocket: {distance} cells away";
            SetObjectApproachGait();
        }
        else _inventoryStatus = $"cannot pick that pocket: {distance} cells away and no standable cell within {AreaInventoryHost.Reach}";
        RefreshAreaStatus();
    }

    /// <summary>Starts the attempt once the walk to the NPC has ended settled; dropped under the click guards.</summary>
    private void ProcessPickPocketAfterRoute()
    {
        if (_pickPocketAfterRoute is not { } key || _worldActor.Session is not AreaGridSession walking || walking.State.Phase != MovementPhase.Settled
            || !ReferenceEquals(_worldRouteSession, walking) || _worldRoute is not { } route || route.Status == RouteFollowStatus.Following) return;
        _pickPocketAfterRoute = null;
        var cell = ActorCell()!.Value;
        if (_worldLifecycleSession is not null || _worldResetPending || _worldReloadPending) _inventoryStatus = "walk to pick a pocket cancelled: reset/reload";
        else if (route.Status != RouteFollowStatus.Arrived) _inventoryStatus = "walk to pick a pocket stopped: " + (route.StopReason ?? route.Status.ToString());
        else if (!_worldFocused || WorldModal || WorldUiFocus) _inventoryStatus = "walk to pick a pocket dropped: input quarantined/modal";
        else if (_inventoryHost?.Map(CurrentAreaMap) is not { } map || !map.TryGetEntity(key, out var npc)) _inventoryStatus = "walk to pick a pocket ended: the NPC is gone";
        else if (Chebyshev(map.Row(npc), cell) > AreaInventoryHost.Reach) _inventoryStatus = "walk to pick a pocket ended out of reach";
        else { StartPickPocket(npc); return; }
        RefreshAreaStatus();
    }

    /// <summary>The start's rules (PP1) on an NPC in reach: the steal window, a body's loot, or a refusal.</summary>
    private void StartPickPocket(string npc)
    {
        _lastPickPocketStart = null;
        if (_areaSession is not { } session || AreaAiHostNow() is not { } ai || _combatHost is not { } combat) { _inventoryStatus = "pick pocket: not ready"; RefreshAreaStatus(); return; }
        var start = AreaPickPocket.Start(session, ai, combat.Fields(npc), npc);
        _lastPickPocketStart = start;
        var name = _inventoryHost!.Name(npc, _hoverNames?.Catalog);
        GD.Print($"ARC-SKILLS-010 pick-pocket start entity={npc} open={start.Open?.ToString() ?? "none"} {start.Detail}");
        switch (start.Open)
        {
            case PickPocketOpen.Steal:
                _pickPocketVictim = npc;
                _containerLootSession = null;
                OpenInventory(npc);
                if (!InventoryOpen) _pickPocketVictim = null;
                return;
            case PickPocketOpen.Loot when _inventoryHost.Target(session, CurrentAreaMap, _inventoryHost.Map(CurrentAreaMap)!.Row(npc).Key) is { Kind: AreaInventoryTargetKind.DeadNpc } body:
                ActOnInventoryTarget(body);
                return;
            case PickPocketOpen.NotAllowed: _inventoryStatus = $"{name}: {SkillLine(PickPocketRules.NotAllowedLine)}"; break;
            case PickPocketOpen.NotSeen: _inventoryStatus = $"{name}: not seen, nothing happens"; break;
            case PickPocketOpen.Refused: _inventoryStatus = $"{name}: refused"; break;
            default: _inventoryStatus = $"{name}: pick pocket not ready ({start.Detail})"; break;
        }
        RefreshAreaStatus();
    }

    private string SkillLine(int number) => (_gameBarContent?.Texts ?? AreaGameBarTexts.Project("no game bar")).SkillLine(number);

    /// <summary>A Steal or Plant press in the steal window: one attempt through the session.</summary>
    private void PickPocketAttempt(PickPocketDirection direction, string item)
    {
        if (!InventoryOpen || _pickPocketVictim is not { } npc || _inventoryPanel!.Holder != npc || _areaSession is not { } session) return;
        // The window opened within reach and the world waits while it is open, so the attempt does not walk (the original's goal does).
        if (AreaAiHostNow() is not { } ai || AreaPickPocket.Victim(ai, npc) is not { } victim || _inventoryHost!.Map(CurrentAreaMap) is not { } map)
        { RenderPickPocket("pick pocket: not ready (no AI host)"); return; }
        var view = InventoryView(npc);
        var line = (direction == PickPocketDirection.Steal ? view.Loot : view.Pc).FirstOrDefault(l => l.Entity == item);
        var stack = line?.StackKind is { Type: SessionItemType.Ammo } kind ? kind : default;
        ResumeLoadedRound();
        var world = session.QueryCombatRound() is not null ? AreaCombatWorldNow() : null;
        var result = session.PickPocket(session.Version, victim, direction, item, stack, _inventoryHost.InsertCheck, world, _inventoryHost.ItemScripts(session), ItemTriggersNow());
        // ARC-COMBAT-020: the charge's round events (a refused charge's next subturn) go to the round log.
        if (result.Charge is { } charge) LogRound(charge.Events);
        var itemName = _inventoryHost.Name(item, _hoverNames?.Catalog);
        AreaPickPocketCaught? caught = null;
        string message;
        if (result.Disposition == GameSessionDisposition.Rejected) message = $"pick pocket refused: {result.Failure}";
        else
        {
            message = $"{SkillLine(result.Moved > 0 ? PickPocketRules.SuccessLine : PickPocketRules.FailureLine)} ({(direction == PickPocketDirection.Steal ? "steal" : "plant")} {itemName}" +
                (result.Moved > 1 ? $" ×{result.Moved}" : "") + ")";
            if (result.Alignment is { } alignment) message += $"; alignment {alignment}";
            if (result.Noticed)
            {
                caught = AreaPickPocket.Caught(session, map.Row(npc), npc, CatchingThiefScript, victimNpc => ai.HeroAttacked(victimNpc));
                message += $"; caught: {caught.Detail}";
            }
        }
        _lastPickPocket = (result, caught);
        GD.Print($"ARC-SKILLS-010 pick-pocket attempt direction={direction} item={item} disposition={result.Disposition} failure={result.Failure} " +
            $"success={result.Check?.Success} critical={result.Check?.Critical} moved={result.Moved} noticed={result.Noticed} die={result.PerceptionRoll?.ToString() ?? "-"} " +
            $"fate={result.FateUsed} caught={caught?.Detail ?? "-"}");
        SyncAreaInventory();
        // A caught thief's window closes (a viewer choice: the original's window after a noticed failure was not read).
        if (result.Noticed) { _inventoryStatus = message; EndInventory(); RefreshAreaStatus(); return; }
        RenderPickPocket(message);
    }

    private void RenderPickPocket(string message)
    {
        _inventoryStatus = message;
        if (InventoryOpen && _pickPocketVictim is { } npc) _inventoryPanel!.Render(InventoryView(npc), message);
        RefreshReadyStatus();
    }

    /// <summary>The victim's catching-thief script (point 11) through the shown map's use-script host, the PC as the triggerer.</summary>
    private AreaScriptedUse CatchingThiefScript(AreaObjectRecord row) =>
        _readyArea is { } area && UseScriptHost(area.Snapshot) is { } host
            ? Awarded(host, host.RunCatchingThief(row, _areaSession!.Rules.Scripts))
            : new(AreaScriptedUseOutcome.ContentUnavailable, null, [], null, "no use-script host");
}
