using System.Collections.Immutable;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;

namespace Arcanum.Simulation.Ai;

/// <summary>
/// The animation goals the NPC executor runs (stage A6, research ARC-RES-066; slice I7, ARC-WORLD-086), by the original's goal
/// number with its own name (the goal-name table 0x5A164C) in the comment.
/// </summary>
public enum AiGoalType
{
    /// <summary>4 [ag_run_to_tile]: the back-off's run to its tile.</summary>
    RunToTile = 4,
    /// <summary>5 [ag_attempt_move]: the plain walk of a planned segment (the flight's and the run to a tile's).</summary>
    AttemptMove = 5,
    /// <summary>6 [ag_move_to_pause]: the one-second real-time pause before a run without a path ends.</summary>
    MoveToPause = 6,
    /// <summary>8 [ag_move_near_obj]: the follower's walk to its leader.</summary>
    MoveNear = 8,
    /// <summary>18 [ag_attack]: the attack goal.</summary>
    Attack = 18,
    /// <summary>19 [ag_attempt_attack]: one attack animation and its hit.</summary>
    AttemptAttack = 19,
    /// <summary>24 [ag_follow]: following the leader.</summary>
    Follow = 24,
    /// <summary>25 [ag_flee]: the flight.</summary>
    Flee = 25,
    /// <summary>52 [ag_move_away_from_obj]: the escape line away from the source.</summary>
    MoveAway = 52,
    /// <summary>56 [ag_run_near_obj]: the follower's run to its leader.</summary>
    RunNear = 56,
    /// <summary>61 [ag_attempt_move_near]: the melee approach's walk of a planned segment.</summary>
    MeleeApproach = 61,
    /// <summary>69 [ag_attempt_spread_out]: the spread-out order's goal.</summary>
    SpreadOut = 69,
    /// <summary>79 [ag_move_near_obj_combat]: the combat approach that plans the attack's path.</summary>
    CombatApproach = 79,
    /// <summary>80 [ag_attempt_move_near_combat]: the ranged approach's walk of a planned segment.</summary>
    RangedApproach = 80,
}

/// <summary>
/// One level of a run's goal stack: the goal, the node it continues at (1 is its first) and its data. A pushed goal gets a copy
/// of the parent's data (target, tile, range, sound); the walk goals also hold their planned segment.
/// </summary>
public sealed record AiGoalFrame
{
    public AiGoalFrame(AiGoalType goal, string? target = null, CellCoord? tile = null, int range = 0, int? sound = null)
    {
        if (!Enum.IsDefined(goal)) throw new ArgumentOutOfRangeException(nameof(goal));
        if (range < 0) throw new ArgumentOutOfRangeException(nameof(range));
        Goal = goal; Target = target; Tile = tile; Range = range; Sound = sound;
    }

    public AiGoalType Goal { get; init; }
    /// <summary>The node the goal continues at; a goal starts and restarts at 1.</summary>
    public int Node { get; init; } = 1;
    /// <summary>The goal's object: the attack's target, the flight's source, the follower's leader.</summary>
    public string? Target { get; init; }
    /// <summary>The run to a tile's destination.</summary>
    public CellCoord? Tile { get; init; }
    /// <summary>The goal's range in tiles (the attack's approach 1, the flight's 9, the follower's 2 or 5).</summary>
    public int Range { get; init; }
    /// <summary>The attack's alerted sound, played once when the approach starts moving and then cleared.</summary>
    public int? Sound { get; init; }
    /// <summary>The walk goals' planned segment (at most <see cref="AiExecutorRules.SegmentSteps"/> tiles), else empty.</summary>
    public ImmutableArray<CellCoord> Path { get; init; } = [];
    /// <summary>The next step of <see cref="Path"/>.</summary>
    public int PathIndex { get; init; }
    /// <summary>The target's tile when the segment was planned (the approach walks stop when it changes).</summary>
    public CellCoord? PlannedTargetTile { get; init; }
    /// <summary>The attempt-attack goal's wait from the action frame to the animation's end, in game milliseconds.</summary>
    public int RestMs { get; init; }
}

/// <summary>The run's flags the executor reads (project names; the original's run flags in brackets).</summary>
[Flags]
public enum AiRunFlags
{
    None = 0,
    /// <summary>Run flag 0x400 (a flight): every path request is a straight line that accepts every step and does not search,
    /// and the walk tests each next tile for blocking.</summary>
    Flight = 1 << 0,
    /// <summary>The run walks running (the starters' "running on"); a step begins running only below encumbrance
    /// <see cref="AiExecutorRules.RunRefusedEncumbrance"/>.</summary>
    Running = 1 << 1,
}

/// <summary>
/// One animated object's goal run (the original's run slot: at most <see cref="AiExecutorRules.MaxDepth"/> goals). Immutable:
/// <see cref="AiExecutorRules.Step"/> returns the next one. <see cref="Reason"/> is the last reason code the run reported, for
/// the overlay.
/// </summary>
public sealed record AiGoalRun
{
    public AiGoalRun(string owner, AiGoalFrame bottom, AiRunFlags flags = AiRunFlags.None)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(bottom);
        Owner = owner; Frames = [bottom]; Flags = flags;
    }

    public string Owner { get; }
    /// <summary>The goal stack, bottom first; never empty.</summary>
    public ImmutableArray<AiGoalFrame> Frames { get; init; }
    public AiRunFlags Flags { get; init; }
    public AiExecutorReason Reason { get; init; }

    /// <summary>The goal that runs now.</summary>
    public AiGoalFrame Top => Frames[^1];
    /// <summary>The goal the run was started with.</summary>
    public AiGoalFrame Bottom => Frames[0];
}

/// <summary>Why a run ended, or <see cref="Running"/> while it goes on.</summary>
public enum AiRunEnd
{
    /// <summary>The run goes on after the step's delay.</summary>
    Running,
    /// <summary>The bottom goal popped: the run is done (no end step).</summary>
    Finished,
    /// <summary>A node chose the end step: the end nodes run and, in a round, the owner may be charged the pending cost (EX6).</summary>
    EndStep,
    /// <summary>A node killed the run (a step or an attack it cannot pay): no end step, no charge.</summary>
    Killed,
    /// <summary>A push beyond <see cref="AiExecutorRules.MaxDepth"/> goals ended the run.</summary>
    Overflow,
    /// <summary>The 101st node of one step: the owner's subturn ends and the run is killed.</summary>
    NodeLimit,
}

/// <summary>The executor's reason codes for the overlay, tests and mods (research section 10.6), plus the ends it reports.</summary>
public enum AiExecutorReason
{
    None,
    /// <summary>The attack goal found its target in reach and attacks.</summary>
    AttackInReach,
    /// <summary>The attack goal approaches its target.</summary>
    AttackApproach,
    /// <summary>No path to the target (real time: after the one-second pause).</summary>
    AttackNoPath,
    /// <summary>The target is gone or dead: the attack goal ends.</summary>
    AttackTargetGone,
    /// <summary>The attack became impossible (test 2): the goal ends, in a round with the subturn.</summary>
    AttackImpossible,
    /// <summary>Adjacent to the target but out of reach (a blocked line).</summary>
    AttackBlocked,
    /// <summary>The attack's action frame struck.</summary>
    AttackHit,
    /// <summary>At the action frame the target was out of reach: no hit, the cost is spent.</summary>
    AttackOutOfReach,
    /// <summary>The flight walks its escape line.</summary>
    FleeEscapeLine,
    /// <summary>The flight is beyond 9 tiles, or its way back is blocked: it ends.</summary>
    FleeSafe,
    /// <summary>No escape line: the NPC faces its source and gets NO_FLEE for good.</summary>
    FleeCornered,
    /// <summary>The follower stands within its range.</summary>
    FollowInRange,
    /// <summary>The follower walks to its leader.</summary>
    FollowWalk,
    /// <summary>The follower runs to its leader.</summary>
    FollowRun,
    /// <summary>The spread-out goal moves away from a leader that is too close.</summary>
    SpreadOutAway,
    /// <summary>The run to a tile walks or reached its tile.</summary>
    RunToTile,
    /// <summary>A path the goal needed was not found.</summary>
    NoPath,
    /// <summary>A step was blocked (a living critter near the path's end, a spotted trap, a step that cannot start).</summary>
    StepBlocked,
    /// <summary>A step or an attack the owner cannot pay in its subturn: the run is killed.</summary>
    Unpaid,
    /// <summary>The leader or the target is not known to the world any more.</summary>
    ObjectGone,
}

/// <summary>
/// One step of a run (<see cref="AiExecutorRules.Step"/>): the run after it (null once it ended), the delay in game milliseconds
/// before the next step (0 once it ended), how it ended, the nodes it ran and the reason code it reported.
/// </summary>
public sealed record AiGoalStepResult(AiGoalRun? Run, int DelayMs, AiRunEnd End, int Nodes, AiExecutorReason Reason)
{
    public bool Ended => End != AiRunEnd.Running;
}

/// <summary>A path request (the planners 0x4280D0, 0x427990 and the escape path 0x426A80 over 0x41F3C0).</summary>
public enum AiPathKind
{
    /// <summary>The attack's approach (0x4280D0): the route into the target's tile, one step short.</summary>
    Attack,
    /// <summary>The follower's approach (0x427990): the route to within the range of the target.</summary>
    Near,
    /// <summary>A route to <see cref="AiPathRequest.Tile"/> (the run to a tile).</summary>
    ToTile,
    /// <summary>The escape line to <see cref="AiPathRequest.Tile"/>. With <see cref="AiPathRequest.Straight"/> (a flight, flag
    /// 0x400) a straight line that accepts every step and does not search; without it the straight line may stop short and a
    /// search finish it.</summary>
    Escape,
}

/// <summary>A path the executor asks the world for. <see cref="Running"/> names the walk's gait for the planner's costs.</summary>
public sealed record AiPathRequest(AiPathKind Kind, string? Target, CellCoord? Tile, int Range, bool Straight, bool Running);

/// <summary>
/// A planned path: its steps from the next tile on (the executor walks at most <see cref="AiExecutorRules.SegmentSteps"/> of them
/// before it plans again) and, for an escape line, whether its straight line stopped short after at least one step and a search
/// finished it (the planner's partial-step count is not zero, 0x426D9A).
/// </summary>
public sealed record AiPath(ImmutableArray<CellCoord> Steps, bool StraightStoppedShort = false);

/// <summary>
/// What the executor reads about the object that runs: <see cref="Pc"/> the local PC; knocked down and concealed (the goals get
/// it up and unconceal it first, a PC is not unconcealed); the encumbrance level (4 is "significant"); current and maximum
/// fatigue; <see cref="CannotTurn"/> stoned, stunned or paralysed; <see cref="Entangled"/>; <see cref="SpreadOut"/> NPC flag
/// "spread out".
/// </summary>
public readonly record struct AiMoverFacts(
    bool Pc = false,
    bool KnockedDown = false,
    bool Concealed = false,
    int Encumbrance = 0,
    int Fatigue = 0,
    int MaxFatigue = 0,
    bool CannotTurn = false,
    bool Entangled = false,
    bool SpreadOut = false);

/// <summary>
/// The attack-possible test's facts (0x4294F0): the attacker not active, in Body of Air, elemental; the target destroyed or off,
/// dead; the line of fire reports a blocker other than the target, and then whether no path to the target exists (0x426500). The
/// default is an attack that is possible.
/// </summary>
public readonly record struct AiAttackFacts(
    bool AttackerInactive = false,
    bool BodyOfAir = false,
    bool Elemental = false,
    bool TargetGone = false,
    bool TargetDead = false,
    bool ForeignBlocker = false,
    bool NoPathToTarget = false);

/// <summary>
/// The reach test's facts (0x429960; <see cref="LineOfFireRules.InReach{TObject}"/>): the Chebyshev distance, the wielded
/// weapon's range (0 without one), whether a weapon is wielded, and whether the line of fire is clear (cost below 26 and no
/// blocker but the target).
/// </summary>
public readonly record struct AiReachFacts(int Distance, int WeaponRange, bool Weapon, bool ClearLine);

/// <summary>The attack animation's timing: game milliseconds to its action frame, then to its end (with the pause, RT8).</summary>
public readonly record struct AiAttackSwing(int ActionFrameMs, int RestMs);

/// <summary>
/// The world the goals run in: positions, the facts each node tests, the planners, the step walker, the attack animation and
/// the round's action points. Ids are the host's object ids; <see cref="AiSchedulerRules.Pc"/> names the local PC. Every member is
/// called at the position of the original's call.
/// </summary>
public interface IAiGoalWorld
{
    /// <summary>The object's tile, also for one destroyed or switched off (its location is kept); null when the world does not
    /// know it.</summary>
    CellCoord? Tile(string id);
    /// <summary>Destroyed or switched off (object flags 0x5).</summary>
    bool Gone(string id);
    /// <summary>A critter that is dead.</summary>
    bool DeadCritter(string id);
    AiMoverFacts Mover(string id);
    /// <summary>The first step from the object's tile toward <paramref name="toward"/> is clear (the step test of 0x425130, which the
    /// executor asks after its own "within the goal's range" test); with <paramref name="ignoreTarget"/> (0x425840, the flight's
    /// test) the target's own tile is not blocking for the walker and nothing but the target blocks the step.</summary>
    bool FirstStepClear(string id, string toward, bool ignoreTarget);
    AiAttackFacts AttackFacts(string id, string target);
    AiReachFacts Reach(string id, string target);
    /// <summary>The wielded weapon's attack cost in action points (0x4B7C30).</summary>
    int AttackCost(string id);
    /// <summary>Plans a path, or null when there is none.</summary>
    AiPath? PlanPath(string id, AiPathRequest request);
    /// <summary>The next step is blocked (a living critter on it near the path's end, a newly spotted trap, a step that cannot
    /// start; with <paramref name="flight"/> any blocking tile).</summary>
    bool StepBlocked(string id, CellCoord next, bool flight);
    /// <summary>Starts the step to <paramref name="next"/> (doors, windows and the gait are the walker's): the game milliseconds it
    /// takes, at least 1. The object stands on <paramref name="next"/> when the next step of the run comes.</summary>
    int BeginStep(string id, CellCoord next, bool running);
    /// <summary>Gets the object up (goal 63): the game milliseconds it takes.</summary>
    int GetUp(string id);
    /// <summary>Unconceals the object (goal 54): the game milliseconds it takes.</summary>
    int Unconceal(string id);
    /// <summary>Turns the object to face <paramref name="toward"/> (0x42C240).</summary>
    void Face(string id, string toward);
    /// <summary>Plays the object's sound (the attack's alerted sound).</summary>
    void PlaySound(string id, int sound);
    /// <summary>Starts the attack animation toward the target (0x432700, after the cost was paid): its timing.</summary>
    AiAttackSwing BeginAttack(string id, string target);
    /// <summary>The hit step at the action frame (0x42A630 → 0x4B3BB0 → the attack resolution 0x4B3170): the world resolves the
    /// attack (the resolver, the wear step, the missile, the notification).</summary>
    void Hit(string id, string target);
    /// <summary>Sets NO_FLEE for good (0x4AF1D0).</summary>
    void SetNoFlee(string id);
    /// <summary>A turn-based round runs (0x4B6D70).</summary>
    bool InRound { get; }
    /// <summary>The critter whose subturn it is (0x4B6D80), or null.</summary>
    string? RoundActor { get; }
    /// <summary>The round actor's action points left.</summary>
    int ActionPoints(string id);
    /// <summary>The round's pending cost (0x5FC244): the value the last action-point check wrote; the host sets it to 0 at each
    /// subturn start (0x4B75D0).</summary>
    int PendingCost { get; set; }
    /// <summary>The action-point consume for the round's actor (0x4B7CD0; a refusal empties the action points and passes the turn,
    /// TB9).</summary>
    void Consume(string id, int cost);
    /// <summary>Ends the critter's subturn (0x4B7C90).</summary>
    void EndSubturn(string id);
}

/// <summary>The action the pass record names (field +0x14): perform runs it before the danger state's branch.</summary>
public enum AiPassAction
{
    None = 0,
    /// <summary>1: a spell or a spell item (the magic invocation, slice I8 and the magic domain).</summary>
    Cast = 1,
    /// <summary>2: an item use on the target (the item-use goal 39, slice I8).</summary>
    UseItem = 2,
    /// <summary>3: a skill use on the target (the skill goal 33, the skills domain).</summary>
    UseSkill = 3,
}

/// <summary>What one perform (0x4AC180) did.</summary>
public enum AiPerformKind
{
    /// <summary>In a round, another critter's subturn: nothing.</summary>
    NotActor,
    /// <summary>BACKING_OFF: the run to the back-off's tile started.</summary>
    BackOffRun,
    /// <summary>BACKING_OFF: reached, or no "away" path (MR15): the bit was cleared.</summary>
    BackOffEnded,
    /// <summary>BACKING_OFF: the run to the tile was refused by its starter.</summary>
    BackOffRefused,
    Cast,
    UseItem,
    UseSkill,
    /// <summary>Danger state NONE with a leader that is not waiting: the follow starter was asked (<see cref="AiPerformResult.Run"/>
    /// is the started run, or null when a run was kept or refused) and the loyalty check ran.</summary>
    Follow,
    /// <summary>Danger state NONE without a leader: the routine ran.</summary>
    Routine,
    /// <summary>Danger state NONE, a waiting follower: only the combat-mode exit.</summary>
    Waiting,
    /// <summary>COMBAT_FOCUS: a back-off started (BACKING_OFF set; the run follows on the next pass).</summary>
    BackOffStart,
    /// <summary>COMBAT_FOCUS: a grenade was thrown (slice I8).</summary>
    Grenade,
    /// <summary>COMBAT_FOCUS: the attack starter ran (<see cref="AiPerformResult.Run"/> and <see cref="AiPerformResult.Refusal"/>).</summary>
    Attack,
    /// <summary>COMBAT_FOCUS: a "default throws" weapon was taken off and thrown at the target's tile.</summary>
    ThrowWeapon,
    /// <summary>FLEE: perform fleeing ran; <see cref="AiPerformResult.Run"/> is the flee goal's run when it started.</summary>
    Flee,
    /// <summary>SURRENDER: perform surrender ran.</summary>
    Surrender,
}

/// <summary>Why a starter refused to start its goal (none: it started, or kept or updated the run).</summary>
public enum AiStartRefusal
{
    None,
    /// <summary>The start gate (0x4348E0): not active, or in a round not the actor or (an NPC) the cost above its action points with
    /// fatigue at most 35 %.</summary>
    Gate,
    /// <summary>An attack on oneself.</summary>
    Self,
    /// <summary>In Body of Air without the elemental flag.</summary>
    BodyOfAir,
    /// <summary>An equivalent goal is already queued (an attack on the same target, a follow or a walk to the leader).</summary>
    AlreadyQueued,
    /// <summary>The interrupt of the lower-priority animations (priority 3) failed.</summary>
    Interrupt,
    /// <summary>A boomerang of the attacker is in flight.</summary>
    BoomerangInFlight,
    /// <summary>Not an NPC (the follow starter).</summary>
    NotNpc,
    /// <summary>The follower is close enough to its leader (within 4, 7 spread out): no new goal.</summary>
    LeaderNear,
    /// <summary>No target, leader or source to start toward.</summary>
    NoObject,
}

/// <summary>What one perform did, the run it started or updated (null: none) and a starter's refusal.</summary>
public sealed record AiPerformResult(AiPerformKind Kind, AiGoalRun? Run = null, AiStartRefusal Refusal = AiStartRefusal.None);

/// <summary>Perform fleeing's answer (0x4AC620, the session's <c>PerformFleeing</c>): whether the flee goal starts, and from what.</summary>
public readonly record struct AiFleeing(bool StartsFleeGoal, string? Source);

/// <summary>
/// The world perform runs in: the goal world plus the danger record, the I5 morale steps, the pipeline's other actions and the
/// starters' facts. Members are called at the original's positions.
/// </summary>
public interface IAiPerformWorld : IAiGoalWorld
{
    /// <summary>Active (0x45D790), the start gate's first test.</summary>
    bool Active(string id);
    /// <summary>The danger state as perform reads it (0x4AAB00).</summary>
    DangerState ReadDanger(string npc);
    DangerFlags DangerBits(string npc);
    /// <summary>The back-off's step (<see cref="MoraleRules.BackOff"/>).</summary>
    BackOffRun BackOff(string npc, string? target);
    /// <summary>The "away" path's end tile at <paramref name="range"/> (0x41F3C0 along <see cref="MoraleRules.AwayOffsets"/>),
    /// or null when no straight line is clear.</summary>
    CellCoord? AwayTile(string npc, string? target, int range);
    /// <summary>Clears BACKING_OFF (the session's <c>EndBackOff</c>).</summary>
    void EndBackOff(string npc);
    /// <summary>Casts the pass record's spell or spell item (0x4AC250, the magic domain).</summary>
    void Cast(string npc);
    /// <summary>Starts the item use (0x4AC320 → 0x435450, slice I8).</summary>
    void UseItem(string npc);
    /// <summary>Starts the skill use (0x4AC350 → 0x4350F0, the skills domain).</summary>
    void UseSkill(string npc);
    /// <summary>Takes the NPC out of combat mode (non-combat perform's first step).</summary>
    void LeaveCombatMode(string npc);
    /// <summary>The NPC's leader (0x45DE00), <see cref="AiSchedulerRules.Pc"/> for the PC, or null.</summary>
    string? Leader(string npc);
    /// <summary>NPC flag "wait here".</summary>
    bool Waiting(string npc);
    /// <summary>The loyalty check after the follow request (companion C13).</summary>
    void LoyaltyCheck(string npc);
    /// <summary>The routine of an NPC without a leader (S22).</summary>
    void RunRoutine(string npc);
    /// <summary>Combat perform's back-off start (<see cref="MoraleRules.StartsBackOff"/> and the session's <c>StartBackOff</c>):
    /// true when it started.</summary>
    bool StartBackOff(string npc, string? target);
    /// <summary>Combat perform's grenade (0x4AC910, slice I8): true when one was thrown.</summary>
    bool ThrowGrenade(string npc, string? target);
    /// <summary>The wielded weapon has the "default throws" flag and can be unwielded.</summary>
    bool ThrowsWieldedWeapon(string npc);
    /// <summary>Takes the weapon off and starts the throw goal (37) at the target's tile.</summary>
    void ThrowWeapon(string npc, string target);
    bool BoomerangInFlight(string npc);
    /// <summary>Interrupts the object's animations below priority 3 (0x424070): false when that fails.</summary>
    bool InterruptLowerAnimations(string id);
    /// <summary>Asks the critters sharing the object's tile to step apart (0x4364D0).</summary>
    void StepApart(string id);
    /// <summary>Perform fleeing (the session's <c>PerformFleeing</c>).</summary>
    AiFleeing PerformFleeing(string npc);
    /// <summary>Perform surrender (<see cref="MoraleRules.SurrenderEnds"/> and the setter's NONE).</summary>
    void PerformSurrender(string npc);
}
