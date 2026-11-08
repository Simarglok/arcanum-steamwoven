using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.Simulation.Skills;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-SKILLS-010: the pick-pocket skill in map mode (contract docs/world/pick-pocket-runtime.md). The palette's "Pick pocket" arms the
// skill (the skill bar's button stays a later task); the next left press on an NPC runs the start's rules at once, at sight distance
// (PP1): a stoned target or the hero itself is refused, a "no pickpocket" or mechanical critter floats skill_ui.mes line 525, a dead one
// is looted as any body, and a living one the hero sees opens the inventory window in steal mode. There each Steal button and each
// Plant button is one attempt through the session (4 action points in the hero's subturn); a noticed failure runs the victim's
// catching-thief script, and unless it or the victim's leadership stops it, the attack event of the hero on the victim, and closes the
// window. As the original, the press opens the window at sight distance and each drag or button press walks to range 1 (ARC-DEV-186).
public partial class TerrainViewerApp
{
    // ARC-DEV-110 (MIG16 S3): the armed skill is interface mode 2 (skill target) on the viewer's stack, read as PickPocketArmed.
    // The NPC whose pocket the window shows in steal mode, and the press waiting for its walk.
    private string? _pickPocketVictim;

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

    // ARC-DEV-184 (MIG13 S12d): a Steal or Plant is the skill goal 33 → 86 of the table; the attempt it asks for waits here for the frame's next safe point.
    private (string Npc, string Item, bool Plant)? _pickPocketPending;
    // A Steal or Plant press whose attempt has not been made yet (the status line says "trying" meanwhile).
    private bool _pickPocketTrying;
    // ARC-DEV-321 (MIG14 S9e): the session a Steal or Plant press was seen in; a pick pocket run of another session (a restored one) makes its attempt without the window.
    private GameSessionOwner? _pickPocketOwner;

    private string PickPocketTargetOf(string npc) => "pp:" + npc;

    /// <summary>The map row of the victim a "pp:" target names (the steal window's holder), or null.</summary>
    internal AreaObjectRecord? PickPocketRowOf(string target)
    {
        if (!target.StartsWith("pp:", StringComparison.Ordinal) || _inventoryHost?.Map(CurrentAreaMap) is not { } map) return null;
        try { return map.Row(target[3..]); }
        catch (KeyNotFoundException) { return null; }
    }

    /// <summary>The cell of a skill goal's target: a pick pocket's victim ("pp:") or a lock pick's door or window (the portal's session entity).</summary>
    private CellCoord? SkillTargetCell(string target)
    {
        if (PickPocketRowOf(target) is { } row) { var (x, y) = LiveWorldCell(row); return new(checked((int)x), checked((int)y)); }
        return _areaDoors?.PortalOf(target) is { } portal ? new(checked((int)portal.Cell.X), checked((int)portal.Cell.Y)) : null;
    }

    /// <summary>The skill goal's turn toward the victim (0x42C0F0): the hero faces it at once, drawn while it stands on its cell (as the cast's turn).</summary>
    internal bool HeroFaceSkill(string target)
    {
        if (_worldActor.Session is not AreaGridSession grid || HeroWorldCell() is not { } at || SkillTargetCell(target) is not { } there) return false;
        if (there != at)
        {
            var octant = DiagnosticGridActor.Octant((NeighborDirection)LineOfFireRules.Rotation(at, there));
            _worldActor.TurnedFacing = (octant, grid.State.Facing);
            PublishWorldSelection();
        }
        return true;
    }

    /// <summary>Goal 86's attempt node asked for the attempt: it is made at the next safe point.</summary>
    internal void HeroPickPocketAsked(string target, string item, bool plant)
    {
        if (target.StartsWith("pp:", StringComparison.Ordinal)) _pickPocketPending = (target[3..], item, plant);
    }

    private void CancelPickPocket(string why)
    {
        // The skill run waiting or walking on the table goes with the cancel; so does an attempt not yet made.
        try { _heroRunsHost?.EndSkill("ROUTE_STOPPED_BY_SKILL_END"); }
        catch (ObjectDisposedException) { /* a replaced session */ }
        _pickPocketPending = null; _pickPocketTrying = false;
        if (!PickPocketArmed) return;
        CloseTargetMode(InterfaceMode.SkillTarget);
        _inventoryStatus = "pick pocket cancelled: " + why;
    }

    /// <summary>The armed press: an NPC of the map's seed starts at once, at any distance (the walk is each attempt's); anything else cancels the skill.</summary>
    private void PickPocketPress(Vector2 screen)
    {
        // The press leaves the skill's target mode (SA51: pick pocket leaves it and opens the steal window).
        if (PickPocketArmed) CloseTargetMode(InterfaceMode.SkillTarget);
        if (InventoryOpen || ActorCell() is null || AreaObjectAtScreen(screen) is not { } key || _inventoryHost?.Map(CurrentAreaMap) is not { } map
            || !map.TryGetEntity(key, out var npc) || !map.Seed.Holders.Any(h => h.Entity == npc && h.Kind == SessionHolderKind.Npc))
        {
            _inventoryStatus = "pick pocket cancelled: not an NPC"; RefreshAreaStatus(); return;
        }
        CancelTalkApproach("a pick-pocket press replaced it"); CancelAreaUseApproach("a pick-pocket press replaced it"); CancelInventoryApproach(null);
        // ARC-DEV-186 (GT144): the press opens the window at sight distance and walks nowhere (the skill-bar branch 0x57A320 starts no goal and no walk);
        // each Steal or Plant is the skill goal 33, which walks within reach 1 with the window open.
        StartPickPocket(npc);
    }

    /// <summary>The frame's safe point of the pick pocket: the attempt goal 86 asked for is made, or the window says the run ended without it.</summary>
    private void ProcessPickPocketPending()
    {
        if (_pickPocketPending is { } asked)
        {
            _pickPocketPending = null; _pickPocketTrying = false;
            // The attempt belongs to the window it was pressed in: a window for another victim (or none) drops it. ARC-DEV-321 (MIG14 S9e): a restored run (no press seen in this session) has no window, as
            // the original's interface is not saved: its attempt is made without one, as goal 86's node 4 makes it whatever the interface shows (0x42A2A0 → 0x4C7160, GT204).
            var direction = asked.Plant ? PickPocketDirection.Plant : PickPocketDirection.Steal;
            if (_worldLifecycleSession is not null || _worldResetPending || _worldReloadPending) { }
            else if (asked.Npc == _pickPocketVictim) PerformPickPocket(direction, asked.Item);
            else if (_areaSession is { } shown && !ReferenceEquals(_pickPocketOwner, shown)) { _pickPocketOwner = shown; PerformPickPocket(direction, asked.Item, restored: asked.Npc); }
        }
        else if (_pickPocketTrying && HeroRunsHost() is { SkillRun: null })
        {
            // The skill run ended without asking for the attempt (the victim is gone, the hero could not turn, a cancel): the window says so.
            _pickPocketTrying = false;
            RenderPickPocket("pick pocket: the attempt did not happen");
        }
    }

    /// <summary>The start's rules (PP1) on an NPC the hero sees: the steal window, a body's loot, or a refusal.</summary>
    private void StartPickPocket(string npc)
    {
        _lastPickPocketStart = null;
        if (_areaSession is not { } session || AreaAiHostNow() is not { } ai || _combatHost is not { } combat) { _inventoryStatus = "pick pocket: not ready"; RefreshAreaStatus(); return; }
        var start = AreaPickPocket.Start(session, ai, combat.Fields(npc), npc);
        _lastPickPocketStart = start;
        var name = _inventoryHost!.Name(npc, _hoverNames?.Catalog, WindowRecognition);
        GD.Print($"ARC-SKILLS-010 pick-pocket start entity={npc} open={start.Open?.ToString() ?? "none"} {start.Detail}");
        switch (start.Open)
        {
            case PickPocketOpen.Steal:
                StopWorldRoute("ROUTE_REPLACED_BY_PICK_POCKET");
                _pickPocketVictim = npc;
                _containerLootSession = null;
                OpenInventory(npc);
                if (!InventoryOpen) _pickPocketVictim = null;
                return;
            case PickPocketOpen.Loot when _inventoryHost.Target(session, CurrentAreaMap, _inventoryHost.Map(CurrentAreaMap)!.Row(npc).Key) is { Kind: AreaInventoryTargetKind.DeadNpc } body:
                // A dead target is looted as any body (PP1 has no sight test for it): the click's use goal 81 walks to reach 1 and opens the loot window.
                var click = HeroRequests.Apply(new ObjectClickRequest(InventoryTargetOf(body.Row.Key), Alt: false));
                _inventoryStatus = click.Outcome == HeroRequestOutcome.Done ? $"{name}: walking to loot, goal 81 started" : $"{name}: the loot order was refused";
                RefreshAreaStatus();
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
        if (!InventoryOpen || _pickPocketVictim is not { } npc || _inventoryPanel!.Holder != npc || _areaSession is null) return;
        // ARC-DEV-184/186: the press is the skill goal 33: it walks within reach 1 (goal 8) with the window open (the world runs under it, GT144) and turns; the attempt is made when goal 86's node asks for it.
        if (HeroRunsHost() is not null)
        {
            if (PickPocketRowOf(PickPocketTargetOf(npc)) is null) { RenderPickPocket("pick pocket: the victim is gone"); return; }
            _pickPocketOwner = _areaSession;
            var started = HeroRequests.Apply(new PickPocketRequest(npc, item, direction == PickPocketDirection.Plant));
            if (started.Outcome == HeroRequestOutcome.Done) _pickPocketTrying = true;
            RenderPickPocket(started.Outcome == HeroRequestOutcome.Done ? "pick pocket: trying" : "pick pocket: the skill did not start");
            return;
        }
        PerformPickPocket(direction, item);
    }

    /// <summary>The attempt itself, once goal 86 asked for it (or at once without a table): one try through the session.</summary>
    /// <param name="restored">ARC-DEV-321: the victim of a restored run's attempt, made without the steal window.</param>
    private void PerformPickPocket(PickPocketDirection direction, string item, string? restored = null)
    {
        string npc;
        if (restored is not null) npc = restored;
        else if (!InventoryOpen || _pickPocketVictim is not { } shown || _inventoryPanel!.Holder != shown) return;
        else npc = shown;
        if (_areaSession is not { } session) return;
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
        if (result.Noticed) { _inventoryStatus = message; if (InventoryOpen) EndInventory(); RefreshAreaStatus(); return; }
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
