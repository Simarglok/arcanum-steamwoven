using Arcanum.Simulation.Travel;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Travel;

/// <summary>ARC-WORLD-1159 (E2-c, ledger PO15–PO18): the roll of the world-map random encounter over invented tables and a scripted draw.</summary>
[TestClass]
public sealed class EncounterRulesTests
{
    private sealed class Scripted(params int[] values) : IEncounterRolls
    {
        private readonly Queue<int> _values = new(values);
        public List<(int Min, int Max)> Asked { get; } = [];
        public int Roll(int min, int max)
        {
            Asked.Add((min, max));
            var value = _values.Dequeue();
            Assert.IsTrue(value >= min && value <= max, $"the draw {value} is inside {min}..{max}");
            return value;
        }
        public int Left => _values.Count;
    }

    // Terrain 1 (base 0), power 2 (the default), day: table 0 + ... = 1 - 8 + (2 + 3) * 2 = 3? night 0: 0 - 8 + 10 = 2; the tables are keyed by that index below.
    private static readonly EncounterPlace Grass = new(new(1000, 1000), 0, 0, false, false);

    private static EncounterTables Tables(IReadOnlyList<EncounterRegion>? frequency = null, IReadOnlyList<EncounterRegion>? power = null, IReadOnlyList<EncounterRegion>? over = null,
        params (int Table, EncounterRow[] Rows)[] rows)
    {
        var messages = new List<(int, string)> { (10000, "0, 0, 1, 5%"), (20000, "0, 0, 1, Average"), (30000, "0, 0, 1, 0"), (49999, "0") };
        var loaded = EncounterTables.Parse(messages);
        // The parser's output is immutable; build the tables through the loader's own text so the rows are what a file would give.
        var text = new List<(int, string)>(messages);
        void Chart(int @base, IReadOnlyList<EncounterRegion>? regions, Func<EncounterRegion, string> value)
        {
            if (regions is null) return;
            text.RemoveAll(m => m.Item1 == @base);
            for (var i = 0; i < regions.Count; i++) text.Add((@base + i, $"{regions[i].X}, {regions[i].Y}, {regions[i].Radius}, {value(regions[i])}"));
        }
        Chart(10000, frequency, r => r.Value + "%");
        Chart(20000, power, r => new[] { "none", "easy", "average", "powerful" }[r.Value]);
        Chart(30000, over, r => r.Value.ToString());
        foreach (var (table, tableRows) in rows)
            for (var i = 0; i < tableRows.Length; i++)
            {
                var row = tableRows[i];
                var groups = string.Join(", ", row.Groups.Select((g, n) => $"{new[] { "First", "Second", "Third", "Fourth", "Fifth" }[n]}: {g.Prototype}, {g.Min}-{g.Max}"));
                var keys = $", MinLevel: {row.MinLevel}, MaxLevel: {row.MaxLevel}" + (row.GlobalFlag >= 0 ? $", GlobalFlag: {row.GlobalFlag}" : "") + (row.TriggerCap >= 0 ? $", TriggerCount: {row.TriggerCap}" : "");
                text.Add((50000 + table * 100 + i, $"{row.Weight}%, {groups}{keys}"));
            }
        Assert.AreEqual(EncounterTablesStatus.Loaded, loaded.Status);
        return EncounterTables.Parse(text);
    }

    private static EncounterRow Row(int weight, int proto, int min = 1, int max = 1, int minLevel = 0, int maxLevel = 100, int flag = -1, int cap = -1) =>
        new(0, weight, [new(proto, min, max)], minLevel, maxLevel, flag, cap);

    private static EncounterWho Who(int hour = 12, int level = 5, Func<int, bool>? flags = null, Func<int, int>? uses = null) =>
        new(hour, level, flags ?? (_ => false), uses ?? (_ => 0));

    // The day, average power, terrain value 1 (terrain base 0) table.
    private const int GrassDayAverage = 2;

    [TestMethod]
    public void The_first_region_that_covers_the_location_gives_the_value_by_the_chebyshev_distance()
    {
        var chart = new[] { new EncounterRegion(0, 0, 10, 7), new EncounterRegion(5, 5, 100, 9) };
        Assert.AreEqual(7, EncounterRules.RegionValue(chart, new(10, -10)));
        Assert.AreEqual(9, EncounterRules.RegionValue(chart, new(11, 0)), "the first circle does not reach; the second does");
        Assert.IsNull(EncounterRules.RegionValue(chart, new(500, 500)));
    }

    [TestMethod]
    public void The_terrain_value_needs_both_types_to_be_known_and_have_a_value()
    {
        Assert.AreEqual(1, EncounterRules.TerrainValue(0, 0));
        Assert.AreEqual(3, EncounterRules.TerrainValue(1, 0), "the base type's value");
        Assert.AreEqual(0, EncounterRules.TerrainValue(2, 0), "type 2 has no value");
        Assert.AreEqual(0, EncounterRules.TerrainValue(0, 9), "nor type 9");
        Assert.AreEqual(0, EncounterRules.TerrainValue(19, 0), "nor an unknown type");
        Assert.AreEqual(0, EncounterRules.TerrainValue(-1, -1), "a sector outside the grid");
        Assert.AreEqual(9, EncounterRules.TerrainValue(6, 6));
    }

    [TestMethod]
    public void The_night_is_before_six_and_from_eighteen_and_the_table_index_follows_the_original()
    {
        Assert.IsTrue(EncounterRules.IsNight(5)); Assert.IsFalse(EncounterRules.IsNight(6)); Assert.IsFalse(EncounterRules.IsNight(17)); Assert.IsTrue(EncounterRules.IsNight(18));
        Assert.AreEqual(0, EncounterRules.TableIndex(0, 1, 1), "terrain 1, easy, day");
        Assert.AreEqual(1, EncounterRules.TableIndex(1, 1, 1));
        Assert.AreEqual(GrassDayAverage, EncounterRules.TableIndex(0, 2, 1));
        Assert.AreEqual(5, EncounterRules.TableIndex(1, 3, 1));
        Assert.AreEqual(6, EncounterRules.TableIndex(0, 1, 2), "the next terrain starts six tables on");
    }

    [TestMethod]
    public void The_chance_defaults_to_five_per_cent_and_grows_fivefold_while_the_rest_window_is_open()
    {
        var tables = Tables(rows: (GrassDayAverage, [Row(10, 700)]));
        Assert.AreEqual(EncounterOutcome.ChanceMissed, EncounterRules.Roll(tables, Grass, Who(), new Scripted(6)).Outcome);
        var rolls = new Scripted(5, 1, 1);
        Assert.IsTrue(EncounterRules.Roll(tables, Grass, Who(), rolls).Fired, "a roll of 5 passes 5 per cent");
        Assert.AreEqual((1, 100), rolls.Asked[0]);
        var resting = Grass with { RestWindowOpen = true };
        Assert.IsTrue(EncounterRules.Roll(tables, resting, Who(), new Scripted(25, 1, 1)).Fired, "25 passes five times five");
        Assert.AreEqual(EncounterOutcome.ChanceMissed, EncounterRules.Roll(tables, resting, Who(), new Scripted(26)).Outcome);
    }

    [TestMethod]
    public void A_region_of_the_frequency_chart_sets_the_chance_and_a_zero_never_passes()
    {
        var tables = Tables(frequency: [new(1000, 1000, 50, 20), new(0, 0, 1_000_000, 0)], rows: (GrassDayAverage, [Row(10, 700)]));
        Assert.IsTrue(EncounterRules.Roll(tables, Grass, Who(), new Scripted(20, 1, 1)).Fired);
        Assert.AreEqual(EncounterOutcome.ChanceMissed, EncounterRules.Roll(tables, Grass, Who(), new Scripted(21)).Outcome);
        var far = Grass with { Location = new(5000, 5000) };
        Assert.AreEqual(EncounterOutcome.ChanceMissed, EncounterRules.Roll(tables, far, Who(), new Scripted(1)).Outcome, "a chance of 0 misses even a roll of 1");
    }

    [TestMethod]
    public void A_zero_power_a_town_map_sector_and_no_terrain_value_stop_the_roll_after_the_chance_draw()
    {
        var none = Tables(power: [new(1000, 1000, 50, 0)], rows: (GrassDayAverage, [Row(10, 700)]));
        var rolls = new Scripted(1);
        Assert.AreEqual(EncounterOutcome.NoPower, EncounterRules.Roll(none, Grass, Who(), rolls).Outcome);
        Assert.AreEqual(0, rolls.Left);
        var tables = Tables(rows: (GrassDayAverage, [Row(10, 700)]));
        Assert.AreEqual(EncounterOutcome.TownMapSector, EncounterRules.Roll(tables, Grass with { InTownMapSector = true }, Who(), new Scripted(1)).Outcome);
        Assert.AreEqual(EncounterOutcome.NoTerrain, EncounterRules.Roll(tables, Grass with { TerrainBase = 2 }, Who(), new Scripted(1)).Outcome);
    }

    [TestMethod]
    public void The_power_and_the_night_pick_the_table_and_an_override_region_replaces_it()
    {
        var tables = Tables(power: [new(1000, 1000, 50, 3)], over: [new(1000, 1000, 5, 7)],
            rows: (5, [Row(10, 111)]));
        // Day, power 3 (powerful), terrain 1: index 0 - 8 + (3 + 3) * 2 = 4; the night adds one: 5 = the row table above.
        Assert.AreEqual(EncounterOutcome.NoEligibleRow, EncounterRules.Roll(tables, Grass with { Location = new(1000, 1010) }, Who(hour: 12), new Scripted(1)).Outcome);
        var night = EncounterRules.Roll(tables, Grass with { Location = new(1000, 1010) }, Who(hour: 22), new Scripted(1, 1, 1));
        Assert.IsTrue(night.Fired);
        Assert.AreEqual((5, 111), (night.Table, night.Prototypes!.Single()));
        // The override circle covers the location: table 7 is the index, with no rows: nothing eligible.
        Assert.AreEqual((EncounterOutcome.NoEligibleRow, 7), (EncounterRules.Roll(tables, Grass, Who(hour: 22), new Scripted(1)).Outcome, EncounterRules.Roll(tables, Grass, Who(hour: 22), new Scripted(1)).Table));
        var outside = Tables(over: [new(0, 0, 1, 900)], rows: (GrassDayAverage, [Row(10, 1)]));
        Assert.AreEqual(EncounterOutcome.Fired, EncounterRules.Roll(outside, Grass, Who(), new Scripted(1, 1, 1)).Outcome, "an override region that does not cover the location changes nothing");
        var wild = Tables(over: [new(1000, 1000, 5, 900)], rows: (GrassDayAverage, [Row(10, 1)]));
        Assert.AreEqual((EncounterOutcome.TableOutOfRange, 900), (EncounterRules.Roll(wild, Grass, Who(), new Scripted(1)).Outcome, 900));
    }

    [TestMethod]
    public void Rows_are_eligible_by_level_flag_and_use_cap()
    {
        var tables = Tables(rows: (GrassDayAverage, [Row(10, 1, minLevel: 6, maxLevel: 9), Row(10, 2, flag: 40), Row(10, 3, cap: 2), Row(10, 4)]));
        var rows = tables.Tables[GrassDayAverage].ToArray();
        Assert.IsFalse(EncounterRules.Eligible(rows[0], Who(level: 5))); Assert.IsTrue(EncounterRules.Eligible(rows[0], Who(level: 6)));
        Assert.IsTrue(EncounterRules.Eligible(rows[0], Who(level: 9))); Assert.IsFalse(EncounterRules.Eligible(rows[0], Who(level: 10)));
        Assert.IsFalse(EncounterRules.Eligible(rows[1], Who())); Assert.IsTrue(EncounterRules.Eligible(rows[1], Who(flags: f => f == 40)));
        Assert.IsTrue(EncounterRules.Eligible(rows[2], Who(uses: _ => 1))); Assert.IsFalse(EncounterRules.Eligible(rows[2], Who(uses: _ => 2)));
        Assert.IsFalse(EncounterRules.Eligible(rows[3], Who(level: -1)), "no hero: no row");
    }

    [TestMethod]
    public void The_pick_takes_a_row_when_the_roll_is_below_its_weight_and_the_last_one_when_it_runs_off_the_end()
    {
        var tables = Tables(rows: (GrassDayAverage, [Row(10, 1), Row(10, 2), Row(10, 3)]));
        int Pick(int roll) => EncounterRules.Roll(tables, Grass, Who(), new Scripted(1, roll, 1)).Prototypes!.Single();
        Assert.AreEqual(1, Pick(1)); Assert.AreEqual(1, Pick(9));
        Assert.AreEqual(2, Pick(10), "a roll equal to a weight falls to the next row: the walk compares strictly");
        Assert.AreEqual(2, Pick(19)); Assert.AreEqual(3, Pick(20)); Assert.AreEqual(3, Pick(29));
        Assert.AreEqual(3, Pick(30), "the roll that equals the whole sum runs off the end onto the last eligible row");
        var skipping = Tables(rows: (GrassDayAverage, [Row(10, 1, minLevel: 50), Row(10, 2)]));
        var rolls = new Scripted(1, 5, 1);
        Assert.AreEqual(2, EncounterRules.Roll(skipping, Grass, Who(), rolls).Prototypes!.Single());
        Assert.AreEqual((1, 10), rolls.Asked[1], "the pick roll is over the eligible weights only");
    }

    [TestMethod]
    public void A_row_with_no_creature_still_counts_as_used_and_a_ranged_group_draws_its_size()
    {
        var empty = Tables(rows: (GrassDayAverage, [Row(10, 9, 0, 0)]));
        var none = EncounterRules.Roll(empty, Grass, Who(), new Scripted(1, 1));
        Assert.AreEqual((EncounterOutcome.NoMonsters, 50000 + GrassDayAverage * 100), (none.Outcome, none.UsedRow ?? -1));
        var ranged = Tables(rows: (GrassDayAverage, [new EncounterRow(0, 10, [new(1, 2, 4), new(2, 3, 3)], 0, 100, -1, -1)]));
        var rolls = new Scripted(1, 1, 3, 50);
        var fired = EncounterRules.Roll(ranged, Grass, Who(), rolls);
        CollectionAssert.AreEqual(new[] { 3, 3 }, fired.Sizes!.ToArray(), "the ranged group draws 2..4, the fixed one draws nothing");
        Assert.AreEqual((2, 4), rolls.Asked[2]);
        Assert.IsTrue(fired.West, "an orientation roll of 50 puts the groups west");
        Assert.IsFalse(EncounterRules.Roll(ranged, Grass, Who(), new Scripted(1, 1, 3, 51)).West);
        Assert.AreEqual(0, rolls.Left);
    }

    [TestMethod]
    public void The_creatures_stand_on_rings_west_or_north_of_the_location()
    {
        var at = new TravelLocation(100, 200);
        Assert.AreEqual((94L, 201L), EncounterRules.Spot(at, true, 0));
        Assert.AreEqual((95L, 201L), EncounterRules.Spot(at, true, 1));
        Assert.AreEqual((95L, 202L), EncounterRules.Spot(at, true, 2));
        Assert.AreEqual((95L, 200L), EncounterRules.Spot(at, true, 3));
        Assert.AreEqual((96L, 201L), EncounterRules.Spot(at, true, 4));
        Assert.AreEqual((100L, 195L), EncounterRules.Spot(at, false, 0));
        Assert.AreEqual((101L, 196L), EncounterRules.Spot(at, false, 2));
        Assert.AreEqual((101L, 195L), EncounterRules.Spot(at, false, 1));
        Assert.AreEqual(3_000_000L, EncounterRules.DelayMs(50));
    }
}
