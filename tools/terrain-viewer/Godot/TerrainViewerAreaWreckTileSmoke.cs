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
// It is the stage that lets a scripted conversation open in a native smoke; the other stages hold them (ARC-WORLD-118).
// ARC-SCRIPT-052: the "altar" stage (alone, `--sector 1452,1291`: a window with Virgil's home and the altar) does the same on the altar tiles of script 1328,
// whose call-ex (81) runs Virgil's own dialog point from line 14, so his talk opens in the DLG of his dialog script 1324.
public partial class TerrainViewerApp
{
    private const int WreckTileScript = 1695, WreckDialogScript = 1696, AltarTileScript = 1328, AltarDialogScript = 1324;

    private Task RunAreaWreckTileSmokeAsync() => RunAreaVirgilTileSmokeAsync("ARC-SCRIPT-046 wreck smoke PASS", "wreck", WreckTileScript, WreckDialogScript, atDialogPoint: false);

    private Task RunAreaAltarTileSmokeAsync() => RunAreaVirgilTileSmokeAsync("ARC-SCRIPT-052 altar smoke PASS", "altar", AltarTileScript, AltarDialogScript, atDialogPoint: true);

    // passLine: the stage's PASS marker (its FAIL line has FAIL in place of PASS); atDialogPoint: the talk runs at Virgil's dialog point
    // (a call-ex), which resumes his dialog slot, not a scripted conversation of a called script.
    private async Task RunAreaVirgilTileSmokeAsync(string passLine, string place, int tileScript, int dialogScript, bool atDialogPoint)
    {
        void Require(bool value, string why)
        {
            if (value) return;
            GD.Print(passLine.Replace(" PASS", " FAIL", StringComparison.Ordinal) + ": " + why);
            throw new InvalidOperationException("AREA_" + place.ToUpperInvariant() + "_TILE_SMOKE_FAILED");
        }
        Require(_areaSession is not null && _conversationHost is { PartyWired: true } && _readyArea is not null && _worldActor.Session is AreaGridSession
            && _mapTransfers is not null, $"a map-mode session with a party (map {_readyMapName}: {_companionData?.Detail})");
        var session = _areaSession!; var grid = (AreaGridSession)_worldActor.Session!; var area = _readyArea!;
        var map = new AreaMapRuntime(area.Snapshot, grid.Snapshot, grid.Chart);
        var tiles = _mapTransfers!.ScriptTiles;
        var wrecks = tiles.Where(c => _mapTransfers.TileScript(c)?.Script == tileScript).OrderBy(c => c.Y).ThenBy(c => c.X).ToArray();
        // ARC-DEV-089: the stage needs Virgil's home and the script's tile in the window (the Crash Site, or the altar's sector); another
        // window (the town, the wilderness) has neither, which is no failure of the game: the stage reports NONE there.
        var template = _companionTemplates.SingleOrDefault(t => t.Facts.ScriptId == AreaCompanionRoster.Virgil.ScriptId && t.Home.RuntimeMap == CurrentAreaMap);
        if (wrecks.Length == 0 || template is null)
        {
            GD.Print($"{passLine[..passLine.IndexOf(' ', StringComparison.Ordinal)]} native-smoke {place} NONE map={_readyMapName} ({(wrecks.Length == 0 ? $"no tile of script {tileScript} among {tiles.Count} script tiles" : $"Virgil is no companion template here, {_companionTemplates.Count} templates")})");
            return;
        }
        // A line through one of its tiles: Virgil one cell before it and the PC four cells past it, so he starts to follow (farther than
        // the start distance 4) and walks the line to within 2 of the PC, his first step onto the tile; every cell walkable, every step open.
        var routes = new AreaRouteSource(map.Steps, map.Chart);
        bool Open(CellCoord from, NeighborDirection direction) => routes.Contains(from)
            && routes.Contains(Arcanum.Simulation.Navigation.RoutePlanner.Neighbor(from, direction))
            && routes.Assess(from, direction) == Arcanum.Simulation.Navigation.RouteStepVerdict.Open;
        (Arcanum.Data.Traversal.AreaStepCell Tile, CellCoord From, CellCoord Pc)? line = null;
        var best = 0;
        foreach (var tile in wrecks)
        foreach (var direction in Enum.GetValues<NeighborDirection>())
        {
            var at = map.Chart.ToChart(tile);
            var from = Arcanum.Simulation.Navigation.RoutePlanner.Neighbor(at, (NeighborDirection)(((int)direction + 4) % 8));
            var cells = new List<CellCoord> { from };
            for (var step = 0; step < 5; step++) cells.Add(Arcanum.Simulation.Navigation.RoutePlanner.Neighbor(cells[^1], direction));
            if (line is null && cells.Take(5).All(c => Open(c, direction)))
                line = (tile, from, cells[^1]);
            best = Math.Max(best, cells.Take(5).TakeWhile(c => Open(c, direction)).Count());
        }
        Require(line is not null, $"a walkable line through a {place} tile ({wrecks.Length} tiles: {string.Join(" ", wrecks)}; longest open run {best} of 5)");
        var wreck = line!.Value.Tile;
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        var takesScripted = _smokeTakesScriptedConversations;
        _worldFocusSimulationForSelfTest = true; _smokeTakesScriptedConversations = true;
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
            Require(ConversationOpen && _conversationScripted != atDialogPoint, $"his conversation opened in {frames} frames: Virgil at {map.Chart.ToWorld(companion!.Grid.State.CommittedCell)}, notice {_conversationNotice}");
            Require(_voiceStart is { Status: AreaConversationStartStatus.Started } && _voiceStart.DialogScript == dialogScript && _conversationSpeaker?.Row.Key == template.Speaker.Row.Key,
                $"the DLG of script {dialogScript} with Virgil: {_voiceStart?.Status} {_voiceStart?.Detail}");
            var node = _conversationPanel!.Conversation?.CurrentNode.Value;
            for (var close = 0; close < 8 && ConversationOpen; close++) _conversationPanel.LeaveForSelfTest.EmitSignal(BaseButton.SignalName.Pressed);
            Require(!ConversationOpen, "his conversation closed");
            GD.Print(passLine + " " + System.Text.Json.JsonSerializer.Serialize(new {
                Map = _readyMapName, Tile = wreck, Virgil = from, Pc = map.Chart.ToWorld(line.Value.Pc), Frames = frames, Node = node,
                Script = dialogScript, Parity = "NOT_VERIFIED" }));
        }
        finally
        {
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus; _smokeTakesScriptedConversations = takesScripted;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
        }
        await Task.CompletedTask;
    }
}
