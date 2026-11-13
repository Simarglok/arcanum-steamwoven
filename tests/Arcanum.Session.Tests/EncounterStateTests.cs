using Arcanum.Scripting.GlobalFlags;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Travel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>ARC-WORLD-1159 (E2-c, schema 97, ledger PO15–PO19): the Random Encounters event in the session: the chain a host with tables arms, the roll at its firing, the critters it creates, the use counts and the save. Invented tables.</summary>
[TestClass]
public sealed class EncounterStateTests
{
    private sealed class PlainWorld : ISessionEventWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }

    // The spell caster's pass world holds the host's world inside a wrapper; the chain must arm through it too.
    private sealed class WrappingWorld(ISessionEventWorld wrapped) : ISessionWrappingEventWorld
    {
        public ISessionEventWorld Wrapped => wrapped;
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => wrapped.CritterFacts(map, entity);
    }

    // Every step is open; the host's critters stand nowhere.
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
        public bool HasCritter(CellCoord cell) => false;
    }

    private sealed class Host(EncounterTables? tables) : ISessionEncounterWorld
    {
        public EncounterTables? EncounterTables { get; } = tables;
        public SessionEncounterSite? Site { get; set; }
        public SessionEncounterSite? EncounterSite() => Site;
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }

    private static readonly CellCoord Hero = new(1000, 1000);
    private static readonly EncounterPlace Place = new(new(1000, 1000), 0, 0, false, false);

    // Always passes the chance (100 per cent over the place), terrain value 1 with the default power: table 2 by day, 3 by night, both given the same rows.
    private static EncounterTables Tables(string row0 = "10%, First: 4001, 2, Second: 4002, 1", string? row1 = null)
    {
        var messages = new List<(int, string)> { (10000, "0, 0, 100000, 100%"), (20000, "0, 0, 1, Average"), (30000, "0, 0, 1, 0"), (49999, "0") };
        foreach (var table in new[] { 2, 3 }) { messages.Add((50000 + table * 100, row0)); if (row1 is not null) messages.Add((50000 + table * 100 + 1, row1)); }
        var tables = EncounterTables.Parse(messages);
        Assert.AreEqual(EncounterTablesStatus.Loaded, tables.Status);
        return tables;
    }

    private static GameSessionOwner Create()
    {
        var created = GameSessionOwner.Create(Descriptor() with { Hero = HeroStateTests.HalfElf() });
        Assert.AreEqual(GameSessionFailure.None, created.Failure);
        return created.Owner!;
    }

    private static Host HostWith(EncounterTables? tables = null, bool site = true) =>
        new(tables ?? Tables()) { Site = site ? new(Exterior, Place, Hero) { DropMap = new OpenMap() } : null };

    private static void Advance(GameSessionOwner owner, long ms) =>
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, ms, ClockAdvanceReason.ExplicitWait).Disposition);

    private static SessionEventPassResult Pass(GameSessionOwner owner, ISessionEventWorld world)
    {
        var result = owner.ProcessEvents(world);
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result;
    }

    private const long MinDelay = 300 * 60_000, MaxDelay = 700 * 60_000;

    [TestMethod]
    public void The_chain_arms_through_a_wrapped_pass_world()
    {
        using var owner = Create();
        Pass(owner, new WrappingWorld(HostWith()));
        Assert.AreEqual(1, owner.QueryRandomEncounterDeadlines().Count, "a pass that goes through the spell caster's wrapper arms the chain");
        Pass(owner, new WrappingWorld(new PlainWorld()));
        Assert.AreEqual(1, owner.QueryRandomEncounterDeadlines().Count);
    }

    [TestMethod]
    public void A_host_with_tables_arms_one_chain_event_and_a_plain_world_arms_none()
    {
        using var owner = Create();
        Pass(owner, new PlainWorld());
        Assert.AreEqual(0, owner.QueryRandomEncounterDeadlines().Count);
        Pass(owner, new Host(EncounterTables.Disabled("off")));
        Assert.AreEqual(0, owner.QueryRandomEncounterDeadlines().Count, "disabled tables arm nothing");
        var now = owner.QueryClockSnapshot().GameTimestampMs;
        Pass(owner, HostWith());
        var deadline = owner.QueryRandomEncounterDeadlines().Single();
        Assert.IsTrue(deadline - now is >= MinDelay and <= MaxDelay, "300 to 700 game minutes on");
        var revision = owner.Version.Revision;
        Pass(owner, HostWith());
        Assert.AreEqual(1, owner.QueryRandomEncounterDeadlines().Count, "a second pass keeps the one event");
        Assert.AreEqual(revision, owner.Version.Revision, "and changes nothing");
    }

    [TestMethod]
    public void The_event_re_arms_first_and_a_fired_roll_creates_the_rolled_creatures_on_the_ring()
    {
        using var owner = Create();
        var host = HostWith();
        Pass(owner, host);
        var first = owner.QueryRandomEncounterDeadlines().Single();
        Advance(owner, MaxDelay + 1);
        var revision = owner.Version.Revision;
        Pass(owner, host);
        var notice = owner.TakeEncounterNotices().Single();
        Assert.AreEqual((SessionEncounterOutcome.Fired, EncounterOutcome.Fired, true), (notice.Outcome, notice.Roll, notice.Rearmed));
        // The first creature of the second slot stands on the tile of the first slot's first one, so the original destroys it (0x559260 looks for another critter on its tile): two are made, one is lost.
        Assert.AreEqual((2, 1), (notice.Made.Count, notice.Lost));
        CollectionAssert.AreEqual(new[] { 4001, 4001 }, notice.Made.Select(m => m.Prototype).ToArray());
        Assert.IsTrue(notice.Made.All(m => m.Kind == SessionCreatedKind.Critter && m.Map == Exterior && m.Entity.StartsWith("oid:critter:")));
        Assert.IsTrue(notice.Made.All(m => owner.QueryObjectFlags(m.Entity).CritterSet == 0), "the roll sets no encounter flag (PO9)");
        // West: x 994.., y 1001 / 1002 for the group's creatures; north: x 1000.., y 995.. (the ring of 0x5594E0).
        var west = notice.Made[0].Cell == new CellCoord(994, 1001);
        var expected = west ? new[] { (994, 1001), (995, 1001) } : new[] { (1000, 995), (1001, 995) };
        CollectionAssert.AreEqual(expected, notice.Made.Select(m => (m.Cell.X, m.Cell.Y)).ToArray());
        Assert.AreEqual(1, owner.QueryRandomEncounterDeadlines().Count, "the chain goes on");
        Assert.IsTrue(owner.QueryRandomEncounterDeadlines().Single() > first, "a later deadline");
        Assert.IsTrue(owner.Version.Revision > revision);
        Assert.AreEqual(0, owner.TakeEncounterNotices().Count, "the notices are taken once");
        Assert.AreEqual(1, owner.QueryEncounterUses().Single().Count);
    }

    [TestMethod]
    public void A_creature_on_a_critters_tile_is_destroyed_and_a_blocked_tile_is_searched_again_from_the_hero()
    {
        using var owner = Create();
        var host = HostWith();
        // Every ring tile is blocked and the tile search from the hero lands on blocked tiles too: no creature.
        host.Site = host.Site! with { Blocked = _ => true };
        Pass(owner, host);
        Advance(owner, MaxDelay + 1);
        Pass(owner, host);
        var lost = owner.TakeEncounterNotices().Single();
        Assert.AreEqual((SessionEncounterOutcome.Fired, 0, 3), (lost.Outcome, lost.Made.Count, lost.Lost));
        Assert.AreEqual(0, owner.QueryCreatedObjects(Exterior).Count);
        // Only the first spot is blocked: its creature is searched for again six tiles from the hero (the host's open map) and made there.
        using var other = Create();
        var oneBlocked = new CellCoord[2];
        host.Site = host.Site! with { Blocked = cell => cell.X == 994 && cell.Y == 1001 || cell.X == 1000 && cell.Y == 995 };
        Pass(other, host);
        Advance(other, MaxDelay + 1);
        Pass(other, host);
        var notice = other.TakeEncounterNotices().Single();
        Assert.AreEqual(3, notice.Made.Count + notice.Lost);
        Assert.IsTrue(notice.Made.All(m => m.Cell != new CellCoord(994, 1001) && m.Cell != new CellCoord(1000, 995)));
        _ = oneBlocked;
    }

    [TestMethod]
    public void No_site_means_no_roll_but_the_chain_goes_on()
    {
        using var owner = Create();
        var host = HostWith(site: false);
        Pass(owner, host);
        Advance(owner, MaxDelay + 1);
        var draws = owner.QueryRandom().Draws;
        Pass(owner, host);
        var notice = owner.TakeEncounterNotices().Single();
        Assert.AreEqual((SessionEncounterOutcome.NoSite, true), (notice.Outcome, notice.Rearmed));
        Assert.AreEqual(draws + 1, owner.QueryRandom().Draws, "only the re-arming draws (0x559640 first)");
        Assert.AreEqual(1, owner.QueryRandomEncounterDeadlines().Count);
        Assert.AreEqual(0, owner.QueryEncounterUses().Count);
    }

    [TestMethod]
    public void A_cap_of_one_lets_each_row_fire_once_and_the_counts_are_saved()
    {
        using var owner = Create();
        // The day table and the night table each hold one row of cap one: however the hours fall, at most two events can fire.
        var host = HostWith(Tables("10%, First: 4001, 1, TriggerCount: 1"));
        Pass(owner, host);
        var outcomes = new List<(SessionEncounterOutcome Outcome, EncounterOutcome Roll)>();
        for (var i = 0; i < 6; i++)
        {
            Advance(owner, MaxDelay + 1);
            Pass(owner, host);
            var notice = owner.TakeEncounterNotices().Single();
            outcomes.Add((notice.Outcome, notice.Roll));
        }
        var uses = owner.QueryEncounterUses();
        Assert.IsTrue(uses.Count is >= 1 and <= 2 && uses.All(use => use.Count == 1 && use.Line is 50200 or 50300), "a row is counted once and no more");
        Assert.AreEqual(uses.Count, outcomes.Count(o => o.Outcome == SessionEncounterOutcome.Fired), "every row fired exactly once");
        Assert.IsTrue(outcomes.Where(o => o.Outcome != SessionEncounterOutcome.Fired).All(o => o.Roll == EncounterOutcome.NoEligibleRow), "the rest found no eligible row");
        // Saved and restored: the counts and the pending event come back, and a used row stays used up.
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"encounters\":{\"uses\":[{\"line\":" + uses[0].Line + ",\"count\":1}");
        using var back = Restore(Decode(text));
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(back.Capture()));
        CollectionAssert.AreEqual(owner.QueryRandomEncounterDeadlines().ToArray(), back.QueryRandomEncounterDeadlines().ToArray());
        CollectionAssert.AreEqual(uses.ToArray(), back.QueryEncounterUses().ToArray());
    }

    [TestMethod]
    public void A_row_that_needs_a_global_flag_waits_for_it()
    {
        using var owner = Create();
        var host = HostWith(Tables("10%, First: 4001, 1, GlobalFlag: 3"));
        Pass(owner, host);
        Advance(owner, MaxDelay + 1);
        Pass(owner, host);
        Assert.AreEqual(EncounterOutcome.NoEligibleRow, owner.TakeEncounterNotices().Single().Roll, "flag 3 is false in a new game");
        Advance(owner, MaxDelay + 1);
        var set = owner.Flags.Write(new GlobalFlagWriteRequest(new GlobalFlagRequestId(Guid.NewGuid()), owner.Flags.Version, new FlagId(3), true));
        Assert.AreEqual(GlobalFlagWriteDisposition.Applied, set.Disposition);
        Pass(owner, host);
        Assert.AreEqual(EncounterOutcome.Fired, owner.TakeEncounterNotices().Single().Roll);
    }

    [TestMethod]
    public void A_schema_91_save_upgrades_to_the_empty_encounters_section_and_a_damaged_one_is_refused()
    {
        using var owner = Create();
        var current = GameSessionSnapshotCodec.Encode(owner.Capture());
        var older = SnapshotUpgradeTests.Schema91(current);
        Assert.IsFalse(older.Contains("\"encounters\""));
        var upgraded = Decode(older);
        Assert.AreEqual(GameSessionSnapshot.CurrentSchemaVersion, upgraded.SchemaVersion);
        Assert.AreEqual(current, GameSessionSnapshotCodec.Encode(upgraded));
        var damaged = GameSessionSnapshotCodec.TryDecode(older.Replace(",\"lighting\":", NoEncounters + ",\"lighting\":"));
        Assert.AreEqual(SnapshotDecodeFailure.UnknownField, damaged.Failure, "an older text that already holds the section is damaged");
        foreach (var text in new[]
        {
            current.Replace(NoEncounters, ",\"encounters\":{\"uses\":[{\"line\":50000,\"count\":0}]}"),
            current.Replace(NoEncounters, ",\"encounters\":{\"uses\":[{\"line\":49999,\"count\":1}]}"),
            current.Replace(NoEncounters, ",\"encounters\":{\"uses\":[{\"line\":50000,\"count\":1},{\"line\":50000,\"count\":2}]}"),
        })
        {
            var decoded = GameSessionSnapshotCodec.TryDecode(text);
            Assert.IsTrue(decoded.Snapshot is null || GameSessionOwner.Restore(decoded.Snapshot, RestoreOptions()).Failure == GameSessionFailure.InvalidSnapshot);
        }
    }
}
