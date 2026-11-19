using Arcanum.Simulation;
using Arcanum.Simulation.Equipment;

namespace Arcanum.Session;

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-WORLD-1652 (ledger GS5, the copy 0x4EF920 → 0x43CC30 → 0x4421A0 → 0x406010): a monster generator's copy is the whole generator object, so it holds a fresh copy of every item the generator holds. 0x406010 reads the
    /// generator's inventory list (field 0xED/0xEE for a critter) and copies each item the same way, index by index, so the copy's list has the generator's order; an item is never a holder, so the recursion into a container
    /// (field 0x3B/0x3C, type 2) copies nothing here (the generator is a critter and a critter's list holds items only). Each copy is a new made item (<c>made:n</c>) of the same kind in the copy's list, with the facts the original's copy
    /// carries and the session keeps for a made item: its weight facts (weight, magic weight, complexity), the coins of a gold pile, the type and units of an ammunition stack, and the slot it is worn in (a worn item is worn by the
    /// copy: the copy's worn record is the generator's). The raw field-array store 0x4077b0 (then the item's parent field 0x58 by 0x406F20) runs no insert check and no transfer script. The restock of the copy's inventory source runs after the copy (0x442260, RK16), as for any created critter.
    /// Limits: a key or key ring is not copied (the lists do not admit them, <see cref="InventoryDomain.Admitted"/>); an item beyond the list, store or number budgets ends the copy there (the original has no bound); item fields the
    /// session keeps no record of (an instance override of the item's own content fields, scripts or flags of the instance) are the prototype's: the census of the installed generators (ARC-WORLD-1707, ledger GS5) finds one such override, the
    /// armor class (slot 25 of one armor, which no reader asks of an item), and no item flag the session reads (0x10, 0x20, 0x2000, 0x20000). What the session did record for the item travels (<see cref="CopyItemRecords"/>). A made
    /// item of the generator's list (restocked, scripted) keeps its tracked weight facts. Part of the caller's revision. Returns the items copied.
    /// </summary>
    [OriginalFunction(0x406010, OriginalFunctionScope.Partial)]
    private int CopyGeneratorItems(SessionMapId map, string generator, string copy)
    {
        if (!_inventory.Seeds.TryGetValue(map, out var seed) || !seed.Holders.TryGetValue(generator, out var holder) || holder.Kind != SessionHolderKind.Npc) return 0;
        var worn = new SortedDictionary<EquipmentSlot, string>();
        var copied = 0;
        foreach (var source in _inventory.Current(seed, holder, entity => DroppedCell(map, seed, entity)).ToArray())
        {
            var facts = source.Facts;
            if (!InventoryDomain.Admitted(facts.Kind.Type) && facts.Kind.Type != SessionItemType.Gold) continue;
            var ammo = facts.Kind.Type == SessionItemType.Ammo;
            var gold = facts.Kind.Type == SessionItemType.Gold;
            if (ammo && (facts.AmmoType is not { } type || type is < 0 or >= SessionAmmoRecord.AmmoTypes || facts.Quantity is not >= 0)) continue;
            if (gold && facts.Quantity is not >= 1) continue;
            if (_inventory.AddedTo(copy).Count + SeedItemCount(copy) >= SessionMapInventorySeed.MaxListLength || _inventory.NpcHeld >= _limits.MaxNpcItems ||
                ammo && _inventory.Ammo.Count >= _limits.MaxAmmoRecords || gold && _inventory.Piles.Count >= _limits.MaxGoldRecords) break;
            if (NextObjectEntity(SessionObjectOrigin.Made) is not { } entity) break;
            var made = CreateMadeItem(new(entity, facts.Kind, SessionCreatePlace.NpcList, _inventory.Weights.TryGetValue(source.Entity, out var tracked) ? tracked : facts.WeightFacts, facts.Complexity, Holder: copy));
            if (ammo) _inventory.Ammo[made.Entity] = new(made.Entity, facts.AmmoType!.Value, facts.Quantity!.Value);
            if (gold) _inventory.Piles[made.Entity] = facts.Quantity!.Value;
            CopyItemRecords(source.Entity, made.Entity);
            if (facts.Worn && Enum.IsDefined((EquipmentSlot)facts.InventoryLocation)) worn[(EquipmentSlot)facts.InventoryLocation] = made.Entity;
            copied++;
        }
        if (worn.Count > 0 && (_inventory.NpcWorn.Count < SessionInventoryState.MaxNpcWorn)) _inventory.NpcWorn[copy] = worn;
        return copied;
    }

    /// <summary>
    /// ARC-WORLD-1707 (ledger GS5): the original's copy of the generator object carries each item object whole, so what the session recorded for the generator's item travels to its copy: the identified mark, the fields scripts wrote (written start, display
    /// bit), the kept item script slots, the condition, the uses, the spent charges and the mana store. Each record is copied only inside its own budget (a full store leaves the copy without it, as the item budget ends the copy
    /// above). Not copied: a pending item recharge event of a mana store (the copy's store recharges only when a payment or recharge writes it again) and the object-state records of an item with a map row (art number, off flag, name
    /// number): a made item has no row to keep them on. Installed data never reaches these records (generators neither fight nor run item scripts), so this is for sessions that wrote one.
    /// </summary>
    private void CopyItemRecords(string from, string to)
    {
        if (_inventory.Identified.Contains(from) && _inventory.Identified.Count < SessionInventoryState.MaxIdentified) _inventory.Identified.Add(to);
        if (_inventory.ItemFields.TryGetValue(from, out var written) && _inventory.ItemFields.Count < ItemFieldRowLimit) _inventory.ItemFields[to] = written;
        foreach (var (key, slot) in _inventory.ScriptSlots.Where(pair => pair.Key.Entity == from).OrderBy(pair => pair.Key.Point).ToArray())
            if (_inventory.ScriptSlots.Count < SessionItemScripts.MaxStoredSlots) _inventory.ScriptSlots[(to, key.Point)] = slot;
        if (_durability.Records.TryGetValue(from, out var condition) && _durability.Count < _limits.MaxDurabilityRecords) _durability.Records[to] = condition;
        if (_durability.Uses.TryGetValue(from, out var uses) && _durability.Count < _limits.MaxDurabilityRecords) _durability.Uses[to] = uses;
        if (_durability.Charges.TryGetValue(from, out var charges) && _durability.Charges.Count < _limits.MaxItemCharges) _durability.Charges[to] = charges;
        if (_durability.ManaStores.TryGetValue(from, out var store) && _durability.ManaStores.Count < _limits.MaxManaStores) _durability.ManaStores[to] = store;
    }
}
