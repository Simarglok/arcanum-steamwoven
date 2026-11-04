using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Party;
using Arcanum.TerrainViewer.Core;
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

    private async Task RunAreaMapCompanionSmokeAsync()
    {
        _smokeCompanion = null;
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
            Require(ConversationOpen, "a conversation with the NPC opened: " + _conversationNotice);
            var joined = session.ForceJoinParty(session.Version, template.Entity);
            Require(joined.Disposition != GameSessionDisposition.Rejected, "joined: " + joined.Failure);
            for (var close = 0; close < 4 && ConversationOpen; close++) _conversationPanel!.LeaveForSelfTest.EmitSignal(BaseButton.SignalName.Pressed);
            Require(!ConversationOpen, "the conversation closed");
            Require(_areaCompanions?.Companions.SingleOrDefault(c => c.Entity == template.Entity) is { Standing: CompanionStanding.Following },
                "the NPC is a following companion: " + _areaCompanions?.Status());
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
            _smokeCompanion = template.Entity;
            GD.Print("ARC-WORLD-037 native-smoke follow PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Map = _readyMapName, template.Speaker.ScriptId, Prototype = template.Speaker.Row.PrototypeNumber, template.Facts.Level, template.Facts.Thresholds,
                From = map.Chart.ToWorld(template.HomeOrigin), Pc = map.Chart.ToWorld(pc), At = world, UnderRoof = underRoof, Frames = frames, Templates = _companionTemplates.Count, Parity = "NOT_VERIFIED" }));
        }
        finally
        {
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
        }
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
        if (companion.Template.Home.RuntimeMap != CurrentAreaMap) { TalkToAreaMapVisitor(companion); FightNextToAreaMapVisitor(companion); }
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
        { GD.Print("ARC-DEV-065 native-smoke visitor-fight NONE (no hero)"); return; }
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
            // The hero's attack: the attacker's followers are told, the victim weighs fleeing against the hero's party, witnesses hear it.
            host.HeroAttacked(entity);
            _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            var hostMs = 30_000_000L;
            TickAreaClock(hostMs);
            var frames = 0;
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
    /// ARC-WORLD-048: on the destination the carried companion is a visitor: it talks through its own host over its home map's
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
        Require(_conversationHost is not null && !_conversationHost.Speakers.Any(s => s.Row.Key == companion.Key), "the shown map's host does not hold the visitor");
        var speaker = VisitorSpeaker(companion.Key);
        Require(speaker is not null && SpeakerPresent(speaker), "the visitor is a speaker here");
        BeginConversation(speaker!);
        Require(ConversationOpen && _talkHost is { } host && host.Map == companion.Template.Home && host.Speakers.Contains(speaker!),
            $"the talk opened through its home-map host: open {ConversationOpen}, host map {_talkHost?.Map.RuntimeMap}, notice {_conversationNotice}");
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
}
