using Arcanum.Simulation.Traps;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Traps;

// ARC-WORLD-124: the trap device's plan of ledger IA13 and TP2. Spell numbers are the installed devices' (176 and 220) and invented ones.
[TestClass]
public sealed class TrapDeviceRulesTests
{
    [TestMethod]
    public void A_slot_one_spell_makes_a_timed_device_with_the_88_second_fuse()
    {
        Assert.AreEqual(new TrapDevicePlan(TrapDeviceKind.Timed, 176, 4047, true, 88_000), TrapDeviceRules.Plan(176, onTile: true));
        Assert.AreEqual(new TrapDevicePlan(TrapDeviceKind.Timed, 220, 4049, false, 88_000), TrapDeviceRules.Plan(220, onTile: true));
        Assert.AreEqual(new TrapDevicePlan(TrapDeviceKind.Timed, 150, 0, false, 88_000), TrapDeviceRules.Plan(150, onTile: true), "no marker for another record");
    }

    [TestMethod]
    public void The_marker_is_made_only_on_a_tile_and_the_alarm_is_record_176_s_alone()
    {
        Assert.AreEqual(new TrapDevicePlan(TrapDeviceKind.Timed, 176, 0, true, 88_000), TrapDeviceRules.Plan(176, onTile: false));
        Assert.AreEqual(new TrapDevicePlan(TrapDeviceKind.Timed, 220, 0, false, 88_000), TrapDeviceRules.Plan(220, onTile: false));
        Assert.AreEqual(0, TrapDeviceRules.MarkerPrototype(0));
    }

    [TestMethod]
    public void An_empty_slot_is_a_script_trap_and_a_number_outside_the_records_is_invalid()
    {
        Assert.AreEqual(TrapDeviceKind.Script, TrapDeviceRules.Plan(TrapDeviceRules.NoSpell, true).Kind);
        Assert.AreEqual(TrapDeviceKind.Script, TrapDeviceRules.Plan(10000, false).Kind);
        foreach (var slot in new[] { -1, 223, 224, 9999, 10001, int.MaxValue })
            Assert.AreEqual(TrapDeviceKind.Invalid, TrapDeviceRules.Plan(slot, true).Kind, slot.ToString());
        Assert.AreEqual(TrapDeviceKind.Timed, TrapDeviceRules.Plan(0, true).Kind);
        Assert.AreEqual(TrapDeviceKind.Timed, TrapDeviceRules.Plan(222, true).Kind);
    }
}
