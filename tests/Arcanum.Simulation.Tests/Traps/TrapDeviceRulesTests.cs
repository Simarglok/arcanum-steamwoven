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

    // ARC-WORLD-125 (ledger IA13, TP2): the script trap on an object or a tile. The marker table is the dwords 26000..26007 at 0x5B5F60; the numbers 30000..30007 are the native scripts'.
    [TestMethod]
    public void A_native_script_picks_its_own_marker_and_another_script_the_marker_of_its_class()
    {
        for (var native = 30000; native <= 30007; native++)
            Assert.AreEqual(26000 + native - 30000, TrapDeviceRules.ScriptMarkerPrototype(native, TrapClass.Nonmagical), native.ToString());
        Assert.AreEqual(26000, TrapDeviceRules.ScriptMarkerPrototype(30000, TrapClass.Magical), "the table decides for a native script, whatever its class");
        Assert.AreEqual(26000, TrapDeviceRules.ScriptMarkerPrototype(1234, TrapClass.Magical));
        Assert.AreEqual(26001, TrapDeviceRules.ScriptMarkerPrototype(1234, TrapClass.Nonmagical));
        Assert.AreEqual(26001, TrapDeviceRules.ScriptMarkerPrototype(1234, TrapClass.None), "a script with no trap flag takes the second marker");
        Assert.AreEqual(26001, TrapDeviceRules.ScriptMarkerPrototype(29999, TrapClass.Nonmagical), "just below the native range");
        Assert.AreEqual(26001, TrapDeviceRules.ScriptMarkerPrototype(30008, TrapClass.Nonmagical), "just above it");
    }

    [TestMethod]
    public void Only_a_portal_or_a_container_without_a_use_script_takes_a_script_trap()
    {
        Assert.AreEqual(new ScriptTrapPlan(true, false, 0, 0), TrapDeviceRules.ScriptOnObject(TrapDeviceRules.ObjectTypePortal, 0));
        Assert.AreEqual(new ScriptTrapPlan(true, false, 0, 0), TrapDeviceRules.ScriptOnObject(TrapDeviceRules.ObjectTypeContainer, 0));
        var refused = new ScriptTrapPlan(false, false, 0, 2);
        Assert.AreEqual(refused, TrapDeviceRules.ScriptOnObject(TrapDeviceRules.ObjectTypePortal, 30000), "it holds a script already");
        Assert.AreEqual(refused, TrapDeviceRules.ScriptOnObject(TrapDeviceRules.ObjectTypeContainer, 1));
        foreach (var type in new[] { 3, 4, 5, 10, 14, 15, 16, 17 })
            Assert.AreEqual(refused, TrapDeviceRules.ScriptOnObject(type, 0), $"object type {type}");
    }

    [TestMethod]
    public void A_script_trap_on_a_tile_makes_its_marker_unless_a_trap_object_stands_there()
    {
        Assert.AreEqual(new ScriptTrapPlan(true, true, 26003, 0), TrapDeviceRules.ScriptOnTile(false, 30003, TrapClass.Nonmagical));
        Assert.AreEqual(new ScriptTrapPlan(true, true, 26001, 0), TrapDeviceRules.ScriptOnTile(false, 77, TrapClass.None));
        Assert.AreEqual(new ScriptTrapPlan(false, true, 0, 3), TrapDeviceRules.ScriptOnTile(true, 30003, TrapClass.Nonmagical));
    }
}
