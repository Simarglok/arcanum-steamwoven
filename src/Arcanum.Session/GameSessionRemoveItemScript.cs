using Arcanum.Scripting.Scr;

namespace Arcanum.Session;

/// <summary>
/// ARC-SCRIPT-2854 (ledger IE3, TQ20; exe read 0x467860, 0x43CCA0, 0x447220): the remove-item script (point 28) of an item the session deletes out of a holder's list. The original's removal (0x466E00 → 0x467860) unlinks the item from the holder and only
/// then runs point 28 with the holder as the triggerer and the item as the attachee, ignoring the result; every destroy of an item that has a parent goes through it (the destroy script, point 2, first). The session runs it for the destroy paths that have
/// a script host: the script action destroy item named, the used-up item of the hero, and an item an NPC's script or craft deletes from its own list.
/// </summary>
public sealed partial class GameSessionOwner
{
    // What the removal's script came to: a result the move ends with (a refusal), or the prepared run (null when the item has no point 28). The runs are committed by the caller with its own change, one revision.
    private sealed record RemovalScripted(InventoryMutationResult? Done, ItemScriptRuns? Runs);

    // The item's script row belongs to another attached map: the host answers for the shown map's rows alone, so such a run is refused, never skipped.
    private bool ItemRowOfOtherMap(string item)
    {
        foreach (var (map, seed) in _inventory.Seeds)
            if (map != _actor.Map && seed.Items.ContainsKey(item)) return true;
        return false;
    }

    /// <summary>
    /// ARC-SCRIPT-2854 (IE3): runs the remove-item script (28) of <paramref name="item"/> as the removal from <paramref name="holder"/> (null: the hero) does, with the holder as the triggerer and the item as the attachee, under
    /// <see cref="SessionItemScripts.Admission"/> and prepared, not committed: a run the session cannot apply, a host callback that changed what the move rests on or an item whose row belongs to another map refuses the destroy before any effect.
    /// Without a point 28 nothing runs.
    /// </summary>
    private RemovalScripted RunRemovalScript(string item, string? holder, IReadOnlyList<int> points, ISessionItemScriptHost scripts) => RunRemovalScripts([(item, points)], holder, scripts);

    /// <summary>
    /// ARC-SCRIPT-2880 (IE3): the same for several items one destroy takes out of the dying holder in order (the inventory-on-destroy walk, 0x4639E0 → 0x467860): one run set, so a later run reads what the earlier ones did. Items without a point 28 run nothing.
    /// </summary>
    private RemovalScripted RunRemovalScripts(IReadOnlyList<(string Item, IReadOnlyList<int> Points)> items, string? holder, ISessionItemScriptHost scripts)
    {
        var scripted = items.Where(item => item.Points.Contains(SessionScriptPoint.RemoveItem)).Select(item => item.Item).ToArray();
        if (scripted.Length == 0) return new(null, null);
        if (scripted.Any(ItemRowOfOtherMap)) return new(RejectItem(GameSessionFailure.ItemScriptUnsupported), null);
        // As LootItem's runs, made before the commit: the admitted commands read nothing the unlink changes, so running before it is the same as after (the original runs the script after the unlink).
        var (revision, map, flagVersion) = (_revision, _actor.Map, _flags.Version);
        var complexities = scripted.ToDictionary(item => item, item => _inventory.Weights.TryGetValue(item, out var weight) ? weight.Complexity : 0, StringComparer.Ordinal);
        var runs = new ItemScriptRuns(this, scripts, map.RuntimeMap, complexities) { NpcTriggerer = holder is not null && KnownNpc(holder) };
        foreach (var item in scripted)
        {
            runs.Run(SessionScriptPoint.RemoveItem, item, holder ?? SessionEventKinds.PlayerEntity, null);
            if (runs.Failure != GameSessionFailure.None) break;
        }
        ThrowIfDisposed();
        if (_revision != revision || _actor.Map != map || _flags.Version != flagVersion) return new(RejectItem(GameSessionFailure.StaleVersion) with { Scripts = runs.Runs }, null);
        if (runs.Failure == GameSessionFailure.None) runs.Prepare();
        if (runs.Failure != GameSessionFailure.None) return new(RejectItem(runs.Failure) with { Scripts = runs.Runs }, null);
        return new(null, runs);
    }
}
