using Arcanum.Scripting.Dialog;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Skills;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The hero's fate points in the session (ARC-WORLD-054, ledger FT1–FT8).
[TestClass]
public sealed class FateStateTests
{
    private static GameSessionOwner Owner(int points = 0)
    {
        var owner = CreateWith(HalfElf());
        for (var i = 0; i < points; i++) Assert.AreEqual(GameSessionDisposition.Changed, owner.GrantHeroFatePoint(owner.Version).Disposition);
        return owner;
    }

    private static void Refused(SessionFateResult result, GameSessionFailure failure, GameSessionOwner owner, long revision)
    {
        Assert.AreEqual((GameSessionDisposition.Rejected, failure), (result.Disposition, result.Failure));
        Assert.AreEqual(revision, owner.Version.Revision);
    }

    [TestMethod]
    public void ANewHeroHasNoFateAndAHerolessSessionRefuses()
    {
        using var owner = Owner();
        Assert.AreEqual(FateState.None, owner.QueryHeroFate());
        Refused(owner.ArmHeroFate(owner.Version, FateKind.CriticalHit), GameSessionFailure.FateRefused, owner, 0);
        Assert.AreEqual(FateStatus.NoPoints, owner.ArmHeroFate(owner.Version, FateKind.CriticalHit).Change!.Value.Status);
        using var heroless = GameSessionOwner.Create(Descriptor()).Owner!;
        Assert.IsNull(heroless.QueryHeroFate());
        Assert.IsFalse(heroless.HeroFateArmed(FateKind.Heal));
        Refused(heroless.GrantHeroFatePoint(heroless.Version), GameSessionFailure.FateUnavailable, heroless, 0);
        Refused(heroless.ArmHeroFate(heroless.Version, FateKind.Heal), GameSessionFailure.FateUnavailable, heroless, 0);
        Refused(owner.ArmHeroFate(owner.Version, (FateKind)12), GameSessionFailure.InvalidFateKind, owner, 0);
        Refused(owner.GrantHeroFatePoint(new(owner.Identity, 7)), GameSessionFailure.StaleVersion, owner, 0);
    }

    [TestMethod]
    public void ArmUndoUseAndGrantEachTakeOneRevision()
    {
        using var owner = Owner(points: 2);
        Assert.AreEqual(new FateState(2, 0), owner.QueryHeroFate());
        Assert.AreEqual(2, owner.Version.Revision);
        var armed = owner.ArmHeroFate(owner.Version, FateKind.Heal);
        Assert.AreEqual((GameSessionDisposition.Changed, FateStatus.Armed, 3L), (armed.Disposition, armed.Change!.Value.Status, owner.Version.Revision));
        Assert.IsTrue(owner.HeroFateArmed(FateKind.Heal));
        Assert.AreEqual(FateKind.Heal, owner.HeroSkillFate(Skill.Heal));
        Assert.IsNull(owner.HeroSkillFate(Skill.Repair));
        Assert.IsNull(owner.HeroSkillFate(Skill.Melee));
        Refused(owner.ArmHeroFate(owner.Version, FateKind.Heal), GameSessionFailure.FateRefused, owner, 3);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.UndoHeroFate(owner.Version, FateKind.Heal).Disposition);
        Assert.AreEqual(new FateState(2, 0), owner.QueryHeroFate());
        Refused(owner.UndoHeroFate(owner.Version, FateKind.Heal), GameSessionFailure.FateRefused, owner, 4);
        owner.ArmHeroFate(owner.Version, FateKind.SpellAtMaximum);
        var used = owner.UseHeroFate(owner.Version, FateKind.SpellAtMaximum);
        Assert.AreEqual((GameSessionDisposition.Changed, FateStatus.Used), (used.Disposition, used.Change!.Value.Status));
        Assert.AreEqual(new FateState(1, 0), owner.QueryHeroFate(), "used, not refunded");
        var idle = owner.UseHeroFate(owner.Version, FateKind.SpellAtMaximum);
        Assert.AreEqual((GameSessionDisposition.NoChange, FateStatus.NotArmed, 6L), (idle.Disposition, idle.Change!.Value.Status, owner.Version.Revision));
    }

    [TestMethod]
    public void PointsStopAtAHundred()
    {
        using var owner = Owner(points: FateRules.MaxPoints);
        var full = owner.GrantHeroFatePoint(owner.Version);
        Assert.AreEqual((GameSessionDisposition.NoChange, 100), (full.Disposition, owner.QueryHeroFate()!.Value.Points));
        owner.ArmHeroFate(owner.Version, FateKind.Repair);
        owner.GrantHeroFatePoint(owner.Version);
        owner.UndoHeroFate(owner.Version, FateKind.Repair);
        Assert.AreEqual(new FateState(100, 0), owner.QueryHeroFate(), "the refund at 100 is lost, as the stat setter clamps it");
    }

    [TestMethod]
    public void TheFullHealSpendsAPointAndClearsTheDamageInOneRevision()
    {
        using var owner = Owner(points: 1);
        Assert.AreEqual(GameSessionDisposition.Changed,
            owner.ApplyDamage(owner.Version, SessionLifeTarget.Hero, new LifeDamage { HitPoints = 5, Fatigue = 4, Poison = 120 }).Disposition);
        var before = owner.Version.Revision;
        var healed = owner.ArmHeroFate(owner.Version, FateKind.FullHeal);
        Assert.AreEqual((GameSessionDisposition.Changed, FateStatus.UsedAtOnce, before + 1), (healed.Disposition, healed.Change!.Value.Status, owner.Version.Revision));
        var state = owner.QueryHeroLife()!.Record.State;
        Assert.AreEqual((0, 0, 0), (state.HitPointDamage, state.FatigueDamage, state.PoisonLevel));
        Assert.AreEqual(FateState.None, owner.QueryHeroFate());
        Assert.IsFalse(owner.HeroFateArmed(FateKind.FullHeal));
    }

    [TestMethod]
    public void TheFullHealOfADeadHeroSpendsNothing()
    {
        using var owner = Owner(points: 1);
        owner.Kill(owner.Version, SessionLifeTarget.Hero);
        var revision = owner.Version.Revision;
        var refused = owner.ArmHeroFate(owner.Version, FateKind.FullHeal);
        Refused(refused, GameSessionFailure.FateRefused, owner, revision);
        Assert.IsTrue(refused.Heal!.Events.HasFlag(LifeEvents.AlreadyDead));
        Assert.AreEqual(new FateState(1, 0), owner.QueryHeroFate());
    }

    [TestMethod]
    public void FateRoundTripsThroughTheSnapshotAndABadValueIsRefused()
    {
        using var owner = Owner(points: 3);
        owner.ArmHeroFate(owner.Version, FateKind.CriticalMiss);
        owner.ArmHeroFate(owner.Version, FateKind.DisarmTraps);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"fatePoints\":1,\"armedFates\":1028,\"background\":0,\"backgroundDescription\":0}");
        using var restored = Restore(Decode(text));
        Assert.AreEqual(new FateState(1, 1028), restored.QueryHeroFate());
        foreach (var bad in new[] { "\"fatePoints\":101,", "\"fatePoints\":-1," })
        {
            var restore = GameSessionOwner.Restore(Decode(text.Replace("\"fatePoints\":1,", bad)), RestoreOptions());
            Assert.AreEqual(GameSessionFailure.InvalidHero, restore.Failure, bad);
        }
        var stray = GameSessionOwner.Restore(Decode(text.Replace("\"armedFates\":1028", "\"armedFates\":2048")), RestoreOptions());
        Assert.AreEqual(GameSessionFailure.InvalidHero, stray.Failure);
    }

    [TestMethod]
    public void TheDialogPortGrantsAPoint()
    {
        using var owner = Owner();
        var port = new SessionDialogFatePort(owner, new DialogParticipantId(1));
        Assert.AreEqual(DialogCommandResult.Applied, port.Execute(new FatePointCommand(new DialogParticipantId(1))));
        Assert.AreEqual(1, owner.QueryHeroFate()!.Value.Points);
        Assert.AreEqual(DialogCommandResult.Rejected, port.Execute(new FatePointCommand(new DialogParticipantId(2))));
        using var heroless = GameSessionOwner.Create(Descriptor()).Owner!;
        Assert.AreEqual(DialogCommandResult.Rejected, new SessionDialogFatePort(heroless, new DialogParticipantId(1)).Execute(new FatePointCommand(new DialogParticipantId(1))));
    }
}
