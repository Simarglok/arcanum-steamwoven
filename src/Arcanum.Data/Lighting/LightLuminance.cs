namespace Arcanum.Data.Lighting;

/// <summary>
/// ARC-WORLD-1149 (FR-LIGHT-011): the grey level of a light colour, which the original's light-aware checks read as "the light at an
/// object or a location" (the skill difficulty 0x4C8430 through 0x4DCE10 for an object and 0x4D9240 for a location, ledger LL1–LL3).
/// Both functions end in the same three table reads and a sum; the tables are built at video start (0x52C9B0) with one entry per
/// channel value <c>i</c>, <c>trunc(i / max × weight)</c>, the weights being 76.5, 150.45 and 28.05 (0.30, 0.59 and 0.11 of 255) for the
/// red, green and blue channel. A full-white colour therefore reads 254, not 255; every caller turns the level into
/// <c>30 × (255 − level) / 255</c> with integer division, which is 0 for 254 as for 255.
/// Steamwoven reads the colour's 8-bit channels (the entry <c>i</c> over 255). The original indexes the display's channel values (a 16-bit
/// display has 5/6/5 bits, so its channel is quantised first); the display depth is a run-time property of the player's machine and is not
/// modelled (NOT_VERIFIED). The entries are computed in double precision; where a quotient lands on an integer (the weights 150.45 and 28.05 are not exact
/// doubles) the original's FPU precision mode could differ by one in an entry, which is not modelled either.
/// </summary>
public static class LightLuminance
{
    /// <summary>The level of a full-white colour (0xFF in each table).</summary>
    public const int White = 254;

    private const double RedWeight = 76.5, GreenWeight = 150.45, BlueWeight = 28.05;
    private const int ChannelMax = 255;

    /// <summary>The grey level 0..254 of <paramref name="color"/>: the sum of its three channels' table entries.</summary>
    [OriginalFunction(0x4DCE10, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x4D9240, OriginalFunctionScope.Partial)]
    public static int Gray(AmbientColor color) =>
        Entry(color.R, RedWeight) + Entry(color.G, GreenWeight) + Entry(color.B, BlueWeight);

    // The table build: an int to double divide by the channel's largest value, the weight, then the truncating conversion.
    private static int Entry(byte value, double weight) => (int)((double)value / ChannelMax * weight);
}
