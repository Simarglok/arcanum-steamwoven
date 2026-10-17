using Arcanum.Simulation.Character;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Character;

// ARC-WORLD-029: the source's aggregation of one value's effect changes (effect_adjust_func, checked at 0x4EA6C0).
[TestClass]
public sealed class EffectModifiersTests
{
    private static EffectModifier M(StatModifierOperator op, int value) => new(op, value);

    [TestMethod]
    public void PercentThenMultiplyThenDivideThenAddThenTheBounds()
    {
        // 30 × (100 + 20 − 10) / 100 = 33, × 3 = 99, / 2 = 49, + 7 − 2 = 54.
        Assert.AreEqual(54, EffectModifiers.Apply(30, [M(StatModifierOperator.Add, 7), M(StatModifierOperator.Divide, 2),
            M(StatModifierOperator.Percent, 20), M(StatModifierOperator.Multiply, 3), M(StatModifierOperator.Percent, -10),
            M(StatModifierOperator.Add, -2)]));
        // The highest minimum and the lowest maximum, applied after the sum.
        Assert.AreEqual(40, EffectModifiers.Apply(30, [M(StatModifierOperator.Minimum, 25), M(StatModifierOperator.Minimum, 40)]));
        Assert.AreEqual(12, EffectModifiers.Apply(30, [M(StatModifierOperator.Maximum, 20), M(StatModifierOperator.Maximum, 12)]));
        Assert.AreEqual(40, EffectModifiers.Apply(30, [M(StatModifierOperator.Minimum, 50), M(StatModifierOperator.Maximum, 40)]),
            "the maximum is tested last, so it wins over a higher minimum");
        // −1 is the bounds' "unset" mark (EF6): never applied, and it clears a lower minimum or a higher maximum met before it.
        Assert.AreEqual(30, EffectModifiers.Apply(30, [M(StatModifierOperator.Maximum, -1)]));
        Assert.AreEqual(30, EffectModifiers.Apply(30, [M(StatModifierOperator.Maximum, 5), M(StatModifierOperator.Maximum, -1)]));
        Assert.AreEqual(-5, EffectModifiers.Apply(-5, [M(StatModifierOperator.Minimum, -3), M(StatModifierOperator.Minimum, -1)]));
        Assert.AreEqual(5, EffectModifiers.Apply(30, [M(StatModifierOperator.Maximum, -1), M(StatModifierOperator.Maximum, 5)]));
        Assert.AreEqual(-3, EffectModifiers.Apply(-5, [M(StatModifierOperator.Minimum, -3)]));
        // Truncation toward zero, and no change without modifiers.
        Assert.AreEqual(-7, EffectModifiers.Apply(-15, [M(StatModifierOperator.Divide, 2)]));
        Assert.AreEqual(30, EffectModifiers.Apply(30, []));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => EffectModifiers.Apply(1, [M(StatModifierOperator.Divide, 0)]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => EffectModifiers.Apply(1, [M((StatModifierOperator)6, 0)]));
    }

    [TestMethod]
    public void ValidityBoundsCountMagnitudeAndOperators()
    {
        Assert.IsTrue(EffectModifiers.IsValid([M(StatModifierOperator.Add, 10)], 1, 10));
        Assert.IsFalse(EffectModifiers.IsValid([M(StatModifierOperator.Add, 11)], 1, 10));
        Assert.IsFalse(EffectModifiers.IsValid([M(StatModifierOperator.Add, 1), M(StatModifierOperator.Add, 1)], 1, 10));
        Assert.IsFalse(EffectModifiers.IsValid([M(StatModifierOperator.Divide, 0)], 1, 10));
        Assert.IsFalse(EffectModifiers.IsValid([M((StatModifierOperator)(-1), 0)], 1, 10));
        Assert.IsFalse(EffectModifiers.IsValid(null, 1, 10));
    }
}
