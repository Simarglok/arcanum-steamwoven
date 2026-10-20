using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Magic;

// ARC-MAGIC-002: the magic rule settings and the MG18 target mask.
[TestClass]
public sealed class MagicAnomalyProfileTests
{
    [TestMethod]
    public void The_two_settings_and_their_names()
    {
        Assert.IsFalse(MagicAnomalyProfile.Steamwoven.NotDeadCritterTargetLacksCritterBit);
        Assert.IsTrue(MagicAnomalyProfile.Original.NotDeadCritterTargetLacksCritterBit);
        CollectionAssert.AreEqual(new[] { MagicAnomalyProfile.Steamwoven, MagicAnomalyProfile.Original }, MagicAnomalyProfile.All.ToArray());
        Assert.AreSame(MagicAnomalyProfile.Original, MagicAnomalyProfile.FromName("original"));
        Assert.IsNull(MagicAnomalyProfile.FromName("Original"));
        Assert.IsNull(MagicAnomalyProfile.FromName(null));
    }

    [TestMethod]
    public void Mg18_adds_the_critter_bit_only_in_steamwoven_and_only_to_a_mask_naming_the_target()
    {
        const ulong notDeadCritter = 0x80000004UL, obj = 0x4UL, critter = 0x44UL;
        Assert.AreEqual(0x80000044UL, SpellTargetRules.Targets(notDeadCritter, MagicAnomalyProfile.Steamwoven));
        Assert.AreEqual(notDeadCritter, SpellTargetRules.Targets(notDeadCritter, MagicAnomalyProfile.Original));
        Assert.AreEqual(obj, SpellTargetRules.Targets(obj, MagicAnomalyProfile.Steamwoven));
        Assert.AreEqual(critter | notDeadCritter, SpellTargetRules.Targets(critter | notDeadCritter, MagicAnomalyProfile.Steamwoven));
        Assert.ThrowsExactly<ArgumentNullException>(() => SpellTargetRules.Targets(obj, null!));
    }
}
