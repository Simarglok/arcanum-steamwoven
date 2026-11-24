using Arcanum.Data.Area;
using Arcanum.Presentation.Movies;
using Arcanum.Scripting.GlobalFlags;
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
    // ARC-RENDER-2197: three further endings follow, each from a new game of its own (the menu reopened by the reset): the Vendigroth underground's use script 2862 (movies 3 and 5, then the end
    // game) with global flag 2053, which script 997 answers with slide 3 alone (used with the item named 6711 that the script tests its extra object for), and the end game asked for with flag 2632 (slide 1) and with flag 2050 (slide 4), each with the installed picture,
    // fades and voice, the reset and the menu fade-in as above. ARC-RENDER-2660: a fifth ending walks the chain under script 2928 (quest 1058 completed and flag 1016: slides 6, 10, 20, 33, 39, 44) from Kerghan's second dying script 3061; the other chain rows stay pinned by the opt-in test AreaEndingQueuesRealDataTests.
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
            var content = SlideScriptContent();
            Require(content is not null, "the script content of the shown module");
            var table = DeathSlides();
            Require(table.Loaded, "the slide table is read: " + table.Detail);

            // A new game from the main menu; the show's hooks are on for it (a living hero starts nothing).
            async Task<GameSessionOwner> NewGame()
            {
                Require(MainMenuOpen && _mainMenu!.Window == AreaMenuWindow.Main, "the main menu is open for a new game");
                _moviesForSelfTest = false; // a new game's start plays movies 1 and 7: not wanted here
                Choose(10); Choose(50); Choose(420);
                MainMenuCommand(() => _mainMenu!.Start());
                await Frames(3);
                Require(!MainMenuOpen && _areaSession?.QueryHero() is not null, "a new game began with a hero: " + _quickSaveStatus);
                _heroDeathForSelfTest = true;
                await Frames(2);
                Require(_heroDeath.Stage == AreaDeathStage.Watching, "a living hero starts nothing");
                return _areaSession!;
            }
            // What script 997 queues for this game's flags is what the show must show; every queued slide is in the installed table.
            AreaSlideScriptResult Expect(GameSessionOwner session, string label)
            {
                var queued = AreaSlideScripts.Run(AreaSlideKind.EndGame, content, session);
                Require(queued.Slides.Count > 0 && queued.Slides.All(id => table.Pair(id) is not null), $"{label}: script 997 queues slides the table knows ({queued.Detail}: {string.Join(",", queued.Slides)})");
                return queued;
            }
            // The movies play through the real table and decoder, in this stage only (switched on after each new game's start); the start-up sequence is not wanted again when the menu reopens.
            _moviesForSelfTest = true; _moviePathForSelfTest = null; _movieTableForSelfTest = null; _movieSourceForSelfTest = null; _startupMoviesDone = true;
            var movies = MovieRefusal() is null;
            _moviesForSelfTest = false;
            // ARC-DEV-3116: a PR run ends each movie by the real key path (HandleMovieInput) once its checks are done; ARC_SMOKE_ALL=1 (the complete run, the daily build, `--all`) plays every movie to its own last frame.
            var full = SmokeAll;
            GD.Print($"{SlidesCode} slides smoke movies {(movies ? "play (installed table, real decoder)" : "passed over: " + MovieRefusal())}");
            GD.Print($"{SlidesCode} slides smoke mode {(!movies ? "movies passed over" : full ? "full (every movie plays to its end)" : "skip (each movie ends by the key skip after its checks)")}");

            // The movies of one script run: each covers the game with the clocks standing still and draws frames, and the show does not begin under them.
            async Task<List<Vector2I>> PlayMovies(GameSessionOwner game, string label, int count, int playedBefore, int failedBefore)
            {
                var frameSizes = new List<Vector2I>();
                long gameMs = 0, realMs = 0;
                var movieClock = System.Diagnostics.Stopwatch.StartNew();
                var (endedBefore, skippedBefore) = (_moviesPlayed, _moviesSkipped);
                while (_heroDeath.Stage == AreaDeathStage.Watching)
                {
                    Require(movieClock.Elapsed.TotalSeconds < (movies ? 120 : 5), $"{label}: the movies played and the end game began within {(movies ? 120 : 5)} s (movie covering {MovieCovering}, played {_scriptFadesPlayed - playedBefore})");
                    SyncScriptFades();
                    if (MovieCovering)
                    {
                        if (frameSizes.Count == 0 && gameMs == 0) { gameMs = game.QueryClockSnapshot().GameTimestampMs; realMs = game.QueryFrameClocks().RealMs; }
                        Require(_heroDeath.Stage == AreaDeathStage.Watching && WorldModal, $"{label}: no slide begins under a movie and the world is under the movie screen");
                        if (_moviePanel is { FramesDrawn: >= 12 } panel && _scriptFadesPlayed - playedBefore == frameSizes.Count + 1)
                        {
                            frameSizes.Add(panel.FrameSize ?? Vector2I.Zero);
                            Require(game.QueryClockSnapshot().GameTimestampMs == gameMs && game.QueryFrameClocks().RealMs == realMs && WorldFrozenFrame,
                                $"{label}: the session's clocks stand still under movie {frameSizes.Count} (game {game.QueryClockSnapshot().GameTimestampMs - gameMs} ms, real {game.QueryFrameClocks().RealMs - realMs} ms)");
                            GD.Print($"{SlidesCode} slides smoke {label} movie {frameSizes.Count}: {panel.FrameSize}, audio {panel.AudioPlaying}");
                            await Shoot($"{label}-movie-{frameSizes.Count}");
                            // The PR run's end of the movie: a key press and release through the world's input, the path a player's skip takes (the cover takes the press, ends the movie, and takes the release too).
                            if (!full) Key(Godot.Key.Space);
                        }
                    }
                    await Frames(1);
                }
                if (movies)
                {
                    Require(frameSizes.Count == count && frameSizes.All(size => size.X >= 640 && size.Y >= 300), $"{label}: the {count} installed movies drew frames ({string.Join(" ", frameSizes)})");
                    Require(_scriptFadesPlayed - playedBefore == count && _moviesFailed == failedBefore, $"{label}: the movies started and none failed (started {_scriptFadesPlayed - playedBefore}, failed {_moviesFailed})");
                    // A skipped movie is not a failed one: the skip run counts them skipped and none ended by itself; the full run counts them played and none skipped.
                    var (ended, skipped) = (_moviesPlayed - endedBefore, _moviesSkipped - skippedBefore);
                    Require(full ? ended == count && skipped == 0 : skipped == count && ended == 0,
                        $"{label}: the movies {(full ? "ended by themselves" : "were ended by the key skip")} (ended {ended}, skipped {skipped}, of {count})");
                }
                else Require(frameSizes.Count == 0, $"{label}: no movie played");
                return frameSizes;
            }

            // The show: the screen fades to black, then the queue of script 997 plays slide by slide; then the game resets to the start map with a fresh session and the main menu reopens and fades in.
            async Task<(long WaitMs, List<(int Id, string Image, Vector2I Size, bool Voice, long InMs, long OutMs)> Shown)> Show(GameSessionOwner game, AreaSlideScriptResult expected, string label)
            {
                Require(_heroDeath.Kind == AreaSlideKind.EndGame && _heroDeath.Stage is AreaDeathStage.Waiting or AreaDeathStage.FadeOut && !_endGameAsked && !MovieCovering, $"{label}: the end game began after the movies ({_heroDeath.Kind} {_heroDeath.Stage})");
                var waitMs = await Until(() => _heroDeath.Stage == AreaDeathStage.FadeOut || _heroDeath.Stage == AreaDeathStage.SlideIn, 5, $"{label}: the screen fades to black");
                Require(WorldModal && HeroDeathCovering, $"{label}: the death screen covers the game");
                Require(_heroDeath.Queue.SequenceEqual(expected.Slides), $"{label}: the show's queue is script 997's ({string.Join(",", _heroDeath.Queue)} against {string.Join(",", expected.Slides)})");
                var gameShow = game.QueryClockSnapshot().GameTimestampMs; var realShow = game.QueryFrameClocks().RealMs;
                for (var i = 0; i < 6; i++) TickAreaClock(80_000_000L + i * 250);
                Require(!game.QueryClockSnapshot().PauseDisposition.Paused && game.QueryClockSnapshot().GameTimestampMs == gameShow && game.QueryFrameClocks().RealMs == realShow && _frameTap is { Flags.Frozen: true },
                    $"{label}: the cover is a frozen frame: no clock moved and no pause was raised");
                var shown = new List<(int Id, string Image, Vector2I Size, bool Voice, long InMs, long OutMs)>();
                var lastIndex = -1;
                while (true)
                {
                    await Until(() => _heroDeath.Stage is AreaDeathStage.SlideIn && _heroDeath.Index != lastIndex || _heroDeath.Stage is AreaDeathStage.Reset or AreaDeathStage.MenuFadeIn or AreaDeathStage.Watching, 15, $"{label}: the next slide or the reset");
                    if (_heroDeath.Stage != AreaDeathStage.SlideIn) break;
                    lastIndex = _heroDeath.Index;
                    var slide = _heroDeath.Slide!;
                    Require(_heroDeathPanel is { PictureShown: true } && _deathMusic is not null, $"{label}: slide {slide.Id}: its picture is on the screen and the slides' music plays");
                    var size = _heroDeathPanel!.PictureSizeForSelfTest ?? Vector2I.Zero;
                    Require(size.X > 0 && size.Y > 0, $"{label}: slide {slide.Id}: the installed picture {slide.Image} was read ({size})");
                    var inMs = await Until(() => _heroDeath.Stage == AreaDeathStage.Slide, 6, $"{label}: slide {slide.Id} faded in");
                    Require(_heroDeathPanel.Veil == 0, $"{label}: slide {slide.Id}: clear after the fade in");
                    var voice = _deathVoice is { } handle && _audio?.Volume(handle) is not null;
                    if (capture is not null) await Shoot($"{label}-slide-{shown.Count + 1:00}-{slide.Id}");
                    Require(!_heroDeathPanel.AsksInArt, $"{label}: slide {slide.Id}: an end game asks nothing");
                    Key(Godot.Key.Space);
                    var outMs = await Until(() => _heroDeath.Stage != AreaDeathStage.Slide && _heroDeath.Stage != AreaDeathStage.SlideOut, 6, $"{label}: slide {slide.Id} faded out");
                    shown.Add((slide.Id, slide.Image, size, voice, inMs, outMs));
                }
                var expectedShown = expected.Slides.Count(id => table.Pair(id) is not null);
                Require(shown.Count == expectedShown && shown.Select(slide => slide.Id).SequenceEqual(expected.Slides.Where(id => table.Pair(id) is not null)),
                    $"{label}: every queued slide showed in order ({shown.Count} of {expectedShown})");
                Require(shown.All(slide => slide.Voice && slide.OutMs >= AreaHeroDeath.FadeTime - 700), $"{label}: each slide's voice started and each slide faded out over about 2 s (" + string.Join(", ", shown.Select(slide => $"{slide.Id}: voice {slide.Voice}, out {slide.OutMs} ms")) + ")");
                Require(shown.All(slide => slide.InMs >= AreaHeroDeath.FadeTime - 700), $"{label}: each slide faded in over about 2 s (" + string.Join(", ", shown.Select(slide => $"{slide.Id}: {slide.InMs} ms")) + ")");
                Require(_heroDeath.Kind == AreaSlideKind.EndGame && _heroDeath.Shown == shown.Count, $"{label}: the show counted its slides ({_heroDeath.Shown})");

                // The reset: the start map loads again with a fresh session without a hero, and the main menu reopens and fades in.
                await (_lastLoad ?? Task.CompletedTask);
                await Until(() => _heroDeath.Stage == AreaDeathStage.Watching, 30, $"{label}: the main menu faded in");
                Require(_readyMapName == start && MainMenuOpen && _mainMenu!.Window == AreaMenuWindow.Main && _mainMenu.Reopened && !ReferenceEquals(game, _areaSession) && _areaSession!.QueryHero() is null
                    && !_heroDeathPanel!.IsOpen && _deathMusic is null && !MovieCovering,
                    $"{label}: the start map loaded again with a fresh session and the main menu reopened (map {_readyMapName}, menu {MainMenuOpen} {_mainMenu?.Window} reopened {_mainMenu?.Reopened}, hero {_areaSession?.QueryHero() is not null}, panel {_heroDeathPanel?.IsOpen}, music {_deathMusic is not null})");
                await Shoot($"{label}-menu-after");
                return (waitMs, shown);
            }
            void SetFlag(GameSessionOwner game, int flag)
            {
                var receipt = game.Flags.Write(new GlobalFlagWriteRequest(new GlobalFlagRequestId(Guid.NewGuid()), game.Flags.Version, new FlagId(flag), true));
                Require(receipt.Disposition == GlobalFlagWriteDisposition.Applied, $"global flag {flag} was set ({receipt.Disposition} {receipt.Reason})");
            }

            // 1. Kerghan's dying run (the new game's flags): the installed script at the dying point of a creature row of the shown map (the kill runs it before any death processing, 0x45DA57).
            var game1 = await NewGame();
            _moviesForSelfTest = true;
            var expected = Expect(game1, "Kerghan's dying script");
            var row = ScriptedRow(CurrentAreaMap, ScrPoint.Dying, 2863);
            var host = new AreaUseScriptHost(game1, CurrentAreaMap, [row], [], content!, new AreaScriptWorld(() => null, _ => false, _ => null, () => null));
            var ran = host.RunDying(AreaUseScriptHost.EntityId(row), host.Pc, game1.Rules.Scripts);
            Require(ran.Outcome == AreaScriptedUseOutcome.NoTeleport && ran.Run is { Outcome: not ScrOutcome.Refused }, $"the dying script 2863 ran: {ran.Outcome} {ran.Detail}");
            var (playedBefore, failedBefore) = (_scriptFadesPlayed, _moviesFailed);
            AdoptScriptRun(host);
            Require(_scriptFades.Select(fade => fade.Movie).SequenceEqual([2, 5]) && _endGameAsked, $"the run asked for movies 2 and 5 and the end game ({_scriptFades.Count} fades, end game {_endGameAsked})");
            Require(_heroDeath.Stage == AreaDeathStage.Watching, "the end game waits for the movies");
            var frameSizes = await PlayMovies(game1, "dying-2863", 2, playedBefore, failedBefore);
            // The movies are done: the rest of the show plays without them (the later endings play theirs again).
            _moviesForSelfTest = false;
            var (waitMs, shown) = await Show(game1, expected, "dying-2863");
            var endings = new List<object> { new { Name = "dying-2863", Queue = expected.Slides, Slides = shown.Select(slide => slide.Id) } };

            // 2. ARC-RENDER-2197: the Vendigroth underground's use script 2862 (a use point of a type-14 object, used with the item named 6711 that it tests its extra object for; movies 3 and 5, then the end game) with global flag 2053 set, which script 997 answers with slide 3 alone.
            var game2 = await NewGame();
            SetFlag(game2, 2053);
            var expected2 = Expect(game2, "use script 2862 with flag 2053");
            Require(expected2.Slides.SequenceEqual([3]), $"flag 2053 queues slide 3 alone ({string.Join(",", expected2.Slides)})");
            _moviesForSelfTest = true;
            var useRow = ScriptedRow(CurrentAreaMap, ScrPoint.Use, 2862);
            var itemRow = NamedRow(CurrentAreaMap, 6711);
            var useHost = new AreaUseScriptHost(game2, CurrentAreaMap, [useRow, itemRow], [], content!, new AreaScriptWorld(() => null, _ => false, _ => null, () => null));
            var used = useHost.RunUseScript(AreaUseScriptHost.EntityId(useRow), useHost.Pc, new ScrObjectRef(CurrentAreaMap, AreaUseScriptHost.EntityId(itemRow)), game2.Rules.Scripts);
            Require(used.Outcome == AreaScriptedUseOutcome.NoTeleport && used.Run is { Outcome: not ScrOutcome.Refused }, $"the use script 2862 ran: {used.Outcome} {used.Detail}");
            (playedBefore, failedBefore) = (_scriptFadesPlayed, _moviesFailed);
            AdoptScriptRun(useHost);
            Require(_scriptFades.Select(fade => fade.Movie).SequenceEqual([3, 5]) && _endGameAsked, $"the use run asked for movies 3 and 5 and the end game ({_scriptFades.Count} fades, end game {_endGameAsked})");
            Require(_heroDeath.Stage == AreaDeathStage.Watching, "the end game waits for the movies of 2862");
            await PlayMovies(game2, "use-2862", 2, playedBefore, failedBefore);
            _moviesForSelfTest = false;
            var shown2 = (await Show(game2, expected2, "use-2862")).Shown;
            endings.Add(new { Name = "use-2862-flag-2053", Queue = expected2.Slides, Slides = shown2.Select(slide => slide.Id) });

            // 3. ARC-RENDER-2197: the other flags script 997 picks single-slide endings by (2632: slide 1; 2050: slide 4), asked for as the end game is, without a movie.
            foreach (var (flag, slide) in new[] { (2632, 1), (2050, 4) })
            {
                var label = $"flag-{flag}";
                var gameN = await NewGame();
                SetFlag(gameN, flag);
                var expectedN = Expect(gameN, label);
                Require(expectedN.Slides.SequenceEqual([slide]), $"{label} queues slide {slide} alone ({string.Join(",", expectedN.Slides)})");
                Require(RequestEndGame() && _heroDeath.Stage == AreaDeathStage.Waiting && _heroDeath.Kind == AreaSlideKind.EndGame, $"{label}: the end game is asked for");
                var shownN = (await Show(gameN, expectedN, label)).Shown;
                endings.Add(new { Name = label, Queue = expectedN.Slides, Slides = shownN.Select(s => s.Id) });
            }
            // 4. ARC-RENDER-2660: a multi-slide chain ending down script 2928 other than the new game's, and Kerghan's second dying script 3061: a new game whose quest 1058 is completed and flag 1016 set, which
            // the chain answers with slides 6 and 10 in front of the common tail (20, 33, 39, 44); the end game comes from the installed dying script 3061 (movies 2 and 5), as for 2863.
            var game4 = await NewGame();
            SetFlag(game4, 1016);
            var quest = game4.SetQuestState(game4.Version, 1058, SessionQuestState.Completed);
            Require(quest.Disposition == GameSessionDisposition.Changed, $"quest 1058 was completed ({quest.Disposition} {quest.Failure})");
            var expected4 = Expect(game4, "dying script 3061 with quest 1058 completed and flag 1016");
            Require(expected4.Slides.SequenceEqual([6, 10, 20, 33, 39, 44]), $"quest 1058 completed and flag 1016 queue the chain 6,10,20,33,39,44 ({string.Join(",", expected4.Slides)})");
            _moviesForSelfTest = true;
            var row4 = ScriptedRow(CurrentAreaMap, ScrPoint.Dying, 3061);
            var host4 = new AreaUseScriptHost(game4, CurrentAreaMap, [row4], [], content!, new AreaScriptWorld(() => null, _ => false, _ => null, () => null));
            var ran4 = host4.RunDying(AreaUseScriptHost.EntityId(row4), host4.Pc, game4.Rules.Scripts);
            Require(ran4.Outcome == AreaScriptedUseOutcome.NoTeleport && ran4.Run is { Outcome: not ScrOutcome.Refused }, $"the dying script 3061 ran: {ran4.Outcome} {ran4.Detail}");
            (playedBefore, failedBefore) = (_scriptFadesPlayed, _moviesFailed);
            AdoptScriptRun(host4);
            Require(_scriptFades.Select(fade => fade.Movie).SequenceEqual([2, 5]) && _endGameAsked, $"the run of 3061 asked for movies 2 and 5 and the end game ({_scriptFades.Count} fades, end game {_endGameAsked})");
            Require(_heroDeath.Stage == AreaDeathStage.Watching, "the end game waits for the movies of 3061");
            await PlayMovies(game4, "dying-3061", 2, playedBefore, failedBefore);
            _moviesForSelfTest = false;
            var shown4 = (await Show(game4, expected4, "dying-3061-chain")).Shown;
            Require(shown4.Count >= 2, $"the chain ending showed several slides ({shown4.Count})");
            endings.Add(new { Name = "dying-3061-quest-1058-flag-1016", Queue = expected4.Slides, Slides = shown4.Select(slide => slide.Id) });
            GD.Print("ARC-RENDER-1879 native-smoke slides PASS " + System.Text.Json.JsonSerializer.Serialize(new
            {
                Start = start, Movies = movies, Mode = !movies ? "passed-over" : full ? "full" : "skip", MovieFrames = frameSizes.Select(size => size.ToString()), Queue = expected.Slides, EndGameWaitMs = waitMs,
                Slides = shown.Select(slide => new { slide.Id, slide.Image, Size = slide.Size.ToString(), slide.Voice, slide.InMs, slide.OutMs }),
                Endings = endings,
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

    // A row of the shown map with an installed script at one attachment point: the dying point (12) of Kerghan's prototypes 27387 to 27389 (2863 and 3061), the use point (1) of the Vendigroth underground's object (2862).
    private static AreaObjectRecord ScriptedRow(int map, int point, int script)
    {
        var key = new AreaObjectKey(new string('a', 64), map, AreaSourceKind.Mobile, 1, 1, 100L, new string('b', 64), 100);
        var fields = new List<AreaFieldValue>
        {
            new(31, AreaPresence.Array, AreaValueOrigin.Instance, null, [new AreaSparseElement(point, [0, 0, script])], null),
            new(18, AreaPresence.Value, AreaValueOrigin.Instance, 0u, [], null),
        };
        return new(key, 0, 16, 0, "p", "p", "r", "s", AreaScope.Core, null, 11, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], fields, true, true, true);
    }

    // The item row the use script 2862 names as its extra object (name number 6711, field 21).
    private static AreaObjectRecord NamedRow(int map, int name)
    {
        var key = new AreaObjectKey(new string('c', 64), map, AreaSourceKind.Mobile, 1, 2, 200L, new string('d', 64), 100);
        var fields = new List<AreaFieldValue> { new(21, AreaPresence.Value, AreaValueOrigin.Instance, (long)name, [], null) };
        return new(key, 0, 16, 0, "p", "p", "r", "s", AreaScope.Core, null, 12, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], fields, true, true, true);
    }
}
