using Arcanum.Data.Content;
using Arcanum.Data.Messages;

namespace Arcanum.Data.Characters;

public enum PortraitCatalogueStatus { Ready, FileMissing, ReadFailed, ParseFailed }

/// <summary>The portrait catalogue, or why the game file could not be loaded (<see cref="File"/> names it). A user file that
/// is present but could not be read or parsed leaves the catalogue without one, as in the original, and is reported in
/// <see cref="UserRefusal"/>.</summary>
public sealed record PortraitCatalogueLoad(PortraitCatalogueStatus Status, PortraitCatalogue? Catalogue, string? File = null,
    ContentReadStatus? Read = null, MesParseResult? Parse = null, PortraitCatalogueLoad? UserRefusal = null);

/// <summary>
/// The character a portrait is filtered for: its race (0..10, the eleven entries of the original's code table), gender (0
/// female, 1 male), whether it is an NPC (object type 16, which may also take <c>NPC</c> portraits) and whether it is a PC
/// marked as a multiplayer character (PC flag 0x4 of field 0xFD), for which every portrait search fails.
/// </summary>
public sealed record PortraitSubject
{
    public PortraitSubject(int race, int gender, bool npc = false, bool multiplayerPc = false)
    {
        if (race is < 0 or >= PortraitCatalogue.RaceCount) throw new ArgumentOutOfRangeException(nameof(race), "the code table holds races 0..10");
        if (gender is not (PortraitCatalogue.FemaleGender or PortraitCatalogue.MaleGender)) throw new ArgumentOutOfRangeException(nameof(gender));
        if (npc && multiplayerPc) throw new ArgumentException("the multiplayer flag is a PC flag", nameof(multiplayerPc));
        (Race, Gender, Npc, MultiplayerPc) = (race, gender, npc, multiplayerPc);
    }

    public int Race { get; }
    public int Gender { get; }
    public bool Npc { get; }
    public bool MultiplayerPc { get; }
}

public enum PortraitStemStatus
{
    Found,
    /// <summary>No entry: the file the id belongs to has no message with that number.</summary>
    Missing,
    /// <summary>An id 1..999 while <c>portrait\userport.mes</c> is not loaded (the original's path is then empty).</summary>
    NoUserFile,
    /// <summary>A negative id. The original passes its output buffer as the message-file handle (a technical bug); the
    /// reader refuses it in both rule modes.</summary>
    NegativeId,
}

/// <summary>A portrait's text, the file stem of its images: the stored MES bytes of <see cref="Number"/> (0 reads 1000) in
/// <see cref="User"/> <c>userport.mes</c> or else <c>gameport.mes</c>.</summary>
public sealed record PortraitStem(PortraitStemStatus Status, int Number, bool User, ReadOnlyMemory<byte> Text);

public enum PortraitImageStatus
{
    Found,
    /// <summary>The stem is missing (<see cref="PortraitImage.Stem"/> says why); the original builds an empty path and draws
    /// nothing.</summary>
    Missing,
    /// <summary>A stem that is empty, holds a byte outside printable ASCII or a path separator (<c>/</c>, <c>\</c>). The
    /// original formats any text into the path; such a stem is refused instead.</summary>
    InvalidStem,
}

/// <summary>The image paths of a portrait: <see cref="Path"/> first and, when it fails to load, <see cref="Fallback"/> (the
/// path with its <c>_b.bmp</c> ending, compared case-insensitively, replaced by <c>.bmp</c>; null when there is none).</summary>
public sealed record PortraitImage(PortraitImageStatus Status, PortraitStem Stem, string? Path, string? Fallback);

/// <summary>Which creation-screen portrait button was pressed.</summary>
public enum PortraitButton { Previous, Next }

/// <summary>
/// ARC-DATA-045 (N10a, FR-CHAR-004): the portraits the original's Portrait module loads at its init (0x4CE350, gamelib table
/// "Portrait"): <c>portrait\gameport.mes</c> (required) and <c>portrait\userport.mes</c> (optional), both from the base
/// sources (the game-start phase). Read in the executable (ledger PO1–PO10): ids from 1000 are the game file's, ids 1..999
/// the user file's, 0 reads 1000; a portrait's text is the stem of <c>portrait\&lt;stem&gt;.bmp</c> (small) and
/// <c>portrait\&lt;stem&gt;_b.bmp</c> (size 128 and up, falling back to the small one); a subject may take a portrait whose
/// text starts, case-insensitively, with its race code and <c>F</c>/<c>M</c>, with <c>ANY</c>, or, for an NPC, with
/// <c>NPC</c>; the searches walk the game ids from 1000 and the user ids from 1 while they are present and do not wrap,
/// the creation buttons do. Read-only: storing the chosen id (field 0xE7), loading and drawing the art and the
/// multiplayer PC's own portrait path (0x4A4C40) are the caller's.
/// </summary>
public sealed class PortraitCatalogue
{
    public const string GamePath = "portrait/gameport.mes", UserPath = "portrait/userport.mes";
    public const int FirstGameId = 1000, LargeSize = 128, RaceCount = 11, CodeLength = 3;
    public const int FemaleGender = 0, MaleGender = 1;

    // The race codes of the compatibility check (table 0x5B7C0C: races 8, 9 and 10 reuse EL, HG and HO) and the gender
    // letters ("FM", 0x5B7C38).
    private static readonly string[] RaceCodes = ["HU", "DW", "EL", "HE", "GN", "HA", "HO", "HG", "EL", "HG", "HO"];
    private const string GenderLetters = "FM";
    private static readonly byte[][] Codes = RaceCodes
        .SelectMany(race => GenderLetters.Select(gender => System.Text.Encoding.ASCII.GetBytes(race + gender))).ToArray();
    private static readonly byte[] Any = "ANY"u8.ToArray(), Npc = "NPC"u8.ToArray();

    private readonly MesFile _game;
    private readonly MesFile? _user;

    private PortraitCatalogue(MesFile game, MesFile? user) => (_game, _user) = (game, user);

    /// <summary>The catalogue over already-parsed files (synthetic data or another loader); <paramref name="user"/> null
    /// means no user file.</summary>
    public static PortraitCatalogue Create(MesFile game, MesFile? user = null) => new(game ?? throw new ArgumentNullException(nameof(game)), user);

    /// <summary>Reads both files from <paramref name="stack"/>'s base sources, as the module's init does at game start. The
    /// game file is required; a user file that is missing, unreadable or refused by the MES reader is no user file (the
    /// original only records whether it loaded), the latter two reported in <see cref="PortraitCatalogueLoad.UserRefusal"/>.</summary>
    [OriginalFunction(0x4CE350, OriginalFunctionScope.Full)]
    public static PortraitCatalogueLoad Load(ContentStack stack, MesReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stack);
        options ??= new();
        options.Validate();
        var game = Read(stack, GamePath, options, out var refusal);
        if (game is null) return refusal!;
        var user = Read(stack, UserPath, options, out refusal);
        return new(PortraitCatalogueStatus.Ready, new(game, user),
            UserRefusal: user is null && refusal!.Status != PortraitCatalogueStatus.FileMissing ? refusal : null);
    }

    public bool HasUserFile => _user is not null;

    /// <summary>The number of messages in the game file; the backward search into the game ids starts at this plus 999.</summary>
    public int GameCount => _game.Count;

    /// <summary>The text of portrait <paramref name="id"/>: from 1000 the game file's, 1..999 the user file's, 0 the game
    /// file's 1000.</summary>
    public PortraitStem Stem(int id)
    {
        if (id < 0) return new(PortraitStemStatus.NegativeId, id, false, ReadOnlyMemory<byte>.Empty);
        var number = id == 0 ? FirstGameId : id;
        var user = number < FirstGameId;
        if (user && _user is null) return new(PortraitStemStatus.NoUserFile, number, true, ReadOnlyMemory<byte>.Empty);
        return (user ? _user! : _game).Find(number) is { } message
            ? new(PortraitStemStatus.Found, number, user, message.Bytes)
            : new(PortraitStemStatus.Missing, number, user, ReadOnlyMemory<byte>.Empty);
    }

    /// <summary>The image paths of portrait <paramref name="id"/> drawn at <paramref name="size"/>: under 128 the small image
    /// <c>portrait/&lt;stem&gt;.bmp</c>, from 128 the large <c>portrait/&lt;stem&gt;_b.bmp</c>.</summary>
    [OriginalFunction(0x4CE3C0, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x4CE4A0, OriginalFunctionScope.Partial)]
    public PortraitImage Image(int id, int size)
    {
        var stem = Stem(id);
        if (stem.Status != PortraitStemStatus.Found) return new(PortraitImageStatus.Missing, stem, null, null);
        if (!ValidStem(stem.Text.Span)) return new(PortraitImageStatus.InvalidStem, stem, null, null);
        var path = "portrait/" + System.Text.Encoding.ASCII.GetString(stem.Text.Span) + (size < LargeSize ? ".bmp" : "_b.bmp");
        var fallback = path.EndsWith("_b.bmp", StringComparison.OrdinalIgnoreCase) ? path[..^"_b.bmp".Length] + ".bmp" : null;
        return new(PortraitImageStatus.Found, stem, path, fallback);
    }

    /// <summary>
    /// Whether <paramref name="subject"/> may take a portrait with text <paramref name="stem"/>: its first three bytes equal,
    /// ignoring ASCII case, the race code followed by <c>F</c> or <c>M</c>, or <c>ANY</c>, or, for an NPC, <c>NPC</c>.
    /// </summary>
    [OriginalFunction(0x4CEAC0, OriginalFunctionScope.Full)]
    public static bool Matches(ReadOnlySpan<byte> stem, PortraitSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        var code = Codes[subject.Race * GenderLetters.Length + subject.Gender];
        return Prefix(stem, code) || Prefix(stem, Any) || (subject.Npc && Prefix(stem, Npc));
    }

    /// <summary>Whether portrait <paramref name="id"/> exists (<see cref="Stem"/>) and <paramref name="subject"/> may take it.</summary>
    public bool Allows(int id, PortraitSubject subject) => Stem(id) is { Status: PortraitStemStatus.Found } stem && Matches(stem.Text.Span, subject);

    /// <summary>The first portrait <paramref name="subject"/> may take: the game ids from 1000 while present, then the user
    /// ids from 1 while present (below 1000); null when there is none.</summary>
    [OriginalFunction(0x4CE6B0, OriginalFunctionScope.Full)]
    public int? First(PortraitSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (subject.MultiplayerPc) return null;
        return GameUp(FirstGameId, subject) ?? UserUp(1, subject);
    }

    /// <summary>The last portrait <paramref name="subject"/> may take among the game ids from <paramref name="start"/> while
    /// present; when there is none, among the user ids from 1; null when neither has one.</summary>
    [OriginalFunction(0x4CE7A0, OriginalFunctionScope.Full)]
    public int? Last(int start, PortraitSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (subject.MultiplayerPc) return null;
        int? last = null;
        for (var id = start; _game.Find(id) is { } message; id++)
        {
            if (id != 0 && Matches(message.Bytes.Span, subject)) last = id;
            if (id == int.MaxValue) break;
        }
        if (last is not null || _user is null) return last;
        for (var id = 1; id < FirstGameId && _user.Find(id) is { } message; id++)
            if (Matches(message.Bytes.Span, subject)) last = id;
        return last;
    }

    /// <summary>The next portrait after <paramref name="current"/>: from a game id the later game ids while present, then
    /// the user ids from 1; from a user id the later user ids; null at the end (no wrap).</summary>
    [OriginalFunction(0x4CE8B0, OriginalFunctionScope.Full)]
    public int? Next(int current, PortraitSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (subject.MultiplayerPc) return null;
        var id = unchecked(current + 1);
        if (id >= FirstGameId)
        {
            if (GameUp(id, subject) is { } game) return game;
            id = 1;
        }
        return UserUp(id, subject);
    }

    /// <summary>The previous portrait before <paramref name="current"/>: none before 1000; from a user id the earlier user ids
    /// while present, then the game ids down from <see cref="GameCount"/> + 999; from a later game id the earlier game ids
    /// while present (down to 1000); null at the end (no wrap).</summary>
    [OriginalFunction(0x4CE9B0, OriginalFunctionScope.Full)]
    public int? Previous(int current, PortraitSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (subject.MultiplayerPc || current == FirstGameId) return null;
        var id = unchecked(current - 1);
        if (id < FirstGameId)
        {
            if (_user is not null)
                for (; id >= 1 && _user.Find(id) is { } message; id--)
                    if (Matches(message.Bytes.Span, subject)) return id;
            id = _game.Count + FirstGameId - 1;
            if (id < FirstGameId) return null;
        }
        for (; id >= FirstGameId && _game.Find(id) is { } message; id--)
            if (Matches(message.Bytes.Span, subject)) return id;
        return null;
    }

    /// <summary>
    /// The creation screen's portrait buttons (handler 0x544FF0), from the PC's stored portrait <paramref name="current"/>:
    /// previous takes the previous portrait, else the last one from <paramref name="current"/>, else keeps
    /// <paramref name="current"/>; next takes the next portrait, else the first, else 0 (the first search clears its result
    /// before it looks). The screen stores the result in field 0xE7 either way.
    /// </summary>
    [OriginalFunction(0x544FF0, OriginalFunctionScope.Partial)]
    public int Press(PortraitButton button, int current, PortraitSubject subject) => button switch
    {
        PortraitButton.Previous => Previous(current, subject) ?? Last(current, subject) ?? current,
        PortraitButton.Next => Next(current, subject) ?? First(subject) ?? 0,
        _ => throw new ArgumentOutOfRangeException(nameof(button)),
    };

    /// <summary>The portrait after the creation screen changes the race or the gender: the first portrait the new race and
    /// gender may take, else <paramref name="current"/> unchanged.</summary>
    [OriginalFunction(0x5453A0, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x545550, OriginalFunctionScope.Partial)]
    public int AfterRaceOrGender(int current, PortraitSubject subject) => First(subject) ?? current;

    /// <summary>
    /// The portrait shown for a character whose stored portrait (field 0xE7) is <paramref name="stored"/>: an NPC whose
    /// non-zero portrait equals the player's (<paramref name="playerPortrait"/>) shows its next portrait, else its previous
    /// one, else 0; anyone else shows the stored one.
    /// </summary>
    [OriginalFunction(0x4CEB80, OriginalFunctionScope.Full)]
    public int Shown(int stored, PortraitSubject subject, int playerPortrait)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (!subject.Npc || stored == 0 || stored != playerPortrait) return stored;
        return Next(stored, subject) ?? Previous(stored, subject) ?? 0;
    }

    private int? GameUp(int start, PortraitSubject subject)
    {
        for (var id = start; _game.Find(id) is { } message; id++)
        {
            if (Matches(message.Bytes.Span, subject)) return id;
            if (id == int.MaxValue) break;
        }
        return null;
    }

    private int? UserUp(int start, PortraitSubject subject)
    {
        if (_user is null) return null;
        for (var id = start; id < FirstGameId && _user.Find(id) is { } message; id++)
            if (Matches(message.Bytes.Span, subject)) return id;
        return null;
    }

    private static bool Prefix(ReadOnlySpan<byte> text, ReadOnlySpan<byte> code)
    {
        if (text.Length < CodeLength) return false;
        for (var i = 0; i < CodeLength; i++)
            if (Lower(text[i]) != Lower(code[i])) return false;
        return true;
    }

    private static byte Lower(byte b) => b is >= (byte)'A' and <= (byte)'Z' ? (byte)(b + 0x20) : b;

    private static bool ValidStem(ReadOnlySpan<byte> text)
    {
        if (text.IsEmpty) return false;
        foreach (var b in text)
            if (b is < 0x20 or > 0x7e or (byte)'/' or (byte)'\\') return false;
        return true;
    }

    private static MesFile? Read(ContentStack stack, string path, MesReaderOptions options, out PortraitCatalogueLoad? refusal)
    {
        refusal = null;
        if (ContentPhase.Candidate(stack, path, ContentLoadPhase.GameStart) is not { } source)
        {
            refusal = new(PortraitCatalogueStatus.FileMissing, null, path);
            return null;
        }
        if (source.Length > options.MaxBytes)
        {
            refusal = new(PortraitCatalogueStatus.ParseFailed, null, path, Parse: new(MesParseStatus.TooLarge, null));
            return null;
        }
        var read = stack.Read(source);
        if (read.Status != ContentReadStatus.Read)
        {
            refusal = new(PortraitCatalogueStatus.ReadFailed, null, path, read.Status);
            return null;
        }
        var parsed = MesFile.Parse(read.Bytes!, options);
        if (parsed.Status == MesParseStatus.Parsed) return parsed.File;
        refusal = new(PortraitCatalogueStatus.ParseFailed, null, path, Parse: parsed);
        return null;
    }
}
