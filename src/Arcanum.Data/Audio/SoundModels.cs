using Arcanum.Data.Content;
using Arcanum.Data.Messages;

namespace Arcanum.Data.Audio;

/// <summary>Why <see cref="SoundCatalog.Open"/> did or did not produce a catalogue.</summary>
public enum SoundCatalogStatus
{
    Ready,
    /// <summary>A file the original's sound init needs (<c>snd_00index.mes</c>, a table it names, <c>SchemeIndex.mes</c>,
    /// <c>SchemeList.mes</c>) is not in any base source. The original refuses to start without the index; for the others
    /// its behaviour with an unloaded table is not established, so the catalogue refuses too.</summary>
    MissingFile,
    /// <summary>One of those files could not be read (archive or file error, or over a limit).</summary>
    Unreadable,
    /// <summary>One of those files is not a MES file in the original grammar (<see cref="SoundCatalogResult.Mes"/>).</summary>
    Malformed,
    /// <summary>The index names an unusable table: an empty or non-ASCII name, or more tables than allowed.</summary>
    InvalidIndex,
}

/// <param name="File">The virtual path of the file that stopped the catalogue, when one did.</param>
/// <param name="Mes">The MES refusal, for <see cref="SoundCatalogStatus.Malformed"/>.</param>
public sealed record SoundCatalogResult(SoundCatalogStatus Status, SoundCatalog? Catalog, string? File = null, MesParseStatus? Mes = null);

public enum SoundIssueCode
{
    /// <summary><c>soundparams.mes</c> is missing or unreadable: the original keeps its built-in large-size values and
    /// leaves small, medium and extra-large at zero (silent).</summary>
    PositionalParametersMissing,
    /// <summary>A <c>soundparams.mes</c> value is not a whole number the original's <c>atoi</c> reads the same way.</summary>
    PositionalParameterMalformed,
    /// <summary>The module's <c>snd_user.mes</c> is missing or unusable; the original then searches no user table.</summary>
    UserTableMissing,
    /// <summary>An index entry numbered 0, which the original's walk over the index skips.</summary>
    IndexEntryZeroSkipped,
}

/// <param name="File">The virtual path the issue is about.</param>
/// <param name="Number">The message number involved, when there is one.</param>
public sealed record SoundIssue(SoundIssueCode Code, string File, int? Number = null);

/// <summary>One SFX table: a MES file whose records map sound ids to file names under <c>sound/</c>.</summary>
/// <param name="Name">The file name as the index (or, for the user table, the original) names it.</param>
/// <param name="Key">The virtual path read.</param>
/// <param name="Candidate">The copy read: the first base source for the index tables (read by the sound init before the
/// module is mounted), the stack winner for the module's user table (read when the module loads).</param>
/// <param name="IsUserTable">The module's <c>snd_user.mes</c>, searched only after every index table missed.</param>
public sealed record SoundTable(string Name, string Key, ContentCandidate Candidate, MesFile Messages, bool IsUserTable);

public enum SoundFileStatus
{
    /// <summary>A source of the stack holds the file.</summary>
    Found,
    /// <summary>No source holds it; the original's play request then plays nothing.</summary>
    Missing,
    /// <summary>The name is not a supported virtual path (non-ASCII, rooted, <c>..</c>, too long for the original's
    /// 260-byte path buffer, ...).</summary>
    InvalidPath,
}

/// <summary>A sound file reached by name. Sound files are opened while the game runs, so the stack winner is read.</summary>
/// <param name="Key">The normalized virtual path (lower case, <c>/</c>), or the raw name for <see cref="SoundFileStatus.InvalidPath"/>.</param>
public sealed record SoundFile(SoundFileStatus Status, string Key, ContentCandidate? Candidate);

public enum SfxStatus
{
    /// <summary>A table holds the id with a file name; see <see cref="SfxResolution.File"/> for whether the file exists.</summary>
    Named,
    /// <summary>No table holds the id; nothing plays.</summary>
    UnknownId,
    /// <summary>The first table holding the id has an empty record; the original builds the bare folder name, which opens
    /// nothing.</summary>
    EmptyRecord,
}

/// <param name="Table">The table that holds the id (the first in search order), or null for <see cref="SfxStatus.UnknownId"/>.</param>
/// <param name="Name">The record's text as stored, for <see cref="SfxStatus.Named"/>.</param>
/// <param name="File">The file the name reaches, for <see cref="SfxStatus.Named"/>.</param>
public sealed record SfxResolution(int Id, SfxStatus Status, SoundTable? Table, string? Name, SoundFile? File)
{
    /// <summary>True when the id names a file that a source holds: the only case in which the original plays something.</summary>
    public bool Playable => Status == SfxStatus.Named && File!.Status == SoundFileStatus.Found;
}

/// <summary>The PC's gender as the dialogue voice reads it: a non-zero gender stat is male.</summary>
public enum SpeechGender { Female, Male }

public enum SpeechStatus
{
    /// <summary>A candidate file exists; <see cref="SpeechResolution.File"/> is the one played.</summary>
    Found,
    /// <summary>The line has no voice (identity −1); the original also leaves any previous voice playing.</summary>
    NoVoice,
    /// <summary>No candidate file exists; nothing plays and the text is unaffected.</summary>
    Missing,
}

/// <param name="Folder">Bits 16–30 of the identity: the dialogue script's number.</param>
/// <param name="Line">Bits 0–15: the line's voice number.</param>
/// <param name="Candidates">The files tried in order: the PC's gender, then (for a female PC only) the male file.</param>
public sealed record SpeechResolution(int Identity, SpeechStatus Status, int Folder, int Line, IReadOnlyList<SoundFile> Candidates, SoundFile? File);

/// <summary>The sound record of a sector file (version 0xAA0003 and later): raw flags, music and ambient scheme ids.</summary>
public sealed record SectorSoundRecord(uint Flags, int MusicScheme, int AmbientScheme)
{
    /// <summary>True when the original's sector change applies the pair: at least one id is non-zero.</summary>
    public bool AppliesOnEntry => MusicScheme != 0 || AmbientScheme != 0;
}
