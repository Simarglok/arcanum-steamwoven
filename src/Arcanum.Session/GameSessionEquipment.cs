using Arcanum.Simulation;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Magic;

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
public sealed record SessionWornItem(EquipmentSlot Slot, string Entity, EquipmentItem Item)
{
    /// <summary>ARC-SCRIPT-042: the item event points the item carries, ascending: insert item (21), wield on (24), wield off (25) and
    /// remove item (28); empty for an item without them. Its next move runs them only through a script host.</summary>
    public IReadOnlyList<int> ScriptPoints { get; init; } = NoScripts;

    /// <summary>ARC-MAGIC-022: the item carries item spells and was worn through a trigger host (its wear executor ran), so it comes off
    /// only through one, which runs its unwear (<see cref="GameSessionOwner.Unequip"/>).</summary>
    public bool ItemSpells { get; init; }

    internal static readonly IReadOnlyList<int> NoScripts = Array.AsReadOnly(Array.Empty<int>());

    public bool Equals(SessionWornItem? other) => other is not null && Slot == other.Slot && Entity == other.Entity && Item == other.Item &&
        ScriptPoints.SequenceEqual(other.ScriptPoints) && ItemSpells == other.ItemSpells;

    public override int GetHashCode() => HashCode.Combine(Slot, Entity, Item, ScriptPoints.Count, ItemSpells);
}

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
    EquipmentRefusal Refusal, string? Displaced, SessionInsertVerdict? Verdict, GameSessionVersion Version)
{
    private readonly IReadOnlyList<SessionScriptMessage>? _messages;
    private readonly IReadOnlyList<SessionItemScriptRun>? _scripts;

    /// <summary>ARC-SCRIPT-042: the lines the move's scripts printed, in order, for the host to show; empty when none.</summary>
    public IReadOnlyList<SessionScriptMessage> Messages { get => _messages ?? Array.Empty<SessionScriptMessage>(); init => _messages = value; }

    /// <summary>ARC-SCRIPT-042: every item script run of the move, in order (also on a refusal); empty without a script host or
    /// without item event scripts. Their eye candy (<see cref="Arcanum.Scripting.Scr.ScrEffects.EyeCandies"/>) is the host's to show.</summary>
    public IReadOnlyList<SessionItemScriptRun> Scripts { get => _scripts ?? Array.Empty<SessionItemScriptRun>(); init => _scripts = value; }

    private readonly IReadOnlyList<SessionItemTriggerResult>? _itemSpells;

    /// <summary>ARC-MAGIC-020: what the move's item spell executors did, in order (the item's unwear when it left another slot, the
    /// occupant's unwear, the item's wear); empty without an item trigger host or when nothing moved.</summary>
    public IReadOnlyList<SessionItemTriggerResult> ItemSpells { get => _itemSpells ?? Array.Empty<SessionItemTriggerResult>(); init => _itemSpells = value; }

    /// <summary>ARC-SCRIPT-042: the insert check refused the occupant after the scripts of both removals ran, so both items went back
    /// where they were, running the scripts of those insertions too (ledger IE6); what the runs changed is applied
    /// (<see cref="Disposition"/> Changed, or NoChange when they changed nothing) and <see cref="Verdict"/> is the refusal.</summary>
    public bool RolledBack { get; init; }
}

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

    /// <summary>ARC-SCRIPT-042: the item event points among <paramref name="points"/>, ascending (<see cref="SessionWornItem.ScriptPoints"/>).</summary>
    internal static IReadOnlyList<int> MovePoints(IReadOnlyList<int> points)
    {
        var moves = MoveScripts.Where(points.Contains).Order().ToArray();
        return moves.Length == 0 ? SessionWornItem.NoScripts : Array.AsReadOnly(moves);
    }
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
    /// (<see cref="GameSessionFailure.InsertRefused"/>; for a Technophobia hero a technological occupant is
    /// <see cref="SessionInsertVerdict.TechnologyRefused"/>, ARC-WORLD-069). The host answers that check as the source asks it: with
    /// both the item and the occupant already out of the grid (the room the item leaves counts, the occupant's weight once). The facts
    /// it gets are the occupant's saved kind and its complexity; weight and grid size are the host's to look up. An item worn in another slot (a ring) leaves it.
    /// One revision.
    /// ARC-SCRIPT-042: with <paramref name="scripts"/>, the item events of the move run in the original's order (ledger IE6, WE7): the
    /// item's removal (wield off 25 when it was worn, then remove item 28), for an occupied slot the occupant's removal (25, 28) and then
    /// its insert check; the occupant's insertion into the inventory (insert item 21) and the item's at the slot (wield on 24, then 21).
    /// When the check refuses, both go back where they were with those insertions' scripts, and what the runs changed is applied
    /// (<see cref="EquipmentMutationResult.RolledBack"/>). Without a host, an item or occupant with item event scripts is refused
    /// (<see cref="GameSessionFailure.ItemScriptUnsupported"/>). A run the session cannot apply refuses the move with nothing changed.
    /// ARC-MAGIC-020: an item with item spells needs <paramref name="triggers"/> that support it, and an occupant whose spells run (a
    /// run with it as source) needs a host, else <see cref="GameSessionFailure.ItemEffectUnsupported"/>. After the move the host's
    /// executors run (<see cref="EquipmentMutationResult.ItemSpells"/>): the unwear (0x4CBFC0) of the item when it left another slot and
    /// of the occupant, then the item's wear (0x4CB7D0), the hero as holder. In the original they run inside the wield and unwield
    /// (0x4677B0, 0x467CB0), before the light recalculation and the wield scripts; here they follow the move and its scripts.
    /// </summary>
    [OriginalFunction(0x464AE0, OriginalFunctionScope.Partial)]
    public EquipmentMutationResult Equip(GameSessionVersion expected, string entity, EquipmentSlot slot, SessionWearableItem item,
        SessionWearArtCheck artCheck, SessionInsertCheck insertCheck, ISessionItemScriptHost? scripts = null, ISessionItemTriggers? triggers = null)
    {
        ThrowIfDisposed();
        var wasWorn = entity is not null && _equipment.SlotOf(entity) is not null;
        var result = EquipMove(expected, entity!, slot, item, artCheck, insertCheck, scripts, triggers);
        if (triggers is null || result.Disposition != GameSessionDisposition.Changed || result.RolledBack) return result;
        var spells = new List<SessionItemTriggerResult>();
        if (wasWorn) spells.Add(triggers.Withdraw(ItemSpellTriggerRules.Unwear, entity!, SessionEventKinds.PlayerEntity));
        if (result.Displaced is { } displaced) spells.Add(triggers.Withdraw(ItemSpellTriggerRules.Unwear, displaced, SessionEventKinds.PlayerEntity));
        spells.Add(triggers.Fire(ItemSpellTriggerRules.Wear, entity!, SessionEventKinds.PlayerEntity));
        return result with { ItemSpells = spells.AsReadOnly(), Version = Version };
    }

    private EquipmentMutationResult EquipMove(GameSessionVersion expected, string entity, EquipmentSlot slot, SessionWearableItem item,
        SessionWearArtCheck artCheck, SessionInsertCheck insertCheck, ISessionItemScriptHost? scripts, ISessionItemTriggers? triggers)
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
        var points = EquipmentDomain.MovePoints(item.ScriptPoints);
        if (scripts is null && (points.Count > 0 || current?.ScriptPoints.Count > 0)) return RejectWear(GameSessionFailure.ItemScriptUnsupported);
        if (item.HasItemSpells)
        {
            var supported = triggers?.Supports(entity) == true;
            ThrowIfDisposed();
            if (!supported) return RejectWear(GameSessionFailure.ItemEffectUnsupported);
        }
        if (triggers is null && (HasItemRuns(entity) || _equipment.SlotOf(entity) is { } wornIn && _equipment.Worn[wornIn].ItemSpells ||
                current is not null && (current.ItemSpells || HasItemRuns(current.Entity))))
            return RejectWear(GameSessionFailure.ItemEffectUnsupported);

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
        // ARC-UI-019: into an empty slot the item's own move (0x5788C0) runs the insert check (IW12, IW13): the hero's load above its carry
        // weight refuses it, and a worn item moved to another slot needs a free grid slot under the Original setting (IWA1). A grid item
        // always has room (its own cells). The original puts the item back with no message.
        if (current is null)
        {
            var moved = HeroInsertVerdict(HeroOverloaded() ? SessionInsertVerdict.TooHeavy : SessionInsertVerdict.Accepted, item.Item.MagickTechComplexity,
                room: _equipment.SlotOf(entity) is null || !_rules.Items.WornMoveNeedsRoom || HeroGridRoom(entity, null));
            if (moved != SessionInsertVerdict.Accepted) return RejectWear(GameSessionFailure.InsertRefused, verdict: moved);
        }
        if (scripts is not null && (points.Count > 0 || current?.ScriptPoints.Count > 0))
            return EquipWithScripts(entity, slot, item, points, current, insertCheck, scripts, revision, item.HasItemSpells && triggers is not null);
        SessionInsertVerdict? verdict = null;
        if (current is not null)
        {
            verdict = HeroInsertVerdict(HeroOverloaded(entity) ? SessionInsertVerdict.TooHeavy
                : insertCheck(new SessionInventoryItem(current.Entity, CarriedFacts(current.Entity, current.Item.MagickTechComplexity))),
                current.Item.MagickTechComplexity, room: HeroGridRoom(current.Entity, entity));
            ThrowIfDisposed();
            if (_revision != revision) return RejectWear(GameSessionFailure.StaleVersion, verdict: verdict);
            if (verdict != SessionInsertVerdict.Accepted) return RejectWear(GameSessionFailure.InsertRefused, verdict: verdict);
        }
        if (_equipment.SlotOf(entity) is { } previous) _equipment.Worn.Remove(previous);
        _equipment.Worn[slot] = new(slot, entity, item.Item) { ItemSpells = item.HasItemSpells && triggers is not null };
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, EquipmentRefusal.None, current?.Entity, verdict, Version);
    }

    // ARC-SCRIPT-042: the move of Equip with its item events (IE3–IE6), after the checks that come before any move.
    private EquipmentMutationResult EquipWithScripts(string entity, EquipmentSlot slot, SessionWearableItem item, IReadOnlyList<int> points,
        SessionWornItem? current, SessionInsertCheck insertCheck, ISessionItemScriptHost scripts, long revision, bool itemSpells)
    {
        var previous = _equipment.SlotOf(entity);
        var items = new Dictionary<string, int>(StringComparer.Ordinal) { [entity] = item.Item.MagickTechComplexity };
        if (current is not null) items[current.Entity] = current.Item.MagickTechComplexity;
        var (map, flagVersion) = (_actor.Map, _flags.Version);
        var runs = new ItemScriptRuns(this, scripts, map.RuntimeMap, items);
        bool Stale() => _revision != revision || _actor.Map != map || _flags.Version != flagVersion;
        EquipmentMutationResult Refused(GameSessionFailure failure, SessionInsertVerdict? verdict = null) =>
            RejectWear(failure, verdict: verdict) with { Scripts = runs.Runs };

        // A ring moved between ring slots leaves with the points it was worn with.
        RemovalRuns(runs, entity, previous is { } wornIn ? _equipment.Worn[wornIn].ScriptPoints : points, previous is not null);
        SessionInsertVerdict? verdict = null;
        var rolledBack = false;
        if (current is null) InsertionRuns(runs, entity, points, wield: true);
        else
        {
            RemovalRuns(runs, current.Entity, current.ScriptPoints, worn: true);
            ThrowIfDisposed();
            if (Stale()) return Refused(GameSessionFailure.StaleVersion);
            if (runs.Failure != GameSessionFailure.None) return Refused(runs.Failure);
            verdict = HeroInsertVerdict(HeroOverloaded(entity) ? SessionInsertVerdict.TooHeavy
                : insertCheck(new SessionInventoryItem(current.Entity, CarriedFacts(current.Entity, current.Item.MagickTechComplexity))),
                current.Item.MagickTechComplexity, room: HeroGridRoom(current.Entity, entity));
            ThrowIfDisposed();
            if (Stale()) return Refused(GameSessionFailure.StaleVersion, verdict);
            if (verdict == SessionInsertVerdict.Accepted)
            {
                InsertionRuns(runs, current.Entity, current.ScriptPoints, wield: false);
                InsertionRuns(runs, entity, points, wield: true);
            }
            else
            {
                // The item goes back first, worn again when it was worn, then the occupant into the slot (0x464BC4).
                rolledBack = true;
                InsertionRuns(runs, entity, points, wield: previous is not null);
                InsertionRuns(runs, current.Entity, current.ScriptPoints, wield: true);
            }
        }
        ThrowIfDisposed();
        if (Stale()) return Refused(GameSessionFailure.StaleVersion, verdict);
        runs.Prepare();
        if (runs.Failure != GameSessionFailure.None) return Refused(runs.Failure, verdict);
        if (rolledBack && !runs.Changes)
            return new(GameSessionDisposition.NoChange, GameSessionFailure.None, EquipmentRefusal.None, null, verdict, Version)
                { Scripts = runs.Runs, Messages = runs.Messages, RolledBack = true };
        runs.Commit();
        if (!rolledBack)
        {
            if (previous is { } worn) _equipment.Worn.Remove(worn);
            _equipment.Worn[slot] = new(slot, entity, item.Item) { ScriptPoints = points, ItemSpells = itemSpells };
        }
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, EquipmentRefusal.None, rolledBack ? null : current?.Entity, verdict, Version)
            { Scripts = runs.Runs, Messages = runs.Messages, RolledBack = rolledBack };
    }

    // IE3: a removal from the hero's inventory unwields a worn item first (wield off, 0x467CB0), then runs remove item; IE4: an insertion
    // wields an item put into a wear slot (wield on, 0x4677B0) and then runs insert item. The hero is the triggerer of each, the item the
    // attachee. Only the points the item carries run.
    private static void RemovalRuns(ItemScriptRuns runs, string entity, IReadOnlyList<int> points, bool worn)
    {
        if (worn && points.Contains(SessionScriptPoint.WieldOff)) runs.Run(SessionScriptPoint.WieldOff, entity, SessionEventKinds.PlayerEntity, null);
        if (points.Contains(SessionScriptPoint.RemoveItem)) runs.Run(SessionScriptPoint.RemoveItem, entity, SessionEventKinds.PlayerEntity, null);
    }

    private static void InsertionRuns(ItemScriptRuns runs, string entity, IReadOnlyList<int> points, bool wield)
    {
        if (wield && points.Contains(SessionScriptPoint.WieldOn)) runs.Run(SessionScriptPoint.WieldOn, entity, SessionEventKinds.PlayerEntity, null);
        if (points.Contains(SessionScriptPoint.InsertItem)) runs.Run(SessionScriptPoint.InsertItem, entity, SessionEventKinds.PlayerEntity, null);
    }

    /// <summary>
    /// Takes the item in <paramref name="slot"/> off into the hero's inventory (the source's 0x464C80): NoChange for an
    /// empty slot; refused for an item with the no-drop flag (<see cref="GameSessionFailure.WearRefused"/>,
    /// <see cref="EquipmentRefusal.NotDroppable"/>), then by the insert check (<see cref="GameSessionFailure.InsertRefused"/>; a
    /// technological item for a Technophobia hero is <see cref="SessionInsertVerdict.TechnologyRefused"/>, ARC-WORLD-069), which
    /// the source asks with the item still worn (its weight counts twice near the carry limit). One revision.
    /// ARC-SCRIPT-042: with <paramref name="scripts"/>, an item with item event scripts runs them after the insert check accepts, as the
    /// original's removal and insertion do (wield off 25, remove item 28, insert item 21; ledger IE6, WE7); without a host such an item is
    /// refused (<see cref="GameSessionFailure.ItemScriptUnsupported"/>).
    /// ARC-MAGIC-020: an item whose spells run (a run with it as source) needs <paramref name="triggers"/>, else
    /// <see cref="GameSessionFailure.ItemEffectUnsupported"/>; after the move the host's unwear executor runs (0x4CBFC0,
    /// <see cref="EquipmentMutationResult.ItemSpells"/>), in the original inside the unwield before its light and scripts.
    /// ARC-UI-019: with <paramref name="gridSlot"/> the item goes to that slot of the hero's grid (the window's release of a worn item on the
    /// grid, ledger IW4): a slot outside the grid is <see cref="GameSessionFailure.InvalidItem"/>, and after the no-drop refusal its rectangle
    /// must lie inside the grid on cells no other item holds (<see cref="GameSessionFailure.GridCellsTaken"/>); without it the item takes the
    /// first free slot (<see cref="QueryHeroGrid"/>).
    /// </summary>
    [OriginalFunction(0x464C80, OriginalFunctionScope.Partial)]
    public EquipmentMutationResult Unequip(GameSessionVersion expected, EquipmentSlot slot, SessionInsertCheck insertCheck, ISessionItemScriptHost? scripts = null,
        ISessionItemTriggers? triggers = null, int? gridSlot = null)
    {
        var result = UnequipMove(expected, slot, insertCheck, scripts, triggers, gridSlot);
        if (triggers is null || result.Disposition != GameSessionDisposition.Changed || result.Displaced is not { } item) return result;
        return result with { ItemSpells = [triggers.Withdraw(ItemSpellTriggerRules.Unwear, item, SessionEventKinds.PlayerEntity)], Version = Version };
    }

    private EquipmentMutationResult UnequipMove(GameSessionVersion expected, EquipmentSlot slot, SessionInsertCheck insertCheck, ISessionItemScriptHost? scripts,
        ISessionItemTriggers? triggers, int? gridSlot)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(insertCheck);
        if (!Enum.IsDefined(slot) || gridSlot is < 0 or >= SessionHeroGrid.Slots) return RejectWear(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectWear(GameSessionFailure.StaleVersion);
        if (_hero is null) return RejectWear(GameSessionFailure.EquipmentUnavailable);
        if (!_equipment.Worn.TryGetValue(slot, out var current))
            return new(GameSessionDisposition.NoChange, GameSessionFailure.None, EquipmentRefusal.None, null, null, Version);
        if (scripts is null && current.ScriptPoints.Count > 0) return RejectWear(GameSessionFailure.ItemScriptUnsupported);
        if (triggers is null && (current.ItemSpells || HasItemRuns(current.Entity))) return RejectWear(GameSessionFailure.ItemEffectUnsupported);
        var refusal = EquipmentRules.CheckRemove(current.Item);
        if (refusal != EquipmentRefusal.None) return RejectWear(GameSessionFailure.WearRefused, refusal);
        var (width, height) = GridSize(current.Entity);
        if (gridSlot is { } at && !GridCellsFree(new(at, width, height), null)) return RejectWear(GameSessionFailure.GridCellsTaken);
        if (_revision == long.MaxValue) return RejectWear(GameSessionFailure.RevisionExhausted);
        var revision = _revision;
        var verdict = HeroInsertVerdict(HeroOverloaded() ? SessionInsertVerdict.TooHeavy
            : insertCheck(new SessionInventoryItem(current.Entity, CarriedFacts(current.Entity, current.Item.MagickTechComplexity))),
            current.Item.MagickTechComplexity, room: gridSlot is not null || HeroGridRoom(current.Entity, null));
        ThrowIfDisposed();
        if (_revision != revision) return RejectWear(GameSessionFailure.StaleVersion, verdict: verdict);
        if (verdict != SessionInsertVerdict.Accepted) return RejectWear(GameSessionFailure.InsertRefused, verdict: verdict);
        if (scripts is not null && current.ScriptPoints.Count > 0)
        {
            var (map, flagVersion) = (_actor.Map, _flags.Version);
            var runs = new ItemScriptRuns(this, scripts, map.RuntimeMap,
                new Dictionary<string, int>(StringComparer.Ordinal) { [current.Entity] = current.Item.MagickTechComplexity });
            RemovalRuns(runs, current.Entity, current.ScriptPoints, worn: true);
            InsertionRuns(runs, current.Entity, current.ScriptPoints, wield: false);
            ThrowIfDisposed();
            if (_revision != revision || _actor.Map != map || _flags.Version != flagVersion)
                return RejectWear(GameSessionFailure.StaleVersion, verdict: verdict) with { Scripts = runs.Runs };
            runs.Prepare();
            if (runs.Failure != GameSessionFailure.None) return RejectWear(runs.Failure, verdict: verdict) with { Scripts = runs.Runs };
            runs.Commit();
            _equipment.Worn.Remove(slot);
            if (gridSlot is { } into) _inventory.Grid[current.Entity] = new(current.Entity, into, width, height);
            Revise();
            return new(GameSessionDisposition.Changed, GameSessionFailure.None, EquipmentRefusal.None, current.Entity, verdict, Version)
                { Scripts = runs.Runs, Messages = runs.Messages };
        }
        _equipment.Worn.Remove(slot);
        if (gridSlot is { } target) _inventory.Grid[current.Entity] = new(current.Entity, target, width, height);
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, EquipmentRefusal.None, current.Entity, verdict, Version);
    }

    // ARC-MAGIC-020: whether a spell run has the item as its source (its wear spell runs, or ran and is still in the table).
    private bool HasItemRuns(string entity) => _spellRuns.Runs.Any(run => run.Source?.Id == entity);

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
    private SessionItemFacts CarriedFacts(string entity, int complexity = 0) => new(HeldKind(entity)!.Value, 0, complexity, -1, null, []);

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
            // ARC-SCRIPT-042: item event points only, strictly ascending.
            if (worn.ScriptPoints is null || !worn.ScriptPoints.SequenceEqual(EquipmentDomain.MovePoints(worn.ScriptPoints)) ||
                worn.ScriptPoints.Distinct().Count() != worn.ScriptPoints.Count) return GameSessionFailure.InvalidEquipment;
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
