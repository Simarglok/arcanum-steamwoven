using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Skills;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Skills;

// The shared skill check (ARC-SKILLS-003, ledger SC1–SC12): numbers read in the original 1.0.7.4 executable by two readers.
[TestClass]
public sealed class SkillCheckRulesTests
{
    private static readonly SkillEffectivenessFacts Plain = new();

    [TestMethod]
    public void FlagsGiveTheOpponentsTheCombatDivisorsAndTheFateKinds()
    {
        Assert.AreEqual(SkillOpponent.Perception, SkillCheckRules.Opponent(Skill.PickPocket));
        Assert.AreEqual(SkillOpponent.Perception, SkillCheckRules.Opponent(Skill.Prowling));
        Assert.AreEqual(SkillOpponent.SameSkill, SkillCheckRules.Opponent(Skill.Gambling));
        Assert.AreEqual(SkillOpponent.Intelligence, SkillCheckRules.Opponent(Skill.Persuasion));
        Assert.AreEqual(4, Enum.GetValues<Skill>().Count(s => SkillCheckRules.Opponent(s) != SkillOpponent.None));
        CollectionAssert.AreEquivalent(new[] { Skill.Bow, Skill.Dodge, Skill.Melee, Skill.Throwing, Skill.Firearms },
            Enum.GetValues<Skill>().Where(SkillCheckRules.IsCombat).ToArray());
        CollectionAssert.AreEqual(
            new[] { SkillFate.PickPocket, SkillFate.Gambling, SkillFate.Heal, SkillFate.Repair, SkillFate.PickLocks, SkillFate.DisarmTraps },
            new[] { Skill.PickPocket, Skill.Gambling, Skill.Heal, Skill.Repair, Skill.PickLocks, Skill.DisarmTraps }.Select(SkillCheckRules.Fate).ToArray());
        Assert.AreEqual(6, Enum.GetValues<Skill>().Count(s => SkillCheckRules.Fate(s) != SkillFate.None));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SkillCheckRules.Opponent((Skill)16));
    }

    [TestMethod]
    public void OpposedReadsAQuarterOfTheTableBetweenTheThresholds()
    {
        Assert.AreEqual(100, SkillCheckRules.Opposed(3, null));
        Assert.AreEqual(100, SkillCheckRules.Opposed(15, 5));
        Assert.AreEqual(0, SkillCheckRules.Opposed(0, 10));
        int[] expected = [13, 16, 19, 22, 26, 30, 34, 38, 42, 47, 52, 56, 61, 65, 68, 72, 75, 78, 81];
        for (var d = -9; d <= 9; d++) Assert.AreEqual(expected[d + 9], SkillCheckRules.Opposed(10 + d, 10), $"difference {d}");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SkillCheckRules.Opposed(0, 2_000_000));
    }

    [TestMethod]
    public void EffectivenessFormulasOfEachSkill()
    {
        int E(Skill skill, int level) => SkillCheckRules.Effectiveness(skill, level, Plain);
        Assert.AreEqual(25, E(Skill.Bow, 0));
        Assert.AreEqual(125, E(Skill.Bow, 20));
        Assert.AreEqual(40, E(Skill.Dodge, 20));
        Assert.AreEqual(75, E(Skill.Melee, 10));
        Assert.AreEqual(95, E(Skill.Throwing, 10));
        Assert.AreEqual(0, E(Skill.Backstab, 20));
        Assert.AreEqual(40, E(Skill.SpotTrap, 10));
        Assert.AreEqual(50, E(Skill.Haggle, 10));
        Assert.AreEqual(90, E(Skill.Haggle, 20));
        Assert.AreEqual(50, E(Skill.Heal, 10));
        Assert.AreEqual(40, E(Skill.Repair, 10));
        Assert.AreEqual(75, E(Skill.Firearms, 10));
        Assert.AreEqual(60, E(Skill.PickLocks, 10));
        Assert.AreEqual(0, E(Skill.DisarmTraps, 0));
        Assert.AreEqual(24, E(Skill.DisarmTraps, 1));
        // Opposed skills without a target read 100, capped at 95.
        Assert.AreEqual(95, E(Skill.Persuasion, 0));
        Assert.AreEqual(52, SkillCheckRules.Effectiveness(Skill.PickPocket, 5, new() { OpponentValue = 4 }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => E(Skill.Bow, 21));
    }

    [TestMethod]
    public void EffectivenessModifiersInTheSourcesOrder()
    {
        // Magnetic Inversion only for firearms; +10 for extraordinary intelligence, then the local PC's difficulty, then the cap.
        Assert.AreEqual(55, SkillCheckRules.Effectiveness(Skill.Firearms, 10, new() { TargetMagneticInversion = true }));
        Assert.AreEqual(75, SkillCheckRules.Effectiveness(Skill.Bow, 10, new() { TargetMagneticInversion = true }));
        Assert.AreEqual(85, SkillCheckRules.Effectiveness(Skill.Melee, 10, new() { ExtraordinaryIntelligence = true }));
        Assert.AreEqual(127, SkillCheckRules.Effectiveness(Skill.Melee, 10,
            new() { ExtraordinaryIntelligence = true, LocalPc = true, Difficulty = GameDifficulty.Easy }));
        Assert.AreEqual(64, SkillCheckRules.Effectiveness(Skill.Melee, 10,
            new() { ExtraordinaryIntelligence = true, LocalPc = true, Difficulty = GameDifficulty.Hard }));
        Assert.AreEqual(85, SkillCheckRules.Effectiveness(Skill.Melee, 10,
            new() { ExtraordinaryIntelligence = true, LocalPc = false, Difficulty = GameDifficulty.Easy }));
        // Easy on a capped skill stops at 95; a combat skill has no cap.
        Assert.AreEqual(90, SkillCheckRules.Effectiveness(Skill.PickLocks, 10, new() { LocalPc = true, Difficulty = GameDifficulty.Easy }));
        Assert.AreEqual(95, SkillCheckRules.Effectiveness(Skill.PickLocks, 12, new() { LocalPc = true, Difficulty = GameDifficulty.Easy }));
        Assert.AreEqual(202, SkillCheckRules.Effectiveness(Skill.Bow, 20, new() { ExtraordinaryIntelligence = true, LocalPc = true, Difficulty = GameDifficulty.Easy }));
        // Hard truncates toward zero; firearms at 0 with Magnetic Inversion is 5.
        Assert.AreEqual(5, SkillCheckRules.Effectiveness(Skill.Firearms, 0, new() { TargetMagneticInversion = true }));
        Assert.AreEqual(4, SkillCheckRules.Effectiveness(Skill.Firearms, 0, new() { TargetMagneticInversion = true, LocalPc = true, Difficulty = GameDifficulty.Hard }));
    }

    [TestMethod]
    public void DifficultyTermsOfAnAttackLikeCheck()
    {
        var facts = new SkillDifficultyFacts
        {
            Modifier = -30,
            Weapon = true, WeaponToHit = 5, WeaponMinimumStrength = 12, SourceStrength = 10,
            Target = true, TargetCritter = true, TargetUnaware = true, TargetArmorClass = 41,
            AimedLocation = HitLocation.Head,
            Distance = 8, Range = 6, SourcePerception = 10, Cover = 7,
            Light = 0, SourceLight = LightEmission.Small,
            SourceInjuries = SkillSourceInjuries.Blinded | SkillSourceInjuries.CrippledArmOne | SkillSourceInjuries.CrippledLegsBoth,
        };
        var d = SkillCheckRules.Difficulty(Skill.Bow, SkillTraining.None, 60, facts);
        Assert.AreEqual(-30, d.Modifier);
        Assert.AreEqual(-5 + 10, d.Weapon);
        Assert.AreEqual(-30, d.Awareness);
        Assert.AreEqual(50, d.Aim);
        Assert.AreEqual(60 * 20 / 100, d.ArmorClass);
        Assert.AreEqual(SkillCheckRules.Impossible + 15, d.Range);
        Assert.AreEqual(7, d.Cover);
        Assert.AreEqual(22, d.Light);
        // Bow reads the eyes and the arms, not the legs.
        Assert.AreEqual(50, d.Injury);
        Assert.AreEqual(0, d.Skill);

        // An expert in firearms takes two thirds of the aimed penalty; a master pays no distance; a blocked line has no cover.
        var expert = SkillCheckRules.Difficulty(Skill.Firearms, SkillTraining.Master, 60, facts with { LineOfFireBlocked = true });
        Assert.AreEqual(33, expert.Aim);
        Assert.AreEqual(2 * SkillCheckRules.Impossible, expert.Range);
        Assert.AreEqual(0, expert.Cover);
        // Melee reads no distance; no item, no range test.
        Assert.AreEqual(0, SkillCheckRules.Difficulty(Skill.Melee, SkillTraining.None, 60, facts).Range);
        Assert.AreEqual(15, SkillCheckRules.Difficulty(Skill.Bow, SkillTraining.None, 60, facts with { Range = null }).Range);
        // A trained backstabber's true backstab ignores the armour class.
        Assert.AreEqual(0, SkillCheckRules.Difficulty(Skill.Melee, SkillTraining.None, 60, facts with { TrueBackstabByTrained = true }).ArmorClass);
    }

    [TestMethod]
    public void AwarenessTakesTheFirstMatchingStateAndProwlingSkipsUnawareness()
    {
        int A(SkillTargetConditions c, Skill skill = Skill.Melee, bool unaware = false, bool critter = true) =>
            SkillCheckRules.Difficulty(skill, SkillTraining.None, 0,
                new() { Target = true, TargetCritter = critter, TargetConditions = c, TargetUnaware = unaware }).Awareness;
        Assert.AreEqual(-50, A(SkillTargetConditions.Paralyzed | SkillTargetConditions.Stunned));
        Assert.AreEqual(-50, A(SkillTargetConditions.Unconscious));
        Assert.AreEqual(-30, A(SkillTargetConditions.FallingDown | SkillTargetConditions.Sleeping));
        Assert.AreEqual(-30, A(SkillTargetConditions.Stunned));
        Assert.AreEqual(-50, A(SkillTargetConditions.Sleeping));
        Assert.AreEqual(-30, A(SkillTargetConditions.None, unaware: true));
        Assert.AreEqual(0, A(SkillTargetConditions.None, Skill.Prowling, unaware: true));
        Assert.AreEqual(-50, A(SkillTargetConditions.Sleeping, Skill.Prowling));
        Assert.AreEqual(0, A(SkillTargetConditions.Sleeping, critter: false));
        // Heal has no awareness flag.
        Assert.AreEqual(0, A(SkillTargetConditions.Sleeping, Skill.Heal));
    }

    [TestMethod]
    public void LightFollowsTheFlagTheTrainingAndTheSourcesOwnLight()
    {
        int L(Skill skill, SkillTraining training = SkillTraining.None, SkillDifficultyFacts? facts = null) =>
            SkillCheckRules.LightPenalty(skill, training, facts ?? new() { Light = 0 });
        Assert.AreEqual(30, L(Skill.Bow));
        Assert.AreEqual(30, L(Skill.PickLocks));
        Assert.AreEqual(0, L(Skill.PickLocks, SkillTraining.Expert));
        Assert.AreEqual(30, L(Skill.SpotTrap));
        Assert.AreEqual(0, L(Skill.SpotTrap, SkillTraining.Apprentice));
        Assert.AreEqual(0, L(Skill.DisarmTraps, SkillTraining.Apprentice));
        Assert.AreEqual(30, L(Skill.Melee, SkillTraining.Apprentice));
        Assert.AreEqual(0, L(Skill.Melee, SkillTraining.Expert));
        Assert.AreEqual(0, L(Skill.Heal));
        // A seeing check reads light for any skill while untrained; the observer's dark sight decides the inversion.
        Assert.AreEqual(30, L(Skill.Prowling, facts: new() { Light = 0, SeeingCheck = true, ObserverDarkSight = true }));
        Assert.AreEqual(0, L(Skill.Prowling, facts: new() { Light = 0, SeeingCheck = true }));
        Assert.AreEqual(0, L(Skill.Prowling, SkillTraining.Apprentice, new() { Light = 0, SeeingCheck = true, ObserverDarkSight = true }));
        Assert.AreEqual(14, L(Skill.Bow, facts: new() { Light = 128 }));
        Assert.AreEqual(22, L(Skill.Bow, facts: new() { Light = 0, SourceLight = LightEmission.Small }));
        Assert.AreEqual(15, L(Skill.Bow, facts: new() { Light = 0, SourceLight = LightEmission.Medium }));
        Assert.AreEqual(10, L(Skill.Bow, facts: new() { Light = 0, SourceLight = LightEmission.Large }));
        Assert.AreEqual(7, L(Skill.Bow, facts: new() { Light = 0, SourceLight = LightEmission.ExtraLarge }));
        Assert.AreEqual(0, L(Skill.Bow, facts: new() { Light = 0, SourceDarkSight = true }));
        Assert.AreEqual(30, L(Skill.Bow, facts: new() { Light = 255, SourceDarkSight = true }));
        Assert.AreEqual(0, L(Skill.Bow, facts: new() { Light = 0, SourceUndead = true }));
    }

    [TestMethod]
    public void PerSkillTerms()
    {
        SkillDifficulty D(Skill skill, SkillTraining training, SkillDifficultyFacts facts) => SkillCheckRules.Difficulty(skill, training, 0, facts);
        // Pick pocket: a worn slot's penalty and 5 × width × height, halved for an expert.
        Assert.AreEqual(70 + 30, D(Skill.PickPocket, SkillTraining.None, new() { ItemLocation = 1004, ItemWidth = 2, ItemHeight = 3 }).Skill);
        Assert.AreEqual(1000 + 15, D(Skill.PickPocket, SkillTraining.Expert, new() { ItemLocation = 1008, ItemWidth = 2, ItemHeight = 3 }).Skill);
        Assert.AreEqual(5, D(Skill.PickPocket, SkillTraining.None, new() { ItemLocation = 7, ItemWidth = 1, ItemHeight = 1 }).Skill);
        // Prowling: a hidden prowler reads −100 in all; else minus the cover, +50 without cover below expert.
        var hidden = D(Skill.Prowling, SkillTraining.None, new() { SeeingCheck = true, ProwlerHidden = true, Modifier = 40 });
        Assert.AreEqual(SkillDifficulty.HiddenProwler, hidden.Total);
        Assert.AreEqual(50, D(Skill.Prowling, SkillTraining.Apprentice, new() { SeeingCheck = true }).Skill);
        Assert.AreEqual(0, D(Skill.Prowling, SkillTraining.Expert, new() { SeeingCheck = true }).Skill);
        Assert.AreEqual(-12, D(Skill.Prowling, SkillTraining.None, new() { SeeingCheck = true, ProwlerCover = 12 }).Skill);
        // Hearing: each armour's silent-move adjustment, halved from apprentice.
        Assert.AreEqual(-7 + 3, D(Skill.Prowling, SkillTraining.None, new() { HearingCheck = true, ArmorSilentMoves = [7, -3] }).Skill);
        Assert.AreEqual(-3 + 1, D(Skill.Prowling, SkillTraining.Apprentice, new() { HearingCheck = true, ArmorSilentMoves = [7, -3] }).Skill);
        Assert.AreEqual(0, D(Skill.Prowling, SkillTraining.None, new() { ArmorSilentMoves = [7] }).Skill);
        // Heal needs a healing item.
        Assert.AreEqual(SkillCheckRules.Impossible, D(Skill.Heal, SkillTraining.None, new()).Skill);
        Assert.AreEqual(-20, D(Skill.Heal, SkillTraining.None, new() { HealingItem = true, HealingItemBonus = 20 }).Skill);
        // Pick locks: a portal or container only, the lockpick's bonus, the lock halved for a master.
        Assert.AreEqual(SkillCheckRules.Impossible, D(Skill.PickLocks, SkillTraining.None, new() { LockDifficulty = 30 }).Skill);
        Assert.AreEqual(30 - 10, D(Skill.PickLocks, SkillTraining.Expert, new() { TargetLockable = true, LockpickBonus = 10, LockDifficulty = 30 }).Skill);
        Assert.AreEqual(15 - 10, D(Skill.PickLocks, SkillTraining.Master, new() { TargetLockable = true, LockpickBonus = 10, LockDifficulty = 30 }).Skill);
        Assert.AreEqual(-5, D(Skill.PickLocks, SkillTraining.Master, new() { TargetLockable = true, LockDifficulty = -5 }).Skill);
        // Disarm traps: a trap's difficulty.
        Assert.AreEqual(25, D(Skill.DisarmTraps, SkillTraining.None, new() { TargetTrap = true, TrapDifficulty = 25 }).Skill);
        Assert.AreEqual(0, D(Skill.DisarmTraps, SkillTraining.None, new() { TrapDifficulty = 25 }).Skill);
    }

    [TestMethod]
    public void CriticalChances()
    {
        var none = new SkillCriticalFacts();
        Assert.AreEqual(4, SkillCheckRules.CriticalSuccessChance(Skill.Melee, 99, none));
        Assert.AreEqual(49, SkillCheckRules.CriticalSuccessChance(Skill.Heal, 99, none));
        Assert.AreEqual(4 + 10 + 3 + 6, SkillCheckRules.CriticalSuccessChance(Skill.Melee, 99,
            new() { AimedLocation = HitLocation.Head, WeaponMagicCriticalHit = 3, BackstabTerm = 6 }));
        Assert.AreEqual(2 * (4 + 6), SkillCheckRules.CriticalSuccessChance(Skill.Bow, 99, new()
        {
            AimedLocation = HitLocation.Leg,
            CriticalHitEffects = [new EffectModifier(StatModifierOperator.Multiply, 2)],
        }));
        Assert.AreEqual(-3, SkillCheckRules.CriticalSuccessChance(Skill.Bow, 0, new() { CriticalHitEffects = [new EffectModifier(StatModifierOperator.Add, -3)] }));

        Assert.AreEqual(5, SkillCheckRules.CriticalFailureChance(Skill.Melee, 60, none));
        Assert.AreEqual(20, SkillCheckRules.CriticalFailureChance(Skill.Repair, 60, none));
        Assert.AreEqual(2, SkillCheckRules.CriticalFailureChance(Skill.Melee, 99, none));
        Assert.AreEqual(5 + 7 + 1, SkillCheckRules.CriticalFailureChance(Skill.Melee, 60,
            new() { Item = true, ItemHitPoints = 50, ItemMaxHitPoints = 100, WeaponMagicCriticalMiss = 1 }));
        Assert.AreEqual(20 + 25, SkillCheckRules.CriticalFailureChance(Skill.Repair, 60, new() { Item = true, ItemHitPoints = 50, ItemMaxHitPoints = 100 }));
        Assert.AreEqual(2, SkillCheckRules.CriticalFailureChance(Skill.Melee, 60, new() { CriticalMissEffects = [new EffectModifier(StatModifierOperator.Add, -50)] }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            SkillCheckRules.CriticalFailureChance(Skill.Melee, 60, new() { Item = true, ItemHitPoints = 5, ItemMaxHitPoints = 4 }));
    }

    [TestMethod]
    public void RunRollsTheCheckThenTheCriticalAndAppliesForceMasterAndFate()
    {
        var request = new SkillCheckRequest { Skill = Skill.PickLocks, Level = 10, Difficulty = new() { TargetLockable = true, LockDifficulty = 20 } };
        // 60 effectiveness, 20 difficulty: a roll of 40 succeeds, 41 fails.
        var rolls = new Rolls(40, 30);
        var result = SkillCheckRules.Run(request, rolls, modifierCountedTwice: false);
        CollectionAssert.AreEqual(new[] { SkillCheckRoll.Check, SkillCheckRoll.Critical }, rolls.Asked);
        Assert.AreEqual(new SkillCheck(60, 20, 40, true, 30, 30, true), result.Check);
        Assert.IsFalse(result.Fated);
        Assert.IsNull(result.NextGamblingRoll);
        var failed = SkillCheckRules.Run(request, new Rolls(41, 21), false).Check;
        Assert.AreEqual(new SkillCheck(60, 20, 41, false, 20, 21, false), failed);
        Assert.IsTrue(SkillCheckRules.Run(request with { Forced = true }, new Rolls(100, 100), false).Success);

        // A2: the modifier counted again.
        var modified = request with { Difficulty = request.Difficulty with { Modifier = 10 } };
        Assert.AreEqual(30, SkillCheckRules.Run(modified, new Rolls(1, 100), false).Check.Difficulty);
        Assert.AreEqual(40, SkillCheckRules.Run(modified, new Rolls(1, 100), true).Check.Difficulty);
        Assert.AreEqual(30, SkillCheckRules.Run(modified, new Rolls(1, 100), true).Difficulty.Total);

        // A melee master never fails critically, but the critical roll is still drawn.
        var melee = new SkillCheckRequest { Skill = Skill.Melee, Level = 0, Training = SkillTraining.Master, Difficulty = new() { Modifier = 100 } };
        var masterRolls = new Rolls(50, 1);
        var master = SkillCheckRules.Run(melee, masterRolls, false).Check;
        Assert.AreEqual(2, masterRolls.Asked.Count);
        Assert.IsFalse(master.Success);
        Assert.IsFalse(master.Critical);
        Assert.AreEqual(0, master.CriticalChance);
        Assert.IsTrue(SkillCheckRules.Run(melee with { Training = SkillTraining.Expert }, new Rolls(50, 1), false).Critical);

        // An armed fate point of the skill's kind makes a critical success; a skill without a fate kind ignores it.
        var fated = SkillCheckRules.Run(request with { FateArmed = true }, new Rolls(100, 100), false);
        Assert.IsTrue(fated.Success && fated.Critical && fated.Fated);
        Assert.IsFalse(SkillCheckRules.Run(melee with { FateArmed = true }, new Rolls(50, 50), false).Fated);
    }

    [TestMethod]
    public void GamblingUsesItsStoredRollAndKeepsTheNewOne()
    {
        var request = new SkillCheckRequest { Skill = Skill.Gambling, Level = 10, Effectiveness = new() { OpponentValue = 10 }, GamblingRoll = 20 };
        var result = SkillCheckRules.Run(request, new Rolls(77, 100), false);
        Assert.AreEqual(20, result.Check.Roll);
        Assert.AreEqual(77, result.NextGamblingRoll);
        Assert.IsTrue(result.Success);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SkillCheckRules.Run(request with { GamblingRoll = null }, new Rolls(1, 1), false));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            SkillCheckRules.Run(new SkillCheckRequest { Skill = Skill.Heal, Level = 1, GamblingRoll = 5 }, new Rolls(1, 1), false));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            SkillCheckRules.Run(new SkillCheckRequest { Skill = Skill.Heal, Level = 1 }, new Rolls(0, 1), false));
    }

    [TestMethod]
    public void InvalidFactsAreRefused()
    {
        void Refused(SkillDifficultyFacts facts) =>
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SkillCheckRules.Difficulty(Skill.Bow, SkillTraining.None, 50, facts));
        Refused(new() { Light = 256 });
        Refused(new() { TargetCritter = true });
        Refused(new() { Distance = -1 });
        Refused(new() { Modifier = 2_000_000 });
        Refused(new() { SourceLight = (LightEmission)9 });
        Refused(new() { AimedLocation = (HitLocation)9 });
        Refused(new() { TargetConditions = (SkillTargetConditions)64 });
        Refused(new() { ArmorSilentMoves = new int[10] });
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SkillCheckRules.Difficulty(Skill.Bow, (SkillTraining)4, 50, new()));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SkillCheckRules.CriticalSuccessChance(Skill.Bow, 50,
            new() { CriticalHitEffects = [new EffectModifier(StatModifierOperator.Divide, 0)] }));
    }

    private sealed class Rolls(params int[] values) : ISkillCheckRolls
    {
        private int _next;
        public List<SkillCheckRoll> Asked { get; } = [];

        public int D100(SkillCheckRoll roll)
        {
            Asked.Add(roll);
            return values[_next++];
        }
    }
}
