using Arcanum.Simulation.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

[TestClass]
public sealed class SessionRandomTests
{
    private const ulong Seed = 0x0123456789ABCDEFUL;

    private static GameSessionOwner Seeded(ulong seed = Seed)
    {
        var result = GameSessionOwner.Create(Descriptor() with { RandomSeed = seed });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    private static SessionRoll Rolled(SessionRollResult result)
    {
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        Assert.IsNotNull(result.Roll);
        return result.Roll;
    }

    [TestMethod]
    public void RollsAreLabelledAndFollowTheSeedWithoutMovingTheRevision()
    {
        using var owner = Seeded();
        Assert.AreEqual(new SessionRandomState(Seed, 0), owner.QueryRandom());
        var version = owner.Version;
        // The SplitMix64 values of SeededRandomTests for the same seed.
        Assert.AreEqual(new SessionRoll("combat.AttackCheck", 1, 100, 66, 1), Rolled(owner.Roll("combat.AttackCheck", 1, 100)));
        Assert.AreEqual(new SessionRoll("loot:gold", 1, 100, 44, 2), Rolled(owner.Roll("loot:gold", 1, 100)));
        Assert.AreEqual(new SessionRoll("damage.normal", 5, 10, 7, 3), Rolled(owner.Roll("damage.normal", 5, 10)));
        Assert.AreEqual(new SessionRoll("fixed", 4, 4, 4, 3), Rolled(owner.Roll("fixed", 4, 4)));
        Assert.AreEqual(new SessionRandomState(Seed, 3), owner.QueryRandom());
        Assert.AreEqual(version, owner.Version);

        // A new session without a seed starts the sequence of seed 0.
        using var unseeded = Create();
        Assert.AreEqual(new SessionRandomState(0, 0), unseeded.QueryRandom());
    }

    [TestMethod]
    public void TheLabelNamesTheRollButDoesNotChangeIt()
    {
        using var first = Seeded();
        using var second = Seeded();
        foreach (var (a, b) in new[] { ("combat.DodgeCheck", "x"), ("skill:pick-lock", "Y_1"), ("a", "b") })
            Assert.AreEqual(Rolled(first.Roll(a, 1, 1000)).Value, Rolled(second.Roll(b, 1, 1000)).Value);
        using var other = Seeded(Seed + 1);
        Assert.AreNotEqual(66, Rolled(other.Roll("combat.AttackCheck", 1, 100)).Value);
    }

    [TestMethod]
    public void BadLabelsAndRangesAreRefusedWithoutADraw()
    {
        using var owner = Seeded();
        foreach (var label in new[] { null, "", " roll", "roll ", "ход", "a/b", new string('a', 65) })
            Assert.AreEqual(new SessionRollResult(null, GameSessionFailure.InvalidRoll), owner.Roll(label!, 1, 100), label);
        Assert.AreEqual(new SessionRollResult(null, GameSessionFailure.InvalidRoll), owner.Roll("ok", 2, 1));
        Assert.AreEqual(new SessionRandomState(Seed, 0), owner.QueryRandom());
        Assert.AreEqual(66, Rolled(owner.Roll(new string('a', 64), 1, 100)).Value);
    }

    [TestMethod]
    public void ARestoredSessionMakesTheSameRollsAsTheOneThatWasSaved()
    {
        using var owner = Seeded();
        for (var i = 0; i < 3; i++) Rolled(owner.Roll("warmup", 1, 100));
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, ",\"random\":{\"seed\":\"0123456789abcdef\",\"draws\":3}" + NoLife + NoReactions + NoCombat + NoEvents + ",\"lighting\":null}");
        var expected = new[] { 41, 53, 47, 2, 10 };
        CollectionAssert.AreEqual(expected, Enumerable.Range(0, 5).Select(_ => Rolled(owner.Roll("after", 1, 100)).Value).ToArray());

        using var restored = Restore(Decode(text));
        Assert.AreEqual(new SessionRandomState(Seed, 3), restored.QueryRandom());
        CollectionAssert.AreEqual(expected, Enumerable.Range(0, 5).Select(_ => Rolled(restored.Roll("after", 1, 100)).Value).ToArray());
        Assert.AreEqual(owner.QueryRandom(), restored.QueryRandom());
    }

    [TestMethod]
    public void TheAttackResolverReplaysAFightFromASave()
    {
        using var owner = Seeded();
        var saved = owner.Capture();
        var request = new AttackRequest
        {
            Attacker = new Combatant { Strength = 10, Perception = 8 },
            Target = new Combatant { Strength = 10, Perception = 8 },
            Weapon = new CombatWeapon { Kind = WeaponKind.Weapon, Damage = DamageRanges.None with { Normal = new DamageRange(5, 10) }, Range = 1 },
            Skill = CombatSkill.Melee,
            SkillLevel = 10,
        };
        var fight = Enumerable.Range(0, 4).Select(_ => AttackResolver.Resolve(request, owner.CombatRolls)).ToArray();
        Assert.AreEqual(fight.Sum(attack => attack.Rolls.Count), owner.QueryRandom().Draws, "one draw per roll from this seed");

        using var restored = Restore(saved);
        var replay = Enumerable.Range(0, 4).Select(_ => AttackResolver.Resolve(request, restored.CombatRolls)).ToArray();
        for (var i = 0; i < fight.Length; i++)
        {
            CollectionAssert.AreEqual(fight[i].Rolls.ToArray(), replay[i].Rolls.ToArray());
            Assert.AreEqual((fight[i].Outcome, fight[i].HitLocation, fight[i].Damage?.HitPointDamage),
                (replay[i].Outcome, replay[i].HitLocation, replay[i].Damage?.HitPointDamage));
        }
        Assert.AreEqual(owner.QueryRandom(), restored.QueryRandom());

        // The first rolls of this seed decide the opening: location 66 (torso), no technology failure or magick cancel.
        CollectionAssert.AreEqual(new[] { new CombatRoll(CombatRollPurpose.HitLocation, 1, 100, 66),
            new CombatRoll(CombatRollPurpose.TechnologyFailure, 1, 100, 44) }, fight[0].Rolls.Take(2).ToArray());
    }

    [TestMethod]
    public void CombatRollsAreLabelledByPurpose()
    {
        Assert.AreEqual("combat.AttackCheck", SessionCombatRolls.Label(CombatRollPurpose.AttackCheck));
        foreach (var purpose in Enum.GetValues<CombatRollPurpose>())
            Assert.IsTrue(SessionValidation.Code(SessionCombatRolls.Label(purpose), SessionValidation.MaxRollLabelCharacters), purpose.ToString());
        using var owner = Seeded();
        Assert.AreEqual(66, owner.CombatRolls.Roll(CombatRollPurpose.HitLocation, 1, 100));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => owner.CombatRolls.Roll((CombatRollPurpose)999, 1, 100));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => owner.CombatRolls.Roll(CombatRollPurpose.HitLocation, 2, 1));
        Assert.AreEqual(new SessionRandomState(Seed, 1), owner.QueryRandom());
    }

    [TestMethod]
    public void AGeneratorAtItsLastDrawRefusesRollsThatNeedOne()
    {
        using var fresh = Seeded();
        var text = GameSessionSnapshotCodec.Encode(fresh.Capture())
            .Replace("\"draws\":0", "\"draws\":9223372036854775807", StringComparison.Ordinal);
        using var owner = Restore(Decode(text));
        Assert.AreEqual(new SessionRollResult(null, GameSessionFailure.RandomExhausted), owner.Roll("late", 1, 100));
        Assert.AreEqual(new SessionRoll("late", 3, 3, 3, long.MaxValue), Rolled(owner.Roll("late", 3, 3)));
        Assert.ThrowsExactly<InvalidOperationException>(() => owner.CombatRolls.Roll(CombatRollPurpose.HitLocation, 1, 100));
        Assert.AreEqual(new SessionRandomState(Seed, long.MaxValue), owner.QueryRandom());
    }

    [TestMethod]
    public void DecodeRefusesADamagedRandomSection()
    {
        Assert.AreEqual(new SessionRandomState(0, 0), Decode(GoldenJson).Random);
        var seeded = GoldenJson.Replace(FreshRandom, ",\"random\":{\"seed\":\"ffffffffffffffff\",\"draws\":12}", StringComparison.Ordinal);
        Assert.AreEqual(new SessionRandomState(ulong.MaxValue, 12), Decode(seeded).Random);

        void Fails(string random, SnapshotDecodeFailure failure, string path)
        {
            var result = GameSessionSnapshotCodec.TryDecode(GoldenJson.Replace(FreshRandom, ",\"random\":" + random, StringComparison.Ordinal));
            Assert.AreEqual((failure, path), (result.Failure, result.Path), random);
        }
        Fails("null", SnapshotDecodeFailure.WrongType, "$.random");
        Fails("{\"seed\":\"0000000000000000\"}", SnapshotDecodeFailure.MissingField, "$.random.draws");
        Fails("{\"seed\":\"0000000000000000\",\"draws\":0,\"label\":\"x\"}", SnapshotDecodeFailure.UnknownField, "$.random.label");
        Fails("{\"seed\":0,\"draws\":0}", SnapshotDecodeFailure.WrongType, "$.random.seed");
        Fails("{\"seed\":\"0123456789ABCDEF\",\"draws\":0}", SnapshotDecodeFailure.NotCanonical, "$.random.seed");
        Fails("{\"seed\":\"0123456789abcdeg\",\"draws\":0}", SnapshotDecodeFailure.OutOfRange, "$.random.seed");
        Fails("{\"seed\":\"123456789abcdef\",\"draws\":0}", SnapshotDecodeFailure.OutOfRange, "$.random.seed");
        Fails("{\"seed\":\"00123456789abcdef\",\"draws\":0}", SnapshotDecodeFailure.OutOfRange, "$.random.seed");
        Fails("{\"seed\":\"0x23456789abcdef\",\"draws\":0}", SnapshotDecodeFailure.OutOfRange, "$.random.seed");
        Fails("{\"seed\":\"+123456789abcdef\",\"draws\":0}", SnapshotDecodeFailure.OutOfRange, "$.random.seed");
        Fails("{\"seed\":\"0000000000000000\",\"draws\":-1}", SnapshotDecodeFailure.OutOfRange, "$.random.draws");
        Fails("{\"seed\":\"0000000000000000\",\"draws\":1.0}", SnapshotDecodeFailure.OutOfRange, "$.random.draws");
        Fails("{\"seed\":\"0000000000000000\",\"draws\":9223372036854775808}", SnapshotDecodeFailure.OutOfRange, "$.random.draws");
        Fails("{\"seed\":\"0000000000000000\",\"draws\":\"0\"}", SnapshotDecodeFailure.WrongType, "$.random.draws");
    }

    [TestMethod]
    public void RestoreRefusesANegativePositionBuiltByHand()
    {
        using var owner = Seeded();
        var saved = owner.Capture();
        var snapshot = new GameSessionSnapshot(saved.SchemaVersion, saved.Identity, saved.Revision, saved.Clock, saved.Flags,
            saved.Actor, saved.Maps.ToArray(), saved.Story, saved.Party, saved.Hero, saved.Inventory, new(Seed, -1), saved.Life, saved.Reactions, saved.Combat, saved.Events, saved.Lighting);
        var result = GameSessionOwner.Restore(snapshot, RestoreOptions());
        Assert.AreEqual((null, GameSessionFailure.InvalidSnapshot), (result.Owner, result.Failure));
    }

    [TestMethod]
    public void ADisposedSessionNoLongerRolls()
    {
        var owner = Seeded();
        owner.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => owner.Roll("late", 1, 100));
        Assert.ThrowsExactly<ObjectDisposedException>(() => owner.QueryRandom());
        Assert.ThrowsExactly<ObjectDisposedException>(() => owner.CombatRolls);
    }
}
