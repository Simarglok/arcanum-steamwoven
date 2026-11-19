using Arcanum.Data.Magic;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Goals;
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
    /// ARC-SCRIPT-1592 (FR-SCRIPT-002, FR-RENDER; ledger WE4, WE5; ARC-SCRIPT-1828, WE12: the stop) native observation (stage "script-eye-candy", only when named, with --hero, on a map window with the inventory active): the installed tile script 2549 (script eye candy
    /// on the player, then the teleport) runs for the hero through the map's own placed-script runner (its teleport is taken as accepted and not carried out). The stage checks that the use host kept the play request, that the viewer's pump
    /// started it as an overlay run of the script list on the hero (entry reference of the script list, the table's own art), that the run begins and its frame advances, that the draw placed its parts, and that a queued stop request (action 124) takes the run off and a second finds nothing, and that a queued play starts it again. NONE when the script does not play eye candy here. <c>ARC_AREA_SCRIPT_EYE_CANDY_CAPTURE=FOLDER</c> saves a readback of the view before the run and while the eye candy shows (for the thread's before/after picture; never committed).
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
            _scriptEyeCandyPlayedForSelfTest.Clear(); _scriptEyeCandyTurnedForSelfTest.Clear();
            var takenBefore = _scriptEyeCandyTaken; var soundsBefore = _scriptEyeCandySounds; var facingAtPlay = _worldActor.State.Facing & 7;
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
            // ARC-SCRIPT-1829: the entry's art is turned to the hero's facing at the play (0x502880 into 0x502930), its sound plays at the hero's cell when the entry has one.
            var turned = _scriptEyeCandyTurnedForSelfTest.First(t => t.Owner == owner);
            Require(turned.Rotation == facingAtPlay, $"the play took the hero's facing as the art modifier: {turned.Rotation} against {facingAtPlay}");
            Require(shown!.Eye.Art == EyeCandyArt.TurnTo(entry.Art!.Value, turned.Rotation), "the run holds the list entry's art turned to the facing");
            Require(entry.Sound < 0 || _scriptEyeCandySounds > soundsBefore, $"the entry's sound {entry.Sound} was asked for");
            var first = shown.Eye.Frame;
            for (var i = 0; i < 100 && (ShownEyeCandyHost?.Shown().FirstOrDefault(o => o.Handle.Equals(shown.Handle))?.Eye.Frame ?? first) == first; i += 2) await Frames(2);
            var moved = ShownEyeCandyHost?.Shown().FirstOrDefault(o => o.Handle.Equals(shown.Handle));
            Require(moved is null || moved.Eye.Frame != first, $"the frame advances ({first} -> {moved?.Eye.Frame})");
            Require(_eyeCandyDrawnForSelfTest.Contains(entryRef), "the draw placed the entry's parts");
            await Shot("during");
            // ARC-SCRIPT-1828 (WE12): the script's stop action (124) takes the run of the entry off at once, as the hosts' requests queue it. A play and a stop queued together are taken in order in one pump, so the stop finds the run its play started
            // (the play's flags 0 give even a looping entry the goal-45 run the original's walk 0x4243E0 removes), whether or not the first run has ended by itself; the owner's eye-candy runs are then no more than before.
            int Runs() => ShownEyeCandyHost?.Session.QueryGoals().RunsOf(owner).Count(r => !r.Done && r.Bottom.Goal == GoalType.EyeCandy) ?? 0;
            var runsBefore = Runs();
            _scriptEyeCandyStoppedForSelfTest.Clear();
            var stopFrom = _scriptEyeCandyStopTaken;
            var heroRef = new ScrObjectRef(CurrentAreaMap, AreaScriptHost.PcCode);
            QueueScriptEyeCandies([new ScrEyeCandy(number, heroRef), new ScrEyeCandy(number, heroRef, ScrEyeCandyKind.StopScript)]);
            await Frames(1);
            Require(_scriptEyeCandyStopTaken == stopFrom + 1 && _scriptEyeCandyStoppedForSelfTest.SequenceEqual([(owner, number)]), $"the stop request was taken once and took a run of the entry off the hero: taken {_scriptEyeCandyStopTaken - stopFrom}, stopped {string.Join(";", _scriptEyeCandyStoppedForSelfTest)}");
            Require(Runs() <= runsBefore, $"the stop left no more eye-candy runs on the hero than before ({runsBefore} -> {Runs()})");
            await Shot("stopped");
            // ARC-MAGIC-1830: the queue the session moves hand their scripts' requests to (an NPC's or the hero's rewield, a regeneration's wield) plays through the same pump: a request queued for the hero starts the same entry again (the stop above took the run of its own play off, so this one is a fresh run).
            _scriptEyeCandyPlayedForSelfTest.Clear();
            var queuedFrom = _scriptEyeCandyTaken;
            QueueScriptEyeCandies([new ScrEyeCandy(number, new ScrObjectRef(CurrentAreaMap, AreaScriptHost.PcCode))]);
            await Frames(1);
            Require(_scriptEyeCandyTaken == queuedFrom + 1 && _scriptEyeCandyPlayedForSelfTest.SequenceEqual([(owner, number)]), $"the queued request was taken once and played on the hero: taken {_scriptEyeCandyTaken - queuedFrom}, played {string.Join(";", _scriptEyeCandyPlayedForSelfTest)}");
            await Shot("queued");
            // ARC-SCRIPT-1829: an object that is no critter plays too: a scenery, container or portal row of the map with a cell, named the way the script host names it (a ground item and a created critter follow the same cell lookup).
            var still = area.Snapshot.Objects.FirstOrDefault(o => o.Type is 1 or 2 or 3 && o.WorldX is not null && o.WorldY is not null && _spellCastMap?.IdOf(o.Key) is not null && ObjectTargets()?.Gone(session).Contains(o.Key) != true);
            var stillNote = "NONE (the map has no scenery, container or portal row)";
            if (still is not null)
            {
                var id = _spellCastMap!.IdOf(still.Key)!;
                _scriptEyeCandyPlayedForSelfTest.Clear();
                QueueScriptEyeCandies([new ScrEyeCandy(number, new ScrObjectRef(CurrentAreaMap, AreaUseScriptHost.EntityId(still)))]);
                await Frames(1);
                Require(_scriptEyeCandyPlayedForSelfTest.Count == 1 && _scriptEyeCandyPlayedForSelfTest[0].Owner == id, $"the request on the row {id} (type {still.Type}) played on it: {string.Join(";", _scriptEyeCandyPlayedForSelfTest)}");
                for (var i = 0; i < 200 && !(ShownEyeCandyHost?.Shown().Any(o => o.Owner == id && o.Eye.Entry == entryRef) ?? false); i += 2) await Frames(2);
                Require(ShownEyeCandyHost?.Shown().Any(o => o.Owner == id && o.Eye.Entry == entryRef) ?? false, $"the eye candy shows on the row {id}");
                // ARC-SCRIPT-1828: the stop of that entry on the same row (the heartbeat's stop on a scenery) takes its run off.
                _scriptEyeCandyStoppedForSelfTest.Clear();
                QueueScriptEyeCandies([new ScrEyeCandy(number, new ScrObjectRef(CurrentAreaMap, AreaUseScriptHost.EntityId(still)), ScrEyeCandyKind.StopScript)]);
                await Frames(1);
                Require(_scriptEyeCandyStoppedForSelfTest.SequenceEqual([(id, number)]), $"the stop request took the run off the row {id}: {string.Join(";", _scriptEyeCandyStoppedForSelfTest)}");
                Require(!(ShownEyeCandyHost?.Shown().Any(o => o.Owner == id && o.Eye.Entry == entryRef) ?? false), $"the stopped eye candy no longer shows on the row {id}");
                stillNote = $"PASS type={still.Type} id={id}";
                await Shot("object");
            }
            GD.Print("ARC-SCRIPT-1829 script-eye-candy object-without-critter " + stillNote);
            // ARC-SCRIPT-2020: a map item's script eye candy is anchored on the item: at its row cell while it lies there, none once the hero carries it, at the placed cell after a drop.
            var itemNote = "NONE (the map has no ground item with a row, or no inventory host)";
            var lying = _inventoryHost is null || _spellCastMap is null ? null
                : session.QueryMapInventory(new SessionMapId(CurrentAreaMap))?.Ground.FirstOrDefault(g => _spellCastMap.Row(g.Item.Entity) is { Type: >= 5 and <= 14 } && ObjectTargets()?.Gone(session).Contains(_spellCastMap.Row(g.Item.Entity)!.Key) != true
                    && !session.QueryItemOff(new SessionMapId(CurrentAreaMap), g.Item.Entity, g.Item.Facts.OffInData));
            if (lying is not null)
            {
                var item = lying.Item.Entity;
                Require(EyeCandyCell(session, item) == lying.Cell, $"the eye candy of the lying item {item} stands at its cell {lying.Cell}: {EyeCandyCell(session, item)}");
                if (!AreaInventoryHost.Moved(_inventoryHost!.PickUp(session, item))) itemNote = $"NONE (the pickup of {item} was refused: the carried case is not observed)";
                else
                {
                    Require(EyeCandyCell(session, item) is null, $"the carried item {item} has no cell for a script eye candy: {EyeCandyCell(session, item)}");
                    _scriptEyeCandyPlayedForSelfTest.Clear();
                    QueueScriptEyeCandies([new ScrEyeCandy(number, new ScrObjectRef(CurrentAreaMap, AreaUseScriptHost.EntityId(_spellCastMap!.Row(item)!)))]);
                    await Frames(1);
                    Require(_scriptEyeCandyPlayedForSelfTest.Count == 0, $"the request on the carried item {item} played nothing: {string.Join(";", _scriptEyeCandyPlayedForSelfTest)}");
                    itemNote = $"PASS item={item} cell={lying.Cell}";
                }
            }
            GD.Print("ARC-SCRIPT-2020 script-eye-candy carried-item " + itemNote);
            GD.Print("ARC-SCRIPT-1592 native-smoke script-eye-candy PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = _readyMapName, Script = VoidExitScript, Number = number, Entry = entryRef, Rotation = turned.Rotation, Sound = entry.Sound, SoundsAsked = _scriptEyeCandySounds, Looping = (entry.Flags & SpellEyeCandyFlags.Forever) != 0, FirstFrame = first, MovedTo = moved?.Eye.Frame, Stopped = _scriptEyeCandyStopped }));
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
