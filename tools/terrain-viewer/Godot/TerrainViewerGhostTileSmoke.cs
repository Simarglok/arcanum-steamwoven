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
    // is run as the viewer's step does (the tile's entry): it must be run, not refused (the log of the report: `enter TileEntry Outcome=ScriptRefused script 2768`), its trace must
    // reach cast spell on (line 8) and the called script's float line must be queued for the floater field. The story flag the stage sets is cleared again at the end.
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
        var ghostRun = -1;
        ClearEyeCandy("tile-ghost start"); _eyeCandyDrawnForSelfTest.Clear();
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
            // ARC-DEV-338 (B53): the cast of spell 216 has no caster, so its run has no casting goal and begins by its magic-tech event (2 ms later, 0x455C30: the clock ticks and the frame step fires it). Its Begin
            // components must reach the screen: the spoken-with-dead flag on the ghost and the two eye candies (entries 6 x 216 + 2 and + 3), which are the ghost's picture; drawn as moving bodies.
            var ghostSpell = 216;
            for (var wait = 0; wait < 600 && SpellRecordsNow() is null; wait++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            // The host the script's cast went through (the cast built it; asking SpellCasterNow() here could rebuild it, which gives up the run).
            SessionSpellCaster Caster() => _spellCaster ?? throw new InvalidOperationException("AREA_TILE_GHOST_NO_CAST_HOST");
            Require(_spellCaster is not null && ReferenceEquals(_spellCaster.Owner, session), "the cast host of the cave exists");
            var eyeEntries = new[] { ghostSpell * 6 + 2, ghostSpell * 6 + 3 };
            var frames = 0;
            for (; frames < 60 && !Caster().Log.Any(e => e.Kind == SpellCastEventKind.EyeCandyAdded && eyeEntries.Contains(e.Value)); frames++)
            { TickAreaClock(_talkSmokeHostMs += 100); AreaSmokeFrame(); }
            var caster = Caster();
            var added = caster.Log.Where(e => e.Kind == SpellCastEventKind.EyeCandyAdded && eyeEntries.Contains(e.Value)).ToArray();
            GD.Print($"ARC-DEV-338 tile-ghost run frames={frames} runs={string.Join(",", session.QuerySpellRuns().Runs.Select(r => $"{r.Id}:{r.Spell}:{r.Action}"))} log={string.Join(",", caster.Log.Select(e => e.Kind))}");
            Require(added.Length == 2, $"the begun run logged the ghost's two eye candies ({added.Length}) after {frames} frames");
            var ghost = added[0].Object ?? "";
            ghostRun = added[0].Run;
            Require(caster.Log.Any(e => e.Kind == SpellCastEventKind.ObjectFlagWritten && e.Object == ghost && e.Value == SessionSpellCaster.SpokenWithDeadSpellFlag) && caster.SpokenWithDead(ghost),
                "the run set the spoken-with-dead flag on the ghost");
            // The viewer posts the cast log on each world frame: the eye candy plays on the ghost and is drawn as a moving body.
            for (var step = 0; step < 5; step++) { TickAreaClock(_talkSmokeHostMs += 100); AreaSmokeFrame(); }
            Require(_spellLogSeen >= caster.Log.Count, $"the cast log was posted ({_spellLogSeen} of {caster.Log.Count})");
            var parts = _eyeCandies.Where(e => e.Object == ghost).Select(e => e.Index).Concat((ShownEyeCandyHost?.Shown() ?? []).Where(o => o.Owner == ghost).Select(o => o.Eye.Entry)).Distinct().ToArray();
            GD.Print($"ARC-DEV-338 tile-ghost eye candy shown entries={string.Join(",", parts)} drawn={string.Join(",", _eyeCandyDrawnForSelfTest)} bodies={_eyeCandyBodies.Count}");
            Require(parts.Length > 0 && parts.All(eyeEntries.Contains) && _eyeCandyBodies.Count > 0 && _eyeCandyDrawnForSelfTest.Overlaps(parts), $"the ghost's eye candy is drawn (shown {parts.Length}, bodies {_eyeCandyBodies.Count})");
            Require((AreaMovingCommands() ?? []).Any(c => _eyeCandyBodies.ContainsValue(c)), "the drawn eye candy is among the moving bodies the canvas gets");
            GD.Print("ARC-DEV-298 native-smoke tile-ghost PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = _readyMapName, Tile = new { tile.X, tile.Y }, Outcome = ran.Outcome.ToString(), Dialog = ran.Dialog?.Script, Ghost = ghost, Parts = parts }));
        }
        finally
        {
            // The ghost's spell run ends with the stage: its END takes the flag and the eye candy off.
            if (ghostRun >= 0 && _spellCaster is { } ending && ending.Owner.QuerySpellRuns()[ghostRun] is not null) { ending.Act(new(SessionSpellRunActionKind.ForceEnd, ghostRun)); FlushSpellLog(null); }
            if (_areaSession is { } flagOwner)
                flagOwner.Flags.Write(new(new(Guid.NewGuid()), flagOwner.Flags.Version, new(GhostStoryFlag), false));
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus; _smokeTakesScriptedConversations = takesScripted;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            _aiForSmoke = false; RetireAiHost(); _aiHostStamp = null;
        }
        await Task.CompletedTask;
    }
}
