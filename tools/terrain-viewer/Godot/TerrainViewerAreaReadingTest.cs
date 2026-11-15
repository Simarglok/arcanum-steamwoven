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
            // ARC-UI-1428: a header line of a font under 9 px at the screen factor (Garmond8, art 482) takes the text-only factor (8 x 2 = 16 px), Zurich20 stays 20,
            // an unknown font takes 16, and the panel's other texts carry explicit sizes.
            _readingPanel!.Render(new AreaReadingView(WrittenSubtype.Newspaper, 487, ["Masthead", "Small line", "Other"], "", "", 0, 1, null, null, [485, 482, 999]), "synthetic");
            var readingFactor = AreaGameBarPanel.ScreenFactor(_readingPanel.GetViewport().GetVisibleRect().Size);
            Require(_readingPanel.HeaderForSelfTest == "Masthead  ·  Small line  ·  Other"
                && _readingPanel.HeaderSizesForSelfTest.SequenceEqual([AreaReadingText.HeaderLinePx(485, readingFactor), AreaReadingText.HeaderLinePx(482, readingFactor), AreaReadingText.HeaderLinePx(999, readingFactor)])
                && (readingFactor != 1 || _readingPanel.HeaderSizesForSelfTest.SequenceEqual([20, 16, 16])), "masthead sizes: " + string.Join(",", _readingPanel.HeaderSizesForSelfTest));

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

            // ARC-UI-1125 (SA40): a script's plaque replaces the open inventory at once (its opener pops the top mode), the inventory leaving through its End
            // routine; a second request closes the window (the opener toggles, WR2).
            InventoryKey(Key.I);
            Require(InventoryOpen && !ReadingOpen, "the inventory is open for the replacement");
            QueueScriptReadings([new(ScrReadingKind.Plaque, 500, pc)]);
            TakeScriptReadings();
            Require(ReadingOpen && !InventoryOpen && WindowPanelsMatchStack && StackText() == "Game>Book" && _readingScriptOpen is null, "the plaque replaced the inventory: " + StackText());
            QueueScriptReadings([new(ScrReadingKind.Plaque, 500, pc)]);
            TakeScriptReadings();
            Require(!ReadingOpen && WindowPanelsMatchStack && _readingScriptOpen is null && _readingSounds.Last() == BookCloseSound, "a second request closed the reading (the opener toggles)");
            // The dialog opener pops the top mode too (0x567460 with 0x553320): the hero's talk goal opens its dialog over an open window; the click path keeps refusing.
            var standInRow = _readyArea.Snapshot.Objects[0];
            _selfTestStandInSpeaker = new AreaSpeaker(0, standInRow, 1, standInRow.WorldX ?? 0, standInRow.WorldY ?? 0);
            InventoryKey(Key.I);
            BeginConversation(_selfTestStandInSpeaker);
            Require(InventoryOpen && !ConversationOpen, "a plain dialog start leaves the open inventory alone");
            BeginConversation(_selfTestStandInSpeaker, replaceWindows: true);
            Require(ConversationOpen && !InventoryOpen && WindowPanelsMatchStack && StackText() == "Game>Dialog", "the talk goal's dialog replaced the inventory: " + StackText());
            BeginConversation(_selfTestStandInSpeaker, replaceWindows: true);
            Require(ConversationOpen && WindowPanelsMatchStack, "a dialog opened over a dialog is refused");
            _conversationPanel!.LeaveForSelfTest.EmitSignal(BaseButton.SignalName.Pressed);
            Require(!ConversationOpen && !WorldModal, "the dialog closed");
            // ARC-UI-1467: the reading opener pops the dialog too (0x56BC00 pops the top mode, whose leave case 0x5678D0 ends the dialog; nothing resumes): a script's
            // plaque asked for while a dialog is open ends the dialog and opens at once; the dialog opener keeps refusing over a dialog.
            BeginConversation(_selfTestStandInSpeaker, replaceWindows: true);
            Require(ConversationOpen && !ReadingOpen, "a dialog is open for the plaque");
            QueueScriptReadings([new(ScrReadingKind.Plaque, 500, pc)]);
            TakeScriptReadings();
            Require(ReadingOpen && !ConversationOpen && WindowPanelsMatchStack && StackText() == "Game>Book" && _readingScriptOpen is null,
                "the plaque ended the open dialog and opened in its place: " + StackText());
            CloseReadingByPlayer();
            Require(!ReadingOpen && !ConversationOpen && !WorldModal, "the plaque closed and the dialog did not come back");
            _selfTestStandInSpeaker = null;

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
                "keys owned, Escape (3009), book kept, script plaque waits for the palette then opens (and ends an open dialog, #1467), controller cancel, picture art refused, dead hero refused";
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
            // ARC-UI-1877: the window's art and fonts read, and every readable prototype's document composes in the original's look: its text lines stay inside the window and the book's
            // pages inside the bound (counts only, no text of the original data).
            for (var i = 0; i < 1800 && AdoptReadingArt() is null; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var artContent = AdoptReadingArt();
            Require(artContent is { Ready: true }, "the window's art and fonts read: " + artContent?.Detail);
            var composition = ReadingCompositionCheck(session, data, readable.Select(p => p.Value).ToArray(), artContent!);
            Require(composition.Outside == 0 && composition.Composed == readable.Length, $"the documents compose inside the window: {composition}");
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
            // ARC-UI-1877: with its art read the window opens in the original's look; the book turns its own spreads (by pixel height).
            Require(_readingPanel!.ArtMode && _readingPanel.ArtViewForSelfTest.Visible && _readingPanel.ArtViewForSelfTest.LinesForSelfTest.Count > 0 == (used.Document!.Body.Count > 0),
                "the window opens in the original's look");
            var shownLook = _readingPanel.ArtViewForSelfTest;
            // ARC-UI-1462: the masthead's Garmond8 and the telegram form's Garmond6 are drawn with Garmond9 at screen factor 1 (under 9 px), and untouched at 2 and up.
            var textFactor = AreaGameBarPanel.ScreenFactor(_readingPanel.GetViewport().GetVisibleRect().Size);
            Require(shownLook.ShownFontNumber(482) == (textFactor == 1 ? AreaReadingText.SubstituteFont : 482) && shownLook.ShownFontNumber(481) == (textFactor == 1 ? AreaReadingText.SubstituteFont : 481)
                && shownLook.ShownFontNumber(483) == 483 && shownLook.ShownFontNumber(480) == 480 && shownLook.ShownFontNumber(485) == 485, $"the small fonts at screen factor {textFactor}: {shownLook.ShownFontNumber(482)}");
            var artSpreads = shownLook.Spreads;
            if (shownLook.HasNext) { InventoryKey(Key.Right); Require(shownLook.SpreadIndex == 1 && _readingSounds.Last() == BookPageSound, "Right turned a spread (3010)"); InventoryKey(Key.Left); Require(shownLook.SpreadIndex == 0, "Left turned back"); }
            else Require(view!.Subtype != WrittenSubtype.Book || artSpreads == 1, "a book with no next button has one spread");
            InventoryKey(Key.Escape);
            Require(ReadingOpen, "Escape does not close the reading window");
            Require(MainMenuOpen, "Escape opened the menu"); InventoryKey(Key.Escape); Require(ReadingOpen && !MainMenuOpen, "Escape continued play: the window is still open");
            CloseReadingByPlayer();
            Require(!ReadingOpen && _readingSounds.Last() == BookCloseSound, "the player's close closed it (3009)");
            Require(session.Version.Revision == revision, "the live session is untouched");
            // ARC-UI-1877: the opt-in captures open and close their own windows, so they run after the checks above.
            await ReadingLookCaptureAsync(session, data, readable.Select(p => p.Value).ToArray());
            await ReadingWindowArtSmokeAsync(session, data, readable.Select(p => p.Value).ToArray());
            GD.Print("ARC-UI-049 native-smoke reading PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Data = _readingData.Detail, Readable = readable.Length, Views = views, WithUseScript = scripted, Item = chosen.Key, Subtype = view!.Subtype.ToString(),
                Spreads = view.Spreads, ArtSpreads = artSpreads, Look = _readingPanel!.ArtMode ? "original" : "diagnostic", Composed = composition.Composed, Lines = composition.Lines, Pages = composition.Pages,
                Body = used.Document!.Body.Count, Schematic = schematic.Key, Registered = registered.Schematic }));
        }
        finally
        {
            if (ReadingOpen) EndReading();
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
        }
    }
    /// <summary>ARC-UI-1877: the counts of the original look's composition of every readable written prototype of the game (documents composed, text lines and book pages) and how many
    /// lines or pages fell outside the window or its bounds.</summary>
    private readonly record struct ReadingComposition(int Composed, int Lines, int Pages, int Outside);

    private ReadingComposition ReadingCompositionCheck(Arcanum.Session.GameSessionOwner session, AreaReadingData data, IReadOnlyList<Arcanum.Session.SessionWrittenFacts> facts, AreaReadingArtContent content)
    {
        var fonts = content.Fonts.Values.Where(load => load.Status == AreaInterfaceArtStatus.Ready && load.Frames.Count > 0).ToDictionary(load => load.Number, load => new AreaArtFont(load.Frames));
        // ARC-UI-1462: the fonts the window draws with at its screen factor (a font under 9 px is Garmond9 when that read).
        var factor = AreaGameBarPanel.ScreenFactor(GetViewport().GetVisibleRect().Size);
        AreaArtFont FontOf(int number) => fonts.TryGetValue(AreaReadingText.ShownFont(number, factor), out var font) ? font : fonts.TryGetValue(number, out var own) ? own : fonts[AreaReadingArtLayout.NoteFont];
        int composed = 0, lines = 0, pages = 0, outside = 0;
        foreach (var fact in facts)
        {
            var document = session.QueryWrittenDocument(fact, data.Texts);
            if (AreaReading.View(document, data.Texts, 0, AreaHoverIndex.TextProfile, session.Rules.Written) is not { Art: { } art }) continue;
            IReadOnlyList<AreaReadingLine> placed;
            if (art.Subtype == WrittenSubtype.Book)
            {
                var book = AreaReadingFlow.BookPages(art.Body, FontOf);
                pages += book.Count;
                placed = book.SelectMany(page => page.Lines).ToArray();
            }
            else placed = AreaReadingFlow.Single(art, FontOf);
            composed++; lines += placed.Count;
            // A document with text and no line drawn would be a wrong flow (a font without height, a rectangle too small): counted with the lines outside the window.
            var hasText = art.Subtype != WrittenSubtype.Picture && (art.Body.Any(message => message.Text.Length > 0) || art.Joined.Length > 0);
            if (hasText && placed.Count == 0) outside++;
            foreach (var line in placed)
            {
                var font = FontOf(line.Font);
                var right = line.X + (line.Glyphs.Count == 0 ? 0 : line.Glyphs[^1].X + line.Glyphs[^1].Glyph.Width);
                var window = AreaReadingArtLayout.Window;
                if (line.X < 0 || line.Y < 0 || line.Y + font.LineHeight > window.Height || right > window.Width) outside++;
            }
        }
        return new(composed, lines, pages, outside);
    }

    /// <summary>ARC-UI-1877: with ARC_AREA_READING_CAPTURE=PREFIX the viewport after a drawn frame is saved as PREFIX-KIND-original.png and PREFIX-KIND-diagnostic.png for one document of
    /// every kind (book, note, newspaper, telegram, plaque; local, never in git) and, ARC-UI-1462, PREFIX-newspaper-issue-original.png for a printed issue (start line 1300); the open window is put back at the end.</summary>
    private async Task ReadingLookCaptureAsync(Arcanum.Session.GameSessionOwner session, AreaReadingData data, IReadOnlyList<Arcanum.Session.SessionWrittenFacts> facts)
    {
        if (System.Environment.GetEnvironmentVariable("ARC_AREA_READING_CAPTURE") is not { Length: > 0 } capture) return;
        async Task Shot(string name)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var path = System.IO.Path.GetFullPath(capture) + "-" + name + ".png";
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            using var image = GetViewport().GetTexture().GetImage();
            if (image is null || image.SavePng(path) != Error.Ok) throw new InvalidOperationException("ARC-UI-1877 reading capture not saved: " + path);
            GD.Print($"ARC-UI-1877 reading capture {name} {image.GetWidth()}x{image.GetHeight()}");
        }
        if (ReadingOpen) CloseReadingByPlayer();
        foreach (var kind in ReadingKinds)
        {
            if (ReadingFactFor(session, data, facts, kind) is not { } fact) { GD.Print($"ARC-UI-1877 reading capture {kind}: no document"); continue; }
            if (kind == WrittenSubtype.Newspaper
                && AreaReading.View(session.QueryWrittenDocument(fact, data.Texts), data.Texts, 0, AreaHoverIndex.TextProfile, session.Rules.Written) is { Art: { } paper })
            {
                var fonts = _readingArt!.Fonts.Values.Where(load => load.Status == AreaInterfaceArtStatus.Ready && load.Frames.Count > 0).ToDictionary(load => load.Number, load => new AreaArtFont(load.Frames));
                AreaArtFont FontOf(int number) => fonts.TryGetValue(number, out var font) ? font : fonts[AreaReadingArtLayout.NoteFont];
                GD.Print($"ARC-UI-1877 newspaper capture start={fact.TextStartLine} body={paper.Body.Count} fillers={paper.Fillers.Count}: "
                    + string.Join(" | ", paper.Body.Select((m, i) => $"#{i} font={m.Font} len={m.Text.Length} h={AreaReadingFlow.Height(FontOf(m.Font), m.Text, 100)}")));
                foreach (var line in AreaReadingFlow.Newspaper(paper, FontOf))
                    GD.Print($"ARC-UI-1877 newspaper line font={line.Font} x={line.X} y={line.Y} glyphs={line.Glyphs.Count}");
            }
            foreach (var look in new[] { "original", "diagnostic" })
            {
                if (ReadingOpen) CloseReadingByPlayer();
                _readingPanel!.DiagnosticLook = look == "diagnostic";
                OpenReading(session.QueryWrittenDocument(fact, data.Texts), "capture", replaceWindows: true);
                await Shot($"{kind.ToString().ToLowerInvariant()}-{look}");
            }
        }
        // ARC-UI-1462: a printed issue (the template newspaper at line 0 shows the masthead messages as its body), the one the masthead and the columns are judged on.
        if (session.QueryWrittenDocument(new((int)WrittenSubtype.Newspaper, 1300), data.Texts) is { Readable: true, Body.Count: > 0 } issue)
        {
            if (ReadingOpen) CloseReadingByPlayer();
            _readingPanel!.DiagnosticLook = false;
            OpenReading(issue, "capture", replaceWindows: true);
            await Shot("newspaper-issue-original");
        }
        if (ReadingOpen) CloseReadingByPlayer();
        _readingPanel!.DiagnosticLook = false;
    }

    private static readonly WrittenSubtype[] ReadingKinds = [WrittenSubtype.Book, WrittenSubtype.Note, WrittenSubtype.Newspaper, WrittenSubtype.Telegram, WrittenSubtype.Plaque];

    /// <summary>ARC-UI-1877: a prototype's document of this kind that has text first; the plaque has none (a script opens it), so the first message number that reads; null when there is none.</summary>
    private static Arcanum.Session.SessionWrittenFacts? ReadingFactFor(Arcanum.Session.GameSessionOwner session, AreaReadingData data, IReadOnlyList<Arcanum.Session.SessionWrittenFacts> facts, WrittenSubtype kind)
    {
        var found = facts.Where(candidate => candidate.Subtype == (int)kind && session.QueryWrittenDocument(candidate, data.Texts) is { Readable: true, Body.Count: > 0 })
            .Cast<Arcanum.Session.SessionWrittenFacts?>().FirstOrDefault();
        if (found is null && kind == WrittenSubtype.Plaque)
            for (var number = 0; number < 4000 && found is null; number++)
                if (session.QueryWrittenDocument(new((int)kind, number), data.Texts) is { Readable: true, Body.Count: > 0 }) found = new((int)kind, number);
        return found;
    }

    // ARC-UI-1201: the reading window's drawn frames in the generated interface (ARC_AREA_WINDOW_ART=legacy|hd|generated, with ARC_HD_PACK for the last two), once for every kind of document: the side
    // bar (494), the lens (231), the kind's background (477, 489, 487, 491 or 633; 817 for the alternate newspaper) and a book's shown page button (495, 496). In `generated` every one is generated chrome at
    // round(size x paint scale) pixels of the window's own device scale, again after a bigger window (1600 x 1200) and back; in `hd` and `legacy` none is. The text, the picture arts and the fonts stay
    // pictures. ARC_AREA_WINDOW_CAPTURE=PREFIX saves the viewport as PREFIX-reading-KIND-MODE[-big|-back].png (local, never in git).
    private async Task ReadingWindowArtSmokeAsync(Arcanum.Session.GameSessionOwner session, AreaReadingData data, IReadOnlyList<Arcanum.Session.SessionWrittenFacts> facts)
    {
        if (!WindowArtSmokeRequested) return;
        if (_readingArtLook is not { } look) throw new InvalidOperationException("ARC-UI-1201 reading window art smoke: the window's art is not read");
        foreach (var kind in ReadingKinds)
        {
            if (ReadingFactFor(session, data, facts, kind) is not { } fact) { GD.Print($"ARC-UI-1201 reading window art smoke {kind}: no document"); continue; }
            if (ReadingOpen) CloseReadingByPlayer();
            var document = session.QueryWrittenDocument(fact, data.Texts);
            OpenReading(document, "window-art", replaceWindows: true);
            var panel = _readingPanel!;
            var shown = panel.ArtViewForSelfTest;
            var arts = new List<int> { AreaReadingArtLayout.FrameArt, AreaReadingArtLayout.LensArt, document.BackgroundArt };
            if (kind == WrittenSubtype.Book && shown.HasNext) arts.Add(AreaReadingArtLayout.NextArt);
            if (kind == WrittenSubtype.Book && shown.SpreadIndex > 0) arts.Add(AreaReadingArtLayout.PreviousArt);
            if (!panel.ArtMode || !shown.Visible) throw new InvalidOperationException($"ARC-UI-1201 reading window art smoke {kind}: the window is not in the original's look");
            if (arts.Any(art => !look.Frames.ContainsKey(art))) throw new InvalidOperationException($"ARC-UI-1201 reading window art smoke {kind}: arts {string.Join(",", arts.Where(art => !look.Frames.ContainsKey(art)))} were not read");
            await WindowArtSmokeAsync("reading-" + kind.ToString().ToLowerInvariant(), () => arts.Select(art => look.Frames[art][0] as HdInterfaceTexture
                ?? throw new InvalidOperationException($"ARC-UI-1201 reading window art smoke: art {art} is no interface texture")).ToArray(),
                arts, shown.DeviceScale, () => panel.ArtMode && shown.Visible, new Vector2I(1600, 1200));
        }
        if (ReadingOpen) CloseReadingByPlayer();
    }
}
