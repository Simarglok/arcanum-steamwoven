using System.Reflection;
using Arcanum.Data.Area;
using Arcanum.Data.Characters;
using Arcanum.Data.Messages;
using Arcanum.Input;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Clock;
using Arcanum.TerrainViewer.Core;
using Arcanum.TerrainViewer.Tests;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public static partial class TerrainViewerSelfTest
{
    /// <summary>ARC-UI-008 over the strict synthetic AREA owner in the production app: L opens the logbook without a hero,
    /// lists the session's rumors and quests in time order with synthetic texts and the original's styles, owns the keys,
    /// switches tabs with Tab and the controller, keeps the tab across close and open, follows the session, and a dead hero
    /// closes and refuses it.</summary>
    private static async Task AssertAreaLogbookAsync(Node host)
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
            summary = app.AreaLogbookForSelfTest();
        }
        finally
        {
            app.ClearSyntheticForSelfTest();
            viewport.Free();
        }
        GD.Print($"ARC-UI-008 self-test PASS synthetic logbook ({summary}); original texts covered by the native smoke");
    }
}

public partial class TerrainViewerApp
{
    /// <summary>ARC-INPUT-004 through the production key route: each window letter (L, C, I, T, R and W) does nothing with Ctrl,
    /// Alt, Command or Ctrl+Shift held (no window, status, notice or reserved report), and acts with Shift as bare; an open
    /// window's letter does not close it under Ctrl. W is routed on a named map, so the check names one for its silent half.</summary>
    private void AssertWindowKeyModifiers(Action<bool, string> require)
    {
        void Press(Key key, WorldKeyModifiers modifiers)
        {
            foreach (var pressed in new[] { true, false })
                InputWorld(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed, CtrlPressed = modifiers.HasFlag(WorldKeyModifiers.Ctrl),
                    ShiftPressed = modifiers.HasFlag(WorldKeyModifiers.Shift), AltPressed = modifiers.HasFlag(WorldKeyModifiers.Alt), MetaPressed = modifiers.HasFlag(WorldKeyModifiers.Meta) });
        }
        string Windows() => string.Join("|", LogbookOpen, SheetOpen, InventoryOpen, SchematicsOpen, WorldMapOpen, WorldModal, HeroInCombatMode,
            _logbookStatus, _sheetStatus, _inventoryStatus, _schematicsStatus, _worldMapStatus, _combatModeNotice, _worldLastReserved);
        var silent = new[] { WorldKeyModifiers.Ctrl, WorldKeyModifiers.Alt, WorldKeyModifiers.Meta, WorldKeyModifiers.Ctrl | WorldKeyModifiers.Shift };
        var mapName = _readyMapName;
        _readyMapName ??= "synthetic-map";
        try
        {
            require(LogbookActive && SheetActive && SchematicsActive && CombatModeActive && WorldMapActive, "the window keys are routed");
            foreach (var key in new[] { Key.L, Key.C, Key.I, Key.T, Key.R, Key.W })
                foreach (var modifiers in silent)
                {
                    var before = Windows();
                    Press(key, modifiers);
                    require(Windows() == before, $"{modifiers}+{key} did something: {before} -> {Windows()}");
                }
        }
        finally { _readyMapName = mapName; }
        // Shift gives the capital, the same action as the bare letter (the original's character table has both cases).
        foreach (var key in new[] { Key.C, Key.T, Key.R })
        {
            var before = Windows();
            Press(key, WorldKeyModifiers.Shift);
            require(Windows() != before && !WorldModal, $"Shift+{key} acted as {key} (no hero: a refusal status)");
        }
        Press(Key.L, WorldKeyModifiers.Shift);
        require(LogbookOpen && WorldModal, "Shift+L opened the logbook");
        Press(Key.L, WorldKeyModifiers.Ctrl);
        require(LogbookOpen, "Ctrl+L did not close the open logbook");
        Press(Key.L, WorldKeyModifiers.Shift);
        require(!LogbookOpen && !WorldModal, "Shift+L closed the logbook");
    }

    /// <summary>A synthetic art set for the book (every art number of the layout, solid colours; a frame or three for the buttons) and a
    /// synthetic font of 6-pixel cells, which the synthetic module's missing game folder cannot give.</summary>
    private static (IReadOnlyDictionary<int, Texture2D[]> Art, AreaTextFonts Fonts) SyntheticLogbookArt()
    {
        static Texture2D Solid(int width, int height, Color colour)
        {
            var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
            image.Fill(colour);
            return ImageTexture.CreateFromImage(image);
        }
        var art = new Dictionary<int, Texture2D[]>
        {
            [AreaLogbookArtLayout.SideArt] = [Solid(165, 400, new(.2f, .1f, .1f))],
            [AreaLogbookArtLayout.BookArt] = [Solid(635, 400, new(.8f, .7f, .5f))],
            [AreaLogbookArtLayout.PageArt] = [Solid(561, 355, new(.9f, .85f, .7f))],
            [AreaLogbookArtLayout.PreviousArt] = Enumerable.Range(0, 3).Select(i => Solid(30, 30, new(.1f * i, .5f, .5f))).ToArray(),
            [AreaLogbookArtLayout.NextArt] = Enumerable.Range(0, 3).Select(i => Solid(30, 30, new(.1f * i, .5f, .5f))).ToArray(),
            [AreaLogbookArtLayout.LensArt] = [Solid(89, 89, new(.6f, .6f, .2f, .5f))],
        };
        foreach (var (number, _, _) in AreaLogbookArtLayout.Tabs)
            art[number] = Enumerable.Range(0, 3).Select(i => Solid(30, 40, new(.1f * i, .4f, .4f))).ToArray();
        var cell = new AreaInterfaceFrame(5, 8, Enumerable.Repeat((byte)255, 5 * 8 * 4).ToArray(), 6);
        var frames = Enumerable.Repeat(cell, 225).ToArray();
        return (art, new AreaTextFonts(new AreaArtFontFace(frames), new AreaArtFontFace(frames)));
    }

    /// <summary>ARC-UI-098 (ledger LB23): a hovered tab asks the message window for its hint and leaving takes it away; the lens shows the round view
    /// of the hero under the side art (which leaves the lens's square out); a left release in the lens closes the window, a press does not, and a
    /// diagnostic list has no lens. Leaves the book open in the original look, as it found it.</summary>
    private void AssertLogbookLensAndHint(AreaLogbookArtView book, Func<Vector2, bool, InputEventMouseButton> click)
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("AREA logbook lens: " + why); }
        static InputEventMouseMotion Move(Vector2 at) => new() { Position = at };
        var hints = new List<string?>(); _logbookPanel!.HintRequested += hints.Add;
        try
        {
            var quests = new Vector2(AreaLogbookArtLayout.Tabs[1].X + 2, AreaLogbookArtLayout.Tabs[1].Y + 2);
            var rumors = new Vector2(AreaLogbookArtLayout.Tabs[0].X + 2, AreaLogbookArtLayout.Tabs[0].Y + 2);
            // Hover-in asks for the tab's message (the project's name here: the synthetic texts have no logbk_ui.mes); nothing new while it stays on the tab.
            book._GuiInput(Move(quests)); book._GuiInput(Move(quests + new Vector2(1, 1)));
            Require(hints.SequenceEqual([AreaLogbook.TabName(AreaLogbookTab.Quests)]), "the quests tab's hint once: " + string.Join("|", hints));
            Require(_messageLog.Shown is { Kind: AreaMessageKind.Hover } shown && shown.Title == AreaLogbook.TabName(AreaLogbookTab.Quests), "the hint is in the message window");
            // Another tab: its own hint; the page area: none, and the message window is clear.
            book._GuiInput(Move(rumors));
            Require(hints.Count == 2 && hints[1] == AreaLogbook.TabName(AreaLogbookTab.Rumors), "the rumors tab's hint");
            book._GuiInput(Move(new Vector2(300, 200)));
            Require(hints.Count == 3 && hints[2] is null && _messageLog.Shown is not { Kind: AreaMessageKind.Hover }, "leaving the tab clears the hint");
            book._GuiInput(Move(quests)); book._Notification((int)Control.NotificationMouseExit);
            Require(hints.Count == 5 && hints[4] is null, "the pointer leaving the window clears the hint");

            // The lens: with a holder the round view shows and the side art leaves its square out; the art is drawn over it.
            _logbookPanel.HolderView = () => (new Vector2(400, 300), 1f);
            _logbookStamp = null; SyncAreaLogbook();
            var (origin, side) = book.LensOnScreen;
            Require(_logbookPanel.RoundViewShown && book.LensCut && side == 89 * book.Scale.X && origin == book.GlobalPosition + new Vector2(25, 24) * book.Scale.X,
                $"the lens shows the round view: shown={_logbookPanel.RoundViewShown} cut={book.LensCut} origin={origin} side={side}");
            // A press in the lens does nothing; the release closes the window (a left release is the first test of the callback, LB14).
            var inside = new Vector2(AreaLogbookArtLayout.Lens.X + 40, AreaLogbookArtLayout.Lens.Y + 40);
            book._GuiInput(click(inside, true));
            Require(LogbookOpen, "a press in the lens does not close the window");
            book._GuiInput(click(inside, false));
            Require(!LogbookOpen && !_logbookPanel.RoundViewShown && !book.LensCut, "a release in the lens closed the window and its round view");

            // Just outside the square (right edge, bottom edge) a release does not close it; the lens needs a holder to show.
            InventoryKey(Key.L);
            Require(LogbookOpen && _logbookPanel.RoundViewShown, "the lens shows again when the book reopens");
            var (right, below) = (new Vector2(AreaLogbookArtLayout.Lens.X + 89 + 1, AreaLogbookArtLayout.Lens.Y + 40), new Vector2(AreaLogbookArtLayout.Lens.X + 40, AreaLogbookArtLayout.Lens.Y + 89 + 1));
            foreach (var outside in new[] { right, below })
            {
                book._GuiInput(click(outside, true)); book._GuiInput(click(outside, false));
                Require(LogbookOpen, "a release outside the lens' square does not close the window: " + outside);
            }
            _logbookPanel.HolderView = null;
            _logbookStamp = null; SyncAreaLogbook();
            Require(!_logbookPanel.RoundViewShown && !book.LensCut, "without a holder there is no round view");
            // The diagnostic list has no lens at all.
            _logbookPanel.HolderView = () => (new Vector2(400, 300), 1f);
            _logbookPanel.LookToggleForSelfTest.EmitSignal(BaseButton.SignalName.Pressed);
            Require(!_logbookPanel.ArtMode && !_logbookPanel.RoundViewShown, "the diagnostic list shows no round view");
            _logbookPanel.LookToggleForSelfTest.EmitSignal(BaseButton.SignalName.Pressed);
            Require(_logbookPanel.ArtMode && _logbookPanel.RoundViewShown, "the original look shows the round view again");
            _logbookPanel.HolderView = null;
        }
        finally
        {
            _logbookPanel.HintRequested -= hints.Add;
            _logbookPanel.HolderView = InventoryHolderView;
        }
    }

    internal string AreaLogbookForSelfTest()
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("AREA logbook: " + why); }
        if (!SelfTestBootstrap || _readyArea is null || _areaSession is null) throw new InvalidOperationException("AREA_LOGBOOK_READY_REQUIRED");
        var simulatedFocus = _worldFocusSimulationForSelfTest;
        _worldFocusSimulationForSelfTest = true; _worldFocused = true;
        try
        {
            // Synthetic texts (invented, ASCII): quest 1000 and 1001 described, rumor 1001 and 1002 with text at 20 × number.
            static MesFile Mes(string text) => MesFile.Parse(System.Text.Encoding.Latin1.GetBytes(text)).File!;
            _logbookTextsTask = null; _logbookStamp = null;
            _logbookTexts = new(AreaLogbookTextsStatus.Ready, LogbookTexts.Create(true, Mes("{1000}{Find the [lost] gear.}\n{1001}{Mend the pump.}"),
                Mes("{1000}{Gear lost.}"), Mes("{20020}{The mill is haunted.}\n{20040}{The well is dry.}"), Mes("{20020}{Mill bad.}")), "synthetic");

            var session = _areaSession;
            Require(session.QueryHero() is null, "the synthetic session starts without a hero");
            InventoryKey(Key.L);
            Require(LogbookOpen && WorldModal && WindowPanelsMatchStack && !_worldGate.IsOpen && _logbookPanel!.ViewForSelfTest is { Tab: AreaLogbookTab.Rumors, Entries.Count: 0, LowIntelligence: false },
                "L opened an empty rumors tab without a hero (modal)");
            Require(_worldLastReserved?.Contains("Logbook") != true, "L is not reported as a reserved key");
            Require(GetSelfTestSnapshot().Hud.Contains(" | logbook open: L closes", StringComparison.Ordinal), "HUD open status");
            InventoryKey(Key.L);
            Require(!LogbookOpen && !WorldModal, "L closed the logbook");
            AssertWindowKeyModifiers(Require);

            // Story through the session: rumor 1002 then 1001 an hour later, 1002 quelled; quest 1001 completed, 1000 accepted later.
            Require(session.LearnRumor(session.Version, 1002).Disposition == GameSessionDisposition.Changed, "learn rumor 1002");
            Require(session.SetQuestState(session.Version, 1001, SessionQuestState.Completed).Disposition == GameSessionDisposition.Changed, "complete quest 1001");
            Require(session.JumpClocks(session.QueryClockSnapshot().Token, 3_600_000).Disposition == ClockMutationDisposition.Changed, "wait an hour");
            Require(session.LearnRumor(session.Version, 1001).Disposition == GameSessionDisposition.Changed, "learn rumor 1001");
            Require(session.SetQuestState(session.Version, 1000, SessionQuestState.Accepted).Disposition == GameSessionDisposition.Changed, "accept quest 1000");
            Require(session.QuellRumor(session.Version, 1002).Disposition == GameSessionDisposition.Changed, "quell rumor 1002");
            var revision = session.Version.Revision;

            InventoryKey(Key.L);
            var rumors = _logbookPanel!.ViewForSelfTest!;
            Require(rumors.Tab == AreaLogbookTab.Rumors && rumors.Entries.Select(e => (e.Number, e.Style)).SequenceEqual([(1002, AreaLogbookStyle.Struck), (1001, AreaLogbookStyle.Plain)])
                && rumors.Entries[0].Text == "The well is dry." && rumors.Note == "no hero: normal texts", "rumors in time order with their mark: " + string.Join(",", rumors.Entries));
            Require(_logbookPanel.TextForSelfTest.Contains("The mill is haunted.", StringComparison.Ordinal), "panel text: " + _logbookPanel.TextForSelfTest);
            // ARC-DEV-111 (SA40): the Character sheet key is the original's global key; it replaces the open logbook (the moves are tested below).
            InventoryKey(Key.Tab);
            var quests = _logbookPanel.ViewForSelfTest!;
            Require(quests.Tab == AreaLogbookTab.Quests && quests.Entries.Select(e => (e.Number, e.State, e.Style)).SequenceEqual(
                [(1001, "completed", AreaLogbookStyle.Completed), (1000, "accepted", AreaLogbookStyle.Active)]) && quests.Entries[1].Text == "Find the [lost] gear.",
                "Tab switched to the quests: " + string.Join(",", quests.Entries));
            Require(_logbookPanel.TextForSelfTest.Contains("Find the [lost] gear.", StringComparison.Ordinal), "a bracket in the data is shown literally");
            // A session change rebuilds the open logbook.
            Require(session.SetQuestState(session.Version, 1000, SessionQuestState.Botched).Disposition == GameSessionDisposition.Changed, "botch quest 1000");
            revision = session.Version.Revision;
            SyncAreaLogbook();
            Require(_logbookPanel.ViewForSelfTest!.Entries[^1] is { Number: 1000, State: "botched", Style: AreaLogbookStyle.Failed }, "rebuilt quests");
            // ARC-DEV-111: Escape opens the menu over the logbook (it stays: only the inventory family closes), and Continue returns to it.
            InventoryKey(Key.Escape);
            Require(LogbookOpen && MainMenuOpen && WindowPanelsMatchStack, "Escape opened the menu over the logbook, which stays");
            InventoryKey(Key.Escape);
            Require(LogbookOpen && !MainMenuOpen && session.Version.Revision == revision, "Escape continued play: the logbook is as it was");
            InventoryKey(Key.L);
            Require(!LogbookOpen && !WorldModal && !_worldGate.IsOpen && session.Version.Revision == revision, "L closed it; the logbook changed nothing");
            // The tab is kept across close and open.
            InventoryKey(Key.L);
            Require(_logbookPanel.ViewForSelfTest!.Tab == AreaLogbookTab.Quests, "the quests tab is kept");
            // Controller pulses through the production pulse route: confirm switches the tab, cancel closes.
            static GamepadLab.Core.IntentFrame Pulse(bool confirm, bool cancel) =>
                new(default, default, confirm, cancel, false, false, false, false, false, 0, 0, GamepadLab.Core.InputMethod.Gamepad, true, "ARC-UI-008 self-test");
            HandleWorldPulse(Pulse(confirm: true, cancel: false), true);
            Require(LogbookOpen && _logbookPanel.ViewForSelfTest!.Tab == AreaLogbookTab.Reputation, "controller confirm switched the tab (to the next built one)");
            HandleWorldPulse(Pulse(confirm: false, cancel: true), true);
            Require(!LogbookOpen && !PaletteOpen, "controller cancel closed the logbook without opening the palette");

            // ARC-UI-074: the original's book over a synthetic art set and font (the synthetic module has no game folder).
            _logbookArtForSelfTest = SyntheticLogbookArt();
            try
            {
                for (var rumor = 1100; rumor < 1170; rumor++)
                    Require(session.LearnRumor(session.Version, rumor).Disposition == GameSessionDisposition.Changed, "learn rumor " + rumor);
                _logbookTab = AreaLogbookTab.Rumors;
                InventoryKey(Key.L);
                var book = _logbookPanel.ArtViewForSelfTest;
                Require(LogbookOpen && _logbookPanel.ArtMode && book.Visible && !_logbookPanel.ListShownForSelfTest && book.Scale.X >= 1,
                    "the book is the shown look, the list is hidden");
                Require(_logbookPanel.ViewForSelfTest is { Tab: AreaLogbookTab.Rumors, Entries.Count: 72 }, "72 rumors listed");
                var first = book.Spread;
                Require(first.Start == 0 && first.Placed.Count > 8 && first.Placed[0] is { Column: 0, Y: 0 } && first.Placed.Any(p => p.Column == 1)
                    && book.HasNext && !book.HasPrevious && book.SpreadIndex == 0, "the first spread fills the left column, then the right: " + first.Placed.Count);
                var pageSounds = new List<int>(); _logbookPanel.SoundRequested += pageSounds.Add;
                // The next-page button at its corner (213/675, 77) turns the spread: a left press and release over it.
                static InputEventMouseButton Click(Vector2 at, bool pressed) => new() { ButtonIndex = MouseButton.Left, Pressed = pressed, Position = at };
                var next = new Vector2(AreaLogbookArtLayout.NextCorner.X + 2, AreaLogbookArtLayout.NextCorner.Y + 2);
                book._GuiInput(Click(next, true)); book._GuiInput(Click(next, false));
                Require(book.SpreadIndex == 1 && book.Spread.Start == first.End + 1 && book.HasPrevious,
                    $"the next button turned the page: spread={book.SpreadIndex} start={book.Spread.Start} first.End={first.End} next={book.HasNext}");
                Require(pageSounds.SequenceEqual([BookPageSound]), "the page turn asked for the book page sound");
                Require(_logbookPanel.Turn(-1) && book.SpreadIndex == 0 && !_logbookPanel.Turn(-1), "the key turn goes back and stops at the first spread");
                // The controller's next turns a page; the spread is kept over a rebuild and over a close and an open (LB3).
                HandleWorldPulse(new(default, default, false, false, false, false, true, false, false, 0, 0, GamepadLab.Core.InputMethod.Gamepad, true, "ARC-UI-074 self-test"), true);
                Require(book.SpreadIndex == 1, "the controller's next turned the page");
                var keptStart = book.Spread.Start;
                SyncAreaLogbook(); _logbookStamp = null; SyncAreaLogbook();
                Require(book.Spread.Start == keptStart, "a rebuild redraws the kept spread");
                InventoryKey(Key.L); InventoryKey(Key.L);
                Require(LogbookOpen && book.Spread.Start == keptStart && book.SpreadIndex == 1, "the spread is kept across a close and an open");
                _logbookPanel.Turn(-1);
                // A press on the quests tab button (696, 129) and its release switch the tab and start at the first spread.
                _logbookPanel.Turn(1);
                var questsTab = new Vector2(AreaLogbookArtLayout.Tabs[1].X + 2, AreaLogbookArtLayout.Tabs[1].Y + 2);
                book._GuiInput(Click(questsTab, true)); book._GuiInput(Click(questsTab, false));
                Require(_logbookPanel.ViewForSelfTest!.Tab == AreaLogbookTab.Quests && book.SpreadIndex == 0, "the tab button switched the tab and the spread restarted");
                // The kills and injuries tab button switches to its tab; a session of these checks has no hero, so the page is empty with a note.
                book._GuiInput(Click(new Vector2(AreaLogbookArtLayout.Tabs[4].X + 2, AreaLogbookArtLayout.Tabs[4].Y + 2), true));
                book._GuiInput(Click(new Vector2(AreaLogbookArtLayout.Tabs[4].X + 2, AreaLogbookArtLayout.Tabs[4].Y + 2), false));
                Require(_logbookPanel.ViewForSelfTest is { Tab: AreaLogbookTab.KillsInjuries, Entries.Count: 0 } heroless && heroless.Note!.Contains("no hero", StringComparison.Ordinal)
                    && book.Spread.Placed.Count == 0, "the kills tab of a session without a hero is an empty page");
                // Tab (the keyboard) goes on to the next tab; the diagnostic list is one click away and back.
                InventoryKey(Key.Tab);
                Require(_logbookPanel.ViewForSelfTest!.Tab == AreaLogbookTab.Background, "Tab goes on to the next tab");
                _logbookPanel.LookToggleForSelfTest.EmitSignal(BaseButton.SignalName.Pressed);
                Require(!_logbookPanel.ArtMode && _logbookPanel.ListShownForSelfTest && !book.Visible && _logbookPanel.TextForSelfTest.Length > 0, "Diagnostic view shows the list");
                _logbookPanel.LookToggleForSelfTest.EmitSignal(BaseButton.SignalName.Pressed);
                Require(_logbookPanel.ArtMode && book.Visible && !_logbookPanel.ListShownForSelfTest, "Original look shows the book again");
                AssertLogbookLensAndHint(book, Click);
                InventoryKey(Key.L);
                Require(!LogbookOpen, "L closed the book");
            }
            finally { _logbookArtForSelfTest = null; if (LogbookOpen) EndLogbook(); }
            InventoryKey(Key.L);
            Require(LogbookOpen && !_logbookPanel.ArtSet && _logbookPanel.ListShownForSelfTest, "without art the list is shown again");
            InventoryKey(Key.L);

            // A synthetic hero session (invented values, intelligence 4) through the production replacement path, as a quick load
            // does: the tab starts at the rumors again and the low-intelligence texts are read.
            var levels = MesFile.Parse(System.Text.Encoding.Latin1.GetBytes(
                string.Concat(Enumerable.Range(2, 50).Select(level => $"{{{level}}}{{{1000 * (level - 1) * (level - 1)}}}")))).File!;
            var empty = MesFile.Parse(System.Text.Encoding.Latin1.GetBytes("")).File!;
            var experience = AreaStartingHero.Experience(levels, empty, empty, out var detail) ?? throw new InvalidOperationException("AREA logbook: rules " + detail);
            var hero = new SessionHeroDefinition(16089, CharacterRace.HalfElf, CharacterGender.Male, [8, 8, 8, 8, 4, 8, 8, 8, 0, 0], 1, 5, 0,
                null, null, 385, []);
            var actor = session.QueryActor();
            var created = AreaTransitionCoordinator.CreateSession(session.QueryClockSnapshot().ProfileBinding, Guid.NewGuid(), Guid.NewGuid(),
                new(actor.Cell.X, actor.Cell.Y), actor.Map.RuntimeMap,
                new AreaHeroLoad(AreaHeroStatus.Ready, AreaStartingHero.Find("half-elf-male")!, hero, 2352, 0, null, "synthetic", experience));
            var owner = created.Owner ?? throw new InvalidOperationException("AREA logbook: session " + created.Failure);
            ReplaceAreaSession(owner, AreaRuntimes(), actor.Map.RuntimeMap, _conversationHost?.Rebind(owner));
            Require(owner.QueryHeroDialogFacts()?.Intelligence == 4, "the synthetic hero's intelligence is 4");
            Require(owner.LearnRumor(owner.Version, 1001).Disposition == GameSessionDisposition.Changed, "the hero learns rumor 1001");
            InventoryKey(Key.L);
            Require(_logbookPanel.ViewForSelfTest is { Tab: AreaLogbookTab.Rumors, LowIntelligence: true, Note: null } low && low.Entries.Single().Text == "Mill bad.",
                "a new session starts at the rumors; low-intelligence text");

            // A hero who dies while the logbook is open closes it; L is then refused, as the original refuses a dead PC.
            Require(owner.Kill(owner.Version, SessionLifeTarget.Hero).Disposition == GameSessionDisposition.Changed, "kill the hero");
            SyncAreaLogbook();
            Require(!LogbookOpen, "the dead hero's logbook closed");
            InventoryKey(Key.L);
            Require(!LogbookOpen && _logbookStatus == "refused: the hero is dead", "L for a dead hero: " + _logbookStatus);
            return "no-hero open, rumor and quest order with quelled/completed/accepted/botched styles, Tab and controller tab switch, keys owned, " +
                "tab kept and reset for a new session, low-intelligence texts, rebuild on change, dead-hero close and refusal; ARC-UI-074 the original's book over " +
                "synthetic art (tab buttons, page buttons, spreads, a tab of a session without a hero, the Diagnostic view switch)";
        }
        finally
        {
            if (LogbookOpen) EndLogbook();
            _worldFocusSimulationForSelfTest = simulatedFocus;
        }
    }

    /// <summary>
    /// ARC-UI-008 native observation on the original data: the module's logbook texts load through the content stack; the
    /// production L key opens the logbook over the live session, Tab switches the tab, the open logbook owns the keys, Escape
    /// closes it, the tab is kept and the logbook changes nothing. Then, on a detached copy of the session, every quest with a
    /// normal description is accepted and every rumor with a normal text is learned (every seventh quelled) and both tabs are
    /// built: every entry must carry its text. Prints counts and outcomes only, no text of the original data.
    /// </summary>
    private async Task RunAreaLogbookSmokeAsync()
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-UI-008 logbook smoke: " + why); }
        // A map without a session clock has no session (ARC-WORLD-057: only one whose start clock cannot be read); there is
        // no logbook to observe.
        if (_areaSession is not { } session) { GD.Print("ARC-UI-008 native-smoke logbook SKIP (no area session)"); return; }
        for (var i = 0; i < 600 && _logbookTexts is null; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); AdoptLogbookTexts(); }
        Require(_logbookTexts is { Status: AreaLogbookTextsStatus.Ready, Texts: not null }, "texts " + _logbookTexts?.Status + " " + _logbookTexts?.Detail);
        var texts = _logbookTexts!.Texts!;
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused;
        _worldFocusSimulationForSelfTest = true; _worldFocused = true;
        try
        {
            var revision = session.Version.Revision;
            InventoryKey(Key.L);
            Require(LogbookOpen && WorldModal && _logbookPanel!.ViewForSelfTest is not null, "L opened the logbook: " + _logbookStatus);
            var first = _logbookPanel!.ViewForSelfTest!.Tab;
            InventoryKey(Key.Tab);
            Require(_logbookPanel.ViewForSelfTest!.Tab != first, "Tab switched the tab");
            // ARC-DEV-111 (SA40): another window's key replaces the open window: its opener pops the logbook and pushes the inventory.
            InventoryKey(Key.I);
            Require(!LogbookOpen && InventoryOpen && WindowPanelsMatchStack && session.Version.Revision == revision, "the I key replaced the logbook");
            InventoryKey(Key.L);
            Require(LogbookOpen && !InventoryOpen && WindowPanelsMatchStack, "the L key replaced the inventory");
            InventoryKey(Key.L);
            Require(!LogbookOpen && session.Version.Revision == revision, "L closed the logbook; the logbook changed nothing");
            InventoryKey(Key.L);
            Require(LogbookOpen && _logbookPanel.ViewForSelfTest!.Tab != first, "the tab is kept");
            InventoryKey(Key.L);
            Require(!LogbookOpen, "L closed the logbook");

            var saved = session.Capture();
            var options = new GameSessionRestoreOptions(new(saved.Identity.World, session.Flags.Binding.Identity.Module),
                session.QueryClockSnapshot().ProfileBinding, new(Guid.NewGuid()), session.Limits, session.Rules.Mode, session.Experience, session.Difficulty,
                LoadRandom: session.QueryRandom());
            using var copy = GameSessionOwner.Restore(saved, options).Owner ?? throw new InvalidOperationException("ARC-UI-008 logbook smoke: copy");
            // The texts the logbook reads for this PC: the hero's intelligence, the normal texts without a hero.
            var intelligence = copy.QueryHeroDialogFacts()?.Intelligence ?? AreaLogbook.NoHeroIntelligence;
            var quests = Enumerable.Range(LogbookTexts.FirstQuest, LogbookTexts.LastQuest - LogbookTexts.FirstQuest + 1)
                .Where(quest => texts.QuestDescription(quest, intelligence) is not null && copy.QueryQuest(quest) is null).ToHashSet();
            var rumors = Enumerable.Range(SessionStoryLimits.FirstRumor, SessionStoryLimits.MaxRumors)
                .Where(rumor => texts.RumorText(rumor, intelligence) is not null && copy.QueryRumor(rumor) is null && !copy.QueryRumorQuelled(rumor)).ToArray();
            foreach (var quest in quests) Require(copy.SetQuestState(copy.Version, quest, SessionQuestState.Accepted).Disposition == GameSessionDisposition.Changed, $"accept a quest");
            var quelled = new HashSet<int>();
            for (var i = 0; i < rumors.Length; i++)
            {
                Require(copy.LearnRumor(copy.Version, rumors[i]).Disposition == GameSessionDisposition.Changed, "learn a rumor");
                if (i % 7 == 0 && copy.QuellRumor(copy.Version, rumors[i]).Disposition == GameSessionDisposition.Changed) quelled.Add(rumors[i]);
            }
            var lowQuests = Enumerable.Range(LogbookTexts.FirstQuest, LogbookTexts.LastQuest - LogbookTexts.FirstQuest + 1).Count(quest => texts.QuestLogLow?.Find(quest) is not null);
            var lowRumors = Enumerable.Range(SessionStoryLimits.FirstRumor, SessionStoryLimits.MaxRumors).Count(rumor => texts.RumorText(rumor, LogbookTexts.LowIntelligence) is not null);
            // ARC-UI-079: every reputation of the module's table is held on the copy.
            foreach (var record in texts.Reputations?.Records ?? []) copy.AddReputation(copy.Version, record.Number);
            // ARC-UI-080: every blessing and curse of the module's tables is held on the copy (up to the list's capacity).
            if (texts.Blessings is not null && texts.Curses is not null && copy.QueryHeroBlessings(SessionBlessingKind.Blessing) is not null)
                foreach (var (kind, table) in new[] { (SessionBlessingKind.Blessing, texts.Blessings), (SessionBlessingKind.Curse, texts.Curses) })
                    foreach (var number in BlessingRules.Numbers(table).Take(SessionEffectLimits.MaxBlessings))
                    {
                        copy.AddHeroBlessing(copy.Version, kind, number, new SessionEffectDefinition(BlessingRules.EffectOf(table, number).Effect ?? 0, []));
                        copy.JumpClocks(copy.QueryClockSnapshot().Token, 60_000);
                    }
            // ARC-UI-081: every key the module's key names hold is carried on the copy (up to the list's capacity), in descending number order so the stored order shows.
            if (texts.Keys is not null && copy.QueryHeroKeys() is not null)
                foreach (var number in texts.Keys.Messages.Select(m => m.Number).Where(SessionStoryLimits.IsKey).OrderDescending().Take(SessionStoryLimits.MaxKeys))
                    copy.AddHeroKey(copy.Version, number);
            // ARC-UI-082: a few kills and every injury kind are carried on the copy; the names are the module's description numbers 1..40 that have a text.
            if (copy.QueryHeroKillLog() is not null && texts.Descriptions is not null)
            {
                var named = Enumerable.Range(1, 40).Where(id => texts.DescriptionName(id) is not null).Take(6).ToArray();
                for (var i = 0; i < named.Length; i++)
                {
                    copy.RecordHeroKill(copy.Version, named[i], 3 + i * 2, i % 2 == 0 ? 10 * (i + 1) : -10 * (i + 1), i % 3 == 0 ? 5 : -5);
                    copy.RecordHeroInjury(copy.Version, named[i], i % SessionStoryLimits.InjuryKinds);
                }
            }
            var questView = AreaLogbook.Build(copy, texts, AreaLogbookTab.Quests, AreaHoverIndex.TextProfile).View!;
            var rumorView = AreaLogbook.Build(copy, texts, AreaLogbookTab.Rumors, AreaHoverIndex.TextProfile).View!;
            var hero = copy.QueryHeroDialogFacts() is not null;
            Require(quests.Count > 0 && rumors.Length > 0, "the module has logbook texts");
            var ours = questView.Entries.Where(e => quests.Contains(e.Number)).ToArray();
            Require(questView.Entries.Count == copy.QueryQuestLog().Count && ours.Length == quests.Count
                && ours.All(e => e.Text.Length > 0 && e.Style == AreaLogbookStyle.Active && e.State is { Length: > 0 }), "every accepted quest shows its description");
            var learned = rumorView.Entries.Where(e => rumors.Contains(e.Number)).ToArray();
            Require(rumorView.Entries.Count == copy.QueryRumorLog().Count && learned.Length == rumors.Length && learned.All(e => e.Text.Length > 0
                && (e.Style == AreaLogbookStyle.Struck) == quelled.Contains(e.Number)), "every learned rumor shows its text and mark");
            Require(questView.Note is null or "no hero: normal texts" && rumorView.Note is null or "no hero: normal texts", "texts decodable: " + questView.Note + " / " + rumorView.Note);
            Require(session.Version.Revision == revision, "the live session is untouched");
            // ARC-UI-074: with the game's art and fonts read the book is the shown look, and every entry of both tabs lands on exactly one
            // spread of the original's layout (an entry cut at a column's bottom is counted, never lost).
            var book = "list (art not read)";
            if (LogbookArtLook() is { } look)
            {
                InventoryKey(Key.L);
                Require(LogbookOpen && _logbookPanel!.ArtMode && _logbookPanel.ArtViewForSelfTest.Visible, "the book is the shown look");
                InventoryKey(Key.L);
                var details = new List<string>();
                var reputationView = AreaLogbook.Build(copy, texts, AreaLogbookTab.Reputation, AreaHoverIndex.TextProfile).View!;
                var backgroundView = AreaLogbook.Build(copy, texts, AreaLogbookTab.Background, AreaHoverIndex.TextProfile).View!;
                var keyView = AreaLogbook.Build(copy, texts, AreaLogbookTab.Keys, AreaHoverIndex.TextProfile).View!;
                Require(keyView.Entries.Count == (copy.QueryHeroKeys()?.Count ?? 0) && keyView.Entries.Select(e => e.Number).SequenceEqual(copy.QueryHeroKeys() ?? [])
                    && keyView.Entries.All(e => e.Text.Length > 0 == (texts.KeyName(e.Number) is { Bytes.Length: > 0 })), "the keys tab lists every carried key in stored order with its name: " + keyView.Entries.Count);
                var killView = AreaLogbook.Build(copy, texts, AreaLogbookTab.KillsInjuries, AreaHoverIndex.TextProfile).View!;
                Require(killView.Entries.Count == 9 + (copy.QueryHeroKillLog()?.Injuries.Count ?? 0) && killView.Entries[0].Text.EndsWith($": {copy.QueryHeroKillLog()?.Total ?? 0}", StringComparison.Ordinal)
                    && killView.Entries[7].Text.Length == 0, "the kills tab shows its nine fixed lines and every injury: " + killView.Entries.Count);
                var blessingView = AreaLogbook.Build(copy, texts, AreaLogbookTab.BlessingsCurses, AreaHoverIndex.TextProfile).View!;
                var heldBlessings = (copy.QueryHeroBlessings(SessionBlessingKind.Blessing)?.Count ?? 0) + (copy.QueryHeroBlessings(SessionBlessingKind.Curse)?.Count ?? 0);
                Require(blessingView.Entries.Count == heldBlessings && blessingView.Entries.Zip(blessingView.Entries.Skip(1)).All(pair => pair.First.TimestampMs <= pair.Second.TimestampMs)
                    && blessingView.Entries.All(e => e.Style is AreaLogbookStyle.Blessing or AreaLogbookStyle.Curse),
                    "the blessings tab lists every held blessing and curse oldest first: " + blessingView.Entries.Count);
                Require(reputationView.Entries.Count == copy.QueryReputations().Count && (texts.Reputations is null || reputationView.Entries.Any(e => e.Text.Length > 0)),
                    "the reputations tab lists every held reputation with names: " + reputationView.Entries.Count);
                foreach (var view in new[] { questView, rumorView, reputationView, blessingView, killView, backgroundView, keyView })
                {
                    int Lines(string text) => AreaPremadeBrowserArtLayout.Wrap(look.Fonts.Text.Font, text, AreaLogbookArtLayout.Columns[0].Width).Count;
                    var counts = view.Entries.SelectMany(entry => view.Tab == AreaLogbookTab.Background
                            ? AreaLogbookArtLayout.SplitPieces(AreaLogbook.Line(entry), piece => Lines(piece) * look.Fonts.TextLineHeight, AreaLogbookArtLayout.Columns[0].Height)
                            : [AreaLogbook.Line(entry)]).Select(Lines).ToArray();
                    var starts = AreaLogbookArtLayout.Starts(counts, look.Fonts.TextLineHeight);
                    var covered = 0; var cut = 0;
                    foreach (var start in starts)
                    {
                        var spread = AreaLogbookArtLayout.Spread(counts, look.Fonts.TextLineHeight, start);
                        Require(spread.Start == covered && spread.End >= spread.Start && spread.Placed.Count > 0, $"spread {start} continues the previous one");
                        covered = spread.End + 1; cut += spread.Placed.Count(p => p.Truncated);
                    }
                    Require(covered == counts.Length || starts.Count == AreaLogbookArtLayout.MaxSpreads, $"every {view.Tab} entry lands on a spread: {covered}/{counts.Length}");
                    details.Add($"{view.Tab}:entries={counts.Length},spreads={starts.Count},cut={cut}");
                }
                book = $"art line={look.Fonts.TextLineHeight} {string.Join(" ", details)} ui={texts.Ui is not null}";
            }
            GD.Print("ARC-UI-074 native-smoke logbook book " + book);
            // ARC_AREA_LOGBOOK_CAPTURE=<ignored path prefix> saves readbacks of the open book. It changes the live session (a few rumors
            // and quests are added to show something), so it is for a look only and never part of a check.
            if (System.Environment.GetEnvironmentVariable("ARC_AREA_LOGBOOK_CAPTURE") is { Length: > 0 } capture && LogbookArtLook() is not null)
            {
                foreach (var rumor in rumors.Take(40)) session.LearnRumor(session.Version, rumor);
                foreach (var record in (texts.Reputations?.Records ?? []).Take(12)) session.AddReputation(session.Version, record.Number);
                foreach (var quest in quests.Take(14)) session.SetQuestState(session.Version, quest, SessionQuestState.Accepted);
                session.SetQuestState(session.Version, quests.First(), SessionQuestState.Completed);
                async Task Shot(string name)
                {
                    for (var i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using var image = GetViewport().GetTexture().GetImage();
                    Require(image.SavePng($"{capture}-{name}.png") == Error.Ok, "capture saved");
                }
                InventoryKey(Key.L);
                _logbookTab = AreaLogbookTab.Rumors; SyncAreaLogbook();
                await Shot("rumors-1");
                _logbookPanel!.Turn(1); await Shot("rumors-2");
                SelectLogbookTab(AreaLogbookTab.Quests); await Shot("quests-1");
                SelectLogbookTab(AreaLogbookTab.Reputation); await Shot("reputation");
                if (texts.Blessings is not null && texts.Curses is not null && session.QueryHeroBlessings(SessionBlessingKind.Blessing) is not null)
                    foreach (var (kind, table) in new[] { (SessionBlessingKind.Blessing, texts.Blessings), (SessionBlessingKind.Curse, texts.Curses) })
                        foreach (var number in BlessingRules.Numbers(table).Take(6))
                        {
                            session.AddHeroBlessing(session.Version, kind, number, new SessionEffectDefinition(BlessingRules.EffectOf(table, number).Effect ?? 0, []));
                            session.JumpClocks(session.QueryClockSnapshot().Token, 3_600_000);
                        }
                SelectLogbookTab(AreaLogbookTab.BlessingsCurses); await Shot("blessings");
                if (texts.Descriptions is not null && session.QueryHeroKillLog() is not null)
                    foreach (var number in Enumerable.Range(1, 40).Where(id => texts.DescriptionName(id) is not null).Take(4))
                    {
                        session.RecordHeroKill(session.Version, number, 4 + number, number % 2 == 0 ? 30 : -30, 0);
                        session.RecordHeroInjury(session.Version, number, number % SessionStoryLimits.InjuryKinds);
                    }
                SelectLogbookTab(AreaLogbookTab.KillsInjuries); await Shot("kills");
                SelectLogbookTab(AreaLogbookTab.Background); await Shot("background");
                if (texts.Keys is not null && session.QueryHeroKeys() is not null)
                    foreach (var number in texts.Keys.Messages.Select(m => m.Number).Where(SessionStoryLimits.IsKey).Take(12)) session.AddHeroKey(session.Version, number);
                SelectLogbookTab(AreaLogbookTab.Keys); await Shot("keys");
                InventoryKey(Key.L);
            }
            GD.Print("ARC-UI-008 native-smoke logbook PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Texts = _logbookTexts.Detail, Quests = quests.Count, LowQuestTexts = lowQuests, Rumors = rumors.Length, LowRumorTexts = lowRumors,
                QuestEntries = questView.Entries.Count, RumorEntries = rumorView.Entries.Count, Hero = hero, questView.LowIntelligence, Quelled = quelled.Count }));
        }
        finally
        {
            if (LogbookOpen) EndLogbook();
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
        }
    }
}
