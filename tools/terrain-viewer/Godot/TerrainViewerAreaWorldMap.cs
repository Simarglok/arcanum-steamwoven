using Arcanum.Data.Area;
using Arcanum.Input;
using Arcanum.Session;
using Arcanum.Simulation.ChangeOfPlace;
using Arcanum.Simulation.Travel;
using Arcanum.TerrainViewer.Core;
using Arcanum.Simulation.Movement;
using Arcanum.World;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-048: the world map in map mode on the start map (runtime map 1, the map the world map is keyed to). The original key W
// (logical key down) opens the diagnostic panel under the in-game key handler's guards like C, L and T; it is modal (gate
// closed and a walk stopped; the clock runs behind it, ARC-DEV-126). Waypoints follow the rules of ARC-WORLD-056 (ledger WT5–WT8) with the
// session's rule setting; Travel runs one travel step per 62 real ms (WT10) as the session's real-time events (MIG12 S8: the first
// 50 ms after the start, each next 62 ms after it, run by the frame step even behind the pause): the step's game time jumps the
// session clock, a discovered area becomes known. The journey ends by its landing (WT12), or by W or a controller cancel
// (Escape opens the in-play menu over the map instead, ARC-DEV-111, and the steps go on behind its pause), which lands where it stands; the panel closes and the start map loads around the landing as a transfer does, the session and
// following companions carried. The panel refuses on another map, in combat mode, with a transfer pending and without the data.
// When the landing load fails, the start map reloads where the PC stood, with the journey's hours and discoveries kept. Not
// modelled: travel music, hiding the party, encounters, town maps (whose sectors the original keeps off the walkable world map).
// ARC-DEV-050: in debug mode (ViewerDebugMode) W opens on any map; the panel lists every area and Teleport lands the PC at the
// chosen area's location on the start map at once (a load as the journey's landing, from another map a transfer to the start
// map), with no game time and no discovered area. Without the debug mode there is no area list and no teleport.
public partial class TerrainViewerApp
{
    private AreaWorldMapPanel? _worldMapPanel;
    private AreaWorldMap? _worldMap;
    private string? _worldMapStatus;
    private string? _worldMapMessage;
    private Task<AreaWorldMapDataLoad>? _worldMapDataTask;
    private string? _worldMapDataModule;
    private AreaWorldMapDataLoad? _worldMapData;
    // ARC-UI-073: the original look's art and tiles, read once a module beside the data (null without a game folder or when they did not read).
    private Task<AreaWorldMapArtContent>? _worldMapArtTask;
    private AreaWorldMapArtLook? _worldMapArtLook;
    private bool _worldMapArtAdopted;
    // ARC-UI-084: the town-map files, opened beside the data for the same module (null without a game folder or when they did not open).
    private Task<AreaTownMapSource?>? _townMapTask;
    private AreaTownMapSource? _townMapSource;
    // The smoke's journey part needs the walkable world, which a PC in a town-map sector does not have: after its town checks it sets this (smoke only).
    private bool _worldMapTownGateOffForSmoke;
    private bool WorldMapOpen => WindowOpen(InterfaceMode.WorldMap);
    private bool WorldMapActive => _areaSession is not null && _readyMode == TerrainViewerMode.Area && _readyMapName is not null;

    /// <summary>Starts loading the world-map data for this module once; a reload or a map transfer reuses them.</summary>
    private void BindAreaWorldMap(TerrainViewerArguments arguments)
    {
        if (arguments.AreaMapName is null || _worldMapDataModule == arguments.ModulePath) return;
        _worldMapDataModule = arguments.ModulePath; _worldMapData = null;
        var module = arguments.ModulePath;
        _worldMapDataTask = Task.Run(() => AreaWorldMap.LoadData(module));
        _worldMapArtTask = SelfTestBootstrap || module is null ? null : Task.Run(() => AreaWorldMapArtContent.Load(module));
        _worldMapArtLook = null; _worldMapArtAdopted = false;
        // A source the background open finished but nothing adopted is dropped with its stack otherwise.
        _townMapSource ??= _townMapTask is { IsCompletedSuccessfully: true } done ? done.Result : null;
        _townMapSource?.Dispose(); _townMapSource = null;
        _townMapTask = SelfTestBootstrap || module is null ? null : Task.Run(() =>
        {
            var source = AreaTownMapSource.Open(module, out var detail);
            GD.Print($"ARC-UI-084 town maps {(source is null ? "not read: " : "read: ")}{detail}");
            return source;
        });
    }

    /// <summary>The town-map files once their background open is done (null before, and when they did not open).</summary>
    private AreaTownMapSource? AdoptTownMapSource()
    {
        if (_townMapSource is null && _townMapTask is { IsCompletedSuccessfully: true } task) _townMapSource = task.Result;
        return _townMapSource;
    }

    /// <summary>Builds the window's textures once the background read is done; the window keeps the diagnostic look until then and when the art did not read.</summary>
    private AreaWorldMapArtLook? AdoptWorldMapArt()
    {
        if (_worldMapArtAdopted || _worldMapArtTask is not { IsCompleted: true } task) return _worldMapArtLook;
        _worldMapArtAdopted = true;
        if (!task.IsCompletedSuccessfully) { GD.Print("ARC-UI-073 world-map art load threw " + task.Exception?.GetBaseException().GetType().Name); return null; }
        var content = task.Result;
        if (!content.Ready) { GD.Print($"ARC-UI-073 world-map art not ready: {content.Detail}"); return null; }
        // The font's glyph frames stay as they are for the art text draw, not as textures.
        var art = content.Art.Values.Where(load => load.Status == AreaInterfaceArtStatus.Ready && load.Frames.Count > 0 && load.Number != AreaWorldMapArtLayout.FontArt)
            .ToDictionary(load => load.Number, load => load.Frames.Select(frame => (Texture2D)InterfaceHd.Texture(frame, premultipliedAlpha: load.Number != AreaWorldMapArtLayout.BackgroundArt)).ToArray());
        var tiles = new List<Texture2D>();
        foreach (var bytes in content.Tiles)
        {
            var image = new Image();
            if (image.LoadBmpFromBuffer(bytes!) != Error.Ok) { GD.Print("ARC-UI-073 world-map tile did not decode"); return null; }
            image.Convert(Image.Format.Rgba8);
            tiles.Add(ImageTexture.CreateFromImage(image));
        }
        // The continent picture is optional (WM62): without it the window refuses only its view.
        ImageTexture? zoomedTexture = null;
        if (content.Zoomed is { } zoomedBytes)
        {
            var zoomed = new Image();
            if (zoomed.LoadBmpFromBuffer(zoomedBytes) == Error.Ok) { zoomed.Convert(Image.Format.Rgba8); zoomedTexture = ImageTexture.CreateFromImage(zoomed); }
            else GD.Print("ARC-UI-083 world-map continent picture did not decode");
        }
        _worldMapArtLook = new(art, tiles, zoomedTexture, new AreaArtFontFace(content.Art[AreaWorldMapArtLayout.FontArt].Frames), content);
        GD.Print($"ARC-UI-073 world-map art ready {content.Detail}");
        GD.Print("ARC-UI-105 world-map marker art sizes " + string.Join(" ", new[] { 204, 205, 206, 143, 814 }.Where(art.ContainsKey).Select(n => $"{n}={art[n][0].GetWidth()}x{art[n][0].GetHeight()}x{art[n].Length}")));
        return _worldMapArtLook;
    }

    private void AdoptWorldMapData()
    {
        if (_worldMapData is not null || _worldMapDataTask is not { IsCompleted: true } task) return;
        _worldMapData = task.IsCompletedSuccessfully ? task.Result
            : new(AreaWorldMapDataStatus.StackFailed, null, "load threw " + (task.Exception?.GetBaseException().GetType().Name ?? "cancelled"));
        _worldMapDataTask = null;
        GD.Print($"ARC-UI-048 world-map data status={_worldMapData.Status} detail={_worldMapData.Detail}");
    }

    private void OpenWorldMap()
    {
        if (!WorldMapActive || !WindowCanOpen(InterfaceMode.WorldMap)) return;
        // ARC-WORLD-1159 (0x560720): the opener is refused (message 602) for 4,000 real milliseconds after an encounter.
        if (_worldMapLockedUntilHostMs > _clockHostMs)
        {
            _worldMapStatus = "the world map cannot be opened right now (message 602)";
            GD.Print("ARC-WORLD-1159 world map refused after an encounter");
            RefreshAreaStatus();
            return;
        }
        AdoptWorldMapData();
        // ARC-UI-084 (WM5, WM65): a PC in a sector with a town map is not on the walkable world and the original look opens on the town map, on any map.
        var art = AdoptWorldMapArt();
        var artLook = DebugMode ? null : art;
        // ARC-MAGIC-038: the teleport mode picks areas of the world map wherever the PC is, so the town map is not shown for it and the start-map refusal is off.
        var teleport = _teleportMode is not null;
        var townNumber = artLook is not null && !_worldMapTownGateOffForSmoke && !teleport ? PcTownMapNumber() : 0;
        var townLoading = townNumber > 0 && _townMapSource is null && _townMapTask is { IsCompleted: false };
        var townShow = townLoading ? null : TownMapShowFor(townNumber, PcCell().X, PcCell().Y);
        string? refused = _worldMapData is not { Data: { } data } ? $"world-map data {(_worldMapData is null ? "loading" : _worldMapData.Status.ToString())}"
            : townLoading ? "the town maps are still loading"
            : _readyArea!.Snapshot.RuntimeMap != data.StartRuntime && !DebugMode && !teleport && townShow is null ? $"only on the start map ({data.StartMap})"
            : HeroInCombatMode ? "leave combat mode first"
            : AreaTransferPending ? "a transfer is pending"
            : _worldActor.Session is not AreaGridSession { State.Phase: Arcanum.Simulation.Movement.MovementPhase.Settled } ? "the actor is moving"
            : null;
        if (refused is not null)
        {
            _worldMapStatus = refused;
            GD.Print("ARC-UI-048 world map refused " + refused);
            RefreshAreaStatus();
            return;
        }
        var session = (AreaGridSession)_worldActor.Session!;
        var cell = session.Chart.ToWorld(session.State.CommittedCell);
        // ARC-DEV-050: off the start map (debug mode only) the view has no position and only teleports.
        var onStart = _readyArea!.Snapshot.RuntimeMap == _worldMapData!.Data!.StartRuntime && townNumber == 0;
        // ARC-MAGIC-038: the teleport mode picks areas from any map (0x5618B0), so its view takes a position off the start map too, the location of the map's own area, and the picture's clicks reach the pick.
        TravelLocation? start = onStart ? new(cell.X, cell.Y) : teleport ? AreaSpellTeleport.MapAreaLocation(_worldMapData.Data, _readyArea.Snapshot.RuntimeMap) ?? default(TravelLocation) : null;
        var worldMap = _worldMap = new AreaWorldMap(_worldMapData.Data, _areaSession!, start) { TravelCheck = WorldMapTravelCheck };
        if (_worldMapPanel is null)
        {
            _worldMapPanel = new AreaWorldMapPanel(); AddChild(_worldMapPanel);
            // ARC-MAGIC-038: in the teleport mode a click picks the nearest known area (0x5618B0 case 0) in place of adding a waypoint.
            _worldMapPanel.WaypointRequested += pixel =>
            {
                if (_teleportMode is not null && _worldMapData?.Data is { } d) TeleportPick(WorldTravelGeometry.Location(pixel, d.Width));
                else WorldMapWaypoint(() => _worldMap!.AddWaypoint(pixel), $"pixel ({pixel.X},{pixel.Y})");
            };
            _worldMapPanel.AreaRequested += area =>
            {
                if (_teleportMode is not null) { if (_worldMapData?.Data is { } d && area > 0 && area < d.TravelAreas.Count) TeleportPick(d.TravelAreas[area].Location); }
                else WorldMapWaypoint(() => _worldMap!.AddArea(area), $"area {area}");
            };
            _worldMapPanel.TravelRequested += StartWorldMapTravel;
            _worldMapPanel.ClearRequested += () => { if (_worldMap?.Clear() == true) RenderWorldMap(null); };
            _worldMapPanel.RemoveRequested += WorldMapRemoveWaypoint;
            _worldMapPanel.TownWaypointRefused += refusal => GD.Print($"ARC-UI-092 town waypoint refused {refusal} waypoints={_townWaypoints.Count}");
            _worldMapPanel.TownTravelRequested += StartTownTravel;
            _worldMapPanel.TownNotesChanged += (town, notes) => { if (_areaSession is { } owner) RecordTownNotes(owner, town, notes); };
            _worldMapPanel.WorldNotesChanged += (kind, notes) => { if (_areaSession is { } owner) RecordMapNotes(owner, kind, notes); };
            _worldMapPanel.CloseRequested += CloseWorldMapByPlayer;
            _worldMapPanel.TeleportRequested += TeleportWorldMap;
            _worldMapPanel.SoundRequested += id => PlayInterfaceSound(id, "world map button");
            _worldMapPanel.PollGate = () => !MainMenuOpen;
            // ARC-UI-134 (WM140): the lens shows the game view around the hero (the round view of the inventory, the barter window and the logbook).
            _worldMapPanel.HolderView = InventoryHolderView;
            // ARC-UI-083: a marker's description is written into the message window at once (WM60), a button's hint after the quiet time (WM61).
            _worldMapPanel.DescriptionRequested += text =>
            {
                if (text is null) _messageLog.ClearHover(); else _messageLog.ShowTransient(AreaMessageEntry.Text(AreaMessageKind.Hover, text));
                _gameBarPanel?.MessageLog.Render();
            };
            _worldMapPanel.HintRequested += text =>
            {
                if (text is null) { _messageLog.ClearHover(); _gameBarPanel?.MessageLog.Render(); }
                else PostMessage(AreaMessageEntry.Text(AreaMessageKind.Hover, text), "hover");
            };
        }
        _worldMapPanel.SetArtLook(art);
        StopWorldRoute("ROUTE_STOPPED_BY_WORLD_MAP");
        QuarantineWorld(); _quickKeys.Reset();
        // ARC-UI-134 (WM140): the window build turns the view-window mode on, which centres the game view on the PC (0x551A10) for the lens; the diagnostic panel has no lens.
        if (artLook is not null) CentreGameViewOnHolder();
        var names = _worldMapData.Data!.Areas.Areas;
        if (!OpenWindowMode(InterfaceMode.WorldMap, () => _worldMapPanel!.Open(worldMap, _areaSession!.IsAreaKnown, area => names[area].Name, DebugMode, townShow, MapNotesOf(_areaSession!, SessionStoryLimits.MapNoteView),
            MapNotesOf(_areaSession!, SessionStoryLimits.ContinentNoteView)), () => _worldMapPanel!.CloseView()))
        {
            _worldMap = null;
            return;
        }
        _worldMapStatus = null; _worldMapMessage = null;
        GD.Print($"ARC-UI-048 world map open position=({worldMap.Position.X},{worldMap.Position.Y}) onStartMap={onStart} debugMode={DebugMode} "
                 + $"known={_areaSession!.QueryKnownAreas().Count} rules={_areaSession.Rules.Travel.Name}");
        RefreshReadyStatus();
    }

    // ARC-WORLD-122 (WM78): whenever the PC changes location in a sector with a town map, the tile under the new location is revealed in the session (one tile, no
    // radius). The viewer watches the hero's committed cell once per frame; a cell outside the town's picture reveals nothing.
    private (GameSessionOwner? Session, long X, long Y, int Town) _townRevealLast;

    private void RevealWalkedTownMapCell()
    {
        if (!WorldMapActive || _areaSession is not { } session || _worldActor.Session is not AreaGridSession) return;
        var (x, y) = PcCell();
        var number = PcTownMapNumber();
        if (_townRevealLast == (session, x, y, number)) return;
        // The town files open in the background: until they have, the cell is asked again next frame (a hero standing still must not miss its tile).
        var source = number > 0 ? AdoptTownMapSource() : null;
        if (number > 0 && source is null && _townMapTask is { IsCompleted: false }) return;
        _townRevealLast = (session, x, y, number);
        if (source is null || source.Prepare(number) is not { } plan || AreaTownMapLayout.CellOf(plan.Info, x, y) is not { } cell) return;
        var result = session.RevealTownMapCell(session.Version, number, cell);
        if (result.Disposition == GameSessionDisposition.Changed) GD.Print($"ARC-WORLD-122 town map {number} cell {cell} revealed at ({x},{y})");
        else if (result.Disposition == GameSessionDisposition.Rejected) GD.Print($"ARC-WORLD-122 town map {number} cell {cell} refused: {result.Failure}");
    }

    /// <summary>WM65: the town-map number of the sector the PC stands in, read from the sectors the shown map loaded (0 = none).</summary>
    private (long X, long Y) PcCell()
    {
        if (_worldActor.Session is not AreaGridSession located) return default;
        var at = located.Chart.ToWorld(located.State.CommittedCell);
        return (at.X, at.Y);
    }

    private int PcTownMapNumber()
    {
        if (_readyArea is not { } area || _worldActor.Session is not AreaGridSession located) return 0;
        var at = located.Chart.ToWorld(located.State.CommittedCell);
        return AreaPcTownMap.At(area.Snapshot.Sectors, at.X, at.Y);
    }

    /// <summary>The town map to show for the PC's sector (WM72): its files and plan; null for no town map, or when the load fails (the window then falls back
    /// to the map view, as the original does). The tiles' reveal: a whole map, or the session's reveal of the town (ARC-WORLD-122 adds the walked cells).</summary>
    private AreaTownMapShow? TownMapShowFor(int townNumber, long x, long y)
    {
        if (townNumber <= 0) return null;
        if (AdoptTownMapSource() is not { } source) { GD.Print($"ARC-UI-084 town map {townNumber} not shown: the town-map files are not open"); return null; }
        if (source.Prepare(townNumber) is not { } plan) { GD.Print($"ARC-UI-084 town map {townNumber} not shown: its files did not load"); return null; }
        var owner = _areaSession!;
        GD.Print($"ARC-UI-084 town map {townNumber} \"{plan.Entry.Name}\" whole={plan.Entry.Whole} tiles={plan.Info.Columns}x{plan.Info.Rows} of {plan.TileWidth}x{plan.TileHeight} party=({x},{y}) origin=({plan.Info.OriginX},{plan.Info.OriginY}) pixel={AreaTownMapLayout.ToPixel(plan.Info, x, y)}");
        // The draw asks for the reveal; a session replaced meanwhile reveals nothing (the window closes with it).
        // WM89: the town waypoints outlive the window and are dropped by the next load (here: another town, map or session).
        var key = (owner, _readyArea?.Snapshot.RuntimeMap ?? 0, townNumber);
        if (_townWaypointsKey != key) { _townWaypoints.Clear(); _townWaypointsKey = key; }
        return new(source, plan, (x, y), index => { try { return owner.IsTownMapCellRevealed(townNumber, index); } catch (ObjectDisposedException) { return false; } },
            _townWaypoints, TownPathExists, TownFollowerLocations, TownNotesOf(owner, townNumber));
    }

    // ARC-UI-103/104 (WM90, WM91, WM127): the notes of a town map live in the session (the original keeps them in a file of the save folder, `<town>.tmn`); the window
    // and the hover mark work on a list read from it and hand the list back when a note was added.
    private static AreaTownNotes TownNotesOf(GameSessionOwner session, int town) =>
        session.QueryTownNotes(town) is { } saved
            ? AreaTownNotes.Restore(saved.Notes.Select(n => new AreaTownNote(n.Id, n.Flags, n.PixelX, n.PixelY, n.Type, n.Text)), saved.NextId)
            : new AreaTownNotes();

    private bool RecordTownNotes(GameSessionOwner session, int town, AreaTownNotes notes)
    {
        var result = session.RecordTownNotes(session.Version, town, notes.NextId, [.. notes.Items.Select(n => new SessionTownNote(n.Id, n.Flags, n.PixelX, n.PixelY, n.Type, n.Text))]);
        if (result.Disposition == GameSessionDisposition.Rejected) GD.Print($"ARC-UI-104 town map {town} notes refused: {result.Failure}");
        return result.Disposition != GameSessionDisposition.Rejected;
    }

    // ARC-UI-110 (Steamwoven rules): the notes of the map view and of the continent view live in the session like the towns' (`story.mapNotes`, schema 82); the window works on a
    // list read from it and hands the list back when the quill changed it.
    private static AreaTownNotes MapNotesOf(GameSessionOwner session, int view) =>
        session.QueryMapNotes(view) is { } saved
            ? AreaTownNotes.Restore(saved.Notes.Select(n => new AreaTownNote(n.Id, n.Flags, n.PixelX, n.PixelY, n.Type, n.Text)), saved.NextId)
            : new AreaTownNotes();

    private bool RecordMapNotes(GameSessionOwner session, AreaWorldMapKind kind, AreaTownNotes notes)
    {
        var view = kind == AreaWorldMapKind.Continent ? SessionStoryLimits.ContinentNoteView : SessionStoryLimits.MapNoteView;
        var result = session.RecordMapNotes(session.Version, view, notes.NextId, [.. notes.Items.Select(n => new SessionTownNote(n.Id, n.Flags, n.PixelX, n.PixelY, n.Type, n.Text))]);
        if (result.Disposition == GameSessionDisposition.Rejected) GD.Print($"ARC-UI-110 world map view {view} notes refused: {result.Failure}");
        return result.Disposition != GameSessionDisposition.Rejected;
    }

    private const int SceneryFlagsSlot = 36;

    /// <summary>ARC-UI-103 (WM127, 0x555D80 → 0x564AF0): a hovered scenery with the town-map flag marks the PC's town map with a note carrying its name, when the object lies in
    /// the same town map as the PC; one already near the spot stops it. With no town map there is nothing to mark (the original only logs a warning).</summary>
    private void MarkTownMapByScenery(AreaObjectRecord row, string name)
    {
        var flags = row.EffectiveFields.FirstOrDefault(f => f.Slot == SceneryFlagsSlot && f.Presence == AreaPresence.Value)?.Number is { } value ? unchecked((uint)value) : 0u;
        if ((flags & AreaTownNotes.MarkedByScenery) == 0 || row.WorldX is not { } x || row.WorldY is not { } y) return;
        if (_readyArea is not { } area || _areaSession is not { } session || !WorldMapActive) return;
        var town = PcTownMapNumber();
        if (town <= 0 || AreaPcTownMap.At(area.Snapshot.Sectors, x, y) != town) return;
        if (AdoptTownMapSource() is not { } source || source.Prepare(town) is not { } plan) return;
        var notes = TownNotesOf(session, town);
        if (notes.AddForObject(plan.Info, x, y, name) && RecordTownNotes(session, town, notes)) GD.Print($"ARC-UI-103 town map {town} note \"{name}\" at ({x},{y})");
    }

    // ARC-UI-093 (WM81): the companions the party says follow the PC and stand on this map, at their committed cells now (waiting and free companions are not
    // followers); a companion away on another map has no place here.
    private IReadOnlyList<(long X, long Y)> TownFollowerLocations() =>
        _areaCompanions is { } group && _areaSession is { } session && group.Map.RuntimeMap == CurrentAreaMap
            ? [.. group.Companions.Where(c => session.QueryCompanion(c.Entity).Standing == CompanionStanding.Following).Select(c => group.WorldCell(c.Speaker))]
            : [];

    // ARC-UI-092, WM89, WM107, WM108: Travel in the town map closes the window and walks the PC to the first waypoint like a ground click; the other waypoints are stacked in the same run (ARC-DEV-322).
    private void StartTownTravel()
    {
        if (!WorldMapOpen || _townWaypoints.Count == 0 || _worldActor.Session is not AreaGridSession located) return;
        var tiles = _townWaypoints.Items.Select(w => w.Location).ToList();
        EndWorldMap();
        BeginTownRoute(tiles);
    }

    private void BeginTownRoute(List<(long X, long Y)> tiles)
    {
        // The first walk is a ground click's (0x433640: always-run option XOR Ctrl); a walk already heading there is under way, and a refused one lets the next waypoint
        // start a fresh walk (WM108: the first waypoint is then skipped, which is reproduced).
        var outcome = StartTownWalk(tiles[0]);
        // ARC-DEV-311, ARC-DEV-322 (WM108, WM133; D9, no switch): the other waypoints are goal-3 walks stacked in the same run of the table, in both settings.
        var appended = AppendTownRoute(tiles);
        GD.Print($"ARC-DEV-311 town travel waypoints={tiles.Count} first={tiles[0]} outcome={outcome} run={appended}");
    }

    /// <summary>
    /// ARC-DEV-311 (0x433A00): each further waypoint goes to the bottom of the hero's walk run; when the first waypoint's start was refused (or its walk is over already) the run is missing and the waypoint starts a fresh
    /// walk, so the first one is skipped (WM108). A run of eight goals refuses the rest. Returns what the route holds: the goals in the hero's walk run.
    /// </summary>
    private int AppendTownRoute(IReadOnlyList<(long X, long Y)> tiles)
    {
        if (HeroRunsHost() is not { } host) return 0;
        foreach (var tile in tiles.Skip(1))
        {
            HeroWalkAppendResult result;
            try { result = host.AppendWalk(new CellCoord(checked((int)tile.X), checked((int)tile.Y))); }
            catch (OverflowException) { continue; }
            if (result == HeroWalkAppendResult.NoRun) StartTownWalk(tile);
            else if (result == HeroWalkAppendResult.StackFull) break;
        }
        return host.WalkRun?.Depth ?? 0;
    }

    private HeroRequestOutcome StartTownWalk((long X, long Y) tile)
    {
        if (_worldActor.Session is not AreaGridSession located) return HeroRequestOutcome.Ignored;
        try { return HeroRequests.Apply(new WalkRequest(located.Chart.ToChart(new(tile.X, tile.Y)), Ctrl: Godot.Input.IsKeyPressed(Key.Ctrl))).Outcome; }
        catch (OverflowException) { return HeroRequestOutcome.Ignored; } // a tile far outside the chart: no walk to it
    }

    private readonly AreaTownWaypoints _townWaypoints = new();
    private (GameSessionOwner Session, int Map, int Town) _townWaypointsKey;

    /// <summary>
    /// ARC-UI-092, the town path test (WM105, WM114–WM119, WM125): the PC's own walk from the start tile to the target tile finds a path now. The start and the target must
    /// differ and lie less than 32 tiles apart on both axes (the original's search window is 64 × 64 tiles), and the walk's first plan (the budget of LR2, no corridor, no
    /// straight-line retry) must find the path. Blockers are the route planner's (walls, scenery, creatures it knows, doors); whether the original also refuses a target
    /// holding a creature, container or scenery is read as F1 (WM118). The original's stale search budget (WM115) is a technical flaw not reproduced: each test has its own budget.
    /// </summary>
    private bool TownPathExists((long X, long Y) start, (long X, long Y) target)
    {
        if (start == target || Math.Abs(start.X - target.X) >= 32 || Math.Abs(start.Y - target.Y) >= 32) return false;
        if (_worldActor.Session is not AreaGridSession located || WorldRoute(located) is not { } route) return false;
        try
        {
            var from = located.Chart.ToChart(new(start.X, start.Y));
            var goal = located.Chart.ToChart(new(target.X, target.Y));
            if (!located.Chart.Contains(from) || !located.Chart.Contains(goal)) return false;
            var plan = route.PlanWalk(from, goal, straight: false);
            return plan is { Far: false, Straight: false, Plan.Outcome: Arcanum.Simulation.Navigation.RouteOutcome.Found };
        }
        catch (OverflowException) { return false; }
    }

    private void WorldMapWaypoint(Func<AreaWorldMapRefusal> add, string what)
    {
        if (!WorldMapOpen || _worldMap is null) return;
        var refusal = add();
        var message = refusal switch
        {
            AreaWorldMapRefusal.None => null,
            AreaWorldMapRefusal.NoRoute => "no route there (the original's message 601): add waypoints around mountains or water",
            AreaWorldMapRefusal.TooMany => "at most 30 waypoints",
            AreaWorldMapRefusal.Travelling => "travelling: waypoints are set before the journey",
            AreaWorldMapRefusal.NotOnStartMap => "not on the start map: no journey from here (teleport only)",
            _ => "off the map",
        };
        GD.Print($"ARC-UI-048 waypoint {what} refusal={refusal} waypoints={_worldMap.Waypoints.Count}");
        RenderWorldMap(message);
    }

    // ARC-UI-085, WM98: a right release or Delete removes the selected waypoint, or the last; during a journey the last one goes and the journey is left (the PC lands where it stands).
    private void WorldMapRemoveWaypoint()
    {
        if (!WorldMapOpen || _worldMap is not { } map) return;
        var removal = map.RemoveWaypoint();
        GD.Print($"ARC-UI-085 waypoint removed result={removal.Result} waypoints={map.Waypoints.Count} selected={map.Selected}");
        if (removal.Result == AreaWorldMapRemoved.RemovedAndLeftTravel && removal.Landing is { } landing) { LandWorldMap(landing, "cancelled"); return; }
        RenderWorldMap(removal.Result == AreaWorldMapRemoved.NoRoute ? "waypoint kept: the next waypoint has no route without it (PROJECT: the original keeps the old steps, rule mode Original)" : null);
    }

    private void StartWorldMapTravel()
    {
        if (!WorldMapOpen || _worldMap is null) return;
        // MIG12 S8: the journey is a chain of real-time events of the session, which only a frame step moves (a lit map's, or an unlit map's frame driver, ARC-DEV-124).
        if (!HasFrameSource)
        {
            GD.Print("ARC-UI-048 travel start refused: no game clock on this map");
            RenderWorldMap("no game clock on this map: the journey cannot run");
            return;
        }
        var refusal = _worldMap.Start();
        var (steps, hours) = _worldMap.Estimate();
        GD.Print($"ARC-UI-048 travel start refusal={refusal} steps={steps} hours={hours}");
        // The first step falls due 50 ms later (TL28); each step asks for the next one 62 ms on (RunWorldMapStep).
        if (refusal == AreaWorldMapRefusal.None && _areaSession!.ScheduleRealStep(SessionRealStep.WorldMapStep, WorldJourney.FirstStepRealMs) == GameSessionDisposition.Rejected)
        {
            GD.Print("ARC-UI-048 travel step refused by the session");
            CloseWorldMapByPlayer();
            return;
        }
        RenderWorldMap(refusal switch
        {
            AreaWorldMapRefusal.None => null,
            AreaWorldMapRefusal.NotOnStartMap => "not on the start map: no journey from here (teleport only)",
            AreaWorldMapRefusal.ActionsRefused => "the hero's action cannot be interrupted: try again in a moment",
            _ => "nothing to travel to: add a waypoint",
        });
    }

    /// <summary>ARC-MAGIC-059 (0x565140, ledger WT9, UE6): the travel start check, before the journey and before each later segment. The hero's running
    /// actions are interrupted at level 3 (0x424070: a walk or a swing ends with its end nodes; an attempted cast of priority 4 or 5 refuses), then the spells
    /// the hero maintains end (0x457450, <see cref="SessionSpellCaster.EndMaintainedSpells"/>). False when a run refused: no journey is entered.</summary>
    private bool WorldMapTravelCheck()
    {
        if (HeroRunsHost() is { } runs && !runs.Interrupt(Arcanum.Simulation.Goals.GoalInterruptRules.OrdinaryActions, "TRAVEL_START")) return false;
        SpellCasterNow()?.EndMaintainedSpells(SessionSpellCaster.Hero);
        return true;
    }

    private void RenderWorldMap(string? message)
    {
        _worldMapMessage = message;
        _worldMapPanel?.Render(message);
        RefreshAreaStatus();
    }

    /// <summary>Per frame: a replaced session or another map closes the panel, and a journey that ended lands. MIG12 S8 (ARC-DEV-127): the travel steps
    /// themselves are the session's real-time events (<see cref="RunWorldMapStep"/>, run by the frame step: 50 ms after the start, then every
    /// <see cref="WorldJourney.StepRealMs"/> of real time, behind the Esc menu), not a delta loop of this frame.</summary>
    private void SyncAreaWorldMap()
    {
        RevealWalkedTownMapCell();
        if (!WorldMapOpen) { AdoptWorldMapData(); return; }
        if (!WorldMapActive || _worldMap is null || !ReferenceEquals(_worldMap.Session, _areaSession)) { EndWorldMap(); return; }
        if (_worldMap.Travelling)
        {
            // The chain's safety net: a repeat the session refused (a full queue) must not freeze the journey; it is asked for again here.
            if (_areaSession!.QueryRealStep(SessionRealStep.WorldMapStep) is null)
                _areaSession.ScheduleRealStep(SessionRealStep.WorldMapStep, WorldJourney.StepRealMs);
            return;
        }
        if (_worldMap.Landing is { } landing) LandWorldMap(landing, "arrived");
    }

    /// <summary>The WorldMap event's host (TL28): one travel step with its time jump of game and animation time together (TL26), a discovered area made
    /// known; returns the real time to the next step while the route continues, null when the journey ended (the frame lands it) or is gone.</summary>
    private long? RunWorldMapStep()
    {
        if (!WorldMapOpen || !WorldMapActive || _worldMap is not { Travelling: true } map || !ReferenceEquals(map.Session, _areaSession)) return null;
        var step = map.Step(JumpWorldMapClock);
        if (step.Discovered > 0) GD.Print($"ARC-UI-048 discovered area={step.Discovered} step={map.Steps}");
        if (!map.Travelling) return null;
        _worldMapPanel!.Render(_worldMapMessage);
        return WorldJourney.StepRealMs;
    }

    // The step's game time: a jump of game and animation time (TL26), through the session alone; the events it made due fire in the same frame step
    // (the pass that follows the step), and the lighting follows the hour changed it reports (TickAreaClock).
    private void JumpWorldMapClock(long gameMs)
    {
        var result = _areaSession!.JumpClocks(_areaSession.QueryClockSnapshot().Token, gameMs);
        if (result.Disposition == Arcanum.Simulation.Clock.ClockMutationDisposition.Rejected) GD.Print("ARC-UI-048 clock refused " + result.Failure);
    }

    /// <summary>ARC-DEV-050, debug mode only: Teleport on the selected area lands the PC at its location at once.</summary>
    private void TeleportWorldMap(int area)
    {
        if (!DebugMode || !WorldMapOpen || _worldMap is not { } map) return;
        var refusal = map.Teleport(area, out var landing);
        GD.Print($"ARC-DEV-050 debug teleport area={area} refusal={refusal} landing=({landing.X},{landing.Y}) from map={CurrentAreaMap}");
        if (refusal != AreaWorldMapRefusal.None)
        {
            RenderWorldMap(refusal == AreaWorldMapRefusal.Travelling ? "travelling: teleport before the journey" : $"area {area} has no location on the map");
            return;
        }
        LandWorldMap(landing, $"debug teleport to area {area}", teleport: true);
    }

    /// <summary>The panel closes and the start map loads around the landing location, as a transfer to it does. ARC-DEV-050: a
    /// debug teleport may start on another map (a transfer to the start map), and it opens at the landing's sector without an
    /// arrival tile, so the PC stands on the walkable tile nearest to the sector's middle (an area's location tile may be blocked,
    /// and an arrival is taken whenever its sector is loaded, walkable or not). ARC-DEV-115 (MIG11 S4, WT12, RL24, RL29): the landing is
    /// the session's change of place (caller <see cref="ChangeOfPlaceCaller.WorldMapLanding"/>, blank-screen flag alone), requested for
    /// the start map and pinged in the same frame (<see cref="TerrainViewerApp.RequestWorldMapLanding"/>).</summary>
    private void LandWorldMap(TravelLocation landing, string why, bool teleport = false, ChangeOfPlaceCaller caller = ChangeOfPlaceCaller.WorldMapLanding,
        ChangeOfPlaceFlags flags = AreaChangeOfPlace.LandingFlags, bool? onStartMap = null)
    {
        var steps = _worldMap?.Steps ?? 0; var hours = (_worldMap?.GameMs ?? 0) / WorldJourney.MsPerSector;
        // ARC-MAGIC-038: the area portal lands from outside the window (no world map object): the shown map says whether the PC is on the start map.
        var onStart = onStartMap ?? _worldMap?.OnStartMap != false;
        var startMap = _worldMapData?.Data?.StartMap;
        var startRuntime = _worldMapData?.Data?.StartRuntime;
        EndWorldMap();
        if (_worldActor.Session is not AreaGridSession session || _arguments is null || _readyMapName is null || _areaSession is not { } owner) return;
        var target = onStart ? _readyMapName : startMap;
        if (target is null || startRuntime is not { } startRuntimeMap) return;
        // A refused request (the slot already holds a local-player request: unlikely while the modal map was open) leaves the panel closed and
        // the hero out of combat mode; the old path could not refuse.
        LeaveCombatModeForMapChange();
        var origin = _arguments;
        var next = teleport
            ? origin with { AreaMapName = target, AreaMapArrival = null, AreaMapSector = new Arcanum.Data.Area.AreaSectorCoordinate(landing.X >> 6, landing.Y >> 6) }
            : origin with { AreaMapName = target, AreaMapArrival = new(landing.X, landing.Y), AreaMapSector = null };
        _worldMapStatus = $"{why} after {steps} step(s), {hours} h: landing at ({landing.X},{landing.Y})";
        GD.Print("ARC-UI-048 landing " + System.Text.Json.JsonSerializer.Serialize(new { Why = why, landing.X, landing.Y, Steps = steps, Hours = hours,
            Known = _areaSession?.QueryKnownAreas().ToArray() }));
        RefreshAreaStatus();
        RequestWorldMapLanding(owner, session, origin, next, target, startRuntimeMap, new(checked((int)landing.X), checked((int)landing.Y)), sameMap: onStart, caller, flags);
    }

    // W, a controller cancel or the button (Escape is the menu's key, not this window's): before the journey the panel closes; during it the PC lands where it stands (WT12).
    private void CloseWorldMapByPlayer()
    {
        if (!WorldMapOpen || _worldMap is null) return;
        if (_worldMap.Travelling) { LandWorldMap(_worldMap.Cancel(), "cancelled"); return; }
        EndWorldMap();
    }

    private void EndWorldMap()
    {
        if (_worldMapPanel is null || !WorldMapOpen) { _worldMap = null; return; }
        GD.Print("ARC-UI-048 world map closed");
        // MIG12 S8: a journey that ended any way (landing, cancel, the panel closed) leaves no travel step behind.
        _areaSession?.CancelRealStep(SessionRealStep.WorldMapStep);
        CloseWindowMode(InterfaceMode.WorldMap); _worldMap = null; _teleportMode = null;
        QuarantineWorld(); RefreshReadyStatus();
    }

    /// <summary>The original World map key (W) acts on its logical key down while a map-mode session exists. Returns true when
    /// the key belongs to it, so it is not reported as a reserved, unimplemented original key.</summary>
    private bool RouteWorldMapKey(WorldKeyDecision decision)
    {
        if (!WorldMapActive || decision.Key != WorldKey.W) return false;
        if (!decision.Pressed || decision.Echo) return true;
        if (!_worldFocused || WorldModal || WorldUiFocus) { _worldMapStatus = "W ignored: palette, dialog, window or UI focus owns input"; RefreshAreaStatus(); return true; }
        OpenWorldMap();
        return true;
    }

    /// <summary>While the panel is open it owns every key: W closes it (or ends the journey), Enter travels; Escape is the menu's (it opens over the map).</summary>
    private bool HandleWorldMapKey(InputEventKey key)
    {
        var routed = key.Keycode != Key.None ? key.Keycode : key.PhysicalKeycode;
        // ARC-UI-106: a note's text entry owns the keys but Enter (keeps the note) and Escape (cancels it, so the menu does not open over the entry); typed keys
        // reach the field.
        if (_worldMapPanel!.EditingNote)
        {
            if (key.Pressed && !key.Echo && routed is Key.Enter or Key.KpEnter) _worldMapPanel.AcceptNoteEdit();
            else if (key.Pressed && !key.Echo && routed == Key.Escape) _worldMapPanel.CancelNoteEdit();
            else return false;
            return true;
        }
        if (!key.Pressed || key.Echo) return true;
        if (routed == Key.Q && !key.CtrlPressed && !key.AltPressed && !key.MetaPressed) _worldMapPanel!.ToggleQuill();
        else if (routed == Key.W && !CharacterKeySilenced(key, routed)) CloseWorldMapByPlayer();
        else if (routed is Key.Enter or Key.KpEnter) _worldMapPanel!.Confirm();
        else if (routed == Key.Home) _worldMapPanel!.CentreOnParty();
        else if (routed == Key.Z && !CharacterKeySilenced(key, routed)) _worldMapPanel!.ToggleKind();
        else if (routed == Key.Delete) _worldMapPanel!.RemoveWaypoint();
        return true;
    }

    private void HandleWorldMapPulse(IntentFrame intent)
    {
        if (_worldMapPanel!.EditingNote) return;
        if (intent.Cancel) CloseWorldMapByPlayer();
        else if (intent.Confirm) _worldMapPanel!.Confirm();
    }

    private string AreaWorldMapStatus() => !WorldMapActive ? "" : WorldMapOpen ? " | world map open: W closes"
        : " | W world map" + (DebugMode ? " (DEBUG MODE: teleport)" : "") + (_worldMapStatus is null ? "" : ": " + _worldMapStatus);
}
