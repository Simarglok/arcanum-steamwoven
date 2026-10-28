namespace Arcanum.Presentation.Hud;

/// <summary>The original's five floater fonts, by index (ledger CH10): their colours are <see cref="FloaterRules.Rgb"/>.</summary>
public enum FloaterColor { White = 0, Red = 1, Green = 2, Blue = 3, Yellow = 4 }

/// <summary>Whose a damaged critter is, for its floater colour (CH12): the PC, an NPC whose PC leader is the local PC, anyone else.</summary>
public enum FloaterSide { Other, Pc, PcFollower }

/// <summary>
/// ARC-UI-021: the original's floating text (docs/research/combat-hud.md; checked in the 1.0.7.4 executable, ledger CH10–CH12): the
/// "text floaters" level and the "float speed" setting, the five colours, a line's start offset, rise and fade, and the box it is
/// drawn in over its object. Offsets are pixels from the box's top; a line starts 4 line heights down and rises to the top. The
/// line height is the font's text height + 2. Engine-free; <see cref="FloaterField{TKey}"/> keeps the lines, the host draws.
/// </summary>
public static class FloaterRules
{
    /// <summary>"Text floaters" 0: no floater is drawn (lines are still made and move, CH10).</summary>
    public const int Never = 0;
    /// <summary>"Text floaters" 1, the original's default: messages only.</summary>
    public const int Messages = 1;
    /// <summary>"Text floaters" 2: messages and numbers (the damage and poison lines, CH12).</summary>
    public const int Numbers = 2;
    public const int DefaultLevel = Messages, MaxLevel = Numbers;
    /// <summary>"Float speed": 0–10 (the options window's slider), default 2 (OP5, CH10).</summary>
    public const int DefaultSpeed = 2, MaxSpeed = 10;
    /// <summary>The lines rise at most once every 50 ms (CH11).</summary>
    public const int StepMs = 50;
    /// <summary>The box is 200 px wide and <see cref="BoxLines"/> line heights high; a new line starts <see cref="StartLines"/>
    /// line heights below its top (CH11).</summary>
    public const int BoxWidth = 200, BoxLines = 5, StartLines = 4;
    /// <summary>The box's bottom stands 90 px above the object's tile centre (the original adds the object's draw offset), centred on
    /// it (CH11).</summary>
    public const int BoxRise = 90;

    /// <summary>A floater font's colour (font art 229 tinted, 0x4D4E20).</summary>
    public static (byte R, byte G, byte B) Rgb(FloaterColor color) => color switch
    {
        FloaterColor.White => (255, 255, 255),
        FloaterColor.Red => (255, 0, 0),
        FloaterColor.Green => (0, 255, 0),
        FloaterColor.Blue => (64, 64, 255),
        FloaterColor.Yellow => (255, 255, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(color)),
    };

    /// <summary>A damaged critter's colour (CH12): red for a PC, yellow for an NPC whose PC leader is the local PC, white otherwise.</summary>
    public static FloaterColor SideColor(FloaterSide side) => side switch
    {
        FloaterSide.Pc => FloaterColor.Red,
        FloaterSide.PcFollower => FloaterColor.Yellow,
        _ => FloaterColor.White,
    };

    /// <summary>The line height: the font's text height + 2 (0x4D4E20).</summary>
    public static int LineHeight(int textHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(textHeight);
        return textHeight + 2;
    }

    /// <summary>The pixels every line rises at a step: the speed + 1 (CH10).</summary>
    public static int Step(int speed)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(speed);
        return speed + 1;
    }

    /// <summary>A new line's offset (0x4D5450): 4 line heights, or, when the object's newest line is still lower than 3 line heights
    /// (it has risen less than one), one line height below that line, so stacked lines never overlap.</summary>
    [OriginalFunction(0x4D5450, OriginalFunctionScope.Partial)]
    public static int StartOffset(int? newestOffset, int lineHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lineHeight);
        return newestOffset is { } newest && newest > 3 * lineHeight ? checked(newest + lineHeight) : StartLines * lineHeight;
    }

    /// <summary>A line's opacity 0–255 at its offset (0x4D5AA0): with m = offset + h / 2 (h / 2 truncated), and m above 3h counted
    /// as 5h − m, opaque above 2h, 0 at h / 2 or less, else 255 × (m − h / 2) / (2h − h / 2) truncated, computed in doubles as
    /// the original's x87 code with its 53-bit precision. A line fades in from 4h to 2.5h, is opaque to 1.5h and fades out to the
    /// top; a line stacked below 4h is invisible until it rises there.</summary>
    [OriginalFunction(0x4D5AA0, OriginalFunctionScope.Full)]
    public static int Alpha(int offset, int lineHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lineHeight);
        var half = lineHeight / 2;
        var m = (long)offset + half;
        if (m > 3L * lineHeight) m = 5L * lineHeight - m;
        var full = 2L * lineHeight;
        if (m > full) return 255;
        if (m <= half) return 0;
        return (int)((double)(m - half) / (full - half) * 255.0);
    }

    /// <summary>The box's top-left corner on the screen (0x4D58C0) for an object whose tile centre (with its draw offset) is
    /// (<paramref name="anchorX"/>, <paramref name="anchorY"/>): 100 px to its left and 90 px + 5 line heights above it.</summary>
    [OriginalFunction(0x4D58C0, OriginalFunctionScope.Partial)]
    public static (int Left, int Top) Box(int anchorX, int anchorY, int lineHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lineHeight);
        return (checked(anchorX - BoxWidth / 2), checked(anchorY - BoxRise - BoxLines * lineHeight));
    }

    /// <summary>A line's left edge (0x4D5A60): centred in the box, (200 − width) / 2 truncated toward zero (a line wider than the box
    /// starts left of it).</summary>
    [OriginalFunction(0x4D5A60, OriginalFunctionScope.Partial)]
    public static int LineLeft(int boxLeft, int textWidth)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(textWidth);
        return checked(boxLeft + (BoxWidth - textWidth) / 2);
    }
}

/// <summary>One floating line: its text, colour and offset from the box's top.</summary>
public readonly record struct FloaterLine(string Text, FloaterColor Color, int Offset);

/// <summary>
/// ARC-UI-021: the floating lines of every object, as the original keeps them (0x4D5450, 0x4D5190): a list of lines per object in
/// the order they were made; at most every 50 ms every line rises by the step and a line above the box's top is removed, and an
/// object without lines is dropped. The level does not gate a line here (the original's callers do, CH12); the host draws
/// nothing at level 0. At most <see cref="MaxLines"/> lines are kept (a limit of the project's: a line beyond it is refused).
/// </summary>
public sealed class FloaterField<TKey> where TKey : notnull
{
    public const int MaxLines = 256;
    private readonly Dictionary<TKey, List<FloaterLine>> _lines = [];
    private readonly List<TKey> _order = [];
    private long? _lastStep;

    public FloaterField(int lineHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lineHeight);
        LineHeight = lineHeight;
    }

    public int LineHeight { get; }
    public int Count { get; private set; }
    /// <summary>The objects with lines, in the order their first line was made.</summary>
    public IReadOnlyList<TKey> Owners => _order;

    /// <summary>The lines of <paramref name="owner"/> in the order they were made (oldest first); empty when it has none.</summary>
    public IReadOnlyList<FloaterLine> Lines(TKey owner) => _lines.TryGetValue(owner, out var lines) ? lines : [];

    /// <summary>Adds a line over <paramref name="owner"/> at its start offset; false when the field is full or the text is empty.</summary>
    public bool Add(TKey owner, string text, FloaterColor color)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0 || Count >= MaxLines || !Enum.IsDefined(color)) return false;
        if (!_lines.TryGetValue(owner, out var lines)) { lines = []; _lines[owner] = lines; _order.Add(owner); }
        lines.Add(new(text, color, FloaterRules.StartOffset(lines.Count > 0 ? lines[^1].Offset : null, LineHeight)));
        Count++;
        return true;
    }

    /// <summary>The tick (0x4D5190) at <paramref name="nowMs"/>: when 50 ms or more passed since the last step (or none was made), every
    /// line rises by <see cref="FloaterRules.Step"/>(<paramref name="speed"/>), lines above the top go and objects without lines are
    /// dropped. True when it stepped.</summary>
    [OriginalFunction(0x4D5190, OriginalFunctionScope.Partial)]
    public bool Advance(long nowMs, int speed)
    {
        var step = FloaterRules.Step(speed);
        if (_lastStep is { } last && nowMs - last < FloaterRules.StepMs) return false;
        _lastStep = nowMs;
        for (var i = _order.Count - 1; i >= 0; i--)
        {
            var owner = _order[i];
            var lines = _lines[owner];
            for (var j = lines.Count - 1; j >= 0; j--)
            {
                var offset = lines[j].Offset - step;
                if (offset < 0) { lines.RemoveAt(j); Count--; }
                else lines[j] = lines[j] with { Offset = offset };
            }
            if (lines.Count == 0) { _lines.Remove(owner); _order.RemoveAt(i); }
        }
        return true;
    }

    /// <summary>Removes every line (a new scene).</summary>
    public void Clear()
    {
        _lines.Clear(); _order.Clear(); Count = 0; _lastStep = null;
    }
}
