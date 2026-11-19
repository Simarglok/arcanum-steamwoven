namespace Arcanum.Session;

/// <summary>What the remove-item scripts (point 28) an event pass's removals ran came to (ARC-SCRIPT-2879): the runs and their printed lines in the order made, and how many removals had a point 28 the session did not run.</summary>
public sealed record SessionPassRemovals(IReadOnlyList<SessionItemScriptRun> Scripts, IReadOnlyList<SessionScriptMessage> Messages, int Skipped)
{
    public static readonly SessionPassRemovals None = new([], [], 0);
}

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-SCRIPT-2879 (ledger IE3, TQ20; exe read 0x467860, 0x463B30, 0x463860): the remove-item scripts of an event pass that deletes or drops a holder's items out of its list on its own: the restock's clear (0x463B30, the container or NPC as
    /// the triggerer) and a dead body's inventory walk (0x463860, the corpse). Each removal runs point 28 with the holder as the triggerer and the item as the attachee, result ignored, at the item's own place in the walk (after its unlink, before its
    /// destroy or drop), so the caller calls <see cref="Run"/> item by item, right before it applies the item. A run is made, prepared and committed on its own (one revision comes from the pass). An event pass cannot refuse a removal, so a run the session
    /// cannot apply (a host callback, a row of another map, an effect the session does not model) or no script host in the world is counted in <see cref="SessionPassRemovals.Skipped"/> and the removal goes on without it, as before the scripts were run.
    /// </summary>
    private sealed class PassRemovals(GameSessionOwner owner, ISessionItemScriptHost? host, SessionMapId map)
    {
        private readonly List<SessionItemScriptRun> _scripts = [];
        private readonly List<SessionScriptMessage> _messages = [];
        private int _skipped;

        internal void Run(string holder, string item, IReadOnlyList<int> points)
        {
            if (!points.Contains(SessionScriptPoint.RemoveItem)) return;
            if (host is null || map != owner._actor.Map) { _skipped++; return; }
            var removal = owner.RunRemovalScript(item, holder, points, host);
            if (removal.Runs is not { } runs) { _skipped++; return; }
            runs.Commit();
            _scripts.AddRange(runs.Runs);
            _messages.AddRange(runs.Messages);
        }

        internal SessionPassRemovals Result => _scripts.Count == 0 && _messages.Count == 0 && _skipped == 0 ? SessionPassRemovals.None : new(_scripts.AsReadOnly(), _messages.AsReadOnly(), _skipped);
    }

    // The attachment points of an item as the session knows them: the points of its row in the map's seed (when the map's seed lists it), and the slots the session kept or a made item inherits.
    private IReadOnlyList<int> PassItemPoints(SessionMapId map, string item) =>
        KnownScriptPoints(item, _inventory.Seeds.TryGetValue(map, out var seed) && seed.Items.TryGetValue(item, out var row) ? row.Facts : null);
}
