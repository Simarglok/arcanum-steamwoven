using Arcanum.Simulation;
using Arcanum.Simulation.Ai;

namespace Arcanum.Session;

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// Set no-flee (0x4AF1D0) of an NPC, as the executor's move-away goal asks it (<see cref="AiExecutorRules.Step"/>: a flight
    /// with no escape line, or one bent after a partial straight line): <see cref="DangerFlags.NoFlee"/> on its danger record, for
    /// good, in one revision (none when it is set already). The setter then turns every FLEE request into COMBAT_FOCUS (rule 2).
    /// </summary>
    [OriginalFunction(0x4AF1D0, OriginalFunctionScope.Partial)]
    public SessionDangerResult SetNoFlee(GameSessionVersion expected, string entity) =>
        ChangeDanger(expected, entity, null, false, plan => plan.Update(entity, record => record with { Flags = record.Flags | DangerFlags.NoFlee }));
}
