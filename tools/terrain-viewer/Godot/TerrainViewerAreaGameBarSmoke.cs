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
        var savedGenerated = _gameSettings.GeneratedInterface;
        // ARC-UI-140: the generated interface runs the HD path with the pack (pictures from its redraw) and the chrome generated.
        var verifyGenerated = System.Environment.GetEnvironmentVariable("ARC_AREA_BAR_GENERATED_VERIFY") == "1";
        var verifyHd = verifyGenerated || System.Environment.GetEnvironmentVariable("ARC_AREA_BAR_HD_VERIFY") == "1";
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
            void SendButton(Vector2 at, bool pressed) => GetViewport().PushInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left, Pressed = pressed, Position = at, GlobalPosition = at, ButtonMask = pressed ? MouseButtonMask.Left : 0,
            });
            async Task Button(Vector2 at, bool pressed)
            {
                SendButton(at, pressed);
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
                _gameSettings = _gameSettings with { HdInterface = true, GeneratedInterface = verifyGenerated };
                SyncInterfaceHd();
                if (verifyGenerated) await VerifyGeneratedInterfaceAsync(panel, Frames);
                await VerifyTopBarHdAsync(panel, Frames, Move, Button);
            }
            await CaptureHudAsync(verifyGenerated ? "generated" : verifyHd ? "hd" : "legacy", Frames);
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
            // ARC-UI-127/132: the sheet stands on the bottom strip at every window size, centred; in HD it also reaches the top strip (the whole
            // free height at the art's exact scale, aspect ratio kept), the original's bitmaps stand at the largest whole scale that fits.
            // ARC_AREA_SHEET_CAPTURE=DIR saves a readback of the whole viewport per window size (ignored local path, original-derived).
            async Task SheetOnHud(string at)
            {
                await Frames(3);
                var window = _sheetPanel!.ArtViewForSelfTest!;
                var bottom = panel.StripRectForSelfTest(AreaBarStrip.Bottom);
                var top = panel.StripRectForSelfTest(AreaBarStrip.Top);
                var scale = window.Scale.X;
                var end = window.GlobalPosition.Y + AreaCharacterSheetArtLayout.Height * scale;
                var freeHeight = bottom.Position.Y - top.End.Y;
                var side = window.GlobalPosition.X;
                var viewWidth = GetViewport().GetVisibleRect().Size.X;
                Require(Math.Abs(end - bottom.Position.Y) <= 0.51f && window.GlobalPosition.Y >= top.End.Y - 0.5f
                    && Math.Abs(side + AreaCharacterSheetArtLayout.Width * scale + side - viewWidth) < 1.5f
                    && scale == window.Scale.Y && scale >= panel.Factor
                    && (verifyHd ? Math.Abs(scale - Math.Min(freeHeight / AreaCharacterSheetArtLayout.Height, viewWidth / AreaCharacterSheetArtLayout.Width)) < 0.01f
                        : scale == Math.Floor(scale) && (AreaCharacterSheetArtLayout.Height * (scale + 1) > freeHeight || AreaCharacterSheetArtLayout.Width * (scale + 1) > viewWidth)),
                    $"the sheet stands between the strips {at}: window {window.GlobalPosition} scale {window.Scale} end {end}, bottom strip {bottom}, top strip {top}");
                if (System.Environment.GetEnvironmentVariable("ARC_AREA_SHEET_CAPTURE") is { Length: > 0 } sheetCapture)
                {
                    var sheetDir = System.IO.Path.GetFullPath(sheetCapture);
                    System.IO.Directory.CreateDirectory(sheetDir);
                    using var sheetImage = GetViewport().GetTexture().GetImage();
                    Require(sheetImage is not null && sheetImage.SavePng(System.IO.Path.Combine(sheetDir, $"sheet-{(verifyGenerated ? "generated" : verifyHd ? "hd" : "legacy")}-{sheetImage.GetWidth()}x{sheetImage.GetHeight()}.png")) == Error.Ok, "sheet readback saved");
                }
            }
            var sheetSize = DisplayServer.WindowGetSize();
            await SheetOnHud($"at {sheetSize}");
            await SetSmokeWindowSizeAsync(new Vector2I(1920, 1080));
            await SheetOnHud("at 1920 x 1080");
            await SetSmokeWindowSizeAsync(sheetSize);
            await SheetOnHud($"back at {sheetSize}");

            async Task Click(AreaBarButton button)
            {
                var at = panel.ButtonCentreForSelfTest(button);
                await Move(at); await Button(at, true); await Button(at, false);
            }
            await Click(AreaBarButton.Inventory);
            Require(!SheetOpen && InventoryOpen, "Inventory switched from the sheet to the inventory: " + _gameBarNotice);
            if (verifyGenerated) await VerifyGeneratedInventoryAsync(Frames, SetSmokeWindowSizeAsync);
            else await CaptureHudAsync(verifyHd ? "inventory-hd" : "inventory-legacy", Frames);
            await Click(AreaBarButton.Inventory);
            Require(!InventoryOpen, "a second press closed it");
            foreach (var (button, open) in new (AreaBarButton, Func<bool>)[] { (AreaBarButton.Logbook, () => LogbookOpen), (AreaBarButton.Schematics, () => SchematicsOpen) })
            {
                await Click(button);
                Require(open(), $"{button} opened its window: {_gameBarNotice}");
                await Click(button);
                Require(!open() && !WorldModal, $"{button} closed it");
            }
            // ARC-UI-123: the window changes on release, but the button stays visible.
            foreach (var (button, isOpen) in new (AreaBarButton, Func<bool>)[]
            {
                (AreaBarButton.Character, () => SheetOpen),
                (AreaBarButton.Inventory, () => InventoryOpen),
                (AreaBarButton.Logbook, () => LogbookOpen),
                (AreaBarButton.Schematics, () => SchematicsOpen),
            })
            {
                var strip = AreaGameBarLayout.Spec(button).Strip;
                var control = panel.GetNode<TextureRect>($"{strip}Strip/{button}");
                var at = control.GetGlobalRect().GetCenter();
                var stripRect = panel.StripRectForSelfTest(strip);
                var outside = new Vector2(at.X, strip == AreaBarStrip.Top ? stripRect.End.Y + 5 : stripRect.Position.Y - 5);

                Require(!isOpen(), $"{button} feedback check starts with its window closed");
                await Move(at);
                var hoverFrame = panel.FrameForSelfTest(button);
                var soundsBefore = _gameBarSoundsForSelfTest.Count;
                await Button(at, true);
                var heldFrame = panel.FrameForSelfTest(button);
                var heldTexture = control.Texture;
                Require(_gameBar.Held == button && !isOpen() && heldFrame == 1 && hoverFrame != heldFrame && heldTexture is not null,
                    $"{button} shows Down while held without opening its window");
                Require((heldTexture is HdInterfaceTexture { IsHd: true }) == verifyHd, $"{button} Down uses the requested art");
                await Move(outside);
                await Button(outside, false);
                Require(_gameBar.Held is null && !isOpen() && panel.FrameForSelfTest(button) == 0, $"{button} cancels outside");

                foreach (var opens in new[] { true, false })
                {
                    await Move(at);
                    SendButton(at, true);
                    SendButton(at, false);
                    Require(_gameBar.Held is null && isOpen() == opens, $"{button} release immediately {(opens ? "opens" : "closes")} its window");
                    await Frames(1);
                    var quickFrame = panel.FrameForSelfTest(button);
                    Require(panel.Visible && quickFrame == heldFrame && control.Texture == heldTexture,
                        $"{button} first draw after quick {(opens ? "open" : "close")} shows held Down, got frame {quickFrame}");
                    await WaitForDownFeedback();
                    Require(panel.FrameForSelfTest(button) == hoverFrame, $"{button} feedback settles to Hover");
                }

                Require(_gameBarSoundsForSelfTest.Skip(soundsBefore).Select(sound => sound.Id).SequenceEqual(new[]
                {
                    AreaGameBarLayout.PressSound,
                    AreaGameBarLayout.PressSound, AreaGameBarLayout.ReleaseSound,
                    AreaGameBarLayout.PressSound, AreaGameBarLayout.ReleaseSound,
                }), $"{button} keeps the press and release sounds for cancellation, opening and closing");
                var art = verifyGenerated ? "Generated" : verifyHd ? "HD" : "Legacy";
                GD.Print($"ARC-UI-123 native {button} PASS held=Down quick-open=Down quick-close=Down settled=Hover canceled=Idle art={art}");
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
            // ARC-UI-125: the logbook canvas is premultiplied, so every HD art it draws must be (Book_Quests's transparent texels carry RGB).
            Require(_logbookArtTextures is { Count: > 0 } && _logbookArtTextures.Values.SelectMany(frames => frames).All(texture => texture is not HdInterfaceTexture { Premultiplied: false }),
                "the logbook's HD art is premultiplied for its PremultAlpha canvas");
            // ARC-UI-125: ARC_AREA_LOGBOOK_HD_CAPTURE=PATH saves an in-app readback of the whole viewport with the logbook open (ignored local path)
            // and the window's rectangle next to it, to compare the layers the player sees with the pack's frames.
            if (System.Environment.GetEnvironmentVariable("ARC_AREA_LOGBOOK_HD_CAPTURE") is { Length: > 0 } logbookCapture)
            {
                var logbookPath = System.IO.Path.GetFullPath(logbookCapture);
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logbookPath)!);
                using var logbookImage = GetViewport().GetTexture().GetImage();
                Require(logbookImage.SavePng(logbookPath) == Error.Ok, "logbook readback saved");
                var (lensAt, lensSide) = bookView.LensOnScreen;
                System.IO.File.WriteAllText(logbookPath + ".json", System.Text.Json.JsonSerializer.Serialize(new
                {
                    hd = InterfaceHd.Enabled, panel = new { x = book.Position.X, y = book.Position.Y, w = book.Size.X, h = book.Size.Y },
                    viewport = new { w = logbookImage.GetWidth(), h = logbookImage.GetHeight() }, lens = new { x = lensAt.X, y = lensAt.Y, side = lensSide },
                }));
            }
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
            // ARC-UI-120: route a same-frame click through the GUI, then compare its rendered pixels to a held press.
            var combatControl = panel.GetNode<TextureRect>("BottomStrip/Combat");
            var combatRect = combatControl.GetGlobalRect();
            var combat = combatRect.GetCenter();
            byte[] ButtonPixels(Rect2 rect)
            {
                using var viewportImage = GetViewport().GetTexture().GetImage();
                using var crop = viewportImage.GetRegion(new Rect2I((Vector2I)rect.Position, (Vector2I)rect.Size));
                return crop.GetData();
            }
            async Task WaitForDownFeedback()
            {
                await ToSignal(GetTree().CreateTimer(0.15, ignoreTimeScale: true), SceneTreeTimer.SignalName.Timeout);
                await Frames(1);
            }

            Require(InterfaceHd.Enabled == verifyHd, "the bar smoke is running in the requested interface mode");
            await Move(combat);
            Require(panel.FrameForSelfTest(AreaBarButton.Combat) == 2, "Combat starts at Hover");
            Require((combatControl.Texture is HdInterfaceTexture { IsHd: true }) == verifyHd, "Combat Hover uses the requested art");
            var combatHoverPixels = ButtonPixels(combatRect);
            var releasesBeforeCombat = _gameBarSoundsForSelfTest.Count(sound => sound.Id == AreaGameBarLayout.ReleaseSound);
            SendButton(combat, true);
            Require(!HeroInCombatMode && _gameBar.Held == AreaBarButton.Combat, "the quick press holds Combat without toggling it");
            SendButton(combat, false);
            Require(HeroInCombatMode && _gameBar.Held is null, "the quick release immediately entered combat and cleared the hold");
            await Frames(1);
            Require(panel.FrameForSelfTest(AreaBarButton.Combat) == 1, $"the first draw after a quick click shows Down, got {panel.FrameForSelfTest(AreaBarButton.Combat)}");
            Require((combatControl.Texture is HdInterfaceTexture { IsHd: true }) == verifyHd, "Combat Down uses the requested art");
            var quickDownPixels = ButtonPixels(combatRect);
            Require(!quickDownPixels.AsSpan().SequenceEqual(combatHoverPixels), "quick Down differs from Hover in the viewport");
            await WaitForDownFeedback();
            Require(HeroInCombatMode && panel.FrameForSelfTest(AreaBarButton.Combat) == 2, "feedback settles to Hover with combat still on");

            await Button(combat, true);
            await WaitForDownFeedback();
            Require(HeroInCombatMode && panel.FrameForSelfTest(AreaBarButton.Combat) == 1, "holding stays Down without toggling combat");
            Require(ButtonPixels(combatRect).AsSpan().SequenceEqual(quickDownPixels), "the quick click drew the same pixels as held Down");
            await Button(combat, false);
            Require(!HeroInCombatMode && panel.FrameForSelfTest(AreaBarButton.Combat) == 2, "releasing a held press inside left combat and showed Hover");

            await Button(combat, true);
            var outsideCombat = combatRect.End + new Vector2(2, -combatRect.Size.Y / 2);
            await Move(outsideCombat);
            Require(panel.FrameForSelfTest(AreaBarButton.Combat) == 0, "dragging away while held shows Idle");
            await Button(outsideCombat, false);
            Require(!HeroInCombatMode && _gameBar.Held is null && panel.FrameForSelfTest(AreaBarButton.Combat) == 0,
                "releasing outside clears the hold and leaves combat off");
            Require(_gameBarSoundsForSelfTest.Count(sound => sound.Id == AreaGameBarLayout.ReleaseSound) == releasesBeforeCombat + 2,
                "only the two inside releases activated Combat, once each");
            GD.Print($"ARC-UI-120 native Combat feedback PASS art={(verifyGenerated ? "Generated" : verifyHd ? "HD" : "Legacy")} first-draw=Down held=Down settled=Hover canceled=Idle");
            await Click(AreaBarButton.Spells);
            if (SpellMapActive)
            {
                Require(_spellPane?.Shown == true && _gameBar.IsOn(AreaBarButton.Spells) && panel.FrameForSelfTest(AreaBarButton.Spells) == 1, "Spells showed the pane and stays down: " + _gameBarNotice);
                await Click(AreaBarButton.Spells);
                Require(_spellPane?.Shown != true && !_gameBar.IsOn(AreaBarButton.Spells), "a second press hid it");
            }
            else Require(!_gameBar.IsOn(AreaBarButton.Spells) && _gameBarNotice is not null, "Spells refused off a cast map: " + _gameBarNotice);
            var topStrip = panel.StripRectForSelfTest(AreaBarStrip.Top);
            var outsideTopButtons = topStrip.Position + new Vector2(720, 20) * panel.Factor;
            foreach (var (button, notice) in new[]
            {
                (AreaBarButton.FatePoints, "fate points: the window is not built"),
                (AreaBarButton.Sleep, "sleep: the window is not built"),
            })
            {
                var spec = AreaGameBarLayout.Spec(button);
                var artFrames = panel.ArtFrames(spec.Art);
                Require(artFrames is { Length: >= 2 }, $"{button} has a Down frame");
                var hoverFrame = AreaGameBarLayout.Frame(AreaBarButtonLook.Hovered, artFrames!.Length);
                var control = panel.GetNode<TextureRect>($"TopStrip/{button}");
                var rect = control.GetGlobalRect();
                var at = rect.GetCenter();
                string TextureSource()
                {
                    Require(control.Texture is not null, $"{button} has a button texture");
                    if (control.Texture is HdInterfaceTexture { IsHd: true }) return "HD";
                    return verifyHd ? "Legacy fallback" : "Legacy";
                }

                await Move(at);
                Require(panel.FrameForSelfTest(button) == hoverFrame, $"{button} starts at Hover");
                var hoverSource = TextureSource();
                var hoverPixels = ButtonPixels(rect);
                var soundsBefore = _gameBarSoundsForSelfTest.Count;
                var previousNotice = _gameBarNotice;
                SendButton(at, true);
                Require(_gameBar.Held == button && _gameBarNotice == previousNotice, $"{button} press only holds the button");
                SendButton(at, false);
                Require(_gameBar.Held is null && _gameBarNotice == notice, $"{button} release acts immediately");
                await Frames(1);
                var firstFrame = panel.FrameForSelfTest(button);
                Require(firstFrame == 1, $"{button} first draw after quick click is Down, got {firstFrame}");
                var downSource = TextureSource();
                var downPixels = ButtonPixels(rect);
                Require(!downPixels.AsSpan().SequenceEqual(hoverPixels), $"{button} Down differs from Hover in the viewport");
                await WaitForDownFeedback();
                Require(panel.FrameForSelfTest(button) == hoverFrame, $"{button} settles to Hover");

                await Button(at, true);
                await WaitForDownFeedback();
                Require(_gameBar.Held == button && panel.FrameForSelfTest(button) == 1, $"{button} stays Down while held");
                Require(TextureSource() == downSource && ButtonPixels(rect).AsSpan().SequenceEqual(downPixels),
                    $"{button} quick Down matches held Down in the viewport");
                await Button(at, false);
                Require(_gameBar.Held is null && _gameBarNotice == notice && panel.FrameForSelfTest(button) == hoverFrame,
                    $"{button} held release acts and shows Hover");

                await Button(at, true);
                await Move(outsideTopButtons);
                Require(panel.FrameForSelfTest(button) == 0, $"{button} goes Idle when dragged outside");
                var idleSource = TextureSource();
                await Button(outsideTopButtons, false);
                Require(_gameBar.Held is null && panel.FrameForSelfTest(button) == 0, $"{button} outside release stays Idle and clears the hold");
                Require(_gameBarSoundsForSelfTest.Skip(soundsBefore).Select(sound => sound.Id).SequenceEqual(new[]
                {
                    AreaGameBarLayout.PressSound, AreaGameBarLayout.ReleaseSound,
                    AreaGameBarLayout.PressSound, AreaGameBarLayout.ReleaseSound,
                    AreaGameBarLayout.PressSound,
                }), $"{button} plays one press sound per press and one release sound per inside release");
                GD.Print($"ARC-UI-121 native {button} PASS first-draw=Down ({downSource}) held=Down settled=Hover frame={hoverFrame} ({hoverSource}) canceled=Idle ({idleSource})");
            }

            await RunAreaMessageLogSmokeAsync(panel);

            // A press on the top strip right of every button (its clock) is the bar's, not the world's.
            var clock = outsideTopButtons;
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
            if (verifyHd) { _gameSettings = _gameSettings with { HdInterface = savedInterfaceHd, GeneratedInterface = savedGenerated }; SyncInterfaceHd(); }
            _gameBarForSelfTest = false; _messageClockShiftForSelfTest = 0;
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
        }
    }
}
