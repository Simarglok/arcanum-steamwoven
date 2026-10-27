using System.Reflection;
using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.TerrainViewer.Tests;
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
        var read = typeof(TerrainLoadCoordinator).Assembly.GetType("Arcanum.TerrainViewer.Core.AreaLoadOperation")!
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
                "a typed letter reaches the description field");
            _saveList!.Description = "Synthetic start";
            InventoryKey(Key.Enter);
            var listed = library.List().Entries;
            Require(listed is [{ Name: "slot0000", Details: GameSaveDetails.Present }] && listed[0].DisplayDescription == "Synthetic start",
                "Return saved slot0000 with its description: " + _quickSaveStatus);
            var saved = listed[0];
            Require(saved.HasThumbnail == framed && SaveListOpen && _saveListPanel.NoticeForSelfTest == "saved as slot0000"
                && _saveListPanel.RowsForSelfTest.SequenceEqual(["<new save>", "Synthetic start"]) && _saveList.Selected == 1, "the list shows the new save, selected");
            Require(_saveListPanel.PreviewShownForSelfTest == framed, "the new save's preview is shown when a frame was read");
            var savedBytes = System.IO.File.ReadAllBytes(File("slot0000.json"));

            // Overwrite asks first: Escape answers no (the window stays), Return yes.
            _saveList.Description = "Typed but not used";
            InventoryKey(Key.Enter);
            Require(_saveList.Prompt == AreaSavePrompt.ConfirmOverwrite && SaveListOpen, "Return on a listed save asks before overwriting");
            InventoryKey(Key.Escape);
            Require(_saveList.Prompt == AreaSavePrompt.None && SaveListOpen && library.List().Entries.Single().DisplayDescription == "Synthetic start",
                "Escape answered no and kept the window and the save");
            Age("slot0000", 5);
            InventoryKey(Key.Enter); InventoryKey(Key.Enter);
            Require(System.IO.File.GetLastWriteTimeUtc(File("slot0000.json")) > DateTime.UtcNow.AddMinutes(-1)
                && library.List().Entries.Single().DisplayDescription == "Synthetic start", "Return, Return overwrote the save, keeping its description");
            // The open window owns the keys: F7 saves nothing, L opens no logbook.
            InventoryKey(Key.F7); InventoryKey(Key.L);
            Require(!System.IO.File.Exists(File("quicksave.json")) && !LogbookOpen && SaveListOpen, "F7 and L did nothing under the save window");
            InventoryKey(Key.Escape);
            Require(!SaveListOpen && !WorldModal, "Escape closed the save window");

            // F7: the auto-save with the original's description and the details of the session.
            QuickKey(Key.F7);
            var auto = library.List().Entries[0];
            Require(auto is { Name: "quicksave", IsAuto: true, Details: GameSaveDetails.Present } && auto.DisplayDescription == "Auto-Save"
                && auto.Summary!.Location == "Crash Site" && auto.Summary.HeroName == "diagnostic PC" && auto.HasThumbnail == framed, "F7 wrote the auto-save: " + _quickSaveStatus);
            Age("slot0000", 30); Age("quicksave", 10);

            // The load window: the auto-save first; it cannot be deleted; Down and Return load slot0000 under a new session.
            OpenSaveList(AreaSaveListMode.Load);
            Require(_saveListPanel.RowsForSelfTest.SequenceEqual(["[auto] Auto-Save", "Synthetic start"]) && _saveList!.Selected == 0, "load rows, auto first");
            InventoryKey(Key.Delete);
            Require(_saveList.Prompt == AreaSavePrompt.None && _saveListPanel.NoticeForSelfTest == "the auto-save cannot be deleted" && library.AutoSlot.Exists,
                "the auto-save is not deleted");
            var running = _areaSession;
            InventoryKey(Key.Down); InventoryKey(Key.Enter);
            Require(!SaveListOpen && !ReferenceEquals(running, _areaSession) && _areaSession!.QueryActor() == start
                && _quickSaveStatus?.StartsWith("Load quick loaded", StringComparison.Ordinal) == true, "Return loaded slot0000 and closed the window: " + _quickSaveStatus);
            Require(System.IO.File.ReadAllBytes(File("slot0000.json")).Length > 0 && savedBytes.Length > 0, "the save files stay");

            // F8 loads the newest save (the auto-save here), not the selected or the first numbered one.
            var beforeQuick = _areaSession;
            QuickKey(Key.F8);
            Require(!ReferenceEquals(beforeQuick, _areaSession) && _quickSaveStatus?.StartsWith("F8 quick loaded", StringComparison.Ordinal) == true, "F8 loaded: " + _quickSaveStatus);
            Age("slot0000", 1);
            Require(library.Latest()!.Name == "slot0000", "a newer numbered save becomes the latest");

            // A delete asks first; Return deletes; the list keeps the auto-save.
            OpenSaveList(AreaSaveListMode.Load);
            Require(_saveListPanel.RowsForSelfTest.SequenceEqual(["[auto] Auto-Save", "Synthetic start"]), "the auto-save stays first");
            InventoryKey(Key.Down); InventoryKey(Key.Backspace);
            Require(_saveList!.Prompt == AreaSavePrompt.ConfirmDelete && library.Slot("slot0000").Exists, "Backspace asked before deleting");
            InventoryKey(Key.Enter);
            Require(!library.Slot("slot0000").Exists && _saveListPanel.RowsForSelfTest.SequenceEqual(["[auto] Auto-Save"])
                && _saveListPanel.NoticeForSelfTest == "deleted", "Return deleted slot0000");
            InventoryKey(Key.Escape);
            Require(!SaveListOpen, "Escape closed the load window");
            return $"new save, overwrite asked (no then yes), keys owned, F7 auto-save details, preview {(framed ? "written and shown" : "skipped headless")}, " +
                "auto-save first and not deletable, load by row, F8 newest, delete asked";
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
