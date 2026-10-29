using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Scr.ScrTestPrograms;

namespace Arcanum.Scripting.Tests.Scr;

/// <summary>ARC-SCRIPT-047: a heartbeat that opens an NPC's talk with the PC as soon as the PC is in its vicinity (ledger TG3–TG6), on a
/// synthetic replica of the shape of the installed heartbeat 1100 and dialog 1324: loop for over everyone in the vicinity, get object type,
/// call-ex of the attachee's dialog point at a line whose dialog action names a node.</summary>
[TestClass]
public sealed class ScrHeartbeatTalkTests
{
    private const byte Attachee = 1, Everyone = 8, Anyone = 9, Looped = 10;
    private const int Heartbeat = 1100, Talk = 1324, PcType = 15, NpcType = 16;
    private static readonly ScrObjectRef Other = new(1, "npc.2");
    private static readonly ScrRunOptions Options = new() { Admission = ScrAdmissionSet.S17 };

    private sealed class TalkHost(FakeHost inner) : IScrHost, IScrObjectQueries, IScrWorldQueries, IScrSkillQueries, IScrItemQueries
    {
        public FakeHost Inner { get; } = inner;
        public List<ScrObjectRef> Critters { get; } = new();
        public Dictionary<ScrObjectRef, int> Types { get; } = new() { [Pc] = PcType, [Npc] = NpcType, [Other] = NpcType };
        public List<ScrObjectRef> Centres { get; } = new();

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
        public int QuestState(ScrObjectRef pc, int quest) => 0;
        public bool HoldsItemNamed(ScrObjectRef obj, int name) => false;
        public int NameOf(ScrObjectRef obj) => 0;
        public bool IsMindControlled(ScrObjectRef obj) => false;
        public int? WieldedItemName(ScrObjectRef obj, int location) => null;
        public bool HasReputation(ScrObjectRef obj, int reputation) => false;
        public int? ObjectType(ScrObjectRef obj) => Types.TryGetValue(obj, out var type) ? type : null;
        public IReadOnlyList<ScrObjectRef> SceneryInVicinity(ScrObjectRef centre) => [];
        public IReadOnlyList<ScrObjectRef> CrittersInVicinity(ScrObjectRef centre) { Centres.Add(centre); return Critters.ToArray(); }
        public int Random(int low, int high) => low;
        public int SkillLevel(ScrObjectRef obj, int skill) => 0;
        public bool? IsIdentified(ScrObjectRef obj) => null;
        public int? PowerRatio(ScrObjectRef item, ScrObjectRef owner) => null;
        public bool ItemsBeingRewielded() => false;
    }

    // 1100's shape: for everyone in the vicinity, a looped PC gets a call-ex of the attachee's dialog point at line 4; then this script is
    // removed and the run returns skip default. Past the loop the run falls off the end (skip default too).
    private static ScrProgram HeartbeatProgram(byte focus = Everyone) => Program(
        Do(C(ScrCatalogue.Action.LoopFor, (focus, 0))),
        Do(C(ScrCatalogue.Action.GetObjectType, (Looped, 0), (Local, 0))),
        L(C(ScrCatalogue.Condition.Equal, (Local, 0), N(PcType)), C(0), C(ScrCatalogue.Action.Goto, N(6))),
        Do(C(ScrCatalogue.Action.CallScriptEx, (Attachee, 0), N(ScrPoint.Dialog), N(4), (Looped, 0))),
        Do(C(ScrCatalogue.Action.RemoveThisScript)),
        Do(C(ScrCatalogue.Action.ReturnSkipDefault)),
        Do(C(ScrCatalogue.Action.LoopEnd)));

    // 1324's shape from line 4: local flag 3 set, then the dialog action at node 1 (line 5); line 6 is where a resumption goes on.
    private static ScrProgram TalkProgram(uint fileFlags = 0) => Program(fileFlags,
        Do(C(ScrCatalogue.Action.ReturnSkipDefault)), Do(C(ScrCatalogue.Action.ReturnSkipDefault)), Do(C(ScrCatalogue.Action.ReturnSkipDefault)),
        Do(C(ScrCatalogue.Action.ReturnSkipDefault)),
        Do(C(ScrCatalogue.Action.SetLocalFlag, N(3))),
        Do(C(ScrCatalogue.Action.Dialog, N(1))),
        Do(C(ScrCatalogue.Action.ReturnSkipDefault)));

    private static TalkHost Host(uint talkFlags = 0, params ScrObjectRef[] critters)
    {
        var inner = new FakeHost();
        inner.Attach(Npc, ScrPoint.Heartbeat, Heartbeat, HeartbeatProgram());
        inner.Attach(Npc, ScrPoint.Dialog, Talk, TalkProgram(talkFlags));
        var host = new TalkHost(inner);
        host.Critters.AddRange(critters);
        return host;
    }

    private static ScrRunResult Beat(TalkHost host) => ScrDispatcher.RunObject(new(ScrPoint.Heartbeat, Npc, Npc, null), host, Options);

    [TestMethod]
    public void A_heartbeat_opens_the_talk_once_the_pc_is_in_the_vicinity_and_removes_itself()
    {
        // The vicinity holds the NPC itself, another NPC and the PC (TG4: the centre is in its own list).
        var host = Host(0, Npc, Other, Pc);
        var run = Beat(host);
        Assert.AreEqual((ScrOutcome.SkipDefault, ScrStop.ReturnSkipDefault), (run.Outcome, run.Stop));
        CollectionAssert.AreEqual(new[] { Npc }, host.Centres, "the list is read once, around the attachee");
        // The talk: the PC with the NPC, the DLG of the dialog script at node 1, the dialog action's line, at the dialog point.
        Assert.AreEqual(new ScrDialogStart(Pc, Npc, Talk, 5, 1) { Point = ScrPoint.Dialog }, run.Effects.Dialog);
        // The dialog slot keeps the callee's local flag (X11's store and the call-ex writeback are the same slot), the heartbeat is removed.
        Assert.AreEqual(new ScrWriteback(ScrSource.Object, Npc, ScrPoint.Dialog, new ScrSlot(Talk, 1u << 3, 0)), run.Effects.SlotWrites[^1]);
        Assert.AreEqual(new ScrWriteback(ScrSource.Object, Npc, ScrPoint.Heartbeat, new ScrSlot(0, 0, 0)), run.Effects.Writeback);
    }

    [TestMethod]
    public void Without_the_pc_in_the_vicinity_the_heartbeat_skips_the_pass_and_stays()
    {
        // An empty list goes on after its loop end, a line outside the script (PG11); a list without the PC loops to its end and runs past
        // the last line: skip default either way.
        foreach (var (critters, stop) in new[] { (Array.Empty<ScrObjectRef>(), ScrStop.LineOutOfRange), ([Npc, Other], ScrStop.FellOffEnd) })
        {
            var run = Beat(Host(0, critters));
            Assert.AreEqual((ScrOutcome.SkipDefault, stop), (run.Outcome, run.Stop), $"{critters.Length} critters");
            Assert.IsNull(run.Effects.Dialog);
            Assert.IsNull(run.Effects.Writeback, "the heartbeat stays attached");
        }
    }

    [TestMethod]
    public void The_dialog_gates_meet_a_call_ex_of_the_dialog_point()
    {
        // E6 (TG5): a dead NPC without the death-speech file flag runs nothing (the caller goes on and removes itself); with it, it talks.
        var dead = Host(0, Pc);
        dead.Inner.Dialog = ScrDialogState.Dead;
        var silent = Beat(dead);
        Assert.IsNull(silent.Effects.Dialog);
        Assert.AreEqual(0, silent.Effects.SlotWrites.Count);
        Assert.AreEqual(0, silent.Effects.Writeback!.Slot.Script, "the caller removed itself all the same");
        var speaking = Host(ScrCatalogue.FileFlag.DeathSpeech, Pc);
        speaking.Inner.Dialog = ScrDialogState.Dead;
        Assert.AreEqual(Talk, Beat(speaking).Effects.Dialog!.Script);
        // Surrendered without the surrender-speech flag: nothing.
        var surrendered = Host(0, Pc);
        surrendered.Inner.Dialog = ScrDialogState.Surrendered;
        Assert.IsNull(Beat(surrendered).Effects.Dialog);
        // Mind-controlled: script 2188 runs in place of the NPC's own from the same line, its own number stays in the slot.
        var controlled = Host(0, Pc);
        controlled.Inner.Dialog = ScrDialogState.MindControlled;
        controlled.Inner.Scripts[ScrDispatcher.MindControlScript] = ScrScriptLookup.Of(TalkProgram());
        var swapped = Beat(controlled);
        Assert.AreEqual(ScrDispatcher.MindControlScript, swapped.Effects.Dialog!.Script);
        Assert.AreEqual(Talk, swapped.Effects.SlotWrites[^1].Slot.Script);
    }

    [TestMethod]
    public void The_critter_vicinity_kinds_are_admitted_in_actions_only()
    {
        // A loop over anyone in the vicinity reads the same list.
        var host = Host(0, Pc);
        host.Inner.Attach(Npc, ScrPoint.Heartbeat, Heartbeat, HeartbeatProgram(Anyone));
        Assert.AreEqual(Talk, Beat(host).Effects.Dialog!.Script);
        // A condition tests every object of a list (SG6), which the interpreter does not model: refused.
        var condition = Program(L(C(ScrCatalogue.Condition.IsNamed, (Everyone, 0), N(6409)), C(ScrCatalogue.Action.ReturnRunDefault), C(0)));
        Assert.AreEqual(ScrRefusalCode.FocusKindNotAdmitted, ScrAdmission.Admit(condition, Options.Admission).Refusal!.Code);
        // A host without world queries cannot run a set with them.
        var plain = new FakeHost();
        plain.Attach(Npc, ScrPoint.Heartbeat, Heartbeat, HeartbeatProgram());
        Assert.ThrowsExactly<ArgumentException>(() => ScrDispatcher.RunObject(new(ScrPoint.Heartbeat, Npc, Npc, null), plain,
            new() { Admission = new("vicinity", ScrAdmissionSet.S5.Conditions, ScrAdmissionSet.S5.Actions, ScrAdmissionSet.S5.ValueKinds,
                ScrAdmissionSet.S5.FocusKinds.Append(ScrFocusKind.EveryoneInVicinity)) }));
    }
}
