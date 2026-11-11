using Arcanum.Simulation;
using Arcanum.Simulation.Magic;

namespace Arcanum.Session;

/// <summary>
/// ARC-MAGIC-058 (MT9b, ledger MX1-MX6): the spell runs of a player character's change to another map (<see cref="SpellRunTravelRules"/>, 0x44F7A0). The run table is the
/// session's one, so a run whose objects all travel needs nothing (the original writes it into the destination map's <c>MT.dat</c> and reads it back at the map entry), and
/// every other run ends at once through the forced end (0x457270), its END processed on the shown map where its objects still are.
/// </summary>
public sealed partial class SessionSpellCaster
{
    /// <summary>
    /// The player's change of map: ends, in slot order, every run in use that names an object that does not travel (the hero and <paramref name="travelling"/>, its followers
    /// recursively, and the items they hold). A run an earlier end already ended (a cancel) is not ended again; the table is read live, as the original's slot loop does.
    /// Returns the ids of the runs it ended.
    /// </summary>
    public IReadOnlyList<int> EndRunsLeavingMap(IReadOnlyCollection<string> travelling)
    {
        ArgumentNullException.ThrowIfNull(travelling);
        bool Travels(string id) => id == Hero || travelling.Contains(id);
        var ended = new List<int>();
        for (var slot = 0; slot < SpellRunTable.Slots; slot++)
        {
            if (_owner.QuerySpellRuns()[slot] is not { } run || SpellRunTravelRules.Goes(run, Travels, r => SummonedOf(r).Select(summoned => summoned.Id))) continue;
            Act(new(SessionSpellRunActionKind.ForceEnd, slot));
            ended.Add(slot);
        }
        return ended;
    }
}
