using Arcanum.Session;
using Arcanum.Data.Area;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Party;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-AI-2084 (ledger GW1-GW8, 0x4AEE50): the guard witnesses of an act on a portal, container or scenery: the square of 10 tiles, the guard class (the name needs a notify NPC), sight or hearing, the catching-thief script,
/// the attack event of a done crime, the shout of an awake witness of an attempt with the actor as its listener. Invented NPCs (the rebind tests' fixture), no original data.
/// </summary>
[TestClass]
public sealed class AreaAiGuardWitnessTests
{
    private const int SocialClassSlot = 85, GuardClass = 5, Portal = 1, Scenery = 3, Item = 4;
    private static readonly CellCoord Near = new(60, 14), Door = new(61, 13);
    private const string Door1 = "door-1";

    private static AreaObjectRecord WithGuardClass(AreaObjectRecord row, int socialClass)
    {
        AreaFieldValue[] Swap(IReadOnlyList<AreaFieldValue> fields) => [.. fields.Select(f => f.Slot == SocialClassSlot ? f with { Number = socialClass } : f)];
        return row with { DirectFields = Swap(row.DirectFields), EffectiveFields = Swap(row.EffectiveFields) };
    }

    private sealed class OpenField : ILineOfFireMap<AreaObjectRecord>
    {
        private static readonly (int X, int Y)[] Offsets = [(-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1)];
        public bool Contains(CellCoord tile) => tile.X >= 0 && tile.Y >= 0 && tile.X < 128 && tile.Y < 128;
        public CellCoord? Step(CellCoord from, int rotation) { var next = new CellCoord(from.X + Offsets[rotation].X, from.Y + Offsets[rotation].Y); return Contains(next) ? next : null; }
        public IEnumerable<LineOfFirePiece<AreaObjectRecord>> Edge(CellCoord tile, int edge) => [];
        public IEnumerable<LineOfFireObject<AreaObjectRecord>> Objects(CellCoord tile) => [];
    }

    private static (AreaAiHostRebindTests.Fixture F, AreaAiHost Host) Open(int witnessClass, CellCoord? witness = null, Func<string, string, string, bool>? script = null)
    {
        var f = AreaAiHostRebindTests.Open(changeB: row => WithGuardClass(row, witnessClass));
        var scene = f.Scene(0, AreaAiHostRebindTests.Sector(0, 0), [AreaAiHostRebindTests.NpcA(f), new(f.B, f.RowB, witness ?? Near)]) with { LineOfFire = () => new OpenField(), GuardCatchingThief = script };
        return (f, f.Build(scene));
    }

    [TestMethod]
    public void A_guard_near_a_portal_that_sees_the_hero_gets_the_attack_event_of_a_done_crime()
    {
        var (f, host) = Open(GuardClass);
        using var _ = f;

        var reports = host.GuardWitnesses(AiSchedulerRules.Pc, Door1, Portal, notifyNpc: 6402, Door, GuardWitnessMode.Done, Loudness.Loud);

        Assert.AreEqual(1, reports.Count, host.TraceOf(f.B)?.Setter);
        Assert.AreEqual(new AreaAiHost.GuardWitnessReport(f.B, ReactionLowered: false, Attacked: true, Shouted: false), reports[0]);
        Assert.IsNotNull(host.TraceOf(f.B)?.Event, "the attack event ran on the witness");
    }

    [TestMethod]
    public void An_awake_witness_of_an_attempt_shouts_its_line_to_the_actor()
    {
        var (f, host) = Open(GuardClass);
        using var _ = f;
        var heard = new List<(string Speaker, LoyaltyLines Lines, string? Listener)>();
        host.AiBarks = (speaker, lines, listener) => heard.Add((speaker, lines, listener));

        var reports = host.GuardWitnesses(AiSchedulerRules.Pc, Door1, Portal, 6402, Door, GuardWitnessMode.Attempt, Loudness.Loud);

        Assert.AreEqual(1, reports.Count);
        Assert.IsTrue(reports[0].Shouted);
        CollectionAssert.AreEqual(new[] { (f.B, GuardWitnessRules.ShoutLines, (string?)AiSchedulerRules.Pc) }, heard, "the line is the witness's, the listener the actor");
    }

    [TestMethod]
    public void A_done_crime_makes_no_line()
    {
        var (f, host) = Open(GuardClass);
        using var _ = f;
        var heard = 0;
        // The attack event's own reaction may flee and say its 3000-3099 line; only the shout's range counts.
        host.AiBarks = (_, lines, _) => heard += lines == GuardWitnessRules.ShoutLines ? 1 : 0;

        var reports = host.GuardWitnesses(AiSchedulerRules.Pc, Door1, Portal, 6402, Door, GuardWitnessMode.Done, Loudness.Silent);

        Assert.AreEqual(1, reports.Count, "the guard saw the hero (a silent act is still seen) and was called: " + host.TraceOf(f.B)?.Setter);
        Assert.AreEqual(0, heard, "mode 0 never floats");
    }

    [TestMethod]
    public void No_notify_npc_a_non_guard_a_far_witness_or_an_object_of_another_type_calls_nobody()
    {
        var (f, host) = Open(GuardClass);
        using var _ = f;
        Assert.AreEqual(0, host.GuardWitnesses(AiSchedulerRules.Pc, Door1, Portal, notifyNpc: 0, Door, GuardWitnessMode.Done, Loudness.Loud).Count, "a portal with no notify NPC");
        Assert.AreEqual(0, host.GuardWitnesses(AiSchedulerRules.Pc, Door1, Item, 6402, Door, GuardWitnessMode.Done, Loudness.Loud).Count, "an item");
        Assert.AreEqual(0, host.GuardWitnesses(AiSchedulerRules.Pc, Door1, Portal, 6402, new CellCoord(40, 14), GuardWitnessMode.Done, Loudness.Loud).Count, "the witness is 20 tiles from the door");
        Assert.AreEqual(1, host.GuardWitnesses(AiSchedulerRules.Pc, Door1, Scenery, 0, Door, GuardWitnessMode.Done, Loudness.Loud).Count, "scenery has no notify NPC and still calls a guard");

        var (plain, plainHost) = Open(witnessClass: 0);
        using var __ = plain;
        Assert.AreEqual(0, plainHost.GuardWitnesses(AiSchedulerRules.Pc, Door1, Scenery, 0, Door, GuardWitnessMode.Done, Loudness.Loud).Count, "scenery: a non-guard never matches -1");
        Assert.AreEqual(0, plainHost.GuardWitnesses(AiSchedulerRules.Pc, Door1, Portal, 6402, Door, GuardWitnessMode.Done, Loudness.Loud).Count, "a portal: a non-guard whose name is not the notify NPC");
    }

    [TestMethod]
    public void The_witness_must_see_or_hear_and_its_script_must_run_the_default()
    {
        var edge = new CellCoord(75, 14);
        var (far, farHost) = Open(GuardClass, edge);
        using var _ = far;
        Assert.AreEqual(0, farHost.GuardWitnesses(AiSchedulerRules.Pc, Door1, Portal, 6402, new CellCoord(70, 14), GuardWitnessMode.Done, Loudness.Silent).Count, "a silent act 17 tiles from the guard is neither seen nor heard");
        // The positive control is A_done_crime_makes_no_line: the same silent act beside the guard is seen, so the 0 above is the perception's.

        var asked = new List<(string, string, string)>();
        var (skipped, skippedHost) = Open(GuardClass, script: (witness, actor, guarded) => { asked.Add((witness, actor, guarded)); return false; });
        using var __ = skipped;
        Assert.AreEqual(0, skippedHost.GuardWitnesses(AiSchedulerRules.Pc, Door1, Portal, 6402, Door, GuardWitnessMode.Done, Loudness.Loud).Count, "a skipped default leaves the witness out");
        CollectionAssert.AreEqual(new[] { (skipped.B, AiSchedulerRules.Pc, Door1) }, asked, "the witness is the attachee, the actor the triggerer, the object the extra");
    }

    [TestMethod]
    public void A_non_guard_whose_name_is_the_notify_npc_is_a_witness_and_a_follower_of_the_hero_is_a_valid_actor()
    {
        var (f, host) = Open(witnessClass: 0);
        using var _ = f;
        // Both fixture NPCs carry name number 1 and stand in the square, so both are witnesses by name.
        Assert.AreEqual(2, host.GuardWitnesses(AiSchedulerRules.Pc, Door1, Portal, notifyNpc: 1, Door, GuardWitnessMode.Done, Loudness.Loud).Count, "the name is the notify NPC");

        var (led, ledHost) = Open(GuardClass);
        using var __ = led;
        Assert.AreEqual(GameSessionDisposition.Changed, led.Session.ForceJoinParty(led.Session.Version, led.A).Disposition);
        Assert.IsNotNull(ledHost.PcLeader(led.A), "the fixture NPC is now led by the hero");
        var reports = ledHost.GuardWitnesses(led.A, Door1, Portal, 6402, Door, GuardWitnessMode.Done, Loudness.Loud);
        Assert.AreEqual(1, reports.Count, "a follower of the hero acts for the hero: " + ledHost.TraceOf(led.B)?.Setter);
    }

    [TestMethod]
    public void An_attempt_asks_the_reaction_change_and_the_threshold_test_and_a_done_crime_does_neither()
    {
        var (f, host) = Open(GuardClass);
        using var _ = f;
        var attempt = host.GuardWitnesses(AiSchedulerRules.Pc, Door1, Portal, 6402, Door, GuardWitnessMode.Attempt, Loudness.Loud);
        Assert.AreEqual(1, attempt.Count);
        var trace = host.TraceOf(f.B)?.Setter;
        StringAssert.Contains(trace, $"mode={GuardWitnessMode.Attempt}", trace);
        // The reaction change is the session's (0x4C0DE0): it is asked (lowered) whenever the fixture holds the NPC's reaction facts, and the report says what the host did.
        Assert.AreEqual(attempt[0].ReactionLowered, trace!.Contains("lowered=True"));
        Assert.IsTrue(attempt[0].Shouted);

        var (g, doneHost) = Open(GuardClass);
        using var __ = g;
        var done = doneHost.GuardWitnesses(AiSchedulerRules.Pc, Door1, Portal, 6402, Door, GuardWitnessMode.Done, Loudness.Loud);
        Assert.IsFalse(done[0].ReactionLowered, "mode 0 lowers nothing");
        Assert.IsTrue(done[0].Attacked);
    }

    [TestMethod]
    public void An_actor_that_is_no_player_character_or_follower_of_one_is_not_witnessed()
    {
        var (f, host) = Open(GuardClass);
        using var _ = f;
        Assert.AreEqual(0, host.GuardWitnesses(f.A, Door1, Portal, 6402, Door, GuardWitnessMode.Done, Loudness.Loud).Count, "an NPC no PC leads");
    }
}
