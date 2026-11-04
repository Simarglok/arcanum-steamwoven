using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-RENDER-041: the pools of blood the session leaves (ledger BP1–BP9), saved in schema 58. NPC ids, cells and offsets are invented.
[TestClass]
public sealed class BloodPoolStateTests
{
    private const string Wolf = "oid:wolf";
    private static readonly SessionMapId Map = new(79);

    private static GameSessionOwner Owner()
    {
        var result = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    private static void Kill(GameSessionOwner owner, string npc) =>
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Kill(owner.Version, SessionLifeTarget.Critter(npc, new LifeFacts(), default)).Disposition);

    private static void Advance(GameSessionOwner owner, long ms) => Assert.AreEqual(ClockMutationDisposition.Changed,
        owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, ms, ClockAdvanceReason.OrdinaryAdvance).Disposition);

    private static SessionBloodPoolResult Leave(GameSessionOwner owner, string critter, BloodPoolFacts facts = default) =>
        owner.LeaveBloodPool(owner.Version, critter, Map, new(105, 200), 12, -7, facts);

    [TestMethod]
    public void ADeadCritterLeavesOnePoolPerDeath()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionFailure.InvalidLifeTarget, Leave(owner, Wolf).Failure, "an NPC without a recorded death leaves none");
        Kill(owner, Wolf);
        var revision = owner.Version.Revision;
        var left = Leave(owner, Wolf);
        Assert.AreEqual(GameSessionDisposition.Changed, left.Disposition);
        Assert.AreEqual(revision + 1, owner.Version.Revision);
        var now = owner.QueryClockSnapshot().GameTimestampMs;
        Assert.AreEqual(new SessionBloodPool("pool:1", Wolf, now, Map, new CellCoord(105, 200), 12, -7, now), left.Pool);
        CollectionAssert.AreEqual(new[] { left.Pool }, owner.QueryBloodPools(Map).ToArray());
        Assert.AreEqual(0, owner.QueryBloodPools(new(80)).Count);

        // A repeated call answers the pool this death left.
        var again = Leave(owner, Wolf);
        Assert.AreEqual(GameSessionDisposition.NoChange, again.Disposition);
        Assert.AreEqual(left.Pool, again.Pool);
        Assert.AreEqual(revision + 1, owner.Version.Revision);

        // The hero leaves one once it has died.
        Assert.AreEqual(GameSessionFailure.InvalidLifeTarget, Leave(owner, SessionEventKinds.PlayerEntity).Failure);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Kill(owner.Version, SessionLifeTarget.Hero).Disposition);
        Assert.AreEqual("pool:2", Leave(owner, SessionEventKinds.PlayerEntity).Pool!.Entity);
    }

    [TestMethod]
    public void TheRulesRefusalLeavesNoneAndChangesNothing()
    {
        using var owner = Owner();
        Kill(owner, Wolf);
        var revision = owner.Version.Revision;
        foreach (var (facts, refusal) in new[]
        {
            (new BloodPoolFacts(0, 0, 0, true, false), BloodPoolRefusal.TileBlocking),
            (new BloodPoolFacts(0, 0, 0, false, true), BloodPoolRefusal.ViolenceFilter),
            (new BloodPoolFacts(BloodPoolRules.UndeadFlag, 0, 0, false, false), BloodPoolRefusal.UndeadOrMechanical),
            (new BloodPoolFacts(0, BloodPoolRules.ElementalFlag2, 0, false, false), BloodPoolRefusal.Elemental),
            (new BloodPoolFacts(0, 0, BloodPoolRules.StonedSpellFlag, false, false), BloodPoolRefusal.Stoned),
        })
        {
            var result = Leave(owner, Wolf, facts);
            Assert.AreEqual(GameSessionDisposition.NoChange, result.Disposition);
            Assert.AreEqual(refusal, result.Refusal);
            Assert.IsNull(result.Pool);
        }
        Assert.AreEqual(revision, owner.Version.Revision);
        Assert.AreEqual(0, owner.QueryBloodPools(Map).Count);
    }

    [TestMethod]
    public void BadInputIsRefused()
    {
        using var owner = Owner();
        Kill(owner, Wolf);
        var stale = owner.Version;
        Kill(owner, "oid:bear");
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.LeaveBloodPool(stale, Wolf, Map, new(1, 1), 0, 0, default).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidMap, owner.LeaveBloodPool(owner.Version, Wolf, new(0), new(1, 1), 0, 0, default).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidMap, owner.LeaveBloodPool(owner.Version, Wolf, Map, new(-1, 1), 0, 0, default).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidLifeTarget, owner.LeaveBloodPool(owner.Version, "bad id", Map, new(1, 1), 0, 0, default).Failure);
    }

    [TestMethod]
    public void APoolLastsTwoGameDaysAndADecayedOneLeavesItsPlace()
    {
        using var owner = Owner();
        Kill(owner, Wolf);
        Assert.AreEqual(GameSessionDisposition.Changed, Leave(owner, Wolf).Disposition);
        Advance(owner, BloodPoolRules.ObjectDecayDelayMs - 1);
        Assert.AreEqual(1, owner.QueryBloodPools(Map).Count);
        Assert.AreEqual(SessionObjectPlace.Ground, owner.QueryObject("pool:1")!.Place, "ARC-DEV-084: a pool lies on the ground");
        Advance(owner, 1);
        Assert.AreEqual(0, owner.QueryBloodPools(Map).Count);
        Assert.AreEqual(SessionObjectPlace.Destroyed, owner.QueryObject("pool:1")!.Place, "a decayed pool is gone");
        Assert.AreEqual(1, owner.Capture().Inventory.BloodPools.Count, "kept until the next pool is left");
        Kill(owner, "oid:bear");
        var next = Leave(owner, "oid:bear");
        Assert.AreEqual("pool:2", next.Pool!.Entity, "the decayed pool is dropped, its number is not given again (MIG6, TM1: an id is never reassigned)");
        Assert.AreEqual(2, owner.Capture().Inventory.LastPoolNumber);
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        Assert.AreEqual(2, restored.Capture().Inventory.LastPoolNumber, "the mark survives a save and a load");
        CollectionAssert.AreEqual(new[] { next.Pool }, owner.Capture().Inventory.BloodPools.ToArray());
    }

    [TestMethod]
    public void PoolsAreSavedAndRestored()
    {
        using var owner = Owner();
        Kill(owner, Wolf);
        Assert.AreEqual(GameSessionDisposition.Changed, Leave(owner, Wolf).Disposition);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"bloodPools\":[{\"entity\":\"pool:1\",\"critter\":\"oid:wolf\",\"deathTimeMs\":");
        StringAssert.Contains(text, "\"map\":79,\"x\":105,\"y\":200,\"offsetX\":12,\"offsetY\":-7,\"createdMs\":");
        using var restored = Restore(Decode(text));
        CollectionAssert.AreEqual(owner.QueryBloodPools(Map).ToArray(), restored.QueryBloodPools(Map).ToArray());
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));

        GameSessionFailure Restored(string changed) => GameSessionOwner.Restore(Decode(changed), RestoreOptions()).Failure;
        var pool = text[text.IndexOf("{\"entity\":\"pool:1\"", StringComparison.Ordinal)..];
        pool = pool[..(pool.IndexOf('}') + 1)];
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, Restored(text.Replace(pool, pool.Replace("pool:1", "pool:01"))));
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, Restored(text.Replace(pool, pool + "," + pool)), "one entity once");
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, Restored(text.Replace(pool, pool + "," + pool.Replace("pool:1", "pool:2"))), "one pool per death");
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, Restored(text.Replace(pool, pool.Replace("\"critter\":\"oid:wolf\"", "\"critter\":\"-\""))));
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, Restored(text.Replace(pool, pool.Replace("\"createdMs\":", "\"createdMs\":9"))), "not after the clock");
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, Restored(text.Replace(pool, pool.Replace("\"deathTimeMs\":", "\"deathTimeMs\":9"))), "not before the death");
        Assert.AreEqual(SnapshotDecodeFailure.UnknownField, GameSessionSnapshotCodec.TryDecode(text.Replace(pool, pool.Replace("{\"entity\"", "{\"x2\":1,\"entity\""))).Failure);

        // A live pool numbered at the integer bound takes no successor (its number would not restore).
        using var highest = Restore(Decode(text.Replace(pool, pool.Replace("pool:1", "pool:2147483647"))));
        Kill(highest, "oid:bear");
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, Leave(highest, "oid:bear").Failure);
    }
}
