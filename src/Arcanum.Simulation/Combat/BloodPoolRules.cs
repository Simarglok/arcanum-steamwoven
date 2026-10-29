namespace Arcanum.Simulation.Combat;

/// <summary>
/// The facts the original reads before it leaves a pool of blood under a dead critter (ARC-RENDER-041): the critter's critter flags
/// (field 0xDA), critter flags 2 (field 0xDB) and spell flags (field 0x14), whether its tile blocks (the sector's block mask, a facade
/// tile or the tile name's blocking flag, 0x4D7110) and the "violence filter" setting.
/// </summary>
public readonly record struct BloodPoolFacts(uint CritterFlags, uint CritterFlags2, uint SpellFlags, bool TileBlocking, bool ViolenceFilter);

/// <summary>Why no pool of blood is left (<see cref="BloodPoolRefusal.None"/>: one is), in the order the original tests.</summary>
public enum BloodPoolRefusal { None, TileBlocking, ViolenceFilter, UndeadOrMechanical, Elemental, Stoned }

/// <summary>
/// ARC-RENDER-041: the pool of blood a dying critter leaves (read in the retail 1.0.7.4 executable, ledger "Pool of blood" BP1–BP9).
/// When the dying goal (31) has played its frames, its node 0x42FED0 creates the pool unless the critter's tile blocks; the creation
/// (0x432D90) refuses with the violence filter on, for an undead or mechanical critter, an elemental or a stoned one, and otherwise
/// creates the scenery of basic prototype <see cref="BasicPrototype"/> ("Pool Of Blood", <c>art\scenery\blood.ART</c>) at the
/// critter's tile with the critter's pixel offsets. The pool plays its frames once (goal 0, <see cref="Frame"/>) and keeps the last;
/// the Decay Dead Bodies event destroys it <see cref="DecayDelay"/> later. Critter flags 2's "no blood splotches" (0x100) does not
/// stop it: that flag only stops the hit's blood eye candy (0x433020), and no installed critter prototype carries it.
/// </summary>
public static class BloodPoolRules
{
    /// <summary>The pool's basic prototype, the number the creation names (0x4685A0).</summary>
    public const int BasicPrototype = 4028;
    /// <summary>The pool's prototype file number (basic number + 20): a scenery, flat, not blocking, clicked through, not animating on
    /// its own (BP4).</summary>
    public const int Prototype = BasicPrototype + 20;
    public const uint UndeadFlag = 0x4, MechanicalFlag = 0x20000000, ElementalFlag2 = 0x400, StonedSpellFlag = 0x10000;
    /// <summary>The decay delay of a dead critter (one game day) and of any other object, such as a pool (two game days).</summary>
    public const long CritterDecayDelayMs = 86_400_000, ObjectDecayDelayMs = 172_800_000;

    /// <summary>
    /// The dying goal's pool node (0x42FED0): a critter on a blocking tile leaves none, any other one what <see cref="Refusal"/>
    /// says. The location, the creation and the goal's next node are the caller's.
    /// </summary>
    [OriginalFunction(0x42FED0, OriginalFunctionScope.Partial)]
    public static BloodPoolRefusal Leaves(BloodPoolFacts facts) => facts.TileBlocking ? BloodPoolRefusal.TileBlocking : Refusal(facts);

    /// <summary>
    /// The creation's refusals (0x432D90), in its order: the violence filter, an undead or mechanical critter (critter flags
    /// 0x20000004), an elemental (critter flags 2, 0x400), a stoned critter (spell flags 0x10000). A multiplayer client creates none
    /// (not modelled: single player).
    /// </summary>
    [OriginalFunction(0x432D90, OriginalFunctionScope.Partial)]
    public static BloodPoolRefusal Refusal(BloodPoolFacts facts) =>
        facts.ViolenceFilter ? BloodPoolRefusal.ViolenceFilter
        : (facts.CritterFlags & (UndeadFlag | MechanicalFlag)) != 0 ? BloodPoolRefusal.UndeadOrMechanical
        : (facts.CritterFlags2 & ElementalFlag2) != 0 ? BloodPoolRefusal.Elemental
        : (facts.SpellFlags & StonedSpellFlag) != 0 ? BloodPoolRefusal.Stoned
        : BloodPoolRefusal.None;

    /// <summary>
    /// The frame a pool shows <paramref name="elapsedMs"/> after its creation under the animate goal (0): its start node sets frame 0
    /// and the delay 1000 / the ART's frame rate (integer division, 0x42CB10); the frame step node (0x42CC80) runs at once and then once
    /// per delay, stepping one frame each time until the frame is the last, where the goal ends and the frame stays (the goal's end
    /// 0x42B090 resets the art of a critter only). So frame 1 shows from the start, frame n from (n − 1) delays. A rate of 0 or below
    /// shows the last frame (the original would divide by zero; no installed pool art has one).
    /// </summary>
    [OriginalFunction(0x42CC80, OriginalFunctionScope.Partial)]
    public static int Frame(long elapsedMs, int frames, int framesPerSecond)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(frames, 1);
        if (frames == 1) return 0;
        if (framesPerSecond <= 0) return frames - 1;
        if (elapsedMs < 0) return 0;
        var delay = Math.Max(1, 1000 / framesPerSecond);
        return (int)Math.Min(frames - 1, 1 + elapsedMs / delay);
    }

    /// <summary>The Decay Dead Bodies delay (0x45EBE0): one game day for a critter (PC or NPC), two for any other object. The event
    /// itself is the caller's.</summary>
    [OriginalFunction(0x45EBE0, OriginalFunctionScope.Partial)]
    public static long DecayDelay(bool critter) => critter ? CritterDecayDelayMs : ObjectDecayDelayMs;

    /// <summary>Whether a pool created at <paramref name="createdMs"/> is gone at game time <paramref name="nowMs"/>: the Decay Dead
    /// Bodies event destroys a non-critter object when it fires (0x45EB50), <see cref="DecayDelay"/> after its creation.</summary>
    [OriginalFunction(0x45EB50, OriginalFunctionScope.Partial)]
    public static bool Decayed(long createdMs, long nowMs) => nowMs - createdMs >= DecayDelay(false);
}
