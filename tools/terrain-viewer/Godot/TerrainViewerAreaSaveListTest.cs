using System.Reflection;
using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.TerrainViewer.Tests;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public static partial class TerrainViewerSelfTest
{
    /// <summary>ARC-UI-010 over the strict synthetic AREA owner in the production app, on a temporary save folder: the save window
    /// writes a new numbered save, asks before overwriting (Escape answers no, Return yes), owns the keys (F7 and L do nothing);
    /// F7 writes the auto-save with its details (and a preview when a frame can be read); the load window lists the auto-save first,
    /// refuses to delete it, loads the chosen save under a new session and closes; F8 loads the newest save; a delete asks first.</summary>
    private static async Task AssertAreaSaveListAsync(Node host)
    {
        using var source = new AreaOwnerFixture(motionBodies: true);
        var read = typeof(TerrainLoadCoordinator).Assembly.GetType("Arcanum.World.AreaLoadOperation")!
            .GetMethod("ReadArea", BindingFlags.NonPublic | BindingFlags.Static)!;
        var area = (AreaLoadResult)read.Invoke(null, [7L, source.Owner, CancellationToken.None, null])!;
        var steps = AreaOwnerFixture.Steps(area.Snapshot, source.Streams[new(AreaSourceRole.Module)].Bytes, 7);
        source.Owner.Dispose();
        var result = SyntheticActorFixture.Create(human: true, area: true).Result with { Generation = 7, Area = area, AreaStepSnapshot = steps };
        var arguments = TerrainViewerArguments.Parse(["--mode", "crash-site-area", "--module", "synthetic-module",
            "--resource", "synthetic-terrain", "--actor-resource", "synthetic-actor", "--prototypes", "synthetic-prototypes",
            "--area-mobile", "synthetic-mobile", "--area-effects", "synthetic-effects", "--area-patch", "synthetic-patch", "--source-id", "synthetic"]);
        var viewport = new SubViewport { Size = new(800, 600), TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        var app = new TerrainViewerApp { SelfTestBootstrap = true };
        viewport.AddChild(app); host.AddChild(viewport);
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        app.SetProcess(false); app.SetProcessInput(false);
        string summary;
        try
        {
            app.PublishSyntheticForSelfTest(arguments, result);
            // Two frames: the published scene is drawn before F7 and the save window read a frame (a headless run draws none).
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            summary = app.AreaSaveListForSelfTest();
        }
        finally
        {
            app.ClearSyntheticForSelfTest();
            viewport.Free();
        }
        GD.Print($"ARC-UI-010 self-test PASS synthetic save list ({summary})");
    }
}

public partial class TerrainViewerApp
{
    internal string AreaSaveListForSelfTest()
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("AREA save list: " + why); }
        if (!SelfTestBootstrap || _readyArea is null || _areaSession is null) throw new InvalidOperationException("AREA_SAVE_LIST_READY_REQUIRED");
        var simulatedFocus = _worldFocusSimulationForSelfTest;
        _worldFocusSimulationForSelfTest = true; _worldFocused = true;
        var directory = System.IO.Directory.CreateTempSubdirectory("arc-ui-010-").FullName;
        _saveLibraryForSelfTest = new(directory);
        var library = _saveLibraryForSelfTest;
        var framed = DisplayServer.GetName() != "headless";
        try
        {
            string File(string name) => System.IO.Path.Combine(directory, name);
            void Age(string name, int minutesAgo) => System.IO.File.SetLastWriteTimeUtc(File(name + ".json"), DateTime.UtcNow.AddMinutes(-minutesAgo));
            var start = _areaSession.QueryActor();

            // The save window: modal, the new-save row only, the description field has the keyboard.
            OpenSaveList(AreaSaveListMode.Save);
            Require(SaveListOpen && WorldModal && !_worldGate.IsOpen, "the save window opened modal");
            Require(_saveListPanel!.RowsForSelfTest.SequenceEqual(["<new save>"]) && _saveList!.Selected == 0, "an empty folder lists the new-save row only");
            Require(GetSelfTestSnapshot().Hud.Contains(" | save list open: Escape closes", StringComparison.Ordinal), "HUD open status");
            Require(_saveListPanel.TypingDescription && !HandleSaveListKey(new InputEventKey { Keycode = Key.A, PhysicalKeycode = Key.A, Pressed = true }),
                "a typed letter reaches the description field on the new-save row");
            _saveList!.Description = "Synthetic start";
            InventoryKey(Key.Enter);
            var listed = library.List().Entries;
            Require(listed is [{ Name: "slot0000", Details: GameSaveDetails.Present }] && listed[0].DisplayDescription == "Synthetic start",
                "Return saved slot0000 with its description: " + _quickSaveStatus);
            Require(listed[0].HasThumbnail == framed && !SaveListOpen && !WorldModal
                && _quickSaveStatus?.StartsWith("Save saved slot0000", StringComparison.Ordinal) == true, "a successful save closed the window: " + _quickSaveStatus);

            // Overwrite asks first: Escape answers no (the window stays), Return yes; the listed save keeps its description.
            OpenSaveList(AreaSaveListMode.Save);
            Require(_saveListPanel.RowsForSelfTest.SequenceEqual(["<new save>", "Synthetic start"]) && _saveList!.Selected == 1 && !_saveListPanel.TypingDescription,
                "the first save is selected and the field does not take the keys there");
            Require(_saveListPanel.PreviewShownForSelfTest == framed, "the save's preview is shown when a frame was read");
            _saveList!.Description = "Typed but not used";
            InventoryKey(Key.Enter);
            Require(_saveList.Prompt == AreaSavePrompt.ConfirmOverwrite && SaveListOpen, "Return on a listed save asks before overwriting");
            // A double click on another row does not answer the question.
            _saveListPanel.EmitRowActivatedForSelfTest(0);
            Require(_saveList.Prompt == AreaSavePrompt.ConfirmOverwrite && library.List().Entries.Count == 1, "a row activation left the question open");
            InventoryKey(Key.Escape);
            Require(_saveList.Prompt == AreaSavePrompt.None && SaveListOpen, "Escape answered no and kept the window");
            // The open window owns the keys: F7 saves nothing, L opens no logbook.
            InventoryKey(Key.F7); InventoryKey(Key.L);
            Require(!System.IO.File.Exists(File("quicksave.json")) && !LogbookOpen && SaveListOpen, "F7 and L did nothing under the save window");
            Age("slot0000", 5);
            InventoryKey(Key.Enter); InventoryKey(Key.Enter);
            Require(!SaveListOpen && System.IO.File.GetLastWriteTimeUtc(File("slot0000.json")) > DateTime.UtcNow.AddMinutes(-1)
                && library.List().Entries.Single().DisplayDescription == "Synthetic start", "Return, Return overwrote the save, kept its description and closed");

            // F7: the auto-save with the original's description and the details of the session.
            QuickKey(Key.F7);
            var auto = library.List().Entries[0];
            Require(auto is { Name: "quicksave", IsAuto: true, Details: GameSaveDetails.Present } && auto.DisplayDescription == "Auto-Save"
                && auto.Summary!.Location == "Crash Site" && auto.Summary.HeroName == "diagnostic PC" && auto.HasThumbnail == framed, "F7 wrote the auto-save: " + _quickSaveStatus);

            // The load window: the auto-save first; it cannot be deleted; Down and Return load slot0000 under a new session.
            Age("slot0000", 30); Age("quicksave", 10);
            OpenSaveList(AreaSaveListMode.Load);
            Require(_saveListPanel.RowsForSelfTest.SequenceEqual(["[auto] Auto-Save", "Synthetic start"]) && _saveList!.Selected == 0, "load rows, auto first");
            InventoryKey(Key.Delete);
            Require(_saveList.Prompt == AreaSavePrompt.None && _saveListPanel.NoticeForSelfTest == "the auto-save cannot be deleted" && library.AutoSlot.Exists,
                "the auto-save is not deleted");
            var running = _areaSession;
            InventoryKey(Key.Down); InventoryKey(Key.Enter);
            Require(!SaveListOpen && !ReferenceEquals(running, _areaSession) && _areaSession!.QueryActor() == start
                && _quickSaveStatus?.StartsWith("Load quick loaded", StringComparison.Ordinal) == true && _quickSaveStatus.EndsWith(" from slot0000", StringComparison.Ordinal),
                "Return loaded slot0000 and closed the window: " + _quickSaveStatus);

            // F8 loads the newest save by file date: the auto-save while it is newer, then a numbered save made newer.
            QuickKey(Key.F8);
            Require(_quickSaveStatus?.StartsWith("F8 quick loaded", StringComparison.Ordinal) == true && _quickSaveStatus.EndsWith(" from quicksave", StringComparison.Ordinal),
                "F8 loaded the newer auto-save: " + _quickSaveStatus);
            Age("slot0000", 1);
            var beforeQuick = _areaSession;
            QuickKey(Key.F8);
            Require(!ReferenceEquals(beforeQuick, _areaSession) && _quickSaveStatus?.EndsWith(" from slot0000", StringComparison.Ordinal) == true,
                "F8 loaded the newer numbered save, not the auto-save: " + _quickSaveStatus);

            // ARC-UI-1504: with the load screen on, the window loads behind the screen: it stays until the load ran and then closes; a damaged save keeps it, with the reason.
            _loadScreenDeferForSelfTest = true;
            try
            {
                OpenSaveList(AreaSaveListMode.Load);
                var beforeScreen = _areaSession; var screens = _transitionLoads;
                InventoryKey(Key.Down); InventoryKey(Key.Enter);
                Require(SaveListOpen && _pendingLoad is not null && ReferenceEquals(beforeScreen, _areaSession) && _transitionScreen is { Loading: true, Alpha: >= 1 } && _transitionLoads == screens + 1,
                    "Return raised the load screen before the load ran");
                TickTransitionScreen(100);
                Require(SaveListOpen && _pendingLoad is not null && ReferenceEquals(beforeScreen, _areaSession), "a frame drew the screen before the load ran");
                TickTransitionScreen(100);
                Require(!SaveListOpen && _pendingLoad is null && !_transitionScreen.Covering && !ReferenceEquals(beforeScreen, _areaSession)
                    && _quickSaveStatus?.StartsWith("Load quick loaded", StringComparison.Ordinal) == true, "the load ran, the window closed and the screen went: " + _quickSaveStatus);
                var intact = System.IO.File.ReadAllBytes(library.Slot("slot0000").Path);
                try
                {
                    System.IO.File.WriteAllText(library.Slot("slot0000").Path, "{\"format\":\"arcanum-steamwoven-session\",");
                    OpenSaveList(AreaSaveListMode.Load);
                    var kept = _areaSession;
                    InventoryKey(Key.Down); InventoryKey(Key.Enter);
                    TickTransitionScreen(100); TickTransitionScreen(100);
                    Require(SaveListOpen && _pendingLoad is null && !_transitionScreen.Covering && ReferenceEquals(kept, _areaSession) && _quickSaveStatus?.Contains("FAILED", StringComparison.Ordinal) == true,
                        "a refused load cleared the screen, kept the window and the game: " + _quickSaveStatus);
                    InventoryKey(Key.Escape);
                    Require(!SaveListOpen, "Escape closed the load window");
                }
                finally { System.IO.File.WriteAllBytes(library.Slot("slot0000").Path, intact); }

                // ARC-UI-1574: a load that throws still ends as a refused one: the screen goes and the window is told (it stays, with the reason).
                OpenSaveList(AreaSaveListMode.Load);
                var faulted = _areaSession;
                InventoryKey(Key.Down); InventoryKey(Key.Enter);
                Require(SaveListOpen && _pendingLoad is not null, "the load waits for its screen before the fault");
                _pendingLoadFaultForSelfTest = () => throw new InvalidOperationException("self-test fault");
                var threw = false;
                try { RunPendingLoad(); } catch (InvalidOperationException) { threw = true; } finally { _pendingLoadFaultForSelfTest = null; }
                Require(threw && SaveListOpen && _pendingLoad is null && !_transitionScreen.Covering && ReferenceEquals(faulted, _areaSession)
                    && _quickSaveStatus?.Contains("FAILED", StringComparison.Ordinal) == true,
                    "a load that threw cleared the screen and told the window: " + _quickSaveStatus);
                InventoryKey(Key.Escape);
                Require(!SaveListOpen, "Escape closed the load window after the fault");

                // ARC-UI-1574: a load that is not a player's (the death reload, a smoke) landing in the window of a pending load supersedes it, not refuses.
                OpenSaveList(AreaSaveListMode.Load);
                var superseded = _areaSession;
                InventoryKey(Key.Down); InventoryKey(Key.Enter);
                Require(SaveListOpen && _pendingLoad is not null, "the load waits for its screen before the second load");
                Require(LoadGame(library.Slot("slot0000"), "smoke ") && _pendingLoad is null && !_transitionScreen.Covering && !ReferenceEquals(superseded, _areaSession),
                    "a load inside the window of a pending load was made, not refused: " + _quickSaveStatus);
                if (SaveListOpen) InventoryKey(Key.Escape);
                Require(!SaveListOpen, "the load window is closed after the superseding load");
                // A player's load inside the window is still refused (F8 twice does not drop the first).
                var pendingBefore = _areaSession;
                OpenSaveList(AreaSaveListMode.Load);
                InventoryKey(Key.Down); InventoryKey(Key.Enter);
                var firstPending = _pendingLoad;
                QuickKey(Key.F8);
                Require(firstPending is not null && ReferenceEquals(_pendingLoad, firstPending) && ReferenceEquals(pendingBefore, _areaSession), "F8 inside the window of a pending load was refused");
                TickTransitionScreen(100); TickTransitionScreen(100);
                Require(_pendingLoad is null && !_transitionScreen.Covering, "the first load ran after the refused F8");
                if (SaveListOpen) InventoryKey(Key.Escape);
            }
            finally { _loadScreenDeferForSelfTest = false; }

            // A delete asks first; Return deletes; the selection is cleared and the list keeps the auto-save.
            OpenSaveList(AreaSaveListMode.Load);
            Require(_saveListPanel.RowsForSelfTest.SequenceEqual(["[auto] Auto-Save", "Synthetic start"]), "the auto-save stays first");
            InventoryKey(Key.Down); InventoryKey(Key.Backspace);
            Require(_saveList!.Prompt == AreaSavePrompt.ConfirmDelete && library.Slot("slot0000").Exists, "Backspace asked before deleting");
            InventoryKey(Key.Enter);
            Require(!library.Slot("slot0000").Exists && _saveListPanel.RowsForSelfTest.SequenceEqual(["[auto] Auto-Save"])
                && _saveListPanel.NoticeForSelfTest == "deleted" && _saveList.Selected == -1, "Return deleted slot0000 and cleared the selection");
            InventoryKey(Key.Escape);
            Require(!SaveListOpen, "Escape closed the load window");
            return $"new save closes, overwrite asked (row activation ignored, no then yes) keeping the description, keys owned, F7 auto-save details, " +
                $"preview {(framed ? "written and shown" : "skipped headless")}, auto-save first and not deletable, load by row, F8 newest (auto then numbered), load behind the screen (done, refused), " +
                "delete asked and selection cleared";
        }
        finally
        {
            if (SaveListOpen) EndSaveList();
            _saveLibraryForSelfTest = null; _quickKeys.Reset();
            _worldFocusSimulationForSelfTest = simulatedFocus;
            System.IO.Directory.Delete(directory, recursive: true);
        }
    }
}
