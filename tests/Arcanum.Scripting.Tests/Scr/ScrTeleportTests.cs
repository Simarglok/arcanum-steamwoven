using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>The teleport actions and the guard conditions of set S4 (ARC-SCRIPT-014). Programs, objects and numbers are
/// invented.</summary>
[TestClass]
public sealed class ScrTeleportTests
{
    private const byte Triggerer = 0, Attachee = 1;
    private static readonly ScrRunOptions S4 = new() { Admission = ScrAdmissionSet.S4 };

    /// <summary>A recording host that also answers the guard questions.</summary>
    private sealed class QueryHost(FakeHost inner) : IScrHost, IScrObjectQueries
    {
        public FakeHost Inner { get; } = inner;
        public Dictionary<(ScrObjectRef, int), int> Quests { get; } = new();
        public HashSet<(ScrObjectRef, int)> Held { get; } = new();
        public Dictionary<ScrObjectRef, int> Names { get; } = new();
        public HashSet<ScrObjectRef> MindControlled { get; } = new();
        public Dictionary<(ScrObjectRef, int), int> Wielded { get; } = new();
        public HashSet<(ScrObjectRef, int)> Reputations { get; } = new();
        public List<int> WieldAsks { get; } = new();

        public ScrSlot? Slot(ScrObjectRef attachee, int point) => Inner.Slot(attachee, point);
        public ScrScriptLookup Script(int number) => Inner.Script(number);
        public bool GlobalFlag(int index) => Inner.GlobalFlag(index);
        public int StoryState() => Inner.StoryState();
        public bool IsMonsterGenerator(ScrObjectRef attachee) => Inner.IsMonsterGenerator(attachee);
        public ScrDialogState DialogState(ScrObjectRef attachee) => Inner.DialogState(attachee);
        public ScrTrapCheck CheckTrapBeforeUse(ScrObjectRef triggerer, ScrObjectRef attachee) => Inner.CheckTrapBeforeUse(triggerer, attachee);
        public bool IsTrapObject(ScrObjectRef attachee) => Inner.IsTrapObject(attachee);
        public ScrObjectRef? Player() => Inner.Player();
        public ScrObjectRef? Leader(ScrObjectRef critter) => Inner.Leader(critter);
        public bool IsJilted(ScrObjectRef critter) => Inner.IsJilted(critter);
        public bool IsPc(ScrObjectRef critter) => Inner.IsPc(critter);

        public int QuestState(ScrObjectRef pc, int quest) => Quests.GetValueOrDefault((pc, quest));
        public bool HoldsItemNamed(ScrObjectRef obj, int name) => Held.Contains((obj, name));
        public int NameOf(ScrObjectRef obj) => Names.GetValueOrDefault(obj);
        public bool IsMindControlled(ScrObjectRef obj) => MindControlled.Contains(obj);
        public int? WieldedItemName(ScrObjectRef obj, int location) { WieldAsks.Add(location); return Wielded.TryGetValue((obj, location), out var name) ? name : null; }
        public bool HasReputation(ScrObjectRef obj, int reputation) => Reputations.Contains((obj, reputation));
        public Dictionary<ScrObjectRef, int> Types { get; } = new();
        public int? ObjectType(ScrObjectRef obj) => Types.TryGetValue(obj, out var type) ? type : null;
    }

    private static QueryHost With(ScrProgram program)
    {
        var host = new FakeHost();
        host.Attach(Chest, ScrPoint.Use, 2793, program);
        return new QueryHost(host);
    }

    private static ScrRunResult Use(IScrHost host, ScrRunOptions? options = null) => ScrDispatcher.RunObject(new(ScrPoint.Use, Chest, Pc, null), host, options ?? S4);

    [TestMethod]
    public void A_teleport_asks_for_the_object_map_and_tile_and_the_run_goes_on()
    {
        var host = With(Program(Do(C(44, (Triggerer, 0), N(5078), N(2087), N(2018))), Do(C(100, N(7))), Do(C(1))));
        var run = Use(host);
        Assert.AreEqual(ScrStop.ReturnSkipDefault, run.Stop);
        var teleport = run.Effects.Teleports.Single();
        Assert.AreEqual(new ScrTeleport(Pc, 5078, 2087, 2018), teleport);
        Assert.AreEqual(79, teleport.RuntimeMap, "the catalogue number less 4,999");
        Assert.IsNull(teleport.Fade);
        Assert.AreEqual(7, run.Effects.GlobalFlags.Single().Index, "the next line ran");
        Assert.AreEqual(unchecked(int.MinValue - 4999), new ScrTeleport(Pc, int.MinValue, 0, 0).RuntimeMap, "32-bit, as the original");
    }

    [TestMethod]
    public void A_fade_and_teleport_carries_its_seconds_sound_and_movie()
    {
        var host = With(Program(Do(C(102, N(3600), N(4012), N(9), (Attachee, 0), N(5001), N(110), N(220))), Do(C(1))));
        var teleport = Use(host).Effects.Teleports.Single();
        Assert.AreEqual(new ScrTeleport(Chest, 5001, 110, 220, new(3600, 4012, 9)), teleport);
        Assert.AreEqual(2, teleport.RuntimeMap);
    }

    [TestMethod]
    public void A_teleport_of_an_absent_object_asks_for_nothing_and_requests_keep_their_order()
    {
        var host = With(Program(Do(C(44, (21, 0), N(5000), N(1), N(2))), Do(C(44, (Triggerer, 0), N(5001), N(3), N(4))), Do(C(44, (Attachee, 0), N(5002), N(5), N(6))), Do(C(1))));
        host.Inner.PlayerRef = null;
        CollectionAssert.AreEqual(new[] { new ScrTeleport(Pc, 5001, 3, 4), new ScrTeleport(Chest, 5002, 5, 6) }, Use(host).Effects.Teleports.ToArray());
    }

    [TestMethod]
    public void The_guards_answer_from_the_host_over_one_object()
    {
        // Each guard sets one flag on its true branch: quest 1003 in state 2, holds item 6001, named 9000, wields 6002,
        // reputation 31.
        var program = Program(
            L(C(6, (Triggerer, 0), N(1003), N(2)), C(100, N(1)), C(0)),
            L(C(17, (Triggerer, 0), N(6001)), C(100, N(2)), C(0)),
            L(C(20, (Attachee, 0), N(9000)), C(100, N(3)), C(0)),
            L(C(21, (Triggerer, 0), N(6002)), C(100, N(4)), C(0)),
            L(C(33, (Triggerer, 0), N(31)), C(100, N(5)), C(0)),
            Do(C(1)));
        int[] Flags(QueryHost h) => Use(h).Effects.GlobalFlags.Select(f => f.Index).ToArray();

        var none = With(program);
        CollectionAssert.AreEqual(Array.Empty<int>(), Flags(none));
        CollectionAssert.AreEqual(Enumerable.Range(1000, 9).ToArray(), none.WieldAsks.ToArray(), "every wield location 1000..1008, in order");

        var all = With(program);
        all.Quests[(Pc, 1003)] = 2; all.Held.Add((Pc, 6001)); all.Names[Chest] = 9000; all.Wielded[(Pc, 1008)] = 6002; all.Reputations.Add((Pc, 31));
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, Flags(all));
        Assert.AreEqual(9, all.WieldAsks.Count, "the last location is the one that matched");

        var other = With(program);
        other.Quests[(Pc, 1003)] = 3; other.Held.Add((Chest, 6001)); other.Names[Chest] = 9000; other.MindControlled.Add(Chest);
        other.Wielded[(Pc, 1009)] = 6002; other.Reputations.Add((Pc, 32));
        CollectionAssert.AreEqual(Array.Empty<int>(), Flags(other), "another state, another holder, mind-controlled, a location past 1008, another reputation");
    }

    [TestMethod]
    public void A_guard_over_an_absent_object_stops_and_values_may_be_variables()
    {
        // The player is absent: the guard on line 1 stops the run there (the original would take the false branch).
        var absent = With(Program(Do(C(100, N(1))), L(C(17, (21, 0), N(6001)), C(100, N(2)), C(100, N(3))), Do(C(1))));
        absent.Inner.PlayerRef = null;
        var stopped = Use(absent);
        Assert.AreEqual((ScrStop.MissingObject, 1), (stopped.Stop, stopped.StopLine));
        CollectionAssert.AreEqual(new[] { 1 }, stopped.Effects.GlobalFlags.Select(f => f.Index).ToArray(), "effects before the stop are kept");
        var fade = With(Program(Do(C(102, N(1), N(2), N(3), (21, 0), N(5000), N(1), N(1))), Do(C(1))));
        fade.Inner.PlayerRef = null;
        Assert.AreEqual(0, Use(fade).Effects.Teleports.Count);

        // A local variable as the name, set on line 0; the false branch has its own effect; location 999 is not walked.
        var program = Program(Do(C(10, (2, 0), N(6002))), L(C(21, (Triggerer, 0), (2, 0)), C(100, N(4)), C(100, N(5))), Do(C(1)));
        var below = With(program);
        below.Wielded[(Pc, 999)] = 6002;
        CollectionAssert.AreEqual(new[] { 5 }, Use(below).Effects.GlobalFlags.Select(f => f.Index).ToArray());
        CollectionAssert.AreEqual(Enumerable.Range(1000, 9).ToArray(), below.WieldAsks.ToArray());
        var held = With(program);
        held.Wielded[(Pc, 1000)] = 6002;
        CollectionAssert.AreEqual(new[] { 4 }, Use(held).Effects.GlobalFlags.Select(f => f.Index).ToArray());
        Assert.AreEqual(1, held.WieldAsks.Count, "the first matching location ends the walk");
    }

    [TestMethod]
    public void The_guards_and_teleports_need_set_S4_and_a_host_that_answers()
    {
        var guard = Program(L(C(17, (Triggerer, 0), N(6001)), C(1), C(1)));
        var refused = Use(With(guard), new ScrRunOptions { Admission = ScrAdmissionSet.S7 });
        Assert.AreEqual((ScrOutcome.Refused, ScrRefusalCode.ConditionNotAdmitted), (refused.Outcome, refused.Refusal!.Code));
        var teleport = Program(Do(C(44, (Triggerer, 0), N(5078), N(1), N(1))), Do(C(1)));
        Assert.AreEqual(ScrRefusalCode.ActionNotAdmitted, Use(With(teleport), new ScrRunOptions()).Refusal!.Code, "the default set is S5");

        // A set with a guard and a host that cannot answer is a caller error before any event runs, whatever the script.
        var plain = new FakeHost();
        plain.Attach(Chest, ScrPoint.Use, 2793, teleport);
        Assert.ThrowsExactly<ArgumentException>(() => Use(plain));
        var teleportsOnly = new ScrAdmissionSet("teleports", ScrAdmissionSet.S7.Conditions, ScrAdmissionSet.S4.Actions, ScrAdmissionSet.S7.ValueKinds, ScrAdmissionSet.S7.FocusKinds);
        Assert.AreEqual(1, Use(plain, new ScrRunOptions { Admission = teleportsOnly }).Effects.Teleports.Count, "a teleport needs no questions");

        Assert.IsTrue(ScrAdmissionSet.S7.IsSubsetOf(ScrAdmissionSet.S4));
        CollectionAssert.AreEquivalent(ScrAdmissionSet.S7.Actions.Append(44).Append(102).ToArray(), ScrAdmissionSet.S4.Actions.ToArray());
        CollectionAssert.AreEquivalent(ScrAdmissionSet.S7.Conditions.Concat(ScrAdmissionSet.ObjectQueryConditions).ToArray(), ScrAdmissionSet.S4.Conditions.ToArray());
    }

    // Opt-in (DAT_ROOT = the game folder): how many installed scripts and how many teleport scripts set S4 admits from line 0.
    [TestMethod, TestCategory("RealData")]
    public void Set_S4_admits_the_installed_teleport_scripts_it_can_run()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in S4 check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        int total = 0, s7 = 0, s4 = 0, teleportScripts = 0, teleportS7 = 0, teleportS4 = 0, requested = 0;
        var host = new QueryHost(new FakeHost());
        foreach (var key in stack.Keys("scr/").Where(k => k.EndsWith(".scr", StringComparison.Ordinal) && k.Count(c => c == '/') == 1))
        {
            var program = ScrProgram.Read(stack.Read(stack.Resolve(key).Winner!).Bytes!).Program!;
            total++;
            var teleports = program.Lines.Any(l => l.OnTrue.Type is 44 or 102 || l.OnFalse.Type is 44 or 102);
            var inS7 = ScrAdmission.AdmitFrom(program, ScrAdmissionSet.S7, 0).IsAdmitted;
            var inS4 = ScrAdmission.AdmitFrom(program, ScrAdmissionSet.S4, 0).IsAdmitted;
            if (inS7) s7++;
            if (inS4) s4++;
            if (!teleports) continue;
            teleportScripts++;
            if (inS7) teleportS7++;
            if (!inS4) continue;
            teleportS4++;
            var number = int.Parse(key[4..].TakeWhile(char.IsAsciiDigit).ToArray(), System.Globalization.CultureInfo.InvariantCulture);
            host.Inner.Slots.Clear(); host.Inner.Scripts.Clear();
            host.Inner.Attach(Chest, ScrPoint.Use, number, program);
            var run = Use(host);
            Assert.AreNotEqual(ScrOutcome.Refused, run.Outcome, key);
            requested += run.Effects.Teleports.Count;
        }
        // Measured on the 1.0.7.4 GOG installation: from line 0 set S7 admits 469 scripts, S4 774. Of the 357 scripts with a
        // teleport action, S7 admits 22 (their teleports are not reached from line 0) and S4 294; with a host that answers
        // no quests, items, names, wielded items, reputations or flags, those runs ask for 257 teleports.
        Assert.AreEqual((2399, 469, 774), (total, s7, s4));
        Assert.AreEqual((357, 22, 294, 257), (teleportScripts, teleportS7, teleportS4, requested));
    }
}
