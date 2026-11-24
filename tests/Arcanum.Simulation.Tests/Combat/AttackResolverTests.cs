using Arcanum.Simulation.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Tests.Combat.CombatFixtures;

namespace Arcanum.Simulation.Tests.Combat;

[TestClass]
public sealed class AttackResolverTests
{
    [TestMethod]
    public void HitAtTheThresholdAndMissOneAbove()
    {
        // Effectiveness 75, difficulty 0: a roll of 75 hits, 76 misses.
        var hit = Run(Melee(), Concat(Opening(),
            new[] { (AttackCheck, 75), (AttackCritical, 4), (DodgeCheck, 1), (DodgeCritical, 100), (Normal, 7) }));
        Assert.AreEqual(AttackOutcome.Hit, hit.Outcome);
        Assert.AreEqual(HitLocation.Torso, hit.HitLocation);
        Assert.IsFalse(hit.Aimed);
        Assert.AreEqual(new SkillCheck(75, 0, 75, true, 3, 4, false), hit.AttackCheck);
        Assert.IsFalse(hit.DodgeCheck!.Success);
        Assert.AreEqual(DamageRecipient.Target, hit.Damage!.Recipient);
        Assert.AreEqual(7, hit.Damage.HitPointDamage);
        Assert.AreEqual(DamageByType.Zero with { Normal = 7 }, hit.Damage.Final);
        Assert.AreEqual(DamageMultiplier.None, hit.Damage.Multiplier);
        Assert.AreEqual(8, hit.Rolls.Count);

        var miss = Run(Melee(), Concat(Opening(), new[] { (AttackCheck, 76), (AttackCritical, 4) }));
        Assert.AreEqual(AttackOutcome.Miss, miss.Outcome);
        Assert.AreEqual(AttackMissCause.SkillCheck, miss.MissCause);
        Assert.AreEqual(3, miss.AttackCheck!.CriticalChance);
        Assert.IsNull(miss.Damage);
        Assert.IsNull(miss.DodgeCheck);
    }

    [TestMethod]
    public void AThrownItemRollsAgainstACorpseWithoutDodgeAndItsHitDoesNoDamage()
    {
        // ARC-COMBAT-2392 (ledger TW8): the throw's hit resolve asks nothing of the target's life; the dead defender is easier to hit (-50), never dodges and takes no damage.
        var alive = Melee() with { Skill = CombatSkill.Throwing, Distance = 1 };
        var dead = alive with { Target = Npc() with { Conditions = CombatantConditions.Dead } };
        var living = Run(alive, Concat(Opening(), new[] { (AttackCheck, 1), (AttackCritical, 100), (DodgeCheck, 100), (DodgeCritical, 100), (Normal, 5) }));
        var corpse = Run(dead, Concat(Opening(), new[] { (AttackCheck, 1), (AttackCritical, 100) }));
        Assert.AreEqual(AttackRefusal.None, corpse.Refusal);
        Assert.AreEqual(living.AttackCheck!.Difficulty - 50, corpse.AttackCheck!.Difficulty, "the corpse is 50 easier");
        Assert.AreEqual(AttackOutcome.Hit, corpse.Outcome);
        Assert.IsNull(corpse.DodgeCheck, "a corpse never dodges (nothing rolled)");
        Assert.IsNull(corpse.Damage, "no damage to a corpse");
        // Every other attack still refuses a corpse, and a thrown item landing on one hits it without damage.
        Assert.AreEqual(AttackRefusal.TargetDead, AttackResolver.Resolve(dead with { Skill = CombatSkill.Melee }, new ScriptedRolls([])).Refusal);
        var carried = Run(dead with { DeferHit = true }, Concat(Opening(), new[] { (AttackCheck, 1), (AttackCritical, 100) }));
        var landed = AttackResolver.LandHit(dead, carried, new ScriptedRolls([]));
        Assert.AreEqual((AttackRefusal.None, AttackOutcome.Hit, (AttackDamage?)null), (landed.Refusal, landed.Outcome, landed.Damage));
    }

    [TestMethod]
    public void AThrowAtALocationRollsHitAndCriticalWithNoDefenderTermsAndNoDodge()
    {
        // ARC-COMBAT-2435 (ledger TW11, 0x4C7160 with no defender): technology failure, magick cancel, hit, critical, and no dodge; the defender's armour class counts for nothing.
        var request = Melee() with { Skill = CombatSkill.Throwing, Distance = 1, DeferHit = true, NoDefender = true };
        var armoured = request with { Target = request.Target with { ArmorClass = 90 } };
        var hit = Run(request, Concat(Opening(), new[] { (AttackCheck, 1), (AttackCritical, 100) }));
        var againstArmour = Run(armoured, Concat(Opening(), new[] { (AttackCheck, 1), (AttackCritical, 100) }));
        Assert.AreEqual((AttackOutcome.Hit, (AttackDamage?)null, (SkillCheck?)null), (hit.Outcome, hit.Damage, hit.DodgeCheck), "a hit carries no damage and rolls no dodge");
        Assert.AreEqual(hit.AttackCheck!.Difficulty, againstArmour.AttackCheck!.Difficulty, "no armour class term");
        Assert.AreEqual(hit.AttackCheck.Difficulty, Run(Melee() with { Skill = CombatSkill.Throwing, Distance = 1, DeferHit = true, NoDefender = true, Target = Npc() with { Conditions = CombatantConditions.Unconscious } },
            Concat(Opening(), new[] { (AttackCheck, 1), (AttackCritical, 100) })).AttackCheck!.Difficulty, "no awareness term");
        // A miss: the critical roll is made too (a critical failure turns the throw on the thrower), still no dodge.
        var miss = Run(request, Concat(Opening(), new[] { (AttackCheck, 100), (AttackCritical, 100) }));
        Assert.AreEqual((AttackOutcome.Miss, AttackMissCause.SkillCheck, (SkillCheck?)null), (miss.Outcome, miss.MissCause, miss.DodgeCheck));
        // Without the flag the same request still hits unrolled at a non-critter and meets a dodge at a critter.
        Assert.IsNotNull(Run(request with { NoDefender = false }, Concat(Opening(), new[] { (AttackCheck, 1), (AttackCritical, 100), (DodgeCheck, 100), (DodgeCritical, 100) })).DodgeCheck);
    }

    [TestMethod]
    public void AProjectileCarriesTheHitAndItsDamageIsRolledOnArrival()
    {
        // ARC-COMBAT-016 (ledger PF1, PF5): the shot rolls its check only; the strike rolls the damage on the target as it is then.
        var shot = Run(Melee() with { DeferHit = true }, PlainHit());
        Assert.AreEqual((AttackOutcome.Hit, (AttackDamage?)null, (AttackWear?)null), (shot.Outcome, shot.Damage, shot.Wear));
        var arrival = new ScriptedRolls(new[] { (Normal, 7) });
        var landed = AttackResolver.LandHit(Melee(), shot, arrival);
        arrival.AssertConsumed();
        Assert.AreEqual((AttackOutcome.Hit, 7, 1), (landed.Outcome, landed.Damage!.HitPointDamage, landed.Rolls.Count));
        // A critical hit carries its critical: the multiplier is rolled on arrival.
        var critical = Run(Melee() with { DeferHit = true }, CriticalHit());
        Assert.AreEqual((AttackOutcome.CriticalHit, (AttackDamage?)null), (critical.Outcome, critical.Damage));
        var crit = new ScriptedRolls(Concat(new[] { (Normal, 7) }, NoMultiplier()));
        Assert.AreEqual(AttackOutcome.CriticalHit, AttackResolver.LandHit(Melee(), critical, crit).Outcome);
        crit.AssertConsumed();
        // A miss resolves at the shot as before.
        Assert.AreEqual(AttackOutcome.Miss, Run(Melee() with { DeferHit = true }, Concat(Opening(), new[] { (AttackCheck, 76), (AttackCritical, 4) })).Outcome);
        // The target dead on arrival is refused; a resolution with damage or a miss cannot be landed.
        var dead = Melee() with { Target = Npc() with { Conditions = CombatantConditions.Dead } };
        Assert.AreEqual(AttackRefusal.TargetDead, AttackResolver.LandHit(dead, shot, new ScriptedRolls([])).Refusal);
        Assert.ThrowsExactly<ArgumentException>(() => AttackResolver.LandHit(Melee(), Run(Melee(), Concat(PlainHit(), new[] { (Normal, 7) })), new ScriptedRolls([])));
        // The forced hit of a stray strike: torso, not aimed, no critical, nothing rolled until the damage.
        var forced = AttackResolver.ForcedHit(weaponMagicCancelled: false);
        Assert.AreEqual((AttackOutcome.Hit, HitLocation.Torso, false, 0), (forced.Outcome, forced.HitLocation, forced.Aimed, forced.Rolls.Count));
        Assert.AreEqual(7, AttackResolver.LandHit(Melee(), forced, new ScriptedRolls(new[] { (Normal, 7) })).Damage!.HitPointDamage);
    }

    [TestMethod]
    public void AimedAttackSkipsTheLocationRoll()
    {
        var request = Melee() with { AimedLocation = HitLocation.Head };
        // Difficulty +50, crit chance 3 + 10 = 13.
        var result = Run(request, new[]
        {
            (TechnologyFailure, 100), (MagickCancel, 100), (AttackCheck, 25), (AttackCritical, 13), (DodgeCheck, 100),
            (DodgeCritical, 100), (Normal, 5), (Triple, 100), (DoubleRoll, 100), (OneAndHalf, 100),
        });
        Assert.AreEqual(HitLocation.Head, result.HitLocation);
        Assert.IsTrue(result.Aimed);
        Assert.AreEqual(50, result.Difficulty!.Aim);
        Assert.AreEqual(AttackOutcome.CriticalHit, result.Outcome);
        Assert.AreEqual(7, result.Damage!.HitPointDamage);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            AttackResolver.Resolve(Melee() with { AimedLocation = HitLocation.Torso }, new SeededRolls(1)));
    }

    [TestMethod]
    public void RolledLocationsFollowTheTable()
    {
        Assert.AreEqual(HitLocation.Leg, Run(Melee(), Concat(Opening(71), new[] { (AttackCheck, 100), (AttackCritical, 100) })).HitLocation);
        Assert.AreEqual(HitLocation.Head, Run(Melee(), Concat(Opening(96), new[] { (AttackCheck, 100), (AttackCritical, 100) })).HitLocation);
    }

    [TestMethod]
    public void DodgeTurnsAHitIntoAMissAndACriticalDodgeIntoACriticalMiss()
    {
        // Dodge 20: effectiveness 2 × 20 = 40 (the original's 2L), critical chance 40 / 20 = 2.
        var target = Npc() with { DodgeLevel = 20, DodgeTraining = SkillTraining.Expert };
        var dodged = Run(Melee() with { Target = target }, Concat(Opening(),
            new[] { (AttackCheck, 10), (AttackCritical, 1), (DodgeCheck, 40), (DodgeCritical, 3) }));
        Assert.AreEqual(AttackOutcome.Miss, dodged.Outcome);
        Assert.AreEqual(AttackMissCause.Dodged, dodged.MissCause);
        Assert.IsTrue(dodged.AttackCheck!.Critical, "the dodge also removes the critical hit");
        Assert.AreEqual(new SkillCheck(40, 0, 40, true, 2, 3, false), dodged.DodgeCheck);

        var critical = Run(Melee() with { Target = target }, Concat(Opening(), new[]
        {
            (AttackCheck, 10), (AttackCritical, 100), (DodgeCheck, 40), (DodgeCritical, 2), (DodgeCriticalMiss, 50),
            (CriticalSelfHit, 51), (Normal, 6),
        }));
        Assert.AreEqual(AttackOutcome.CriticalMiss, critical.Outcome);
        Assert.AreEqual(AttackMissCause.Dodged, critical.MissCause);
        Assert.IsFalse(critical.CriticalSelfHit);
        Assert.AreEqual(DamageRecipient.Attacker, critical.Damage!.Recipient);
        Assert.AreEqual(6, critical.Damage.HitPointDamage);

        // Expert: 50 % chance, so 51 keeps a plain dodge.
        var plain = Run(Melee() with { Target = target }, Concat(Opening(), new[]
        {
            (AttackCheck, 10), (AttackCritical, 100), (DodgeCheck, 40), (DodgeCritical, 2), (DodgeCriticalMiss, 51),
        }));
        Assert.AreEqual(AttackOutcome.Miss, plain.Outcome);
    }

    [TestMethod]
    public void DodgeAutoFailsForAnIncapacitatedTarget()
    {
        var target = Npc() with { DodgeLevel = 20, DodgeTraining = SkillTraining.Master, Conditions = CombatantConditions.Stunned };
        var result = Run(Melee() with { Target = target }, Concat(Opening(),
            new[] { (AttackCheck, 1), (AttackCritical, 100), (DodgeCheck, 1), (DodgeCritical, 1), (Normal, 5) }));
        Assert.AreEqual(AttackOutcome.Hit, result.Outcome);
        Assert.IsFalse(result.DodgeCheck!.Success);
        Assert.IsFalse(result.DodgeCheck.Critical);
        Assert.AreEqual(-30, result.Difficulty!.Awareness);
    }

    [TestMethod]
    public void DodgeDifficultyFromBlindnessAndLegs()
    {
        var target = Npc() with
        {
            DodgeLevel = 20, Conditions = CombatantConditions.Blinded | CombatantConditions.CrippledLegsBoth,
        };
        var result = Run(Melee() with { Target = target }, Concat(Opening(),
            new[] { (AttackCheck, 1), (AttackCritical, 100), (DodgeCheck, 36), (DodgeCritical, 100), (Normal, 5) }));
        Assert.AreEqual(60, result.DodgeCheck!.Difficulty);
        Assert.IsFalse(result.DodgeCheck.Success, "60 + 36 > 95");
        Assert.AreEqual(AttackOutcome.Hit, result.Outcome);
    }

    [TestMethod]
    public void CriticalHitMultiplierAndResistance()
    {
        var target = Npc() with { Resistances = DamageResistances.None with { Normal = 50 } };
        var weapon = Sword() with { Damage = DamageRanges.None with { Normal = new DamageRange(5, 10), Fatigue = new DamageRange(4, 4) } };
        var request = Melee() with { Target = target, Weapon = weapon };

        var doubled = Run(request, Concat(CriticalHit(), new[] { (Normal, 10), (Triple, 11), (DoubleRoll, 30) }));
        Assert.AreEqual(AttackOutcome.CriticalHit, doubled.Outcome);
        Assert.AreEqual(DamageByType.Zero with { Normal = 10, Fatigue = 4 }, doubled.Damage!.Rolled);
        // Normal 10 − 50 % = 5; fatigue 4 − ¾ × 50 % (37 %) = 3.
        Assert.AreEqual(DamageByType.Zero with { Normal = 5, Fatigue = 3 }, doubled.Damage.Final);
        Assert.AreEqual(DamageMultiplier.Double, doubled.Damage.Multiplier);
        Assert.AreEqual(10, doubled.Damage.HitPointDamage);
        Assert.AreEqual(3, doubled.Damage.FatigueDamage);

        var tripled = Run(request, Concat(CriticalHit(), new[] { (Normal, 10), (Triple, 10) }));
        Assert.AreEqual(15, tripled.Damage!.HitPointDamage);

        var fallback = Run(request, Concat(CriticalHit(), new[] { (Normal, 10) }, NoMultiplier()));
        Assert.AreEqual(DamageMultiplier.OneAndHalf, fallback.Damage!.Multiplier);
        Assert.IsTrue(fallback.Damage.MultiplierFromFallback);
        Assert.AreEqual(7, fallback.Damage.HitPointDamage);

        var rolledHalf = Run(request, Concat(CriticalHit(), new[] { (Normal, 10), (Triple, 100), (DoubleRoll, 100), (OneAndHalf, 60) }));
        Assert.AreEqual(DamageMultiplier.OneAndHalf, rolledHalf.Damage!.Multiplier);
        Assert.IsFalse(rolledHalf.Damage.MultiplierFromFallback);

        // An effect from the table stops the fallback: the critical can carry no multiplier at all.
        var stun = new FixedCriticalEffects { Hit = CriticalHitEffects.Stun };
        var stunned = Run(request, Concat(CriticalHit(), new[] { (Normal, 10) }, NoMultiplier()), stun);
        Assert.AreEqual(DamageMultiplier.None, stunned.Damage!.Multiplier);
        Assert.AreEqual(CriticalHitEffects.Stun, stunned.Damage.HitEffects);
        Assert.AreEqual(5, stunned.Damage.HitPointDamage);
        Assert.AreEqual(1, stun.HitContexts.Count);
        Assert.AreEqual(CriticalHitChart.Crushing, stun.HitContexts[0].Chart);
        Assert.IsFalse(stun.HitContexts[0].NpcHitsPc);
    }

    [TestMethod]
    public void IgnoredResistanceAndHighResistances()
    {
        var target = Npc() with { Resistances = new DamageResistances(95, 95, 95, 95) };
        var weapon = Sword() with { Damage = DamageRanges.None with { Normal = new DamageRange(20, 20) } };
        var resisted = Run(Melee() with { Target = target, Weapon = weapon }, PlainHit());
        Assert.AreEqual(1, resisted.Damage!.HitPointDamage, "20 − 95 × 20 / 100 = 1");

        var ignoring = Run(Melee() with { Target = target, Weapon = weapon with { IgnoresResistance = true } }, PlainHit());
        Assert.AreEqual(20, ignoring.Damage!.HitPointDamage);

        // A monstrous resistance above 100 % turns damage negative, which the source lets heal.
        var monstrous = Run(Melee() with { Target = Npc() with { Resistances = DamageResistances.None with { Normal = 150 } }, Weapon = weapon },
            PlainHit());
        Assert.AreEqual(-10, monstrous.Damage!.HitPointDamage);
    }

    [TestMethod]
    public void NpcHittingPcCriticallyGetsNoMultiplierRolls()
    {
        var request = Melee() with { Target = Npc() with { IsPc = true } };
        var result = Run(request, Concat(CriticalHit(), new[] { (Normal, 10) }));
        Assert.AreEqual(DamageMultiplier.OneAndHalf, result.Damage!.Multiplier);
        Assert.IsTrue(result.Damage.MultiplierFromFallback);
        Assert.AreEqual(15, result.Damage.HitPointDamage);
    }

    [TestMethod]
    public void PcDifficultyScalesEffectivenessAndHitPointDamage()
    {
        var easy = Melee() with { Attacker = Npc() with { IsPc = true }, Difficulty = GameDifficulty.Easy };
        var eased = Run(easy, Concat(Opening(), new[]
        {
            (AttackCheck, 100), (AttackCritical, 6), (DodgeCheck, 100), (DodgeCritical, 100), (Normal, 8),
        }));
        Assert.AreEqual(112, eased.AttackCheck!.Effectiveness);
        Assert.AreEqual(12, eased.Damage!.HitPointDamage);

        var hard = easy with { Difficulty = GameDifficulty.Hard };
        var hardened = Run(hard, Concat(PlainHit(), new[] { (Normal, 8) }));
        Assert.AreEqual(57, hardened.AttackCheck!.Effectiveness);
        Assert.AreEqual(6, hardened.Damage!.HitPointDamage);
    }

    [TestMethod]
    public void FateForcesACriticalHitOrACriticalMiss()
    {
        var pc = Npc() with { IsPc = true, CriticalHitFateArmed = true };
        var hit = Run(Melee() with { Attacker = pc }, Concat(Opening(), new[] { (AttackCheck, 100), (AttackCritical, 100), (Normal, 5) },
            NoMultiplier()));
        Assert.AreEqual(AttackOutcome.CriticalHit, hit.Outcome);
        Assert.AreEqual(FateUse.AttackerCriticalHit, hit.Fate);
        Assert.AreEqual(AttackMissCause.None, hit.MissCause);
        Assert.AreEqual(7, hit.Damage!.HitPointDamage);

        var pcTarget = Npc() with { IsPc = true, CriticalMissFateArmed = true };
        var miss = Run(Melee() with { Target = pcTarget }, Concat(PlainHit(),
            new[] { (CriticalSelfHit, 1), (Normal, 5), (Triple, 1) }));
        Assert.AreEqual(AttackOutcome.CriticalMiss, miss.Outcome);
        Assert.AreEqual(FateUse.TargetCriticalMiss, miss.Fate);
        Assert.AreEqual(AttackMissCause.Fate, miss.MissCause);
        Assert.IsTrue(miss.CriticalSelfHit);
        Assert.AreEqual(15, miss.Damage!.HitPointDamage);

        // A PC attacking a PC with an armed critical-miss fate uses only its own fate.
        var pcAgainstPc = Run(Melee() with { Attacker = Npc() with { IsPc = true }, Target = pcTarget },
            Concat(PlainHit(), new[] { (Normal, 5) }));
        Assert.AreEqual(AttackOutcome.Hit, pcAgainstPc.Outcome);
        Assert.AreEqual(FateUse.None, pcAgainstPc.Fate);
    }

    [TestMethod]
    public void TechnologyFailureIsACriticalMiss()
    {
        var request = Melee() with
        {
            Attacker = Npc() with { MagickTechAptitude = 40 },
            Weapon = Sword() with { MagickTechComplexity = -50 },
        };
        var result = Run(request, new[] { (LocationRoll, 50), (TechnologyFailure, 20), (CriticalSelfHit, 100), (Normal, 5) });
        Assert.AreEqual(AttackOutcome.CriticalMiss, result.Outcome);
        Assert.AreEqual(AttackMissCause.TechnologyFailure, result.MissCause);
        Assert.IsNull(result.AttackCheck);
        Assert.AreEqual(5, result.Damage!.HitPointDamage);

        var survived = Run(request, Concat(new[] { (LocationRoll, 50), (TechnologyFailure, 21), (MagickCancel, 100) },
            new[] { (AttackCheck, 100), (AttackCritical, 100) }));
        Assert.AreEqual(AttackOutcome.Miss, survived.Outcome);

        // A thrown weapon takes no aptitude checks: both rolls are still made, against chance 0.
        var thrown = request with { Skill = CombatSkill.Throwing, Distance = 1 };
        var threw = Run(thrown, Concat(new[] { (LocationRoll, 50), (TechnologyFailure, 1), (MagickCancel, 1) },
            new[] { (AttackCheck, 100), (AttackCritical, 100) }));
        Assert.AreEqual(AttackOutcome.Miss, threw.Outcome);
        Assert.AreEqual(AttackMissCause.SkillCheck, threw.MissCause);
    }

    [TestMethod]
    public void MagickCancellation()
    {
        var target = Npc() with { MagickTechAptitude = -60 };
        var magickal = Sword() with { MagickTechComplexity = 50, ToHitBonus = 5, MagicToHitAdjustment = 10 };

        // A magickal melee weapon still swings without its magic.
        var swung = Run(Melee() with { Target = target, Weapon = magickal }, Concat(
            new[] { (LocationRoll, 50), (TechnologyFailure, 100), (MagickCancel, 60) },
            new[] { (AttackCheck, 1), (AttackCritical, 100), (DodgeCheck, 100), (DodgeCritical, 100), (Normal, 5) }));
        Assert.AreEqual(AttackOutcome.Hit, swung.Outcome);
        Assert.IsTrue(swung.WeaponMagicCancelled);
        Assert.AreEqual(-5, swung.Difficulty!.Weapon);

        var kept = Run(Melee() with { Target = target, Weapon = magickal }, Concat(
            new[] { (LocationRoll, 50), (TechnologyFailure, 100), (MagickCancel, 61) },
            new[] { (AttackCheck, 1), (AttackCritical, 100), (DodgeCheck, 100), (DodgeCritical, 100), (Normal, 5) }));
        Assert.IsFalse(kept.WeaponMagicCancelled);
        Assert.AreEqual(-15, kept.Difficulty!.Weapon);

        // A magickal bow simply misses.
        var bow = Melee() with { Target = target, Weapon = magickal with { Range = 10 }, Skill = CombatSkill.Bow };
        var missed = Run(bow, new[] { (LocationRoll, 50), (TechnologyFailure, 100), (MagickCancel, 60) });
        Assert.AreEqual(AttackOutcome.Miss, missed.Outcome);
        Assert.AreEqual(AttackMissCause.MagickCancelled, missed.MissCause);
        Assert.IsNull(missed.AttackCheck);
    }

    [TestMethod]
    public void ObjectsAreHitWithoutARollAndPortalsSubtractTheirMaterial()
    {
        var wood = Melee() with { TargetKind = AttackTargetKind.Portal, Target = Npc() with { Material = ObjectMaterial.Wood } };
        var result = Run(wood, Concat(Opening(), new[] { (Normal, 8) }));
        Assert.AreEqual(AttackOutcome.Hit, result.Outcome);
        Assert.IsNull(result.AttackCheck);
        Assert.IsNull(result.Difficulty);
        Assert.AreEqual(3, result.Damage!.HitPointDamage);
        Assert.AreEqual(0, result.Damage.FatigueDamage);

        var stone = wood with { TargetKind = AttackTargetKind.Container, Target = Npc() with { Material = ObjectMaterial.Stone } };
        Assert.AreEqual(0, Run(stone, Concat(Opening(), new[] { (Normal, 6) })).Damage!.HitPointDamage);

        // A critical against an object rolls no multiplier: the critical procedure is for critters only.
        var pc = Npc() with { IsPc = true, CriticalHitFateArmed = true };
        var critical = Run(wood with { Attacker = pc }, Concat(Opening(), new[] { (Normal, 8) }));
        Assert.AreEqual(AttackOutcome.CriticalHit, critical.Outcome);
        Assert.AreEqual(DamageMultiplier.None, critical.Damage!.Multiplier);
        Assert.AreEqual(3, critical.Damage.HitPointDamage);

        var wall = Run(Melee() with { TargetKind = AttackTargetKind.Wall }, Concat(Opening(), new[] { (Normal, 8) }));
        Assert.AreEqual(DamagePrevention.Wall, wall.Damage!.Prevented);
        Assert.AreEqual(0, wall.Damage.HitPointDamage);
        Assert.AreEqual(8, wall.Damage.Rolled.Normal);
    }

    [TestMethod]
    public void InvulnerableTargets()
    {
        var invulnerable = Run(Melee() with { Target = Npc() with { Conditions = CombatantConditions.Invulnerable } },
            Concat(CriticalHit(), new[] { (Normal, 8) }));
        Assert.AreEqual(DamagePrevention.Invulnerable, invulnerable.Damage!.Prevented);
        Assert.AreEqual(0, invulnerable.Damage.HitPointDamage);
        Assert.AreEqual(DamageMultiplier.None, invulnerable.Damage.Multiplier);

        var nigh = Melee() with { Target = Npc() with { Conditions = CombatantConditions.NighInvulnerable } };
        Assert.AreEqual(DamagePrevention.NighInvulnerable, Run(nigh, Concat(PlainHit(), new[] { (Normal, 8) })).Damage!.Prevented);
        var uber = Run(nigh with { Weapon = Sword() with { Uber = true } }, Concat(PlainHit(), new[] { (Normal, 8) }));
        Assert.AreEqual(DamagePrevention.None, uber.Damage!.Prevented);
        Assert.AreEqual(8, uber.Damage.HitPointDamage);
        var hands = Run(nigh with { Weapon = CombatWeapon.Unarmed() }, Concat(PlainHit(), new[] { (Normal, 3), (Fatigue, 3) }));
        Assert.AreEqual(DamagePrevention.NighInvulnerable, hands.Damage!.Prevented);
    }

    [TestMethod]
    public void DifficultyParts()
    {
        // Firearms expert, level 10 (effectiveness 75), aimed at the head (two thirds of 50), weak (ST 8 < 12),
        // nine cells off with PE 8, cover 7, a dark unaware target with AC 40, blinded with one crippled arm.
        var request = new AttackRequest
        {
            Attacker = Npc() with
            {
                Strength = 8, Perception = 8,
                Conditions = CombatantConditions.Blinded | CombatantConditions.CrippledArmOne,
            },
            Target = Npc() with { ArmorClass = 40, LightLevel = 0 },
            Weapon = Sword() with { ToHitBonus = 5, MinimumStrength = 12, Range = 20 },
            Skill = CombatSkill.Firearms,
            SkillLevel = 10,
            Training = SkillTraining.Expert,
            AimedLocation = HitLocation.Head,
            TargetUnaware = true,
            Distance = 9,
            CoverRating = 7,
        };
        var closed = new[] { (TechnologyFailure, 100), (MagickCancel, 100), (AttackCheck, 100), (AttackCritical, 100) };
        var result = Run(request, closed);
        Assert.AreEqual(new AttackDifficulty(0, 15, -30, 33, 15, 25, 7, 30, 50), result.Difficulty);
        Assert.AreEqual(145, result.Difficulty!.Total);
        Assert.AreEqual(145, result.AttackCheck!.Difficulty);

        // A master takes no distance penalty and still the two-thirds aim penalty.
        var master = Run(request with { Training = SkillTraining.Master }, closed);
        Assert.AreEqual(0, master.Difficulty!.Range);
        Assert.AreEqual(33, master.Difficulty.Aim);
        // A bow expert takes the full aim penalty.
        Assert.AreEqual(50, Run(request with { Skill = CombatSkill.Bow }, closed).Difficulty!.Aim);
        // Out of range or blocked: no roll can succeed.
        Assert.AreEqual(AttackRules.Impossible + 5 * 17, Run(request with { Distance = 21 }, closed).Difficulty!.Range);
        var blocked = Run(request with { LineOfFireBlocked = true }, closed).Difficulty!;
        Assert.AreEqual(AttackRules.Impossible + 25, blocked.Range);
        Assert.AreEqual(0, blocked.Cover);

        // Melee: no range, cover or distance terms.
        var melee = Run(request with { Skill = CombatSkill.Melee, AimedLocation = null, Distance = 9 },
            Concat(Opening(), new[] { (AttackCheck, 100), (AttackCritical, 100) }));
        Assert.AreEqual(0, melee.Difficulty!.Range);
        Assert.AreEqual(0, melee.Difficulty.Cover);
    }

    [TestMethod]
    public void AwarenessTakesTheFirstMatchingState()
    {
        Assert.AreEqual(-50, Awareness(CombatantConditions.Paralyzed | CombatantConditions.Stunned, unaware: true));
        Assert.AreEqual(-50, Awareness(CombatantConditions.Unconscious | CombatantConditions.Stunned, unaware: true));
        Assert.AreEqual(-30, Awareness(CombatantConditions.FallingDown | CombatantConditions.Sleeping, unaware: true));
        Assert.AreEqual(-50, Awareness(CombatantConditions.Sleeping, unaware: true));
        Assert.AreEqual(-30, Awareness(CombatantConditions.None, unaware: true));
        Assert.AreEqual(0, Awareness(CombatantConditions.None, unaware: false));

        static int Awareness(CombatantConditions conditions, bool unaware)
        {
            // Skill 0 against AC 95 keeps every roll a miss, so only the difficulty matters.
            var request = Melee() with
            {
                SkillLevel = 0, Target = Npc() with { Conditions = conditions, ArmorClass = 95 }, TargetUnaware = unaware,
            };
            return Run(request, Concat(Opening(), new[] { (AttackCheck, 100), (AttackCritical, 100) })).Difficulty!.Awareness;
        }
    }

    [TestMethod]
    public void TrueBackstabSkipsArmorAddsCriticalChanceAndDamage()
    {
        var attacker = Npc() with { BackstabLevel = 4, BackstabTraining = SkillTraining.Apprentice };
        var target = Npc() with { ArmorClass = 40, Level = 3 };
        var request = Melee() with { Attacker = attacker, Target = target, Backstab = BackstabKind.TrueBackstab };
        var result = Run(request, Concat(PlainHit(), new[] { (Normal, 5) }));
        Assert.AreEqual(0, result.Difficulty!.Armor);
        // 75 / 20 + 2 × 4 − 3.
        Assert.AreEqual(3 + 8 - 3, result.AttackCheck!.CriticalChance);
        Assert.AreEqual(5 + 20, result.Damage!.HitPointDamage);

        var seen = Run(request with { Backstab = BackstabKind.Backstab }, Concat(PlainHit(), new[] { (Normal, 5) }));
        Assert.AreEqual(15, seen.Difficulty!.Armor, "75 × (40 / 2) / 100");
        Assert.AreEqual(3, seen.AttackCheck!.CriticalChance);
        Assert.AreEqual(5 + 4, seen.Damage!.HitPointDamage);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            AttackResolver.Resolve(request with { Weapon = CombatWeapon.Unarmed() }, new SeededRolls(1)));
    }

    [TestMethod]
    public void CriticalMissAndSelfHitThresholds()
    {
        // Critical miss chance (100 − 75) / 7 = 3: a roll of 3 is critical, 4 is not.
        var critical = Run(Melee(), Concat(Opening(), new[] { (AttackCheck, 100), (AttackCritical, 3), (CriticalSelfHit, 50), (Normal, 5) },
            NoMultiplier()));
        Assert.AreEqual(AttackOutcome.CriticalMiss, critical.Outcome);
        Assert.IsTrue(critical.CriticalSelfHit, "50 is still a critical self-hit");
        Assert.AreEqual(7, critical.Damage!.HitPointDamage);

        var plain = Run(Melee(), Concat(Opening(), new[] { (AttackCheck, 100), (AttackCritical, 4) }));
        Assert.AreEqual(AttackOutcome.Miss, plain.Outcome);
    }

    [TestMethod]
    public void MagneticInversionHampersFirearmsOnly()
    {
        var target = Npc() with { Conditions = CombatantConditions.MagneticInversion };
        var gun = Melee() with { Target = target, Skill = CombatSkill.Firearms, Weapon = Sword() with { Range = 10 } };
        var rolls = Concat(Opening(), new[] { (AttackCheck, 100), (AttackCritical, 100) });
        Assert.AreEqual(55, Run(gun, rolls).AttackCheck!.Effectiveness);
        Assert.AreEqual(75, Run(gun with { Skill = CombatSkill.Bow }, rolls).AttackCheck!.Effectiveness);
    }

    [TestMethod]
    public void MeleeMasterNeverFailsCritically()
    {
        var request = Melee() with { Training = SkillTraining.Master };
        var result = Run(request, Concat(Opening(), new[] { (AttackCheck, 100), (AttackCritical, 1) }));
        Assert.AreEqual(AttackOutcome.Miss, result.Outcome);
        Assert.IsFalse(result.AttackCheck!.Critical);

        var apprentice = Run(Melee(), Concat(Opening(), new[] { (AttackCheck, 100), (AttackCritical, 1), (CriticalSelfHit, 100), (Normal, 5) }));
        Assert.AreEqual(AttackOutcome.CriticalMiss, apprentice.Outcome);
    }

    [TestMethod]
    public void DamageImmunitiesAndFatigueFlags()
    {
        var weapon = Sword() with
        {
            Damage = new DamageRanges(new DamageRange(5, 10), new DamageRange(1, 5), DamageRange.Zero, DamageRange.Zero, new DamageRange(2, 8)),
        };
        var request = Melee() with { Weapon = weapon };

        var living = Run(request, Concat(PlainHit(), new[] { (Normal, 6), (Poison, 3), (Fatigue, 2) }));
        Assert.AreEqual(3, living.Damage!.PoisonDamage);
        Assert.AreEqual(2, living.Damage.FatigueDamage);
        Assert.AreEqual(6, living.Damage.HitPointDamage, "poison and fatigue never reduce hit points");

        // Undead take no poison and draw no poison roll.
        var undead = Run(request with { Target = Npc() with { Conditions = CombatantConditions.Undead } },
            Concat(PlainHit(), new[] { (Normal, 6), (Fatigue, 2) }));
        Assert.AreEqual(0, undead.Damage!.PoisonDamage);

        var draining = Run(request with { Target = Npc() with { Conditions = CombatantConditions.FatigueDraining } },
            Concat(PlainHit(), new[] { (Normal, 6), (Poison, 3), (Fatigue, 2) }));
        Assert.AreEqual(8, draining.Damage!.FatigueDamage);
        var limiting = Run(request with { Target = Npc() with { Conditions = CombatantConditions.FatigueLimiting } },
            Concat(PlainHit(), new[] { (Normal, 6), (Poison, 3), (Fatigue, 7) }));
        Assert.AreEqual(1, limiting.Damage!.FatigueDamage);
        var immune = Run(request with { Target = Npc() with { Conditions = CombatantConditions.FatigueImmune } },
            Concat(PlainHit(), new[] { (Normal, 6), (Poison, 3), (Fatigue, 7) }));
        Assert.AreEqual(7, immune.Damage!.Final.Fatigue);
        Assert.AreEqual(0, immune.Damage.FatigueDamage);

        // A stoned critter's damage is zeroed but its critical still rolls.
        var stoned = Run(request with { Target = Npc() with { Conditions = CombatantConditions.Stoned } },
            Concat(CriticalHit(), new[] { (Normal, 6), (Fatigue, 7), (Triple, 1) }));
        Assert.AreEqual(DamageByType.Zero, stoned.Damage!.Final);
        Assert.AreEqual(0, stoned.Damage.HitPointDamage);
        Assert.AreEqual(DamageMultiplier.Triple, stoned.Damage.Multiplier);
    }

    [TestMethod]
    public void UnarmedAttacksAndBodySpells()
    {
        var request = Melee() with
        {
            Attacker = Npc() with { DamageBonus = 2, UnarmedBodySpell = UnarmedBodySpell.BodyOfFire },
            Weapon = CombatWeapon.Unarmed(),
            Target = Npc() with { Resistances = DamageResistances.None with { Fire = 20 } },
        };
        var result = Run(request, Concat(PlainHit(), new[] { (Normal, 4), (Fatigue, 3) }));
        Assert.AreEqual(DamageByType.Zero with { Normal = 4, Fire = 15, Fatigue = 3 }, result.Damage!.Rolled);
        // Fire 15 − 20 % = 12; fire damage wears armour.
        Assert.AreEqual(4 + 12, result.Damage.HitPointDamage);
        Assert.IsTrue(result.Damage.DamagesArmor);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            AttackResolver.Resolve(request with { Skill = CombatSkill.Bow }, new SeededRolls(1)));

        var earth = Run(request with { Attacker = Npc() with { UnarmedBodySpell = UnarmedBodySpell.BodyOfEarth } },
            Concat(PlainHit(), new[] { (Normal, 4), (Fatigue, 3) }));
        Assert.AreEqual(9, earth.Damage!.HitPointDamage);
        Assert.IsFalse(earth.Damage.DamagesArmor);
        var water = Run(request with { Attacker = Npc() with { UnarmedBodySpell = UnarmedBodySpell.BodyOfWater } },
            Concat(PlainHit(), new[] { (Normal, 4), (Fatigue, 3) }));
        Assert.AreEqual(18, water.Damage!.FatigueDamage);
        var hands = Run(request with { Attacker = Npc() with { UnarmedBodySpell = UnarmedBodySpell.HardenedHands } },
            Concat(PlainHit(), new[] { (Normal, 4), (Fatigue, 3) }));
        Assert.AreEqual(6, hands.Damage!.HitPointDamage);
        // Body spells need bare hands.
        var armed = Run(request with { Weapon = Sword() }, Concat(PlainHit(), new[] { (Normal, 8) }));
        Assert.AreEqual(8, armed.Damage!.HitPointDamage);
    }

    [TestMethod]
    public void DamageArmorFlagFromTheWeapon()
    {
        var result = Run(Melee() with { Weapon = Sword() with { DamagesArmor = true } }, Concat(PlainHit(), new[] { (Normal, 5) }));
        Assert.IsTrue(result.Damage!.DamagesArmor);
    }

    [TestMethod]
    public void TooLittleAmmunitionRefusesAfterTheHitLocationRoll()
    {
        // ARC-COMBAT-012: the dispatcher rolls the hit location, then checks the ammunition (0x4B65D0) before every other roll.
        var shortOf = AttackResolver.Resolve(Melee() with { AmmunitionShort = true }, new ScriptedRolls(new[] { (LocationRoll, 50) }));
        Assert.AreEqual((AttackRefusal.OutOfAmmunition, AttackOutcome.Refused, 1), (shortOf.Refusal, shortOf.Outcome, shortOf.Rolls.Count));
        var aimed = AttackResolver.Resolve(Melee() with { AmmunitionShort = true, AimedLocation = HitLocation.Head },
            new ScriptedRolls(Array.Empty<(CombatRollPurpose, int)>()));
        Assert.AreEqual((AttackRefusal.OutOfAmmunition, 0), (aimed.Refusal, aimed.Rolls.Count), "an aimed shot rolls nothing first");
        // An inactive attacker is refused first, with no roll.
        Assert.AreEqual(AttackRefusal.AttackerInactive, AttackResolver.Resolve(Melee() with
        {
            AmmunitionShort = true, Attacker = Npc() with { Conditions = CombatantConditions.Stunned },
        }, new ScriptedRolls(Array.Empty<(CombatRollPurpose, int)>())).Refusal);
    }

    [TestMethod]
    public void RefusalsMakeNoRolls()
    {
        var stunned = AttackResolver.Resolve(Melee() with { Attacker = Npc() with { Conditions = CombatantConditions.Stunned } },
            new ScriptedRolls(Array.Empty<(CombatRollPurpose, int)>()));
        Assert.AreEqual(AttackRefusal.AttackerInactive, stunned.Refusal);
        Assert.AreEqual(AttackOutcome.Refused, stunned.Outcome);
        Assert.AreEqual(0, stunned.Rolls.Count);

        var corpse = AttackResolver.Resolve(Melee() with { Target = Npc() with { Conditions = CombatantConditions.Dead } },
            new ScriptedRolls(Array.Empty<(CombatRollPurpose, int)>()));
        Assert.AreEqual(AttackRefusal.TargetDead, corpse.Refusal);
        // A "dead" flag on an object means nothing.
        var portal = Run(Melee() with { TargetKind = AttackTargetKind.Portal, Target = Npc() with { Conditions = CombatantConditions.Dead } },
            Concat(Opening(), new[] { (Normal, 5) }));
        Assert.AreEqual(AttackOutcome.Hit, portal.Outcome);
    }

    [TestMethod]
    public void RollTraceAndSourceChecks()
    {
        var result = Run(Melee(), Concat(PlainHit(), new[] { (Normal, 7) }));
        CollectionAssert.AreEqual(
            new[] { LocationRoll, TechnologyFailure, MagickCancel, AttackCheck, AttackCritical, DodgeCheck, DodgeCritical, Normal },
            result.Rolls.Select(r => r.Purpose).ToArray());
        Assert.AreEqual(new CombatRoll(Normal, 5, 10, 7), result.Rolls[^1]);

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            AttackResolver.Resolve(Melee(), new ScriptedRolls(new[] { (LocationRoll, 101) })));
    }

    [TestMethod]
    public void SeededRollsReplayExactly()
    {
        var request = Melee() with
        {
            Target = Npc() with { DodgeLevel = 8, Resistances = new DamageResistances(10, 0, 0, 0) },
            Weapon = Sword() with { Damage = DamageRanges.None with { Normal = new DamageRange(2, 12), Fatigue = new DamageRange(1, 4) } },
        };
        for (var seed = 0; seed < 50; seed++)
        {
            var first = AttackResolver.Resolve(request, new SeededRolls(seed));
            var second = AttackResolver.Resolve(request, new SeededRolls(seed));
            CollectionAssert.AreEqual(first.Rolls.ToArray(), second.Rolls.ToArray());
            Assert.AreEqual(first.Outcome, second.Outcome);
            Assert.AreEqual(first.Damage, second.Damage);
        }
    }

    [TestMethod]
    public void InvalidRequestsThrow()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => AttackResolver.Resolve(null!, new SeededRolls(1)));
        Assert.ThrowsExactly<ArgumentNullException>(() => AttackResolver.Resolve(Melee(), null!));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AttackResolver.Resolve(Melee() with { SkillLevel = 21 }, new SeededRolls(1)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            AttackResolver.Resolve(Melee() with { Target = Npc() with { ArmorClass = 96 } }, new SeededRolls(1)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            AttackResolver.Resolve(Melee() with { Weapon = Sword() with { Hp = 5, MaxHp = 4 } }, new SeededRolls(1)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            AttackResolver.Resolve(Melee() with { Weapon = Sword() with { Kind = WeaponKind.ThrownItem } }, new SeededRolls(1)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            AttackResolver.Resolve(Melee() with { Target = Npc() with { Conditions = (CombatantConditions)(1 << 30) } }, new SeededRolls(1)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            AttackResolver.Resolve(Melee() with { Weapon = Sword() with { Damage = DamageRanges.None with { Fire = new DamageRange(-1, 2) } } },
                new SeededRolls(1)));
    }
}
