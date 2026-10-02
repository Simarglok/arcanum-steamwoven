using System.Numerics;

namespace Arcanum.Simulation.Movement;

/// <summary>A canonical finite fraction. The default value is invalid, not zero.</summary>
public readonly record struct ExactScalar : IComparable<ExactScalar>
{
    private ExactScalar(Int128 numerator, Int128 denominator)
    {
        Numerator = numerator;
        Denominator = denominator;
    }

    public Int128 Numerator { get; }
    public Int128 Denominator { get; }
    public bool IsValid => Denominator > 0;
    public static ExactScalar Zero => new(0, 1);
    public static ExactScalar Create(Int128 numerator, Int128 denominator) => Reduce(numerator, denominator);
    public static ExactScalar Integer(Int128 value) => new(value, 1);

    // Scratch operands are exclusively fixed-width fields. No public arbitrary-precision input.
    private static ExactScalar Reduce(BigInteger n, BigInteger d)
    {
        if (d <= 0) throw new ArgumentOutOfRangeException(nameof(d));
        var gcd = BigInteger.GreatestCommonDivisor(n, d);
        return new(checked((Int128)(n / gcd)), checked((Int128)(d / gcd)));
    }

    private void RequireValid()
    {
        if (!IsValid) throw new ArgumentException("Invalid exact scalar.");
    }

    public int CompareTo(ExactScalar other)
    {
        RequireValid();
        other.RequireValid();
        return ((BigInteger)Numerator * other.Denominator).CompareTo((BigInteger)other.Numerator * Denominator);
    }

    public static ExactScalar operator +(ExactScalar a, ExactScalar b)
    {
        a.RequireValid(); b.RequireValid();
        return Reduce((BigInteger)a.Numerator * b.Denominator + (BigInteger)b.Numerator * a.Denominator,
            (BigInteger)a.Denominator * b.Denominator);
    }
    public static ExactScalar operator -(ExactScalar a, ExactScalar b)
    {
        a.RequireValid(); b.RequireValid();
        return Reduce((BigInteger)a.Numerator * b.Denominator - (BigInteger)b.Numerator * a.Denominator,
            (BigInteger)a.Denominator * b.Denominator);
    }
    public static ExactScalar operator *(ExactScalar a, ExactScalar b)
    {
        a.RequireValid(); b.RequireValid();
        return Reduce((BigInteger)a.Numerator * b.Numerator, (BigInteger)a.Denominator * b.Denominator);
    }
    public static ExactScalar operator /(ExactScalar a, ExactScalar b)
    {
        a.RequireValid(); b.RequireValid();
        if (b.Numerator == 0) throw new DivideByZeroException();
        var n = (BigInteger)a.Numerator * b.Denominator;
        var d = (BigInteger)a.Denominator * b.Numerator;
        return d < 0 ? Reduce(-n, -d) : Reduce(n, d);
    }
    public static bool operator <(ExactScalar a, ExactScalar b) => a.CompareTo(b) < 0;
    public static bool operator >(ExactScalar a, ExactScalar b) => a.CompareTo(b) > 0;
    public static bool operator <=(ExactScalar a, ExactScalar b) => a.CompareTo(b) <= 0;
    public static bool operator >=(ExactScalar a, ExactScalar b) => a.CompareTo(b) >= 0;
    public override string ToString() => IsValid ? $"{Numerator}/{Denominator}" : "INVALID";
}

public readonly record struct SimulationTime(ExactScalar Value)
{
    public bool IsValid => Value.IsValid && Value.Numerator >= 0;
    public static SimulationTime At(Int128 numerator) => new(ExactScalar.Integer(numerator));
    public static SimulationTime At(Int128 numerator, Int128 denominator) => new(ExactScalar.Create(numerator, denominator));
}

public readonly record struct StepDuration(ExactScalar Value)
{
    public bool IsValid => Value.IsValid && Value.Numerator > 0;
    public static StepDuration Of(Int128 numerator) => new(ExactScalar.Integer(numerator));
    public static StepDuration Of(Int128 numerator, Int128 denominator) => new(ExactScalar.Create(numerator, denominator));
}
