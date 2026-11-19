using Arcanum.Simulation.Combat;

namespace Arcanum.Simulation.Ai;

/// <summary>The script attachment points the scheduler and the gate run (ledger SU2): the NPC's own script at each.</summary>
public enum AiScriptPoint
{
    /// <summary>Point 10, run by a wake-up tick; anything but "continue" skips to the far path.</summary>
    FirstHeartbeat = 10,
    /// <summary>Point 15, run by every pass that reaches the gate's combat test while the NPC is in combat mode (P5).</summary>
    StartCombat = 15,
    /// <summary>Point 16, run before point 15 outside a round (P5).</summary>
    EndCombat = 16,
    /// <summary>Point 19, run by a near tick and at the NPC's subturn; only "continue" lets the NPC think.</summary>
    Heartbeat = 19,
}

/// <summary>The NPC's conversation partner (the first slot of its reaction-PC list, 0x4C1110): none, the local PC or another critter.</summary>
public enum AiConversation { None, Pc, Other }

/// <summary>
/// The parts of an NPC's critter-flags word (object field 0xDA) the session holds and the gate's recoveries write (CL5): the
/// stun and injury conditions (0x20 stunned, 0x80 blinded, 0x100/0x200/0x400 crippled), the danger bits of that word
/// (<see cref="CritterBits"/>: FLEEING 0x10, SURRENDERED 0x4000, SPELL_FLEE 0x10000), the combat-mode bit 18 and the
/// concealment bits 0x1 and 0x2 (set together).
/// </summary>
public readonly record struct AiCritterFlags(LifeConditions Conditions, DangerFlags Danger, bool CombatMode, bool Concealed)
{
    /// <summary>The danger bits that live in the critter-flags word; the others are NPC flags or critter flags 2.</summary>
    public const DangerFlags CritterBits = DangerFlags.Fleeing | DangerFlags.Surrendered | DangerFlags.SpellFlee;

    /// <summary>The crippled conditions the gate's 1-in-8000 recovery clears together (0x700).</summary>
    public const LifeConditions Crippled = LifeConditions.CrippledArmOne | LifeConditions.CrippledArmsBoth | LifeConditions.CrippledLegsBoth;
}

/// <summary>
/// What the follower catch-up (0x4AD4D0) reads besides death and the round: whether the NPC has a PC leader (0x45DDA0), the
/// distance to it (0x441AE0; negative when there is none), the NPC's waiting flag (NPC flag 0x8), its paralysis (critter flag
/// 0x40) and whether it is active (0x45D790).
/// </summary>
public readonly record struct AiCatchUpFacts(bool HasPcLeader, long LeaderDistance, bool Waiting, bool Paralyzed, bool Active);

/// <summary>What one AI tick (0x4AD200) did, in its order.</summary>
[Flags]
public enum AiTickSteps
{
    None = 0,
    /// <summary>The wake-up block ran: "check wield" set, waypoints or standpoints, the first heartbeat.</summary>
    WakeUp = 1 << 0,
    /// <summary>The leaderless NPC outside combat mode ran its waypoints.</summary>
    Waypoints = 1 << 1,
    /// <summary>Its waypoints did nothing, so it ran its standpoints.</summary>
    Standpoints = 1 << 2,
    /// <summary>The first heartbeat (point 10) answered "continue".</summary>
    FirstHeartbeatContinued = 1 << 3,
    /// <summary>The NPC was near (<see cref="AiSchedulerRules.IsNear"/>) and its heartbeat (point 19) ran.</summary>
    Heartbeat = 1 << 4,
    /// <summary>The heartbeat answered "continue".</summary>
    HeartbeatContinued = 1 << 5,
    /// <summary>A generator asked the spawner, which named the next tick's time.</summary>
    Spawned = 1 << 6,
    /// <summary>A generator asked the spawner, which refused.</summary>
    SpawnRefused = 1 << 7,
    /// <summary>One AI pass ran (<see cref="AiSchedulerRules.Pass"/>).</summary>
    Pass = 1 << 8,
    /// <summary>The far path interrupted the NPC's animations (priority 4).</summary>
    AnimationsInterrupted = 1 << 9,
    /// <summary>The far path moved a PC follower to its leader.</summary>
    CaughtUp = 1 << 10,
    /// <summary>An encounter creature was destroyed and no tick follows.</summary>
    Destroyed = 1 << 11,
}

/// <summary>How the next tick is scheduled.</summary>
public enum AiTickNext
{
    /// <summary>After <see cref="AiTickResult.DelayMs"/> by the distance (0x4AD700), jittered after a wake-up.</summary>
    Delay,
    /// <summary>After the spawner's delay (0x4AD730).</summary>
    Spawner,
    /// <summary>No tick: the NPC wakes again when its sector is folded in.</summary>
    None,
}

/// <summary>One tick: its steps, how the next tick comes and its delay in game milliseconds (0 for <see cref="AiTickNext.None"/>).</summary>
public sealed record AiTickResult(AiTickSteps Steps, AiTickNext Next, long DelayMs, AiPassResult? Pass);

/// <summary>Why the gate (0x4A8570) ended the pass, or <see cref="Passed"/>; the original's test order.</summary>
public enum AiGateStop
{
    Passed,
    /// <summary>1: the NPC is dead.</summary>
    Dead,
    /// <summary>2: not sleeping, and switched off or not drawn (object flags 0x102).</summary>
    Hidden,
    /// <summary>3: the local PC is switched off (object flag 0x2).</summary>
    PcHidden,
    /// <summary>4: turned to stone (spell flag 0x10000).</summary>
    Stoned,
    /// <summary>5: the NPC's conversation partner is set.</summary>
    Conversation,
    /// <summary>7: a round runs and it is another critter's turn.</summary>
    NotOwnTurn,
    /// <summary>8: a goal of animation priority above 2 runs.</summary>
    Busy,
    /// <summary>11: stunned (CL1: also the pass that clears the flag).</summary>
    Stunned,
    /// <summary>12: unconscious.</summary>
    Unconscious,
    /// <summary>13: a generator (NPC flag 0x80000).</summary>
    Generator,
}

/// <summary>What the gate did besides its tests.</summary>
[Flags]
public enum AiGateSteps
{
    None = 0,
    /// <summary>5: the dialog callback ended the conversation with the PC of an NPC in COMBAT_FOCUS or FLEE.</summary>
    EndsConversation = 1 << 0,
    /// <summary>6: the end-combat script (point 16) ran (outside a round).</summary>
    EndCombatScript = 1 << 1,
    /// <summary>6: the start-combat script (point 15) ran.</summary>
    StartCombatScript = 1 << 2,
    /// <summary>9: the 1-in-160 roll cleared blindness.</summary>
    BlindnessCleared = 1 << 3,
    /// <summary>10: the 1-in-8000 roll cleared every crippled condition.</summary>
    CripplingCleared = 1 << 4,
    /// <summary>11: the stun animation no longer runs, so the stun was cleared (the pass still ends).</summary>
    StunCleared = 1 << 5,
    /// <summary>14: "check wield": the best item in every wear slot; "check wield" and "check weapon" cleared (CL2).</summary>
    ReWieldAll = 1 << 6,
    /// <summary>14: "check weapon": the best weapon (slot 1004); "check weapon" cleared.</summary>
    ReWieldWeapon = 1 << 7,
    /// <summary>15: DEMAINTAIN_SPELLS cleared and the NPC's maintained spells ended.</summary>
    Demaintained = 1 << 8,
}

/// <summary>The gate's answer and what it did.</summary>
public sealed record AiGateResult(AiGateStop Stop, AiGateSteps Steps)
{
    public bool Passed => Stop == AiGateStop.Passed;
}

/// <summary>Which stage of the pass's pipeline chose the action perform then carries out.</summary>
public enum AiPipelineChoice
{
    /// <summary>Heal (0x4A8940) chose a heal of the NPC, its leader or a follower.</summary>
    Heal,
    /// <summary>Heal chose nothing and the item search (0x4A8E70) chose ammunition, a weapon or armour.</summary>
    ItemSearch,
    /// <summary>Neither chose, so the danger update (0x4A92D0) ran.</summary>
    DangerUpdate,
}

/// <summary>
/// One AI pass (0x4A84F0): <see cref="MapClearing"/> when it ended before the gate, else the gate's answer and, when the gate
/// passed, the stage that chose before perform (0x4AC180) ran.
/// </summary>
public sealed record AiPassResult(bool MapClearing, AiGateResult? Gate, AiPipelineChoice? Choice)
{
    /// <summary>ARC-AI-2793: the pass ended before the gate because the host no longer holds the NPC (<see cref="IAiHost.Knows"/>).</summary>
    public bool NotHeld { get; init; }

    /// <summary>Perform ran.</summary>
    public bool Performed => Choice is not null;
}

/// <summary>
/// The subturn hook (0x4B7010) for one critter: <see cref="Acts"/> when it is the current non-PC participant outside a load;
/// whether its heartbeat answered "continue", the pass that then ran and whether the subturn ended because no animation runs.
/// </summary>
public sealed record AiSubturnHookResult(bool Acts, bool HeartbeatContinued, AiPassResult? Pass, bool EndsSubturn);

/// <summary>
/// What the host answers and does for the scheduler, the gate and the pass: the facts and actions outside the session's state.
/// Ids are the host's critter ids; <see cref="AiSchedulerRules.Pc"/> names the local PC. Every member is called at the
/// position of the original's call, so a host may read state a previous call changed.
/// </summary>
public interface IAiHost
{
    /// <summary>The distance in tiles to the local PC (0x441AE0); negative when there is none (another map, no PC).</summary>
    long DistanceToPc(string npc);
    /// <summary>ARC-AI-2793: the host holds the NPC's row now (a window of its map, a companion, a created critter). A tick of an NPC the host
    /// forgot (its window left it standing on its cell) runs its heartbeat but no pass: the pipeline reads rows the host no longer has. A host that
    /// holds every NPC it is asked about keeps the default.</summary>
    bool Knows(string npc) => true;
    /// <summary>Runs the NPC's script at <paramref name="point"/> with <paramref name="triggerer"/> (null: none) and the NPC as
    /// attachee; true when it answered "continue" (run default).</summary>
    bool RunScript(AiScriptPoint point, string npc, string? triggerer);
    /// <summary>Runs the NPC's waypoints (0x4ACBB0); false when it has none to run.</summary>
    bool RunWaypoints(string npc);
    /// <summary>Runs the NPC's standpoints (0x4ACDB0).</summary>
    void RunStandpoints(string npc);
    /// <summary>NPC flag 0x80000: a generator.</summary>
    bool IsGenerator(string npc);
    /// <summary>The spawner (0x4BA790): the delay in game milliseconds of the generator's next tick, or null when it refused.</summary>
    long? Spawn(string npc);
    /// <summary>Critter flag 0x20000: an encounter creature, destroyed when it is left without a tick.</summary>
    bool IsEncounterCreature(string npc);
    void Destroy(string npc);
    /// <summary>Interrupts the NPC's animations with priority 4 (0x424070).</summary>
    void InterruptAnimations(string npc);
    /// <summary>The NPC's sector is loaded (0x4D04E0).</summary>
    bool SectorLoaded(string npc);
    /// <summary>The catch-up's facts; the session answers <see cref="AiCatchUpFacts.Waiting"/> itself for its companions.</summary>
    AiCatchUpFacts CatchUp(string npc);
    /// <summary>Moves the follower to its leader's location (0x43E770) after interrupting its animations.</summary>
    void MoveToLeader(string npc);
    /// <summary>The map is clearing its objects (0x410060): no pass.</summary>
    bool MapClearing { get; }
    /// <summary>The NPC is sleeping (0x45D870).</summary>
    bool Sleeping(string npc);
    /// <summary>Object flags 0x102 (switched off or not drawn).</summary>
    bool Hidden(string npc);
    /// <summary>The local PC has object flag 0x2.</summary>
    bool PcHidden { get; }
    AiConversation Conversation(string npc);
    /// <summary>The dialog-end callback (0x5F848C) is set.</summary>
    bool ConversationCallback { get; }
    /// <summary>The callback ends the NPC's conversation with the PC.</summary>
    void EndConversation(string npc);
    /// <summary>The priority of the NPC's running animation goal (0x4233D0), 0 when none runs.</summary>
    int AnimationPriority(string npc);
    /// <summary>The stun animation (goal 57) still runs (0x44E830).</summary>
    bool StunAnimationRunning(string npc);
    /// <summary>Any animation of the NPC runs (0x423300).</summary>
    bool AnimationRunning(string npc);
    /// <summary>Wields the best item in every wear slot (0x4654F0), or the best weapon (slot 1004, 0x465170).</summary>
    void ReWield(string npc, bool allSlots);
    /// <summary>The NPC's leader (0x45DE00), <see cref="AiSchedulerRules.Pc"/> for the PC, or null.</summary>
    string? Leader(string npc);
    /// <summary>Ends the NPC's maintained spells (0x457450).</summary>
    void EndMaintainedSpells(string npc);
    /// <summary>Heal (0x4A8940, slice I8): true when it chose an action.</summary>
    bool Heal(string npc);
    /// <summary>The item search (0x4A8E70, slice I8): true when it chose an action.</summary>
    bool SearchItems(string npc);
    /// <summary>The danger update (0x4A92D0; decisions of slices I4 and I5).</summary>
    void UpdateDanger(string npc);
    /// <summary>Perform (0x4AC180, slices I5 and I7).</summary>
    void Perform(string npc);
    /// <summary>A saved game is loading (0x4038D0): the subturn hook does nothing.</summary>
    bool Loading { get; }
}

/// <summary>
/// The scheduler's whole world: the host's part and the state the session owns (life, the round, combat mode, the danger
/// record), read and written at the original's positions.
/// </summary>
public interface IAiSchedulerWorld : IAiHost
{
    /// <summary>Dead (0x45D8D0); also asked about a leader. An unknown critter counts as dead.</summary>
    bool IsDead(string id);
    /// <summary>Unconscious (0x45D800).</summary>
    bool IsUnconscious(string npc);
    /// <summary>Turned to stone (spell flag 0x10000).</summary>
    bool IsStoned(string npc);
    /// <summary>A turn-based round runs (0x4B6D70).</summary>
    bool InRound { get; }
    /// <summary>The critter whose subturn it is (0x4B6D80), or null.</summary>
    string? RoundActor { get; }
    /// <summary>In combat mode (0x4B3D50); also asked about a leader.</summary>
    bool InCombatMode(string id);
    /// <summary>The danger state and its source (0x4AAB00), read when the pass is set up.</summary>
    (DangerState State, string? Source) Danger(string npc);
    /// <summary>The NPC's danger bits as stored now.</summary>
    DangerFlags DangerBits(string npc);
    /// <summary>Sets and clears NPC danger bits ("check wield", "check weapon", DEMAINTAIN_SPELLS).</summary>
    void WriteDangerBits(string npc, DangerFlags set, DangerFlags clear);
    /// <summary>The critter-flags word's parts as stored now.</summary>
    AiCritterFlags CritterFlags(string npc);
    /// <summary>Writes the critter-flags word's parts.</summary>
    void WriteCritterFlags(string npc, AiCritterFlags flags);
    /// <summary>The subturn ends: action points 0 and the next participant (0x4B7080).</summary>
    void EndSubturn(string npc);
}
