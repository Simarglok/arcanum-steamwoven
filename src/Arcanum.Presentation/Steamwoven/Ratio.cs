namespace Arcanum.Presentation.Steamwoven;

/// <summary>
/// ARC-UI-2450 (SWI-LAY-5): an exact fraction of two whole numbers, always reduced, so the Steamwoven scale, the choice of the baked step and the
/// test for an exact step never meet floating-point equality. The default value is 0. Magnitudes stay small (screen sizes times grid constants),
/// so plain 64-bit arithmetic is enough; <see cref="SteamwovenLayoutBuilder"/> refuses sizes where it would not be.
/// </summary>
public readonly record struct Ratio : IComparable<Ratio>
{
    // The denominator is stored minus one so that default(Ratio) is 0/1 and not a zero denominator.
    private readonly long _numerator;
    private readonly long _denominatorMinusOne;

    public Ratio(long numerator, long denominator = 1)
    {
        if (denominator == 0) throw new ArgumentOutOfRangeException(nameof(denominator), "a fraction needs a non-zero denominator");
        if (denominator < 0) { numerator = -numerator; denominator = -denominator; }
        var divisor = Gcd(Math.Abs(numerator), denominator);
        _numerator = numerator / divisor;
        _denominatorMinusOne = denominator / divisor - 1;
    }

    public long Numerator => _numerator;
    public long Denominator => _denominatorMinusOne + 1;

    public static Ratio Zero => default;
    public static Ratio One => new(1);

    public static implicit operator Ratio(long whole) => new(whole);

    public static Ratio operator +(Ratio a, Ratio b) => new(a.Numerator * b.Denominator + b.Numerator * a.Denominator, a.Denominator * b.Denominator);
    public static Ratio operator -(Ratio a, Ratio b) => new(a.Numerator * b.Denominator - b.Numerator * a.Denominator, a.Denominator * b.Denominator);
    public static Ratio operator *(Ratio a, Ratio b) => new(a.Numerator * b.Numerator, a.Denominator * b.Denominator);

    public static Ratio operator /(Ratio a, Ratio b)
    {
        if (b.Numerator == 0) throw new DivideByZeroException();
        return new Ratio(a.Numerator * b.Denominator, a.Denominator * b.Numerator);
    }

    public static bool operator <(Ratio a, Ratio b) => a.CompareTo(b) < 0;
    public static bool operator >(Ratio a, Ratio b) => a.CompareTo(b) > 0;
    public static bool operator <=(Ratio a, Ratio b) => a.CompareTo(b) <= 0;
    public static bool operator >=(Ratio a, Ratio b) => a.CompareTo(b) >= 0;

    public int CompareTo(Ratio other) => (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);

    public static Ratio Max(Ratio a, Ratio b) => a >= b ? a : b;

    /// <summary>The nearest whole number; a half goes up (SWI-LAY-7), also below zero (-1/2 gives 0).</summary>
    public int RoundHalfUp()
    {
        var twice = checked(2 * Numerator + Denominator);
        var step = 2 * Denominator;
        var floor = twice / step;
        if (twice % step < 0) floor--;
        return checked((int)floor);
    }

    public double ToDouble() => (double)Numerator / Denominator;

    public override string ToString() => Denominator == 1 ? Numerator.ToString() : $"{Numerator}/{Denominator}";

    private static long Gcd(long a, long b)
    {
        while (b != 0) (a, b) = (b, a % b);
        return a == 0 ? 1 : a;
    }
}
