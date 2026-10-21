using Arcanum.Simulation.Technology;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Technology;

/// <summary>ARC-TECHNOLOGY-008: magick against technology for a device's holder (ledger DV2–DV5), invented numbers.</summary>
[TestClass]
public sealed class TechDeviceRulesTests
{
    [TestMethod]
    public void A_technological_device_is_never_refused_to_a_mage_and_a_weapon_fails_in_combat()
    {
        // A mage (aptitude 60) with a technological weapon (complexity −80): usable, power trunc((−80 + 60) / 2) = −10 (12 %), 48 % to fail
        // critically in combat.
        Assert.AreEqual(new TechDeviceUse(true, -10, 12, 48), TechDeviceRules.Use(-80, 60, weapon: true));
        Assert.AreEqual(new TechDeviceUse(true, -10, 12, 0), TechDeviceRules.Use(-80, 60, weapon: false), "only a weapon fails in combat");
        // A technologist (−60): full power side, no failure.
        Assert.AreEqual(new TechDeviceUse(true, -70, 87, 0), TechDeviceRules.Use(-80, -60, weapon: true));
        // The gate refuses only a magickal item to a technologist who draws no power from it.
        Assert.IsFalse(TechDeviceRules.Use(40, -60, weapon: false).Usable);
        Assert.IsTrue(TechDeviceRules.Use(40, -30, weapon: false).Usable == (Arcanum.Simulation.Equipment.EquipmentRules.EffectivePower(40, -30) > 0));
        Assert.IsTrue(TechDeviceRules.Use(0, -100, weapon: true).Usable, "a neutral item");
        Assert.AreEqual(Arcanum.Simulation.Combat.AttackRules.TechnologyFailureChance(-80, 60), TechDeviceRules.Use(-80, 60, true).CombatFailureChance);
    }
}
