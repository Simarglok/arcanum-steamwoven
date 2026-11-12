using System.Diagnostics;
using Arcanum.Presentation.Movies;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Presentation.Tests;

/// <summary>ARC-RENDER-1170: the FFmpeg source. The probe's JSON is parsed on synthetic text; the real decoder runs on a file FFmpeg itself
/// generates (a test pattern with a two-track tone, no game data) and is reported inconclusive when no <c>ffmpeg</c> is installed.</summary>
[TestClass]
public sealed class FfmpegMovieSourceTests
{
    [TestMethod]
    public void The_probe_json_gives_the_size_rate_frames_and_audio_tracks()
    {
        const string json = """
            {"streams":[{"index":0,"codec_type":"video","width":800,"height":400,"r_frame_rate":"15/1","avg_frame_rate":"15/1","duration":"20.133333"},
                        {"index":1,"codec_type":"audio","sample_rate":"44100","channels":2},{"index":2,"codec_type":"audio"}]}
            """;
        var info = FfmpegMovieSource.ParseProbe(json)!;
        Assert.AreEqual(new MovieInfo(800, 400, 15, 302, 2), info, "302 frames from 20.133 s at 15 fps");
        Assert.IsTrue(info.Valid);
        Assert.AreEqual(new MovieInfo(720, 346, 15, 1460, 0), FfmpegMovieSource.ParseProbe("""{"streams":[{"codec_type":"video","width":720,"height":346,"avg_frame_rate":"0/0","r_frame_rate":"15/1","nb_frames":"1460"}]}"""));
        Assert.IsNull(FfmpegMovieSource.ParseProbe("""{"streams":[{"codec_type":"audio"}]}"""), "no video stream");
        Assert.IsNull(FfmpegMovieSource.ParseProbe("""{"programs":[]}"""));
    }

    [TestMethod]
    public void Without_a_decoder_or_a_file_the_open_fails_with_a_reason_and_nothing_throws()
    {
        var none = new FfmpegMovieSource(null);
        Assert.AreEqual(MovieOpenStatus.NoDecoder, none.Open("x.bik", 0).Status);
        Assert.IsFalse(none.HasDecoder);
        var tools = FfmpegLocator.Find();
        if (tools is null) Assert.Inconclusive("no ffmpeg is installed here");
        var missing = new FfmpegMovieSource(tools).Open(Path.Combine(Path.GetTempPath(), "arc-no-such-movie.bik"), 0);
        Assert.AreEqual(MovieOpenStatus.FileMissing, missing.Status);
        Assert.IsNull(missing.Stream);
    }

    [TestMethod]
    public void The_locator_takes_the_configured_program_before_the_search_path()
    {
        Assert.IsNull(FfmpegLocator.Find(Path.Combine(Path.GetTempPath(), "arc-no-such-ffmpeg"), ""), "a configured path that is not a file finds nothing");
    }

    [TestMethod]
    public void The_real_decoder_streams_the_frames_and_the_chosen_audio_track_of_a_generated_movie()
    {
        var tools = FfmpegLocator.Find();
        if (tools is null) Assert.Inconclusive("no ffmpeg is installed here");
        var folder = Directory.CreateTempSubdirectory("arc-movie-test-").FullName;
        try
        {
            var path = Path.Combine(folder, "pattern.mkv");
            // 20 frames of 64 x 48 at 10 fps, a 1 s 440 Hz tone and a 1 s 880 Hz tone as two audio tracks.
            Run(tools.Ffmpeg, "-v", "error", "-f", "lavfi", "-i", "testsrc=size=64x48:rate=10:duration=2", "-f", "lavfi", "-i", "sine=frequency=440:duration=2:sample_rate=44100",
                "-f", "lavfi", "-i", "sine=frequency=880:duration=2:sample_rate=44100", "-map", "0:v", "-map", "1:a", "-map", "2:a", "-c:v", "ffv1", "-c:a", "pcm_s16le", path);
            var source = new FfmpegMovieSource(tools);
            var opened = source.Open(path, 1);
            Assert.IsTrue(opened.Opened, opened.Detail);
            using var stream = opened.Stream!;
            Assert.AreEqual((64, 48, 10.0, 20, 2), (stream.Info.Width, stream.Info.Height, stream.Info.FramesPerSecond, stream.Info.FrameCount, stream.Info.AudioTracks));
            Assert.IsNotNull(stream.Audio);
            Assert.AreEqual(2 * 44100 * 4, stream.Audio!.Length, "two seconds of 16-bit stereo 44.1 kHz");
            var frame = new byte[stream.Info.FrameBytes];
            var got = 0; var deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline)
            {
                var poll = stream.PollFrame(frame);
                if (poll == MovieFramePoll.Frame) { got++; continue; }
                if (poll == MovieFramePoll.NotYet) { Thread.Sleep(5); continue; }
                Assert.AreEqual(MovieFramePoll.End, poll, "a clean end");
                break;
            }
            Assert.AreEqual(20, got, "every frame came");
            Assert.AreEqual(255, frame[3], "RGBA: the alpha byte of the first pixel");
            // The two tracks differ (the second track is the higher tone): the zero crossings of the left channel differ.
            var first = source.Open(path, 0).Stream!; var second = stream;
            Assert.AreNotEqual(Crossings(first.Audio!), Crossings(second.Audio!), "track 1 is not the default track");
            first.Dispose();
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [TestMethod]
    public void A_playback_over_the_real_decoder_runs_to_its_end()
    {
        var tools = FfmpegLocator.Find();
        if (tools is null) Assert.Inconclusive("no ffmpeg is installed here");
        var folder = Directory.CreateTempSubdirectory("arc-movie-test-").FullName;
        try
        {
            var path = Path.Combine(folder, "pattern.mkv");
            Run(tools.Ffmpeg, "-v", "error", "-f", "lavfi", "-i", "testsrc=size=32x32:rate=15:duration=1", "-c:v", "ffv1", path);
            var opened = new FfmpegMovieSource(tools).Open(path, 0);
            Assert.IsTrue(opened.Opened, opened.Detail);
            Assert.IsNull(opened.Stream!.Audio, "a movie with no audio has none");
            var shown = 0;
            using var playback = new MoviePlayback(opened.Stream, new CountingOutput(() => shown++));
            var deadline = DateTime.UtcNow.AddSeconds(15);
            var clock = Stopwatch.StartNew(); var last = 0.0;
            while (playback.Running && DateTime.UtcNow < deadline)
            {
                var now = clock.Elapsed.TotalMilliseconds;
                playback.Advance(now - last); last = now;
                Thread.Sleep(2);
            }
            Assert.AreEqual(MovieEnd.Finished, playback.End);
            Assert.IsTrue(shown >= 8, $"{shown} frames drawn (the decoder starts while the clock runs, so a few early frames may be dropped)");
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    /// <summary>The installed movies (opt-in, <c>DAT_ROOT</c>): every file opens through the real decoder with the header numbers of
    /// docs/research/bink-movies.md section 1, its first frame comes, and movie 4 gives its second audio track. Only numbers are printed.</summary>
    [TestMethod, TestCategory("RealData")]
    public void The_installed_movies_open_with_the_researched_headers_and_movie_4_has_two_audio_tracks()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var tools = FfmpegLocator.Find();
        Assert.IsNotNull(tools, "ffmpeg must be installed for this check");
        var source = new FfmpegMovieSource(tools);
        (string File, int Width, int Height, int Frames, int Tracks)[] expected =
        [
            ("00069.bik", 720, 346, 1460, 1), ("51169.bik", 800, 400, 302, 1), ("91568.bik", 800, 400, 363, 1), ("02112.bik", 800, 400, 2100, 2),
            ("G0021.bik", 800, 400, 414, 1), ("A0021.bik", 800, 400, 2432, 1), ("01138.bik", 720, 346, 1371, 1), ("50000.bik", 800, 600, 171, 1),
        ];
        foreach (var (file, width, height, frames, tracks) in expected)
        {
            var path = Path.Combine(root, "modules", "Arcanum", "movies", file);
            var opened = source.Open(path, 0);
            Assert.IsTrue(opened.Opened, $"{file}: {opened.Detail}");
            using var stream = opened.Stream!;
            Console.WriteLine($"ARC-RENDER-1170 {file} {stream.Info.Width}x{stream.Info.Height} frames={stream.Info.FrameCount} tracks={stream.Info.AudioTracks} audioBytes={stream.Audio?.Length}");
            Assert.AreEqual((width, height, frames, tracks), (stream.Info.Width, stream.Info.Height, stream.Info.FrameCount, stream.Info.AudioTracks), file);
            Assert.IsNotNull(stream.Audio, file + " has audio");
            var frame = new byte[stream.Info.FrameBytes]; var deadline = DateTime.UtcNow.AddSeconds(15); MovieFramePoll poll;
            while ((poll = stream.PollFrame(frame)) == MovieFramePoll.NotYet && DateTime.UtcNow < deadline) Thread.Sleep(5);
            Assert.AreEqual(MovieFramePoll.Frame, poll, file + " first frame");
        }
        using var second = source.Open(Path.Combine(root, "modules", "Arcanum", "movies", "02112.bik"), 1).Stream!;
        using var first = source.Open(Path.Combine(root, "modules", "Arcanum", "movies", "02112.bik"), 0).Stream!;
        Assert.AreEqual(first.Audio!.Length, second.Audio!.Length, "both tracks of movie 4 have the same length");
        Assert.IsFalse(first.Audio.AsSpan().SequenceEqual(second.Audio), "the second track is not the first");
    }

    private sealed class CountingOutput(Action shown) : IMovieOutput
    {
        public void ShowFrame(byte[] rgba, int width, int height) => shown();
        public void StartAudio(byte[] pcm) { }
        public void StopAudio() { }
    }

    private static int Crossings(byte[] pcm)
    {
        var count = 0; short last = 0;
        for (var i = 0; i + 3 < pcm.Length; i += 4)
        {
            var sample = BitConverter.ToInt16(pcm, i);
            if ((last < 0) != (sample < 0)) count++;
            last = sample;
        }
        return count;
    }

    private static void Run(string program, params string[] arguments)
    {
        var start = new ProcessStartInfo(program) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.AreEqual(0, process.ExitCode, error);
    }
}
