using Arcanum.Scripting.Scr;
using Arcanum.Simulation;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;

namespace Arcanum.Session;

/// <summary>
/// ARC-COMBAT-1955: the item scripts the hero's throw runs (the lift's removal 0x466E00 → 0x467860 and the throw 0x4B3C00 → 0x441980). Contract: docs/world/hero-attack-runtime.md (Throw script), docs/research/ledger/combat.md (TW3, TT1, TT8).
/// </summary>
public sealed partial class GameSessionOwner
{
    // What the scripts of a hero's throw came to: a result the throw ends with (a refusal, nothing to throw, the declined throw), or the runs whose changes were applied (Applied: they changed the session and took a revision of their own).
    private sealed record HeroThrowScripted(SessionHeroThrowResult? Done, ItemScriptRuns? Runs, bool Applied);

    /// <summary>
    /// ARC-COMBAT-1955 (0x4B3C00, ledger TT1, TT8, TW3; exe read 0x573740 → 0x5788C0 with flag 4, 0x466E00, 0x467860, 0x441980): the scripts around the hero's throw, run before anything of the throw is decided. In the original the lift removes the item from the
    /// hero's list (0x466E00 → 0x467860: the wield-off script (25) of an item the hero wears, then its remove-item script (28)), and the throw (0x4B3C00) runs the item's throw script (point 6, 0x441980) with the hero as the triggerer, the item as the
    /// attachee and no other object (the press throws at a location, TT2: the target object is none). Steamwoven takes the item out at the throw, so all three run here, in that order, under <see cref="SessionItemScripts.Admission"/> with the hero as the
    /// triggerer, each reading what the earlier runs did (<see cref="ItemScriptRuns"/>); a run the session cannot apply (a script outside the set, an effect or an object it does not model, a printed line whose gender text the host cannot choose) refuses the
    /// throw before any effect. A throw script that answers skip default (the original's 0) throws nothing: the removal's and the script's own changes stay, a worn item is worn no more and lies in the hero's list again (the original puts it on the hero's tile,
    /// 0x4415C0, and inserts it back, 0x4617F0) and the result is <see cref="SessionHeroThrowResult.ScriptDeclined"/> with the removal's spell steps (a worn item's unwear, then the removal's). Otherwise the runs' changes are applied in a revision of
    /// their own and the throw goes on from the new version. Refused before any effect: the hero's worn items, an inactive hero (NoChange), the shot budget, the version, the revision, a worn item the hero's list has no room for when the throw script could
    /// give it back, and the runs above. NOT READY: a refusal of the throw after its scripts ran that this check cannot foresee (a struck critter's refusals: the result is then changed with the failure named, the item still where it was).
    /// </summary>
    private HeroThrowScripted RunHeroThrowScripts(GameSessionVersion expected, SessionHeroThrow request, SessionWornItem? worn, IReadOnlyList<int> points, bool split, SessionItemKind kind, bool throwScript)
    {
        SessionHeroThrowResult Refused(GameSessionFailure failure, ItemScriptRuns? made = null) => new(GameSessionDisposition.Rejected, failure, null, Version) { Scripts = made?.Runs ?? [] };
        HeroThrowScripted Stop(SessionHeroThrowResult done) => new(done, null, false);
        var item = request.Item;
        var scripts = request.Scripts!;
        // The refusals of the throw that no script changes are made before any script runs, so a refused throw leaves nothing applied.
        if (!ValidWear(new SessionWearSide { Life = SessionLifeTarget.Hero, Worn = request.HeroWorn }, new SessionWearSide(), targetBroken: false, wear: null)) return Stop(Refused(GameSessionFailure.InvalidWear));
        var life = _life.Hero!.Value.State;
        if (!LifeRules.Evaluate(life, HeroLifeFacts(life)).IsActive) return Stop(new(GameSessionDisposition.NoChange, GameSessionFailure.None, null, Version));
        if (ShotsInFlight > MaxShotsInFlight - 1) return Stop(Refused(GameSessionFailure.BudgetExceeded));
        if (!IsCurrent(expected)) return Stop(Refused(GameSessionFailure.StaleVersion));
        // The runs' revision, the throw's fate, concealment and leaving the hand.
        if (_revision >= long.MaxValue - 6) return Stop(Refused(GameSessionFailure.RevisionExhausted));
        // A refusing throw script gives a worn item back to the list: there must be a cell for it.
        if (throwScript && worn is not null && !HeroGridRoom(item, null)) return Stop(Refused(GameSessionFailure.BudgetExceeded));
        var (revision, map, seeds, flagVersion) = (_revision, _actor.Map, _inventory.SeedGeneration, _flags.Version);
        var complexity = worn?.Item.MagickTechComplexity ?? (_inventory.Weights.TryGetValue(item, out var weight) ? weight.Complexity : 0);
        var runs = new ItemScriptRuns(this, scripts, map.RuntimeMap, new Dictionary<string, int>(StringComparer.Ordinal) { [item] = complexity });
        // The removal 0x467860 for a worn item, then the throw script; a run the session cannot apply ends the sequence.
        if (worn is not null && points.Contains(SessionScriptPoint.WieldOff)) runs.Run(SessionScriptPoint.WieldOff, item, SessionEventKinds.PlayerEntity, null);
        if (runs.Failure == GameSessionFailure.None && !split && points.Contains(SessionScriptPoint.RemoveItem)) runs.Run(SessionScriptPoint.RemoveItem, item, SessionEventKinds.PlayerEntity, null);
        ScrRunResult? answer = null;
        if (runs.Failure == GameSessionFailure.None && throwScript) answer = runs.Run(SessionScriptPoint.Throw, item, SessionEventKinds.PlayerEntity, null);
        ThrowIfDisposed();
        // A host callback changed nothing the throw rests on: the revision, the attached seeds, the shown map, (for the runs that read them) the flags and the item.
        if (_revision != revision || _actor.Map != map || _inventory.SeedGeneration != seeds || _flags.Version != flagVersion || HeldKind(item) != kind) return Stop(Refused(GameSessionFailure.StaleVersion, runs));
        if (runs.Failure == GameSessionFailure.None) runs.Prepare();
        if (runs.Failure != GameSessionFailure.None) return Stop(Refused(runs.Failure, runs));
        if (answer?.Outcome == ScrOutcome.SkipDefault)
        {
            // The throw script refused: nothing is thrown, the item is back in the hero's list, not worn (0x4617F0 inserts it, it does not wield it).
            runs.Commit();
            var wornBefore = worn is not null;
            if (worn is not null)
            {
                _equipment.Worn.Remove(worn.Slot);
                PlaceInGrid(item, null);
            }
            Revise();
            var spells = request.ItemTriggers is { } triggers && !split ? RemovalSpellSteps(triggers, item, wornBefore) : [];
            return Stop(new SessionHeroThrowResult(GameSessionDisposition.Changed, GameSessionFailure.None, null, Version)
                { ScriptDeclined = true, RemovalSpells = spells, Messages = runs.Messages, EyeCandies = runs.EyeCandies, Scripts = runs.Runs, });
        }
        var applied = runs.Changes;
        if (applied)
        {
            runs.Commit();
            Revise();
        }
        return new(null, runs, applied);
    }
}
