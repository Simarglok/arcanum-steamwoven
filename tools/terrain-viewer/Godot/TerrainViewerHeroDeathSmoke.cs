using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // Native observation (ARC_AREA_MAP_SMOKE_ONLY=death, map mode on the start map with --main-menu true), on the wall clock: after a
    // new game as the first pre-made hero, the hero dies (killed through the session). Five seconds later the screen fades to black,
    // the installed rules\slide.mes gives the default death slide, whose picture (slide\death.bmp, 800 × 600) fades in while the
    // slides' music plays, then its voice; a left press through the window ends it. The game resets: the start map loads again with
    // a fresh session without a hero and the main menu reopened, which fades in. ARC_AREA_DEATH_CAPTURE=PATH.png saves the shown
    // slide (original art: keep it out of git).
    private async Task RunAreaHeroDeathSmokeAsync()
    {
        static void Require(bool value, string why) { if (!value) { GD.Print("ARC-UI-026 death smoke FAIL " + why); throw new InvalidOperationException("ARC-UI-026 death smoke: " + why); } }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused;
        _worldFocusSimulationForSelfTest = true; _worldFocused = true;
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
            _heroDeathForSelfTest = true;
            await Frames(2);
            Require(_heroDeath.Stage == AreaDeathStage.Watching, "a living hero starts nothing");
            Require(game.Kill(game.Version, SessionLifeTarget.Hero).Disposition == GameSessionDisposition.Changed, "kill the hero");
            await Until(() => _heroDeath.Stage == AreaDeathStage.Waiting, 2, "the death waits");
            var waited = await Until(() => _heroDeath.Stage != AreaDeathStage.Waiting, 8, "the death came");
            Require(waited >= AreaHeroDeath.DeathDelay - 200 && _heroDeath.Stage == AreaDeathStage.FadeOut && WorldModal, $"after 5 s the screen fades to black ({waited} ms)");
            var slides = DeathSlides();
            Require(slides.Loaded && _heroDeath.Queue.SequenceEqual([AreaHeroDeath.DefaultSlide]), "the installed slide table queues the default slide: " + slides.Detail);
            await Until(() => _heroDeath.Stage == AreaDeathStage.SlideIn, 4, "the slide's picture");
            var slide = _heroDeath.Slide;
            Require(_heroDeathPanel!.PictureSizeForSelfTest == new Vector2I(800, 600) && _deathMusic is not null && slide is { Id: AreaHeroDeath.DefaultSlide },
                $"the 800 × 600 death picture shows with the slides' music ({_heroDeathPanel.PictureSizeForSelfTest})");
            await Until(() => _heroDeath.Stage == AreaDeathStage.Slide, 4, "the slide faded in");
            Require(_heroDeathPanel.Veil == 0, "clear after the fade in");
            var voice = _deathVoice is { } handle && _audio?.Volume(handle) is not null;
            if (System.Environment.GetEnvironmentVariable("ARC_AREA_DEATH_CAPTURE") is { Length: > 0 } capture)
                GetViewport().GetTexture().GetImage().SavePng(capture);
            // The voice may end the slide on its own; otherwise a left press through the window ends it.
            await Frames(2);
            var clicked = false;
            if (_heroDeath.Stage == AreaDeathStage.Slide)
            {
                var at = GetViewport().GetVisibleRect().GetCenter();
                GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at, ButtonMask = MouseButtonMask.Left });
                GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
                await Frames(1);
                clicked = true;
                Require(_heroDeath.Stage == AreaDeathStage.SlideOut && _deathVoice is null, "the press ended the slide and its voice");
            }
            await Until(() => _heroDeath.Stage is AreaDeathStage.Reset or AreaDeathStage.MenuFadeIn, 4, "the slide faded out and the game resets");
            await (_lastLoad ?? Task.CompletedTask);
            await Until(() => _heroDeath.Stage == AreaDeathStage.Watching, 30, "the main menu faded in");
            Require(_readyMapName == start && MainMenuOpen && _mainMenu!.Window == AreaMenuWindow.Main && _mainMenu.Reopened
                && !ReferenceEquals(game, _areaSession) && _areaSession!.QueryHero() is null && !_heroDeathPanel.IsOpen && _deathMusic is null,
                "the start map loaded again with a fresh session and the main menu reopened");
            GD.Print("ARC-UI-026 native-smoke death PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Start = start, Waited = waited, Slides = slides.Detail, Slide = slide!.Image, slide.Voice, VoiceStarted = voice, Clicked = clicked,
                Revision = _areaSession!.Version.Revision }));
        }
        finally
        {
            _heroDeathForSelfTest = false;
            if (_mainMenu is not null) EndMainMenu("smoke end");
            _quickKeys.Reset();
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
        }
    }
}
