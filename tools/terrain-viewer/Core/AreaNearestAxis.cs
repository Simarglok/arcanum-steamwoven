using System.Globalization;
using System.Numerics;

namespace Arcanum.TerrainViewer.Core;

/// <summary>Exact binary-rational screen-center sampling. No GPU UV tie policy.
/// ARC-RENDER-3418: a value of no heap memory for the origins and zooms a view uses (the exact integers fit 128 bits);
/// any other single-precision input takes the same rule in big integers.</summary>
public readonly struct AreaNearestAxis
{
    // The largest binary exponent and integer part of an origin or a zoom the 128-bit path takes: with them the step,
    // the bias and the denominator stay far inside the bounds of AreaNearestRowKey.
    private const int NarrowBits = 32;
    private readonly AreaNearestRowKey _rule;

    public AreaNearestAxis(float origin, float zoom, int destinationStart, int destinationSize,
        int sourceStart, int sourceSize, bool reflected)
    {
        if (!float.IsFinite(origin) || !float.IsFinite(zoom) || zoom <= 0 || destinationSize <= 0
            || sourceStart < 0 || sourceSize <= 0 || (long)sourceStart + sourceSize > 65534)
            throw new InvalidOperationException("AREA_NEAREST_AXIS_DOMAIN");
        var (om, op) = Binary(origin); var (zm, zp) = Binary(zoom);
        if (Narrow(om, op) && Narrow(zm, zp))
        {
            var (on, od) = Fraction(om, op); var (zn, zd) = Fraction(zm, zp);
            _rule = AreaNearestRowKey.Reduced(2 * od * zd * sourceSize,
                (od * zd - 2 * on * zd - 2 * od * zn * destinationStart) * sourceSize,
                2 * od * zn * destinationSize, sourceStart, sourceSize, reflected);
        }
        else
        {
            var (on, od) = BigFraction(om, op); var (zn, zd) = BigFraction(zm, zp);
            _rule = AreaNearestRowKey.Reduced(2 * od * zd * sourceSize,
                (od * zd - 2 * on * zd - 2 * od * zn * destinationStart) * sourceSize,
                2 * od * zn * destinationSize, sourceStart, sourceSize, reflected);
        }
    }

    /// <summary>True only when the centre of <paramref name="screenPixel"/> certainly lies outside the destination's span
    /// on the screen, so <see cref="SourceIndex"/> of that axis would be -1: a cheap test before the exact rule. False
    /// near the edges and for inputs the constructor refuses.</summary>
    public static bool CertainlyOutside(float origin, float zoom, int destinationStart, int destinationSize, int screenPixel)
    {
        if (!float.IsFinite(origin) || !float.IsFinite(zoom) || zoom <= 0 || destinationSize <= 0) return false;
        double low = origin + (double)zoom * destinationStart, high = low + (double)zoom * destinationSize;
        // One whole pixel, and far more than the rounding of the two sums above.
        var margin = 1 + (Math.Abs((double)origin) + zoom * (Math.Abs((double)destinationStart) + destinationSize)) * 1e-12;
        var centre = screenPixel + 0.5;
        return centre < low - margin || centre > high + margin;
    }

    public int SourceIndex(int screenPixel) => _rule.SourceIndex(screenPixel);

    /// <summary>The exact rule for screen pixels <c>start + i</c>, as a function of <c>i</c>
    /// alone. Equal keys select equal source indices for every <c>i</c>, so one index row
    /// serves every item and camera translation with that key. Shifting the origin and
    /// <paramref name="start"/> by the same integer leaves the key unchanged.</summary>
    public AreaNearestRowKey RowKey(int start) => _rule.From(start);

    public string[] CoordinateRatio(int screenPixel) => _rule.Ratio(screenPixel);

    // value = mantissa * 2^power, the mantissa odd or zero.
    private static (long Mantissa, int Power) Binary(float value)
    {
        var bits = BitConverter.SingleToUInt32Bits(value);
        var exponent = (int)((bits >> 23) & 255);
        long mantissa = bits & 0x7fffff;
        if (exponent != 0) mantissa += 1 << 23;
        if (mantissa == 0) return (0, 0);
        var zeros = BitOperations.TrailingZeroCount(mantissa);
        var power = (exponent == 0 ? -149 : exponent - 150) + zeros;
        mantissa >>= zeros;
        return ((bits & 0x80000000) != 0 ? -mantissa : mantissa, power);
    }

    private static bool Narrow(long mantissa, int power) => power < 0
        ? -power <= NarrowBits && Math.Abs(mantissa) < 1L << NarrowBits
        : power < NarrowBits && Math.Abs(mantissa) < 1L << (NarrowBits - power);

    private static (Int128 Numerator, Int128 Denominator) Fraction(long mantissa, int power) =>
        power >= 0 ? ((Int128)mantissa << power, Int128.One) : (mantissa, Int128.One << -power);

    private static (BigInteger Numerator, BigInteger Denominator) BigFraction(long mantissa, int power) =>
        power >= 0 ? ((BigInteger)mantissa << power, BigInteger.One) : (mantissa, BigInteger.One << -power);
}

/// <summary>Translation-invariant identity of one exact nearest index row: the source index of row position i is
/// floor((step * i + bias) / denominator), kept when it lies in the source. The three integers are the reduced ones, held
/// in 128 bits while the step is at most 2^90, the bias at most 2^120 and the denominator at most 2^100 in magnitude, so
/// no product or sum of a row overflows; a wider rule is <see cref="Wide"/> and the three are zero. The form follows from
/// the values alone, so equal rules are equal keys.</summary>
public readonly record struct AreaNearestRowKey
{
    private static readonly Int128 StepBound = Int128.One << 90, BiasBound = Int128.One << 120, DenominatorBound = Int128.One << 100;

    private AreaNearestRowKey(Int128 step, Int128 bias, Int128 denominator, AreaNearestWideRule? wide, int sourceStart, int sourceSize, bool reflected)
        => (Step, Bias, Denominator, Wide, SourceStart, SourceSize, Reflected) = (step, bias, denominator, wide, sourceStart, sourceSize, reflected);

    public Int128 Step { get; }
    public Int128 Bias { get; }
    public Int128 Denominator { get; }
    public AreaNearestWideRule? Wide { get; }
    public int SourceStart { get; }
    public int SourceSize { get; }
    public bool Reflected { get; }

    /// <summary>Source index for row position <paramref name="offset"/>, or -1 when excluded.</summary>
    public int SourceIndex(int offset)
    {
        int index;
        if (Wide is { } wide)
        {
            var numerator = wide.Step * offset + wide.Bias;
            if (numerator < 0 || numerator >= wide.Denominator * SourceSize) return -1;
            index = (int)(numerator / wide.Denominator);
        }
        else
        {
            var numerator = Step * offset + Bias;
            if (numerator < 0 || numerator >= Denominator * SourceSize) return -1;
            index = numerator <= ulong.MaxValue && Denominator <= ulong.MaxValue
                ? (int)((ulong)numerator / (ulong)Denominator) : (int)(numerator / Denominator);
        }
        return SourceStart + (Reflected ? SourceSize - 1 - index : index);
    }

    /// <summary>The same rule counted from row position <paramref name="start"/>.</summary>
    internal AreaNearestRowKey From(int start) => Wide is { } wide
        ? Reduced(wide.Step, wide.Bias + wide.Step * start, wide.Denominator, SourceStart, SourceSize, Reflected)
        : Reduced(Step, Bias + Step * start, Denominator, SourceStart, SourceSize, Reflected);

    internal string[] Ratio(int offset) => Wide is { } wide
        ? [(wide.Step * offset + wide.Bias).ToString(CultureInfo.InvariantCulture), wide.Denominator.ToString(CultureInfo.InvariantCulture)]
        : [(Step * offset + Bias).ToString(CultureInfo.InvariantCulture), Denominator.ToString(CultureInfo.InvariantCulture)];

    // The step and the denominator are positive; the callers keep |bias| below 2^123, so its negation cannot overflow.
    internal static AreaNearestRowKey Reduced(Int128 step, Int128 bias, Int128 denominator, int sourceStart, int sourceSize, bool reflected)
    {
        var divisor = (Int128)Divisor(Divisor((UInt128)step, (UInt128)(bias < 0 ? -bias : bias)), (UInt128)denominator);
        if (divisor != 1) { step /= divisor; bias /= divisor; denominator /= divisor; }
        return step <= StepBound && bias <= BiasBound && bias >= -BiasBound && denominator <= DenominatorBound
            ? new(step, bias, denominator, null, sourceStart, sourceSize, reflected)
            : new(0, 0, 0, new(step, bias, denominator), sourceStart, sourceSize, reflected);
    }

    internal static AreaNearestRowKey Reduced(BigInteger step, BigInteger bias, BigInteger denominator, int sourceStart, int sourceSize, bool reflected)
    {
        var divisor = BigInteger.GreatestCommonDivisor(BigInteger.GreatestCommonDivisor(step, bias), denominator);
        step /= divisor; bias /= divisor; denominator /= divisor;
        return step <= (BigInteger)StepBound && BigInteger.Abs(bias) <= (BigInteger)BiasBound && denominator <= (BigInteger)DenominatorBound
            ? new((Int128)step, (Int128)bias, (Int128)denominator, null, sourceStart, sourceSize, reflected)
            : new(0, 0, 0, new(step, bias, denominator), sourceStart, sourceSize, reflected);
    }

    private static UInt128 Divisor(UInt128 a, UInt128 b)
    {
        while (b != 0)
        {
            if (a <= ulong.MaxValue && b <= ulong.MaxValue)
            {
                ulong x = (ulong)a, y = (ulong)b;
                while (y != 0) (x, y) = (y, x % y);
                return x;
            }
            (a, b) = (b, a % b);
        }
        return a;
    }
}

/// <summary>The reduced integers of a row rule that is too wide for 128 bits.</summary>
public sealed record AreaNearestWideRule(BigInteger Step, BigInteger Bias, BigInteger Denominator);
