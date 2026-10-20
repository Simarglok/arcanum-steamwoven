using Arcanum.Data.Content;
using Arcanum.Data.Messages;

namespace Arcanum.Data.Technology;

public enum TechnologyDataStatus
{
    Read,
    /// <summary>A file the original's init loads is not in the base sources (its init fails).</summary>
    FileMissing,
    FileUnreadable,
    /// <summary>The file has no message (the original's loader fails on an empty file).</summary>
    FileEmpty,
    /// <summary>The file is not one the MES grammar admits (project policy); a text of 2,000 bytes or more is refused here,
    /// before the schematic parse (TC6).</summary>
    FileRefused,
    /// <summary>A schematic list line with more than three tokens: the original writes past the field and the record (TC6, an
    /// out-of-bounds bug, refused in both rule modes).</summary>
    TooManyTokens,
    /// <summary>A schematic list line of 2,000 bytes or more: the original's copy overflows its buffer (TC6). The MES grammar
    /// already refuses such a text; this status is for a caller that hands the parse a line directly.</summary>
    LineTooLong,
    /// <summary>The number of learned record starts (2000, 2010, ... below 3999 with an entry) differs from the learned count
    /// (entries in 2000..3999 / 7): the original fills its list of "count" slots without a bound (more starts overflow the heap)
    /// and walks it to the count (fewer leave slots unwritten). TC11, refused in both rule modes.</summary>
    LearnedListMismatch,
    /// <summary>A learned record start at 3600 or above (discipline 8 or 9): the original's init counts it in a per-discipline
    /// table of eight, past its end. TC11, refused in both rule modes.</summary>
    LearnedDisciplineOutOfRange,
}

/// <summary>Why a technology data file was refused: the file, the message number and the token involved.</summary>
public sealed record TechnologyDataRefusal(TechnologyDataStatus Status, string File, int? Entry = null, string? Token = null);

/// <summary>The loader the technology readers share: a file of the original's init phase through the content stack.</summary>
internal static class TechnologyFiles
{
    /// <summary>The file the original's init reads: the first base source holding <paramref name="path"/> (ARC-DATA-027 load
    /// phases), parsed by the MES grammar; null with <paramref name="file"/> set, or the refusal.</summary>
    internal static TechnologyDataRefusal? Load(ContentStack stack, string path, MesReaderOptions options, out MesFile? file)
    {
        file = null;
        if (ContentPhase.Candidate(stack, path, ContentLoadPhase.GameStart) is not { } source) return new(TechnologyDataStatus.FileMissing, path);
        if (source.Length > options.MaxBytes) return new(TechnologyDataStatus.FileRefused, path, Token: nameof(MesParseStatus.TooLarge));
        var read = stack.Read(source);
        if (read.Status != ContentReadStatus.Read) return new(TechnologyDataStatus.FileUnreadable, path, Token: read.Status.ToString());
        var parsed = MesFile.Parse(read.Bytes!, options);
        if (parsed.Status != MesParseStatus.Parsed) return new(TechnologyDataStatus.FileRefused, path, parsed.Number, $"{parsed.Status} line {parsed.Line}");
        if (parsed.File!.Count == 0) return new(TechnologyDataStatus.FileEmpty, path);
        file = parsed.File;
        return null;
    }
}
