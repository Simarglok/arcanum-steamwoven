using Arcanum.Scripting.Scr;
using Arcanum.Simulation;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Randomness;

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
    /// give it back, and the runs above. ARC-COMBAT-2176: with a struck critter the refusals of the attack on it (<see cref="StruckThrowPreflight"/>) are checked first too. ARC-COMBAT-2390: a refusal of the attack on the struck critter after the scripts ran that this check cannot foresee (the hero's values a script changed, the random sequence a host callback used) is made by <see cref="ThrowHeroItem"/>: the item is thrown at the location, as for a dead critter (the original's throw flies in any case, ledger TW8).
    /// </summary>
    private HeroThrowScripted RunHeroThrowScripts(GameSessionVersion expected, SessionHeroThrow request, SessionWornItem? worn, IReadOnlyList<int> points, bool split, SessionItemKind kind, bool throwScript, SessionHeroAttack? struckHero)
    {
        SessionHeroThrowResult Refused(GameSessionFailure failure, ItemScriptRuns? made = null) => new(GameSessionDisposition.Rejected, failure, null, Version) { Scripts = made?.Runs ?? [] };
        HeroThrowScripted Stop(SessionHeroThrowResult done) => new(done, null, false);
        var item = request.Item;
        var scripts = request.Scripts!;
        // ARC-COMBAT-2182 (TW3, QW5): a split stack's throw script runs for the stack the split makes. The original splits at the lift, so the script has its object; here the split comes after the scripts (a refusal leaves nothing made), so the script
        // runs for the entity the split will take, which answers as that made item meanwhile (_pendingThrowMade).
        var splitEntity = split ? NextObjectEntity(SessionObjectOrigin.Made) : null;
        if (split && (splitEntity is null || request.SplitKind is null)) return Stop(Refused(GameSessionFailure.BudgetExceeded));
        var attachee = splitEntity ?? item;
        // The refusals of the throw that no script changes are made before any script runs, so a refused throw leaves nothing applied.
        if (!ValidWear(new SessionWearSide { Life = SessionLifeTarget.Hero, Worn = request.HeroWorn }, new SessionWearSide(), targetBroken: false, wear: null)) return Stop(Refused(GameSessionFailure.InvalidWear));
        var life = _life.Hero!.Value.State;
        if (!LifeRules.Evaluate(life, HeroLifeFacts(life)).IsActive) return Stop(new(GameSessionDisposition.NoChange, GameSessionFailure.None, null, Version));
        if (ShotsInFlight > MaxShotsInFlight - 1) return Stop(Refused(GameSessionFailure.BudgetExceeded));
        if (!IsCurrent(expected)) return Stop(Refused(GameSessionFailure.StaleVersion));
        // The runs' revision, the throw's fate, concealment and leaving the hand.
        if (_revision >= long.MaxValue - 6) return Stop(Refused(GameSessionFailure.RevisionExhausted));
        // ARC-COMBAT-2176: the refusals of the attack on the struck critter (CritterAttack's, as the throw would meet them) come before any script runs.
        if (struckHero is not null && StruckThrowPreflight(request, struckHero) is var struckFailure && struckFailure != GameSessionFailure.None) return Stop(Refused(struckFailure));
        // A refusing throw script gives a worn item back to the list: there must be a cell for it.
        if (throwScript && worn is not null && !HeroGridRoom(item, null)) return Stop(Refused(GameSessionFailure.BudgetExceeded));
        var (revision, map, seeds, flagVersion) = (_revision, _actor.Map, _inventory.SeedGeneration, _flags.Version);
        // A made stack has no complexity of its own (its weight row is made with none).
        var complexity = worn?.Item.MagickTechComplexity ?? (!split && _inventory.Weights.TryGetValue(item, out var weight) ? weight.Complexity : 0);
        var runs = new ItemScriptRuns(this, scripts, map.RuntimeMap, new Dictionary<string, int>(StringComparer.Ordinal) { [attachee] = complexity });
        if (split) _pendingThrowMade = (splitEntity!, request.SplitKind!.Value);
        try
        {
            // The removal 0x467860 for a worn item, then the throw script; a run the session cannot apply ends the sequence.
            if (worn is not null && points.Contains(SessionScriptPoint.WieldOff)) runs.Run(SessionScriptPoint.WieldOff, attachee, SessionEventKinds.PlayerEntity, null);
            if (runs.Failure == GameSessionFailure.None && !split && points.Contains(SessionScriptPoint.RemoveItem)) runs.Run(SessionScriptPoint.RemoveItem, attachee, SessionEventKinds.PlayerEntity, null);
            ScrRunResult? answer = null;
            if (runs.Failure == GameSessionFailure.None && throwScript) answer = runs.Run(SessionScriptPoint.Throw, attachee, SessionEventKinds.PlayerEntity, null);
            ThrowIfDisposed();
            // A host callback changed nothing the throw rests on: the revision, the attached seeds, the shown map, (for the runs that read them) the flags and the item (a split stack: the source holds on and the planned entity is still the next made one).
            if (_revision != revision || _actor.Map != map || _inventory.SeedGeneration != seeds || _flags.Version != flagVersion
                || (split ? NextObjectEntity(SessionObjectOrigin.Made) != splitEntity : HeldKind(item) != kind)) return Stop(Refused(GameSessionFailure.StaleVersion, runs));
            if (runs.Failure == GameSessionFailure.None) runs.Prepare();
            if (runs.Failure != GameSessionFailure.None) return Stop(Refused(runs.Failure, runs));
            if (answer?.Outcome == ScrOutcome.SkipDefault)
            {
                // The throw script refused: nothing is thrown, the item is back in the hero's list, not worn (0x4617F0 inserts it, it does not wield it). A split stack's units go back to the stack they came from (the insertion merges them, 0x466640) and the
                // item that was to carry them is never made: the slots the runs wrote for it and the calls they scheduled on it are dropped.
                runs.Commit();
                var wornBefore = worn is not null;
                if (worn is not null)
                {
                    _equipment.Worn.Remove(worn.Slot);
                    PlaceInGrid(item, null);
                }
                if (split) ForgetNeverMadeThrowItem(splitEntity!);
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
        finally
        {
            _pendingThrowMade = null;
        }
    }

    /// <summary>ARC-COMBAT-2182: drops what the throw script's runs left for the item of a split that is never made (a skipped throw, a later refusal): the script slots they wrote and the deferred calls they scheduled with it as the attachee, so the next
    /// made item that takes the same entity inherits nothing.</summary>
    private void ForgetNeverMadeThrowItem(string entity)
    {
        foreach (var key in _inventory.ScriptSlots.Keys.Where(key => key.Entity == entity).ToArray()) _inventory.ScriptSlots.Remove(key);
        while (_events.RemoveFirst(SessionEventKinds.ScriptCall, e => SessionEventKinds.Call(e.Payload).Attachee?.Entity == entity) is not null) { }
    }

    /// <summary>
    /// ARC-COMBAT-2176: the refusals <see cref="CritterAttack"/> makes of a hero's pack throw at a struck critter, in its order, judged before the item's scripts run (as <see cref="NpcThrowPreflight"/> does for an NPC): the struck critter's record and
    /// facts, the two sides' worn items, the distance, the struck cell's light, the flight budget, the revisions of the throw, the defender's combat values and the roll sequence (the resolver and a missed throw's scatter run on a copy of it, nothing is
    /// drawn). A struck critter that is dead is no refusal here (the attack answers NoChange and the throw goes to the location). <paramref name="hero"/> is the hero as it throws now.
    /// </summary>
    private GameSessionFailure StruckThrowPreflight(SessionHeroThrow request, SessionHeroAttack hero)
    {
        var target = request.Struck!;
        var resolved = Resolve(target.Target, out var record, out var facts, out _);
        if (resolved != GameSessionFailure.None) return resolved;
        var attack = HeroThrowAttack(request, request.ItemTriggers);
        if (!ValidWear(new SessionWearSide { Life = SessionLifeTarget.Hero, Worn = attack.HeroWorn }, new SessionWearSide { Life = target.Target, Worn = attack.TargetWorn }, targetBroken: false, wear: null)) return GameSessionFailure.InvalidWear;
        if (!AttackDistanceFits(hero, attack.Distance) || attack.TargetLight is < 0 or > AttackRules.MaxLightLevel) return GameSessionFailure.InvalidAttack;
        if (ShotsInFlight > MaxShotsInFlight - ShotProjectiles(hero)) return GameSessionFailure.BudgetExceeded;
        if (_revision >= long.MaxValue - 2) return GameSessionFailure.RevisionExhausted;
        if (StrikeDefender(attack, target, record, facts!) is not { } defender) return GameSessionFailure.InvalidAttack;
        var pack = new PackThrow(request.Item, request.Target, hero);
        var strike = StrikeRequest(attack, hero, defender, null, pack, request.Item);
        var rolls = new TrialCombatRolls(new SeededRandom(_random.Seed, _random.Draws));
        try
        {
            var resolution = AttackResolver.Resolve(strike, rolls, OriginalCriticalEffects.Instance, _rules.Combat);
            if (resolution.Refusal == AttackRefusal.None && resolution.Outcome is not (AttackOutcome.Hit or AttackOutcome.CriticalHit)) rolls.Roll(CombatRollPurpose.ThrowScatter, 0, ThrowRules.ScatterDirections - 1);
        }
        catch (TrialCombatRolls.ExhaustedException) { return GameSessionFailure.RandomExhausted; }
        catch (ArgumentException) { return GameSessionFailure.InvalidAttack; }
        return GameSessionFailure.None;
    }
}
