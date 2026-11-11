using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>
/// ARC-MAGIC-042 (T-m1, ledger TI4–TI6): the actions transform into a basic prototype (40), force follow (21) and attack (36) of set S33 hand one object request each to the host in action order, the attachee stays the object the event
/// named (the host reads it as the new object after a transform), and the installed creature-item scripts are admitted and run to the requests the ledger records.
/// </summary>
[TestClass]
public sealed class ScrThrownItemTests
{
    private const byte TriggererKind = 0, AttacheeKind = 1, EveryoneInVicinity = 8;
    private static readonly ScrObjectRef Item = new(1, "made:1");
    private static readonly ScrObjectRef Bystander = new(1, "npc.2");
    private static readonly ScrAdmissionSet Set = new("test-thrown", ScrAdmissionSet.S1.Conditions,
        ScrAdmissionSet.S1.Actions.Concat([ScrCatalogue.Action.TransformObject, ScrCatalogue.Action.MakeFollow, ScrCatalogue.Action.Attack]), ScrAdmissionSet.S1.ValueKinds,
        [ScrFocusKind.Triggerer, ScrFocusKind.Attachee, ScrFocusKind.ExtraObject, ScrFocusKind.Player, ScrFocusKind.EveryoneInVicinity, ScrFocusKind.AnyoneInVicinity]);
    private static readonly ScrRunOptions Options = new() { Admission = Set };

    private sealed class Host(FakeHost inner) : IScrHost, IScrWorldQueries
    {
        public IReadOnlyList<ScrObjectRef> Around { get; set; } = [];
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
        public IReadOnlyList<ScrObjectRef> SceneryInVicinity(ScrObjectRef centre) => [];
        public IReadOnlyList<ScrObjectRef> CrittersInVicinity(ScrObjectRef centre) => Around;
        public int Random(int low, int high) => low;
        public (long X, long Y)? Location(ScrObjectRef obj) => null;
    }

    private static ScrRunResult Run(ScrProgram program, IReadOnlyList<ScrObjectRef>? around = null, ScrObjectRef? extra = null)
    {
        var fake = new FakeHost();
        fake.Attach(Item, ScrPoint.Hit, 1022, program);
        return ScrDispatcher.RunObject(new(ScrPoint.Hit, Item, Pc, extra), new Host(fake) { Around = around ?? [] }, Options);
    }

    [TestMethod]
    public void A_creature_script_transforms_the_attachee_and_makes_it_follow_the_triggerer()
    {
        var run = Run(Program(
            Do(C(ScrCatalogue.Action.TransformObject, N(28460))),
            Do(C(ScrCatalogue.Action.MakeFollow, (AttacheeKind, 0), (TriggererKind, 0))),
            Do(C(ScrCatalogue.Action.ReturnRunDefault))));
        Assert.AreEqual(ScrOutcome.RunDefault, run.Outcome);
        CollectionAssert.AreEqual(new[]
        {
            new ScrObjectStateRequest(ScrObjectStateChange.Transform, Item, 28460),
            new ScrObjectStateRequest(ScrObjectStateChange.MakeFollow, Item, Other: Pc),
        }, run.Effects.ObjectStates.ToArray(), "the follower is the attachee, the leader the triggerer; the host reads the attachee as the new object");
    }

    [TestMethod]
    public void Attack_hands_over_one_request_per_object_of_the_set_with_the_attachee_as_the_attacker()
    {
        var run = Run(Program(
            Do(C(ScrCatalogue.Action.TransformObject, N(27391))),
            Do(C(ScrCatalogue.Action.Attack, (EveryoneInVicinity, 0), (AttacheeKind, 0))),
            Do(C(ScrCatalogue.Action.ReturnRunDefault))), around: [Bystander, Pc]);
        CollectionAssert.AreEqual(new[]
        {
            new ScrObjectStateRequest(ScrObjectStateChange.Transform, Item, 27391),
            new ScrObjectStateRequest(ScrObjectStateChange.Attack, Bystander, Other: Item),
            new ScrObjectStateRequest(ScrObjectStateChange.Attack, Pc, Other: Item),
        }, run.Effects.ObjectStates.ToArray());
    }

    [TestMethod]
    public void An_empty_set_and_an_absent_object_ask_for_nothing()
    {
        var empty = Run(Program(Do(C(ScrCatalogue.Action.Attack, (EveryoneInVicinity, 0), (AttacheeKind, 0))), Do(C(ScrCatalogue.Action.ReturnRunDefault))));
        Assert.AreEqual(0, empty.Effects.ObjectStates.Count, "nobody around");
        // The extra object (12) was not given: the follower operand names nobody.
        var absent = Run(Program(Do(C(ScrCatalogue.Action.MakeFollow, (12, 0), (TriggererKind, 0))), Do(C(ScrCatalogue.Action.ReturnRunDefault))));
        Assert.AreEqual(0, absent.Effects.ObjectStates.Count);
    }

    [TestMethod]
    public void S33_adds_the_three_over_S32()
    {
        var program = Program(Do(C(ScrCatalogue.Action.TransformObject, N(28460))));
        Assert.IsTrue(ScrAdmission.Admit(program, ScrAdmissionSet.S33).IsAdmitted);
        Assert.AreEqual(ScrRefusalCode.ActionNotAdmitted, ScrAdmission.Admit(program, ScrAdmissionSet.S32).Refusal!.Code);
        Assert.IsTrue(ScrAdmissionSet.S32.IsSubsetOf(ScrAdmissionSet.S33));
    }

    // The installed creature items' scripts (prototypes 15086, 15112, 15136, 15153 and 15192, 15232): four make a pet (transform, follow, return run default), 29017 an aggressor (transform, attack, return).
    [TestMethod, TestCategory("RealData")]
    public void The_installed_creature_item_scripts_run_to_the_requests_the_ledger_records()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in thrown item script check; this is not a synthetic pass.");
        var opened = ContentStack.Open(ContentMountPlan.Discover(root).Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        using var stack = opened.Stack!;
        (int Script, int Prototype, bool Attacks)[] expected = [(1022, 28460, false), (1343, 28461, false), (30008, 28462, false), (30300, 28467, false), (29017, 27391, true)];
        foreach (var (script, prototype, attacks) in expected)
        {
            var key = stack.Keys("scr/").Single(k => k.StartsWith($"scr/{script:D5}", StringComparison.Ordinal));
            var program = ScrProgram.Read(stack.Read(stack.Resolve(key).Winner!).Bytes!).Program!;
            Assert.IsTrue(ScrAdmission.Admit(program, ScrAdmissionSet.S33).IsAdmitted, $"script {script} is admitted by S33");
            foreach (var point in new[] { ScrPoint.Hit, ScrPoint.Miss })
            {
                var fake = new FakeHost();
                fake.Attach(Item, point, script, program);
                var run = ScrDispatcher.RunObject(new(point, Item, Pc, Bystander), new Host(fake) { Around = [Bystander, Pc] }, Options);
                Assert.AreEqual(ScrOutcome.RunDefault, run.Outcome, $"script {script} point {point}: return run default");
                var requests = run.Effects.ObjectStates.ToArray();
                Assert.AreEqual(new ScrObjectStateRequest(ScrObjectStateChange.Transform, Item, prototype), requests[0], $"script {script}");
                if (attacks) CollectionAssert.AreEqual(new[] { new ScrObjectStateRequest(ScrObjectStateChange.Attack, Bystander, Other: Item), new ScrObjectStateRequest(ScrObjectStateChange.Attack, Pc, Other: Item) }, requests[1..]);
                else CollectionAssert.AreEqual(new[] { new ScrObjectStateRequest(ScrObjectStateChange.MakeFollow, Item, Other: Pc) }, requests[1..]);
            }
        }
    }
}
