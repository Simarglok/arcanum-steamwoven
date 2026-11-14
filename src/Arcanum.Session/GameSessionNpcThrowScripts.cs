using Arcanum.Scripting.Scr;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;

namespace Arcanum.Session;

/// <summary>
/// ARC-COMBAT-1761: the item scripts an NPC's throw runs (the attack start's removal 0x466E00 → 0x467860 and the throw 0x4B3C00 → 0x441980). Contract: docs/world/ai-host.md (Session throw), docs/research/ledger/combat.md (TW1, TW3, VC12).
/// </summary>
public sealed partial class GameSessionOwner
{
    // What the scripts of a throw came to: a result the throw ends with (a refusal, nothing to throw, the declined throw), or the runs whose changes are applied (Applied: they changed the session and took a revision of their own).
    private sealed record NpcThrowScripted(SessionNpcThrowResult? Done, ItemScriptRuns? Runs, bool Applied);

    /// <summary>
    /// ARC-COMBAT-1761 (0x4B3C00, ledger TW1, TW3; exe read 0x434B00, 0x466E00, 0x467860, 0x441980): the scripts around an NPC's throw, run before anything of the throw is decided. The attack start removes the item from its holder first
    /// (0x466E00 → 0x467860: the wield-off script (25) of an item the NPC wears, then its remove-item script (28)); the throw (0x4B3C00) then runs the item's throw script (point 6, 0x441980) with the thrower as the triggerer, the item as
    /// the attachee and no other object (the throw goal's target object is none for an attack's or the AI's throw, TW2). All three are run under <see cref="SessionItemScripts.Admission"/> with the NPC as the triggerer and as the wearer
    /// of the effect changes, each reading what the earlier runs did (<see cref="ItemScriptRuns"/>); a run the session cannot apply (a script outside the set, an effect or an object it does not model, a printed line whose gender text the
    /// host cannot choose) refuses the throw before any effect. A throw script that answers skip default (the original's 0) throws nothing: the removal's and the script's own changes stay, the item goes back to the thrower's list unworn
    /// (the original puts it on the thrower's tile, 0x4415C0, and inserts it again, 0x4617F0, which sets the NPC's re-wield bit; the insert-item script of that insertion is not run: an item with one is refused earlier) and the result is
    /// <see cref="SessionNpcThrowResult.ScriptDeclined"/>. Otherwise the runs' changes are applied in a revision of their own and the throw goes on from the new version. Refused before any effect: the thrower's life record, an inactive
    /// thrower (NoChange), the version, the revision, the throw's own refusals that no script changes (<see cref="NpcThrowPreflight"/>: the thrower's and the struck critter's values, the wear, the shot budget) and the runs above.
    /// NOT READY: the item spells' unwear and the removal's light recalculation (the host's, RM3d), the eye candy shown on the thrower, and a refusal of the throw after its scripts ran that the preflight cannot foresee (the result is then changed with the failure
    /// named, the item still worn here; none is known).
    /// </summary>
    private NpcThrowScripted RunNpcThrowScripts(GameSessionVersion expected, SessionNpcThrow request, SessionItemFacts facts, bool worn, bool generic, bool struck)
    {
        SessionNpcThrowResult Refused(GameSessionFailure failure, ItemScriptRuns? made = null) => new(GameSessionDisposition.Rejected, failure, null, null, Version) { Scripts = made?.Runs ?? [] };
        NpcThrowScripted Stop(SessionNpcThrowResult done) => new(done, null, false);
        if (request.Scripts is not { } scripts || request.Thrower.Entity is not { } npc) return Stop(Refused(GameSessionFailure.ItemScriptUnsupported));
        var resolved = Resolve(request.Thrower.Target, out var record, out var lifeFacts, out _);
        if (resolved != GameSessionFailure.None) return Stop(Refused(resolved));
        // An inactive thrower throws nothing, as the throw itself answers: no script runs for it.
        if (!LifeRules.Evaluate(record.State, lifeFacts!).IsActive) return Stop(new(GameSessionDisposition.NoChange, GameSessionFailure.None, null, null, Version));
        if (!IsCurrent(expected)) return Stop(Refused(GameSessionFailure.StaleVersion));
        // The runs' revision, the landing's and the fate's.
        if (_revision >= long.MaxValue - 6) return Stop(Refused(GameSessionFailure.RevisionExhausted));
        // A refusal of the throw itself that no script can change is made before any script runs, so a refused throw leaves nothing applied and the AI's next try does not run the scripts again.
        var preflight = NpcThrowPreflight(request, generic, struck);
        if (preflight != GameSessionFailure.None) return Stop(Refused(preflight));
        // A declined throw writes the NPC's worn record when its map data wore the item (see UnwearNpcItem): the saved budget must hold it.
        if (worn && !_inventory.NpcWorn.ContainsKey(npc) && _inventory.NpcWorn.Count >= SessionInventoryState.MaxNpcWorn) return Stop(Refused(GameSessionFailure.BudgetExceeded));
        var (revision, map, seeds, flagVersion) = (_revision, _actor.Map, _inventory.SeedGeneration, _flags.Version);
        var item = request.Item;
        var runs = new ItemScriptRuns(this, scripts.Host, map.RuntimeMap, new Dictionary<string, int>(StringComparer.Ordinal) { [item] = facts.Complexity },
            new NpcWearer(npc, scripts.Target, NpcStatsOverSheet(npc, scripts.Stored))) { NpcTriggerer = true };
        // The removal 0x467860 for an item the NPC wears, then the throw script; a run the session cannot apply ends the sequence.
        if (worn && facts.HasScript(SessionScriptPoint.WieldOff)) runs.Run(SessionScriptPoint.WieldOff, item, npc, null);
        if (runs.Failure == GameSessionFailure.None && facts.HasScript(SessionScriptPoint.RemoveItem)) runs.Run(SessionScriptPoint.RemoveItem, item, npc, null);
        ScrRunResult? answer = null;
        if (runs.Failure == GameSessionFailure.None && facts.HasScript(SessionScriptPoint.Throw)) answer = runs.Run(SessionScriptPoint.Throw, item, npc, null);
        ThrowIfDisposed();
        // A host callback changed nothing the throw rests on: the revision, the attached seeds, the shown map and (for the runs that read them) the flags.
        if (_revision != revision || _actor.Map != map || _inventory.SeedGeneration != seeds || _flags.Version != flagVersion) return Stop(Refused(GameSessionFailure.StaleVersion, runs));
        if (runs.Failure == GameSessionFailure.None) runs.Prepare();
        if (runs.Failure != GameSessionFailure.None) return Stop(Refused(runs.Failure, runs));
        if (answer?.Outcome == ScrOutcome.SkipDefault)
        {
            // The throw script refused: nothing is thrown, the item is back with the thrower, not worn (0x4617F0 inserts it, not re-wields it), and the insertion sets the NPC's re-wield bit.
            runs.Commit();
            UnwearNpcItem(npc, item);
            StoreInsertPlace(npc, item);
            MarkCheckWield(npc);
            Revise();
            return Stop(new(GameSessionDisposition.Changed, GameSessionFailure.None, null, null, Version)
                { ScriptDeclined = true, Messages = runs.Messages, EyeCandies = runs.EyeCandies, Scripts = runs.Runs });
        }
        var applied = runs.Changes;
        if (applied)
        {
            runs.Commit();
            Revise();
        }
        return new(null, runs, applied);
    }

    // ARC-COMBAT-1761: the refusals of the throw that do not depend on its scripts, checked as the throw itself checks them (ThrowNpcGeneric, the unstruck weapon's path and NpcAttack's opening), so they come before any script runs. An inactive thrower is the
    // caller's NoChange.
    private GameSessionFailure NpcThrowPreflight(SessionNpcThrow request, bool generic, bool struck)
    {
        var attacker = request.Thrower;
        var resolved = Resolve(attacker.Target, out var record, out var facts, out _);
        if (resolved != GameSessionFailure.None) return resolved;
        var status = LifeRules.Evaluate(record.State, facts!);
        if (!status.IsActive) return GameSessionFailure.None;
        if (generic)
        {
            try
            {
                Levelled(attacker).Combatant(status, record.State, _rules.Combat);
                Levelled(attacker).SkillValues(CombatSkill.Throwing, record.State);
            }
            catch (ArgumentException) { return GameSessionFailure.InvalidAttack; }
            return ShotsInFlight > MaxShotsInFlight - 1 ? GameSessionFailure.BudgetExceeded : GameSessionFailure.None;
        }
        var target = request.Struck;
        if (target is not null && string.Equals(target.Entity, attacker.Entity, StringComparison.Ordinal)) return GameSessionFailure.InvalidAttack;
        var targetLife = target?.Target ?? SessionLifeTarget.Hero;
        SessionWearSide targetSide = new();
        if (struck)
        {
            resolved = Resolve(targetLife, out var targetRecord, out var targetFacts, out _);
            if (resolved != GameSessionFailure.None) return resolved;
            targetSide = new SessionWearSide { Life = targetLife, Worn = request.StruckWorn };
            if (target is not null)
            {
                try { Levelled(target).Combatant(LifeRules.Evaluate(targetRecord.State, targetFacts!), targetRecord.State, _rules.Combat); }
                catch (ArgumentException) { return GameSessionFailure.InvalidAttack; }
            }
        }
        if (!ValidWear(new SessionWearSide { Life = attacker.Target, Worn = request.ThrowerWorn }, targetSide, targetBroken: false, wear: null)) return GameSessionFailure.InvalidWear;
        SessionNpcAttackValues values;
        try { values = Levelled(attacker).AttackValues(status, record.State, _rules.Combat, request.ThrowerWorn?.Keys); }
        catch (ArgumentException) { return GameSessionFailure.InvalidAttack; }
        if (values.Weapon.Kind != WeaponKind.Weapon || values.Skill != CombatSkill.Melee || struck && !values.Supported) return GameSessionFailure.AttackUnsupported;
        return GameSessionFailure.None;
    }

    // ARC-COMBAT-1761: the NPC's item is worn no more. A dressed NPC's record loses it; an NPC the session never dressed gets a record of what its map data has worn without it (the record then replaces the map data's inventory locations), within the
    // saved budget (a record that does not fit leaves the map data's location, as the wield's would).
    private void UnwearNpcItem(string npc, string item)
    {
        if (_inventory.NpcWorn.ContainsKey(npc))
        {
            _inventory.Unwear(item);
            return;
        }
        if (QueryHolderItems(npc) is not { } listed) return;
        var kept = new SortedDictionary<EquipmentSlot, string>();
        var wasWorn = false;
        foreach (var held in listed)
        {
            if (!held.Facts.Worn || held.Facts.InventoryLocation is not (>= (int)EquipmentSlot.Helmet and <= (int)EquipmentSlot.Boots)) continue;
            if (held.Entity == item) wasWorn = true;
            else kept[(EquipmentSlot)held.Facts.InventoryLocation] = held.Entity;
        }
        if (wasWorn && _inventory.NpcWorn.Count < SessionInventoryState.MaxNpcWorn) _inventory.NpcWorn[npc] = kept;
    }
}
