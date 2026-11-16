using Arcanum.Simulation;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;

namespace Arcanum.Session;

/// <summary>One item whose durability the session changed: its entity and its whole condition.</summary>
public readonly record struct SessionItemDurability(string Entity, ItemCondition Condition);

/// <summary>One item whose remaining uses the session changed (ARC-SKILLS-005: a healing item's charges, field 0xD5).</summary>
public readonly record struct SessionItemUses(string Entity, int Uses);

/// <summary>One item whose spell charges (field 0x69) the session spent (ARC-MAGIC-022): the charges left. The payment that spends a
/// charge also sets the item's magical flag (0x4, ledger DV1), so a row means that flag is set too.</summary>
public readonly record struct SessionItemCharges(string Entity, int Charges);

/// <summary>One item whose mana store (field 92, <c>obj_f_item_mana_store</c>) the session changed (ARC-MAGIC-2371, schema 103): the store now. A spell
/// payment drains a worn item's store and a recharge event (kind 0x17) restores it, with no cap at the prototype's value (ledger BC10).</summary>
public readonly record struct SessionItemManaStore(string Entity, int Store);

/// <summary>
/// Saved durability (ARC-WORLD-053, schema 22): the condition of every item the session damaged, broke or repaired, by entity
/// in ordinal order, and the items it deleted (a second break or a destroying critical), in ordinal order. An item without a
/// record has its host's baseline. Since ARC-SKILLS-005 also the remaining uses of every item whose uses the session spent
/// (<see cref="Uses"/>, in ordinal order; at most <see cref="SessionItemUseLimits.MaxUses"/> each), and since ARC-MAGIC-022 (schema 49)
/// the spell charges left of every item whose charges a spell payment spent (<see cref="Charges"/>, in ordinal order; 0 up to
/// <see cref="SessionItemUseLimits.MaxUses"/> each), and since ARC-MAGIC-2371 (schema 103) the mana store of every item a payment drained or a
/// recharge restored (<see cref="ManaStores"/>, in ordinal order; any 32-bit value each since ARC-MAGIC-2401, as the original's field holds one).
/// </summary>
public sealed class SessionDurabilityState
{
    internal SessionDurabilityState(SessionItemDurability[] items, string[] destroyed, SessionItemUses[]? uses = null, SessionItemCharges[]? charges = null,
        SessionItemManaStore[]? manaStores = null)
    {
        Items = Array.AsReadOnly(items);
        Destroyed = Array.AsReadOnly(destroyed);
        Uses = Array.AsReadOnly(uses ?? []);
        Charges = Array.AsReadOnly(charges ?? []);
        ManaStores = Array.AsReadOnly(manaStores ?? []);
    }

    public static SessionDurabilityState Empty { get; } = new([], []);

    public IReadOnlyList<SessionItemDurability> Items { get; }
    public IReadOnlyList<string> Destroyed { get; }
    public IReadOnlyList<SessionItemUses> Uses { get; }
    public IReadOnlyList<SessionItemCharges> Charges { get; }
    public IReadOnlyList<SessionItemManaStore> ManaStores { get; }
}

/// <summary>Bounds of an item's remaining uses (project policy; the source's field is a 32-bit count).</summary>
public static class SessionItemUseLimits
{
    public const int MaxUses = 1_000_000;
}

/// <summary>
/// Outcome of a durability call: the item's condition afterwards (null when it was deleted), the break step it reached, and
/// the hero's wear slot it left (a first break or a deletion of an item the hero wore). For an item an NPC wears, taking it off on a break is
/// the host's (<see cref="ItemDamageOutcome.TakeOffAndRunBust"/>); the BUST script (point 30) and the sounds are the host's
/// in both cases.
/// </summary>
public readonly record struct ItemDurabilityResult(GameSessionDisposition Disposition, GameSessionFailure Failure, ItemCondition? Condition,
    ItemBreakStep Break, EquipmentSlot? Unworn, GameSessionVersion Version)
{
    /// <summary>The repair rule's status, for a repair call.</summary>
    public ItemRepairStatus? Repair { get; init; }
}

/// <summary>The bridge from stored fields (ARC-DATA-021) to an item's durability baseline: hp_pts, hp_adj, hp_damage and the
/// destroyed bit of the current and the inventory art, as the item's data holds them (a prototype, or an instance over it).</summary>
public static class SessionItemConditions
{
    /// <summary>The baseline of an item; null for an object that is not an item or values outside the rules' bounds.</summary>
    public static ItemCondition? FromCombatFields(Arcanum.Data.Combat.CombatFields fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if (fields.Item is not { } item) return null;
        var common = fields.Object;
        var condition = new ItemCondition(common.HitPoints, common.HitPointAdjustment, common.HitPointDamage,
            Arcanum.Data.Combat.CombatItemArt.Destroyed(common.CurrentArt), Arcanum.Data.Combat.CombatItemArt.Destroyed(item.InventoryArt));
        return ItemDurabilityRules.IsValid(condition) ? condition : null;
    }
}

/// <summary>The durability domain of one owner: changed conditions and deleted items, by entity (together at most
/// <see cref="GameSessionLimits.MaxDurabilityRecords"/>).</summary>
internal sealed class DurabilityDomain
{
    internal SortedDictionary<string, ItemCondition> Records { get; } = new(StringComparer.Ordinal);
    internal SortedSet<string> Destroyed { get; } = new(StringComparer.Ordinal);
    internal SortedDictionary<string, int> Uses { get; } = new(StringComparer.Ordinal);
    internal SortedDictionary<string, int> Charges { get; } = new(StringComparer.Ordinal);
    internal SortedDictionary<string, int> ManaStores { get; } = new(StringComparer.Ordinal);

    internal int Count => Records.Count + Destroyed.Count + Uses.Count + Charges.Count + ManaStores.Count;

    internal SessionDurabilityState Capture() =>
        new([.. Records.Select(pair => new SessionItemDurability(pair.Key, pair.Value))], [.. Destroyed],
            [.. Uses.Select(pair => new SessionItemUses(pair.Key, pair.Value))], [.. Charges.Select(pair => new SessionItemCharges(pair.Key, pair.Value))],
            [.. ManaStores.Select(pair => new SessionItemManaStore(pair.Key, pair.Value))]);
}

/// <summary>
/// ARC-WORLD-053: the item durability owner of every item the session tracks: the hero's starting kit, the items the PC took,
/// and the items of every attached map seed (any map, any holder or the ground). An item's condition is its host's baseline
/// (<see cref="SessionItemConditions.FromCombatFields"/>) until the session damages, breaks or repairs it; from then on the
/// session's record holds it, saved in <see cref="GameSessionSnapshot.Durability"/>. The rules are
/// <see cref="ItemDurabilityRules"/>. Combat calls them through the attack's wear (<see cref="ApplyAttackWear"/>, ARC-COMBAT-007).
/// </summary>
public sealed partial class GameSessionOwner
{
    private DurabilityDomain _durability = new();

    /// <summary>The item's current condition: the session's record, or <paramref name="baseline"/> when it has none; null for an
    /// item the session deleted. Throws for an invalid entity or baseline.</summary>
    public ItemCondition? QueryItemCondition(string entity, ItemCondition baseline)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(entity) || !ItemDurabilityRules.IsValid(baseline)) throw new ArgumentException("Invalid item or baseline.", nameof(entity));
        if (_durability.Destroyed.Contains(entity)) return null;
        return _durability.Records.TryGetValue(entity, out var recorded) ? recorded : baseline;
    }

    /// <summary>Whether the session deleted the item.</summary>
    public bool IsItemDestroyed(string entity) { ThrowIfDisposed(); return _durability.Destroyed.Contains(entity); }

    /// <summary>Detached copy of the saved durability records.</summary>
    public SessionDurabilityState QueryDurability() { ThrowIfDisposed(); return _durability.Capture(); }

    /// <summary>
    /// One damage step (<see cref="ItemDurabilityRules.AddDamage"/>) on an item the session tracks, as combat damage on an item
    /// and armour and weapon wear add it: the damage grows by <paramref name="amount"/>, and at 0 hit points or less the item
    /// breaks (a first break takes it off the hero's slot when the hero wears it) or, already broken, is deleted. Refused for an
    /// invalid input (<see cref="GameSessionFailure.InvalidItem"/>), a stale version, an item the session does not track or
    /// deleted (<see cref="GameSessionFailure.ItemNotAvailable"/>) and a full record budget. Amount 0 that breaks nothing is
    /// NoChange. One revision.
    /// </summary>
    public ItemDurabilityResult DamageItem(GameSessionVersion expected, string entity, ItemCondition baseline, int amount)
    {
        ThrowIfDisposed();
        if (amount is < 0 or > ItemDurabilityLimits.MaxDamageStep) return RejectDurability(GameSessionFailure.InvalidItem);
        return DurabilityWrite(expected, entity, baseline, current => ItemDurabilityRules.AddDamage(current, amount));
    }

    /// <summary>The break (<see cref="ItemDurabilityRules.Break"/>) of an item the session tracks, as a script or a critical
    /// effect breaks it: first broken, then deleted. Refusals as <see cref="DamageItem"/>. One revision.</summary>
    public ItemDurabilityResult BreakItem(GameSessionVersion expected, string entity, ItemCondition baseline)
    {
        ThrowIfDisposed();
        return DurabilityWrite(expected, entity, baseline, ItemDurabilityRules.Break);
    }

    /// <summary>Deletes an item the session tracks at once, without a break (the "destroy weapon" critical, combat core C21).
    /// Refusals as <see cref="DamageItem"/>. One revision.</summary>
    public ItemDurabilityResult DestroyItem(GameSessionVersion expected, string entity, ItemCondition baseline)
    {
        ThrowIfDisposed();
        return DurabilityWrite(expected, entity, baseline, current => new(current, ItemBreakStep.Deleted));
    }

    /// <summary>
    /// The item side of a repair (<see cref="ItemDurabilityRules.Repair"/>) with the amount and the hp_adj share the Repair
    /// owner computed (<see cref="ItemDurabilityRules.RepairAmount"/>, <see cref="ItemDurabilityRules.RepairAdjustmentPercent"/>).
    /// A repair the rule does not make (nothing to repair, a broken item and no master) is NoChange with its status in
    /// <see cref="ItemDurabilityResult.Repair"/>. Refusals as <see cref="DamageItem"/>, and an amount below 1 or a share outside
    /// 0..100 (<see cref="GameSessionFailure.InvalidItem"/>). One revision.
    /// </summary>
    public ItemDurabilityResult RepairItem(GameSessionVersion expected, string entity, ItemCondition baseline, int amount, int adjustmentPercent,
        RepairTraining repairer)
    {
        ThrowIfDisposed();
        if (amount < 1 || adjustmentPercent is < 0 or > 100 || !Enum.IsDefined(repairer)) return RejectDurability(GameSessionFailure.InvalidItem);
        ItemRepairStatus? status = null;
        var result = DurabilityWrite(expected, entity, baseline, current =>
        {
            var repaired = ItemDurabilityRules.Repair(current, amount, adjustmentPercent, repairer);
            status = repaired.Status;
            return new(repaired.Condition, ItemBreakStep.None);
        });
        return result with { Repair = status };
    }

    /// <summary>The wear values of an item with the session's record of its condition: the broken bit and the hit points the wear
    /// check and the best-item choice read (a deleted item is not held, so it never reaches here).</summary>
    internal SessionWearableItem Conditioned(string entity, SessionWearableItem item) =>
        _durability.Records.TryGetValue(entity, out var condition) ? item with { Item = Conditioned(item.Item, condition) } : item;

    private static EquipmentItem Conditioned(EquipmentItem item, ItemCondition condition)
    {
        var maximum = Math.Clamp(condition.MaximumHitPoints, 0, EquipmentLimits.MaxHitPoints);
        return item with { Broken = condition.Broken, MaxHitPoints = maximum, HitPoints = Math.Clamp(condition.HitPoints, 0, maximum) };
    }

    private ItemDurabilityResult DurabilityWrite(GameSessionVersion expected, string entity, ItemCondition baseline,
        Func<ItemCondition, ItemDamageOutcome> rule)
    {
        if (!SessionValidation.Entity(entity) || !ItemDurabilityRules.IsValid(baseline)) return RejectDurability(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectDurability(GameSessionFailure.StaleVersion);
        if (!TracksItem(entity)) return RejectDurability(GameSessionFailure.ItemNotAvailable);
        var current = _durability.Records.TryGetValue(entity, out var recorded) ? recorded : baseline;
        var outcome = rule(current);
        if (outcome.Break == ItemBreakStep.None && outcome.Condition == current)
            return new(GameSessionDisposition.NoChange, GameSessionFailure.None, current, ItemBreakStep.None, null, Version);
        if (!ItemDurabilityRules.IsValid(outcome.Condition)) return RejectDurability(GameSessionFailure.InvalidItem);
        if (!_durability.Records.ContainsKey(entity) && _durability.Count >= _limits.MaxDurabilityRecords)
            return RejectDurability(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return RejectDurability(GameSessionFailure.RevisionExhausted);

        var slot = _equipment.SlotOf(entity);
        if (outcome.Break == ItemBreakStep.Deleted)
        {
            DestroyObject(entity);
            Revise();
            return new(GameSessionDisposition.Changed, GameSessionFailure.None, null, ItemBreakStep.Deleted, slot, Version);
        }
        _durability.Records[entity] = outcome.Condition;
        EquipmentSlot? unworn = null;
        if (slot is { } worn)
        {
            // A first break takes the item off (into the hero's inventory, where it already counts); otherwise the worn record
            // follows the condition.
            if (outcome.Break == ItemBreakStep.Broken) { _equipment.Worn.Remove(worn); unworn = worn; }
            else _equipment.Worn[worn] = _equipment.Worn[worn] with { Item = Conditioned(_equipment.Worn[worn].Item, outcome.Condition) };
        }
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, outcome.Condition, outcome.Break, unworn, Version);
    }

    /// <summary>An item of the hero's kit, one the PC took or made, a made item added to an NPC (ARC-WORLD-055), or one an attached seed
    /// lists, and not deleted.</summary>
    private bool TracksItem(string entity) =>
        !_durability.Destroyed.Contains(entity) && (_inventory.Hero.InPack(entity) || _inventory.PlacedItem(entity) is not null || _inventory.NpcOf(entity) is not null ||
            _inventory.Seeds.Values.Any(seed => seed.Items.ContainsKey(entity)));

    private ItemDurabilityResult RejectDurability(GameSessionFailure failure) =>
        new(GameSessionDisposition.Rejected, failure, null, ItemBreakStep.None, null, Version);

    // Records by entity, deleted items apart; ranges are the rules' bounds. The kit and the PC's lists are checked against the
    // deleted items by ImportInventory, worn items against the records by ImportWornConditions.
    private static GameSessionFailure ImportDurability(SessionDurabilityState saved, GameSessionLimits limits, out DurabilityDomain durability)
    {
        durability = new();
        if (saved.Items.Count + saved.Destroyed.Count + saved.Uses.Count + saved.Charges.Count + saved.ManaStores.Count > limits.MaxDurabilityRecords) return GameSessionFailure.BudgetExceeded;
        foreach (var entity in saved.Destroyed)
            if (!SessionValidation.Entity(entity) || !durability.Destroyed.Add(entity)) return GameSessionFailure.InvalidDurability;
        foreach (var item in saved.Items)
        {
            if (!SessionValidation.Entity(item.Entity) || !ItemDurabilityRules.IsValid(item.Condition) || durability.Destroyed.Contains(item.Entity) ||
                !durability.Records.TryAdd(item.Entity, item.Condition)) return GameSessionFailure.InvalidDurability;
        }
        foreach (var item in saved.Uses)
            if (!SessionValidation.Entity(item.Entity) || item.Uses is < 1 or > SessionItemUseLimits.MaxUses || durability.Destroyed.Contains(item.Entity) ||
                !durability.Uses.TryAdd(item.Entity, item.Uses)) return GameSessionFailure.InvalidDurability;
        foreach (var item in saved.Charges)
            if (!SessionValidation.Entity(item.Entity) || item.Charges is < 0 or > SessionItemUseLimits.MaxUses || durability.Destroyed.Contains(item.Entity) ||
                !durability.Charges.TryAdd(item.Entity, item.Charges)) return GameSessionFailure.InvalidDurability;
        foreach (var item in saved.ManaStores)
            if (!SessionValidation.Entity(item.Entity) || durability.Destroyed.Contains(item.Entity) ||
                !durability.ManaStores.TryAdd(item.Entity, item.Store)) return GameSessionFailure.InvalidDurability;
        return GameSessionFailure.None;
    }

    // A worn item with a record is not broken (a break takes it off) and carries the record's hit points.
    private static GameSessionFailure ImportWornConditions(EquipmentDomain equipment, DurabilityDomain durability)
    {
        foreach (var worn in equipment.Worn.Values)
            if (durability.Records.TryGetValue(worn.Entity, out var condition) && (condition.Broken || worn.Item != Conditioned(worn.Item, condition)))
                return GameSessionFailure.InvalidDurability;
        return GameSessionFailure.None;
    }
}
