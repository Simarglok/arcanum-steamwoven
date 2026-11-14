namespace Arcanum.Presentation.Hud;

/// <summary>What a pointer press did to the dialog reply band (<see cref="DialogReplyBand.Press"/>).</summary>
public enum DialogReplyHitKind
{
    /// <summary>The pointer is outside the band: the press only goes to the window system (0x5517A0), which this panel has no use for, and the event is consumed.</summary>
    Outside,
    /// <summary>Inside the band but on no shown reply (an empty slot, or the gap between rows in Steamwoven): refused.</summary>
    Empty,
    /// <summary>A reply row was hit; <see cref="DialogReplyHit.Row"/> is its zero-based index.</summary>
    Row,
}

/// <summary>The result of a press: what it hit and, for <see cref="DialogReplyHitKind.Row"/>, which row.</summary>
public readonly record struct DialogReplyHit(DialogReplyHitKind Kind, int Row);

/// <summary>
/// ARC-UI-2025: the pointer rules of the original's dialog reply band (0x4C9A10 with the row drawer 0x4C9810, ledger DH1–DH7). The band is
/// 100 px tall and holds five slots; row <c>i</c> is drawn 16 px tall at y = 19 i + 3, but a press picks the row as y / 20: the click bands drift
/// down 1 px a row against the drawn rows (an original slip). A press selects on the press itself. Coordinates are relative to the band's
/// top-left; <c>x</c> must lie in [0, width) and <c>y</c> in [0, 100) to count as in the band. Engine-free; the host owns the pixels.
/// </summary>
public static class DialogReplyBand
{
    /// <summary>The band is 100 px tall (0x4C9280) and shows up to five replies; a row is drawn 16 px tall (the slot height is (100 - 18) / 5).</summary>
    public const int Height = 100, Slots = 5, RowHeight = 16;
    /// <summary>Row i is drawn at y = 19 i + 3 (0x4C9810): the row height plus 3.</summary>
    public const int RowPitch = 19, FirstRowTop = 3;
    /// <summary>A press and a move pick the row as (y - top) / 20 (0x4C9A10): the band height divided by the slot count.</summary>
    public const int ClickBand = Height / Slots;

    /// <summary>The top of the drawn row <paramref name="row"/>.</summary>
    public static int RowTop(int row)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        return RowPitch * row + FirstRowTop;
    }

    /// <summary>Whether the pointer height <paramref name="y"/> lies on the drawn pixels of <paramref name="row"/>.</summary>
    public static bool DrawnRowContains(int row, int y) => y >= RowTop(row) && y < RowTop(row) + RowHeight;

    private static bool InBand(int x, int y, int width) => x >= 0 && x < width && y >= 0 && y < Height;

    private static int Shown(int shownRows) => Math.Clamp(shownRows, 0, Slots);

    /// <summary>
    /// A press at (<paramref name="x"/>, <paramref name="y"/>) in a band <paramref name="width"/> px wide showing <paramref name="shownRows"/> replies.
    /// Original (<paramref name="clickBandsDrift"/>): the row is y / 20 and an empty slot is refused; Steamwoven: the row whose drawn pixels hold y.
    /// </summary>
    [OriginalFunction(0x4C9A10, OriginalFunctionScope.Partial)]
    public static DialogReplyHit Press(int x, int y, int width, int shownRows, bool clickBandsDrift)
    {
        if (!InBand(x, y, width)) return new(DialogReplyHitKind.Outside, -1);
        var rows = Shown(shownRows);
        if (clickBandsDrift)
        {
            var row = y / ClickBand;
            return row < rows ? new(DialogReplyHitKind.Row, row) : new(DialogReplyHitKind.Empty, -1);
        }
        for (var row = 0; row < rows; row++)
            if (DrawnRowContains(row, y)) return new(DialogReplyHitKind.Row, row);
        return new(DialogReplyHitKind.Empty, -1);
    }

    /// <summary>
    /// The original's hover bookkeeping (0x4C9A10 move records, 0x4C9810 row drawer): a move redraws the old row and the row y / 20, and a row
    /// is drawn highlighted only when the pointer is on its drawn pixels at the time it is drawn, so near a row's top edge the row y / 20 is
    /// the one above and the lower row stays plain until the pointer moves lower; leaving the band redraws the old row plain.
    /// </summary>
    public sealed class Hover(int width, int shownRows)
    {
        private readonly bool[] _lit = new bool[Slots];
        private int _old = -1, _y;
        private bool _inBand;

        /// <summary>Whether <paramref name="row"/> is drawn highlighted now.</summary>
        public bool IsLit(int row) => row >= 0 && row < Slots && _lit[row];

        /// <summary>The row the original remembers as the last hovered one (DAT_005b7218), or -1.</summary>
        public int Remembered => _old;

        /// <summary>The pointer moved to (<paramref name="x"/>, <paramref name="y"/>) relative to the band's top-left.</summary>
        public void Move(int x, int y)
        {
            _y = y;
            _inBand = InBand(x, y, width);
            if (!_inBand)
            {
                if (_old != -1) Draw(_old);
                _old = -1;
                return;
            }
            var row = y / ClickBand;
            if (_old != -1 && _old != row) Draw(_old);
            if (row < Shown(shownRows)) { Draw(row); _old = row; }
            else _old = -1;
        }

        /// <summary>The pointer left the band area: the old row is redrawn plain.</summary>
        public void Leave() => Move(-1, -1);

        // 0x4C9810: a row is lit when the pointer is in the band and on its drawn pixels; a lit row that is not the remembered one redraws the
        // remembered row (plain, the pointer is elsewhere) and becomes the remembered one.
        private void Draw(int row)
        {
            var lit = _inBand && DrawnRowContains(row, _y);
            _lit[row] = lit;
            if (!lit || _old == row) return;
            var previous = _old;
            if (previous != -1 && previous < Shown(shownRows)) Draw(previous);
            _old = row;
        }
    }
}
