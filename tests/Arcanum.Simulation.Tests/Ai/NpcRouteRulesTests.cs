using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Goals;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Ai;

/// <summary>
/// ARC-DEV-117 (MIG9 S4b, ledger WS1–WS12): the forced waypoint and standpoint steps over invented facts. Each case names the branch of
/// 0x4ACBB0 / 0x4ACDB0 it follows; none needs the original's data.
/// </summary>
[TestClass]
public sealed class NpcRouteRulesTests
{
    private const int Map = 5, Noon = 12, Midnight = 0;
    private static readonly RouteLocation Origin = new(100, 100);

    private static NpcRouteFacts Facts(int hour = Noon, uint flags = NpcRouteFlags.WaypointsDay | NpcRouteFlags.WaypointsNight, RouteLocation? at = null,
        RouteLocation[]? waypoints = null, int index = 0, int teleportMap = Map, bool sleeping = false, bool hidden = false, bool leader = false,
        RouteLocation? day = null, RouteLocation? night = null) =>
        new(sleeping, hidden, leader, hour, flags, teleportMap, Map, at ?? Origin, waypoints ?? [new(110, 100), new(120, 100), new(130, 100)], index,
            day ?? new(50, 60), night ?? new(70, 80));

    [TestMethod]
    public void The_day_is_hour_6_to_20()
    {
        Assert.IsFalse(NpcRouteRules.IsDay(5));
        Assert.IsTrue(NpcRouteRules.IsDay(6));
        Assert.IsTrue(NpcRouteRules.IsDay(20));
        Assert.IsFalse(NpcRouteRules.IsDay(21));
        Assert.IsFalse(NpcRouteRules.IsDay(0));
        Assert.IsFalse(NpcRouteRules.IsDay(23));
    }

    [TestMethod]
    public void A_waypoint_farther_than_three_tiles_is_the_target_and_the_index_stays()
    {
        var step = NpcRouteRules.ForcedWaypoints(Facts(index: 1));
        Assert.AreEqual(NpcRouteOutcome.Relocate, step.Outcome);
        Assert.IsTrue(step.Answer);
        Assert.AreEqual(new RouteLocation(120, 100), step.Target);
        Assert.IsNull(step.NewWaypointIndex);
    }

    [TestMethod]
    public void A_waypoint_within_three_tiles_is_reached_and_the_next_one_is_the_target()
    {
        var step = NpcRouteRules.ForcedWaypoints(Facts(at: new(117, 103), index: 1));
        Assert.AreEqual(new RouteLocation(130, 100), step.Target);
        Assert.AreEqual(2, step.NewWaypointIndex);
        var four = NpcRouteRules.ForcedWaypoints(Facts(at: new(116, 100), index: 1));
        Assert.AreEqual(new RouteLocation(120, 100), four.Target, "four tiles away is not reached");
        Assert.IsNull(four.NewWaypointIndex);
    }

    [TestMethod]
    public void The_last_waypoint_wraps_to_the_first_and_a_single_waypoint_to_itself()
    {
        var wrap = NpcRouteRules.ForcedWaypoints(Facts(at: new(130, 100), index: 2));
        Assert.AreEqual(0, wrap.NewWaypointIndex);
        Assert.AreEqual(new RouteLocation(110, 100), wrap.Target);
        var one = NpcRouteRules.ForcedWaypoints(Facts(at: new(100, 100), waypoints: [new(101, 100)]));
        Assert.AreEqual(0, one.NewWaypointIndex);
        Assert.AreEqual(new RouteLocation(101, 100), one.Target);
    }

    [TestMethod]
    public void The_distance_is_signed_across_the_unsigned_words()
    {
        // 0xFFFFFFFF and 0 are 4,294,967,295 tiles apart, not 1.
        var far = NpcRouteRules.ForcedWaypoints(Facts(at: new(0, 0), waypoints: [new(uint.MaxValue, 0)]));
        Assert.IsNull(far.NewWaypointIndex);
        Assert.AreEqual(new RouteLocation(uint.MaxValue, 0), far.Target);
    }

    [TestMethod]
    public void The_day_waypoint_flag_gates_the_day_and_the_night_flag_the_night()
    {
        Assert.AreEqual(NpcRouteOutcome.NoStep, NpcRouteRules.ForcedWaypoints(Facts(Noon, NpcRouteFlags.WaypointsNight)).Outcome);
        Assert.AreEqual(NpcRouteOutcome.Relocate, NpcRouteRules.ForcedWaypoints(Facts(Noon, NpcRouteFlags.WaypointsDay)).Outcome);
        Assert.AreEqual(NpcRouteOutcome.NoStep, NpcRouteRules.ForcedWaypoints(Facts(Midnight, NpcRouteFlags.WaypointsDay)).Outcome);
        Assert.AreEqual(NpcRouteOutcome.Relocate, NpcRouteRules.ForcedWaypoints(Facts(Midnight, NpcRouteFlags.WaypointsNight)).Outcome);
        Assert.AreEqual(NpcRouteOutcome.Relocate, NpcRouteRules.ForcedWaypoints(Facts(20, NpcRouteFlags.WaypointsDay)).Outcome);
        Assert.AreEqual(NpcRouteOutcome.Relocate, NpcRouteRules.ForcedWaypoints(Facts(21, NpcRouteFlags.WaypointsNight)).Outcome);
        Assert.AreEqual(NpcRouteOutcome.NoStep, NpcRouteRules.ForcedWaypoints(Facts(Noon, 0)).Outcome);
    }

    [TestMethod]
    public void An_empty_list_or_a_hidden_npc_does_nothing_and_a_sleeping_hidden_one_goes_on()
    {
        Assert.AreEqual(NpcRouteOutcome.NoStep, NpcRouteRules.ForcedWaypoints(Facts(waypoints: [])).Outcome);
        var hidden = NpcRouteRules.ForcedWaypoints(Facts(hidden: true));
        Assert.AreEqual(NpcRouteOutcome.NoStep, hidden.Outcome);
        Assert.IsFalse(hidden.Answer);
        Assert.AreEqual(NpcRouteOutcome.Sleeping, NpcRouteRules.ForcedWaypoints(Facts(hidden: true, sleeping: true)).Outcome, "the hidden test is skipped for a sleeper");
        Assert.AreEqual(NpcRouteOutcome.NoStep, NpcRouteRules.ForcedWaypoints(Facts(flags: 0, sleeping: true)).Outcome, "a sleeper still needs the day or night flag");
    }

    [TestMethod]
    public void A_sleeping_npc_is_taken_over_and_moves_nowhere()
    {
        var step = NpcRouteRules.ForcedWaypoints(Facts(sleeping: true));
        Assert.AreEqual(NpcRouteOutcome.Sleeping, step.Outcome);
        Assert.IsTrue(step.Answer);
        Assert.IsNull(step.Target);
        Assert.IsNull(step.NewWaypointIndex);
    }

    [TestMethod]
    public void The_teleport_map_is_set_to_the_open_map_while_it_is_zero_even_when_a_flag_ends_the_step()
    {
        var set = NpcRouteRules.ForcedWaypoints(Facts(teleportMap: 0));
        Assert.AreEqual(Map, set.TeleportMapSet);
        Assert.AreEqual(NpcRouteOutcome.Relocate, set.Outcome, "an unset map counts as the open one");
        Assert.AreEqual(Map, NpcRouteRules.ForcedWaypoints(Facts(Noon, 0, teleportMap: 0)).TeleportMapSet);
        Assert.IsNull(NpcRouteRules.ForcedWaypoints(Facts(teleportMap: 9)).TeleportMapSet);
        Assert.IsNull(NpcRouteRules.ForcedWaypoints(Facts(waypoints: [], teleportMap: 0)).TeleportMapSet, "an empty list ends the step before the map is read");
    }

    [TestMethod]
    public void A_teleport_map_other_than_the_open_one_makes_a_request_not_a_relocation()
    {
        var step = NpcRouteRules.ForcedWaypoints(Facts(teleportMap: 9, at: new(117, 103), index: 1));
        Assert.AreEqual(NpcRouteOutcome.RequestTeleport, step.Outcome);
        Assert.AreEqual(9, step.TeleportMap);
        Assert.AreEqual(new RouteLocation(130, 100), step.Target);
        Assert.AreEqual(2, step.NewWaypointIndex, "the index is stored before the request");
    }

    [TestMethod]
    public void An_index_outside_the_list_is_not_acted_on()
    {
        foreach (var index in new[] { -1, 3, 99 })
        {
            var step = NpcRouteRules.ForcedWaypoints(Facts(index: index));
            Assert.AreEqual(NpcRouteOutcome.IndexOutsideList, step.Outcome);
            Assert.IsFalse(step.Answer);
        }
    }

    [TestMethod]
    public void The_standpoint_is_the_day_one_by_day_and_the_night_one_by_night()
    {
        Assert.AreEqual(new RouteLocation(50, 60), NpcRouteRules.ForcedStandpoint(Facts(Noon)).Target);
        Assert.AreEqual(new RouteLocation(70, 80), NpcRouteRules.ForcedStandpoint(Facts(Midnight)).Target);
        Assert.AreEqual(new RouteLocation(50, 60), NpcRouteRules.ForcedStandpoint(Facts(6)).Target);
        Assert.AreEqual(new RouteLocation(70, 80), NpcRouteRules.ForcedStandpoint(Facts(21)).Target);
        Assert.AreEqual(NpcRouteOutcome.Relocate, NpcRouteRules.ForcedStandpoint(Facts(Noon)).Outcome);
    }

    [TestMethod]
    public void An_npc_within_the_standpoint_radius_is_at_it_and_a_wanderer_has_a_wider_radius()
    {
        var at = new RouteLocation(51, 61);
        var near = NpcRouteRules.ForcedStandpoint(Facts(Noon, 0, at: at));
        Assert.AreEqual(NpcRouteOutcome.AtStandpoint, near.Outcome);
        Assert.IsFalse(near.Answer, "no wander: the original answers 0");
        Assert.IsNull(near.Target);
        var out2 = new RouteLocation(52, 60);
        Assert.AreEqual(NpcRouteOutcome.Relocate, NpcRouteRules.ForcedStandpoint(Facts(Noon, 0, at: out2)).Outcome);
        var wander = NpcRouteRules.ForcedStandpoint(Facts(Noon, NpcRouteFlags.Wanders, at: new(54, 56)));
        Assert.AreEqual(NpcRouteOutcome.AtStandpoint, wander.Outcome);
        Assert.IsTrue(wander.Answer, "a wanderer starts a wander and answers 1");
        var dark = NpcRouteRules.ForcedStandpoint(Facts(Noon, NpcRouteFlags.WandersInDark, at: new(54, 56)));
        Assert.AreEqual(NpcRouteOutcome.AtStandpoint, dark.Outcome);
        Assert.IsTrue(dark.Answer, "WANDERS_IN_DARK also starts a wander and answers 1");
        Assert.AreEqual(NpcRouteOutcome.Relocate, NpcRouteRules.ForcedStandpoint(Facts(Noon, NpcRouteFlags.Wanders, at: new(55, 60))).Outcome);
        // ARC-AI-1313 (VAG2): the near branch names the wander goal the host starts, tethered to the standpoint.
        Assert.AreEqual(GoalType.Wander, wander.WanderGoal);
        Assert.AreEqual(GoalType.WanderSeekDarkness, dark.WanderGoal);
        Assert.AreEqual(new RouteLocation(50, 60), wander.Standpoint);
        Assert.IsNull(near.WanderGoal, "a plain NPC starts no wander");
    }

    [TestMethod]
    public void The_wander_goal_follows_the_flags_in_the_originals_order_and_a_sleeper_starts_none()
    {
        Assert.AreEqual(GoalType.Wander, NpcRouteRules.WanderGoalOf(NpcRouteFlags.Wanders));
        Assert.AreEqual(GoalType.WanderSeekDarkness, NpcRouteRules.WanderGoalOf(NpcRouteFlags.WandersInDark));
        Assert.AreEqual(GoalType.Wander, NpcRouteRules.WanderGoalOf(NpcRouteFlags.Wanders | NpcRouteFlags.WandersInDark), "0x1000 is tested first");
        Assert.IsNull(NpcRouteRules.WanderGoalOf(NpcRouteFlags.WaypointsDay));
        // The forced and the unforced pass agree: a wanderer asleep at its standpoint starts nothing, a waker at the standpoint of either hour starts its wander.
        var sleeper = NpcRouteRules.ForcedStandpoint(Facts(Noon, NpcRouteFlags.Wanders, at: new(54, 56), sleeping: true));
        Assert.IsNull(sleeper.WanderGoal);
        Assert.IsNull(NpcRouteRules.UnforcedStandpoint(Facts(Noon, NpcRouteFlags.Wanders, at: new(54, 56), sleeping: true), () => true).WanderGoal);
        var night = NpcRouteRules.UnforcedStandpoint(Facts(Midnight, NpcRouteFlags.WandersInDark, at: new(72, 82)), () => true);
        Assert.AreEqual(GoalType.WanderSeekDarkness, night.WanderGoal);
        Assert.AreEqual(new RouteLocation(70, 80), night.Standpoint, "the night standpoint is the tether at night");
        var far = NpcRouteRules.UnforcedStandpoint(Facts(Noon, NpcRouteFlags.Wanders, at: new(80, 90)), () => true);
        Assert.IsNull(far.WanderGoal, "a wanderer outside its radius walks to the standpoint first");
    }

    [TestMethod]
    public void The_steps_say_which_standpoint_a_near_npc_is_at_and_which_sleepers_go_through_the_re_seat()
    {
        // ARC-WORLD-1162 (SWB17, SWB21): the near branch names the current standpoint (the tile the bed is looked for on); a sleeper is re-seated by the waypoint step and by the far standpoint step, not by the near one.
        var near = NpcRouteRules.ForcedStandpoint(Facts(Noon, 0, at: new(51, 61)));
        Assert.AreEqual(new RouteLocation(50, 60), near.Standpoint);
        Assert.AreEqual(new RouteLocation(70, 80), NpcRouteRules.ForcedStandpoint(Facts(Midnight, 0, at: new(70, 80))).Standpoint);
        Assert.IsNull(NpcRouteRules.ForcedStandpoint(Facts(Noon, 0, at: new(20, 20))).Standpoint, "a far NPC is moved, not at it");
        Assert.IsFalse(near.Reseat);
        Assert.IsFalse(NpcRouteRules.ForcedStandpoint(Facts(sleeping: true, at: new(50, 60))).Reseat, "a sleeper near its standpoint is not woken");
        Assert.IsNull(NpcRouteRules.ForcedStandpoint(Facts(sleeping: true, at: new(50, 60))).Standpoint);
        Assert.IsTrue(NpcRouteRules.ForcedStandpoint(Facts(sleeping: true)).Reseat, "a sleeper far from the current standpoint goes through the re-seat (the hour switch)");
        Assert.IsTrue(NpcRouteRules.ForcedWaypoints(Facts(sleeping: true)).Reseat);
        Assert.IsTrue(NpcRouteRules.UnforcedWaypoints(Facts(sleeping: true)).Reseat);
        Assert.IsTrue(NpcRouteRules.UnforcedStandpoint(Facts(sleeping: true), () => true).Reseat);
        Assert.IsFalse(NpcRouteRules.UnforcedStandpoint(Facts(sleeping: true, at: new(50, 60)), () => true).Reseat);
        Assert.AreEqual(new RouteLocation(50, 60), NpcRouteRules.UnforcedStandpoint(Facts(Noon, 0, at: new(51, 61)), () => true).Standpoint);
    }

    [TestMethod]
    public void The_standpoint_step_skips_a_leader_and_a_hidden_npc_and_takes_over_a_sleeper()
    {
        Assert.AreEqual(NpcRouteOutcome.NoStep, NpcRouteRules.ForcedStandpoint(Facts(leader: true)).Outcome);
        Assert.AreEqual(NpcRouteOutcome.NoStep, NpcRouteRules.ForcedStandpoint(Facts(hidden: true)).Outcome);
        var sleeper = NpcRouteRules.ForcedStandpoint(Facts(sleeping: true, hidden: true));
        Assert.AreEqual(NpcRouteOutcome.Sleeping, sleeper.Outcome);
        Assert.IsTrue(sleeper.Answer);
        Assert.IsNull(sleeper.Target);
        var near = NpcRouteRules.ForcedStandpoint(Facts(sleeping: true, at: new(50, 60)));
        Assert.AreEqual(NpcRouteOutcome.Sleeping, near.Outcome);
        Assert.IsTrue(near.Answer, "a sleeper near its standpoint answers 1 without a wander flag");
    }

    [TestMethod]
    public void The_standpoint_step_sets_the_teleport_map_and_requests_a_teleport_for_another_map()
    {
        Assert.AreEqual(Map, NpcRouteRules.ForcedStandpoint(Facts(teleportMap: 0)).TeleportMapSet);
        Assert.IsNull(NpcRouteRules.ForcedStandpoint(Facts(leader: true, teleportMap: 0)).TeleportMapSet);
        var request = NpcRouteRules.ForcedStandpoint(Facts(teleportMap: 3));
        Assert.AreEqual(NpcRouteOutcome.RequestTeleport, request.Outcome);
        Assert.AreEqual(3, request.TeleportMap);
        Assert.AreEqual(new RouteLocation(50, 60), request.Target);
    }

    // ---- ARC-DEV-170 (B06): the unforced steps, which start a walk goal instead of relocating ----

    [TestMethod]
    public void An_unforced_waypoint_step_starts_the_walk_to_the_current_waypoint_with_mode_three()
    {
        var step = NpcRouteRules.UnforcedWaypoints(Facts(index: 1));
        Assert.AreEqual(NpcRouteOutcome.StartWalk, step.Outcome);
        Assert.IsTrue(step.Answer);
        Assert.AreEqual(new RouteLocation(120, 100), step.Target);
        Assert.AreEqual(3, step.Mode);
        Assert.IsNull(step.NewWaypointIndex);
    }

    [TestMethod]
    public void An_unforced_waypoint_step_within_three_tiles_moves_on_to_the_next_waypoint()
    {
        var step = NpcRouteRules.UnforcedWaypoints(Facts(at: new(117, 103), index: 1));
        Assert.AreEqual(NpcRouteOutcome.StartWalk, step.Outcome);
        Assert.AreEqual(new RouteLocation(130, 100), step.Target);
        Assert.AreEqual(2, step.NewWaypointIndex);
    }

    [TestMethod]
    public void An_unforced_waypoint_step_answers_0_for_another_map_a_missing_flag_and_an_empty_list_but_still_sets_the_teleport_map()
    {
        var other = NpcRouteRules.UnforcedWaypoints(Facts(teleportMap: Map + 1));
        Assert.AreEqual(NpcRouteOutcome.NoStep, other.Outcome);
        Assert.IsFalse(other.Answer);
        Assert.IsNull(other.Target);
        // The forced step of the same NPC asks for its teleport instead.
        Assert.AreEqual(NpcRouteOutcome.RequestTeleport, NpcRouteRules.ForcedWaypoints(Facts(teleportMap: Map + 1)).Outcome);
        var unset = NpcRouteRules.UnforcedWaypoints(Facts(teleportMap: 0, flags: 0));
        Assert.AreEqual(NpcRouteOutcome.NoStep, unset.Outcome);
        Assert.AreEqual(Map, unset.TeleportMapSet, "the getter wrote the open map before the flag test ended the step");
        Assert.AreEqual(NpcRouteOutcome.NoStep, NpcRouteRules.UnforcedWaypoints(Facts(waypoints: [])).Outcome);
        Assert.AreEqual(NpcRouteOutcome.NoStep, NpcRouteRules.UnforcedWaypoints(Facts(hidden: true)).Outcome);
        Assert.AreEqual(NpcRouteOutcome.Sleeping, NpcRouteRules.UnforcedWaypoints(Facts(sleeping: true)).Outcome);
    }

    [TestMethod]
    public void An_unforced_standpoint_step_walks_to_the_standpoint_with_mode_one_and_stops_at_it()
    {
        var far = NpcRouteRules.UnforcedStandpoint(Facts(hour: Noon), () => throw new AssertFailedException("no roll outside hour 6 and 21"));
        Assert.AreEqual(NpcRouteOutcome.StartWalk, far.Outcome);
        Assert.IsTrue(far.Answer);
        Assert.AreEqual(new RouteLocation(50, 60), far.Target);
        Assert.AreEqual(1, far.Mode);
        var night = NpcRouteRules.UnforcedStandpoint(Facts(hour: Midnight), () => false);
        Assert.AreEqual(new RouteLocation(70, 80), night.Target);
        var near = NpcRouteRules.UnforcedStandpoint(Facts(at: new(51, 60)), () => false);
        Assert.AreEqual(NpcRouteOutcome.AtStandpoint, near.Outcome);
        Assert.IsFalse(near.Answer);
        Assert.IsNull(near.Target);
    }

    [TestMethod]
    public void An_unforced_standpoint_step_goes_on_at_hour_6_and_21_only_on_a_roll_and_skips_a_leader_and_another_map()
    {
        var asked = 0;
        Func<bool> roll(bool hit) => () => { asked++; return hit; };
        foreach (var hour in new[] { 6, 21 })
        {
            Assert.AreEqual(NpcRouteOutcome.NoStep, NpcRouteRules.UnforcedStandpoint(Facts(hour: hour), roll(false)).Outcome, "hour " + hour + " without the roll");
            Assert.AreEqual(NpcRouteOutcome.StartWalk, NpcRouteRules.UnforcedStandpoint(Facts(hour: hour), roll(true)).Outcome, "hour " + hour + " with the roll");
        }
        Assert.AreEqual(4, asked);
        Assert.AreEqual(NpcRouteOutcome.NoStep, NpcRouteRules.UnforcedStandpoint(Facts(leader: true), roll(true)).Outcome);
        Assert.AreEqual(NpcRouteOutcome.NoStep, NpcRouteRules.UnforcedStandpoint(Facts(teleportMap: Map + 1, hour: 6), roll(true)).Outcome);
        Assert.AreEqual(4, asked, "a leader and another map end the step before the roll");
        // A sleeper farther away is taken over; a wanderer within four tiles is at its standpoint.
        Assert.AreEqual(NpcRouteOutcome.Sleeping, NpcRouteRules.UnforcedStandpoint(Facts(sleeping: true), roll(true)).Outcome);
        var wanderer = NpcRouteRules.UnforcedStandpoint(Facts(at: new(54, 60), flags: NpcRouteFlags.Wanders), roll(true));
        Assert.AreEqual(NpcRouteOutcome.AtStandpoint, wanderer.Outcome);
        Assert.IsTrue(wanderer.Answer);
    }
}
