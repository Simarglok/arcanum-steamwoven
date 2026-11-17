using Arcanum.Scripting.Dialog;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Party;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-WORLD-037 native observation (map mode, after the resource stages and before the transfer smoke): a drawn dialog NPC
// of the map with follow facts from its data joins the session party (the session's forced join, as a script follow
// does, while a conversation with it is open; the dialog's JO goes through the same session party port); closing the
// conversation rebuilds the group, so it becomes a companion, follows the PC placed 6 cells
// away on a cell it can walk to, to within 2, and is drawn there (or hidden by a roof over its cell). It stays following, so the transfer smoke that runs next carries it
// (ARC-WORLD-037 native-smoke carry).
public partial class TerrainViewerApp
{
    private string? _smokeCompanion;
    // ARC-AI-1695: the NPC of the start map without a dialog that the follow smoke force-joined as well (what Dominate Will does), so the transfer carries it too.
    private string? _smokeLedNpc;

    private async Task RunAreaMapCompanionSmokeAsync()
    {
        _smokeCompanion = null; _smokeLedNpc = null;
        if (_areaSession is not { } session || _conversationHost is not { PartyWired: true } host || _readyArea is not { } area
            || _worldActor.Session is not AreaGridSession grid)
        { GD.Print($"ARC-WORLD-037 native-smoke follow NONE map={_readyMapName} (no session party: {_companionData?.Detail})"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-WORLD-037 follow smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_MAP_FOLLOW_SMOKE_FAILED");
        }
        var map = new AreaMapRuntime(area.Snapshot, grid.Snapshot, grid.Chart);
        // The first free living NPC of the map (ARC-WORLD-049: a dead one lies as a corpse and is looted, not joined) with a
        // cell 6 cells from its T0 cell that it can walk to (ARC-RENDER-024: a main-map window can hold NPCs standing where
        // no open step leads away).
        var routes = new AreaRouteSource(map.Steps, map.Chart);
        CellCoord? Far(CellCoord origin)
        {
            var seen = new HashSet<CellCoord> { origin }; var queue = new Queue<CellCoord>([origin]);
            while (queue.TryDequeue(out var from) && seen.Count < 20000)
                foreach (var direction in Enum.GetValues<NeighborDirection>())
                {
                    var next = Arcanum.Simulation.Navigation.RoutePlanner.Neighbor(from, direction);
                    if (seen.Contains(next) || !routes.Contains(next) || routes.Assess(from, direction) != Arcanum.Simulation.Navigation.RouteStepVerdict.Open) continue;
                    seen.Add(next); queue.Enqueue(next);
                    if (FollowDistanceRules.Distance(next, origin) >= 6) return next;
                }
            return null;
        }
        var candidates = _companionTemplates.Where(t => t.Home.RuntimeMap == CurrentAreaMap && !SpeakerDead(t.Speaker)
            && session.QueryCompanion(t.Entity).Standing == CompanionStanding.Free).ToArray();
        var (template, far) = candidates.Select(t => (Template: t, Far: Far(t.HomeOrigin))).FirstOrDefault(p => p.Far is not null);
        if (template is null)
        { GD.Print($"ARC-WORLD-037 native-smoke follow NONE map={_readyMapName} templates={_companionTemplates.Count} free={candidates.Length} (none can walk 6 cells)"); return; }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            // The join lands while a conversation with the NPC is open, so closing it runs the production rebuild.
            BeginConversation(template.Speaker);
            AreaSmokeFramesUntilConversation(); Require(ConversationOpen, "a conversation with the NPC opened: " + _conversationNotice);
            var joined = session.ForceJoinParty(session.Version, template.Entity);
            Require(joined.Disposition != GameSessionDisposition.Rejected, "joined: " + joined.Failure);
            for (var close = 0; close < 4 && ConversationOpen; close++) _conversationPanel!.LeaveForSelfTest.EmitSignal(BaseButton.SignalName.Pressed);
            Require(!ConversationOpen, "the conversation closed");
            Require(_areaCompanions?.Companions.SingleOrDefault(c => c.Entity == template.Entity) is { Standing: CompanionStanding.Following },
                "the NPC is a following companion: " + _areaCompanions?.Status());
            // ARC-DEV-110 (MIG16 S3): a portrait's walk order waits for its tile as interface mode 18 (follower order) pushed over the base, with
            // the game cursor; the next press, a cancel or a window pops it, and a window that opens over it replaces it.
            FollowerMenuChosen(template.Entity, FollowerMenuItem.Walk);
            Require(_pendingOrder is not null && TargetModeOpen(InterfaceMode.FollowerOrder) && TargetsMatchStack && _interfaceWindows.Stack.Depth == 2
                && _interfaceWindows.Stack.Current == InterfaceMode.FollowerOrder && CursorMode == AreaCursorMode.Game, "the walk order is mode 18 on the stack: " + StackText() + " " + _companionNotice);
            CancelPendingOrder("the smoke");
            Require(_pendingOrder is null && !TargetModeOpen(InterfaceMode.FollowerOrder) && TargetsMatchStack && _interfaceWindows.Stack.Depth == 1, "the cancel left mode 18: " + StackText());
            FollowerMenuChosen(template.Entity, FollowerMenuItem.Walk);
            Require(TargetModeOpen(InterfaceMode.FollowerOrder), "the order waits again: " + _companionNotice);
            // A new menu choice replaces the order still waiting (one order at a time), and the cancel leaves the mode again.
            FollowerMenuChosen(template.Entity, FollowerMenuItem.Walk);
            Require(TargetModeOpen(InterfaceMode.FollowerOrder) && TargetsMatchStack && _interfaceWindows.Stack.Depth == 2, "a second choice keeps one order: " + StackText());
            CancelPendingOrder("the smoke");
            Require(_pendingOrder is null && _interfaceWindows.Stack.Depth == 1 && TargetsMatchStack, "the order left: " + StackText());
            // The PC 6 cells away on a cell the NPC can walk to (a breadth-first search over open steps from its T0 cell).
            AreaSmokePlace(map, far!.Value);
            var companion = _areaCompanions!.Companions.Single(c => c.Entity == template.Entity);
            var pc = ((AreaGridSession)_worldActor.Session!).State.CommittedCell;
            var frames = 0;
            for (; frames < 600 && (companion.Grid.State.Phase != MovementPhase.Settled
                || FollowDistanceRules.Distance(companion.Grid.State.CommittedCell, pc) > FollowDistanceRules.Range); frames++) AreaSmokeFrame();
            var cell = companion.Grid.State.CommittedCell;
            var reached = FollowDistanceRules.Distance(cell, pc) <= FollowDistanceRules.Range;
            _camera!.ResetWorld(WorldAnchor, WorldViewport, _worldPreferredZoom);
            PublishWorldSelection();
            for (var frame = 0; frame < 3; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            }
            var world = map.Chart.ToWorld(cell);
            var drawn = _canvas!.AreaVisibleCommands.Any(c => c.Identity == companion.Key && c.MapX == world.X && c.MapY == world.Y);
            // A roof over its cell (the PC outside the building) hides it there, as the original does (ledger RF4).
            var underRoof = !drawn && _canvas.AreaRoofCovers(companion.Key);
            var moving = AreaCompanionCommands()?.Where(c => c.Identity == companion.Key).ToArray() ?? [];
            Require(reached, $"it followed within {FollowDistanceRules.Range} of the PC in {frames} frames: at {world}, PC {map.Chart.ToWorld(pc)}");
            Require(drawn || underRoof, $"its body is drawn on its cell {world}: moving commands {moving.Length} at "
                + string.Join(",", moving.Select(c => $"{c.MapX},{c.MapY} hidden {_canvas.AreaHiddenOf(c)}"))
                + "; visible with its key " + string.Join(",", _canvas.AreaVisibleCommands.Where(c => c.Identity == companion.Key).Select(c => $"{c.MapX},{c.MapY}"))
                + $"; companions map {_areaCompanions?.Map.RuntimeMap} shown {_readyArea?.Snapshot.RuntimeMap}");
            RecordAreaCompanions("smoke");
            RequireCompanionAttackClick(companion, Require);
            _smokeCompanion = template.Entity;
            GD.Print("ARC-WORLD-037 native-smoke follow PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Map = _readyMapName, template.Speaker.ScriptId, Prototype = template.Speaker.Row.PrototypeNumber, template.Facts.Level, template.Facts.Thresholds,
                From = map.Chart.ToWorld(template.HomeOrigin), Pc = map.Chart.ToWorld(pc), At = world, UnderRoof = underRoof, Frames = frames, Templates = _companionTemplates.Count, Parity = "NOT_VERIFIED" }));
            JoinLedMapNpcForSmoke(session, area, map, template.Entity, pc);
        }
        finally
        {
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
        }
    }

    /// <summary>
    /// ARC-COMBAT-1945 (0x54ED30, 0x54EFDA): in combat mode a click on a companion that moves with its own actor (here the release over its portrait, the body click takes the same branch) is an attack only with left Alt held; the attack
    /// goal names the companion and reads its live cell, not its T0 cell. The goal is cancelled before any frame, so the companion stays unhurt and the hero stays put for the transfer smoke that follows.
    /// </summary>
    private void RequireCompanionAttackClick(AreaCompanion companion, Action<bool, string> require)
    {
        var wasInCombat = HeroInCombatMode;
        if (!wasInCombat) ToggleCombatMode();
        try
        {
            require(HeroInCombatMode, "the hero is in combat mode for the companion attack click");
            _leftAltForSelfTest = false;
            FollowerPortraitClick(companion.Entity);
            require(_attack is null, "a companion click in combat mode without left Alt attacks nothing: " + _companionNotice);
            _leftAltForSelfTest = true;
            FollowerPortraitClick(companion.Entity);
            require(_attack is { } goal && goal.Entity == companion.Entity, "a companion click with left Alt starts the attack goal on it: " + _companionNotice + " / " + _attackNotice);
            var (x, y) = _areaCompanions!.WorldCell(companion.Speaker);
            var live = new CellCoord(checked((int)x), checked((int)y));
            require(LiveWorldCell(companion.Speaker.Row) == (x, y) && (x, y) != (companion.Speaker.WorldX, companion.Speaker.WorldY), "the attack reads the companion's live cell, not its T0 cell");
            var aimed = HeroAttackTarget(AttackTargetOf(_attack!));
            require(aimed is { Dead: false } && aimed.Value.Tile == live, $"the goal's target is the companion on its live cell {live}: {aimed} life={_areaSession!.QueryLife(_attack!.Combat!.Target).View?.Status} target={AreaTargetOf(companion.Key)?.Dead}");
            GD.Print($"ARC-COMBAT-1945 native-smoke companion-attack PASS target={companion.Entity} live={live} t0={companion.Speaker.WorldX},{companion.Speaker.WorldY}");
        }
        finally
        {
            _leftAltForSelfTest = false;
            CancelAreaAttack(null);
            HeroRequests.Apply(new StopRequest(null));
            if (HeroInCombatMode && !wasInCombat) ToggleCombatMode();
        }
    }

    /// <summary>
    /// ARC-AI-1695: the part of the follow smoke that joins an NPC of the map without a dialog (a Dominate Will target: <c>ForceJoinParty</c>, no conversation) next to the dialog
    /// companion. The nearest living free NPC of the map that is drawn, has follow facts and is not a dialog speaker joins; the group is rebuilt with its led body (script 0) and it
    /// follows the hero within the follow distance, drawn. It stays following, so the transfer smoke that runs next carries it (<see cref="ReportAreaMapLedCarry"/>) and the window
    /// moves of follower-far and teleport-follow check it on the main map (<see cref="_smokeLedNpc"/>).
    /// </summary>
    private void JoinLedMapNpcForSmoke(GameSessionOwner session, AreaLoadResult area, AreaMapRuntime map, string dialogCompanion, CellCoord pc)
    {
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-AI-1695 led-follow smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_MAP_LED_FOLLOW_FAILED");
        }
        if (_inventoryHost?.Maps.SingleOrDefault(m => m.RuntimeMap == CurrentAreaMap) is not { } inventory)
        { GD.Print($"ARC-AI-1695 native-smoke led-follow NONE map={_readyMapName} (no inventory of the map)"); return; }
        var speakerKeys = AreaConversationHost.FindSpeakers(area.Snapshot).Select(sp => sp.Row.Key).ToHashSet();
        var here = map.Chart.ToWorld(pc);
        var candidate = session.QueryMapInventory(new(CurrentAreaMap))!.Holders
            .Where(h => h.Kind == SessionHolderKind.Npc && !h.Dead && h.Entity != dialogCompanion && session.QueryCompanion(h.Entity).Standing == CompanionStanding.Free)
            .Select(h => (Holder: h, Row: inventory.Row(h.Entity)))
            // Not a pre-made hero's NPC (their heartbeat scripts 1781-1792 kill the unchosen ones once the AI runs, ARC-WORLD-114).
            // ARC-AI-1851: not an NPC whose stored object is off or not drawn (0x2 / 0x100: the AI host takes it for hidden, so no forced pass sends it home, and no spell targets it).
            .Where(c => c.Row.Type == 16 && !speakerKeys.Contains(c.Row.Key) && c.Row.WorldX is not null && c.Row.WorldY is not null
                && (_combatHost?.Fields(c.Holder.Entity)?.Object.Flags & 0x102) == 0
                && area.Plan.Commands.Any(command => command.Identity == c.Row.Key)
                && AreaUseScriptHost.LoadedSlot(c.Row, Arcanum.Scripting.Scr.ScrPoint.Heartbeat)?.Script is not (>= 1781 and <= 1792)
                && MapCompanionFacts(new AreaSpeaker(AreaCompanionTemplate.LedMapNpcIndexBase, c.Row, 0, c.Row.WorldX!.Value, c.Row.WorldY!.Value)) is not null)
            .OrderBy(c => Math.Max(Math.Abs(c.Row.WorldX!.Value - here.X), Math.Abs(c.Row.WorldY!.Value - here.Y))).ThenBy(c => c.Holder.Entity, StringComparer.Ordinal)
            .FirstOrDefault();
        if (candidate.Holder is null)
        { GD.Print($"ARC-AI-1695 native-smoke led-follow NONE map={_readyMapName} (no living free NPC without a dialog with follow facts that the scene draws)"); return; }
        var led = candidate.Holder.Entity;
        var joined = session.ForceJoinParty(session.Version, led);
        Require(joined.Disposition != GameSessionDisposition.Rejected, "the NPC without a dialog was force-joined: " + joined.Failure);
        var frames = 0;
        for (; frames < 600 && (_areaCompanions?.Companions.SingleOrDefault(c => c.Entity == led) is not { } found || found.Speaker.ScriptId != 0
            || found.Grid.State.Phase != MovementPhase.Settled || FollowDistanceRules.Distance(found.Grid.State.CommittedCell, ((AreaGridSession)_worldActor.Session!).State.CommittedCell) > FollowDistanceRules.Range); frames++) AreaSmokeFrame();
        var companion = _areaCompanions?.Companions.SingleOrDefault(c => c.Entity == led);
        Require(companion is { Standing: CompanionStanding.Following } && companion.Speaker.ScriptId == 0, "the NPC without a dialog is a following companion with a led body (script 0): " + _areaCompanions?.Status());
        var cell = companion!.Grid.State.CommittedCell;
        var close = FollowDistanceRules.Distance(cell, ((AreaGridSession)_worldActor.Session!).State.CommittedCell);
        Require(close <= FollowDistanceRules.Range, $"it followed within {FollowDistanceRules.Range} of the PC in {frames} frames: {close}");
        RecordAreaCompanions("led-follow smoke");
        _smokeLedNpc = led;
        GD.Print("ARC-AI-1695 native-smoke led-follow PASS " + System.Text.Json.JsonSerializer.Serialize(new {
            Map = _readyMapName, Entity = led, Prototype = candidate.Row.PrototypeNumber, From = $"{candidate.Row.WorldX},{candidate.Row.WorldY}", Distance = close, Frames = frames }));
    }

    /// <summary>
    /// ARC-AI-1695: after the transfer smoke, the NPC without a dialog that the follow smoke force-joined arrived with the hero as the dialog companion did: a companion with a led body (script 0), following,
    /// within the follow distance and drawn; on another map it is a visitor whose body its home map's read built (the read lists it as led). The hero then releases it: the destination's forced pass sends it
    /// back to its home map (<see cref="SendAreaMapFormerFollowerHome"/>, as the follower-home stage does for the dialog companion).
    /// </summary>
    private void ReportAreaMapLedCarry(string entity, string from, bool sameMap)
    {
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-AI-1695 led-carry smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_MAP_LED_CARRY_FAILED");
        }
        var companion = _areaCompanions?.Companions.SingleOrDefault(c => c.Entity == entity);
        Require(companion is not null, $"the NPC without a dialog came with the hero: {_areaCompanions?.Status()} unread {string.Join("; ", _unreadVisitors)}");
        var pc = ((AreaGridSession)_worldActor.Session!).State.CommittedCell;
        Require(companion!.Speaker.ScriptId == 0 && companion.Standing == CompanionStanding.Following && _areaSession!.QueryCompanion(entity).LedByPc, "it is a following companion with a led body (script 0)");
        var distance = FollowDistanceRules.Distance(companion.Grid.State.CommittedCell, pc);
        Require(distance <= FollowDistanceRules.Range, $"it stands within {FollowDistanceRules.Range} of the PC: {distance}");
        var cell = _areaCompanions!.WorldCell(companion.Speaker);
        var drawn = _canvas!.AreaVisibleCommands.Any(c => c.Identity == companion.Key && c.MapX == cell.X && c.MapY == cell.Y) || _canvas.AreaRoofCovers(companion.Key);
        Require(drawn, $"its body is drawn on {cell.X},{cell.Y}");
        var visiting = companion.Template.Home.RuntimeMap != CurrentAreaMap;
        if (visiting)
        {
            // ARC-AI-1851: the directory may hold several reads of the home map (the viewer's own window and the led read around the stored cell); Held(map) answers the first,
            // so the check looks at every held read of the map for the one that was asked for this NPC.
            var homeMap = companion.Template.Home.RuntimeMap;
            var homeReads = _homes?.Reads.Where(read => read.Map == homeMap).ToArray() ?? [];
            Require(_unreadVisitors.Count == 0 && homeReads.Any(read => read.Led.Contains(entity) && read.Template(entity) is not null),
                $"its body came from its home map's read as a led NPC's: unread [{string.Join("; ", _unreadVisitors)}] reads of map {homeMap}: " +
                string.Join(", ", homeReads.Select(read => $"window={read.CoreSectors is not null} led={read.Led.Count} body={read.Template(entity) is not null}")));
        }
        GD.Print("ARC-AI-1695 native-smoke led-carry PASS " + System.Text.Json.JsonSerializer.Serialize(new {
            From = from, To = _readyMapName, SameMap = sameMap, Visiting = visiting, Home = companion.Template.Home.RuntimeMap, At = $"{cell.X},{cell.Y}", Distance = distance }));
        // The carry is reported again after the jump smoke: the released NPC is checked once (it stands on its home map afterwards).
        if (visiting) { _smokeLedNpc = null; SendAreaMapFormerFollowerHome(entity); }
    }

    /// <summary>After the transfer smoke: the companion the follow smoke joined stands where the PC arrived and is drawn there.</summary>
    private void ReportAreaMapCarry(string from, bool sameMap)
    {
        if (_smokeCompanion is not { } entity) return;
        var companion = _areaCompanions?.Companions.SingleOrDefault(c => c.Entity == entity);
        var pc = (_worldActor.Session as AreaGridSession)?.State.CommittedCell;
        var close = companion is not null && pc is { } at && FollowDistanceRules.Distance(companion.Grid.State.CommittedCell, at) <= FollowDistanceRules.Range;
        var cell = companion is null ? default : _areaCompanions!.WorldCell(companion.Speaker);
        var drawn = companion is not null && (_canvas!.AreaVisibleCommands.Any(c => c.Identity == companion.Key && c.MapX == cell.X && c.MapY == cell.Y)
            || _canvas.AreaRoofCovers(companion.Key));
        if (companion is null || !close || !drawn)
        {
            GD.Print($"ARC-WORLD-037 follow smoke FAIL: the companion did not arrive with the PC or is not drawn (drawn {drawn}; {_areaCompanions?.Status()})");
            throw new InvalidOperationException("AREA_MAP_CARRY_FAILED");
        }
        GD.Print("ARC-WORLD-037 native-smoke carry PASS " + System.Text.Json.JsonSerializer.Serialize(new {
            From = from, To = _readyMapName, SameMap = sameMap, Visiting = companion.Template.Home.RuntimeMap != CurrentAreaMap,
            At = $"{cell.X},{cell.Y}", Drawn = drawn }));
        if (companion.Template.Home.RuntimeMap != CurrentAreaMap) { TalkToAreaMapVisitor(companion); GiveFromAreaMapVisitor(companion); FightNextToAreaMapVisitor(companion); ReadAreaMapVisitorBody(companion); }
        // ARC-AI-1695: the NPC without a dialog that was force-joined with it arrived too (after the visitor checks, which run on the dialog companion and the hero's combat).
        if (_smokeLedNpc is { } led) ReportAreaMapLedCarry(led, from, sameMap);
    }

    /// <summary>
    /// ARC-DEV-065 / ARC-DEV-075: on the destination (the Crash Site cave from the main map), with the AI on and a hero, the hero strikes
    /// the living monstrous NPC with the fewest hit points and that NPC bites the hero back while the visitor stands here. Since
    /// ARC-DEV-075 (MIG2) the AI host describes a visitor by identity (its home map's record, read on demand by the record resolver): it is
    /// told of the hero's attack, stands in the NPC's square, strikes the NPC and is a target of the cave NPC. When no record resolves (the
    /// main map's window as a home, MIG9) the ARC-DEV-065 guard keeps it out of the rules as before and the fight checks that instead
    /// (before that guard the bite stopped the game, ActorPlan). The hero, the clock and the AI are restored after.
    /// </summary>
    private void FightNextToAreaMapVisitor(AreaCompanion companion)
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _combatHost is null || _readyArea is not { } area || _mapTransfers is not { } transfers)
        { GD.Print("ARC-DEV-065 native-smoke visitor-fight NONE (no hero, combat host or map transfers)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-DEV-065 visitor-fight smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_MAP_VISITOR_FIGHT_FAILED");
        }
        var map = transfers.Map;
        if (_inventoryHost?.Maps.SingleOrDefault(m => m.RuntimeMap == map.RuntimeMap) is not { } inventory)
        { GD.Print($"ARC-DEV-065 native-smoke visitor-fight NONE map={map.RuntimeMap} (no inventory seed)"); return; }
        var commands = area.Plan.Commands.Where(c => c.Identity is not null).Select(c => c.Identity!.Value).ToHashSet();
        var target = session.QueryMapInventory(new(map.RuntimeMap))!.Holders
            .Where(h => h.Kind == SessionHolderKind.Npc && !h.Dead && session.QueryCompanion(h.Entity).Standing == CompanionStanding.Free)
            .Select(h => (Holder: h, Row: inventory.Row(h.Entity), Build: _combatHost.Build(session, map.RuntimeMap, h.Entity)))
            .Where(c => c.Build.Combat is { Monstrous: true } && commands.Contains(c.Row.Key) && c.Row.Scope == Arcanum.Data.Area.AreaScope.Core)
            .OrderBy(c => Arcanum.Simulation.Combat.LifeRules.MaxHitPoints(c.Build.Combat!.Facts, 0)).ThenBy(c => c.Holder.Entity, StringComparer.Ordinal)
            .FirstOrDefault();
        if (target.Holder is null) { GD.Print($"ARC-DEV-065 native-smoke visitor-fight NO-TARGET map={map.RuntimeMap}"); return; }
        var entity = target.Holder.Entity;
        var targetCell = map.Chart.ToChart(new(target.Row.WorldX!.Value, target.Row.WorldY!.Value));
        var home = ((AreaGridSession)_worldActor.Session!).State.CommittedCell;
        var testTime = _worldTestTime; var turnBased = _turnBasedSetting;
        _aiForSmoke = true;
        try
        {
            // Real time, as the player's fight (the AI stages' own setting).
            _turnBasedSetting = false;
            AreaSmokePlace(map, AreaSmokeStandable(map, targetCell, 2));
            if (!HeroInCombatMode) ToggleCombatMode();
            Require(HeroInCombatMode, "the hero is in combat mode");
            var host = AreaAiHostNow();
            Require(host is not null && host.RunCompanions.Contains(companion.Entity), "the AI host runs the visitor's walk");
            var resolved = host!.Find(companion.Entity) is not null;
            _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            var hostMs = 30_000_000L;
            TickAreaClock(hostMs);
            var frames = 0;
            // ARC-BUG-1500: the victim has just been folded in, so its first tick runs the standpoint step, which since ARC-AI-1313 starts the wander goal (65/66, priority 3) of a wanderer, and the AI gate
            // leaves an NPC whose main run has a priority above 2 busy (0x4233D0, GS20): the tick's pass in that same frame was lost and the visitor killed the NPC before its next tick. The gate is the
            // original's; a real hit's reaction (hit animation, then the immediate pass) ends the wander before the victim thinks, which this smoke's stand-in for the hit (the attack event alone) does not. So the victim takes its first tick, its wander
            // (a run that restarts every 300 ms while no tile is accepted) is ended as the hit would end it, and only then the hero attacks: the check is the fight, not the wander.
            for (var settle = 0; settle < 20 && host.TraceOf(entity)?.Route is null; settle++)
            {
                _areaSmokeTime++;
                TickAreaClock(hostMs += 50);
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
            }
            if (host.RunOf(entity) is { Bottom.Goal: Arcanum.Simulation.Goals.GoalType.Wander or Arcanum.Simulation.Goals.GoalType.WanderSeekDarkness }) host.InterruptAnimations(entity);
            Require(host.TraceOf(entity)?.Hit is null, "the NPC bit nobody before the hero's attack: " + host.TraceOf(entity)?.Hit);
            // The hero's attack: the attacker's followers are told, the victim weighs fleeing against the hero's party, witnesses hear it.
            host.HeroAttacked(entity);
            // Its bite on the hero: the victim's followers are told.
            for (; frames < 4000 && host.TraceOf(entity)?.Hit is null; frames++)
            {
                _areaSmokeTime++;
                TickAreaClock(hostMs += 50);
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
            }
            Require(host.TraceOf(entity)?.Hit is { } hit && !hit.StartsWith(nameof(GameSessionDisposition.Rejected), StringComparison.Ordinal),
                $"the NPC's bite was resolved after {frames} frames: {host.TraceOf(entity)?.Hit} {host.TraceOf(entity)?.Step}");
            if (resolved)
            {
                var told = host.TraceOf(companion.Entity)?.Event;
                // The trace keeps the last event: the hero's attack (as its attacker) or the NPC's bite on the hero (as its victim).
                Require(told is not null && told.StartsWith("told ", StringComparison.Ordinal), "the visitor was told of an attack: " + told);
                Require(host.Square(entity, 20).Contains(companion.Entity), "the visitor stands in the NPC's square: the cave NPC can target it");
                // The visitor walks up and strikes the NPC it was told of.
                for (; frames < 8000 && host.TraceOf(companion.Entity)?.Hit is null; frames++)
                {
                    _areaSmokeTime++;
                    TickAreaClock(hostMs += 50);
                    WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
                }
                Require(host.TraceOf(companion.Entity)?.Hit is { } visitorHit && !visitorHit.StartsWith(nameof(GameSessionDisposition.Rejected), StringComparison.Ordinal),
                    $"the visitor's strike was resolved after {frames} frames: {host.TraceOf(companion.Entity)?.Hit} {host.TraceOf(companion.Entity)?.Step}");
            }
            else
            {
                Require(host.TraceOf(companion.Entity)?.Event is null, "an unresolved visitor was told of no attack: " + host.TraceOf(companion.Entity)?.Event);
                Require(!host.Square(entity, 20).Contains(companion.Entity), "an unresolved visitor stands in no square the rules read");
            }
            GD.Print("ARC-DEV-065 native-smoke visitor-fight PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Map = map.RuntimeMap, Visitor = companion.Speaker.ScriptId, Target = target.Row.PrototypeNumber, Frames = frames, Resolved = resolved,
                Hit = host.TraceOf(entity)?.Hit, VisitorHit = resolved ? host.TraceOf(companion.Entity)?.Hit : null }));
        }
        finally
        {
            _aiForSmoke = false; RetireAiHost(); _aiHostStamp = null;
            _areaSmokeTime = (_areaSmokeTime + 19) / 20;
            RestoreWorldTestTime(testTime);
            // The carry and talk leave the hero out of combat mode; it leaves as the AI stages do (no exit gate, no round).
            if (HeroInCombatMode) session.SetCombatMode(session.Version, SessionCombatant.Hero, false, SessionCombatModeContext.RealTime);
            _turnBasedSetting = turnBased;
            AreaSmokePlace(map, home);
        }
    }

    /// <summary>
    /// ARC-WORLD-048: on the destination the carried companion is a visitor: it talks through the one host (ARC-DEV-081) over its home map's
    /// dialog state, its follow standing stays the session party's, and after the conversation it stands where it stood.
    /// </summary>
    private void TalkToAreaMapVisitor(AreaCompanion companion)
    {
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-WORLD-048 visitor-talk smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_MAP_VISITOR_TALK_FAILED");
        }
        var cell = companion.Grid.State.CommittedCell;
        Require(_conversationHost is not null, "the shown map has its conversation host");
        var speaker = PresentSpeaker(companion.Key);
        Require(speaker is not null && SpeakerPresent(speaker), "the visitor is a speaker here");
        BeginConversation(speaker!);
        Require(ConversationOpen && _conversationHost is { } host && host.Speakers.Contains(speaker!) && host.StateMap(speaker!) == companion.Template.Home,
            $"the talk opened through the one host over its home-map state: open {ConversationOpen}, state map {_conversationHost?.StateMap(speaker!).RuntimeMap}, notice {_conversationNotice}");
        var started = _voiceStart?.Status;
        var detail = _voiceStart?.Detail;
        // The dialog script ran on its home-map slot: content, file or I/O failures are not a talk.
        Require(started is not (null or AreaConversationStartStatus.ContentUnavailable or AreaConversationStartStatus.SourceChanged
            or AreaConversationStartStatus.EntryError), $"the dialog script ran: {started} {detail}");
        EndConversation();
        var after = _areaCompanions?.Companions.SingleOrDefault(c => c.Entity == companion.Entity);
        Require(after is not null && after.Grid.State.CommittedCell == cell && after.Standing == CompanionStanding.Following,
            $"after the talk it stands where it stood and still follows: {_areaCompanions?.Status()}");
        GD.Print("ARC-WORLD-048 native-smoke visitor-talk PASS " + System.Text.Json.JsonSerializer.Serialize(new {
            Map = CurrentAreaMap, Home = companion.Template.Home.RuntimeMap, Script = companion.Speaker.ScriptId, Status = started?.ToString(), Detail = detail,
            At = $"{cell.X},{cell.Y}" }));
    }

    /// <summary>
    /// ARC-DEV-087 (MIG6e): the negative IN of a dialog takes an item from the carried companion's list, which is on its home map's seed (the
    /// dialog port attaches the seed a read on demand left out): the first of its items the session moves enters the hero's list with the home
    /// map as its origin, and the hero gives it back to the companion. A companion with no movable item, or with no session hero, is NONE.
    /// </summary>
    private void GiveFromAreaMapVisitor(AreaCompanion companion)
    {
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-DEV-087 visitor-give smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_MAP_VISITOR_GIVE_FAILED");
        }
        var home = companion.Template.Home;
        if (_areaSession is not { } session || session.QueryHero() is null || _inventoryHost is not { } inventory)
        { GD.Print($"ARC-DEV-087 native-smoke visitor-give NONE map={CurrentAreaMap} (no hero or inventory host)"); return; }
        // The candidates are the names of the companion's items in its home map's seed rows; the session may hold no seed of that map yet (the
        // dialog port attaches it, which is what this step observes).
        var homeSeed = inventory.Resolver.Held(home.RuntimeMap)?.Seed;
        var seedHolder = homeSeed?.Holders.FirstOrDefault(h => h.Entity == companion.Entity);
        if (homeSeed is null || seedHolder is null)
        { GD.Print($"ARC-DEV-087 native-smoke visitor-give NONE map={CurrentAreaMap} home={home.RuntimeMap} (the home map's seed lists no such holder)"); return; }
        var names = seedHolder.Items.Select(entity => homeSeed.Items.First(item => item.Entity == entity).Facts.Kind.Name).ToArray();
        var (pc, npc) = (AreaConversationState.PcParticipant, new DialogParticipantId(2));
        var port = inventory.DialogPort(session, id => id == npc ? companion.Entity : null);
        foreach (var name in names)
        {
            var before = session.QueryPcInventory().Count;
            var applied = port.Execute(new GiveItemCommand(npc, pc, name));
            var result = port.LastResult;
            // ARC-WORLD-1131: an item the insert check refused lies on the ground (Changed, a refusing verdict): not one that entered the list.
            if (result is not { Disposition: GameSessionDisposition.Changed, Item: { } item, Verdict: SessionInsertVerdict.Accepted }) continue;
            Require(applied == DialogCommandResult.Applied && session.QueryPcInventory().Count == before + 1, "the item entered the hero's list");
            var taken = session.QueryPcInventory().Single(entry => entry.Entity == item);
            Require(taken.Origin == new SessionMapId(home.RuntimeMap), $"the item keeps its home map as origin: {taken.Origin.RuntimeMap}");
            Require(session.QueryMapInventory(new(home.RuntimeMap))!.Holders.Single(h => h.Entity == companion.Entity).Items.All(entry => entry.Entity != item),
                "the companion's list no longer holds it");
            var back = session.GiveItemToNpc(session.Version, companion.Entity, item, inventory.InsertCheck);
            Require(back.Disposition == GameSessionDisposition.Changed, $"the hero gave it back: {back.Disposition} {back.Failure}");
            GD.Print("ARC-DEV-087 native-smoke visitor-give PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Map = CurrentAreaMap, Home = home.RuntimeMap, Candidates = names.Length, Name = name }));
            return;
        }
        GD.Print($"ARC-DEV-087 native-smoke visitor-give NONE map={CurrentAreaMap} home={home.RuntimeMap} (none of its {names.Length} items moves: {port.LastResult?.Failure})");
    }

    /// <summary>
    /// ARC-DEV-079 (MIG4): what a quick load or a fresh run on this foreign map leaves is no body kept and no map remembered. The home maps'
    /// directory is made anew and the companions are rebound from the session: the visiting companion's body and frames are read from its home
    /// map (a window of the main map around the stored cell the transfer recorded, or a whole map), it is drawn and still follows, and a
    /// fresh record resolver finds its base record there too (the case the AI kept it out of the rules for until now).
    /// </summary>
    private void ReadAreaMapVisitorBody(AreaCompanion companion)
    {
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-DEV-079 visitor-body smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_MAP_VISITOR_BODY_FAILED");
        }
        if (_areaSession is not { } session || _arguments is not { } arguments || _inventoryHost is not { } inventory)
        { GD.Print($"ARC-DEV-079 native-smoke visitor-body NONE map={CurrentAreaMap} (no session or inventory)"); return; }
        var home = companion.Template.Home;
        var stored = AreaCompanionPlacement.HomeCell(session, home, companion.Entity);
        Require(stored is not null, $"the transfer recorded the stored cell of its object on map {home.RuntimeMap}");
        Require(RecordAreaCompanions("visitor-body smoke"), "the companions' cells were recorded");
        var started = System.Environment.TickCount64;
        var production = _homes;
        _homes = new AreaHomeDirectory(new AreaMapHomes(arguments, MapCompanionFacts));
        _visitorFrames.Clear();
        BindAreaCompanions();
        var again = _areaCompanions?.Companions.SingleOrDefault(c => c.Entity == companion.Entity);
        var frames = again is null ? 0 : again.Template.Commands.Select(c => c.Frame).Distinct().Count(key => _visitorFrames.TryGetValue(again.Entity, out var kept) && kept.ContainsKey(key));
        Require(again is not null && _unreadVisitors.Count == 0 && again.Template.Key == companion.Key && again.Standing == CompanionStanding.Following,
            $"its body was read from map {home.RuntimeMap} and it still follows: {string.Join("; ", _unreadVisitors)} {_areaCompanions?.Status()}");
        Require(frames == again!.Template.Commands.Select(c => c.Frame).Distinct().Count() && frames > 0, $"the frames its body draws were read ({frames})");
        // A fresh resolver (nothing remembered, as after a quick load) finds the record through the same directory.
        var resolver = new Arcanum.World.RecordResolver(inventory.Resolver.Maps.Where(map => map.RuntimeMap == CurrentAreaMap),
            new AreaMapRecords(arguments, _homes, map => AreaVisitors.HomeWindow(session, map)));
        var lookup = resolver.Resolve(companion.Entity, home.RuntimeMap);
        Require(lookup.Status == Arcanum.World.RecordStatus.Found, $"a fresh resolver finds its record on map {home.RuntimeMap}: {lookup.Status} {lookup.Detail}");
        var reads = _homes.Reads.Select(read => new { read.Map, Window = read.CoreSectors is not null }).ToArray();
        // ARC-AI-1646: an NPC of the home map without a dialog that the hero leads (a Dominate Will target) has its body read from the home map too: the real read is asked for the
        // map's NPC holders (the rows the directory knows) as led, and builds a script-0 body, with its frames, for those whose follow facts read.
        var homeHeld = _homes.Held(home.RuntimeMap, stored);
        var speakerEntities = homeHeld?.Templates.Select(t => t.Entity).ToHashSet(StringComparer.Ordinal) ?? [];
        var ledCandidates = homeHeld?.Rows.Seed.Holders.Where(h => h.Kind == SessionHolderKind.Npc).Select(h => h.Entity)
            .Where(e => !speakerEntities.Contains(e)).Take(60).ToHashSet(StringComparer.Ordinal) ?? [];
        var ledRead = new AreaMapHomes(arguments, MapCompanionFacts).Read(home.RuntimeMap, stored, ledCandidates, default);
        var ledBodies = ledRead.Read?.Templates.Where(t => t.Speaker.ScriptId == 0 && ledCandidates.Contains(t.Entity)).ToArray() ?? [];
        Require(ledRead.Read is not null, $"the home map reads with led NPCs asked for: {ledRead.Detail}");
        Require(ledBodies.Length == 0 || ledBodies.All(t => ledRead.Read!.FramesOf(t).Count > 0), "the led NPC bodies come with their frames");
        GD.Print($"ARC-AI-1646 visitor-body led-npc candidates={ledCandidates.Count} bodies={ledBodies.Length} read-detail={ledRead.Detail}");
        // The production wiring (the inventory host's resolver shares this directory) is put back for the stages that follow.
        _homes = production; BindAreaCompanions();
        GD.Print("ARC-DEV-079 native-smoke visitor-body PASS " + System.Text.Json.JsonSerializer.Serialize(new {
            Map = CurrentAreaMap, Home = home.RuntimeMap, Stored = $"{stored!.Value.X},{stored.Value.Y}", Script = again.Speaker.ScriptId, Frames = frames,
            Reads = reads, LedCandidates = ledCandidates.Count, LedBodies = ledBodies.Length, Ms = System.Environment.TickCount64 - started }));
    }
}
