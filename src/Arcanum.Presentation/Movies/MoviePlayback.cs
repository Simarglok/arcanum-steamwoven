namespace Arcanum.Presentation.Movies;

/// <summary>
/// ARC-RENDER-1170: one movie playing, on the host's frame time. The original's player loop (0x531530, 0x5317D0) waits until the next frame
/// is due, decodes it, copies it to the screen and goes to the next; a key press ends it (section 2 of docs/research/bink-movies.md). Here
/// the host calls <see cref="Advance"/> every frame with the real milliseconds that passed; the player shows the frame that is due (frames
/// that fell behind are dropped, as Bink's wait does) and starts the audio together with the first frame. Nothing here blocks: a decoder
/// that does not deliver for <see cref="StallLimitMilliseconds"/> ends the movie as failed, so a hung decoder never hangs the game.
/// </summary>
public sealed class MoviePlayback : IDisposable
{
    /// <summary>A frame that does not come for this long ends the movie as <see cref="MovieEnd.Failed"/>.</summary>
    public const double StallLimitMilliseconds = 5000;

    private readonly IMovieStream _stream;
    private readonly IMovieOutput _output;
    private readonly byte[] _frame;
    private double _elapsed;
    private double _stalled;
    private int _next;
    private bool _audioStarted, _audioPlaying;
    private bool _disposed;

    public MoviePlayback(IMovieStream stream, IMovieOutput output)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(output);
        if (!stream.Info.Valid) throw new ArgumentException("The movie's header is not usable.", nameof(stream));
        _stream = stream; _output = output; _frame = new byte[stream.Info.FrameBytes];
    }

    public MovieInfo Info => _stream.Info;

    /// <summary>The index of the next frame to show; the frames shown so far (some dropped) are below it.</summary>
    public int NextFrame => _next;

    /// <summary>How many frames reached the screen (a frame that fell behind and was passed over is not counted).</summary>
    public int FramesShown { get; private set; }

    /// <summary>Real milliseconds played so far.</summary>
    public double ElapsedMilliseconds => _elapsed;

    public MovieEnd End { get; private set; }

    public bool Running => End == MovieEnd.None;

    /// <summary>Moves the movie on by <paramref name="milliseconds"/> of real time and shows the frame that is due. A negative step counts as zero.</summary>
    [OriginalFunction(0x5317D0, OriginalFunctionScope.Partial)]
    public void Advance(double milliseconds)
    {
        if (_disposed || !Running) return;
        var step = Math.Max(0, milliseconds);
        // The clock starts with the first frame on the screen, so the audio (which starts with it) and the picture begin together whatever the decoder's start-up took.
        if (FramesShown > 0) _elapsed += step;
        var info = _stream.Info;
        // Frame k is due when k / fps seconds have passed; the movie is over when the last frame has had its time.
        var due = (int)Math.Min(int.MaxValue - 1, Math.Floor(_elapsed * info.FramesPerSecond / 1000.0));
        var read = 0;
        while (_next <= due && _next < info.FrameCount)
        {
            var poll = _stream.PollFrame(_frame);
            if (poll == MovieFramePoll.NotYet) break;
            if (poll != MovieFramePoll.Frame)
            {
                // The decoder's clean end ends the movie as it stands; a decoder that failed ends it as failed.
                if (read > 0) Show();
                Finish(poll == MovieFramePoll.End ? MovieEnd.Finished : MovieEnd.Failed);
                return;
            }
            _next++; read++;
        }
        // Only the newest due frame is drawn: a frame that fell behind was read to keep the decoder moving and is dropped, as Bink's wait does.
        if (read > 0) { Show(); _stalled = 0; }
        else if (_next <= due && _next < info.FrameCount) _stalled += step;
        if (_stalled >= StallLimitMilliseconds) Finish(MovieEnd.Failed);
        else if (due >= info.FrameCount && _next >= info.FrameCount) Finish(MovieEnd.Finished);
    }

    /// <summary>A key press: ends the movie at once (a movie that has ended already stays ended).</summary>
    public void Skip()
    {
        if (!_disposed && Running) Finish(MovieEnd.Skipped);
    }

    private void Show()
    {
        var info = _stream.Info;
        _output.ShowFrame(_frame, info.Width, info.Height);
        FramesShown++;
        if (_audioStarted) return;
        _audioStarted = true;
        if (_stream.Audio is not { Length: > 0 } audio) return;
        _output.StartAudio(audio);
        _audioPlaying = true;
    }

    private void Finish(MovieEnd end)
    {
        End = end;
        if (_audioPlaying) _output.StopAudio();
        _audioPlaying = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (Running) Finish(MovieEnd.Skipped);
        _disposed = true;
        _stream.Dispose();
    }
}
