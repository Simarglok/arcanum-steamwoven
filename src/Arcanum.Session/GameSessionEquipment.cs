using Arcanum.Simulation;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;

namespace Arcanum.Session;

/// <summary>
/// An item the host offers for a wear slot (ARC-WORLD-028): the saved kind it must match, its script attachment points and
/// whether it carries item spells (both refuse, see <see cref="GameSessionOwner.Equip"/>), and its stored wear and combat values.
/// The host builds it from the item's data, for an original item with <see cref="SessionEquipmentFacts.FromCombatFields"/> or, with
/// the facts the best-item choice reads too, <see cref="SessionEquipmentFacts.Wearable"/>. <see cref="Worth"/>,
/// <see cref="IdentifiedFlag"/>, <see cref="Throwable"/> and <see cref="Weight"/> (the item's effective weight) are read only by
/// <see cref="GameSessionOwner.EquipBest"/> (ARC-WORLD-034).
/// </summary>
public sealed record SessionWearableItem(SessionItemKind Kind, IReadOnlyList<int> ScriptPoints, bool HasItemSpells, EquipmentItem Item)
{
    public int Worth { get; init; }
    public bool IdentifiedFlag { get; init; }
    public bool Throwable { get; init; }
    public int Weight { get; init; }
}

/// <summary>One worn item: its slot, the PC's item entity (a kit item or an item taken from the world) and its saved values.</summary>
public sealed record SessionWornItem(EquipmentSlot Slot, string Entity, EquipmentItem Item);

/// <summary>Saved equipment of the hero: the worn items in slot order.</summary>
public sealed class SessionEquipmentState
{
    internal SessionEquipmentState(SessionWornItem[] worn) => Worn = Array.AsReadOnly(worn);

    public static SessionEquipmentState Empty { get; } = new([]);

    public IReadOnlyList<SessionWornItem> Worn { get; }
}

/// <summary>
/// The host's answer to the wear check's art lookup: whether the hero's art has the item's weapon, shield or armour
/// (<see cref="EquipmentRules.NeedsArtCheck"/>). False refuses the item as <see cref="EquipmentRefusal.NotWieldable"/>.
/// </summary>
public delegate bool SessionWearArtCheck(EquipmentSlot slot, SessionWearableItem item);

/// <summary>
/// Outcome of an equipment call. <see cref="Refusal"/> names the wear rule that refused (<see cref="GameSessionFailure.WearRefused"/>);
/// <see cref="Displaced"/> is the item that left the slot for the inventory; <see cref="Verdict"/> is the insert check's answer when it ran.
/// </summary>
public readonly record struct EquipmentMutationResult(GameSessionDisposition Disposition, GameSessionFailure Failure,
    EquipmentRefusal Refusal, string? Displaced, SessionInsertVerdict? Verdict, GameSessionVersion Version);

/// <summary>The equipment domain of one owner: slot to worn item.</summary>
internal sealed class EquipmentDomain
{
    internal SortedDictionary<EquipmentSlot, SessionWornItem> Worn { get; } = [];

    internal SessionEquipmentState Capture() => new([.. Worn.Values]);

    internal IReadOnlyDictionary<EquipmentSlot, EquipmentItem> Items() => Worn.ToDictionary(pair => pair.Key, pair => pair.Value.Item);

    internal EquipmentSlot? SlotOf(string entity) => Worn.Values.FirstOrDefault(worn => worn.Entity == entity)?.Slot;

    /// <summary>The PC item types a wear slot can hold.</summary>
    internal static bool Matches(SessionItemType type, EquipmentItemKind kind) => (type, kind) switch
    {
        (SessionItemType.Weapon, EquipmentItemKind.Weapon) or (SessionItemType.Armor, EquipmentItemKind.Armor)
            or (SessionItemType.Generic, EquipmentItemKind.Generic) => true,
        _ => false,
    };

    /// <summary>A worn item runs no script and no item spell when it moves: the source would run <c>SAP_INSERT_ITEM</c>,
    /// <c>SAP_REMOVE_ITEM</c>, <c>SAP_WIELD_ON</c> and <c>SAP_WIELD_OFF</c> and the item's wear, unwear, pickup and drop spells.</summary>
    internal static readonly int[] MoveScripts =
        [SessionScriptPoint.InsertItem, SessionScriptPoint.WieldOn, SessionScriptPoint.WieldOff, SessionScriptPoint.RemoveItem];
}

/// <summary>
/// The bridge from stored combat fields (ARC-DATA-021) to the wear rules' item values: the item flags (no drop 0x20, uber
/// 0x40000, light sizes 0x200..0x1000), the condition (hit points plus adjustment, less damage), the "destroyed" bit of the
/// current art, the armour coverage of the inventory art, and the weapon and armour blocks. The critical charts and the
/// weapon skill come as stored.
/// </summary>
public static class SessionEquipmentFacts
{
    private const uint NoDropFlag = 0x20, UberFlag = 0x40000, LightShift = 9, TorchShieldFlag = 0x1, IdentifiedItemFlag = 0x1, NoSpell = 10000;

    /// <summary>
    /// Everything the session reads about a held item (ARC-WORLD-034): its saved kind, its script points (as read), whether any of its five item
    /// spells is set, the wear values of <see cref="FromCombatFields"/>, the stored worth, the identified flag (item flag 0x1), the
    /// throwable weapon flag and the stored weight. The magic weight adjustment is not read (ARC-DATA-021 has no slot for it), so
    /// <see cref="SessionWearableItem.Weight"/> is the stored weight; only a thrown weapon without the throwable flag reads it.
    /// Null for an item that is not worn anywhere (not a weapon, armour or generic item) or whose values are out of bounds.
    /// </summary>
    public static SessionWearableItem? Wearable(SessionItemKind kind, Arcanum.Data.Combat.CombatFields fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if (FromCombatFields(fields) is not { } item || fields.Item is not { } stored) return null;
        return new SessionWearableItem(kind, fields.Object.ScriptPoints, stored.Spells.Any(spell => spell != NoSpell), item)
        {
            Worth = stored.Worth,
            IdentifiedFlag = (stored.Flags & IdentifiedItemFlag) != 0,
            Throwable = fields.Weapon?.Flags.HasFlag(Arcanum.Data.Combat.CombatWeaponFlags.Throwable) == true,
            Weight = stored.Weight,
        };
    }

    /// <summary>The wear values of a weapon, armour or generic item; null for other types or a field outside the rules' bounds.</summary>
    public static EquipmentItem? FromCombatFields(Arcanum.Data.Combat.CombatFields fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if (fields.Item is not { } item) return null;
        var common = fields.Object;
        var maxHp = (long)common.HitPoints + common.HitPointAdjustment;
        EquipmentItem result;
        try
        {
            result = new EquipmentItem
            {
                Kind = fields.Weapon is not null ? EquipmentItemKind.Weapon : fields.Armor is not null ? EquipmentItemKind.Armor
                    : fields.Generic is not null ? EquipmentItemKind.Generic : throw new InvalidOperationException(),
                MagickTechComplexity = item.MagickTechComplexity,
                NoDrop = (item.Flags & NoDropFlag) != 0,
                Uber = (item.Flags & UberFlag) != 0,
                Light = (EquipmentLight)((item.Flags >> (int)LightShift) & 0xF),
                Broken = Arcanum.Data.Combat.CombatItemArt.Destroyed(common.CurrentArt),
                MaxHitPoints = checked((int)maxHp),
                HitPoints = checked((int)(maxHp - common.HitPointDamage)),
                UsesShieldLocation = fields.Generic is { } generic && (generic.Flags & TorchShieldFlag) != 0,
                Weapon = fields.Weapon is { } w ? Weapon(w, item) : null,
                Armor = fields.Armor is { } a ? Armor(a, item) : null,
            };
        }
        catch (Exception e) when (e is InvalidOperationException or OverflowException or ArgumentOutOfRangeException) { return null; }
        return EquipmentRules.IsValid(result) ? result : null;
    }

    private static EquipmentWeapon Weapon(Arcanum.Data.Combat.CombatWeaponFields w, Arcanum.Data.Combat.CombatItemFields item)
    {
        DamageRange Range(int i) => new(w.Damage[i].Minimum, w.Damage[i].Maximum);
        var flags = w.Flags;
        return new EquipmentWeapon
        {
            TwoHanded = flags.HasFlag(Arcanum.Data.Combat.CombatWeaponFlags.TwoHanded),
            HandCountFixed = flags.HasFlag(Arcanum.Data.Combat.CombatWeaponFlags.HandCountFixed),
            IgnoresResistance = flags.HasFlag(Arcanum.Data.Combat.CombatWeaponFlags.IgnoreResistance),
            DamagesArmor = flags.HasFlag(Arcanum.Data.Combat.CombatWeaponFlags.DamageArmor),
            Skill = (CombatSkill)(int)w.Skill,
            // Damage arrays are in the source's damage order, which DamageType shares.
            Damage = new(Range(0), Range(1), Range(2), Range(3), Range(4)),
            MagicDamage = new(w.MagicDamageAdjustment[0], w.MagicDamageAdjustment[1], w.MagicDamageAdjustment[2], w.MagicDamageAdjustment[3],
                w.MagicDamageAdjustment[4]),
            ToHit = w.BonusToHit, MagicToHit = w.MagicHitAdjustment,
            SpeedFactor = w.SpeedFactor, MagicSpeed = w.MagicSpeedAdjustment,
            Range = w.Range, MagicRange = w.MagicRangeAdjustment,
            MinimumStrength = w.MinimumStrength, MagicMinimumStrength = w.MagicMinimumStrengthAdjustment,
            AmmoType = w.AmmoType == Arcanum.Data.Combat.CombatFieldReader.NoAmmo ? null : w.AmmoType,
            AmmoConsumption = w.AmmoConsumption,
            CriticalHitChart = Defined<CriticalHitChart>(w.CriticalHitChart),
            CriticalMissChart = Defined<CriticalMissChart>(w.CriticalMissChart),
            MagicCriticalHitChance = w.MagicCriticalHitChance, MagicCriticalHitEffect = w.MagicCriticalHitEffect,
            MagicCriticalMissChance = w.MagicCriticalMissChance, MagicCriticalMissEffect = w.MagicCriticalMissEffect,
        };
    }

    private static EquipmentArmor Armor(Arcanum.Data.Combat.CombatArmorFields a, Arcanum.Data.Combat.CombatItemFields item)
    {
        // Resistance arrays are in the source's resistance order: normal, fire, electrical, poison, magic.
        static EquipmentResistances Of(IReadOnlyList<int> r) => new(r[0], r[1], r[2], r[3], r[4]);
        var flags = a.Flags;
        return new EquipmentArmor
        {
            Coverage = Defined<ArmorCoverage>(Arcanum.Data.Combat.CombatItemArt.Coverage(item.InventoryArt)),
            Sizes = (ArmorSizes)((uint)flags & 7),
            MaleOnly = flags.HasFlag(Arcanum.Data.Combat.CombatArmorFlags.MaleOnly),
            FemaleOnly = flags.HasFlag(Arcanum.Data.Combat.CombatArmorFlags.FemaleOnly),
            AcAdjustment = a.ArmorClassAdjustment, MagicAcAdjustment = a.MagicArmorClassAdjustment,
            Resistances = Of(a.ResistanceAdjustment), MagicResistances = Of(a.MagicResistanceAdjustment),
            UnarmedBonusDamage = a.UnarmedBonusDamage,
        };
    }

    private static T Defined<T>(int value) where T : struct, Enum =>
        Enum.IsDefined(typeof(T), value) ? (T)(object)value : throw new ArgumentOutOfRangeException(nameof(value));
}

public sealed partial class GameSessionOwner
{
    /// <summary>The hero's worn items in slot order (empty without a hero).</summary>
    public SessionEquipmentState QueryEquipment() { ThrowIfDisposed(); return _equipment.Capture(); }

    /// <summary>
    /// The worn items' part of the hero's combat values for the attack resolver and the round (<see cref="EquipmentRules.Evaluate"/>):
    /// armour-class and resistance adjustments, light, the attack it strikes with and its weapon speed and attack cost, for the
    /// hero's effective magick/tech aptitude. <paramref name="weaponTraining"/> overrides the hero's training in the wielded weapon's
    /// skill; null reads it from <see cref="QueryHeroSkills"/> (ARC-SKILLS-002). Null without a hero.
    /// </summary>
    public EquipmentCombat? QueryHeroEquipmentCombat(SkillTraining? weaponTraining = null)
    {
        ThrowIfDisposed();
        if (weaponTraining is { } training && !Enum.IsDefined(training)) throw new ArgumentOutOfRangeException(nameof(weaponTraining));
        return _hero is null ? null : EquipmentRules.Evaluate(_equipment.Items(), HeroAptitude(), weaponTraining ?? HeroWeaponTraining(HeroSkills()));
    }

    /// <summary>
    /// Puts an item the hero holds into a wear slot (the source's 0x464AE0). Refused before any effect, in order:
    /// input (<see cref="GameSessionFailure.InvalidItem"/>), version, no hero (<see cref="GameSessionFailure.EquipmentUnavailable"/>),
    /// an item the hero does not hold (<see cref="GameSessionFailure.ItemNotAvailable"/>) or whose saved kind differs
    /// (<see cref="GameSessionFailure.InventoryMismatch"/>); NoChange when it already is in that slot; a move script or item spell
    /// (<see cref="GameSessionFailure.ItemScriptUnsupported"/>, <see cref="GameSessionFailure.ItemEffectUnsupported"/>); the wear check
    /// with the hero's race, gender, aptitude and crippled arms and then the host's <paramref name="artCheck"/>
    /// (<see cref="GameSessionFailure.WearRefused"/>); the item's and the current occupant's no-drop flag (WearRefused,
    /// <see cref="EquipmentRefusal.NotDroppable"/>); last the insert check of the occupant going back to the inventory
    /// (<see cref="GameSessionFailure.InsertRefused"/>). The host answers that check as the source asks it: with both the item and
    /// the occupant already out of the grid (the room the item leaves counts, the occupant's weight once). The facts it gets are the
    /// occupant's saved kind only; weight and grid size are the host's to look up. An item worn in another slot (a ring) leaves it.
    /// One revision.
    /// </summary>
    [OriginalFunction(0x464AE0, OriginalFunctionScope.Partial)]
    public EquipmentMutationResult Equip(GameSessionVersion expected, string entity, EquipmentSlot slot, SessionWearableItem item,
        SessionWearArtCheck artCheck, SessionInsertCheck insertCheck)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(artCheck);
        ArgumentNullException.ThrowIfNull(insertCheck);
        if (!SessionValidation.Entity(entity) || !Enum.IsDefined(slot) || !ValidWearable(item)) return RejectWear(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectWear(GameSessionFailure.StaleVersion);
        if (_hero is null) return RejectWear(GameSessionFailure.EquipmentUnavailable);
        if (HeldKind(entity) is not { } kind) return RejectWear(GameSessionFailure.ItemNotAvailable);
        // The session's record of the item's condition (ARC-WORLD-053) wins over the host's data: a broken item is refused.
        item = Conditioned(entity, item);
        if (kind != item.Kind || !EquipmentDomain.Matches(kind.Type, item.Item.Kind)) return RejectWear(GameSessionFailure.InventoryMismatch);
        _equipment.Worn.TryGetValue(slot, out var current);
        if (current?.Entity == entity) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, EquipmentRefusal.None, null, null, Version);
        if (EquipmentDomain.MoveScripts.Any(item.ScriptPoints.Contains)) return RejectWear(GameSessionFailure.ItemScriptUnsupported);
        if (item.HasItemSpells) return RejectWear(GameSessionFailure.ItemEffectUnsupported);

        var worn = _equipment.Items();
        var refusal = EquipmentRules.CheckWear(item.Item, slot, HeroWearer(), worn);
        if (refusal != EquipmentRefusal.None) return RejectWear(GameSessionFailure.WearRefused, refusal);
        if (_revision == long.MaxValue) return RejectWear(GameSessionFailure.RevisionExhausted);
        var revision = _revision;
        if (EquipmentRules.NeedsArtCheck(item.Item, slot))
        {
            var art = artCheck(slot, item);
            ThrowIfDisposed();
            if (_revision != revision) return RejectWear(GameSessionFailure.StaleVersion);
            if (!art) return RejectWear(GameSessionFailure.WearRefused, EquipmentRefusal.NotWieldable);
        }
        refusal = EquipmentRules.CheckRemove(item.Item);
        if (refusal == EquipmentRefusal.None && current is not null) refusal = EquipmentRules.CheckRemove(current.Item);
        if (refusal != EquipmentRefusal.None) return RejectWear(GameSessionFailure.WearRefused, refusal);
        SessionInsertVerdict? verdict = null;
        if (current is not null)
        {
            verdict = insertCheck(new SessionInventoryItem(current.Entity, CarriedFacts(current.Entity)));
            ThrowIfDisposed();
            if (_revision != revision) return RejectWear(GameSessionFailure.StaleVersion, verdict: verdict);
            if (verdict != SessionInsertVerdict.Accepted) return RejectWear(GameSessionFailure.InsertRefused, verdict: verdict);
        }
        if (_equipment.SlotOf(entity) is { } previous) _equipment.Worn.Remove(previous);
        _equipment.Worn[slot] = new(slot, entity, item.Item);
        _revision++;
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, EquipmentRefusal.None, current?.Entity, verdict, Version);
    }

    /// <summary>
    /// Takes the item in <paramref name="slot"/> off into the hero's inventory (the source's 0x464C80): NoChange for an
    /// empty slot; refused for an item with the no-drop flag (<see cref="GameSessionFailure.WearRefused"/>,
    /// <see cref="EquipmentRefusal.NotDroppable"/>), then by the insert check (<see cref="GameSessionFailure.InsertRefused"/>), which
    /// the source asks with the item still worn (its weight counts twice near the carry limit). One revision.
    /// </summary>
    [OriginalFunction(0x464C80, OriginalFunctionScope.Partial)]
    public EquipmentMutationResult Unequip(GameSessionVersion expected, EquipmentSlot slot, SessionInsertCheck insertCheck)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(insertCheck);
        if (!Enum.IsDefined(slot)) return RejectWear(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectWear(GameSessionFailure.StaleVersion);
        if (_hero is null) return RejectWear(GameSessionFailure.EquipmentUnavailable);
        if (!_equipment.Worn.TryGetValue(slot, out var current))
            return new(GameSessionDisposition.NoChange, GameSessionFailure.None, EquipmentRefusal.None, null, null, Version);
        var refusal = EquipmentRules.CheckRemove(current.Item);
        if (refusal != EquipmentRefusal.None) return RejectWear(GameSessionFailure.WearRefused, refusal);
        if (_revision == long.MaxValue) return RejectWear(GameSessionFailure.RevisionExhausted);
        var revision = _revision;
        var verdict = insertCheck(new SessionInventoryItem(current.Entity, CarriedFacts(current.Entity)));
        ThrowIfDisposed();
        if (_revision != revision) return RejectWear(GameSessionFailure.StaleVersion, verdict: verdict);
        if (verdict != SessionInsertVerdict.Accepted) return RejectWear(GameSessionFailure.InsertRefused, verdict: verdict);
        _equipment.Worn.Remove(slot);
        _revision++;
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, EquipmentRefusal.None, current.Entity, verdict, Version);
    }

    private EquipmentMutationResult RejectWear(GameSessionFailure failure, EquipmentRefusal refusal = EquipmentRefusal.None,
        SessionInsertVerdict? verdict = null) => new(GameSessionDisposition.Rejected, failure, refusal, null, verdict, Version);

    private static bool ValidWearable(SessionWearableItem? item) => item?.ScriptPoints is not null && Enum.IsDefined(item.Kind.Type) &&
        item.ScriptPoints.All(point => point is >= 0 and <= SessionScriptPoint.Last) && EquipmentRules.IsValid(item.Item);

    /// <summary>The saved kind of an item the hero holds: a kit item or an item taken from the world.</summary>
    private SessionItemKind? HeldKind(string entity)
    {
        foreach (var kit in _inventory.Kit) if (kit.Entity == entity) return kit.Kind;
        foreach (var pc in _inventory.Pc) if (pc.Entity == entity) return pc.Kind;
        foreach (var made in _inventory.Made) if (made.Entity == entity) return made.Kind;
        return null;
    }

    /// <summary>What the insert check is told about an item going back to the hero's grid: its saved kind; weight, complexity and
    /// scripts are the host's to look up (the session keeps only kinds for the PC's items).</summary>
    private SessionItemFacts CarriedFacts(string entity) => new(HeldKind(entity)!.Value, 0, 0, -1, null, []);

    private int HeroAptitude() =>
        Math.Clamp(_hero!.Stats.Query(new(0)).Snapshot!.GetEffective(CharacterStat.MagickTechAptitude), EquipmentLimits.MinAptitude, EquipmentLimits.MaxAptitude);

    /// <summary>The hero as a wearer: race, gender, aptitude, the crippled arms of its life state and (ARC-WORLD-069) the Magick Allergy
    /// background. No spell or unique art is session state, so the hero is never transformed or unique.</summary>
    private EquipmentWearer HeroWearer()
    {
        var conditions = _life.Hero?.State.Conditions ?? LifeConditions.None;
        return new EquipmentWearer
        {
            Race = _hero!.State.Race,
            Gender = _hero.State.Gender,
            MagickTechAptitude = HeroAptitude(),
            CrippledArmOne = conditions.HasFlag(LifeConditions.CrippledArmOne),
            CrippledArmsBoth = conditions.HasFlag(LifeConditions.CrippledArmsBoth),
            MagickAllergy = _hero.State.Background == SessionBackgrounds.MagickAllergy,
        };
    }

    /// <summary>The hero's effective normal resistance for its life facts: the worn armour's adjustment (the base of a pre-made PC
    /// and resistance effects are not session state), clamped 0..95 like the source's resistance.</summary>
    private int HeroNormalResistance() => _hero is null || _equipment.Worn.Count == 0 ? 0
        : Math.Clamp(EquipmentRules.ResistanceAdjustments(_equipment.Items().Values, HeroAptitude()).Normal, 0, 95);

    // Each worn item must be one the hero holds, of a matching type, in one slot, the slot its values belong in, and valid.
    private static GameSessionFailure ImportEquipment(SessionEquipmentState saved, InventoryDomain inventory, bool hero, out EquipmentDomain equipment)
    {
        equipment = new();
        if (saved.Worn.Count > 0 && !hero) return GameSessionFailure.InvalidEquipment;
        var kinds = inventory.Kit.Select(kit => (kit.Entity, kit.Kind)).Concat(inventory.Pc.Select(pc => (pc.Entity, pc.Kind)))
            .Concat(inventory.Made.Select(made => (made.Entity, made.Kind))).ToDictionary(pair => pair.Entity, pair => pair.Kind, StringComparer.Ordinal);
        foreach (var worn in saved.Worn)
        {
            if (worn is null || !Enum.IsDefined(worn.Slot) || !EquipmentRules.IsValid(worn.Item)) return GameSessionFailure.InvalidEquipment;
            if (!kinds.TryGetValue(worn.Entity, out var kind) || !EquipmentDomain.Matches(kind.Type, worn.Item.Kind)) return GameSessionFailure.InvalidEquipment;
            var location = worn.Slot == EquipmentSlot.Ring2 ? EquipmentSlot.Ring1 : worn.Slot;
            if (EquipmentRules.WearSlot(worn.Item) != location || equipment.SlotOf(worn.Entity) is not null ||
                !equipment.Worn.TryAdd(worn.Slot, worn)) return GameSessionFailure.InvalidEquipment;
        }
        // A fixed two-handed weapon never shares the hands with a shield.
        var items = equipment.Items();
        if (items.TryGetValue(EquipmentSlot.Weapon, out var weapon) && weapon.Weapon is { TwoHanded: true, HandCountFixed: true } &&
            items.ContainsKey(EquipmentSlot.Shield)) return GameSessionFailure.InvalidEquipment;
        return GameSessionFailure.None;
    }
}
