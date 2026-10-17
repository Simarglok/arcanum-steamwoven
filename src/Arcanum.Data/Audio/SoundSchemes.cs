using System.Text;
using Arcanum.Data.Messages;

namespace Arcanum.Data.Audio;

/// <summary>What a scheme entry is, from its options (docs/research/audio-evidence.md SN13).</summary>
public enum SoundSchemeEntryKind
{
    /// <summary>No song option: a one-shot effect fired by chance while the hour is in its window.</summary>
    Ambient,
    /// <summary><c>/loop</c> (without <c>/over</c>): a music file looped while the hour is in its window.</summary>
    LoopSong,
    /// <summary><c>/anchor</c> alone: a music file played once when the scheme loads, never restored.</summary>
    AnchorSong,
    /// <summary><c>/over</c> (it wins over <c>/loop</c>): played once when the scheme loads; when it ends, the scheme pair it
    /// replaced is requested again.</summary>
    TransitionSong,
}

/// <summary>One parsed SchemeList record. Volume and balance are the original's raw 0–127 values (percent × 127 / 100,
/// truncated and clamped); hours are inclusive and are not wrapped past midnight here (a reversed window is kept as
/// written, <see cref="HourWindowReversed"/>; the player applies the rule setting).</summary>
/// <param name="ListNumber">The record's number in <c>SchemeList.mes</c>.</param>
/// <param name="Resource">The resource text before the first space, lower-cased as the original does.</param>
/// <param name="SfxId">For a <c>#N</c> resource, N: the file comes from the SFX tables.</param>
/// <param name="File">What the resource reaches: an SFX table's file or <c>sound/</c> + the name.</param>
/// <param name="SfxStatus">For a <c>#N</c> resource, how the SFX tables resolved it.</param>
/// <param name="Frequency">Ambient chance per tick in per mille (the original draws 0–999 and fires below it).</param>
public sealed record SoundSchemeEntry(int ListNumber, SoundSchemeEntryKind Kind, string Resource, int? SfxId, SoundFile File,
    SfxStatus? SfxStatus, int Frequency, int HourStart, int HourEnd, int VolumeMin, int VolumeMax, int BalanceLeft, int BalanceRight)
{
    public bool HourWindowReversed => HourStart > HourEnd;
    /// <summary>True when the entry has an hour window the original reads (ambient and loop songs).</summary>
    public bool HasHourWindow => Kind is SoundSchemeEntryKind.Ambient or SoundSchemeEntryKind.LoopSong;
}

public enum SoundSchemeStatus
{
    /// <summary>The index entry names a list base; <see cref="SoundScheme.Entries"/> holds what the original loads.</summary>
    Loaded,
    /// <summary>Id 0: the original loads nothing ("leave this slot empty").</summary>
    None,
    /// <summary>No SchemeIndex record has the id; the original loads nothing.</summary>
    NotInIndex,
    /// <summary>The index record has no <c>#</c> (such as "No Sound"); the original loads nothing: a silent slot.</summary>
    NoListBase,
    /// <summary>More than 25 records in the scheme's 100 list numbers: the original dereferences a null pointer on the
    /// 26th (an original bug); both rule settings refuse the scheme instead (owner rule: Original never reproduces a crash).</summary>
    TooManyEntries,
    /// <summary>The index or a list record is outside what the original reads safely: a list base that is not a positive
    /// whole number (a project refusal: the original would treat 0 as an empty slot and scan wrapping list numbers past
    /// int32), or a resource longer than the original's path buffer (an overflow there).</summary>
    Malformed,
}

/// <param name="Name">The index record's text up to <c>#</c>, trimmed (bytes taken as Latin-1, display only).</param>
/// <param name="ListBase">The first SchemeList number the original scans (it scans 100 numbers, gaps allowed).</param>
/// <param name="Detail">For <see cref="SoundSchemeStatus.Malformed"/>, which record and why.</param>
public sealed record SoundScheme(int Id, SoundSchemeStatus Status, string? Name, int? ListBase, IReadOnlyList<SoundSchemeEntry> Entries, string? Detail = null)
{
    public bool IsMusicName => Name?.StartsWith("M.", StringComparison.Ordinal) == true;
    public bool IsAmbientName => Name?.StartsWith("A.", StringComparison.Ordinal) == true;
}

/// <summary>Parses SchemeIndex/SchemeList records with the original's grammar (docs/research/audio-evidence.md SN12–SN13):
/// the whole record lower-cased, the resource up to the first space, options found anywhere in the rest by substring,
/// values read like C <c>atoi</c>, a second value after the first <c>,</c> or else the first <c>-</c>.</summary>
public static class SoundSchemeParser
{
    /// <summary>The original keeps at most this many records per scheme.</summary>
    public const int MaxEntries = 25;
    /// <summary>The original scans this many consecutive list numbers from the base.</summary>
    public const int ListSpan = 100;
    /// <summary>The original copies the resource into a 260-byte buffer and later builds <c>sound\</c> + it.</summary>
    public const int MaxResourceBytes = 259 - 6;
    internal const int RawMax = 127;

    /// <summary>Parses one SchemeList record. Returns false (with a reason) for a record the original cannot read safely.</summary>
    public static bool TryParseEntry(int listNumber, ReadOnlySpan<byte> record, Func<int, (SfxStatus Status, SoundFile? File)> sfx,
        Func<string, SoundFile> file, out SoundSchemeEntry? entry, out string? failure)
    {
        ArgumentNullException.ThrowIfNull(sfx); ArgumentNullException.ThrowIfNull(file);
        entry = null; failure = null;
        var text = Lower(record);
        var space = text.IndexOf(' ');
        var resource = space < 0 ? text : text[..space];
        var options = space < 0 ? null : text[(space + 1)..];
        if (Encoding.Latin1.GetByteCount(resource) > MaxResourceBytes) { failure = "resource longer than the original's path buffer"; return false; }

        var kind = SoundSchemeEntryKind.Ambient;
        int frequency = 5, start = 0, end = 23, volumeMin = 100, volumeMax = 100, left = 50, right = 50;
        if (options is not null)
        {
            if (!Param(options, "/vol:", ref volumeMin, ref volumeMax, true, out failure)) return false;
            var over = options.Contains("/over", StringComparison.Ordinal);
            var loop = options.Contains("/loop", StringComparison.Ordinal);
            if (over || loop || options.Contains("/anchor", StringComparison.Ordinal))
            {
                if (over) kind = SoundSchemeEntryKind.TransitionSong;
                else if (loop) { kind = SoundSchemeEntryKind.LoopSong; if (!Param(options, "/time:", ref start, ref end, true, out failure)) return false; }
                else kind = SoundSchemeEntryKind.AnchorSong;
            }
            else
            {
                var unused = 0;
                if (!Param(options, "/freq:", ref frequency, ref unused, false, out failure)
                    || !Param(options, "/time:", ref start, ref end, true, out failure)
                    || !Param(options, "/bal:", ref left, ref right, true, out failure)) return false;
                int scatter = -1, scatterSecond = -1;
                if (!Param(options, "/scatter:", ref scatter, ref scatterSecond, true, out failure)) return false;
                if (scatter >= 0)
                {
                    volumeMax = 100; volumeMin = unchecked(100 - scatter);
                    left = unchecked(50 - scatter / 2); right = unchecked(50 + scatter / 2);
                }
            }
        }
        if (volumeMax < volumeMin) volumeMax = volumeMin;
        if (right < left) right = left;

        int? sfxId = null; SfxStatus? sfxStatus = null; SoundFile resolved;
        if (resource.StartsWith('#'))
        {
            var id = Atoi(resource.AsSpan(1));
            sfxId = id;
            var (status, found) = sfx(id);
            sfxStatus = status;
            // An unknown id or an empty record gives the original an empty or bare folder path: nothing to open.
            resolved = found ?? new SoundFile(SoundFileStatus.InvalidPath, "sound/", null);
        }
        else resolved = file(resource);
        entry = new(listNumber, kind, resource, sfxId, resolved, sfxStatus, frequency, start, end,
            Raw(volumeMin), Raw(volumeMax), Raw(left), Raw(right));
        return true;
    }

    /// <summary>The original's percent → raw conversion: <c>127 × v</c> in 32 bits (it wraps for |v| above 16,909,320),
    /// divided by 100 truncating toward zero, then clamped to 0–127 (a balance-left above 127 clamps to 127, not 0 as in CE).</summary>
    internal static int Raw(int percent) => Math.Clamp(unchecked(RawMax * percent) / 100, 0, RawMax);

    private static bool Param(string options, string key, ref int first, ref int second, bool hasSecond, out string? failure)
    {
        failure = null;
        var at = options.IndexOf(key, StringComparison.Ordinal);
        if (at < 0) return true;
        var token = options[(at + key.Length)..];
        var space = token.IndexOf(' ');
        if (space >= 0) token = token[..space];
        var value = Atoi(token);
        first = value;
        if (!hasSecond) return true;
        var separator = token.IndexOf(',');
        if (separator < 0) separator = token.IndexOf('-');
        if (separator < 0) { second = value; return true; }
        second = Atoi(token.AsSpan(separator + 1));
        return true;
    }

    /// <summary>The executable's C <c>atoi</c>: leading ASCII white space, an optional sign, digits up to the first other
    /// character; 0 when there are none. The value is built in 32 bits without an overflow check, so it wraps modulo 2³².</summary>
    internal static int Atoi(ReadOnlySpan<char> text)
    {
        var i = 0;
        while (i < text.Length && text[i] is ' ' or '\t' or '\n' or '\v' or '\f' or '\r') i++;
        var negative = false;
        if (i < text.Length && text[i] is '+' or '-') negative = text[i++] == '-';
        var value = 0;
        while (i < text.Length && text[i] is >= '0' and <= '9') value = unchecked(value * 10 + (text[i++] - '0'));
        return negative ? unchecked(-value) : value;
    }

    /// <summary>The original lower-cases the record with C <c>tolower</c>: only ASCII letters change; other bytes are kept
    /// (as Latin-1 characters, so a non-ASCII name later fails as an invalid path instead of being guessed).</summary>
    internal static string Lower(ReadOnlySpan<byte> bytes)
    {
        var chars = new char[bytes.Length];
        for (var i = 0; i < bytes.Length; i++) chars[i] = bytes[i] is >= (byte)'A' and <= (byte)'Z' ? (char)(bytes[i] + 32) : (char)bytes[i];
        return new string(chars);
    }

    /// <summary>Reads one scheme as the original's load does.</summary>
    internal static SoundScheme Load(int id, MesFile index, MesFile list, Func<int, (SfxStatus, SoundFile?)> sfx, Func<string, SoundFile> file)
    {
        if (id == 0) return new(id, SoundSchemeStatus.None, null, null, Array.Empty<SoundSchemeEntry>());
        if (index.Find(id) is not { } record) return new(id, SoundSchemeStatus.NotInIndex, null, null, Array.Empty<SoundSchemeEntry>());
        var text = Encoding.Latin1.GetString(record.Bytes.Span);
        var hash = text.IndexOf('#');
        var name = (hash < 0 ? text : text[..hash]).Trim();
        if (hash < 0) return new(id, SoundSchemeStatus.NoListBase, name, null, Array.Empty<SoundSchemeEntry>());
        var listBase = Atoi(text.AsSpan(hash + 1));
        if (listBase <= 0 || listBase > int.MaxValue - ListSpan)
            return new(id, SoundSchemeStatus.Malformed, name, null, Array.Empty<SoundSchemeEntry>(), "list base is not a positive whole number");
        var entries = new List<SoundSchemeEntry>();
        for (var number = listBase; number < listBase + ListSpan; number++)
        {
            if (list.Find(number) is not { } line) continue;
            if (entries.Count == MaxEntries)
                return new(id, SoundSchemeStatus.TooManyEntries, name, listBase, Array.Empty<SoundSchemeEntry>(), $"record {number} is the 26th");
            if (!TryParseEntry(number, line.Bytes.Span, n => sfx(n), file, out var entry, out var failure))
                return new(id, SoundSchemeStatus.Malformed, name, listBase, Array.Empty<SoundSchemeEntry>(), $"record {number}: {failure}");
            entries.Add(entry!);
        }
        return new(id, SoundSchemeStatus.Loaded, name, listBase, entries.AsReadOnly());
    }
}
