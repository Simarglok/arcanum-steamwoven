using System.Buffers.Binary;
using System.Text;
using Arcanum.Data.Content;

namespace Arcanum.Data.Audio;

public enum SoundPayloadKind
{
    /// <summary>RIFF WAVE with uncompressed PCM.</summary>
    Wav,
    /// <summary>MPEG audio Layer III frames, optionally after an ID3v2 tag.</summary>
    Mp3,
}

public enum SoundPayloadStatus
{
    /// <summary>The bytes are in a class a player can decode (<see cref="SoundPayload.Wav"/> or <see cref="SoundPayload.Mp3"/>).</summary>
    Admitted,
    /// <summary>The file is not in the stack (or the <see cref="SoundFile"/> was not <see cref="SoundFileStatus.Found"/>).</summary>
    Missing,
    InvalidPath,
    /// <summary>Over <see cref="SoundPayloadOptions.MaxBytes"/>, or over the stack's or archive's own limits.</summary>
    TooLarge,
    /// <summary>The archive or file could not be read.</summary>
    Unreadable,
    /// <summary>Neither a RIFF WAVE file nor MPEG audio.</summary>
    UnknownContainer,
    /// <summary>A well-formed file of a class outside the admitted set (compressed WAV, other rates or sample sizes,
    /// MPEG layers I/II, free-format bit rate, ...).</summary>
    Unsupported,
    /// <summary>The structure is inconsistent: truncated or overlapping chunks, a missing or repeated fmt/data chunk, a
    /// loop outside the samples, no two consecutive MPEG frames.</summary>
    Malformed,
}

/// <summary>A <c>smpl</c> loop: frame numbers of its first and last sample (inclusive) and the play count (0 = forever).</summary>
public sealed record WavLoop(int Type, int StartFrame, int EndFrame, int PlayCount);

/// <param name="DataOffset">Byte offset of the PCM samples in the payload.</param>
/// <param name="FrameCount">Whole sample frames in the data chunk; a trailing partial frame is not counted.</param>
/// <param name="IgnoredTailBytes">Bytes after the fmt and data chunks that do not form a chunk (a damaged header running
/// past the end); they are not audio and are not read.</param>
public sealed record WavFormat(int Channels, int SampleRate, int BitsPerSample, int DataOffset, int DataLength, int FrameCount, IReadOnlyList<WavLoop> Loops,
    int IgnoredTailBytes)
{
    public int BlockAlign => Channels * BitsPerSample / 8;
    public double DurationSeconds => (double)FrameCount / SampleRate;
}

/// <param name="Version">1, 2 or 25 (MPEG-2.5).</param>
/// <param name="FirstFrameOffset">Byte offset of the first frame header (after an ID3v2 tag, when there is one).</param>
/// <param name="FirstFrameBitrate">Bit rate of the first frame in kbit/s; later frames may differ (VBR).</param>
public sealed record Mp3Format(int Version, int SampleRate, bool Stereo, int FirstFrameOffset, int FirstFrameBitrate);

/// <param name="Key">The virtual path the payload came from.</param>
/// <param name="Bytes">The whole payload for <see cref="SoundPayloadStatus.Admitted"/> (and for <see cref="SoundPayloadStatus.Unsupported"/>,
/// so a caller can report it); empty otherwise. Owned by the caller; nothing else keeps it.</param>
/// <param name="Detail">Why a payload was not admitted, in words (never payload bytes).</param>
public sealed record SoundPayload(SoundPayloadStatus Status, string Key, SoundPayloadKind? Kind, WavFormat? Wav, Mp3Format? Mp3, ReadOnlyMemory<byte> Bytes, string? Detail = null);

public sealed class SoundPayloadOptions
{
    /// <summary>Upper bound on one payload; the largest installed file (a music track) is under 3 MiB.</summary>
    public int MaxBytes { get; init; } = 32 * 1024 * 1024;
    /// <summary>Upper bound on RIFF chunks walked in one WAV file.</summary>
    public int MaxWavChunks { get; init; } = 128;
    /// <summary>Upper bound on <c>smpl</c> loops in one WAV file (all chunks together).</summary>
    public int MaxWavLoops { get; init; } = 16;
    /// <summary>How far after an ID3v2 tag (or the file start) the first MPEG frame may begin.</summary>
    public int MaxMp3SyncSearchBytes { get; init; } = 64 * 1024;

    internal void Validate()
    {
        if (MaxBytes <= 0 || MaxWavChunks <= 0 || MaxWavLoops < 0 || MaxMp3SyncSearchBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(SoundPayloadOptions), "Sound payload limits must be positive.");
    }
}

/// <summary>
/// ARC-DATA-041: admits a sound payload for playback by its content (docs/audio/sound-catalog.md#payloads): RIFF WAVE PCM
/// (8- or 16-bit, mono or stereo, 8–48 kHz) or MPEG-1/2/2.5 Layer III with two consecutive valid frames, the classes the
/// installed original uses. Nothing is decoded; a player receives the admitted bytes and the format.
/// </summary>
public static class SoundPayloadReader
{
    private static readonly int[] WavRates = [8000, 11025, 16000, 22050, 32000, 44100, 48000];

    public static SoundPayload Inspect(string key, ReadOnlyMemory<byte> payload, SoundPayloadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        options ??= new();
        options.Validate();
        if (payload.Length > options.MaxBytes) return Refuse(SoundPayloadStatus.TooLarge, key, null, "over the payload limit");
        var bytes = payload.Span;
        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WAVE"u8)) return Wav(key, payload, options);
        if (Mp3Start(bytes, options, out var failure) is { } frame) return Mp3(key, payload, frame);
        return failure is null ? Refuse(SoundPayloadStatus.UnknownContainer, key, null, "neither RIFF WAVE nor MPEG audio")
            : Refuse(SoundPayloadStatus.Malformed, key, SoundPayloadKind.Mp3, failure);
    }

    private static SoundPayload Wav(string key, ReadOnlyMemory<byte> payload, SoundPayloadOptions options)
    {
        var bytes = payload.Span;
        int? fmtAt = null, fmtSize = null, dataAt = null, dataSize = null;
        var loopChunks = new List<(int At, int Size)>();
        var at = 12; var chunks = 0; var ignored = 0;
        while (at < bytes.Length)
        {
            if (++chunks > options.MaxWavChunks) return Refuse(SoundPayloadStatus.Malformed, key, SoundPayloadKind.Wav, "too many chunks");
            if (bytes.Length - at < 8)
            {
                // A short tail after the last chunk (padding written by some tools) carries no chunk.
                if (bytes[at..].IndexOfAnyExcept((byte)0) < 0) break;
                return Refuse(SoundPayloadStatus.Malformed, key, SoundPayloadKind.Wav, "truncated chunk header");
            }
            var id = bytes.Slice(at, 4);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(at + 4, 4));
            var body = at + 8;
            if (size > (uint)(bytes.Length - body))
            {
                // Some installed files carry unrelated bytes after their samples. Once both fmt and data are found, a
                // damaged chunk after them ends the walk; it is reported, never read as audio.
                if (fmtAt is not null && dataAt is not null) { ignored = bytes.Length - at; break; }
                return Refuse(SoundPayloadStatus.Malformed, key, SoundPayloadKind.Wav, $"chunk '{Ascii(id)}' runs past the end");
            }
            if (id.SequenceEqual("fmt "u8)) { if (fmtAt is not null) return Refuse(SoundPayloadStatus.Malformed, key, SoundPayloadKind.Wav, "two fmt chunks"); (fmtAt, fmtSize) = (body, (int)size); }
            else if (id.SequenceEqual("data"u8)) { if (dataAt is not null) return Refuse(SoundPayloadStatus.Malformed, key, SoundPayloadKind.Wav, "two data chunks"); (dataAt, dataSize) = (body, (int)size); }
            else if (id.SequenceEqual("smpl"u8)) loopChunks.Add((body, (int)size));
            at = body + (int)size + (int)(size & 1);
        }
        if (fmtAt is null || dataAt is null) return Refuse(SoundPayloadStatus.Malformed, key, SoundPayloadKind.Wav, "no fmt or data chunk");
        if (fmtSize < 16) return Refuse(SoundPayloadStatus.Malformed, key, SoundPayloadKind.Wav, "fmt chunk shorter than 16 bytes");
        var fmt = bytes.Slice(fmtAt.Value, 16);
        var tag = BinaryPrimitives.ReadUInt16LittleEndian(fmt);
        var channels = BinaryPrimitives.ReadUInt16LittleEndian(fmt[2..]);
        var rate = BinaryPrimitives.ReadUInt32LittleEndian(fmt[4..]);
        var byteRate = BinaryPrimitives.ReadUInt32LittleEndian(fmt[8..]);
        var blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(fmt[12..]);
        var bits = BinaryPrimitives.ReadUInt16LittleEndian(fmt[14..]);
        if (tag != 1) return Unsupported(key, payload, SoundPayloadKind.Wav, $"format tag {tag} is not PCM");
        if (channels is not (1 or 2) || bits is not (8 or 16) || Array.IndexOf(WavRates, (int)rate) < 0)
            return Unsupported(key, payload, SoundPayloadKind.Wav, $"{channels} channels, {bits} bits, {rate} Hz");
        if (blockAlign != channels * bits / 8 || byteRate != rate * blockAlign)
            return Refuse(SoundPayloadStatus.Malformed, key, SoundPayloadKind.Wav, "block align or byte rate disagrees with the format");
        var frames = dataSize!.Value / blockAlign;
        var loops = new List<WavLoop>();
        foreach (var (loopAt, loopSize) in loopChunks)
        {
            if (loopSize < 36) return Refuse(SoundPayloadStatus.Malformed, key, SoundPayloadKind.Wav, "smpl chunk shorter than its header");
            var count = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(loopAt + 28, 4));
            if (count > (uint)(options.MaxWavLoops - loops.Count)) return Refuse(SoundPayloadStatus.Malformed, key, SoundPayloadKind.Wav, "too many loops");
            if (36 + count * 24 > (uint)loopSize) return Refuse(SoundPayloadStatus.Malformed, key, SoundPayloadKind.Wav, "smpl loops run past the chunk");
            for (var i = 0; i < count; i++)
            {
                var loop = bytes.Slice(loopAt + 36 + i * 24, 24);
                var (type, start, end, playCount) = (BinaryPrimitives.ReadUInt32LittleEndian(loop[4..]), BinaryPrimitives.ReadUInt32LittleEndian(loop[8..]),
                    BinaryPrimitives.ReadUInt32LittleEndian(loop[12..]), BinaryPrimitives.ReadUInt32LittleEndian(loop[20..]));
                if (start > end || end >= (uint)frames || type > int.MaxValue || playCount > int.MaxValue)
                    return Refuse(SoundPayloadStatus.Malformed, key, SoundPayloadKind.Wav, "loop outside the samples");
                loops.Add(new((int)type, (int)start, (int)end, (int)playCount));
            }
        }
        return new(SoundPayloadStatus.Admitted, key, SoundPayloadKind.Wav,
            new(channels, (int)rate, bits, dataAt.Value, dataSize.Value, frames, loops.AsReadOnly(), ignored), null, payload);
    }

    private readonly record struct Frame(int Offset, int Version, int Layer, int Bitrate, int SampleRate, bool Stereo, int Length);

    /// <summary>The first of two consecutive valid frames after an optional ID3v2 tag, or null; <paramref name="failure"/>
    /// is set when the bytes look like MPEG audio (a tag or a frame sync) but do not hold two valid frames.</summary>
    private static Frame? Mp3Start(ReadOnlySpan<byte> bytes, SoundPayloadOptions options, out string? failure)
    {
        failure = null; var tagEnd = 0;
        var tagged = bytes.Length >= 10 && bytes[..3].SequenceEqual("ID3"u8);
        if (tagged)
        {
            if (bytes[3] is not (2 or 3 or 4) || ((bytes[6] | bytes[7] | bytes[8] | bytes[9]) & 0x80) != 0) { failure = "unsupported or damaged ID3v2 tag"; return null; }
            var size = (bytes[6] << 21) | (bytes[7] << 14) | (bytes[8] << 7) | bytes[9];
            tagEnd = 10 + size + ((bytes[5] & 0x10) != 0 ? 10 : 0);
            if (tagEnd > bytes.Length) { failure = "ID3v2 tag runs past the end"; return null; }
        }
        var limit = Math.Min(bytes.Length - 4, tagEnd + options.MaxMp3SyncSearchBytes);
        for (var at = tagEnd; at <= limit; at++)
        {
            if (bytes[at] != 0xFF || (bytes[at + 1] & 0xE0) != 0xE0) continue;
            if (Header(bytes, at) is not { } first) continue;
            var next = at + first.Length;
            if (next + 4 <= bytes.Length && Header(bytes, next) is { } second && second.Version == first.Version && second.Layer == first.Layer && second.SampleRate == first.SampleRate)
                return first;
            if (next + 4 > bytes.Length) break;
        }
        // A tag, or a frame sync at the very start, says the file is meant as MPEG audio; anything else is not.
        if (tagged || (bytes.Length >= 2 && bytes[0] == 0xFF && (bytes[1] & 0xE0) == 0xE0)) failure = "no two consecutive valid MPEG frames";
        return null;
    }

    private static Frame? Header(ReadOnlySpan<byte> bytes, int at)
    {
        if (bytes[at] != 0xFF || (bytes[at + 1] & 0xE0) != 0xE0) return null;
        var versionBits = (bytes[at + 1] >> 3) & 3; var layerBits = (bytes[at + 1] >> 1) & 3;
        var bitrateIndex = bytes[at + 2] >> 4; var rateIndex = (bytes[at + 2] >> 2) & 3; var padding = (bytes[at + 2] >> 1) & 1;
        if (versionBits == 1 || layerBits == 0 || bitrateIndex is 0 or 15 || rateIndex == 3) return null;
        var version = versionBits switch { 3 => 1, 2 => 2, _ => 25 };
        var layer = 4 - layerBits;
        var bitrate = Bitrate(version, layer, bitrateIndex);
        var rate = (version switch { 1 => new[] { 44100, 48000, 32000 }, 2 => new[] { 22050, 24000, 16000 }, _ => new[] { 11025, 12000, 8000 } })[rateIndex];
        var length = layer == 1 ? (12 * bitrate * 1000 / rate + padding) * 4
            : layer == 3 && version != 1 ? 72 * bitrate * 1000 / rate + padding
            : 144 * bitrate * 1000 / rate + padding;
        return new(at, version, layer, bitrate, rate, ((bytes[at + 3] >> 6) & 3) != 3, length);
    }

    private static int Bitrate(int version, int layer, int index)
    {
        int[] table = (version, layer) switch
        {
            (1, 1) => [32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448],
            (1, 2) => [32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384],
            (1, 3) => [32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320],
            (_, 1) => [32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256],
            _ => [8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160],
        };
        return table[index - 1];
    }

    private static SoundPayload Mp3(string key, ReadOnlyMemory<byte> payload, Frame frame) =>
        frame.Layer != 3 ? Unsupported(key, payload, SoundPayloadKind.Mp3, $"MPEG layer {frame.Layer}")
            : new(SoundPayloadStatus.Admitted, key, SoundPayloadKind.Mp3, null, new(frame.Version, frame.SampleRate, frame.Stereo, frame.Offset, frame.Bitrate), payload);

    private static SoundPayload Unsupported(string key, ReadOnlyMemory<byte> payload, SoundPayloadKind kind, string detail) =>
        new(SoundPayloadStatus.Unsupported, key, kind, null, null, payload, detail);

    internal static SoundPayload Refuse(SoundPayloadStatus status, string key, SoundPayloadKind? kind, string detail) =>
        new(status, key, kind, null, null, ReadOnlyMemory<byte>.Empty, detail);

    private static string Ascii(ReadOnlySpan<byte> id) => string.Concat(id.ToArray().Select(b => b is >= 0x20 and < 0x7F ? (char)b : '?'));
}
