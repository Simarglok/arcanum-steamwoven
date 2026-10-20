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
    }
}
