using Arcanum.Simulation.Items;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Items;

/// <summary>ARC-WORLD-062: the original's decay test (0x468010) and firing (0x467FC0).</summary>
[TestClass]
public sealed class ItemDecayRulesTests
{
    [TestMethod]
    public void OnlyALooseItemOrOneInAJunkPileDecaysWithoutTheFlagWhileDecayIsOn()
    {
        Assert.IsTrue(ItemDecayRules.Decays(false, 0, ItemDecayPlace.Loose));
        Assert.IsTrue(ItemDecayRules.Decays(false, 0x20, ItemDecayPlace.JunkPile), "other item flags do not matter");
        Assert.IsFalse(ItemDecayRules.Decays(false, 0, ItemDecayPlace.Held));
        Assert.IsFalse(ItemDecayRules.Decays(false, ItemDecayRules.NoDecayFlag, ItemDecayPlace.Loose));
        Assert.IsFalse(ItemDecayRules.Decays(true, 0, ItemDecayPlace.Loose), "the editor's switch");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => ItemDecayRules.Decays(false, 0, (ItemDecayPlace)9));
    }

    [TestMethod]
    public void TheFiringDestroysRetriesWhileTheLootWindowShowsAContainerOrKeeps()
    {
        Assert.AreEqual(ItemDecayStep.Destroy, ItemDecayRules.Fire(false, true));
        Assert.AreEqual(ItemDecayStep.Retry, ItemDecayRules.Fire(true, true));
        Assert.AreEqual(ItemDecayStep.Keep, ItemDecayRules.Fire(false, false));
        Assert.AreEqual(ItemDecayStep.Keep, ItemDecayRules.Fire(true, false), "the retry tests the item again (0x468090)");
    }
}
