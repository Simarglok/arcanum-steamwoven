using Arcanum.Simulation.Character;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Character;

/// <summary>ARC-MAGIC-056 (MA-9, ledger XR6, owner decision on XR14 (a)): the speed change of Tempus Fugit for one critter; invented facts only.</summary>
[TestClass]
public sealed class TempusFugitShiftTests
{
    [TestMethod]
    public void A_holder_gains_ten_every_other_critter_loses_ten_and_nobody_changes_while_no_critter_holds_it()
    {
        Assert.AreEqual(10, CharacterStatRules.TempusFugitShift(true, true));
        Assert.AreEqual(-10, CharacterStatRules.TempusFugitShift(false, true));
        Assert.AreEqual(0, CharacterStatRules.TempusFugitShift(false, false));
        Assert.AreEqual(0, CharacterStatRules.TempusFugitShift(true, false), "a holder without any holder cannot happen; the rule stays neutral");
    }

    [TestMethod]
    public void Only_speed_reads_the_shift_and_the_crippled_legs_add_to_it()
    {
        var context = new CharacterStatContext(0, CrippledLegs: true, SpeedShift: 10);
        Assert.AreEqual(5, CharacterStatRules.Situational(CharacterStat.Speed, context));
        Assert.AreEqual(0, CharacterStatRules.Situational(CharacterStat.Strength, context));
        Assert.AreEqual(0, CharacterStatRules.Situational(CharacterStat.MagickPoints, context));
    }
}
