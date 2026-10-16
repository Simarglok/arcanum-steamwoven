using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Party;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-WORLD-016: Virgil follows the PC in crash-site-area. The measured companions of the shown map (the roster in
// AreaCompanionRoster) are moving actors of an AreaCompanionGroup: each walks on its own grid session over the map's
// T0 step evidence, and while the session party says it follows the PC it is driven after the PC to the same frame
// boundary (start beyond 4 cells, stop within 2, stand on the PC beyond 30 then step off). The PC and the companions
// block each other where they stand; a companion's T0 record no longer blocks its serialized cell. The group is
// rebuilt from the session whenever the PC's session is (spawn, reset, reload, transfer, quick load); a companion's
// cell is written to the session when the PC leaves the map and on F7, like the PC's. The body is the NPC's T0 frame
// moved with the actor. ARC-WORLD-017: a following companion goes with the PC through the cave entrance and exit and is
// drawn, lit, picked and talked to where it stands there; on the exterior its T0 body is not in the world while it is away.
public partial class TerrainViewerApp
{
    private AreaCompanionGroup? _areaCompanions;
    private string? _companionNotice;
    // ARC-WORLD-017: the roster companions' home T0 data (exterior), set with the area session.
    private IReadOnlyList<AreaCompanionTemplate> _companionTemplates = [];

    /// <summary>Rebuilds the companions of the shown map from the session (placement and standing), at the PC's time.</summary>
    private void BindAreaCompanions()
    {
        _areaCompanions = null;
        if (_areaSession is not { } session || _conversationHost is not { PartyWired: true } || _worldActor.Session is not AreaGridSession grid) return;
        _areaCompanions = AreaCompanionGroup.Create(_companionTemplates, grid.Snapshot, grid.Chart, session, new(CurrentAreaMap), grid.State.Time, grid.Timing);
        foreach (var skipped in _areaCompanions.Skipped) GD.Print("ARC-WORLD-016 companion skipped " + skipped);
        GD.Print("ARC-WORLD-016 companions bound " + System.Text.Json.JsonSerializer.Serialize(_areaCompanions.Companions.Select(c => new {
            c.Speaker.ScriptId, Standing = c.Standing.ToString(), Cell = grid.Chart.ToWorld(c.Grid.State.CommittedCell), Visiting = c.Template.Home.RuntimeMap != CurrentAreaMap }).ToArray())
            + " map=" + CurrentAreaMap + " absent=" + _areaCompanions.Absent.Count);
    }

    private void UnbindAreaCompanions() { _areaCompanions = null; _companionNotice = null; }

    /// <summary>Writes the companions' cells to the session (quick save, leaving the map). False when the session refused.</summary>
    private bool RecordAreaCompanions(string why)
    {
        if (_areaCompanions is not { } group || _areaSession is not { } session) return true;
        var failure = group.Record(session);
        GD.Print($"ARC-WORLD-016 companions recorded why={why} failure={failure}");
        if (failure == GameSessionFailure.None) return true;
        _companionNotice = $"companion placement not saved ({failure})";
        return false;
    }

    /// <summary>One frame after the PC's advance to <paramref name="frontier"/>: every following companion drives to it.</summary>
    private void DriveAreaCompanions(IGridSession session, SimulationTime frontier)
    {
        if (_areaCompanions is not { } group || _areaSession is not { } owner || session is not AreaGridSession pc) return;
        foreach (var (companion, advance) in group.Drive(owner, pc.State, frontier))
        foreach (var item in advance.Movement.Events)
            if (item.Kind is MovementEventKind.StepStarted or MovementEventKind.StepRejected or MovementEventKind.CenterArrived)
                GD.Print($"ARC-WORLD-016 companion script={companion.Speaker.ScriptId} event={item.Kind} {item.Key.From.X},{item.Key.From.Y}->{item.Key.To.X},{item.Key.To.Y} reason={item.Reason}"
                    + (advance.AreaEvidence.LastOrDefault(e => e.Key == item.Key) is { } evidence ? $" step={evidence.Live?.ReasonCode ?? evidence.Assessment.ReasonCode}" : ""));
        foreach (var companion in group.Companions.Where(c => c.LastUpdate.Action == FollowAction.Jump && c.Grid.State.Time == frontier))
            GD.Print($"ARC-WORLD-016 companion script={companion.Speaker.ScriptId} jumped onto the PC cell {pc.State.CommittedCell}");
    }

    /// <summary>The companions' bodies as they stand now, for the shown plan only.</summary>
    private IReadOnlyList<AreaDrawCommand>? AreaCompanionCommands() =>
        _areaCompanions is { } group && _readyArea is { } area && group.Map.RuntimeMap == area.Snapshot.RuntimeMap ? group.Commands() : null;

    /// <summary>ARC-WORLD-017: whether a dialog NPC is in the shown map's world: a companion standing here, or (on the
    /// exterior, the speakers' map) any other speaker that is not a companion away on another map.</summary>
    private bool SpeakerPresent(AreaSpeaker speaker) => _areaCompanions is { } group
        ? group.Of(speaker) is not null || (!InCave && !group.Absent.Contains(speaker.Row.Key))
        : !InCave;

    /// <summary>Where a speaker stands now: a companion's live cell, else its T0 cell.</summary>
    private Func<AreaSpeaker, (long X, long Y)>? SpeakerCell => _areaCompanions is { } group ? group.WorldCell : null;

    private string CompanionStatus()
    {
        if (_conversationHost is not { PartyWired: true }) return "";
        var text = _areaCompanions is { } group ? group.Status() : "companions: none";
        return " | " + text + (_companionNotice is { } notice ? " | " + notice : "");
    }

    // Native observation (ARC-WORLD-016) on the exterior: Virgil joins through his own dialog and the session party
    // port, then follows the PC to within 2 cells through the frames, is drawn on his new cell, waits when told to, and
    // his cell is session state. Frames run on simulated focus and test time, as the transition smoke does. Runs last
    // of the area stages, so the others see Virgil at T0.
    private async Task RunAreaFollowSmokeAsync()
    {
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        try
        {
            // A failed check prints its detail, then throws a closed code the smoke runner reports.
            static void Require(bool value, string why)
            {
                if (value) return;
                GD.Print("ARC-WORLD-016 follow smoke FAIL: " + why);
                throw new InvalidOperationException("AREA_FOLLOW_SMOKE_FAILED");
            }
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            Require(_areaTransitions?.CurrentMap == 1 && !InCave, "the exterior is published");
            var map = _areaTransitions!.Current;
            var host = _conversationHost!;
            var speaker = host.Speakers.Single(s => AreaCompanionRoster.For(s) is not null);
            var origin = map.Chart.ToChart(new(speaker.WorldX, speaker.WorldY));
            // Stand next to Virgil and talk: the first Direct replies walk his join (JO through the session party).
            AreaSmokePlace(map, AreaSmokeStandable(map, origin, 2));
            var companion = _areaCompanions!.Companions.Single();
            Require(companion.Standing == CompanionStanding.Free && companion.Grid.State.CommittedCell == origin, "Virgil starts free on his T0 cell");
            BeginConversation(speaker);
            var conversation = _conversationPanel!.Conversation ?? throw new InvalidOperationException("ARC-WORLD-016 follow smoke: no conversation");
            for (var i = 0; i < 64 && conversation.Phase == Arcanum.Scripting.Dialog.DialogConversationPhase.AwaitingSelection; i++)
            {
                var option = conversation.Options.FirstOrDefault(o => o.Block is null && o.Transition == Arcanum.Scripting.Dialog.DialogOptionTransition.Direct)
                    ?? conversation.Options.First(o => o.Block is null);
                conversation.Select(option.Token);
            }
            EndConversation();
            Require(_areaSession!.QueryCompanion(companion.Entity).Standing == CompanionStanding.Following, "the dialog joined him through the session: " + conversation.Phase);

            // The PC 12 cells away: frames bring Virgil within 2, settled, and draw him there.
            var far = AreaSmokeStandable(map, origin with { X = origin.X + 12 }, 3);
            AreaSmokePlace(map, far);
            companion = _areaCompanions!.Companions.Single();
            // The placement frames already run: rebound following from his T0 cell, he has started toward the PC.
            Require(companion.Standing == CompanionStanding.Following && companion.Grid.State.CommittedCell.X >= origin.X, "rebound following: " + group());
            var pc = ((AreaGridSession)_worldActor.Session!).State.CommittedCell;
            for (var i = 0; i < 400 && (companion.Grid.State.Phase != MovementPhase.Settled
                || FollowDistanceRules.Distance(companion.Grid.State.CommittedCell, pc) > FollowDistanceRules.Range); i++) AreaSmokeFrame();
            var cell = companion.Grid.State.CommittedCell;
            var plans = companion.Follow.Route.PlannedSegments;
            Require(FollowDistanceRules.Distance(cell, pc) <= FollowDistanceRules.Range && companion.Grid.State.Phase == MovementPhase.Settled,
                $"Virgil followed within 2: {cell} vs PC {pc}; {group()}");
            // Frame the PC (an earlier stage may have left the camera elsewhere), then observe the drawn submission.
            _camera!.ResetWorld(WorldAnchor, WorldViewport, _worldPreferredZoom);
            PublishWorldSelection();
            for (var frame = 0; frame < 3; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            }
            var world = map.Chart.ToWorld(cell);
            Require(_canvas!.AreaVisibleCommands.Any(c => c.Identity == companion.Key && c.MapX == world.X && c.MapY == world.Y)
                && !_canvas.AreaVisibleCommands.Any(c => c.Identity == companion.Key && c.MapX == speaker.WorldX && c.MapY == speaker.WorldY),
                "his body is drawn on his new cell only: expected " + world + " drawn " + string.Join(",", _canvas.AreaVisibleCommands
                    .Where(c => c.Identity == companion.Key).Select(c => $"({c.MapX},{c.MapY}) dest {c.Destination.X},{c.Destination.Y}"))
                    + $" moving {AreaCompanionCommands()?.Count ?? -1} map {_areaCompanions?.Map.RuntimeMap} ready {_readyArea?.Snapshot.RuntimeMap} visible {_canvas.AreaVisibleCount}");
            // He is picked where he is drawn: the hover names him and a left click there talks (his follower entry).
            var drawn = _canvas.AreaVisibleCommands.First(c => c.Identity == companion.Key);
            var point = PickedScreenPoint(drawn);
            Require(point is not null, "the pick finds him on an opaque pixel of his drawn body");
            AreaSmokeClick(point!.Value);
            Require(ConversationOpen, "a click on his drawn body talks: " + _conversationNotice);
            EndConversation();
            var capture = System.Environment.GetEnvironmentVariable("ARC_AREA_FOLLOW_CAPTURE");
            if (!string.IsNullOrWhiteSpace(capture))
            {
                using var image = GetViewport().GetTexture().GetImage();
                if (image.SavePng(capture) != Error.Ok) throw new InvalidOperationException("ARC-WORLD-016 follow smoke: capture failed");
            }
            // His cell is session state, and talking now uses where he stands.
            Require(RecordAreaCompanions("smoke"), "placement recorded");
            Require(_areaSession.QueryMapChange(new(1), AreaCompanionPlacement.Key(companion.Entity)) is { } value
                && AreaCompanionPlacement.Decode(value)?.World == new CellCoord((int)world.X, (int)world.Y), "the session holds his cell");
            Require(host.Nearest(map.Chart.ToWorld(pc).X, map.Chart.ToWorld(pc).Y, SpeakerCell) == speaker, "within talk reach where he stands");

            // Waiting: the PC goes back to where Virgil started; he stays on his recorded cell.
            Require(_areaSession.WaitParty(_areaSession.Version, companion.Entity).Disposition == GameSessionDisposition.Changed, "wait");
            AreaSmokePlace(map, AreaSmokeStandable(map, origin, 2));
            companion = _areaCompanions!.Companions.Single();
            Require(companion.Grid.State.CommittedCell == cell && companion.Standing == CompanionStanding.Waiting, "rebound waiting on his recorded cell: " + group());
            var revision = _areaSession.Version.Revision;
            for (var i = 0; i < 40; i++) AreaSmokeFrame();
            Require(companion.Grid.State.CommittedCell == cell && companion.Standing == CompanionStanding.Waiting
                && FollowDistanceRules.Distance(cell, ((AreaGridSession)_worldActor.Session!).State.CommittedCell) > FollowDistanceRules.StartDistance,
                "a waiting Virgil stays although the PC is farther than 4");
            GD.Print("ARC-WORLD-016 native-smoke follow PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Origin = new { speaker.WorldX, speaker.WorldY }, Pc = map.Chart.ToWorld(pc), Followed = world, Distance = FollowDistanceRules.Distance(cell, pc),
                Plans = plans, Session = revision, Parity = "NOT_VERIFIED" }));
            string group() => _areaCompanions?.Status() ?? "none";
        }
        finally
        {
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
        }
    }

    // Native observation (ARC-WORLD-017), after the follow stage (Virgil waits on the exterior): following again, he goes
    // with the PC through the entrance onto the arrival cell, steps off it, is drawn and lit there and talks when clicked;
    // told to wait in the cave he stays there while the PC leaves, and the exterior has no T0 Virgil meanwhile; following
    // again he comes back out with the PC. Every transfer goes through the production use click (UseNextToTriggerAsync).
    private async Task RunAreaCompanionCaveSmokeAsync()
    {
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        try
        {
            static void Require(bool value, string why)
            {
                if (value) return;
                GD.Print("ARC-WORLD-017 cave smoke FAIL: " + why);
                throw new InvalidOperationException("AREA_COMPANION_CAVE_SMOKE_FAILED");
            }
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            Require(_areaTransitions?.CurrentMap == 1 && !InCave && _areaCompanions is { Companions.Count: 1 }, "the exterior with Virgil is published");
            var session = _areaSession!;
            var template = _companionTemplates.Single();
            var key = template.Key; var entity = template.Entity;
            var facts = AreaCompanionRoster.Facts(template.Facts, 0, 50);
            Require(session.UnwaitParty(session.Version, entity, facts, false).Disposition == GameSessionDisposition.Changed, "unwait on the exterior");
            string group() => _areaCompanions?.Status() ?? "none";
            AreaCompanion Virgil() => _areaCompanions!.Companions.Single();
            CellCoord Pc() => ((AreaGridSession)_worldActor.Session!).State.CommittedCell;
            void Settle()
            {
                for (var i = 0; i < 80 && (Virgil().Grid.State.Phase != MovementPhase.Settled || Virgil().Grid.State.CommittedCell == Pc()); i++) AreaSmokeFrame();
            }
            async Task<bool> Drawn(Arcanum.Data.Traversal.AreaStepCell? at)
            {
                PublishWorldSelection();
                for (var frame = 0; frame < 3; frame++)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                }
                return _canvas!.AreaVisibleCommands.Any(c => c.Identity == key && (at is null || (c.MapX == at.Value.X && c.MapY == at.Value.Y)));
            }

            // In: he goes with the PC onto the arrival cell, then steps off it.
            var inbound = await UseNextToTriggerAsync(79);
            var caveMap = _areaTransitions!.Current;
            var arrival = caveMap.Chart.Spawn;
            Require(InCave && _areaCompanions is { Companions.Count: 1 } && Virgil().Template.Home.RuntimeMap == 1, "Virgil came into the cave: " + group());
            Require(AreaCompanionPlacement.Where(session, template.Home, entity) is { Map.RuntimeMap: 79, Placement: { } placed }
                && caveMap.Chart.ToChart(new(placed.World.X, placed.World.Y)) == arrival, "the session puts him on the cave arrival cell");
            Settle();
            var caveCell = Virgil().Grid.State.CommittedCell;
            Require(FollowDistanceRules.Distance(caveCell, Pc()) == 1 && Virgil().Grid.State.Phase == MovementPhase.Settled, "he stepped off the shared cell: " + group());
            var caveWorld = caveMap.Chart.ToWorld(caveCell);
            _camera!.ResetWorld(WorldAnchor, WorldViewport, _worldPreferredZoom);
            Require(await Drawn(caveWorld), "he is drawn on his cave cell: " + string.Join(",", _canvas!.AreaVisibleCommands.Where(c => c.Identity == key).Select(c => $"({c.MapX},{c.MapY})")));
            var drawn = _canvas.AreaVisibleCommands.First(c => c.Identity == key);
            var point = PickedScreenPoint(drawn);
            Require(point is not null, "the pick finds him in the cave");
            var capture = System.Environment.GetEnvironmentVariable("ARC_AREA_CAVE_FOLLOW_CAPTURE");
            if (!string.IsNullOrWhiteSpace(capture))
            {
                using var image = GetViewport().GetTexture().GetImage();
                if (image.SavePng(capture) != Error.Ok) throw new InvalidOperationException("ARC-WORLD-017 cave smoke: capture failed");
            }
            AreaSmokeClick(point!.Value);
            Require(ConversationOpen, "a click on him in the cave talks: " + _conversationNotice);
            EndConversation();

            // Waiting in the cave: the PC leaves alone; he stays, and the exterior has no T0 Virgil.
            Require(session.WaitParty(session.Version, entity).Disposition == GameSessionDisposition.Changed, "wait in the cave");
            // The smoke stands the PC next to the exit with a fresh session, which rebuilds the group from the session: his
            // cell is recorded first, as F7 or leaving the map would.
            Require(RecordAreaCompanions("smoke"), "placement recorded");
            await UseNextToTriggerAsync(1);
            Require(AreaCompanionPlacement.Where(session, template.Home, entity) is { Map.RuntimeMap: 79, Placement: { } stayed }
                && stayed.World == new CellCoord((int)caveWorld.X, (int)caveWorld.Y), "a waiting Virgil stays on his cave cell");
            Require(_areaCompanions is { Companions.Count: 0 } && _areaCompanions.Absent.Contains(key), "absent from the exterior: " + group());
            var t0 = template.Commands.First();
            _camera.ResetWorld(new GamepadLab.Core.Vec2(t0.Destination.X + t0.Destination.Width / 2d, t0.Destination.Y + t0.Destination.Height / 2d), WorldViewport, 1);
            Require(!await Drawn(null), "his T0 body is not drawn while he is away");
            Require(_conversationHost!.Nearest(template.Speaker.WorldX, template.Speaker.WorldY, SpeakerCell, SpeakerPresent) is null, "nobody to talk to on his T0 cell");
            Require(_areaCompanions!.OccupancyForPc().Moved.Contains(key), "his T0 record does not block");

            // Back in: he waits where he stood; following again, he comes out with the PC.
            await UseNextToTriggerAsync(79);
            Require(_areaCompanions is { Companions.Count: 1 } && Virgil().Grid.State.CommittedCell == caveCell && Virgil().Standing == CompanionStanding.Waiting,
                "he waited in the cave: " + group());
            Require(session.UnwaitParty(session.Version, entity, facts, false).Disposition == GameSessionDisposition.Changed, "unwait in the cave");
            var outbound = await UseNextToTriggerAsync(1);
            Require(_areaCompanions is { Companions.Count: 1, Absent.Count: 0 } && session.QueryMapChange(template.Home, AreaCompanionPlacement.MapKey(entity)) is null,
                "he came back out: " + group());
            Require(Virgil().Grid.State.CommittedCell == Pc(), "on the PC's arrival cell");
            Settle();
            var home = Virgil().Grid.State.CommittedCell;
            _camera.ResetWorld(WorldAnchor, WorldViewport, _worldPreferredZoom);
            var homeWorld = _areaTransitions!.Current.Chart.ToWorld(home);
            Require(FollowDistanceRules.Distance(home, Pc()) == 1 && await Drawn(homeWorld), "drawn next to the PC outside: " + group());
            GD.Print("ARC-WORLD-017 native-smoke cave PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Inbound = inbound, CaveArrival = caveMap.Chart.ToWorld(arrival), Cave = caveWorld, Outbound = outbound, Outside = homeWorld,
                Session = session.Version.Revision, Parity = "NOT_VERIFIED" }));
        }
        finally
        {
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
        }
    }

    /// <summary>A screen point outside the HUD where <paramref name="command"/> draws an opaque pixel and the production
    /// pick answers its object, or null.</summary>
    private Vector2? PickedScreenPoint(AreaDrawCommand command)
    {
        var transform = AreaDrawItem.RasterTransform(GetViewport()); var size = GetViewport().GetVisibleRect().Size;
        var hud = _hud!.Rect;
        for (var y = 0; y < (int)size.Y; y += 2)
        for (var x = 0; x < (int)size.X; x += 2)
        {
            var point = new Vector2(x + .5f, y + .5f);
            if (!hud.HasPoint(point) && AreaAlphaAt(command, _readyArea!.Frames, transform, x, y) != 0 && AreaObjectAtScreen(point) == command.Identity) return point;
        }
        return null;
    }

    /// <summary>A cell within <paramref name="range"/> of <paramref name="center"/> the PC can stand on (see <c>ApproachCell</c>).</summary>
    private CellCoord AreaSmokeStandable(AreaMapRuntime map, CellCoord center, int range)
    {
        var world = map.Chart.ToWorld(center);
        var probe = new AreaGridSession(map.Steps, SimulationTime.At(0), chart: map.Chart, start: map.Chart.Spawn);
        return ApproachCell(probe, world.X, world.Y, range) ?? throw new InvalidOperationException("ARC-WORLD-016 follow smoke: no standable cell near " + center);
    }
}
