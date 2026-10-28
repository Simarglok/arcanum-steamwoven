namespace Arcanum.Presentation.Hud;

/// <summary>
/// ARC-UI-020: the original's health and fatigue bars (docs/research/combat-hud.md; checked in the 1.0.7.4 executable, ledger
/// CH7–CH9): the PC's two gauges and their numbers, the hovered critter's bars and their text, and the follower portraits' bars
/// and red tint. Every percent is current × 100 / maximum truncated toward zero, as the original's integer division. The original
/// divides by a zero maximum in the hovered-critter and portrait paths (a crash, a technical bug): the rules read that percent as
/// 0 in both rule modes. The PC's gauges keep the original's guard (half full at a zero maximum). ARC-UI-024 adds where the
/// gauges' vial art goes (ledger PA1–PA3) and the compact panel the original draws with the interface hidden (PA4–PA5).
/// Engine-free; the host draws.
/// </summary>
public static class LifeBarRules
{
    /// <summary>The height of the PC's gauges in the original's 800 × 600 interface (CH7).</summary>
    public const int GaugeHeight = 88;
    /// <summary>A follower's portrait turns red below this health percent (CH9).</summary>
    public const int PortraitTintBelow = 20;
    /// <summary>The rows of a gauge's fill art drawn above its fill line (the liquid's surface, PA3).</summary>
    public const int GaugeLip = 8;
    /// <summary>The height of a compact panel column's fill (PA5): it ends at row 100 of the 102-row panel.</summary>
    public const int CompactHeight = 100;
    /// <summary>A compact panel's poison level is printed this far below its health number (PA5).</summary>
    public const int CompactPoisonOffset = 40;

    /// <summary>current × 100 / maximum truncated toward zero (not clamped: a dead critter's is negative); 0 for a maximum of 0
    /// (the original divides by zero there, a technical bug fixed in both rule modes).</summary>
    public static int Percent(int current, int maximum) =>
        maximum == 0 ? 0 : (int)Math.Clamp((long)current * 100 / maximum, int.MinValue, int.MaxValue);

    /// <summary>The filled height of one of the PC's gauges of <paramref name="height"/> pixels: height × percent / 100 truncated,
    /// with the percent 50 for a maximum of 0 (the original's guard), clamped to the gauge (the original's drawing of a fill
    /// beyond it, a dead PC's negative one, was not traced).</summary>
    [OriginalFunction(0x54AF10, OriginalFunctionScope.Partial)]
    public static int GaugeFill(int current, int maximum, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        var percent = maximum == 0 ? 50 : Percent(current, maximum);
        return (int)Math.Clamp((long)height * percent / 100, 0, height);
    }

    /// <summary>
    /// Where a gauge of <paramref name="height"/> rows filled <paramref name="fill"/> rows (<see cref="GaugeFill"/>) draws its
    /// vial art (PA2, PA3), in rows from the gauge's top. The empty part shows the empty vial art's top <c>EmptyRows</c> rows at
    /// the top. The fill art (its 96 rows are the gauge's 88 and the 8-row lip) is placed with its top 8 rows above the fill
    /// line and clipped to the gauge: its rows from <c>FillSourceTop</c> are drawn from gauge row <c>FillTop</c>, <c>FillRows</c>
    /// of them, so the lip overlaps the empty part, a full gauge shows the art's rows 8 to 95 and an empty one no fill art.
    /// </summary>
    public static (int EmptyRows, int FillTop, int FillSourceTop, int FillRows) GaugeArt(int fill, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        fill = Math.Clamp(fill, 0, height);
        var empty = height - fill;
        if (fill == 0) return (empty, 0, 0, 0);
        var top = Math.Max(0, empty - GaugeLip);
        return (empty, top, top - (empty - GaugeLip), height - top);
    }

    /// <summary>
    /// One column of the compact panel (PA5): the damage percent d is (maximum − current) × 100 / maximum truncated toward zero;
    /// the fill starts at row min(d + 1, 100), or 101 for the fatigue column, and runs to row 100, one row more while d is below
    /// 100: nothing at d of 100 or more. The number is printed at <c>Top</c> even when nothing is filled. The original divides by
    /// a zero maximum (a crash, a technical bug): that column reads as fully damaged in both rule modes.
    /// </summary>
    [OriginalFunction(0x569070, OriginalFunctionScope.Partial)]
    public static (int Top, int Rows) CompactFill(int current, int maximum, bool fatigue)
    {
        var damage = maximum == 0 ? CompactHeight : Percent((int)Math.Clamp((long)maximum - current, int.MinValue, int.MaxValue), maximum);
        var top = (int)Math.Min((long)damage + 1, fatigue ? CompactHeight + 1 : CompactHeight);
        var rows = (long)CompactHeight - top + (damage < CompactHeight ? 1 : 0);
        return (top, (int)Math.Clamp(rows, 0, int.MaxValue));
    }

    /// <summary>A compact panel number as the original prints it: C's <c>%02d</c> (−5 is "-5", 7 is "07").</summary>
    public static string CompactNumber(int value)
    {
        if (value >= 0) return value.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
        return "-" + (-(long)value).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>A gauge number as the original prints it: C's <c>%03d</c>, three digits zero-padded with the sign counted in the
    /// width (−5 is "-05", 1234 is "1234"). Used for the PC's current health and fatigue and the poison level.</summary>
    [OriginalFunction(0x54AD00, OriginalFunctionScope.Partial)]
    public static string GaugeNumber(int value)
    {
        if (value >= 0) return value.ToString("000", System.Globalization.CultureInfo.InvariantCulture);
        var digits = (-(long)value).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return "-" + digits.PadLeft(2, '0');
    }

    /// <summary>The filled width of a hovered critter's bar of <paramref name="width"/> pixels: the percent clamped to 0–100; none
    /// at 0, otherwise width × percent / 100 rounded down, plus one unless that is the whole width.</summary>
    [OriginalFunction(0x554640, OriginalFunctionScope.Partial)]
    public static int HoverFill(int percent, int width)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        var p = Math.Clamp(percent, 0, 100);
        if (p == 0) return 0;
        var filled = (int)((long)width * p / 100);
        return filled < width ? filled + 1 : filled;
    }

    /// <summary>The text of a hovered critter's bar: "current/maximum" when it is the PC or one of the PC's followers
    /// (<paramref name="pcSide"/>), otherwise the unclamped percent with a percent sign.</summary>
    [OriginalFunction(0x553F70, OriginalFunctionScope.Partial)]
    public static string HoverText(int current, int maximum, bool pcSide)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        return pcSide ? current.ToString(culture) + "/" + maximum.ToString(culture) : Percent(current, maximum).ToString(culture) + "%";
    }

    /// <summary>The cropped width of a follower portrait's bar art of <paramref name="width"/> pixels: none at a percent of 0 or
    /// less, the percent capped at 100, width × percent / 100 rounded down.</summary>
    [OriginalFunction(0x56B510, OriginalFunctionScope.Partial)]
    public static int PortraitFill(int percent, int width)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        return percent <= 0 ? 0 : (int)((long)width * Math.Min(percent, 100) / 100);
    }

    /// <summary>Whether a follower's portrait gets the red fill: its health percent below 20 (a dead follower's too).</summary>
    [OriginalFunction(0x56B290, OriginalFunctionScope.Partial)]
    public static bool PortraitTinted(int health, int maximumHealth) => Percent(health, maximumHealth) < PortraitTintBelow;
}
