using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Movement;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-WORLD-1162 (E3-d slice 2, ledger SWB16–SWB21): an NPC's bed over invented rows. The NPC (0) stands at world cell (10, 20); its night standpoint holds a bed, its day standpoint (20, 25) none. The standpoint step's near
/// branch sets the latch (the original's NPC flag 0x200) once and seats the NPC; the far branch and the hour switch wake it through the re-seat; an attack wakes it; a rebuilt host seats a saved sleeper again.
/// </summary>
[TestClass]
public sealed class AreaAiBedTests
{
    private static readonly CellCoord NightStand = new(10, 20), DayStand = new(20, 25);
    private static readonly AreaObjectKey BedKey = new(new string('b', 64), 1, AreaSourceKind.Static, 0, 7, 700L, new string('c', 64), 100);

    private static AreaAiRouteTests.Scene Open(AreaBedOccupancy occupancy, bool bed = true, uint flags = 0, (int X, int Y)[]? waypoints = null) =>
        AreaAiRouteTests.Open(AreaAiRouteTests.Npc(flags, waypoints ?? [], 0, (DayStand.X, DayStand.Y), (NightStand.X, NightStand.Y)),
            beds: new AreaAiBeds(occupancy, () => bed ? [(BedKey, NightStand)] : []));

    private static void JumpToHour(AreaAiRouteTests.Scene scene, int hour)
    {
        var now = scene.Session.QueryClockSnapshot();
        var delta = ((hour - now.Hour + 24) % 24) * 3_600_000L;
        Assert.AreEqual(ClockMutationDisposition.Changed, scene.Session.JumpClocks(now.Token, Math.Max(delta, 1)).Disposition);
        Assert.AreEqual(hour, scene.Session.QueryClockSnapshot().Hour);
    }

    private static string Route(AreaAiRouteTests.Scene scene) => scene.Host.TraceOf(scene.Npc)?.Route ?? "";

    [TestMethod]
    public void An_npc_at_its_night_standpoint_sets_the_latch_and_lies_in_the_bed_there()
    {
        var occupancy = new AreaBedOccupancy();
        var scene = Open(occupancy);
        JumpToHour(scene, 3);
        Assert.IsFalse(scene.Host.Sleeping(scene.Npc));
        scene.Host.RunStandpoints(scene.Npc);
        StringAssert.Contains(Route(scene), "bed: seated");
        Assert.AreEqual(scene.Npc, occupancy.Occupant(BedKey));
        Assert.AreEqual(BedKey, scene.Host.BedOfSleeper(scene.Npc));
        Assert.IsTrue(scene.Host.Arrived.Contains(scene.Npc), "NPC flag 0x200");
        Assert.IsTrue(scene.Host.Sleeping(scene.Npc), "critter flag 0x1000 over the data's");
        Assert.AreEqual(0x1000, scene.Session.QueryObjectFlags(scene.Npc).CritterSet);
        Assert.AreEqual(0x100, scene.Session.QueryObjectFlags(scene.Npc).ObjectSet, "object flag 0x100: the bed's occupied frame shows the sleeper");
        Assert.IsTrue(scene.Host.Hidden(scene.Npc));
        Assert.AreEqual(new CellCoord(11, 21), scene.Host.Tile(scene.Npc), "a sleeper on the bed's tile is moved to the neighbour in direction 4 (x + 1, y + 1)");
        StringAssert.Contains(Route(scene), "moved 10,20 -> 11,21");
    }

    [TestMethod]
    public void A_seated_npc_answers_on_every_later_step_without_moving_or_asking_again()
    {
        var occupancy = new AreaBedOccupancy();
        var scene = Open(occupancy);
        JumpToHour(scene, 3);
        scene.Host.RunStandpoints(scene.Npc);
        scene.Host.RunStandpoints(scene.Npc);
        StringAssert.Contains(Route(scene), "standpoint Sleeping");
        Assert.AreEqual(scene.Npc, occupancy.Occupant(BedKey));
        Assert.AreEqual(1, occupancy.Count);
    }

    [TestMethod]
    public void The_seat_answer_is_not_read_a_taken_bed_keeps_the_latch_and_is_not_asked_again()
    {
        var occupancy = new AreaBedOccupancy();
        Assert.IsTrue(occupancy.Seat(BedKey, "pc"));
        var scene = Open(occupancy);
        JumpToHour(scene, 3);
        scene.Host.RunStandpoints(scene.Npc);
        StringAssert.Contains(Route(scene), "bed taken by pc");
        Assert.IsTrue(scene.Host.Arrived.Contains(scene.Npc));
        Assert.IsFalse(scene.Host.Sleeping(scene.Npc));
        occupancy.Unseat(BedKey, "pc");
        scene.Host.RunStandpoints(scene.Npc);
        Assert.IsNull(occupancy.Occupant(BedKey), "the latch is set: not tried again until the NPC leaves its radius");
        Assert.IsFalse(scene.Host.Sleeping(scene.Npc));
    }

    [TestMethod]
    public void A_standpoint_without_a_bed_sets_the_latch_and_seats_nobody()
    {
        var occupancy = new AreaBedOccupancy();
        var scene = Open(occupancy, bed: false);
        JumpToHour(scene, 3);
        scene.Host.RunStandpoints(scene.Npc);
        StringAssert.Contains(Route(scene), "no bed on the standpoint");
        Assert.IsTrue(scene.Host.Arrived.Contains(scene.Npc));
        Assert.AreEqual(0, occupancy.Count);
        Assert.IsFalse(scene.Host.Sleeping(scene.Npc));
    }

    [TestMethod]
    public void The_hour_switch_to_the_day_standpoint_wakes_the_sleeper_and_the_next_step_moves_it()
    {
        var occupancy = new AreaBedOccupancy();
        var scene = Open(occupancy);
        JumpToHour(scene, 3);
        scene.Host.RunStandpoints(scene.Npc);
        Assert.IsTrue(scene.Host.Sleeping(scene.Npc));
        JumpToHour(scene, 12);
        scene.Host.RunStandpoints(scene.Npc);
        StringAssert.Contains(Route(scene), "bed: woken");
        Assert.IsNull(occupancy.Occupant(BedKey));
        Assert.IsFalse(scene.Host.Sleeping(scene.Npc));
        Assert.IsFalse(scene.Host.Arrived.Contains(scene.Npc), "the unseat clears NPC flag 0x200");
        Assert.IsTrue(scene.Session.QueryObjectFlags(scene.Npc).IsEmpty, "the flag write that gives back the data's value leaves no change behind");
        Assert.AreEqual(new CellCoord(11, 21), scene.Host.Tile(scene.Npc), "the waking step moves nothing: the NPC stays where it was seated");
        Assert.IsFalse(scene.Host.Hidden(scene.Npc));
        scene.Host.RunStandpoints(scene.Npc);
        Assert.AreEqual(DayStand, scene.Host.Tile(scene.Npc), "the next step relocates it: " + Route(scene));
    }

    [TestMethod]
    public void The_far_branch_clears_the_latch_before_it_moves_the_npc()
    {
        var occupancy = new AreaBedOccupancy();
        var scene = Open(occupancy, bed: false);
        JumpToHour(scene, 3);
        scene.Host.RunStandpoints(scene.Npc);
        Assert.IsTrue(scene.Host.Arrived.Contains(scene.Npc));
        JumpToHour(scene, 12);
        scene.Host.RunStandpoints(scene.Npc);
        Assert.IsFalse(scene.Host.Arrived.Contains(scene.Npc));
        Assert.AreEqual(DayStand, scene.Host.Tile(scene.Npc));
    }

    [TestMethod]
    public void The_waypoint_step_wakes_a_sleeper_too_when_the_period_has_a_waypoint_list()
    {
        var occupancy = new AreaBedOccupancy();
        var scene = Open(occupancy, flags: Arcanum.Simulation.Ai.NpcRouteFlags.WaypointsNight | Arcanum.Simulation.Ai.NpcRouteFlags.WaypointsDay, waypoints: [(25, 25)]);
        JumpToHour(scene, 3);
        // The night's waypoint step does not run before the standpoint step seats the NPC, so seat it by the standpoint step, then let the waypoint step meet a sleeper.
        scene.Host.RunStandpoints(scene.Npc);
        Assert.IsTrue(scene.Host.Sleeping(scene.Npc));
        Assert.IsTrue(scene.Host.RunWaypoints(scene.Npc), "a sleeper is taken over: the step answers 1");
        StringAssert.Contains(Route(scene), "bed: woken");
        Assert.IsFalse(scene.Host.Sleeping(scene.Npc));
        Assert.IsNull(occupancy.Occupant(BedKey));
    }

    [TestMethod]
    public void An_attack_on_the_sleeper_wakes_it_through_the_re_seat()
    {
        var occupancy = new AreaBedOccupancy();
        var scene = Open(occupancy);
        JumpToHour(scene, 3);
        scene.Host.RunStandpoints(scene.Npc);
        Assert.IsTrue(scene.Host.Sleeping(scene.Npc));
        scene.Host.HeroAttacked(scene.Npc);
        Assert.IsFalse(scene.Host.Sleeping(scene.Npc), scene.Host.TraceOf(scene.Npc)?.Route);
        Assert.IsNull(occupancy.Occupant(BedKey));
    }

    [TestMethod]
    public void A_host_built_again_seats_a_sleeper_the_session_kept_and_the_re_seat_can_wake_it()
    {
        // A load: the session's flag stays, the bed's occupant is not saved (a fresh occupancy).
        var occupancy = new AreaBedOccupancy();
        var again = Open(occupancy);
        // The session's saved flag, written as a load would restore it.
        Assert.AreEqual(GameSessionDisposition.Changed, again.Session.WriteObjectFlag(again.Session.Version, again.Npc, SessionObjectFlagField.Critter, 0x1000, true, false).Disposition);
        JumpToHour(again, 3);
        Assert.IsTrue(again.Host.Sleeping(again.Npc));
        Assert.IsNull(occupancy.Occupant(BedKey));
        again.Host.RunStandpoints(again.Npc);
        Assert.AreEqual(again.Npc, occupancy.Occupant(BedKey), "the first bed question of the host seated the saved sleeper again");
        JumpToHour(again, 12);
        again.Host.RunStandpoints(again.Npc);
        StringAssert.Contains(Route(again), "woken");
        Assert.IsFalse(again.Host.Sleeping(again.Npc));
    }

    [TestMethod]
    public void A_sleeper_that_does_not_stand_on_the_beds_tile_is_not_moved()
    {
        var occupancy = new AreaBedOccupancy();
        var bed = new CellCoord(11, 20);
        var scene = AreaAiRouteTests.Open(AreaAiRouteTests.Npc(0, [], 0, (DayStand.X, DayStand.Y), (bed.X, bed.Y)), beds: new AreaAiBeds(occupancy, () => [(BedKey, bed)]));
        JumpToHour(scene, 3);
        scene.Host.RunStandpoints(scene.Npc);
        Assert.AreEqual(scene.Npc, occupancy.Occupant(BedKey), Route(scene));
        Assert.AreEqual(new CellCoord(10, 20), scene.Host.Tile(scene.Npc));
        Assert.IsTrue(scene.Host.Hidden(scene.Npc));
    }

    [TestMethod]
    public void Waking_gives_the_flags_back_and_the_bed_revision_counts_each_change()
    {
        var occupancy = new AreaBedOccupancy();
        var scene = Open(occupancy);
        JumpToHour(scene, 3);
        var before = occupancy.Revision;
        scene.Host.RunStandpoints(scene.Npc);
        Assert.AreEqual(before + 1, occupancy.Revision);
        JumpToHour(scene, 12);
        scene.Host.RunStandpoints(scene.Npc);
        Assert.AreEqual(before + 2, occupancy.Revision);
        Assert.IsTrue(scene.Session.QueryObjectFlags(scene.Npc).IsEmpty, "both session writes give back the data's values");
    }

    private static void WriteSavedSleeper(AreaAiRouteTests.Scene scene) =>
        Assert.AreEqual(GameSessionDisposition.Changed, scene.Session.WriteObjectFlag(scene.Session.Version, scene.Npc, SessionObjectFlagField.Critter, 0x1000, true, false).Disposition);

    [TestMethod]
    public void A_load_into_a_save_where_the_npc_stood_awake_frees_the_bed_it_held_and_the_npc_is_seated_by_its_own_step()
    {
        // The occupancy outlives the session: an NPC seated at 03:00, then a load of a save where it stands awake at its standpoint.
        var occupancy = new AreaBedOccupancy();
        var scene = Open(occupancy);
        Assert.IsTrue(occupancy.Seat(BedKey, scene.Npc));
        JumpToHour(scene, 3);
        scene.Host.RunStandpoints(scene.Npc);
        StringAssert.Contains(Route(scene), "bed: seated");
        Assert.AreEqual(scene.Npc, occupancy.Occupant(BedKey));
        Assert.IsTrue(scene.Host.Sleeping(scene.Npc));
    }

    [TestMethod]
    public void An_attack_before_the_first_step_of_a_loaded_host_still_wakes_the_saved_sleeper()
    {
        var occupancy = new AreaBedOccupancy();
        var scene = Open(occupancy);
        WriteSavedSleeper(scene);
        scene.Host.HeroAttacked(scene.Npc);
        Assert.IsFalse(scene.Host.Sleeping(scene.Npc), scene.Host.TraceOf(scene.Npc)?.Route);
        Assert.IsNull(occupancy.Occupant(BedKey));
    }

    [TestMethod]
    public void A_saved_sleeper_whose_bed_was_taken_is_woken_by_the_re_seat_instead_of_sleeping_for_ever()
    {
        var occupancy = new AreaBedOccupancy();
        Assert.IsTrue(occupancy.Seat(BedKey, "pc"));
        var scene = Open(occupancy);
        WriteSavedSleeper(scene);
        JumpToHour(scene, 12);
        scene.Host.RunStandpoints(scene.Npc);
        StringAssert.Contains(Route(scene), "stranded sleeper woken");
        Assert.IsFalse(scene.Host.Sleeping(scene.Npc));
        Assert.AreEqual("pc", occupancy.Occupant(BedKey), "the hero's bed is not touched");
    }

    [TestMethod]
    public void A_killed_sleeper_leaves_its_bed()
    {
        var occupancy = new AreaBedOccupancy();
        var scene = Open(occupancy);
        JumpToHour(scene, 3);
        scene.Host.RunStandpoints(scene.Npc);
        Assert.AreEqual(scene.Npc, occupancy.Occupant(BedKey));
        scene.Host.NoticeKill(scene.Npc, null);
        Assert.IsNull(occupancy.Occupant(BedKey));
        Assert.IsFalse(scene.Host.Sleeping(scene.Npc));
    }

    [TestMethod]
    public void An_attack_on_a_seated_sleeper_goes_on_after_the_wake_the_victim_turns_and_reacts()
    {
        // The seated sleeper is not drawn (flag 0x100): the attack event must not stop at "victim gone" once the wake has given the flag back.
        var occupancy = new AreaBedOccupancy();
        var scene = Open(occupancy);
        JumpToHour(scene, 3);
        scene.Host.RunStandpoints(scene.Npc);
        Assert.IsTrue(scene.Host.Hidden(scene.Npc));
        scene.Host.HeroAttacked(scene.Npc);
        Assert.IsFalse(scene.Host.Hidden(scene.Npc));
        Assert.AreNotEqual("VictimGone", scene.Host.TraceOf(scene.Npc)?.Event, "the event went on after the wake");
    }

    [TestMethod]
    public void The_nudge_is_refused_when_the_hero_stands_on_the_neighbour_tile()
    {
        var occupancy = new AreaBedOccupancy();
        var scene = AreaAiRouteTests.Open(AreaAiRouteTests.Npc(0, [], 0, (DayStand.X, DayStand.Y), (NightStand.X, NightStand.Y)), hero: () => new CellCoord(11, 21),
            beds: new AreaAiBeds(occupancy, () => [(BedKey, NightStand)]));
        JumpToHour(scene, 3);
        scene.Host.RunStandpoints(scene.Npc);
        Assert.AreEqual(scene.Npc, occupancy.Occupant(BedKey));
        Assert.AreEqual(new CellCoord(10, 20), scene.Host.Tile(scene.Npc), "the hero's tile is not taken: " + Route(scene));
        StringAssert.Contains(Route(scene), "no tile to move to");
    }
}
