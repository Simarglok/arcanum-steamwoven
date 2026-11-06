using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;

namespace Arcanum.Simulation.Ai;

/// <summary>
/// ARC-DEV-144 (MIG13 S9c, docs/architecture/goal-table.md, GT74, GT94–GT97): what the burning goals (77 and the eye candy 75/76) need of the world for the fire damage 0x42A720: the turn-based round they gate on and the burn of the objects
/// on the burning object's tile. The tile, the victims and the damage path (resistance, life, the stun or death a result starts) are the host's.
/// </summary>
public interface IAiFireWorld
{
    /// <summary>The id of the turn-based round in progress (0x4B80D0 while 0x4B6D70 is set; the first round is 1), or null outside a round.</summary>
    int? FireRound();
    /// <summary>
    /// The burn of the objects on <paramref name="burner"/>'s own tile (types 5..16, the burner itself included): <paramref name="normal"/> 1 normal damage, else 3 fire, both doubled when <paramref name="doubled"/>.
    /// Answers how many victims reached the damage path (a dead critter counts, an item the host cannot damage does not); the round gate stores the id only for a burn that reached one.
    /// </summary>
    int Burn(string burner, bool normal, bool doubled);
}

public static partial class AiExecutorRules
{
    /// <summary>
    /// The fire damage node (0x42A720) of a burning goal: the round gate of the run's slot 19 (<see cref="FireDamageRules.Gate"/>), the burn, and the frame with the round id stored when a burn in a round reached a victim. The damage is
    /// normal for frame flag 0x4000 (the eye candy's burn), else fire (goal 77). Always answers true in the original; the frame is the only result.
    /// </summary>
    public static GoalFrame FireDamage(string burner, GoalFrame goal, IAiFireWorld world)
    {
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(world);
        var round = world.FireRound();
        var gate = FireDamageRules.Gate(round, goal.FireRound);
        if (!gate.Burns) return goal;
        var reached = world.Burn(burner, (goal.Flags & GoalFrameFlags.NormalDamage) != 0, gate.Doubled);
        return round is { } id && reached > 0 ? goal with { FireRound = id } : goal;
    }
}
