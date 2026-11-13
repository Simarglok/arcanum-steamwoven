namespace Arcanum.Simulation.Reaction;

/// <summary>
/// ARC-DEV-304 (B24, GT217): what the head of the can-speak test (0x4AD800) asks about the NPC, in the order the executable asks it. Every field is a fact the caller knows; a fact a
/// host does not model stays at its default (false), which only lets the test go on.
/// </summary>
/// <param name="Destroyed">The object flags have destroyed or off (0x3).</param>
/// <param name="IsNpc">The object is an NPC (a critter of the NPC type); anything else is answered "free" at once.</param>
/// <param name="StunnedParalysedOrMute">Critter flags 0x2060: stunned, paralysed or mute.</param>
/// <param name="Asleep">Critter flag 0x1000.</param>
/// <param name="Spell58">Under the influence of spell 58 (0x459380).</param>
/// <param name="Stoned">Spell flag 0x10000.</param>
/// <param name="Dead">No hit points left (0x45D8D0).</param>
/// <param name="SpokenWithDead">Spell flag 0x2000000 (<c>OSF_SPOKEN_WITH_DEAD</c>, spell 56).</param>
/// <param name="Unconscious">0x45D800: the fatigue is spent.</param>
public readonly record struct CanSpeakFacts(bool Destroyed = false, bool IsNpc = true, bool StunnedParalysedOrMute = false, bool Asleep = false, bool Spell58 = false,
    bool Stoned = false, bool Dead = false, bool SpokenWithDead = false, bool Unconscious = false);

/// <summary>
/// ARC-AI-1677 (0x4AD800, the tail after the head): what the rest of the test asks about a living, conscious NPC. <paramref name="InCombatFocusOrFlee"/> is danger state 1 or 2 (0x4AAB00);
/// <paramref name="GoalRefusesInterrupt"/> is a refusal of the interrupt 0x424070 (NPC, level 2: a priority-3 run); <paramref name="OtherPartner"/> is the NPC's conversation partner (field 0x129 slot 0, 0x4C1110)
/// being set and not the object the test is asked for.
/// </summary>
public readonly record struct CanSpeakTailFacts(bool InCombatFocusOrFlee = false, bool GoalRefusesInterrupt = false, bool OtherPartner = false);

/// <summary>
/// ARC-DEV-304 (GT217, F1): the first half of the can-speak test <c>0x4AD800</c> (the NPC, the other object, the flag) as a pure function. The answers are the executable's: 0 can speak
/// (and the test is over), 1 a dead NPC without the spoken-with-dead spell flag, 5 and 6 the refusals "off or unable" and "asleep or unconscious". The tail of the test (the combat focus,
/// the interrupt of the NPC's goals, another player character's conversation) is <see cref="Tail"/>; the interrupt call itself is the caller's.
/// </summary>
public static class CanSpeakRules
{
    public const int Free = 0, DeadAnswer = 1, Unable = 5, Resting = 6;

    /// <summary>The answer of the head, or null when the tail of the test goes on (a living, conscious NPC that nothing above refused).</summary>
    [OriginalFunction(0x4AD800, OriginalFunctionScope.Partial)]
    public static int? Head(in CanSpeakFacts facts)
    {
        if (facts.Destroyed) return Unable;
        if (!facts.IsNpc) return Free;
        if (facts.StunnedParalysedOrMute) return Unable;
        if (facts.Asleep) return Resting;
        if (facts.Spell58 || facts.Stoned) return Unable;
        // A dead NPC: 1, or 0 under the spoken-with-dead flag, which also skips the tests below (a dead NPC is never "unconscious").
        if (facts.Dead) return facts.SpokenWithDead ? Free : DeadAnswer;
        return facts.Unconscious ? Resting : null;
    }

    /// <summary>Answer 2: a living NPC in combat focus or fleeing (flag 0 only).</summary>
    public const int Fighting = 2;

    /// <summary>Answer 3: a priority-3 run refuses the interrupt (flag 0 only).</summary>
    public const int Busy = 3;

    /// <summary>Answer 4: another conversation partner.</summary>
    public const int OtherConversation = 4;

    /// <summary>
    /// ARC-AI-1677: the tail of the can-speak test after <see cref="Head"/> returned null. With the fifth argument 0 (the generic greeting, the talk start) the test asks the combat focus
    /// (2), then the interrupt of the NPC's goals at level 2 (3, which also ends its priority-1 and -2 runs: the caller makes that call only when the focus test passed), then the partner. With the argument 1 (the
    /// follower lines) it skips the first two and asks only the partner: set and not the asker (4). With one player character only the hero asks, and a follower's partner
    /// is the hero or none, so a follower line is never refused here.
    /// </summary>
    /// <remarks>The tail of 0x4AD800 (the registry row names <see cref="Head"/>), read in the export; flag 0 callers 0x4132A0 and 0x567460, flag 1 callers 0x413A90, 0x413DF0, 0x414490 and their twins.</remarks>
    public static int Tail(in CanSpeakTailFacts facts, bool skipsActivityTests)
    {
        if (!skipsActivityTests)
        {
            if (facts.InCombatFocusOrFlee) return Fighting;
            if (facts.GoalRefusesInterrupt) return Busy;
        }
        return facts.OtherPartner ? OtherConversation : Free;
    }

    /// <summary>Whether a dead NPC can be talked to: the flag spell 56 sets and removes. A living NPC is not asked here (null: the test goes on).</summary>
    public static bool DeadMayTalk(bool spokenWithDead) => Head(new CanSpeakFacts(Dead: true, SpokenWithDead: spokenWithDead)) == Free;
}
