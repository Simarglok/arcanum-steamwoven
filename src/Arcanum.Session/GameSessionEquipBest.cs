using Arcanum.Simulation;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Skills;

namespace Arcanum.Session;

/// <summary>The host's facts of one item the hero holds, for <see cref="GameSessionOwner.EquipBest"/>: null for an item that is worn
/// nowhere (food, a scroll, a key...). The answer must not call back into the owner.</summary>
public delegate SessionWearableItem? SessionWearableLookup(string entity);

/// <summary>
/// What <see cref="GameSessionOwner.EquipBest"/> did in one slot: the item it took off (to the inventory), the item it wielded, and,
/// when a step was refused, the wear rule (<see cref="Refusal"/>) or the insert check's answer (<see cref="Verdict"/>). When the
/// chosen item has a move script or item spells, which the session does not run, <see cref="Unsupported"/> names the item and
/// <see cref="Failure"/> the reason (<see cref="GameSessionFailure.ItemScriptUnsupported"/> or
/// <see cref="GameSessionFailure.ItemEffectUnsupported"/>), and the slot keeps what it held when the call began (a weapon taken off
/// for the choice goes back on). A slot where nothing happened is not listed.
/// </summary>
public sealed record SessionEquipBestStep(EquipmentSlot Slot, string? TakenOff, string? Wielded, EquipmentRefusal Refusal, SessionInsertVerdict? Verdict)
{
    public string? Unsupported { get; init; }
    public GameSessionFailure Failure { get; init; }
}

/// <summary>Outcome of <see cref="GameSessionOwner.EquipBest"/>: the steps in slot order (none when refused).</summary>
public sealed record EquipBestResult(GameSessionDisposition Disposition, GameSessionFailure Failure, IReadOnlyList<SessionEquipBestStep> Steps,
    GameSessionVersion Version);

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// The hero wields its best items (the original's best-items call, which a new game runs for the pre-made hero before it starts,
    /// ARC-WORLD-034): for each slot from the helmet (1000) to the boots (1008) in turn, <see cref="WieldBest"/>. The host gives the
    /// facts of every item the hero holds (<paramref name="items"/>), answers the art lookup and the insert check. Refused before any
    /// effect: version, no hero (<see cref="GameSessionFailure.EquipmentUnavailable"/>), an item whose facts are invalid
    /// (<see cref="GameSessionFailure.InvalidItem"/>) or of another kind than the saved one or, for a worn item, other values than the
    /// saved ones (<see cref="GameSessionFailure.InventoryMismatch"/>), and a callback that changed the session
    /// (<see cref="GameSessionFailure.StaleVersion"/>). A step the rules or the insert check refuse leaves its slot as it is and the call
    /// goes on, as in the source. A chosen item with a move script or item spells is not wielded (project policy: the session runs no
    /// item script or spell; the original would wear it and run them): its step names it (<see cref="SessionEquipBestStep.Unsupported"/>)
    /// and the call goes on. One revision when the worn items changed; NoChange otherwise.
    /// </summary>
    [OriginalFunction(0x4654F0, OriginalFunctionScope.Partial)]
    public EquipBestResult EquipBest(GameSessionVersion expected, SessionWearableLookup items, SessionWearArtCheck artCheck, SessionInsertCheck insertCheck)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(artCheck);
        ArgumentNullException.ThrowIfNull(insertCheck);
        if (!IsCurrent(expected)) return RejectBest(GameSessionFailure.StaleVersion);
        if (_hero is null) return RejectBest(GameSessionFailure.EquipmentUnavailable);
        var plan = new BestPlan(this, items, artCheck, insertCheck);
        try
        {
            plan.Collect();
            foreach (var slot in Enum.GetValues<EquipmentSlot>()) WieldBest(plan, slot);
        }
        catch (BestPlan.Refused refused) { return RejectBest(refused.Failure); }
        var changed = plan.Worn.Count != _equipment.Worn.Count || plan.Worn.Any(pair => !_equipment.Worn.TryGetValue(pair.Key, out var worn) || worn != pair.Value);
        if (!changed) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, Array.AsReadOnly(plan.Steps.ToArray()), Version);
        if (_revision == long.MaxValue) return RejectBest(GameSessionFailure.RevisionExhausted);
        _equipment.Worn.Clear();
        foreach (var (slot, worn) in plan.Worn) _equipment.Worn[slot] = worn;
        _revision++;
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, Array.AsReadOnly(plan.Steps.ToArray()), Version);
    }

    /// <summary>
    /// One slot of <see cref="EquipBest"/> (the original's best-item call for a slot, without a target): an item in the slot that still
    /// passes the wear check stays as the worth to beat, except in the weapon slot; any other is taken off (no-drop and the insert check
    /// can keep it on). Then <see cref="EquipmentChoice.ChooseBest"/> over the held items in creation order (the kit, then the items
    /// taken from the world), and the choice is wielded as by <see cref="Equip"/>; with no choice an empty weapon slot stays empty.
    /// </summary>
    [OriginalFunction(0x465170, OriginalFunctionScope.Partial)]
    private static void WieldBest(BestPlan plan, EquipmentSlot slot)
    {
        var step = new StepRecord(slot);
        int? keptWorth = null;
        var start = plan.Worn.GetValueOrDefault(slot);
        if (plan.Worn.TryGetValue(slot, out var current))
        {
            var facts = plan.Facts[current.Entity];
            if (slot != EquipmentSlot.Weapon && plan.Eligible(current.Entity, facts, slot)) keptWorth = EquipmentChoice.Worth(plan.Candidate(current.Entity));
            else plan.TakeOff(slot, step);
        }
        var held = plan.Held;
        var candidates = held.Select(plan.Candidate).ToArray();
        var best = EquipmentChoice.ChooseBest(slot, candidates, i => plan.Eligible(held[i], plan.Facts[held[i]], slot), keptWorth, plan.Wielder);
        if (best is { } index) plan.Wield(held[index], slot, step);
        // An unsupported choice leaves the slot as the call found it: an item taken off for it goes back on.
        if (step.Unsupported is not null && start is not null && step.TakenOff == start.Entity)
        {
            plan.Worn[slot] = start;
            step.TakenOff = null;
        }
        if (step.Happened) plan.Steps.Add(step.Capture());
    }

    private EquipBestResult RejectBest(GameSessionFailure failure) => new(GameSessionDisposition.Rejected, failure, [], Version);

    private sealed class StepRecord(EquipmentSlot slot)
    {
        internal string? TakenOff { get; set; }
        internal string? Wielded { get; set; }
        internal EquipmentRefusal Refusal { get; set; }
        internal SessionInsertVerdict? Verdict { get; set; }
        internal string? Unsupported { get; set; }
        internal GameSessionFailure Failure { get; set; }
        internal bool Happened => TakenOff is not null || Wielded is not null || Refusal != EquipmentRefusal.None || Verdict is not null || Unsupported is not null;
        internal SessionEquipBestStep Capture() => new(slot, TakenOff, Wielded, Refusal, Verdict) { Unsupported = Unsupported, Failure = Failure };
    }

    /// <summary>The planned equipment of one <see cref="EquipBest"/> call: the owner's state stays untouched until the call commits.</summary>
    private sealed class BestPlan(GameSessionOwner owner, SessionWearableLookup lookup, SessionWearArtCheck artCheck, SessionInsertCheck insertCheck)
    {
        private readonly long _revision = owner._revision;

        internal SortedDictionary<EquipmentSlot, SessionWornItem> Worn { get; } = new(owner._equipment.Worn);
        internal Dictionary<string, SessionWearableItem> Facts { get; } = new(StringComparer.Ordinal);
        /// <summary>The wearable items the hero holds, in creation order.</summary>
        internal List<string> Held { get; } = [];
        internal List<SessionEquipBestStep> Steps { get; } = [];
        internal EquipmentWearer Wearer { get; } = owner.HeroWearer();
        internal EquipmentChooser Wielder { get; } = owner.HeroChooser();

        internal sealed class Refused(GameSessionFailure failure) : Exception
        {
            internal GameSessionFailure Failure { get; } = failure;
        }

        internal void Collect()
        {
            var kinds = owner._inventory.Kit.Select(kit => (kit.Entity, kit.Kind)).Concat(owner._inventory.Pc.Select(pc => (pc.Entity, pc.Kind)));
            foreach (var (entity, kind) in kinds)
            {
                var facts = lookup(entity);
                Fresh();
                if (facts is null)
                {
                    if (owner._equipment.SlotOf(entity) is not null) throw new Refused(GameSessionFailure.InventoryMismatch);
                    continue;
                }
                if (!ValidWearable(facts) || facts.Weight is < 0 or > EquipmentChoice.MaxWeight) throw new Refused(GameSessionFailure.InvalidItem);
                if (facts.Kind != kind || !EquipmentDomain.Matches(kind.Type, facts.Item.Kind)) throw new Refused(GameSessionFailure.InventoryMismatch);
                if (owner._equipment.SlotOf(entity) is { } slot && owner._equipment.Worn[slot].Item != facts.Item) throw new Refused(GameSessionFailure.InventoryMismatch);
                Facts[entity] = facts;
                Held.Add(entity);
            }
        }

        internal EquipmentCandidate Candidate(string entity)
        {
            var facts = Facts[entity];
            return new EquipmentCandidate(facts.Item)
            {
                WornIn = Worn.Values.FirstOrDefault(worn => worn.Entity == entity)?.Slot,
                Worth = facts.Worth, IdentifiedFlag = facts.IdentifiedFlag, Throwable = facts.Throwable, Weight = facts.Weight,
            };
        }

        /// <summary>The wear check against the planned equipment, the host's art answer included.</summary>
        internal bool Eligible(string entity, SessionWearableItem facts, EquipmentSlot slot)
        {
            var worn = Worn.ToDictionary(pair => pair.Key, pair => pair.Value.Item);
            if (EquipmentRules.CheckWear(facts.Item, slot, Wearer, worn) != EquipmentRefusal.None) return false;
            if (!EquipmentRules.NeedsArtCheck(facts.Item, slot)) return true;
            var art = artCheck(slot, facts);
            Fresh();
            return art;
        }

        /// <summary>Takes the slot's item off unless it cannot leave or has no room (the original's unwield to the grid).</summary>
        internal void TakeOff(EquipmentSlot slot, StepRecord step)
        {
            var current = Worn[slot];
            if (EquipmentRules.CheckRemove(current.Item) is var refusal and not EquipmentRefusal.None) { step.Refusal = refusal; return; }
            var verdict = Insert(current.Entity);
            if (verdict != SessionInsertVerdict.Accepted) { step.Verdict = verdict; return; }
            Worn.Remove(slot);
            step.TakenOff = current.Entity;
        }

        /// <summary>The original's wield: the wear check again, the item's and the occupant's remove checks, the occupant's insert.</summary>
        internal void Wield(string entity, EquipmentSlot slot, StepRecord step)
        {
            Worn.TryGetValue(slot, out var current);
            if (current?.Entity == entity) return;
            var facts = Facts[entity];
            var unsupported = EquipmentDomain.MoveScripts.Any(facts.ScriptPoints.Contains) ? GameSessionFailure.ItemScriptUnsupported
                : facts.HasItemSpells ? GameSessionFailure.ItemEffectUnsupported : GameSessionFailure.None;
            if (unsupported != GameSessionFailure.None) { step.Unsupported = entity; step.Failure = unsupported; return; }
            var worn = Worn.ToDictionary(pair => pair.Key, pair => pair.Value.Item);
            var refusal = EquipmentRules.CheckWear(facts.Item, slot, Wearer, worn);
            if (refusal == EquipmentRefusal.None && EquipmentRules.NeedsArtCheck(facts.Item, slot))
            {
                var art = artCheck(slot, facts);
                Fresh();
                if (!art) refusal = EquipmentRefusal.NotWieldable;
            }
            if (refusal == EquipmentRefusal.None) refusal = EquipmentRules.CheckRemove(facts.Item);
            if (refusal == EquipmentRefusal.None && current is not null) refusal = EquipmentRules.CheckRemove(current.Item);
            if (refusal != EquipmentRefusal.None) { step.Refusal = refusal; return; }
            if (current is not null)
            {
                var verdict = Insert(current.Entity);
                if (verdict != SessionInsertVerdict.Accepted) { step.Verdict = verdict; return; }
                step.TakenOff ??= current.Entity;
            }
            if (Worn.Values.FirstOrDefault(worn => worn.Entity == entity) is { } previous) Worn.Remove(previous.Slot);
            Worn[slot] = new(slot, entity, facts.Item);
            step.Wielded = entity;
        }

        private SessionInsertVerdict Insert(string entity)
        {
            var verdict = insertCheck(new SessionInventoryItem(entity, owner.CarriedFacts(entity)));
            Fresh();
            return verdict;
        }

        // A host callback that moved the session makes the whole call stale.
        private void Fresh()
        {
            owner.ThrowIfDisposed();
            if (owner._revision != _revision) throw new Refused(GameSessionFailure.StaleVersion);
        }
    }

    /// <summary>The hero as the best-item choice scores it: its effective DamageBonus and aptitude, its weapon skill levels
    /// (<see cref="QueryHeroSkills"/>, ARC-SKILLS-002; firearms a tech skill), extraordinary intelligence, the local PC under the
    /// session's difficulty, and no ammunition (the PC's list holds none).</summary>
    internal EquipmentChooser HeroChooser()
    {
        var state = _life.Hero!.Value.State;
        var snapshot = _hero!.Stats.Query(new(0, state.PoisonLevel, state.Conditions.HasFlag(LifeConditions.CrippledLegsBoth))).Snapshot!;
        var skills = HeroSkills();
        return new EquipmentChooser
        {
            DamageBonus = snapshot.GetEffective(CharacterStat.DamageBonus),
            MagickTechAptitude = HeroAptitude(),
            MeleeLevel = skills[Skill.Melee].Level,
            BowLevel = skills[Skill.Bow].Level,
            ThrowingLevel = skills[Skill.Throwing].Level,
            FirearmsLevel = skills[Skill.Firearms].Level,
            ExtraordinaryIntelligence = snapshot.GetEffective(CharacterStat.Intelligence) >= CharacterStatRules.ExtraordinaryThreshold,
            IsPc = true,
            Difficulty = _difficulty,
        };
    }
}
