namespace Arcanum.Simulation.Equipment;

/// <summary>
/// ARC-RENDER-037: the weapon and shield a critter shows, and the attack animation it plays, as the retail 1.0.7.4 executable sets them
/// (ledger WA1–WA10). A critter shows its weapon only in combat mode: the weapon art (0x465020) gives the art id the wielded weapon's
/// use-art subtype (1 bare-handed) and, for an armour item in the shield slot, the shield bit; out of combat mode both are 0. The
/// combat-mode toggle (0x4B3F80) and the wield-on and wield-off hooks (BA1) set it. The attack animation (the attempt-attack goal's
/// node 0x42B9C0) is the throw for a returning (throwing-skill) weapon, the low attack against a shrunk, falling or short target, else
/// the attack. Which file an art id names (the missing-art repair, the per-animation clearing of MA1–MA3) is the art owner's.
/// </summary>
public static class WeaponArtRules
{
    /// <summary>The weapon parts an art id holds (bits 0..3: 0 none, 1 bare hands, 2..14 the weapon art subtypes).</summary>
    public const int WeaponParts = 15;
    /// <summary>Bare hands: the weapon part of a critter in combat mode without a weapon.</summary>
    public const int Unarmed = 1;
    /// <summary>The animations of an attack: the throw (14), the attack (20) and the low attack (21).</summary>
    public const int ThrowAnimation = 14, AttackAnimation = 20, LowAttackAnimation = 21;
    /// <summary>The armour item type (object field 0x14F) a shield-slot item must have to show as a shield.</summary>
    public const int ArmorItemType = 7;
    /// <summary>Object flag 0x80 (shrunk) of the target (object field 0x13).</summary>
    public const uint ShrunkFlag = 0x80;
    /// <summary>Weapon flag 0x40 (field 0x70): a returning weapon, thrown with the throwing skill (0x465D80).</summary>
    public const uint ReturningWeaponFlag = 0x40;

    // 0x503F60's tables: a critter's height by body (0x5BE980; the critter branch reads entry 0, see Height) and a monster's by
    // species (0x5BE994, bits 23..27): 0 short, 1 middle, 2 tall.
    private static readonly int[] CritterHeights = [1, 0, 0, 2, 1];
    private static readonly int[] MonsterHeights =
        [0, 0, 1, 2, 1, 1, 1, 1, 2, 0, 0, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 1, 1, 0, 2, 2, 2, 1, 1, 1];

    private static bool HoldsWeapon(uint art) => (art >> 28) is BodyArmorArtRules.CritterArt or BodyArmorArtRules.MonsterArt or BodyArmorArtRules.UniqueNpcArt;

    /// <summary>The art with its weapon part set (0x504100): bits 0..3 of a critter, monster or unique NPC art id; a part of 15 or more
    /// (or below 0) sets 0 (the original logs it); another art type is returned unchanged.</summary>
    [OriginalFunction(0x504100, OriginalFunctionScope.Full)]
    public static uint WithWeapon(uint art, int weapon) =>
        HoldsWeapon(art) ? (art & ~15u) | (uint)(weapon is >= 0 and < WeaponParts ? weapon : 0) : art;

    /// <summary>The art with its shield bit set (0x504180): bit 19 of a critter, monster or unique NPC art id; a value of 2 or more (or
    /// below 0) sets 0 (the original logs it); another art type is returned unchanged.</summary>
    [OriginalFunction(0x504180, OriginalFunctionScope.Full)]
    public static uint WithShield(uint art, int shield) =>
        HoldsWeapon(art) ? (art & ~(1u << 19)) | ((uint)(shield is 0 or 1 ? shield : 0) << 19) : art;

    /// <summary>An art id's weapon part (bits 0..3).</summary>
    public static int Weapon(uint art) => (int)(art & 15);

    /// <summary>An art id's shield bit (bit 19).</summary>
    public static int Shield(uint art) => (int)((art >> 19) & 1);

    /// <summary>The weapon part a critter shows (0x465020): 0 out of combat mode; in it the use-art subtype of the item in the weapon
    /// slot (<see cref="BodyArmorArtRules.ItemArmor"/>'s bits 6..9, whatever the item's type), bare hands (1) with no item, and also 1
    /// for an item whose subtype is 0 (the original logs that).</summary>
    /// <param name="weaponUseArt">The weapon slot's item's use art (item field 0x5F), or null for an empty slot.</param>
    public static int WeaponPart(bool combatMode, uint? weaponUseArt) =>
        !combatMode ? 0 : weaponUseArt is { } use && BodyArmorArtRules.ItemArmor(use) is > 0 and var part ? part : Unarmed;

    /// <summary>The shield bit a critter shows (0x465020): 1 in combat mode with an armour item (type 7) in the shield slot, else 0.</summary>
    /// <param name="shieldItemType">The shield slot's item's type (object field 0x14F), or null for an empty slot.</param>
    public static int ShieldPart(bool combatMode, int? shieldItemType) => combatMode && shieldItemType == ArmorItemType ? 1 : 0;

    /// <summary>
    /// The critter's art for its weapon and shield slots (0x465020): <paramref name="currentArt"/> with <see cref="WeaponPart"/> and
    /// <see cref="ShieldPart"/>. The combat-mode toggle sets it after changing the mode, the wield-on and wield-off hooks after a weapon
    /// or shield change.
    /// </summary>
    [OriginalFunction(0x465020, OriginalFunctionScope.Full)]
    public static uint WieldedArt(uint currentArt, bool combatMode, uint? weaponUseArt, int? shieldItemType) =>
        WithShield(WithWeapon(currentArt, WeaponPart(combatMode, weaponUseArt)), ShieldPart(combatMode, shieldItemType));

    /// <summary>A target art's height (0x503F60): a critter's is 1 (the body table's first entry: the body lookup 0x502710 answers 0 for
    /// every critter art), a monster's its species' (bits 23..27) in the species table (0 short, 1 middle, 2 tall), any other art 1.</summary>
    [OriginalFunction(0x503F60, OriginalFunctionScope.Full)]
    public static int Height(uint art) => (art >> 28) switch
    {
        BodyArmorArtRules.CritterArt => CritterHeights[0],
        BodyArmorArtRules.MonsterArt => MonsterHeights[(art >> 23) & 31],
        _ => 1,
    };

    /// <summary>The animation of an art (0x503E20): bits 6..10 of a critter, monster or unique NPC art id, 0 for any other art type
    /// (the attack animation's target test, ARC-RENDER-039).</summary>
    [OriginalFunction(0x503E20, OriginalFunctionScope.Full)]
    public static int AnimationOf(uint art) => HoldsWeapon(art) ? (int)((art >> 6) & 31) : 0;

    /// <summary>
    /// The animation of an attack (the attempt-attack goal's node 0x42B9C0, set on the attacker's art only in combat mode): the throw
    /// (14) when the wielded weapon has the returning flag 0x40; else the low attack (21) when the target is shrunk (object flag 0x80),
    /// plays an animation 7..11 (falling, lying or getting up) or is short (<see cref="Height"/> 0); else the attack (20). An art the
    /// critter lacks fails the goal in the original (it logs the art); the missing-art repair turns a missing low attack into the
    /// attack when the art is resolved (MA1).
    /// </summary>
    /// <param name="weaponFlags">The wielded weapon's flags (field 0x70), 0 bare-handed.</param>
    /// <param name="targetObjectFlags">The target's object flags (field 0x13).</param>
    /// <param name="targetAnimation">The animation of the target's current art (<see cref="AnimationOf"/>).</param>
    /// <param name="targetArt">The target's current art (field 1), for its height.</param>
    [OriginalFunction(0x42B9C0, OriginalFunctionScope.Partial)]
    public static int Animation(uint weaponFlags, uint targetObjectFlags, int targetAnimation, uint targetArt) =>
        (weaponFlags & ReturningWeaponFlag) != 0 ? ThrowAnimation
        : (targetObjectFlags & ShrunkFlag) != 0 || targetAnimation is >= 7 and <= 11 || Height(targetArt) == 0 ? LowAttackAnimation
        : AttackAnimation;
}
