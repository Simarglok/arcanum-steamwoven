using Arcanum.Data.Content;
using Arcanum.Data.Messages;

namespace Arcanum.Data.Audio;

public enum TileSoundTableStatus
{
    Ready,
    MissingFile,
    Unreadable,
    /// <summary>Not a MES table in the original grammar (see <see cref="TileSoundTableResult.Mes"/>).</summary>
    Malformed,
    /// <summary>A category's first record (0, 100, 200 or 300) is missing, or a record is one the original refuses or reads
    /// out of bounds (<see cref="TileSoundTableResult.Record"/>).</summary>
    Invalid,
}

/// <param name="Record">The record number an <see cref="TileSoundTableStatus.Invalid"/> table stopped on (null for a missing
/// category start).</param>
public sealed record TileSoundTableResult(TileSoundTableStatus Status, TileSoundTable? Table, MesParseStatus? Mes = null, int? Record = null);

/// <summary>
/// ARC-WORLD-041: the footstep sound class of every ground tile, from <c>art/tile/tilename.mes</c> (docs/audio/sound-cues.md;
/// checked in the 1.0.7.4 executable, docs/research/audio-evidence.md SN34). The name init reads the file while the game
/// initialises, so it comes from the base sources (<see cref="ContentLoadPhase.GameStart"/>, ledger LP1). Records 0–99 are the
/// outdoor flippable tiles, 100–199 outdoor, 200–299 indoor flippable, 300–399 indoor; each category is its records in order
/// and a tile names its record by position (bits 22–27 of its art id). A record with a '/' has flag letters up to the first
/// space and the number after them (C <c>atoi</c>, 0 when none); a record without one is the number after its first three
/// characters, read before the name is cut there. The classes are 0 dirt, 1 sand, 2 snow, 3 stone, 4 water, 5 wood; the
/// table keeps any number, the footstep rule refuses the rest.
/// </summary>
public sealed class TileSoundTable
{
    public const string Path = "art/tile/tilename.mes";
    public const int Categories = 4;

    private readonly int[][] _classes;

    private TileSoundTable(int[][] classes) => _classes = classes;

    /// <summary>The sound numbers of a category (0 outdoor flippable, 1 outdoor, 2 indoor flippable, 3 indoor), by position.</summary>
    public IReadOnlyList<int> Category(int category) =>
        category is >= 0 and < Categories ? Array.AsReadOnly(_classes[category]) : throw new ArgumentOutOfRangeException(nameof(category));

    public static TileSoundTableResult Open(ContentStack stack, MesReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stack);
        if (ContentPhase.Candidate(stack, Path, ContentLoadPhase.GameStart) is not { } candidate) return new(TileSoundTableStatus.MissingFile, null);
        var read = stack.Read(candidate);
        if (read.Status != ContentReadStatus.Read) return new(TileSoundTableStatus.Unreadable, null);
        var status = MesTable.Read(read.Bytes, options ?? new(), out var records);
        return status == MesParseStatus.Parsed ? Parse(records) : new(TileSoundTableStatus.Malformed, null, status);
    }

    /// <summary>The table from the records of a parsed <c>tilename.mes</c>. Records from 400 on are not read.</summary>
    [OriginalFunction(0x4EB2E0, OriginalFunctionScope.Partial)]
    public static TileSoundTableResult Parse(IReadOnlyDictionary<int, string> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        // The original requires record 0; a category with records but without its start record (100, 200 or 300) would run
        // the position count on past the end of its table. Any missing start is refused (project policy).
        if (!records.ContainsKey(0) || !records.ContainsKey(100) || !records.ContainsKey(200) || !records.ContainsKey(300))
            return new(TileSoundTableStatus.Invalid, null);
        var categories = Enumerable.Range(0, Categories).Select(_ => new List<int>()).ToArray();
        foreach (var (number, text) in records.Where(r => r.Key is >= 0 and < 400).OrderBy(r => r.Key))
        {
            if (Sound(text) is not { } sound) return new(TileSoundTableStatus.Invalid, null, Record: number);
            categories[number / 100].Add(sound);
        }
        return new(TileSoundTableStatus.Ready, new TileSoundTable([.. categories.Select(c => c.ToArray())]));
    }

    /// <summary>
    /// The sound number of the ground under a location, from the art id its sector's tile slot holds (0x4EBBE0), or null when
    /// its position is beyond its category, where the original reads past its table. A tile (type 0) is outdoor by bit 8,
    /// flippable by bit 7 and has its position in bits 22–27; ARC-DATA-043: a facade (type 11), which a tile slot can hold, is
    /// outdoor by bit 25, flippable by bit 26 and has its position in bits 11–16 (0x5038C0, 0x503950, 0x503900, 0x5036B0); any
    /// other art type reads as indoor, not flippable, position 0.
    /// </summary>
    [OriginalFunction(0x4EBBE0, OriginalFunctionScope.Partial)]
    public int? SoundClass(uint tileArt)
    {
        var (outdoor, flippable, position) = (tileArt >> 28) switch
        {
            0 => ((tileArt & 0x100) != 0, (tileArt & 0x80) != 0, (int)((tileArt >> 22) & 0x3F)),
            11 => ((tileArt >> 25 & 1) != 0, (tileArt >> 26 & 1) != 0, (int)((tileArt >> 11) & 0x3F)),
            _ => (false, false, 0),
        };
        var category = _classes[outdoor ? (flippable ? 0 : 1) : (flippable ? 2 : 3)];
        return position < category.Length ? category[position] : null;
    }

    // A text with a NUL would end the original's string early; one without '/' shorter than three characters fails its load.
    private static int? Sound(string text)
    {
        if (text.Contains('\0')) return null;
        var slash = text.IndexOf('/');
        if (slash < 0) return text.Length < 3 ? null : SoundSchemeParser.Atoi(text.AsSpan(3));
        var end = slash + 1;
        while (end < text.Length && text[end] != ' ') end++;
        return SoundSchemeParser.Atoi(text.AsSpan(end));
    }
}
