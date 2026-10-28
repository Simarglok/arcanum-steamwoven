using Arcanum.Simulation.Items;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Items;

/// <summary>ARC-WORLD-093: the original's restock event (0x4640C0, 0x463C60, 0x463E20, 0x463B30, 0x4641A0).</summary>
[TestClass]
public sealed class RestockRulesTests
{
    private static RestockHolder Npc(int source = 5, bool dead = false, bool pcLeader = false) => new(RestockHolderKind.Npc, source, dead, pcLeader, 0, false);

    private static RestockHolder Chest(int source = 7, uint flags = 0, bool onScreen = false) => new(RestockHolderKind.Container, source, false, false, flags, onScreen);

    private sealed class Rolls(params int[] values) : IRestockRolls
    {
        private readonly Queue<int> _values = new(values);
        public List<(RestockRoll Purpose, int Min, int Max)> Asked { get; } = [];

        public int Roll(RestockRoll purpose, int min, int max)
        {
            Asked.Add((purpose, min, max));
            return _values.Dequeue();
        }
    }

    [TestMethod]
    public void OnlyAnNpcOrAContainerHasAnInventorySource()
    {
        Assert.AreEqual(5, RestockRules.InventorySource(Npc()));
        Assert.AreEqual(7, RestockRules.InventorySource(Chest()));
        Assert.AreEqual(0, RestockRules.InventorySource(new(RestockHolderKind.Other, 9, false, false, 0, false)), "a PC or any other object");
    }

    [TestMethod]
    public void TheDelayIsTheDrawOfTwelveToTwentyFourHoursAndAQuarterInMultiplayer()
    {
        Assert.AreEqual((RestockRules.MinDelayMs, RestockRules.MaxDelayMs), (43_200_000L, 86_400_000L));
        Assert.AreEqual(50_000_001L, RestockRules.Delay(50_000_001, false));
        Assert.AreEqual(12_500_000L, RestockRules.Delay(50_000_001, true), "shifted right twice");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => RestockRules.Delay(RestockRules.MinDelayMs - 1, false));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => RestockRules.Delay(RestockRules.MaxDelayMs + 1, false));
    }

    [TestMethod]
    public void AnNpcRegeneratesWhenItHasASourceIsAliveAndHasNoPcLeader()
    {
        Assert.AreEqual(new RestockDecision(RestockStep.Regenerate, false), RestockRules.Fire(Npc(), null));
        Assert.AreEqual(RestockStep.None, RestockRules.Fire(Npc(source: 0), null).Step);
        Assert.AreEqual(RestockStep.None, RestockRules.Fire(Npc(dead: true), null).Step);
        Assert.AreEqual(RestockStep.None, RestockRules.Fire(Npc(pcLeader: true), null).Step);
        Assert.AreEqual(RestockStep.None, RestockRules.Fire(new(RestockHolderKind.Other, 5, false, false, 0, false), null).Step);
        Assert.AreEqual(RestockStep.Regenerate, RestockRules.Fire(Npc() with { ContainerFlags = 0x400, OnScreen = true }, null).Step,
            "an NPC's screen place and flags do not matter");
    }

    [TestMethod]
    public void AContainerRegeneratesAloneWithItsFlagIsRescheduledOnScreenAndOtherwiseNeedsItsMerchant()
    {
        Assert.AreEqual(RestockStep.None, RestockRules.Fire(Chest(source: 0, flags: RestockRules.RegeneratesAloneFlag), Npc()).Step);
        Assert.AreEqual(RestockStep.Regenerate, RestockRules.Fire(Chest(flags: RestockRules.RegeneratesAloneFlag, onScreen: true), null).Step);
        Assert.AreEqual(RestockStep.Reschedule, RestockRules.Fire(Chest(onScreen: true), Npc()).Step);
        Assert.AreEqual(RestockStep.None, RestockRules.Fire(Chest(), null).Step, "no merchant near");
        Assert.AreEqual(RestockStep.None, RestockRules.Fire(Chest(), Npc(dead: true)).Step);
        Assert.AreEqual(RestockStep.None, RestockRules.Fire(Chest(), Npc(pcLeader: true)).Step);
        Assert.AreEqual(new RestockDecision(RestockStep.RegenerateMerchantAndContainer, true), RestockRules.Fire(Chest(), Npc()));
        Assert.AreEqual(new RestockDecision(RestockStep.RegenerateMerchantAndContainer, false), RestockRules.Fire(Chest(), Npc(source: 0)),
            "a merchant without a source of its own leaves only the container to regenerate (the four installed merchants)");
        Assert.AreEqual(RestockStep.RegenerateMerchantAndContainer, RestockRules.Fire(Chest(flags: RestockRules.GeneratedOnceFlag | 0x9), Npc(source: 0)).Step,
            "other container flags do not matter");
        Assert.ThrowsExactly<ArgumentException>(() => RestockRules.Fire(Chest(), Chest()));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => RestockRules.Fire(new((RestockHolderKind)9, 5, false, false, 0, false), null));
    }

    [TestMethod]
    public void OnlyAPersistentItemStaysThroughTheClearing()
    {
        Assert.IsTrue(RestockRules.Keeps(RestockRules.PersistentItemFlag));
        Assert.IsTrue(RestockRules.Keeps(RestockRules.PersistentItemFlag | 0x1));
        Assert.IsFalse(RestockRules.Keeps(0));
        Assert.IsFalse(RestockRules.Keeps(0x20000 | 0x1), "the no-decay and identified flags do not keep an item");
    }

    [TestMethod]
    public void TheGenerationDrawsTheCoinsThenOnePercentPerLineAndCreatesAtOrBelowTheRate()
    {
        var set = new RestockSet(10, 20, [new(50, 6029), new(50, 6030), new(100, 6031), new(0, 6032), new(-5, 6033), new(150, 6034)]);
        var rolls = new Rolls(15, 50, 51, 100, 1, 1, 100);
        var loot = RestockRules.Generate(set, rolls);
        Assert.AreEqual(15, loot.Coins);
        CollectionAssert.AreEqual(new[] { 6029, 6031, 6034 }, loot.BasicPrototypes.ToArray());
        CollectionAssert.AreEqual(new[] { (RestockRoll.Coins, 10, 20) }.Concat(Enumerable.Repeat((RestockRoll.Line, 1, 100), 6)).ToArray(),
            rolls.Asked.ToArray(), "every line draws, whatever its rate");
    }

    [TestMethod]
    public void ReversedCoinBoundsAreSwappedAndNoPileIsMadeAtZeroOrBelow()
    {
        var reversed = new Rolls(12);
        Assert.AreEqual(12, RestockRules.Generate(new RestockSet(20, 10, []), reversed).Coins);
        Assert.AreEqual((RestockRoll.Coins, 10, 20), reversed.Asked.Single());
        Assert.AreEqual(0, RestockRules.Generate(new RestockSet(0, 0, []), new Rolls(0)).Coins);
        Assert.AreEqual(0, RestockRules.Generate(new RestockSet(-10, -5, []), new Rolls(-7)).Coins, "negative coins are kept by the loot line and give nothing");
        Assert.ThrowsExactly<InvalidOperationException>(() => RestockRules.Generate(new RestockSet(1, 2, []), new Rolls(3)));
        Assert.ThrowsExactly<InvalidOperationException>(() => RestockRules.Generate(new RestockSet(1, 1, [new(50, 6029)]), new Rolls(1, 0)));
    }
}
