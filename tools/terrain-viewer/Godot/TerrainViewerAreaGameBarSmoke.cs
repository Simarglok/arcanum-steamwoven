using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // Native observation (ARC_AREA_MAP_SMOKE_ONLY=bar, map mode on the start map with --main-menu true; an automated run, which
    // draws the bars only because this smoke asks): pointer events go through the window's GUI. In the main menu the pointer over
    // Single Player plays 3026 and a press 3027. After a new game as the first pre-made hero the bars show the game's art; each
    // window button is hovered (its name and hover frame), pressed (down frame, 3000) and released (3001), opening its window, and a
    // second press closes it; Inventory over the open sheet switches to it. Combat and Spells toggle the hero's combat mode and the
    // spell pane, Fate Points says it is not built, and a press on the strip between buttons never reaches the world.
    private async Task RunAreaGameBarSmokeAsync()
    {
        static void Require(bool value, string why) { if (!value) { GD.Print("ARC-UI-018 bar smoke FAIL " + why); throw new InvalidOperationException("ARC-UI-018 bar smoke: " + why); } }
        var savedInterfaceHd = _gameSettings.HdInterface;
        var verifyHd = System.Environment.GetEnvironmentVariable("ARC_AREA_BAR_HD_VERIFY") == "1";
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused;
        _worldFocusSimulationForSelfTest = true; _worldFocused = true;
        _gameBarForSelfTest = true; _gameBarSoundsForSelfTest.Clear();
        try
        {
            async Task Frames(int count)
            {
                for (var i = 0; i < count; i++)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                }
            }
            async Task Move(Vector2 at)
            {
                GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at });
                await Frames(1);
            }
            async Task Button(Vector2 at, bool pressed)
            {
                GetViewport().PushInput(new InputEventMouseButton
                {
                    ButtonIndex = MouseButton.Left, Pressed = pressed, Position = at, GlobalPosition = at, ButtonMask = pressed ? MouseButtonMask.Left : 0,
                });
                await Frames(1);
            }
            void Key(Key key, char unicode = '\0')
            {
                InputWorld(new InputEventKey { Keycode = key, PhysicalKeycode = key, Unicode = unicode, Pressed = true });
                InputWorld(new InputEventKey { Keycode = key, PhysicalKeycode = key, Unicode = unicode, Pressed = false });
            }
            void Choose(int message)
            {
                var button = _mainMenu!.Shown.FirstOrDefault(shown => shown.Message == message) ?? throw new InvalidOperationException("ARC-UI-018 bar smoke: no button " + message);
                var letter = _mainMenu.Texts.Hotkey(button) ?? throw new InvalidOperationException("ARC-UI-018 bar smoke: no letter for " + message);
                Key(letter is >= 'A' and <= 'Z' ? (Key)letter : Godot.Key.None, char.ToLowerInvariant(letter));
            }
            bool Sounded(int id) => _gameBarSoundsForSelfTest.Any(sound => sound.Id == id && sound.Started);
            string Sounds() => string.Join(",", _gameBarSoundsForSelfTest.Select(sound => $"{sound.Id}{(sound.Started ? "" : "!")}"));

            Require(_arguments?.MainMenu == true && MainMenuOpen && _mainMenu!.Window == AreaMenuWindow.Main, "the menu is open (run with --main-menu true)");
            await Frames(2);
            var single = _mainMenuPanel!.ButtonCentreForSelfTest(0) ?? throw new InvalidOperationException("ARC-UI-018 bar smoke: no menu button");
            await Move(single);
            Require(Sounded(AreaGameBarLayout.MenuHoverSound), "the pointer over a menu button played 3026: " + Sounds());
            await Button(single, true);
            Require(Sounded(AreaGameBarLayout.MenuPressSound), "a press on it played 3027: " + Sounds());
            await Button(single, false);
            Require(_mainMenu!.Window == AreaMenuWindow.SinglePlayer, "the release opened Single Player: " + _mainMenu.Window);
            Choose(50); Choose(420);
            MainMenuCommand(() => _mainMenu!.Start());
            await Frames(3);
            Require(!MainMenuOpen && _areaSession?.QueryHero() is not null, "a new game began with a hero");
            Require(_gameBarPanel is { Visible: true } && _gameBarContent is { ArtReady: true } content && content.Texts.GameCount == AreaGameBarTexts.Count,
                "the bars show with the game's art and texts: " + _gameBarContent?.Detail);
            var panel = _gameBarPanel!;
            if (verifyHd)
            {
                Require(_canvas?.HdSource?.Pack is not null, "HD verification needs ARC_HD_PACK");
                _gameSettings = _gameSettings with { HdInterface = true };
                SyncInterfaceHd();
                await VerifyTopBarHdAsync(panel, Frames, Move, Button);
            }
            GD.Print($"ARC-UI-018 bar smoke bars factor={panel.Factor} top={panel.StripRectForSelfTest(AreaBarStrip.Top)} bottom={panel.StripRectForSelfTest(AreaBarStrip.Bottom)}");

            // Hover, press and release one button, watching its frames and sounds. ARC-UI-023: the name is a hover entry of the message
            // log, drawn only 3 s after the last stored entry (the new game may have posted one).
            _messageClockShiftForSelfTest += AreaMessageLog.QuietMilliseconds + 1;
            var character = panel.ButtonCentreForSelfTest(AreaBarButton.Character);
            await Move(character);
            Require(panel.HoverTextForSelfTest == "Character Editor" && panel.FrameForSelfTest(AreaBarButton.Character) == 2,
                $"hover: '{panel.HoverTextForSelfTest}' frame {panel.FrameForSelfTest(AreaBarButton.Character)}");
            _gameBarSoundsForSelfTest.Clear();
            await Button(character, true);
            Require(panel.FrameForSelfTest(AreaBarButton.Character) == 1 && Sounded(AreaGameBarLayout.PressSound) && !SheetOpen, "down: frame 1, 3000, nothing opened yet");
            await Button(character, false);
            Require(Sounded(AreaGameBarLayout.ReleaseSound) && SheetOpen, "the release played 3001 and opened the sheet: " + Sounds() + " " + _gameBarNotice);

            async Task Click(AreaBarButton button)
            {
                var at = panel.ButtonCentreForSelfTest(button);
                await Move(at); await Button(at, true); await Button(at, false);
            }
            await Click(AreaBarButton.Inventory);
            Require(!SheetOpen && InventoryOpen, "Inventory switched from the sheet to the inventory: " + _gameBarNotice);
            await Click(AreaBarButton.Inventory);
            Require(!InventoryOpen, "a second press closed it");
            foreach (var (button, open) in new (AreaBarButton, Func<bool>)[] { (AreaBarButton.Logbook, () => LogbookOpen), (AreaBarButton.Schematics, () => SchematicsOpen) })
            {
                await Click(button);
                Require(open(), $"{button} opened its window: {_gameBarNotice}");
                await Click(button);
                Require(!open() && !WorldModal, $"{button} closed it");
            }
            // The logbook laid out in the part the bars leave free, and its own close button is pressed with the bars shown.
            await Click(AreaBarButton.Logbook);
            var book = _logbookPanel!.PanelRectForSelfTest;
            Require(book.Position.Y >= AreaGameBarPanel.TopInset && book.End.Y <= GetViewport().GetVisibleRect().Size.Y - AreaGameBarPanel.BottomInset,
                $"the logbook {book} lies between the strips");
            await Move(book.GetCenter()); await Button(book.GetCenter(), true); await Button(book.GetCenter(), false);
            Require(LogbookOpen && _gameBar.Hovered is null, "a press inside the window is the window's");
            // ARC-UI-098 (LB23), through the real pointer route with the player's art: the lens shows the round view of the hero, a tab's hover writes
            // its own message of logbk_ui.mes to the message window and leaving takes it away, and a left release in the lens closes the window.
            var bookView = _logbookPanel.ArtViewForSelfTest;
            Require(_logbookPanel.ArtMode && bookView.Visible, "the logbook shows the original's book with the player's art");
            await Frames(2);
            Require(_logbookPanel.RoundViewShown && bookView.LensCut, "the lens shows the round view of the hero");
            var questsTab = bookView.WindowPointForSelfTest(AreaLogbookArtLayout.Tabs[1].X + 5, AreaLogbookArtLayout.Tabs[1].Y + 5);
            await Move(questsTab);
            var tabHint = _logbookPanel.ViewForSelfTest!.HintFor(AreaLogbookTab.Quests);
            Require(tabHint.Length > 0 && _messageLog.Shown is { Kind: AreaMessageKind.Hover } shownHint && shownHint.Title == tabHint, $"the quests tab's hint '{tabHint}' is in the message window");
            await Move(bookView.WindowPointForSelfTest(300, 200));
            Require(_messageLog.Shown is not { Kind: AreaMessageKind.Hover }, "leaving the tab took the hint away");
            var lensPoint = bookView.WindowPointForSelfTest(AreaLogbookArtLayout.Lens.X + 40, AreaLogbookArtLayout.Lens.Y + 40);
            await Move(lensPoint); await Button(lensPoint, true);
            Require(LogbookOpen, "a press in the lens does not close the window");
            await Button(lensPoint, false);
            Require(!LogbookOpen && !WorldModal && !_logbookPanel.RoundViewShown, "a release in the lens closed the window");
            CloseLogbookByPlayer();
            // The world map does not fit between the strips: the bars hide while it is open, and come back with the pointer state reset.
            await Click(AreaBarButton.Map);
            await Frames(1);
            Require(WorldMapOpen && !_gameBarPanel!.Visible, "Map opened the world map and the bars hid: " + _gameBarNotice);
            CloseWorldMapByPlayer();
            await Frames(1);
            Require(!WorldMapOpen && _gameBarPanel!.Visible && _gameBar.Hovered is null && panel.HoverTextForSelfTest == "", "the bars came back with no hover");
            await Click(AreaBarButton.Combat);
            Require(HeroInCombatMode, "Combat entered combat mode: " + _gameBarNotice);
            await Click(AreaBarButton.Combat);
            Require(!HeroInCombatMode, "Combat left it");
            await Click(AreaBarButton.Spells);
            if (SpellMapActive)
            {
                Require(_spellPane?.Shown == true && _gameBar.IsOn(AreaBarButton.Spells) && panel.FrameForSelfTest(AreaBarButton.Spells) == 1, "Spells showed the pane and stays down: " + _gameBarNotice);
                await Click(AreaBarButton.Spells);
                Require(_spellPane?.Shown != true && !_gameBar.IsOn(AreaBarButton.Spells), "a second press hid it");
            }
            else Require(!_gameBar.IsOn(AreaBarButton.Spells) && _gameBarNotice is not null, "Spells refused off a cast map: " + _gameBarNotice);
            await Click(AreaBarButton.FatePoints);
            Require(_gameBarNotice?.Contains("not built") == true, "Fate Points: " + _gameBarNotice);

            await RunAreaMessageLogSmokeAsync(panel);

            // A press on the top strip right of every button (its clock) is the bar's, not the world's.
            var strip = panel.StripRectForSelfTest(AreaBarStrip.Top);
            var clock = strip.Position + new Vector2(720, 20) * panel.Factor;
            var before = _worldRouteClick;
            await Move(clock);
            Require(panel.HoverTextForSelfTest == "", "leaving the buttons cleared the hover name");
            await Button(clock, true); await Button(clock, false);
            Require(ReferenceEquals(before, _worldRouteClick), "the world took no press from the strip: " + _worldRouteClick);
            Require(_gameBarSoundsForSelfTest.All(sound => sound.Started), "every bar sound started: " + Sounds());
            GD.Print($"ARC-UI-018 native-smoke bar PASS sounds={Sounds()}");
        }
        finally
        {
            if (verifyHd) { _gameSettings = _gameSettings with { HdInterface = savedInterfaceHd }; SyncInterfaceHd(); }
            _gameBarForSelfTest = false; _messageClockShiftForSelfTest = 0;
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
        }
    }
}
