using Arcanum.Presentation.Movies;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-RENDER-1170 (owner decision 2026-11-12, docs/research/bink-movies.md): the original's movie player (0x531530) in the viewer. A movie
// is the installed Bink file of the movie table (movies\movies.mes) decoded by the player's own ffmpeg program (FfmpegMovieSource, O1);
// nothing of the movie is written to disk, and without a decoder or a table nothing plays and nothing waits (the failed open of the
// original: the caller goes on). It covers the whole window, ends on any key press (every shipped movie is skippable, section 2), and the
// world stands still under it as it does under the death slides (TL25: a movie does not ping). The callers: the main menu's View Intro
// (movies 1 and 7), a new game's start (movies 1 and 7, the request's flags 0x45) and (ARC-SCRIPT-1241) the script action 103 fade, whose movies 2 to 6 the dialog, use, tile, sector and
// dying scripts hand over after jumping the game clock (SyncScriptFades). The start-up movie 8 and the two logos are not
// played (owner: not needed in the viewer). Sound: the scene's and the menu's sounds stop for the movie and nothing starts under it
// (the original locks its sound system around a movie, 0x41C610; the session's own lock state is not touched here, as no scheme
// request can come while the world stands still).
public partial class TerrainViewerApp
{
    private AreaMoviePanel? _moviePanel;
    private MoviePlayback? _moviePlayback;
    private readonly Queue<int> _movieQueue = new();
    private bool _movieActive;
    private Action? _movieDone;
    private int _movieTrack;
    private IMovieSource? _movieSource;
    private AreaMovieTable? _movieTable;
    private string? _movieTableModule;
    private string? _movieCache;
    private IMovieSource? _movieSourceForSelfTest;
    private AreaMovieTable? _movieTableForSelfTest;
    private Func<int, string?>? _moviePathForSelfTest;
    private bool _moviesForSelfTest;
    private string? _movieStatus;
    private int _moviesPlayed, _moviesSkipped, _moviesFailed;
    // ARC-SCRIPT-1241: the movies the scripts' fades asked for and the viewer has not started yet, in order (a run with two movie fades plays them one after the other).
    private readonly Queue<AreaScriptFade> _scriptFades = new();
    private int _scriptFadesPlayed;

    /// <summary>A movie covers the game: it owns the input and the world stands still under it.</summary>
    private bool MovieCovering => _movieActive;

    private IMovieSource MovieSource() => _movieSourceForSelfTest ?? (_movieSource ??= FfmpegMovieSource.Locate());

    /// <summary>The movie table of the shown module, read once a module (none in the self-test or without a game folder).</summary>
    private AreaMovieTable MovieTable()
    {
        if (_movieTableForSelfTest is { } synthetic) return synthetic;
        if (_arguments?.ModulePath is not { } module || SelfTestBootstrap) return AreaMovieTable.None("no game folder");
        if (_movieTable is { } cached && _movieTableModule == module) return cached;
        var table = AreaSoundCatalog() is not null && _soundContent?.Stack is { } stack ? AreaMovieTable.Load(stack)
            : AreaMovieTable.None("no content stack: " + (_soundContent?.Detail ?? "not opened"));
        GD.Print("ARC-RENDER-1170 movie table " + table.Detail);
        if (table.Loaded) { _movieTable = table; _movieTableModule = module; }
        return table;
    }

    /// <summary>Why movies cannot play (null: they can): no decoder program, or no movie table in the shown module.</summary>
    private string? MovieRefusal()
    {
        if (MovieSource() is FfmpegMovieSource { HasDecoder: false }) return $"movies need ffmpeg (install it or set {FfmpegLocator.EnvironmentVariable})";
        var table = MovieTable();
        return table.Loaded ? null : "the movie table is not read: " + table.Detail;
    }

    /// <summary>An automated run (a smoke, a self-test, an exported check) plays no movie unless its own check turns them on: movies last
    /// seconds to minutes, wait for a key, and many runs start new games for other checks.</summary>
    private bool MoviesEnabled => _moviesForSelfTest || !AutomatedVisualRun;

    /// <summary>Plays <paramref name="numbers"/> one after another on the movie screen, each ended by a key or by its last frame, then runs
    /// <paramref name="done"/>. False, and nothing started (the caller goes on at once), when movies cannot play or are off for this run.</summary>
    private bool PlayMovies(IReadOnlyList<int> numbers, int track, Action? done, string reason)
    {
        if (_movieActive || !MoviesEnabled || MovieRefusal() is { } refusal)
        {
            if (MoviesEnabled && !_movieActive) GD.Print($"ARC-RENDER-1170 movies {string.Join(",", numbers)} not played ({reason}): {MovieRefusal()}");
            return false;
        }
        _movieQueue.Clear();
        foreach (var number in numbers) _movieQueue.Enqueue(number);
        _movieTrack = track; _movieDone = done; _movieActive = true;
        _movieStatus = $"playing {string.Join(",", numbers)} ({reason})";
        GD.Print($"ARC-RENDER-1170 movies {string.Join(",", numbers)} start ({reason})");
        // The scene's and the menu's sounds stand still for the movies; nothing starts under them (TickAudio).
        StopMenuMusic(); _audio?.StopAll();
        (_moviePanel ??= NewMoviePanel()).Open();
        return true;
    }

    private AreaMoviePanel NewMoviePanel()
    {
        var panel = new AreaMoviePanel();
        AddChild(panel);
        return panel;
    }

    /// <summary>Per frame, on the frame's real time: starts the next queued movie when none plays and moves the playing one on.</summary>
    private void TickMovies(double milliseconds)
    {
        if (!_movieActive) return;
        try
        {
            // The movie's own time is real time; a frame that took long (a hiccup) drops frames, it does not stretch the movie.
            if (_moviePlayback is null && !OpenNextMovie()) { EndMovies(); return; }
            if (_moviePlayback is not { } playback) return;
            playback.Advance(milliseconds);
            if (playback.Running) return;
            var end = playback.End;
            if (end == MovieEnd.Skipped) _moviesSkipped++; else if (end == MovieEnd.Failed) _moviesFailed++; else _moviesPlayed++;
            GD.Print($"ARC-RENDER-1170 movie ended {end} frames={playback.FramesShown}");
            playback.Dispose(); _moviePlayback = null;
        }
        catch (Exception error)
        {
            // A movie must never lock the game: whatever went wrong, the screen closes and the caller goes on.
            GD.Print($"ARC-RENDER-1170 movie failed: {error.GetType().Name} {error.Message}");
            _moviesFailed++;
            EndMovies();
        }
    }

    /// <summary>Opens the next movie of the queue that opens; a movie that does not (no number, no file, no decode) is passed over, as the
    /// original's failed open returns to its caller. False when the queue is empty.</summary>
    private bool OpenNextMovie()
    {
        while (_movieQueue.Count > 0)
        {
            var number = _movieQueue.Dequeue();
            var path = _moviePathForSelfTest?.Invoke(number) ?? (MovieTable() is { } table && _soundContent?.Stack is { } stack
                ? table.Path(stack, number, _movieCache ??= System.IO.Path.Combine(System.IO.Path.GetTempPath(), "arcanum-steamwoven-movies-" + System.Environment.UserName)) : null);
            if (path is null) { _moviesFailed++; GD.Print($"ARC-RENDER-1170 movie {number}: no file, nothing plays"); continue; }
            var opened = MovieSource().Open(path, _movieTrack);
            if (!opened.Opened) { _moviesFailed++; GD.Print($"ARC-RENDER-1170 movie {number}: not opened ({opened.Status}: {opened.Detail})"); continue; }
            _moviePlayback = new MoviePlayback(opened.Stream!, _moviePanel!);
            var info = _moviePlayback.Info;
            GD.Print($"ARC-RENDER-1170 movie {number} opened {info.Width}x{info.Height} {info.FramesPerSecond:0.##} fps frames={info.FrameCount} audioTracks={info.AudioTracks} track={_movieTrack}");
            return true;
        }
        return false;
    }

    private void EndMovies()
    {
        _movieActive = false;
        _moviePlayback?.Dispose(); _moviePlayback = null;
        _movieQueue.Clear();
        _moviePanel?.Close();
        _movieStatus = null;
        var done = _movieDone; _movieDone = null;
        GD.Print($"ARC-RENDER-1170 movies done played={_moviesPlayed} skipped={_moviesSkipped} failed={_moviesFailed}");
        done?.Invoke();
        // The menu's song stopped for the movies; it plays again when the main menu still stands (a started game ended the menu).
        if (_mainMenu is { Root: AreaMenuWindow.Main }) StartMenuMusic();
    }

    /// <summary>
    /// ARC-SCRIPT-1241: the movies of the script fades (action 103) the dialog host and the use host collected, started one at a time: the clock jump was applied with the run's effects, the movie
    /// plays now (the original plays it inside the action and the run goes on after it; here the run has finished and the movie covers the game until it ends or a key skips it). A movie
    /// that cannot play (no decoder, no table, an automated run) is dropped at once, as the original's failed open returns to its caller.
    /// </summary>
    private void SyncScriptFades()
    {
        if (_readyMode != TerrainViewerMode.Area) { _scriptFades.Clear(); return; }
        if (_conversationHost is { } talk) foreach (var fade in talk.Scripts.TakeFades()) _scriptFades.Enqueue(fade);
        if (_useScriptHost is { } use) foreach (var fade in use.Host.TakeFades()) _scriptFades.Enqueue(fade);
        while (!_movieActive && _scriptFades.Count > 0)
        {
            var fade = _scriptFades.Dequeue();
            if (PlayMovies([fade.Movie], fade.Track, null, "script fade")) { _scriptFadesPlayed++; break; }
        }
    }

    /// <summary>The scene ended or the app closes: a movie in progress stops without its continuation.</summary>
    private void AbortMovies()
    {
        _scriptFades.Clear();
        if (!_movieActive) return;
        _movieDone = null;
        EndMovies();
    }

    /// <summary>Any input while a movie plays; true when it took it (it takes all). A key press ends the movie (a press, not an auto-repeat).</summary>
    private bool HandleMovieInput(InputEvent input)
    {
        if (!MovieCovering) return false;
        if (input is InputEventKey { Pressed: true, Echo: false })
        {
            GD.Print("ARC-RENDER-1170 movie key skip");
            // Between two movies (none open yet) a key drops the rest of the queue.
            if (_moviePlayback is { } playback) playback.Skip(); else _movieQueue.Clear();
        }
        return true;
    }

    /// <summary>Controller: confirm or cancel is a key press.</summary>
    private void HandleMoviePulse(IntentFrame intent)
    {
        if ((intent.Confirm || intent.Cancel) && _moviePlayback is { } playback) playback.Skip();
    }

    /// <summary>The caller of a new game's start (movies 1 and 7, the request's flags 0x45): plays them first and then runs
    /// <paramref name="start"/>, or runs it at once when they cannot play.</summary>
    private void WithNewGameMovies(Action start)
    {
        if (!PlayMovies([1, 7], 0, start, "new game")) start();
    }
}
