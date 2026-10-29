using System.Reflection;
using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.TerrainViewer.Core;
using Arcanum.TerrainViewer.Tests;
using Godot;

namespace Arcanum.TerrainViewer;

public static partial class TerrainViewerSelfTest
{
    /// <summary>ARC-UI-026 over the strict synthetic AREA owner in the production app, with a synthetic slide table, picture and
    /// voice; the start scene "loads" by publishing the synthetic scene again (as the scene flow self-test): a new game's hero
    /// dies; 5 s later the open in-play menu closes and the death comes again 0.3 s later; the screen fades to black, owning the
    /// input (Escape opens no menu), the slide fades in and a key ends it; the game resets to the reopened main menu, which fades
    /// in. A second death whose hero lives again when it fires does nothing; a third ends its slide when the voice ends.
    /// ARC-UI-028: with a save in the list the Steamwoven setting asks in a box: Y loads the newest save, which fades in; a corrupt save
    /// keeps the box with its refusal; Escape resets; the box's No button resets; Original resets without asking. ARC-WORLD-120: the box
    /// asks over the faded-in slide (No fades it out first, Yes while the save loads), and only a save of the dead hero is offered.</summary>
    private static async Task AssertAreaHeroDeathAsync(Node host)
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
            app.PrepareMainMenuForSelfTest();
            app.PrepareSceneFlowForSelfTest(arguments, result);
            app.PublishSyntheticForSelfTest(arguments, result);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            summary = app.AreaHeroDeathForSelfTest();
        }
        finally
        {
            app.ClearSyntheticForSelfTest();
            viewport.Free();
        }
        GD.Print($"ARC-UI-026 self-test PASS synthetic hero death ({summary})");
    }
}

public partial class TerrainViewerApp
{
    internal string AreaHeroDeathForSelfTest()
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("AREA hero death: " + why); }
        if (!SelfTestBootstrap || _readyArea is null || _areaSession is null || _mainMenuDirectoryForSelfTest is null)
            throw new InvalidOperationException("AREA_HERO_DEATH_READY_REQUIRED");
        var simulatedFocus = _worldFocusSimulationForSelfTest;
        _worldFocusSimulationForSelfTest = true; _worldFocused = true;
        var voicePlaying = false;
        var voices = 0;
        _heroDeathForSelfTest = true;
        _deathSlidesForSelfTest = AreaSlideTable.From(id => id == AreaHeroDeath.DefaultSlide ? "death.bmp DeathSound.mp3" : null, "synthetic");
        _deathPictureForSelfTest = slide => slide.Image == "slide/death.bmp" ? Image.CreateEmpty(800, 600, false, Image.Format.Rgb8) : null;
        _deathVoiceForSelfTest = slide => { voices++; voicePlaying = slide.Voice == "sound/speech/slide/DeathSound.mp3"; return voicePlaying; };
        _deathVoicePlayingForSelfTest = () => voicePlaying;
        void Key(Key key, char unicode = '\0')
        {
            InputWorld(new InputEventKey { Keycode = key, PhysicalKeycode = key, Unicode = unicode, Pressed = true });
            InputWorld(new InputEventKey { Keycode = key, PhysicalKeycode = key, Unicode = unicode, Pressed = false });
        }
        GameSessionOwner NewGame()
        {
            if (_mainMenu is not null) EndMainMenu("self-test new game");
            Require(StartNewGame(0, sceneFlow: false) is null && _areaSession!.QueryHero() is not null, "a new game began with a hero: " + _quickSaveStatus);
            TickHeroDeath(16);
            Require(_heroDeath.Stage == AreaDeathStage.Watching, "a living hero starts nothing");
            return _areaSession!;
        }
        void Kill(GameSessionOwner session) =>
            Require(session.Kill(session.Version, SessionLifeTarget.Hero).Disposition == GameSessionDisposition.Changed, "kill the hero");
        try
        {
            // A hero already dead when the session comes starts nothing; only a death does.
            var game = NewGame();
            Kill(game);
            TickHeroDeath(16);
            Require(_heroDeath.Stage == AreaDeathStage.Waiting && !HeroDeathCovering && !WorldModal, "the death waits 5 s with play going on");

            // The in-play menu the player opened closes when the death comes, which comes again 0.3 s later (PD3).
            Key(Godot.Key.Escape);
            Require(MainMenuOpen && _mainMenu!.Window == AreaMenuWindow.InPlay, "Escape opened the in-play menu while waiting");
            TickHeroDeath(AreaHeroDeath.DeathDelay - 16);
            Require(MainMenuOpen && _heroDeath.Stage == AreaDeathStage.Waiting, "not before 5 s");
            TickHeroDeath(16);
            Require(!MainMenuOpen && _mainMenu is null && _heroDeath.Stage == AreaDeathStage.Waiting && _heroDeath.Remaining == AreaHeroDeath.MenuRetryDelay,
                "the menu closed and the death comes again 0.3 s later");
            TickHeroDeath(AreaHeroDeath.MenuRetryDelay);
            Require(_heroDeath.Stage == AreaDeathStage.FadeOut && HeroDeathCovering && WorldModal && _heroDeathPanel!.IsOpen, "the screen fades to black");
            TickHeroDeath(AreaHeroDeath.FadeTime / 2);
            Require(Math.Abs(_heroDeathPanel!.Veil - .5f) < .02f, "half way to black: " + _heroDeathPanel.Veil);
            // The death screen owns the input: Escape opens no menu and does nothing during a fade.
            Key(Godot.Key.Escape);
            Require(!MainMenuOpen && _heroDeath.Stage == AreaDeathStage.FadeOut, "Escape during the fade did nothing");
            TickHeroDeath(AreaHeroDeath.FadeTime / 2);
            Require(_heroDeath.Stage == AreaDeathStage.SlideIn && _heroDeathPanel.PictureShown && _heroDeathPanel.PictureSizeForSelfTest == new Vector2I(800, 600)
                && _heroDeathPanel.Veil == 1, "the death slide's picture is there under the black veil");
            TickHeroDeath(AreaHeroDeath.FadeTime);
            Require(_heroDeath.Stage == AreaDeathStage.Slide && _heroDeathPanel.Veil == 0 && voices == 1, "the slide faded in and its voice started");
            TickHeroDeath(16);
            Require(_heroDeath.Stage == AreaDeathStage.Slide, "the slide stays while its voice plays");
            Key(Godot.Key.A, 'a');
            Require(_heroDeath.Stage == AreaDeathStage.SlideOut, "a key ended the slide");
            var loads = _sceneLoadsForSelfTest;
            TickHeroDeath(AreaHeroDeath.FadeTime);
            Require(_sceneLoadsForSelfTest == loads + 1 && !ReferenceEquals(game, _areaSession) && _areaSession!.QueryHero() is null
                && MainMenuOpen && _mainMenu!.Window == AreaMenuWindow.Main && _mainMenu.Reopened && _heroDeath.Stage == AreaDeathStage.MenuFadeIn
                && _heroDeathPanel.IsOpen && !_heroDeathPanel.PictureShown,
                "the game reset to the reopened main menu with a fresh session, fading in");
            TickHeroDeath(AreaHeroDeath.FadeTime);
            Require(_heroDeath.Stage == AreaDeathStage.Watching && !_heroDeathPanel.IsOpen && MainMenuOpen, "the menu faded in and the screen closed");

            // A hero brought back to life before the death comes: nothing happens (PD3).
            game = NewGame();
            Kill(game);
            TickHeroDeath(16);
            Require(game.Heal(game.Version, SessionLifeTarget.Hero, new LifeHeal { Full = true, Resurrect = true }).Disposition == GameSessionDisposition.Changed
                && game.QueryHeroLife()?.Status.IsDead == false, "the hero lives again");
            TickHeroDeath(AreaHeroDeath.DeathDelay);
            Require(_heroDeath.Stage == AreaDeathStage.Watching && !HeroDeathCovering && ReferenceEquals(game, _areaSession), "a living hero is not reset");

            // A slide ends when its voice ends.
            Kill(game);
            TickHeroDeath(16);
            TickHeroDeath(AreaHeroDeath.DeathDelay); TickHeroDeath(AreaHeroDeath.FadeTime); TickHeroDeath(AreaHeroDeath.FadeTime);
            Require(_heroDeath.Stage == AreaDeathStage.Slide && voices == 2, "the second death's slide shows");
            voicePlaying = false;
            TickHeroDeath(16);
            Require(_heroDeath.Stage == AreaDeathStage.SlideOut, "the voice's end ended the slide");
            TickHeroDeath(AreaHeroDeath.FadeTime); TickHeroDeath(AreaHeroDeath.FadeTime);
            Require(_heroDeath.Stage == AreaDeathStage.Watching && MainMenuOpen && _mainMenu!.Reopened && _sceneLoadsForSelfTest == loads + 2, "the second reset");

            // A start scene that does not publish: the black screen gives way to the game as it is.
            game = NewGame();
            var published = _sceneLoadForSelfTest;
            _sceneLoadForSelfTest = _ => { _sceneLoadsForSelfTest++; return Task.CompletedTask; };
            Kill(game);
            TickHeroDeath(16);
            TickHeroDeath(AreaHeroDeath.DeathDelay); TickHeroDeath(AreaHeroDeath.FadeTime); TickHeroDeath(AreaHeroDeath.FadeTime);
            voicePlaying = false;
            TickHeroDeath(16); TickHeroDeath(AreaHeroDeath.FadeTime);
            Require(_heroDeath.Stage == AreaDeathStage.MenuFadeIn && _sceneLoadsForSelfTest == loads + 3 && ReferenceEquals(game, _areaSession) && !MainMenuOpen,
                "an unpublished start scene fades in over the game as it is: " + _heroDeath.Stage);
            TickHeroDeath(AreaHeroDeath.FadeTime);
            Require(!HeroDeathCovering && !_heroDeathPanel.IsOpen && !WorldModal, "the screen cleared");
            _sceneLoadForSelfTest = published;

            // ARC-UI-028: a save in the list; under Steamwoven the box asks (ARC-WORLD-120: over the faded-in slide), Y loads it and the
            // loaded game fades in once the slide has faded out.
            AreaDeathStage DieToTheBox(GameSessionOwner session)
            {
                Kill(session);
                TickHeroDeath(16);
                TickHeroDeath(AreaHeroDeath.DeathDelay); TickHeroDeath(AreaHeroDeath.FadeTime); TickHeroDeath(AreaHeroDeath.FadeTime);
                return _heroDeath.Stage;
            }
            game = NewGame();
            var saved = SaveGame(GameSaveLibrary.AutoName, GameSaveLibrary.AutoDescription, null, "self-test ");
            Require(saved == GameSaveLibrary.AutoName && SaveLibrary.Latest()?.Name == saved, "the new game saved: " + _quickSaveStatus);
            loads = _sceneLoadsForSelfTest;
            Require(DieToTheBox(game) == AreaDeathStage.Asking && _heroDeathPanel.Asking && _heroDeathPanel.Veil == 0 && _heroDeathPanel.PictureShown
                && voicePlaying && !_heroDeathPanel.AsksInArt
                && _heroDeathPanel.QuestionForSelfTest == AreaHeroDeath.LoadQuestion && _heroDeathPanel.SaveForSelfTest == "Auto-Save (quicksave)"
                && _heroDeathPanel.ButtonsForSelfTest.SequenceEqual(["Yes  (Y)", "No  (N)"]) && _heroDeath.OfferedSave == saved,
                "the box asks over the shown slide (the diagnostic box without game art) to load the hero's save: "
                + string.Join("|", _heroDeathPanel.ButtonsForSelfTest) + " " + _heroDeathPanel.SaveForSelfTest);
            Key(Godot.Key.A, 'a');
            voicePlaying = false;
            TickHeroDeath(16);
            Require(_heroDeath.Stage == AreaDeathStage.Asking && !MainMenuOpen && _heroDeathPanel.PictureShown, "another key and the voice's end do nothing");
            Key(Godot.Key.Y, 'y');
            Require(_heroDeath.Stage == AreaDeathStage.Loading && !_heroDeathPanel.Asking && !ReferenceEquals(game, _areaSession)
                && _areaSession!.QueryHeroLife()?.Status.IsDead == false && _sceneLoadsForSelfTest == loads,
                "Y loaded the save with its living hero, no reset: " + _quickSaveStatus);
            TickHeroDeath(AreaHeroDeath.FadeTime / 2);
            Require(_heroDeath.Stage == AreaDeathStage.Loading && Math.Abs(_heroDeathPanel.Veil - .5f) < .02f, "the slide fades to black meanwhile");
            TickHeroDeath(AreaHeroDeath.FadeTime / 2);
            Require(_heroDeath.Stage == AreaDeathStage.MenuFadeIn && _heroDeath.Loaded && !_heroDeathPanel.PictureShown, "the loaded game fades in");
            TickHeroDeath(AreaHeroDeath.FadeTime);
            Require(_heroDeath.Stage == AreaDeathStage.Watching && !HeroDeathCovering && !_heroDeathPanel.IsOpen && !MainMenuOpen && !WorldModal,
                "play goes on in the loaded game");

            // A corrupt save keeps the box with the refusal; Escape then resets as the original.
            game = _areaSession!;
            Require(DieToTheBox(game) == AreaDeathStage.Asking, "the box again");
            System.IO.File.WriteAllText(SaveLibrary.Slot(saved!).Path, "{");
            Key(Godot.Key.Enter);
            Require(_heroDeath.Stage == AreaDeathStage.Asking && _heroDeath.LoadRefusal is { Length: > 0 } && _heroDeathPanel.NoticeForSelfTest == _heroDeath.LoadRefusal
                && ReferenceEquals(game, _areaSession), "Return on a corrupt save keeps the box with why: " + _heroDeath.LoadRefusal);
            Key(Godot.Key.Escape);
            Require(_heroDeath.Stage == AreaDeathStage.SlideOut && !_heroDeathPanel.Asking, "Escape: the slide fades out");
            TickHeroDeath(AreaHeroDeath.FadeTime);
            Require(_sceneLoadsForSelfTest == loads + 1 && MainMenuOpen && _mainMenu!.Reopened && _heroDeath.Stage == AreaDeathStage.MenuFadeIn
                && !_heroDeathPanel.Asking, "Escape reset to the main menu");
            TickHeroDeath(AreaHeroDeath.FadeTime);

            // The box's No button resets too.
            game = NewGame();
            Require(SaveGame(GameSaveLibrary.AutoName, GameSaveLibrary.AutoDescription, null, "self-test ") == saved, "saved again");
            Require(DieToTheBox(game) == AreaDeathStage.Asking, "the box once more");
            _heroDeathPanel.PressForSelfTest(false);
            TickHeroDeath(AreaHeroDeath.FadeTime);
            Require(_sceneLoadsForSelfTest == loads + 2 && MainMenuOpen && _heroDeath.Stage == AreaDeathStage.MenuFadeIn, "No reset to the main menu");
            TickHeroDeath(AreaHeroDeath.FadeTime);

            // The N key resets as well.
            game = NewGame();
            Require(SaveGame(GameSaveLibrary.AutoName, GameSaveLibrary.AutoDescription, null, "self-test ") == saved, "saved for the N key");
            Require(DieToTheBox(game) == AreaDeathStage.Asking, "the box for the N key");
            Key(Godot.Key.N, 'n');
            TickHeroDeath(AreaHeroDeath.FadeTime);
            Require(_sceneLoadsForSelfTest == loads + 3 && MainMenuOpen && _heroDeath.Stage == AreaDeathStage.MenuFadeIn, "N reset to the main menu");
            TickHeroDeath(AreaHeroDeath.FadeTime);

            // Original never asks (PD8), a save there or not: the slide plays until its voice ends and the game resets.
            AreaDeathStage DieToTheReset(GameSessionOwner session)
            {
                Require(DieToTheBox(session) == AreaDeathStage.Slide && !_heroDeathPanel.Asking, "the slide shows without the box: " + _heroDeath.Stage);
                voicePlaying = false;
                TickHeroDeath(16); TickHeroDeath(AreaHeroDeath.FadeTime);
                return _heroDeath.Stage;
            }
            game = NewGame();
            Require(game.SelectRules(RuleMode.Original) == GameSessionDisposition.Changed, "Original selected");
            Require(DieToTheReset(game) == AreaDeathStage.MenuFadeIn && _sceneLoadsForSelfTest == loads + 4 && MainMenuOpen && !_heroDeathPanel.Asking,
                "Original reset to the main menu without the box: " + _heroDeath.Stage);
            TickHeroDeath(AreaHeroDeath.FadeTime);

            // ARC-WORLD-120: only the dead hero's saves are offered: with only another hero's save (the auto slot, which cannot be deleted,
            // written over with another hero's details) the game resets as the original.
            game = NewGame();
            var grid = (AreaGridSession)_worldActor.Session!;
            var cell = grid.Chart.ToWorld(grid.State.CommittedCell);
            var other = AreaQuickSave.Save(game, new ActorPlacement(new(CurrentAreaMap), new(checked((int)cell.X), checked((int)cell.Y)), grid.State.Facing), SaveLibrary, saved,
                session => AreaSaveList.Summary(session, "another hero", SaveLocation()) with { HeroName = "Another Hero" });
            Require(other.Saved && SaveLibrary.List().Entries.Count == 1 && DeadHeroSave() is null, "the only save (the auto slot) is another hero's: " + other.Status);
            Require(DieToTheReset(game) == AreaDeathStage.MenuFadeIn && _sceneLoadsForSelfTest == loads + 5 && MainMenuOpen && !_heroDeathPanel.Asking,
                "another hero's save is not offered: " + _heroDeath.Stage);
            TickHeroDeath(AreaHeroDeath.FadeTime);
            return "a death waits 5 s, closes the in-play menu and comes 0.3 s later, fades to black owning the input, shows the slide "
                + "(picture, voice) until a key, resets to the reopened main menu fading in; a hero alive again is not reset; a voice's end ends the slide; "
                + "an unpublished start scene clears the screen; Steamwoven with a save of this hero asks over the slide: Y loads it while the slide "
                + "fades out and it fades in, a corrupt save keeps the box, Escape, the No button and N fade the slide out and reset; Original resets "
                + "without asking; another hero's save is not offered";
        }
        finally
        {
            if (SaveListOpen) EndSaveList();
            if (_mainMenu is not null) EndMainMenu("self-test end");
            StopHeroDeath();
            _heroDeathForSelfTest = false; _deathSlidesForSelfTest = null; _deathPictureForSelfTest = null; _deathVoiceForSelfTest = null;
            _deathVoicePlayingForSelfTest = null;
            _saveLibraryForSelfTest = null; _quickKeys.Reset(); _heroLoaderForSelfTest = null; _mainMenuExitForSelfTest = null;
            _sceneLoadForSelfTest = null; _launchArguments = null; _sceneFlow = null;
            _worldFocusSimulationForSelfTest = simulatedFocus;
            System.IO.Directory.Delete(_mainMenuDirectoryForSelfTest, recursive: true);
            _mainMenuDirectoryForSelfTest = null;
        }
    }
}
