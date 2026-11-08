using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    private const int GhostTileScript = 2768, GhostStoryFlag = 2672;

    // ARC-DEV-298 (B20, smoke plan SM16) native observation (stage "tile-ghost", only when named, map mode with --hero, AI on, the map `crash site cave`): the tiles of script 2768 in the cave loop
    // over everyone in the vicinity and cast a spell on the one it names (action 61, cast spell on), then call the dialog point of the looped object. The hero is put on such a tile and the script
    // is run as the viewer's step does (the tile's entry): it must be run, not refused, the cast must reach the AI host and the ghost's conversation must open (the log of
    // the report: `enter TileEntry Outcome=ScriptRefused script 2768`).
    private async Task RunAreaGhostTileSmokeAsync()
    {
        void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-DEV-298 tile-ghost smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_TILE_GHOST_FAILED");
        }
        if (_areaSession is not { } session || session.QueryHero() is null || _readyArea is null || _mapTransfers is not { } transfers || _worldActor.Session is not AreaGridSession grid)
        { GD.Print($"ARC-DEV-298 native-smoke tile-ghost NONE map={_readyMapName} (needs a hero, an area and map transfers)"); return; }
        var tiles = transfers.ScriptTiles.Where(c => transfers.TileScript(c)?.Script == GhostTileScript).OrderBy(c => c.Y).ThenBy(c => c.X).ToArray();
        if (tiles.Length == 0)
        { GD.Print($"ARC-DEV-298 native-smoke tile-ghost NONE map={_readyMapName} (no tile of script {GhostTileScript} among {transfers.ScriptTiles.Count} script tiles)"); return; }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        var takesScripted = _smokeTakesScriptedConversations;
        _worldFocusSimulationForSelfTest = true; _smokeTakesScriptedConversations = true;
        _aiForSmoke = true;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            var map = new AreaMapRuntime(_readyArea.Snapshot, grid.Snapshot, grid.Chart);
            var tile = tiles[0];
            AreaSmokePlace(map, map.Chart.ToChart(tile));
            for (var frame = 0; frame < 40; frame++) AreaSmokeFrame();
            Require(AreaAiHostNow() is not null, "the AI host runs in the cave window");
            string Trace(AreaScriptedUse use) => use.Run is { } run ? string.Join(' ', run.Trace.Select(t => $"{t.Line}:c{t.Condition}{(t.Held ? '+' : '-')}a{t.Action}")) : "";
            AreaScriptedUse Step(string label)
            {
                var entered = transfers.Enter(tile);
                var use = transfers.LastScripted;
                Require(use is not null, $"{label}: the tile {tile.X},{tile.Y} ran a script ({string.Join(" | ", entered.Select(e => e.Outcome + " " + e.Reason))})");
                GD.Print($"ARC-DEV-298 tile-ghost {label} tile={tile.X},{tile.Y} outcome={use!.Outcome} detail={use.Detail} dialog={use.Dialog?.Script}/{use.Dialog?.Node} lines={use.Lines.Count} trace={Trace(use)}");
                Require(use.Outcome is not (AreaScriptedUseOutcome.Refused or AreaScriptedUseOutcome.EffectsRejected or AreaScriptedUseOutcome.ContentUnavailable), $"{label}: script {GhostTileScript} ran: {use.Outcome} {use.Detail}");
                return use;
            }
            // Control: the story flag is clear, so the script returns before the loop and casts nothing.
            var control = Step("control");
            Require(!Trace(control).Contains("+a61", StringComparison.Ordinal), $"no cast while the story flag {GhostStoryFlag} is clear: {Trace(control)}");
            var set = session.Flags.Write(new(new(Guid.NewGuid()), session.Flags.Version, new(GhostStoryFlag), true));
            Require(session.Flags.Read(new(new(Guid.NewGuid()), session.Flags.Version, new(GhostStoryFlag))).Value == true, $"the story flag {GhostStoryFlag} is set ({set})");
            _tileScriptFloats.Clear();
            var ran = Step("story");
            var trace = Trace(ran);
            Require(trace.Contains("+a61", StringComparison.Ordinal), $"the run reached cast spell on: {trace}");
            // The run hands the ghost's line (script 2789 float line) to the floater field, which draws it on the next frame.
            Require(_tileScriptFloats.Count > 0 && _tileScriptFloats.All(f => f.Text.Length > 0), $"the ghost's float line was queued ({_tileScriptFloats.Count} lines)");
            GD.Print("ARC-DEV-298 tile-ghost floats " + string.Join(" | ", _tileScriptFloats.Select(f => $"{f.Owner}: {f.Text}")));
            GD.Print("ARC-DEV-298 native-smoke tile-ghost PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = _readyMapName, Tile = new { tile.X, tile.Y }, Outcome = ran.Outcome.ToString(), Dialog = ran.Dialog?.Script }));
        }
        finally
        {
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus; _smokeTakesScriptedConversations = takesScripted;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            _aiForSmoke = false; RetireAiHost(); _aiHostStamp = null;
        }
        await Task.CompletedTask;
    }
}
