using System.Globalization;
using System.Text;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.Data.Messages;

namespace Arcanum.Data.Audio;

public sealed class SoundCatalogOptions
{
    public MesReaderOptions Mes { get; init; } = new();
    /// <summary>Upper bound on the tables <c>snd_00index.mes</c> may name; the installed index names six.</summary>
    public int MaxTables { get; init; } = 256;

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Mes);
        if (MaxTables <= 0) throw new ArgumentOutOfRangeException(nameof(SoundCatalogOptions), "MaxTables must be positive.");
    }
}

/// <summary>
/// ARC-DATA-040: the original's sound catalogue over a <see cref="ContentStack"/>, for any module
/// (docs/audio/sound-catalog.md; behaviour checked in the 1.0.7.4 executable, docs/research/audio-evidence.md SN1–SN3,
/// SN12–SN13, SN21–SN22). The sound init runs while the game initialises, before the module is mounted, so
/// <c>snd_00index.mes</c>, the tables it names, <c>SchemeIndex.mes</c>, <c>SchemeList.mes</c> and <c>soundparams.mes</c>
/// come from the base sources (<see cref="ContentLoadPhase.GameStart"/>); the module's <c>snd_user.mes</c> is read when the
/// module loads, and sound files are opened while the game runs (both the stack winner). Nothing here plays sound or
/// decodes audio. The stack stays owned by the caller and must outlive the catalogue.
/// </summary>
public sealed class SoundCatalog
{
    public const string IndexPath = "sound/snd_00index.mes";
    public const string UserTablePath = "sound/snd_user.mes";
    public const string SchemeIndexPath = "sound/schemeindex.mes";
    public const string SchemeListPath = "sound/schemelist.mes";
    public const string PositionalPath = "sound/soundparams.mes";
    /// <summary>The longest path the original builds into its 260-byte buffer.</summary>
    public const int MaxPathBytes = 259;
    /// <summary>Combat stingers are <c>combat 1.mp3</c> … <c>combat 6.mp3</c>, one drawn when combat music starts.</summary>
    public const int CombatStingerCount = 6;

    private readonly ContentStack _stack;
    private readonly MesFile _schemeIndex;
    private readonly MesFile _schemeList;

    private SoundCatalog(ContentStack stack, IReadOnlyList<SoundTable> tables, SoundTable? user, MesFile schemeIndex, MesFile schemeList,
        SoundPositionalProfile positional, IReadOnlyList<SoundIssue> issues)
    {
        _stack = stack; Tables = tables; UserTable = user; _schemeIndex = schemeIndex; _schemeList = schemeList;
        Positional = positional; Issues = issues;
        SchemeIds = Array.AsReadOnly(schemeIndex.Messages.Select(m => m.Number).ToArray());
    }

    /// <summary>The index tables in search order (ascending index number).</summary>
    public IReadOnlyList<SoundTable> Tables { get; }
    /// <summary>The module's user table, searched after every index table missed; null when it did not load.</summary>
    public SoundTable? UserTable { get; }
    public SoundPositionalProfile Positional { get; }
    /// <summary>Every SchemeIndex number, ascending (0 included when the file has it).</summary>
    public IReadOnlyList<int> SchemeIds { get; }
    public IReadOnlyList<SoundIssue> Issues { get; }

    public static SoundCatalogResult Open(ContentStack stack, SoundCatalogOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stack);
        options ??= new();
        options.Validate();
        var issues = new List<SoundIssue>();

        var index = Load(stack, IndexPath, ContentLoadPhase.GameStart, options, out var failure);
        if (index is null) return failure!;
        var entries = index.Value.File.Messages;
        if (entries.Count == 0 || entries.All(e => e.Number == 0)) return new(SoundCatalogStatus.InvalidIndex, null, IndexPath);
        if (entries.Count > options.MaxTables) return new(SoundCatalogStatus.InvalidIndex, null, IndexPath);
        var tables = new List<SoundTable>();
        foreach (var entry in entries)
        {
            if (entry.Number == 0) { issues.Add(new(SoundIssueCode.IndexEntryZeroSkipped, IndexPath, 0)); continue; }
            var name = Encoding.Latin1.GetString(entry.Bytes.Span);
            if (name.Length == 0 || ContentStack.NormalizeQuery("sound/" + name) is not { } key) return new(SoundCatalogStatus.InvalidIndex, null, IndexPath);
            var table = Load(stack, key, ContentLoadPhase.GameStart, options, out failure);
            if (table is null) return failure!;
            tables.Add(new(name, key, table.Value.Candidate, table.Value.File, false));
        }

        SoundTable? user = null;
        var userFile = Load(stack, UserTablePath, ContentLoadPhase.ModuleLoaded, options, out _);
        if (userFile is null) issues.Add(new(SoundIssueCode.UserTableMissing, UserTablePath));
        else user = new("snd_user.mes", UserTablePath, userFile.Value.Candidate, userFile.Value.File, true);

        var schemeIndex = Load(stack, SchemeIndexPath, ContentLoadPhase.GameStart, options, out failure);
        if (schemeIndex is null) return failure!;
        var schemeList = Load(stack, SchemeListPath, ContentLoadPhase.GameStart, options, out failure);
        if (schemeList is null) return failure!;

        var positional = SoundPositionalProfile.WithoutFile;
        var parameters = Load(stack, PositionalPath, ContentLoadPhase.GameStart, options, out _);
        if (parameters is null) issues.Add(new(SoundIssueCode.PositionalParametersMissing, PositionalPath));
        else positional = SoundPositionalProfile.FromMes(parameters.Value.File, PositionalPath, issues);

        return new(SoundCatalogStatus.Ready, new SoundCatalog(stack, tables.AsReadOnly(), user, schemeIndex.Value.File, schemeList.Value.File,
            positional, issues.AsReadOnly()));
    }

    /// <summary>The original's SFX lookup (SN1): the first index table holding the id wins, even with an empty record;
    /// only when none does, the user table is searched.</summary>
    public SfxResolution ResolveSfx(int id)
    {
        foreach (var table in Tables.Append(UserTable))
        {
            if (table?.Messages.Find(id) is not { } record) continue;
            if (record.Bytes.Length == 0) return new(id, SfxStatus.EmptyRecord, table, null, null);
            var name = Encoding.Latin1.GetString(record.Bytes.Span);
            return new(id, SfxStatus.Named, table, name, ResolveSoundFile(name));
        }
        return new(id, SfxStatus.UnknownId, null, null, null);
    }

    /// <summary>A file named relative to <c>sound/</c> (a table record, a scheme resource, a music or speech path), as
    /// the original opens it while the game runs: the stack winner.</summary>
    public SoundFile ResolveSoundFile(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return ResolveFile("sound/" + name);
    }

    /// <summary>A virtual path such as <c>sound/music/combatmusic.mp3</c>; separators either way, case ignored.</summary>
    public SoundFile ResolveFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (Encoding.Latin1.GetByteCount(path) > MaxPathBytes || ContentStack.NormalizeQuery(path) is not { } key)
            return new(SoundFileStatus.InvalidPath, path, null);
        var winner = ContentPhase.Candidate(_stack, key, ContentLoadPhase.ModuleLoaded);
        return new(winner is null ? SoundFileStatus.Missing : SoundFileStatus.Found, key, winner);
    }

    /// <summary>One scheme as the original's load reads it (SN12–SN13).</summary>
    public SoundScheme Scheme(int id) => SoundSchemeParser.Load(id, _schemeIndex, _schemeList, SchemeSfx, ResolveSoundFile);

    private (SfxStatus, SoundFile?) SchemeSfx(int id)
    {
        var resolution = ResolveSfx(id);
        return (resolution.Status, resolution.File);
    }

    /// <summary>The dialogue voice of a line (SN22): <c>sound/speech/NNNNN/vL_g.mp3</c> with the PC's gender; a female PC
    /// falls back to the male file, a male PC never to the female one.</summary>
    public SpeechResolution ResolveSpeech(int identity, SpeechGender pcGender)
    {
        if (identity == -1) return new(identity, SpeechStatus.NoVoice, 0, 0, Array.Empty<SoundFile>(), null);
        var (folder, line) = ((identity >> 16) & 0x7FFF, identity & 0xFFFF);
        var candidates = new List<SoundFile> { ResolveSoundFile(SpeechName(folder, line, pcGender)) };
        if (pcGender == SpeechGender.Female && candidates[0].Status != SoundFileStatus.Found)
            candidates.Add(ResolveSoundFile(SpeechName(folder, line, SpeechGender.Male)));
        var found = candidates.FirstOrDefault(c => c.Status == SoundFileStatus.Found);
        return new(identity, found is null ? SpeechStatus.Missing : SpeechStatus.Found, folder, line, candidates.AsReadOnly(), found);
    }

    private static string SpeechName(int folder, int line, SpeechGender gender) =>
        string.Create(CultureInfo.InvariantCulture, $"speech/{folder:D5}/v{line}_{(gender == SpeechGender.Male ? 'm' : 'f')}.mp3");

    /// <summary>The voice identity of an NPC line (SN21): <c>(script number &amp; 0x7FFF) &lt;&lt; 16 | (number in the line's
    /// condition field &amp; 0xFFFF)</c>, read with C <c>atoi</c>; −1 when the script number is 0 or the line has no such
    /// field (<paramref name="conditionField"/> null).</summary>
    public static int SpeechIdentity(int scriptNumber, string? conditionField)
    {
        if (scriptNumber == 0 || conditionField is null) return -1;
        var number = SoundSchemeParser.Atoi(conditionField, out var overflow);
        if (overflow) number = 0;
        return ((scriptNumber & 0x7FFF) << 16) | (number & 0xFFFF);
    }

    /// <summary>The sound record of a sector read by <see cref="WorldCatalog.ReadSector"/>, or null for a sector version
    /// without one (before 0xAA0003). Words 3–5 (0-based) of the metadata-and-sound section are flags, music and ambient.</summary>
    public static SectorSoundRecord? SectorSound(WorldSector sector)
    {
        ArgumentNullException.ThrowIfNull(sector);
        return sector.MetadataAndSound is { Count: >= 6 } words ? new(unchecked((uint)words[3]), words[4], words[5]) : null;
    }

    /// <summary>The combat stinger <paramref name="number"/> (1–6) under <c>sound/</c>.</summary>
    public static string CombatStingerName(int number) =>
        number is >= 1 and <= CombatStingerCount ? string.Create(CultureInfo.InvariantCulture, $"music/combat {number}.mp3")
            : throw new ArgumentOutOfRangeException(nameof(number));

    /// <summary>The combat loop started together with the stinger.</summary>
    public const string CombatLoopName = "music/combatmusic.mp3";
    /// <summary>Looped while the party travels on the world map.</summary>
    public const string WorldMapTravelMusicName = "music/arcanum.mp3";
    /// <summary>Looped while story slides show.</summary>
    public const string SlideMusicName = "music/dwarvenmusic.mp3";

    private static (ContentCandidate Candidate, MesFile File)? Load(ContentStack stack, string path, ContentLoadPhase phase, SoundCatalogOptions options,
        out SoundCatalogResult? failure)
    {
        failure = null;
        if (ContentPhase.Candidate(stack, path, phase) is not { } candidate) { failure = new(SoundCatalogStatus.MissingFile, null, path); return null; }
        var read = stack.Read(candidate);
        if (read.Status != ContentReadStatus.Read) { failure = new(SoundCatalogStatus.Unreadable, null, path); return null; }
        var parsed = MesFile.Parse(read.Bytes, options.Mes);
        if (parsed.Status != MesParseStatus.Parsed) { failure = new(SoundCatalogStatus.Malformed, null, path, parsed.Status); return null; }
        return (candidate, parsed.File!);
    }
}
