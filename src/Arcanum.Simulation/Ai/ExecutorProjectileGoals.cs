using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;

namespace Arcanum.Simulation.Ai;

/// <summary>What one tick of a projectile's flight did (<see cref="IAiProjectileWorld.ProjectileTick"/>, the tick 0x42F6A0): the flight goes on, it ended (a strike, the range, the destination, a point off the map: the tick answers false), or a boomerang turned
/// back (BR4: the goal takes the thrower's tile as its destination and builds its line anew).</summary>
public enum ProjectileTickResult { Continue, Ended, Turned }

/// <summary>A tick's answer: the result and, for a turn, the tile the goal now flies to.</summary>
public readonly record struct ProjectileTickAnswer(ProjectileTickResult Result, CellCoord? Turn = null)
{
    public static ProjectileTickAnswer Continue { get; } = new(ProjectileTickResult.Continue);
    public static ProjectileTickAnswer Ended { get; } = new(ProjectileTickResult.Ended);
}

/// <summary>
/// ARC-DEV-140 (MIG13 S8b, docs/architecture/goal-table.md §3.5, the S8 read GT53–GT55, GT62, GT63): what the projectile goal needs of the world. A projectile is a created object (session entity string) that owns its run:
/// goal 36 <c>ag_projectile</c> alone on the stack (priority 5, so no sweep but the map's close ends it). The shooter, the carried outcome, the thrown item and the struck object are the host's (the original keeps the shooter
/// in slot 4 and the outcome in the projectile's fields); the line, its steps and the crossing of tiles (0x4B2870) are the host's too. Everything is by the projectile's entity.
/// </summary>
public interface IAiProjectileWorld
{
    /// <summary>Node 3 of goal 36 (0x424D90): the projectile is on the goal's destination tile already (the run pops at once: no strike, no script, no item event).</summary>
    bool ProjectileAtDestination(string projectile, CellCoord destination);
    /// <summary>Node 4 (0x424BC0): the facing art; fails only for a missing projectile or shooter. Its result is dropped by the original (a dead computation).</summary>
    bool ProjectileFace(string projectile);
    /// <summary>Node 6 (0x4269D0): builds the line from the projectile's tile to <paramref name="destination"/> (10-pixel steps, at most 100); false when the line is refused.</summary>
    bool ProjectileBuildLine(string projectile, CellCoord destination);
    /// <summary>Node 7 (0x42F5C0): the path node: the step index to 0, the range start the projectile's own tile, the flight begins. False ends the run.</summary>
    bool ProjectileSetup(string projectile);
    /// <summary>Node 2 (0x42F6A0): one tick of the flight's steps, each tile left handled before the projectile moves on (0x4B2870), the projectile moved in the session.</summary>
    ProjectileTickAnswer ProjectileTick(string projectile);
    /// <summary>The end node (0x42CAC0 → 0x4B2650 → 0x4B2690 without a target or the trigger): unless the projectile is destroyed already, a thrown item is placed on its tile and shown again, a boomerang gets its second end, the projectile is destroyed.</summary>
    void ProjectileEnded(string projectile);
}

public static partial class AiExecutorRules
{
    /// <summary>The tick of a projectile's flight, 35 ms apart (the global 0x5DE6CC every tick sets, GT53).</summary>
    public const int ProjectileTickMs = ProjectileRules.TickMs;

    /// <summary>The projectile goal the projectile stepper interprets (36).</summary>
    public static bool IsProjectileGoal(GoalType goal) => goal == GoalType.Projectile;

    /// <summary>The goal a projectile's own run starts with (36).</summary>
    public static bool IsProjectileRunGoal(GoalType goal) => goal == GoalType.Projectile;

    /// <summary>The frame of a projectile's run: goal 36 at its struck object (the intended target, or the line of fire's blocker; null for a flight at a tile) and its destination tile, with the path flag set (GT58).</summary>
    public static GoalFrame ProjectileFrame(string? target, CellCoord destination) =>
        new(GoalType.Projectile, target, destination) { PathPending = true };

    /// <summary>
    /// One step of a projectile's run (the stepper 0x423560 over the nodes of goal 36): 1 the flight flag (set: 2, clear: 3); 2 the tick (an end pops, else it goes on 35 ms later); 3 on the destination already (pop) else 4; 4 the
    /// facing (fails: pop) else 5; 5 the path flag (set: 6, clear: 7); 6 the line (refused: pop) else 7; 7 the path node (the flight flag set, the first tick follows at once). The pop of the one goal ends the run, as every end
    /// runs the end node. A boomerang's turn clears the flight flag and sets the path flag with the thrower's tile as the destination (BR4), so nodes 3 to 7 run again.
    /// </summary>
    public static AiGoalStepResult StepProjectile(GoalRun run, IAiProjectileWorld world)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(world);
        return StepObjectRun(run, (owner, top) => ProjectileNode(owner, top, world), frame => ProjectileEndNode(run.Owner, frame, world));
    }

    /// <summary>The end nodes of a projectile run ended from outside the stepper (a map's close, a teleport), top down; a killed run skips them (GS21).</summary>
    public static void EndProjectileRun(GoalRun run, IAiProjectileWorld world)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(world);
        if ((run.Flags & GoalRunFlags.Killed) != 0) return;
        for (var i = run.Frames.Length - 1; i >= 0; i--) ProjectileEndNode(run.Owner, run.Frames[i], world);
    }

    private static void ProjectileEndNode(string projectile, GoalFrame frame, IAiProjectileWorld world)
    {
        if (frame.Goal == GoalType.Projectile) world.ProjectileEnded(projectile);
    }

    private static Transition ProjectileNode(string projectile, GoalFrame goal, IAiProjectileWorld world)
    {
        if (goal.Goal != GoalType.Projectile) return new(Move.Kill);
        var destination = goal.Tile ?? default;
        switch (goal.Node)
        {
            // 1: the flight flag (the run's 0x10) set -> the tick (2), clear -> the path (3).
            case 1: return PlayingNow(goal) ? Next(2) : Next(3);
            // 2: the tick; an end (a strike, the range, the destination, off the map) pops and the end node runs; a boomerang's turn takes the thrower's tile and starts over; else 35 ms later.
            case 2:
                var tick = world.ProjectileTick(projectile);
                return tick.Result switch
                {
                    ProjectileTickResult.Continue => new(Move.Restart, Delay: ProjectileTickMs),
                    ProjectileTickResult.Turned => new(Move.Restart, Delay: ProjectileTickMs,
                        Top: WithPlaying(goal with { Tile = tick.Turn ?? destination, PathPending = true }, false)),
                    _ => new(Move.Pop),
                };
            // 3: already on the destination -> pop (no strike, no script, no item event), else 4.
            case 3: return world.ProjectileAtDestination(projectile, destination) ? new(Move.Pop) : Next(4);
            // 4: the facing, which the original computes and drops; fails only for a missing projectile.
            case 4: return world.ProjectileFace(projectile) ? Next(5) : new(Move.Pop);
            // 5: the path flag set -> build the line (6), clear -> the path node (7).
            case 5: return goal.PathPending ? Next(6) : Next(7);
            // 6: the line; a refused one pops. The builder clears the flag.
            case 6: return world.ProjectileBuildLine(projectile, destination) ? new(Move.Next, 7, Top: goal with { PathPending = false }) : new(Move.Pop);
            // 7: the path node; the flight flag is set and the first tick follows at once.
            default: return world.ProjectileSetup(projectile) ? new(Move.Restart, Top: WithPlaying(goal, true)) : new(Move.Pop);
        }
    }
}
