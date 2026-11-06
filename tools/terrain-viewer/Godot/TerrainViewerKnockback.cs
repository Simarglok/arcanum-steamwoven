using Arcanum.Session;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-DEV-141 (MIG13 S8c): a knockback (goals 43 and 64, the S8 read GT59–GT62) is a goal run of the pushed NPC that the frame step hands to the knockback host through the router. A spell or technology with an
// AGoal component for goal 43 logs the request (SpellCastEventKind.Knockback); the host starts the run and the slide moves the NPC through the AI host's movers. The hero's, a companion's and the objects' slides are not ready.
public partial class TerrainViewerApp
{
    private AreaKnockbackHost? _knockbackHost;
    // The pushes that began and ended so far, for the smoke: the pushed entity, how it ended and the tiles it was pushed over.
    private readonly List<(string Target, KnockbackOutcome Outcome, IReadOnlyList<CellCoord> Path)> _knockbackEndsForSelfTest = [];
    private int _knockbackStartsForSelfTest, _knockbackEndCountForSelfTest;
    // The log entries already taken (by reference): a cast host's log can be flushed twice (a replaced host's entries after a mark), and a push must not start twice from one entry.
    private readonly HashSet<SpellCastEvent> _knockbackTaken = new(ReferenceEqualityComparer.Instance);

    /// <summary>The knockback host of the shown session: a new session (a reload, a carried session) gives up the old host's pushes first.</summary>
    private AreaKnockbackHost KnockbackHost()
    {
        if (_knockbackHost is { } host && ReferenceEquals(host.Session, _areaSession)) return host;
        _knockbackHost?.LoseAll("the session changed");
        return _knockbackHost = new(_areaSession!, new AreaAiKnockbackBodies(() => _aiHost), OnKnockbackFinished, end => _aiHost?.RunEndNodes(end));
    }

    /// <summary>A cast host log entry asking for a push (an AGoal component 43): the target is pushed away from the entry's source tile. Nothing without an AI host or for an object that cannot be pushed here.</summary>
    private void TakeKnockbackEvent(SpellCastEvent entry)
    {
        if (entry.Kind != SpellCastEventKind.Knockback || entry.Object is not { } target || _areaSession is null) return;
        if (_knockbackTaken.Count >= 256) _knockbackTaken.Clear();
        if (!_knockbackTaken.Add(entry)) return;
        var started = KnockbackHost().Start(target, new CellCoord(entry.Value, entry.Detail));
        if (started) _knockbackStartsForSelfTest++;
        GD.Print($"ARC-DEV-141 knockback-start run={entry.Run} target={target} source={entry.Value},{entry.Detail} started={started}");
    }

    private void OnKnockbackFinished(Knockback push, KnockbackFinished end)
    {
        _knockbackEndCountForSelfTest++;
        _knockbackEndsForSelfTest.Add((push.Target, end.Outcome, [.. push.Path]));
        if (_knockbackEndsForSelfTest.Count > 16) _knockbackEndsForSelfTest.RemoveAt(0);
        GD.Print($"ARC-DEV-141 knockback-end target={push.Target} outcome={end.Outcome} from={push.From.X},{push.From.Y} to={end.Tile.X},{end.Tile.Y}"
            + $" destination={push.Destination.X},{push.Destination.Y} ticks={push.Ticks} tiles={push.Path.Count} bump={end.BumpStepsLeft}");
    }

    // The end of a knockback's run the session ended by itself (an interrupt, a map's close).
    private void KnockbackRunEnded(GoalRunEnd end) => _knockbackHost?.EndNodes(end);
}
