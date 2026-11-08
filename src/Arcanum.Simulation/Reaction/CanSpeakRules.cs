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
/// ARC-DEV-304 (GT217, F1): the first half of the can-speak test <c>0x4AD800</c> (the NPC, the other object, the flag) as a pure function. The answers are the executable's: 0 can speak
/// (and the test is over), 1 a dead NPC without the spoken-with-dead spell flag, 5 and 6 the refusals "off or unable" and "asleep or unconscious". The tail of the test (the combat focus,
/// the interrupt of the NPC's goals, another player character's conversation) is the caller's and is not modelled here.
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

    /// <summary>Whether a dead NPC can be talked to: the flag spell 56 sets and removes. A living NPC is not asked here (null: the test goes on).</summary>
    public static bool DeadMayTalk(bool spokenWithDead) => Head(new CanSpeakFacts(Dead: true, SpokenWithDead: spokenWithDead)) == Free;
}
