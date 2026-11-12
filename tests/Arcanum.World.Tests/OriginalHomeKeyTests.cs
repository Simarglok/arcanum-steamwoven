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
        var all = Enumerable.Range(0, InterfaceModes.Count).Select(i => (InterfaceMode)i).ToArray();
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 18 }, all.Where(m => InterfaceModes.FollowerOrderKeys(m)).Select(m => (int)m).ToArray());
        Assert.IsTrue(all.All(m => !InterfaceModes.FollowerOrderKeys(m, anotherHasTheTurn: true)));
    }
}
