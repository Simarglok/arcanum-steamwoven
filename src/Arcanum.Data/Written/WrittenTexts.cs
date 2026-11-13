using System.Text;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;

namespace Arcanum.Data.Written;

/// <summary>The written subtypes the reading window knows (0x56BC00; prototypes set 0..5, scripts open 0, 4 and 6).</summary>
public enum WrittenSubtype { Book = 0, Note = 1, Newspaper = 2, Telegram = 3, Picture = 4, Schematic = 5, Plaque = 6 }

public enum WrittenDataStatus
{
    Read,
    /// <summary>The file is not in the base sources (the window then refuses that subtype silently).</summary>
    FileMissing,
    FileUnreadable,
    /// <summary>The file is not one the MES grammar admits (project policy).</summary>
    FileRefused,
    /// <summary>The file has no message (the original's loader fails on it).</summary>
    FileEmpty,
}

/// <summary>Why a written-text file was not loaded.</summary>
public sealed record WrittenFileRefusal(WrittenDataStatus Status, string Path, string? Detail = null);

/// <summary>How a reading resolved.</summary>
public enum WrittenReadStatus
{
    Read,
    /// <summary>A schematic document (subtype 5) never reaches the window: it registers instead (ARC-TECHNOLOGY-003).</summary>
    Schematic,
    /// <summary>A subtype outside 0..6: the original reads its art and text tables out of bounds (WR anomaly, both modes refuse).</summary>
    SubtypeOutOfRange,
    /// <summary>The subtype's text file did not load (the original's open refuses silently).</summary>
    TextsUnavailable,
    /// <summary>A picture art number outside 0..4095: the original's art id builder refuses it and the caller draws garbage
    /// (WR anomaly, both modes refuse).</summary>
    ArtOutOfRange,
}

/// <summary>One shown message: its number, its text after the subtype's markup (Latin-1, as stored), the font art the markup names
/// (0 when none, or when it is outside 0..4095, which the original turns into garbage) and whether it is centred.</summary>
public sealed record WrittenMessage(int Number, string Text, int Font, bool Centered);

/// <summary>
/// What the reading window shows for one written item or script opener (ledger WR rows): the subtype, the start line, the frame
/// and background art, the body messages (at most ten, from the start line to the first missing number), the subtype's fixed
/// header texts (a telegram's 1000..1011, a newspaper's masthead) and, for a picture, its art.
/// </summary>
public sealed record WrittenDocument(WrittenReadStatus Status, WrittenSubtype? Subtype, int StartLine, int BackgroundArt,
    IReadOnlyList<WrittenMessage> Body, IReadOnlyList<WrittenMessage> Header, int? PictureArt)
{
    public bool Readable => Status == WrittenReadStatus.Read;
}

/// <summary>
/// ARC-UI-049: the texts of the reading window (module init 0x56BAA0, open 0x56BC00, composition 0x56BCC0): the five files
/// <c>mes\gamebook.mes</c>, <c>gamenote</c>, <c>gamenewspaper</c>, <c>gametelegram</c> and <c>gameplaque</c>, each loaded on its own
/// (a missing one refuses only its subtype), and the per-subtype composition. The written end line is never read by the original
/// (WR3): a document shows its start line and the next nine, to the first missing number.
/// </summary>
public sealed class WrittenTexts
{
    public const int MessagesPerDocument = 10, FrameArt = 494, NewspaperAlternatePrototype = 14092, NewspaperAlternateArt = 817,
        NewspaperAlternateFont = 815, MaxArt = 4095, TelegramSuffix = 1012, FirstTelegramHeader = 1000, LastTelegramHeader = 1011,
        FirstNewspaperFiller = 10, LastNewspaperFiller = 999, BookPreviousArt = 495, BookNextArt = 496;
    /// <summary>The most filler messages one call gives (the original fills by the remaining column height; a caller asks for a few).</summary>
    public const int MaxFillerCount = 1_000;
    /// <summary>The note and telegram buffer of the original (0x67BE00): texts are appended without a bound (WR anomaly); the
    /// composition stops one byte short of this (19,999 bytes) in both modes.</summary>
    public const int JoinedBufferBytes = 20_000;
    /// <summary>The book page table of the original holds 100 page starts and one more page writes past it (WR anomaly); both modes
    /// stop at this many pages.</summary>
    public const int MaxBookPages = 100;

    public static IReadOnlyList<string> Paths { get; } = Array.AsReadOnly(new[]
    {
        @"mes\gamebook.mes", @"mes\gamenote.mes", @"mes\gamenewspaper.mes", @"mes\gametelegram.mes", @"mes\gameplaque.mes",
    });

    private static readonly int[] BackgroundArts = [477, 489, 487, 491, -1, -1, 633];
    // The newspaper's six masthead entries in drawing order (messages and fonts; the positions and alignments are the layout's).
    private static readonly (int Message, int Font)[] Masthead = [(0, 485), (1, 484), (0, 485), (2, 482), (3, 482), (2, 482)];
    private static readonly (int Message, int Font)[] AlternateMasthead = [(0, 485), (4, NewspaperAlternateFont), (0, 485), (2, 482), (5, 482), (2, 482)];
    private readonly MesFile?[] _files;

    private WrittenTexts(MesFile?[] files, IReadOnlyList<WrittenFileRefusal> refusals)
    {
        _files = files;
        Refusals = refusals;
        // 0x4D4550(10, 999): 0 unless message 10 exists, else the entries from 10 while their number is at most 999.
        NewspaperFillers = files[2] is { } newspaper && newspaper.Find(FirstNewspaperFiller) is not null
            ? newspaper.Messages.Count(m => m.Number is >= FirstNewspaperFiller and <= LastNewspaperFiller) : 0;
    }

    /// <summary>The files that did not load (each refuses only its own subtype).</summary>
    public IReadOnlyList<WrittenFileRefusal> Refusals { get; }

    /// <summary>The count of newspaper messages numbered 10..999, which the filler numbering wraps at (counted at init).</summary>
    public int NewspaperFillers { get; }

    /// <summary>Loads the five files from the content stack (the module is mounted when the reading UI inits); each that fails is a refusal.</summary>
    [OriginalFunction(0x56BAA0, OriginalFunctionScope.Partial)]
    public static WrittenTexts Open(ContentStack stack, MesReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stack);
        options ??= new();
        options.Validate();
        var files = new MesFile?[Paths.Count];
        var refusals = new List<WrittenFileRefusal>();
        for (var i = 0; i < Paths.Count; i++)
        {
            var refusal = Load(stack, Paths[i], options, out files[i]);
            if (refusal is not null) refusals.Add(refusal);
        }
        return new(files, refusals.AsReadOnly());
    }

    /// <summary>The texts from parsed files in the order book, note, newspaper, telegram, plaque (synthetic data); a null file is
    /// missing and an empty one refused, as the loader would.</summary>
    public static WrittenTexts Create(MesFile? book, MesFile? note, MesFile? newspaper, MesFile? telegram, MesFile? plaque)
    {
        var files = new[] { book, note, newspaper, telegram, plaque };
        var refusals = new List<WrittenFileRefusal>();
        for (var i = 0; i < files.Length; i++)
        {
            if (files[i] is null) refusals.Add(new(WrittenDataStatus.FileMissing, Paths[i]));
            else if (files[i]!.Count == 0) { refusals.Add(new(WrittenDataStatus.FileEmpty, Paths[i])); files[i] = null; }
        }
        return new(files, refusals.AsReadOnly());
    }

    /// <summary>
    /// The document of a written item or a script opener (0x56BC00 / 0x56BCC0): <paramref name="subtype"/> and the start line, and
    /// whether the item is the alternate newspaper prototype 14092 (the executable's 14072). A schematic, a subtype outside 0..6, a missing file or a picture
    /// art outside 0..4095 is a refusal with no body. The markup follows <paramref name="rules"/> (WRD3); without rules it reads as the
    /// retail game does (a tool's view), so a game session passes its own.
    /// </summary>
    [OriginalFunction(0x56BCC0, OriginalFunctionScope.Partial)]
    public WrittenDocument Read(int subtype, int startLine, bool alternateNewspaper = false, WrittenRuleProfile? rules = null)
    {
        if (subtype is < 0 or > (int)WrittenSubtype.Plaque) return Refused(WrittenReadStatus.SubtypeOutOfRange, null, startLine);
        var kind = (WrittenSubtype)subtype;
        if (kind == WrittenSubtype.Schematic) return Refused(WrittenReadStatus.Schematic, kind, startLine);
        if (kind == WrittenSubtype.Picture)
            return startLine is < 0 or > MaxArt ? Refused(WrittenReadStatus.ArtOutOfRange, kind, startLine)
                : new(WrittenReadStatus.Read, kind, startLine, startLine, [], [], startLine);
        if (File(kind) is not { } file) return Refused(WrittenReadStatus.TextsUnavailable, kind, startLine);
        // The prototype-14092 flag selects art 817 for every subtype but a picture (WR3); only that newspaper prototype sets it.
        var background = alternateNewspaper ? NewspaperAlternateArt : BackgroundArts[subtype];
        var raw = Lines(file, startLine).ToArray();
        IReadOnlyList<WrittenMessage> body = kind switch
        {
            WrittenSubtype.Book or WrittenSubtype.Plaque or WrittenSubtype.Newspaper => raw.Select(m => Markup(m.Number, m.Text, rules)).ToArray(),
            WrittenSubtype.Telegram => Joined(raw.Select(m => new WrittenMessage(m.Number, m.Text + Text(file, TelegramSuffix), 0, false))),
            _ => Joined(raw.Select(m => new WrittenMessage(m.Number, m.Text + "\n", 0, false))),
        };
        IReadOnlyList<WrittenMessage> header = kind switch
        {
            WrittenSubtype.Telegram => Enumerable.Range(FirstTelegramHeader, LastTelegramHeader - FirstTelegramHeader + 1)
                .Select(n => new WrittenMessage(n, Text(file, n), 0, false)).ToArray(),
            WrittenSubtype.Newspaper => (alternateNewspaper ? AlternateMasthead : Masthead)
                .Select(entry => new WrittenMessage(entry.Message, Text(file, entry.Message), entry.Font, false)).ToArray(),
            _ => [],
        };
        return new(WrittenReadStatus.Read, kind, startLine, background, Array.AsReadOnly(body.ToArray()), Array.AsReadOnly(header.ToArray()), null);
    }

    /// <summary>
    /// The newspaper filler messages after the body (the redraw 0x56C050): the first number is 10 + start / 10 (truncated toward
    /// zero), and after each message the number rises by one; a number at or past 10 + <see cref="NewspaperFillers"/> becomes 10. A
    /// missing message stops the filling. <paramref name="count"/> is how many the caller's columns take (the original fills their
    /// remaining height). Empty without newspaper texts or filler messages. The markup follows <paramref name="rules"/> as in
    /// <see cref="Read"/>.
    /// </summary>
    [OriginalFunction(0x56C050, OriginalFunctionScope.Partial)]
    public IReadOnlyList<WrittenMessage> NewspaperFiller(int startLine, int count, WrittenRuleProfile? rules = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, MaxFillerCount);
        if (File(WrittenSubtype.Newspaper) is not { } file) return [];
        var result = new List<WrittenMessage>(Math.Min(count, NewspaperFillers));
        var end = (long)FirstNewspaperFiller + NewspaperFillers;
        var number = FirstNewspaperFiller + (long)startLine / 10;
        if (number >= end) number = FirstNewspaperFiller;
        for (var i = 0; i < count; i++)
        {
            if (file.Find((int)number) is not { } message) break;
            result.Add(Markup((int)number, Encoding.Latin1.GetString(message.Bytes.Span), rules));
            if (++number >= end) number = FirstNewspaperFiller;
        }
        return result.AsReadOnly();
    }

    /// <summary>The first default issue's offset above <see cref="DefaultIssueBase"/>, and the step between the probed numbers (0x4BF330: 5010, 5020, ...).</summary>
    public const int DefaultIssueBase = 5000, DefaultIssueStep = 10;

    /// <summary>
    /// The headline of newspaper issue <paramref name="number"/> as the interface's message hook gives it (0x56CCA0, ARC-UI-1739, ledger NP2): message <paramref name="number"/> of the newspaper file with the markup
    /// of the written texts taken off (0x56C9F0, under <paramref name="rules"/> as in <see cref="Read"/>) and every line feed made a space. An issue with no message, or no newspaper file, gives an empty text (the hook clears its buffer first).
    /// </summary>
    [OriginalFunction(0x56CCA0, OriginalFunctionScope.Partial)]
    public string Headline(int number, WrittenRuleProfile? rules = null)
    {
        if (File(WrittenSubtype.Newspaper)?.Find(number) is not { } message) return string.Empty;
        return Markup(number, Encoding.Latin1.GetString(message.Bytes.Span), rules).Text.Replace('\n', ' ');
    }

    /// <summary>
    /// How many default issues the newspaper file offers above <see cref="DefaultIssueBase"/> (the probe loop of 0x4BF330, ledger NQ4): the headlines of 5010, 5020, ... are asked one after the other and the count is how many came
    /// back with text before the first that did not. The default issue is then 5000 + 10 × a number drawn from 0 to the count, so the count is the draw's upper bound.
    /// </summary>
    [OriginalFunction(0x4BF330, OriginalFunctionScope.Partial)]
    public int DefaultIssueChoices(WrittenRuleProfile? rules = null)
    {
        var count = 0;
        // The numbers of a file are int; the probe stops at the first missing headline, so it never runs past the file.
        for (long number = DefaultIssueBase + DefaultIssueStep; number <= int.MaxValue; number += DefaultIssueStep, count++)
            if (Headline((int)number, rules).Length == 0) break;
        return count;
    }

    /// <summary>
    /// The markup of book, plaque and newspaper messages (0x56C9F0): the font is atoi of the text (leading whitespace and a sign
    /// admitted), as an interface art number; the parser then skips the leading digits only, tests the next character for <c>c</c> or
    /// <c>C</c> (centred) and drops it, then drops one following whitespace character. So a message with no prefix uses font 0 and
    /// loses its first character (centred when that is a <c>c</c>). A font outside 0..4095 reads as 0 (the original turns it into
    /// garbage: WR anomaly, both modes). Under <see cref="WrittenRuleProfile.Steamwoven"/> a message whose first character is not an
    /// ASCII digit has no markup: its whole text, font 0, not centred (WRD3); null <paramref name="rules"/> read as the original.
    /// </summary>
    [OriginalFunction(0x56C9F0, OriginalFunctionScope.Partial)]
    public static WrittenMessage Markup(int number, string text, WrittenRuleProfile? rules = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (rules is { UnprefixedMessageLosesFirstCharacter: false } && (text.Length == 0 || !char.IsAsciiDigit(text[0])))
            return new(number, text, 0, false);
        var font = Atoi(text);
        var i = 0;
        while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
        var centered = i < text.Length && text[i] is 'c' or 'C';
        if (i < text.Length) i++;
        if (i < text.Length && text[i] is ' ' or '\t' or '\n' or '\v' or '\f' or '\r') i++;
        return new(number, text[i..], font is >= 0 and <= MaxArt ? (int)font : 0, centered);
    }

    // C atoi: leading whitespace, an optional sign, then digits (saturated here; the original's overflow is undefined).
    private static long Atoi(string text)
    {
        var i = 0;
        while (i < text.Length && text[i] is ' ' or '\t' or '\n' or '\v' or '\f' or '\r') i++;
        var negative = i < text.Length && text[i] == '-';
        if (i < text.Length && text[i] is '-' or '+') i++;
        long value = 0;
        while (i < text.Length && char.IsAsciiDigit(text[i]) && value <= int.MaxValue) value = value * 10 + (text[i++] - '0');
        return negative ? -value : value;
    }

    private MesFile? File(WrittenSubtype subtype) => subtype switch
    {
        WrittenSubtype.Book => _files[0], WrittenSubtype.Note => _files[1], WrittenSubtype.Newspaper => _files[2],
        WrittenSubtype.Telegram => _files[3], WrittenSubtype.Plaque => _files[4], _ => null,
    };

    /// <summary>The start line and the next nine, to the first missing number (an overflowing number ends the text).</summary>
    private static IEnumerable<(int Number, string Text)> Lines(MesFile file, int start)
    {
        for (long number = start; number < (long)start + MessagesPerDocument && number <= int.MaxValue; number++)
        {
            if (file.Find((int)number) is not { } message) yield break;
            yield return ((int)number, Encoding.Latin1.GetString(message.Bytes.Span));
        }
    }

    /// <summary>A note's or telegram's messages as the original joins them into its 20,000-byte buffer, stopping at the bound.</summary>
    private static WrittenMessage[] Joined(IEnumerable<WrittenMessage> messages)
    {
        var result = new List<WrittenMessage>();
        var used = 0;
        foreach (var message in messages)
        {
            if (used + message.Text.Length >= JoinedBufferBytes)
            {
                var room = Math.Max(0, JoinedBufferBytes - 1 - used);
                if (room > 0) result.Add(message with { Text = message.Text[..room] });
                break;
            }
            used += message.Text.Length;
            result.Add(message);
        }
        return result.ToArray();
    }

    /// <summary>A fixed text of the subtype's file; a missing one shows the original's missing-line text (0x4D43A0), here empty.</summary>
    private static string Text(MesFile file, int number) => file.Find(number) is { } message ? Encoding.Latin1.GetString(message.Bytes.Span) : string.Empty;

    private static WrittenDocument Refused(WrittenReadStatus status, WrittenSubtype? subtype, int start) => new(status, subtype, start, -1, [], [], null);

    private static WrittenFileRefusal? Load(ContentStack stack, string path, MesReaderOptions options, out MesFile? file)
    {
        file = null;
        if (ContentPhase.Candidate(stack, path, ContentLoadPhase.ModuleLoaded) is not { } source) return new(WrittenDataStatus.FileMissing, path);
        if (source.Length > options.MaxBytes) return new(WrittenDataStatus.FileRefused, path, nameof(MesParseStatus.TooLarge));
        var read = stack.Read(source);
        if (read.Status != ContentReadStatus.Read) return new(WrittenDataStatus.FileUnreadable, path, read.Status.ToString());
        var parsed = MesFile.Parse(read.Bytes!, options);
        if (parsed.Status != MesParseStatus.Parsed) return new(WrittenDataStatus.FileRefused, path, $"{parsed.Status} line {parsed.Line}");
        if (parsed.File!.Count == 0) return new(WrittenDataStatus.FileEmpty, path);
        file = parsed.File;
        return null;
    }
}
