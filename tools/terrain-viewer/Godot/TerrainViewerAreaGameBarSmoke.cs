using Arcanum.Session;
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
    // ARC-UI-1111: with ARC_AREA_BAR_CAPTURE=PREFIX saves the top strip as PREFIX-clock-before.png (the clock window hidden: the strip's art as
    // it was) and PREFIX-clock-after.png (ignored local files, original-derived, for the graphics report; never committed).
    private async Task CaptureClockAsync(AreaGameBarPanel panel, Func<int, Task> frames)
    {
        if (System.Environment.GetEnvironmentVariable("ARC_AREA_BAR_CAPTURE") is not { Length: > 0 } capture) return;
        var strip = panel.StripRectForSelfTest(AreaBarStrip.Top);
        foreach (var (shown, name) in new[] { (false, "before"), (true, "after") })
        {
            panel.ClockShownForSelfTest = shown;
            await frames(3);
            var path = System.IO.Path.GetFullPath(capture) + $"-clock-{name}.png";
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            using var image = GetViewport().GetTexture().GetImage();
            using var top = image?.GetRegion(new Rect2I((int)strip.Position.X, (int)strip.Position.Y, (int)strip.Size.X, (int)strip.Size.Y));
            if (top is null || top.SavePng(path) != Error.Ok) throw new InvalidOperationException("ARC-UI-1111 clock capture not saved: " + path);
            GD.Print($"ARC-UI-1111 clock capture {name} {top.GetWidth()}x{top.GetHeight()}");
        }
        panel.ClockShownForSelfTest = true;
        await frames(2);
    }

    // ARC-UI-1111: with ARC_AREA_BAR_CAPTURE also saves the top strip at three later game hours (PREFIX-clock-at-HHMM.png) for the report's
    // variants; the clock refreshes at each jump. Runs after the checks, so a game hour jump here changes nothing they read.
    private async Task CaptureClockTimesAsync(AreaGameBarPanel panel, GameSessionOwner session, Func<int, Task> frames)
    {
        if (System.Environment.GetEnvironmentVariable("ARC_AREA_BAR_CAPTURE") is not { Length: > 0 } capture) return;
        var strip = panel.StripRectForSelfTest(AreaBarStrip.Top);
        foreach (var hours in new[] { 5, 8, 7 })
        {
            session.JumpClocks(session.QueryClockSnapshot().Token, hours * AreaClockLayout.RefreshMs);
            await frames(3);
            var stamp = AreaClockText.Time(session.QueryClockSnapshot().GameTimestampMs)[..5].Replace(":", "");
            var path = System.IO.Path.GetFullPath(capture) + $"-clock-at-{stamp}.png";
            using var image = GetViewport().GetTexture().GetImage();
            using var top = image?.GetRegion(new Rect2I((int)strip.Position.X, (int)strip.Position.Y, (int)strip.Size.X, (int)strip.Size.Y));
            if (top is null || top.SavePng(path) != Error.Ok) throw new InvalidOperationException("ARC-UI-1111 clock capture not saved: " + path);
            GD.Print($"ARC-UI-1111 clock capture at {stamp} face={_gameClock.Face}");
        }
    }

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
            // ARC-UI-1364: a new game lights nothing (what the session starts with is the baseline, not an event).
            Require(new[] { AreaLightSlot.Character, AreaLightSlot.Logbook, AreaLightSlot.Inventory, AreaLightSlot.WorldMap }.All(slot => !_gameBar.Lights.IsLit(slot)),
                "a new game starts with every button idle");
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

            // ARC-UI-1111 (GB8-GB11): the clock window draws the game's sky and the needle, the click on it writes the time and the date as a
            // hover entry (silently), leaving it clears the entry, and the window refreshes an hour of game time after its last refresh.
            {
                var clockSession = _areaSession!;
                var (clockSlices, clockNeedle, clockMoonArt) = panel.ClockForSelfTest;
                var clockFace = _gameClock.Face;
                Require(clockFace is not null && clockSlices.Count is >= 1 and <= 3 && clockSlices.Sum(slice => slice.Width) == AreaClockLayout.WindowWidth && clockNeedle && clockMoonArt == clockFace.Value.MoonArt,
                    $"the clock window draws {clockSlices.Count} sky slices and the needle: face {clockFace}");
                var clockRect = panel.ClockRectForSelfTest;
                Require(Math.Abs(clockRect.Size.X - AreaClockLayout.WindowWidth * panel.Factor) < 0.5f && Math.Abs(clockRect.Size.Y - AreaClockLayout.WindowHeight * panel.Factor) < 0.5f
                    && Math.Abs(clockRect.Position.X - (panel.StripRectForSelfTest(AreaBarStrip.Top).Position.X + AreaClockLayout.WindowX * panel.Factor)) < 0.5f,
                    $"the clock window stands at (648, 5) of the strip, 128 x 30: {clockRect}");
                await CaptureClockAsync(panel, Frames);
                // The pixels: the sky is not left black. The window is read back from the viewport after a frame.
                await Frames(2);
                using (var clockShot = GetViewport().GetTexture().GetImage())
                {
                    Require(clockShot is not null, "the viewport can be read back");
                    var clockLit = 0; var clockTotal = 0;
                    for (var y = (int)clockRect.Position.Y; y < (int)clockRect.End.Y; y++)
                        for (var x = (int)clockRect.Position.X; x < (int)clockRect.End.X; x++)
                        {
                            clockTotal++;
                            var pixel = clockShot!.GetPixel(x, y);
                            if (pixel.R + pixel.G + pixel.B > 0.06f) clockLit++;
                        }
                    Require(clockLit > clockTotal / 8, $"the clock window shows the sky: {clockLit} of {clockTotal} pixels are not dark");
                }
                _messageClockShiftForSelfTest += AreaMessageLog.QuietMilliseconds + 1;
                ClockDateOrderForSelfTest = AreaClockDateOrder.DayMonthYear;
                _gameBarSoundsForSelfTest.Clear();
                var clockAt = panel.ClockCentreForSelfTest;
                await Move(clockAt);
                Require(_gameBar.Hovered == AreaBarButton.Clock && panel.HoverTextForSelfTest == "", $"the pointer over the clock writes no name: hovered {_gameBar.Hovered}, text '{panel.HoverTextForSelfTest}'");
                var clockBefore = clockSession.QueryClockSnapshot();
                await Button(clockAt, true);
                Require(_gameBar.Held == AreaBarButton.Clock && ClockLineForSelfTest is null, "a press on the clock writes nothing yet");
                await Button(clockAt, false);
                var clockAfter = clockSession.QueryClockSnapshot();
                var clockTexts = _gameBarContent!.Texts;
                var clockLines = Enumerable.Range((int)(clockBefore.GameTimestampMs / 1000), (int)(clockAfter.GameTimestampMs / 1000 - clockBefore.GameTimestampMs / 1000) + 1)
                    .Select(second => AreaClockText.Line(clockTexts.Text(AreaClockLayout.TimeMessage), clockTexts.Text(AreaClockLayout.DateMessage), second * 1000L,
                        clockAfter.ProfileBinding.StartYear, AreaClockDateOrder.DayMonthYear)).ToList();
                Require(ClockLineForSelfTest is { } clockLine && clockLines.Contains(clockLine), $"the click wrote the time and date: '{ClockLineForSelfTest}' (expected one of {clockLines.Count} lines such as '{clockLines[0]}')");
                Require(panel.HoverTextForSelfTest == ClockLineForSelfTest, $"the message window shows it: '{panel.HoverTextForSelfTest}'");
                Require(_gameBarSoundsForSelfTest.Count == 0, "the clock button is silent: " + Sounds());
                GD.Print($"ARC-UI-1111 clock smoke line='{ClockLineForSelfTest}' face={clockFace} slices={string.Join(",", clockSlices.Select(slice => $"{slice.Piece}@{slice.SourceX}+{slice.Width}"))}");
                await Move(new Vector2(clockAt.X, panel.StripRectForSelfTest(AreaBarStrip.Top).End.Y + 40));
                Require(_gameBar.Hovered is null && panel.HoverTextForSelfTest == "", "leaving the clock takes the entry away: '" + panel.HoverTextForSelfTest + "'");
                ClockDateOrderForSelfTest = null;
                // The checks after this block run an hour of game time on. An hour of game time later the Clock event is due and the window refreshes once (a missed period is not replayed).
                var clockRefreshes = _gameClock.Refreshes;
                var clockJumped = clockSession.JumpClocks(clockSession.QueryClockSnapshot().Token, AreaClockLayout.RefreshMs + 1);
                Require(clockJumped.Disposition == Arcanum.Simulation.Clock.ClockMutationDisposition.Changed, "an hour of game time passes");
                await Frames(2);
                Require(_gameClock.Refreshes == clockRefreshes + 1 && _gameClock.Face!.Value.RefreshedAtMs >= clockFace!.Value.RefreshedAtMs + AreaClockLayout.RefreshMs
                    && panel.ClockForSelfTest.MoonArt == _gameClock.Face.Value.MoonArt, $"the clock refreshed once after an hour: refreshes {_gameClock.Refreshes} (was {clockRefreshes}), face {_gameClock.Face}");
                await CaptureClockTimesAsync(panel, clockSession, Frames);
            }

            // ARC-UI-1365 (GB13, GB14): the experience strip under the hot-key bar draws the pieces the hero's progress asks of its table
            // (none at the start of a level), follows an award of experience, and shows on the pixels. A level is not reached here.
            {
                var xpSession = _areaSession!;
                async Task<Image> XpShotAsync(bool shown)
                {
                    panel.ExperienceShownForSelfTest = shown;
                    await Frames(3);
                    var shot = GetViewport().GetTexture().GetImage();
                    Require(shot is not null, "the viewport can be read back");
                    return shot!;
                }
                int XpChanged(Image hidden, Image shown)
                {
                    var rect = panel.StripRectForSelfTest(AreaBarStrip.Bottom);
                    var changed = 0;
                    for (var y = (int)(rect.Position.Y + AreaExperienceStripLayout.ClearY * panel.Factor); y < (int)(rect.Position.Y + (AreaExperienceStripLayout.ClearY + 30) * panel.Factor); y++)
                        for (var x = (int)(rect.Position.X + AreaExperienceStripLayout.ClearX * panel.Factor); x < (int)(rect.Position.X + (AreaExperienceStripLayout.ClearX + AreaExperienceStripLayout.ClearWidth) * panel.Factor); x++)
                        {
                            var a = hidden.GetPixel(x, y); var b = shown.GetPixel(x, y);
                            if (Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) > 0.05f) changed++;
                        }
                    return changed;
                }
                void XpCheck(string when)
                {
                    var view = xpSession.QueryHeroExperience();
                    Require(view is { Progress: not null }, $"the hero's progress toward the next level is known ({when})");
                    var expected = AreaExperienceStripLayout.Pieces(view!.Progress);
                    var drawn = panel.ExperienceForSelfTest;
                    Require(drawn.Count == expected.Count && drawn.Select(d => d.Piece).SequenceEqual(expected), $"the strip draws {string.Join(",", expected)} at progress {view.Progress} ({when}), drew {drawn.Count}");
                    foreach (var (piece, width) in drawn)
                    {
                        var art = panel.ArtFrame(piece.Art);
                        Require(art is not null && width == (piece.WidthPercent >= 100 ? art.GetWidth() : AreaExperienceStripLayout.CutWidth(art.GetWidth(), piece.WidthPercent)), $"piece art {piece.Art} drew {width} px ({when})");
                    }
                }
                var xpStart = xpSession.QueryHeroExperience();
                Require(xpStart is { Progress: not null, ToNextLevel: > 20 }, $"the hero's progress is known with a next level ahead: {xpStart}");
                var xpStep = xpStart!.ToNextLevel!.Value;
                XpCheck("start");
                var xpHiddenAtStart = await XpShotAsync(false);
                var xpZero = await XpShotAsync(true);
                // A hero that starts a level has nothing to draw; one that starts with experience is checked by XpCheck alone.
                if (xpStart.Progress == 0) Require(panel.ExperienceForSelfTest.Count == 0 && XpChanged(xpHiddenAtStart, xpZero) == 0, "the strip adds nothing at progress 0");
                string? xpCapture = System.Environment.GetEnvironmentVariable("ARC_AREA_BAR_CAPTURE") is { Length: > 0 } c ? System.IO.Path.GetFullPath(c) : null;
                void XpSave(Image image, string name)
                {
                    if (xpCapture is null) return;
                    var rect = panel.StripRectForSelfTest(AreaBarStrip.Bottom);
                    var path = xpCapture + $"-xp-{name}.png";
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                    using var region = image.GetRegion(new Rect2I((int)rect.Position.X, (int)rect.Position.Y, (int)rect.Size.X, (int)rect.Size.Y));
                    Require(region.SavePng(path) == Error.Ok, "ARC-UI-1365 capture saved: " + path);
                    GD.Print($"ARC-UI-1365 xp capture {name} {region.GetWidth()}x{region.GetHeight()}");
                }
                XpSave(xpHiddenAtStart, "before");
                var awarded = xpSession.AwardHeroExperience(xpSession.Version, xpStep / 2);
                Require(awarded.Disposition == GameSessionDisposition.Changed, "experience is awarded: " + awarded.Disposition);
                await Frames(2);
                XpCheck("half");
                var xpHalf = xpSession.QueryHeroExperience()!;
                Require(xpHalf.Progress is > 0 and < 1000 && xpHalf.Level == xpStart.Level, $"half a level reached no level: {xpHalf}");
                var xpHidden = await XpShotAsync(false);
                var xpShown = await XpShotAsync(true);
                var xpChanged = XpChanged(xpHidden, xpShown);
                Require(xpChanged > 200, $"the strip shows on the pixels at progress {xpHalf.Progress}: {xpChanged} pixels differ");
                XpSave(xpShown, $"after-{xpHalf.Progress}");
                // Difficulty changes an award (easy adds half): three tenths of the remainder keeps every difficulty below the next level.
                var more = xpSession.QueryHeroExperience()!.ToNextLevel!.Value * 3 / 10;
                if (more > 0 && xpSession.AwardHeroExperience(xpSession.Version, more).Disposition == GameSessionDisposition.Changed)
                {
                    await Frames(2);
                    var xpNear = xpSession.QueryHeroExperience()!;
                    Require(xpNear.Level == xpStart.Level, $"no level is reached late in the step: {xpNear}");
                    XpCheck("late in the step");
                    XpSave(await XpShotAsync(true), $"after-{xpNear.Progress}");
                }
                GD.Print($"ARC-UI-1365 experience strip PASS step={xpStep} half={xpHalf.Progress} pieces={string.Join(",", panel.ExperienceForSelfTest.Select(d => $"{d.Piece.Art}x{d.Width}"))}");
                panel.ExperienceShownForSelfTest = true;
                await Frames(2);
            }

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
                    await WaitForSettled(button, hoverFrame);
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
            // ARC-UI-2306: the world map window lies between the strips (800 × 400 at (0, 41)), so the bars stay up while it is open (its note entry runs on the bottom strip).
            await Click(AreaBarButton.Map);
            await Frames(1);
            Require(WorldMapOpen && _gameBarPanel!.Visible, "Map opened the world map and the bars stayed up: " + _gameBarNotice);
            CloseWorldMapByPlayer();
            await Frames(1);
            Require(!WorldMapOpen && _gameBarPanel!.Visible, "the bars are up after the map closed");
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
            // ARC-DEV-1193: the panel keeps Down for DownMinimumMs counted from the first frame actually drawn, and the draw can come
            // late on a loaded machine, so a fixed wait after a quick click is not enough. Wait for the settled frame itself (frames
            // keep running); a Down that never ends, or a minimum stretched past the limit, still fails the check.
            async Task WaitForSettled(AreaBarButton button, int frame)
            {
                var started = Time.GetTicksMsec();
                while (panel.FrameForSelfTest(button) != frame && Time.GetTicksMsec() - started < 2000) await Frames(1);
                if (Time.GetTicksMsec() - started >= 200) GD.Print($"ARC-DEV-1193 bar smoke: {button} took {Time.GetTicksMsec() - started} ms to show frame {frame} (a slow draw, waited for)");
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
            await WaitForSettled(AreaBarButton.Combat, 2);
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
            // ARC-UI-1112: the Fate Point window opens in no other interface window (the inventory of the steps above is still open).
            if (AnyWindowOpen) { CloseOtherInterfaceWindows(); await Frames(2); }
            for (var wait = 0; wait < 600 && !FateArtReadyForSelfTest; wait++) await Frames(1);
            Require(FateArtReadyForSelfTest, "the Fate Point window's art read: " + _fateContent?.Detail);
            var topStrip = panel.StripRectForSelfTest(AreaBarStrip.Top);
            var outsideTopButtons = topStrip.Position + new Vector2(720, 20) * panel.Factor;
            foreach (var (button, notice) in new (AreaBarButton, string?)[]
            {
                (AreaBarButton.FatePoints, null), // ARC-UI-1112: the button toggles the Fate Point window (a notice only without a living hero)
                (AreaBarButton.Sleep, null), // ARC-WORLD-1160: the button toggles the sleep window (a notice only without a session hero)
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
                var sleepWas = SleepOpen;
                var fateWas = FateOpen;
                bool Acted(bool opened) => notice is not null ? _gameBarNotice == notice
                    : button == AreaBarButton.FatePoints ? FateOpen == (opened ? !fateWas : fateWas) : SleepOpen == (opened ? !sleepWas : sleepWas);
                SendButton(at, true);
                Require(_gameBar.Held == button && _gameBarNotice == previousNotice, $"{button} press only holds the button");
                SendButton(at, false);
                Require(_gameBar.Held is null && Acted(true), $"{button} release acts immediately");
                await Frames(1);
                var firstFrame = panel.FrameForSelfTest(button);
                Require(firstFrame == 1, $"{button} first draw after quick click is Down, got {firstFrame}");
                var downSource = TextureSource();
                var downPixels = ButtonPixels(rect);
                Require(!downPixels.AsSpan().SequenceEqual(hoverPixels), $"{button} Down differs from Hover in the viewport");
                await WaitForSettled(button, hoverFrame);
                Require(panel.FrameForSelfTest(button) == hoverFrame, $"{button} settles to Hover");

                await Button(at, true);
                await WaitForDownFeedback();
                Require(_gameBar.Held == button && panel.FrameForSelfTest(button) == 1, $"{button} stays Down while held");
                Require(TextureSource() == downSource && ButtonPixels(rect).AsSpan().SequenceEqual(downPixels),
                    $"{button} quick Down matches held Down in the viewport");
                await Button(at, false);
                Require(_gameBar.Held is null && Acted(notice is not null) && panel.FrameForSelfTest(button) == hoverFrame,
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

            await RunAreaFateSmokeAsync(panel, Frames, Move, Button, pressed => Key(pressed), Click);

            await RunAreaMessageLogSmokeAsync(panel);

            // A press on the top strip right of every button (its clock) is the bar's, not the world's.
            var clock = outsideTopButtons;
            var before = _worldRouteClick;
            await Move(clock);
            Require(panel.HoverTextForSelfTest == "", "leaving the buttons cleared the hover name");
            await Button(clock, true); await Button(clock, false);
            Require(ReferenceEquals(before, _worldRouteClick), "the world took no press from the strip: " + _worldRouteClick);
            Require(_gameBarSoundsForSelfTest.All(sound => sound.Started), "every bar sound started: " + Sounds());
            // ARC-UI-1364: last, as its level-up and quest writes change the hero and the message window.
            await RunAreaBarLightsSmokeAsync(panel, Frames, Click);
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
