using Arcanum.Simulation.Character;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Tests.Character.CharacterFixtures;

namespace Arcanum.Simulation.Tests.Character;

/// <summary>ARC-COMBAT-002: poison and crippled legs adjust base values before effects, as the stat getter 0x4B0490 does.</summary>
[TestClass]
public sealed class ConditionContextTests
{
    private static CharacterStatsSnapshot Query(CharacterStatsOwner owner, int poison = 0, bool legs = false)
    {
        var result = owner.Query(new(0, poison, legs));
        Assert.AreEqual(CharacterStatsFailure.None, result.Failure);
        return result.Snapshot!;
    }

    [TestMethod]
    public void PoisonLowersStrengthAndDexterityUpToThree()
    {
        var owner = Owner();
        foreach (var (poison, penalty) in new[] { (0, 0), (99, 0), (100, 1), (250, 2), (399, 3), (1000, 3) })
        {
            var snapshot = Query(owner, poison);
            Assert.AreEqual((8 - penalty, 8 - penalty), (snapshot.GetEffective(CharacterStat.Strength), snapshot.GetEffective(CharacterStat.Dexterity)), $"poison {poison}");
            Assert.AreEqual(8, snapshot.GetBase(CharacterStat.Strength), "the stored base is unchanged");
            Assert.AreEqual(8, snapshot.GetEffective(CharacterStat.Constitution));
        }
        // Derived stats read the lowered values: carry weight 500 × 6, speed = DX 6.
        var poisoned = Query(owner, 200);
        Assert.AreEqual((3000, 6), (poisoned.GetEffective(CharacterStat.CarryWeight), poisoned.GetEffective(CharacterStat.Speed)));
    }

    [TestMethod]
    public void ThePenaltyComesBeforeEffectsAndTheClamp()
    {
        // Strength 20 at the human maximum with +3: 23 clamps to 20; under penalty 3 it is 17 + 3 = 20, not 20 − 3.
        var owner = Owner();
        Accepted(owner.SetBase(CharacterStat.Strength, 20));
        Assert.IsNotNull(owner.AddEffect(Effect(1, Add(CharacterStat.Strength, 3)), EffectCause.Item).AddedEffect);
        Assert.AreEqual(20, Query(owner).GetEffective(CharacterStat.Strength));
        Assert.AreEqual(20, Query(owner, 300).GetEffective(CharacterStat.Strength));
        // At strength 1 the race minimum holds.
        var weak = Owner();
        Accepted(weak.SetBase(CharacterStat.Strength, 1));
        Assert.AreEqual(1, Query(weak, 300).GetEffective(CharacterStat.Strength));
    }

    [TestMethod]
    public void CrippledLegsCostFiveSpeed()
    {
        var owner = Owner();
        Assert.AreEqual((8, 3), (Query(owner).GetEffective(CharacterStat.Speed), Query(owner, legs: true).GetEffective(CharacterStat.Speed)));
        Assert.AreEqual(8, Query(owner, legs: true).GetBase(CharacterStat.Speed), "the derived base is the formula");
        // Speed stays within its range 1..100.
        var slow = Owner();
        Accepted(slow.SetBase(CharacterStat.Dexterity, 3));
        Assert.AreEqual(1, Query(slow, legs: true).GetEffective(CharacterStat.Speed));
    }

    [TestMethod]
    public void APoisonLevelOutsideTheStatRangeIsAnInvalidContext()
    {
        var owner = Owner();
        Assert.AreEqual(CharacterStatsFailure.InvalidContext, owner.Query(new(0, -1)).Failure);
        Assert.AreEqual(CharacterStatsFailure.InvalidContext, owner.Query(new(0, 1001)).Failure);
        Assert.AreEqual(CharacterStatsFailure.None, owner.Query(new(0, 1000)).Failure);
    }
}
