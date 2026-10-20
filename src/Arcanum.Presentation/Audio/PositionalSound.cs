namespace Arcanum.Presentation.Audio;

/// <summary>A point in the original's sound coordinates: the isometric screen projection of a tile (x = 40 × (Y − X − 1),
/// y = 20 × (X + Y)), relative to a fixed origin the host chooses (distances do not depend on it).</summary>
public readonly record struct SoundPoint(long X, long Y);

/// <summary>A size class's radii (projected screen units) and the volume percentage applied after attenuation.</summary>
public sealed record PositionalRange(long MinRadius, long MaxRadius, int VolumePercent);

/// <summary>The positional profile the host passes: one range per size (small, medium, large, extra-large) and the pan
/// distances.</summary>
public sealed record PositionalProfile(PositionalRange Small, PositionalRange Medium, PositionalRange Large, PositionalRange ExtraLarge,
    long MinPanDistance, long MaxPanDistance)
{
    public PositionalRange this[PositionalSize size] => size switch
    {
        PositionalSize.Small => Small,
        PositionalSize.Medium => Medium,
        PositionalSize.Large => Large,
        PositionalSize.ExtraLarge => ExtraLarge,
        _ => throw new ArgumentOutOfRangeException(nameof(size)),
    };
}

/// <summary>The size of a sounding object (the original's order).</summary>
public enum PositionalSize { Small, Medium, Large, ExtraLarge }

/// <summary>A positional sound's volume (0–127, before the effects channel) and balance (0 left, 64 centre, 127 right).</summary>
public readonly record struct PositionalLevel(int Volume, int Balance);

/// <summary>
/// ARC-UI-042: the original's positional sound rules (docs/audio/positional-sound.md; checked in the 1.0.7.4 executable,
/// docs/research/audio-evidence.md SN4–SN7). Volume falls linearly from 127 at the minimum radius to 0 at the maximum
/// radius of the source's size, over a distance that counts screen y twice and is rounded to the nearest unit, then takes the
/// size's volume percentage; balance moves linearly from the centre between the minimum and maximum pan distance by the
/// horizontal offset alone. Engine-free; the host keeps the listener and the live sounds.
/// </summary>
public static class PositionalSoundRules
{
    public const int Centre = 64;
    /// <summary>The largest tile coordinate the rules accept (far beyond any map), so every product stays exact.</summary>
    public const long MaxCoordinate = 1L << 24;

    /// <summary>The sound point of the tile (<paramref name="x"/>, <paramref name="y"/>): the original's isometric
    /// projection, without its view origin.</summary>
    [OriginalFunction(0x4B8680, OriginalFunctionScope.Partial)]
    public static SoundPoint Point(long x, long y)
    {
        if (x is < -MaxCoordinate or > MaxCoordinate) throw new ArgumentOutOfRangeException(nameof(x));
        if (y is < -MaxCoordinate or > MaxCoordinate) throw new ArgumentOutOfRangeException(nameof(y));
        return new(40 * (y - x - 1), 20 * (x + y));
    }

    /// <summary>The size of an object: scenery (type 3) with scenery flag 0x40 is small, 0x80 medium, 0x100 extra-large;
    /// everything else is large.</summary>
    [OriginalFunction(0x41B980, OriginalFunctionScope.Full)]
    public static PositionalSize Size(int type, uint sceneryFlags)
    {
        if (type != 3) return PositionalSize.Large;
        if ((sceneryFlags & 0x40) != 0) return PositionalSize.Small;
        if ((sceneryFlags & 0x80) != 0) return PositionalSize.Medium;
        return (sceneryFlags & 0x100) != 0 ? PositionalSize.ExtraLarge : PositionalSize.Large;
    }

    /// <summary>
    /// The volume and balance of a sound at <paramref name="source"/> heard at <paramref name="listener"/>. Distance:
    /// <c>sqrt(dx² + (2·dy)²)</c> in single precision, rounded to the nearest (ties to even). Volume 127 below the minimum
    /// radius, 0 above the maximum, else <c>127 × (min − d) / (max − min) + 127</c> (truncated, clamped 0–127; 127 when the
    /// range is empty), then <c>× percent / 100</c>. Balance 64 within the minimum pan distance, hard left or right beyond the
    /// maximum, else <c>64 ± 64 × (|dx| − min) / (max − min)</c> (truncated, clamped 0–127).
    /// </summary>
    [OriginalFunction(0x41B420, OriginalFunctionScope.Full)]
    public static PositionalLevel Level(SoundPoint source, SoundPoint listener, PositionalProfile profile, PositionalSize size)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var range = profile[size];
        var dx = source.X - listener.X;
        var dy = 2 * (source.Y - listener.Y);
        var distance = (long)Math.Round((float)Math.Sqrt((double)(dx * dx + dy * dy)), MidpointRounding.ToEven);
        var offset = Math.Abs(dx);

        var volume = AudioVolumeRules.MaxVolume;
        if (distance < range.MinRadius) volume = AudioVolumeRules.MaxVolume;
        else if (distance > range.MaxRadius) volume = 0;
        else if (range.MaxRadius > range.MinRadius)
            volume = (int)Math.Clamp(AudioVolumeRules.MaxVolume * (range.MinRadius - distance) / (range.MaxRadius - range.MinRadius) + AudioVolumeRules.MaxVolume,
                0, AudioVolumeRules.MaxVolume);

        var balance = Centre;
        if (source.X != listener.X)
        {
            var sign = source.X < listener.X ? -1 : 1;
            if (offset > profile.MaxPanDistance) balance = sign < 0 ? 0 : AudioVolumeRules.MaxVolume;
            else if (offset > profile.MinPanDistance)
                balance = (int)Math.Clamp(sign * Centre * (offset - profile.MinPanDistance) / (profile.MaxPanDistance - profile.MinPanDistance) + Centre,
                    0, AudioVolumeRules.MaxVolume);
        }
        return new(range.VolumePercent * volume / 100, balance);
    }

    /// <summary>The backend pan (−1 left … 1 right) of a balance: <c>(balance − 64) / 64</c>, clamped (a Steamwoven mapping;
    /// the original hands the balance to its mixer).</summary>
    public static double Pan(int balance) => Math.Clamp((balance - Centre) / (double)Centre, -1, 1);
}
