namespace Arcanum.Presentation.Hud;

/// <summary>The three buttons of the map note entry (table 0x5C6CA8): Done accepts, Clear empties the text, Cancel restores the text the entry opened with.</summary>
public enum MapNoteAction { Done, Clear, Cancel }

/// <summary>One of the entry's buttons: its action, art number and place on the 800 × 600 screen.</summary>
public readonly record struct MapNoteButton(MapNoteAction Action, int Art, int X, int Y);

/// <summary>One marker of the radio group: the note type it picks (a row of the note marker table 0x5C9160), its art number and place on the 800 × 600 screen.</summary>
public readonly record struct MapNoteMarkerButton(int Type, int Art, int X, int Y);

/// <summary>
/// ARC-UI-2306: the original's map note entry, bottom strip bar mode 6 (ledger BM0, BM4; art 200 <c>MpWt_Rot</c>): a text entry of up to 50 characters (record 0x5C9B20,
/// no character filter, edit as 0x566F00 like the order line's) in the rectangle (262, 508, 303 × 24) of the strip, three push buttons (Done 203, Clear 201, Cancel 202)
/// and a radio group of the note marker (146 note, 148 skull, 145 chest). Opened on a note's text (0x564030, which keeps a copy for Cancel); Enter and Done accept
/// (0x5640C0), Clear empties the text and keeps the entry bound (0x5672A0), Cancel puts the opened text back and keeps it bound (0x5672D0). The radio group's choice
/// (0x564000, the global 0x66D89C, first entry at the start) is read when a new note is made and persists between entries: choosing while an entry is open changes the next
/// new note, not this one. The note list's side of it (add, delete, flags) belongs to the host. Engine-free.
/// </summary>
public sealed class MapNoteEntry
{
    /// <summary>The bar mode (table 0x5C6548 entry 6).</summary>
    public const int BarMode = 6;
    /// <summary>The mode's background (art 200 <c>MpWt_Rot</c>), at the place every strip pane uses (196, 492).</summary>
    public const int BackgroundArt = 200, BackgroundX = 196, BackgroundY = 492;
    /// <summary>The text rectangle (table 0x5C6D60 row 6): left, top, width, height on the 800 × 600 screen.</summary>
    public const int TextX = 262, TextY = 508, TextWidth = 303, TextHeight = 24;
    /// <summary>The record's maximum text length (typing stops at 49 characters).</summary>
    public const int MaxLength = 50;
    /// <summary>The caret mark (string 0x5C286C) and its offset from the end of the text (0x5521B0: x + 3 + text width).</summary>
    public const string CaretMark = "*";
    public const int CaretOffset = 3;
    /// <summary>The text colour: the first note marker font's (table 0x5C9170 row 0, white), whatever marker is chosen.</summary>
    public static readonly (byte R, byte G, byte B) TextColour = (255, 255, 255);

    /// <summary>The buttons (table 0x5C6CA8: Done 203 (281, 549) → 0x567320, Clear 201 (396, 550) → 0x5672A0, Cancel 202 (511, 550) → 0x5672D0).</summary>
    public static IReadOnlyList<MapNoteButton> Buttons { get; } =
        [new(MapNoteAction.Done, 203, 281, 549), new(MapNoteAction.Clear, 201, 396, 550), new(MapNoteAction.Cancel, 202, 511, 550)];

    /// <summary>The radio group (0x538670): the type picked is the row of the note marker table 0x5C9160 (0 note, 1 skull, 2 chest).</summary>
    public static IReadOnlyList<MapNoteMarkerButton> Markers { get; } =
        [new(0, 146, 213, 509), new(1, 148, 213, 536), new(2, 145, 213, 563)];

    /// <summary>The arts the entry needs read with the bar's arts.</summary>
    public static IReadOnlyList<int> ArtNumbers { get; } = [BackgroundArt, .. Buttons.Select(b => b.Art), .. Markers.Select(m => m.Art)];

    private readonly StripTextEntry _entry = new(MaxLength);
    private string _saved = "";
    private int _marker;
    private int _revision;

    /// <summary>Whether the entry is open (bound, 0x566EF0).</summary>
    public bool IsOpen { get; private set; }

    /// <summary>The text.</summary>
    public string Text => _entry.Text;

    /// <summary>The text the entry opened with (the copy 0x566E50 keeps for Cancel).</summary>
    public string Saved => _saved;

    /// <summary>The caret's position.</summary>
    public int Caret => _entry.Caret;

    /// <summary>The radio group's selected marker type (0 at the start; it persists between entries).</summary>
    public int Marker => _marker;

    /// <summary>A counter that grows with each change the strip shows (text, caret, marker, opening and closing), so the host redraws only then.</summary>
    public int Revision => _revision + _entry.Revision;

    /// <summary>Opens the entry on <paramref name="text"/> (0x564030): the text is bound with the caret at its end and kept for Cancel.</summary>
    [OriginalFunction(0x564030, OriginalFunctionScope.Partial)]
    public void Open(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _saved = text; _entry.Bind(text); IsOpen = true; _revision++;
    }

    /// <summary>Unbinds the entry and drops the text (0x564070 and the closer that runs on a bar mode change). Returns whether it was open.</summary>
    [OriginalFunction(0x564070, OriginalFunctionScope.Partial)]
    public bool Close()
    {
        var was = IsOpen;
        _entry.Reset(); _saved = ""; IsOpen = false;
        if (was) _revision++;
        return was;
    }

    /// <summary>A typed character (the entry's character event). Returns whether the text changed.</summary>
    public bool Type(char c) => IsOpen && _entry.Type(c);

    /// <summary>An edit key (the entry's key event). Returns whether anything changed.</summary>
    public bool Press(StripEntryKey key) => IsOpen && _entry.Press(key);

    /// <summary>The Clear button (0x5672A0): the text is emptied and the entry stays open. Returns whether anything changed.</summary>
    [OriginalFunction(0x5672A0, OriginalFunctionScope.Full)]
    public bool Clear()
    {
        if (!IsOpen) return false;
        var before = _entry.Revision;
        _entry.Clear();
        return _entry.Revision != before;
    }

    /// <summary>The Cancel button (0x5672D0): the text the entry opened with is put back and the entry stays open (the note and the mode are untouched). Returns whether anything changed.</summary>
    [OriginalFunction(0x5672D0, OriginalFunctionScope.Partial)]
    public bool Cancel()
    {
        if (!IsOpen) return false;
        var before = _entry.Revision;
        _entry.Replace(_saved);
        return _entry.Revision != before;
    }

    /// <summary>The radio group's choice (0x564000): the type a new note takes from now on.</summary>
    [OriginalFunction(0x564000, OriginalFunctionScope.Full)]
    public bool Select(int type)
    {
        if (type < 0 || type >= Markers.Count || type == _marker) return false;
        _marker = type; _revision++;
        return true;
    }
}
