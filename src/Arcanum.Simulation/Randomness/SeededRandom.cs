namespace Arcanum.Simulation.Randomness;

/// <summary>
/// Seeded, replayable gameplay randomness (ARC-WORLD-019): the SplitMix64 sequence of a 64-bit <see cref="Seed"/>
/// (Steele, Lea and Flood, 2014; the public-domain reference by S. Vigna). Draw <c>k</c> (1-based) is a pure function
/// of the seed and <c>k</c>, so the whole generator state is the seed and the count of draws made
/// (<see cref="Draws"/>), and a generator rebuilt from both continues the same sequence.
/// An inclusive integer range takes one draw, reduced by rejection so every value is equally likely; a draw in the
/// rejected low tail (under 2^-32 of draws) is replaced by the next one. Equal bounds draw nothing.
/// This is a project generator, not the original's: see <c>docs/world/session-random-runtime.md</c>.
/// Caller-serialized; no locks, clocks or global state.
/// </summary>
public sealed class SeededRandom
{
    /// <summary>The SplitMix64 increment (the odd integer nearest to 2^64 divided by the golden ratio).</summary>
    public const ulong Gamma = 0x9E3779B97F4A7C15UL;

    /// <summary>Starts the sequence of <paramref name="seed"/> after <paramref name="draws"/> draws (0 for a new one).</summary>
    public SeededRandom(ulong seed, long draws = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(draws);
        Seed = seed;
        Draws = draws;
    }

    public ulong Seed { get; }

    /// <summary>Draws made so far: the position in the sequence. <see cref="long.MaxValue"/> is the end.</summary>
    public long Draws { get; private set; }

    /// <summary>Draw number <paramref name="index"/> (1-based) of the sequence of <paramref name="seed"/>.</summary>
    public static ulong Output(ulong seed, long index)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(index);
        var z = unchecked(seed + (ulong)index * Gamma);
        z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
        z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
        return z ^ (z >> 31);
    }

    /// <summary>
    /// A uniform integer in <c>min..max</c> inclusive; <paramref name="min"/> above <paramref name="max"/> throws.
    /// Returns false (value 0), with no draw made, when the sequence would run past <see cref="long.MaxValue"/> draws.
    /// </summary>
    public bool TryNext(int min, int max, out int value)
    {
        if (min > max) throw new ArgumentOutOfRangeException(nameof(min), "min must not exceed max");
        value = min;
        if (min == max) return true;
        var span = (ulong)((long)max - min + 1);
        // 2^64 mod span: draws below it would make the low values of the range more likely.
        var threshold = unchecked(0UL - span) % span;
        var position = Draws;
        ulong draw;
        do
        {
            if (position == long.MaxValue)
            {
                value = 0;
                return false;
            }
            draw = Output(Seed, ++position);
        } while (draw < threshold);
        Draws = position;
        value = (int)(min + (long)(draw % span));
        return true;
    }
}
