namespace Arcanum.Simulation.Combat;

/// <summary>The weapon skill an attack is resolved with.</summary>
public enum CombatSkill { Melee, Bow, Throwing, Firearms }

/// <summary>Training level of a skill; the source orders the four levels None &lt; Apprentice &lt; Expert &lt; Master.</summary>
public enum SkillTraining { None, Apprentice, Expert, Master }

/// <summary>Hit location. An attack aimed at the torso does not exist in the source: torso means "not aimed".</summary>
public enum HitLocation { Torso, Head, Arm, Leg }

/// <summary>The five damage types, in the source's order.</summary>
public enum DamageType { Normal, Poison, Electrical, Fire, Fatigue }

/// <summary>Game difficulty; a PC's effectiveness, a PC attacker's hit-point damage and the PC's experience awards
/// (ARC-WORLD-030) depend on it.</summary>
public enum GameDifficulty { Normal, Easy, Hard }

/// <summary>What is attacked. Only a critter makes a skill roll, dodges or takes critical effects.</summary>
public enum AttackTargetKind { Critter, Portal, Container, Wall, OtherObject }

/// <summary>Object material; a portal or container subtracts its material reduction from hit-point damage.</summary>
public enum ObjectMaterial { Stone, Brick, Wood, Plant, Flesh, Metal, Glass, Cloth, Liquid, Paper, Gas, Force, Fire, Powder }

/// <summary>
/// What the attacker strikes with: bare hands or a monster's natural attack (<see cref="Unarmed"/>), a weapon item, or a
/// thrown item that is not a weapon (no to-hit bonus and no magic adjustments).
/// </summary>
public enum WeaponKind { Unarmed, Weapon, ThrownItem }

/// <summary>
/// The caller's backstab decision (see <see cref="AttackRules.Backstab"/>). A true backstab is one the target can
/// neither see nor hear; it skips armour, adds critical chance and five times the backstab damage.
/// </summary>
public enum BackstabKind { None, Backstab, TrueBackstab }

/// <summary>Weapon classes the backstab rule distinguishes.</summary>
public enum BackstabWeaponClass { Dagger, Sword, Axe, TwoHandedSword, Other }

/// <summary>The light a critter itself emits; it shrinks the darkness penalty of its attacks.</summary>
public enum LightEmission { None, Small, Medium, Large, ExtraLarge }

/// <summary>A weapon's critical-hit chart; bare hands and non-weapon items count as <see cref="Crushing"/>.</summary>
public enum CriticalHitChart { Cutting, Crushing, Impaling, Electrical, Fire }

/// <summary>A weapon's critical-miss chart; bare hands count as <see cref="Hands"/>, a non-weapon item as <see cref="Bludgeon"/>.</summary>
public enum CriticalMissChart { Bladed, Bludgeon, Handled, Guns, Bows, Hands, Explosive, Fire, Electrical }

/// <summary>Unarmed damage spells on the attacker; the source applies the first that is active, in this order.</summary>
public enum UnarmedBodySpell { None, BodyOfEarth, BodyOfFire, BodyOfWater, HardenedHands }

/// <summary>
/// Combat-relevant states of a combatant. <see cref="Unconscious"/> is the caller's resolution of "fatigue at most 0
/// and not undead"; <see cref="FallingDown"/> means the critter is playing its fall-down animation.
/// </summary>
[Flags]
public enum CombatantConditions
{
    None = 0,
    Dead = 1 << 0,
    Unconscious = 1 << 1,
    Paralyzed = 1 << 2,
    Stunned = 1 << 3,
    Stoned = 1 << 4,
    Sleeping = 1 << 5,
    FallingDown = 1 << 6,
    Blinded = 1 << 7,
    CrippledArmOne = 1 << 8,
    CrippledArmsBoth = 1 << 9,
    CrippledLegsBoth = 1 << 10,
    Undead = 1 << 11,
    Mechanical = 1 << 12,
    FatigueImmune = 1 << 13,
    FatigueLimiting = 1 << 14,
    FatigueDraining = 1 << 15,
    Invulnerable = 1 << 16,
    NighInvulnerable = 1 << 17,
    MagneticInversion = 1 << 18,
    DarkSight = 1 << 19,
}

/// <summary>An inclusive damage range.</summary>
public readonly record struct DamageRange(int Min, int Max)
{
    public static readonly DamageRange Zero = new(0, 0);
}

/// <summary>A damage range per damage type.</summary>
public readonly record struct DamageRanges(
    DamageRange Normal, DamageRange Poison, DamageRange Electrical, DamageRange Fire, DamageRange Fatigue)
{
    public static readonly DamageRanges None =
        new(DamageRange.Zero, DamageRange.Zero, DamageRange.Zero, DamageRange.Zero, DamageRange.Zero);

    public DamageRange this[DamageType type] => type switch
    {
        DamageType.Normal => Normal,
        DamageType.Poison => Poison,
        DamageType.Electrical => Electrical,
        DamageType.Fire => Fire,
        DamageType.Fatigue => Fatigue,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
}

/// <summary>An amount per damage type.</summary>
public readonly record struct DamageByType(int Normal, int Poison, int Electrical, int Fire, int Fatigue)
{
    public static readonly DamageByType Zero = new(0, 0, 0, 0, 0);

    public int this[DamageType type] => type switch
    {
        DamageType.Normal => Normal,
        DamageType.Poison => Poison,
        DamageType.Electrical => Electrical,
        DamageType.Fire => Fire,
        DamageType.Fatigue => Fatigue,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    public DamageByType With(DamageType type, int value) => type switch
    {
        DamageType.Normal => this with { Normal = value },
        DamageType.Poison => this with { Poison = value },
        DamageType.Electrical => this with { Electrical = value },
        DamageType.Fire => this with { Fire = value },
        DamageType.Fatigue => this with { Fatigue = value },
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
}

/// <summary>
/// Effective resistances in percent: base + worn armour + armour magic (+ 5 × (CN − 4) for poison when positive) +
/// effects, clamped 0..95 except for monstrous NPCs (ARC-RES-030 C19). Fatigue damage uses ¾ of <see cref="Normal"/>.
/// </summary>
public readonly record struct DamageResistances(int Normal, int Poison, int Electrical, int Fire)
{
    public static readonly DamageResistances None = new(0, 0, 0, 0);
}

/// <summary>
/// One side of an attack with every value already effective (stats, effects, worn items and aptitude scaling resolved by
/// the caller). The same record serves the attacker, the target and the attacker hit by its own critical miss; a
/// non-critter target reads only <see cref="Resistances"/>, <see cref="Conditions"/> and <see cref="Material"/>. The
/// four <c>Critical…Adjustment</c> values are additive stand-ins for the source's effect adjustments of the critical
/// hit chance, critical miss chance, critical hit effect chance and critical miss effect chance.
/// </summary>
public sealed record Combatant
{
    /// <summary>A player character: gets difficulty scaling, fate and the NPC-hits-PC critical rules.</summary>
    public bool IsPc { get; init; }
    public int Level { get; init; } = 1;
    public int Strength { get; init; } = 8;
    public int Perception { get; init; } = 8;
    public bool ExtraordinaryIntelligence { get; init; }
    /// <summary>Effective DamageBonus stat, added to melee normal and fatigue damage.</summary>
    public int DamageBonus { get; init; }
    /// <summary>Magick/tech aptitude, −100 (technological) .. 100 (magickal).</summary>
    public int MagickTechAptitude { get; init; }
    /// <summary>Effective armour class 0..95 (see <see cref="AttackRules.ArmorClass"/>).</summary>
    public int ArmorClass { get; init; }
    public DamageResistances Resistances { get; init; } = DamageResistances.None;
    public CombatantConditions Conditions { get; init; }
    public LightEmission LightEmission { get; init; }
    /// <summary>Light level at the combatant's position, 0 (dark) .. 255 (bright).</summary>
    public int LightLevel { get; init; } = 255;
    /// <summary>Effective dodge skill level 0..20.</summary>
    public int DodgeLevel { get; init; }
    public SkillTraining DodgeTraining { get; init; }
    /// <summary>Effective backstab skill level 0..20.</summary>
    public int BackstabLevel { get; init; }
    public SkillTraining BackstabTraining { get; init; }
    public int CriticalHitChanceAdjustment { get; init; }
    public int CriticalMissChanceAdjustment { get; init; }
    public int CriticalEffectChanceAdjustment { get; init; }
    public int CriticalMissEffectChanceAdjustment { get; init; }
    public UnarmedBodySpell UnarmedBodySpell { get; init; }
    /// <summary>PC fate "critical hit" is armed; consumed when this PC attacks.</summary>
    public bool CriticalHitFateArmed { get; init; }
    /// <summary>PC fate "critical miss" is armed; consumed when a non-PC attacks this PC.</summary>
    public bool CriticalMissFateArmed { get; init; }
    /// <summary>Material of a non-critter target; only portals and containers use it.</summary>
    public ObjectMaterial Material { get; init; } = ObjectMaterial.Flesh;
}

/// <summary>
/// What the attacker strikes with, its values effective for this wielder: the four <c>Magic…</c> values and
/// <see cref="MagicDamageAdjustment"/> are the item's magic adjustments after the source's aptitude scaling; they are
/// dropped when the weapon's magic is cancelled. <see cref="Range"/> is the weapon range, or the throwing distance for
/// any throwing attack (thrown weapons included). <see cref="Hp"/> and <see cref="MaxHp"/> are the item's condition (0/0 for none). The two charts are
/// read only for <see cref="WeaponKind.Weapon"/>.
/// </summary>
public sealed record CombatWeapon
{
    public WeaponKind Kind { get; init; }
    /// <summary>Base damage per type: the weapon's or thrown item's ranges, or the unarmed or monster table.</summary>
    public DamageRanges Damage { get; init; } = DamageRanges.None;
    public int Range { get; init; } = 1;
    public int ToHitBonus { get; init; }
    public int MinimumStrength { get; init; }
    /// <summary>Magick/tech complexity: positive magickal, negative technological, 0 neutral.</summary>
    public int MagickTechComplexity { get; init; }
    public int MagicToHitAdjustment { get; init; }
    public int MagicCriticalHitChance { get; init; }
    public int MagicCriticalMissChance { get; init; }
    public int MagicCriticalEffectChance { get; init; }
    public int MagicCriticalMissEffectChance { get; init; }
    public CriticalHitChart CriticalHitChart { get; init; } = CriticalHitChart.Crushing;
    public CriticalMissChart CriticalMissChart { get; init; } = CriticalMissChart.Bludgeon;
    public DamageByType MagicDamageAdjustment { get; init; } = DamageByType.Zero;
    public int Hp { get; init; }
    public int MaxHp { get; init; }
    public bool IgnoresResistance { get; init; }
    public bool DamagesArmor { get; init; }
    /// <summary>The item's "uber" flag: only such a weapon damages a nigh-invulnerable critter.</summary>
    public bool Uber { get; init; }

    /// <summary>
    /// Bare hands with a gauntlet's unarmed bonus: normal and fatigue <c>max(1, bonus − 25) .. bonus + 5</c>, nothing
    /// else (ARC-RES-030 C18).
    /// </summary>
    public static CombatWeapon Unarmed(int gauntletBonus = 0)
    {
        var range = new DamageRange(Math.Max(1, gauntletBonus - 25), gauntletBonus + 5);
        return new CombatWeapon
        {
            Kind = WeaponKind.Unarmed,
            Damage = DamageRanges.None with { Normal = range, Fatigue = range },
        };
    }

    /// <summary>A monstrous critter's natural attack from its own damage table.</summary>
    public static CombatWeapon Natural(DamageRanges damage) => new() { Kind = WeaponKind.Unarmed, Damage = damage };
}

/// <summary>
/// One attack as the caller sets it up. <see cref="SkillLevel"/> and <see cref="Training"/> are the attacker's effective
/// level 0..20 and training in <see cref="Skill"/>. A null <see cref="AimedLocation"/> rolls the location; the torso
/// cannot be aimed at. <see cref="TargetUnaware"/> is the caller's resolution of "the target is not in combat mode and can
/// neither see nor hear the attacker". <see cref="Distance"/>, <see cref="CoverRating"/> and
/// <see cref="LineOfFireBlocked"/> matter for bow, throwing and firearms only; a ranged shot whose line of fire hits
/// another object is retargeted by the caller before it calls the resolver. <see cref="TargetWasInBed"/> marks a
/// sleeper the caller pulled out of the attacked bed.
/// </summary>
public sealed record AttackRequest
{
    public required Combatant Attacker { get; init; }
    public required Combatant Target { get; init; }
    public AttackTargetKind TargetKind { get; init; } = AttackTargetKind.Critter;
    public required CombatWeapon Weapon { get; init; }
    public CombatSkill Skill { get; init; } = CombatSkill.Melee;
    public int SkillLevel { get; init; }
    public SkillTraining Training { get; init; }
    public HitLocation? AimedLocation { get; init; }
    public BackstabKind Backstab { get; init; }
    public bool TargetUnaware { get; init; }
    public bool TargetWasInBed { get; init; }
    public int Distance { get; init; } = 1;
    public int CoverRating { get; init; }
    public bool LineOfFireBlocked { get; init; }
    public GameDifficulty Difficulty { get; init; } = GameDifficulty.Normal;
}

/// <summary>Why the resolver refused the attack before any roll (ARC-RES-030 C11, C16).</summary>
public enum AttackRefusal
{
    None,
    /// <summary>The attacker is dead, unconscious, paralysed, stunned or stoned.</summary>
    AttackerInactive,
    /// <summary>The target critter is dead; corpses are not attacked.</summary>
    TargetDead,
}

/// <summary>The attack's result for the target.</summary>
public enum AttackOutcome
{
    Refused,
    Hit,
    CriticalHit,
    Miss,
    /// <summary>The attack turned on the attacker, which always hits itself (see <see cref="AttackResolution.CriticalSelfHit"/>).</summary>
    CriticalMiss,
}

/// <summary>Why a non-critical miss (or the critical miss that preceded a self-hit) happened.</summary>
public enum AttackMissCause
{
    None,
    /// <summary>The attack roll failed.</summary>
    SkillCheck,
    /// <summary>The target dodged a hit.</summary>
    Dodged,
    /// <summary>The target's opposing aptitude cancelled the weapon.</summary>
    MagickCancelled,
    /// <summary>A technological weapon failed in magickal hands (always critical).</summary>
    TechnologyFailure,
    /// <summary>The target's critical-miss fate turned the attack into a critical miss.</summary>
    Fate,
}

/// <summary>Which PC fate the attack consumed.</summary>
public enum FateUse { None, AttackerCriticalHit, TargetCriticalMiss }

/// <summary>Hit-point damage multiplier from a critical hit.</summary>
public enum DamageMultiplier { None, OneAndHalf, Double, Triple }

/// <summary>Why damage was not dealt although the attack hit.</summary>
public enum DamagePrevention { None, Invulnerable, NighInvulnerable, Wall }

/// <summary>Who takes the damage.</summary>
public enum DamageRecipient { Target, Attacker }

/// <summary>
/// One skill check: hit when <c>difficulty + roll ≤ effectiveness</c>; the second roll decides the critical against
/// <see cref="CriticalChance"/> (critical hit on success, critical miss on failure).
/// </summary>
public sealed record SkillCheck(
    int Effectiveness, int Difficulty, int Roll, bool Success, int CriticalChance, int CriticalRoll, bool Critical);

/// <summary>
/// The parts of an attack's difficulty (ARC-RES-030 FR-COMBAT-005 step 6). <see cref="Range"/> includes the
/// out-of-range and blocked penalties of <see cref="AttackRules.Impossible"/>.
/// </summary>
public sealed record AttackDifficulty(
    int Invocation, int Weapon, int Awareness, int Aim, int Armor, int Range, int Cover, int Light, int Injury)
{
    public int Total => Invocation + Weapon + Awareness + Aim + Armor + Range + Cover + Light + Injury;
}

/// <summary>
/// The damage an attack deals. <see cref="Rolled"/> is per type before immunities and resistance, <see cref="Final"/>
/// after them. <see cref="HitPointDamage"/> is normal + electrical + fire after the difficulty scaling and the critical
/// multiplier (and, for a portal or container, the material reduction); it is negative only for a resistance above
/// 100 %, which the source lets heal. <see cref="FatigueDamage"/> and <see cref="PoisonDamage"/> are 0 for non-critters
/// and fatigue is 0 for a fatigue-immune critter. Applying the packet to vital state is not the resolver's job.
/// </summary>
public sealed record AttackDamage(
    DamageRecipient Recipient,
    DamageByType Rolled,
    DamageByType Final,
    int HitPointDamage,
    int FatigueDamage,
    int PoisonDamage,
    DamageMultiplier Multiplier,
    bool MultiplierFromFallback,
    CriticalHitEffects HitEffects,
    CriticalMissEffects MissEffects,
    bool DamagesArmor,
    DamagePrevention Prevented);

/// <summary>
/// The resolved attack. <see cref="HitLocation"/> is the aimed or rolled location; <see cref="Aimed"/> is cleared when a
/// critical miss turns the attack on the attacker. <see cref="Rolls"/> lists every roll in the order it was made.
/// </summary>
public sealed record AttackResolution(
    AttackRefusal Refusal,
    AttackOutcome Outcome,
    AttackMissCause MissCause,
    HitLocation HitLocation,
    bool Aimed,
    bool WeaponMagicCancelled,
    AttackDifficulty? Difficulty,
    SkillCheck? AttackCheck,
    SkillCheck? DodgeCheck,
    FateUse Fate,
    bool CriticalSelfHit,
    AttackDamage? Damage,
    IReadOnlyList<CombatRoll> Rolls);
