using Arcanum.Data.Area;
using Arcanum.Presentation.Movies;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    private const string SlidesCode = "ARC-RENDER-1879";

    // ARC-RENDER-1879 native observation (stage "slides", map mode on the start map with --main-menu true; wall clock; ffmpeg for the movies, without it the
    // movies are passed over and said so): the ending path of the original from Kerghan's death to the end of the game, on the installed data. After a new game
    // from the menu the installed dying script of Kerghan (2863: movies 2 and 5, then end game 132) runs through the use-script host (a row standing for Kerghan on
    // the shown map, the dying run the kill makes); its two movies play from the installed table through the real decoder, covering the game with the clocks standing
    // still, and only after them the end-game show asks for itself (50 ms event, script 997 queues the slides of the session's flags); the screen fades to black
    // (2 s), each slide's installed picture fades in (2 s) with the slides' music and its voice, a key release ends it and it fades out (2 s); then the game resets to
    // the start map with a fresh session and the main menu reopened, which fades in. No box asks and no slide of the default death is queued (end game, not death).
    // ARC_AREA_SLIDES_CAPTURE=FOLDER saves a frame of each movie, each slide and the reopened menu as PNG for the graphics report (original art: keep it out of git).
    private async Task RunAreaSlidesSmokeAsync()
    {
        static void Require(bool value, string why) { if (!value) { GD.Print($"{SlidesCode} slides smoke FAIL " + why); throw new InvalidOperationException($"{SlidesCode} slides smoke: " + why); } }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused;
        _worldFocusSimulationForSelfTest = true; _worldFocused = true;
        var (forTest, pathFor, tableFor, sourceFor, startupDone) = (_moviesForSelfTest, _moviePathForSelfTest, _movieTableForSelfTest, _movieSourceForSelfTest, _startupMoviesDone);
        var heroDeathTest = _heroDeathForSelfTest;
        var capture = System.Environment.GetEnvironmentVariable("ARC_AREA_SLIDES_CAPTURE") is { Length: > 0 } folder ? folder : null;
        if (capture is not null) System.IO.Directory.CreateDirectory(capture);
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
                    Require(clock.Elapsed.TotalSeconds < seconds, $"{what} within {seconds} s (death stage {_heroDeath.Stage} {_heroDeath.Kind}, {_heroDeathStatus}; movie covering {MovieCovering})");
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
                var button = _mainMenu!.Shown.FirstOrDefault(shown => shown.Message == message) ?? throw new InvalidOperationException($"{SlidesCode} slides smoke: no button " + message);
                var letter = _mainMenu.Texts.Hotkey(button) ?? throw new InvalidOperationException($"{SlidesCode} slides smoke: no letter for " + message);
                Key(letter is >= 'A' and <= 'Z' ? (Key)letter : Godot.Key.None, char.ToLowerInvariant(letter));
            }
            async Task Shoot(string name)
            {
                if (capture is null) return;
                await Frames(2);
                GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(capture, name + ".png"));
            }

            Require(_arguments?.MainMenu == true && MainMenuOpen && _mainMenu!.Window == AreaMenuWindow.Main && CurrentAreaMap == 1, "the menu is open over the start map (run with --main-menu true)");
            var start = _readyMapName;
            Choose(10); Choose(50); Choose(420);
            MainMenuCommand(() => _mainMenu!.Start());
            await Frames(3);
            Require(!MainMenuOpen && _areaSession?.QueryHero() is not null, "a new game began with a hero: " + _quickSaveStatus);
            var game = _areaSession!;
            _heroDeathForSelfTest = true;
            await Frames(2);
            Require(_heroDeath.Stage == AreaDeathStage.Watching, "a living hero starts nothing");

            // The slide table and the end-game script of the shown module: what script 997 queues for this game's flags is what the show must show.
            var table = DeathSlides();
            Require(table.Loaded, "the slide table is read: " + table.Detail);
            var content = SlideScriptContent();
            Require(content is not null, "the script content of the shown module");
            var expected = AreaSlideScripts.Run(AreaSlideKind.EndGame, content, game);
            Require(expected.Slides.Count > 0 && expected.Slides.All(id => table.Pair(id) is not null), $"script 997 queues slides the table knows ({expected.Detail}: {string.Join(",", expected.Slides)})");

            // The movies play through the real table and decoder, in this stage only; the start-up sequence is not wanted again when the menu reopens.
            _moviesForSelfTest = true; _moviePathForSelfTest = null; _movieTableForSelfTest = null; _movieSourceForSelfTest = null; _startupMoviesDone = true;
            var movies = MovieRefusal() is null;
            GD.Print($"{SlidesCode} slides smoke movies {(movies ? "play (installed table, real decoder)" : "passed over: " + MovieRefusal())}");

            // Kerghan's dying run: the installed script at the dying point of a creature row of the shown map (the kill runs it before any death processing, 0x45DA57).
            var row = KerghanRow(CurrentAreaMap, 2863);
            var host = new AreaUseScriptHost(game, CurrentAreaMap, [row], [], content!, new AreaScriptWorld(() => null, _ => false, _ => null, () => null));
            var ran = host.RunDying(AreaUseScriptHost.EntityId(row), host.Pc, game.Rules.Scripts);
            Require(ran.Outcome == AreaScriptedUseOutcome.NoTeleport && ran.Run is { Outcome: not ScrOutcome.Refused }, $"the dying script 2863 ran: {ran.Outcome} {ran.Detail}");
            var (playedBefore, failedBefore) = (_scriptFadesPlayed, _moviesFailed);
            AdoptScriptRun(host);
            Require(_scriptFades.Select(fade => fade.Movie).SequenceEqual([2, 5]) && _endGameAsked, $"the run asked for movies 2 and 5 and the end game ({_scriptFades.Count} fades, end game {_endGameAsked})");
            Require(_heroDeath.Stage == AreaDeathStage.Watching, "the end game waits for the movies");

            // The movies: each covers the game with the clocks standing still and draws frames, and the show does not begin under them.
            var frameSizes = new List<Vector2I>();
            long gameMs = 0, realMs = 0;
            var movieClock = System.Diagnostics.Stopwatch.StartNew();
            while (_heroDeath.Stage == AreaDeathStage.Watching)
            {
                Require(movieClock.Elapsed.TotalSeconds < (movies ? 120 : 5), $"the movies played and the end game began within {(movies ? 120 : 5)} s (movie covering {MovieCovering}, played {_scriptFadesPlayed - playedBefore})");
                SyncScriptFades();
                if (MovieCovering)
                {
                    if (frameSizes.Count == 0 && gameMs == 0) { gameMs = game.QueryClockSnapshot().GameTimestampMs; realMs = game.QueryFrameClocks().RealMs; }
                    Require(_heroDeath.Stage == AreaDeathStage.Watching && WorldModal, "no slide begins under a movie and the world is under the movie screen");
                    if (_moviePanel is { FramesDrawn: >= 12 } panel && _scriptFadesPlayed - playedBefore == frameSizes.Count + 1)
                    {
                        frameSizes.Add(panel.FrameSize ?? Vector2I.Zero);
                        Require(game.QueryClockSnapshot().GameTimestampMs == gameMs && game.QueryFrameClocks().RealMs == realMs && WorldFrozenFrame,
                            $"the session's clocks stand still under movie {frameSizes.Count} (game {game.QueryClockSnapshot().GameTimestampMs - gameMs} ms, real {game.QueryFrameClocks().RealMs - realMs} ms)");
                        GD.Print($"{SlidesCode} slides smoke movie {frameSizes.Count}: {panel.FrameSize}, audio {panel.AudioPlaying}");
                        await Shoot($"movie-{frameSizes.Count}");
                    }
                }
                await Frames(1);
            }
            if (movies)
            {
                Require(frameSizes.Count == 2 && frameSizes.All(size => size.X >= 640 && size.Y >= 300), $"both installed movies drew frames ({string.Join(" ", frameSizes)})");
                Require(_scriptFadesPlayed - playedBefore == 2 && _moviesFailed == failedBefore, $"both movies started and none failed (started {_scriptFadesPlayed - playedBefore}, failed {_moviesFailed})");
            }
            else Require(frameSizes.Count == 0, "no movie played");
            // The movies are done: the rest of the stage plays without them.
            _moviesForSelfTest = false;
            Require(_heroDeath.Kind == AreaSlideKind.EndGame && _heroDeath.Stage is AreaDeathStage.Waiting or AreaDeathStage.FadeOut && !_endGameAsked && !MovieCovering, $"the end game began after the movies ({_heroDeath.Kind} {_heroDeath.Stage})");

            // The show: the screen fades to black, then the queue of script 997 plays slide by slide.
            var waitMs = await Until(() => _heroDeath.Stage == AreaDeathStage.FadeOut || _heroDeath.Stage == AreaDeathStage.SlideIn, 5, "the screen fades to black");
            Require(WorldModal && HeroDeathCovering, "the death screen covers the game");
            Require(_heroDeath.Queue.SequenceEqual(expected.Slides), $"the show's queue is script 997's ({string.Join(",", _heroDeath.Queue)} against {string.Join(",", expected.Slides)})");
            var gameShow = game.QueryClockSnapshot().GameTimestampMs; var realShow = game.QueryFrameClocks().RealMs;
            for (var i = 0; i < 6; i++) TickAreaClock(80_000_000L + i * 250);
            Require(!game.QueryClockSnapshot().PauseDisposition.Paused && game.QueryClockSnapshot().GameTimestampMs == gameShow && game.QueryFrameClocks().RealMs == realShow && _frameTap is { Flags.Frozen: true },
                "the cover is a frozen frame: no clock moved and no pause was raised");
            var shown = new List<(int Id, string Image, Vector2I Size, bool Voice, long InMs, long OutMs)>();
            var lastIndex = -1;
            while (true)
            {
                await Until(() => _heroDeath.Stage is AreaDeathStage.SlideIn && _heroDeath.Index != lastIndex || _heroDeath.Stage is AreaDeathStage.Reset or AreaDeathStage.MenuFadeIn or AreaDeathStage.Watching, 15, "the next slide or the reset");
                if (_heroDeath.Stage != AreaDeathStage.SlideIn) break;
                lastIndex = _heroDeath.Index;
                var slide = _heroDeath.Slide!;
                Require(_heroDeathPanel is { PictureShown: true } && _deathMusic is not null, $"slide {slide.Id}: its picture is on the screen and the slides' music plays");
                var size = _heroDeathPanel!.PictureSizeForSelfTest ?? Vector2I.Zero;
                Require(size.X > 0 && size.Y > 0, $"slide {slide.Id}: the installed picture {slide.Image} was read ({size})");
                var inMs = await Until(() => _heroDeath.Stage == AreaDeathStage.Slide, 6, $"slide {slide.Id} faded in");
                Require(_heroDeathPanel.Veil == 0, $"slide {slide.Id}: clear after the fade in");
                var voice = _deathVoice is { } handle && _audio?.Volume(handle) is not null;
                if (capture is not null) await Shoot($"slide-{shown.Count + 1:00}-{slide.Id}");
                Require(!_heroDeathPanel.AsksInArt, $"slide {slide.Id}: an end game asks nothing");
                Key(Godot.Key.Space);
                var outMs = await Until(() => _heroDeath.Stage != AreaDeathStage.Slide && _heroDeath.Stage != AreaDeathStage.SlideOut, 6, $"slide {slide.Id} faded out");
                shown.Add((slide.Id, slide.Image, size, voice, inMs, outMs));
            }
            var expectedShown = expected.Slides.Count(id => table.Pair(id) is not null);
            Require(shown.Count == expectedShown && shown.Select(slide => slide.Id).SequenceEqual(expected.Slides.Where(id => table.Pair(id) is not null)),
                $"every queued slide showed in order ({shown.Count} of {expectedShown})");
            Require(shown.All(slide => slide.Voice && slide.OutMs >= AreaHeroDeath.FadeTime - 700), "each slide's voice started and each slide faded out over about 2 s (" + string.Join(", ", shown.Select(slide => $"{slide.Id}: voice {slide.Voice}, out {slide.OutMs} ms")) + ")");
            Require(shown.All(slide => slide.InMs >= AreaHeroDeath.FadeTime - 700), "each slide faded in over about 2 s (" + string.Join(", ", shown.Select(slide => $"{slide.Id}: {slide.InMs} ms")) + ")");
            Require(_heroDeath.Kind == AreaSlideKind.EndGame && _heroDeath.Shown == shown.Count, $"the show counted its slides ({_heroDeath.Shown})");

            // The reset: the start map loads again with a fresh session without a hero, and the main menu reopens and fades in.
            await (_lastLoad ?? Task.CompletedTask);
            await Until(() => _heroDeath.Stage == AreaDeathStage.Watching, 30, "the main menu faded in");
            Require(_readyMapName == start && MainMenuOpen && _mainMenu!.Window == AreaMenuWindow.Main && _mainMenu.Reopened && !ReferenceEquals(game, _areaSession) && _areaSession!.QueryHero() is null
                && !_heroDeathPanel!.IsOpen && _deathMusic is null && !MovieCovering,
                $"the start map loaded again with a fresh session and the main menu reopened (map {_readyMapName}, menu {MainMenuOpen} {_mainMenu?.Window} reopened {_mainMenu?.Reopened}, hero {_areaSession?.QueryHero() is not null}, panel {_heroDeathPanel?.IsOpen}, music {_deathMusic is not null})");
            await Shoot("menu-after");
            GD.Print("ARC-RENDER-1879 native-smoke slides PASS " + System.Text.Json.JsonSerializer.Serialize(new
            {
                Start = start, Movies = movies, MovieFrames = frameSizes.Select(size => size.ToString()), Queue = expected.Slides, EndGameWaitMs = waitMs,
                Slides = shown.Select(slide => new { slide.Id, slide.Image, Size = slide.Size.ToString(), slide.Voice, slide.InMs, slide.OutMs }),
            }));
        }
        finally
        {
            if (HeroDeathCovering) { GD.Print($"{SlidesCode} slides smoke: dropped a show at stage {_heroDeath.Stage}"); StopHeroDeath(); }
            AbortMovies();
            _heroDeathForSelfTest = heroDeathTest;
            (_moviesForSelfTest, _moviePathForSelfTest, _movieTableForSelfTest, _movieSourceForSelfTest, _startupMoviesDone) = (forTest, pathFor, tableFor, sourceFor, startupDone);
            if (_mainMenu is not null) EndMainMenu("smoke end");
            _quickKeys.Reset();
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
        }
    }

    // A creature row of the shown map with the installed dying script at its dying point (attachment point 12), as Kerghan's prototypes 27387 to 27389 carry 2863 and 3061.
    private static AreaObjectRecord KerghanRow(int map, int script)
    {
        var key = new AreaObjectKey(new string('a', 64), map, AreaSourceKind.Mobile, 1, 1, 100L, new string('b', 64), 100);
        var fields = new List<AreaFieldValue>
        {
            new(31, AreaPresence.Array, AreaValueOrigin.Instance, null, [new AreaSparseElement(ScrPoint.Dying, [0, 0, script])], null),
            new(18, AreaPresence.Value, AreaValueOrigin.Instance, 0u, [], null),
        };
        return new(key, 0, 16, 0, "p", "p", "r", "s", AreaScope.Core, null, 11, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], fields, true, true, true);
    }
}
