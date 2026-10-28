using Arcanum.Simulation.Ai;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>ARC-WORLD-088 (AI slice I8): the look-for and grenade bits on the danger record (schema 47) and the redirect's focus
/// and who-hit-me-last writes (0x4A94C0).</summary>
[TestClass]
public sealed class SupportTests
{
    private const string Wolf = "oid:wolf";

    private static GameSessionOwner Owner()
    {
        var result = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    [TestMethod]
    public void SupportBitsAreSetClearedCheckedAndRoundTrip()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionFailure.InvalidDangerTarget, owner.SetAiSupportBits(owner.Version, "bad id", DangerFlags.LookForAmmo).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidDangerFacts, owner.SetAiSupportBits(owner.Version, Wolf, DangerFlags.Fighting).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidDangerFacts, owner.ClearAiSupportBits(owner.Version, Wolf, DangerFlags.CheckWeapon).Failure);
        var revision = owner.Version.Revision;
        Assert.AreEqual(GameSessionFailure.StaleVersion,
            owner.SetAiSupportBits(new GameSessionVersion(owner.Identity, revision + 1), Wolf, DangerFlags.LookForAmmo).Failure);

        var outOfAmmo = DangerFlags.LookForAmmo | DangerFlags.LookForWeapon | DangerFlags.CheckWeapon;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetAiSupportBits(owner.Version, Wolf, outOfAmmo | DangerFlags.LookForArmor).Disposition);
        Assert.AreEqual((outOfAmmo | DangerFlags.LookForArmor, revision + 1), (owner.QueryDanger(Wolf).Flags, owner.Version.Revision));
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.SetAiSupportBits(owner.Version, Wolf, DangerFlags.LookForAmmo).Disposition, "already set");

        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"LookForAmmo\",\"LookForWeapon\",\"LookForArmor\"");
        using var restored = Restore(Decode(text));
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));

        Assert.AreEqual(GameSessionDisposition.Changed, owner.ClearAiSupportBits(owner.Version, Wolf, DangerFlags.LookForAmmo | DangerFlags.LookForArmor).Disposition);
        Assert.AreEqual(DangerFlags.LookForWeapon | DangerFlags.CheckWeapon, owner.QueryDanger(Wolf).Flags);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.ClearAiSupportBits(owner.Version, Wolf, DangerFlags.CheckGrenade).Disposition, "clear already");
    }

    [TestMethod]
    public void RedirectWritesFocusAndHitByWhateverTheState()
    {
        using var owner = Owner();
        var sogg = SessionCombatant.Critter("oid:sogg");
        Assert.AreEqual(GameSessionFailure.InvalidDangerTarget, owner.RedirectDanger(owner.Version, "bad id", sogg).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidDangerTarget, owner.RedirectDanger(owner.Version, Wolf, SessionCombatant.Critter(Wolf)).Failure, "never its own focus");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RedirectDanger(owner.Version, Wolf, sogg).Disposition);
        var danger = owner.QueryDanger(Wolf);
        Assert.AreEqual((DangerState.None, sogg, sogg), (danger.State, danger.Focus, danger.HitBy), "the state is not set");
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.RedirectDanger(owner.Version, Wolf, sogg).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RedirectDanger(owner.Version, Wolf, SessionCombatant.Hero).Disposition);
        Assert.AreEqual(SessionCombatant.Hero, owner.QueryDanger(Wolf).HitBy);
    }
}
