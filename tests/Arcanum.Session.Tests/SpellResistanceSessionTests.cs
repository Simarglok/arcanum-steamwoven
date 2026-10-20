using Arcanum.Simulation.Character;
using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The session side of the resistance (ARC-MAGIC-005): labelled seeded rolls, the hero's fate save, the rule-mode profile.
[TestClass]
public sealed class SpellResistanceSessionTests
{
    private static readonly SpellResistRecord Charm = new(10, false, false, true, 0, SpellResistanceRules.Willpower, 2);
    private static readonly SpellResistTarget Hero = new(false, SpellTargetKind.Pc, false, 30, 0, 8, false);
    private static readonly SpellResistRun Begin = new(true, false, false);

    [TestMethod]
    public void MagicRollsAreLabelledSessionRollsThatMoveTheSavedPosition()
    {
        using var owner = Create();
        var before = owner.QueryRandom();
        var value = owner.SpellRolls.Roll(SpellRollPurpose.SaveD20, 1, 20);
        Assert.IsTrue(value is >= 1 and <= 20);
        Assert.AreEqual(before.Draws + 1, owner.QueryRandom().Draws);
        Assert.AreEqual("magic.FailChanceD20", SessionSpellRolls.Label(SpellRollPurpose.FailChanceD20));
        Assert.AreEqual(0, owner.Version.Revision, "a roll does not move the revision");

        using var replay = Create();
        Assert.AreEqual(value, replay.SpellRolls.Roll(SpellRollPurpose.SaveD20, 1, 20), "the same seed and position give the same roll");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => replay.SpellRolls.Roll((SpellRollPurpose)9, 1, 20));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => replay.SpellRolls.Roll(SpellRollPurpose.SaveD20, 20, 1));
    }

    [TestMethod]
    public void TheHerosArmedSaveAgainstMagickIsUsedByTheResistance()
    {
        using var owner = CreateWith(HalfElf());
        Assert.AreEqual(GameSessionDisposition.Changed, owner.GrantHeroFatePoint(owner.Version).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ArmHeroFate(owner.Version, FateKind.SaveAgainstMagick).Disposition);
        var revision = owner.Version.Revision;
        var result = SpellResistanceRules.Resist(Charm, Hero, Begin, owner.SpellRolls, owner.HeroMagickSave, owner.Rules.Magic);
        // MG13: margin 10, display 100, then the save (S = 8 + 2 = 10) still rolls the session's d20: a success adds S − d20.
        Assert.IsTrue(result.DisplayMargin == 100 ? result.Margin == 10 : result.Margin == result.DisplayMargin && result.Margin >= 10,
            $"margin {result.Margin}, display {result.DisplayMargin}");
        Assert.IsFalse(owner.HeroFateArmed(FateKind.SaveAgainstMagick), "the fate was used");
        Assert.AreEqual(revision + 1, owner.Version.Revision, "using the fate is one revision");
        Assert.IsFalse(owner.HeroMagickSave.ConsumeSaveAgainstMagick(), "not armed any more");

        using var heroless = Create();
        Assert.IsFalse(heroless.HeroMagickSave.ConsumeSaveAgainstMagick());
    }

    [TestMethod]
    public void TheMagicProfileFollowsTheRuleMode()
    {
        Assert.AreSame(MagicAnomalyProfile.Steamwoven, GameRules.Steamwoven.Magic);
        Assert.AreSame(MagicAnomalyProfile.Original, GameRules.Original.Magic);
        Assert.IsTrue(GameRules.Original.Magic.SaveSubtractsResistance);
        Assert.IsFalse(GameRules.Steamwoven.Magic.SaveSubtractsResistance);
        Assert.AreEqual(GameRules.Steamwoven.Name, GameRules.Steamwoven.Magic.Name);
        Assert.AreEqual(GameRules.Original.Name, GameRules.Original.Magic.Name);
    }
}
