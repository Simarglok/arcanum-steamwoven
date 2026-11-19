using Arcanum.Simulation.Combat;

namespace Arcanum.Simulation.Ai;

/// <summary>
/// The NPC AI scheduler (stage A1, research ARC-RES-054; slice I6, ARC-WORLD-084, re-read in the retail 1.0.7.4 executable): the
/// AI tick's cadence and paths, the 30-tile activity test, the follower catch-up, one AI pass with its gate, and the subturn hook
/// of a turn-based round, as drivers over an <see cref="IAiSchedulerWorld"/> and an <see cref="IAiRollSource"/>. The world runs
/// the scripts, the animations and the pipeline's stages (heal and item search of slice I8, the danger update of slices I4 and
/// I5, perform of slices I5 and I7); the session schedules the tick on its event queue and holds the state the gate writes.
/// </summary>
public static class AiSchedulerRules
{
    /// <summary>The id <see cref="IAiHost.Leader"/>, the script triggerers and <see cref="IAiSchedulerWorld.RoundActor"/> use for the PC.</summary>
    public const string Pc = "pc";

    /// <summary>An NPC thinks in real time only within this many tiles of the PC; a follower farther from its PC leader catches up.</summary>
    public const int ActivityRange = 30;

    /// <summary>The tick delay at distance 0, in game milliseconds.</summary>
    public const long NearDelayMs = 250;

    /// <summary>The tick delay at the activity range and beyond.</summary>
    public const long FarDelayMs = NearDelayMs + 4750;

    /// <summary>A wake-up tick adds 0..this many milliseconds to the tick it schedules.</summary>
    public const int WakeUpJitterMs = 5000;

    /// <summary>The gate clears blindness on a roll of 1 in 1..this.</summary>
    public const int BlindRecoverySides = 160;

    /// <summary>The gate clears every crippled condition on a roll of 1 in 1..this.</summary>
    public const int CrippleRecoverySides = 8000;

    /// <summary>The gate ends the pass while a goal of a priority above this runs.</summary>
    public const int BusyPriority = 2;

    // ---- cadence ----

    /// <summary>The distance the delay counts (0x4AD610): the distance to the PC, or the activity range when it is negative or
    /// larger.</summary>
    [OriginalFunction(0x4AD610, OriginalFunctionScope.Partial)]
    public static int TickDistance(long distance) => distance is < 0 or > ActivityRange ? ActivityRange : (int)distance;

    /// <summary>The delay of the next tick (0x4AD5D0): <c>250 + 4750 × d / 30</c> game milliseconds, truncating, <c>d</c> the
    /// <see cref="TickDistance"/>. 250 ms next to the PC, 5000 ms at 30 tiles and beyond.</summary>
    [OriginalFunction(0x4AD5D0, OriginalFunctionScope.Full)]
    public static long TickDelayMs(long distance) => NearDelayMs + 4750L * TickDistance(distance) / ActivityRange;

    /// <summary>The delay a tick schedules: <see cref="TickDelayMs"/>, plus a uniform 0..5000 ms (<see cref="AiRollPurpose.TickWakeUpJitter"/>)
    /// when the tick carried the wake-up marker; the tick it schedules carries none.</summary>
    public static long NextTickDelayMs(long distance, bool wakeUp, IAiRollSource rolls)
    {
        ArgumentNullException.ThrowIfNull(rolls);
        var delay = TickDelayMs(distance);
        return wakeUp ? delay + rolls.Roll(AiRollPurpose.TickWakeUpJitter, 0, WakeUpJitterMs) : delay;
    }

    /// <summary>The near test (0x4AD420): alive, no round, and at most 30 tiles from the PC; a negative distance counts as near
    /// (unlike <see cref="TickDistance"/>).</summary>
    [OriginalFunction(0x4AD420, OriginalFunctionScope.Partial)]
    public static bool IsNear(bool dead, bool inRound, long distance) => !dead && !inRound && distance <= ActivityRange;

    /// <summary>Whether the follower catch-up (0x4AD4D0) moves the NPC to its leader: alive, no round, a PC leader more than 30
    /// tiles away (a negative distance is not), not waiting, not paralysed and active.</summary>
    [OriginalFunction(0x4AD4D0, OriginalFunctionScope.Partial)]
    public static bool CatchesUp(bool dead, bool inRound, AiCatchUpFacts facts) =>
        !dead && !inRound && facts.HasPcLeader && facts.LeaderDistance > ActivityRange && !facts.Waiting && !facts.Paralyzed && facts.Active;

    // ---- the tick ----

    /// <summary>
    /// One AI tick of <paramref name="npc"/> (0x4AD200). A wake-up of a living NPC sets "check wield", runs a leaderless NPC's
    /// waypoints outside combat mode (its standpoints when the waypoints did nothing) and the first heartbeat; anything but
    /// "continue" goes to the far path. Near (<see cref="IsNear"/>): the heartbeat, and on "continue" a generator's spawner (its
    /// delay schedules the next tick; a refusal destroys an encounter creature outside a round and schedules nothing) or one
    /// <see cref="Pass"/>. Far: outside a round a living NPC's animations are interrupted, then the catch-up
    /// (<see cref="CatchesUp"/>); a moved follower or a loaded sector gets the next tick, otherwise an encounter creature is
    /// destroyed outside a round and nothing is scheduled. The next tick comes after <see cref="NextTickDelayMs"/>.
    /// </summary>
    [OriginalFunction(0x4AD200, OriginalFunctionScope.Partial)]
    public static AiTickResult Tick(IAiSchedulerWorld world, string npc, bool wakeUp, IAiRollSource rolls, CombatAnomalyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(npc);
        ArgumentNullException.ThrowIfNull(rolls);
        ArgumentNullException.ThrowIfNull(profile);
        var steps = AiTickSteps.None;
        var near = true;
        if (wakeUp && !world.IsDead(npc))
        {
            steps |= AiTickSteps.WakeUp;
            world.WriteDangerBits(npc, DangerFlags.CheckWield, DangerFlags.None);
            if (world.Leader(npc) is null && !world.InCombatMode(npc))
            {
                steps |= AiTickSteps.Waypoints;
                if (!world.RunWaypoints(npc))
                {
                    steps |= AiTickSteps.Standpoints;
                    world.RunStandpoints(npc);
                }
            }
            if (world.RunScript(AiScriptPoint.FirstHeartbeat, npc, npc)) steps |= AiTickSteps.FirstHeartbeatContinued;
            else near = false;
        }
        AiPassResult? pass = null;
        if (near && IsNear(world.IsDead(npc), world.InRound, world.DistanceToPc(npc)))
        {
            steps |= AiTickSteps.Heartbeat;
            if (world.RunScript(AiScriptPoint.Heartbeat, npc, npc))
            {
                steps |= AiTickSteps.HeartbeatContinued;
                if (world.IsGenerator(npc))
                {
                    if (world.Spawn(npc) is { } spawned) return new(steps | AiTickSteps.Spawned, AiTickNext.Spawner, spawned, null);
                    return Unscheduled(world, npc, steps | AiTickSteps.SpawnRefused, null);
                }
                pass = Pass(world, npc, rolls, profile);
                steps |= AiTickSteps.Pass;
            }
            return Next(world, npc, wakeUp, rolls, steps, pass);
        }
        if (!world.InRound && !world.IsDead(npc))
        {
            world.InterruptAnimations(npc);
            steps |= AiTickSteps.AnimationsInterrupted;
        }
        var loaded = world.SectorLoaded(npc);
        // The catch-up asks for its facts only about a living NPC outside a round.
        if (!world.IsDead(npc) && !world.InRound && CatchesUp(false, false, world.CatchUp(npc)))
        {
            world.MoveToLeader(npc);
            steps |= AiTickSteps.CaughtUp;
        }
        else if (!loaded) return Unscheduled(world, npc, steps, null);
        return Next(world, npc, wakeUp, rolls, steps, null);
    }

    private static AiTickResult Next(IAiSchedulerWorld world, string npc, bool wakeUp, IAiRollSource rolls, AiTickSteps steps, AiPassResult? pass) =>
        new(steps, AiTickNext.Delay, NextTickDelayMs(world.DistanceToPc(npc), wakeUp, rolls), pass);

    // No next tick: an encounter creature is destroyed outside a round.
    private static AiTickResult Unscheduled(IAiSchedulerWorld world, string npc, AiTickSteps steps, AiPassResult? pass)
    {
        if (!world.InRound && world.IsEncounterCreature(npc))
        {
            world.Destroy(npc);
            steps |= AiTickSteps.Destroyed;
        }
        return new(steps, AiTickNext.None, 0, pass);
    }

    // ---- the pass ----

    /// <summary>
    /// One AI pass of <paramref name="npc"/> (0x4A84F0): none while the map clears its objects; the danger state and its source
    /// are read, then the <see cref="Gate"/>; when it passes, heal, else the item search, else the danger update, then perform.
    /// </summary>
    [OriginalFunction(0x4A84F0, OriginalFunctionScope.Partial)]
    public static AiPassResult Pass(IAiSchedulerWorld world, string npc, IAiRollSource rolls, CombatAnomalyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(npc);
        ArgumentNullException.ThrowIfNull(rolls);
        ArgumentNullException.ThrowIfNull(profile);
        if (world.MapClearing) return new(true, null, null);
        // ARC-AI-2793: the heartbeat of an NPC the host forgot (a window move) reaches here; the stages below read the host's row, which is gone.
        if (!world.Knows(npc)) return new(false, null, null) { NotHeld = true };
        var gate = Gate(world, npc, world.Danger(npc), rolls, profile);
        if (!gate.Passed) return new(false, gate, null);
        var choice = world.Heal(npc) ? AiPipelineChoice.Heal
            : world.SearchItems(npc) ? AiPipelineChoice.ItemSearch
            : AiPipelineChoice.DangerUpdate;
        if (choice == AiPipelineChoice.DangerUpdate) world.UpdateDanger(npc);
        world.Perform(npc);
        return new(false, gate, choice);
    }

    /// <summary>
    /// The gate (0x4A8570) in the original's order, with the danger state and source read when the pass was set up. Dead,
    /// hidden (unless sleeping), the PC hidden, stoned and a set conversation partner end the pass (with the PC as partner, the
    /// callback set and the state COMBAT_FOCUS or FLEE, the conversation is ended first). In combat mode the end-combat script
    /// (outside a round) and the start-combat script run, the source as triggerer (P5). Another critter's turn and a busy
    /// animation end the pass. Blindness clears on 1 in 160, every crippled condition on 1 in 8000; a stunned NPC ends the pass,
    /// clearing the stun first when its animation no longer runs (CL1). These three test the critter flags read before the
    /// conversation test; the Original setting writes that copy back too, undoing a change of the scripts (CL5,
    /// <see cref="CombatAnomalyProfile.GateRecoveryWritesStaleFlags"/>), Steamwoven clears only its own bits of the current
    /// flags. Unconscious and a generator end the pass; "check wield" re-wields every slot and clears "check wield" and
    /// "check weapon" (CL2), else "check weapon" the weapon slot; DEMAINTAIN_SPELLS with the leader missing, dead or out of
    /// combat mode is cleared and ends the maintained spells.
    /// </summary>
    [OriginalFunction(0x4A8570, OriginalFunctionScope.Full)]
    public static AiGateResult Gate(IAiSchedulerWorld world, string npc, (DangerState State, string? Source) danger, IAiRollSource rolls,
        CombatAnomalyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(npc);
        ArgumentNullException.ThrowIfNull(rolls);
        ArgumentNullException.ThrowIfNull(profile);
        var steps = AiGateSteps.None;
        if (world.IsDead(npc)) return new(AiGateStop.Dead, steps);
        if (!world.Sleeping(npc) && world.Hidden(npc)) return new(AiGateStop.Hidden, steps);
        if (world.PcHidden) return new(AiGateStop.PcHidden, steps);
        if (world.IsStoned(npc)) return new(AiGateStop.Stoned, steps);
        var copy = world.CritterFlags(npc);
        var partner = world.Conversation(npc);
        if (partner != AiConversation.None)
        {
            if (partner == AiConversation.Pc && world.ConversationCallback && danger.State is DangerState.CombatFocus or DangerState.Flee)
            {
                world.EndConversation(npc);
                steps |= AiGateSteps.EndsConversation;
            }
            return new(AiGateStop.Conversation, steps);
        }
        if (world.InCombatMode(npc))
        {
            if (!world.InRound)
            {
                world.RunScript(AiScriptPoint.EndCombat, npc, danger.Source);
                steps |= AiGateSteps.EndCombatScript;
            }
            world.RunScript(AiScriptPoint.StartCombat, npc, danger.Source);
            steps |= AiGateSteps.StartCombatScript;
        }
        if (world.InRound && world.RoundActor != npc) return new(AiGateStop.NotOwnTurn, steps);
        if (world.AnimationPriority(npc) > BusyPriority) return new(AiGateStop.Busy, steps);

        // Steps 9–11 test the copy; each write clears its bits of the copy (the Original setting writes it whole) or of the
        // current flags (Steamwoven).
        void Clear(LifeConditions bits)
        {
            copy = copy with { Conditions = copy.Conditions & ~bits };
            var current = profile.GateRecoveryWritesStaleFlags ? copy : world.CritterFlags(npc);
            world.WriteCritterFlags(npc, current with { Conditions = current.Conditions & ~bits });
        }
        if (copy.Conditions.HasFlag(LifeConditions.Blinded) &&
            rolls.Roll(AiRollPurpose.GateBlindRecovery, 1, BlindRecoverySides) <= 1)
        {
            Clear(LifeConditions.Blinded);
            steps |= AiGateSteps.BlindnessCleared;
        }
        if ((copy.Conditions & AiCritterFlags.Crippled) != 0 &&
            rolls.Roll(AiRollPurpose.GateCrippleRecovery, 1, CrippleRecoverySides) <= 1)
        {
            Clear(AiCritterFlags.Crippled);
            steps |= AiGateSteps.CripplingCleared;
        }
        if (copy.Conditions.HasFlag(LifeConditions.Stunned))
        {
            if (!world.StunAnimationRunning(npc))
            {
                Clear(LifeConditions.Stunned);
                steps |= AiGateSteps.StunCleared;
            }
            return new(AiGateStop.Stunned, steps);
        }
        if (world.IsUnconscious(npc)) return new(AiGateStop.Unconscious, steps);
        if (world.IsGenerator(npc)) return new(AiGateStop.Generator, steps);
        var bits = world.DangerBits(npc);
        if (bits.HasFlag(DangerFlags.CheckWield))
        {
            world.ReWield(npc, true);
            world.WriteDangerBits(npc, DangerFlags.None, DangerFlags.CheckWield | DangerFlags.CheckWeapon);
            steps |= AiGateSteps.ReWieldAll;
        }
        else if (bits.HasFlag(DangerFlags.CheckWeapon))
        {
            world.ReWield(npc, false);
            world.WriteDangerBits(npc, DangerFlags.None, DangerFlags.CheckWeapon);
            steps |= AiGateSteps.ReWieldWeapon;
        }
        if (world.DangerBits(npc).HasFlag(DangerFlags.DemaintainSpells) &&
            (world.Leader(npc) is not { } leader || world.IsDead(leader) || !world.InCombatMode(leader)))
        {
            world.WriteDangerBits(npc, DangerFlags.None, DangerFlags.DemaintainSpells);
            world.EndMaintainedSpells(npc);
            steps |= AiGateSteps.Demaintained;
        }
        return new(AiGateStop.Passed, steps);
    }

    // ---- turn-based ----

    /// <summary>
    /// The subturn hook (0x4B7010), run at the start of a non-PC subturn and after each animation a critter finishes in a round:
    /// for the current participant, when it is not the PC and no game is loading, the heartbeat (the NPC as triggerer) and on
    /// "continue" one <see cref="Pass"/>; then, when no animation of it runs, its subturn ends.
    /// </summary>
    [OriginalFunction(0x4B7010, OriginalFunctionScope.Full)]
    public static AiSubturnHookResult SubturnHook(IAiSchedulerWorld world, string critter, IAiRollSource rolls, CombatAnomalyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(critter);
        ArgumentNullException.ThrowIfNull(rolls);
        ArgumentNullException.ThrowIfNull(profile);
        if (world.RoundActor != critter || critter == Pc || world.Loading) return new(false, false, null, false);
        var heartbeat = world.RunScript(AiScriptPoint.Heartbeat, critter, critter);
        var pass = heartbeat ? Pass(world, critter, rolls, profile) : null;
        var ends = !world.AnimationRunning(critter);
        if (ends) world.EndSubturn(critter);
        return new(true, heartbeat, pass, ends);
    }
}
