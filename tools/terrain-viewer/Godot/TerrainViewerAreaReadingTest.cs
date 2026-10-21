using System.Reflection;
using Arcanum.Data.Area;
using Arcanum.Data.Characters;
using Arcanum.Data.Messages;
using Arcanum.Data.Written;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.TerrainViewer.Core;
using Arcanum.TerrainViewer.Tests;
using Godot;

namespace Arcanum.TerrainViewer;

public static partial class TerrainViewerSelfTest
{
    /// <summary>ARC-UI-049 over the strict synthetic AREA owner in the production app: with a synthetic hero holding a book and a
    /// schematic document and synthetic texts, the inventory window shows a Read button per written item; reading the document
    /// registers its schematic (message 89, the document gone) and keeps the inventory; reading the book replaces the inventory
    /// with the modal reading window (3008), Left/Right turn two pages (3010) within bounds, the window owns the keys, Escape closes it
    /// (3009); a script's plaque waits while the palette owns input, then opens; a controller cancel closes it; a picture art out of
    /// range is refused; a dead hero cannot read.</summary>
    private static async Task AssertAreaReadingAsync(Node host)
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
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            summary = app.AreaReadingForSelfTest();
        }
        finally
        {
            app.ClearSyntheticForSelfTest();
            viewport.Free();
        }
        GD.Print($"ARC-UI-049 self-test PASS synthetic reading ({summary}); original data covered by the native smoke");
    }
}

public partial class TerrainViewerApp
{
    internal string AreaReadingForSelfTest()
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("AREA reading: " + why); }
        if (!SelfTestBootstrap || _readyArea is null || _areaSession is null) throw new InvalidOperationException("AREA_READING_READY_REQUIRED");
        if (_inventoryHost is null) throw new InvalidOperationException("AREA_READING_INVENTORY_REQUIRED");
        var simulatedFocus = _worldFocusSimulationForSelfTest;
        _worldFocusSimulationForSelfTest = true; _worldFocused = true;
        try
        {
            var session = _areaSession;
            // Synthetic texts (invented, ASCII): a book of ten long messages at 100..109 (several spreads) and a plaque at 500; written
            // prototypes 9001 (a book at 100) and 9002 (a schematic document numbered 2000).
            static MesFile Mes(string text) => MesFile.Parse(System.Text.Encoding.Latin1.GetBytes(text)).File!;
            var book = string.Concat(Enumerable.Range(100, 10).Select(n => $"{{{n}}}{{12 {string.Concat(Enumerable.Repeat($"Line {n} words. ", 30))}}}"));
            var texts = WrittenTexts.Create(Mes(book), null, null, null, Mes("{500}{40c Plaque text}"));
            var data = new AreaReadingData(texts, new Dictionary<int, SessionWrittenFacts>
            {
                [9001] = new((int)WrittenSubtype.Book, 100) { Prototype = 9001 },
                [9002] = new((int)WrittenSubtype.Schematic, 2000) { Prototype = 9002 },
            });
            _readingDataTask = null; _readingData = new(AreaReadingDataStatus.Ready, data, "synthetic");

            // A synthetic hero holding the book and the document in its kit, through the production replacement path (as a quick load does).
            var bookKind = new SessionItemKind(SessionItemType.Written, 9001, 1, 2);
            var documentKind = new SessionItemKind(SessionItemType.Written, 9002, 3, 4);
            var hero = new SessionHeroDefinition(16089, CharacterRace.HalfElf, CharacterGender.Male, [8, 8, 8, 8, 15, 8, 8, 8, 0, 0], 1, 5, 0,
                null, null, 385, [new(bookKind), new(documentKind)]);
            var actor = session.QueryActor();
            var created = AreaTransitionCoordinator.CreateSession(session.QueryClockSnapshot().ProfileBinding, Guid.NewGuid(), Guid.NewGuid(),
                new(actor.Cell.X, actor.Cell.Y), actor.Map.RuntimeMap,
                new AreaHeroLoad(AreaHeroStatus.Ready, AreaStartingHero.Find("half-elf-male")!, hero, 2352, 0, null, "synthetic"));
            var owner = created.Owner ?? throw new InvalidOperationException("AREA reading: session " + created.Failure);
            ReplaceAreaSession(owner, AreaRuntimes(), actor.Map.RuntimeMap, _conversationHost?.Rebind(owner));
            _readingSounds.Clear();
            var bookEntity = owner.QueryHeroKit().Single(k => k.Kind.Prototype == 9001).Entity;
            var documentEntity = owner.QueryHeroKit().Single(k => k.Kind.Prototype == 9002).Entity;

            InventoryKey(Key.I);
            Require(InventoryOpen && _inventoryPanel!.ViewForSelfTest!.Pc.Count(line => line.Written) == 2
                && _inventoryPanel.UseButtonsForSelfTest.Count(b => b.Text.StartsWith("Read: ", StringComparison.Ordinal)) == 2, "two Read buttons");
            var inventory = _inventoryPanel!;
            ReadItem(documentEntity);
            Require(InventoryOpen && !ReadingOpen && owner.QueryHeroFoundSchematics()!.Value.SequenceEqual([2000]) && owner.IsItemDestroyed(documentEntity)
                && inventory.MessageForSelfTest == "[tech 89] schematic 2000", "the document registered 2000 and is gone: " + inventory.MessageForSelfTest);
            ReadItem(documentEntity);
            Require(inventory.MessageForSelfTest == "not read: not a written item the hero holds", "the gone document: " + inventory.MessageForSelfTest);
            Require(_readingSounds.Count == 0, "no reading sound for a schematic document");

            ReadItem(bookEntity);
            var first = _readingPanel?.ViewForSelfTest;
            Require(!InventoryOpen && ReadingOpen && WorldModal && !_worldGate.IsOpen && first is { Subtype: WrittenSubtype.Book, BackgroundArt: 477, Spread: 0, Spreads: > 1 },
                "the book replaced the inventory (modal): " + first);
            Require(_readingSounds.Last() == BookOpenSound, "open sound 3008");
            Require(_readingPanel!.TextForSelfTest.Contains("Line 100 words.", StringComparison.Ordinal), "the first page: " + _readingPanel.TextForSelfTest[..Math.Min(80, _readingPanel.TextForSelfTest.Length)]);
            Require(GetSelfTestSnapshot().Hud.Contains(" | reading: Escape closes", StringComparison.Ordinal), "HUD open status");
            InventoryKey(Key.Right);
            Require(_readingPanel.ViewForSelfTest is { Spread: 1 } && _readingSounds.Last() == BookPageSound, "Right turned two pages (3010)");
            InventoryKey(Key.Left);
            var sounds = _readingSounds.Count;
            InventoryKey(Key.Left);
            Require(_readingPanel.ViewForSelfTest is { Spread: 0 } && _readingSounds.Count == sounds, "no spread before the first, no sound");
            for (var i = 0; i < first!.Spreads + 1; i++) InventoryKey(Key.Pagedown);
            Require(_readingPanel.ViewForSelfTest!.Spread == first.Spreads - 1 && !_readingPanel.ViewForSelfTest.HasNext, "Page Down stops at the last spread");
            InventoryKey(Key.I);
            Require(ReadingOpen && !InventoryOpen, "the open window owns the I key");
            InventoryKey(Key.Escape);
            Require(!ReadingOpen && !WorldModal && _readingSounds.Last() == BookCloseSound, "Escape closed it (3009)");
            Require(GetViewport().GuiGetFocusOwner() is null, "closing releases the GUI focus");
            Require(owner.QueryHeroKit().Any(k => k.Entity == bookEntity), "reading kept the book");

            // Script action 114 (a plaque, PC viewer) waits while the palette owns input, then opens; a controller cancel closes it.
            var pc = new ScrObjectRef(actor.Map.RuntimeMap, AreaScriptHost.PcCode);
            ToggleWorldSettings();
            QueueScriptReadings([new(ScrReadingKind.Plaque, 500, pc)]);
            TakeScriptReadings();
            Require(!ReadingOpen, "the request waits while the palette owns input");
            ToggleWorldSettings();
            TakeScriptReadings();
            Require(ReadingOpen && _readingPanel.ViewForSelfTest is { Subtype: WrittenSubtype.Plaque, BackgroundArt: 633 }
                && _readingPanel.TextForSelfTest.Contains("Plaque text", StringComparison.Ordinal), "the plaque opened: " + _readingPanel.ViewForSelfTest);
            static GamepadLab.Core.IntentFrame Pulse(bool confirm, bool cancel) =>
                new(default, default, confirm, cancel, false, false, false, false, false, 0, 0, GamepadLab.Core.InputMethod.Gamepad, true, "ARC-UI-049 self-test");
            HandleWorldPulse(Pulse(confirm: false, cancel: true), true);
            Require(!ReadingOpen && !PaletteOpen, "controller cancel closed the window without opening the palette");

            // Action 65 with a picture art outside 0..4095: refused in both modes (the original draws out of bounds).
            QueueScriptReadings([new(ScrReadingKind.Picture, 5000, pc)]);
            TakeScriptReadings();
            Require(!ReadingOpen && _readingStatus == "not read: a picture art outside 0..4095", "picture out of range: " + _readingStatus);

            // A dead hero cannot read (the original's start returns for a dead reader).
            Require(owner.Kill(owner.Version, SessionLifeTarget.Hero).Disposition == GameSessionDisposition.Changed, "kill the hero");
            OpenInventory();
            ReadItem(bookEntity);
            Require(!ReadingOpen && inventory.MessageForSelfTest == "not read: no hero or the hero is dead", "a dead hero: " + inventory.MessageForSelfTest);
            EndInventory();
            return "Read buttons, schematic document registered (89, gone), book replaces the inventory (3008, modal), page turns with 3010 and bounds, " +
                "keys owned, Escape (3009), book kept, script plaque waits for the palette then opens, controller cancel, picture art refused, dead hero refused";
        }
        finally
        {
            if (ReadingOpen) EndReading();
            if (InventoryOpen) EndInventory();
            if (PaletteOpen) ToggleWorldSettings();
            _worldFocusSimulationForSelfTest = simulatedFocus;
        }
    }
}
