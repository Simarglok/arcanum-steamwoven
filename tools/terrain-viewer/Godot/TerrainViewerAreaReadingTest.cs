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
using Arcanum.World;
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
            Require(GetSelfTestSnapshot().Hud.Contains(" | reading open", StringComparison.Ordinal), "HUD open status");
            InventoryKey(Key.Right);
            Require(_readingPanel.ViewForSelfTest is { Spread: 1 } && _readingSounds.Last() == BookPageSound, "Right turned two pages (3010)");
            InventoryKey(Key.Left);
            var sounds = _readingSounds.Count;
            InventoryKey(Key.Left);
            Require(_readingPanel.ViewForSelfTest is { Spread: 0 } && _readingSounds.Count == sounds, "no spread before the first, no sound");
            for (var i = 0; i < first!.Spreads + 1; i++) InventoryKey(Key.Pagedown);
            Require(_readingPanel.ViewForSelfTest!.Spread == first.Spreads - 1 && !_readingPanel.ViewForSelfTest.HasNext, "Page Down stops at the last spread");
            // ARC-DEV-111 (SA51): Escape opens the menu over the window, which stays; Continue returns to it, and the player's close shuts it.
            InventoryKey(Key.Escape);
            Require(ReadingOpen, "Escape does not close the reading window");
            Require(MainMenuOpen, "Escape opened the menu"); InventoryKey(Key.Escape); Require(ReadingOpen && !MainMenuOpen, "Escape continued play: the window is still open");
            CloseReadingByPlayer();
            Require(!ReadingOpen && !WorldModal && _readingSounds.Last() == BookCloseSound, "the player's close closed it (3009)");
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
            // ARC-DEV-111 (SA52): the inventory opener itself refuses a dead hero, so the Read button is not reachable; the window never opens.
            OpenInventory();
            Require(!InventoryOpen && !ReadingOpen, "a dead hero: the inventory opener refused (stack " + StackText() + ")");
            ReadItem(bookEntity);
            Require(!ReadingOpen, "a dead hero: nothing reads");
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

public partial class TerrainViewerApp
{
    /// <summary>
    /// ARC-UI-049 native observation on the original data: the module's written texts and written prototypes load through the content
    /// stack; every readable written prototype's document builds a view; on a detached session whose synthetic hero holds the first
    /// readable written prototype without a use script (one with text, a book first) and the first schematic document with a number (a template numbered 0
    /// is refused under TC8 Steamwoven), the session uses both through the
    /// inventory host's facts: the document registers (and is destroyed), the readable item gives its document; that document opens in
    /// the production window over the live app (3008), a book turns a spread (3010), the window owns the keys and Escape closes it
    /// (3009); the live session is untouched. Prints counts and outcomes only, no text of the original data.
    /// </summary>
    private async Task RunAreaReadingSmokeAsync()
    {
        static void Require(bool value, string why) { if (value) return; GD.Print("ARC-UI-049 native-smoke reading FAIL " + why); throw new InvalidOperationException("ARC-UI-049 reading smoke: " + why); }
        if (_areaSession is not { } session || _inventoryHost is not { } inventory) { GD.Print("ARC-UI-049 native-smoke reading SKIP (no area session or inventory)"); return; }
        for (var i = 0; i < 600 && _readingData is null; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); AdoptReadingData(); }
        Require(_readingData is { Status: AreaReadingDataStatus.Ready, Data: not null }, "data " + _readingData?.Status + " " + _readingData?.Detail);
        var data = _readingData!.Data!;
        Require(data.Texts.Refusals.Count == 0, "text files refused: " + string.Join(",", data.Texts.Refusals.Select(r => $"{r.Path}:{r.Status}")));
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused;
        _worldFocusSimulationForSelfTest = true; _worldFocused = true;
        try
        {
            var revision = session.Version.Revision;
            var readable = data.Prototypes.OrderBy(p => p.Key)
                .Where(p => session.QueryWrittenDocument(p.Value, data.Texts) is { Readable: true }).ToArray();
            var views = readable.Count(p => AreaReading.View(session.QueryWrittenDocument(p.Value, data.Texts), data.Texts, 0, AreaHoverIndex.TextProfile, session.Rules.Written) is not null);
            Require(readable.Length > 0 && views == readable.Length, $"readable {readable.Length}, views {views}");
            // A use script or the item-spell path is refused here (not run): the first readable item without either, a book when there is one.
            var plain = readable.Where(p => !p.Value.UseScript && !p.Value.ItemSpell).ToArray();
            Require(plain.Length > 0, "no readable written prototype without a use script");
            // A document with text first (template prototypes at start line 0 read empty), a book before the other kinds.
            var chosen = plain.OrderBy(p => session.QueryWrittenDocument(p.Value, data.Texts).Body.Count == 0)
                .ThenBy(p => p.Value.Subtype != (int)WrittenSubtype.Book).First();
            var scripted = readable.Length - plain.Length;
            var schematic = data.Prototypes.OrderBy(p => p.Key).First(p => p.Value.Subtype == (int)WrittenSubtype.Schematic && p.Value.TextStartLine > 0);

            // The detached hero's kit: the readable item and the schematic document (kit items use their prototype's facts).
            var actor = session.QueryActor();
            var definition = new SessionHeroDefinition(16089, CharacterRace.HalfElf, CharacterGender.Male, [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 5, 0,
                null, null, 0, [new(new SessionItemKind(SessionItemType.Written, chosen.Key, 0, 0)), new(new SessionItemKind(SessionItemType.Written, schematic.Key, 0, 0))]);
            var created = AreaTransitionCoordinator.CreateSession(session.QueryClockSnapshot().ProfileBinding, Guid.NewGuid(), Guid.NewGuid(),
                new(actor.Cell.X, actor.Cell.Y), actor.Map.RuntimeMap,
                new AreaHeroLoad(AreaHeroStatus.Ready, AreaStartingHero.Find("half-elf-male")!, definition, 2352, 0, null, "synthetic"));
            using var copy = created.Owner ?? throw new InvalidOperationException("ARC-UI-049 reading smoke: session " + created.Failure);
            var itemEntity = copy.QueryHeroKit()[0].Entity; var documentEntity = copy.QueryHeroKit()[1].Entity;
            var registered = copy.UseWrittenItem(copy.Version, documentEntity, inventory.WrittenFacts(copy, documentEntity, data)!.Value, data.Texts);
            Require(registered is { Disposition: GameSessionDisposition.Changed, Reading: Arcanum.Simulation.Technology.SchematicReading.Registered, DocumentDestroyed: true }
                && copy.QueryHeroFoundSchematics()!.Value.SequenceEqual([schematic.Value.TextStartLine]), $"the schematic document: {registered.Disposition} {registered.Failure} {registered.Reading}");
            var used = copy.UseWrittenItem(copy.Version, itemEntity, inventory.WrittenFacts(copy, itemEntity, data)!.Value, data.Texts);
            Require(used is { Disposition: GameSessionDisposition.NoChange, Document.Readable: true }, $"the readable item: {used.Disposition} {used.Failure} {used.Document?.Status}");

            _readingSounds.Clear();
            OpenReading(used.Document!, "smoke");
            var view = _readingPanel?.ViewForSelfTest;
            Require(ReadingOpen && WorldModal && WindowPanelsMatchStack && view is not null && _readingSounds.Last() == BookOpenSound, "the window opened (3008)");
            if (view!.HasNext) { InventoryKey(Key.Right); Require(_readingPanel!.ViewForSelfTest!.Spread == 1 && _readingSounds.Last() == BookPageSound, "Right turned a spread (3010)"); }
            InventoryKey(Key.Escape);
            Require(ReadingOpen, "Escape does not close the reading window");
            Require(MainMenuOpen, "Escape opened the menu"); InventoryKey(Key.Escape); Require(ReadingOpen && !MainMenuOpen, "Escape continued play: the window is still open");
            CloseReadingByPlayer();
            Require(!ReadingOpen && _readingSounds.Last() == BookCloseSound, "the player's close closed it (3009)");
            Require(session.Version.Revision == revision, "the live session is untouched");
            GD.Print("ARC-UI-049 native-smoke reading PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Data = _readingData.Detail, Readable = readable.Length, Views = views, WithUseScript = scripted, Item = chosen.Key, Subtype = view.Subtype.ToString(),
                Spreads = view.Spreads, Body = used.Document!.Body.Count, Schematic = schematic.Key, Registered = registered.Schematic }));
        }
        finally
        {
            if (ReadingOpen) EndReading();
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
        }
    }
}
