namespace Arcanum.Presentation.Display;

/// <summary>
/// ARC-UI-1737 (ledger OH1, FR-UI-006, FR-LIGHT-003): the "brightness" setting of the Video page. The original (callback 0x4BE0F0, gamma set
/// 0x51FFE0) turns the whole value 0–9 (default 0) into the factor g = 1.0 + 0.1 × value and scales its display's hardware gamma ramp by it:
/// each of the ramp's 256 entries becomes the base entry times g, truncated, capped at 0xFF00; a factor equal to the last one set changes
/// nothing. Steamwoven has no hardware ramp, so the same scaling is one final colour multiply applied last, over the interface and the
/// fades too (the screen fades 0x4BDFA0 and the movie player's fades 0x51FCA0 return to this ramp, so a faded picture ends at the player's
/// brightness): value 0 draws nothing extra, which is today's picture byte for byte. Godot-free; the viewer's top layer applies
/// <see cref="Apply"/>'s arithmetic in a shader.
/// </summary>
public static class BrightnessRules
{
    public const int Max = 9, Default = 0;
    /// <summary>The ramp's top entry, 0xFF00: the standard ramp's entry for the channel value 255 is 255 × 256, so the cap is white itself (inferred: the base ramp is the display's own, normally that identity; OH1's "0.996" read the cap as a fraction of 0xFFFF).</summary>
    public const int RampTop = 0xFF00;
    /// <summary>Added before the whole-number cut so that a product that is a whole number in the original's decimal constants (an integer times a multiple of 0.1, for instance 10 × 1.3) stays whole in float arithmetic; the original's x87 arithmetic is not reproduced.</summary>
    public const double WholeGuard = 1e-3;

    /// <summary>The factor g = 1.0 + 0.1 × <paramref name="value"/> (1.0 to 1.9; 0.1 × 10 would make 2.0, which the original refuses as out of range).</summary>
    [OriginalFunction(0x4BE0F0, OriginalFunctionScope.Partial)]
    public static double Factor(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, Max);
        return 1.0 + 0.1 * value;
    }

    /// <summary>Whether the setting changes the picture at all (value 0 is the factor 1.0, which the original leaves alone).</summary>
    public static bool Active(int value) => value > 0;

    /// <summary>One colour channel (0–255) as the ramp shows it: the channel × g, whole-number cut, capped at white. Value 0 returns it as it is.</summary>
    public static int Apply(int channel, int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(channel);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(channel, 255);
        if (!Active(value)) return channel;
        return Math.Min((int)Math.Floor(channel * Factor(value) + WholeGuard), RampTop >> 8);
    }
}
