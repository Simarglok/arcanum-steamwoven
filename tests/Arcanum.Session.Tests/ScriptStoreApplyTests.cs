using System.Buffers.Binary;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>ARC-DEV-100 (MIG8 slice 4, GV1–GV4): <see cref="GameSessionOwner.ApplyScriptStores"/> applies the store changes a real run of the interpreter
/// leaves, in order and as versioned writes: a session without a hero ignores the PC's (SA14), and a write the session refuses stops the later ones.</summary>
[TestClass]
public sealed class ScriptStoreApplyTests
{
    private const byte GlobalVariable = 1, Number = 3, PcVariable = 5, PcFlag = 6, Triggerer = 0, Player = 21;
    // ARC-DEV-265: a second PC (a network game's), which the session holds no arrays for.
    private static readonly ScrObjectRef Chest = new(1, "obj.chest"), PcRef = new(1, "pc"), SecondPc = new(1, "pc.2");
    private static readonly ScrAdmissionSet Set = new("store", ScrAdmissionSet.S1.Conditions, ScrAdmissionSet.S1.Actions,
        ScrAdmissionSet.S1.ValueKinds.Concat(ScrAdmissionSet.StoreValueKinds), [ScrFocusKind.Player, ScrFocusKind.Triggerer]);

    private sealed class Host(ScrProgram program) : IScrHost, IScrStoreQueries
    {
        public ScrSlot? Slot(ScrObjectRef attachee, int point) => new(1, 0, 0);
        public ScrScriptLookup Script(int number) => ScrScriptLookup.Of(program);
        public bool GlobalFlag(int index) => false;
        public int StoryState() => 0;
        public bool IsMonsterGenerator(ScrObjectRef attachee) => false;
        public ScrDialogState DialogState(ScrObjectRef attachee) => ScrDialogState.Normal;
        public ScrTrapCheck CheckTrapBeforeUse(ScrObjectRef triggerer, ScrObjectRef attachee) => ScrTrapCheck.NoTrap;
        public bool IsTrapObject(ScrObjectRef attachee) => false;
        public ScrObjectRef? Player() => PcRef;
        public ScrObjectRef? Leader(ScrObjectRef critter) => null;
        public bool IsJilted(ScrObjectRef critter) => false;
        public bool IsPc(ScrObjectRef critter) => critter == PcRef || critter == SecondPc;
        public int GlobalVariable(int index) => 0;
        public int PcVariable(ScrObjectRef pc, int index) => 0;
        public bool PcFlag(ScrObjectRef pc, int index) => false;
    }

    private static ScrProgram Program(params (int Type, (byte Kind, int Value)[] Operands)[] actions)
    {
        var bytes = new byte[64 + actions.Length * 132];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(52), actions.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(56), actions.Length);
        for (var line = 0; line < actions.Length; line++)
        {
            var slot = bytes.AsSpan(64 + line * 132 + 44, 44);
            BinaryPrimitives.WriteInt32LittleEndian(slot, actions[line].Type);
            for (var i = 0; i < actions[line].Operands.Length; i++)
            {
                slot[4 + i] = actions[line].Operands[i].Kind;
                BinaryPrimitives.WriteInt32LittleEndian(slot[(12 + 4 * i)..], actions[line].Operands[i].Value);
            }
        }
        var read = ScrProgram.Read(bytes);
        Assert.AreEqual(ScrReadStatus.Read, read.Status);
        return read.Program!;
    }

    private static ScrEffects Run(params (int Type, (byte Kind, int Value)[] Operands)[] actions) => RunBy(PcRef, actions);

    private static ScrEffects RunBy(ScrObjectRef triggerer, params (int Type, (byte Kind, int Value)[] Operands)[] actions)
    {
        var host = new Host(Program(actions));
        var run = ScrDispatcher.RunObject(new(ScrPoint.Use, Chest, triggerer, null), host, new() { Admission = Set });
        Assert.AreEqual(ScrStop.FellOffEnd, run.Stop, run.Stop.ToString());
        return run.Effects;
    }

    private static (int, (byte, int)[]) Assign((byte, int) store, (byte, int) value) => (ScrCatalogue.Action.Assign, [store, value]);
    private static (byte, int) G(int index) => (GlobalVariable, index);
    private static (byte, int) N(int value) => (Number, value);
    private static (byte, int) Pc(byte kind, int index) => (kind, (Player << 16) | index);
    private static (byte, int) Trig(byte kind, int index) => (kind, (Triggerer << 16) | index);

    private static GameSessionOwner Hero()
    {
        var result = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    [TestMethod]
    public void A_run_s_store_changes_are_applied_as_versioned_writes_in_order()
    {
        using var owner = Hero();
        var revision = owner.Version.Revision;
        var effects = Run(Assign(G(5), N(-3)), Assign(Pc(PcVariable, 65535), N(11)), Assign(Pc(PcFlag, 33), N(1)), Assign(Pc(PcFlag, 5), N(1)));
        Assert.AreEqual((1, 1, 2), (effects.GlobalVariables.Count, effects.PcVariables.Count, effects.PcFlags.Count));
        Assert.IsTrue(owner.ApplyScriptStores(effects));
        Assert.AreEqual((-3, 11, true, true), (owner.QueryGlobalVariable(5), owner.QueryPcVariable(65535), owner.QueryPcFlag(5), owner.QueryPcFlag(33)));
        Assert.AreEqual(revision + 4, owner.Version.Revision, "one revision for each value that changed");
        Assert.IsTrue(owner.ApplyScriptStores(effects), "the same values again change nothing and are no failure");
        Assert.AreEqual(revision + 4, owner.Version.Revision);
    }

    [TestMethod]
    public void A_session_without_a_hero_applies_the_global_variables_and_ignores_the_PC_s()
    {
        using var owner = Create();
        var revision = owner.Version.Revision;
        Assert.IsTrue(owner.ApplyScriptStores(Run(Assign(Pc(PcVariable, 4), N(9)), Assign(G(1999), N(77)), Assign(Pc(PcFlag, 4), N(1)))));
        Assert.AreEqual((77, 0, false), (owner.QueryGlobalVariable(1999), owner.QueryPcVariable(4), owner.QueryPcFlag(4)), "SA14: no PC, nothing to write to");
        Assert.AreEqual(revision + 1, owner.Version.Revision);
    }

    [TestMethod]
    public void A_second_PC_s_writes_are_its_own_and_the_session_drops_them()
    {
        using var owner = Hero();
        var revision = owner.Version.Revision;
        // The triggerer is the second PC, the player the hero: the same index is two cells (fields 272 and 273 of each PC, SA14).
        var effects = RunBy(SecondPc, Assign(Trig(PcVariable, 4), N(9)), Assign(Pc(PcVariable, 4), N(5)), Assign(Trig(PcFlag, 7), N(1)));
        CollectionAssert.AreEqual(new[] { new ScrPcVariableWrite(PcRef, 4, 5), new ScrPcVariableWrite(SecondPc, 4, 9) }, effects.PcVariables.ToArray());
        CollectionAssert.AreEqual(new[] { new ScrPcFlagWrite(SecondPc, 7, true) }, effects.PcFlags.ToArray());
        Assert.IsTrue(owner.ApplyScriptStores(effects));
        Assert.AreEqual((5, false), (owner.QueryPcVariable(4), owner.QueryPcFlag(7)), "the hero's cell takes the hero's write; the second PC's are dropped");
        Assert.AreEqual(revision + 1, owner.Version.Revision);
    }

    [TestMethod]
    public void The_empty_effects_apply_to_nothing_and_a_null_argument_is_refused()
    {
        using var owner = Create();
        Assert.IsTrue(owner.ApplyScriptStores(ScrEffects.None));
        Assert.ThrowsExactly<ArgumentNullException>(() => owner.ApplyScriptStores(null!));
        owner.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => owner.ApplyScriptStores(Run(Assign(G(1), N(1)))));
    }
}
