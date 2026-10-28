namespace Arcanum.Simulation.Randomness;

/// <summary>
/// ARC-WORLD-098: the original's one random generator (ledger RK11, read in the retail 1.0.7.4 executable): a 32-bit linear
/// congruential state (global 0x601778) stepped as <c>state × 0x19660D + 0x3C6EF35F</c> (wrapping), each draw bits 8..22 of the new
/// state (0..32767). The game seeds it from the clock at start (0x4CEEE0); a container generated once (flag 0x200) seeds it from the
/// PC and the container's place before its draws (<see cref="Items.RestockRules.GeneratedOnceSeed"/>). The session keeps its own
/// generator (<see cref="SeededRandom"/>) for every other draw; this one serves a draw sequence the original makes reproducible.
/// Caller-serialized; no locks, clocks or global state.
/// </summary>
public sealed class OriginalRandom
{
    /// <summary>The multiplier and increment of the state step (0x4CEF50).</summary>
    public const uint Multiplier = 0x19660D, Increment = 0x3C6EF35F;
    /// <summary>The largest draw: bits 8..22 of the state.</summary>
    public const int MaxDraw = 0x7FFF;

    /// <summary>Starts the generator at <paramref name="seed"/> (0x4CEED0 → 0x4CEF40).</summary>
    public OriginalRandom(int seed) => Seed(seed);

    /// <summary>The 32-bit state (global 0x601778).</summary>
    public uint State { get; private set; }

    /// <summary>Sets the state to <paramref name="seed"/> as it is (0x4CEF40; 0x4CEED0 passes its argument on).</summary>
    [OriginalFunction(0x4CEF40, OriginalFunctionScope.Full)]
    public void Seed(int seed) => State = unchecked((uint)seed);

    /// <summary>One draw (0x4CEF50): the state steps once and its bits 8..22 are the draw, 0..32767.</summary>
    [OriginalFunction(0x4CEF50, OriginalFunctionScope.Full)]
    public int Next()
    {
        State = unchecked(State * Multiplier + Increment);
        return (int)((State >> 8) & MaxDraw);
    }

    /// <summary>
    /// An inclusive integer between two bounds in either order (0x4CEF00): equal bounds draw nothing; otherwise one draw modulo the
    /// range's width plus the lower bound, in signed 32-bit arithmetic (the width wraps to a negative divisor for a range over 2^31
    /// values; the signed remainder of the non-negative draw still lands in the range). The draw is under 32,768, so a range wider than
    /// that never reaches its upper part. The whole 32-bit range wraps the width to 0, where the original divides by zero (a technical
    /// bug, fixed in both modes without a switch): its draw is the lower bound plus the draw, as a width of 2^32 would give.
    /// </summary>
    [OriginalFunction(0x4CEF00, OriginalFunctionScope.Partial)]
    public int Between(int first, int second)
    {
        if (first == second) return first;
        var (low, high) = first < second ? (first, second) : (second, first);
        var draw = Next();
        var width = unchecked(high - low + 1);
        return width == 0 ? unchecked(low + draw) : unchecked(draw % width + low);
    }
}
