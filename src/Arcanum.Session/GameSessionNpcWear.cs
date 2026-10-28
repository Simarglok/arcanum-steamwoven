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
public sealed record SessionNpcWield(GameSessionFailure Failure, IReadOnlyList<SessionEquipBestStep> Steps);

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-WORLD-103: the worn items of an NPC the session dressed (a regeneration's best-items wield) that are still in its list, in slot
    /// order; null when the session never dressed it, so the inventory locations of its map data stand. The session's lists of the NPC
    /// (<see cref="QueryMapInventory"/>) already show these locations.
    /// </summary>
    public SessionNpcWear? QueryNpcWorn(string npc)
    {
        ThrowIfDisposed();
        if (npc is null || !_inventory.NpcWorn.TryGetValue(npc, out var worn)) return null;
        return new(npc, Array.AsReadOnly(worn.Where(pair => _inventory.NpcOf(pair.Value) == npc || SeedListing(npc, pair.Value) is not null)
            .Select(pair => new SessionNpcWornItem(pair.Key, pair.Value)).ToArray()));
    }

    // ARC-WORLD-103: whether an item of the NPC's list is worn, as the session's lists show it (a dressed NPC's record, else the map data).
    private bool NpcWears(string npc, string item, SessionItemFacts facts) => _inventory.Worn(npc, item, facts).Worn;

    /// <summary>
    /// ARC-WORLD-103: the NPC's best-items wield at the end of its regeneration (0x463E20 calls 0x4654F0 for an NPC, type 16, after the
    /// lines, without a target, ledger RK15): for each slot from the helmet (1000) to the boots (1008) the rules of
    /// <see cref="EquipBest"/> (WB1–WB8) over the NPC's list in its order, starting from what it wears after the clear (its persistent
    /// items keep their slots). Its insert check takes an item a step takes off. A choice with a move script or item spells is not
    /// wielded (the session runs none; the original would), as for the hero. On success the NPC's worn items are recorded (the record then
    /// replaces its map data's inventory locations); on a refusal nothing is recorded. Part of the regeneration's one revision.
    /// </summary>
    private SessionNpcWield WieldNpcBest(ISessionRestockWorld world, SessionMapId map, InventoryDomain.Seed seed, string npc)
    {
        if (world.RestockWielder(map, npc) is not { Wearer: not null, Chooser: not null, Items: not null, ArtCheck: not null } wielder)
            return new(GameSessionFailure.EquipmentUnavailable, []);
        if (!ValidChooser(wielder.Chooser)) return new(GameSessionFailure.InvalidEquipment, []);
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
            }
            foreach (var slot in Enum.GetValues<EquipmentSlot>()) WieldBest(plan, slot);
        }
        catch (BestPlan.Refused refused) { return new(refused.Failure, []); }
        var record = new SortedDictionary<EquipmentSlot, string>();
        foreach (var (slot, worn) in plan.Worn) record[slot] = worn.Entity;
        _inventory.NpcWorn[npc] = record;
        return new(GameSessionFailure.None, Array.AsReadOnly(plan.Steps.ToArray()));
    }

    // The bounds the choice's weapon score reads: skill levels 0..20, the aptitude's range, a damage bonus the combat rules admit.
    private static bool ValidChooser(EquipmentChooser chooser) =>
        new[] { chooser.MeleeLevel, chooser.BowLevel, chooser.ThrowingLevel, chooser.FirearmsLevel }.All(level => level is >= 0 and <= Arcanum.Simulation.Combat.AttackRules.MaxSkillLevel) &&
        chooser.MagickTechAptitude is >= EquipmentLimits.MinAptitude and <= EquipmentLimits.MaxAptitude &&
        Math.Abs((long)chooser.DamageBonus) <= Arcanum.Simulation.Combat.CombatSkillRules.MaxValue && Enum.IsDefined(chooser.Difficulty);
}
