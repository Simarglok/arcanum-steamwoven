using Arcanum.Scripting.Scr;

namespace Arcanum.Session;

/// <summary>
/// ARC-SCRIPT-2910 (ledger TQ19, NT1, GM1; exe 0x464830 → 0x4617F0 → 0x461810 → 0x466640): the get (4) and insert-item (21) scripts of the gold pile a hero without one is given. Contract: docs/world/inventory-runtime.md.
/// </summary>
public sealed partial class GameSessionOwner
{
    // The runs of the hero's new gold pile between the get script and the commit (the pile's scripts are its prototype's array; the pile has no entity of its own yet: it answers as the fixed SessionHeroGrid.GoldEntity while they run).
    private sealed record FreshHeroGold(ItemScriptRuns Runs, bool Declined, bool HasInsert, Func<bool> Stale);

    /// <summary>
    /// A hero that holds no gold pile is given a new gold object (0x464830 makes it, 0x4617F0 → 0x461810 moves it in), so its prototype's get (4) script runs first with the hero as the triggerer, before the insert check; an answer that skips the default
    /// refuses the insert (the pile stays on the hero's tile, as for a grid with no place). A hero that holds a pile grows it and runs nothing (the merged case is #2909). Null when the call makes no new pile or the prototype has neither point;
    /// a refusal when the prototype has one and there is no <paramref name="scripts"/> host (<see cref="GameSessionFailure.ItemScriptUnsupported"/>) or a run the session cannot apply.
    /// </summary>
    private (InventoryMutationResult? Refusal, FreshHeroGold? Pile) BeginFreshHeroGold(ISessionItemScriptHost? scripts)
    {
        if (_inventory.Gold != 0) return (null, null);
        _pendingMade = (SessionHeroGrid.GoldEntity, SessionHeroGrid.GoldKind);
        try
        {
            var hasGet = InheritedSlot(SessionHeroGrid.GoldEntity, SessionScriptPoint.Get) is not null;
            var hasInsert = InheritedSlot(SessionHeroGrid.GoldEntity, SessionScriptPoint.InsertItem) is not null;
            if (!hasGet && !hasInsert) return (null, null);
            if (scripts is null) return (RejectItem(GameSessionFailure.ItemScriptUnsupported), null);
            var (revision, seeds, map, flagVersion) = (_revision, _inventory.SeedGeneration, _actor.Map, _flags.Version);
            bool Stale() => _revision != revision || _inventory.SeedGeneration != seeds || _actor.Map != map || _flags.Version != flagVersion;
            var runs = new ItemScriptRuns(this, scripts, map.RuntimeMap, new Dictionary<string, int>(StringComparer.Ordinal) { [SessionHeroGrid.GoldEntity] = 0 });
            var declined = false;
            if (hasGet)
            {
                var gate = runs.Run(SessionScriptPoint.Get, SessionHeroGrid.GoldEntity, SessionEventKinds.PlayerEntity, null);
                ThrowIfDisposed();
                if (Stale()) return (RejectItem(GameSessionFailure.StaleVersion), null);
                if (runs.Failure != GameSessionFailure.None) return (RejectItem(runs.Failure) with { Scripts = runs.Runs }, null);
                declined = gate?.Outcome == ScrOutcome.SkipDefault;
            }
            return (null, new(runs, declined, hasInsert, Stale));
        }
        finally { _pendingMade = null; }
    }

    // The end of the insert, once the caller knows the pile takes its place in the grid (taken) or stays on the ground: the insert-item script (21) of a pile that enters the list, then the runs are prepared. A refusal when a run cannot be applied; nothing is applied yet.
    private InventoryMutationResult? FinishFreshHeroGold(FreshHeroGold pile, bool taken, out ScrRunResult? insertRun)
    {
        insertRun = null;
        _pendingMade = (SessionHeroGrid.GoldEntity, SessionHeroGrid.GoldKind);
        try
        {
            if (taken && pile.HasInsert) insertRun = pile.Runs.Run(SessionScriptPoint.InsertItem, SessionHeroGrid.GoldEntity, SessionEventKinds.PlayerEntity, null);
            ThrowIfDisposed();
            if (pile.Stale()) return RejectItem(GameSessionFailure.StaleVersion);
            pile.Runs.Prepare();
            return pile.Runs.Failure == GameSessionFailure.None ? null : RejectItem(pile.Runs.Failure) with { Scripts = pile.Runs.Runs };
        }
        finally { _pendingMade = null; }
    }

    // The runs' commit. The hero's gold pile is the session's counter under one fixed entity (the owner's ARC-UI-090 model: a pile keeps no script slots of its own), so the slots the runs wrote for it are not kept (Steamwoven difference, documented in inventory-runtime.md).
    private void CommitFreshHeroGold(FreshHeroGold pile)
    {
        _pendingMade = (SessionHeroGrid.GoldEntity, SessionHeroGrid.GoldKind);
        try
        {
            pile.Runs.Commit();
            foreach (var key in _inventory.ScriptSlots.Keys.Where(key => key.Entity == SessionHeroGrid.GoldEntity).ToArray()) _inventory.ScriptSlots.Remove(key);
        }
        finally { _pendingMade = null; }
    }

    // The move's result with the new pile's runs, lines and insert-item answer after the source pile's own.
    private static InventoryMutationResult WithFreshHeroGold(InventoryMutationResult result, FreshHeroGold? pile, ScrRunResult? insertRun) => pile is null ? result
        : result with { Scripts = [.. result.Scripts, .. pile.Runs.Runs], Messages = [.. result.Messages, .. pile.Runs.Messages], InsertScript = result.InsertScript ?? insertRun, DeclinedBy = pile.Declined ? SessionScriptPoint.Get : result.DeclinedBy };
}
