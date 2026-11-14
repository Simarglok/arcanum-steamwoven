using Arcanum.Simulation.Party;

namespace Arcanum.Simulation.Ai;

/// <summary>
/// ARC-AI-2084 (ledger TB28, GW1-GW8): how a guard witness of an act on a portal, container or scenery is told: the call's mode.
/// </summary>
public enum GuardWitnessMode
{
    /// <summary>Mode 0: the crime is done (the object broke, the lock opened by a pick or a spell): the witness gets the attack event only.</summary>
    Done = 0,
    /// <summary>Mode 1: the attempt (damage that left the object whole, a failed pick): an awake witness lowers its reaction, may attack and shouts a line.</summary>
    Attempt = 1,
}

/// <summary>What a guard witness does after it passed the perception and script tests, in the order the original does it.</summary>
/// <param name="AttackEvent">The attack event of the actor on the witness (flags 0): always for <see cref="GuardWitnessMode.Done"/> and for a sleeping witness; for an awake witness of an attempt only when its reaction is at most AI parameter 10.</param>
/// <param name="ReactionDelta">The reaction change toward the player character (the reaction step): -20 for an awake witness of an attempt, else 0.</param>
/// <param name="ChecksReaction">Whether the witness's effective reaction is tested against the hostility threshold (parameter 10) to decide the attack event.</param>
/// <param name="Shouts">Whether the witness then says its line (an awake witness of an attempt, whatever the attack event did).</param>
public readonly record struct GuardWitnessPlan(bool AttackEvent, int ReactionDelta, bool ChecksReaction, bool Shouts);

/// <summary>
/// ARC-AI-2084 (FR-AI, ledger TB28 and GW1-GW8): the guard witness rule of a portal, container or scenery (0x4AEE50), the parts that need no map: which objects it
/// is called for, which NPCs qualify, and what a qualified witness does. The host (<c>AreaAiHost.GuardWitnesses</c>) walks the NPCs in the square around the object,
/// asks the perception and the script and applies the plan.
/// </summary>
public static class GuardWitnessRules
{
    /// <summary>The half-size of the square of tiles around the object that is searched for witnesses.</summary>
    public const int Radius = 10;
    /// <summary>The reaction change of an awake witness of an attempt.</summary>
    public const int AttemptReaction = -20;
    /// <summary>The line range of the shout (can-speak flag 1; the speaker's override entry 12039 comes first).</summary>
    public static readonly LoyaltyLines ShoutLines = new(4000, 4099);

    private const int PortalType = 1, ContainerType = 2, SceneryType = 3;
    private const int SceneryKey = -1;

    /// <summary>
    /// The key an NPC's name is compared to for an act on an object of <paramref name="objectType"/> (1 portal, 2 container, 3 scenery): the portal's or container's
    /// notify NPC (<paramref name="notifyNpc"/>; 0 means no witness at all, not even a guard), -1 for scenery (no name matches, only guards); null for every other type
    /// (an item, a wall, a trap: the rule returns at once).
    /// </summary>
    [OriginalFunction(0x4AEE50, OriginalFunctionScope.Partial)]
    public static int? Key(int objectType, int notifyNpc) => objectType switch
    {
        PortalType or ContainerType => notifyNpc == 0 ? null : notifyNpc,
        SceneryType => SceneryKey,
        _ => null,
    };

    /// <summary>
    /// Whether an NPC is a candidate witness: its name number equals the key, or it is a guard (social class 5). The dead and the mind-controlled never qualify
    /// (the tests that follow the name test in the original); the perception and the script come after.
    /// </summary>
    public static bool Qualifies(int key, int nameNumber, bool guard, bool dead, bool mindControlled) =>
        (nameNumber == key || guard) && !dead && !mindControlled;

    /// <summary>
    /// What a witness that passed every test does: mode 0 or a sleeping witness (critter flag 0x1000), the attack event only; an awake witness of an attempt lowers its
    /// reaction by 20, attacks when the new effective reaction is at most AI parameter 10, and shouts a line either way.
    /// </summary>
    public static GuardWitnessPlan Plan(GuardWitnessMode mode, bool sleeping) =>
        mode == GuardWitnessMode.Done || sleeping
            ? new(AttackEvent: true, ReactionDelta: 0, ChecksReaction: false, Shouts: false)
            : new(AttackEvent: false, ReactionDelta: AttemptReaction, ChecksReaction: true, Shouts: true);
}
