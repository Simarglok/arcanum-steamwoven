namespace Arcanum.Presentation.Hud;

/// <summary>The original's four bubble fonts by index (ledger TB4): white, blue, green, red. Single player uses white (anyone's line) and
/// green (a PC's float line); blue and red only come from network packets.</summary>
public enum BubbleColor { White = 0, Blue = 1, Green = 2, Red = 3 }

/// <summary>One bubble's wrapped text: its lines with their left edge inside the bubble's rect, and the rect's size.</summary>
public sealed record TextBubbleLayout(IReadOnlyList<(string Text, int Left)> Lines, int Width, int Height);

/// <summary>
/// ARC-UI-1997: the original's text bubbles over objects (the "TB" module, 0x4D5B80–0x4D67F0, ledger TB1–TB22): a line a script,
/// a follower or a speaker says, drawn over its object's head in the font art 229 with a drop shadow, for "Text Duration" seconds.
/// Engine-free rules; <see cref="TextBubbleField{TKey}"/> keeps the eight entries, the host measures, draws and places.
/// </summary>
public static class TextBubbleRules
{
    /// <summary>The table holds eight bubbles (0x602930, 0x30 bytes each, 0x4D5B80).</summary>
    public const int Capacity = 8;
    /// <summary>A bubble's text is wrapped to a 200 px wide, 200 px high rectangle (0x5B8EB0).</summary>
    public const int BoxSize = 200;
    /// <summary>The life argument of "until the dialog ends" (persistent) and of "the default" (0x4D6160).</summary>
    public const int Persistent = -2, DefaultLife = -1;
    /// <summary>The shadow's offset in pixels (drawn first, in <see cref="ShadowRgb"/>, font flag 0x08) and the shadow colour (0x630D50).</summary>
    public const int ShadowOffset = 1;
    public static readonly (byte R, byte G, byte B) ShadowRgb = (10, 10, 10);
    /// <summary>The bubble's anchor is the object's tile centre: its screen point + (40, 20) (the original adds the object's draw offset too; the viewer does not yet).</summary>
    public const int AnchorX = 40, AnchorY = 20;

    /// <summary>A bubble font's colour (0x5B8EA0).</summary>
    public static (byte R, byte G, byte B) Rgb(BubbleColor color) => color switch
    {
        BubbleColor.White => (255, 255, 255),
        BubbleColor.Blue => (0, 0, 255),
        BubbleColor.Green => (0, 255, 0),
        BubbleColor.Red => (255, 0, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(color)),
    };

    /// <summary>The default life in milliseconds: the "text duration" setting × 1000 (the setting's callback is <see cref="TextBubbleField{TKey}.SetTextDuration"/>).</summary>
    public static long LifeMs(int textDuration)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(textDuration);
        return textDuration * 1000L;
    }

    /// <summary>
    /// The text's lines (the original renders it once into the bubble's own 200 × 200 buffer at creation; the line breaker's word-break detail is
    /// not read: words are broken at spaces, a word wider than the box stands alone): lines are added while the next one fits in the
    /// 200 px height; the rest is cut. Each line is centred in the rect, which is the tight box of the lines plus the shadow's pixel.
    /// </summary>
    public static TextBubbleLayout Layout(string text, int lineHeight, Func<string, int> measure)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(measure);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lineHeight);
        var maxLines = BoxSize / lineHeight;
        var raw = new List<string>();
        foreach (var paragraph in text.Split('\n'))
        {
            var line = "";
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && measure(candidate) > BoxSize) { raw.Add(line); line = word; }
                else line = candidate;
            }
            if (line.Length > 0) raw.Add(line);
        }
        if (raw.Count > maxLines) raw.RemoveRange(maxLines, raw.Count - maxLines);
        var widths = raw.Select(measure).ToArray();
        var wide = widths.Length == 0 ? 0 : widths.Max();
        var lines = new (string Text, int Left)[raw.Count];
        for (var i = 0; i < raw.Count; i++) lines[i] = (raw[i], (wide - widths[i]) / 2);
        return new(lines, wide + ShadowOffset, raw.Count * lineHeight + ShadowOffset);
    }

    // The nine candidate orders of the eight placement slots by the anchor's region (table 0x5B8EC0).
    private static readonly int[][] Orders =
    [
        [0, 7, 6, 5, 4, 1, 2, 3], [0, 1, 7, 2, 6, 3, 5, 4], [0, 1, 2, 3, 4, 7, 6, 5],
        [7, 6, 5, 0, 4, 1, 3, 2], [0, 1, 2, 3, 4, 5, 6, 7], [1, 2, 3, 0, 4, 7, 5, 6],
        [7, 6, 5, 4, 3, 2, 1, 0], [4, 3, 5, 2, 6, 1, 7, 0], [1, 2, 3, 4, 5, 6, 7, 0],
    ];

    /// <summary>The rect's top-left for placement slot 0–7 around the anchor (0x4D6410): above, then clockwise to the upper left.</summary>
    [OriginalFunction(0x4D6410, OriginalFunctionScope.Partial)]
    public static (int Left, int Top) SlotPosition(int slot, int anchorX, int anchorY, int width, int height) => slot switch
    {
        0 => (anchorX - width / 2, anchorY - height - 100),
        1 => (anchorX + 40, anchorY - height / 2 - 55),
        2 => (anchorX + 80, anchorY - height / 2),
        3 => (anchorX + 40, anchorY + height / 2 + 20),
        4 => (anchorX - width / 2, anchorY + 10),
        5 => (anchorX - width - 40, anchorY + height / 2 + 20),
        6 => (anchorX - width - 80, anchorY - height / 2),
        7 => (anchorX - width - 40, anchorY - height / 2 - 55),
        _ => throw new ArgumentOutOfRangeException(nameof(slot)),
    };

    /// <summary>
    /// The placement slot a new bubble takes (0x4D63B0, once, when it is first laid out): the anchor's region (horizontal 0–2 by 380 and 420, plus
    /// three times the vertical 0–2 by 190 and 290, the constants of the original's 800 × 600 view) gives an order of the slots, and the first whose rect
    /// lies wholly in the view wins; none fits: slot 0. The constants stand for the view's centre (400, 300) here: they are taken from the centre of
    /// a view of any size (a project adaptation; at 800 × 600 they are the original's). The slot is kept, so a bubble follows its object without moving.
    /// </summary>
    [OriginalFunction(0x4D63B0, OriginalFunctionScope.Partial)]
    public static int ChooseSlot(int anchorX, int anchorY, int width, int height, int viewLeft, int viewTop, int viewWidth, int viewHeight)
    {
        var cx = viewLeft + viewWidth / 2;
        var cy = viewTop + viewHeight / 2;
        var h = anchorX < cx - 20 ? 0 : anchorX < cx + 20 ? 1 : 2;
        var v = anchorY < cy - 110 ? 0 : anchorY < cy - 10 ? 1 : 2;
        foreach (var slot in Orders[h + 3 * v])
        {
            var (left, top) = SlotPosition(slot, anchorX, anchorY, width, height);
            if (left >= viewLeft && top >= viewTop && left + width <= viewLeft + viewWidth && top + height <= viewTop + viewHeight) return slot;
        }
        return 0;
    }
}

/// <summary>One bubble in the table: its owner, text and layout, colour, whether it never expires, when it was made and its life.</summary>
public sealed record TextBubble<TKey>(TKey Owner, string Text, TextBubbleLayout Layout, BubbleColor Color, bool Persistent, long CreatedMs, long LifeMs, int? Slot);

/// <summary>
/// ARC-UI-1997: the bubble table (0x4D5FE0 create, 0x4D6160 set life, 0x4D62B0 remove, 0x4D5EB0 tick, 0x4D6320 clear all). Eight entries; a
/// new bubble takes its object's own entry, else the highest free one, else (full) the oldest non-persistent entry, the first entry
/// when all are persistent (so a persistent bubble can be evicted, as in the original). A bubble lives the default life
/// ("text duration" × 1000 ms) unless set otherwise and is removed by <see cref="Tick"/> once its age exceeds its life; a persistent one
/// (life −2) does not age. The clock is the real one: nothing pauses a bubble.
/// </summary>
public sealed class TextBubbleField<TKey> where TKey : notnull
{
    private readonly TextBubble<TKey>?[] _entries = new TextBubble<TKey>?[TextBubbleRules.Capacity];
    private long _defaultLifeMs;

    public TextBubbleField(int textDuration = 6) { _defaultLifeMs = TextBubbleRules.LifeMs(textDuration); TextDuration = textDuration; }

    public int TextDuration { get; private set; }
    public long DefaultLifeMs => _defaultLifeMs;
    public int Count => _entries.Count(e => e is not null);

    /// <summary>The live bubbles in table order (slot 0 first: a later one is drawn over an earlier one).</summary>
    public IEnumerable<TextBubble<TKey>> Bubbles => _entries.OfType<TextBubble<TKey>>();

    public TextBubble<TKey>? Of(TKey owner) => _entries.FirstOrDefault(e => e is not null && EqualityComparer<TKey>.Default.Equals(e.Owner, owner));

    /// <summary>The "text duration" setting changed (0x4D67F0, also the setting's callback): new bubbles live n seconds and every live
    /// non-persistent bubble now lives n seconds from its own start. The original multiplies the live ones by 1,000,000 instead of 1000
    /// (a unit slip: they stay about 100 minutes); that technical bug is fixed in both modes, no switch (ledger TB17).</summary>
    [OriginalFunction(0x4D67F0, OriginalFunctionScope.Partial)]
    public void SetTextDuration(int textDuration)
    {
        var life = TextBubbleRules.LifeMs(textDuration);
        TextDuration = textDuration; _defaultLifeMs = life;
        for (var i = 0; i < _entries.Length; i++)
            if (_entries[i] is { Persistent: false } entry) _entries[i] = entry with { LifeMs = life };
    }

    /// <summary>Makes a bubble over <paramref name="owner"/> at <paramref name="nowMs"/> (0x4D5FE0): the object's own entry is replaced, else the
    /// highest free one, else the eviction victim. Returns its table index.</summary>
    [OriginalFunction(0x4D5FE0, OriginalFunctionScope.Partial)]
    public int Create(TKey owner, string text, TextBubbleLayout layout, BubbleColor color, long nowMs)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(layout);
        if (!Enum.IsDefined(color)) throw new ArgumentOutOfRangeException(nameof(color));
        var index = Array.FindIndex(_entries, e => e is not null && EqualityComparer<TKey>.Default.Equals(e.Owner, owner));
        if (index < 0) index = Array.FindLastIndex(_entries, e => e is null);
        if (index < 0)
        {
            index = 0;
            for (var i = 1; i < _entries.Length; i++)
                if (!_entries[i]!.Persistent && (uint)_entries[i]!.CreatedMs < (uint)_entries[index]!.CreatedMs) index = i;
        }
        _entries[index] = new(owner, text, layout, color, false, nowMs, _defaultLifeMs, null);
        return index;
    }

    /// <summary>The bubble's placement slot, chosen at its first layout and kept (see <see cref="TextBubbleRules.ChooseSlot"/>).</summary>
    public void SetSlot(TKey owner, int slot)
    {
        var index = Array.FindIndex(_entries, e => e is not null && EqualityComparer<TKey>.Default.Equals(e.Owner, owner));
        if (index >= 0) _entries[index] = _entries[index]! with { Slot = slot };
    }

    /// <summary>The life of an object's bubble (0x4D6160): −2 makes it persistent and changes nothing else; another negative number is the
    /// default life from now; n seconds from now otherwise. An object without a bubble is ignored.</summary>
    [OriginalFunction(0x4D6160, OriginalFunctionScope.Full)]
    public void SetLife(TKey owner, int seconds, long nowMs)
    {
        var index = Array.FindIndex(_entries, e => e is not null && EqualityComparer<TKey>.Default.Equals(e.Owner, owner));
        if (index < 0) return;
        var entry = _entries[index]!;
        _entries[index] = seconds == TextBubbleRules.Persistent ? entry with { Persistent = true }
            : entry with { Persistent = false, CreatedMs = nowMs, LifeMs = seconds < 0 ? _defaultLifeMs : TextBubbleRules.LifeMs(seconds) };
    }

    /// <summary>Removes an object's bubble (0x4D62B0); false when it had none.</summary>
    [OriginalFunction(0x4D62B0, OriginalFunctionScope.Partial)]
    public bool Remove(TKey owner)
    {
        var index = Array.FindIndex(_entries, e => e is not null && EqualityComparer<TKey>.Default.Equals(e.Owner, owner));
        if (index < 0) return false;
        _entries[index] = null;
        return true;
    }

    /// <summary>The ping (0x4D5EB0): every live, non-persistent bubble older than its life goes (the age is clamped to 0x7FFFFFFF, the compare
    /// is strict: a life of 0 lasts until the first ping with a later time). True when any went.</summary>
    [OriginalFunction(0x4D5EB0, OriginalFunctionScope.Partial)]
    public bool Tick(long nowMs)
    {
        var removed = false;
        for (var i = 0; i < _entries.Length; i++)
        {
            if (_entries[i] is not { Persistent: false } entry) continue;
            var age = Math.Min(Math.Max(nowMs - entry.CreatedMs, 0), 0x7FFFFFFF);
            if (age > entry.LifeMs) { _entries[i] = null; removed = true; }
        }
        return removed;
    }

    /// <summary>Removes every bubble (0x4D6320: a new map, a reset, a script's black fade).</summary>
    [OriginalFunction(0x4D6320, OriginalFunctionScope.Partial)]
    public void Clear() => Array.Clear(_entries);
}
