using System.Diagnostics;
using Arcanum.Presentation.Movies;
using Arcanum.Session.Settings;
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
        var pictureFor = _startupPictureForSelfTest;
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
            // ARC-UI-3453: the stage checks the queue also when its run was started with the skip.
            var skipBefore = _startupMoviesSkip;
            (_startupMoviesDone, _startupMoviesSkip) = (false, null);
            var playedBefore = _moviesPlayed;
            // ARC-UI-3501: the loading picture is the third entry; synthetic 80 x 40 bitmaps, the saved number 4 of two files picks the first.
            var drawn = SyntheticStartupBitmap(80, 40);
            _startupPictureForSelfTest = new StartupPictureCheck(["art/splash/a.bmp", "art/splash/b.bmp"], _ => drawn, 4);
            PlayStartupMovies();
            Require(MovieCovering && _movieQueue.Count == 4 && _movieQueue.Select(item => item.ToString()).SequenceEqual(["movies/SierraLogo.bik", "movies/TroikaLogo.bik", "picture", "8"]),
                "the start-up queue is the two logos by path, the loading picture and then movie 8");
            Require(Pump(() => _pictureLeft is not null, 30), "the loading picture came after the logos");
            Require(_moviesPlayed == playedBefore + 2 && _movieQueue.Count == 1 && _startupPictureShown == "art/splash/a.bmp" && _startupPictureNext == 1,
                $"the picture stands between the second logo and movie 8 (played {_moviesPlayed - playedBefore}, left {_movieQueue.Count}, picture {_startupPictureShown}, next {_startupPictureNext})");
            Require(_moviePanel!.IsOpen && _moviePanel.Shown is { } still && still.GetSize() == new Vector2(800, 600) && WorldFrozenFrame, "the original 800 x 600 screen with the picture covers the game");
            Require(HandleMovieInput(new InputEventKey { Pressed = true, Keycode = Key.Space }) && HandleMovieInput(new InputEventKey { Pressed = false, Keycode = Key.Space }), "a key is taken under the picture");
            TickMovies(60_000);
            Require(_pictureLeft is { } leftNow && leftNow > 0 && _movieQueue.Count == 1, "neither a key nor one long frame ends the picture or drops movie 8");
            Require(Pump(() => !MovieCovering, 30) && _moviesPlayed == playedBefore + 3, $"the picture ended by itself and movie 8 played (played {_moviesPlayed - playedBefore} of 3)");
            PlayStartupMovies();
            Require(!MovieCovering, "the start-up movies play once a run");
            // ARC-UI-3453: with the skip option no start-up movie plays, the loading picture still shows (ARC-UI-3501), and other movies still play.
            (_startupMoviesDone, playedBefore) = (false, _moviesPlayed);
            SkipStartupMovies("smoke");
            PlayStartupMovies();
            Require(MovieCovering && _startupMoviesDone && Pump(() => _pictureLeft is not null, 5) && _movieQueue.Count == 0 && Pump(() => !MovieCovering, 20)
                && _moviesPlayed == playedBefore, "with the start-up movies skipped the loading picture shows alone and ends");
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
            // ARC-UI-3501: without a decoder the start-up still shows the loading picture, alone.
            var skipNow = _startupMoviesSkip;
            (_startupMoviesDone, _startupMoviesSkip) = (false, null);
            PlayStartupMovies();
            Require(Pump(() => _pictureLeft is not null, 5) && _movieQueue.Count == 0 && Pump(() => !MovieCovering, 20), "without a decoder the picture shows alone and ends");
            _startupMoviesSkip = skipNow;
            _startupPictureForSelfTest = pictureFor;
            // ARC-UI-3501: the installed game's three pictures are found and each can be drawn.
            Require(AreaSoundCatalog() is not null && _soundContent?.Stack is not null, "the installed content stack opened");
            var stack = _soundContent!.Value.Stack!;
            var installed = AreaStartupPicture.Paths(stack);
            Require(installed.Count > 0 && installed.All(path => AreaStartupPicture.Read(stack, path) is { } bytes && AreaStartupPicture.Compose(bytes) is not null),
                $"every installed start-up picture can be drawn ({string.Join(",", installed)})");
            GD.Print($"ARC-UI-3501 installed start-up pictures={string.Join(",", installed)}");
            CheckStartupPictureRotation(folder, installed);
            await CaptureInstalledMovieAsync();
            await CaptureStartupPicturesAsync();
            GD.Print($"ARC-RENDER-1170 native-smoke movies PASS played={_moviesPlayed} skipped={_moviesSkipped} failed={_moviesFailed} frame={size}");
        }
        finally
        {
            AbortMovies();
            (_moviesForSelfTest, _moviePathForSelfTest, _movieTableForSelfTest, _movieSourceForSelfTest) = (forTest, pathFor, tableFor, sourceFor);
            (_startupPictureForSelfTest, _startupPictureNext) = (pictureFor, null);
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    // ARC-UI-3501: the splash line from file to picture and back: the next start reads what this one saved, and the other settings lines stay.
    private void CheckStartupPictureRotation(string folder, IReadOnlyList<string> installed)
    {
        var file = System.IO.Path.Combine(folder, "arcanum.cfg");
        System.IO.File.WriteAllText(file, "hd=0\nsplash=1\n");
        var storeBefore = _startupPictureStore;
        try
        {
            _startupPictureStore = new StartupPictureStore(file, true);
            _moviesForSelfTest = true; _startupPictureForSelfTest = null;
            for (var start = 1; start <= 2; start++)
            {
                if (!PlayMovieItems([new MovieItem(0, null, Picture: true)], 0, null, "rotation")) throw new InvalidOperationException("AREA_MOVIES_SMOKE_FAILED");
                TickMovies(0);
                var shown = _startupPictureShown;
                AbortMovies();
                SaveStartupPictureCounter();
                var saved = ArcanumConfig.Read(file);
                if (shown != installed[start % installed.Count] || saved.Get("splash") != (start % installed.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) || saved.Get("hd") != "0")
                {
                    GD.Print($"{MoviesCode} movies smoke FAIL: splash rotation start {start} shown {shown} saved {saved.Get("splash")} hd {saved.Get("hd")}");
                    throw new InvalidOperationException("AREA_MOVIES_SMOKE_FAILED");
                }
            }
            GD.Print("ARC-UI-3501 splash rotation read, shown and saved");
        }
        finally { AbortMovies(); (_startupPictureStore, _startupPictureNext) = (storeBefore, null); }
    }

    /// <summary>A plain uncompressed 24-bit bottom-up bitmap of one colour, made for the check.</summary>
    private static byte[] SyntheticStartupBitmap(int width, int height)
    {
        var stride = (width * 3 + 3) / 4 * 4;
        var file = new byte[54 + stride * height];
        using var writer = new BinaryWriter(new MemoryStream(file));
        writer.Write((byte)'B'); writer.Write((byte)'M'); writer.Write(file.Length); writer.Write(0); writer.Write(54);
        writer.Write(40); writer.Write(width); writer.Write(height); writer.Write((short)1); writer.Write((short)24);
        for (var i = 54; i < file.Length; i += 3) if (i + 2 < file.Length) file[i + 1] = 160;
        return file;
    }

    // ARC-UI-3501: when ARC_AREA_STARTUP_PICTURE_CAPTURE names a folder, each installed start-up picture is saved there as the window draws it, in the
    // original look and with HD interface art on (the ARC_HD_PACK pack's 16:9 redraw), at 16:9 and 16:10 window sizes, for the graphics report.
    private async Task CaptureStartupPicturesAsync()
    {
        if (System.Environment.GetEnvironmentVariable("ARC_AREA_STARTUP_PICTURE_CAPTURE") is not { Length: > 0 } folder) return;
        Directory.CreateDirectory(folder);
        var window = GetWindow();
        var (sizeBefore, settingsBefore, nextBefore, composedBefore) = (window.Size, _gameSettings, _startupPictureNext, _originalCompositionForced);
        try
        {
            var stack = _soundContent!.Value.Stack!;
            var paths = AreaStartupPicture.Paths(stack);
            foreach (var (width, height) in new[] { (1920, 1080), (1920, 1200) })
            {
                window.Size = new Vector2I(width, height);
                foreach (var hd in new[] { false, true })
                {
                    _gameSettings = _gameSettings with { HdInterface = hd };
                    // The original look's whole-factor composition, which a smoke run otherwise leaves off.
                    _originalCompositionForced = !hd;
                    for (var index = 0; index < paths.Count; index++)
                    {
                        _startupPictureForSelfTest = new StartupPictureCheck(paths, path => AreaStartupPicture.Read(stack, path), (uint)index);
                        SyncInterfaceHd();
                        for (var frame = 0; frame < 30; frame++) { SyncInterfaceHd(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
                        _moviesForSelfTest = true;
                        if (!PlayMovieItems([new MovieItem(0, null, Picture: true)], 0, null, "capture")) { GD.Print($"{MoviesCode} start-up picture capture not started"); continue; }
                        for (var frame = 0; frame < 10; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        var name = $"{System.IO.Path.GetFileNameWithoutExtension(paths[index])}-{(hd ? "hd" : "original")}-{width}x{height}.png";
                        var shot = GetViewport().GetTexture().GetImage();
                        shot.SavePng(System.IO.Path.Combine(folder, name));
                        GD.Print($"{MoviesCode} start-up picture capture {name} viewport={shot.GetWidth()}x{shot.GetHeight()} window={window.Size.X}x{window.Size.Y}");
                        AbortMovies();
                    }
                }
            }
        }
        finally
        {
            (window.Size, _gameSettings, _startupPictureForSelfTest, _startupPictureNext, _originalCompositionForced) = (sizeBefore, settingsBefore, null, nextBefore, composedBefore);
            SyncInterfaceHd();
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
