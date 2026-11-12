namespace Arcanum.Presentation.Audio;

/// <summary>What the scenery loop's begin node does with the object's looping sound (see <see cref="SceneryLoopSoundRules"/>).</summary>
public enum SceneryLoopSoundDecision { None, Start, Stop }

/// <summary>
/// ARC-RENDER-1126: the looping sound of an animated scenery (docs/audio/positional-sound.md; ledger SN26, AS3, checked in the
/// 1.0.7.4 executable at the loop's begin node 0x42DA50 and the size and radius helpers 0x41B980, 0x41B9E0). The begin node plays the
/// object's misc sound of kind 2 (<c>MiscSoundAction.Animating</c>, base + 2) as a looping positional
/// sound when the local PC is within the sound's range (the run's flag 0x20000 also skips it; what sets that flag is not read here): the range is the maximum radius of the object's size class divided by 40 (the tile's screen width, truncating), the
/// distance the larger of the two coordinate differences of the object's and the PC's tiles (the Chebyshev distance of AS4). A voice
/// that exists is left alone within the range and stopped beyond it; with no voice the sound starts within the range. The original
/// tests this at the begin only: a loop that began far away starts no sound later, and one that began near keeps its voice, whose
/// volume the positional rules take down to 0 when the listener moves away (there is no further stop). Engine-free; the host keeps the
/// voices.
/// </summary>
public static class SceneryLoopSoundRules
{
    /// <summary>The divisor turning a size class's maximum radius (projected screen units) into tiles (0x28).</summary>
    public const int TileWidth = 40;

    /// <summary>The sound range of a size class in tiles: its maximum radius divided by <see cref="TileWidth"/>, truncating (0x41B9E0).</summary>
    public static long RangeTiles(PositionalProfile profile, PositionalSize size)
    {
        ArgumentNullException.ThrowIfNull(profile);
        // The original reads the radius as a 32-bit int (0x41B9E0), as SoundSizeParameters.RangeTiles does.
        return unchecked((int)profile[size].MaxRadius) / TileWidth;
    }

    /// <summary>The Chebyshev distance of two tiles: the larger coordinate difference (0x4B96F0).</summary>
    public static long Distance(long x, long y, long otherX, long otherY) => Math.Max(Math.Abs(x - otherX), Math.Abs(y - otherY));

    /// <summary>
    /// The begin node's decision: <see cref="SceneryLoopSoundDecision.Stop"/> when the PC is farther than the range and a voice exists,
    /// <see cref="SceneryLoopSoundDecision.Start"/> when the PC is within it (inclusive) and no voice exists, else nothing.
    /// </summary>
    public static SceneryLoopSoundDecision Decide(long rangeTiles, long distanceTiles, bool hasVoice) =>
        rangeTiles < distanceTiles ? (hasVoice ? SceneryLoopSoundDecision.Stop : SceneryLoopSoundDecision.None)
        : hasVoice ? SceneryLoopSoundDecision.None : SceneryLoopSoundDecision.Start;
}
