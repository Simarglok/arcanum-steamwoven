using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>
/// ARC-SCRIPT-1771 (ledger CS12–CS16): obj has bad associates (11), obj is monster of type (19) and obj is within range (34) of the set S54 (0x4451B0 cases 11, 19 and 34). The first two follow the list rule of the other object tests (monster of type reads
/// its number once for the whole list); within range takes the first object of operand 0 and three values and holds when the tile distance (0x4B96F0) of the object and the point is at most the range. Programs, objects and answers are invented.
/// </summary>
[TestClass]
public sealed class ScrCritterTailTests
{
    private const byte Attachee = 1, Extra = 12;
    private static readonly ScrObjectRef Sword = new(1, "kit:3"), Victim = new(1, "kit:4");

    private sealed class Host(FakeHost inner) : IScrHost, IScrCritterStateQueries, IScrWorldQueries
    {
        public Dictionary<ScrObjectRef, bool> Associates { get; } = new();
        public Dictionary<(ScrObjectRef Object, int Type), bool> Monsters { get; } = new();
        public Dictionary<ScrObjectRef, (long X, long Y)> Places { get; } = new();
        public List<string> Asked { get; } = [];

        public bool? HasBadAssociates(ScrObjectRef obj) { Asked.Add($"bad {obj.Code}"); return Associates.TryGetValue(obj, out var a) ? a : null; }
        public bool? IsMonsterOfType(ScrObjectRef obj, int type) { Asked.Add($"monster {obj.Code} {type}"); return Monsters.TryGetValue((obj, type), out var a) ? a : null; }
        public (long X, long Y)? Location(ScrObjectRef obj) => Places.TryGetValue(obj, out var at) ? at : null;
        public IReadOnlyList<ScrObjectRef> SceneryInVicinity(ScrObjectRef centre) => [];
        public IReadOnlyList<ScrObjectRef> CrittersInVicinity(ScrObjectRef centre) => [];
        public int Random(int low, int high) => low;

        public bool? MetPcBefore(ScrObjectRef npc, ScrObjectRef pc) => null;
        public bool? IsInvisible(ScrObjectRef obj) => null;
        public bool? IsSurrendered(ScrObjectRef obj) => null;
        public bool? IsInDialog(ScrObjectRef obj) => null;
        public bool? CanSee(ScrObjectRef observer, ScrObjectRef target) => null;
        public bool? CanHear(ScrObjectRef listener, ScrObjectRef source) => null;
        public bool? IsInvulnerable(ScrObjectRef obj) => null;
        public bool? IsUndead(ScrObjectRef obj) => null;
        public bool? IsProwling(ScrObjectRef obj) => null;
        public ScrSlot? Slot(ScrObjectRef attachee, int point) => inner.Slot(attachee, point);
        public ScrScriptLookup Script(int number) => inner.Script(number);
        public bool GlobalFlag(int index) => inner.GlobalFlag(index);
        public int StoryState() => inner.StoryState();
        public bool IsMonsterGenerator(ScrObjectRef attachee) => inner.IsMonsterGenerator(attachee);
        public ScrDialogState DialogState(ScrObjectRef attachee) => inner.DialogState(attachee);
        public ScrTrapCheck CheckTrapBeforeUse(ScrObjectRef triggerer, ScrObjectRef attachee) => inner.CheckTrapBeforeUse(triggerer, attachee);
        public bool IsTrapObject(ScrObjectRef attachee) => inner.IsTrapObject(attachee);
        public ScrObjectRef? Player() => inner.Player();
        public ScrObjectRef? Leader(ScrObjectRef critter) => inner.Leader(critter);
        public bool IsJilted(ScrObjectRef critter) => inner.IsJilted(critter);
        public bool IsPc(ScrObjectRef critter) => inner.IsPc(critter);
    }

    private static readonly ScrAdmissionSet Set = new("critter-tail", ScrAdmissionSet.S5.Conditions.Concat(ScrAdmissionSet.CritterTailConditions), ScrAdmissionSet.S5.Actions, ScrAdmissionSet.S5.ValueKinds, ScrAdmissionSet.S5.FocusKinds);

    private static Host With(ScrProgram program)
    {
        var inner = new FakeHost();
        inner.Attach(Sword, 7, 700, program);
        return new Host(inner);
    }

    private static ScrRunResult Hit(IScrHost host, ScrObjectRef? extra = null) => ScrDispatcher.RunObject(new(7, Sword, Pc, extra ?? Victim), host, new() { Admission = Set });

    private static ScrProgram Flag(int condition, params (byte, int)[] operands) =>
        Program(L(C(condition, operands), C(ScrCatalogue.Action.SetGlobalFlag, N(7)), C(0)), Do(C(ScrCatalogue.Action.ReturnRunDefault)));

    [TestMethod]
    public void Bad_associates_asks_the_host_of_the_object_its_operand_names_and_stops_for_an_unknown_one()
    {
        foreach (var (answer, flags) in new[] { (true, 1), (false, 0) })
        {
            var host = With(Flag(ScrCatalogue.Condition.BadAssociates, (Extra, 0)));
            host.Associates[Victim] = answer;
            var run = Hit(host);
            Assert.AreEqual(flags, run.Effects.GlobalFlags.Count, $"answers {answer}");
            CollectionAssert.AreEqual(new[] { "bad kit:4" }, host.Asked);
        }
        var unknown = Hit(With(Flag(ScrCatalogue.Condition.BadAssociates, (Extra, 0))));
        Assert.AreEqual((ScrStop.MissingObject, 0), (unknown.Stop, unknown.StopLine));
        var absentHost = With(Flag(ScrCatalogue.Condition.BadAssociates, (Extra, 0)));
        var absent = ScrDispatcher.RunObject(new(7, Sword, Pc, null), absentHost, new() { Admission = Set });
        Assert.AreEqual(0, absent.Effects.GlobalFlags.Count);
        Assert.AreEqual(0, absentHost.Asked.Count);
    }

    [TestMethod]
    public void Monster_of_type_hands_the_number_of_operand_1_to_the_host_with_the_object()
    {
        var host = With(Flag(ScrCatalogue.Condition.MonsterOfType, (Extra, 0), N(21)));
        host.Monsters[(Victim, 21)] = true;
        Assert.AreEqual(1, Hit(host).Effects.GlobalFlags.Count);
        CollectionAssert.AreEqual(new[] { "monster kit:4 21" }, host.Asked);

        var other = With(Flag(ScrCatalogue.Condition.MonsterOfType, (Extra, 0), N(22)));
        other.Monsters[(Victim, 21)] = true;
        Assert.AreEqual(ScrStop.MissingObject, Hit(other).Stop, "the host knows the object for another number only: the pair is not answered");
    }

    // Within range of the object Victim at tile (100, 200) against the point (x, y) and the range.
    private static ScrRunResult Within(int range, int x, int y, (long X, long Y)? at = null, bool placed = true)
    {
        var host = With(Flag(ScrCatalogue.Condition.WithinRange, (Extra, 0), N(range), N(x), N(y)));
        if (placed) host.Places[Victim] = at ?? (100, 200);
        return Hit(host);
    }

    [TestMethod]
    public void Within_range_holds_when_the_larger_axis_difference_is_at_most_the_range()
    {
        Assert.AreEqual(1, Within(5, 105, 200).Effects.GlobalFlags.Count, "exactly the range on x");
        Assert.AreEqual(0, Within(4, 105, 200).Effects.GlobalFlags.Count, "one more than the range");
        Assert.AreEqual(1, Within(7, 107, 193).Effects.GlobalFlags.Count, "the larger of the two differences (7 and 7)");
        Assert.AreEqual(0, Within(6, 107, 193).Effects.GlobalFlags.Count, "the x difference of 7 is the distance, not the sum of the steps");
        Assert.AreEqual(1, Within(0, 100, 200).Effects.GlobalFlags.Count, "the same tile at range 0");
        Assert.AreEqual(0, Within(-1, 100, 200).Effects.GlobalFlags.Count, "a negative range is below any distance");
        Assert.AreEqual(1, Within(3, 98, 203).Effects.GlobalFlags.Count, "differences are absolute");
    }

    [TestMethod]
    public void Within_range_reads_a_negative_x_as_an_unsigned_word_and_a_y_of_all_ones()
    {
        // 0x4451B0 builds the point's y as operand 3 or-ed with the sign of operand 2, and the distance takes unsigned 32-bit differences: a negative x is a huge distance.
        Assert.AreEqual(0, Within(1000, -1, 200).Effects.GlobalFlags.Count);
        Assert.AreEqual(0, Within(int.MaxValue, -1, 200).Effects.GlobalFlags.Count, "an unsigned distance of about 4.29e9 is above any range of a signed word");
    }

    [TestMethod]
    public void Within_range_stops_for_an_object_without_a_place_or_an_absent_one()
    {
        Assert.AreEqual(ScrStop.MissingObject, Within(5, 100, 200, placed: false).Stop);
        var host = With(Flag(ScrCatalogue.Condition.WithinRange, (Extra, 0), N(5), N(100), N(200)));
        Assert.AreEqual(ScrStop.MissingObject, ScrDispatcher.RunObject(new(7, Sword, Pc, null), host, new() { Admission = Set }).Stop);
    }

    [TestMethod]
    public void The_set_and_the_host_checks()
    {
        CollectionAssert.AreEquivalent(new[] { 11, 19, 34 }, ScrAdmissionSet.CritterTailConditions.ToArray());
        CollectionAssert.AreEquivalent(ScrAdmissionSet.CritterTailConditions.ToArray(), ScrAdmissionSet.S54.Conditions.Except(ScrAdmissionSet.S53.Conditions).ToArray());
        CollectionAssert.AreEquivalent(ScrAdmissionSet.S53.Actions.ToArray(), ScrAdmissionSet.S54.Actions.ToArray());
        Assert.IsTrue(ScrAdmissionSet.S53.IsSubsetOf(ScrAdmissionSet.S54));
        CollectionAssert.AreEqual(new[] { ScrOperandRole.Object, ScrOperandRole.Number, ScrOperandRole.Number, ScrOperandRole.Number }, ScrCatalogue.Signature(ScrClauseSide.Condition, 34)!.Take(4).ToArray());
        CollectionAssert.AreEqual(new[] { ScrOperandRole.Object, ScrOperandRole.Number }, ScrCatalogue.Signature(ScrClauseSide.Condition, 19)!.Take(2).ToArray());
        var plain = new FakeHost();
        plain.Attach(Sword, 7, 700, Program(Do(C(ScrCatalogue.Action.ReturnRunDefault))));
        Assert.ThrowsExactly<ArgumentException>(() => ScrDispatcher.RunObject(new(7, Sword, Pc, Victim), plain, new() { Admission = Set }), "a host without the critter-state or world queries");
        // The monster and associate answers are not enough for within range, which needs the world's location.
        var withinOnly = new ScrAdmissionSet("within", ScrAdmissionSet.S5.Conditions.Append(ScrCatalogue.Condition.WithinRange), ScrAdmissionSet.S5.Actions, ScrAdmissionSet.S5.ValueKinds, ScrAdmissionSet.S5.FocusKinds);
        Assert.ThrowsExactly<ArgumentException>(() => ScrDispatcher.RunObject(new(7, Sword, Pc, Victim), plain, new() { Admission = withinOnly }));
    }
}
