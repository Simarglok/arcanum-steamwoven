using Arcanum.Data.Magic;
using Arcanum.Session;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    internal const string ScriptEyeCandyCode = "ARC-SCRIPT-1592";

    // The installed exit tile script of the void maps: its first action plays script eye candy on the triggerer, the second teleports (docs/research/ledger/scripts.md VT8).
    private const int VoidExitScript = 2549;

    /// <summary>
    /// ARC-SCRIPT-1592 (FR-SCRIPT-002, FR-RENDER; ledger WE4, WE5) native observation (stage "script-eye-candy", only when named, with --hero, on a map window with the inventory active): the installed tile script 2549 (script eye candy
    /// on the player, then the teleport) runs for the hero through the map's own placed-script runner (its teleport is taken as accepted and not carried out). The stage checks that the use host kept the play request, that the viewer's pump
    /// started it as an overlay run of the script list on the hero (entry reference of the script list, the table's own art), that the run begins and its frame advances, that the draw placed its parts, and that the run ends (or is taken off) at
    /// the end. NONE when the script does not play eye candy here. <c>ARC_AREA_SCRIPT_EYE_CANDY_CAPTURE=FOLDER</c> saves a readback of the view before the run and while the eye candy shows (for the thread's before/after picture; never committed).
    /// </summary>
    private async Task RunAreaScriptEyeCandyStageAsync(TerrainLoadResult? mapMode = null)
    {
        for (var wait = 0; wait < 1800 && (ExtraArt() is null || !InventoryActive); wait++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (_areaSession is not { } session || session.QueryHero() is null || _readyArea is not { } area || _worldActor.Session is not AreaGridSession || !InventoryActive || _mapTransfers?.PlacedRunner is null)
        { GD.Print($"{ScriptEyeCandyCode} native-smoke script-eye-candy SKIP (needs --hero, an area session, the inventory and the map's placed-script runner)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print($"{ScriptEyeCandyCode} script-eye-candy smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_SCRIPT_EYE_CANDY_SMOKE_FAILED");
        }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        var scaled = false;
        var captureDir = System.Environment.GetEnvironmentVariable("ARC_AREA_SCRIPT_EYE_CANDY_CAPTURE");
        async Task Shot(string name)
        {
            if (string.IsNullOrEmpty(captureDir)) return;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            System.IO.Directory.CreateDirectory(captureDir);
            using var image = GetViewport().GetTexture().GetImage();
            image.SavePng(System.IO.Path.Combine(captureDir, name + ".png"));
        }
        try
        {
            _worldFocusSimulationForSelfTest = true; _worldFocused = true; SelectSimulatedPadForSelfTest();
            var map = mapMode is { Area: { } loaded, AreaStepSnapshot: { } mapSteps }
                ? new AreaMapRuntime(loaded.Snapshot, mapSteps, loaded.Plan.Chart) : _areaTransitions!.Current;
            AreaSmokePlace(map, ((AreaGridSession)_worldActor.Session!).State.CommittedCell);
            _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
            PublishWorldSelection();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            for (var wait = 0; wait < 600 && (SpellCasterNow() is null || SpellRecordsNow() is null); wait++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(SpellCasterNow() is not null && SpellRecordsNow() is not null, "the map's cast host and the spell records are built (spell data " + _spellData?.Status + ")");
            var table = _spellData?.ScriptEyeCandy;
            Require(table is not null, "the script eye-candy list is read: " + _spellData?.ScriptEyeCandyDetail);
            // A placement restarts the smoke clock in tenths of a second; the frames here are twentieths.
            scaled = true; _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            async Task Frames(int count)
            {
                for (var i = 0; i < count; i++) { TechSmokeFrame(); FlushSpellLog(null); }
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            await Frames(5);
            var heroCell = HeroWorldCell() ?? throw new InvalidOperationException("AREA_SCRIPT_EYE_CANDY_NO_HERO_CELL");
            Require((ShownEyeCandyHost?.Shown().Count() ?? 0) == 0, "no eye candy shows before the run");
            _scriptEyeCandyPlayedForSelfTest.Clear();
            var takenBefore = _scriptEyeCandyTaken;
            await Shot("before");
            var placed = new Arcanum.Data.Maps.MapPlacedScript(Arcanum.Data.Maps.MapTransitionTrigger.TileEntry, new(0, 0), -1, new(heroCell.X, heroCell.Y), 0, 0, VoidExitScript);
            var ran = _mapTransfers!.PlacedRunner!(placed, _ => null);
            GD.Print($"{ScriptEyeCandyCode} script-eye-candy run outcome={ran.Outcome} detail={ran.Detail}");
            if (ran.Outcome != AreaScriptedUseOutcome.Teleport) { GD.Print($"{ScriptEyeCandyCode} native-smoke script-eye-candy NONE (script {VoidExitScript} did not run here: {ran.Outcome} {ran.Detail})"); return; }
            await Frames(1);
            Require(_scriptEyeCandyTaken > takenBefore, "the viewer took the run's play request from the use host");
            Require(_scriptEyeCandyPlayedForSelfTest.Count >= 1 && _scriptEyeCandyPlayedForSelfTest.All(p => p.Owner == SessionSpellCaster.Hero), $"the request played on the hero: {string.Join(";", _scriptEyeCandyPlayedForSelfTest)}");
            var (owner, number) = _scriptEyeCandyPlayedForSelfTest[0];
            var entryRef = EyeCandyEntryRef.Make(EyeCandyList.Script, number)!.Value;
            var entry = table!.At(number);
            Require(entry.Art is { } art && (entry.Flags & SpellEyeCandyFlags.Animates) != 0, $"entry {number} of the script list has art and animates: {entry}");
            // The run begins on the animation clock and shows frame 0, then its frame advances.
            for (var i = 0; i < 200 && !(ShownEyeCandyHost?.Shown().Any(o => o.Owner == owner && o.Eye.Entry == entryRef) ?? false); i += 2) await Frames(2);
            var shown = ShownEyeCandyHost?.Shown().FirstOrDefault(o => o.Owner == owner && o.Eye.Entry == entryRef);
            Require(shown is not null, "the eye candy of the script list shows on the hero");
            Require(shown!.Eye.Art == entry.Art, "the run holds the list entry's art");
            var first = shown.Eye.Frame;
            for (var i = 0; i < 100 && (ShownEyeCandyHost?.Shown().FirstOrDefault(o => o.Handle.Equals(shown.Handle))?.Eye.Frame ?? first) == first; i += 2) await Frames(2);
            var moved = ShownEyeCandyHost?.Shown().FirstOrDefault(o => o.Handle.Equals(shown.Handle));
            Require(moved is null || moved.Eye.Frame != first, $"the frame advances ({first} -> {moved?.Eye.Frame})");
            Require(_eyeCandyDrawnForSelfTest.Contains(entryRef), "the draw placed the entry's parts");
            await Shot("during");
            // It runs to its end (a looping entry is taken off: the original's script has no stop action modelled yet, so the stage ends it as a map change does).
            var forever = (entry.Flags & SpellEyeCandyFlags.Forever) != 0;
            if (!forever)
            {
                for (var i = 0; i < 1200 && (ShownEyeCandyHost?.Shown().Any(o => o.Owner == owner && o.Eye.Entry == entryRef) ?? false); i += 5) await Frames(5);
                Require(!(ShownEyeCandyHost?.Shown().Any(o => o.Owner == owner && o.Eye.Entry == entryRef) ?? false), "the eye candy ran to its end");
            }
            GD.Print("ARC-SCRIPT-1592 native-smoke script-eye-candy PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = _readyMapName, Script = VoidExitScript, Number = number, Entry = entryRef, Looping = forever, FirstFrame = first, MovedTo = moved?.Eye.Frame }));
        }
        finally
        {
            HeroRunsHost()?.LoseAll("SMOKE_SCRIPT_EYE_CANDY_END");
            _eyeCandyHost?.LoseAll();
            _useScriptHost?.Host.TakeEyeCandies();
            if (scaled) _areaSmokeTime = (_areaSmokeTime + 19) / 20;
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            PublishWorldSelection();
        }
    }
}
