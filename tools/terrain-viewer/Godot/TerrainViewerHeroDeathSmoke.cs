using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // Native observation (ARC_AREA_MAP_SMOKE_ONLY=death, map mode on the start map with --main-menu true), on the wall clock: after a
    // new game as the first pre-made hero, the hero dies (killed through the session). Five seconds later the screen fades to black,
    // the installed rules\slide.mes gives the default death slide, whose picture (slide\death.bmp, 800 × 600) fades in while the
    // slides' music plays, then its voice. ARC-UI-028: the new game was saved first (into a temporary save folder), so under the
    // Steamwoven setting a box asks whether to load it; ARC-WORLD-120: the original's dialog box (DialogueBox art, Lil_Accept and
    // Lil_Cancel) over the faded-in slide, its music going on, offering the hero's own save although another hero's save is newer; the
    // pointer over Yes lights it, a left click on Yes loads the save while the slide fades out, and the loaded game, its hero alive,
    // fades in. The hero dies again under the Original setting, Escape ends the slide show and
    // the game resets without asking: the start map loads again with a fresh session without a hero and the main menu reopened,
    // which fades in. ARC_AREA_DEATH_CAPTURE=PATH.png saves the shown slide with the box (original art: keep it out of git).
    private async Task RunAreaHeroDeathSmokeAsync()
    {
        static void Require(bool value, string why) { if (!value) { GD.Print("ARC-UI-026 death smoke FAIL " + why); throw new InvalidOperationException("ARC-UI-026 death smoke: " + why); } }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused;
        _worldFocusSimulationForSelfTest = true; _worldFocused = true;
        var saves = System.IO.Directory.CreateTempSubdirectory("arc-ui-028-").FullName;
        _saveLibraryForSelfTest = new(saves);
        var savedInterfaceHd = _gameSettings.HdInterface;
        try
        {
            // ARC-UI-144: ARC_AREA_DEATH_HD_VERIFY=1 shows the box with the HD interface art of ARC_HD_PACK (the bar and sheet smokes' switch), for a native look at
            // the round buttons (a straight-alpha texture on the box's premultiplied canvas drew a square behind each); the setting is restored below.
            if (System.Environment.GetEnvironmentVariable("ARC_AREA_DEATH_HD_VERIFY") == "1")
            {
                Require(_canvas?.HdSource?.Pack is not null, "HD verification needs ARC_HD_PACK");
                _gameSettings = _gameSettings with { HdInterface = true };
                SyncInterfaceHd();
                Require(InterfaceHd.Enabled, "HD interface switch enabled for this isolated smoke");
            }
            async Task Frames(int count)
            {
                for (var i = 0; i < count; i++)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                }
            }
            async Task<long> Until(Func<bool> done, int seconds, string what)
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                while (!done())
                {
                    Require(clock.Elapsed.TotalSeconds < seconds, $"{what} within {seconds} s (stage {_heroDeath.Stage}, {_heroDeathStatus})");
                    await Frames(1);
                }
                return clock.ElapsedMilliseconds;
            }
            void Click(Vector2 at)
            {
                GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at, ButtonMask = MouseButtonMask.Left });
                GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
            }
            void Key(Key key, char unicode = '\0')
            {
                InputWorld(new InputEventKey { Keycode = key, PhysicalKeycode = key, Unicode = unicode, Pressed = true });
                InputWorld(new InputEventKey { Keycode = key, PhysicalKeycode = key, Unicode = unicode, Pressed = false });
            }
            void Choose(int message)
            {
                var button = _mainMenu!.Shown.FirstOrDefault(shown => shown.Message == message) ?? throw new InvalidOperationException("ARC-UI-026 death smoke: no button " + message);
                var letter = _mainMenu.Texts.Hotkey(button) ?? throw new InvalidOperationException("ARC-UI-026 death smoke: no letter for " + message);
                Key(letter is >= 'A' and <= 'Z' ? (Key)letter : Godot.Key.None, char.ToLowerInvariant(letter));
            }

            Require(_arguments?.MainMenu == true && MainMenuOpen && _mainMenu!.Window == AreaMenuWindow.Main && CurrentAreaMap == 1,
                "the menu is open over the start map (run with --main-menu true)");
            var start = _readyMapName;
            Choose(10); Choose(50); Choose(420);
            MainMenuCommand(() => _mainMenu!.Start());
            await Frames(3);
            Require(!MainMenuOpen && _areaSession?.QueryHero() is not null, "a new game began with a hero: " + _quickSaveStatus);
            var game = _areaSession!;
            // The box is the Steamwoven setting's, whatever the stored rule preference is.
            Require(game.SelectRules(RuleMode.Steamwoven) != GameSessionDisposition.Rejected, "Steamwoven selected");
            var saved = SaveGame(GameSaveLibrary.AutoName, GameSaveLibrary.AutoDescription, null, "smoke ");
            Require(saved is not null && SaveLibrary.Latest()?.Name == saved, "the new game saved: " + _quickSaveStatus);
            // ARC-WORLD-120: a newer save of another hero is not offered.
            {
                var grid = (AreaGridSession)_worldActor.Session!;
                var cell = grid.Chart.ToWorld(grid.State.CommittedCell);
                var other = AreaQuickSave.Save(game, new ActorPlacement(new(CurrentAreaMap), new(checked((int)cell.X), checked((int)cell.Y)), grid.State.Facing), SaveLibrary,
                    null, session => AreaSaveList.Summary(session, "another hero", SaveLocation()) with { HeroName = "Another Hero" });
                Require(other.Saved, "another hero's save written: " + other.Status);
                System.IO.File.SetLastWriteTimeUtc(SaveLibrary.Slot(other.Library!.Name!).Path, DateTime.UtcNow.AddMinutes(1));
                Require(SaveLibrary.Latest()?.Name == other.Library.Name && DeadHeroSave() == saved, "the newest save is another hero's, the hero's own is found");
            }
            _heroDeathForSelfTest = true;
            await Frames(2);
            Require(_heroDeath.Stage == AreaDeathStage.Watching, "a living hero starts nothing");
            Require(game.Kill(game.Version, SessionLifeTarget.Hero).Disposition == GameSessionDisposition.Changed, "kill the hero");
            await Until(() => _heroDeath.Stage == AreaDeathStage.Waiting, 2, "the death waits");
            var waited = await Until(() => _heroDeath.Stage != AreaDeathStage.Waiting, 8, "the death came");
            Require(waited >= AreaHeroDeath.DeathDelay - 200 && _heroDeath.Stage == AreaDeathStage.FadeOut && WorldModal, $"after 5 s the screen fades to black ({waited} ms)");
            // ARC-DEV-126 (MIG12 S7, TL25): the cover and its slide show are frozen frames, not a pause: the production tick at literal stamps observes none,
            // so no clock of the session moves, and the session holds no pause for it.
            var coverGame = game.QueryClockSnapshot().GameTimestampMs; var coverReal = game.QueryFrameClocks().RealMs;
            for (var i = 0; i < 6; i++) TickAreaClock(40_000_000L + i * 250);
            Require(HeroDeathCovering && !game.QueryClockSnapshot().PauseDisposition.Paused && game.QueryClockSnapshot().GameTimestampMs == coverGame
                && game.QueryFrameClocks().RealMs == coverReal && _frameTap is { Flags.Frozen: true },
                $"the cover is a frozen frame: no clock moved and no pause was raised (game {game.QueryClockSnapshot().GameTimestampMs - coverGame} ms, real {game.QueryFrameClocks().RealMs - coverReal} ms)");
            var slides = DeathSlides();
            Require(slides.Loaded && _heroDeath.Queue.SequenceEqual([AreaHeroDeath.DefaultSlide]), "the installed slide table queues the default slide: " + slides.Detail);
            await Until(() => _heroDeath.Stage == AreaDeathStage.SlideIn, 4, "the slide's picture");
            var slide = _heroDeath.Slide;
            Require(_heroDeathPanel!.PictureSizeForSelfTest == new Vector2I(800, 600) && _deathMusic is not null && slide is { Id: AreaHeroDeath.DefaultSlide },
                $"the 800 × 600 death picture shows with the slides' music ({_heroDeathPanel.PictureSizeForSelfTest})");
            await Until(() => _heroDeath.Stage == AreaDeathStage.Asking, 4, "the slide faded in and the box asks");
            Require(_heroDeathPanel.Veil == 0, "clear after the fade in");
            var voice = _deathVoice is { } handle && _audio?.Volume(handle) is not null;
            // ARC-WORLD-120: the original's dialog box asks over the shown slide at once, with the hero's own save.
            Require(_heroDeath.Stage == AreaDeathStage.Asking && _heroDeathPanel.AsksInArt && _heroDeathPanel.PictureShown && _deathMusic is not null
                && _heroDeath.OfferedSave == saved && _heroDeathPanel.DialogForSelfTest.TextForSelfTest.StartsWith(AreaHeroDeath.LoadQuestion + "\n", StringComparison.Ordinal)
                && _heroDeathPanel.DialogForSelfTest.LinesForSelfTest >= 2, $"the dialog box asks over the slide ({_heroDeath.Stage}, art {_dialogBoxContent?.Detail})");
            await Frames(2);
            if (System.Environment.GetEnvironmentVariable("ARC_AREA_DEATH_CAPTURE") is { Length: > 0 } capture)
                GetViewport().GetTexture().GetImage().SavePng(capture);
            var yes = _heroDeathPanel.ButtonCentreForSelfTest(true)!.Value;
            Require(_heroDeathPanel.DialogForSelfTest.ButtonFrameForSelfTest(true) == 0, "Yes idle");
            GetViewport().PushInput(new InputEventMouseMotion { Position = yes, GlobalPosition = yes });
            await Frames(1);
            Require(_heroDeathPanel.DialogForSelfTest.ButtonFrameForSelfTest(true) == 2 && _heroDeathPanel.DialogForSelfTest.ButtonFrameForSelfTest(false) == 0,
                "the pointer over Yes lights it (frame 2)");
            Click(yes);
            await Frames(1);
            Require(_heroDeath.Stage is AreaDeathStage.Loading or AreaDeathStage.MenuFadeIn && _heroDeath.Loaded, "the click on Yes loads: " + _heroDeathStatus);
            await Until(() => _heroDeath.Stage == AreaDeathStage.Watching, 30, "the loaded game faded in");
            var loaded = _areaSession!;
            Require(!ReferenceEquals(game, loaded) && loaded.QueryHeroLife()?.Status.IsDead == false && !MainMenuOpen && !_heroDeathPanel.IsOpen && !WorldModal
                && _readyMapName == start, "the save loaded with its living hero and play goes on: " + _quickSaveStatus);

            // Under Original the death resets without asking (PD8), a save there or not; Escape ends the show.
            Require(loaded.SelectRules(RuleMode.Original) == GameSessionDisposition.Changed, "Original selected");
            Require(loaded.Kill(loaded.Version, SessionLifeTarget.Hero).Disposition == GameSessionDisposition.Changed, "kill the loaded hero");
            await Until(() => _heroDeath.Stage == AreaDeathStage.Slide, 14, "the second death's slide");
            InputWorld(new InputEventKey { Keycode = Godot.Key.Escape, PhysicalKeycode = Godot.Key.Escape, Pressed = true });
            InputWorld(new InputEventKey { Keycode = Godot.Key.Escape, PhysicalKeycode = Godot.Key.Escape, Pressed = false });
            game = loaded;
            await Until(() => _heroDeath.Stage is AreaDeathStage.Reset or AreaDeathStage.MenuFadeIn, 4, "the slide faded out and the game resets without the box");
            await (_lastLoad ?? Task.CompletedTask);
            await Until(() => _heroDeath.Stage == AreaDeathStage.Watching, 30, "the main menu faded in");
            Require(_readyMapName == start && MainMenuOpen && _mainMenu!.Window == AreaMenuWindow.Main && _mainMenu.Reopened
                && !ReferenceEquals(game, _areaSession) && _areaSession!.QueryHero() is null && !_heroDeathPanel.IsOpen && _deathMusic is null,
                "the start map loaded again with a fresh session and the main menu reopened");
            GD.Print("ARC-UI-026 native-smoke death PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Start = start, Waited = waited, Slides = slides.Detail, Slide = slide!.Image, slide.Voice, VoiceStarted = voice, Box = _dialogBoxContent?.Detail,
                LoadedSave = saved, Revision = _areaSession!.Version.Revision }));
            GD.Print("ARC-UI-028 native-smoke death load PASS " + System.Text.Json.JsonSerializer.Serialize(new { Saved = saved,
                Rules = "steamwoven asks in the original's dialog box over the slide, this hero's save only; original resets" }));
        }
        finally
        {
            // A smoke that failed part-way must not leave a covering death for a later stage.
            if (HeroDeathCovering) { GD.Print($"ARC-UI-026 death smoke: dropped a death at stage {_heroDeath.Stage}"); StopHeroDeath(); }
            _heroDeathForSelfTest = false;
            if (_gameSettings.HdInterface != savedInterfaceHd) { _gameSettings = _gameSettings with { HdInterface = savedInterfaceHd }; SyncInterfaceHd(); }
            if (_mainMenu is not null) EndMainMenu("smoke end");
            _quickKeys.Reset();
            _saveLibraryForSelfTest = null;
            try { System.IO.Directory.Delete(saves, recursive: true); }
            catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException) { GD.Print("ARC-UI-028 death smoke: temporary saves not removed: " + exception.GetType().Name); }
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
        }
    }
}
