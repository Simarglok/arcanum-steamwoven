using Arcanum.Presentation.Movies;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Presentation.Tests;

/// <summary>ARC-RENDER-1170: the movie player on a synthetic decoder (no free Bink encoder exists, so the tests drive the stream contract,
/// docs/research/bink-movies.md section 4): frames on the host's time, the key skip, a failed decoder, a stalled decoder.</summary>
[TestClass]
public sealed class MoviePlaybackTests
{
    private sealed class FakeStream(MovieInfo info, byte[]? audio, int failAfter = int.MaxValue, bool ready = true) : IMovieStream
    {
        public MovieInfo Info { get; } = info;
        public byte[]? Audio { get; } = audio;
        public int Polled;
        public bool Ready = ready;
        public bool Disposed;
        public MovieFramePoll PollFrame(byte[] destination)
        {
            if (!Ready) return MovieFramePoll.NotYet;
            if (Polled >= failAfter) return MovieFramePoll.Failed;
            if (Polled >= Info.FrameCount) return MovieFramePoll.End;
            destination[0] = (byte)Polled;
            Polled++;
            return MovieFramePoll.Frame;
        }
        public void Dispose() => Disposed = true;
    }

    private sealed class Screen : IMovieOutput
    {
        public readonly List<int> Shown = [];
        public int AudioStarts, AudioStops;
        public int AudioStartedAtFrames = -1;
        public void ShowFrame(byte[] rgba, int width, int height) => Shown.Add(rgba[0]);
        public void StartAudio(byte[] pcm) { AudioStarts++; AudioStartedAtFrames = Shown.Count; }
        public void StopAudio() => AudioStops++;
    }

    private static MovieInfo Info(int frames = 30) => new(2, 2, 15, frames, 1);

    [TestMethod]
    public void Frames_come_on_the_hosts_time_and_the_movie_finishes_after_the_last_one()
    {
        var screen = new Screen(); var stream = new FakeStream(Info(), [1, 2, 3, 4]);
        using var playback = new MoviePlayback(stream, screen);
        playback.Advance(0);
        CollectionAssert.AreEqual(new[] { 0 }, screen.Shown, "frame 0 is due at once");
        playback.Advance(66);
        CollectionAssert.AreEqual(new[] { 0 }, screen.Shown, "frame 1 is due at 66.7 ms");
        playback.Advance(1);
        CollectionAssert.AreEqual(new[] { 0, 1 }, screen.Shown, "frame 1 at 67 ms");
        Assert.AreEqual((1, 1), (screen.AudioStarts, screen.AudioStartedAtFrames), "the audio starts with the first frame, once");
        // A long frame time drops frames: the newest due frame is shown, the others are read and passed over.
        playback.Advance(400);
        Assert.AreEqual(7, screen.Shown[^1], "at 467 ms frame 7 is due");
        Assert.AreEqual(3, screen.Shown.Count, "the frames in between were dropped, not drawn");
        playback.Advance(1600 + 5);
        Assert.AreEqual(MovieEnd.Finished, playback.End);
        Assert.AreEqual(29, screen.Shown[^1], "the last frame was shown");
        Assert.AreEqual(1, screen.AudioStops, "the audio stops at the end");
    }

    [TestMethod]
    public void The_clock_starts_with_the_first_frame_so_a_slow_decoder_start_drops_nothing()
    {
        var screen = new Screen(); var stream = new FakeStream(Info(), [1, 2, 3, 4], ready: false);
        using var playback = new MoviePlayback(stream, screen);
        for (var i = 0; i < 8; i++) playback.Advance(50);
        Assert.AreEqual(0, screen.Shown.Count);
        stream.Ready = true;
        playback.Advance(50);
        CollectionAssert.AreEqual(new[] { 0 }, screen.Shown, "400 ms of waiting did not eat frames: the first frame shown is frame 0, with the audio");
        Assert.AreEqual(1, screen.AudioStarts);
    }

    [TestMethod]
    public void A_key_skips_at_once_and_stops_the_audio()
    {
        var screen = new Screen(); var stream = new FakeStream(Info(), [1, 2, 3, 4]);
        var playback = new MoviePlayback(stream, screen);
        playback.Advance(100);
        playback.Skip();
        Assert.AreEqual(MovieEnd.Skipped, playback.End);
        Assert.AreEqual(1, screen.AudioStops);
        var shown = screen.Shown.Count;
        playback.Advance(5000); playback.Skip();
        Assert.AreEqual((shown, MovieEnd.Skipped, 1), (screen.Shown.Count, playback.End, screen.AudioStops), "an ended movie stays ended and draws no more");
        playback.Dispose();
        Assert.IsTrue(stream.Disposed, "disposing closes the decoder");
    }

    [TestMethod]
    public void A_movie_without_audio_starts_none()
    {
        var screen = new Screen();
        using var playback = new MoviePlayback(new FakeStream(Info(), null), screen);
        playback.Advance(100);
        playback.Skip();
        Assert.AreEqual((0, 0), (screen.AudioStarts, screen.AudioStops));
    }

    [TestMethod]
    public void A_failed_decoder_ends_the_movie_as_failed_after_the_frames_it_gave()
    {
        var screen = new Screen();
        using var playback = new MoviePlayback(new FakeStream(Info(), [1, 2, 3, 4], failAfter: 4), screen);
        playback.Advance(0); playback.Advance(1000);
        Assert.AreEqual(MovieEnd.Failed, playback.End);
        Assert.AreEqual(3, screen.Shown[^1], "the last good frame was drawn");
        Assert.AreEqual(1, screen.AudioStops);
    }

    [TestMethod]
    public void A_decoder_that_gives_nothing_ends_the_movie_after_the_stall_limit_and_never_hangs()
    {
        var screen = new Screen(); var stream = new FakeStream(Info(), [1, 2, 3, 4], ready: false);
        using var playback = new MoviePlayback(stream, screen);
        for (var i = 0; i < 49; i++) playback.Advance(100);
        Assert.AreEqual(MovieEnd.None, playback.End, "4.9 s of waiting is within the limit");
        playback.Advance(100);
        Assert.AreEqual(MovieEnd.Failed, playback.End);
        Assert.AreEqual(0, screen.Shown.Count, "nothing was drawn");
    }

    [TestMethod]
    public void A_stalled_decoder_that_recovers_resets_the_stall()
    {
        var screen = new Screen(); var stream = new FakeStream(Info(200), [1, 2, 3, 4], ready: false);
        using var playback = new MoviePlayback(stream, screen);
        for (var i = 0; i < 40; i++) playback.Advance(100);
        stream.Ready = true;
        playback.Advance(100);
        Assert.AreEqual(MovieEnd.None, playback.End);
        Assert.AreEqual(1, screen.Shown.Count);
    }

    [TestMethod]
    public void A_decoder_that_ends_cleanly_ends_the_movie_as_finished()
    {
        var screen = new Screen();
        using var playback = new MoviePlayback(new FakeStream(new(2, 2, 15, 10, 1), null, failAfter: int.MaxValue), screen);
        playback.Advance(0); playback.Advance(5000);
        Assert.AreEqual(MovieEnd.Finished, playback.End);
        Assert.AreEqual(9, screen.Shown[^1]);
    }

    [TestMethod]
    public void A_header_that_is_not_usable_is_refused()
    {
        Assert.IsFalse(new MovieInfo(0, 2, 15, 3, 0).Valid);
        Assert.IsFalse(new MovieInfo(2, 2, 0, 3, 0).Valid);
        Assert.IsFalse(new MovieInfo(2, 2, 15, 0, 0).Valid);
        Assert.IsFalse(new MovieInfo(100000, 100000, 15, 3, 0).Valid);
        Assert.IsTrue(new MovieInfo(800, 600, 15, 171, 1).Valid);
        Assert.ThrowsExactly<ArgumentException>(() => new MoviePlayback(new FakeStream(new(0, 0, 15, 3, 0), null), new Screen()));
    }
}
