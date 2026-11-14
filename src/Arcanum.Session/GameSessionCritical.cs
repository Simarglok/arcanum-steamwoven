using Arcanum.Simulation.Ai;
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
    /// <summary>ARC-COMBAT-013: the units <see cref="SessionCriticalStep.Units"/> left the hero's stack <see cref="SessionCriticalStep.Entity"/>
    /// of the weapon's ammunition type (<see cref="GameSessionOwner.SpendAmmo"/>); a stack spent to 0 is deleted.</summary>
    AmmunitionSpent,
    /// <summary>ARC-COMBAT-013: nothing spent: the hero holds no ammunition of the weapon's type, or fewer units than five shots take
    /// (the original spends all or nothing, 0x4B65D0). ARC-DEV-283: also an NPC without a PC leader, which spends nothing (CR15, RA3)
    /// and one led by a PC that lacks the units.</summary>
    NoAmmunition,
    /// <summary>Not applied, NOT READY (ARC-MAGIC-022): the hero's worn item carries the item spells mark, whose unwear and drop
    /// executors (0x4CBFC0, 0x4CC130) this path does not run.</summary>
    ItemSpellsUnsupported,
}

/// <summary>
/// One critical item step of an attack: what it was, whose (the critter that took the damage), the slot and item, how it ended and,
/// for a drop, the cell. The original places a dropped item 2..4 cells away with draws of its own (0x461A90, 0x4F4E40); that
/// placement and its draws are NOT READY, so the item lies on the holder's cell and the session generator does not draw for it.
/// </summary>
public sealed record SessionCriticalStep(SessionCriticalStepKind Kind, DamageRecipient Whose, EquipmentSlot? Slot, string? Entity,
    SessionCriticalStepOutcome Outcome, CellCoord? Cell)
{
    /// <summary>ARC-COMBAT-013: the ammunition units a step spent (0 for any other step).</summary>
    public int Units { get; init; }
}

/// <summary>ARC-DEV-262: an NPC's seed item that lies loose on the ground because a critical dropped it or the NPC threw it: the item (with its
/// facts), the NPC that held it in the seed, the map it lies on and its cell.</summary>
public sealed record SessionDroppedItem(SessionInventoryItem Item, string Holder, SessionMapId Map, CellCoord Cell);

public sealed partial class GameSessionOwner
{
    /// <summary>The map change of an NPC's seed item a critical dropped (ARC-COMBAT-009): its cell, each coordinate in 24 bits, + 1.
    /// The snapshot already carries map changes, so drops add no schema.</summary>
    public const string ItemDroppedProperty = "item.dropped";

    /// <summary>The cell of an NPC's seed item a critical dropped, or null.</summary>
    internal CellCoord? DroppedCell(SessionMapId map, InventoryDomain.Seed seed, string item)
    {
        if (!seed.HolderOf.TryGetValue(item, out var holder) || seed.Holders[holder].Kind != SessionHolderKind.Npc) return null;
        if (_maps.Get(map, MapChangeKey.ForEntity(item, ItemDroppedProperty)) is not { } packed || packed <= 0) return null;
        return new CellCoord((int)((packed - 1) & 0xFFFFFF), (int)(((packed - 1) >> 24) & 0xFFFFFF));
    }

    /// <summary>
    /// ARC-DEV-262: the NPC seed item <paramref name="entity"/> lying loose where a critical dropped it or its NPC threw it (the
    /// <see cref="ItemDroppedProperty"/> map change), or null: a held, taken or deleted item, or an item that was never an NPC's. The cast host's
    /// lying-item lookup (a thrown grenade of an NPC explodes where it lies, as a hero's) and the item spell lookup read it. An item in flight is
    /// not lying yet (<see cref="QueryItemInFlight"/>). NOT READY: the change is read under the seed's own map (a companion's items sit in its home map's
    /// seed while the drop is written under the map it is on), and a critical's drop settled into a junk pile still counts as lying at its cell.
    /// </summary>
    public SessionDroppedItem? QueryDroppedItem(string entity)
    {
        ThrowIfDisposed();
        return DroppedItemOf(entity);
    }

    private SessionDroppedItem? DroppedItemOf(string entity)
    {
        // An item in flight has left its thrower but is not on the ground yet (its shot lands it).
        if (_inventory.Taken.Contains(entity) || _durability.Destroyed.Contains(entity) || ItemInFlight(entity)) return null;
        foreach (var (map, seed) in _inventory.Seeds)
            if (DroppedCell(map, seed, entity) is { } cell && seed.Items.TryGetValue(entity, out var row))
                // ARC-MAGIC-1230: a push moves the dropped item on from its drop cell.
                return new(new(entity, _inventory.WithAmmo(entity, row.Facts)), seed.HolderOf[entity], map, MovedCell(map, entity) ?? cell);
        return null;
    }

    /// <summary>Whether a critical dropped the NPC's seed item on any attached map: it left the NPC's list.</summary>
    internal bool Dropped(string item) => _inventory.Seeds.Any(pair => DroppedCell(pair.Key, pair.Value, item) is not null);

    /// <summary>Where a seed item lies loose on the ground: its own cell, its spilled container's (ARC-COMBAT-008) or the cell a
    /// critical dropped it on; null while a holder holds it.</summary>
    internal CellCoord? LooseCell(SessionMapId map, InventoryDomain.Seed seed, string item) =>
        // ARC-MAGIC-1230: a pushed item lies on the cell the push left it on.
        seed.Items.TryGetValue(item, out var row) && (row.Cell ?? SpilledCell(map, seed, item) ?? DroppedCell(map, seed, item)) is { } home ? MovedCell(map, item) ?? home : null;

    private static long DroppedValue(CellCoord cell) => ItemDroppedValue(cell);

    /// <summary>The <see cref="ItemDroppedProperty"/> value of a cell (each coordinate 0 .. 2^24 − 1, packed in 24 bits, + 1), for a
    /// host that observes the drop's map change (ARC-RENDER-033).</summary>
    public static long ItemDroppedValue(CellCoord cell) => cell.X is >= 0 and < 1 << 24 && cell.Y is >= 0 and < 1 << 24
        ? ((long)cell.Y << 24 | (uint)cell.X) + 1 : throw new ArgumentOutOfRangeException(nameof(cell));

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

    /// <summary>
    /// ARC-MAGIC-1232 (DW2, XR15): the weapon block of a spell's damage step on <paramref name="who"/>: the critter's item in the weapon slot goes through <see cref="CriticalDrop"/> (the attack paths' step, not a copy), the host's
    /// <paramref name="worn"/> items and <paramref name="cell"/> as for an attack's recipient (the hero's drop is at its own cell). Returns the one step, empty when no step ran; <paramref name="failure"/> is a refusal of the
    /// session (the generator ran dry, a stale version), whose step is in the list as <see cref="SessionCriticalStepOutcome.Unknown"/>.
    /// </summary>
    internal IReadOnlyList<SessionCriticalStep> SpellWeaponDrop(SessionLifeTarget who, IReadOnlyDictionary<EquipmentSlot, SessionWearItem>? worn, CellCoord? cell, IDropMap? dropMap,
        out GameSessionFailure failure)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(who);
        failure = GameSessionFailure.None;
        var steps = new List<SessionCriticalStep>();
        CriticalDrop(SessionCriticalStepKind.DropWeapon, DamageRecipient.Target, new SessionWearSide { Life = who, Worn = worn }, EquipmentSlot.Weapon, cell, dropMap, steps, ref failure);
        // A drop that drew from the generator and was then refused changed no state a revision covers (an attack's own revision does): the spell's step has none, so the draw is revised here.
        if (failure == GameSessionFailure.None && steps.Any(step => step.Outcome is SessionCriticalStepOutcome.Refused or SessionCriticalStepOutcome.ScriptUnsupported
            or SessionCriticalStepOutcome.ItemSpellsUnsupported or SessionCriticalStepOutcome.HeroCellUnfit or SessionCriticalStepOutcome.NotSeedItem) && dropMap is not null && _revision != long.MaxValue)
            Revise();
        return steps;
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
        if (hero && _equipment.Worn[slot].ItemSpells) { Skip(SessionCriticalStepOutcome.ItemSpellsUnsupported); return side; }
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
            if (NpcItemSeed(side.Life.Entity!, item.Entity) is not { } seedItem) { Skip(SessionCriticalStepOutcome.NotSeedItem, landed); return side; }
            var written = ApplyChanges(Version, null, [new(_actor.Map, MapChangeKey.ForEntity(item.Entity, ItemDroppedProperty), DroppedValue(landed))]);
            if (written.Disposition == GameSessionDisposition.Rejected) { failure = written.Failure; Skip(SessionCriticalStepOutcome.Unknown); return side; }
            // ARC-WORLD-103: a dropped item a dressed NPC wore is worn no more.
            _inventory.Unwear(item.Entity);
            // ARC-WORLD-1284 (VPG1): the drop's removal 0x467860 sets NPC flag 0x40 on the NPC.
            MarkCheckWield(side.Life.Entity);
            // ARC-WORLD-1288 (VP6, VPG3): the drop's tail for an NPC owner (0x461AB0): a dropped weapon sets LOOK_FOR_WEAPON, a dropped armour item LOOK_FOR_ARMOR.
            NoteDropForAi(side.Life.Entity!, seedItem.Facts.Kind.Type);
            // ARC-WORLD-063: the placement may put it into a junk pile; ARC-WORLD-062: the dropped item decays (ledger ID4).
            SettleDrop(item.Entity, _actor.Map, landed);
            ScheduleDecay(item.Entity, _actor.Map);
        }
        steps.Add(new(kind, whose, slot, item.Entity, SessionCriticalStepOutcome.Dropped, landed));
        var left = new Dictionary<EquipmentSlot, SessionWearItem>(side.Worn);
        left.Remove(slot);
        return side with { Worn = left };
    }

    // ARC-WORLD-1288 (ledger VP6, VPG3; the item drop 0x461AB0, found in ARC-RES-1104): an NPC that dropped an item of type 5 (weapon) gets the AI's look-for-weapon bit
    // (0x2000000), one of type 7 (armour, shields and helmets included) the look-for-armour bit (0x4000000), so its next AI pass searches the ground (0x4A8E70); any other
    // item type sets none. Only an NPC owner reaches here: the hero's drop sets no bit. The danger record is saved by the snapshot as it is.
    private void NoteDropForAi(string npc, SessionItemType type)
    {
        var bit = type switch { SessionItemType.Weapon => DangerFlags.LookForWeapon, SessionItemType.Armor => DangerFlags.LookForArmor, _ => DangerFlags.None };
        if (bit == DangerFlags.None) return;
        var record = _danger.Get(npc);
        if (record.Flags.HasFlag(bit)) return;
        // As the other writes that may start a record: a full store keeps the bit unwritten rather than exceed its limit (a snapshot over it is refused at restore).
        if (_danger.Records.Count >= _limits.MaxDangerRecords && !_danger.Records.ContainsKey(npc)) return;
        _danger.Put(record with { Flags = record.Flags | bit });
    }

    // The hero, or an NPC whose chain of leaders reaches the PC (0x4BBFE0 reads 0x45DDA0, ARC-WORLD-2119), avoids only a spotted trap on the drop's path; another NPC avoids
    // every trap (0x4BBFE0).
    private DropOwner DropOwnerOf(SessionLifeTarget who) => who.IsHero || PcLeads(who.Entity!) ? DropOwner.PcSide : DropOwner.Npc;

    // The seed listing of an NPC's item still in its list on the actor's map, or null (an added made item, another map's).
    private SessionSeedItem? NpcItemSeed(string npc, string item) =>
        _inventory.Seeds.TryGetValue(_actor.Map, out var seed) && seed.HolderOf.TryGetValue(item, out var holder) && holder == npc &&
        !_inventory.Taken.Contains(item) ? seed.Items[item] : null;

    // A world item of the hero's whose attached origin seed shows a drop script (the original runs it and it may refuse).
    private bool HeroItemDropScript(string item) =>
        _inventory.Hero.PcItem(item) is { } taken && _inventory.Seeds.TryGetValue(taken.Origin, out var origin) &&
        origin.Items.TryGetValue(item, out var listed) && listed.Facts.HasScript(SessionScriptPoint.Drop);

    // The ammunition effects of a critical miss, after the weapon's destruction and before "drop weapon" (CW3; ARC-COMBAT-013, ledger
    // CR15, AM4): the damage step's 0x4B65D0 takes, for "destroy ammunition" (0x40000), every unit of the weapon's ammunition type the
    // holder carries, and for "lose ammunition" (0x20000) five times the weapon's per-shot consumption, all or nothing. None when the
    // weapon was destroyed. The holder goes through SpendAmmo: the hero, or (ARC-DEV-283) an NPC led by a PC from its own stack; an NPC with no PC leader loses nothing.
    // The weapon's facts are the attack's (captured before the steps of this attack broke or dropped it). A count of 0 (no units, or a
    // consumption of 0) spends nothing; a type outside 0..3 names no stack the session holds (AMA3).
    private void AmmunitionSteps(AttackDamage damage, SessionWearSide side, (int? Type, int Consumption)? attackWeapon, List<SessionCriticalStep> steps,
        ref GameSessionFailure failure)
    {
        if (side.Worn?.GetValueOrDefault(EquipmentSlot.Weapon) is { } weapon && _durability.Destroyed.Contains(weapon.Entity)) return;
        foreach (var (effect, kind) in new[] { (CriticalMissEffects.DestroyAmmo, SessionCriticalStepKind.DestroyAmmo), (CriticalMissEffects.LoseAmmo, SessionCriticalStepKind.LoseAmmo) })
        {
            if (!damage.MissEffects.HasFlag(effect)) continue;
            // After a refused step, as the other steps, nothing more is applied or reported.
            if (failure != GameSessionFailure.None) return;
            // ARC-DEV-283 (CR15, RA3): the gate of 0x4B65D0: an NPC whose leader chain reaches no PC spends nothing; a PC or an NPC led by one spends from its own stack.
            var holder = side.Life is { IsHero: true } ? SessionAmmoHolder.Hero : side.Life?.Entity is { } npc && PcLeads(npc) ? new SessionAmmoHolder(npc) : (SessionAmmoHolder?)null;
            if (holder is not { } who)
            {
                steps.Add(new(kind, damage.Recipient, EquipmentSlot.Weapon, null, SessionCriticalStepOutcome.NoAmmunition, null));
                continue;
            }
            // The attack's weapon gives the type and the consumption (fields 125 and 126 through its equipment facts).
            var held = attackWeapon;
            var stack = held?.Type is { } type and >= 0 and < SessionAmmoRecord.AmmoTypes ? who.IsHero ? HeroStackOfType(type) : NpcStackOfType(who.Npc!, type) : null;
            var count = stack is { } carried ? effect == CriticalMissEffects.DestroyAmmo ? carried.Units : checked(5 * held!.Value.Consumption) : 0;
            if (stack is not { } found || found.Units < count || count == 0)
            {
                steps.Add(new(kind, damage.Recipient, EquipmentSlot.Weapon, stack?.Entity, SessionCriticalStepOutcome.NoAmmunition, null));
                continue;
            }
            var spent = SpendAmmo(Version, who, found.AmmoType, count);
            if (spent.Disposition == GameSessionDisposition.Rejected)
            {
                failure = spent.Failure;
                steps.Add(new(kind, damage.Recipient, EquipmentSlot.Weapon, found.Entity, SessionCriticalStepOutcome.Unknown, null));
                continue;
            }
            steps.Add(new(kind, damage.Recipient, EquipmentSlot.Weapon, found.Entity, SessionCriticalStepOutcome.AmmunitionSpent, null) { Units = count });
        }
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
