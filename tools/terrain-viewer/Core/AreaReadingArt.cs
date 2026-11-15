using Arcanum.Data.Content;
using Arcanum.Data.Written;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>One placed line of the reading window's text: the font art it is drawn in, its top left in the window and its glyphs (each with its x from the line's left).</summary>
public sealed record AreaReadingLine(int Font, int X, int Y, IReadOnlyList<(int X, AreaArtGlyph Glyph)> Glyphs);

/// <summary>A page of a book or a plaque: its lines and where the text goes on (message index and character offset), null when it ended.</summary>
public sealed record AreaReadingPage(IReadOnlyList<AreaReadingLine> Lines, (int Index, int Offset)? Next);

/// <summary>A text rectangle of the window (x, y, width, height in the window's pixels).</summary>
public readonly record struct AreaReadingRect(int X, int Y, int Width, int Height);

/// <summary>One header entry of a telegram's form or a newspaper's masthead: the font art, the point and the alignment (0 the text starts at the point, 1 it is centred on it, 2 it ends at it).</summary>
public readonly record struct AreaReadingHeaderSlot(int Font, int X, int Y, int Align);

/// <summary>
/// ARC-UI-1877: the reading window in the original's look, read from the window descriptor 0x5CA478, the create 0x56BCC0, the redraw 0x56C050, the callback 0x56BFC0 and the
/// tables 0x5CA4A8..0x5CA777 (ledger WR17–WR23). The window is the game windows' 800 × 399 at the screen's (0, 41); every coordinate below is the window's. The frame art 494 is drawn at
/// (0, 0), the subtype's background art over it at (132, 0) (0x56C590 with the offsets 0x84 and 0), the lens art 231 over the round view of the hero at (25, 24), a book's previous and next
/// buttons (arts 495 and 496) at (213, 77) and (675, 77). Godot-free.
/// </summary>
public static class AreaReadingArtLayout
{
    public static (int X, int Y, int Width, int Height) Window { get; } = (0, 41, 800, 399);

    /// <summary>The frame art (<see cref="WrittenTexts.FrameArt"/>) at (0, 0); the background art of every subtype at (132, 0) (the redraw 0x56C050 asks 0x56C590 for it with the x offset 0x84).</summary>
    public const int FrameArt = WrittenTexts.FrameArt, LensArt = 231, PreviousArt = WrittenTexts.BookPreviousArt, NextArt = WrittenTexts.BookNextArt;
    public static (int X, int Y) BackgroundCorner { get; } = (132, 0);

    /// <summary>The close hotspot and the lens (0x5CA4A8, 0x5CA4B0): 89 × 89 at (25, 24); a left release inside it closes the window (the callback 0x56BFC0 tests it with 0x551000).</summary>
    public static (int X, int Y, int Side) Lens { get; } = (25, 24, 89);

    public static (int X, int Y) PreviousCorner { get; } = (213, 77);
    public static (int X, int Y) NextCorner { get; } = (675, 77);

    /// <summary>The backgrounds by subtype (0x5CA4B8): book 477, note 489, newspaper 487, telegram 491, plaque 633; the alternate newspaper prototype's 817.</summary>
    public static IReadOnlyList<int> BackgroundArts { get; } = [477, 489, 487, 491, 633, WrittenTexts.NewspaperAlternateArt];

    /// <summary>The font arts the fixed texts use: the telegram's form (481 and 479, 0x5CA538) and body (480), the newspaper's masthead (485, 484, 482; the alternate's 815) and the note (497).</summary>
    public const int NoteFont = 497, TelegramBodyFont = 480, TelegramFormFont = 481, TelegramNameFont = 479, MastheadFont = 485, MastheadSideFont = 484, MastheadLineFont = 482;
    public static IReadOnlyList<int> FixedFontArts { get; } = [TelegramNameFont, TelegramBodyFont, TelegramFormFont, MastheadLineFont, MastheadSideFont, MastheadFont, NoteFont, WrittenTexts.NewspaperAlternateFont];

    /// <summary>The arts without which the window is not drawn in the original's look (the alternate newspaper's 817 and 815 are optional).</summary>
    public static IReadOnlyList<int> RequiredArtNumbers { get; } =
        [FrameArt, LensArt, PreviousArt, NextArt, 477, 489, 487, 491, 633, .. FixedFontArts.Where(font => font != WrittenTexts.NewspaperAlternateFont)];

    /// <summary>The page rectangles of a book spread (0x5CA618, 0x5CA628): two pages of 200 × 290 at (227, 57) and (476, 57).</summary>
    public static IReadOnlyList<AreaReadingRect> BookPages { get; } = [new(227, 57, 200, 290), new(476, 57, 200, 290)];

    /// <summary>A note's text rectangle (0x5CA4F8), a plaque's (0x5CA528), a telegram's body (0x5CA508) and its last form line, message 1011 (0x5CA518).</summary>
    public static AreaReadingRect NoteText { get; } = new(349, 49, 215, 300);
    public static AreaReadingRect PlaqueText { get; } = new(269, 40, 428, 327);
    public static AreaReadingRect TelegramBody { get; } = new(327, 126, 250, 240);
    public static AreaReadingRect TelegramLastForm { get; } = new(335, 90, 234, 20);

    /// <summary>The telegram's eleven form lines, messages 1000..1010 in this order (0x5CA538).</summary>
    public static IReadOnlyList<AreaReadingHeaderSlot> TelegramForm { get; } =
    [
        new(481, 342, 35, 1), new(481, 342, 41, 1), new(481, 321, 50, 0), new(481, 321, 59, 0), new(481, 321, 68, 0), new(479, 450, 36, 1),
        new(479, 450, 61, 1), new(481, 558, 35, 1), new(481, 539, 50, 0), new(481, 539, 60, 0), new(481, 539, 69, 0),
    ];

    /// <summary>The newspaper's six masthead lines (messages 0, 1, 0, 2, 3, 2; the alternate prototype's 0, 4, 0, 2, 5, 2 with the font 815 for the second; 0x5CA638, 0x5CA6B0).</summary>
    public static IReadOnlyList<AreaReadingHeaderSlot> Masthead { get; } =
    [
        new(485, 216, 44, 1), new(484, 453, 41, 1), new(485, 689, 44, 1), new(482, 194, 76, 0), new(482, 449, 76, 1), new(482, 712, 76, 2),
    ];
    public static IReadOnlyList<AreaReadingHeaderSlot> AlternateMasthead { get; } =
    [
        new(485, 216, 44, 1), new(815, 453, 41, 1), new(485, 689, 44, 1), new(482, 194, 76, 0), new(482, 449, 76, 1), new(482, 712, 76, 2),
    ];

    /// <summary>The newspaper's headline rectangle (0x5CA728) and its four columns (0x5CA738): the headline is the first message, the next messages and the fillers flow down the columns.</summary>
    public static AreaReadingRect Headline { get; } = new(186, 97, 255, 90);
    public static IReadOnlyList<AreaReadingRect> Columns { get; } = [new(188, 196, 120, 175), new(328, 196, 120, 175), new(460, 103, 120, 270), new(592, 103, 120, 270)];

    /// <summary>A book keeps at most this many page starts (the table 0x67BC70 holds 100; the original writes past it, WRA4, and shows no next button from the hundredth).</summary>
    public const int MaxPages = WrittenTexts.MaxBookPages;

    /// <summary>The text colours (the display-table levels 0 and 255 of the text draws): black, the plaque's white.</summary>
    public static readonly (byte R, byte G, byte B) Black = (0, 0, 0), White = (255, 255, 255);

    public static (byte R, byte G, byte B) Colour(WrittenSubtype subtype) => subtype == WrittenSubtype.Plaque ? White : Black;

    /// <summary>The lens's rectangle test (0x551000 over the window's point): left and top edges in, right and bottom out.</summary>
    public static bool LensContains(double x, double y) => x >= Lens.X && x < Lens.X + Lens.Side && y >= Lens.Y && y < Lens.Y + Lens.Side;

    /// <summary>Every font art a document names: the fixed ones the window loads at the module's open and the markup fonts of the written texts, and Garmond9 (<see cref="AreaReadingText.SubstituteFont"/>, optional: without it a small font keeps its own glyphs).</summary>
    public static IReadOnlyList<int> FontArts(IEnumerable<int> markupFonts) =>
        [.. FixedFontArts.Concat(markupFonts).Append(AreaReadingText.SubstituteFont).Where(font => font > 0).Distinct().Order()];
}

/// <summary>
/// ARC-UI-1877: the reading window's text draw as the original lays it out (ledger WR17–WR23): a text is broken at a rectangle's width with 0x535C40 and drawn by 0x535570 line by line at the
/// font's line height (the height of the space glyph) while the next line fits the rectangle's height; the fit test 0x56C800 measures the whole text (0x535390: its lines at that height) and,
/// when it is too tall, draws the longest prefix that ends at a space or a line feed and still fits; a book's page (0x56CB60) and the newspaper's columns (0x56C050) flow messages one under
/// the other with it. Godot-free; the fonts are asked for by art number.
/// </summary>
public static class AreaReadingFlow
{
    /// <summary>One run of the fit test: whether the whole text fitted, how many pixels of height it took (whole) or how many characters were drawn (not whole), and the lines.</summary>
    public readonly record struct Fit(bool Whole, int Amount, IReadOnlyList<AreaReadingLine> Lines);

    /// <summary>0x535C40: the index of the character a line from <paramref name="start"/> ends at (drawn with it) and the line's width (its characters' advances up to that character, a line
    /// feed, a space break and the end excluded), the text's length when the rest fits, or −1 when it passes the width with no break.</summary>
    private static (int End, int Width) Break(AreaArtFont font, string text, int start, int maxWidth)
    {
        var x = 0;
        var candidate = -1;
        var candidateWidth = 0;
        for (var i = start; i < text.Length; i++)
        {
            var character = text[i];
            if (character == '\n') return (i, x);
            var (advance, glyphWidth) = Metrics(font, character);
            if (character == ' ') { candidate = i; candidateWidth = x; }
            if (x + (i + 1 < text.Length ? advance : glyphWidth) > maxWidth) return (candidate, candidateWidth);
            x += advance;
            if (character == '-') { candidate = i; candidateWidth = x; }
        }
        return (text.Length, x);
    }

    // PROJECT (as the premade browser's, MW26): a tab byte advances 20 with no width; a character without a glyph is skipped.
    private static (int Advance, int Width) Metrics(AreaArtFont font, char character) =>
        character == '\t' ? (AreaPremadeBrowserArtLayout.TabAdvance, 0) : font.Glyph(character) is { } glyph ? (glyph.Advance, glyph.Width) : (0, 0);

    /// <summary>0x535390 with a width: the height of the text broken at <paramref name="width"/>, its lines at the font's line height; a text that ends in a line feed counts one more (empty) line;
    /// an empty text, a font with no line height or a word wider than the width gives 0.</summary>
    public static int Height(AreaArtFont font, string text, int width)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0 || font.LineHeight <= 0) return 0;
        var count = 1;
        var at = 0;
        for (var guard = 0; guard <= text.Length + 1; guard++)
        {
            var (end, _) = Break(font, text, at, width);
            if (end < 0) return 0;
            if (end >= text.Length) return count * font.LineHeight;
            at = end + 1;
            count++;
        }
        return 0;
    }

    /// <summary>0x535570: the lines of a text in <paramref name="rect"/>, from its top, <paramref name="centred"/> each centred in the width (the font flag 0x10), while the rectangle's height holds one more
    /// line; a text that passes the width with no break stops the draw.</summary>
    public static IReadOnlyList<AreaReadingLine> Draw(AreaArtFont font, int fontArt, string text, AreaReadingRect rect, bool centred)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(text);
        var height = font.LineHeight;
        var lines = new List<AreaReadingLine>();
        if (height <= 0 || height > rect.Height || text.Length == 0) return lines;
        var rows = rect.Height / height;
        var start = 0;
        while (start < text.Length && lines.Count < rows)
        {
            var (end, width) = Break(font, text, start, rect.Width);
            if (end < 0) break;
            var glyphs = new List<(int, AreaArtGlyph)>();
            var x = 0;
            for (var i = start; i <= end && i < text.Length; i++)
            {
                if (text[i] == '\t') { x += AreaPremadeBrowserArtLayout.TabAdvance; continue; }
                if (font.Glyph(text[i]) is not { } glyph) continue;
                glyphs.Add((x, glyph));
                x += glyph.Advance;
            }
            lines.Add(new(fontArt, rect.X + (centred ? (rect.Width - width) / 2 : 0), rect.Y + lines.Count * height, glyphs));
            start = end + 1;
        }
        return lines;
    }

    /// <summary>0x56C800: the fit test. The whole text when its height fits <paramref name="rect"/> (<see cref="Fit.Amount"/> the height); else the longest prefix that ends after a space or a line
    /// feed (every character is tried, the last included: the loop's bound is the length) whose height, measured without that break character, still fits (<see cref="Fit.Amount"/> the characters drawn, 0 when no break fits).</summary>
    public static Fit FitText(AreaArtFont font, int fontArt, string text, bool centred, AreaReadingRect rect)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(text);
        var whole = Height(font, text, rect.Width);
        if (rect.Height >= whole) return new(true, whole, Draw(font, fontArt, text, rect, centred));
        var cut = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not (' ' or '\n')) continue;
            if (rect.Height < Height(font, text[..i], rect.Width)) break;
            cut = i + 1;
        }
        return new(false, cut, cut > 0 ? Draw(font, fontArt, text[..cut], rect, centred) : []);
    }

    /// <summary>0x56C630: one line of header text at a point (alignment 0: starts at x, 1: centred on x, 2: ends at x), its width the text's own.</summary>
    public static AreaReadingLine? HeaderLine(AreaArtFont font, int fontArt, string text, AreaReadingHeaderSlot slot)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(text);
        var glyphs = font.Layout(text);
        if (glyphs.Count == 0) return null;
        var width = font.Measure(text).Width;
        var x = slot.Align switch { 0 => slot.X, 1 => slot.X - width / 2, _ => slot.X - width };
        return new(fontArt, x, slot.Y, glyphs);
    }

    /// <summary>0x56CB60 (a page of a book, a plaque): the messages from (<paramref name="start"/>) flow one under the other in <paramref name="rect"/>; a message that does not fit is cut at its
    /// last fitting break and the page ends, the text going on at that point on the next page; a page that took every message has no next.</summary>
    public static AreaReadingPage Page(IReadOnlyList<AreaReadingMessage> messages, (int Index, int Offset) start, AreaReadingRect rect, Func<int, AreaArtFont> fontOf)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(fontOf);
        var lines = new List<AreaReadingLine>();
        var (index, offset) = start;
        var (y, height) = (rect.Y, rect.Height);
        while (index < messages.Count)
        {
            var message = messages[index];
            var text = offset < message.Text.Length ? message.Text[offset..] : "";
            var fit = FitText(fontOf(message.Font), message.Font, text, message.Centred, new(rect.X, y, rect.Width, height));
            lines.AddRange(fit.Lines);
            if (!fit.Whole) return new(lines, (index, offset + fit.Amount));
            y += fit.Amount; height -= fit.Amount;
            index++; offset = 0;
        }
        return new(lines, null);
    }

    /// <summary>The pages of a book (0x56CAA0 registers each next page's start while it draws the one before): left, right, left, … up to <see cref="AreaReadingArtLayout.MaxPages"/>.</summary>
    public static IReadOnlyList<AreaReadingPage> BookPages(IReadOnlyList<AreaReadingMessage> messages, Func<int, AreaArtFont> fontOf)
    {
        var pages = new List<AreaReadingPage>();
        var start = (0, 0);
        while (pages.Count < AreaReadingArtLayout.MaxPages)
        {
            var page = Page(messages, start, AreaReadingArtLayout.BookPages[pages.Count % 2], fontOf);
            pages.Add(page);
            if (page.Next is not { } next) break;
            start = next;
        }
        return pages;
    }

    /// <summary>The newspaper (0x56C050, case 2): the masthead's six lines, the headline (the first message) in its rectangle, then the next messages and the fillers flowing down the four columns;
    /// a message too tall for a column goes on at the top of the next, and the draw ends when the last column is full. The fillers follow only when a second message exists.</summary>
    public static IReadOnlyList<AreaReadingLine> Newspaper(AreaReadingArtDocument document, Func<int, AreaArtFont> fontOf)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(fontOf);
        var lines = new List<AreaReadingLine>();
        var slots = document.Alternate ? AreaReadingArtLayout.AlternateMasthead : AreaReadingArtLayout.Masthead;
        for (var i = 0; i < slots.Count && i < document.Header.Count; i++)
        {
            var font = document.Header[i].Font > 0 ? document.Header[i].Font : slots[i].Font;
            if (HeaderLine(fontOf(font), font, document.Header[i].Text, slots[i]) is { } line) lines.Add(line);
        }
        if (document.Body.Count == 0) return lines;
        var headline = document.Body[0];
        lines.AddRange(FitText(fontOf(headline.Font), headline.Font, headline.Text, headline.Centred, AreaReadingArtLayout.Headline).Lines);
        if (document.Body.Count < 2) return lines;
        var column = 0;
        var rect = AreaReadingArtLayout.Columns[0];
        bool Flow(AreaReadingMessage message)
        {
            var text = message.Text;
            var font = fontOf(message.Font);
            while (true)
            {
                var fit = FitText(font, message.Font, text, message.Centred, rect);
                lines.AddRange(fit.Lines);
                if (fit.Whole)
                {
                    rect = rect with { Y = rect.Y + fit.Amount, Height = rect.Height - fit.Amount };
                    return true;
                }
                if (++column >= AreaReadingArtLayout.Columns.Count) return false;
                rect = AreaReadingArtLayout.Columns[column];
                text = text[Math.Min(fit.Amount, text.Length)..];
            }
        }
        for (var i = 1; i < document.Body.Count; i++)
            if (!Flow(document.Body[i])) return lines;
        foreach (var filler in document.Fillers)
            if (!Flow(filler)) break;
        return lines;
    }

    /// <summary>The telegram (0x56C050, case 3): the eleven form lines at their points, message 1011 in its rectangle and the joined body in its own.</summary>
    public static IReadOnlyList<AreaReadingLine> Telegram(AreaReadingArtDocument document, Func<int, AreaArtFont> fontOf)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(fontOf);
        var lines = new List<AreaReadingLine>();
        var form = AreaReadingArtLayout.TelegramForm;
        for (var i = 0; i < form.Count && i < document.Header.Count; i++)
            if (HeaderLine(fontOf(form[i].Font), form[i].Font, document.Header[i].Text, form[i]) is { } line) lines.Add(line);
        if (document.Header.Count > form.Count)
            lines.AddRange(Draw(fontOf(AreaReadingArtLayout.TelegramFormFont), AreaReadingArtLayout.TelegramFormFont, document.Header[form.Count].Text, AreaReadingArtLayout.TelegramLastForm, false));
        lines.AddRange(Draw(fontOf(AreaReadingArtLayout.TelegramBodyFont), AreaReadingArtLayout.TelegramBodyFont, document.Joined, AreaReadingArtLayout.TelegramBody, false));
        return lines;
    }

    /// <summary>The text of a note, a telegram, a newspaper or a plaque (not a book, which has pages, nor a picture, which has none).</summary>
    public static IReadOnlyList<AreaReadingLine> Single(AreaReadingArtDocument document, Func<int, AreaArtFont> fontOf)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(fontOf);
        return document.Subtype switch
        {
            WrittenSubtype.Note => Draw(fontOf(AreaReadingArtLayout.NoteFont), AreaReadingArtLayout.NoteFont, document.Joined, AreaReadingArtLayout.NoteText, false),
            WrittenSubtype.Telegram => Telegram(document, fontOf),
            WrittenSubtype.Newspaper => Newspaper(document, fontOf),
            WrittenSubtype.Plaque => Page(document.Body, (0, 0), AreaReadingArtLayout.PlaqueText, fontOf).Lines,
            _ => [],
        };
    }
}

/// <summary>ARC-UI-1877: what the original-look reading window draws from the game folder: its interface art and its fonts.</summary>
public sealed record AreaReadingArtContent(IReadOnlyDictionary<int, AreaInterfaceArtLoad> Art, IReadOnlyDictionary<int, AreaInterfaceArtLoad> Fonts, string Detail)
{
    /// <summary>True when every required art and font read: the window then shows the original look (all or nothing).</summary>
    public bool Ready => AreaReadingArtLayout.RequiredArtNumbers.All(number => Loaded(Art, number) || Loaded(Fonts, number));

    private static bool Loaded(IReadOnlyDictionary<int, AreaInterfaceArtLoad> loads, int number) =>
        loads.TryGetValue(number, out var load) && load.Status == AreaInterfaceArtStatus.Ready && load.Frames.Count > 0;

    public static AreaReadingArtContent Empty(string reason) => new(new Dictionary<int, AreaInterfaceArtLoad>(), new Dictionary<int, AreaInterfaceArtLoad>(), reason);

    /// <summary>Reads the window's art and fonts from the game folder of <paramref name="modulePath"/> (the fonts: the fixed ones and those the written texts' markup names; the frame
    /// limit is raised to 256 for their frames); failures are reported, never thrown.</summary>
    public static AreaReadingArtContent Load(string modulePath)
    {
        ArgumentNullException.ThrowIfNull(modulePath);
        var found = AreaContentRoot.Discover(modulePath);
        if (found.Status != AreaContentRootStatus.Ready) return Empty($"not read ({found.Status} {found.Detail})");
        var opened = ContentStack.OpenShared(found.Plan!);
        if (opened.Status != ContentStackStatus.Ready) return Empty($"not read (content stack {opened.Status})");
        using var stack = opened.Stack!;
        var markup = WrittenTexts.Open(stack).MarkupFonts();
        var fontNumbers = AreaReadingArtLayout.FontArts(markup);
        var art = AreaInterfaceArt.Load(stack, new[] { AreaReadingArtLayout.FrameArt, AreaReadingArtLayout.LensArt, AreaReadingArtLayout.PreviousArt, AreaReadingArtLayout.NextArt }
            .Concat(AreaReadingArtLayout.BackgroundArts));
        var fonts = AreaInterfaceArt.Load(stack, fontNumbers, new AreaInterfaceArtLimits(MaxFrames: 256));
        var failed = art.Values.Concat(fonts.Values).Where(load => load.Status != AreaInterfaceArtStatus.Ready).Select(load => $"{load.Number} {load.Status} {load.Detail}").ToArray();
        return new(art, fonts, $"art {art.Count + fonts.Count - failed.Length}/{art.Count + fonts.Count} read, fonts {string.Join(",", fontNumbers)}{(failed.Length == 0 ? "" : " (" + string.Join("; ", failed) + ")")}");
    }

    /// <summary>Reads one more interface art (a picture's, the written item's own art number) from the game folder; null when it did not read.</summary>
    public static AreaInterfaceArtLoad? LoadPicture(string modulePath, int number)
    {
        ArgumentNullException.ThrowIfNull(modulePath);
        var found = AreaContentRoot.Discover(modulePath);
        if (found.Status != AreaContentRootStatus.Ready) return null;
        var opened = ContentStack.OpenShared(found.Plan!);
        if (opened.Status != ContentStackStatus.Ready) return null;
        using var stack = opened.Stack!;
        var loads = AreaInterfaceArt.Load(stack, [number]);
        return loads.TryGetValue(number, out var load) && load.Status == AreaInterfaceArtStatus.Ready && load.Frames.Count > 0 ? load : null;
    }
}
