using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Movement;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-AI-1653 (ledger VBG19; the shown-pose test 0x426E80, the unconceal goal's end node 0x42AFB0 → 0x45EE30): a concealed follower shows the crouch art
/// (animation 5), so its own goal runs stand it up first (the executor's unconceal step); the end node ends its concealment through the session only
/// when its leader is not concealed. Invented open map: a party member B and an observer A. No original data.
/// </summary>
[TestClass]
public sealed class AreaAiHostFollowerUnconcealTests
{
    private static (AreaAiHostRebindTests.Fixture Fixture, AreaAiHost Host) Setup()
    {
        var f = AreaAiHostRebindTests.Open();
        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.ForceJoinParty(f.Session.Version, f.B).Disposition);
        var scene = f.Scene(0, AreaAiHostRebindTests.Sector(0, 0), [AreaAiHostRebindTests.NpcA(f), new AreaAiNpc(f.B, f.RowB, new(40, 10))]);
        return (f, f.Build(scene));
    }

    [TestMethod]
    public void A_follower_of_the_sneaking_hero_shows_the_crouch_and_the_hero_never_does()
    {
        var (f, host) = Setup();
        using var _ = f;
        Assert.IsFalse(host.Mover(f.B).Concealed || host.Mover(f.B).Prowling, "an ordinary follower stands");
        f.Session.ToggleHeroConcealment(f.Session.Version);
        var mover = host.Mover(f.B);
        Assert.IsTrue(mover.Concealed && mover.Prowling, "the flag crouches it: the shown art is animation 5");
        Assert.AreEqual(AiExecutorRules.ConcealedAnimation, host.ShownAnimation(f.B));
        Assert.IsFalse(host.Mover(f.A).Concealed, "another NPC is not touched");
        Assert.IsFalse(host.Mover(AiSchedulerRules.Pc).Concealed || host.Mover(AiSchedulerRules.Pc).Prowling, "the hero's run is the hero host's: no concealed fact");
        f.Session.ToggleHeroConcealment(f.Session.Version);
        Assert.IsFalse(host.Mover(f.B).Concealed || host.Mover(f.B).Prowling, "the clear stands it again");
        Assert.AreEqual(0, host.ShownAnimation(f.B));
    }

    // ARC-RENDER-2539 (0x42EDC0): a flagged critter below prowling expert steps the stealth walk; an unflagged one walks.
    [TestMethod]
    public void A_concealed_follower_below_expert_steps_the_stealth_walk_and_an_ordinary_one_walks()
    {
        var (f, host) = Setup();
        using var _ = f;
        host.BeginStep(f.B, new CellCoord(41, 10), running: false);
        Assert.IsFalse(host.Movers.Of(f.B)!.Sneaking, "an ordinary follower walks");
        f.Session.ToggleHeroConcealment(f.Session.Version);
        var plain = host.BeginStep(f.B, new CellCoord(42, 10), running: false);
        var mover = host.Movers.Of(f.B)!;
        Assert.IsTrue(mover.Sneaking && !mover.Running, "an untrained prowler sneaks");
        Assert.AreEqual(plain, mover.StepMs, "the step lasts the stealth walk's time");
        host.BeginStep(f.B, new CellCoord(43, 10), running: true);
        Assert.IsFalse(host.Movers.Of(f.B)!.Sneaking && host.Movers.Of(f.B)!.Running, "a step is never both");
        f.Session.ToggleHeroConcealment(f.Session.Version);
        host.BeginStep(f.B, new CellCoord(44, 10), running: false);
        Assert.IsFalse(host.Movers.Of(f.B)!.Sneaking, "the flag cleared: the walk again");
    }

    [TestMethod]
    public void The_unconceal_of_a_follower_of_the_sneaking_hero_only_stands_it_up()
    {
        var (f, host) = Setup();
        using var _ = f;
        f.Session.ToggleHeroConcealment(f.Session.Version);
        Assert.IsTrue(host.Mover(f.B).Concealed);
        var revision = f.Session.Version.Revision;
        Assert.AreEqual(1, host.Unconceal(f.B));
        Assert.AreEqual(revision, f.Session.Version.Revision, "the leader is concealed: the session is not touched");
        Assert.IsTrue(f.Session.IsConcealed(SessionCombatant.Critter(f.B)));
        var mover = host.Mover(f.B);
        Assert.IsFalse(mover.Concealed, "it stands, so the next goal run does not unconceal it again");
        Assert.IsTrue(mover.Prowling, "but it still prowls, so its end node crouches it again");
        host.ShowArt(f.B, AiExecutorRules.ConcealedAnimation, 0);
        Assert.IsTrue(host.Mover(f.B).Concealed, "the end node's crouch art is the next run's unconceal test");
    }

    [TestMethod]
    public void The_unconceal_of_a_concealed_follower_whose_hero_is_not_concealed_ends_its_concealment()
    {
        var (f, host) = Setup();
        using var _ = f;
        f.Session.ToggleHeroConcealment(f.Session.Version);
        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.WaitParty(f.Session.Version, f.B).Disposition);
        f.Session.ToggleHeroConcealment(f.Session.Version);
        Assert.IsTrue(f.Session.IsConcealed(SessionCombatant.Critter(f.B)), "a waiting NPC keeps its own state");
        Assert.IsTrue(host.Mover(f.B).Concealed);
        var revision = f.Session.Version.Revision;
        host.Unconceal(f.B);
        Assert.AreEqual(revision + 1, f.Session.Version.Revision);
        Assert.IsFalse(f.Session.IsConcealed(SessionCombatant.Critter(f.B)));
        Assert.IsFalse(host.Mover(f.B).Concealed || host.Mover(f.B).Prowling);
    }

    [TestMethod]
    public void The_unconceal_of_the_hero_changes_nothing()
    {
        var (f, host) = Setup();
        using var _ = f;
        f.Session.ToggleHeroConcealment(f.Session.Version);
        var revision = f.Session.Version.Revision;
        Assert.AreEqual(1, host.Unconceal(AiSchedulerRules.Pc));
        Assert.AreEqual(revision, f.Session.Version.Revision);
        Assert.IsTrue(f.Session.IsConcealed(SessionCombatant.Hero));
    }
}
