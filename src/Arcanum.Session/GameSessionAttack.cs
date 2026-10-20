using Arcanum.Data.Combat;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Randomness;
using Arcanum.Simulation.Skills;

namespace Arcanum.Session;

/// <summary>Why an NPC's stored fields give no combat values (<see cref="SessionCritterCombat.From"/>).</summary>
public enum SessionCritterCombatFailure
{
    None,
    /// <summary>Not an NPC (type 16 with its NPC block).</summary>
    NotAnNpc,
    InvalidEntity,
    /// <summary>The race stat is not 0..10 or the gender stat not 0 or 1.</summary>
    InvalidRaceOrGender,
    /// <summary>The character owner refused a stat (outside every bound it admits even after the race clamp).</summary>
    StatRefused,
    /// <summary>The pools, level or stored life state are outside the life rules' bounds.</summary>
    InvalidLife,
    /// <summary>The experience worth or pool is outside 0..100,000,000.</summary>
    InvalidExperience,
    /// <summary>A worn item (inventory location 1000..1008) is not a wearable the wear rules admit, or two share a slot.</summary>
    InvalidWornItem,
    /// <summary>A stored skill, resistance or armour class is beyond the bound the combat rules read (±1,000,000).</summary>
    InvalidCombatValues,
}

/// <param name="Combat">The NPC's combat values, or null with the failure.</param>
public sealed record SessionCritterCombatBuild(SessionCritterCombatFailure Failure, SessionCritterCombat? Combat);

/// <summary>
/// One NPC's combat values from its stored fields (ARC-COMBAT-005; ARC-DATA-021 <see cref="CombatFields"/>, the instance's own
/// fields over its prototype's), the bridge the hero's attack (<see cref="GameSessionOwner.AttackCritter"/>) and later AI reads:
/// its life facts and loaded life state (<see cref="SessionLifeTarget.Critter"/>), its experience worth and pool, and what
/// <see cref="Arcanum.Simulation.Combat.Combatant"/> it defends as under a life state. Effective stats are the character owner's
/// for the NPC's race and gender over its stored base stats, each clamped to the race's range first (monsters store values
/// beyond it); the NPC's stored effects are not applied (no reader yet). Skills follow <see cref="CombatSkillRules"/>
/// (melee 20 for a monstrous critter); the worn armour gives armour class, resistances and light through the wear rules.
/// Immutable.
/// </summary>
public sealed class SessionCritterCombat
{
    // Stored stat indices: 0..7 primary, 17 level, 22 magick points, 23 tech points, 24 poison level, 26 gender,
    // 27 race.
    private const int LevelStat = 17, MagickPointsStat = 22, TechPointsStat = 23, PoisonStat = 24, GenderStat = 26, RaceStat = 27;
    // Critter flags (OBJ_F_CRITTER_FLAGS) and flags 2, object flags, spell flags: the source's bits.
    private const uint Undead = 0x4, Animal = 0x8, Stunned = 0x20, Paralyzed = 0x40, Blinded = 0x80, ArmOne = 0x100, ArmsBoth = 0x200,
        LegsBoth = 0x400, Sleeping = 0x1000, Monster = 0x8000, LightSmall = 0x80000, LightMedium = 0x100000, LightLarge = 0x200000,
        LightExtraLarge = 0x400000, Unrevivifiable = 0x800000, Unresurrectable = 0x1000000, FatigueImmune = 0x4000000,
        Mechanical = 0x20000000, FatigueLimiting = 0x80000000;
    private const uint FatigueDraining2 = 0x8, NighInvulnerable2 = 0x200, DarkSight2 = 0x800;
    private const uint InvulnerableObject = 0x400000;
    private const uint StonedSpell = 0x10000, MagneticInversionSpell = 0x400000;

    private readonly CharacterStatsOwner _stats;
    private readonly IReadOnlyDictionary<EquipmentSlot, EquipmentItem> _worn;
    private readonly IReadOnlyList<int> _basicSkills;
    private readonly SessionSkills? _sheetSkills;
    private readonly int _baseArmorClass;
    private readonly DamageResistances _storedResistances;
    private readonly LightEmission _ownLight;
    private readonly CombatantFlags _flags;

    private SessionCritterCombat(string entity, CharacterStatsOwner stats, IReadOnlyDictionary<EquipmentSlot, EquipmentItem> worn,
        IReadOnlyList<int> basicSkills, int baseArmorClass, DamageResistances storedResistances, LightEmission ownLight, CombatantFlags flags,
        LifeFacts facts, LifeState loaded, SessionNpcExperience experience, bool monstrous, int level, SessionSkills? sheetSkills = null)
    {
        Entity = entity; _stats = stats; _worn = worn; _basicSkills = basicSkills; _sheetSkills = sheetSkills; _baseArmorClass = baseArmorClass;
        _storedResistances = storedResistances; _ownLight = ownLight; _flags = flags; Facts = facts; Loaded = loaded;
        Experience = experience; Monstrous = monstrous; Level = level;
        Target = SessionLifeTarget.Critter(entity, facts, loaded);
    }

    public string Entity { get; }
    public LifeFacts Facts { get; }
    /// <summary>The life state the map data stores (hit-point and fatigue damage, poison, injuries); the session's record wins
    /// once it has one.</summary>
    public LifeState Loaded { get; }
    public SessionLifeTarget Target { get; }
    /// <summary>The experience worth and pool the map data stores.</summary>
    public SessionNpcExperience Experience { get; }
    /// <summary>Mechanical, monster, animal or undead (the source's 0x45F730).</summary>
    public bool Monstrous { get; }
    public int Level { get; }

    /// <summary>The same NPC defending with the dodge and backstab of the sheet the session levelled it to (ARC-SKILLS-002, a
    /// follower's saved advancement record, read under the sheet's own stats as <see cref="GameSessionOwner.QueryNpcSkills"/>
    /// reads it) and that sheet's level; its other values stay the map data's.</summary>
    internal SessionCritterCombat WithSheet(AdvancementSheet sheet) => new(Entity, _stats, _worn, sheet.BasicSkills, _baseArmorClass,
        _storedResistances, _ownLight, _flags, Facts, Loaded, Experience, Monstrous, sheet.Level, SessionSkills.Of(sheet, AdvancementModifiers.None));

    /// <summary>
    /// Builds the values of the NPC <paramref name="entity"/> from its fields and the fields of the items it carries
    /// (<paramref name="carried"/>; only those in a wear slot, inventory location 1000..1008, count). Never throws for data.
    /// </summary>
    public static SessionCritterCombatBuild From(string entity, CombatFields fields, IEnumerable<CombatFields>? carried = null)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if (!SessionValidation.Entity(entity)) return Fail(SessionCritterCombatFailure.InvalidEntity);
        if (fields.Object.Type != CombatFieldReader.NpcType || fields.Critter is not { } critter || fields.Npc is not { } npc)
            return Fail(SessionCritterCombatFailure.NotAnNpc);
        var stats = critter.BaseStats;
        var raceValue = stats[RaceStat]; var genderValue = stats[GenderStat];
        if (raceValue is < 0 or > 10 || genderValue is not (0 or 1)) return Fail(SessionCritterCombatFailure.InvalidRaceOrGender);
        static bool Bounded(int value) => Math.Abs((long)value) <= CombatSkillRules.MaxValue;
        if (critter.BasicSkills.Count < 5 || !critter.BasicSkills.All(Bounded) || !fields.Object.Resistances.All(Bounded) || !Bounded(fields.Object.ArmorClass))
            return Fail(SessionCritterCombatFailure.InvalidCombatValues);
        var race = (CharacterRace)raceValue;
        var gender = genderValue == 0 ? CharacterGender.Female : CharacterGender.Male;
        var created = CharacterStatsOwner.Create(new(race, gender, InnateEffectTable.Empty, CharacterStatLimits.Default));
        if (created.Owner is not { } owner) return Fail(SessionCritterCombatFailure.StatRefused);
        foreach (var (stat, index) in StoredStats())
        {
            var range = owner.RangeOf(stat);
            if (owner.SetBase(stat, Math.Clamp(stats[index], range.Minimum, range.Maximum)).Disposition == CharacterStatsDisposition.Rejected)
                return Fail(SessionCritterCombatFailure.StatRefused);
        }

        var worn = new SortedDictionary<EquipmentSlot, EquipmentItem>();
        foreach (var item in carried ?? [])
        {
            if (item?.Item is not { } held || !Enum.IsDefined((EquipmentSlot)held.InventoryLocation)) continue;
            if (SessionEquipmentFacts.FromCombatFields(item) is not { } wearable || EquipmentRules.WearSlot(wearable) is not { } slot ||
                !Fits(slot, (EquipmentSlot)held.InventoryLocation) || !worn.TryAdd((EquipmentSlot)held.InventoryLocation, wearable))
                return Fail(SessionCritterCombatFailure.InvalidWornItem);
        }

        var flags = critter.Flags; var flags2 = critter.Flags2; var spell = fields.Object.SpellFlags;
        var traits = LifeTraits.None;
        void Trait(bool on, LifeTraits trait) { if (on) traits |= trait; }
        Trait((flags & Undead) != 0, LifeTraits.Undead);
        Trait((flags & Mechanical) != 0, LifeTraits.Mechanical);
        Trait((flags & FatigueImmune) != 0, LifeTraits.FatigueImmune);
        Trait((spell & StonedSpell) != 0, LifeTraits.Stoned);
        Trait((flags & Paralyzed) != 0, LifeTraits.Paralyzed);
        Trait((fields.Object.Flags & InvulnerableObject) != 0, LifeTraits.Invulnerable);
        Trait((flags2 & NighInvulnerable2) != 0, LifeTraits.NighInvulnerable);
        Trait((flags & Unrevivifiable) != 0, LifeTraits.Unrevivifiable);
        Trait((flags & Unresurrectable) != 0, LifeTraits.Unresurrectable);
        var conditions = LifeConditions.None;
        void Condition(bool on, LifeConditions condition) { if (on) conditions |= condition; }
        Condition((flags & Stunned) != 0, LifeConditions.Stunned);
        Condition((flags & Blinded) != 0, LifeConditions.Blinded);
        Condition((flags & ArmOne) != 0, LifeConditions.CrippledArmOne);
        Condition((flags & ArmsBoth) != 0, LifeConditions.CrippledArmsBoth);
        Condition((flags & LegsBoth) != 0, LifeConditions.CrippledLegsBoth);
        var monstrous = (flags & (Mechanical | Monster | Animal | Undead)) != 0;
        var combatFlags = CombatantFlags.None;
        if ((flags & Sleeping) != 0) combatFlags |= CombatantFlags.Sleeping;
        if ((flags & FatigueLimiting) != 0) combatFlags |= CombatantFlags.FatigueLimiting;
        if ((flags2 & FatigueDraining2) != 0) combatFlags |= CombatantFlags.FatigueDraining;
        if ((flags2 & DarkSight2) != 0) combatFlags |= CombatantFlags.DarkSight;
        if ((spell & MagneticInversionSpell) != 0) combatFlags |= CombatantFlags.MagneticInversion;
        var light = (flags & LightExtraLarge) != 0 ? LightEmission.ExtraLarge : (flags & LightLarge) != 0 ? LightEmission.Large
            : (flags & LightMedium) != 0 ? LightEmission.Medium : (flags & LightSmall) != 0 ? LightEmission.Small : LightEmission.None;

        var level = stats[LevelStat];
        var poison = stats[PoisonStat];
        var loaded = new LifeState(fields.Object.HitPointDamage, critter.FatigueDamage, poison, conditions, 0);
        var plain = owner.Query(new(0)).Snapshot!;
        int Effective(CharacterStatsSnapshot snapshot, CharacterStat stat) => snapshot.GetEffective(stat);
        var aptitude = Math.Clamp(Effective(plain, CharacterStat.MagickTechAptitude), EquipmentLimits.MinAptitude, EquipmentLimits.MaxAptitude);
        var equipment = EquipmentRules.Evaluate(worn, aptitude, SkillTraining.None);
        var resistances = CombatantRules.Resistances(StoredResistances(fields.Object.Resistances), equipment.Resistances,
            Effective(plain, CharacterStat.Constitution), monstrous);
        var facts = new LifeFacts
        {
            HitPointPoints = fields.Object.HitPoints,
            HitPointAdjustment = fields.Object.HitPointAdjustment,
            FatiguePoints = critter.FatiguePoints,
            FatigueAdjustment = critter.FatigueAdjustment,
            Level = level,
            Strength = Effective(plain, CharacterStat.Strength),
            PoisonedStrength = [.. new[] { 100, 200, 300 }.Select(p => owner.Query(new(0, p)).Snapshot!.GetEffective(CharacterStat.Strength))],
            Constitution = Effective(plain, CharacterStat.Constitution),
            Willpower = Effective(plain, CharacterStat.Willpower),
            HealRate = Effective(plain, CharacterStat.HealRate),
            PoisonRecovery = Effective(plain, CharacterStat.PoisonRecovery),
            NormalResistance = resistances.Normal,
            Traits = traits,
        };
        if (!LifeRules.IsValid(facts) || !LifeRules.IsValid(loaded)) return Fail(SessionCritterCombatFailure.InvalidLife);
        var experience = new SessionNpcExperience(entity, npc.ExperienceWorth, npc.ExperiencePool);
        if (!ExperienceDomain.Valid(experience)) return Fail(SessionCritterCombatFailure.InvalidExperience);
        var lightEmission = (LightEmission)Math.Max((int)light, (int)equipment.Light);
        return new(SessionCritterCombatFailure.None, new(entity, owner, worn, critter.BasicSkills, fields.Object.ArmorClass,
            StoredResistances(fields.Object.Resistances), lightEmission, combatFlags, facts, loaded, experience, monstrous, level));

        static SessionCritterCombatBuild Fail(SessionCritterCombatFailure failure) => new(failure, null);
    }

    /// <summary>What the NPC defends as in this life state: effective stats under its poison and crippled legs, armour class
    /// (<see cref="AttackRules.ArmorClass"/> under <paramref name="profile"/>), resistances, dodge, light and conditions.</summary>
    public Combatant Combatant(LifeStatus status, LifeState state, CombatAnomalyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(profile);
        var snapshot = _stats.Query(new(0, Math.Clamp(state.PoisonLevel, 0, CharacterStatRules.MaxPoisonLevel),
            state.Conditions.HasFlag(LifeConditions.CrippledLegsBoth))).Snapshot!;
        var dexterity = snapshot.GetEffective(CharacterStat.Dexterity);
        var aptitude = Math.Clamp(snapshot.GetEffective(CharacterStat.MagickTechAptitude), EquipmentLimits.MinAptitude, EquipmentLimits.MaxAptitude);
        var equipment = EquipmentRules.Evaluate(_worn, aptitude, SkillTraining.None);
        int SkillLevel(BasicCombatSkill skill) => CombatSkillRules.Level(skill, _basicSkills[(int)skill], dexterity, Monstrous);
        return new Combatant
        {
            IsPc = false,
            Level = Level,
            Strength = snapshot.GetEffective(CharacterStat.Strength),
            Perception = snapshot.GetEffective(CharacterStat.Perception),
            ExtraordinaryIntelligence = snapshot.GetEffective(CharacterStat.Intelligence) >= CharacterStatRules.ExtraordinaryThreshold,
            DamageBonus = snapshot.GetEffective(CharacterStat.DamageBonus),
            MagickTechAptitude = aptitude,
            ArmorClass = AttackRules.ArmorClass(_baseArmorClass, equipment.ArmorClassAdjustment, snapshot.GetEffective(CharacterStat.AcAdjustment),
                value => value, profile),
            Resistances = CombatantRules.Resistances(_storedResistances, equipment.Resistances, snapshot.GetEffective(CharacterStat.Constitution), Monstrous),
            Conditions = CombatantRules.Conditions(status, state, Facts.Traits, _flags),
            LightEmission = _ownLight,
            DodgeLevel = _sheetSkills?[Skill.Dodge].Level ?? SkillLevel(BasicCombatSkill.Dodge),
            DodgeTraining = _sheetSkills?[Skill.Dodge].Training
                ?? CombatSkillRules.Training(BasicCombatSkill.Dodge, _basicSkills[(int)BasicCombatSkill.Dodge], Monstrous, 0, Level),
            BackstabLevel = _sheetSkills?[Skill.Backstab].Level ?? SkillLevel(BasicCombatSkill.Backstab),
            BackstabTraining = _sheetSkills?[Skill.Backstab].Training
                ?? CombatSkillRules.Training(BasicCombatSkill.Backstab, _basicSkills[(int)BasicCombatSkill.Backstab], Monstrous, 0, Level),
        };
    }

    // A worn item is admitted in its own wear slot; rings take either ring slot.
    private static bool Fits(EquipmentSlot wearSlot, EquipmentSlot location) =>
        wearSlot == location || (wearSlot is EquipmentSlot.Ring1 or EquipmentSlot.Ring2 && location is EquipmentSlot.Ring1 or EquipmentSlot.Ring2);

    // Stored resistances are in the source's resistance order: normal, fire, electrical, poison, magic.
    private static DamageResistances StoredResistances(IReadOnlyList<int> stored) => new(stored[0], stored[3], stored[2], stored[1]);

    private static IEnumerable<(CharacterStat Stat, int Index)> StoredStats()
    {
        for (var index = 0; index < 8; index++) yield return ((CharacterStat)index, index);
        yield return (CharacterStat.MagickPoints, MagickPointsStat);
        yield return (CharacterStat.TechPoints, TechPointsStat);
    }
}

/// <summary>
/// What the hero attacks with (<see cref="GameSessionOwner.QueryHeroAttack"/>): its <see cref="Combatant"/> under its life state,
/// the weapon it strikes with (bare hands without one), the weapon's skill with its level and training, the weapon speed, the
/// effective Speed stat (the attack animation's rate), the weapon's range (1 for bare hands) and whether the attack is one this
/// session can run (melee only: no ammunition, projectile or throwing is modelled).
/// </summary>
public sealed record SessionHeroAttack(Combatant Combatant, CombatWeapon Weapon, CombatSkill Skill, int SkillLevel, SkillTraining Training,
    int WeaponSpeed, int Speed, int Range, bool Supported);

/// <summary>
/// One attack of the hero on an NPC (<see cref="GameSessionOwner.AttackCritter"/>). <see cref="Aim"/> is the aimed location (null:
/// not aimed, rolled); <see cref="Distance"/> the Chebyshev distance to the target (a melee attack needs 1 or less).
/// </summary>
public sealed record SessionAttack(SessionCritterCombat Target)
{
    public HitLocation? Aim { get; init; }
    public int Distance { get; init; } = 1;
}

/// <summary>
/// The result of <see cref="GameSessionOwner.AttackCritter"/>: the resolution (null when refused before the rolls), who took the
/// damage and the life outcome of that step (null when none was dealt), and what the target's experience paid the hero for the
/// hit and the kill. A later step that was refused (an experience or life budget, the revision) is <see cref="StepFailure"/>; the
/// steps before it stay applied, as each one is a step of the source's damage sequence.
/// </summary>
public sealed record SessionAttackResult(GameSessionDisposition Disposition, GameSessionFailure Failure, AttackResolution? Resolution,
    DamageRecipient? Recipient, LifeOutcome? Life, SessionExperienceAward? HitAward, SessionExperienceAward? KillAward,
    GameSessionFailure StepFailure, GameSessionVersion Version);

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// What the hero attacks with now (null without a hero): its <see cref="Combatant"/> from the character owner (stats under its
    /// poison and crippled legs), its level, the worn items (armour class, resistances, light, weapon), the difficulty-free values
    /// and its life conditions. Its skills are <see cref="QueryHeroSkills"/> (ARC-SKILLS-002): the level and training of the
    /// weapon's skill (firearms a tech skill), dodge and backstab, and the weapon speed with the weapon skill's training.
    /// </summary>
    public SessionHeroAttack? QueryHeroAttack()
    {
        ThrowIfDisposed();
        if (_hero is null) return null;
        var state = _life.Hero!.Value.State;
        var facts = HeroLifeFacts(state);
        var status = LifeRules.Evaluate(state, facts);
        var snapshot = _hero.Stats.Query(new(0, state.PoisonLevel, state.Conditions.HasFlag(LifeConditions.CrippledLegsBoth))).Snapshot!;
        var aptitude = HeroAptitude();
        var skills = HeroSkills();
        var equipment = EquipmentRules.Evaluate(_equipment.Items(), aptitude, HeroWeaponTraining(skills));
        var combatant = new Combatant
        {
            IsPc = true,
            Level = _hero.State.Level,
            Strength = snapshot.GetEffective(CharacterStat.Strength),
            Perception = snapshot.GetEffective(CharacterStat.Perception),
            ExtraordinaryIntelligence = snapshot.GetEffective(CharacterStat.Intelligence) >= CharacterStatRules.ExtraordinaryThreshold,
            DamageBonus = snapshot.GetEffective(CharacterStat.DamageBonus),
            MagickTechAptitude = aptitude,
            ArmorClass = AttackRules.ArmorClass(0, equipment.ArmorClassAdjustment, snapshot.GetEffective(CharacterStat.AcAdjustment), value => value,
                _rules.Combat),
            Resistances = CombatantRules.Resistances(DamageResistances.None, equipment.Resistances, snapshot.GetEffective(CharacterStat.Constitution), false),
            Conditions = CombatantRules.Conditions(status, state, facts.Traits, CombatantFlags.None),
            LightEmission = equipment.Light,
            DodgeLevel = skills[Skill.Dodge].Level,
            DodgeTraining = skills[Skill.Dodge].Training,
            BackstabLevel = skills[Skill.Backstab].Level,
            BackstabTraining = skills[Skill.Backstab].Training,
        };
        var weaponSkill = skills[SkillRules.Of(equipment.Skill)];
        var range = equipment.Weapon.Kind == WeaponKind.Unarmed ? 1 : Math.Max(1, equipment.Weapon.Range);
        return new(combatant, equipment.Weapon, equipment.Skill, weaponSkill.Level, weaponSkill.Training, equipment.WeaponSpeed,
            snapshot.GetEffective(CharacterStat.Speed), range, equipment.Skill == CombatSkill.Melee);
    }

    /// <summary>
    /// The hero attacks an NPC once (ARC-COMBAT-005; the source's hit step of the attack goal, then 0x4B4390): the attack is
    /// resolved (<see cref="AttackResolver.Resolve"/>, under the active rules and difficulty, rolls from the session generator as
    /// <c>combat.*</c>), its damage applied to the target, or to the hero on a critical self-hit (<see cref="ApplyDamage"/>), then an
    /// NPC's hit-point damage takes its share of the experience pool for the hero unless the NPC is stoned
    /// (<see cref="RecordNpcDamage"/>), and a kill pays the hero a fifth of the worth (<see cref="RecordNpcKill"/>); experience calls
    /// are skipped without experience data. Refused before any roll, in order: no hero (<see cref="GameSessionFailure.AttackUnavailable"/>),
    /// a target whose values do not hold (<see cref="GameSessionFailure.InvalidLifeTarget"/> …), an aim at the torso, a distance
    /// below 0 or beyond the weapon's reach (<see cref="GameSessionFailure.InvalidAttack"/>), a wielded weapon that is not melee
    /// (<see cref="GameSessionFailure.AttackUnsupported"/>), the version. A resolver refusal (the hero cannot act, the target is dead)
    /// is NoChange with the resolution. The resolution's draws are spent once it ran; each later step commits on its own revision.
    /// Not modelled: the target's taking-damage and dying scripts, its reaction and AI, critical-effect tables, weapon wear,
    /// backstab, the target's awareness (an NPC always sees the hero) and light (every position is lit).
    /// </summary>
    public SessionAttackResult AttackCritter(GameSessionVersion expected, SessionAttack attack)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(attack);
        if (_hero is null) return RejectAttack(GameSessionFailure.AttackUnavailable);
        var target = attack.Target;
        var resolved = Resolve(target.Target, out var record, out var facts, out _);
        if (resolved != GameSessionFailure.None) return RejectAttack(resolved);
        var hero = QueryHeroAttack()!;
        if (attack.Aim is HitLocation.Torso || attack.Aim is { } aim && !Enum.IsDefined(aim) || attack.Distance < 0 || attack.Distance > hero.Range)
            return RejectAttack(GameSessionFailure.InvalidAttack);
        if (!hero.Supported) return RejectAttack(GameSessionFailure.AttackUnsupported);
        if (!IsCurrent(expected)) return RejectAttack(GameSessionFailure.StaleVersion);

        var targetStatus = LifeRules.Evaluate(record.State, facts!);
        // A follower the session levelled defends with its saved sheet's skills and level (ARC-SKILLS-002).
        var defending = _advancement.Npcs.TryGetValue(target.Entity, out var levelled) ? target.WithSheet(levelled.Sheet) : target;
        Combatant defender;
        try { defender = defending.Combatant(targetStatus, record.State, _rules.Combat); }
        catch (ArgumentException) { return RejectAttack(GameSessionFailure.InvalidAttack); }
        var request = new AttackRequest
        {
            Attacker = hero.Combatant,
            Target = defender,
            Weapon = hero.Weapon,
            Skill = hero.Skill,
            SkillLevel = hero.SkillLevel,
            Training = hero.Training,
            AimedLocation = attack.Aim,
            Distance = attack.Distance,
            Difficulty = _difficulty,
        };
        var rolls = new TrialCombatRolls(new SeededRandom(_random.Seed, _random.Draws));
        AttackResolution resolution;
        try { resolution = AttackResolver.Resolve(request, rolls, null, _rules.Combat); }
        catch (TrialCombatRolls.ExhaustedException) { return RejectAttack(GameSessionFailure.RandomExhausted); }
        catch (ArgumentException) { return RejectAttack(GameSessionFailure.InvalidAttack); }
        _random = rolls.Trial;
        if (resolution.Refusal != AttackRefusal.None || LifeDamage.FromAttack(resolution) is not { } damage)
            return new(GameSessionDisposition.NoChange, GameSessionFailure.None, resolution, null, null, null, null, GameSessionFailure.None, Version);

        var recipient = resolution.Damage!.Recipient;
        var applied = ApplyDamage(Version, recipient == DamageRecipient.Target ? target.Target : SessionLifeTarget.Hero, damage);
        if (applied.Disposition == GameSessionDisposition.Rejected)
            return new(GameSessionDisposition.Changed, GameSessionFailure.None, resolution, recipient, null, null, null, applied.Failure, Version);
        var outcome = applied.Outcome!;
        SessionExperienceAward? hitAward = null, killAward = null;
        var stepFailure = GameSessionFailure.None;
        if (recipient == DamageRecipient.Target && _experience.Rules is not null)
        {
            if (outcome.HitPointDamage > 0 && !facts!.Traits.HasFlag(LifeTraits.Stoned))
            {
                var hit = RecordNpcDamage(Version, target.Experience, Math.Min(outcome.HitPointDamage, LifeLimits.MaxAmount),
                    Math.Clamp(targetStatus.MaxHitPoints, 1, LifeLimits.MaxAmount), byHero: true);
                hitAward = hit.Award;
                if (hit.Disposition == GameSessionDisposition.Rejected) stepFailure = hit.Failure;
            }
            if (stepFailure == GameSessionFailure.None && outcome.Events.HasFlag(LifeEvents.Died))
            {
                var kill = RecordNpcKill(Version, target.Experience, creditsHero: true);
                killAward = kill.Award;
                if (kill.Disposition == GameSessionDisposition.Rejected) stepFailure = kill.Failure;
            }
        }
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, resolution, recipient, outcome, hitAward, killAward, stepFailure, Version);
    }

    /// <summary>Whether an NPC is dead in this session: its saved record holds the pinned death damage (every death through the
    /// life domain pins it). An NPC the session never changed is as its map data says (false here).</summary>
    public bool QueryNpcKilled(string entity)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(entity)) throw new ArgumentException("Invalid entity id.", nameof(entity));
        return KilledNpc(entity);
    }

    private bool KilledNpc(string entity) =>
        _life.Critters.TryGetValue(entity, out var record) && record.State.HitPointDamage >= LifeLimits.DeathHitPointDamage;

    private SessionAttackResult RejectAttack(GameSessionFailure failure) =>
        new(GameSessionDisposition.Rejected, failure, null, null, null, null, null, GameSessionFailure.None, new(_identity, _revision));
}
