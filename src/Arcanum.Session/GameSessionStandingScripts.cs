using Arcanum.Scripting.Scr;

namespace Arcanum.Session;

/// <summary>
/// ARC-SCRIPT-2900 (ledger IE3, TQ20; exe read 0x464830, 0x4658E0 → 0x43CCA0 → 0x466E00 → 0x467860): the remove-item script (28) of a gold pile or ammunition stack a spend or a move empties, through the host's standing item script host
/// (<see cref="StandingItemScripts"/>) when the call was given none. Contract: docs/world/inventory-runtime.md.
/// </summary>
public sealed partial class GameSessionOwner
{
    // The item script host of a call: the one it was given, else the standing one (a host that throws counts as none). Standing says the runs' lines go to ItemRunOutput, not to the call's result.
    private ISessionItemScriptHost? ResolveItemScripts(ISessionItemScriptHost? given, out bool standing)
    {
        standing = false;
        if (given is not null) return given;
        try { standing = StandingItemScripts?.Invoke() is not null; return standing ? StandingItemScripts!.Invoke() : null; }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or OverflowException) { standing = false; return null; }
    }

    // The lines and eye candy of the runs a standing host made go to the host once the change is applied; a sink that throws leaves the change whole.
    private void DeliverStanding(ItemScriptRuns? runs)
    {
        if (runs is null || ItemRunOutput is not { } sink || runs.Messages.Count == 0 && runs.EyeCandies.Count == 0) return;
        try { sink(runs.Messages, runs.EyeCandies); }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or OverflowException) { }
    }

    // The script points the session knows of an item a holder keeps (an ammunition stack, an assembly's component): its seed row's in any attached map, and the slots kept or inherited.
    private IReadOnlyList<int> HeldItemPoints(string entity)
    {
        SessionItemFacts? facts = null;
        foreach (var seed in _inventory.Seeds.Values)
            if (seed.Items.TryGetValue(entity, out var row)) { facts = row.Facts; break; }
        return KnownScriptPoints(entity, facts);
    }

    // The remove-item run (28) of a pile or stack an operation empties out of its holder (null: the hero), planned and not applied: Runs is null when the item has no point 28; a stack with the script and no host (given or standing), a row of another map or a run the session cannot apply is a Failure
    // (the operation is refused whole). The caller commits the runs with its change and, for a standing host, sends their lines on (DeliverStanding).
    private (ItemScriptRuns? Runs, bool Standing, GameSessionFailure Failure) PlanEmptiedRemoval(string entity, IReadOnlyList<int> points, string? holder, ISessionItemScriptHost? scripts)
    {
        if (!points.Contains(SessionScriptPoint.RemoveItem)) return (null, false, GameSessionFailure.None);
        var host = ResolveItemScripts(scripts, out var standing);
        if (host is null) return (null, false, GameSessionFailure.ItemScriptUnsupported);
        var removal = RunRemovalScript(entity, holder, [SessionScriptPoint.RemoveItem], host);
        return removal.Done is { } refused ? (null, false, refused.Failure) : (removal.Runs, standing, GameSessionFailure.None);
    }

    // The hero's gold pile as the removal sees it: the counter's one pile, with the script points its prototype's array gives.
    private IReadOnlyList<int> HeroGoldPoints() => KnownScriptPoints(SessionHeroGrid.GoldEntity, null);

    // The hero's gold pile is the session's counter under one fixed entity, not a made object (the owner's ARC-UI-090 model), so while its events run it answers as the gold object it is: the item's slots read through its prototype's array (ARC-SCRIPT-2910's way).
    private T WithHeroGoldObject<T>(Func<T> body)
    {
        var before = _pendingMade;
        _pendingMade = (SessionHeroGrid.GoldEntity, SessionHeroGrid.GoldKind);
        try { return body(); }
        finally { _pendingMade = before; }
    }

    // The runs' commit for the hero's gold pile: its slots are the prototype's, so the slots the runs wrote for it are dropped, as a new pile's are (CommitFreshHeroGold).
    private void CommitHeroGoldRuns(ItemScriptRuns? runs)
    {
        if (runs is null) return;
        WithHeroGoldObject(() => { runs.Commit(); return 0; });
        foreach (var key in _inventory.ScriptSlots.Keys.Where(key => key.Entity == SessionHeroGrid.GoldEntity).ToArray()) _inventory.ScriptSlots.Remove(key);
    }
}
