using System.Collections.Immutable;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Goals;

namespace Arcanum.Simulation.Ai;

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
    /// <summary>ARC-AI-1313 (0x425930, 0x425D60): the wander goal found a tile within its radius that a short enough path reaches and became the walk to it.</summary>
    WanderWalk,
    /// <summary>ARC-AI-1313: the wander goal found no tile this time (no path, a path longer than its radius, or for the dark wander a lighter tile) and restarts.</summary>
    WanderRetry,
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
    /// <summary>ARC-AI-1315: the please-move goal found no clear neighbouring tile (0x424E00 answered 0): the goal ends.</summary>
    PleaseMoveNoTile,
    /// <summary>A path the goal needed was not found.</summary>
    NoPath,
    /// <summary>A step was blocked (a living critter near the path's end, a spotted trap, a step that cannot start).</summary>
    StepBlocked,
    /// <summary>ARC-DEV-302 (GT148): the mover is held (paralysed or stoned, an entangled hero in Steamwoven): the walk waits in place for the hold to end.</summary>
    StepHeld,
    /// <summary>A step or an attack the owner cannot pay in its subturn: the run is killed.</summary>
    Unpaid,
    /// <summary>The leader or the target is not known to the world any more.</summary>
    ObjectGone,
    /// <summary>The casting goal's start refused (action points, the pre-animation check, a target destroyed or off, or the caster
    /// cannot turn to it): the run is freed (0x429CD0) and the goal ends.</summary>
    CastRefused,
    /// <summary>The cast plays toward its action frame.</summary>
    CastPlays,
    /// <summary>The cast was released (its BEGIN, or its missile launched).</summary>
    CastReleased,
    /// <summary>The release found its target invalid, or the missile did not start.</summary>
    CastNotReleased,
    /// <summary>ARC-AI-1137 (GT122): the hero's talk was started with Shift (the no-walk flag) and the NPC is 5 or more cells away: the approach never steps, the run ends at once, silently.</summary>
    NoWalkOutOfRange,
    /// <summary>A skill, item-use or pickup goal walks to its object.</summary>
    ActionApproach,
    /// <summary>A skill's attempt ran.</summary>
    SkillAttempted,
    /// <summary>An action goal could not start: no turn to the target, no art, an animation that cannot start or a target destroyed
    /// or off.</summary>
    ActionRefused,
    /// <summary>The item was used on the target.</summary>
    ItemUsed,
    /// <summary>The item is no longer held by the user: nothing is used.</summary>
    ItemNotHeld,
    /// <summary>The item was thrown.</summary>
    Thrown,
    /// <summary>The item was picked up (or was gone).</summary>
    PickedUp,
    /// <summary>ARC-DEV-209 (GT49): the walk met a closed idle door and pushed the opener goal 11.</summary>
    DoorPush,
}

/// <summary>
/// One step of a run (<see cref="AiExecutorRules.Step"/>): the run after it (null once it ended), the delay in game milliseconds
/// before the next step (0 once it ended), how it ended, the nodes it ran and the reason code it reported.
/// </summary>
public sealed record AiGoalStepResult(GoalRun? Run, int DelayMs, AiRunEnd End, int Nodes, AiExecutorReason Reason)
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

/// <summary>A path the executor asks the world for. <see cref="Running"/> names the walk's gait for the planner's costs;
/// <see cref="NearOccupied"/> is the run's flag 0x4000 (<see cref="GoalRunFlags.NearOccupied"/>).</summary>
public sealed record AiPathRequest(AiPathKind Kind, string? Target, CellCoord? Tile, int Range, bool Straight, bool Running)
{
    public bool NearOccupied { get; init; }
}

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

/// <summary>Whether a walk goal's next step is blocked: an occupied tile waits 0..300 ms before the walk gives up, a step that cannot
/// start gives up at once (ledger EX20). ARC-DEV-302 (GT148): a held mover (paralysed or stoned, and in Steamwoven an entangled hero) waits in place: the step start answers false, the node
/// starts again in the next frame and the run stays, so the walk goes on to its old destination when the hold ends.</summary>
public enum AiStepBlock
{
    Clear,
    Occupied,
    CannotStart,
    Held,
}

/// <summary>The attack animation's timing: game milliseconds to its action frame, then to its end (with the pause, RT8).</summary>
public readonly record struct AiAttackSwing(int ActionFrameMs, int RestMs);

/// <summary>The casting goal's start as the cast host answers it (0x429B50: the 4 action points and the pre-animation check; then
/// 0x429BC0: the target neither destroyed nor off).</summary>
public enum AiCastGoalStart
{
    Started,
    /// <summary>The action points or the pre-animation check refused.</summary>
    Refused,
    /// <summary>The run's target is destroyed or switched off.</summary>
    TargetGone,
}

/// <summary>
/// How a cast plays (the attempt goals' start: 0x429F00 for goal 27, the casting animation 0x42A010 for goal 83). Without a play
/// (<see cref="Plays"/> false: the target re-check 0x456A90 at the play's start failed, or the spell has no art to play) the cast is
/// released at once and the goal ends. Otherwise the game milliseconds to the action frame, then to the play's end.
/// </summary>
public readonly record struct AiCastSwing(bool Plays, int ActionFrameMs = 0, int RestMs = 0);

/// <summary>The animation an action goal plays (the art's animation number: 12 a skill and pick locks, 14 a throw).</summary>
public enum AiActionAnimation
{
    /// <summary>Animation 12 started by 0x42CB10 (goals 34 and 86).</summary>
    Skill,
    /// <summary>Animation 12 looped by 0x42D300, its loop count set by the pick-locks starter (goal 67).</summary>
    PickLocks,
    /// <summary>Animation 14 started by 0x42CB10 (goal 37).</summary>
    Throw,
}

/// <summary>Whether an action animation started: the art for the animation is missing (0x42BD40), or it cannot start (0x42CB10: object
/// flag 0x10000, a stunned or paralysed critter; 0x42D300: no loop left or not a critter).</summary>
public enum AiActionStart
{
    Started,
    NoArt,
    CannotStart,
}

/// <summary>An action animation's start and its timing: game milliseconds to its action frame (the pick-locks loops' end), then to
/// its end.</summary>
public readonly record struct AiActionSwing(AiActionStart Start, int ActionFrameMs = 0, int RestMs = 0);

/// <summary>The item types the item use (0x462CC0) branches on (object field 0x14F); every other type is <see cref="Other"/>.</summary>
public enum AiItemKind
{
    Other = 0,
    Food = 9,
    Scroll = 10,
    Written = 13,
    Generic = 14,
}

/// <summary>
/// What the item use (0x462CC0) reads of an item and its user (field names of ledger TP1): its type; for a scroll, whether its
/// spell's minimum intelligence (0x4B1750) is above the user's (stat 4); item flag 0x100 (OIF_NEEDS_TARGET); a trap device (generic
/// flag 0x4, 0x4BCA60); the spell charges (field 0x69) and the magical item flag 0x4; and a generic item's flags 0x2 (lockpick) and 0x8
/// (healing kit).
/// </summary>
public readonly record struct AiUsedItem(
    AiItemKind Kind = AiItemKind.Other,
    bool ScrollTooHard = false,
    bool NeedsTarget = false,
    bool TrapDevice = false,
    int Charges = 0,
    bool Magical = false,
    bool Lockpick = false,
    bool HealingKit = false);

/// <summary>Which branch of the item use (0x462CC0) ran.</summary>
public enum AiItemUseStep
{
    /// <summary>A scroll whose spell needs more intelligence than the user has: nothing (a PC is told, message 13, the host's).</summary>
    ScrollTooHard,
    /// <summary>No target and OIF_NEEDS_TARGET: the targeting cursor opens (0x4EE060) and the use returns.</summary>
    Targeting,
    /// <summary>A trap device: its use on the target (0x4BC2E0, ledger TP2).</summary>
    TrapDevice,
    /// <summary>The use script (0x441980) stopped the use: 4 action points.</summary>
    ScriptStopped,
    /// <summary>A written item without magic: read (0x4606F0).</summary>
    Read,
    /// <summary>A lockpick without magic: the pick-locks skill on the target with the item (0x4352C0, goal 40).</summary>
    Lockpick,
    /// <summary>A lockpick on a target that cannot be locked (0x441CF0): nothing more.</summary>
    NotLockable,
    /// <summary>A healing kit without magic: the heal skill on the target with the item (0x4352C0, goal 40).</summary>
    HealingKit,
    /// <summary>Food or a scroll without magic: spent (a PC eating food is told, message 201, the host's).</summary>
    Spent,
    /// <summary>Any other item without magic: nothing but the 4 action points.</summary>
    Nothing,
    /// <summary>An item with spell charges or the magical flag: its slot-0 spell (0x4F2810 … 0x4CBF70), then food and scrolls spent.</summary>
    Magic,
}

/// <summary>The item use's answer: the branch, whether the 4 action points were charged (only in a round, on the user's subturn) and
/// whether the item was spent (its magic call 0x4574D0 and its destruction 0x43CCA0).</summary>
public sealed record AiItemUseResult(AiItemUseStep Step, bool Charged, bool Spent);

/// <summary>
/// ARC-UI-025: what the item use (0x462CC0, <see cref="AiExecutorRules.UseItemNow"/>) reads and starts, split from
/// <see cref="IAiGoalWorld"/> so a host other than the NPC executor (the player's inventory window) can run the same use.
/// </summary>
public interface IItemUseWorld
{
    /// <summary>The item use's facts of <paramref name="item"/> for <paramref name="user"/> (0x462CC0).</summary>
    AiUsedItem UsedItem(string user, string item);
    /// <summary>The use script (0x441980): false when it stopped the use.</summary>
    bool RunUseScript(string user, string item, string? target);
    /// <summary>Opens the targeting cursor for an item that needs a target (0x4EE060).</summary>
    void OpenTargeting(string user, string item);
    /// <summary>A trap device's use on its target (0x4BC2E0, ledger TP2).</summary>
    void UseTrapDevice(string user, string item, string? target);
    /// <summary>Reads a written item (0x4606F0).</summary>
    void ReadWritten(string user, string item);
    /// <summary>The target can be locked (0x441CF0).</summary>
    bool Lockable(string target);
    /// <summary>Starts a skill use with the item on the target (0x4352C0, goal 40 [ag_use_item_on_object_with_skill]).</summary>
    void StartSkillWithItem(string user, string item, string target, int skill);
    /// <summary>The item's slot-0 spell on the target (0x4F2810, 0x4CB7A0, 0x4605E0, 0x4CBF70).</summary>
    void UseItemMagic(string user, string item, string? target);
    /// <summary>Spends a used food or scroll: its magic call (0x4574D0), then its destruction (0x43CCA0).</summary>
    void SpendItem(string item);
    /// <summary>A turn-based round runs (0x4B6D70).</summary>
    bool InRound { get; }
    /// <summary>The critter whose subturn it is (0x4B6D80), or null.</summary>
    string? RoundActor { get; }
    /// <summary>The action-point consume for the round's actor (0x4B7CD0; a refusal empties the action points and passes the turn,
    /// TB9).</summary>
    void Consume(string id, int cost);
}

/// <summary>
/// The world the goals run in: positions, the facts each node tests, the planners, the step walker, the attack animation and
/// the round's action points. Ids are the host's object ids; <see cref="AiSchedulerRules.Pc"/> names the local PC. Every member is
/// called at the position of the original's call.
/// </summary>
public interface IAiGoalWorld : IItemUseWorld
{
    /// <summary>
    /// ARC-DEV-136 (MIG13 S5b, GT3): every live run of the object in slot order, the runs the original's equal-goal and alias tests walk (0x44E710 reads every run; 0x44E830 and 0x44E940 the first
    /// only). The default answers none, which makes the starters read the run they are handed (<c>current</c>, the object's first run), as a world without a shared table does.
    /// </summary>
    IReadOnlyList<GoalRun> RunsOf(string id) => [];

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
    /// test) the target's own tile is not blocking for the walker and nothing but the target blocks the step. On the object's own tile
    /// (distance 0: a pickup's item, an item used on oneself) there is no step and it answers true.</summary>
    bool FirstStepClear(string id, string toward, bool ignoreTarget);
    AiAttackFacts AttackFacts(string id, string target);
    AiReachFacts Reach(string id, string target);
    /// <summary>The wielded weapon's attack cost in action points (0x4B7C30).</summary>
    int AttackCost(string id);
    /// <summary>Plans a path, or null when there is none.</summary>
    AiPath? PlanPath(string id, AiPathRequest request);
    /// <summary>ARC-AI-1313 (the light sampler of the dark wander's node): the light level 0..255 at a cell, which the dark wander compares between its candidate tile and the NPC's own. The default answers full light everywhere, so a world with no light data
    /// lets the dark wander accept every tile.</summary>
    int CellLight(CellCoord cell) => 255;
    /// <summary>Whether the next step is blocked (0x4270B0 → 0x427110, then 0x42E9B0): by a living critter on it near the path's end,
    /// a newly spotted trap or, with <paramref name="flight"/>, any blocking tile (<see cref="AiStepBlock.Occupied"/>), or because the
    /// step cannot start (<see cref="AiStepBlock.CannotStart"/>).</summary>
    AiStepBlock StepBlocked(string id, CellCoord next, bool flight);
    /// <summary>The blocked-step test of the please-move goal's tile choice (0x425760, ARC-AI-1315): the step from the object's tile to the neighbouring <paramref name="next"/>
    /// is clear when the mover's flags, the tile test and the object traversal (a living critter on it blocks) let it through; no trap check and no random wait. The
    /// default asks <see cref="StepBlocked"/> without flight.</summary>
    bool StepClear(string id, CellCoord next) => StepBlocked(id, next, false) == AiStepBlock.Clear;
    /// <summary>
    /// ARC-DEV-209 (MIG14 S3, GT49): the walker's door test (node 7, 0x4272E0 → 0x440700): the session entity of the closed, idle door the step from the tile of <paramref name="id"/> to
    /// <paramref name="next"/> crosses, or null when it crosses none (an open door, a door with a run of its own, a step that crosses no portal). The default answers none, as a world with no doors.
    /// </summary>
    string? ClosedDoorOn(string id, CellCoord next) => null;
    /// <summary>ARC-DEV-209: the door ports the opener goals 11, 12, 49 and 50 of this world's objects run over (the walker's pushed goals); null when the world has no doors, which ends such a run.</summary>
    IAiDoorWorld? Doors => null;
    /// <summary>Starts the step to <paramref name="next"/> (doors, windows and the gait are the walker's): the game milliseconds it
    /// takes, at least 1. The object stands on <paramref name="next"/> when the next step of the run comes.</summary>
    int BeginStep(string id, CellCoord next, bool running);
    /// <summary>
    /// ARC-WORLD-1469 (ledger TT9, 0x4305D0 → 0x441980): the walker's step onto <paramref name="next"/> meets the first trap object there with the walker as the triggerer; true when the trap
    /// was spotted or went off, which ends the walk on that tile (the path index becomes the path's length, as the original's step function does). Asked right after <see cref="BeginStep"/> for the
    /// same step; a world answers false when that step did not begin. An NPC stands on the tile at once, a companion the host holds walks there over the next frames. The default meets nothing, as a world with no traps.
    /// </summary>
    bool StepMeetsTrap(string id, CellCoord next) => false;
    /// <summary>Gets the object up (goal 63): the game milliseconds it takes.</summary>
    int GetUp(string id);
    /// <summary>Unconceals the object (goal 54): the game milliseconds it takes.</summary>
    int Unconceal(string id);
    /// <summary>Turns the object to face <paramref name="toward"/> (0x42C240).</summary>
    void Face(string id, string toward);
    /// <summary>Plays the object's sound (the attack's alerted sound).</summary>
    void PlaySound(string id, int sound);
    /// <summary>Turns the object to the target and starts the attack animation (0x432700, after the cost was paid): its timing.</summary>
    AiAttackSwing BeginAttack(string id, string target);
    /// <summary>The hit step at the action frame (0x42A630 → 0x4B3BB0 → the attack resolution 0x4B3170): the world resolves the
    /// attack (the resolver, the wear step, the missile, the notification).</summary>
    void Hit(string id, string target);
    /// <summary>Sets NO_FLEE for good (0x4AF1D0).</summary>
    void SetNoFlee(string id);

    // ---- the action goals (slice I8b, ARC-WORLD-089) ----

    /// <summary>The casting goal's start for the magic run <paramref name="run"/> (the cast host's <c>StartGoal</c>: 0x429B50, the 4
    /// action points through the round's consume and the pre-animation check 0x450940, then 0x429BC0).</summary>
    AiCastGoalStart StartCast(string id, int run);
    /// <summary>Turns the caster to the run's target object or tile (0x42C850): false when it cannot turn.</summary>
    bool FaceCast(string id, int run);
    /// <summary>Starts the cast's play: <paramref name="castAnimation"/> for goal 83 (the casting animation), else goal 27 (the caster's
    /// eye candy).</summary>
    AiCastSwing BeginCast(string id, int run, bool castAnimation);
    /// <summary>The play's action frame (0x429C40): a spell with a missile art launches its missile (0x424820, 0x4246E0; its flight
    /// and arrival are the missile flight's), any other is released (<see cref="ReleaseCast"/>). False: not released, or no missile.</summary>
    bool CastActionFrame(string id, int run);
    /// <summary>The release (the cast host's <c>Release</c>, 0x429C80): false when the target re-check refused it.</summary>
    bool ReleaseCast(string id, int run);
    /// <summary>The casting goal ended (the cast host's <c>EndGoal</c>, 0x429CD0); <paramref name="refused"/> before its play.</summary>
    void EndCast(string id, int run, bool refused);
    /// <summary>Turns the object to <paramref name="target"/> (0x42C0F0) or to <paramref name="tile"/> (0x42C390): false for a missing
    /// object, object flag 0x10000 or a stunned or paralysed critter (critter flags 0x60).</summary>
    bool FaceForAction(string id, string? target, CellCoord? tile);
    /// <summary>Sets the action's animation (0x42BD40) and starts it (0x42CB10, 0x42D300): its start and timing.</summary>
    AiActionSwing BeginAction(string id, AiActionAnimation animation);
    /// <summary>The skill attempt's invocation (0x42A2A0 after its 4 action points: 0x4C7090 with the skill, the user, the target and the
    /// tool item, then 0x4C7160, the skills domain): true when its result sets the run's flag 0x40000, which a pickpocket attempt reads
    /// as "no animation".</summary>
    bool AttemptSkill(string id, string target, int skill, string? item);
    /// <summary>The pick-locks attempt (0x42A4E0: the skill invocation for skill 14, without action points).</summary>
    void AttemptPickLocks(string id, string target, string? item);
    /// <summary>The throw at the action frame (0x42AB90 → 0x4B3C00): false when the item is destroyed or off.</summary>
    bool ThrowItem(string id, string item, CellCoord tile);
    /// <summary>The pickup (0x42A9B0 → 0x42AA70: the item moves into the inventory; 1 action point for an item from a junk pile).</summary>
    void PickUp(string id, string item);
    /// <summary>The object whose inventory holds the item (0x4612A0), or null.</summary>
    string? ItemHolder(string item);
    /// <summary>The round actor's action points left.</summary>
    int ActionPoints(string id);
    /// <summary>The round's pending cost (0x5FC244): the value the last action-point check wrote; the host sets it to 0 at each
    /// subturn start (0x4B75D0).</summary>
    int PendingCost { get; set; }
    /// <summary>Ends the critter's subturn (0x4B7C90).</summary>
    void EndSubturn(string id);

    // ---- the critter states (slice S6, ARC-DEV-137: goals 57, 62, 63 and 31) ----

    /// <summary>The ART the object shows for <paramref name="animation"/>: its frames and rate, or null when the header does not read (or there is no art), which the animation starts treat as the original's
    /// unreadable header (100 ms; the stun start then refuses).</summary>
    AiAnimationClip? Clip(string id, int animation) => null;
    /// <summary>The animation the object's art shows now (0 standing, 7 lying in its fall, 8 getting up, 17..19 dying, 23 stunned): what the tests of the critter-state nodes read (0x45D730).</summary>
    int ShownAnimation(string id) => 0;
    /// <summary>The frame of <see cref="ShownAnimation"/> the art shows.</summary>
    int ShownFrame(string id) => 0;
    /// <summary>Sets the object's art to <paramref name="animation"/> at <paramref name="frame"/> (the nodes' art writes).</summary>
    void ShowArt(string id, int animation, int frame) { }
    /// <summary>Whether the critter can act (0x45D790): not destroyed or off, alive, conscious, not stunned or paralysed or stoned.</summary>
    bool CanAct(string id) => !Gone(id) && !DeadCritter(id) && !Mover(id).CannotTurn;
    /// <summary>Whether the critter is unconscious (0x45D800).</summary>
    bool Unconscious(string id) => false;
    /// <summary>Object flag 0x10000 (a stoned object, DA5): the animation steps refuse it.</summary>
    bool Stoned(string id) => false;
    /// <summary>Critter flag 0x40 (a second "cannot act" bit, paralysis by its use).</summary>
    bool Paralysed(string id) => false;
    /// <summary>Clears the critter's stunned bit (the stun's countdown node and end node).</summary>
    void ClearStunned(string id) { }
    /// <summary>The dying goal's last node (0x42FF40): the dead flags and the notification.</summary>
    void DyingFinished(string id) { }
}

/// <summary>One ART's animation as the critter-state nodes read it: its frames and its rate in frames per second.</summary>
public readonly record struct AiAnimationClip(int Frames, int FramesPerSecond);

/// <summary>The action the pass record names (field +0x14): perform runs it before the danger state's branch.</summary>
public enum AiPassAction
{
    None = 0,
    /// <summary>1: a spell or a spell item (the magic invocation and the casting goal 26 or 82).</summary>
    Cast = 1,
    /// <summary>2: an item use on the target (the item-use goal 39).</summary>
    UseItem = 2,
    /// <summary>3: a skill use on the target (the skill goal 33).</summary>
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
    /// <summary>A cast: the casting goal's run when the magic domain started one (<see cref="AiPerformResult.Run"/>).</summary>
    Cast,
    /// <summary>The item-use starter ran (0x435450).</summary>
    UseItem,
    /// <summary>The skill starter ran (0x4350F0, or 0x436220 for pick locks).</summary>
    UseSkill,
    /// <summary>The throw starter ran on its own (0x434F80).</summary>
    Throw,
    /// <summary>The pickup starter ran (0x4356C0).</summary>
    Pickup,
    /// <summary>Danger state NONE with a leader that is not waiting: the follow starter was asked and the loyalty check ran.</summary>
    Follow,
    /// <summary>Danger state NONE without a leader: the routine ran.</summary>
    Routine,
    /// <summary>Danger state NONE, a waiting follower: only the combat-mode exit.</summary>
    Waiting,
    /// <summary>COMBAT_FOCUS: a back-off started (BACKING_OFF set; the run follows on the next pass).</summary>
    BackOffStart,
    /// <summary>COMBAT_FOCUS: the grenade step chose a throw (<see cref="AiGroundRules.Grenade"/>); the throw starter ran with the
    /// grenade at the target's tile, and perform goes no further whether it started or not.</summary>
    Grenade,
    /// <summary>COMBAT_FOCUS: the attack starter ran (<see cref="AiPerformResult.Run"/> and <see cref="AiPerformResult.Refusal"/>).</summary>
    Attack,
    /// <summary>COMBAT_FOCUS: a "default throws" weapon was taken off and the throw starter ran with it at the target's tile.</summary>
    ThrowWeapon,
    /// <summary>FLEE: perform fleeing ran; <see cref="AiPerformResult.Run"/> is the flee goal's run when it started.</summary>
    Flee,
    /// <summary>SURRENDER: perform surrender ran.</summary>
    Surrender,
    /// <summary>ARC-AI-1315: the please-move starter (0x4363E0) started goal 68 on a critter that shares a tile with another (the run is that critter's, not the performer's).</summary>
    PleaseMove,
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
    /// <summary>No target, leader, source, item or tile to start toward.</summary>
    NoObject,
    /// <summary>The pick-locks starter found no art for the skill animation (12).</summary>
    NoArt,
    /// <summary>The item system's pickup switch is off for NPCs (global 0x5E8800 is 0).</summary>
    PickupsOff,
    /// <summary>The magic domain started no casting run (the invocation check refused, or the cast was queued under a running one).</summary>
    NoRun,
    /// <summary>ARC-AI-1315: the attacker's tile is shared with another living, not busy NPC (the finder 0x436720): one of them was asked to move (0x4363E0) and no attack starts.</summary>
    TileSharer,
}

/// <summary>
/// What one perform did, the object's run after it and a starter's refusal. <see cref="Run"/> is the run the host keeps: a started
/// or retargeted run, else the current run it was given (null: none); <see cref="Started"/> says which.
/// </summary>
public sealed record AiPerformResult(AiPerformKind Kind, GoalRun? Run = null, AiStartRefusal Refusal = AiStartRefusal.None)
{
    /// <summary>Perform started or retargeted <see cref="Run"/>, which replaces the object's current run.</summary>
    public bool Started { get; init; }
}

/// <summary>Perform fleeing's answer (0x4AC620, the session's <c>PerformFleeing</c>): whether the flee goal starts, and from what.</summary>
public readonly record struct AiFleeing(bool StartsFleeGoal, string? Source);

/// <summary>
/// The magic domain's answer to perform's cast (0x4AC250): <see cref="Run"/> the magic run whose casting goal starts (null: none
/// started), <see cref="CastAnimation"/> for goal 82 (the record's casting animation is not −1; else goal 26), and
/// <see cref="Queued"/> when the cast was queued under the caster's running cast (SU13), which the host keeps.
/// </summary>
public readonly record struct AiCastStart(int? Run, bool CastAnimation = false, bool Queued = false);

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
    /// <summary>Perform's cast (0x4AC250, the magic domain): the invocation (0x455A20) from the choice's item when one is set, else
    /// the NPC, with the choice's spell and target; its check (0x4564E0); the run (0x455AC0 → 0x455C30), which picks goal 26 or 82
    /// and starts it without a start gate; then, with an item, its magic call (0x4574D0) and a food or scroll destroyed (0x43CCA0).</summary>
    AiCastStart Cast(string npc, AiPassChoice choice);
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
    /// <summary>Combat perform's grenade step (0x4AC910): <see cref="AiGroundRules.Grenade"/> over the host's grenade world.</summary>
    AiGrenadeResult Grenade(string npc, string target);
    /// <summary>The weapon in the weapon slot when it is a weapon with the "default throws" flag (field 0x70, 0x200) that the remove
    /// check (0x466DA0) lets go, else null.</summary>
    string? ThrowableWeapon(string npc);
    /// <summary>The attack starter's removal of the item from its holder (0x466E00: out of the holder's inventory entirely, ledger TA1;
    /// the throw places it, 0x4B3C00).</summary>
    void Unwield(string npc, string item);
    /// <summary>The NPC's art has the skill animation (12; 0x503E50, 0x5030B0), which the pick-locks starter needs.</summary>
    bool HasSkillArt(string npc);
    /// <summary>The item system's pickup switch (global 0x5E8800, 1 after the item system starts; another part of the game raises and
    /// lowers it): the pickup starter refuses an NPC while it is 0.</summary>
    bool NpcPickupsOn { get; }
    bool BoomerangInFlight(string npc);
    /// <summary>Interrupts the object's runs at level 3 (0x424070, ledger GS13): the table's rule decides, the end nodes of the runs it ends run, and false when a run
    /// refuses it (its current goal has priority 4 or 5). ARC-DEV-135 (MIG13 S5): it was always true before.</summary>
    bool InterruptLowerAnimations(string id);
    /// <summary>The critters of both kinds standing on the object's tile, the object among them (the list of 0x436720 and 0x4364D0, 0x406DA0 with the critter mask),
    /// in the tile's list order; each with its dead and busy-run test and its handle stand-in (<see cref="AiTileCritter"/>). ARC-AI-1315. The default sees none.</summary>
    IReadOnlyList<AiTileCritter> TileCritters(string id) => [];
    /// <summary>The please-move starter's last part (0x4363E0 after its choice, gate and busy test): goal 68 with <paramref name="awayFrom"/> as its object starts on
    /// <paramref name="mover"/>, interrupting its runs at the goal's priority; true when the run started. ARC-AI-1315. The default starts nothing.</summary>
    bool StartPleaseMove(string mover, string awayFrom) => false;
    /// <summary>Perform fleeing (the session's <c>PerformFleeing</c>).</summary>
    AiFleeing PerformFleeing(string npc);
    /// <summary>Perform surrender (<see cref="MoraleRules.SurrenderEnds"/> and the setter's NONE).</summary>
    void PerformSurrender(string npc);
}
