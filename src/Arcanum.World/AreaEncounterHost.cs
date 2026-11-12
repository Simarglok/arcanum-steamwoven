using Arcanum.Session;
using Arcanum.Simulation.Travel;

namespace Arcanum.World;

/// <summary>
/// ARC-WORLD-1159 (E2-c): the viewer's side of the Random Encounters event. It holds the encounter tables of the module and asks the viewer, at each firing, where the hook acts (<see cref="SessionEncounterSite"/>: the
/// outdoor map, the hero in no named area, the roll's place); the session rolls and creates the creatures (<see cref="GameSessionOwner"/>, ledger PO17).
/// </summary>
public sealed class AreaEncounterHost(EncounterTables? tables, Func<SessionEncounterSite?> site)
{
    public EncounterTables? EncounterTables { get; } = tables;
    public SessionEncounterSite? EncounterSite() => site();
}
