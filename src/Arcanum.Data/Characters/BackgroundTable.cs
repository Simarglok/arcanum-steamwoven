using Arcanum.Data.Content;
using Arcanum.Data.Messages;

namespace Arcanum.Data.Characters;

public enum BackgroundTableStatus { Ready, FileMissing, ReadFailed, ParseFailed }

/// <summary>The background table, or why one of its two files could not be loaded (<see cref="File"/> names it).</summary>
public sealed record BackgroundTableLoad(BackgroundTableStatus Status, BackgroundTable? Table, string? File = null,
    ContentReadStatus? Read = null, MesParseResult? Parse = null);

public enum BackgroundBlockStatus
{
    Parsed,
    /// <summary>No block: its condition message <c>10b+2</c> is absent (or <c>b</c> is negative or too large for a message
    /// number), so the original's search never stops on it.</summary>
    Unreachable,
    /// <summary>The description, effect, money or item message (<c>10b+0</c>, <c>+1</c>, <c>+3</c>, <c>+4</c>) is absent. The
    /// original then reads its "missing line" error text (number 0, and the item list would be that text's words), so the
    /// block is refused instead. <see cref="BackgroundBlockRead.Message"/> names the message.</summary>
    MissingMessage,
    /// <summary>A number slot that is not empty or one run of 1..9 digits between spaces, or an item token that is not one
    /// such run. The original's <c>atoi</c> would read a prefix or 0; such a slot is refused instead of read partially.</summary>
    Malformed,
    /// <summary>A byte outside printable ASCII other than tab and line feed in a number or item slot: the item list is split
    /// only on space, tab and line feed, so another control byte would glue two numbers together.</summary>
    InvalidText,
}

/// <summary>
/// One block of <c>rules\backgrnd.mes</c>: its description number in <c>mes\gameback.mes</c> (<c>10b+0</c>), its effect
/// number (<c>10b+1</c>; an empty slot reads as effect 0, the module's <c>gameeffect.mes</c> effect, which
/// <see cref="EffectSlotEmpty"/> marks), the race and gender condition (<c>10b+2</c>, the stored bytes), the money
/// (<c>10b+3</c>) and the basic prototype numbers of its items (<c>10b+4</c>, in order).
/// </summary>
public sealed record BackgroundBlock(int Number, int Description, int Effect, bool EffectSlotEmpty, ReadOnlyMemory<byte> Condition,
    int Money, IReadOnlyList<int> Items);

/// <param name="Message">The message number a refusal is about.</param>
public sealed record BackgroundBlockRead(BackgroundBlockStatus Status, BackgroundBlock? Block, int? Message = null);

/// <summary>What choosing a background stores and adds (<c>background_set</c> 0x4C25E0): the background number, its
/// description number (field 0x102) and the effect added with cause 1 (background).</summary>
public sealed record BackgroundChoice(int Background, int Description, int Effect);

public enum BackgroundTextStatus
{
    Found,
    /// <summary>No text: description 1..999, a number <c>mes\gameback.mes</c> lacks, or a negative number.</summary>
    Missing,
    /// <summary>The text has no line feed within its first 32 bytes. The original copies the name into a 32-byte buffer and
    /// logs "Background text has no name" (a name of 32 bytes or more would leave that buffer unterminated).</summary>
    NoName,
}

/// <summary>A background's text: the name (the bytes before the first line feed) and the description shown under it (the
/// bytes after it). Both are stored MES bytes; decoding is the caller's choice of profile (<see cref="MesText"/>).</summary>
public sealed record BackgroundText(BackgroundTextStatus Status, ReadOnlyMemory<byte> Name, ReadOnlyMemory<byte> Body);

/// <summary>
/// ARC-DATA-044 (N10a, FR-CHAR-005): the backgrounds the original's Background module loads at its init (0x4C2270, gamelib
/// table "Background"): <c>rules\backgrnd.mes</c> and <c>mes\gameback.mes</c>, both from the base sources (the game-start
/// phase) and both required (the init fails without either). Read in the executable (ledger BD1–BD8):
/// block <c>b</c> exists while message <c>10b+2</c> exists; a race and gender may take it when its condition is empty or,
/// upper-cased, contains the race code and <c>F</c>/<c>M</c>, <c>ANY</c>, or <c>NPC</c> for an NPC; the creation buttons step
/// through the allowed blocks and wrap; the text's first line is the name. Read-only: applying the effect to a character
/// (<c>background_set</c>/<c>background_clear</c>), the start kit (FR-CHAR-007) and the context backgrounds (FR-CHAR-006)
/// are the caller's. NOT READY: the multiplayer money scaling (0x4C2AA0) and the background item removal (0x4C2830).
/// </summary>
public sealed class BackgroundTable
{
    public const string BackgroundsPath = "rules/backgrnd.mes", TextsPath = "mes/gameback.mes";
    public const int MessagesPerBlock = 10, DefaultDescription = 1000, NameBufferBytes = 32;
    public const int FemaleGender = 0, MaleGender = 1;
    private const int DescriptionSlot = 0, EffectSlot = 1, ConditionSlot = 2, MoneySlot = 3, ItemsSlot = 4;
    private const int LastBlock = (int.MaxValue - ItemsSlot) / MessagesPerBlock;

    // The race codes of the legality check (table 0x5B6AEC, races 0..7) and the gender letters ("FM", 0x5B6B0C).
    private static readonly string[] RaceCodes = ["HU", "DW", "EL", "HE", "GN", "HA", "HO", "HG"];
    private const string GenderLetters = "FM";
    private static readonly byte[] Any = "ANY"u8.ToArray(), Npc = "NPC"u8.ToArray();

    private readonly MesFile _blocks;
    private readonly MesFile _texts;

    private BackgroundTable(MesFile blocks, MesFile texts) => (_blocks, _texts) = (blocks, texts);

    /// <summary>The table over already-parsed <c>backgrnd.mes</c> and <c>gameback.mes</c> (synthetic data or another loader).</summary>
    public static BackgroundTable Create(MesFile backgrounds, MesFile texts) =>
        new(backgrounds ?? throw new ArgumentNullException(nameof(backgrounds)), texts ?? throw new ArgumentNullException(nameof(texts)));

    /// <summary>Reads both files from <paramref name="stack"/>'s base sources, as the module's init does at game start.</summary>
    [OriginalFunction(0x4C2270, OriginalFunctionScope.Full)]
    public static BackgroundTableLoad Load(ContentStack stack, MesReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stack);
        options ??= new();
        options.Validate();
        var blocks = Read(stack, BackgroundsPath, options, out var refusal);
        if (blocks is null) return refusal!;
        var texts = Read(stack, TextsPath, options, out refusal);
        return texts is null ? refusal! : new(BackgroundTableStatus.Ready, new(blocks, texts));
    }

    /// <summary>The number of blocks from 0 up to the first absent condition message: the blocks the first search sees.</summary>
    public int Count
    {
        get
        {
            var count = 0;
            while (HasBlock(count)) count++;
            return count;
        }
    }

    /// <summary>Whether block <paramref name="background"/> exists: its condition message <c>10b+2</c> does.</summary>
    public bool HasBlock(int background) =>
        background is >= 0 and <= LastBlock && _blocks.Find(background * MessagesPerBlock + ConditionSlot) is not null;

    /// <summary>A block's slots (descriptions 0x4C24B0, money 0x4C26F0 and item list 0x4C2B10 read the same messages).</summary>
    public BackgroundBlockRead Block(int background)
    {
        if (!HasBlock(background)) return new(BackgroundBlockStatus.Unreachable, null);
        var first = background * MessagesPerBlock;
        var values = new int[3];
        var empty = false;
        foreach (var (slot, index) in new[] { (DescriptionSlot, 0), (EffectSlot, 1), (MoneySlot, 2) })
        {
            var message = _blocks.Find(first + slot);
            if (message is null) return new(BackgroundBlockStatus.MissingMessage, null, first + slot);
            var status = Number(message.Bytes.Span, out values[index], out var isEmpty);
            if (status != BackgroundBlockStatus.Parsed) return new(status, null, first + slot);
            if (slot == EffectSlot) empty = isEmpty;
        }
        var list = _blocks.Find(first + ItemsSlot);
        if (list is null) return new(BackgroundBlockStatus.MissingMessage, null, first + ItemsSlot);
        var items = new List<int>();
        var text = list.Bytes.Span;
        if (!Printable(text)) return new(BackgroundBlockStatus.InvalidText, null, first + ItemsSlot);
        foreach (var token in System.Text.Encoding.ASCII.GetString(text).Split([' ', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Digits(token, out var item)) return new(BackgroundBlockStatus.Malformed, null, first + ItemsSlot);
            items.Add(item);
        }
        var condition = _blocks.Find(first + ConditionSlot)!.Bytes;
        return new(BackgroundBlockStatus.Parsed, new(background, values[0], values[1], empty, condition, values[2], items.AsReadOnly()));
    }

    /// <summary>
    /// Whether a character of <paramref name="race"/> (0..7) and <paramref name="gender"/> (0 female, 1 male) may take
    /// block <paramref name="background"/>: its condition is empty, or upper-cased (ASCII letters) contains the race code
    /// followed by <c>F</c> or <c>M</c>, or <c>ANY</c>, or, for an NPC, <c>NPC</c>. A block that does not exist is not allowed.
    /// </summary>
    [OriginalFunction(0x4C2B50, OriginalFunctionScope.Full)]
    public bool Allows(int background, int race, int gender, bool npc = false)
    {
        var code = Code(race, gender);
        if (!HasBlock(background)) return false;
        var condition = _blocks.Find(background * MessagesPerBlock + ConditionSlot)!.Bytes.Span;
        if (condition.IsEmpty) return true;
        var upper = condition.ToArray();
        for (var i = 0; i < upper.Length; i++)
            if (upper[i] is >= (byte)'a' and <= (byte)'z') upper[i] -= 0x20;
        return upper.AsSpan().IndexOf(code) >= 0 || upper.AsSpan().IndexOf(Any) >= 0 || (npc && upper.AsSpan().IndexOf(Npc) >= 0);
    }

    /// <summary>The first allowed block from 0, or null (the search stops at the first absent block).</summary>
    [OriginalFunction(0x4C2300, OriginalFunctionScope.Full)]
    public int? First(int race, int gender, bool npc = false) => Search(0, +1, race, gender, npc);

    /// <summary>The next allowed block after <paramref name="current"/>, or null; no wrap.</summary>
    [OriginalFunction(0x4C2390, OriginalFunctionScope.Full)]
    public int? Next(int current, int race, int gender, bool npc = false) => current == int.MaxValue ? null : Search(current + 1, +1, race, gender, npc);

    /// <summary>The previous allowed block before <paramref name="current"/>, or null; no wrap.</summary>
    [OriginalFunction(0x4C2420, OriginalFunctionScope.Full)]
    public int? Previous(int current, int race, int gender, bool npc = false) => current == int.MinValue ? null : Search(current - 1, -1, race, gender, npc);

    /// <summary>The creation screen's "next background" button: the next allowed block, else the first; null when no block is
    /// allowed (the button then changes nothing).</summary>
    [OriginalFunction(0x5452C0, OriginalFunctionScope.Full)]
    public int? ButtonNext(int current, int race, int gender) => Next(current, race, gender) ?? First(race, gender);

    /// <summary>The creation screen's "previous background" button: the previous allowed block, else the last one reached by
    /// stepping forward from <paramref name="current"/>, else <paramref name="current"/> itself. The button always reports
    /// success, so the screen then clears and sets the result even when it is unchanged.</summary>
    [OriginalFunction(0x545300, OriginalFunctionScope.Full)]
    public int ButtonPrevious(int current, int race, int gender)
    {
        if (Previous(current, race, gender) is { } previous) return previous;
        var last = current;
        while (Next(last, race, gender) is { } next) last = next;
        return last;
    }

    /// <summary>What the screen stores and adds when a button lands on <paramref name="background"/>: the description number
    /// (<c>atoi</c> of <c>10b+0</c>) and the effect (<c>atoi</c> of <c>10b+1</c>, cause 1). Null when the block is refused.</summary>
    [OriginalFunction(0x4C24B0, OriginalFunctionScope.Full)]
    [OriginalFunction(0x4C25E0, OriginalFunctionScope.Partial)]
    public BackgroundChoice? Choice(int background) =>
        Block(background).Block is { } block ? new(block.Number, block.Description, block.Effect) : null;

    /// <summary>Block 0's money, the base the multiplayer money scaling divides by.</summary>
    [OriginalFunction(0x4C2A70, OriginalFunctionScope.Full)]
    public int? BaseMoney() => Block(0).Block?.Money;

    /// <summary>
    /// The text of description <paramref name="description"/> (field 0x102): 0 reads 1000, the default text; 1..999 have none.
    /// The name is the bytes before the first line feed, which must fall within the first 32 bytes; the body is the bytes
    /// after it.
    /// </summary>
    [OriginalFunction(0x4C24E0, OriginalFunctionScope.Full)]
    [OriginalFunction(0x4C2530, OriginalFunctionScope.Full)]
    [OriginalFunction(0x4C2590, OriginalFunctionScope.Partial)]
    public BackgroundText Text(int description)
    {
        var number = description == 0 ? DefaultDescription : description;
        if (number < DefaultDescription || _texts.Find(number) is not { } message)
            return new(BackgroundTextStatus.Missing, ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty);
        var bytes = message.Bytes;
        var feed = bytes.Span.IndexOf((byte)'\n');
        return feed is < 0 or >= NameBufferBytes
            ? new(BackgroundTextStatus.NoName, ReadOnlyMemory<byte>.Empty, feed < 0 ? bytes : bytes[(feed + 1)..])
            : new(BackgroundTextStatus.Found, bytes[..feed], bytes[(feed + 1)..]);
    }

    private int? Search(int start, int step, int race, int gender, bool npc)
    {
        Code(race, gender);
        for (var background = start; HasBlock(background); background += step)
            if (Allows(background, race, gender, npc)) return background;
        return null;
    }

    private static byte[] Code(int race, int gender)
    {
        if (race is < 0 or >= 8) throw new ArgumentOutOfRangeException(nameof(race), "the legality table holds races 0..7");
        if (gender is not (FemaleGender or MaleGender)) throw new ArgumentOutOfRangeException(nameof(gender));
        return System.Text.Encoding.ASCII.GetBytes(RaceCodes[race] + GenderLetters[gender]);
    }

    private static BackgroundBlockStatus Number(ReadOnlySpan<byte> text, out int value, out bool empty)
    {
        value = 0;
        empty = false;
        if (!Printable(text)) return BackgroundBlockStatus.InvalidText;
        var trimmed = System.Text.Encoding.ASCII.GetString(text).Trim(' ');
        if (trimmed.Length == 0)
        {
            empty = true;
            return BackgroundBlockStatus.Parsed;
        }
        return Digits(trimmed, out value) ? BackgroundBlockStatus.Parsed : BackgroundBlockStatus.Malformed;
    }

    // Printable ASCII, space, tab and line feed.
    private static bool Printable(ReadOnlySpan<byte> text)
    {
        foreach (var b in text)
            if (b is not (>= 0x20 and <= 0x7e or (byte)'\t' or (byte)'\n')) return false;
        return true;
    }

    private static bool Digits(string token, out int value)
    {
        value = 0;
        return token.Length is >= 1 and <= 9 && token.All(c => c is >= '0' and <= '9') &&
            int.TryParse(token, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private static MesFile? Read(ContentStack stack, string path, MesReaderOptions options, out BackgroundTableLoad? refusal)
    {
        refusal = null;
        if (ContentPhase.Candidate(stack, path, ContentLoadPhase.GameStart) is not { } source)
        {
            refusal = new(BackgroundTableStatus.FileMissing, null, path);
            return null;
        }
        if (source.Length > options.MaxBytes)
        {
            refusal = new(BackgroundTableStatus.ParseFailed, null, path, Parse: new(MesParseStatus.TooLarge, null));
            return null;
        }
        var read = stack.Read(source);
        if (read.Status != ContentReadStatus.Read)
        {
            refusal = new(BackgroundTableStatus.ReadFailed, null, path, read.Status);
            return null;
        }
        var parsed = MesFile.Parse(read.Bytes!, options);
        if (parsed.Status == MesParseStatus.Parsed) return parsed.File;
        refusal = new(BackgroundTableStatus.ParseFailed, null, path, Parse: parsed);
        return null;
    }
}
