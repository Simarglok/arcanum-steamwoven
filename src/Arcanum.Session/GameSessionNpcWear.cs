using Arcanum.Simulation.Equipment;

namespace Arcanum.Session;

/// <summary>ARC-WORLD-103: one worn item of an NPC the session dressed: its slot and the item entity (a seed item of the NPC's list or a
/// made item the session added to it).</summary>
public readonly record struct SessionNpcWornItem(EquipmentSlot Slot, string Item);

/// <summary>ARC-WORLD-103: the worn items of an NPC the session dressed, in slot order (empty: it wears nothing).</summary>
public sealed record SessionNpcWear(string Npc, IReadOnlyList<SessionNpcWornItem> Worn);

/// <summary>ARC-WORLD-103: the host's wear facts of one item of an NPC's list (its entity and facts as the session's list shows them): null
/// for an item that is worn nowhere. The answer must not call back into the owner.</summary>
public delegate SessionWearableItem? SessionNpcWearableLookup(SessionInventoryItem item);

/// <summary>
/// ARC-WORLD-103: what the host gives for an NPC's best-items wield after a regeneration (0x4654F0, ledger RK15): the NPC as a wearer
/// (race, gender, crippled arms in its current life state) and as the choice scores it (<see cref="EquipmentChooser"/>: its damage bonus,
/// aptitude, weapon skill levels, extraordinary intelligence; the session sets <see cref="EquipmentChooser.IsPc"/> false and the carried
/// ammunition from the NPC's stacks), the wear facts of the items of its list and the art lookup of a weapon slot.
/// </summary>
public sealed record SessionNpcWielder(EquipmentWearer Wearer, EquipmentChooser Chooser, SessionNpcWearableLookup Items, SessionWearArtCheck ArtCheck);

/// <summary>
/// ARC-WORLD-103: what an NPC's best-items wield after a regeneration did: <see cref="GameSessionFailure.None"/> with the steps in slot
/// order (as <see cref="GameSessionOwner.EquipBest"/> reports them), or the reason nothing was wielded and the NPC's worn items stayed as
/// the regeneration left them: <see cref="GameSessionFailure.EquipmentUnavailable"/> (the host gives no wielder: Not ready),
/// <see cref="GameSessionFailure.InvalidEquipment"/> (a chooser outside the bounds the weapon score reads: skill levels 0..20, the
/// aptitude's range, a damage bonus within ±1,000,000),
/// <see cref="GameSessionFailure.InvalidItem"/> (an item's facts are invalid), <see cref="GameSessionFailure.InventoryMismatch"/> (an
/// item's facts disagree with the session's list: another kind, or no wear facts for a worn item), <see cref="GameSessionFailure.StaleVersion"/>
/// (a host callback changed the session).
/// </summary>
public sealed record SessionNpcWield(GameSessionFailure Failure, IReadOnlyList<SessionEquipBestStep> Steps)
{
    /// <summary>ARC-MAGIC-1684: the printed lines of the item event scripts the wield ran, in order (the regeneration shows none).</summary>
    public IReadOnlyList<SessionScriptMessage> Messages { get; init; } = [];
}

/// <summary>
/// ARC-MAGIC-1684: what the item event scripts of an NPC's best-items wield need of the host (<see cref="ISessionRestockWorld.NpcWieldScripts"/>): the shown map's item script host, the NPC's life target (its effect list, which the
/// scripts change, and the life rules those changes meet) and its stored stats as the host holds them (the 28 values of the setter's array with what a spell wrote laid over; the session lays an advancement record's sheet over them).
/// The answers must not call back into the owner.
/// </summary>
public sealed record SessionNpcWieldScripts(ISessionItemScriptHost Host, SessionLifeTarget Target, IReadOnlyList<int> Stored);

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-WORLD-103: the worn items of an NPC the session dressed (a regeneration's best-items wield) that are still in its list, in slot
    /// order; null when the session never dressed it, so the inventory locations of its map data stand. The session's lists of the NPC
    /// (<see cref="QueryMapInventory"/>) already show these locations. Throws for an invalid entity id.
    /// </summary>
    public SessionNpcWear? QueryNpcWorn(string npc)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(npc)) throw new ArgumentException("Invalid entity id.", nameof(npc));
        if (!_inventory.NpcWorn.TryGetValue(npc, out var worn)) return null;
        return new(npc, Array.AsReadOnly(worn.Where(pair => _inventory.NpcOf(pair.Value) == npc || SeedListing(npc, pair.Value) is not null)
            .Select(pair => new SessionNpcWornItem(pair.Key, pair.Value)).ToArray()));
    }

    // ARC-WORLD-103: whether an item of the NPC's list is worn, as the session's lists show it (a dressed NPC's record, else the map data).
    private bool NpcWears(string npc, string item, SessionItemFacts facts) => _inventory.Worn(npc, item, facts).Worn;

    /// <summary>
    /// ARC-WORLD-103: the NPC's best-items wield at the end of its regeneration (0x463E20 calls 0x4654F0 for an NPC, type 16, after the
    /// lines, without a target, ledger RK15): for each slot from the helmet (1000) to the boots (1008) the rules of
    /// <see cref="EquipBest"/> (WB1–WB8) over the NPC's list in its order, starting from what it wears after the clear (its persistent
    /// items keep their slots). Its insert check takes an item a step takes off. ARC-MAGIC-1684: the moves run the items' item event scripts
    /// when the host gives their context (<see cref="ISessionRestockWorld.NpcWieldScripts"/>), in the regeneration's one revision; without it, or for a script the session cannot apply, a choice with a move script is not
    /// wielded (the original would), and a choice with item spells never is. On success the NPC's worn items are recorded (the record then
    /// replaces its map data's inventory locations); on a refusal nothing is recorded. Part of the regeneration's one revision.
    /// </summary>
    private SessionNpcWield WieldNpcBest(ISessionRestockWorld world, SessionMapId map, InventoryDomain.Seed seed, string npc)
    {
        var planned = PlanNpcWield(world, map, seed, npc, Enum.GetValues<EquipmentSlot>());
        if (planned.Failure != GameSessionFailure.None) return new(planned.Failure, []);
        // ARC-MAGIC-1684: the scripts' changes are applied with the wield, in the regeneration's one revision.
        planned.Runs?.Commit();
        _inventory.NpcWorn[npc] = planned.Record!;
        foreach (var worn in planned.Record!.Values) _inventory.ForgetSlot(worn);
        return new(GameSessionFailure.None, planned.Steps) { Messages = planned.Runs?.Messages ?? [] };
    }

    /// <summary>
    /// ARC-AI-1139: the AI gate's re-wield of a live NPC (0x4A8570): CHECK_WIELD calls the best-items wield of every slot (0x4654F0,
    /// <paramref name="allSlots"/>), CHECK_WEAPON alone the best item of the weapon slot (0x465170 with 1004), over the NPC's list as the session
    /// shows it now and the NPC's ammunition stacks, so a bow whose ammunition the NPC no longer carries is not chosen and goes to its pack (fists
    /// when no weapon qualifies). The same rules, policy and refusals as <see cref="WieldNpcBest"/> (the world is the host's restock world; none, or no wielder for the NPC: <see cref="GameSessionFailure.EquipmentUnavailable"/>). One revision and the NPC's worn
    /// record when a worn item changed, or one revision (the record untouched) when only the scripts of a step changed the session (ARC-MAGIC-1684), else <see cref="GameSessionDisposition.NoChange"/> with the NPC's record untouched.
    /// </summary>
    public EquipBestResult RewieldNpc(GameSessionVersion expected, string npc, bool allSlots, ISessionRestockWorld? world)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(npc)) return RejectBest(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectBest(GameSessionFailure.StaleVersion);
        if (world is null) return RejectBest(GameSessionFailure.EquipmentUnavailable);
        if (!NpcHolderSeed(npc, out var map, out var seed, out _)) return RejectBest(UnseededHolder());
        var planned = PlanNpcWield(world, map, seed, npc, allSlots ? Enum.GetValues<EquipmentSlot>() : [EquipmentSlot.Weapon]);
        if (planned.Failure != GameSessionFailure.None) return RejectBest(planned.Failure);
        // ARC-MAGIC-1684: the scripts of a step whose move was refused (both moves ran) may still have changed the session.
        var scripted = planned.Runs?.Changes == true;
        if (!planned.Changed && !scripted) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, planned.Steps, Version) { Messages = planned.Runs?.Messages ?? [] };
        if (_revision == long.MaxValue) return RejectBest(GameSessionFailure.RevisionExhausted);
        // A first record for an NPC the session never dressed counts against the saved budget (a save holds at most MaxNpcWorn, as the restock's wield checks).
        if (planned.Changed && !_inventory.NpcWorn.ContainsKey(npc) && _inventory.NpcWorn.Count >= SessionInventoryState.MaxNpcWorn) return RejectBest(GameSessionFailure.BudgetExceeded);
        planned.Runs?.Commit();
        if (planned.Changed)
        {
            _inventory.NpcWorn[npc] = planned.Record!;
            foreach (var worn in planned.Record!.Values) _inventory.ForgetSlot(worn);
        }
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, planned.Steps, Version) { Messages = planned.Runs?.Messages ?? [] };
    }

    // One NPC wield over <slots>, planned and not applied: the steps, the worn record it would write, whether a worn item differs from what the NPC wears now, and (ARC-MAGIC-1684) the runs of the item event scripts of its moves.
    private sealed record NpcWieldPlan(GameSessionFailure Failure, IReadOnlyList<SessionEquipBestStep> Steps, SortedDictionary<EquipmentSlot, string>? Record, bool Changed, ItemScriptRuns? Runs = null);

    // ARC-MAGIC-1684: the plan with the scripts of its moves (IE3-IE6) when the host gives their context; a script the session cannot apply gives up that item's scripts (the item is not moved, as before the scripts were run) and the plan
    // starts again, and one the whole plan cannot apply (the wearer's stats, the runs' changes) gives up all of them.
    private NpcWieldPlan PlanNpcWield(ISessionRestockWorld world, SessionMapId map, InventoryDomain.Seed seed, string npc, IReadOnlyList<EquipmentSlot> slots)
    {
        var given = world.NpcWieldScripts(map, npc);
        ThrowIfDisposed();
        var blocked = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            try { return PlanNpcWieldOnce(world, map, seed, npc, slots, given, blocked); }
            catch (BestPlan.ScriptBlocked item) when (given is not null) { if (!blocked.Add(item.Entity)) given = null; }
            catch (BestPlan.ScriptsRefused) when (given is not null) { given = null; }
        }
    }

    private NpcWieldPlan PlanNpcWieldOnce(ISessionRestockWorld world, SessionMapId map, InventoryDomain.Seed seed, string npc, IReadOnlyList<EquipmentSlot> slots,
        SessionNpcWieldScripts? scripts, HashSet<string> blocked)
    {
        if (world.RestockWielder(map, npc) is not { Wearer: not null, Chooser: not null, Items: not null, ArtCheck: not null } wielder)
            return new(GameSessionFailure.EquipmentUnavailable, [], null, false);
        if (!ValidChooser(wielder.Chooser)) return new(GameSessionFailure.InvalidEquipment, [], null, false);
        var current = _inventory.Current(seed, seed.Holders[npc], item => DroppedCell(map, seed, item)).ToArray();
        var insert = world.RestockInsertCheck(map, npc);
        var listed = current.ToDictionary(item => item.Entity, StringComparer.Ordinal);
        var chooser = wielder.Chooser with
        {
            IsPc = false,
            Ammunition = Enumerable.Range(0, SessionAmmoRecord.AmmoTypes).Select(type => (type, stack: NpcStackOfType(npc, type)))
                .Where(pair => pair.stack is not null).ToDictionary(pair => pair.type, pair => pair.stack!.Value.Units),
        };
        // An item a step takes off goes to the NPC's pack (the original's unwield to the grid).
        var plan = new BestPlan(this, [], wielder.Wearer, chooser, wielder.ArtCheck,
            worn => insert(new(worn.Entity, listed[worn.Entity].Facts with { InventoryLocation = -1 })));
        var before = new SortedDictionary<EquipmentSlot, string>();
        try
        {
            foreach (var item in current)
            {
                var facts = wielder.Items(item);
                plan.Fresh();
                if (facts is null)
                {
                    if (item.Facts.Worn) throw new BestPlan.Refused(GameSessionFailure.InventoryMismatch);
                    continue;
                }
                if (!ValidWearable(facts) || facts.Weight is < 0 or > EquipmentChoice.MaxWeight) throw new BestPlan.Refused(GameSessionFailure.InvalidItem);
                facts = Conditioned(item.Entity, facts);
                if (facts.Kind != item.Facts.Kind || !EquipmentDomain.Matches(facts.Kind.Type, facts.Item.Kind)) throw new BestPlan.Refused(GameSessionFailure.InventoryMismatch);
                plan.Facts[item.Entity] = facts;
                plan.Held.Add(item.Entity);
                if (!item.Facts.Worn) continue;
                // A worn item sits in its own wear slot (a ring in either ring slot), one per slot.
                var slot = (EquipmentSlot)item.Facts.InventoryLocation;
                if (EquipmentRules.WearSlot(facts.Item) is not { } own || own != slot && !(own is EquipmentSlot.Ring1 or EquipmentSlot.Ring2 && slot is EquipmentSlot.Ring1 or EquipmentSlot.Ring2) ||
                    plan.Worn.ContainsKey(slot)) throw new BestPlan.Refused(GameSessionFailure.InventoryMismatch);
                plan.Worn[slot] = new(slot, item.Entity, facts.Item)
                {
                    ScriptPoints = EquipmentDomain.MovePoints(facts.ScriptPoints),
                    ItemSpells = facts.HasItemSpells,
                };
                before[slot] = item.Entity;
            }
            // ARC-MAGIC-1684: the runs of the moves' item event scripts, the NPC as their triggerer; only a plan with an item that has a move script makes any.
            if (scripts is not null && plan.Facts.Values.Any(facts => EquipmentDomain.MoveScripts.Any(facts.ScriptPoints.Contains)))
            {
                var items = plan.Facts.ToDictionary(pair => pair.Key, pair => pair.Value.Item.MagickTechComplexity, StringComparer.Ordinal);
                var runs = new ItemScriptRuns(this, scripts.Host, map.RuntimeMap, items, new NpcWearer(npc, scripts.Target, NpcStatsOverSheet(npc, scripts.Stored))) { NpcTriggerer = true };
                if (runs.ChooserReads() is not { } reads) throw new BestPlan.ScriptsRefused();
                plan.UseRuns(runs, npc, blocked, reads);
            }
            foreach (var slot in slots) WieldBest(plan, slot);
            plan.Fresh();
            if (plan.Runs is { } made)
            {
                made.Prepare();
                if (made.Failure != GameSessionFailure.None) throw new BestPlan.ScriptsRefused();
            }
        }
        catch (BestPlan.Refused refused) { return new(refused.Failure, [], null, false); }
        var record = new SortedDictionary<EquipmentSlot, string>();
        foreach (var (slot, worn) in plan.Worn) record[slot] = worn.Entity;
        var changed = record.Count != before.Count || record.Any(pair => !before.TryGetValue(pair.Key, out var entity) || entity != pair.Value);
        return new(GameSessionFailure.None, Array.AsReadOnly(plan.Steps.ToArray()), record, changed, plan.Runs);
    }

    // The bounds the choice's weapon score reads: skill levels 0..20, the aptitude's range, a damage bonus the combat rules admit.
    private static bool ValidChooser(EquipmentChooser chooser) =>
        new[] { chooser.MeleeLevel, chooser.BowLevel, chooser.ThrowingLevel, chooser.FirearmsLevel }.All(level => level is >= 0 and <= Arcanum.Simulation.Combat.AttackRules.MaxSkillLevel) &&
        chooser.MagickTechAptitude is >= EquipmentLimits.MinAptitude and <= EquipmentLimits.MaxAptitude &&
        Math.Abs((long)chooser.DamageBonus) <= Arcanum.Simulation.Combat.CombatSkillRules.MaxValue && Enum.IsDefined(chooser.Difficulty);
}
