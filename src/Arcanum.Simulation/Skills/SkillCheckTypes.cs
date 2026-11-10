using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;

namespace Arcanum.Simulation.Skills;

/// <summary>What an opposed skill's effectiveness is measured against (the source's flags 0x10, 0x20, 0x40 of the skill).</summary>
public enum SkillOpponent { None, Perception, Intelligence, SameSkill }

/// <summary>
/// The facts a skill's effectiveness reads besides its level (ARC-SKILLS-003): the opponent's value of an opposed skill
/// (its perception, intelligence or level of the same skill; null without a target, which reads 100), whether the target is in
/// Magnetic Inversion (firearms −20), the source's extraordinary intelligence (+10), whether the source is the local PC (only
/// then the game difficulty applies) and the game difficulty.
/// </summary>
public sealed record SkillEffectivenessFacts
{
    public int? OpponentValue { get; init; }
    public bool TargetMagneticInversion { get; init; }
    public bool ExtraordinaryIntelligence { get; init; }
    public bool LocalPc { get; init; }
    public GameDifficulty Difficulty { get; init; }
}

/// <summary>The target critter's state an aware or unaware target gives (the source's awareness term).</summary>
[Flags]
public enum SkillTargetConditions { None = 0, Paralyzed = 1, Unconscious = 2, FallingDown = 4, Stunned = 8, Sleeping = 16 }

/// <summary>The source critter's injuries the difficulty reads.</summary>
[Flags]
public enum SkillSourceInjuries { None = 0, Blinded = 1, CrippledArmOne = 2, CrippledArmsBoth = 4, CrippledLegsBoth = 8 }

/// <summary>
/// The explicit facts of one skill check's difficulty (ARC-SKILLS-003; the source's invocation, its source, target, item and
/// location, as 0x4C8430 reads them). The caller measures what needs the world (light, distance, line of fire, sight and
/// hearing, item fields); the rules apply the source's terms. Only the terms a skill's flags select are read.
/// </summary>
public sealed record SkillDifficultyFacts
{
    /// <summary>The invocation modifier (the bed's −30, a script's or item's modifier).</summary>
    public int Modifier { get; init; }

    // Weapon (an item of type weapon used for the check).
    public bool Weapon { get; init; }
    /// <summary>The weapon's to-hit bonus with its magic adjustment for this source (unless the magic is cancelled).</summary>
    public int WeaponToHit { get; init; }
    public int WeaponMinimumStrength { get; init; }
    public int SourceStrength { get; init; }

    // Target: any object (aim and armour class), a PC or NPC (awareness).
    public bool Target { get; init; }
    /// <summary>The target is a PC or an NPC (implies <see cref="Target"/>).</summary>
    public bool TargetCritter { get; init; }
    public SkillTargetConditions TargetConditions { get; init; }
    /// <summary>The target is not in combat mode and can neither see nor hear the source (the source's two awareness tests;
    /// it also skips them for a target with critter flag 0x40000, which the caller folds in).</summary>
    public bool TargetUnaware { get; init; }
    public int TargetArmorClass { get; init; }
    /// <summary>A true backstab by a trained backstabber ignores the armour class.</summary>
    public bool TrueBackstabByTrained { get; init; }

    // Aim.
    public HitLocation? AimedLocation { get; init; }

    // Distance (bow, throwing, firearms).
    public int Distance { get; init; }
    /// <summary>With an item, the thrown range for throwing, the weapon's range for a weapon, else 10; null without an item
    /// (no range test).</summary>
    public int? Range { get; init; }
    public int SourcePerception { get; init; }
    public bool LineOfFireBlocked { get; init; }
    /// <summary>The line of fire's positive cover value (0 without cover).</summary>
    public int Cover { get; init; }

    // Light.
    /// <summary>The light 0..255: of the source's own position for a seeing check, else of the target object or the location.</summary>
    public int Light { get; init; } = 255;
    public LightEmission SourceLight { get; init; }
    public bool SourceDarkSight { get; init; }
    public bool SourceUndead { get; init; }
    /// <summary>A seeing check of an observer (prowling's sight check): light applies to any skill, only while untrained.</summary>
    public bool SeeingCheck { get; init; }
    /// <summary>A hearing check of an observer (prowling's hearing check).</summary>
    public bool HearingCheck { get; init; }
    /// <summary>The observer of a seeing check has dark sight.</summary>
    public bool ObserverDarkSight { get; init; }

    public SkillSourceInjuries SourceInjuries { get; init; }

    // Per skill.
    /// <summary>Pick pocket: the stolen item's inventory location (1000..1008 worn slots add their penalty), its width and height.</summary>
    public int ItemLocation { get; init; }
    public int ItemWidth { get; init; }
    public int ItemHeight { get; init; }
    /// <summary>Prowling seeing check: a blocking object between observer and prowler (the difficulty becomes −100).</summary>
    public bool ProwlerHidden { get; init; }
    /// <summary>Prowling seeing check: the cover value between them (0 without cover).</summary>
    public int ProwlerCover { get; init; }
    /// <summary>Prowling hearing check: each worn armour's silent-move adjustment with its magic adjustment (at most one per
    /// worn slot, 9).</summary>
    public IReadOnlyList<int> ArmorSilentMoves { get; init; } = [];
    /// <summary>Heal: a generic healing item is used (else the check is impossible), and its usage bonus.</summary>
    public bool HealingItem { get; init; }
    public int HealingItemBonus { get; init; }
    /// <summary>Pick locks: the target is a portal or a container (else the check is impossible), the lockpick's usage bonus
    /// (a generic lockpick, else 0) and the lock's difficulty.</summary>
    public bool TargetLockable { get; init; }
    public int LockpickBonus { get; init; }
    public int LockDifficulty { get; init; }
    /// <summary>Disarm traps: the target is a trap, and its difficulty.</summary>
    public bool TargetTrap { get; init; }
    public int TrapDifficulty { get; init; }
}

/// <summary>The terms of a difficulty in the source's order; <see cref="Total"/> is their sum, or −100 for a hidden prowler.</summary>
public sealed record SkillDifficulty(int Modifier, int Weapon, int Awareness, int Aim, int ArmorClass, int Range, int Cover, int Light,
    int Injury, int Skill, bool ProwlerHidden = false)
{
    /// <summary>The difficulty of a seeing check whose prowler is hidden: the source returns it at once.</summary>
    public const int HiddenProwler = -100;

    public int Total => ProwlerHidden ? HiddenProwler
        : (int)Math.Clamp((long)Modifier + Weapon + Awareness + Aim + ArmorClass + Range + Cover + Light + Injury + Skill, int.MinValue, int.MaxValue);
}

/// <summary>
/// ARC-UI-124 (OH12): the flag bits the difficulty function (0x4C8430) sets on its invocation record for each term that applied, with the
/// executable's own values: the combat panel on a hovered critter draws one icon for each of them.
/// </summary>
[Flags]
public enum SkillDifficultyReasons
{
    None = 0,
    /// <summary>The weapon's minimum strength exceeds the source's strength (0x40, art <c>Pen_MSR</c>).</summary>
    WeaponTooHeavy = 0x40,
    /// <summary>The target is beyond the item's range (0x80, <c>Pen_Range</c>): the check is impossible.</summary>
    OutOfRange = 0x80,
    /// <summary>The distance is beyond half the source's perception, below master (0x100, <c>Pen_Perception</c>).</summary>
    DistancePenalty = 0x100,
    /// <summary>The line of fire has a positive cover cost (0x200, <c>Pen_Cover</c>).</summary>
    Cover = 0x200,
    /// <summary>A darkness penalty applied (0x400, <c>Pen_Light</c>).</summary>
    Darkness = 0x400,
    /// <summary>The source's blindness or crippled limbs added difficulty (0x800, <c>Pen_Injury</c>).</summary>
    Injury = 0x800,
    /// <summary>An object other than the target blocks the line of fire (0x20000, <c>BlockedShot</c>): the check is impossible.</summary>
    BlockedLine = 0x20000,
}

/// <summary>
/// What a critical chance reads besides the effectiveness (ARC-SKILLS-003): the aimed location, the item's magic critical
/// chances for this source (a weapon whose magic is not cancelled), its hit points (an item whose hit points are below the
/// maximum adds its damaged share to a failure), the backstab term of a backstab (the source adds 2 × backstab level −
/// target level, +20 for a master; the caller passes it), and the source's effect changes of each chance.
/// </summary>
public sealed record SkillCriticalFacts
{
    public HitLocation? AimedLocation { get; init; }
    public int WeaponMagicCriticalHit { get; init; }
    public int WeaponMagicCriticalMiss { get; init; }
    public bool Item { get; init; }
    public int ItemHitPoints { get; init; }
    public int ItemMaxHitPoints { get; init; }
    public int BackstabTerm { get; init; }
    public IReadOnlyList<EffectModifier> CriticalHitEffects { get; init; } = [];
    public IReadOnlyList<EffectModifier> CriticalMissEffects { get; init; } = [];
}

/// <summary>A roll of a skill check: the check's d100 and the critical d100.</summary>
public enum SkillCheckRoll { Check, Critical }

/// <summary>The rolls of a skill check, each 1..100; the session labels them (<c>skill.*</c>), the attack logs its purposes.</summary>
public interface ISkillCheckRolls
{
    int D100(SkillCheckRoll roll);
}

/// <summary>
/// One skill check (ARC-SKILLS-003; the generic part of the source's run, 0x4C7160): the skill with its effective level and
/// training, the facts of its effectiveness, difficulty and critical chances, a forced success, the PC's armed fate point of
/// this skill's kind, and for gambling the stored roll the source uses (it draws the next one in its place; required for
/// gambling, refused for the others).
/// </summary>
public sealed record SkillCheckRequest
{
    public required Skill Skill { get; init; }
    public required int Level { get; init; }
    public SkillTraining Training { get; init; }
    public SkillEffectivenessFacts Effectiveness { get; init; } = new();
    public SkillDifficultyFacts Difficulty { get; init; } = new();
    public SkillCriticalFacts Critical { get; init; } = new();
    public bool Forced { get; init; }
    /// <summary>The source is a PC whose fate point of this skill's kind (<see cref="SkillCheckRules.Fate"/>) is armed: the
    /// check becomes a critical success.</summary>
    public bool FateArmed { get; init; }
    public int? GamblingRoll { get; init; }
}

/// <summary>The kinds of fate point a PC's skill check can use (the source's fate kinds 6..11).</summary>
public enum SkillFate { None, Gambling, Heal, PickPocket, Repair, PickLocks, DisarmTraps }

/// <summary>A check's result: the shared <see cref="Combat.SkillCheck"/> numbers, its difficulty terms, and whether a fate
/// point made it (a fated repair restores full hit points);
/// <see cref="NextGamblingRoll"/> is the roll a gambling check drew and stores for the next one.</summary>
public sealed record SkillCheckResult(SkillCheck Check, SkillDifficulty Difficulty, bool Fated, int? NextGamblingRoll = null)
{
    public bool Success => Check.Success;
    public bool Critical => Check.Critical;
}
