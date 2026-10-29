using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Party;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-SCRIPT-046 native observation (map mode on the main map at the Crash Site, the "wreck" stage alone): Virgil joins the party (as
// the follow smoke joins its NPC) and stands next to a plane wreck tile whose script 1695 waits for him, the PC four cells past the
// tile on the same line; following the PC, his first step goes onto the tile, its script runs with him as the triggerer and calls 1696,
// whose dialog opens his conversation with the PC at node 1 of DLG 1696 (no dialog script run); the smoke then closes it.
public partial class TerrainViewerApp
{
    private const int WreckTileScript = 1695, WreckDialogScript = 1696;

    private async Task RunAreaWreckTileSmokeAsync()
    {
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-SCRIPT-046 wreck smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_WRECK_TILE_SMOKE_FAILED");
        }
        Require(_areaSession is not null && _conversationHost is { PartyWired: true } && _readyArea is not null && _worldActor.Session is AreaGridSession
            && _mapTransfers is not null, $"a map-mode session with a party (map {_readyMapName}: {_companionData?.Detail})");
        var session = _areaSession!; var grid = (AreaGridSession)_worldActor.Session!; var area = _readyArea!;
        var map = new AreaMapRuntime(area.Snapshot, grid.Snapshot, grid.Chart);
        var tiles = _mapTransfers!.ScriptTiles;
        var wrecks = tiles.Where(c => _mapTransfers.TileScript(c)?.Script == WreckTileScript).OrderBy(c => c.Y).ThenBy(c => c.X).ToArray();
        Require(wrecks.Length > 0, $"a tile of script {WreckTileScript} is in the window ({tiles.Count} script tiles)");
        var template = _companionTemplates.SingleOrDefault(t => t.Facts.ScriptId == AreaCompanionRoster.Virgil.ScriptId && t.Home.RuntimeMap == CurrentAreaMap);
        Require(template is not null, $"Virgil is a companion template here ({_companionTemplates.Count} templates)");
        // A line through one of its tiles: Virgil one cell before it and the PC four cells past it, so he starts to follow (farther than
        // the start distance 4) and walks the line to within 2 of the PC, his first step onto the tile; every cell walkable, every step open.
        var routes = new AreaRouteSource(map.Steps, map.Chart);
        bool Open(CellCoord from, NeighborDirection direction) => routes.Contains(from)
            && routes.Contains(Arcanum.Simulation.Navigation.RoutePlanner.Neighbor(from, direction))
            && routes.Assess(from, direction) == Arcanum.Simulation.Navigation.RouteStepVerdict.Open;
        (Arcanum.Data.Traversal.AreaStepCell Tile, CellCoord From, CellCoord Pc)? line = null;
        foreach (var tile in wrecks)
        foreach (var direction in Enum.GetValues<NeighborDirection>())
        {
            var at = map.Chart.ToChart(tile);
            var from = Arcanum.Simulation.Navigation.RoutePlanner.Neighbor(at, (NeighborDirection)(((int)direction + 4) % 8));
            var cells = new List<CellCoord> { from };
            for (var step = 0; step < 5; step++) cells.Add(Arcanum.Simulation.Navigation.RoutePlanner.Neighbor(cells[^1], direction));
            if (line is null && cells.Take(5).All(c => Open(c, direction)))
                line = (tile, from, cells[^1]);
        }
        Require(line is not null, $"a walkable line through a wreck tile ({wrecks.Length} tiles: {string.Join(" ", wrecks)})");
        var wreck = line!.Value.Tile;
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            BeginConversation(template!.Speaker);
            Require(ConversationOpen, "a conversation with Virgil opened: " + _conversationNotice);
            var joined = session.ForceJoinParty(session.Version, template.Entity);
            Require(joined.Disposition != GameSessionDisposition.Rejected, "Virgil joined: " + joined.Failure);
            for (var close = 0; close < 4 && ConversationOpen; close++) _conversationPanel!.LeaveForSelfTest.EmitSignal(BaseButton.SignalName.Pressed);
            Require(!ConversationOpen, "the conversation closed");
            AreaSmokePlace(map, line!.Value.Pc);
            var from = map.Chart.ToWorld(line.Value.From);
            var placed = session.ApplyChanges(session.Version, null,
                [AreaCompanionPlacement.Edit(template, new SessionMapId(CurrentAreaMap), new CellCoord(checked((int)from.X), checked((int)from.Y)), NeighborDirection.XPlus)]);
            Require(placed.Disposition != GameSessionDisposition.Rejected, "Virgil placed before the tile: " + placed.Failure);
            BindAreaCompanions();
            var companion = _areaCompanions?.Companions.SingleOrDefault(c => c.Entity == template.Entity);
            Require(companion is { Standing: CompanionStanding.Following } && companion.Grid.State.CommittedCell == line.Value.From,
                $"Virgil follows from {map.Chart.ToWorld(line.Value.From)}: {_areaCompanions?.Status()}");
            var frames = 0;
            for (; frames < 300 && !ConversationOpen; frames++) AreaSmokeFrame();
            Require(ConversationOpen && _conversationScripted, $"his conversation opened in {frames} frames: Virgil at {map.Chart.ToWorld(companion!.Grid.State.CommittedCell)}, notice {_conversationNotice}");
            Require(_voiceStart is { Status: AreaConversationStartStatus.Started, DialogScript: WreckDialogScript } && _conversationSpeaker?.Row.Key == template.Speaker.Row.Key,
                $"the DLG of script {WreckDialogScript} with Virgil: {_voiceStart?.Status} {_voiceStart?.Detail}");
            var node = _conversationPanel!.Conversation?.CurrentNode.Value;
            for (var close = 0; close < 8 && ConversationOpen; close++) _conversationPanel.LeaveForSelfTest.EmitSignal(BaseButton.SignalName.Pressed);
            Require(!ConversationOpen, "his conversation closed");
            GD.Print("ARC-SCRIPT-046 wreck smoke PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Map = _readyMapName, Tile = wreck, Virgil = from, Pc = map.Chart.ToWorld(line.Value.Pc), Frames = frames, Node = node,
                Script = WreckDialogScript, Parity = "NOT_VERIFIED" }));
        }
        finally
        {
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
        }
        await Task.CompletedTask;
    }
}
