using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.World.Tests;

/// <summary>ARC-INPUT-1122: the Home key's decision of 0x54EB60 against its reading (pure logic, no original data).</summary>
[TestClass]
public sealed class OriginalHomeKeyTests
{
    [TestMethod]
    public void WithoutAPlayerCharacterNothingHappens()
    {
        foreach (var centred in new[] { false, true })
            foreach (var round in new[] { false, true })
                Assert.AreEqual(HomeKeyTarget.Nothing, OriginalHomeKey.Target(false, centred, round, round));
    }

    [TestMethod]
    public void AViewAwayFromTheCharacterCentresOnTheCharacterWhateverTheRound()
    {
        Assert.AreEqual(HomeKeyTarget.Pc, OriginalHomeKey.Target(true, false, false, false));
        Assert.AreEqual(HomeKeyTarget.Pc, OriginalHomeKey.Target(true, false, true, true));
        Assert.AreEqual(HomeKeyTarget.Pc, OriginalHomeKey.Target(true, false, true, false));
    }

    [TestMethod]
    public void ACentredViewGoesToTheCurrentParticipantOnlyInARound()
    {
        Assert.AreEqual(HomeKeyTarget.RoundParticipant, OriginalHomeKey.Target(true, true, true, true));
        Assert.AreEqual(HomeKeyTarget.Nothing, OriginalHomeKey.Target(true, true, true, false), "a round with no current participant");
        Assert.AreEqual(HomeKeyTarget.Nothing, OriginalHomeKey.Target(true, true, false, false), "no round");
        Assert.AreEqual(HomeKeyTarget.Nothing, OriginalHomeKey.Target(true, true, false, true), "no round");
    }

    [TestMethod]
    public void TheKeyIsHandledInTheModesOfTheFollowerOrderKeysAndNeverInAnothersTurn()
    {
        for (var i = 0; i < InterfaceModes.Count; i++)
        {
            var mode = (InterfaceMode)i;
            Assert.AreEqual(mode is InterfaceMode.Game or InterfaceMode.SpellTarget or InterfaceMode.SkillTarget or InterfaceMode.FollowerOrder, InterfaceModes.FollowerOrderKeys(mode));
            Assert.IsFalse(InterfaceModes.FollowerOrderKeys(mode, anotherHasTheTurn: true));
        }
    }
}
