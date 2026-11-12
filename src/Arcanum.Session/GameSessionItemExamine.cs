using Arcanum.Scripting.Scr;

namespace Arcanum.Session;

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-SCRIPT-1119 (ledger HK24, OH1): the examine script (attachment point 0) of an item the session made (<c>made:n</c>: made from a
    /// schematic, created by a script, added to an NPC or lying on the ground), which the original runs whenever it describes an object (0x57CD60,
    /// the call at 0x57CD93) through the script dispatcher with the hero as the triggerer, the item as the attachee, no extra object and line 0.
    /// A made item owns no object row in any map, so the viewer's row-based run (<c>AreaUseScriptHost.RunExamine</c>) has none to read: the item's slot
    /// is the session's kept one, else its prototype's element (<see cref="QueryItemScriptSlot"/>, ARC-DEV-097), and a run that changes the slot
    /// keeps it on the item as every item run does (the first write copies the prototype's array onto the item, SA3). The run is an item run under
    /// <see cref="SessionItemScripts.Admission"/>: global flags, script stores, the item's slots, printed lines and the item effects apply as
    /// one revision when the run changed anything; a run the session cannot apply (an effect or command outside the admitted set, an unreadable
    /// script, a question the host cannot answer) is refused with <see cref="GameSessionFailure.ItemScriptUnsupported"/> (or the failure of its
    /// effects) and applies nothing, as a transfer's run is.
    /// The description follows only when the answer is not "skip default" (<see cref="InventoryMutationResult.DeclinedBy"/> is
    /// <see cref="SessionScriptPoint.Examine"/> then, <see cref="InventoryMutationResult.Item"/> null; the disposition is Changed only when that
    /// run wrote something). An item with no script at the point answers <see cref="GameSessionDisposition.NoChange"/> without a run (the
    /// original's dispatcher finds no attachment and answers run default). A script that runs past its last line ends as skip default.
    /// Refused before any run: an entity that is no valid id (<see cref="GameSessionFailure.InvalidItem"/>), a stale version, an entity
    /// that is no made item the session holds (<see cref="GameSessionFailure.ItemNotAvailable"/>: a map item's examine run is the viewer's), the
    /// revision budget. Not modelled (the run is refused): a conversation, a story-state action, a trap object to destroy or the ammunition
    /// units a script uses, which <see cref="SessionItemScripts.Admission"/> keeps out of an item event.
    /// </summary>
    public InventoryMutationResult RunItemExamine(GameSessionVersion expected, string entity, ISessionItemScriptHost scripts)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(scripts);
        if (!SessionValidation.Entity(entity)) return RejectItem(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectItem(GameSessionFailure.StaleVersion);
        if (MadeKind(entity) is null) return RejectItem(GameSessionFailure.ItemNotAvailable);
        if (QueryItemScriptSlot(entity, SessionScriptPoint.Examine) is not { Script: > 0 })
            return new(GameSessionDisposition.NoChange, GameSessionFailure.None, entity, null, Version);
        if (_revision == long.MaxValue) return RejectItem(GameSessionFailure.RevisionExhausted);
        var (revision, map, flagVersion) = (_revision, _actor.Map, _flags.Version);
        var complexity = _inventory.Weights.TryGetValue(entity, out var weight) ? weight.Complexity : 0;
        var runs = new ItemScriptRuns(this, scripts, map.RuntimeMap, new Dictionary<string, int>(StringComparer.Ordinal) { [entity] = complexity });
        var run = runs.Run(SessionScriptPoint.Examine, entity, SessionEventKinds.PlayerEntity, null);
        ThrowIfDisposed();
        if (_revision != revision || _actor.Map != map || _flags.Version != flagVersion) return RejectItem(GameSessionFailure.StaleVersion) with { Scripts = runs.Runs };
        if (runs.Failure != GameSessionFailure.None) return RejectItem(runs.Failure) with { Scripts = runs.Runs };
        if (run?.Outcome == ScrOutcome.SkipDefault) return Declined(SessionScriptPoint.Examine, runs);
        runs.Prepare();
        if (runs.Failure != GameSessionFailure.None) return RejectItem(runs.Failure) with { Scripts = runs.Runs };
        if (!runs.Changes)
            return new(GameSessionDisposition.NoChange, GameSessionFailure.None, entity, null, Version) { Scripts = runs.Runs, Messages = runs.Messages };
        runs.Commit();
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, entity, null, Version) { Scripts = runs.Runs, Messages = runs.Messages };
    }
}
