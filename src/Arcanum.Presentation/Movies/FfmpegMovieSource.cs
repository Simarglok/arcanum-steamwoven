using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Arcanum.Presentation.Movies;

/// <summary>
/// ARC-RENDER-1170 (owner decision 2026-11-12, option O1 of docs/research/bink-movies.md): the movie decoder is FFmpeg run as a separate
/// program on the player's machine. Nothing of FFmpeg is linked or shipped and no decoded frame or sound is written to disk: the frames
/// come over one pipe and the audio over another process's pipe. The program is found through the <c>ARC_FFMPEG</c> environment variable
/// (the path of the <c>ffmpeg</c> binary), the search path and the usual install folders of Homebrew and Linux; <c>ffprobe</c> is looked
/// for beside it, then the same way.
/// </summary>
public static class FfmpegLocator
{
    public const string EnvironmentVariable = "ARC_FFMPEG";

    private static readonly string[] Folders = ["/opt/homebrew/bin", "/usr/local/bin", "/usr/bin", "/snap/bin"];

    /// <summary>The <c>ffmpeg</c> and <c>ffprobe</c> programs, or null when either is missing.</summary>
    public static FfmpegTools? Find(string? configured = null, string? pathVariable = null)
    {
        configured ??= Environment.GetEnvironmentVariable(EnvironmentVariable);
        pathVariable ??= Environment.GetEnvironmentVariable("PATH");
        var ffmpeg = !string.IsNullOrWhiteSpace(configured) ? (File.Exists(configured) ? configured : null) : Search("ffmpeg", pathVariable);
        if (ffmpeg is null) return null;
        var beside = Path.Combine(Path.GetDirectoryName(ffmpeg) ?? "", OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
        var ffprobe = File.Exists(beside) ? beside : Search("ffprobe", pathVariable);
        return ffprobe is null ? null : new FfmpegTools(ffmpeg, ffprobe);
    }

    private static string? Search(string name, string? pathVariable)
    {
        var exe = OperatingSystem.IsWindows() ? name + ".exe" : name;
        var folders = (pathVariable ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Concat(Folders);
        foreach (var folder in folders)
        {
            try
            {
                var candidate = Path.Combine(folder, exe);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException) { }
        }
        return null;
    }
}

/// <summary>The paths of the decoder programs.</summary>
public sealed record FfmpegTools(string Ffmpeg, string Ffprobe);

/// <summary>Opens movie files through <see cref="FfmpegTools"/>: probes the header with <c>ffprobe</c>, decodes the chosen audio track whole
/// (a movie's longest track is about 29 MB of PCM, 0.2 s of decoding) and streams the video frames from a second <c>ffmpeg</c> process.</summary>
public sealed class FfmpegMovieSource : IMovieSource
{
    /// <summary>The probe and the audio decode must finish within this time or the movie does not open.</summary>
    public const int ProgramTimeoutMilliseconds = 8000;
    /// <summary>The audio of one movie is bounded (the longest shipped track is 162 s, 28.6 MB): a longer decode is cut off and the movie plays without sound.</summary>
    public const long MaxAudioBytes = 256L * 1024 * 1024;
    /// <summary>Decoded frames held ahead of the player: the decoder blocks on its pipe when this many wait.</summary>
    public const int FramesAhead = 8;

    private readonly FfmpegTools? _tools;

    public FfmpegMovieSource(FfmpegTools? tools) { _tools = tools; }

    /// <summary>A source over the programs <see cref="FfmpegLocator.Find"/> finds; without them every open answers <see cref="MovieOpenStatus.NoDecoder"/>.</summary>
    public static FfmpegMovieSource Locate() => new(FfmpegLocator.Find());

    public bool HasDecoder => _tools is not null;

    public MovieOpenResult Open(string path, int track)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (_tools is null) return MovieOpenResult.Failed(MovieOpenStatus.NoDecoder, $"no ffmpeg found (set {FfmpegLocator.EnvironmentVariable} or install ffmpeg)");
        // An absolute path, so a name that starts with a dash is never read as an option by the programs.
        path = Path.GetFullPath(path);
        if (!File.Exists(path)) return MovieOpenResult.Failed(MovieOpenStatus.FileMissing, $"{Path.GetFileName(path)} is not a file");
        MovieInfo? info;
        try { info = Probe(_tools, path); }
        catch (Exception error) when (error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or JsonException)
        {
            return MovieOpenResult.Failed(MovieOpenStatus.Unreadable, "probe failed: " + error.Message);
        }
        if (info is null || !info.Valid) return MovieOpenResult.Failed(MovieOpenStatus.Unreadable, $"{Path.GetFileName(path)} has no usable video header");
        // A track the file does not have plays the default one (the caller's id 0).
        var audio = info.AudioTracks == 0 ? null : DecodeAudio(_tools, path, track >= 0 && track < info.AudioTracks ? track : 0);
        try { return new(MovieOpenStatus.Opened, new FfmpegMovieStream(_tools, path, info, audio), "opened"); }
        catch (Exception error) when (error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return MovieOpenResult.Failed(MovieOpenStatus.Unreadable, "decoder did not start: " + error.Message);
        }
    }

    /// <summary>Reads the header with <c>ffprobe</c>: the first video stream's size and rate and the number of audio streams.</summary>
    internal static MovieInfo? Probe(FfmpegTools tools, string path)
    {
        var start = new ProcessStartInfo(tools.Ffprobe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "-v", "error", "-print_format", "json", "-show_streams", "-show_format", path }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("ffprobe did not start");
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEndAsync();
        if (!process.WaitForExit(ProgramTimeoutMilliseconds)) { Kill(process); throw new InvalidOperationException("ffprobe timed out"); }
        if (process.ExitCode != 0 || !output.Wait(ProgramTimeoutMilliseconds)) return null;
        _ = error;
        return ParseProbe(output.Result);
    }

    /// <summary>Reads the JSON of <c>ffprobe -show_streams</c>.</summary>
    public static MovieInfo? ParseProbe(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("streams", out var streams) || streams.ValueKind != JsonValueKind.Array) return null;
        JsonElement? video = null; var audio = 0;
        foreach (var stream in streams.EnumerateArray())
        {
            var kind = stream.TryGetProperty("codec_type", out var type) ? type.GetString() : null;
            if (kind == "video" && video is null) video = stream;
            else if (kind == "audio") audio++;
        }
        if (video is not { } v) return null;
        var width = Int(v, "width"); var height = Int(v, "height");
        var fps = Rate(v.TryGetProperty("avg_frame_rate", out var average) ? average.GetString() : null)
            ?? Rate(v.TryGetProperty("r_frame_rate", out var real) ? real.GetString() : null) ?? 0;
        var count = Int(v, "nb_frames");
        // The frame count: the stream's own, else its duration, else the container's duration; a file that gives none plays to the decoder's end.
        if (count <= 0 && Seconds(v) is { } streamSeconds) count = (int)Math.Round(streamSeconds * fps);
        if (count <= 0 && document.RootElement.TryGetProperty("format", out var format) && Seconds(format) is { } formatSeconds) count = (int)Math.Round(formatSeconds * fps);
        return new(width, height, fps, count > 0 ? count : MovieInfo.UnknownFrameCount, audio);
    }

    private static double? Seconds(JsonElement element) =>
        element.TryGetProperty("duration", out var duration) && double.TryParse(duration.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0 ? seconds : null;

    private static int Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
            ? value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number
                : int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0
            : 0;

    private static double? Rate(string? text)
    {
        if (text is null) return null;
        var parts = text.Split('/');
        if (parts.Length == 2 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator) && denominator > 0 && numerator > 0)
            return numerator / denominator;
        return parts.Length == 1 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var plain) && plain > 0 ? plain : null;
    }

    private static byte[]? DecodeAudio(FfmpegTools tools, string path, int track)
    {
        var start = Start(tools.Ffmpeg, "-v", "error", "-nostdin", "-i", path, "-map", $"0:a:{track}", "-fs", MaxAudioBytes.ToString(CultureInfo.InvariantCulture), "-f", "s16le", "-ar", "44100", "-ac", "2", "-");
        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("ffmpeg did not start");
            _ = process.StandardError.ReadToEndAsync();
            using var buffer = new MemoryStream();
            var copy = process.StandardOutput.BaseStream.CopyToAsync(buffer, 1 << 16);
            if (!process.WaitForExit(ProgramTimeoutMilliseconds)) { Kill(process); return null; }
            if (!copy.Wait(ProgramTimeoutMilliseconds) || process.ExitCode != 0 || buffer.Length is 0 or > MaxAudioBytes) return null;
            // A whole number of stereo samples.
            var bytes = buffer.ToArray();
            return bytes.Length % 4 == 0 ? bytes : bytes[..(bytes.Length / 4 * 4)];
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or AggregateException) { return null; }
    }

    internal static ProcessStartInfo Start(string program, params string[] arguments)
    {
        var start = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return start;
    }

    internal static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }
}

/// <summary>The video of an opened movie: a decoder process whose RGBA frames a reader thread takes off its pipe into a short queue.</summary>
internal sealed class FfmpegMovieStream : IMovieStream
{
    private readonly Process _process;
    private readonly BlockingCollection<byte[]> _frames = new(FfmpegMovieSource.FramesAhead);
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread _reader;
    private volatile bool _failed;
    private bool _disposed;

    public FfmpegMovieStream(FfmpegTools tools, string path, MovieInfo info, byte[]? audio)
    {
        Info = info; Audio = audio;
        var start = FfmpegMovieSource.Start(tools.Ffmpeg, "-v", "error", "-nostdin", "-i", path, "-map", "0:v:0", "-f", "rawvideo", "-pix_fmt", "rgba", "-");
        _process = Process.Start(start) ?? throw new InvalidOperationException("ffmpeg did not start");
        _ = _process.StandardError.ReadToEndAsync();
        _reader = new Thread(Read) { IsBackground = true, Name = "movie-decoder" };
        _reader.Start();
    }

    public MovieInfo Info { get; }
    public byte[]? Audio { get; }

    private void Read()
    {
        var size = Info.FrameBytes;
        try
        {
            var pipe = _process.StandardOutput.BaseStream;
            while (!_stop.IsCancellationRequested)
            {
                var frame = ArrayPool<byte>.Shared.Rent(size);
                var got = 0;
                while (got < size)
                {
                    var read = pipe.Read(frame, got, size - got);
                    if (read <= 0) break;
                    got += read;
                }
                if (got < size)
                {
                    ArrayPool<byte>.Shared.Return(frame);
                    // A part frame (the decoder died in the middle of one) is a failure; no bytes at all is the end of the stream.
                    if (got > 0) _failed = true;
                    break;
                }
                _frames.Add(frame, _stop.Token);
            }
            if (!_failed && !_stop.IsCancellationRequested) { _process.WaitForExit(2000); _failed = _process.HasExited && _process.ExitCode != 0; }
        }
        catch (Exception error) when (error is OperationCanceledException or IOException or ObjectDisposedException or InvalidOperationException)
        {
            if (!_stop.IsCancellationRequested) _failed = true;
        }
        finally { try { _frames.CompleteAdding(); } catch (ObjectDisposedException) { } }
    }

    public MovieFramePoll PollFrame(byte[] destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (_disposed) return MovieFramePoll.End;
        if (_frames.TryTake(out var frame))
        {
            Buffer.BlockCopy(frame, 0, destination, 0, Info.FrameBytes);
            ArrayPool<byte>.Shared.Return(frame);
            return MovieFramePoll.Frame;
        }
        if (!_frames.IsCompleted) return MovieFramePoll.NotYet;
        return _failed ? MovieFramePoll.Failed : MovieFramePoll.End;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        FfmpegMovieSource.Kill(_process);
        _reader.Join(2000);
        while (_frames.TryTake(out var frame)) ArrayPool<byte>.Shared.Return(frame);
        _process.Dispose();
        _frames.Dispose();
        _stop.Dispose();
    }
}
