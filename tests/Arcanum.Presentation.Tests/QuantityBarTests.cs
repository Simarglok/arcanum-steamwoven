using Arcanum.Presentation.Hud;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Presentation.Tests;

// ARC-UI-029: the quantity window's rules (ledger QW1, QW3, QW4).
[TestClass]
public sealed class QuantityBarTests
{
    [TestMethod]
    public void Only_a_gold_or_ammunition_stack_of_more_than_one_unit_asks_outside_a_put_into_the_looted_holder_and_take_all()
    {
        Assert.IsNull(QuantityBar.Ask(QuantityBar.AmmoType, 30, intoLootedHolder: false, takeAll: false), "a drop or a take asks");
        Assert.IsNull(QuantityBar.Ask(QuantityBar.GoldType, 2, false, false));
        Assert.IsNull(QuantityBar.Ask(QuantityBar.GoldType, 0, false, false), "only exactly one unit moves at once");
        Assert.AreEqual(1, QuantityBar.Ask(QuantityBar.AmmoType, 1, false, false));
        Assert.AreEqual(1, QuantityBar.Ask(5, 1, false, false), "an ordinary item");
        Assert.AreEqual(1, QuantityBar.Ask(5, 7, true, true), "an ordinary item never asks");
        Assert.AreEqual(30, QuantityBar.Ask(QuantityBar.AmmoType, 30, intoLootedHolder: true, takeAll: false));
        Assert.AreEqual(250, QuantityBar.Ask(QuantityBar.GoldType, 250, intoLootedHolder: false, takeAll: true));
    }

    [TestMethod]
    public void The_window_opens_at_zero_and_its_presses_stay_within_the_stack()
    {
        var bar = QuantityBar.Open(25);
        Assert.AreEqual(new QuantityBar(0, 25), bar);
        Assert.AreEqual(0, bar.Apply(QuantityAction.Down).Count, "never below 0");
        Assert.AreEqual(1, bar.Apply(QuantityAction.Up).Count);
        Assert.AreEqual(25, bar.Apply(QuantityAction.All).Count);
        Assert.AreEqual(25, bar.Apply(QuantityAction.All).Apply(QuantityAction.Up).Count, "never above the maximum");
        Assert.AreEqual(24, bar.Apply(QuantityAction.All).Apply(QuantityAction.Down).Count);

        var typed = bar.Apply(QuantityAction.Digit, 1).Apply(QuantityAction.Digit, 2);
        Assert.AreEqual(12, typed.Count);
        Assert.AreEqual(25, typed.Apply(QuantityAction.Digit, 7).Count, "127 is clamped to the maximum");
        Assert.AreEqual(1, typed.Apply(QuantityAction.Erase).Count);
        Assert.AreEqual(0, typed.Apply(QuantityAction.Erase).Apply(QuantityAction.Erase).Apply(QuantityAction.Erase).Count);
        Assert.AreEqual(0, bar.Apply(QuantityAction.Digit, 0).Count, "a leading zero stays 0");

        var big = new QuantityBar(int.MaxValue / 10, int.MaxValue);
        Assert.AreEqual(int.MaxValue, big.Apply(QuantityAction.Digit, 9).Count, "no 32-bit wrap");
        Assert.Throws<ArgumentOutOfRangeException>(() => bar.Apply(QuantityAction.Digit, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => QuantityBar.Open(-1));
    }
}
