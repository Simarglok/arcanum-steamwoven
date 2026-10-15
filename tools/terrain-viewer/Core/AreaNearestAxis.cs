using System.Numerics;

namespace Arcanum.TerrainViewer.Core;

/// <summary>Exact binary-rational screen-center sampling. No GPU UV tie policy.</summary>
public sealed class AreaNearestAxis
{
    private readonly BigInteger _step, _bias, _denominator;
    private readonly int _sourceStart, _sourceSize;
    private readonly bool _reflected;

    public AreaNearestAxis(float origin, float zoom, int destinationStart, int destinationSize,
        int sourceStart, int sourceSize, bool reflected)
    {
        if (!float.IsFinite(origin) || !float.IsFinite(zoom) || zoom <= 0 || destinationSize <= 0
            || sourceStart < 0 || sourceSize <= 0 || (long)sourceStart + sourceSize > 65534)
            throw new InvalidOperationException("AREA_NEAREST_AXIS_DOMAIN");
        var (on, od) = Rational(origin); var (zn, zd) = Rational(zoom);
        _step = 2 * od * zd * sourceSize;
        _bias = (od * zd - 2 * on * zd - 2 * od * zn * destinationStart) * sourceSize;
        _denominator = 2 * od * zn * destinationSize;
        var divisor = BigInteger.GreatestCommonDivisor(BigInteger.GreatestCommonDivisor(_step, _bias), _denominator);
        _step /= divisor; _bias /= divisor; _denominator /= divisor;
        _sourceStart = sourceStart; _sourceSize = sourceSize; _reflected = reflected;
    }

    public int SourceIndex(int screenPixel)
    {
        var numerator = _step * screenPixel + _bias;
        if (numerator < 0 || numerator >= _denominator * _sourceSize) return -1;
        var index = (int)(numerator / _denominator);
        return _sourceStart + (_reflected ? _sourceSize - 1 - index : index);
    }

    public string[] CoordinateRatio(int screenPixel)
        => new[] { (_step * screenPixel + _bias).ToString(System.Globalization.CultureInfo.InvariantCulture),
            _denominator.ToString(System.Globalization.CultureInfo.InvariantCulture) };

    private static (BigInteger Numerator, BigInteger Denominator) Rational(float value)
    {
        var bits = BitConverter.SingleToUInt32Bits(value);
        var exponent = (int)((bits >> 23) & 255);
        var mantissa = new BigInteger(bits & 0x7fffff);
        if (exponent != 0) mantissa += 1 << 23;
        var power = exponent == 0 ? -149 : exponent - 150;
        if ((bits & 0x80000000) != 0) mantissa = -mantissa;
        return power >= 0 ? (mantissa << power, BigInteger.One) : (mantissa, BigInteger.One << -power);
    }
}
