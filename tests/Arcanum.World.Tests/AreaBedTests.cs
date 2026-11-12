using Arcanum.Data.Area;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.World.Tests;

/// <summary>ARC-WORLD-1162 (E3-d; ledger SWB1, SWB3, SWB5, SWB14–SWB16): what a bed is, who lies in it, and the open decision of a call with a bed. Synthetic values only.</summary>
[TestClass]
public sealed class AreaBedTests
{
    // An art id of scenery (type 4) with a sub-type (bits 6 to 10), a number (bits 19 to 27) and a frame (bits 14 to 18).
    private static uint Art(uint type, int subType, int number = 0, int frame = 0) => type << 28 | (uint)number << 19 | (uint)frame << 14 | (uint)subType << 6;

    private static AreaObjectKey Key(int index) => new("source", 7, AreaSourceKind.Static, 1, index, index * 100L, "id" + index, 10);

    [TestMethod]
    public void A_bed_is_scenery_art_of_type_four_with_sub_type_0x16_whatever_its_number_or_frame()
    {
        Assert.IsTrue(AreaBeds.IsBedArt(Art(4, 0x16)));
        Assert.IsTrue(AreaBeds.IsBedArt(Art(4, 0x16, number: 7, frame: 1)), "the occupied frame is the same sub-type");
        Assert.IsFalse(AreaBeds.IsBedArt(Art(4, 0x15)));
        Assert.IsFalse(AreaBeds.IsBedArt(Art(4, 0x17)));
        Assert.IsFalse(AreaBeds.IsBedArt(Art(3, 0x16)), "another art type with the same bits is no bed");
        Assert.IsFalse(AreaBeds.IsBedArt(Art(6, 0x16)));
    }

    [TestMethod]
    public void An_occupied_bed_answers_no_to_a_second_seat_and_a_stranger_cannot_take_the_occupant_out()
    {
        var occupancy = new AreaBedOccupancy();
        var bed = Key(1);
        Assert.IsNull(occupancy.Occupant(bed));
        Assert.IsTrue(occupancy.Seat(bed, "pc"));
        Assert.AreEqual("pc", occupancy.Occupant(bed));
        Assert.IsFalse(occupancy.Seat(bed, "npc:a"), "an occupied bed changes nothing");
        Assert.IsFalse(occupancy.Seat(bed, "pc"), "not even for the occupant itself");
        Assert.AreEqual("pc", occupancy.Occupant(bed));
        Assert.IsFalse(occupancy.Unseat(bed, "npc:a"), "an occupant that does not match only logs");
        Assert.AreEqual("pc", occupancy.Occupant(bed));
        Assert.IsTrue(occupancy.Unseat(bed, "pc"));
        Assert.IsNull(occupancy.Occupant(bed));
        Assert.IsFalse(occupancy.Unseat(bed, "pc"), "an empty bed has no occupant to take out");
        Assert.IsTrue(occupancy.Seat(bed, "npc:a"), "an emptied bed takes the next sleeper");
    }

    [TestMethod]
    public void Each_bed_holds_its_own_occupant_and_clear_empties_them_all()
    {
        var occupancy = new AreaBedOccupancy();
        Assert.IsTrue(occupancy.Seat(Key(1), "pc"));
        Assert.IsTrue(occupancy.Seat(Key(2), "npc:a"));
        Assert.AreEqual(2, occupancy.Count);
        Assert.AreEqual(Key(2), occupancy.BedOf("npc:a"), "the bed a sleeper lies in");
        Assert.IsNull(occupancy.BedOf("npc:b"));
        occupancy.Clear();
        Assert.AreEqual(0, occupancy.Count);
        Assert.IsNull(occupancy.Occupant(Key(1)));
    }

    private static AreaRestFacts Facts(bool bed, AreaRestTownMap town = AreaRestTownMap.None, bool combat = false, bool mayLeave = true, bool open = false, bool unconscious = false, bool dead = false) =>
        new(dead, open, unconscious, town, combat, mayLeave, bed);

    [TestMethod]
    public void A_call_with_a_bed_skips_the_town_map_test_and_never_waits()
    {
        // A town-map sector without the wait marker refuses the ground rest (message 20) but not the bed (SWB3); one with the marker waits on the ground but the bed always sleeps.
        Assert.AreEqual(AreaRestMessages.WaitRefused, AreaRestRules.Decide(Facts(bed: false, town: AreaRestTownMap.WaitRefused)).Message);
        Assert.AreEqual(new AreaRestOpening(AreaRestOpenOutcome.Open, false, null, false), AreaRestRules.Decide(Facts(bed: true, town: AreaRestTownMap.WaitRefused)));
        Assert.IsTrue(AreaRestRules.Decide(Facts(bed: false, town: AreaRestTownMap.WaitAllowed)).Wait);
        var sleeps = AreaRestRules.Decide(Facts(bed: true, town: AreaRestTownMap.WaitAllowed));
        Assert.IsFalse(sleeps.Wait);
        Assert.AreEqual(8, AreaRestRules.ButtonCount(sleeps.Wait), "the bed's window has the eight buttons, until healed included");
    }

    [TestMethod]
    public void A_call_with_a_bed_keeps_the_gates_before_it_the_dead_the_open_window_the_unconscious_and_the_combat_exit()
    {
        Assert.AreEqual(AreaRestOpenOutcome.Nothing, AreaRestRules.Decide(Facts(bed: true, dead: true)).Outcome);
        Assert.AreEqual(AreaRestOpenOutcome.Close, AreaRestRules.Decide(Facts(bed: true, open: true)).Outcome);
        Assert.AreEqual(AreaRestMessages.HeroUnconscious, AreaRestRules.Decide(Facts(bed: true, unconscious: true)).Message);
        Assert.AreEqual(AreaRestMessages.CombatRefused, AreaRestRules.Decide(Facts(bed: true, combat: true, mayLeave: false)).Message);
        Assert.IsTrue(AreaRestRules.Decide(Facts(bed: true, combat: true, mayLeave: true)).LeavesCombatMode, "the hero leaves combat mode before the bed's script can refuse him");
    }

    [TestMethod]
    public void The_bed_script_answer_one_refuses_with_message_eight_and_any_other_goes_on_to_the_seat()
    {
        Assert.AreEqual(AreaRestMessages.ScriptRefused, AreaRestRules.BedScriptRefusal(runDefault: true));
        Assert.IsNull(AreaRestRules.BedScriptRefusal(runDefault: false));
    }

    [TestMethod]
    public void A_bed_heals_twice_the_ground_and_the_wait_window_nothing()
    {
        Assert.AreEqual(1, AreaRestRules.HealKind(bed: false));
        Assert.AreEqual(2, AreaRestRules.HealKind(bed: true));
    }
}
