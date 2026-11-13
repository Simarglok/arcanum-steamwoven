using Arcanum.Scripting.GlobalFlags;
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
/// chosen item has a move script or item spells, or (ARC-SCRIPT-042) the worn item the step would take off has item event scripts,
/// which this call does not run, <see cref="Unsupported"/> names the item and
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
    GameSessionVersion Version)
{
    /// <summary>ARC-MAGIC-1684: the printed lines of the item event scripts an NPC's re-wield ran, in order.</summary>
    public IReadOnlyList<SessionScriptMessage> Messages { get; init; } = [];
}

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
    public EquipBestResult EquipBest(GameSessionVersion expected, SessionWearableLookup items, SessionWearArtCheck artCheck, SessionInsertCheck insertCheck) =>
        Best(expected, items, artCheck, insertCheck, Enum.GetValues<EquipmentSlot>(), emptyOnly: false);

    /// <summary>
    /// ARC-UI-036: the start of a new character's game (the original's menu close 0x5412E0 with the best-items flag 0x64C424 that the
    /// new-character window's Accept sets): only the weapon slot (1004) and then the body-armour slot (1006), each only when it is empty
    /// (the item-wield-get test 0x4649C0), take the hero's best item (<see cref="WieldBest"/>, 0x465170). The other slots stay as they
    /// are. The same refusals, policy and revision as <see cref="EquipBest"/>.
    /// </summary>
    [OriginalFunction(0x5412E0, OriginalFunctionScope.Partial)]
    public EquipBestResult WieldStartBest(GameSessionVersion expected, SessionWearableLookup items, SessionWearArtCheck artCheck, SessionInsertCheck insertCheck) =>
        Best(expected, items, artCheck, insertCheck, [EquipmentSlot.Weapon, EquipmentSlot.Armor], emptyOnly: true);

    /// <summary>
    /// ARC-UI-036: the wear rules (<see cref="EquipmentRules.CheckWear"/>) for <paramref name="item"/> in <paramref name="slot"/> on the
    /// hero as it is now, whether or not the hero holds the item (the original's wear check 0x464D20, which the opening shop's kit asks
    /// before it keeps the clothes it made, ledger OS3); the art lookup is the host's. Null when the session has no hero.
    /// </summary>
    public EquipmentRefusal? CheckHeroWear(SessionWearableItem item, EquipmentSlot slot)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(item);
        if (!Enum.IsDefined(slot) || !ValidWearable(item)) throw new ArgumentException("invalid wearable item or slot", nameof(item));
        return _hero is null ? null : EquipmentRules.CheckWear(item.Item, slot, HeroWearer(), _equipment.Items());
    }

    private EquipBestResult Best(GameSessionVersion expected, SessionWearableLookup items, SessionWearArtCheck artCheck, SessionInsertCheck insertCheck,
        IReadOnlyList<EquipmentSlot> slots, bool emptyOnly)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(artCheck);
        ArgumentNullException.ThrowIfNull(insertCheck);
        if (!IsCurrent(expected)) return RejectBest(GameSessionFailure.StaleVersion);
        if (_hero is null) return RejectBest(GameSessionFailure.EquipmentUnavailable);
        var plan = new BestPlan(this, new(_equipment.Worn), HeroWearer(), HeroChooser(), artCheck,
            worn => HeroInsertVerdict(insertCheck(new SessionInventoryItem(worn.Entity, CarriedFacts(worn.Entity, worn.Item.MagickTechComplexity))),
                worn.Item.MagickTechComplexity));
        try
        {
            plan.Collect(items);
            foreach (var slot in slots) if (!emptyOnly || !plan.Worn.ContainsKey(slot)) WieldBest(plan, slot);
        }
        catch (BestPlan.Refused refused) { return RejectBest(refused.Failure); }
        var changed = plan.Worn.Count != _equipment.Worn.Count || plan.Worn.Any(pair => !_equipment.Worn.TryGetValue(pair.Key, out var worn) || worn != pair.Value);
        if (!changed) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, Array.AsReadOnly(plan.Steps.ToArray()), Version);
        if (_revision == long.MaxValue) return RejectBest(GameSessionFailure.RevisionExhausted);
        _equipment.Worn.Clear();
        foreach (var (slot, worn) in plan.Worn) _equipment.Worn[slot] = worn;
        Revise();
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
        var runsBefore = plan.RunCount;
        if (plan.Worn.TryGetValue(slot, out var current))
        {
            var facts = plan.Facts[current.Entity];
            if (slot != EquipmentSlot.Weapon && plan.Eligible(current.Entity, facts, slot)) keptWorth = EquipmentChoice.Worth(plan.Candidate(current.Entity));
            else plan.TakeOff(slot, step);
        }
        var tookOffByScripts = plan.RunCount != runsBefore;
        var held = plan.Held;
        var candidates = held.Select(plan.Candidate).ToArray();
        // ARC-MAGIC-1684: the choice reads the wearer's stats as the earlier steps' scripts left them (a plan whose scripts moved them is not one this call can make).
        plan.CheckChooser();
        var best = EquipmentChoice.ChooseBest(slot, candidates, i => plan.Eligible(held[i], plan.Facts[held[i]], slot), keptWorth, plan.Wielder);
        if (best is { } index) plan.Wield(held[index], slot, step);
        // An unsupported choice leaves the slot as the call found it: an item taken off for it goes back on. A take-off whose scripts ran cannot go back: that item is then wielded no further and the plan starts again (ARC-MAGIC-1684).
        if (step.Unsupported is not null && start is not null && step.TakenOff == start.Entity && tookOffByScripts) throw new BestPlan.ScriptBlocked(start.Entity);
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

    /// <summary>The planned equipment of one <see cref="EquipBest"/> call (the hero's) or of one NPC's wield after a regeneration
    /// (ARC-WORLD-103): the owner's state stays untouched until the call commits. <paramref name="insert"/> is the critter's insert check
    /// for an item a step takes off.</summary>
    private sealed class BestPlan(GameSessionOwner owner, SortedDictionary<EquipmentSlot, SessionWornItem> worn, EquipmentWearer wearer,
        EquipmentChooser wielder, SessionWearArtCheck artCheck, Func<SessionWornItem, SessionInsertVerdict> insert)
    {
        private readonly long _revision = owner._revision;
        // ARC-WORLD-103: a seed attached from a callback changes the lists the plan read without a revision.
        private readonly long _seeds = owner._inventory.SeedGeneration;

        internal SortedDictionary<EquipmentSlot, SessionWornItem> Worn { get; } = worn;
        internal Dictionary<string, SessionWearableItem> Facts { get; } = new(StringComparer.Ordinal);
        /// <summary>The wearable items the critter holds, in creation order (an NPC's: its list order).</summary>
        internal List<string> Held { get; } = [];
        internal List<SessionEquipBestStep> Steps { get; } = [];
        internal EquipmentWearer Wearer { get; } = wearer;
        internal EquipmentChooser Wielder { get; } = wielder;

        internal sealed class Refused(GameSessionFailure failure) : Exception
        {
            internal GameSessionFailure Failure { get; } = failure;
        }

        /// <summary>ARC-MAGIC-1684: the scripts of the item <see cref="Entity"/> cannot be applied in this plan (a run the session cannot apply, a move that cannot be undone, a choice read after the scripts moved the wearer's stats): the plan starts again
        /// without running that item's scripts.</summary>
        internal sealed class ScriptBlocked(string entity) : Exception
        {
            internal string Entity { get; } = entity;
        }

        /// <summary>ARC-MAGIC-1684: the scripts of the whole plan cannot be applied (the wearer's stats do not fit the character rules, the runs' changes do not fit the session): the plan starts again without running any.</summary>
        internal sealed class ScriptsRefused : Exception;

        // ARC-MAGIC-1684: an NPC's plan with the item event scripts of its moves (IE3-IE6): the runs they make, the NPC as their triggerer, the items whose scripts are not run, the values the choice reads at the start and the item
        // that ran last. Without the runs an item with a move script is not moved (the hero's plan, and an NPC's whose host gives no script context).
        private ItemScriptRuns? _runs;
        private string _npc = string.Empty;
        private HashSet<string> _blocked = [];
        private (int DamageBonus, int Aptitude, bool ExtraordinaryIntelligence) _chooserStart;
        private string? _lastRun;
        private string? _culprit;
        private readonly GlobalFlagVersion _flags = owner._flags.Version;

        /// <summary>The runs of the plan's moves, so far (the commit applies them with the plan).</summary>
        internal ItemScriptRuns? Runs => _runs;

        /// <summary>The number of script runs made so far.</summary>
        internal int RunCount => _runs?.Runs.Count ?? 0;

        internal void UseRuns(ItemScriptRuns runs, string npc, HashSet<string> blocked, (int, int, bool) chooser)
        {
            (_runs, _npc, _blocked, _chooserStart) = (runs, npc, blocked, chooser);
        }

        // Whether the item's move scripts can be left to the plan: it has none, or the plan runs them and has not given them up.
        private bool Runnable(string entity, IReadOnlyList<int> points) => !EquipmentDomain.MoveScripts.Any(points.Contains) || _runs is not null && !_blocked.Contains(entity);

        private void Run(int point, string entity)
        {
            _runs!.Run(point, entity, _npc, null);
            Fresh();
            _lastRun = entity;
            if (_runs.Failure != GameSessionFailure.None) throw new ScriptBlocked(entity);
            // The first run that moves what the choice reads is the one to give up when a choice follows.
            if (_culprit is null && _runs.NpcEffectChanges > 0 && (_runs.NpcSkillEffects || _runs.ChooserReads() != _chooserStart)) _culprit = entity;
        }

        // IE3: a removal unwields a worn item (wield off) and then runs remove item; IE4: an insertion wields an item put into a wear slot (wield on) and then runs insert item. Only the points the item carries run.
        private void RemovalRuns(string entity, IReadOnlyList<int> points, bool worn)
        {
            if (_runs is null) return;
            if (worn && points.Contains(SessionScriptPoint.WieldOff)) Run(SessionScriptPoint.WieldOff, entity);
            if (points.Contains(SessionScriptPoint.RemoveItem)) Run(SessionScriptPoint.RemoveItem, entity);
        }

        private void InsertionRuns(string entity, IReadOnlyList<int> points, bool wield)
        {
            if (_runs is null) return;
            if (wield && points.Contains(SessionScriptPoint.WieldOn)) Run(SessionScriptPoint.WieldOn, entity);
            if (points.Contains(SessionScriptPoint.InsertItem)) Run(SessionScriptPoint.InsertItem, entity);
        }

        /// <summary>The values the best-items choice reads of the wearer, as the earlier scripts left them, against the ones the plan started from.</summary>
        internal void CheckChooser()
        {
            if (_culprit is not null) throw new ScriptBlocked(_culprit);
        }

        /// <summary>The hero's held items with the host's facts.</summary>
        internal void Collect(SessionWearableLookup lookup)
        {
            var kinds = owner._inventory.Hero.PackKinds;
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
                // The session's record of the item's condition (ARC-WORLD-053) wins over the host's data.
                facts = owner.Conditioned(entity, facts);
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
            // ARC-SCRIPT-042: a worn item with item event scripts stays when this plan runs none (ARC-MAGIC-1684: an NPC's plan runs them).
            if (!Runnable(current.Entity, current.ScriptPoints)) { step.Unsupported = current.Entity; step.Failure = GameSessionFailure.ItemScriptUnsupported; return; }
            // ARC-MAGIC-022: nor does it run a marked item's unwear.
            if (current.ItemSpells) { step.Unsupported = current.Entity; step.Failure = GameSessionFailure.ItemEffectUnsupported; return; }
            if (EquipmentRules.CheckRemove(current.Item) is var refusal and not EquipmentRefusal.None) { step.Refusal = refusal; return; }
            var verdict = Insert(current);
            if (verdict != SessionInsertVerdict.Accepted) { step.Verdict = verdict; return; }
            // IE6 (take off): after the insert check accepts, the removal and the insertion at the pack.
            RemovalRuns(current.Entity, current.ScriptPoints, worn: true);
            InsertionRuns(current.Entity, current.ScriptPoints, wield: false);
            Worn.Remove(slot);
            step.TakenOff = current.Entity;
        }

        /// <summary>The original's wield: the wear check again, the item's and the occupant's remove checks, the occupant's insert.</summary>
        internal void Wield(string entity, EquipmentSlot slot, StepRecord step)
        {
            Worn.TryGetValue(slot, out var current);
            if (current?.Entity == entity) return;
            var facts = Facts[entity];
            var points = EquipmentDomain.MovePoints(facts.ScriptPoints);
            var unsupported = !Runnable(entity, points) ? GameSessionFailure.ItemScriptUnsupported
                : facts.HasItemSpells ? GameSessionFailure.ItemEffectUnsupported : GameSessionFailure.None;
            if (unsupported != GameSessionFailure.None) { step.Unsupported = entity; step.Failure = unsupported; return; }
            if (current is not null && !Runnable(current.Entity, current.ScriptPoints)) { step.Unsupported = current.Entity; step.Failure = GameSessionFailure.ItemScriptUnsupported; return; }
            if (current?.ItemSpells == true) { step.Unsupported = current.Entity; step.Failure = GameSessionFailure.ItemEffectUnsupported; return; }
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
            var previous = Worn.Values.FirstOrDefault(worn => worn.Entity == entity);
            // IE6 (ARC-MAGIC-1684, an NPC's plan): the item leaves where it was (a worn ring is unwielded first), the occupant is removed and asked the insert check; accepted, the occupant goes to the pack and the item is
            // wielded; refused, both go back (the item where it was, the occupant into the slot) and the scripts of both moves have run.
            RemovalRuns(entity, points, previous is not null);
            if (current is not null)
            {
                RemovalRuns(current.Entity, current.ScriptPoints, worn: true);
                var verdict = Insert(current);
                if (verdict != SessionInsertVerdict.Accepted)
                {
                    InsertionRuns(entity, points, wield: previous is not null);
                    InsertionRuns(current.Entity, current.ScriptPoints, wield: true);
                    step.Verdict = verdict;
                    return;
                }
                InsertionRuns(current.Entity, current.ScriptPoints, wield: false);
                step.TakenOff ??= current.Entity;
            }
            InsertionRuns(entity, points, wield: true);
            if (previous is not null) Worn.Remove(previous.Slot);
            Worn[slot] = new(slot, entity, facts.Item) { ScriptPoints = points };
            step.Wielded = entity;
        }

        private SessionInsertVerdict Insert(SessionWornItem current)
        {
            var verdict = insert(current);
            Fresh();
            return verdict;
        }

        // A host callback that moved the session makes the whole call stale.
        internal void Fresh()
        {
            owner.ThrowIfDisposed();
            if (owner._revision != _revision || owner._inventory.SeedGeneration != _seeds || owner._flags.Version != _flags) throw new Refused(GameSessionFailure.StaleVersion);
        }
    }

    /// <summary>The hero as the best-item choice scores it: its effective DamageBonus and aptitude, its weapon skill levels
    /// (<see cref="QueryHeroSkills"/>, ARC-SKILLS-002; firearms a tech skill), extraordinary intelligence, the local PC under the
    /// session's difficulty, and (ARC-WORLD-066) the units of its ammunition stack of each type (its first stack of the type, the
    /// original's one stack a critter field names).</summary>
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
            Ammunition = Enumerable.Range(0, SessionAmmoRecord.AmmoTypes).Select(type => (type, stack: HeroStackOfType(type)))
                .Where(pair => pair.stack is not null).ToDictionary(pair => pair.type, pair => pair.stack!.Value.Units),
        };
    }
}
