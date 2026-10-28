namespace Arcanum.Presentation.Hud;

/// <summary>
/// ARC-UI-020: the original's health and fatigue bars (docs/research/combat-hud.md; checked in the 1.0.7.4 executable, ledger
/// CH7–CH9): the PC's two gauges and their numbers, the hovered critter's bars and their text, and the follower portraits' bars
/// and red tint. Every percent is current × 100 / maximum truncated toward zero, as the original's integer division. The original
/// divides by a zero maximum in the hovered-critter and portrait paths (a crash, a technical bug): the rules read that percent as
/// 0 in both rule modes. The PC's gauges keep the original's guard (half full at a zero maximum). Engine-free; the host draws.
/// </summary>
public static class LifeBarRules
{
    /// <summary>The height of the PC's gauges in the original's 800 × 600 interface (CH7).</summary>
    public const int GaugeHeight = 88;
    /// <summary>The width of the hovered-critter bars in the original's interface (CH8).</summary>
    public const int HoverBarWidth = 291;
    /// <summary>A follower's portrait turns red below this health percent (CH9).</summary>
    public const int PortraitTintBelow = 20;

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
