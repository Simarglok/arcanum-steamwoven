using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Traps;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Traps.TrapRules;

namespace Arcanum.Simulation.Tests.Traps;

// ARC-WORLD-076: the trap rules of ledger TT1–TT9. Scripts, flags and counters are invented within the original's ranges.
[TestClass]
public sealed class TrapRulesTests
{
    [TestMethod]
    public void A_trap_is_a_use_script_with_a_trap_flag_and_magical_wins()
    {
        Assert.AreEqual(TrapClass.None, Classify(0, MagicalFlag));
        Assert.AreEqual(TrapClass.None, Classify(30001, AutoRemoveFlag | SquareTwoFlag));
        Assert.AreEqual(TrapClass.Nonmagical, Classify(30001, NonmagicalFlag));
        Assert.AreEqual(TrapClass.Magical, Classify(30000, MagicalFlag));
        Assert.AreEqual(TrapClass.Magical, Classify(30000, MagicalFlag | NonmagicalFlag));
        // Any script number with a trap flag is a trap; only the run of 30000..30007 is the trap code's (TT8).
        Assert.AreEqual(TrapClass.Nonmagical, Classify(1234, NonmagicalFlag));
    }

    [TestMethod]
    public void A_free_npc_knows_every_trap_but_the_pcs_and_the_pc_side_knows_only_spotted_or_placed_ones()
    {
        foreach (var (flags, player, free) in new[] { (0, false, true), (SpottedFlag, true, true), (PlacedByPcFlag, true, false),
            (PlacedByPcFlag | SpottedFlag, true, false) })
        {
            Assert.AreEqual(player, Known(TrapObserver.Player, flags), $"player {flags:X}");
            Assert.AreEqual(player, Known(TrapObserver.LedNpc, flags), $"led {flags:X}");
            Assert.AreEqual(free, Known(TrapObserver.FreeNpc, flags), $"free {flags:X}");
            Assert.IsFalse(Known(TrapObserver.NotCritter, flags));
        }
    }

    [TestMethod]
    public void The_spot_gate_skips_non_critters_devices_and_known_traps_and_detect_traps_needs_no_roll()
    {
        Assert.AreEqual(TrapSpotGate.NoTrap, SpotGate(TrapObserver.NotCritter, false, TrapClass.Magical, 0, 0));
        Assert.AreEqual(TrapSpotGate.NoTrap, SpotGate(TrapObserver.Player, true, TrapClass.Magical, 0, 0));
        Assert.AreEqual(TrapSpotGate.NoTrap, SpotGate(TrapObserver.Player, false, TrapClass.None, 0, 0));
        Assert.AreEqual(TrapSpotGate.Known, SpotGate(TrapObserver.Player, false, TrapClass.Nonmagical, SpottedFlag, DetectTrapsSpellFlag));
        Assert.AreEqual(TrapSpotGate.Known, SpotGate(TrapObserver.FreeNpc, false, TrapClass.Nonmagical, 0, 0));
        Assert.AreEqual(TrapSpotGate.Roll, SpotGate(TrapObserver.Player, false, TrapClass.Nonmagical, 0, 0x1));
        Assert.AreEqual(TrapSpotGate.Detected, SpotGate(TrapObserver.LedNpc, false, TrapClass.Nonmagical, 0, DetectTrapsSpellFlag));
        // A free NPC rolls only for a trap the PC placed.
        Assert.AreEqual(TrapSpotGate.Roll, SpotGate(TrapObserver.FreeNpc, false, TrapClass.Nonmagical, PlacedByPcFlag, 0));
    }

    [TestMethod]
    public void Only_the_pc_side_marks_a_trap_a_trap_object_turns_drawn_and_the_player_alone_gets_the_line()
    {
        Assert.AreEqual(new TrapSpotMark(SpottedFlag | 0x8, true, true), MarkSpotted(TrapObserver.Player, TrapCarrier.TrappedObject, 0x8));
        Assert.AreEqual(new TrapSpotMark(SpottedFlag, true, true), MarkSpotted(TrapObserver.Player, TrapCarrier.TrapObject, DontDrawFlag));
        Assert.AreEqual(new TrapSpotMark(SpottedFlag, true, false), MarkSpotted(TrapObserver.LedNpc, TrapCarrier.TrapObject, DontDrawFlag));
        Assert.AreEqual(new TrapSpotMark(SpottedFlag, true, false), MarkSpotted(TrapObserver.Player, TrapCarrier.TrappedObject, 0, quiet: true));
        Assert.AreEqual(new TrapSpotMark(DontDrawFlag, false, false), MarkSpotted(TrapObserver.FreeNpc, TrapCarrier.TrapObject, DontDrawFlag));
        Assert.AreEqual(new TrapSpotMark(0, false, false), MarkSpotted(TrapObserver.NotCritter, TrapCarrier.TrappedObject, 0));
    }

    [TestMethod]
    public void A_disarm_needs_a_known_trap_and_an_experts_critical_success_yields_the_device()
    {
        Assert.AreEqual(TrapDisarm.NotTrapped, Disarm(TrapClass.None, true, true, true, 3, 6047));
        Assert.AreEqual(TrapDisarm.NotKnown, Disarm(TrapClass.Nonmagical, false, true, true, 3, 6047));
        Assert.AreEqual(TrapDisarm.Removed, Disarm(TrapClass.Nonmagical, true, true, false, 3, 6047));
        Assert.AreEqual(TrapDisarm.Removed, Disarm(TrapClass.Nonmagical, true, true, true, ExpertTraining - 1, 6047));
        Assert.AreEqual(TrapDisarm.Removed, Disarm(TrapClass.Nonmagical, true, true, true, ExpertTraining, 0));
        Assert.AreEqual(TrapDisarm.RemovedWithDevice, Disarm(TrapClass.Magical, true, true, true, ExpertTraining, 7038));
        Assert.AreEqual(TrapDisarm.Sprung, Disarm(TrapClass.Nonmagical, true, false, true, 0, 6047));
        Assert.AreEqual(TrapDisarm.Failed, Disarm(TrapClass.Nonmagical, true, false, false, 3, 6047));
    }

    [TestMethod]
    public void The_device_comes_from_the_script_table_or_the_pc_placed_counters()
    {
        int[] expected = [0, 6047, 7038, 7039, 7041, 7040, 10065, 0];
        for (var i = 0; i < expected.Length; i++) Assert.AreEqual(expected[i], DevicePrototype(FirstNativeScript + i, 0, 0), $"{FirstNativeScript + i}");
        Assert.AreEqual(0, DevicePrototype(29999, 0, 0));
        Assert.AreEqual(0, DevicePrototype(30008, PlacedByPcFlag, 0));
        Assert.AreEqual(PlacedDevice, DevicePrototype(30000, PlacedByPcFlag, 0x00FFFFFF));
        Assert.AreEqual(PlacedDeviceSecond, DevicePrototype(30003, PlacedByPcFlag, 0x01000000));
    }

    [TestMethod]
    public void A_native_run_takes_its_square_from_the_flags_and_30007_does_nothing()
    {
        Assert.IsNull(NativeRun(30001, AutoRemoveFlag));
        Assert.IsNull(NativeRun(29999, NonmagicalFlag));
        Assert.IsNull(NativeRun(30008, NonmagicalFlag));
        Assert.AreEqual(new NativeTrapRun(30007, NativeTrapEffect.None, 0, false, -1, -1, -1), NativeRun(30007, NonmagicalFlag | AutoRemoveFlag | SquareFourFlag));
        Assert.AreEqual(new NativeTrapRun(30000, NativeTrapEffect.Spell, 0, true, 0, 1, 2), NativeRun(30000, MagicalFlag | AutoRemoveFlag));
        Assert.AreEqual(new NativeTrapRun(30002, NativeTrapEffect.Attack, 1, false, 6, 7, 8), NativeRun(30002, NonmagicalFlag | SquareOneFlag));
        Assert.AreEqual(2, NativeRun(30006, NonmagicalFlag | SquareOneFlag | SquareTwoFlag)!.Value.Radius);
        Assert.AreEqual(4, NativeRun(30006, NonmagicalFlag | SquareTwoFlag | SquareFourFlag)!.Value.Radius);
    }

    [TestMethod]
    public void Spell_flags_two_and_four_keep_a_mover_off_a_trap()
    {
        Assert.IsTrue(StepTriggers(0));
        Assert.IsTrue(StepTriggers(0x1 | 0x8 | 0x100));
        Assert.IsFalse(StepTriggers(0x2));
        Assert.IsFalse(StepTriggers(0x4 | 0x10));
    }

    // ARC-MAGIC-057 (ledger SP1): the spot trap skill's case of 0x4C7160.
    [TestMethod]
    public void A_passed_first_roll_spots_and_keeps_its_critical_flag_without_a_second_roll()
    {
        var calls = 0;
        int Second() { calls++; return 1; }
        Assert.AreEqual((true, false), SpotCase(true, false, SkillTraining.None, TrapClass.Nonmagical, 50, 0, Second));
        Assert.AreEqual((true, true), SpotCase(true, true, SkillTraining.Master, TrapClass.Magical, 50, 0, Second));
        Assert.AreEqual(0, calls, "the second roll is drawn only for a failed master");
    }

    [TestMethod]
    public void Only_a_master_that_failed_without_a_critical_failure_rolls_again_against_effectiveness_and_total()
    {
        var calls = 0;
        int Roll(int value) { calls++; return value; }
        Assert.AreEqual((false, false), SpotCase(false, false, SkillTraining.Expert, TrapClass.Nonmagical, 90, 0, () => Roll(1)));
        Assert.AreEqual((false, true), SpotCase(false, true, SkillTraining.Master, TrapClass.Nonmagical, 90, 0, () => Roll(1)));
        Assert.AreEqual(0, calls);
        // Effectiveness 60, total 10: a roll of 50 passes (50 + 10 <= 60), 51 does not.
        Assert.AreEqual((true, false), SpotCase(false, false, SkillTraining.Master, TrapClass.Nonmagical, 60, 10, () => Roll(50)));
        Assert.AreEqual((false, false), SpotCase(false, false, SkillTraining.Master, TrapClass.Nonmagical, 60, 10, () => Roll(51)));
        Assert.AreEqual(2, calls);
        // A negative total (a bonus) helps.
        Assert.AreEqual((true, false), SpotCase(false, false, SkillTraining.Master, TrapClass.Nonmagical, 60, -20, () => Roll(80)));
    }

    [TestMethod]
    public void A_magical_trap_is_never_spotted_below_expert_whichever_roll_passed()
    {
        foreach (var training in new[] { SkillTraining.None, SkillTraining.Apprentice })
            Assert.AreEqual((false, false), SpotCase(true, true, training, TrapClass.Magical, 95, 0, () => 1));
        foreach (var training in new[] { SkillTraining.Expert, SkillTraining.Master })
            Assert.AreEqual((true, false), SpotCase(true, false, training, TrapClass.Magical, 95, 0, () => 1));
        Assert.AreEqual((true, false), SpotCase(false, false, SkillTraining.Master, TrapClass.Magical, 95, 0, () => 1), "a master's second roll spots a magical trap");
        Assert.AreEqual((true, false), SpotCase(true, false, SkillTraining.Apprentice, TrapClass.Nonmagical, 95, 0, () => 1));
    }

    // ARC-SKILLS-1156 (ledger SP3): the disarm traps skill's case of 0x4C7160.
    [TestMethod]
    public void A_passed_disarm_check_keeps_its_critical_flag_without_a_second_roll()
    {
        var calls = 0;
        int Second() { calls++; return 1; }
        Assert.AreEqual((true, false), DisarmCase(true, false, SkillTraining.None, 50, 0, Second));
        Assert.AreEqual((true, true), DisarmCase(true, true, SkillTraining.Master, 50, 0, Second));
        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    public void Only_a_master_that_failed_without_a_critical_failure_rolls_the_disarm_check_again()
    {
        var calls = 0;
        int Roll(int value) { calls++; return value; }
        Assert.AreEqual((false, false), DisarmCase(false, false, SkillTraining.Expert, 90, 0, () => Roll(1)));
        Assert.AreEqual((false, true), DisarmCase(false, true, SkillTraining.Master, 90, 0, () => Roll(1)), "a critical failure keeps its flag and draws nothing");
        Assert.AreEqual(0, calls);
        Assert.AreEqual((true, false), DisarmCase(false, false, SkillTraining.Master, 60, 10, () => Roll(50)), "a pass carries no critical flag");
        Assert.AreEqual((false, false), DisarmCase(false, false, SkillTraining.Master, 60, 10, () => Roll(51)));
        Assert.AreEqual(2, calls);
        Assert.AreEqual((true, false), DisarmCase(false, false, SkillTraining.Master, 60, -20, () => Roll(80)), "a negative total helps");
    }
}
