namespace Arcanum.Simulation.Combat;

/// <summary>
/// ARC-RENDER-040: how the original plays a critter's dying animation and its knockdown fall (read in the retail 1.0.7.4 executable,
/// ledger "Dying animation" DA1–DA6). The dying goal (31) first asks whether the critter lies in its fall (<see cref="LiesFallen"/>,
/// 0x45D730): then it keeps that pose and goes on to its pool node at once. Otherwise its node 0x42BC10 sets frame 0 of the animation the
/// kill chose and its start (<see cref="Start"/>, 0x42D570) stores the frame delay; the goal runner then runs its frame step (0x42D6F0) at once and once per delay, one frame
/// each time, until the step finds the last frame: the goal ends there and the frame stays (<see cref="Frame"/>). Only then do the
/// goal's pool node (0x42FED0, <see cref="BloodPoolRules.Leaves"/>) and its end (0x42FF40: the dead flags, the dying item event, the
/// PC's death screen) run (<see cref="EndMs"/>). The knockdown goal (62) plays the fall-down animation from frame 0 alike: its start
/// (0x42E4B0, <see cref="Delay"/>) stores the same delay and the animate step (0x42CC80) steps one frame per delay to the last.
/// Godot-free; the caller keeps the clock, the art and what the critter shows.
/// </summary>
public static class DyingAnimationRules
{
    public const int FallDown = 7, Decapitation = 17, BlownOutChunk = 18, SeveredLeg = 19;
    /// <summary>The frame delay both starts store when the ART's header does not read.</summary>
    public const int UnreadArtDelayMs = 100;

    /// <summary>
    /// ARC-UI-1737 (ledger OH2): the dying animation the art node (0x42BC10) sets: the one the kill stored, or with the "violence filter" setting
    /// on (a plain read of the global 0x5DE6A4) the fall-down 7 whatever the kill stored. The filter-off repair to animation 18 when the art of
    /// the stored one is missing is not here (not built: the world has no "art exists" port); with the filter on the original does not repair at all: a missing art 7
    /// leaves the art as it is and the goal goes on to its pool node (not built either).
    /// </summary>
    [OriginalFunction(0x42BC10, OriginalFunctionScope.Partial)]
    public static int Chosen(int stored, bool violenceFilter) => violenceFilter ? FallDown : stored;

    /// <summary>
    /// The dying goal's third node (0x426F10 → 0x45D730): a PC or NPC that is neither destroyed nor off and whose art shows the fall-down
    /// animation (7, at any frame: a critter knocked out or knocked down) skips the dying animation; the goal goes to its pool node at
    /// once and the critter keeps its fall. Not modelled here: the object flags and type tests (the caller's critter).
    /// </summary>
    [OriginalFunction(0x45D730, OriginalFunctionScope.Partial)]
    public static bool LiesFallen(int shownAnimation) => shownAnimation == FallDown;

    /// <summary>
    /// The dying goal's start (0x42D570), after 0x42BC10 has set the dying animation at frame 0 (so in the goal its own test does not
    /// refuse): a critter whose art already shows the fall or a bloody death (7, 17..19) past its frame 0 lies
    /// already, so its art is not changed and the goal goes on to its pool node at once (null); otherwise the art becomes the kill's
    /// dying animation at frame 0 and the delay is <see cref="Delay"/>. Not modelled here: the type test (a PC or NPC only), the dying or
    /// gruesome-dying sound (animation 7 or another) and the art read itself.
    /// </summary>
    [OriginalFunction(0x42D570, OriginalFunctionScope.Partial)]
    public static int? Start(int shownAnimation, int shownFrame, int framesPerSecond) =>
        (shownAnimation is FallDown or (>= Decapitation and <= SeveredLeg)) && shownFrame > 0 ? null : Delay(framesPerSecond);

    /// <summary>
    /// The frame delay a start stores: 1000 / the ART's frame rate (unsigned integer division), <see cref="UnreadArtDelayMs"/> when the
    /// header does not read. The knockdown's start (0x42E4B0) stores it the same way after it sets the fall-down animation at frame 0.
    /// A rate of 0 or below takes the unread delay here (the original would divide by zero; no installed critter ART has one).
    /// </summary>
    [OriginalFunction(0x42E4B0, OriginalFunctionScope.Partial)]
    public static int Delay(int framesPerSecond) => framesPerSecond > 0 ? 1000 / framesPerSecond : UnreadArtDelayMs;

    /// <summary>
    /// The frame the animation shows <paramref name="elapsedMs"/> after its start. The start shows frame 0 and the runner goes on at once
    /// (delay 0) to the frame step (0x42D6F0), which steps to frame 1 and asks to run again after the stored delay; each run steps one
    /// frame until it finds the last, where it steps no more. So frame 1 shows from the start, frame n from (n − 1) delays, the last
    /// stays. A one-frame animation or a delay of 0 (a rate above 1000: the runner repeats a zero delay at once) shows the last frame.
    /// </summary>
    [OriginalFunction(0x42D6F0, OriginalFunctionScope.Partial)]
    public static int Frame(long elapsedMs, int frames, int delayMs)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(frames, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(delayMs);
        if (frames == 1 || delayMs == 0) return frames - 1;
        if (elapsedMs < 0) return 0;
        return (int)Math.Min(frames - 1, 1 + elapsedMs / delayMs);
    }

    /// <summary>
    /// When the dying goal ends after its start: the step run that finds the last frame, (frames − 1) delays after the start (at once
    /// for a one-frame animation or a delay of 0). The goal's pool node and its end run then.
    /// </summary>
    public static long EndMs(int frames, int delayMs)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(frames, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(delayMs);
        return (long)(frames - 1) * delayMs;
    }
}
