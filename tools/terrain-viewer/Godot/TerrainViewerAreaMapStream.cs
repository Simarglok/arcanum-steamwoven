using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-RENDER-024: the main world map in map mode, streamed by window. A map too large to load whole opens as a window of
// 3×3 core sectors and their context ring (ARC-DATA-032) around a sector (AreaMapLoadOperation). Whenever the actor
// stands in a core sector other than the window's centre, the window around that sector is loaded in the background while
// the shown one stays drawn and playable; once it is loaded (and no dialog, inventory, sheet, logbook or palette is open)
// it replaces the shown one in one publication, which carries the game session (as a transfer does, MapTransferHandoff
// of kind Stream) and stands the actor where it walked meanwhile, with its route goal kept when the new window holds it.
// The original keeps no fixed window: its sector cache holds the 16 most recently used sectors and a teleport pre-caches
// the 3×3 around its target (docs/research/main-map-streaming.md, SS1-SS4); the 3×3 core is that pre-cache, kept around
// the actor as it walks. The window moves once the actor stands StreamMargin cells beyond the centre sector, so a walk
// along a sector border (or a path that bends back over it) does not reload it at every step; the actor then still has
// at least 48 loaded cells ahead. A window that cannot load leaves the shown one in place and is tried again once the
// actor has stood in the centre sector again; another sector is tried at once. No window moves while the scene holds an
// intent a new scene would drop, in combat mode or under a panel (StreamHeld); a move whose window no longer holds the
// actor when it can publish is given up.
// ARC-WORLD-042: a route goal the new window does not hold (the walk bent away from it and the window followed) is not
// dropped: the walk heads for the end of a partial corridor toward it over the new window (RouteCorridor), the cell nearest
// it, and the far goal is kept, so each later move hands it on until a window holds it. A new goal (a click) replaces it.
public partial class TerrainViewerApp
{
    private AreaSectorCoordinate? _mapWindowCentre;
    private AreaSectorCoordinate? _mapStreamFailedAt;
    private string? _mapStreamStatus;
    private int _mapWindowMoves;
    // A route goal beyond the shown window and the cell of it the walk heads for (ARC-WORLD-042).
    private (AreaStepCell Goal, AreaStepCell Toward)? _mapFarGoal;
    // Window moves given up (the actor walked off the new core) or loaded but not published; the walk smoke needs 0.
    private int _mapStreamGivenUp;
    /// <summary>Cells beyond the centre sector's edge (either axis) the actor stands before the window moves.</summary>
    internal const int StreamMargin = 16;
    // In a native smoke the window moves only in its own stage, so the other stages observe one fixed window.
    private bool _mapStreamSmokeActive;

    /// <summary>The window centre of the published map-mode load (null for a whole map).</summary>
    private void NoteMapWindow(TerrainLoadResult result)
    {
        _mapWindowCentre = result.AreaMapWindowCentre;
        if (_mapWindowCentre is null) { _mapStreamStatus = null; _mapWindowMoves = 0; _mapStreamFailedAt = null; _mapFarGoal = null; }
    }

    /// <summary>ARC-DATA-037: the arguments with the data rules of the session a load carries (a transfer, a window move, a quick
    /// load), so a map opens under the session's mode; without one the default rules (no rules kept from an earlier load).</summary>
    private static TerrainViewerArguments WithSessionDataRules(TerrainViewerArguments arguments, Arcanum.Session.GameSessionOwner? session) =>
        arguments with { DataRules = session?.Rules.Data };

    private bool MapStreamInFlight => _mapHandoff is { Kind: MapHandoffKind.Stream };

    /// <summary>How many cells the tile lies beyond the sector's square (the larger of the two axes; 0 inside it).</summary>
    internal static long Beyond(AreaSectorCoordinate sector, AreaStepCell cell) =>
        Math.Max(Math.Max(sector.OriginX - cell.X, cell.X - (sector.OriginX + 63)), Math.Max(Math.Max(sector.OriginY - cell.Y, cell.Y - (sector.OriginY + 63)), 0));

    /// <summary>Per frame in map mode: starts the load of the window around the actor's sector when the actor has left the
    /// shown window's centre sector by the margin. A walk to use, talk or pick up, a pending transfer, combat mode or an open
    /// dialog, inventory, sheet, logbook or palette keep the window where it is until it ends.</summary>
    private void ProcessAreaMapStream(AreaGridSession session)
    {
        if (_mapWindowCentre is not { } centre || _mapHandoff is not null || StreamHeld || _readyMapName is null || _arguments is null
            || (_nativeSmoke && !_mapStreamSmokeActive)) return;
        var cell = session.Chart.ToWorld(session.State.CommittedCell);
        var sector = new AreaSectorCoordinate(cell.X >> 6, cell.Y >> 6);
        // A sector whose window failed is tried again once the actor has stood in the centre sector again.
        if (sector == centre) { _mapStreamFailedAt = null; return; }
        if (sector == _mapStreamFailedAt || Beyond(centre, cell) < StreamMargin) return;
        BeginMapStream(session, cell, sector);
    }

    /// <summary>What keeps the shown window: an intent the scene holds that a new scene would drop (a pending transfer, a walk
    /// to use, talk or pick up), combat mode, or a modal panel.</summary>
    private bool StreamHeld => _mapTransfers?.Pending is not null || _useOpenPending is not null || ShownHeroRunsHost?.UseRun is not null || _talkOpenPending is not null || ShownHeroRunsHost?.TalkRun is not null
        || ShownHeroRunsHost?.PickupRun is not null || HeroInCombatMode || WorldModal;

    private void BeginMapStream(AreaGridSession session, AreaStepCell cell, AreaSectorCoordinate sector)
    {
        var origin = _arguments!;
        var handoff = new MapTransferHandoff(_areaSession, CurrentAreaMap, _readyMapName!, session.State.Facing, origin, cell, _readyMapName!,
            Kind: MapHandoffKind.Stream);
        _mapHandoff = handoff;
        var next = origin with { AreaMapSector = sector, AreaMapArrival = new(cell.X, cell.Y) };
        _mapStreamStatus = $"loading the window around sector ({sector.X},{sector.Y})";
        GD.Print("ARC-RENDER-024 stream begin " + System.Text.Json.JsonSerializer.Serialize(new { From = _mapWindowCentre is { } c ? new { c.X, c.Y } : null,
            To = new { sector.X, sector.Y }, Cell = cell }));
        _lastLoad = StreamMapWindowAsync(next, handoff, sector);
    }

    /// <summary>A stream that ends without publishing gives up its handoff; the session it carried ends unless the scene or a
    /// newer handoff still holds it (a reload during the stream leaves it to nobody).</summary>
    private void EndMapStream(MapTransferHandoff handoff)
    {
        if (ReferenceEquals(_mapHandoff, handoff)) _mapHandoff = null;
        if (handoff.Session is { } session && !ReferenceEquals(session, _areaSession) && !ReferenceEquals(session, _mapHandoff?.Session)
            && !ReferenceEquals(session, _mapHandoff?.Previous)) session.Dispose();
    }

    /// <summary>Loads the next window in the background and publishes it in place of the shown one; the shown scene stays
    /// until then. A newer load (a transfer, a reload) supersedes the stream; F7/F8 wait for it (another map is loading).</summary>
    private async Task StreamMapWindowAsync(TerrainViewerArguments next, MapTransferHandoff handoff, AreaSectorCoordinate sector)
    {
        if (_exiting) return;
        var started = System.Environment.TickCount64;
        var prior = Interlocked.Exchange(ref _loadCancellation, new CancellationTokenSource());
        prior?.Cancel(); prior?.Dispose();
        var token = _loadCancellation!.Token;
        _generation = _coordinator.BeginGeneration();
        var generation = _generation;
        TerrainLoadResult result;
        next = WithSessionDataRules(next, handoff.Session);
        try { result = await _coordinator.LoadAsync(generation, next, token); }
        catch (OperationCanceledException) { EndMapStream(handoff); return; }
        catch (Exception exception)
        {
            if (_exiting) return;
            // A superseded stream ends quietly; the newer load owns the scene and the status.
            if (!ReferenceEquals(_mapHandoff, handoff)) { EndMapStream(handoff); return; }
            EndMapStream(handoff);
            StreamFailed(sector, Frontier(exception), "not loaded");
            return;
        }
        var loaded = System.Environment.TickCount64 - started;
        // The shown window stays while an intent the new scene would drop is pending (a transfer applies and supersedes the
        // stream; a walk to use, talk or pick up runs to its end), in combat mode, and while a panel is open.
        while (!_exiting && ReferenceEquals(_mapHandoff, handoff) && (StreamHeld || _worldLifecycleSession is not null || _worldResetPending || _worldReloadPending))
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (_exiting || !ReferenceEquals(_mapHandoff, handoff)) { EndMapStream(handoff); return; }
        // The actor stands where it walked meanwhile. When the new window does not admit that cell (a hold let the actor walk
        // off its core, or a same-map jump moved it), the move is given up, not applied: the next frame starts the window
        // around where the actor stands, and the actor is never put back on the cell the move began at.
        var area = result.Area!; var chart = area.Plan.Chart;
        AreaStepCell at = chart.ToWorld(chart.Spawn); var facing = handoff.Facing; AreaStepCell? goal = null;
        if (_worldActor.Session is AreaGridSession walking)
        {
            var now = walking.Chart.ToWorld(walking.State.CommittedCell);
            if (!chart.Contains(chart.ToChart(now)) || !result.AreaStepSnapshot!.IsAdmitted(now))
            {
                EndMapStream(handoff);
                _mapStreamStatus = $"window around ({sector.X},{sector.Y}) given up: the actor walked off it meanwhile"; _mapStreamGivenUp++;
                GD.Print("ARC-RENDER-024 stream abandoned " + System.Text.Json.JsonSerializer.Serialize(new { To = new { sector.X, sector.Y }, Actor = now }));
                RefreshAreaStatus();
                return;
            }
            at = now; facing = walking.State.Facing;
            if (ReferenceEquals(_worldRouteSession, walking) && _worldRoute is { Status: RouteFollowStatus.Following or RouteFollowStatus.Arrived } route
                && route.Target is { } target)
            {
                var heading = walking.Chart.ToWorld(target);
                // Heading for (or already on) the cell nearest a far goal: the far goal is handed on. Another arrived walk ends.
                if (_mapFarGoal is { } far && far.Toward == heading) goal = far.Goal;
                else if (route.Status == RouteFollowStatus.Following) goal = heading;
            }
        }
        var moved = handoff with { Facing = facing, OriginCell = at };
        _mapHandoff = moved;
        result = result with { Area = area with { Plan = area.Plan.WithSpawn(chart.ToChart(at)) } };
        var arrived = next with { AreaMapArrival = new(at.X, at.Y) };
        var from = _mapWindowCentre;
        // The companions' cells go to the session first, so the new window binds them where they stand.
        Exception? refused = null;
        var published = _coordinator.TryPublish(generation, () =>
        {
            // ARC-DEV-119 (MIG9 S5c): the session goes on across the window move, so the map's AI host does too (it is bound to the new window below).
            _aiKeepAcrossWindowMove = true;
            var swap = System.Diagnostics.Stopwatch.StartNew();
            try { RecordAreaCompanions("window move"); ClearPublishedScene(); _arguments = arrived; PublishReadyResult(arrived, result); _cursorReapply = _cursorShown is not null; GD.Print($"ARC-UI-056 window swap on the main thread {swap.ElapsedMilliseconds} ms shown={_cursorShown is not null} shape={Godot.Input.GetCurrentCursorShape()}"); }
            catch (Exception exception) { refused = exception; }
            finally { _aiKeepAcrossWindowMove = false; }
        });
        if (refused is not null)
        {
            // The shown scene is gone: the failure is published as for any load (the session ends with the scene).
            EndMapStream(moved);
            GD.Print("ARC-RENDER-024 stream publication failed " + FailureDetail(refused));
            PublishFailure(TerrainViewerMode.Area, new(TerrainViewerErrorCode.RenderFailure, Stage: TerrainViewerFailureStage.Upload));
            if (_nativeSmoke) { GD.Print("ARC-RENDER-007 frontier=AREA_STREAM_PUBLICATION"); GetTree().Quit(1); }
            return;
        }
        if (!published) { EndMapStream(moved); _mapStreamGivenUp++; return; }
        _mapWindowMoves++; _mapStreamFailedAt = null;
        // ARC-DEV-119: the AI host takes the new window's scene now, so nothing reads the old one's chart, rows or providers before the next frame.
        _ = AreaAiHostNow();
        // The walk goes on to its goal when the new window holds it, else toward it through the cell of the window nearest it.
        var resumed = false; _mapFarGoal = null;
        if (goal is { } wanted && _worldActor.Session is AreaGridSession placed && WorldRoute(placed) is { } follower
            && AreaMapGoalHandOff.Resume(new AreaRouteSource(placed.Snapshot, placed.Chart, placed.Occupancy, () => placed.Portals?.Invoke(), () => !HeroInCombatMode), placed.Chart, placed.State.CommittedCell, wanted) is { } resume)
        {
            follower.SetTarget(resume.Target, WorldTime); resumed = true;
            if (resume.FarGoal is { } far) _mapFarGoal = (far, placed.Chart.ToWorld(resume.Target));
        }
        if (from is { } previous) _mapStreamLog.Add((previous, sector, at, resumed, _areaSession, loaded));
        _mapStreamStatus = (_mapStreamStatus?.StartsWith("window moved but", StringComparison.Ordinal) == true ? _mapStreamStatus + "; " : string.Empty)
            + $"window moved to sector ({sector.X},{sector.Y}) in {loaded} ms (move {_mapWindowMoves})";
        GD.Print("ARC-RENDER-024 stream published " + System.Text.Json.JsonSerializer.Serialize(new {
            From = from is { } f ? new { f.X, f.Y } : null, To = new { sector.X, sector.Y }, Actor = at, Goal = goal, Resumed = resumed,
            Toward = _mapFarGoal?.Toward, LoadMs = loaded,
            Moves = _mapWindowMoves, Sectors = area.Snapshot.Sectors.Count(s => s.Present), Visuals = area.Snapshot.Visuals.Count, area.CpuRgbaBytes,
            Session = _areaSession?.Version.Revision }));
        RefreshAreaStatus();
    }

    private void StreamFailed(AreaSectorCoordinate sector, string frontier, string what)
    {
        _mapStreamFailedAt = sector;
        _mapStreamStatus = $"window around ({sector.X},{sector.Y}) {what} ({frontier}); the shown window stays";
        GD.Print("ARC-RENDER-024 stream failed " + frontier);
        RefreshAreaStatus();
        if (_nativeSmoke) { GD.Print("ARC-RENDER-007 frontier=AREA_STREAM_" + frontier.Replace('/', '_').ToUpperInvariant()); GetTree().Quit(1); }
    }

    private static string Frontier(Exception exception) => exception is AreaException refused ? $"{refused.Issue.Stage}/{refused.Issue.Code}"
        : exception.Message.StartsWith("AREA_", StringComparison.Ordinal) && exception.Message.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? exception.Message
        : exception.GetType().Name;

    /// <summary>ARC-RENDER-1355: a failed publication's log line: the frontier, the exception's type and message, and its first stack
    /// frames (a bare type name hid which lookup failed).</summary>
    internal static string FailureDetail(Exception exception, int frames = 6)
    {
        var top = (exception.StackTrace ?? string.Empty).Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Take(frames);
        return Frontier(exception) + " | " + exception.GetType().Name + ": " + exception.Message.ReplaceLineEndings(" ") + " | at " + string.Join(" <- ", top.Select(f => f.StartsWith("at ", StringComparison.Ordinal) ? f[3..] : f));
    }

    private static AreaStepCell? EnvCell(string name)
    {
        var parts = (System.Environment.GetEnvironmentVariable(name) ?? string.Empty).Split(',', StringSplitOptions.TrimEntries);
        return parts.Length == 2 && long.TryParse(parts[0], out var x) && long.TryParse(parts[1], out var y) ? new AreaStepCell(x, y) : null;
    }

    private const int StreamSmokeMoves = 3;
    // Every window publication of this run (smoke report): the centres, the actor's cell, the walk and the session.
    private readonly List<(AreaSectorCoordinate From, AreaSectorCoordinate To, AreaStepCell Actor, bool Walking, GameSessionOwner? Session, long LoadMs)> _mapStreamLog = [];

    // "walk" (ARC-WORLD-042) and "census" (ARC-RENDER-027) run only when named: they walk across the main map or open every
    // map, for minutes.
    private static readonly string[] MapSmokeStageNames = ["map", "resources", "transfer", "roundtrip", "stream", "worldmap", "walk", "census", "cast", "turn-based", "knockdown", "ranged", "container", "junk", "barter", "menu", "scene", "options", "plants", "bar", "ai", "ai-turn-based", "ai-cast", "ai-follower", "premade", "opening", "life", "floaters", "death", "pickpocket", "loot-worn", "loot-room", "inv-arrange", "wreck", "altar", "debug-travel", "report", "hover", "door-close", "dead-window", "inv-ui", "follower-home", "follower-far", "teleport-follow", "npc-resident", "saved-runs", "npc-door", "npc-tile", "tile-ghost", "container-click", "fire-order", "indoor-fade", "route-barrel", "critter-fall", "save-resume", "cast-multi", "grenade", "companion-click", "npc-shot", "ai-react", "please-move", "placement-census", "inv-carry", "spell-end", "hold-hero", "spell-effects", "movement", "spirit-talk", "sleep", "bed", "created-scenery", "created-critter", "script-critters", "worn-light", "critter-light", "eye-light", "summon-flash", "summon-wall", "summon-undead", "tech-craft", "tech-products", "item-aim", "disarm", "creature-item", "trait-body", "movies", "body-styles", "perf", "perf-menu"];

    /// <summary>The stages <c>ARC_AREA_MAP_SMOKE_ONLY</c> names (null: all); an unknown name is refused, so a typo cannot skip
    /// every stage and still pass.</summary>
    private static HashSet<string>? MapSmokeStages()
    {
        if (System.Environment.GetEnvironmentVariable("ARC_AREA_MAP_SMOKE_ONLY") is not { Length: > 0 } list) return null;
        var stages = list.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        return stages.Count > 0 && stages.All(MapSmokeStageNames.Contains) ? stages : throw new InvalidOperationException("AREA_MAP_SMOKE_STAGES_UNKNOWN");
    }

    // Native observation (ARC-RENDER-024, a window map): walks, by the production route follower, to a cell deep in a core
    // sector the actor has not stood in yet, until the window has moved at least three times. Each move loads in the
    // background while the frames keep walking and replaces the shown window with the session carried, the actor on the
    // cell it reached (inside the new window's core) and the walk's goal kept; at least one move happens mid-walk. Every
    // walk reaches its goal, and every published window draws.
    private async Task RunAreaMapStreamSmokeAsync()
    {
        static void Require(bool value, string code)
        {
            if (value) return;
            // The frontier line takes a bare code; the detail after the first space goes to the log first.
            var space = code.IndexOf(' ');
            if (space > 0) GD.Print("ARC-RENDER-024 smoke detail " + code);
            throw new InvalidOperationException("AREA_STREAM_SMOKE_" + (space > 0 ? code[..space] : code));
        }
        if (_mapWindowCentre is null) { GD.Print($"ARC-RENDER-024 native-smoke stream NONE (whole map {_readyMapName})"); return; }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        var observeAi = false;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest(); _mapStreamSmokeActive = true;
            var carried = _areaSession;
            // ARC-RENDER-1355 (opt-in ARC_AREA_STREAM_JOIN=script): the speaker of that script joins the party as the opening's Virgil (1324) does, while a conversation with it is open.
            if (int.TryParse(System.Environment.GetEnvironmentVariable("ARC_AREA_STREAM_JOIN"), out var joinScript) && carried is { } party)
            {
                var template = _companionTemplates.FirstOrDefault(t => t.Speaker.ScriptId == joinScript && t.Home.RuntimeMap == CurrentAreaMap);
                Require(template is not null && _conversationHost is { PartyWired: true }, $"JOIN_NO_TEMPLATE script={joinScript} templates={_companionTemplates.Count}");
                BeginConversation(template!.Speaker);
                AreaSmokeFramesUntilConversation(); Require(ConversationOpen, "JOIN_NO_CONVERSATION " + _conversationNotice);
                Require(party.ForceJoinParty(party.Version, template.Entity).Disposition != GameSessionDisposition.Rejected, "JOIN_REJECTED");
                for (var close = 0; close < 4 && ConversationOpen; close++) _conversationPanel!.LeaveForSelfTest.EmitSignal(BaseButton.SignalName.Pressed);
                Require(!ConversationOpen && _areaCompanions?.Companions.SingleOrDefault(c => c.Entity == template.Entity) is { Standing: CompanionStanding.Following }, "JOIN_NOT_FOLLOWING");
            }
            // ARC-RENDER-1355 (opt-in ARC_AREA_STREAM_LANDING=x,y): the walk starts from a world-map landing on that cell, as a report's did.
            if (EnvCell("ARC_AREA_STREAM_LANDING") is { } landing)
            {
                var before = _readyArea;
                // ARC-RENDER-1355 (opt-in ARC_AREA_STREAM_HOURS=n): the journey's game time, an hour per sector step, passes before the landing (a report's was 42 hours).
                if (long.TryParse(System.Environment.GetEnvironmentVariable("ARC_AREA_STREAM_HOURS"), out var hours) && hours > 0)
                    JumpWorldMapClock(hours * Arcanum.Simulation.Travel.WorldJourney.MsPerSector);
                for (var i = 0; i < 1200 && _worldMapData is null; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); AdoptWorldMapData(); }
                Require(_worldMapData is { Status: AreaWorldMapDataStatus.Ready, Data: not null }, "LANDING_NO_WORLD_MAP_DATA");
                LandWorldMap(new((int)landing.X, (int)landing.Y), "stream smoke landing");
                // The frames are the engine's own while the scene is gone (no smoke frame observes a session that is not there).
                for (var i = 0; i < 4000 && (_readyArea is null || ReferenceEquals(_readyArea, before) || MapStreamInFlight || _worldActor.Session is not AreaGridSession); i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Require(!ReferenceEquals(_readyArea, before) && _mapWindowCentre is not null, "LANDING_NOT_LOADED");
            }
            // ARC-DEV-119 (MIG9 S5c, opt-in: ARC_AREA_STREAM_AI=1): the AI runs through the window moves, and the one host built for the map must still be the shown map's
            // after them, rebound once per move. The NPCs start calm (the main map's residency wakes those near the walk).
            // ARC-RENDER-1355: ARC_AREA_STREAM_AI=live runs the AI as in play (the NPCs not calmed, so they walk and are drawn as AI movers across a window move).
            var aiMode = System.Environment.GetEnvironmentVariable("ARC_AREA_STREAM_AI"); var aiLive = aiMode == "live";
            observeAi = (aiMode == "1" || aiLive) && carried is not null && !_aiForSmoke;
            AreaAiHost? aiHost = null; int aiBuilds = 0, aiRebinds = 0;
            var visited = new HashSet<AreaSectorCoordinate> { _mapWindowCentre!.Value };
            var walks = new List<object>();
            {
                var start = (AreaGridSession)_worldActor.Session!;
                // ARC-RENDER-1355 (opt-in ARC_AREA_STREAM_START=x,y): the first walk starts on that world cell (a report's walk).
                var begin = EnvCell("ARC_AREA_STREAM_START") is { } placed ? start.Chart.ToChart(placed) : start.State.CommittedCell;
                Require(EnvCell("ARC_AREA_STREAM_START") is null || start.Chart.Contains(begin), "START_OUTSIDE_WINDOW");
                AreaSmokePlace(new AreaMapRuntime(_readyArea!.Snapshot, start.Snapshot, start.Chart), begin);
            }
            _mapStreamLog.Clear();
            var releasesAtStart = _cursorReleases; var reappliesAtStart = CursorReappliesForSelfTest;
            if (observeAi)
            {
                if (!aiLive) CalmNpcsForSmoke(carried!, carried!.QueryMapInventory(new(CurrentAreaMap))?.Holders.Where(h => h.Kind == SessionHolderKind.Npc).Select(h => h.Entity) ?? []);
                _aiForSmoke = true; _aiHostBuilds = _aiHostRebinds = 0;
                AreaSmokeFrame();
                aiHost = AreaAiHostNow();
                Require(aiHost is not null && _aiHostBuilds == 1, $"AI_HOST_NOT_BUILT builds={_aiHostBuilds} hero={carried!.QueryHero() is not null} combat={_combatHost is not null} inventory={_inventoryHost is not null} grid={_worldActor.Session is AreaGridSession}");
            }
            // ARC-RENDER-1355 (opt-in ARC_AREA_STREAM_SETTLE=frames, with the live AI): the NPCs walk about before the first walk starts, as they do in play.
            if (int.TryParse(System.Environment.GetEnvironmentVariable("ARC_AREA_STREAM_SETTLE"), out var settle))
                for (var i = 0; i < settle; i++) { AreaSmokeFrame(); if (i % 4 == 0) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
            for (var walk = 0; walk < 6 && _mapStreamLog.Count < StreamSmokeMoves; walk++)
            {
                var session = (AreaGridSession)_worldActor.Session!; var chart = session.Chart; var centre = _mapWindowCentre!.Value;
                var routes = new AreaRouteSource(session.Snapshot, chart);
                // The nearest reachable cell deep in another core sector, preferably one the actor has not stood in (the
                // ground around the Crash Site is enclosed), as deep as the window allows (40, else 24, else 8 cells past the
                // margin), so the walk goes on while the window loads where it can.
                var origin = session.State.CommittedCell;
                var seen = new Dictionary<CellCoord, CellCoord> { [origin] = origin }; var queue = new Queue<CellCoord>([origin]);
                var best = new Dictionary<(bool Visited, int Depth), CellCoord>();
                while (queue.TryDequeue(out var cell))
                {
                    var world = chart.ToWorld(cell);
                    var sector = new AreaSectorCoordinate(world.X >> 6, world.Y >> 6);
                    if (sector != centre)
                        foreach (var depth in new[] { 40, 24, 8 })
                            if (Beyond(centre, world) >= StreamMargin + depth) best.TryAdd((visited.Contains(sector), depth), cell);
                    foreach (var direction in Enum.GetValues<NeighborDirection>())
                    {
                        var next = RoutePlanner.Neighbor(cell, direction);
                        if (seen.ContainsKey(next) || !routes.Contains(next) || routes.Assess(cell, direction) != RouteStepVerdict.Open) continue;
                        seen.Add(next, cell); queue.Enqueue(next);
                    }
                }
                // ARC-RENDER-1355 (opt-in ARC_AREA_STREAM_GOAL=x,y): the first walk goes to that world cell when the window reaches it.
                CellCoord? goal = walk == 0 && EnvCell("ARC_AREA_STREAM_GOAL") is { } named && chart.Contains(chart.ToChart(named)) && seen.ContainsKey(chart.ToChart(named))
                    ? chart.ToChart(named) : new[] { false, true }.SelectMany(v => new[] { 40, 24, 8 }.Select(d => (v, d)))
                    .Where(best.ContainsKey).Select(k => (CellCoord?)best[k]).FirstOrDefault();
                Require(goal is not null, $"NO_NEW_SECTOR from {chart.ToWorld(origin)} reached {seen.Count}");
                var goalWorld = chart.ToWorld(goal!.Value);
                // The path in world cells; the route follower reaches 32 cells per axis, so the walk goes in legs along it.
                var path = new List<AreaStepCell>();
                for (var cell = goal.Value; ; cell = seen[cell]) { path.Add(chart.ToWorld(cell)); if (cell == origin) break; }
                path.Reverse();
                var leg = 0;
                // Each frame: a new leg once the follower stopped short of the goal (on whichever window is shown).
                void Steer()
                {
                    if (_worldActor.Session is not AreaGridSession walking || WorldRoute(walking) is not { } follower) return;
                    var here = walking.Chart.ToWorld(walking.State.CommittedCell);
                    if (here == goalWorld || follower.Status == RouteFollowStatus.Following) return;
                    var index = path.IndexOf(here); if (index < 0) index = leg;
                    var next = index;
                    while (next + 1 < path.Count && Math.Abs(path[next + 1].X - here.X) <= 28 && Math.Abs(path[next + 1].Y - here.Y) <= 28) next++;
                    if (next == index) return;
                    leg = next;
                    follower.SetTarget(walking.Chart.ToChart(path[next]), WorldTime);
                }
                var logged = _mapStreamLog.Count;
                Steer();
                Require(WorldRoute(session)?.Status == RouteFollowStatus.Following, "ROUTE_NOT_SET");
                var frames = 0;
                while (frames < 8000)
                {
                    // During the first window move no frame advances, so the swap meets the actor walking with its goal ahead
                    // (deterministically); during later moves one smoke frame (about 0.1 s of walk) per 0.1 s of wall time, so the
                    // actor walks at its own speed while the window loads, as in play.
                    if (MapStreamInFlight && _mapStreamLog.Count == 0) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); continue; }
                    AreaSmokeFrame(); Steer(); frames++;
                    if (MapStreamInFlight) await ToSignal(GetTree().CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);
                    else if (frames % 2 == 0) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    Require(_readyMapName is not null && _mapStreamFailedAt is null, "WINDOW_NOT_LOADED " + _mapStreamStatus);
                    var now = (AreaGridSession)_worldActor.Session!;
                    if (!MapStreamInFlight && now.State.Phase == MovementPhase.Settled && now.Chart.ToWorld(now.State.CommittedCell) == goalWorld) break;
                }
                var at = ((AreaGridSession)_worldActor.Session!).Chart.ToWorld(((AreaGridSession)_worldActor.Session!).State.CommittedCell);
                Require(at == goalWorld && !MapStreamInFlight, $"GOAL_NOT_REACHED frames={frames} at={at} goal={goalWorld} route={_worldRouteDiagnostic} status={_mapStreamStatus}");
                Require(_mapStreamLog.Count > logged, $"NOT_MOVED goal={goalWorld} status={_mapStreamStatus}");
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                Require(_canvas!.AreaVisibleCount > 0, "NOTHING_DRAWN");
                visited.Add(new(goalWorld.X >> 6, goalWorld.Y >> 6)); visited.Add(_mapWindowCentre!.Value);
                walks.Add(new { Goal = goalWorld, Frames = frames, Moves = _mapStreamLog.Count - logged, Visible = _canvas.AreaVisibleCount, Managed = GC.GetTotalMemory(true) });
            }
            // Every move: a new centre, the actor inside its core, the same session at the actor's cell.
            foreach (var (from, to, actor, _, session, _) in _mapStreamLog)
            {
                Require(to != from, "CENTRE");
                Require(Math.Abs(to.X - (actor.X >> 6)) <= 1 && Math.Abs(to.Y - (actor.Y >> 6)) <= 1, "ACTOR_OUTSIDE_CORE");
                Require(carried is null || ReferenceEquals(session, carried), "SESSION_NOT_CARRIED");
            }
            Require(_mapStreamLog.Count >= StreamSmokeMoves, "MOVES");
            GD.Print($"ARC-UI-056 stream cursor releases={_cursorReleases - releasesAtStart} reapplies={CursorReappliesForSelfTest - reappliesAtStart} shown={_cursorShown is not null}");
            // ARC-UI-056 (#1357): the cursor art stays set across every window move (never released), and is handed over again after each swap.
            if (CursorContent() is { Ready: true })
            {
                Require(_cursorReleases == releasesAtStart, $"CURSOR_RELEASED {_cursorReleases - releasesAtStart}");
                Require(_cursorShown is not null && CursorReappliesForSelfTest - reappliesAtStart >= _mapStreamLog.Count, $"CURSOR_NOT_REAPPLIED {CursorReappliesForSelfTest - reappliesAtStart}/{_mapStreamLog.Count}");
            }
            Require(_mapStreamLog[0].Walking, "FIRST_MOVE_NOT_MID_WALK");
            Require(carried is null || ReferenceEquals(_areaSession, carried), "SESSION_NOT_CARRIED");
            if (observeAi)
            {
                aiBuilds = _aiHostBuilds; aiRebinds = _aiHostRebinds;
                Require(ReferenceEquals(_aiHost, aiHost) && aiBuilds == 1 && aiRebinds == _mapStreamLog.Count,
                    $"AI_HOST_NOT_KEPT same={ReferenceEquals(_aiHost, aiHost)} builds={aiBuilds} rebinds={aiRebinds} moves={_mapStreamLog.Count}");
            }
            GD.Print("ARC-RENDER-024 native-smoke stream PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = _readyMapName,
                Moves = _mapStreamLog.Select(m => new { From = new { m.From.X, m.From.Y }, To = new { m.To.X, m.To.Y }, m.Actor, m.Walking, m.LoadMs }).ToArray(),
                Walks = walks, Session = _areaSession?.Version.Revision, Carried = carried is not null, ManagedBytes = GC.GetTotalMemory(true),
                Ai = observeAi ? new { Builds = aiBuilds, Rebinds = aiRebinds, Npcs = aiHost!.Scene.Npcs.Count } : null }));
        }
        finally
        {
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime); _mapStreamSmokeActive = false;
            if (observeAi) { _aiForSmoke = false; RetireAiHost(); _aiHostStamp = null; }
        }
    }

    // Native observation (ARC-RENDER-024, a window map, after the transfer smoke): from the window, use an exit object to
    // another map (walking next to it first), then on that map use an exit back to this one; the window opens around the
    // authored arrival with the session carried and the actor on the arrival.
    private async Task RunAreaMapRoundTripSmokeAsync()
    {
        static void Require(bool value, string code) { if (!value) { GD.Print("ARC-RENDER-024 smoke detail round trip " + code); throw new InvalidOperationException("AREA_ROUND_TRIP_" + code.Split(' ')[0]); } }
        if (_mapWindowCentre is null) { GD.Print($"ARC-RENDER-024 native-smoke round-trip NONE (whole map {_readyMapName})"); return; }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            var home = _readyMapName!; var homeRuntime = CurrentAreaMap; var session = _areaSession;
            // ARC-RENDER-036: a window without a usable exit to another map (the generated wilderness) has no round trip.
            if (await UseMapExitAsync(site => site.DestinationRuntime != homeRuntime) is not { } there)
            { GD.Print($"ARC-RENDER-024 native-smoke round-trip NONE map={home} objects={_mapTransfers?.Triggers.Count ?? -1} (no usable exit to another map in the window)"); return; }
            Require(_mapWindowCentre is null && SameMapName(_readyMapName, there.To), $"NOT_ARRIVED {_readyMapName}");
            var back = await UseMapExitAsync(site => site.DestinationRuntime == homeRuntime) ?? throw new InvalidOperationException("AREA_ROUND_TRIP_NO_EXIT_BACK");
            Require(SameMapName(_readyMapName, home) && _mapWindowCentre is { } centre && (back.Arrival.X >> 6, back.Arrival.Y >> 6) == (centre.X, centre.Y), $"NOT_BACK {_readyMapName} {_mapWindowCentre}");
            Require(session is null || ReferenceEquals(_areaSession, session), "SESSION_NOT_CARRIED");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Require(_canvas!.AreaVisibleCount > 0, "NOTHING_DRAWN");
            GD.Print("ARC-RENDER-024 native-smoke round-trip PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Home = home, Out = there, Back = back, Window = new { _mapWindowCentre!.Value.X, _mapWindowCentre.Value.Y }, Session = _areaSession?.Version.Revision,
                Carried = session is not null }));
        }
        finally
        {
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
        }
    }

    /// <summary>Stands the actor next to the first usable exit object whose site matches, clicks its sprite through the
    /// production ground-click seam and waits for the destination to publish; null when the shown map has no such exit.</summary>
    private async Task<RoundTripLeg?> UseMapExitAsync(Func<Arcanum.Data.Maps.MapTransitionSite, bool> wanted)
    {
        if (_mapTransfers is not { } transfers) return null;
        var exit = transfers.Triggers.Where(k => transfers.Site(k) is { } site && wanted(site) && transfers.Refusal(site) is null)
            .Select(k => (Key: k, Cell: UsableCell(transfers.Map, k))).FirstOrDefault(p => p.Cell is not null);
        if (exit.Cell is not { } near) return null;
        var site = transfers.Site(exit.Key)!;
        AreaSmokePlace(transfers.Map, near);
        var command = _readyArea!.Plan.Commands.Single(c => c.Identity == exit.Key);
        AreaSmokeClick(await AreaSmokeTriggerPointAsync(command));
        for (var wait = 0; wait < 40 && transfers.Pending is null; wait++) AreaSmokeFrame(); // ARC-DEV-174: the click starts the use goal; its last node asks for the use within a few table steps
        if (transfers.Pending is not { } pending) throw new InvalidOperationException("AREA_ROUND_TRIP_USE_NOT_ACCEPTED");
        AreaSmokeFrame();
        await (_lastLoad ?? Task.CompletedTask);
        var arrived = (AreaGridSession)_worldActor.Session!;
        var at = arrived.Chart.ToWorld(arrived.State.CommittedCell);
        if (!SameMapName(_readyMapName, pending.To.Name) || at != pending.Arrival) throw new InvalidOperationException($"AREA_ROUND_TRIP_ARRIVAL_{SameMapName(_readyMapName, pending.To.Name)}");
        return new(pending.To.Name, site.Script, pending.Arrival);
    }

    private sealed record RoundTripLeg(string To, int Script, AreaStepCell Arrival);

    private string MapStreamStatus() => _mapWindowCentre is { } centre
        ? $" | window centre ({centre.X},{centre.Y}), moves {_mapWindowMoves}, zoom floor {FormatZoom(_camera?.ZoomFloor(WorldViewport) ?? CameraPolicy.MinimumZoom)}{(_mapStreamStatus is { } s ? ": " + s : string.Empty)}" : string.Empty;
}
