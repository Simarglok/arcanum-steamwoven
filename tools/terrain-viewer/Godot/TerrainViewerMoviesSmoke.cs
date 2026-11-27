using System.Diagnostics;
using Arcanum.Presentation.Movies;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    private const string MoviesCode = "ARC-RENDER-1170";

    // ARC-RENDER-1170 native observation (stage "movies", only when named, with --hero; it needs ffmpeg and no game movie): ffmpeg generates two
    // short test movies (a pattern and a tone, no game data) and the movie player plays them as movies 1 and 7: the screen covers the game, the
    // frames and the audio start, the session's clocks stand still (a frozen frame, TL25), a key press ends the first, the second runs to its
    // last frame, and the continuation runs once. Then the failures that must not hang: a movie with no file, and a viewer without a decoder
    // (nothing plays and the caller goes on at once). The real installed movies are probed by the opt-in real-data tests, not here.
    private async Task RunAreaMoviesSmokeAsync()
    {
        if (_areaSession is not { } game || game.QueryHero() is null)
        { GD.Print($"{MoviesCode} native-smoke movies NONE map={_readyMapName} (no hero session)"); return; }
        if (FfmpegLocator.Find() is not { } tools)
        { GD.Print($"{MoviesCode} native-smoke movies NONE (no ffmpeg: install it or set {FfmpegLocator.EnvironmentVariable})"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print($"{MoviesCode} movies smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_MOVIES_SMOKE_FAILED");
        }
        var folder = Directory.CreateTempSubdirectory("arc-movies-smoke-").FullName;
        var (forTest, pathFor, tableFor, sourceFor) = (_moviesForSelfTest, _moviePathForSelfTest, _movieTableForSelfTest, _movieSourceForSelfTest);
        try
        {
            string Make(string name, string size, int seconds, int hertz)
            {
                var path = System.IO.Path.Combine(folder, name);
                var start = new ProcessStartInfo(tools.Ffmpeg) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
                foreach (var argument in new[] { "-v", "error", "-f", "lavfi", "-i", $"testsrc=size={size}:rate=15:duration={seconds}", "-f", "lavfi", "-i",
                    $"sine=frequency={hertz}:duration={seconds}:sample_rate=44100", "-c:v", "ffv1", "-c:a", "pcm_s16le", path }) start.ArgumentList.Add(argument);
                using var process = Process.Start(start)!;
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                Require(process.ExitCode == 0, "ffmpeg made the test movie: " + error);
                return path;
            }
            var first = Make("first.mkv", "160x120", 8, 440);
            var second = Make("second.mkv", "64x48", 1, 880);
            _moviesForSelfTest = true; _movieSourceForSelfTest = null;
            _movieTableForSelfTest = AreaMovieTable.From(_ => "00000.bik", "synthetic table");
            _moviePathForSelfTest = (number, path) => path is not null ? second : number == 1 ? first : number is 7 or 8 ? second : null;
            Require(MovieRefusal() is null, "movies can play: " + MovieRefusal());
            var stamp = 60_000_000L;
            bool Pump(Func<bool> until, int seconds)
            {
                var end = DateTime.UtcNow.AddSeconds(seconds);
                while (!until() && DateTime.UtcNow < end) { TickMovies(50); TickAreaClock(stamp += 50); Thread.Sleep(20); }
                return until();
            }
            var gameBefore = game.QueryClockSnapshot().GameTimestampMs; var realBefore = game.QueryFrameClocks().RealMs;
            var continued = 0;
            Require(PlayMovies([1, 7], 0, () => continued++, "smoke") && MovieCovering, "the movies started");
            Require(_moviePanel is { IsOpen: true } && WorldModal && WorldFrozenFrame, "the movie screen covers the game and the world is a frozen frame");
            Require(Pump(() => _moviePanel!.FramesDrawn >= 3, 15), "frames of the first movie were drawn");
            var size = _moviePanel!.FrameSize;
            Require(size == new Vector2I(160, 120), $"the first movie's frame is 160 x 120 ({size})");
            Require(_moviePanel.AudioPlaying, "the first movie's audio plays");
            Require(game.QueryClockSnapshot().GameTimestampMs == gameBefore && game.QueryFrameClocks().RealMs == realBefore && _frameTap is { Flags.Frozen: true },
                $"no clock of the session moved while the movie played (game {game.QueryClockSnapshot().GameTimestampMs - gameBefore} ms, real {game.QueryFrameClocks().RealMs - realBefore} ms)");
            Require(HandleMovieInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true }) && _moviePlayback!.Running, "a mouse press is taken and does not end the movie");
            // A key press ends the first movie (8 s long, so this is not its end) and the second one starts.
            Require(HandleMovieInput(new InputEventKey { Pressed = true, Keycode = Key.Space }), "the key was taken");
            Require(Pump(() => _moviesSkipped == 1 && _moviePlayback is { } next && next.FramesShown >= 1 && next.Info.Width == 64, 15), $"the key ended the first movie and the second one started (skipped {_moviesSkipped})");
            Require(!_moviePanel.AudioPlaying || _moviePanel.FrameSize == new Vector2I(64, 48), "the first movie's audio stopped with it");
            Require(Pump(() => !MovieCovering, 20), "the second movie ran to its end");
            // ARC-INPUT-2647: the release of the skipping key is taken too (a menu under the movie acts on releases), once.
            Require(HandleMovieInput(new InputEventKey { Pressed = false, Keycode = Key.Space }) && !HandleMovieInput(new InputEventKey { Pressed = false, Keycode = Key.Space }),
                "the release of the key that skipped the movie was taken once after the movie ended");
            Require(continued == 1 && _moviesPlayed >= 1 && !_moviePanel.IsOpen && !WorldModal && !WorldFrozenFrame && !_moviePanel.AudioPlaying,
                $"the screen closed, the world is free and the continuation ran once (continued {continued}, played {_moviesPlayed})");
            // ARC-RENDER-1242: the start-up sequence (the Sierra and the Troika logo by path, then movie 8) plays once, before the first menu is used.
            _startupMoviesDone = false;
            var playedBefore = _moviesPlayed;
            PlayStartupMovies();
            Require(MovieCovering && _movieQueue.Count == 3 && _movieQueue.Select(item => item.ToString()).SequenceEqual(["movies/SierraLogo.bik", "movies/TroikaLogo.bik", "8"]),
                "the start-up queue is the two logos by path and then movie 8");
            Require(Pump(() => !MovieCovering, 30) && _moviesPlayed == playedBefore + 3, $"the start-up movies played in full (played {_moviesPlayed - playedBefore} of 3)");
            PlayStartupMovies();
            Require(!MovieCovering, "the start-up movies play once a run");
            // ARC-UI-3453: with the skip option the start-up queue never starts, and other movies still play.
            (_startupMoviesDone, playedBefore) = (false, _moviesPlayed);
            var skipBefore = _startupMoviesSkip;
            SkipStartupMovies("smoke");
            PlayStartupMovies();
            Require(!MovieCovering && _startupMoviesDone && _moviesPlayed == playedBefore, "the skipped start-up movies never start");
            Require(PlayMovies([7], 0, () => continued++, "smoke") && Pump(() => !MovieCovering, 20) && continued == 2, "another movie still plays with the start-up ones skipped");
            (_startupMoviesSkip, continued) = (skipBefore, 1);
            // Outside the game build and this stage nothing plays: the dev viewer and every other smoke (ARC-RENDER-1242).
            _moviesForSelfTest = false;
            Require(MovieRefusal() == NotGameBuildRefusal && !PlayMovies([1], 0, () => continued++, "smoke") && !MovieCovering && continued == 1,
                "outside the game build and the movies stage no movie plays and nothing waits");
            _moviesForSelfTest = true;
            // A movie with no file plays nothing and the caller goes on (the original's failed open).
            _moviePathForSelfTest = (_, _) => null;
            var failedBefore = _moviesFailed;
            Require(PlayMovies([1], 0, () => continued++, "smoke") && Pump(() => !MovieCovering, 5) && continued == 2 && _moviesFailed == failedBefore + 1 && !_moviePanel.IsOpen,
                "a movie with no file ended at once and the continuation ran");
            // ARC-SCRIPT-1241: the movies of two script fades (action 103; the clock jump belongs to the run's effects, tested apart) start one after the other through the viewer's poll, the
            // second has no file and is passed over, and nothing is left waiting.
            _moviePathForSelfTest = (number, _) => number == 7 ? second : null;
            var (fadesBefore, failedFades) = (_scriptFadesPlayed, _moviesFailed);
            _scriptFades.Enqueue(new AreaScriptFade(0, 7, 0, 0, 0)); _scriptFades.Enqueue(new AreaScriptFade(0, 9, 0, 0, 0));
            SyncScriptFades();
            Require(MovieCovering && _moviePanel!.IsOpen && _scriptFades.Count == 1, "the first script fade's movie started and the second waits");
            Require(Pump(() => { SyncScriptFades(); return !MovieCovering && _scriptFades.Count == 0; }, 20) && _scriptFadesPlayed == fadesBefore + 2 && _moviesFailed == failedFades + 1 && !_moviePanel.IsOpen,
                $"the script fades' movies ran one after the other (started {_scriptFadesPlayed - fadesBefore}, failed {_moviesFailed - failedFades}) and the screen closed");
            // No decoder: nothing starts and the caller goes on at once.
            _movieSourceForSelfTest = new FfmpegMovieSource(null);
            Require(MovieRefusal() is { } refusal && refusal.Contains("ffmpeg", StringComparison.Ordinal) && !PlayMovies([1], 0, () => continued++, "smoke") && !MovieCovering && continued == 2,
                "without a decoder nothing plays and nothing waits");
            await CaptureInstalledMovieAsync();
            GD.Print($"ARC-RENDER-1170 native-smoke movies PASS played={_moviesPlayed} skipped={_moviesSkipped} failed={_moviesFailed} frame={size}");
        }
        finally
        {
            AbortMovies();
            (_moviesForSelfTest, _moviePathForSelfTest, _movieTableForSelfTest, _movieSourceForSelfTest) = (forTest, pathFor, tableFor, sourceFor);
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    // ARC-RENDER-1170: when ARC_AREA_MOVIE_CAPTURE names a folder, the game view and then a frame of the installed movie 8 (800 x 600, through the
    // real table and decoder) are saved there as PNG for the graphics report; nothing is judged and nothing is written without the variable.
    private async Task CaptureInstalledMovieAsync()
    {
        if (System.Environment.GetEnvironmentVariable("ARC_AREA_MOVIE_CAPTURE") is not { Length: > 0 } folder) return;
        _moviesForSelfTest = true; _moviePathForSelfTest = null; _movieTableForSelfTest = null; _movieSourceForSelfTest = null;
        if (MovieRefusal() is { } refusal) { GD.Print($"{MoviesCode} movies capture skipped: {refusal}"); return; }
        Directory.CreateDirectory(folder);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(folder, "before.png"));
        if (!PlayMovies([8], 0, null, "capture")) { GD.Print($"{MoviesCode} movies capture skipped: the installed movie did not start"); return; }
        // The frames come from the app's own per-frame tick (a real run); the stage waits for them and for the screen to be drawn.
        var end = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < end && !(_moviePanel is { FramesDrawn: >= 20 })) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(folder, "after.png"));
        GD.Print($"{MoviesCode} movies capture saved frame={_moviePanel?.FrameSize} drawn={_moviePanel?.FramesDrawn}");
        AbortMovies();
    }
}
