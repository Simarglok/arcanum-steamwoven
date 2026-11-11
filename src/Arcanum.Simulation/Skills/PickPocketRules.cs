using Arcanum.Simulation.Combat;

namespace Arcanum.Simulation.Skills;

/// <summary>Which way a pick-pocket attempt moves its item: from the victim to the thief, or from the thief to the victim.</summary>
public enum PickPocketDirection { Steal, Plant }

/// <summary>What the pick-pocket case moves of an item: gold coins and ammunition units in part, any other item whole (PP3).</summary>
public enum PickPocketItemKind { Gold, Ammunition, Item }

/// <summary>What a pick-pocket start on a target does (the skill bar's use on a critter, ledger PP1).</summary>
public enum PickPocketOpen
{
    /// <summary>Silently refused: a dead thief, a stoned target or the thief itself.</summary>
    Refused,
    /// <summary>A target with "no pickpocket" (critter flags 2 bit 0x80) or a mechanical one: the skill bar floats line 525.</summary>
    NotAllowed,
    /// <summary>A dead target: it is looted (after its use script allows it), not robbed.</summary>
    Loot,
    /// <summary>A living target the thief sees: the inventory window opens in steal mode (mode 3).</summary>
    Steal,
    /// <summary>A living target the thief does not see: nothing happens.</summary>
    NotSeen,
}

/// <summary>The facts a pick-pocket start reads (PP1): the thief's life, whether the target is the thief, the target's stoned spell
/// flag (0x10000), its critter flags 2 bit 0x80 and critter flag 0x20000000, its life and whether the thief sees it (sight 0x4AF260
/// returns 0).</summary>
public readonly record struct PickPocketOpenFacts(bool ThiefDead, bool TargetIsThief, bool TargetStoned, bool TargetNoPickpocket, bool TargetMechanical,
    bool TargetDead, bool TargetSeen);

/// <summary>Whether a failed attempt is noticed (PP4): never, always, or when the victim's perception roll succeeds.</summary>
public enum PickPocketDetection { None, Noticed, PerceptionRoll }

/// <summary>Where the consequences of a noticed failure stop (PP7): the victim's catching-thief script stopped them, the victim is a
/// follower of the thief, or nothing stopped them and the attempt counts as an attack of the thief on the victim.</summary>
public enum PickPocketCaughtStop { VictimScript, VictimFollowsThief, None }

/// <summary>
/// The pick-pocket skill (ARC-SKILLS-010, task S15 of the skills plan) as the retail 1.0.7.4 executable runs it (ledger PP1–PP11): the
/// skill bar's start (0x57A320 → 0x57A5E0 → the inventory window in steal mode), the pick-pocket case of the skill handler after the
/// shared check (0x4C7160 around 0x4C743C: the amount that moves, the detection of a failure, the alignment change of a theft) and the
/// order of a caught thief's consequences. The check itself is <see cref="SkillCheckRules.Run"/> (pick pocket is opposed by the
/// victim's perception; its own difficulty terms are SC11, its fate kind 8 SC14). No rule here reads a random number: the caller
/// rolls the check and the victim's perception die.
/// </summary>
public static class PickPocketRules
{
    /// <summary>Critter flags 2 (field 0xDB) bit 0x80: the critter cannot be robbed.</summary>
    public const uint NoPickpocketFlag2 = 0x80;
    /// <summary>Critter flags (field 0xDA) bit 0x20000000 that 0x57A5E0 tests with the "no pickpocket" bit (a mechanical critter).</summary>
    public const uint MechanicalFlag = 0x2000_0000;
    /// <summary>The object's spell flag 0x10000 (stoned).</summary>
    public const uint StonedSpellFlag = 0x1_0000;
    /// <summary>Critter flags 2 bit 0x01 a theft sets on its victim ("item stolen", PP6).</summary>
    public const uint ItemStolenFlag2 = 0x01;
    /// <summary>The most coins and ammunition units one theft or plant moves (PP3).</summary>
    public const int MaxGold = 100, MaxAmmunition = 20;
    /// <summary>The victim's perception die of a failure by an untrained thief (PP4): <c>random(1, 20)</c>.</summary>
    public const int PerceptionDie = 20;
    /// <summary>The skill bar's lines in <c>mes\skill_ui.mes</c>: success and failure (floated over the thief, PP2) and the refusal of a
    /// target that cannot be robbed (PP1).</summary>
    public const int SuccessLine = 500, FailureLine = 501, NotAllowedLine = 525;

    /// <summary>
    /// The start on a target (0x57A320 with the test 0x57A5E0, PP1): a dead thief, a stoned target and the thief itself are refused
    /// silently; then a target with "no pickpocket" or the mechanical flag floats line 525 (before the life test); a dead target is
    /// looted; a living one opens the window in steal mode only while the thief sees it. Followers are not refused; the start costs
    /// nothing.
    /// </summary>
    [OriginalFunction(0x57A320, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x57A5E0, OriginalFunctionScope.Full)]
    public static PickPocketOpen Open(PickPocketOpenFacts facts)
    {
        if (facts.ThiefDead || facts.TargetStoned || facts.TargetIsThief) return PickPocketOpen.Refused;
        if (facts.TargetNoPickpocket || facts.TargetMechanical) return PickPocketOpen.NotAllowed;
        if (facts.TargetDead) return PickPocketOpen.Loot;
        return facts.TargetSeen ? PickPocketOpen.Steal : PickPocketOpen.NotSeen;
    }

    /// <summary>Whether the critter flags refuse a target (0x57A5E0): critter flags 2 bit 0x80 or critter flag 0x20000000.</summary>
    public static bool NotAllowed(uint critterFlags, uint critterFlags2) =>
        (critterFlags2 & NoPickpocketFlag2) != 0 || (critterFlags & MechanicalFlag) != 0;

    /// <summary>How much of an item a successful attempt moves (PP3): gold min(quantity, 100), ammunition min(quantity, 20), any other
    /// item whole (1). A negative quantity is refused.</summary>
    public static int Amount(PickPocketItemKind kind, int quantity)
    {
        if (quantity < 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        return kind switch
        {
            PickPocketItemKind.Gold => Math.Min(quantity, MaxGold),
            PickPocketItemKind.Ammunition => Math.Min(quantity, MaxAmmunition),
            PickPocketItemKind.Item => 1,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    /// <summary>
    /// Whether a failure is noticed (PP4): a success (forced by a fate point or not) never is; a master planting never is; a critical
    /// failure always is (so a stealing master is caught on one); any other failure only when the thief is untrained, and then when the
    /// victim's <c>random(1, 20)</c> is at most its effective perception (<see cref="NoticedByRoll"/>).
    /// </summary>
    public static PickPocketDetection Detection(PickPocketDirection direction, SkillTraining thief, bool success, bool critical)
    {
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        if (!SkillRules.IsDefined(thief)) throw new ArgumentOutOfRangeException(nameof(thief));
        if (success) return PickPocketDetection.None;
        if (direction == PickPocketDirection.Plant && thief == SkillTraining.Master) return PickPocketDetection.None;
        if (critical) return PickPocketDetection.Noticed;
        return thief == SkillTraining.None ? PickPocketDetection.PerceptionRoll : PickPocketDetection.None;
    }

    /// <summary>The victim's perception roll (0x45F060 against <c>random(1, 20)</c>): noticed when the roll is at most the perception.</summary>
    [OriginalFunction(0x45F060, OriginalFunctionScope.Partial)]
    public static bool NoticedByRoll(int roll, int perception)
    {
        if (roll is < 1 or > PerceptionDie) throw new ArgumentOutOfRangeException(nameof(roll));
        return roll <= perception;
    }

    /// <summary>
    /// The thief's base alignment after a theft that moved something (0x45DC90 with flag 0, PP5), or null when it does not change. With
    /// <c>a</c> the victim's effective alignment and <c>b</c> the thief's: <c>a ≥ 0</c> changes the thief by
    /// <c>−trunc(trunc(a / 20) / 2)</c> with the bound <c>−100 − a</c>; <c>a &lt; 0</c> and <c>b ≤ a</c> by <c>trunc(trunc(a / 20) / 2)</c>
    /// with the bound <c>a − 100</c>; <c>a &lt; 0</c> and <c>b &gt; a</c> not at all. Only a negative change is applied, to the base, and
    /// not below the bound (a base already at or below it stays). A plant, and a victim the thief summoned, change nothing (the caller's).
    /// </summary>
    [OriginalFunction(0x45DC90, OriginalFunctionScope.Partial)]
    public static int? AlignmentAfterTheft(int victim, int thief, int thiefBase)
    {
        int change, bound;
        if (victim >= 0) (change, bound) = (-(victim / 20 / 2), -100 - victim);
        else if (thief <= victim) (change, bound) = (victim / 20 / 2, victim - 100);
        else return null;
        if (change >= 0 || thiefBase <= bound) return null;
        return (int)Math.Max((long)thiefBase + change, bound);
    }

    /// <summary>
    /// The consequences of a noticed failure, steal or plant (PP7), after the thief's script at point 33 (its answer ignored): the
    /// victim's script at point 11 (catching thief, the thief as the triggerer) stops the rest with any answer but "run default" (no
    /// script counts as one); then an NPC victim whose direct leader is the thief stops the rest; otherwise the attack event 0x4A9650
    /// runs with the thief as the attacker (loudness index 1, flags 0). Nothing else writes reaction, focus or the enemy list, changes
    /// alignment or sets a mark.
    /// </summary>
    public static PickPocketCaughtStop Caught(bool victimScriptRunsDefault, bool victimFollowsThief) =>
        !victimScriptRunsDefault ? PickPocketCaughtStop.VictimScript : victimFollowsThief ? PickPocketCaughtStop.VictimFollowsThief : PickPocketCaughtStop.None;
}
