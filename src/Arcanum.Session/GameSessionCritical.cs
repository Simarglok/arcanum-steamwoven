using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Randomness;
using Arcanum.Simulation.Movement;

namespace Arcanum.Session;

/// <summary>What a critical effect does to an item (ARC-COMBAT-009), in the original's order (0x4B4390).</summary>
public enum SessionCriticalStepKind
{
    /// <summary>A crippling arm hit drops the wielded weapon when the wear check (0x464D20) refuses it under the new injuries.</summary>
    ArmHitWeapon,
    /// <summary>The same for the shield slot.</summary>
    ArmHitShield,
    /// <summary>The "drop helmet" critical.</summary>
    DropHelmet,
    /// <summary>The "drop weapon" critical, after the weapon's damage and destruction.</summary>
    DropWeapon,
    /// <summary>The "destroy ammunition" critical miss: all the holder's ammunition of the weapon's type.</summary>
    DestroyAmmo,
    /// <summary>The "lose ammunition" critical miss: five shots' worth.</summary>
    LoseAmmo,
}

/// <summary>How a critical item step ended.</summary>
public enum SessionCriticalStepOutcome
{
    /// <summary>The item now lies on the ground at <see cref="SessionCriticalStep.Cell"/>: an NPC's item of a map seed (a map change), or
    /// since ARC-WORLD-059 the hero's (its placed items, <see cref="GameSessionOwner.QueryPlacedItems"/>).</summary>
    Dropped,
    /// <summary>The wear check still admits the item: an arm hit does not drop it.</summary>
    Kept,
    /// <summary>The slot is empty, or an earlier step of this attack deleted or dropped the item.</summary>
    NoItem,
    /// <summary>The item has the no-drop flag: the original's drop refuses it (0x466DA0).</summary>
    Refused,
    /// <summary>Not applied: the hero's item cannot lie on its cell (outside the drop's 24-bit packing).</summary>
    HeroCellUnfit,
    /// <summary>Not applied, NOT READY: an item the session added to an NPC (ARC-WORLD-055) lies in no map seed, so it cannot go to
    /// the ground before that store exists.</summary>
    NotSeedItem,
    /// <summary>Not applied: the item has a drop script, which the session does not run (the original runs it and may veto).</summary>
    ScriptUnsupported,
    /// <summary>Not applied: the host passed no item data for the critter (<see cref="SessionWearSide.Worn"/> null) or no cell.</summary>
    Unknown,
    /// <summary>Not applied, NOT READY: no ammunition is modelled (FR-COMBAT-007).</summary>
    AmmunitionNotReady,
}

/// <summary>
/// One critical item step of an attack: what it was, whose (the critter that took the damage), the slot and item, how it ended and,
/// for a drop, the cell. The original places a dropped item 2..4 cells away with draws of its own (0x461A90, 0x4F4E40); that
/// placement and its draws are NOT READY, so the item lies on the holder's cell and the session generator does not draw for it.
/// </summary>
public sealed record SessionCriticalStep(SessionCriticalStepKind Kind, DamageRecipient Whose, EquipmentSlot? Slot, string? Entity,
    SessionCriticalStepOutcome Outcome, CellCoord? Cell);

public sealed partial class GameSessionOwner
{
    /// <summary>The map change of an NPC's seed item a critical dropped (ARC-COMBAT-009): its cell, each coordinate in 24 bits, + 1.
    /// The snapshot already carries map changes, so drops add no schema.</summary>
    public const string ItemDroppedProperty = "item.dropped";

    /// <summary>The cell of an NPC's seed item a critical dropped, or null.</summary>
    internal CellCoord? DroppedCell(SessionMapId map, InventoryDomain.Seed seed, string item)
    {
        if (!seed.HolderOf.TryGetValue(item, out var holder) || seed.Holders[holder].Kind != SessionHolderKind.Npc) return null;
        if (!_maps.TryGetValue(map, out var changes) || !changes.TryGetValue(MapChangeKey.ForEntity(item, ItemDroppedProperty), out var packed) ||
            packed <= 0) return null;
        return new CellCoord((int)((packed - 1) & 0xFFFFFF), (int)(((packed - 1) >> 24) & 0xFFFFFF));
    }

    /// <summary>Whether a critical dropped the NPC's seed item on any attached map: it left the NPC's list.</summary>
    internal bool Dropped(string item) => _inventory.Seeds.Any(pair => DroppedCell(pair.Key, pair.Value, item) is not null);

    /// <summary>Where a seed item lies loose on the ground: its own cell, its spilled container's (ARC-COMBAT-008) or the cell a
    /// critical dropped it on; null while a holder holds it.</summary>
    internal CellCoord? LooseCell(SessionMapId map, InventoryDomain.Seed seed, string item) =>
        seed.Items.TryGetValue(item, out var row) ? row.Cell ?? SpilledCell(map, seed, item) ?? DroppedCell(map, seed, item) : null;

    private static long DroppedValue(CellCoord cell) => ((long)cell.Y << 24 | (uint)cell.X) + 1;

    /// <summary>A cell the drop's packing holds: each coordinate 0 .. 2^24 − 1.</summary>
    internal static bool DropCellFits(CellCoord cell) => cell.X is >= 0 and < 1 << 24 && cell.Y is >= 0 and < 1 << 24;

    // An item an earlier step deleted or a critical dropped is no longer worn.
    private bool Gone(string item) => _durability.Destroyed.Contains(item) || Dropped(item);

    /// <summary>
    /// The arm-hit drops of a critter that a crippling arm hit reached (<see cref="LifeEvents.ArmHitDropsItems"/>): the wielded
    /// weapon, then the shield, each when the wear check refuses it under the critter's injuries now. Returns the side without the
    /// items that left it, for the wear steps that follow.
    /// </summary>
    private SessionWearSide ArmHitDrops(DamageRecipient whose, SessionWearSide side, SessionCritterCombat? npc, CellCoord? cell, IDropMap? dropMap,
        List<SessionCriticalStep> steps, ref GameSessionFailure failure)
    {
        foreach (var (kind, slot) in new[] { (SessionCriticalStepKind.ArmHitWeapon, EquipmentSlot.Weapon), (SessionCriticalStepKind.ArmHitShield, EquipmentSlot.Shield) })
        {
            if (failure != GameSessionFailure.None) break;
            var held = side.Worn?.GetValueOrDefault(slot);
            if (held is null || Gone(held.Entity))
            {
                steps.Add(new(kind, whose, slot, held?.Entity, side.Worn is null ? SessionCriticalStepOutcome.Unknown : SessionCriticalStepOutcome.NoItem, null));
                continue;
            }
            // Without the item's wear values (an NPC item its map data does not list) the check cannot run.
            if (WearRefusal(side, npc, slot) is not { } refusal) { steps.Add(new(kind, whose, slot, held.Entity, SessionCriticalStepOutcome.Unknown, null)); continue; }
            if (refusal == EquipmentRefusal.None) { steps.Add(new(kind, whose, slot, held.Entity, SessionCriticalStepOutcome.Kept, null)); continue; }
            side = Drop(kind, whose, side, slot, cell, dropMap, steps, ref failure);
        }
        return side;
    }

    /// <summary>The "drop helmet" or "drop weapon" critical on the critter's item in <paramref name="slot"/>.</summary>
    private SessionWearSide CriticalDrop(SessionCriticalStepKind kind, DamageRecipient whose, SessionWearSide side, EquipmentSlot slot,
        CellCoord? cell, IDropMap? dropMap, List<SessionCriticalStep> steps, ref GameSessionFailure failure)
    {
        if (failure != GameSessionFailure.None) return side;
        if (side.Worn is null) { steps.Add(new(kind, whose, slot, null, SessionCriticalStepOutcome.Unknown, null)); return side; }
        if (!side.Worn.TryGetValue(slot, out var item) || Gone(item.Entity))
        {
            steps.Add(new(kind, whose, slot, item?.Entity, SessionCriticalStepOutcome.NoItem, null));
            return side;
        }
        return Drop(kind, whose, side, slot, cell, dropMap, steps, ref failure);
    }

    // The wear check of the critter's item in the slot under its injuries now; null without the item or without its wear values.
    private EquipmentRefusal? WearRefusal(SessionWearSide side, SessionCritterCombat? npc, EquipmentSlot slot)
    {
        if (side.Worn is null || !side.Worn.TryGetValue(slot, out var held) || Gone(held.Entity)) return null;
        if (side.Life!.IsHero)
            return _equipment.Worn.TryGetValue(slot, out var worn) ? EquipmentRules.CheckWear(worn.Item, slot, HeroWearer(), _equipment.Items()) : null;
        if (npc?.WornItem(slot) is not { } item) return null;
        var conditions = _life.Critters.TryGetValue(npc.Entity, out var record) ? record.State.Conditions : npc.Loaded.Conditions;
        return EquipmentRules.CheckWear(item, slot, npc.Wearer(conditions), npc.WornItems);
    }

    // One drop (0x461A90 then 0x461AB0). The slot was checked by the caller. With the host's drop map the distance is drawn first,
    // then the drop's refusals run, then the tries choose the cell (DropPlacementRules); without it the item lands on its holder's
    // cell and nothing is drawn. The hero's item joins its placed items (ARC-WORLD-059), an NPC's seed item goes to the ground as a
    // map change.
    private SessionWearSide Drop(SessionCriticalStepKind kind, DamageRecipient whose, SessionWearSide side, EquipmentSlot slot, CellCoord? cell,
        IDropMap? dropMap, List<SessionCriticalStep> steps, ref GameSessionFailure failure)
    {
        var item = side.Worn![slot];
        var hero = side.Life!.IsHero;
        CellCoord? owner = hero ? _actor.Cell : cell;
        void Skip(SessionCriticalStepOutcome outcome, CellCoord? at = null) => steps.Add(new(kind, whose, slot, item.Entity, outcome, at));
        if (hero && !(_equipment.Worn.TryGetValue(slot, out var wornItem) && wornItem.Entity == item.Entity))
        {
            // An item an earlier step of this attack broke has left its slot (the first break takes it off): the slot is empty, as
            // the original finds it, and no drop is called.
            Skip(SessionCriticalStepOutcome.NoItem);
            return side;
        }
        if (owner is null) { Skip(SessionCriticalStepOutcome.Unknown); return side; }
        if (_revision == long.MaxValue) { failure = GameSessionFailure.RevisionExhausted; Skip(SessionCriticalStepOutcome.Unknown); return side; }
        var revision = _revision;
        var rolls = new SessionDropRolls(_random);
        int distance;
        try { distance = dropMap is null ? 0 : DropPlacementRules.Distance(rolls); }
        catch (SessionDropRolls.ExhaustedException) { failure = GameSessionFailure.RandomExhausted; Skip(SessionCriticalStepOutcome.Unknown); return side; }

        // The drop's refusals (PI1): the item's drop script, which the session does not run, then the no-drop flag.
        var script = hero ? HeroItemDropScript(item.Entity) : NpcItemSeed(side.Life.Entity!, item.Entity) is { } listed && listed.Facts.HasScript(SessionScriptPoint.Drop);
        if (script) { Skip(SessionCriticalStepOutcome.ScriptUnsupported); return side; }
        if (item.NoDrop) { Skip(SessionCriticalStepOutcome.Refused); return side; }

        CellCoord landed;
        try { landed = dropMap is null ? owner.Value : DropPlacementRules.Place(owner.Value, distance, DropOwnerOf(side.Life), dropMap, rolls); }
        catch (SessionDropRolls.ExhaustedException) { failure = GameSessionFailure.RandomExhausted; Skip(SessionCriticalStepOutcome.Unknown); return side; }
        ThrowIfDisposed();
        if (_revision != revision) { failure = GameSessionFailure.StaleVersion; Skip(SessionCriticalStepOutcome.Unknown); return side; }

        if (!DropCellFits(landed)) { Skip(SessionCriticalStepOutcome.HeroCellUnfit); return side; }
        if (hero) PlaceHeroItem(item.Entity, landed);
        else
        {
            // An item the session added to the NPC (ARC-WORLD-055) lies in no seed: not ready until a store holds it.
            if (NpcItemSeed(side.Life.Entity!, item.Entity) is null) { Skip(SessionCriticalStepOutcome.NotSeedItem, landed); return side; }
            var written = ApplyChanges(Version, null, [new(_actor.Map, MapChangeKey.ForEntity(item.Entity, ItemDroppedProperty), DroppedValue(landed))]);
            if (written.Disposition == GameSessionDisposition.Rejected) { failure = written.Failure; Skip(SessionCriticalStepOutcome.Unknown); return side; }
        }
        steps.Add(new(kind, whose, slot, item.Entity, SessionCriticalStepOutcome.Dropped, landed));
        var left = new Dictionary<EquipmentSlot, SessionWearItem>(side.Worn);
        left.Remove(slot);
        return side with { Worn = left };
    }

    // The hero, or an NPC following it, avoids only a spotted trap on the drop's path; another NPC avoids every trap (0x4BBFE0).
    private DropOwner DropOwnerOf(SessionLifeTarget who) =>
        who.IsHero || _party.Get(who.Entity!).Standing == CompanionStanding.Following ? DropOwner.PcSide : DropOwner.Npc;

    // The seed listing of an NPC's item still in its list on the actor's map, or null (an added made item, another map's).
    private SessionSeedItem? NpcItemSeed(string npc, string item) =>
        _inventory.Seeds.TryGetValue(_actor.Map, out var seed) && seed.HolderOf.TryGetValue(item, out var holder) && holder == npc &&
        !_inventory.Taken.Contains(item) ? seed.Items[item] : null;

    // A world item of the hero's whose attached origin seed shows a drop script (the original runs it and it may refuse).
    private bool HeroItemDropScript(string item) =>
        _inventory.Pc.Find(pc => pc.Entity == item) is { Entity: not null } taken && _inventory.Seeds.TryGetValue(taken.Origin, out var origin) &&
        origin.Items.TryGetValue(item, out var listed) && listed.Facts.HasScript(SessionScriptPoint.Drop);

    // The ammunition effects of a critical miss, after the weapon's destruction and before "drop weapon" (CW3): not ready (no
    // ammunition model); none when the weapon was destroyed.
    private void AmmunitionSteps(AttackDamage damage, SessionWearSide side, List<SessionCriticalStep> steps)
    {
        if (side.Worn?.GetValueOrDefault(EquipmentSlot.Weapon) is { } weapon && _durability.Destroyed.Contains(weapon.Entity)) return;
        if (damage.MissEffects.HasFlag(CriticalMissEffects.DestroyAmmo))
            steps.Add(new(SessionCriticalStepKind.DestroyAmmo, damage.Recipient, EquipmentSlot.Weapon, null, SessionCriticalStepOutcome.AmmunitionNotReady, null));
        if (damage.MissEffects.HasFlag(CriticalMissEffects.LoseAmmo))
            steps.Add(new(SessionCriticalStepKind.LoseAmmo, damage.Recipient, EquipmentSlot.Weapon, null, SessionCriticalStepOutcome.AmmunitionNotReady, null));
    }
}

/// <summary>The session generator as the drop's roll source (ARC-COMBAT-010): it advances the session's own sequence, so a drop's
/// draws are kept like the attack's.</summary>
internal sealed class SessionDropRolls(SeededRandom random) : IDropRolls
{
    public int Roll(DropRoll purpose, int min, int max)
    {
        if (!Enum.IsDefined(purpose)) throw new ArgumentOutOfRangeException(nameof(purpose));
        return random.TryNext(min, max, out var value) ? value : throw new ExhaustedException();
    }

    internal sealed class ExhaustedException : Exception;
}
