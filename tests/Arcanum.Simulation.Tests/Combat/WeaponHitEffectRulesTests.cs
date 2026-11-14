using Arcanum.Simulation.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Combat;

// ARC-RENDER-1990 (0x4377C0, 0x49B290): a weapon's entry in the weapon eye-candy list.
[TestClass]
public sealed class WeaponHitEffectRulesTests
{
    [TestMethod]
    public void The_entry_is_five_for_each_basic_prototype_above_the_first_plus_the_kind()
    {
        // The prototype file's number less 20 is the basic prototype, the list starts at 0x178D (6029).
        Assert.AreEqual(0, WeaponHitEffectRules.Index(6029 + 20, WeaponHitEffectRules.Begin));
        Assert.AreEqual(58, WeaponHitEffectRules.Index(6060, WeaponHitEffectRules.Hit), "the Tesla Rod: 11 groups of five, kind 3");
        Assert.AreEqual(103, WeaponHitEffectRules.Index(6069, WeaponHitEffectRules.Hit), "Bronwyck's Gun: 20 groups, kind 3");
        Assert.AreEqual(102, WeaponHitEffectRules.Index(6069, WeaponHitEffectRules.Projectile));
        Assert.AreEqual(104, WeaponHitEffectRules.Index(6069, WeaponHitEffectRules.Unused));
    }

    [TestMethod]
    public void A_prototype_below_the_list_and_a_kind_outside_it_name_no_entry()
    {
        Assert.IsNull(WeaponHitEffectRules.Index(6029 + 19, WeaponHitEffectRules.Hit));
        Assert.IsNull(WeaponHitEffectRules.Index(0, WeaponHitEffectRules.Hit));
        Assert.IsNull(WeaponHitEffectRules.Index(6060, 5));
        Assert.IsNull(WeaponHitEffectRules.Index(6060, -1));
    }
}
