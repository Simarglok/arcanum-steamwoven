namespace Arcanum.TerrainViewer.Core;

/// <summary>One note of a town map (ledger WM91, WM127): its id, flags, picture pixel, type and text. The original's record has a label rectangle too (four ints,
/// zero for every note that can exist) and 0x130 bytes; the viewer keeps the fields that matter.</summary>
public sealed record AreaTownNote(int Id, int Flags, int PixelX, int PixelY, int Type, string Text);

/// <summary>
/// ARC-UI-103 (ledger WM79, WM91, WM93, WM127, WM128): the notes of one town map, the original's list 0x65E978. A note is added only by hovering a scenery with the
/// town-map flag (<see cref="MarkedByScenery"/>, WM127; the note quill that would add others is unreachable in the retail executable, WM126). ARC-UI-110: the same list type serves the map view and the continent view of the world map (the Steamwoven quill's notes at world-map picture pixels, WM132). Ids start at
/// <see cref="FirstId"/>; at most <see cref="Capacity"/> notes (0x564160 refuses at 199, WM128). A note sits at the picture pixel of the object, is of type 3
/// (question) and carries the object's name; one already within the marker hit box of that pixel stops the add. Godot-free.
/// </summary>
public sealed class AreaTownNotes
{
    public const int Capacity = 199, FirstId = 200, QuestionType = 3, DefaultFlags = 2;
    /// <summary>The note's text buffer is 250 bytes (WM91): at most 249 characters with the terminator.</summary>
    public const int MaxTextLength = 249;
    /// <summary>The scenery flag that marks the town map (bit 0x8 of the scenery flags, WM127).</summary>
    public const uint MarkedByScenery = 0x8;

    private readonly List<AreaTownNote> _items = [];

    public IReadOnlyList<AreaTownNote> Items => _items;
    public int Count => _items.Count;
    /// <summary>The id the next note takes (the file's second int, WM91; 200 for a town without notes).</summary>
    public int NextId { get; private set; } = FirstId;

    /// <summary>Restores a list read from storage (the town's saved notes); the next id is kept and never lower than the highest id plus one.</summary>
    public static AreaTownNotes Restore(IEnumerable<AreaTownNote> notes, int nextId)
    {
        ArgumentNullException.ThrowIfNull(notes);
        var list = new AreaTownNotes();
        foreach (var note in notes.Take(Capacity)) list._items.Add(note with { Text = Limit(note.Text) });
        list.NextId = Math.Max(nextId, list._items.Count == 0 ? FirstId : list._items.Max(n => n.Id) + 1);
        return list;
    }

    /// <summary>The newest note within ±<paramref name="half"/> of a picture pixel (WM58: the newest wins an overlap), or -1.</summary>
    public int Hit(long pictureX, long pictureY, int half = AreaWorldMapArtLayout.HoverHalf) =>
        AreaWorldMapArtLayout.HitMarker([.. _items.Select(n => ((long)n.PixelX, (long)n.PixelY))], pictureX, pictureY, half);

    /// <summary>
    /// A hovered scenery marks the town map (0x564AF0, WM127): a note with the object's <paramref name="name"/> at the picture pixel of its location, unless a note
    /// already lies within the hit box of that pixel or the list is full. The object must lie inside the town's picture: the original's converter gives (0, 0) for a
    /// location outside it and would put the note at the picture's corner, a presentation glitch not reproduced (no note then). True when a note was added.
    /// </summary>
    public bool AddForObject(AreaTownMapInfo info, long locationX, long locationY, string name)
    {
        ArgumentNullException.ThrowIfNull(info); ArgumentNullException.ThrowIfNull(name);
        if (AreaTownMapLayout.FullPixel(info, locationX, locationY) is null) return false;
        var (x, y) = AreaTownMapLayout.ToPixel(info, locationX, locationY);
        if (Hit(x, y) >= 0) return false;
        return Add(x, y, name);
    }

    /// <summary>True when no further note fits (0x564160 refuses at <see cref="Capacity"/>).</summary>
    public bool IsFull => _items.Count >= Capacity || NextId == int.MaxValue;

    /// <summary>
    /// ARC-UI-106 (WM92, the quill of the Steamwoven rules): text is accepted when it holds more than white space; an empty text deletes the note being edited (0x564210)
    /// and adds none. A new note takes <see cref="QuestionType"/> and <see cref="DefaultFlags"/> (the original's "current note type" 0x66D89C is not read, F0).
    /// </summary>
    public static bool IsNoteText(string? text) => !string.IsNullOrWhiteSpace(text);

    /// <summary>The quill's press on empty ground, accepted with <paramref name="text"/>: a note at the picture pixel. False for an empty text, a full list or a negative pixel (the view checks the picture's size).</summary>
    public bool AddByQuill(int pixelX, int pixelY, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        // Characters the session refuses (control characters and surrogates, emoji included) become blanks; a text of nothing else adds no note.
        var kept = Limit(text).Trim();
        return IsNoteText(text) && IsNoteText(kept) && pixelX >= 0 && pixelY >= 0 && Add(pixelX, pixelY, kept);
    }

    /// <summary>The quill's press on a note, accepted with <paramref name="text"/>: the note keeps its place and takes the text; an empty text deletes it. False when nothing changed.</summary>
    public bool EditByQuill(int index, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (index < 0 || index >= _items.Count) return false;
        if (!IsNoteText(text)) { _items.RemoveAt(index); return true; }
        // A text of characters the session refuses only (an emoji) is no deletion and no edit.
        var kept = Limit(text).Trim();
        if (!IsNoteText(kept) || kept == _items[index].Text) return false;
        _items[index] = _items[index] with { Text = kept };
        return true;
    }

    /// <summary>0x564160: refused at <see cref="Capacity"/>; else the note takes the next id and goes to the end of the list.</summary>
    private bool Add(int pixelX, int pixelY, string text)
    {
        if (_items.Count >= Capacity || NextId == int.MaxValue) return false;
        _items.Add(new(NextId++, DefaultFlags, pixelX, pixelY, QuestionType, Limit(text)));
        return true;
    }

    // The session refuses control characters and unpaired surrogates (schema 81), so a name carrying one gets a blank in its place; the length is cut to the buffer.
    private static string Limit(string text)
    {
        var cut = text.Length <= MaxTextLength ? text : text[..MaxTextLength];
        return cut.Any(c => char.IsControl(c) || char.IsSurrogate(c)) ? string.Concat(cut.Select(c => char.IsControl(c) || char.IsSurrogate(c) ? ' ' : c)) : cut;
    }
}
