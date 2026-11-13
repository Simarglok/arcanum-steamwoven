using Arcanum.Simulation.Ai;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>ARC-WORLD-086 (AI slice I7): set no-flee (0x4AF1D0) on the danger record, the setter's rule 2 over it, and its schema-45 bit.</summary>
[TestClass]
public sealed class ExecutorTests
{
    private const string Wolf = "oid:wolf";

    private static GameSessionOwner Owner()
    {
        var result = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    [TestMethod]
    public void NoFleeFirstStopsFleeing()
    {
        // ARC-AI-1513 (0x4AF1D0 runs 0x4AF170 first): an NPC in FLEE goes to NONE through the setter and gets NO_FLEE, in one revision; a stale version changes nothing.
        using var owner = Owner();
        var flee = owner.SetDangerState(owner.Version, new SessionDangerRequest { Entity = Wolf, State = DangerState.Flee, Target = SessionCombatant.Critter("oid:sogg") });
        Assert.AreEqual(GameSessionDisposition.Changed, flee.Disposition);
        Assert.AreEqual(DangerState.Flee, owner.QueryDanger(Wolf).State);
        var revision = owner.Version.Revision;
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.SetNoFlee(new GameSessionVersion(owner.Identity, revision + 1), Wolf).Failure);
        Assert.AreEqual(DangerState.Flee, owner.QueryDanger(Wolf).State, "a refused call writes nothing");
        var set = owner.SetNoFlee(owner.Version, Wolf);
        Assert.AreEqual(GameSessionDisposition.Changed, set.Disposition);
        var danger = owner.QueryDanger(Wolf);
        Assert.AreEqual((DangerState.None, true, revision + 1), (danger.State, danger.Flags.HasFlag(DangerFlags.NoFlee), owner.Version.Revision));
        Assert.AreEqual(DangerSetterRule.None, set.Transition?.Rule, "the setter's step is the stop fleeing's");

        // An NPC that is not fleeing: the state is left alone, only the bit is written.
        var calm = owner.SetNoFlee(owner.Version, "oid:sogg");
        Assert.AreEqual(GameSessionDisposition.Changed, calm.Disposition);
        Assert.IsNull(calm.Transition, "no setter step outside FLEE");
        Assert.AreEqual(DangerState.None, owner.QueryDanger("oid:sogg").State);
    }

    [TestMethod]
    public void NoFleeIsSetForGoodTurnsFlightIntoCombatAndRoundTrips()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionFailure.InvalidDangerTarget, owner.SetNoFlee(owner.Version, "bad id").Failure);
        var revision = owner.Version.Revision;
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.SetNoFlee(new GameSessionVersion(owner.Identity, revision + 1), Wolf).Failure);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetNoFlee(owner.Version, Wolf).Disposition);
        Assert.AreEqual((DangerFlags.NoFlee, revision + 1), (owner.QueryDanger(Wolf).Flags, owner.Version.Revision));
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.SetNoFlee(owner.Version, Wolf).Disposition, "already set");

        var flee = owner.SetDangerState(owner.Version, new SessionDangerRequest { Entity = Wolf, State = DangerState.Flee, Target = SessionCombatant.Critter("oid:sogg") });
        Assert.AreEqual(GameSessionDisposition.Changed, flee.Disposition);
        Assert.AreEqual(DangerState.CombatFocus, owner.QueryDanger(Wolf).State, "setter rule 2 reads the stored bit");
        Assert.IsTrue(owner.QueryDanger(Wolf).Flags.HasFlag(DangerFlags.NoFlee));

        var calm = owner.SetDangerState(owner.Version, new SessionDangerRequest { Entity = Wolf, State = DangerState.None });
        Assert.AreEqual(GameSessionDisposition.Changed, calm.Disposition);
        Assert.IsTrue(owner.QueryDanger(Wolf).Flags.HasFlag(DangerFlags.NoFlee), "kept when the fight ends");

        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"NoFlee\"");
        using var restored = Restore(Decode(text));
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
    }
}
