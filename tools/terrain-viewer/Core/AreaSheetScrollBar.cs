namespace Arcanum.TerrainViewer.Core;

/// <summary>The sizes of the two arrow arts the bar measures itself with (art 317 above, art 318 below; 0x5804E0 reads them).</summary>
public readonly record struct AreaScrollArrows(int TopWidth, int TopHeight, int BottomWidth, int BottomHeight);

/// <summary>
/// ARC-UI-1742: the original's vertical scroll bar widget (created by 0x5804E0 from a descriptor, geometry 0x581550..0x581660, events 0x580B10, thumb
/// drawn by 0x5807F0; ledger ALP10..ALP15), Godot-free. The bar is <see cref="Width"/> × <see cref="Height"/> at (<see cref="X"/>, <see cref="Y"/>) in its
/// window: the up arrow (art 317) at its top, the down arrow (art 318) at its bottom, the thumb between them. One step is <see cref="Unit"/>
/// pixels of thumb travel and the thumb is as long as one step (at least 12 pixels), so it shrinks as the range grows. The window's own art draws
/// the track. A press on the thumb drags it, a press above or below it moves the value by a page, an arrow by a step, the wheel inside the wheel
/// rectangle by three steps. Every change calls <see cref="Changed"/> with the new value, even one that did not move it.
/// </summary>
public sealed class AreaSheetScrollBar
{
    /// <summary>The descriptor the schemes page gives (0x55EFF0): value 0..<c>count − 15</c>, step 1; the page (5) and wheel (3) steps are the defaults of 0x5811F0.</summary>
    public const int DefaultPage = 5, DefaultWheel = 3, MinimumThumb = 12;

    private readonly AreaScrollArrows _arrows;
    private int _dragSteps, _grab = -1;
    private bool _dragging;

    public AreaSheetScrollBar(int x, int y, int width, int height, int minimum, int maximum, int step, int value, AreaScrollArrows arrows,
        (int X, int Y, int Width, int Height) wheel, int page = DefaultPage, int wheelStep = DefaultWheel)
    {
        (X, Y, Width, Height, Minimum, Maximum, Step, Page, WheelStep, Wheel, _arrows) = (x, y, width, height, minimum, maximum, step, page, wheelStep, wheel, arrows);
        if (step <= 0) throw new ArgumentOutOfRangeException(nameof(step));
        // 0x5804E0: a value outside the range is put at the minimum.
        Value = value < minimum || value > maximum ? minimum : value;
    }

    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }
    public int Minimum { get; }
    public int Maximum { get; }
    public int Step { get; }
    public int Page { get; }
    public int WheelStep { get; }
    public (int X, int Y, int Width, int Height) Wheel { get; }
    public int Value { get; private set; }
    public AreaScrollArrows Arrows => _arrows;

    /// <summary>The value after each change (the descriptor's callback: the schemes page takes it as its first shown entry).</summary>
    public event Action<int>? Changed;

    public bool Dragging => _dragging;

    /// <summary>One step of the thumb in pixels (0x581550): the track between the arrows over the range plus one step, times the step.</summary>
    public double Unit => (double)(Height - _arrows.BottomHeight - _arrows.TopHeight) / (Maximum - Minimum + Step) * Step;

    /// <summary>The thumb's length (0x5815A0): the step's pixels, at least 12.</summary>
    public int ThumbLength => Unit < MinimumThumb ? MinimumThumb : (int)Unit;

    /// <summary>The thumb's top for the stored value (0x5815D0).</summary>
    public int ThumbTop => (int)(Unit * ((double)(Value - Minimum) / Step)) + Y + _arrows.TopHeight;

    /// <summary>The thumb's bottom (0x581660).</summary>
    public int ThumbBottom => ThumbTop + ThumbLength;

    /// <summary>Where the thumb is drawn (0x5807F0): the stored value's top plus the pixel offset a drag gives it, kept between the arrows. At the
    /// largest value with no offset the thumb sits against the down arrow.</summary>
    public int DrawnThumbTop(int pixelOffset = 0)
    {
        var top = Value == Maximum && pixelOffset == 0 ? Y + Height - _arrows.BottomHeight - ThumbLength : ThumbTop + pixelOffset;
        var least = Y + _arrows.TopHeight;
        var most = Y + Height - _arrows.BottomHeight - ThumbLength;
        return top < least ? least : top > most ? most : top;
    }

    private bool InColumn(int x) => x >= X && x <= X + Width;

    /// <summary>The up arrow's place (centred in the bar's width, at its top) and the down arrow's (at its bottom): 0x5804E0.</summary>
    public (int X, int Y) UpArrow => ((Width - _arrows.TopWidth) / 2 + X, Y);

    public (int X, int Y) DownArrow => ((Width - _arrows.BottomWidth) / 2 + X, Y + Height - _arrows.BottomHeight);

    /// <summary>What a point of the window is over.</summary>
    public enum Part { None, Up, Down, Thumb, AboveThumb, BelowThumb }

    /// <summary>The part of the bar at (<paramref name="x"/>, <paramref name="y"/>) as 0x580B10 tests a press: the thumb, then the track above it, then below
    /// it (both rectangles include their edges). The arrows are buttons and answer first, on their art's rectangle (PROJECT: the exe tests a button's pixels).</summary>
    public Part PartAt(int x, int y)
    {
        var (ux, uy) = UpArrow;
        if (x >= ux && x < ux + _arrows.TopWidth && y >= uy && y < uy + _arrows.TopHeight) return Part.Up;
        var (dx, dy) = DownArrow;
        if (x >= dx && x < dx + _arrows.BottomWidth && y >= dy && y < dy + _arrows.BottomHeight) return Part.Down;
        if (!InColumn(x)) return Part.None;
        if (y >= ThumbTop && y <= ThumbBottom) return Part.Thumb;
        if (y >= Y + _arrows.TopHeight && y <= ThumbTop) return Part.AboveThumb;
        if (y >= ThumbBottom && y <= Y + Height - _arrows.BottomHeight) return Part.BelowThumb;
        return Part.None;
    }

    public bool InWheelRectangle(int x, int y) => x >= Wheel.X && x <= Wheel.X + Wheel.Width && y >= Wheel.Y && y <= Wheel.Y + Wheel.Height;

    private void Move(int value)
    {
        Value = Math.Clamp(value, Minimum, Maximum);
        Changed?.Invoke(Value);
    }

    /// <summary>An arrow released over itself: one step (0x580B10, the first branch).</summary>
    public void PressArrow(bool up) => Move(up ? Value - Step : Value + Step);

    /// <summary>A press on the track outside the thumb: one page (0x580B10 after the thumb test fails).</summary>
    public void PressTrack(bool above) => Move(above ? Value - Page : Value + Page);

    /// <summary>The wheel inside its rectangle: a turn up (positive) shows an earlier entry, a turn down a later one, by the wheel step.</summary>
    public void Turn(int delta)
    {
        if (delta > 0) Move(Value - WheelStep);
        else if (delta < 0) Move(Value + WheelStep);
    }

    /// <summary>A press on the thumb: <paramref name="y"/> is the pointer's window y; the grab is its distance from the thumb's top.</summary>
    public void BeginDrag(int y)
    {
        _dragging = true;
        _grab = y - ThumbTop;
        _dragSteps = 0;
    }

    /// <summary>The pointer moved while dragging (0x580B10, message 6): the thumb's pixel offset from its stored place is the pointer's y less the grab
    /// and the stored top; whole steps accumulate while that offset (less the steps taken) is more than a step, and the callback gets the stored value
    /// plus them as the pointer moves. Returns the pixel offset the thumb is drawn at (<see cref="DrawnThumbTop"/>).</summary>
    public int Drag(int y)
    {
        if (!_dragging) return 0;
        var offset = y - _grab - ThumbTop;
        var unit = Unit;
        var rest = offset - _dragSteps * unit;
        if (!(rest <= unit || Maximum <= Value + _dragSteps))
        {
            do { rest -= unit; _dragSteps += Step; } while (rest > unit && Value + _dragSteps < Maximum);
        }
        else if (!(-unit <= rest || Value + _dragSteps <= Minimum))
        {
            do { rest += unit; _dragSteps -= Step; } while (rest < -unit && Minimum < Value + _dragSteps);
        }
        var live = Value + _dragSteps;
        Changed?.Invoke(Math.Clamp(live, Minimum, Maximum));
        return offset;
    }

    /// <summary>The press released while dragging: the stored value takes the steps when it stays inside the range, the thumb goes back to its place.</summary>
    public void EndDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        if (Value + _dragSteps >= Minimum && Value + _dragSteps <= Maximum) Value += _dragSteps;
        _dragSteps = 0;
        _grab = -1;
    }
}
