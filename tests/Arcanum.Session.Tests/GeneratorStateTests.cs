using Arcanum.Simulation;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>ARC-WORLD-1157 (E2-a, schema 91, ledger PO1 to PO8): a generator's tick in the session: the copies it makes as created critters, the counter table, the count made, the release at a death, the save. Invented numbers only.</summary>
[TestClass]
public sealed class GeneratorStateTests
{
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
        public bool HasCritter(CellCoord cell) => cell == new CellCoord(100, 201);
    }

    private const int Prototype = 28359;
    private const string Generator = "oid:gen-1", Other = "oid:gen-2";
    private static readonly CellCoord Home = new(100, 201);

    private static uint Packed(int options, int slot, int cap, int budget, int made = 0) => new GeneratorSettings(options, slot, cap, budget, made).Pack();

    private static SessionGeneratorRun Run(uint packed, string entity = Generator, int rate = 3) =>
        new() { Entity = entity, Map = Exterior, Cell = Home, Prototype = Prototype, Packed = packed, Rate = rate, DropMap = new OpenMap() };

    private static SessionLifeTarget Facts(string entity) => SessionLifeTarget.Critter(entity, new LifeFacts { HitPointPoints = 10, FatiguePoints = 10 }, LifeState.Unharmed);

    [TestMethod]
    public void A_tick_makes_one_critter_near_the_generator_and_answers_the_rates_delay()
    {
        using var owner = Create();
        var result = owner.RunGenerator(owner.Version, Run(Packed(GeneratorSettings.ByDay, 7, 3, 5)));
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, 3_600_000L), (result.Disposition, result.Failure, result.DelayMs));
        var copy = result.Made.Single();
        Assert.AreEqual((SessionCreatedKind.Critter, "oid:critter:1", Prototype, Exterior), (copy.Kind, copy.Entity, copy.Prototype, copy.Map));
        Assert.IsTrue(Math.Abs(copy.Cell.X - Home.X) <= 5 && Math.Abs(copy.Cell.Y - Home.Y) <= 5, "the spot is at most five tiles away");
        Assert.AreEqual((1, false), owner.QueryGeneratorSlot(7));
        Assert.AreEqual(1, owner.QueryGeneratorMade(Generator));
        Assert.AreEqual(7, owner.QueryGeneratorCopy(copy.Entity));
        // Only the encounter flag is not written: a generator's creature is no encounter creature (PO5, PO9).
        Assert.AreEqual(0, owner.QueryObjectFlags(copy.Entity).CritterSet);
        CollectionAssert.AreEqual(new[] { copy.Entity }, owner.TakeGeneratedCritters().ToArray());
        Assert.AreEqual(0, owner.TakeGeneratedCritters().Count, "the queue is taken once");
    }

    // ARC-WORLD-1526 (GS5): the copy is of the generator object, so the created record keeps the generator as its template (the world builds the copy's row from the generator's fields); the link is saved.
    [TestMethod]
    public void A_copy_keeps_the_generator_it_copies_as_its_template_and_a_save_keeps_it()
    {
        using var owner = Create();
        var made = owner.RunGenerator(owner.Version, Run(Packed(GeneratorSettings.ByDay | GeneratorSettings.Group, 12, 3, 9))).Made;
        Assert.AreEqual(3, made.Count);
        Assert.IsTrue(made.All(copy => copy.Summoner == Generator && copy.SpellFlags == 0), "a generator's copy carries the generator and no spell flag");
        Assert.IsTrue(made.All(copy => owner.QueryCreatedObject(copy.Entity)!.Summoner == Generator));
        using var back = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        Assert.IsTrue(made.All(copy => back.QueryCreatedObject(copy.Entity)!.Summoner == Generator && back.QueryGeneratorCopy(copy.Entity) == 12));
    }

    [TestMethod]
    public void A_generator_named_as_the_absent_role_is_refused_before_any_effect()
    {
        using var owner = Create();
        var revision = owner.Version.Revision;
        Assert.AreEqual(GameSessionFailure.InvalidCreatedObject, owner.RunGenerator(owner.Version, Run(Packed(GeneratorSettings.ByDay, 4, 2, 9), SessionEventKinds.AbsentRole)).Failure);
        Assert.AreEqual(revision, owner.Version.Revision);
    }

    [TestMethod]
    public void The_cap_stops_the_ticks_and_a_death_frees_one_place_but_not_the_budget()
    {
        using var owner = Create();
        var packed = Packed(GeneratorSettings.ByDay, 7, 2, 3);
        var made = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            var result = owner.RunGenerator(owner.Version, Run(packed));
            Assert.AreEqual(3_600_000L, result.DelayMs, "the spawner answers the delay whatever was made");
            made.AddRange(result.Made.Select(m => m.Entity));
        }
        Assert.AreEqual(2, made.Count, "the cap of two");
        Assert.AreEqual((2, false), owner.QueryGeneratorSlot(7));
        // A death releases the slot (PO7): the next tick makes the third and last creature the budget allows.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Kill(owner.Version, Facts(made[0])).Disposition);
        Assert.AreEqual((1, false), owner.QueryGeneratorSlot(7));
        Assert.AreEqual(1, owner.RunGenerator(owner.Version, Run(packed)).Made.Count);
        Assert.AreEqual(3, owner.QueryGeneratorMade(Generator));
        // The budget is spent: a death frees a place and nothing is made, the count made is never given back.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Kill(owner.Version, Facts(made[1])).Disposition);
        Assert.AreEqual(0, owner.RunGenerator(owner.Version, Run(packed)).Made.Count);
        Assert.AreEqual(3, owner.QueryGeneratorMade(Generator));
    }

    [TestMethod]
    public void An_unlimited_budget_keeps_making_and_does_not_count()
    {
        using var owner = Create();
        var packed = Packed(GeneratorSettings.ByDay | GeneratorSettings.Unlimited, 9, 1, 0);
        for (var i = 0; i < 3; i++)
        {
            var copy = owner.RunGenerator(owner.Version, Run(packed)).Made.Single();
            Assert.AreEqual(Home, copy.Cell, "a cap 1 generator makes its creature on its own tile");
            owner.Kill(owner.Version, Facts(copy.Entity));
        }
        Assert.IsNull(owner.QueryGeneratorMade(Generator), "nothing is counted");
        Assert.AreEqual((0, false), owner.QueryGeneratorSlot(9));
    }

    [TestMethod]
    public void A_cap_of_one_draws_nothing_and_a_group_makes_up_to_the_room_in_one_tick()
    {
        using var owner = Create();
        var before = owner.QueryRandom().Draws;
        Assert.AreEqual(1, owner.RunGenerator(owner.Version, Run(Packed(GeneratorSettings.ByDay, 1, 1, 9))).Made.Count);
        Assert.AreEqual(before, owner.QueryRandom().Draws, "a cap of 1 puts the copy on the generator's tile with no draw");
        var group = owner.RunGenerator(owner.Version, Run(Packed(GeneratorSettings.ByDay | GeneratorSettings.Group, 2, 4, 9), Other));
        Assert.AreEqual(4, group.Made.Count);
        Assert.AreEqual((4, false), owner.QueryGeneratorSlot(2));
        Assert.AreEqual(4, owner.QueryGeneratorMade(Other));
        Assert.IsTrue(owner.QueryRandom().Draws > before);
    }

    [TestMethod]
    public void The_hour_the_block_and_the_view_gate_the_tick_and_two_generators_share_a_slot()
    {
        using var owner = Create();
        // The session starts at hour 15: a night generator makes nothing, the answer is still the delay.
        var night = owner.RunGenerator(owner.Version, Run(Packed(GeneratorSettings.ByNight, 5, 2, 9)));
        Assert.AreEqual((GameSessionDisposition.NoChange, 0, 3_600_000L), (night.Disposition, night.Made.Count, night.DelayMs));
        // A blocked slot makes nothing; the toggle (script action 110) is its own revision and toggles back.
        var day = Packed(GeneratorSettings.ByDay, 5, 2, 9);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ToggleGeneratorBlock(owner.Version, 5).Disposition);
        Assert.AreEqual((0, true), owner.QueryGeneratorSlot(5));
        Assert.AreEqual(0, owner.RunGenerator(owner.Version, Run(day)).Made.Count);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ToggleGeneratorBlock(owner.Version, 5).Disposition);
        Assert.AreEqual(1, owner.RunGenerator(owner.Version, Run(day)).Made.Count);
        // The off-screen option needs the generator's rectangle off the view.
        var hidden = Packed(GeneratorSettings.ByDay | GeneratorSettings.OffScreen, 6, 2, 9);
        Assert.AreEqual(0, owner.RunGenerator(owner.Version, Run(hidden) with { OffView = false }).Made.Count);
        Assert.AreEqual(1, owner.RunGenerator(owner.Version, Run(hidden) with { OffView = true }).Made.Count);
        // Two generators of one slot share the live count (cap 2): the first tick of the other makes the second.
        Assert.AreEqual(1, owner.RunGenerator(owner.Version, Run(day, Other)).Made.Count);
        Assert.AreEqual(0, owner.RunGenerator(owner.Version, Run(day, Other)).Made.Count);
        Assert.AreEqual((2, false), owner.QueryGeneratorSlot(5));
    }

    [TestMethod]
    public void A_night_session_makes_the_night_generators_creatures()
    {
        var result = GameSessionOwner.Create(new(Profile(hour: 22), Flags(), Start, null));
        using var owner = result.Owner!;
        Assert.AreEqual(1, owner.RunGenerator(owner.Version, Run(Packed(GeneratorSettings.ByNight, 5, 2, 9))).Made.Count);
        Assert.AreEqual(0, owner.RunGenerator(owner.Version, Run(Packed(GeneratorSettings.ByDay, 6, 2, 9), Other)).Made.Count);
    }

    [TestMethod]
    public void A_rejected_spot_test_leaves_the_generators_own_tile_after_ten_tries()
    {
        using var owner = Create();
        var before = owner.QueryRandom().Draws;
        var copy = owner.RunGenerator(owner.Version, Run(Packed(GeneratorSettings.ByDay, 3, 2, 9)) with { SpotAccepted = _ => false }).Made.Single();
        Assert.AreEqual(Home, copy.Cell);
        Assert.AreEqual(before + 20, owner.QueryRandom().Draws, "ten tries of a distance and a direction");
    }

    [TestMethod]
    public void A_destroyed_copy_releases_nothing_and_loses_its_mark()
    {
        using var owner = Create();
        var copy = owner.RunGenerator(owner.Version, Run(Packed(GeneratorSettings.ByDay, 4, 2, 9))).Made.Single();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.DestroyCreatedObject(owner.Version, copy.Entity).Disposition);
        Assert.AreEqual((1, false), owner.QueryGeneratorSlot(4), "a destroy leaves the slot counted (PO7)");
        Assert.IsNull(owner.QueryGeneratorCopy(copy.Entity));
    }

    [TestMethod]
    public void A_stale_version_and_invalid_input_are_refused_before_any_effect()
    {
        using var owner = Create();
        var stale = owner.Version;
        owner.ToggleGeneratorBlock(owner.Version, 200);
        var revision = owner.Version.Revision;
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.RunGenerator(stale, Run(Packed(GeneratorSettings.ByDay, 4, 2, 9))).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidCreatedObject, owner.RunGenerator(owner.Version, Run(Packed(GeneratorSettings.ByDay, 4, 2, 9), rate: 8)).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidCreatedObject, owner.ToggleGeneratorBlock(owner.Version, 256).Failure);
        Assert.AreEqual(revision, owner.Version.Revision);
        Assert.IsNull(owner.QueryGeneratorSlot(-1));
    }

    [TestMethod]
    public void The_table_the_counts_and_the_marks_survive_a_save_and_a_death_still_releases()
    {
        using var owner = Create();
        var packed = Packed(GeneratorSettings.ByDay | GeneratorSettings.Group, 12, 3, 9);
        var made = owner.RunGenerator(owner.Version, Run(packed)).Made;
        owner.ToggleGeneratorBlock(owner.Version, 200);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"generators\":{\"slots\":[{\"slot\":12,\"live\":3,\"blocked\":false},{\"slot\":200,\"live\":0,\"blocked\":true}],\"made\":[{\"entity\":\"oid:gen-1\",\"made\":3}]");
        using var back = Restore(Decode(text));
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(back.Capture()));
        Assert.AreEqual((3, false), back.QueryGeneratorSlot(12));
        Assert.AreEqual((0, true), back.QueryGeneratorSlot(200));
        Assert.AreEqual(3, back.QueryGeneratorMade(Generator));
        Assert.AreEqual(12, back.QueryGeneratorCopy(made[0].Entity));
        Assert.AreEqual(0, back.TakeGeneratedCritters().Count, "the pending queue is not saved");
        back.Kill(back.Version, Facts(made[0].Entity));
        Assert.AreEqual((2, false), back.QueryGeneratorSlot(12));
    }

    [TestMethod]
    public void A_copy_that_is_no_created_critter_a_duplicate_slot_or_a_live_count_past_the_cap_is_refused_on_restore()
    {
        using var owner = Create();
        var made = owner.RunGenerator(owner.Version, Run(Packed(GeneratorSettings.ByDay, 12, 3, 9))).Made.Single();
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        var good = $"\"copies\":[{{\"entity\":\"{made.Entity}\",\"slot\":12}}]";
        StringAssert.Contains(text, good);
        void Refused(string changed)
        {
            var decoded = GameSessionSnapshotCodec.TryDecode(changed);
            if (decoded.Snapshot is null) return;
            Assert.AreEqual(GameSessionFailure.InvalidSnapshot, GameSessionOwner.Restore(decoded.Snapshot, RestoreOptions()).Failure);
        }
        Refused(text.Replace(good, "\"copies\":[{\"entity\":\"oid:critter:9\",\"slot\":12}]"));
        Refused(text.Replace(good, "\"copies\":[{\"entity\":\"oid:other\",\"slot\":12}]"));
        Refused(text.Replace(good, $"\"copies\":[{{\"entity\":\"{made.Entity}\",\"slot\":12}},{{\"entity\":\"{made.Entity}\",\"slot\":13}}]"));
        Refused(text.Replace("\"slots\":[{\"slot\":12,\"live\":1,\"blocked\":false}]", "\"slots\":[{\"slot\":12,\"live\":1,\"blocked\":false},{\"slot\":12,\"live\":2,\"blocked\":false}]"));
        Refused(text.Replace("\"slots\":[{\"slot\":12,\"live\":1,\"blocked\":false}]", "\"slots\":[{\"slot\":12,\"live\":0,\"blocked\":false}]"));
    }

    [TestMethod]
    public void A_schema_90_save_upgrades_to_the_empty_generators_section()
    {
        using var owner = Create();
        var current = GameSessionSnapshotCodec.Encode(owner.Capture());
        var older = SnapshotUpgradeTests.Schema90(current);
        Assert.IsFalse(older.Contains("\"generators\""));
        var upgraded = Decode(older);
        Assert.AreEqual(GameSessionSnapshot.CurrentSchemaVersion, upgraded.SchemaVersion);
        Assert.AreEqual(current, GameSessionSnapshotCodec.Encode(upgraded));
        var damaged = GameSessionSnapshotCodec.TryDecode(older.Replace(",\"flights\":", NoGenerators + ",\"flights\":"));
        Assert.AreEqual(SnapshotDecodeFailure.UnknownField, damaged.Failure, "an older text that already holds the section is damaged");
    }
}
