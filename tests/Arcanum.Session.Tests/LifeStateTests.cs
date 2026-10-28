using Arcanum.Simulation.Character;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Randomness;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The session's life domain (ARC-COMBAT-002). Entity ids and facts are invented.
[TestClass]
public sealed class LifeStateTests
{
    private const ulong Seed = 0x0123456789ABCDEFUL;

    // ST 8, CN 8, WP 8, level 1: 30 hit points and 30 fatigue.
    private static readonly LifeFacts Wolf = new();
    private static SessionLifeTarget Npc(string entity = "oid:wolf", LifeFacts? facts = null, LifeState loaded = default) =>
        SessionLifeTarget.Critter(entity, facts ?? Wolf, loaded);

    private static GameSessionOwner Seeded(SessionHeroDefinition? hero = null, GameSessionLimits? limits = null)
    {
        var result = GameSessionOwner.Create(Descriptor(limits) with { Hero = hero, RandomSeed = Seed });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    private static SessionLifeResult Changed(SessionLifeResult result, GameSessionOwner owner, long revision)
    {
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (result.Disposition, result.Failure));
        Assert.AreEqual(new GameSessionVersion(owner.Identity, revision), result.Version);
        Assert.AreEqual(result.Version, owner.Version);
        Assert.IsNotNull(result.Outcome);
        return result;
    }

    private static void Refused(SessionLifeResult result, GameSessionOwner owner, GameSessionFailure failure)
    {
        Assert.AreEqual((GameSessionDisposition.Rejected, failure, (LifeOutcome?)null), (result.Disposition, result.Failure, result.Outcome));
        Assert.AreEqual(owner.Version, result.Version);
    }

    private static int FirstRoll(int min, int max, long draws = 0)
    {
        Assert.IsTrue(new SeededRandom(Seed, draws).TryNext(min, max, out var value));
        return value;
    }

    [TestMethod]
    public void TheHeroStartsUnharmedWithMaximaFromItsStats()
    {
        // Half-elf male: ST 8, CN 8 − 1 = 7, WP 8, level 1, heal rate (7 + 1) / 3 = 2.
        using var owner = Seeded(HalfElf());
        var life = owner.QueryHeroLife()!;
        Assert.AreEqual(new SessionLifeRecord(LifeState.Unharmed, null), life.Record);
        Assert.AreEqual(new LifeStatus(30, 30, 28, 28, false, false, true), life.Status);
        Assert.IsTrue(life.Recorded);
        Assert.AreEqual(life, owner.QueryLife(SessionLifeTarget.Hero).View);

        using var none = Seeded();
        Assert.IsNull(none.QueryHeroLife());
        Assert.AreEqual(new SessionLifeQueryResult(GameSessionFailure.InvalidLifeTarget, null), none.QueryLife(SessionLifeTarget.Hero));
        Refused(none.ApplyDamage(none.Version, SessionLifeTarget.Hero, new LifeDamage { HitPoints = 1 }), none, GameSessionFailure.InvalidLifeTarget);
    }

    [TestMethod]
    public void HeroPoisonLowersItsStrengthAndItsHitPoints()
    {
        using var owner = Seeded(HalfElf());
        Changed(owner.ApplyDamage(owner.Version, SessionLifeTarget.Hero, new LifeDamage { HitPoints = 3, Poison = 250 }), owner, 1);
        var life = owner.QueryHeroLife()!;
        // Strength 8 − 2 = 6: 8 + 2 × (6 + 1) + 4 = 26 hit points, 23 left.
        Assert.AreEqual((26, 23, 250), (life.Status.MaxHitPoints, life.Status.HitPoints, life.Record.State.PoisonLevel));
        Assert.AreEqual(6, owner.QueryHeroStats(new(0, 250))!.Value.Snapshot!.GetEffective(CharacterStat.Strength));

        // The 120-second recovery tick removes the poison recovery stat (CN 7); the damage tick deals 2 at this level.
        Changed(owner.PoisonRecoveryTick(owner.Version, SessionLifeTarget.Hero), owner, 2);
        Assert.AreEqual(243, owner.QueryHeroLife()!.Record.State.PoisonLevel);
        Changed(owner.PoisonDamageTick(owner.Version, SessionLifeTarget.Hero), owner, 3);
        Assert.AreEqual(5, owner.QueryHeroLife()!.Record.State.HitPointDamage);
    }

    [TestMethod]
    public void AnAttackOnAnNpcIsResolvedAndAppliedWithTheSessionGenerator()
    {
        using var owner = Seeded();
        var request = new AttackRequest
        {
            Attacker = new Combatant { Strength = 10, Perception = 8 },
            Target = new Combatant { Strength = 10, Perception = 8 },
            Weapon = new CombatWeapon { Kind = WeaponKind.Weapon, Damage = DamageRanges.None with { Normal = new DamageRange(5, 10) } },
            SkillLevel = 20,
        };
        // Attack until a hit on the target that does not kill (a miss or a self-hit deals the target nothing).
        LifeDamage? damage;
        do
        {
            var resolution = AttackResolver.Resolve(request, owner.CombatRolls);
            damage = resolution.Damage?.Recipient == DamageRecipient.Target ? LifeDamage.FromAttack(resolution) : null;
        }
        while (damage is not { HitPoints: > 0 and < 30 });

        Assert.IsNull(owner.QueryLifeRecord("oid:wolf"));
        var result = Changed(owner.ApplyDamage(owner.Version, Npc(), damage), owner, 1);
        Assert.AreEqual(new LifeState(damage.HitPoints, 0, 0, LifeConditions.None, 0), result.Outcome!.After);
        Assert.AreEqual(new SessionLifeRecord(result.Outcome.After, null), owner.QueryLifeRecord("oid:wolf"));
        Assert.AreEqual(30 - damage.HitPoints, owner.QueryLife(Npc()).View!.Status.HitPoints);
        Assert.IsNull(owner.QueryLifeRecord("oid:other"), "an NPC the session never changed has no record");
    }

    [TestMethod]
    public void TheLoadedStateCountsUntilTheFirstChange()
    {
        using var owner = Seeded();
        var loaded = new LifeState(4, 2, 0, LifeConditions.Blinded, 1);
        var view = owner.QueryLife(Npc(loaded: loaded)).View!;
        Assert.AreEqual((new SessionLifeRecord(loaded, null), false, 26), (view.Record, view.Recorded, view.Status.HitPoints));
        Changed(owner.ApplyDamage(owner.Version, Npc(loaded: loaded), new LifeDamage { HitPoints = 1 }), owner, 1);
        // The record wins over any loaded state afterwards.
        var after = owner.QueryLife(Npc(loaded: LifeState.Unharmed)).View!;
        Assert.AreEqual((loaded with { HitPointDamage = 5 }, true), (after.Record.State, after.Recorded));
    }

    [TestMethod]
    public void AKnockoutRollsFromTheSessionAndReplaysAfterASave()
    {
        using var owner = Seeded();
        var saved = GameSessionSnapshotCodec.Encode(owner.Capture());
        var knockout = new LifeDamage { HitPoints = 2, Effects = CriticalHitEffects.Knockout };
        var first = Changed(owner.ApplyDamage(owner.Version, Npc(), knockout), owner, 1).Outcome!;
        var roll = FirstRoll(10, 20);
        Assert.AreEqual(new LifeState(2, 30 + roll, 0, LifeConditions.None, 0), first.After);
        Assert.AreEqual(LifeEvents.KnockedDown | LifeEvents.KnockedOut | LifeEvents.LostConsciousness, first.Events);
        Assert.AreEqual(new SessionRandomState(Seed, 1), owner.QueryRandom());
        var status = owner.QueryLife(Npc()).View!.Status;
        Assert.AreEqual((false, true, false), (status.IsDead, status.IsUnconscious, status.IsActive));

        using var restored = Restore(Decode(saved));
        Assert.AreEqual(first, restored.ApplyDamage(restored.Version, Npc(), knockout).Outcome);
        Assert.AreEqual(owner.QueryRandom(), restored.QueryRandom());
    }

    [TestMethod]
    public void ADeathStampsTheGameTimeAndRoundTripsThroughTheSnapshot()
    {
        using var owner = Seeded(HalfElf());
        var clock = owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, 5_000, ClockAdvanceReason.OrdinaryAdvance).Snapshot;
        var died = Changed(owner.ApplyDamage(owner.Version, Npc(), new LifeDamage { HitPoints = 31, Location = HitLocation.Leg,
            Effects = CriticalHitEffects.CrippleLeg | CriticalHitEffects.Scar }), owner, 1).Outcome!;
        Assert.AreEqual((LifeEvents.Died | LifeEvents.CrippledLegs | LifeEvents.Scarred, DeathAnimation.Leg), (died.Events, died.Death));
        Assert.AreEqual(new SessionLifeRecord(new(LifeLimits.DeathHitPointDamage, 0, 0, LifeConditions.CrippledLegsBoth, 1), clock.GameTimestampMs),
            owner.QueryLifeRecord("oid:wolf"));
        Changed(owner.ApplyDamage(owner.Version, Npc("oid:adder"), new LifeDamage { Poison = 40, Effects = CriticalHitEffects.Stun | CriticalHitEffects.Blind }), owner, 2);
        Changed(owner.ApplyDamage(owner.Version, SessionLifeTarget.Hero, new LifeDamage { HitPoints = 4, Fatigue = 6 }), owner, 3);

        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, ",\"life\":{\"hero\":{\"hpDamage\":4,\"fatigueDamage\":6,\"poison\":0,\"conditions\":[],\"scars\":0,\"deathTimeMs\":null}," +
            "\"critters\":[{\"entity\":\"oid:adder\",\"hpDamage\":0,\"fatigueDamage\":0,\"poison\":40,\"conditions\":[\"Stunned\",\"Blinded\"],\"scars\":0,\"deathTimeMs\":null}," +
            "{\"entity\":\"oid:wolf\",\"hpDamage\":32000,\"fatigueDamage\":0,\"poison\":0,\"conditions\":[\"CrippledLegsBoth\"],\"scars\":1,\"deathTimeMs\":54005000}]},\"reactions\":");
        using var restored = Restore(Decode(text));
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        Assert.AreEqual(owner.QueryHeroLife(), restored.QueryHeroLife());
        Assert.IsTrue(restored.QueryLife(Npc()).View!.Status.IsDead);

        // A dead NPC takes nothing more: NoChange, but the outcome says why.
        var again = restored.ApplyDamage(restored.Version, Npc(), new LifeDamage { HitPoints = 3 });
        Assert.AreEqual((GameSessionDisposition.NoChange, LifeEvents.AlreadyDead), (again.Disposition, again.Outcome!.Events));
        Assert.AreEqual(restored.Version, again.Version);
    }

    [TestMethod]
    public void HealingRestingAndRecoveryWorkOnTheSessionClock()
    {
        using var owner = Seeded(HalfElf());
        var hero = SessionLifeTarget.Hero;
        Changed(owner.ApplyDamage(owner.Version, hero, new LifeDamage { HitPoints = 20, Fatigue = 30 }), owner, 1);
        Assert.IsTrue(owner.QueryHeroLife()!.Status.IsUnconscious);

        // Scheduled at the start, fired 160 s later: 2 steps × heal rate 2 = 4 fatigue damage.
        var start = owner.QueryClockSnapshot().GameTimestampMs;
        owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, 160_000, ClockAdvanceReason.OrdinaryAdvance);
        var recovered = Changed(owner.RecoverFatigue(owner.Version, hero, start), owner, 2).Outcome!;
        Assert.AreEqual((26, LifeEvents.RegainedConsciousness), (recovered.After.FatigueDamage, recovered.Events));
        Refused(owner.RecoverFatigue(owner.Version, hero, owner.QueryClockSnapshot().GameTimestampMs + 1), owner, GameSessionFailure.InvalidLifeAmount);
        Refused(owner.RecoverFatigue(owner.Version, hero, -1), owner, GameSessionFailure.InvalidLifeAmount);

        // Three hours of rest: 6 hit points and 18 fatigue.
        Assert.AreEqual(new LifeState(14, 8, 0, LifeConditions.None, 0), Changed(owner.RestHeal(owner.Version, hero, 3), owner, 3).Outcome!.After);
        Assert.AreEqual(new LifeState(0, 8, 0, LifeConditions.None, 0),
            Changed(owner.Heal(owner.Version, hero, new LifeHeal { HitPoints = 100 }), owner, 4).Outcome!.After);
        var nothing = owner.Heal(owner.Version, hero, new LifeHeal { HitPoints = 1 });
        Assert.AreEqual((GameSessionDisposition.NoChange, 4L), (nothing.Disposition, owner.Version.Revision));
    }

    [TestMethod]
    public void KillStunAndResurrection()
    {
        using var owner = Seeded();
        Changed(owner.ApplyDamage(owner.Version, Npc(), new LifeDamage { Effects = CriticalHitEffects.Stun }), owner, 1);
        Assert.IsFalse(owner.QueryLife(Npc()).View!.Status.IsActive);
        Assert.AreEqual(LifeEvents.StunEnded, Changed(owner.ClearStun(owner.Version, Npc()), owner, 2).Outcome!.Events);
        var killed = Changed(owner.Kill(owner.Version, Npc()), owner, 3).Outcome!;
        Assert.AreEqual((LifeEvents.Died, DeathAnimation.Plain), (killed.Events, killed.Death));
        var deathTime = owner.QueryLifeRecord("oid:wolf")!.Value.DeathTimeMs;
        Assert.IsNotNull(deathTime);

        // ARC-MAGIC-012: the target's raise script may veto the raise (the host's port); the state stays.
        var vetoed = owner.Heal(owner.Version, Npc(), new LifeHeal { Resurrect = true, Full = true }, () => false);
        Assert.AreEqual((GameSessionDisposition.NoChange, LifeEvents.ResurrectionRefused), (vetoed.Disposition, vetoed.Outcome!.Events));
        var back = Changed(owner.Heal(owner.Version, Npc(), new LifeHeal { Resurrect = true, Full = true }, () => true), owner, 4).Outcome!;
        Assert.AreEqual(new LifeState(0, 10, 0, LifeConditions.None, 0), back.After);
        // The death time stays after a resurrection, like the source's.
        Assert.AreEqual(deathTime, owner.QueryLifeRecord("oid:wolf")!.Value.DeathTimeMs);
        var refused = owner.Heal(owner.Version, Npc(facts: Wolf with { Traits = LifeTraits.Unresurrectable }), new LifeHeal { Resurrect = true });
        Assert.AreEqual(GameSessionDisposition.NoChange, refused.Disposition, "alive: nothing to resurrect");
    }

    [TestMethod]
    public void BadInputsAndStaleVersionsAreRefusedBeforeAnyRoll()
    {
        using var owner = Seeded(HalfElf());
        var arm = new LifeDamage { Effects = CriticalHitEffects.CrippleArm | CriticalHitEffects.Knockout };
        Refused(owner.ApplyDamage(owner.Version, Npc(""), arm), owner, GameSessionFailure.InvalidLifeTarget);
        Refused(owner.ApplyDamage(owner.Version, Npc("a b"), arm), owner, GameSessionFailure.InvalidLifeTarget);
        Refused(owner.ApplyDamage(owner.Version, Npc(facts: Wolf with { Level = 99 }), arm), owner, GameSessionFailure.InvalidLifeFacts);
        Refused(owner.ApplyDamage(owner.Version, Npc(loaded: new LifeState(-1, 0, 0, LifeConditions.None, 0)), arm), owner, GameSessionFailure.InvalidLifeState);
        Refused(owner.ApplyDamage(owner.Version, Npc(), arm with { HitPoints = LifeLimits.MaxAmount + 1 }), owner, GameSessionFailure.InvalidLifeAmount);
        Refused(owner.ApplyDamage(owner.Version, Npc(), arm with { Location = (HitLocation)9 }), owner, GameSessionFailure.InvalidLifeAmount);
        Refused(owner.Heal(owner.Version, Npc(), new LifeHeal { Poison = -1 }), owner, GameSessionFailure.InvalidLifeAmount);
        Refused(owner.RestHeal(owner.Version, Npc(), LifeLimits.MaxRestHours + 1), owner, GameSessionFailure.InvalidLifeAmount);
        Refused(owner.ApplyDamage(new GameSessionVersion(owner.Identity, 7), Npc(), arm), owner, GameSessionFailure.StaleVersion);
        Refused(owner.Kill(new GameSessionVersion(null, 0), SessionLifeTarget.Hero), owner, GameSessionFailure.StaleVersion);
        Assert.AreEqual(new SessionRandomState(Seed, 0), owner.QueryRandom(), "no refusal draws");
        Assert.ThrowsExactly<ArgumentException>(() => owner.QueryLifeRecord("a b"));
        Assert.ThrowsExactly<ArgumentNullException>(() => owner.ApplyDamage(owner.Version, null!, arm));
        Assert.ThrowsExactly<ArgumentNullException>(() => owner.ApplyDamage(owner.Version, Npc(), null!));
    }

    [TestMethod]
    public void NewRecordsStayWithinTheBudget()
    {
        using var owner = Seeded(limits: new GameSessionLimits(maxLifeRecords: 1));
        Changed(owner.ApplyDamage(owner.Version, Npc("oid:a"), new LifeDamage { HitPoints = 1 }), owner, 1);
        Refused(owner.ApplyDamage(owner.Version, Npc("oid:b"), new LifeDamage { HitPoints = 1 }), owner, GameSessionFailure.BudgetExceeded);
        Changed(owner.ApplyDamage(owner.Version, Npc("oid:a"), new LifeDamage { HitPoints = 1 }), owner, 2);
        // A step that would change nothing creates no record.
        using var empty = Seeded();
        var knockdown = empty.ApplyDamage(empty.Version, Npc(), new LifeDamage { Effects = CriticalHitEffects.Knockdown });
        Assert.AreEqual((GameSessionDisposition.NoChange, LifeEvents.KnockedDown), (knockdown.Disposition, knockdown.Outcome!.Events));
        Assert.IsNull(empty.QueryLifeRecord("oid:wolf"));

        var snapshot = owner.Capture();
        Assert.AreEqual(GameSessionFailure.None, GameSessionOwner.Restore(snapshot, RestoreOptions(new GameSessionLimits(maxLifeRecords: 1))).Failure);
        var text = GameSessionSnapshotCodec.Encode(snapshot).Replace("{\"entity\":\"oid:a\"",
            "{\"entity\":\"oid:0\",\"hpDamage\":0,\"fatigueDamage\":0,\"poison\":0,\"conditions\":[],\"scars\":0,\"deathTimeMs\":null},{\"entity\":\"oid:a\"",
            StringComparison.Ordinal);
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, GameSessionOwner.Restore(Decode(text), RestoreOptions(new GameSessionLimits(maxLifeRecords: 1))).Failure);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new GameSessionLimits(maxLifeRecords: 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new GameSessionLimits(maxLifeRecords: GameSessionLimits.PolicyMaxLifeRecords + 1));
    }

    [TestMethod]
    public void AStepThatRollsButChangesNothingStillSpendsItsDraw()
    {
        // One crippled arm already; the second-arm d100 of this seed is 66, above 50.
        using var owner = Seeded();
        Assert.AreEqual(66, FirstRoll(1, 100));
        var arm = owner.ApplyDamage(owner.Version, Npc(loaded: new LifeState(0, 0, 0, LifeConditions.CrippledArmOne, 0)),
            new LifeDamage { Effects = CriticalHitEffects.CrippleArm });
        Assert.AreEqual((GameSessionDisposition.NoChange, LifeEvents.ArmHitDropsItems), (arm.Disposition, arm.Outcome!.Events));
        Assert.AreEqual((new SessionRandomState(Seed, 1), 0L), (owner.QueryRandom(), owner.Version.Revision));
        Assert.IsNull(owner.QueryLifeRecord("oid:wolf"));
    }

    [TestMethod]
    public void AFullBudgetStillReportsAStepThatChangesNothing()
    {
        using var owner = Seeded(limits: new GameSessionLimits(maxLifeRecords: 1));
        Changed(owner.ApplyDamage(owner.Version, Npc("oid:a"), new LifeDamage { HitPoints = 1 }), owner, 1);
        var knockdown = owner.ApplyDamage(owner.Version, Npc("oid:b"), new LifeDamage { Effects = CriticalHitEffects.Knockdown });
        Assert.AreEqual((GameSessionDisposition.NoChange, LifeEvents.KnockedDown), (knockdown.Disposition, knockdown.Outcome!.Events));
        // A knockout would change the new NPC: refused, and its roll is not spent.
        Refused(owner.ApplyDamage(owner.Version, Npc("oid:b"), new LifeDamage { Effects = CriticalHitEffects.Knockout }), owner,
            GameSessionFailure.BudgetExceeded);
        Assert.AreEqual(new SessionRandomState(Seed, 0), owner.QueryRandom());
    }

    [TestMethod]
    public void AnUnresolvedEntityNeverReachesTheHero()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => SessionLifeTarget.Critter(null!, Wolf, LifeState.Unharmed));
        Assert.ThrowsExactly<ArgumentNullException>(() => SessionLifeTarget.Critter("oid:wolf", null!, LifeState.Unharmed));
        Assert.IsTrue(SessionLifeTarget.Hero.IsHero);
        Assert.IsFalse(Npc().IsHero);
    }

    [TestMethod]
    public void AnUnresurrectableCorpseStaysDead()
    {
        using var owner = Seeded();
        var facts = Wolf with { Traits = LifeTraits.Unresurrectable };
        Changed(owner.Kill(owner.Version, Npc(facts: facts)), owner, 1);
        var refused = owner.Heal(owner.Version, Npc(facts: facts), new LifeHeal { Resurrect = true, Full = true });
        Assert.AreEqual((GameSessionDisposition.NoChange, LifeEvents.ResurrectionRefused), (refused.Disposition, refused.Outcome!.Events));
        Assert.IsTrue(owner.QueryLife(Npc(facts: facts)).View!.Status.IsDead);
    }

    [TestMethod]
    public void AnExhaustedGeneratorRefusesARollingStepWithNoDraw()
    {
        using var fresh = Seeded();
        var text = GameSessionSnapshotCodec.Encode(fresh.Capture()).Replace("\"draws\":0", "\"draws\":9223372036854775807", StringComparison.Ordinal);
        using var owner = Restore(Decode(text));
        Refused(owner.ApplyDamage(owner.Version, Npc(), new LifeDamage { HitPoints = 1, Effects = CriticalHitEffects.Knockout }), owner,
            GameSessionFailure.RandomExhausted);
        Assert.IsNull(owner.QueryLifeRecord("oid:wolf"));
        // A step without a roll still applies.
        Changed(owner.ApplyDamage(owner.Version, Npc(), new LifeDamage { HitPoints = 1 }), owner, 1);
    }

    [TestMethod]
    public void TheRevisionCannotOverflow()
    {
        var text = GoldenJson.Replace("\"revision\":2,\"clock\"", "\"revision\":9223372036854775807,\"clock\"", StringComparison.Ordinal);
        using var owner = Restore(Decode(text));
        Refused(owner.ApplyDamage(owner.Version, Npc(), new LifeDamage { HitPoints = 1 }), owner, GameSessionFailure.RevisionExhausted);
        // A step that changes nothing is still NoChange.
        Assert.AreEqual(GameSessionDisposition.NoChange,
            owner.ApplyDamage(owner.Version, Npc(), new LifeDamage { Effects = CriticalHitEffects.Knockdown }).Disposition);
    }

    [TestMethod]
    public void DecodeAndRestoreRefuseLifeRecordsNoTransitionProduces()
    {
        using var owner = Seeded(HalfElf());
        Changed(owner.ApplyDamage(owner.Version, Npc(), new LifeDamage { HitPoints = 2, Effects = CriticalHitEffects.Blind | CriticalHitEffects.CrippleLeg }), owner, 1);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        const string critter = "{\"entity\":\"oid:wolf\",\"hpDamage\":2,\"fatigueDamage\":0,\"poison\":0,\"conditions\":[\"Blinded\",\"CrippledLegsBoth\"],\"scars\":0,\"deathTimeMs\":null}";
        StringAssert.Contains(text, critter);

        void Fails(string from, string to, SnapshotDecodeFailure failure, string path)
        {
            Assert.IsTrue(text.Contains(from, StringComparison.Ordinal), from);
            var result = GameSessionSnapshotCodec.TryDecode(text.Replace(from, to, StringComparison.Ordinal));
            Assert.AreEqual((failure, path), (result.Failure, result.Path), to);
        }
        Fails("[\"Blinded\",\"CrippledLegsBoth\"]", "[\"CrippledLegsBoth\",\"Blinded\"]", SnapshotDecodeFailure.NotCanonical, "$.life.critters[0].conditions[1]");
        Fails("[\"Blinded\",\"CrippledLegsBoth\"]", "[\"Blinded\",\"Blinded\"]", SnapshotDecodeFailure.NotCanonical, "$.life.critters[0].conditions[1]");
        Fails("[\"Blinded\",\"CrippledLegsBoth\"]", "[\"blinded\"]", SnapshotDecodeFailure.OutOfRange, "$.life.critters[0].conditions[0]");
        Fails("\"hpDamage\":2,", "\"hpDamage\":1000001,", SnapshotDecodeFailure.OutOfRange, "$.life.critters[0].hpDamage");
        Fails("\"poison\":0,\"conditions\":[\"Blinded\"", "\"poison\":1001,\"conditions\":[\"Blinded\"", SnapshotDecodeFailure.OutOfRange, "$.life.critters[0].poison");
        Fails("\"scars\":0,\"deathTimeMs\":null}]", "\"scars\":65,\"deathTimeMs\":null}]", SnapshotDecodeFailure.OutOfRange, "$.life.critters[0].scars");
        Fails("\"scars\":0,\"deathTimeMs\":null}]", "\"scars\":0,\"deathTimeMs\":\"0\"}]", SnapshotDecodeFailure.WrongType, "$.life.critters[0].deathTimeMs");
        Fails("\"scars\":0,\"deathTimeMs\":null}]", "\"scars\":0}]", SnapshotDecodeFailure.MissingField, "$.life.critters[0].deathTimeMs");
        Fails("\"entity\":\"oid:wolf\"", "\"entity\":\"oid wolf\"", SnapshotDecodeFailure.OutOfRange, "$.life.critters[0].entity");
        Fails(critter, critter + "," + critter, SnapshotDecodeFailure.NotCanonical, "$.life.critters[1].entity");
        Fails("\"critters\":[", "\"critters\":[],\"x\":[", SnapshotDecodeFailure.UnknownField, "$.life.x");
        var life = text[text.IndexOf(",\"life\":", StringComparison.Ordinal)..text.IndexOf(",\"reactions\":", StringComparison.Ordinal)];
        Fails(life, ",\"life\":[]", SnapshotDecodeFailure.WrongType, "$.life");
        Fails(life, ",\"life\":null", SnapshotDecodeFailure.WrongType, "$.life");

        GameSessionFailure Restored(string changed)
        {
            var result = GameSessionOwner.Restore(Decode(changed), RestoreOptions());
            result.Owner?.Dispose();
            return result.Failure;
        }
        Assert.AreEqual(GameSessionFailure.None, Restored(text));
        // A death after the saved clock, and a hero life without a hero or the other way round.
        Assert.AreEqual(GameSessionFailure.InvalidLifeState, Restored(text.Replace("\"scars\":0,\"deathTimeMs\":null}]", "\"scars\":0,\"deathTimeMs\":54000001}]", StringComparison.Ordinal)));
        Assert.AreEqual(GameSessionFailure.None, Restored(text.Replace("\"scars\":0,\"deathTimeMs\":null}]", "\"scars\":0,\"deathTimeMs\":54000000}]", StringComparison.Ordinal)));
        var heroLife = "\"hero\":{\"hpDamage\":0,\"fatigueDamage\":0,\"poison\":0,\"conditions\":[],\"scars\":0,\"deathTimeMs\":null},\"critters\"";
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, Restored(text.Replace(heroLife, "\"hero\":null,\"critters\"", StringComparison.Ordinal)));
        using var noHero = Seeded();
        var plain = GameSessionSnapshotCodec.Encode(noHero.Capture());
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, Restored(plain.Replace("\"hero\":null,\"critters\"", heroLife, StringComparison.Ordinal)));
    }

    [TestMethod]
    public void ADisposedSessionNoLongerAnswers()
    {
        var owner = Seeded(HalfElf());
        owner.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => owner.QueryHeroLife());
        Assert.ThrowsExactly<ObjectDisposedException>(() => owner.QueryLife(SessionLifeTarget.Hero));
        Assert.ThrowsExactly<ObjectDisposedException>(() => owner.Kill(default, SessionLifeTarget.Hero));
        Assert.ThrowsExactly<ObjectDisposedException>(() => owner.RecoverFatigue(default, SessionLifeTarget.Hero, 0));
    }
}
