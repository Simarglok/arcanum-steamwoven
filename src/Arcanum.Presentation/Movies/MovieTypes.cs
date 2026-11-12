namespace Arcanum.Presentation.Movies;

/// <summary>What the player needs to know about an opened movie: the frame size and rate, the number of frames and the number of audio
/// tracks the file has (docs/research/bink-movies.md section 1: the shipped movies are 15 fps, up to 800 × 600, one audio track, movie 4 has two).</summary>
public sealed record MovieInfo(int Width, int Height, double FramesPerSecond, int FrameCount, int AudioTracks)
{
    /// <summary>The frame count of a file that states none: it plays until the decoder ends.</summary>
    public const int UnknownFrameCount = 1 << 24;

    /// <summary>The frame size must fit this many RGBA bytes (a bound against a bad header, not a game limit).</summary>
    public const long MaxFrameBytes = 4096L * 4096 * 4;

    /// <summary>True when the numbers are usable: a positive size within <see cref="MaxFrameBytes"/>, a rate of 1 to 120 fps, at least one frame.</summary>
    public bool Valid => Width > 0 && Height > 0 && (long)Width * Height * 4 <= MaxFrameBytes && FramesPerSecond is >= 1 and <= 120 && FrameCount > 0;

    /// <summary>The bytes of one RGBA frame.</summary>
    public int FrameBytes => checked(Width * Height * 4);

    /// <summary>The audio an opened movie carries: signed 16-bit little-endian stereo samples at 44.1 kHz (every shipped movie, section 1).</summary>
    public const int AudioSampleRate = 44100;
}

/// <summary>Why a movie did not open. A failed open plays nothing and returns control (the original's player returns an error code and the
/// caller goes on, section 2); it is never a hang or a black screen.</summary>
public enum MovieOpenStatus
{
    Opened,
    /// <summary>No decoder program was found (neither <c>ARC_FFMPEG</c>, nor the search path, nor the usual install folders).</summary>
    NoDecoder,
    /// <summary>The file is not there (or is not a file).</summary>
    FileMissing,
    /// <summary>The decoder could not read the file, or its header gave a size or rate outside <see cref="MovieInfo.Valid"/>.</summary>
    Unreadable,
}

/// <summary>The result of opening a movie: the stream, or the reason and a detail line for the log.</summary>
public sealed record MovieOpenResult(MovieOpenStatus Status, IMovieStream? Stream, string Detail)
{
    public bool Opened => Status == MovieOpenStatus.Opened && Stream is not null;
    public static MovieOpenResult Failed(MovieOpenStatus status, string detail) => new(status, null, detail);
}

/// <summary>The answer of <see cref="IMovieStream.PollFrame"/>.</summary>
public enum MovieFramePoll
{
    /// <summary>A frame was copied to the destination.</summary>
    Frame,
    /// <summary>The next frame is not decoded yet; ask again later.</summary>
    NotYet,
    /// <summary>The decoder ended cleanly: no more frames come.</summary>
    End,
    /// <summary>The decoder stopped with an error: no more frames come.</summary>
    Failed,
}

/// <summary>An opened movie as the player sees it: the decoder's contract, a frame source plus the whole audio of the chosen track. The real
/// source runs an external decoder program (<see cref="FfmpegMovieSource"/>); the tests give the player a synthetic one, because no free
/// Bink encoder exists (section 4).</summary>
public interface IMovieStream : IDisposable
{
    MovieInfo Info { get; }

    /// <summary>The chosen audio track as 16-bit stereo 44.1 kHz little-endian PCM, or null when the movie has no audio or the track did not decode.</summary>
    byte[]? Audio { get; }

    /// <summary>Copies the next RGBA frame (<see cref="MovieInfo.FrameBytes"/> bytes) to <paramref name="destination"/> when it is ready. Never blocks.</summary>
    MovieFramePoll PollFrame(byte[] destination);
}

/// <summary>Opens a movie file. <paramref name="track"/> is the audio track id of the caller (movie 4's second track is 1); 0 is the default track.</summary>
public interface IMovieSource
{
    MovieOpenResult Open(string path, int track);
}

/// <summary>What the player draws and plays on: the host's screen and sound. Called from the player's thread only.</summary>
public interface IMovieOutput
{
    /// <summary>Shows one frame (RGBA, <paramref name="width"/> × <paramref name="height"/>).</summary>
    void ShowFrame(byte[] rgba, int width, int height);

    /// <summary>Starts the movie's audio (16-bit stereo 44.1 kHz PCM) once, with the first frame.</summary>
    void StartAudio(byte[] pcm);

    /// <summary>Stops the audio (a skip, an end or a failure).</summary>
    void StopAudio();
}

/// <summary>How a playback ended.</summary>
public enum MovieEnd
{
    /// <summary>It has not ended.</summary>
    None,
    /// <summary>Every frame was shown.</summary>
    Finished,
    /// <summary>The player pressed a key (every shipped movie is skippable, section 2).</summary>
    Skipped,
    /// <summary>The decoder stopped before the last frame, or no frame came within <see cref="MoviePlayback.StallLimitMilliseconds"/>.</summary>
    Failed,
}
