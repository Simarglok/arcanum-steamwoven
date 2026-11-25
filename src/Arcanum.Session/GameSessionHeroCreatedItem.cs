using Arcanum.Scripting.Scr;
using Arcanum.Simulation.Items;

namespace Arcanum.Session;

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-SKILLS-1156 (ledger TT5): a new made item of <paramref name="kind"/> for the hero, as the disarm of a trap creates its device at the
    /// disarmer's location and gives it to the disarmer. The item is <c>made:n</c> with its prototype's weight facts
    /// (<see cref="AttachItemWeights"/>); it enters the hero's list when the hero can take it (the host's insert check, the weight, Technophobia and a
    /// free grid place, <see cref="HeroInsertVerdict"/>), else it stays where it was created, on the hero's tile (the answer's
    /// <see cref="InventoryMutationResult.Verdict"/> is then the refusal and the disposition still Changed, as a pick-up refused for room). Ammunition,
    /// gold, keys and key rings are not created here. One revision. Refused before any effect: no hero (<see cref="GameSessionFailure.InvalidItem"/>),
    /// the version, an item type the lists do not admit (<see cref="GameSessionFailure.ItemTypeUnsupported"/>), no free made
    /// number (<see cref="GameSessionFailure.BudgetExceeded"/>; a pack at its item limit counts as no room and leaves the item on the tile), a tile the ground cannot hold when the item must lie there
    /// (<see cref="GameSessionFailure.DropTileUnfit"/>), the revision.
    /// </summary>
    /// <remarks>
    /// ARC-SCRIPT-2910 (ledger TQ19; exe 0x4617F0 → 0x461810 → 0x466640): with <paramref name="scripts"/> the new item's prototype scripts run as for any created object moved into a list (<see cref="AddItemToNpc"/>): its get (4) script first, with the hero as the triggerer and
    /// the item as the attachee, before the insert check, and its insert-item (21) script at the end of an insert. A get script that skips the default refuses the insert as a verdict does: the item stays on the hero's tile
    /// (<see cref="InventoryMutationResult.DeclinedBy"/> is the get point, <see cref="InventoryMutationResult.Verdict"/> null, the disposition Changed); the run's effects are applied (the created item exists either way), no insert-item script runs for the item left
    /// on the ground. The runs and lines are in the result. A prototype with such a script and no host is refused (<see cref="GameSessionFailure.ItemScriptUnsupported"/>); a run the session cannot apply refuses the whole call, nothing made.
    /// </remarks>
    public InventoryMutationResult CreateItemForHero(GameSessionVersion expected, SessionItemKind kind, SessionInsertCheck insertCheck, ISessionItemScriptHost? scripts = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(insertCheck);
        if (_hero is null || !Enum.IsDefined(kind.Type)) return RejectItem(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectItem(GameSessionFailure.StaleVersion);
        if (kind.Type == SessionItemType.Ammo || !InventoryDomain.Admitted(kind.Type) || kind.Type == SessionItemType.Gold) return RejectItem(GameSessionFailure.ItemTypeUnsupported);
        if (NextObjectEntity(SessionObjectOrigin.Made) is not { } entity) return RejectItem(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return RejectItem(GameSessionFailure.RevisionExhausted);
        var (revision, seeds, map, flagVersion) = (_revision, _inventory.SeedGeneration, _actor.Map, _flags.Version);
        var weight = CreatedWeight(kind.Prototype);
        // The new item answers as the made item it will be while its scripts run (it is created after them).
        _pendingMade = (entity, kind);
        try
        {
            var hasGet = InheritedSlot(entity, SessionScriptPoint.Get) is not null;
            var hasInsert = InheritedSlot(entity, SessionScriptPoint.InsertItem) is not null;
            if ((hasGet || hasInsert) && scripts is null) return RejectItem(GameSessionFailure.ItemScriptUnsupported);
            var runs = hasGet || hasInsert ? new ItemScriptRuns(this, scripts!, map.RuntimeMap, new Dictionary<string, int>(StringComparer.Ordinal) { [entity] = weight.Complexity }) : null;
            bool Stale() => _revision != revision || _inventory.SeedGeneration != seeds || _actor.Map != map || _flags.Version != flagVersion;
            ScrRunResult? gate = null;
            if (runs is not null && hasGet)
            {
                gate = runs.Run(SessionScriptPoint.Get, entity, SessionEventKinds.PlayerEntity, null);
                ThrowIfDisposed();
                if (Stale()) return RejectItem(GameSessionFailure.StaleVersion);
                if (runs.Failure != GameSessionFailure.None) return RejectItem(runs.Failure) with { Scripts = runs.Runs };
            }
            var declined = gate?.Outcome == ScrOutcome.SkipDefault;
            SessionInsertVerdict? verdict = null;
            if (!declined)
            {
                verdict = HeroInsertVerdict(insertCheck(new SessionInventoryItem(entity, InventoryDomain.MadeFacts(kind) with { Weight = weight.Weight, MagicWeight = weight.MagicWeight })),
                    weight.Complexity, new((int)kind.Type, weight.Weight, weight.MagicWeight, weight.Complexity), room: NewItemRoom(entity, kind));
                ThrowIfDisposed();
                // A seed attached from the callback moves no revision but may change what the hero is or how long a list is.
                if (Stale()) return RejectItem(GameSessionFailure.StaleVersion, verdict);
                if (verdict == SessionInsertVerdict.Accepted && _inventory.Held >= _limits.MaxPcItems) verdict = SessionInsertVerdict.NoRoom;
            }
            var taken = !declined && verdict == SessionInsertVerdict.Accepted;
            ScrRunResult? insertRun = null;
            if (runs is not null)
            {
                // The insert-item script runs at the end of an insert and cannot cancel it: it is run here, before the item is made, and applied with it; an item left on the ground has none.
                if (taken && hasInsert) insertRun = runs.Run(SessionScriptPoint.InsertItem, entity, SessionEventKinds.PlayerEntity, null);
                ThrowIfDisposed();
                if (Stale()) return RejectItem(GameSessionFailure.StaleVersion, verdict);
                runs.Prepare();
                if (runs.Failure != GameSessionFailure.None) return RejectItem(runs.Failure, verdict) with { Scripts = runs.Runs };
            }
            if (taken) CreateMadeItem(new(entity, kind, SessionCreatePlace.HeroList, weight, weight.Complexity));
            else
            {
                var (mapNow, cell) = (_actor.Map, _actor.Cell);
                if (!DropCellFits(cell)) return RejectItem(GameSessionFailure.DropTileUnfit, verdict) with { Scripts = runs?.Runs ?? [] };
                CreateMadeItem(new(entity, kind, SessionCreatePlace.Ground, weight, weight.Complexity, Map: mapNow, Cell: cell, Source: SessionPlacedSource.Made));
            }
            runs?.Commit();
            Revise();
            return new(GameSessionDisposition.Changed, GameSessionFailure.None, entity, verdict, Version)
                { DeclinedBy = declined ? SessionScriptPoint.Get : null, InsertScript = insertRun, Scripts = runs?.Runs ?? [], Messages = runs?.Messages ?? [] };
        }
        finally { _pendingMade = null; }
    }
}
