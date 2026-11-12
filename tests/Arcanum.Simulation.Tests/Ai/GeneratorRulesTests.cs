using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Ai;

/// <summary>ARC-WORLD-1157 (E2-a, ledger PO1 to PO6): the generator's packed settings, the rate table, the day test, the spawner's gates and quota, and the placement's tries. Invented numbers only.</summary>
[TestClass]
public sealed class GeneratorRulesTests
{
    private const long Noon = 12 * GeneratorRules.HourMs, Midnight = 0;

    [TestMethod]
    public void The_dword_unpacks_to_options_slot_cap_budget_and_made_and_packs_back()
    {
        var settings = new GeneratorSettings(Options: 0b10101, Slot: 200, Cap: 7, Budget: 100, Made: 33);
        var packed = settings.Pack();
        Assert.AreEqual((0b10101u << 27) | (200u << 19) | (7u << 14) | (100u << 7) | 33u, packed);
        Assert.AreEqual(settings, GeneratorSettings.Unpack(packed));
        Assert.AreEqual(new GeneratorSettings(0x1F, 255, 31, 127, 127), GeneratorSettings.Unpack(uint.MaxValue));
        Assert.AreEqual(new GeneratorSettings(0, 0, 0, 0, 0), GeneratorSettings.Unpack(0));
    }

    [TestMethod]
    public void The_rate_is_bits_21_to_23_of_the_flag_word_and_names_the_next_ticks_delay()
    {
        Assert.AreEqual(5, GeneratorRules.RateOf(0x80000 | (5u << 21) | 0x2));
        long[] expected = [1_000, 30_000, 60_000, 3_600_000, 86_400_000, 7 * 86_400_000L, 30 * 86_400_000L, 360 * 86_400_000L];
        for (var rate = 0; rate < 8; rate++) Assert.AreEqual(expected[rate], GeneratorRules.NextDelayMs(rate), $"rate {rate}");
    }

    [TestMethod]
    public void Day_is_hour_6_to_17_of_the_game_clocks_hour()
    {
        Assert.AreEqual(17, GeneratorRules.HourOf(17 * GeneratorRules.HourMs + 3_599_999));
        Assert.AreEqual(1, GeneratorRules.HourOf(25 * GeneratorRules.HourMs), "the hour wraps at 24");
        Assert.IsFalse(GeneratorRules.IsDay(5 * GeneratorRules.HourMs + 3_599_999));
        Assert.IsTrue(GeneratorRules.IsDay(6 * GeneratorRules.HourMs));
        Assert.IsTrue(GeneratorRules.IsDay(17 * GeneratorRules.HourMs + 3_599_999));
        Assert.IsFalse(GeneratorRules.IsDay(18 * GeneratorRules.HourMs));
    }

    [TestMethod]
    public void The_spawner_asks_one_creature_only_with_room_an_open_slot_the_hours_option_and_budget_left()
    {
        var day = new GeneratorSettings(GeneratorSettings.ByDay, 1, 3, 5, 0);
        Assert.AreEqual(1, GeneratorRules.Request(day, 0, false, Noon, true));
        Assert.AreEqual(0, GeneratorRules.Request(day, 3, false, Noon, true), "no room: the cap is reached");
        Assert.AreEqual(0, GeneratorRules.Request(day, 4, false, Noon, true), "a live count past the cap is no room");
        Assert.AreEqual(0, GeneratorRules.Request(day, 0, true, Noon, true), "a blocked slot");
        Assert.AreEqual(0, GeneratorRules.Request(day, 0, false, Midnight, true), "the day option at night");
        Assert.AreEqual(1, GeneratorRules.Request(day with { Options = GeneratorSettings.ByNight }, 0, false, Midnight, true));
        Assert.AreEqual(0, GeneratorRules.Request(day with { Options = GeneratorSettings.ByNight }, 0, false, Noon, true), "the night option by day");
        Assert.AreEqual(0, GeneratorRules.Request(day with { Options = 0 }, 0, false, Noon, true), "neither hour option");
    }

    [TestMethod]
    public void The_group_option_asks_up_to_the_room_and_the_budget_cuts_it()
    {
        var group = new GeneratorSettings(GeneratorSettings.ByDay | GeneratorSettings.Group, 1, 6, 10, 0);
        Assert.AreEqual(6, GeneratorRules.Request(group, 0, false, Noon, true));
        Assert.AreEqual(4, GeneratorRules.Request(group, 2, false, Noon, true));
        Assert.AreEqual(3, GeneratorRules.Request(group with { Made = 7 }, 0, false, Noon, true), "the room is cut to the budget left");
        Assert.AreEqual(0, GeneratorRules.Request(group with { Made = 10 }, 0, false, Noon, true), "a spent budget makes nothing");
        Assert.AreEqual(6, GeneratorRules.Request(group with { Options = group.Options | GeneratorSettings.Unlimited, Made = 10 }, 0, false, Noon, true), "an unlimited budget is ignored");
    }

    [TestMethod]
    public void The_off_screen_option_needs_the_generators_rectangle_off_the_view()
    {
        var hidden = new GeneratorSettings(GeneratorSettings.ByDay | GeneratorSettings.OffScreen, 1, 2, 2, 0);
        Assert.AreEqual(1, GeneratorRules.Request(hidden, 0, false, Noon, offView: true));
        Assert.AreEqual(0, GeneratorRules.Request(hidden, 0, false, Noon, offView: false));
        Assert.AreEqual(1, GeneratorRules.Request(hidden with { Options = GeneratorSettings.ByDay }, 0, false, Noon, offView: false), "without the option the view is not asked");
    }

    // Every step is open: a path of one step a tile.
    private sealed class OpenMap : IDropMap
    {
        private static readonly (int X, int Y)[] Offsets = [(-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1)];
        public CellCoord? Step(CellCoord from, int rotation) => rotation == 8 ? from : new(from.X + Offsets[rotation].X, from.Y + Offsets[rotation].Y);
        public DropPath Path(CellCoord from, CellCoord to, DropOwner who)
        {
            var steps = new List<int>();
            for (var at = from; at != to;)
            {
                var rotation = Array.IndexOf(Offsets, (Math.Sign(to.X - at.X), Math.Sign(to.Y - at.Y)));
                steps.Add(rotation);
                at = Step(at, rotation)!.Value;
            }
            return new(steps.Count, 0, steps);
        }
        // The generator stands on its own tile (the critter the walk steps off, 0x4407C0).
        public bool HasCritter(CellCoord cell) => cell == new CellCoord(10, 10);
    }

    private sealed class Rolls(params int[] draws) : IDropRolls
    {
        private int _next;
        public List<(DropRoll Purpose, int Min, int Max)> Asked { get; } = [];
        public int Roll(DropRoll purpose, int min, int max) { Asked.Add((purpose, min, max)); return draws[_next++]; }
    }

    [TestMethod]
    public void The_placement_draws_a_distance_of_one_to_five_then_a_direction_and_takes_the_first_accepted_spot()
    {
        // Distance 3, direction 5 (x + 1): the target is three tiles on; the open path is three steps, under distance + 2, and the walk stops on the first tile with no critter.
        var rolls = new Rolls(3, 5);
        var spot = DropPlacementRules.PlaceGenerated(new(10, 10), new OpenMap(), rolls, _ => true);
        Assert.AreEqual(new CellCoord(11, 10), spot, "the walk leaves the generator's tile (a critter stands there) and stops on the first free tile");
        CollectionAssert.AreEqual(new[] { (DropRoll.Distance, 1, 5), (DropRoll.Direction, 0, 8) }, rolls.Asked);
    }

    [TestMethod]
    public void A_rejected_spot_costs_a_try_and_ten_failures_leave_the_generators_own_tile()
    {
        var rolls = new Rolls(Enumerable.Repeat(new[] { 2, 5 }, 10).SelectMany(pair => pair).ToArray());
        var tried = new List<CellCoord>();
        var spot = DropPlacementRules.PlaceGenerated(new(10, 10), new OpenMap(), rolls, candidate => { tried.Add(candidate); return false; });
        Assert.AreEqual(new CellCoord(10, 10), spot);
        Assert.AreEqual(10, tried.Count, "ten tries, two draws each");
        Assert.AreEqual(20, rolls.Asked.Count);
        // The next try starts after a rejection: the second spot is accepted.
        var second = DropPlacementRules.PlaceGenerated(new(10, 10), new OpenMap(), new Rolls(2, 5, 4, 7), candidate => candidate.X == 10 && candidate.Y == 9);
        Assert.AreEqual(new CellCoord(10, 9), second);
    }
}
