using Arcanum.Presentation.Display;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Presentation.Tests;

// ARC-UI-1737 (ledger OH1): the brightness setting as the display ramp's factor.
[TestClass]
public sealed class BrightnessRulesTests
{
    [TestMethod]
    public void TheFactorIsOnePlusATenthPerStepFromOneToOnePointNine()
    {
        Assert.AreEqual(1.0, BrightnessRules.Factor(0));
        Assert.AreEqual(1.5, BrightnessRules.Factor(5), 1e-12);
        Assert.AreEqual(1.9, BrightnessRules.Factor(9), 1e-12);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => BrightnessRules.Factor(10), "the factor 2.0 is the original's out-of-range refusal");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => BrightnessRules.Factor(-1));
    }

    [TestMethod]
    public void ZeroLeavesEveryChannelAloneAndHigherValuesScaleThemCappedAtWhite()
    {
        Assert.IsFalse(BrightnessRules.Active(0));
        for (var channel = 0; channel <= 255; channel++) Assert.AreEqual(channel, BrightnessRules.Apply(channel, 0), "brightness 0 is today's picture");
        Assert.AreEqual(0, BrightnessRules.Apply(0, 9), "black stays black (a fade to black stays black)");
        Assert.AreEqual(100, BrightnessRules.Apply(100, 0));
        Assert.AreEqual(150, BrightnessRules.Apply(100, 5));
        Assert.AreEqual(190, BrightnessRules.Apply(100, 9));
        Assert.AreEqual(13, BrightnessRules.Apply(10, 3), "10 x 1.3 stays whole");
        Assert.AreEqual(255, BrightnessRules.Apply(200, 5), "capped at white (ramp 0xFF00)");
        Assert.AreEqual(255, BrightnessRules.Apply(255, 1));
        for (var value = 1; value <= 9; value++)
            for (var channel = 0; channel < 255; channel++)
                Assert.IsTrue(BrightnessRules.Apply(channel, value) <= BrightnessRules.Apply(channel + 1, value), "the ramp never goes down");
    }
}
