using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>ARC-DEV-178 (bug B19): get reaction (29) of set S23 (ledger GR1). Programs and reactions are invented.</summary>
[TestClass]
public sealed class ScrReactionTests
{
    private const int Use = 1;
    private const byte Triggerer = 0, Attachee = 1, Extra = 12;
    // The commands these programs use (control flow, arithmetic, call script) with get reaction and the focus kinds that name one object each; S23
    // itself also holds the commands that need the world, so its host would have to answer more.
    private static readonly ScrAdmissionSet Set = new("reaction", ScrAdmissionSet.S1.Conditions, ScrAdmissionSet.S1.Actions.Append(ScrCatalogue.Action.GetReaction),
        ScrAdmissionSet.S1.ValueKinds, [ScrFocusKind.Triggerer, ScrFocusKind.Attachee, ScrFocusKind.ExtraObject]);
    private static readonly ScrRunOptions Options = new() { Admission = Set };

    /// <summary>A recording host that also answers reactions.</summary>
    private sealed class ReactionHost(FakeHost inner) : IScrHost, IScrReactionQueries
    {
        public FakeHost Inner { get; } = inner;
        public Dictionary<(ScrObjectRef, ScrObjectRef), int> Reactions { get; } = new();
        public List<(ScrObjectRef Npc, ScrObjectRef Pc)> Asked { get; } = new();
        public int? ReactionOf(ScrObjectRef npc, ScrObjectRef pc) { Asked.Add((npc, pc)); return Reactions.TryGetValue((npc, pc), out var value) ? value : null; }
        public ScrSlot? Slot(ScrObjectRef attachee, int point) => Inner.Slot(attachee, point);
        public ScrScriptLookup Script(int number) => Inner.Script(number);
        public bool GlobalFlag(int index) => Inner.GlobalFlag(index);
        public int StoryState() => Inner.StoryState();
        public bool IsMonsterGenerator(ScrObjectRef attachee) => false;
        public ScrDialogState DialogState(ScrObjectRef attachee) => ScrDialogState.Normal;
        public ScrTrapCheck CheckTrapBeforeUse(ScrObjectRef triggerer, ScrObjectRef attachee) => ScrTrapCheck.NoTrap;
        public bool IsTrapObject(ScrObjectRef attachee) => false;
        public ScrObjectRef? Player() => Inner.Player();
        public ScrObjectRef? Leader(ScrObjectRef critter) => null;
        public bool IsJilted(ScrObjectRef critter) => false;
        public bool IsPc(ScrObjectRef critter) => Inner.IsPc(critter);
    }

    private static Clause GetReaction(byte npc, byte pc, int counter) => C(ScrCatalogue.Action.GetReaction, (npc, 0), (pc, 0), (Counter, counter));

    private static (ScrRunResult Run, ReactionHost Host) Run(ScrProgram program, Action<ReactionHost>? arrange = null)
    {
        var host = new ReactionHost(new FakeHost());
        host.Inner.Attach(Npc, Use, 2023, program);
        arrange?.Invoke(host);
        return (ScrDispatcher.RunObject(new(Use, Npc, Pc, null), host, Options), host);
    }

    [TestMethod]
    public void Get_reaction_stores_the_hosts_reaction_of_the_first_object_toward_the_second_and_goes_on()
    {
        var (run, host) = Run(Program(Do(GetReaction(Attachee, Triggerer, 0)), Do(C(ScrCatalogue.Action.ReturnSkipDefault))), h => h.Reactions[(Npc, Pc)] = 62);
        Assert.AreEqual(ScrStop.ReturnSkipDefault, run.Stop);
        Assert.AreEqual(62u, run.Effects.Writeback!.Slot.Counters & 0xFF, "the reaction in counter 0");
        CollectionAssert.AreEqual(new[] { (Npc, Pc) }, host.Asked, "the NPC is operand 0, the PC operand 1 (0x447A5A)");
    }

    [TestMethod]
    public void A_reaction_the_host_cannot_answer_ends_the_dispatch_as_not_modelled_with_nothing_applied()
    {
        var (run, _) = Run(Program(Do(GetReaction(Attachee, Triggerer, 0)), Do(C(ScrCatalogue.Action.ReturnSkipDefault))));
        Assert.AreEqual(ScrStop.NotModelled, run.Stop);
        Assert.IsTrue(run.Effects.IsEmpty);
    }

    [TestMethod]
    public void Get_reaction_on_an_absent_object_stops_the_run_and_its_store_must_not_be_a_number()
    {
        var (run, host) = Run(Program(Do(GetReaction(Attachee, Extra, 0)), Do(C(ScrCatalogue.Action.ReturnRunDefault))), h => h.Reactions[(Npc, Pc)] = 62);
        Assert.AreEqual((ScrStop.MissingObject, 0), (run.Stop, run.StopLine), "no extra object: the run stops at the read, as at every object read");
        Assert.AreEqual(0, host.Asked.Count);

        var literal = ScrAdmission.Admit(Program(Do(C(ScrCatalogue.Action.GetReaction, (Attachee, 0), (Triggerer, 0), N(5)))), Set);
        Assert.AreEqual((ScrRefusalCode.WriteToNumber, 2), (literal.Refusal!.Code, literal.Refusal.Slot), "the store is operand 2");
    }

    [TestMethod]
    public void Only_S23_and_the_sets_built_on_it_admit_get_reaction_and_its_host_must_answer_it()
    {
        Assert.IsTrue(ScrAdmissionSet.S22.IsSubsetOf(ScrAdmissionSet.S23));
        Assert.IsFalse(ScrAdmissionSet.S23.IsSubsetOf(ScrAdmissionSet.S22));
        var program = Program(Do(GetReaction(Attachee, Triggerer, 0)), Do(C(1)));
        Assert.AreEqual(ScrRefusalCode.ActionNotAdmitted, ScrAdmission.Admit(program, ScrAdmissionSet.S22).Refusal!.Code);
        Assert.IsTrue(ScrAdmission.Admit(program, ScrAdmissionSet.S23).IsAdmitted);
        var plain = new FakeHost();
        plain.Attach(Npc, Use, 2023, program);
        Assert.ThrowsExactly<ArgumentException>(() => ScrDispatcher.RunObject(new(Use, Npc, Pc, null), plain, Options),
            "a set with get reaction needs a host that answers it, checked before any event runs");
    }
}
