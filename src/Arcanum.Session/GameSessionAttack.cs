using Arcanum.Data.Combat;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Magic;
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
/// (melee 20 for a monstrous critter); the worn armour gives armour class, resistances and light through the wear rules; its stored
/// material is what a melee hit on it wears the attacker's weapon or hands by (ARC-COMBAT-007; a value outside the fourteen
/// materials defends as flesh, which wears nothing, as in the original).
/// Immutable.
/// </summary>
public sealed class SessionCritterCombat
{
    // Stored stat indices: 0..7 primary, 17 level, 19 alignment, 22 magick points, 23 tech points, 24 poison level, 26 gender,
    // 27 race.
    private const int LevelStat = 17, AlignmentStat = 19, MagickPointsStat = 22, TechPointsStat = 23, PoisonStat = 24, GenderStat = 26, RaceStat = 27;
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
    private readonly IReadOnlyList<int> _techSkills;
    private readonly SessionSkills? _sheetSkills;
    private readonly int _baseArmorClass;
    private readonly DamageResistances _storedResistances;
    private readonly LightEmission _ownLight;
    private readonly CombatantFlags _flags;
    private readonly ObjectMaterial _material;
    private readonly int _criticalBody;
    private readonly int _constitutionMaximum;
    private readonly CharacterRace _race;
    private readonly CharacterGender _gender;
    private readonly DamageRanges _natural;

    private SessionCritterCombat(string entity, CharacterStatsOwner stats, IReadOnlyDictionary<EquipmentSlot, EquipmentItem> worn,
        IReadOnlyList<int> basicSkills, IReadOnlyList<int> techSkills, int baseArmorClass, DamageResistances storedResistances, LightEmission ownLight,
        CombatantFlags flags, ObjectMaterial material, int criticalBody, CharacterRace race, CharacterGender gender, LifeFacts facts, LifeState loaded,
        SessionNpcExperience experience, bool monstrous, int level, DamageRanges natural, SessionKillFacts killFacts, SessionSkills? sheetSkills = null)
    {
        _natural = natural; KillFacts = killFacts;
        Entity = entity; _stats = stats; _worn = worn; _basicSkills = basicSkills; _techSkills = techSkills; _sheetSkills = sheetSkills; _baseArmorClass = baseArmorClass;
        _storedResistances = storedResistances; _ownLight = ownLight; _flags = flags; _material = material; _criticalBody = criticalBody;
        _race = race; _gender = gender; _constitutionMaximum = CharacterStatRules.Range(race, CharacterStat.Constitution).Maximum; Facts = facts; Loaded = loaded; Experience = experience; Monstrous = monstrous; Level = level;
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

    /// <summary>ARC-UI-097: what the PC's kill and injury log reads of this NPC (description numbers, stored alignment, aptitude).</summary>
    public SessionKillFacts KillFacts { get; }

    /// <summary>The same NPC defending with the dodge and backstab of the sheet the session levelled it to (ARC-SKILLS-002, a
    /// follower's saved advancement record, read under the sheet's own stats as <see cref="GameSessionOwner.QueryNpcSkills"/>
    /// reads it) and that sheet's level; its other values stay the map data's.</summary>
    internal SessionCritterCombat WithSheet(AdvancementSheet sheet) => new(Entity, _stats, _worn, sheet.BasicSkills, sheet.TechSkills, _baseArmorClass,
        _storedResistances, _ownLight, _flags, _material, _criticalBody, _race, _gender, Facts, Loaded, Experience, Monstrous, sheet.Level,
        _natural, KillFacts, SessionSkills.Of(sheet, AdvancementModifiers.None));

    /// <summary>
    /// Builds the values of the NPC <paramref name="entity"/> from its fields and the fields of the items it carries
    /// (<paramref name="carried"/>; only those whose stored inventory location is a wear slot, 1000..1008, count). Never throws for data.
    /// </summary>
    public static SessionCritterCombatBuild From(string entity, CombatFields fields, IEnumerable<CombatFields>? carried = null)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var worn = new List<KeyValuePair<EquipmentSlot, CombatFields>>();
        foreach (var item in carried ?? [])
            if (item?.Item is { } held && Enum.IsDefined((EquipmentSlot)held.InventoryLocation)) worn.Add(new((EquipmentSlot)held.InventoryLocation, item));
        return FromWorn(entity, fields, worn);
    }

    /// <summary>
    /// ARC-WORLD-106: builds the values of the NPC <paramref name="entity"/> from its fields and the items it wears now, each in the wear
    /// slot the caller names (the session's lists: a regeneration's best-items record, else the map data's locations; an item's stored
    /// location is not read, so a made item's prototype fields serve). An item must be a wearable of that slot (a ring in either ring slot),
    /// one per slot, else <see cref="SessionCritterCombatFailure.InvalidWornItem"/>. Never throws for data.
    /// </summary>
    public static SessionCritterCombatBuild FromWorn(string entity, CombatFields fields, IEnumerable<KeyValuePair<EquipmentSlot, CombatFields>> wornItems)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(wornItems);
        if (!SessionValidation.Entity(entity)) return Fail(SessionCritterCombatFailure.InvalidEntity);
        if (fields.Object.Type != CombatFieldReader.NpcType || fields.Critter is not { } critter || fields.Npc is not { } npc)
            return Fail(SessionCritterCombatFailure.NotAnNpc);
        var stats = critter.BaseStats;
        var raceValue = stats[RaceStat]; var genderValue = stats[GenderStat];
        if (raceValue is < 0 or > 10 || genderValue is not (0 or 1)) return Fail(SessionCritterCombatFailure.InvalidRaceOrGender);
        static bool Bounded(int value) => Math.Abs((long)value) <= CombatSkillRules.MaxValue;
        if (critter.BasicSkills.Count < 5 || !critter.BasicSkills.All(Bounded) || !critter.TechSkills.All(Bounded) || !fields.Object.Resistances.All(Bounded)
            || !Bounded(fields.Object.ArmorClass))
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
        foreach (var (location, item) in wornItems)
        {
            if (item?.Item is null || !Enum.IsDefined(location) || SessionEquipmentFacts.FromCombatFields(item) is not { } wearable ||
                EquipmentRules.WearSlot(wearable) is not { } slot || !Fits(slot, location) || !worn.TryAdd(location, wearable))
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
        var effectiveAptitude = Effective(plain, CharacterStat.MagickTechAptitude);
        var aptitude = Math.Clamp(effectiveAptitude, EquipmentLimits.MinAptitude, EquipmentLimits.MaxAptitude);
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
        // ARC-WORLD-106: the four stored tech skills (repair, firearms, pick locks, disarm traps); a missing entry is 0.
        var tech = Enumerable.Range(0, SkillRules.TechCount).Select(index => index < critter.TechSkills.Count ? critter.TechSkills[index] : 0).ToArray();
        return new(SessionCritterCombatFailure.None, new(entity, owner, worn, critter.BasicSkills, Array.AsReadOnly(tech), fields.Object.ArmorClass,
            StoredResistances(fields.Object.Resistances), lightEmission, combatFlags, Material(fields.Object.Material), critter.CriticalHitChart,
            race, gender, facts, loaded, experience, monstrous, level, Natural(npc.Damage),
            new(fields.Object.Description, npc.UnknownDescription, Math.Clamp(stats[AlignmentStat], SessionHeroLimits.MinAlignment, SessionHeroLimits.MaxAlignment),
                effectiveAptitude)));

        static SessionCritterCombatBuild Fail(SessionCritterCombatFailure failure) => new(failure, null);
    }

    /// <summary>The item the map data has the NPC wear in a slot (ARC-COMBAT-009: the arm-hit drops ask the wear check about it).</summary>
    internal EquipmentItem? WornItem(EquipmentSlot slot) => _worn.TryGetValue(slot, out var item) ? item : null;

    internal IReadOnlyDictionary<EquipmentSlot, EquipmentItem> WornItems => _worn;

    /// <summary>The NPC as a wearer under <paramref name="conditions"/>: race, gender and crippled arms; its spells, background and art
    /// are not read (never transformed, magick-allergic or unique), so a body armour's unique-art refusal is not seen.</summary>
    public EquipmentWearer Wearer(LifeConditions conditions) => new()
    {
        Race = _race,
        Gender = _gender,
        CrippledArmOne = conditions.HasFlag(LifeConditions.CrippledArmOne),
        CrippledArmsBoth = conditions.HasFlag(LifeConditions.CrippledArmsBoth),
    };

    /// <summary>
    /// ARC-WORLD-103: the NPC as its best-items choice scores it (<see cref="EquipmentChoice"/>) in this life state: its effective damage
    /// bonus, aptitude and intelligence under its poison and crippled legs, and the melee, bow and throwing levels of its stored basic skills
    /// (or of the sheet it was levelled to), as <see cref="AttackValues"/> reads them; firearms from its stored tech skill (ARC-WORLD-106,
    /// <see cref="FirearmsLevel"/>). An NPC chooser (<see cref="EquipmentChooser.IsPc"/> false), with no ammunition: the session adds its stacks.
    /// </summary>
    public EquipmentChooser Chooser(LifeState state)
    {
        var snapshot = _stats.Query(new(0, Math.Clamp(state.PoisonLevel, 0, CharacterStatRules.MaxPoisonLevel),
            state.Conditions.HasFlag(LifeConditions.CrippledLegsBoth))).Snapshot!;
        var dexterity = snapshot.GetEffective(CharacterStat.Dexterity);
        int Level(BasicCombatSkill skill, Skill sheet) =>
            _sheetSkills?[sheet].Level ?? CombatSkillRules.Level(skill, _basicSkills[(int)skill], dexterity, Monstrous);
        return new EquipmentChooser
        {
            DamageBonus = snapshot.GetEffective(CharacterStat.DamageBonus),
            MagickTechAptitude = Math.Clamp(snapshot.GetEffective(CharacterStat.MagickTechAptitude), EquipmentLimits.MinAptitude, EquipmentLimits.MaxAptitude),
            MeleeLevel = Level(BasicCombatSkill.Melee, Skill.Melee),
            BowLevel = Level(BasicCombatSkill.Bow, Skill.Bow),
            ThrowingLevel = Level(BasicCombatSkill.Throwing, Skill.Throwing),
            FirearmsLevel = _sheetSkills?[Skill.Firearms].Level ?? FirearmsLevel(snapshot),
            ExtraordinaryIntelligence = snapshot.GetEffective(CharacterStat.Intelligence) >= CharacterStatRules.ExtraordinaryThreshold,
            IsPc = false,
        };
    }

    /// <summary>ARC-MAGIC-014: the NPC's race (its stored race stat).</summary>
    public CharacterRace Race => _race;

    /// <summary>ARC-MAGIC-014: an effective stat of the NPC in this life state (under its poison and crippled legs), as
    /// <see cref="Combatant"/> reads them.</summary>
    public int EffectiveStat(CharacterStat stat, LifeState state) =>
        _stats.Query(new(0, Math.Clamp(state.PoisonLevel, 0, CharacterStatRules.MaxPoisonLevel), state.Conditions.HasFlag(LifeConditions.CrippledLegsBoth)))
            .Snapshot!.GetEffective(stat);

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
            Material = _material,
            DodgeLevel = _sheetSkills?[Skill.Dodge].Level ?? SkillLevel(BasicCombatSkill.Dodge),
            DodgeTraining = _sheetSkills?[Skill.Dodge].Training
                ?? CombatSkillRules.Training(BasicCombatSkill.Dodge, _basicSkills[(int)BasicCombatSkill.Dodge], Monstrous, 0, Level),
            BackstabLevel = _sheetSkills?[Skill.Backstab].Level ?? SkillLevel(BasicCombatSkill.Backstab),
            BackstabTraining = _sheetSkills?[Skill.Backstab].Training
                ?? CombatSkillRules.Training(BasicCombatSkill.Backstab, _basicSkills[(int)BasicCombatSkill.Backstab], Monstrous, 0, Level),
            CriticalBody = _criticalBody,
            Constitution = snapshot.GetEffective(CharacterStat.Constitution),
            ConstitutionMaximum = _constitutionMaximum,
            Worn = CombatantRules.WornItems(_worn.Keys),
            Scarred = state.Scars > 0,
        };
    }

    /// <summary>
    /// ARC-WORLD-092: what the NPC attacks with in this life state (the AI's hit step, 0x4B3170 with an NPC attacker): its
    /// <see cref="Combatant"/>, the wielded weapon from the worn items it was built with (its map data's, or since ARC-WORLD-106 the
    /// session's through <see cref="FromWorn"/>), else a monstrous critter's own damage table (NPC
    /// field 0x12F, two values per damage type) and any other critter's bare hands with its gauntlets' bonus (0x465F70 reads the
    /// gauntlets for melee only, then the monster test 0x45F730 picks the table over them); the weapon's skill with the level and training
    /// of the NPC's stored basic skill (melee 20 and the tiers for a monstrous critter; firearms its stored tech skill, ARC-WORLD-106), or
    /// of the sheet it was levelled to; the weapon
    /// speed and attack cost; the reach (1 bare-handed). <see cref="SessionNpcAttackValues.Supported"/> is a melee attack: bows,
    /// throws and firearms are the ranged slice's. <paramref name="wornNow"/> names the wear slots whose built item the NPC still
    /// wears (a critical may have dropped or destroyed one); null: all of them.
    /// </summary>
    public SessionNpcAttackValues AttackValues(LifeStatus status, LifeState state, CombatAnomalyProfile profile, IEnumerable<EquipmentSlot>? wornNow = null)
    {
        var worn = wornNow is null ? _worn : _worn.Where(pair => wornNow.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);
        var combatant = Combatant(status, state, profile) with { Worn = CombatantRules.WornItems(worn.Keys) };
        var snapshot = _stats.Query(new(0, Math.Clamp(state.PoisonLevel, 0, CharacterStatRules.MaxPoisonLevel),
            state.Conditions.HasFlag(LifeConditions.CrippledLegsBoth))).Snapshot!;
        var aptitude = Math.Clamp(snapshot.GetEffective(CharacterStat.MagickTechAptitude), EquipmentLimits.MinAptitude, EquipmentLimits.MaxAptitude);
        var wielded = worn.TryGetValue(EquipmentSlot.Weapon, out var item) ? item : null;
        var skill = wielded?.Weapon?.Skill ?? CombatSkill.Melee;
        var (level, training) = SkillOf(skill, snapshot);
        var equipment = EquipmentRules.Evaluate(worn, aptitude, training);
        var weapon = wielded is null && Monstrous ? CombatWeapon.Natural(_natural) : equipment.Weapon;
        var range = weapon.Kind == WeaponKind.Unarmed ? 1 : Math.Max(1, weapon.Range);
        return new(combatant, weapon, skill, level, training, equipment.WeaponSpeed, equipment.AttackCost, snapshot.GetEffective(CharacterStat.Speed),
            range, skill == CombatSkill.Melee);
    }

    /// <summary>
    /// ARC-WORLD-113: the level and training of the NPC's combat <paramref name="skill"/> in this life state, as
    /// <see cref="AttackValues"/> reads its weapon's skill: the stored basic skill (melee, bow, throwing; melee 20 and the tiers for a
    /// monstrous critter) under its effective dexterity, firearms from its stored tech skill, or the sheet it was levelled to. The
    /// throw of an NPC (<see cref="GameSessionOwner.ThrowByNpc"/>) checks with its throwing skill (TW3: skill 3).
    /// </summary>
    public (int Level, SkillTraining Training) SkillValues(CombatSkill skill, LifeState state)
    {
        if (!Enum.IsDefined(skill)) throw new ArgumentOutOfRangeException(nameof(skill));
        return SkillOf(skill, _stats.Query(new(0, Math.Clamp(state.PoisonLevel, 0, CharacterStatRules.MaxPoisonLevel),
            state.Conditions.HasFlag(LifeConditions.CrippledLegsBoth))).Snapshot!);
    }

    private (int Level, SkillTraining Training) SkillOf(CombatSkill skill, CharacterStatsSnapshot snapshot)
    {
        BasicCombatSkill? basic = skill switch
        {
            CombatSkill.Melee => BasicCombatSkill.Melee,
            CombatSkill.Bow => BasicCombatSkill.Bow,
            CombatSkill.Throwing => BasicCombatSkill.Throwing,
            _ => null,
        };
        if (_sheetSkills is { } sheet) return (sheet[SkillRules.Of(skill)].Level, sheet[SkillRules.Of(skill)].Training);
        if (basic is { } stored)
        {
            var level = CombatSkillRules.Level(stored, _basicSkills[(int)stored], snapshot.GetEffective(CharacterStat.Dexterity), Monstrous);
            return (level, CombatSkillRules.Training(stored, _basicSkills[(int)stored], Monstrous, level, Level));
        }
        // ARC-WORLD-106: the firearms skill is the NPC's stored tech skill (0x4C65E0, 0x4C67F0).
        return (FirearmsLevel(snapshot), SkillRules.Training(Skill.Firearms, _techSkills[FirearmsTech], false, 0, Level));
    }

    // The tech index of firearms (repair 0, firearms 1, pick locks 2, disarm traps 3).
    private static readonly int FirearmsTech = SkillRules.FamilyIndex(Skill.Firearms);

    /// <summary>
    /// ARC-WORLD-106: the NPC's firearms level from its stored tech skill, as the original's tech level (0x4C65E0, ledger SX2, AL5) reads it
    /// for a PC or an NPC alike: four per stored point, clamped to 0..20, capped by the key stat's table (perception, effective under the
    /// life state's <paramref name="snapshot"/>). The NPC's stored effects are not applied (no reader yet), as for its basic skills.
    /// </summary>
    private int FirearmsLevel(CharacterStatsSnapshot snapshot) =>
        SkillRules.Level(Skill.Firearms, _techSkills[FirearmsTech], snapshot.GetEffective(SkillRules.KeyStat(Skill.Firearms)), false);

    // The monster damage table in the damage order (normal, poison, electrical, fire, fatigue); a missing entry is 0.
    private static DamageRanges Natural(IReadOnlyList<CombatDamageRange> stored)
    {
        DamageRange At(CombatDamageIndex index) => (int)index < stored.Count ? new(stored[(int)index].Minimum, stored[(int)index].Maximum) : DamageRange.Zero;
        return new(At(CombatDamageIndex.Normal), At(CombatDamageIndex.Poison), At(CombatDamageIndex.Electrical), At(CombatDamageIndex.Fire),
            At(CombatDamageIndex.Fatigue));
    }

    // A stored material outside the fourteen wears nothing in the original (its wear table treats every other value as 0), which a
    // critter defending as flesh reproduces: a critter's material is read by the wear alone.
    private static ObjectMaterial Material(int stored) => Enum.IsDefined((ObjectMaterial)stored) ? (ObjectMaterial)stored : ObjectMaterial.Flesh;

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
/// ARC-WORLD-092: what an NPC attacks with (<see cref="SessionCritterCombat.AttackValues"/>), as <see cref="SessionHeroAttack"/>
/// describes the hero's: the combatant, weapon, skill with its level and training, weapon speed, the attack's action-point cost, the
/// effective Speed stat (the attack animation's rate), the reach and whether this session can run the attack (melee).
/// </summary>
public sealed record SessionNpcAttackValues(Combatant Combatant, CombatWeapon Weapon, CombatSkill Skill, int SkillLevel, SkillTraining Training,
    int WeaponSpeed, int AttackCost, int Speed, int Range, bool Supported);

/// <summary>
/// What the hero attacks with (<see cref="GameSessionOwner.QueryHeroAttack"/>): its <see cref="Combatant"/> under its life state,
/// the weapon it strikes with (bare hands without one), the weapon's skill with its level and training, the weapon speed, the
/// effective Speed stat (the attack animation's rate), the weapon's range (1 for bare hands) and whether the attack is one this
/// session can run: melee, bow and firearms (ARC-COMBAT-015), and since ARC-COMBAT-019 a throwing-skill weapon (a boomerang, weapon
/// flag 0x40, 0x465D80), whose projectile flies back to the hero. A weapon that "default throws" (ARC-COMBAT-017) is described as
/// wielded; its throw is <see cref="SessionAttack.Throw"/>.
/// </summary>
public sealed record SessionHeroAttack(Combatant Combatant, CombatWeapon Weapon, CombatSkill Skill, int SkillLevel, SkillTraining Training,
    int WeaponSpeed, int Speed, int Range, bool Supported)
{
    /// <summary>ARC-COMBAT-015: the skill takes the shared check's distance terms (RA2: bow, throwing, firearms): the range limit, the
    /// range penalty and the line of fire's difficulty.</summary>
    public bool RangedSkill => Skill is CombatSkill.Bow or CombatSkill.Throwing or CombatSkill.Firearms;

    /// <summary>ARC-COMBAT-012/015: the attack takes the original's ranged path (RA1: the throwing skill or a range above 1), whose line
    /// of fire may strike another object first (RA4).</summary>
    public bool RangedPath => Skill == CombatSkill.Throwing || Range > 1;
}

/// <summary>
/// ARC-COMBAT-015: the line of fire from the hero's tile to the struck object's as the host traced it (0x4ADE00,
/// <see cref="LineOfFireRules.Trace{TObject}"/>): its summed <see cref="Cost"/> (the shared check's cover, SC8) and whether an object
/// other than the struck one stops it (<see cref="Blocked"/>: the check's impossible +1,000,000).
/// </summary>
public readonly record struct SessionLineOfFire(int Cost, bool Blocked);

/// <summary>
/// One attack of the hero on an NPC (<see cref="GameSessionOwner.AttackCritter"/>). <see cref="Aim"/> is the aimed location (null:
/// not aimed, rolled); <see cref="Distance"/> the tile distance to the target (the larger coordinate difference, 0x4B96F0): a melee
/// weapon's must be within its reach, a bow's or firearm's may lie beyond its range, which the check makes impossible (SC8).
/// <see cref="HeroWorn"/> and <see cref="TargetWorn"/> are the items in the hero's and the target's wear slots with their durability
/// baselines, for the attack's wear (ARC-COMBAT-007, <see cref="GameSessionOwner.ApplyAttackWear"/>); the hero's must be exactly the
/// items it wears. Null: the host passes no item data and that side's items take no wear (the bare-handed hero's own wear still
/// applies).
/// </summary>
public sealed record SessionAttack(SessionCritterCombat Target)
{
    public HitLocation? Aim { get; init; }
    /// <summary>ARC-COMBAT-016: the shot flies as a projectile (the wielded weapon has a projectile art, field 127, on the ranged path,
    /// ledger PF1): the attack is decided now and a hit's damage waits for the strike (<see cref="GameSessionOwner.LandShot"/>);
    /// the result names the shot in flight (<see cref="SessionAttackResult.Shot"/>), a hit or a miss.</summary>
    public bool Flight { get; init; }
    /// <summary>ARC-COMBAT-017: the wielded weapon carries the "default throws" flag (0x200) and is thrown (ledger RH2, TW rows): unless
    /// its no-drop flag keeps it in the hand, it is checked with the throwing skill and its thrown range, leaves the hero at the shot and
    /// flies (<see cref="Flight"/> is required) until it lands (<see cref="GameSessionOwner.LandThrownItem"/>).</summary>
    public SessionThrow? Throw { get; init; }
    public int Distance { get; init; } = 1;
    /// <summary>ARC-COMBAT-015: the line of fire to the target, read by the bow and firearms skills only (RA2); null: an open line
    /// without cover.</summary>
    public SessionLineOfFire? LineOfFire { get; init; }
    public IReadOnlyDictionary<EquipmentSlot, SessionWearItem>? HeroWorn { get; init; }
    public IReadOnlyDictionary<EquipmentSlot, SessionWearItem>? TargetWorn { get; init; }
    /// <summary>The target's cell now, where a critical drops its items (ARC-COMBAT-009); null: its drops are not applied.</summary>
    public Arcanum.Simulation.Movement.CellCoord? TargetCell { get; init; }
    /// <summary>The actor's map as the original's drop reads it (ARC-COMBAT-010): with it a drop draws its distance and directions and
    /// lands where the original's placement puts it; null: on the holder's cell, without draws.</summary>
    public Arcanum.Simulation.Items.IDropMap? DropMap { get; init; }
    /// <summary>ARC-MAGIC-023: the trigger host the damage step's item events fire through (stunned, going unconscious, target going
    /// unconscious, dying, parent hit, damages opponent and target hit: <see cref="SessionAttackResult.ItemSpells"/>), a host of this
    /// session; null: they fire nothing, as before ARC-MAGIC-023.</summary>
    public ISessionItemTriggers? ItemTriggers { get; init; }
}

/// <summary>
/// The result of <see cref="GameSessionOwner.AttackCritter"/>: the resolution (null when refused before the rolls), who took the
/// damage and the life outcome of that step (null when none was dealt), what the target's experience paid the hero for the
/// hit and the kill, and the attack's armour and weapon wear (<see cref="Wear"/>, ARC-COMBAT-007; null when the resolution named no
/// wear or an earlier step was refused). A
/// later step that was refused (an experience, life or item budget, the revision) is <see cref="StepFailure"/>; the steps before it
/// stay applied, as each one is a step of the source's damage sequence.
/// </summary>
public sealed record SessionAttackResult(GameSessionDisposition Disposition, GameSessionFailure Failure, AttackResolution? Resolution,
    DamageRecipient? Recipient, LifeOutcome? Life, SessionExperienceAward? HitAward, SessionExperienceAward? KillAward,
    GameSessionFailure StepFailure, GameSessionVersion Version)
{
    public SessionAttackWearResult? Wear { get; init; }
    /// <summary>ARC-COMBAT-012: the shot's ammunition the hero spent (a hit or a miss alike); null when its weapon takes none.</summary>
    public SessionAmmoUse? Ammunition { get; init; }
    /// <summary>ARC-COMBAT-016: the shot now in flight (<see cref="SessionAttack.Flight"/>, not refused); a hit has no damage yet. The
    /// first of <see cref="Shots"/>.</summary>
    public SessionShot? Shot => Shots.Count > 0 ? Shots[0] : null;
    private readonly IReadOnlyList<SessionShot>? _shots;
    /// <summary>ARC-COMBAT-018: every projectile of the shot now in flight, in launch order: one, or two for a bow at expert training
    /// (0x4B39B0), each striking on its own with the same decided outcome; empty when nothing flies.</summary>
    public IReadOnlyList<SessionShot> Shots { get => _shots ?? Array.Empty<SessionShot>(); init => _shots = value; }
    /// <summary>ARC-COMBAT-018: a missed throw's scattered destination (0x4B39B0, ledger RH2): its shot has no target, flies free from
    /// the start and ends on leaving this tile (<see cref="ProjectileStrikeKind.DestinationEnd"/>) unless it strikes on the way; null
    /// for any other shot.</summary>
    public Arcanum.Simulation.Movement.CellCoord? Scatter { get; init; }
    private readonly IReadOnlyList<SessionCriticalStep>? _criticals;

    /// <summary>The critical item steps (ARC-COMBAT-009) of whoever took the damage, in the original's order: the arm-hit drops, the
    /// helmet and weapon drops and the ammunition effects; empty when the attack had none.</summary>
    public IReadOnlyList<SessionCriticalStep> Criticals { get => _criticals ?? Array.Empty<SessionCriticalStep>(); init => _criticals = value; }
    private readonly IReadOnlyList<SessionItemTriggerFiring>? _itemSpells;

    /// <summary>ARC-MAGIC-023: the executor calls of the damage step's item events, in order (<see cref="SessionAttack.ItemTriggers"/>);
    /// empty without a host or without damage to the target; none from a refused experience or item step on.</summary>
    public IReadOnlyList<SessionItemTriggerFiring> ItemSpells { get => _itemSpells ?? Array.Empty<SessionItemTriggerFiring>(); init => _itemSpells = value; }
    private readonly IReadOnlyList<SessionItemEvent>? _itemEvents;

    /// <summary>ARC-MAGIC-025: the item events of a critical self-hit's life step (its <see cref="SessionLifeResult.ItemEvents"/>: the
    /// fatigue setter's going unconscious, IC4; the hero's damage step has no context to read, IC2), which the host fires
    /// (<see cref="SessionSpellCaster.FireItemEvents"/>); empty otherwise (a hit on the target fires its own damage step,
    /// <see cref="ItemSpells"/>).</summary>
    public IReadOnlyList<SessionItemEvent> ItemEvents { get => _itemEvents ?? Array.Empty<SessionItemEvent>(); init => _itemEvents = value; }
}

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
            CriticalHitFateArmed = FateRules.IsArmed(_hero.State.Fate, FateKind.CriticalHit),
            CriticalMissFateArmed = FateRules.IsArmed(_hero.State.Fate, FateKind.CriticalMiss),
            // ARC-COMBAT-009: every pre-made PC's critical-hit body type is 0 (critical census).
            CriticalBody = 0,
            Constitution = snapshot.GetEffective(CharacterStat.Constitution),
            ConstitutionMaximum = CharacterStatRules.Range(_hero.State.Race, CharacterStat.Constitution).Maximum,
            Worn = CombatantRules.WornItems(_equipment.Worn.Keys),
            Scarred = state.Scars > 0,
        };
        var weaponSkill = skills[SkillRules.Of(equipment.Skill)];
        var range = equipment.Weapon.Kind == WeaponKind.Unarmed ? 1 : Math.Max(1, equipment.Weapon.Range);
        return new(combatant, equipment.Weapon, equipment.Skill, weaponSkill.Level, weaponSkill.Training, equipment.WeaponSpeed,
            snapshot.GetEffective(CharacterStat.Speed), range,
            equipment.Skill is CombatSkill.Melee or CombatSkill.Bow or CombatSkill.Firearms or CombatSkill.Throwing);
    }

    /// <summary>
    /// The hero attacks an NPC once (ARC-COMBAT-005; the source's hit step of the attack goal, then 0x4B4390): the attack is
    /// resolved (<see cref="AttackResolver.Resolve"/>, under the active rules and difficulty, rolls from the session generator as
    /// <c>combat.*</c>), its damage applied to the target, or to the hero on a critical self-hit (<see cref="ApplyDamage"/>), then an
    /// NPC's hit-point damage takes its share of the experience pool for the hero unless the NPC is stoned
    /// (<see cref="RecordNpcDamage"/>), and a kill pays the hero a fifth of the worth (<see cref="RecordNpcKill"/>); experience calls
    /// are skipped without experience data. A critical rolls the original's critical-effect tables (<see cref="OriginalCriticalEffects"/>,
    /// ARC-COMBAT-009) and their item steps follow (<see cref="SessionAttackResult.Criticals"/>: the arm-hit drops, then the wear, then
    /// the helmet drop, the ammunition and the weapon drop; an NPC's item drops at <see cref="SessionAttack.TargetCell"/>, the hero's
    /// among its placed items, and a critical miss spends the hero's ammunition, ARC-COMBAT-013). The attack's armour and weapon wear (<see cref="ApplyAttackWear"/>, ARC-COMBAT-007)
    /// with the hero as attacker and the NPC as target and the items of <see cref="SessionAttack.HeroWorn"/> and
    /// <see cref="SessionAttack.TargetWorn"/>: the armour of whoever took the damage at the hit location, the critical weapon effects,
    /// the hero's weapon (with the art subtype its <see cref="SessionWearItem"/> carries) or bare hands by the NPC's material. Refused before any roll, in order: no hero
    /// (<see cref="GameSessionFailure.AttackUnavailable"/>), a target whose values do not hold
    /// (<see cref="GameSessionFailure.InvalidLifeTarget"/> …), wear items that do not hold (<see cref="GameSessionFailure.InvalidWear"/>), an
    /// aim at the torso, a distance below 0 or beyond the weapon's reach (a bow's or firearm's beyond 100,000), a line-of-fire cost
    /// outside 0..100,000, a target cell outside 0 .. 2^24 − 1, a throw (<see cref="SessionAttack.Throw"/>) that does not fly, has a
    /// weight outside 0..1,000,000, no wielded weapon or one of another skill than melee, or a distance beyond the weapon's reach
    /// (<see cref="GameSessionFailure.InvalidAttack"/>), the hero's boomerang still away (<see cref="GameSessionFailure.WeaponInFlight"/>,
    /// ARC-COMBAT-019), the version, an exhausted revision
    /// (<see cref="GameSessionFailure.RevisionExhausted"/>). A bow or firearm (ARC-COMBAT-015) is checked with the distance terms of
    /// the shared check (SC8: impossible beyond its range, 5 per tile beyond half the Perception below master, impossible when
    /// <see cref="SessionAttack.LineOfFire"/> is blocked, else its cost as cover) and spends its shot's ammunition (RA3). A thrown weapon
    /// (ARC-COMBAT-017) is checked so with the throwing skill and its thrown range; unless the attack is refused it leaves the hero's
    /// hand in a revision of its own and lies hidden on the hero's cell (<see cref="GameSessionOwner.QueryThrownShot"/>).
    /// A boomerang (ARC-COMBAT-019) stays in the hand and is checked with the throwing skill and the thrown range of its own weight
    /// (the session's weight row, 0x461370 with the hero as holder); its shot flies back (<see cref="ReturnShot"/>).
    /// A resolver refusal (the hero cannot act, the target is dead) is NoChange with the resolution. The resolution's draws are spent
    /// once it ran; a fate the resolver used (ARC-WORLD-054) is cleared in a revision of its own, so such an attack is Changed even
    /// without damage; each later step commits on its own revision.
    /// Not modelled: the target's taking-damage and dying scripts, its reaction and AI, the critical effects listed as not ready
    /// (an NPC's ammunition, the NPC search budget of the drop's path), backstab, the target's awareness (an NPC always sees the hero)
    /// and light (every position is lit).
    /// </summary>
    public SessionAttackResult AttackCritter(GameSessionVersion expected, SessionAttack attack) => CritterAttack(expected, attack, null);

    /// <summary>
    /// ARC-COMBAT-016: the projectile of <paramref name="shot"/> strikes an NPC (0x4B2870 → 0x4B2F60, ledger PF rows): the shot's own
    /// target when the shot hit, with the hit decided at the shot (its location, aim, critical and weapon magic), or any other NPC as
    /// a forced hit (a stray strike or an obstruction: torso, no critical, no roll of the check). The damage is rolled now on the NPC
    /// as it is now with the shot's weapon, then every step of <see cref="AttackCritter"/> after the resolution follows (damage,
    /// experience, criticals, wear); the shot is gone afterwards. Nothing of the shot is spent again (fate, concealment, ammunition).
    /// <see cref="SessionAttack.Distance"/>, <see cref="SessionAttack.LineOfFire"/> and <see cref="SessionAttack.Flight"/> are not
    /// read. Refused as <see cref="AttackCritter"/> is, and with <see cref="GameSessionFailure.InvalidAttack"/> for a shot not in
    /// flight, a missed shot striking its own target or a thrown shot that struck already; a target dead now is NoChange with the
    /// refusal, and the shot is gone (a thrown shot waits for its item's landing, <see cref="LandThrownItem"/>). ARC-COMBAT-019: a
    /// boomerang's shot turns back at its strike instead (0x4B2690 before the hit, PF8) and flies back to the hero, striking nothing
    /// more (a second strike is refused), until its flight ends (<see cref="EndShot"/>).
    /// </summary>
    public SessionAttackResult LandShot(GameSessionVersion expected, SessionShot shot, SessionAttack attack) => CritterAttack(expected, attack, shot);

    private SessionAttackResult CritterAttack(GameSessionVersion expected, SessionAttack attack, SessionShot? landing, PackThrow? pack = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(attack);
        if (_hero is null) return RejectAttack(GameSessionFailure.AttackUnavailable);
        var target = attack.Target;
        var resolved = Resolve(target.Target, out var record, out var facts, out _);
        if (resolved != GameSessionFailure.None) return RejectAttack(resolved);
        PendingShot? pending = null;
        if (landing is { } id && (!_shots.TryGetValue(id, out pending) || pending.Struck || pending.Returning || !pending.Hit && pending.Entity == target.Entity))
            return RejectAttack(GameSessionFailure.InvalidAttack);
        // ARC-COMBAT-023: a pack item's throw strikes with the item at its thrown range (ThrowHeroItem).
        var hero = pack?.Hero ?? QueryHeroAttack()!;
        // ARC-COMBAT-016: the projectile carries its weapon (field 0x50) and skill to the strike.
        if (pending is not null) hero = hero with { Weapon = pending.Hero.Weapon, Skill = pending.Hero.Skill, SkillLevel = pending.Hero.SkillLevel, Training = pending.Hero.Training };
        var heroSide = new SessionWearSide { Life = SessionLifeTarget.Hero, Worn = attack.HeroWorn };
        // The weapon's art subtype is item data, passed with its baseline (the session saves none of it).
        if (pending is null && pack is null && attack.HeroWorn?.TryGetValue(EquipmentSlot.Weapon, out var wielded) == true && hero.Weapon.Kind == WeaponKind.Weapon)
            hero = hero with { Weapon = hero.Weapon with { ArtSubtype = wielded.ArtSubtype } };
        var targetSide = new SessionWearSide { Life = target.Target, Worn = attack.TargetWorn };
        if (!ValidWear(heroSide, targetSide, targetBroken: false, wear: null)) return RejectAttack(GameSessionFailure.InvalidWear);
        if (attack.Aim is HitLocation.Torso || attack.Aim is { } aim && !Enum.IsDefined(aim) || pending is null && !AttackDistanceFits(hero, attack.Distance) ||
            attack.LineOfFire is { Cost: < 0 or > AttackResolver.MaxMagnitude } || attack.TargetCell is { } cell && !DropCellFits(cell))
            return RejectAttack(GameSessionFailure.InvalidAttack);
        // ARC-COMBAT-019: the attack start refuses while the hero's boomerang is away (0x434B00, attacker field 219 bit 0x4).
        // ARC-COMBAT-023: the throw starter (0x434F80) makes no such test.
        if (pending is null && pack is null && HeroBoomerangAway()) return RejectAttack(GameSessionFailure.WeaponInFlight);
        // ARC-COMBAT-017: the reach is the weapon's (RH1); a thrown weapon is then checked with the throwing skill and its thrown range.
        var thrown = pack?.Item;
        if (pending is null && pack is null && ThrowingHero(ref hero, attack.Throw, attack.Flight, out thrown) is var throwFailure && throwFailure != GameSessionFailure.None)
            return RejectAttack(throwFailure);
        if (pending is null && thrown is null) BoomerangHero(ref hero);
        if (pending is null && !hero.Supported) return RejectAttack(GameSessionFailure.AttackUnsupported);
        // ARC-COMBAT-018: a missed throw scatters from its target location (RH2).
        var scatterFrom = thrown is null ? null : pack?.Target ?? attack.Throw!.Target ?? attack.TargetCell;
        if (thrown is not null && (scatterFrom is not { } from || !DropCellFits(from))) return RejectAttack(GameSessionFailure.InvalidAttack);
        if (pending is null && attack.Flight && _shots.Count > MaxShotsInFlight - ShotProjectiles(hero)) return RejectAttack(GameSessionFailure.BudgetExceeded);
        if (!IsCurrent(expected)) return RejectAttack(GameSessionFailure.StaleVersion);
        // A throw may take three revisions before the damage (the fate, the concealment, the item leaving the hand).
        if (_revision >= long.MaxValue - (thrown is null ? 0 : 2)) return RejectAttack(GameSessionFailure.RevisionExhausted);

        var targetStatus = LifeRules.Evaluate(record.State, facts!);
        // A follower the session levelled defends with its saved sheet's skills and level (ARC-SKILLS-002).
        var defending = _advancement.Npcs.TryGetValue(target.Entity, out var levelled) ? target.WithSheet(levelled.Sheet) : target;
        Combatant defender;
        try { defender = defending.Combatant(targetStatus, record.State, _rules.Combat); }
        catch (ArgumentException) { return RejectAttack(GameSessionFailure.InvalidAttack); }
        // ARC-COMBAT-009: the items the critical-hit procedure looks at are the ones the host says the NPC wears now (an earlier
        // critical may have dropped or destroyed some of its map data's).
        if (attack.TargetWorn is { } targetWorn) defender = defender with { Worn = CombatantRules.WornItems(targetWorn.Keys) };
        var request = new AttackRequest
        {
            Attacker = hero.Combatant,
            Target = defender,
            Weapon = hero.Weapon,
            Skill = hero.Skill,
            SkillLevel = hero.SkillLevel,
            Training = hero.Training,
            AimedLocation = pending is null ? attack.Aim : null,
            Distance = pending is null ? attack.Distance : 1,
            CoverRating = pending is null ? attack.LineOfFire?.Cost ?? 0 : 0,
            LineOfFireBlocked = pending is null && (attack.LineOfFire?.Blocked ?? false),
            Difficulty = _difficulty,
            // ARC-COMBAT-023: a thrown pack item takes no ammunition of the wielded weapon.
            AmmunitionShort = pending is null && pack is null && ShotAmmunition() is { Short: true },
            DeferHit = pending is null && attack.Flight,
        };
        var rolls = new TrialCombatRolls(new SeededRandom(_random.Seed, _random.Draws));
        AttackResolution resolution;
        int? scatterRotation = null;
        try
        {
            resolution = pending is null ? AttackResolver.Resolve(request, rolls, OriginalCriticalEffects.Instance, _rules.Combat)
                : AttackResolver.LandHit(request, pending.Hit && pending.Entity == target.Entity ? pending.Check : AttackResolver.ForcedHit(pending.Check.WeaponMagicCancelled),
                    rolls, OriginalCriticalEffects.Instance, _rules.Combat);
            // ARC-COMBAT-018: the ranged path draws a missed throw's direction after the dispatcher's decisions (0x4B39B0, 0x4CEF00(0, 7)).
            if (thrown is not null && resolution.Refusal == AttackRefusal.None && resolution.Outcome is not (AttackOutcome.Hit or AttackOutcome.CriticalHit))
                scatterRotation = rolls.Roll(CombatRollPurpose.ThrowScatter, 0, ThrowRules.ScatterDirections - 1);
        }
        catch (TrialCombatRolls.ExhaustedException) { return RejectAttack(GameSessionFailure.RandomExhausted); }
        catch (ArgumentException) { return RejectAttack(GameSessionFailure.InvalidAttack); }
        _random = rolls.Trial;
        if (landing is { } landed) EndStrike(landed, pending!);
        // The fate the resolver used (FT6) is cleared at once, in a revision of its own before the damage steps.
        if (resolution.Fate != FateUse.None && pending is null)
        {
            ClearUsedAttackFate(resolution.Fate);
            Revise();
        }
        // ARC-SKILLS-008 (ST4): the attack event on an NPC the hero leads ends the concealment of the hero and of the followers in
        // its follower list (a waiting one keeps its own), in the same revision as the fate's or one of its own.
        var concealmentEnded = pending is null && resolution.Refusal == AttackRefusal.None && target.Target.Entity is { } victim
            && EndConcealmentForAttackOn(SessionCombatant.Critter(victim));
        if (concealmentEnded && resolution.Fate == FateUse.None) Revise();
        var throwing = thrown is not null && resolution.Refusal == AttackRefusal.None;
        // ARC-UI-086, ARC-UI-094: an ammunition or gold throw splits its units or coins off the stack at the moment the item leaves (checked by ThrowHeroItem).
        if (throwing && pack?.Split is { } split) thrown = SplitForThrow(pack.Item, split.Count, split.Kind);
        if (throwing) heroSide = heroSide with { Worn = Throw(thrown!, attack.HeroWorn) };
        // ARC-COMBAT-012: the shot's ammunition goes for a hit or a miss (the original takes it before every roll).
        var ammunition = pending is null && pack is null && resolution.Refusal == AttackRefusal.None ? SpendShot() : null;
        if (ammunition is { Disposition: GameSessionDisposition.Rejected } refusedSpend)
            return new(GameSessionDisposition.Changed, GameSessionFailure.None, resolution, null, null, null, null, refusedSpend.Failure, Version) { Ammunition = refusedSpend };
        var spent = ammunition is { Disposition: GameSessionDisposition.Changed };
        // ARC-COMBAT-016: a shot that flies is in flight from now on, hit or miss (a critical miss has turned on the hero already);
        // ARC-COMBAT-018: as two projectiles for a bow at expert, and a missed throw without its target, toward its scattered tile.
        var scatter = scatterRotation is { } rotation ? ScatterThrow(scatterFrom!.Value, hero.SkillLevel, rotation, attack.DropMap) : (Arcanum.Simulation.Movement.CellCoord?)null;
        var flying = pending is null && attack.Flight && resolution.Refusal == AttackRefusal.None
            ? Launch(new PendingShot(hero, resolution, resolution.Outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit, scatter is null ? target.Entity : null, null)
                { Thrown = throwing ? new SessionThrownShot(thrown!, hero.Weapon.Range) : null, Boomerang = Boomerang(hero, thrown), WeaponItem = throwing ? thrown : WieldedEntity() },
                ShotProjectiles(hero)) : [];
        if (resolution.Refusal != AttackRefusal.None || LifeDamage.FromAttack(resolution) is not { } damage)
            return new(resolution.Fate != FateUse.None && pending is null || concealmentEnded || spent || throwing ? GameSessionDisposition.Changed : GameSessionDisposition.NoChange,
                GameSessionFailure.None, resolution, null, null, null, null, GameSessionFailure.None, Version) { Ammunition = ammunition, Shots = flying, Scatter = scatter };

        var recipient = resolution.Damage!.Recipient;
        // ARC-MAGIC-023: the hero's worn items and the context's weapon (0x4B2210: the wielded weapon, or the projectile's item) for the
        // damage step's item events, as they are before the step.
        var heroWorn = _equipment.Worn.Values.OrderBy(item => item.Slot).Select(item => item.Entity).ToArray();
        var contextWeapon = pending is null ? WieldedEntity() : pending.WeaponItem;
        var applied = ApplyDamage(Version, recipient == DamageRecipient.Target ? target.Target : SessionLifeTarget.Hero, damage);
        if (applied.Disposition == GameSessionDisposition.Rejected)
            return new(GameSessionDisposition.Changed, GameSessionFailure.None, resolution, recipient, null, null, null, applied.Failure, Version)
            { Ammunition = ammunition, Shots = flying, Scatter = scatter };
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
        // ARC-UI-097: the hero's kill and injury log (0x4F5330 after the death's experience, 0x4F5690 in the damage function): the NPC the hero
        // killed, or the hero's own critical self-hit (the attacker is the victim: the self-inflicted cause).
        if (stepFailure == GameSessionFailure.None && recipient == DamageRecipient.Target && outcome.Events.HasFlag(LifeEvents.Died))
            stepFailure = RecordKillLog(defending);
        else if (stepFailure == GameSessionFailure.None && recipient == DamageRecipient.Attacker)
            stepFailure = RecordHeroInjuries(outcome.Events, SelfInflictedCause);
        SessionAttackWearResult? wear = null;
        var criticals = new List<SessionCriticalStep>();
        // ARC-MAGIC-023: the damage step's item events (IC2). The parent hit's melee bit (0x4CBAD0) reads an attack entry's context and
        // the range of the weapon the swing or shot carries (0x465E30): the reach, which a throw keeps (its thrown range is the weapon's
        // Range only for the check).
        var striker = pending?.Hero ?? hero;
        var itemSteps = recipient == DamageRecipient.Target && stepFailure == GameSessionFailure.None && attack.ItemTriggers is not null
            ? ItemSpellTriggerRules.DamageStep(facts!, damage, outcome, attacker: true, killer: true,
                ItemSpellTriggerRules.MeleeHit(attackEntry: true, striker.Weapon.Kind == WeaponKind.Unarmed ? null : striker.Range))
            : [];
        var npcWorn = attack.TargetWorn?.OrderBy(pair => pair.Key).Select(pair => pair.Value.Entity).ToArray() ?? [];
        // The stun's and the knockdown's walks come before the step's drops and wear (IC3), the others after them.
        var itemSpells = FireDamageStepItems(attack.ItemTriggers, itemSteps.Where(step => BeforeItemSteps(step, outcome)), target.Entity, npcWorn,
            heroWorn, contextWeapon).ToList();
        if (stepFailure == GameSessionFailure.None)
            (wear, stepFailure) = CriticalAndWearSteps(resolution, outcome, heroSide, targetSide, recipient == DamageRecipient.Target ? target : null,
                attack.TargetCell, attack.DropMap, criticals);
        if (stepFailure == GameSessionFailure.None)
            itemSpells.AddRange(FireDamageStepItems(attack.ItemTriggers, itemSteps.Where(step => !BeforeItemSteps(step, outcome)), target.Entity,
                StillWorn(npcWorn, wear, criticals), heroWorn, contextWeapon));
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, resolution, recipient, outcome, hitAward, killAward, stepFailure, Version)
        {
            Wear = wear,
            Criticals = criticals,
            Ammunition = ammunition,
            Shots = flying,
            Scatter = scatter,
            ItemSpells = itemSpells,
            ItemEvents = recipient == DamageRecipient.Attacker ? applied.ItemEvents : [],
        };
    }

    private string? WieldedEntity() => _equipment.Worn.TryGetValue(EquipmentSlot.Weapon, out var wielded) ? wielded.Entity : null;

    /// <summary>ARC-COMBAT-015: a melee weapon strikes within its reach; a bow or firearm also beyond its range, where the shared check's
    /// range limit makes the shot impossible (SC8: the original's attack goal shoots within range, the target may have moved since).</summary>
    private static bool AttackDistanceFits(SessionHeroAttack hero, int distance) =>
        distance >= 0 && distance <= (hero.RangedSkill ? AttackResolver.MaxMagnitude : hero.Range);

    /// <summary>ARC-COMBAT-012 (A): the shot's ammunition of the hero's wielded weapon (the dispatcher's 0x4B65D0, ledger RA1–RA3): its
    /// ammunition type and per-shot consumption (fields 125 and 126), and whether the hero carries fewer units of that type; null for
    /// a weapon without an ammunition type (the hero is a PC, so it always needs it). A type outside 0..3 names no stack (AMA3).</summary>
    private (int Type, int Count, bool Short)? ShotAmmunition()
    {
        if (!_equipment.Worn.TryGetValue(EquipmentSlot.Weapon, out var wielded) || wielded.Item.Weapon is not { AmmoType: { } type } weapon) return null;
        var units = type is >= 0 and < SessionAmmoRecord.AmmoTypes && HeroStackOfType(type) is { } stack ? stack.Units : 0;
        return (type, weapon.AmmoConsumption, units < weapon.AmmoConsumption);
    }

    /// <summary>ARC-COMBAT-012 (A): spends the shot's ammunition after an attack the resolver did not refuse (a hit or a miss): the
    /// weapon's consumption from the hero's stack of its type, all or nothing; null for a weapon without one or a consumption of 0.</summary>
    private SessionAmmoUse? SpendShot() =>
        ShotAmmunition() is { Count: > 0 } shot ? SpendAmmo(Version, SessionAmmoHolder.Hero, shot.Type, shot.Count) : null;

    /// <summary>
    /// The item steps after the life step of an attack on a critter or a critical self-hit (ARC-COMBAT-007, ARC-COMBAT-009), in the
    /// original's relative order (0x4B4390), after the whole life step: the arm-hit drops of whoever took the damage, the wear (its
    /// armour, its weapon's critical damage and destruction, the attacker's side), its helmet drop, the ammunition effects of a
    /// critical miss and its weapon drop. An NPC's item drops at <paramref name="recipientCell"/>, the hero's at its own cell among its
    /// placed items (ARC-WORLD-059); a critical miss spends the hero's ammunition of the attack's weapon (ARC-COMBAT-013).
    /// </summary>
    private (SessionAttackWearResult? Wear, GameSessionFailure StepFailure) CriticalAndWearSteps(AttackResolution resolution, LifeOutcome life,
        SessionWearSide attacker, SessionWearSide target, SessionCritterCombat? npc, Arcanum.Simulation.Movement.CellCoord? recipientCell,
        Arcanum.Simulation.Items.IDropMap? dropMap, List<SessionCriticalStep> criticals)
    {
        var damage = resolution.Damage!;
        var whose = damage.Recipient;
        var recipient = whose == DamageRecipient.Target ? target : attacker;
        var failure = GameSessionFailure.None;
        // The attack's weapon, before a step of this attack breaks or drops it: the damage step's ammunition use reads it (CR15).
        // ARC-WORLD-092: only the attacker's own weapon (a hero hit by an NPC is no attacker).
        var attackWeapon = whose == DamageRecipient.Attacker && recipient.Life is { IsHero: true } && _equipment.Worn.TryGetValue(EquipmentSlot.Weapon, out var wielded) ? wielded.Item.Weapon : null;
        if (life.Events.HasFlag(LifeEvents.ArmHitDropsItems))
            recipient = ArmHitDrops(whose, recipient, npc, recipientCell, dropMap, criticals, ref failure);
        if (failure != GameSessionFailure.None) return (null, failure);
        SessionAttackWearResult? wear = null;
        if (resolution.Wear is { Any: true })
        {
            wear = WearSteps(new SessionAttackWear
            {
                Resolution = resolution,
                Attacker = whose == DamageRecipient.Attacker ? recipient : attacker,
                Target = whose == DamageRecipient.Target ? recipient : target,
            });
            failure = wear.Disposition == GameSessionDisposition.Rejected ? wear.Failure : wear.StepFailure;
            if (failure != GameSessionFailure.None) return (wear, failure);
        }
        if (damage.HitEffects.HasFlag(CriticalHitEffects.DropHelmet))
            recipient = CriticalDrop(SessionCriticalStepKind.DropHelmet, whose, recipient, EquipmentSlot.Helmet, recipientCell, dropMap, criticals, ref failure);
        AmmunitionSteps(damage, recipient, attackWeapon, criticals, ref failure);
        if (damage.HitEffects.HasFlag(CriticalHitEffects.DropWeapon))
            CriticalDrop(SessionCriticalStepKind.DropWeapon, whose, recipient, EquipmentSlot.Weapon, recipientCell, dropMap, criticals, ref failure);
        return (wear, failure);
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
