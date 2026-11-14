using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;

namespace Arcanum.Session;

/// <summary>
/// One item in a wear slot, for the wear step (ARC-COMBAT-007): its entity, its durability baseline
/// (<see cref="SessionItemConditions.FromCombatFields"/>), whether the object is invulnerable (object flag 0x400000), which only
/// the critical weapon effects test (armour wear and the attacker's weapon wear do not), and a weapon's art subtype (bits 6..9 of
/// its use-art fragment, 0..15), which the attacker's weapon wear reads (<see cref="CombatWeapon.ArtSubtype"/>). Like the baseline,
/// these are the item's data, which the host passes at each attack; the session saves none of them. <see cref="FromCombatFields"/>
/// builds one from an item's stored fields.
/// </summary>
public sealed record SessionWearItem(string Entity, ItemCondition Baseline)
{
    private const uint InvulnerableObject = 0x400000, NoDropFlag = 0x20;

    public bool Invulnerable { get; init; }
    public int ArtSubtype { get; init; }
    /// <summary>ARC-DEV-282: the weapon's projectile art (weapon field 127, ledger PF1; null for none, -1, and for a non-weapon): a shot on the ranged path with it flies as a projectile.</summary>
    public uint? MissileArt { get; init; }
    /// <summary>The item's no-drop flag (item flag 0x20): the original's drop refuses it, also a critical's (ARC-COMBAT-009).</summary>
    public bool NoDrop { get; init; }
    /// <summary>ARC-COMBAT-1930 (0x4B2810): the loudness an attack event of this item as the weapon carries (<see cref="AwarenessRules.WeaponLoudness"/>): loud for weapon flag 0x1, silent for 0x2, else normal (also a non-weapon).</summary>
    public Loudness Noise { get; init; } = Loudness.Normal;

    /// <summary>The wear values of an item from its stored fields (ARC-DATA-021); null for an object that is not an item or a
    /// condition outside the durability rules' bounds.</summary>
    public static SessionWearItem? FromCombatFields(string entity, Arcanum.Data.Combat.CombatFields fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if (fields.Item is not { } item || SessionItemConditions.FromCombatFields(fields) is not { } baseline) return null;
        return new(entity, baseline)
        {
            Invulnerable = (fields.Object.Flags & InvulnerableObject) != 0,
            ArtSubtype = fields.Weapon is null ? 0 : Arcanum.Data.Combat.CombatItemArt.Subtype(item.UseArtFragment),
            NoDrop = (item.Flags & NoDropFlag) != 0,
            Noise = AwarenessRules.WeaponLoudness(fields.Weapon is { } noisy ? (uint)noisy.Flags : null),
            MissileArt = fields.Weapon is { } weapon && weapon.MissileArt != Arcanum.Data.Combat.CombatWeaponFields.NoMissileArt ? unchecked((uint)weapon.MissileArt) : null,
        };
    }
}

/// <summary>
/// One side of an attack for its wear step. <see cref="Life"/> is the critter (the hero or an NPC), null for an attacked object
/// that is no critter. <see cref="Worn"/> holds the items in its wear slots with their baselines; an empty slot takes no wear. For
/// the hero it must name exactly the items the hero wears in the session; an item the session deleted (an earlier attack's second
/// break or destroying critical) is refused, so a host leaves those out (<see cref="GameSessionOwner.IsItemDestroyed"/>). Null means the host passes no item data: every item step
/// of that side is skipped (<see cref="SessionWearSkip.ItemsUnknown"/>), the bare-handed attacker's own wear still applies.
/// </summary>
public sealed record SessionWearSide
{
    public SessionLifeTarget? Life { get; init; }
    public IReadOnlyDictionary<EquipmentSlot, SessionWearItem>? Worn { get; init; }
}

/// <summary>
/// The wear step of one resolved attack (<see cref="GameSessionOwner.ApplyAttackWear"/>): the resolution, its attacker (always a
/// critter) and its target. <see cref="TargetBroken"/> says, for an object target, that the hit broke it: the original wears the
/// attacker's weapon or hands only on an object the hit left whole. A critter target never sets it.
/// </summary>
public sealed record SessionAttackWear
{
    public required AttackResolution Resolution { get; init; }
    public required SessionWearSide Attacker { get; init; }
    public required SessionWearSide Target { get; init; }
    public bool TargetBroken { get; init; }
}

/// <summary>One step of the wear, in the original's order.</summary>
public enum SessionWearStepKind
{
    /// <summary>The armour at the hit location of whoever took the damage (<see cref="CombatWearRules.ArmorWear"/>).</summary>
    Armor,
    /// <summary>The critical "damage weapon" on that critter's wielded weapon.</summary>
    WeaponDamage,
    /// <summary>The critical "destroy weapon" or "explode weapon": that critter's wielded weapon is deleted without a break.</summary>
    WeaponDestroy,
    /// <summary>The attacker's wielded weapon wears by the struck one's material.</summary>
    AttackerWeapon,
    /// <summary>The bare-handed attacker loses hit points, never its last one.</summary>
    AttackerHands,
}

/// <summary>Why a step the resolution asked for changed nothing.</summary>
public enum SessionWearSkip
{
    None,
    /// <summary>The slot is empty.</summary>
    NoItem,
    /// <summary>The side's items are not known (<see cref="SessionWearSide.Worn"/> is null).</summary>
    ItemsUnknown,
    /// <summary>An invulnerable weapon is passed by the critical weapon effects.</summary>
    Invulnerable,
    /// <summary>An earlier step of this wear already deleted the item.</summary>
    ItemDeleted,
    /// <summary>The hit broke the object target, so the attacker's side does not wear.</summary>
    TargetBroken,
    /// <summary>The bare-handed attacker has no hit point it may lose (1 or fewer left).</summary>
    NoHitPoints,
}

/// <summary>
/// One wear step: what it was, whose (<see cref="Wearer"/>: the damage recipient for the first three kinds, the attacker for the
/// last two), the slot and item, the amount the resolution asked for, why it was skipped, and the item's durability result (its
/// break step and, for the hero, the slot it left) or the attacker's life outcome (whose hit-point damage is the capped amount). A first break of an item an NPC wears is reported here: taking it off,
/// the bust sound and the BUST script are the host's.
/// </summary>
public sealed record SessionWearStep(SessionWearStepKind Kind, DamageRecipient Wearer, EquipmentSlot? Slot, string? Entity, int Amount,
    SessionWearSkip Skip, ItemDurabilityResult? Item, LifeOutcome? Life);

/// <summary>
/// The result of <see cref="GameSessionOwner.ApplyAttackWear"/>: every step the resolution asked for, in order. A step that was
/// refused (a full record budget, the revision) ends the wear: when steps before it changed something, they stay applied (each is its
/// own revision), the wear is Changed and the refusal is <see cref="StepFailure"/>; when nothing changed, the wear is Rejected with
/// that refusal as <see cref="Failure"/>.
/// </summary>
public sealed record SessionAttackWearResult(GameSessionDisposition Disposition, GameSessionFailure Failure, IReadOnlyList<SessionWearStep> Steps,
    GameSessionFailure StepFailure, GameSessionVersion Version);

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-COMBAT-007: the armour and weapon wear of one resolved attack (<see cref="AttackResolution.Wear"/>), for any attacker and
    /// recipient: the hero's attack (<see cref="AttackCritter"/> calls it) and, later, an NPC's. In the original's order
    /// (0x4B4390): the armour at the hit location of whoever took the damage loses 10 (<see cref="DamageItem"/>, which breaks it at
    /// 0 hit points, and a broken one is deleted), that critter's wielded weapon takes the critical "damage weapon" (10) and then
    /// "destroy weapon" or "explode weapon" (<see cref="DestroyItem"/>) unless it is invulnerable, and last the attacker's side
    /// wears by the struck one's material: its wielded weapon (<see cref="DamageItem"/>), or, bare-handed, its own hit points
    /// (<see cref="CombatWearRules.WearHands"/>, never the last one). The original applies the armour and critical weapon steps
    /// before the hit-point damage and the attacker's side after the damage, experience and death; the item and life domains do
    /// not read each other, so a caller that applies the damage first (as <see cref="AttackCritter"/> does) reaches the same state.
    /// Refused before any change: an invalid side (no attacker critter, the same critter on both sides, a hero side whose items are
    /// not exactly the hero's worn items, an NPC or object side naming an item of the hero's, items on a side that is no critter, an
    /// invalid entity, baseline or art subtype, an entity in two slots, an item the session does not track or deleted, a weapon wear
    /// without the attacker's weapon, armour or weapon steps on a wearer that is no critter, a broken critter target, a life target
    /// the session cannot resolve: <see cref="GameSessionFailure.InvalidWear"/>), the version. No wear at all is NoChange.
    /// </summary>
    public SessionAttackWearResult ApplyAttackWear(GameSessionVersion expected, SessionAttackWear wear)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(wear);
        ArgumentNullException.ThrowIfNull(wear.Resolution);
        if (!ValidWear(wear.Attacker, wear.Target, wear.TargetBroken, wear.Resolution.Wear)) return RejectAttackWear(GameSessionFailure.InvalidWear);
        if (!IsCurrent(expected)) return RejectAttackWear(GameSessionFailure.StaleVersion);
        return WearSteps(wear);
    }

    // The sides hold together: an attacker critter, valid items each in one slot only, the hero's items exactly its worn ones, an
    // attacker weapon to wear when the wear names one, and a broken target only for an object.
    private bool ValidWear(SessionWearSide? attacker, SessionWearSide? target, bool targetBroken, AttackWear? wear)
    {
        if (attacker?.Life is null || target is null) return false;
        if (targetBroken && target.Life is not null) return false;
        if (target.Life is null && target.Worn is not null) return false;
        if (Resolve(attacker.Life, out _, out _, out _) != GameSessionFailure.None ||
            (target.Life is not null && Resolve(target.Life, out _, out _, out _) != GameSessionFailure.None)) return false;
        if (target.Life is not null && attacker.Life.IsHero == target.Life.IsHero &&
            (attacker.Life.IsHero || string.Equals(attacker.Life.Entity, target.Life.Entity, StringComparison.Ordinal))) return false;
        if (wear is not null)
        {
            var wearerIsCritter = wear.Wearer == DamageRecipient.Attacker || target.Life is not null;
            if (wear.WearerIsCritter != wearerIsCritter) return false;
            if (!wear.WearerIsCritter && (wear.ArmorDamage > 0 || wear.WeaponDamage > 0 || wear.DestroyWeapon)) return false;
        }
        if (!ValidSideItems(attacker, target)) return false;
        return wear?.AttackerWear != AttackerWearKind.Weapon || attacker.Worn is null || attacker.Worn.ContainsKey(EquipmentSlot.Weapon);
    }

    // The items of the sides hold together: valid, each in one slot only and tracked, an NPC's not dropped or the hero's, the hero's exactly its worn ones.
    private bool ValidSideItems(params SessionWearSide[] sides)
    {
        var entities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var side in sides)
        {
            if (side.Worn is not { } worn) continue;
            var hero = side.Life!.IsHero;
            foreach (var (slot, item) in worn)
            {
                if (!Enum.IsDefined(slot) || item is null || !SessionValidation.Entity(item.Entity) || !ItemDurabilityRules.IsValid(item.Baseline) ||
                    item.ArtSubtype is < 0 or > CombatWearRules.MaxArtSubtype || !entities.Add(item.Entity) || !TracksItem(item.Entity)) return false;
                // A critical dropped it (ARC-COMBAT-009): it lies on the ground.
                if (!hero && Dropped(item.Entity)) return false;
                // An NPC never wears an item of the hero's.
                if (!hero && (_equipment.SlotOf(item.Entity) is not null || _inventory.Hero.InPack(item.Entity) ||
                        _inventory.PlacedItem(item.Entity) is not null)) return false;
            }
            if (hero && (_hero is null || worn.Count != _equipment.Worn.Count ||
                    _equipment.Worn.Any(pair => !worn.TryGetValue(pair.Key, out var item) || item.Entity != pair.Value.Entity))) return false;
        }
        return true;
    }

    /// <summary>
    /// ARC-DEV-245 (MIG13 S14i, GT195): the armour wear of a damage that has no attacker (a script's fire damage, GT195: "fire damage wears the torso armour (+10)"): the armour at the torso slot of <paramref name="wearer"/> (a critter, the hero or an NPC)
    /// loses <see cref="CombatWearRules.ArmorWear"/> through <see cref="DamageItem"/> (a break takes it off the hero's slot; an NPC's broken item is no longer worn, the host's). The host passes the wearer's worn items as for an attack's wear; a wearer
    /// with no items known or no armour is NoChange. Refused like <see cref="ApplyAttackWear"/> (<see cref="GameSessionFailure.InvalidWear"/>, the version).
    /// </summary>
    public SessionAttackWearResult ApplyArmorWear(GameSessionVersion expected, SessionWearSide wearer)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(wearer);
        if (wearer.Life is null || Resolve(wearer.Life, out _, out _, out _) != GameSessionFailure.None || !ValidSideItems(wearer)) return RejectAttackWear(GameSessionFailure.InvalidWear);
        if (!IsCurrent(expected)) return RejectAttackWear(GameSessionFailure.StaleVersion);
        var wear = new AttackWear(DamageRecipient.Target, true, EquipmentSlot.Armor, CombatWearRules.ArmorWear, 0, false, false, AttackerWearKind.None, 0, false);
        return WearSteps(wear, wearer, wearer, false);
    }

    private SessionAttackWearResult WearSteps(SessionAttackWear request) => WearSteps(request.Resolution.Wear, request.Attacker, request.Target, request.TargetBroken);

    private SessionAttackWearResult WearSteps(AttackWear? asked, SessionWearSide attacker, SessionWearSide target, bool targetBroken)
    {
        var steps = new List<SessionWearStep>();
        if (asked is not { Any: true } wear)
            return new(GameSessionDisposition.NoChange, GameSessionFailure.None, steps, GameSessionFailure.None, Version);
        var wearer = wear.Wearer == DamageRecipient.Target ? target : attacker;
        var changed = false;
        var failure = GameSessionFailure.None;

        // One item step: skipped (no item data, an empty slot, an item an earlier step deleted) or written.
        bool Item(SessionWearStepKind kind, DamageRecipient whose, SessionWearSide side, EquipmentSlot slot, int amount, bool testInvulnerable,
            Func<string, ItemCondition, ItemDurabilityResult> write)
        {
            SessionWearItem? item = null;
            var skip = side.Worn is not { } worn ? SessionWearSkip.ItemsUnknown
                : !worn.TryGetValue(slot, out item) ? SessionWearSkip.NoItem
                : testInvulnerable && item.Invulnerable ? SessionWearSkip.Invulnerable
                : _durability.Destroyed.Contains(item.Entity) ? SessionWearSkip.ItemDeleted
                : SessionWearSkip.None;
            if (skip != SessionWearSkip.None)
            {
                steps.Add(new(kind, whose, slot, item?.Entity, amount, skip, null, null));
                return true;
            }
            var result = write(item!.Entity, item.Baseline);
            steps.Add(new(kind, whose, slot, item.Entity, amount, SessionWearSkip.None, result, null));
            if (result.Disposition == GameSessionDisposition.Rejected) { failure = result.Failure; return false; }
            changed |= result.Disposition == GameSessionDisposition.Changed;
            return true;
        }

        var proceed = true;
        if (wear.ArmorDamage > 0)
            proceed = Item(SessionWearStepKind.Armor, wear.Wearer, wearer, wear.ArmorSlot, wear.ArmorDamage, testInvulnerable: false,
                (entity, baseline) => DamageItem(Version, entity, baseline, wear.ArmorDamage));
        if (proceed && wear.WeaponDamage > 0)
            proceed = Item(SessionWearStepKind.WeaponDamage, wear.Wearer, wearer, EquipmentSlot.Weapon, wear.WeaponDamage, testInvulnerable: true,
                (entity, baseline) => DamageItem(Version, entity, baseline, wear.WeaponDamage));
        if (proceed && wear.DestroyWeapon)
            proceed = Item(SessionWearStepKind.WeaponDestroy, wear.Wearer, wearer, EquipmentSlot.Weapon, 0, testInvulnerable: true,
                (entity, baseline) => DestroyItem(Version, entity, baseline));

        if (proceed && wear.AttackerWear != AttackerWearKind.None)
        {
            var kind = wear.AttackerWear == AttackerWearKind.Weapon ? SessionWearStepKind.AttackerWeapon : SessionWearStepKind.AttackerHands;
            if (wear.AttackerWearNeedsWholeTarget && targetBroken)
                steps.Add(new(kind, DamageRecipient.Attacker, kind == SessionWearStepKind.AttackerWeapon ? EquipmentSlot.Weapon : null, null,
                    wear.AttackerWearAmount, SessionWearSkip.TargetBroken, null, null));
            else if (kind == SessionWearStepKind.AttackerWeapon)
                Item(kind, DamageRecipient.Attacker, attacker, EquipmentSlot.Weapon, wear.AttackerWearAmount, testInvulnerable: false,
                    (entity, baseline) => DamageItem(Version, entity, baseline, wear.AttackerWearAmount));
            else
            {
                var amount = wear.AttackerWearAmount;
                var life = Life(Version, attacker.Life!, amount is >= 0 and <= LifeLimits.MaxAmount,
                    (state, facts, _) => CombatWearRules.WearHands(state, facts, amount));
                var skip = life.Disposition == GameSessionDisposition.NoChange ? SessionWearSkip.NoHitPoints : SessionWearSkip.None;
                steps.Add(new(kind, DamageRecipient.Attacker, null, null, amount, skip, null, life.Outcome));
                if (life.Disposition == GameSessionDisposition.Rejected) failure = life.Failure;
                changed |= life.Disposition == GameSessionDisposition.Changed;
            }
        }
        if (!changed && failure != GameSessionFailure.None) return new(GameSessionDisposition.Rejected, failure, steps, GameSessionFailure.None, Version);
        return new(changed ? GameSessionDisposition.Changed : GameSessionDisposition.NoChange, GameSessionFailure.None, steps, failure, Version);
    }

    private SessionAttackWearResult RejectAttackWear(GameSessionFailure failure) =>
        new(GameSessionDisposition.Rejected, failure, [], GameSessionFailure.None, Version);
}
