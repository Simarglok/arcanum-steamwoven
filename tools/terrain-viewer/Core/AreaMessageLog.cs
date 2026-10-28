namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// The original's message-log entry types the viewer posts (the entry's first field, ledger ML2–ML5): 0 the level-up entry, 4 a game
/// message with sound 3004, 5 the same text layout without a sound, 6 a hover text (a bar button's name), which is shown but never
/// stored. The other types (1–3 and 7–13) are listed in the ledger and not posted by the viewer.
/// </summary>
public enum AreaMessageKind { LevelUp = 0, Message = 4, Plain = 5, Hover = 6 }

/// <summary>
/// One message-log entry. A level-up entry keeps its three lines as composed (title, level line, points or scheme line; <paramref
/// name="Scheme"/> when the last one is the scheme text, which gets two lines); a text entry (message, plain, hover) keeps its whole text
/// in <paramref name="Title"/>, split at its first line break when drawn (ML4).
/// </summary>
public sealed record AreaMessageEntry(AreaMessageKind Kind, string Title, string Line = "", string Detail = "", bool Scheme = false)
{
    public static AreaMessageEntry Text(AreaMessageKind kind, string text) =>
        kind == AreaMessageKind.LevelUp ? throw new ArgumentException("a level-up entry has lines", nameof(kind)) : new(kind, text ?? throw new ArgumentNullException(nameof(text)));

    /// <summary>Hover texts are shown, never stored (the original's types 6–12, ML2).</summary>
    public bool Stored => Kind != AreaMessageKind.Hover;
}

/// <summary>What one post did: whether the entry went into the log, whether the window now shows it (or the entry it repeats), and the
/// sound the drawing played (null none).</summary>
public sealed record AreaMessagePost(bool Stored, bool Shown, int? Sound);

/// <summary>
/// ARC-UI-023: the message log of the original's bottom strip (checked in the 1.0.7.4 executable, ledger ML1–ML8), Godot-free. The
/// last <see cref="Capacity"/> stored entries are kept in a ring (0x552770): a new one becomes the newest and the shown one, and its
/// drawing plays its sound; a message or plain text equal to the newest entry's (ASCII letters compared without case, as the C
/// runtime's <c>_stricmp</c> 0x58ADB0 does) is not stored again, the newest is shown and drawn with its sound once more. A hover text is drawn only in the message mode and only when no entry was stored in the last
/// <see cref="QuietMilliseconds"/> (real time). The upper half of the window shows the next older entry, the lower half the next newer
/// one (0x5528E0, 0x552930). A new stored entry puts the window back in the message mode even while the spells or skills pane owns it
/// (0x552960); the next pointer move over the bottom strip gives the window back to the pane whose button is still down, and a move
/// above the strip takes it from the spells or skills pane back to the message mode (0x54B70B, 0x54B7C0).
/// The original leaves the window blank after a hover text is cleared and after the window returns to the message mode; Steamwoven
/// draws the shown entry again there in both rule modes (a technical display bug, ML7). Caller-serialized.
/// </summary>
public sealed class AreaMessageLog
{
    public const int Capacity = 10;
    public const int QuietMilliseconds = 3000;
    /// <summary>The original copies at most 199 characters of an entry's text into its slot (strncpy of 200, last byte zeroed): a
    /// text entry's text, a level-up entry's scheme text.</summary>
    public const int MaxTextLength = 199;
    /// <summary>The pointer area that gives the window back to a pane (0x54B70B): the bottom strip from y 441 to 590, x 10 to 790.</summary>
    public const int PaneReturnTop = 441, PaneReturnBottom = 590, PaneReturnLeft = 10, PaneReturnRight = 790;

    private readonly AreaMessageEntry?[] _ring = new AreaMessageEntry?[Capacity];
    private int _count, _head, _view;
    private long? _lastStored;

    /// <summary>True while a pane (spells, skills, the weapon's spells) owns the window: nothing of the log is drawn.</summary>
    public bool PaneMode { get; private set; }
    /// <summary>What the window draws now: a stored entry, a hover text, or null (blank).</summary>
    public AreaMessageEntry? Shown { get; private set; }
    public int Count => _count;

    /// <summary>The entry the scroll position is at (null with an empty log).</summary>
    public AreaMessageEntry? Viewed => _count == 0 ? null : _ring[_view];

    /// <summary>The stored entries, oldest first.</summary>
    public IReadOnlyList<AreaMessageEntry> History =>
        Enumerable.Range(0, _count).Select(i => _ring[(_head - _count + 1 + i + Capacity) % Capacity]!).ToArray();

    /// <summary>The sound the drawing of an entry plays (ML5): 3005 a level-up entry, 3004 a message; none for the others.</summary>
    public static int? Sound(AreaMessageKind kind) => kind switch { AreaMessageKind.LevelUp => 3005, AreaMessageKind.Message => 3004, _ => null };

    /// <summary>Posts an entry at <paramref name="nowMilliseconds"/> (a real-time clock).</summary>
    public AreaMessagePost Post(AreaMessageEntry entry, long nowMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!entry.Stored)
        {
            // 0x552770: shown only in the message mode and more than 3 s after the last stored entry; the scroll position stays.
            if (PaneMode || (_lastStored is { } last && nowMilliseconds - last <= QuietMilliseconds)) return new(false, false, null);
            Shown = entry;
            return new(false, true, null);
        }
        _lastStored = nowMilliseconds;
        if (_count != 0 && entry.Kind is AreaMessageKind.Message or AreaMessageKind.Plain && SameText(entry.Title, TextField(_ring[_head]!)))
        {
            // The repeat is not stored: the newest entry is shown and drawn with its sound again.
            _view = _head;
            return Redraw(sound: true);
        }
        _head = _count < Capacity ? _count : (_head + 1) % Capacity;
        if (_count < Capacity) _count++;
        _ring[_head] = Cut(entry);
        _view = _head;
        return Redraw(sound: true);
    }

    /// <summary>The upper half of the window was clicked (0x5528E0): the next older entry, at most as far back as the log holds.</summary>
    public bool Older()
    {
        if (PaneMode) return false;
        var older = (_view + Capacity - 1) % Capacity;
        if (older != _head && (_head - older + Capacity) % Capacity < _count) _view = older;
        return Redraw(sound: false).Shown;
    }

    /// <summary>The lower half of the window was clicked (0x552930): the next newer entry, up to the newest.</summary>
    public bool Newer()
    {
        if (PaneMode) return false;
        if (_view != _head) _view = (_view + 1) % Capacity;
        return Redraw(sound: false).Shown;
    }

    /// <summary>The pointer left a bar button: its hover text goes. The original blanks the window (0x550720); Steamwoven shows the
    /// scroll position's entry again (ML7).</summary>
    public void ClearHover()
    {
        if (PaneMode) return;
        Shown = Viewed;
    }

    /// <summary>A pane took the window (its button went down): nothing of the log is drawn.</summary>
    public void EnterPane() { PaneMode = true; Shown = null; }

    /// <summary>The window went back to the message mode (the pane's button went up). The original draws only the mode's background;
    /// Steamwoven shows the scroll position's entry again (ML7).</summary>
    public void EnterMessages()
    {
        if (!PaneMode) return;
        PaneMode = false;
        Shown = Viewed;
    }

    /// <summary>A pointer move at (<paramref name="x"/>, <paramref name="y"/>) on the 800×600 screen: inside the bottom strip's area a
    /// pane whose button is still down takes the window back from the message mode (0x54B70B); above the strip the spells or skills
    /// pane gives the window to the message mode (0x54B7C0; not the weapon's spells, <paramref name="weaponPane"/>). True when the
    /// window changed.</summary>
    public bool PointerMoved(int x, int y, bool paneButtonDown, bool weaponPane = false)
    {
        if (y < PaneReturnTop)
        {
            if (!PaneMode || weaponPane) return false;
            EnterMessages();
            return true;
        }
        if (PaneMode || !paneButtonDown || y > PaneReturnBottom || x < PaneReturnLeft || x > PaneReturnRight) return false;
        EnterPane();
        return true;
    }

    // The entry's text field (+4), copied with at most 199 characters: a text entry's text, a level-up entry's scheme text (empty
    // with points, ML1). The level-up title and lines are the draw's own messages, not stored.
    private static string TextField(AreaMessageEntry entry) => entry.Kind != AreaMessageKind.LevelUp ? entry.Title : entry.Scheme ? entry.Detail : "";

    private static AreaMessageEntry Cut(AreaMessageEntry entry) => entry.Kind != AreaMessageKind.LevelUp
        ? entry.Title.Length > MaxTextLength ? entry with { Title = entry.Title[..MaxTextLength] } : entry
        : entry.Scheme && entry.Detail.Length > MaxTextLength ? entry with { Detail = entry.Detail[..MaxTextLength] } : entry;

    // 0x58ADB0 in the C locale: A–Z fold to a–z, every other byte compares as it is.
    private static bool SameText(string a, string b)
    {
        if (a.Length != b.Length) return false;
        for (var i = 0; i < a.Length; i++)
        {
            char Fold(char c) => c is >= 'A' and <= 'Z' ? (char)(c + 32) : c;
            if (Fold(a[i]) != Fold(b[i])) return false;
        }
        return true;
    }

    /// <summary>Empties the log (another session).</summary>
    public void Clear()
    {
        Array.Clear(_ring);
        _count = _head = _view = 0;
        _lastStored = null;
        Shown = null;
    }

    // 0x552960: the window goes to the message mode and draws the scroll position's entry (nothing with an empty log).
    private AreaMessagePost Redraw(bool sound)
    {
        PaneMode = false;
        if (_count == 0) return new(true, false, null);
        Shown = _ring[_view]!;
        return new(true, true, sound ? Sound(Shown.Kind) : null);
    }
}

/// <summary>The two fonts of the message window (ML6): the title font (font art 27) and the text font (font art 229), both white.</summary>
public enum AreaMessageFont { Title, Text }

/// <param name="Rect">On the original's 800×600 screen.</param>
/// <param name="Truncate">Characters are cut from the end until the text fits (the wide layout).</param>
public sealed record AreaMessageText((int X, int Y, int Width, int Height) Rect, AreaMessageFont Font, string Text, bool Truncate);

/// <summary>One way to draw an entry: its icon art (null none) and its texts. <paramref name="Widens"/>: when the last text does not
/// fit, the entry is drawn again in its wide layout.</summary>
public sealed record AreaMessageDraw(int? Icon, IReadOnlyList<AreaMessageText> Texts, bool Widens);

/// <summary>
/// ARC-UI-023: where the original draws the message window and its entries (0x5529C0 and its tables, ledger ML4–ML6), on the 800×600
/// screen. Godot-free.
/// </summary>
public static class AreaMessageLayout
{
    /// <summary>The message mode's background art (bar mode 0 of 0x5503F0, table 0x5C6548), drawn at (196, 492).</summary>
    public const int BackgroundArt = 354, BackgroundX = 196, BackgroundY = 492;
    /// <summary>The icon of an entry, drawn at (217, 510) (0x554560).</summary>
    public const int IconX = 217, IconY = 510;
    /// <summary>The message mode's text area (0x5C6D60), which a clear fills with black.</summary>
    public static readonly (int X, int Y, int Width, int Height) Window = (211, 503, 383, 82);
    /// <summary>The two invisible scroll areas (0x5C65F8, 0x5C6608; 382 × 41, no art, no sound): the upper shows an older entry.</summary>
    public static readonly (int X, int Y, int Width, int Height) OlderArea = (210, 504, 382, 41), NewerArea = (210, 545, 382, 41);

    private static readonly (int, int, int, int) Title = (290, 504, 291, 19), Second = (290, 528, 291, 18), Third = (290, 546, 291, 18),
        Whole = (290, 504, 291, 78), SchemeBlock = (290, 546, 291, 36), Body = (290, 528, 291, 54),
        WideTitle = (217, 504, 364, 19), WideWhole = (217, 504, 364, 78), WideBody = (217, 528, 364, 54);

    /// <summary>The icon arts by entry type (0x5C708C).</summary>
    public static int Icon(AreaMessageKind kind) => kind switch
    {
        AreaMessageKind.LevelUp => 438, AreaMessageKind.Message => 442, AreaMessageKind.Plain => 443, _ => 444,
    };

    /// <summary>Every interface art the message window draws.</summary>
    public static IReadOnlyList<int> ArtNumbers { get; } =
        [BackgroundArt, .. Enum.GetValues<AreaMessageKind>().Select(Icon).Distinct()];

    /// <summary>The entry's drawing; <paramref name="wide"/> the second layout of a text entry whose text did not fit.</summary>
    public static AreaMessageDraw Draw(AreaMessageEntry entry, bool wide = false)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Kind == AreaMessageKind.LevelUp)
            return new(Icon(entry.Kind), [new(Title, AreaMessageFont.Title, entry.Title, false), new(Second, AreaMessageFont.Text, entry.Line, false),
                new(entry.Scheme ? SchemeBlock : Third, AreaMessageFont.Text, entry.Detail, false)], false);
        var cut = entry.Title.IndexOf('\n');
        var (head, rest) = cut < 0 ? (entry.Title, (string?)null) : (entry.Title[..cut], entry.Title[(cut + 1)..]);
        if (wide)
            return new(null, rest is null ? [new(WideWhole, AreaMessageFont.Text, head, true)]
                : [new(WideTitle, AreaMessageFont.Title, head, true), new(WideBody, AreaMessageFont.Text, rest, true)], false);
        return new(Icon(entry.Kind), rest is null ? [new(Whole, AreaMessageFont.Text, head, false)]
            : [new(Title, AreaMessageFont.Title, head, false), new(Body, AreaMessageFont.Text, rest, false)], true);
    }
}
