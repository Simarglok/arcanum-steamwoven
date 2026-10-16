using Arcanum.Data.Area;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>The AREA viewer's SCR host (ARC-SCRIPT-009): the speaker's dialog slot over the session, effects into it.</summary>
[TestClass]
public sealed class AreaScriptHostTests
{
    private sealed record Fixture(GameSessionOwner Session, SessionMapId Map, AreaSpeaker Speaker, AreaConversationState State, AreaScriptHost Host) : IDisposable
    {
        public void Dispose() => Session.Dispose();
        public string Entity => AreaConversationState.EntityId(Speaker.Row);
        public long? Change(string property) => Session.QueryMapChange(Map, MapChangeKey.ForEntity(Entity, property));
    }

    private static Fixture Create()
    {
        using var source = new AreaOwnerFixture(motionBodies: true);
        var area = source.Owner.LoadStrict();
        var row = area.Objects.First(o => o.WorldX is not null && o.WorldY is not null);
        var scripts = new AreaFieldValue(31, AreaPresence.Array, AreaValueOrigin.Instance, null, [new AreaSparseElement(ScrPoint.Dialog, [4, 0x0201, 777])], null);
        var speaker = new AreaSpeaker(0, row with { EffectiveFields = [.. row.EffectiveFields.Where(f => f.Slot != 31), scripts] }, 777, row.WorldX!.Value, row.WorldY!.Value);
        var session = ConversationSessionFixture.Create(area.RuntimeMap);
        var map = new SessionMapId(area.RuntimeMap);
        var state = new AreaConversationState(session, map, [AreaConversationState.EntityId(speaker.Row)], 50);
        return new(session, map, speaker, state, new AreaScriptHost(session, map, [speaker], state, "unused.dat"));
    }

    [TestMethod]
    public void The_dialog_slot_is_the_loaded_attachment_until_the_session_overrides_it()
    {
        using var f = Create();
        var npc = f.Host.Object(f.Speaker);
        Assert.AreEqual(new ScrSlot(777, 4, 0x0201), f.Host.Slot(npc, ScrPoint.Dialog));
        Assert.IsNull(f.Host.Slot(npc, ScrPoint.Use), "only the dialog point is modelled");
        Assert.IsNull(f.Host.Slot(new ScrObjectRef(f.Map.RuntimeMap, "oid:other"), ScrPoint.Dialog));
        Assert.IsNull(f.Host.Slot(new ScrObjectRef(f.Map.RuntimeMap + 1, npc.Code), ScrPoint.Dialog));
        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.SetMapChange(f.Session.Version, f.Map, MapChangeKey.ForEntity(f.Entity, AreaConversationState.LocalFlagsProperty), 6).Disposition);
        Assert.AreEqual(new ScrSlot(777, 6, 0x0201), f.Host.Slot(npc, ScrPoint.Dialog));
    }

    [TestMethod]
    public void A_writeback_stores_only_what_differs_from_the_loaded_slot_and_clears_it_on_return()
    {
        using var f = Create();
        var npc = f.Host.Object(f.Speaker);
        var host = new FakeEffectsRun(f.Host, npc);
        Assert.IsTrue(f.Host.Apply(host.Run(Program.SetFlag(0), new(777, 4, 0x0201)).Effects));
        Assert.AreEqual(5L, f.Change(AreaConversationState.LocalFlagsProperty));
        Assert.IsNull(f.Change(AreaScriptHost.CountersProperty));
        Assert.IsNull(f.Change(AreaScriptHost.ScriptProperty));
        Assert.IsTrue(f.State.LocalFlag(0, 0), "the DLG side reads the same key");
        // Removing the script and clearing the flag: the number differs, the flags are the loaded ones again.
        Assert.IsTrue(f.Host.Apply(host.Run(Program.RemoveAndClear(0), f.Host.Slot(npc, ScrPoint.Dialog)!.Value).Effects));
        Assert.AreEqual(0L, f.Change(AreaScriptHost.ScriptProperty));
        Assert.IsNull(f.Change(AreaConversationState.LocalFlagsProperty));
        Assert.AreEqual(new ScrSlot(0, 4, 0x0201), f.Host.Slot(npc, ScrPoint.Dialog));
        // The removed script no longer runs: the talk runs default.
        Assert.AreEqual(ScrStop.NoScript, ScrDispatcher.RunObject(new(ScrPoint.Dialog, npc, f.Host.Pc, null), f.Host).Stop);
    }

    [TestMethod]
    public void Global_flags_go_to_the_session_and_story_state_is_not_bound()
    {
        using var f = Create();
        var npc = f.Host.Object(f.Speaker);
        var run = new FakeEffectsRun(f.Host, npc).Run(Program.SetGlobal(2593), new(777, 4, 0x0201));
        Assert.IsTrue(f.Host.Apply(run.Effects));
        Assert.IsTrue(f.Host.GlobalFlag(2593));
        var flags = f.Session.Flags;
        Assert.AreEqual(true, flags.Read(new GlobalFlagReadRequest(new GlobalFlagRequestId(Guid.NewGuid()), flags.Version, new FlagId(2593))).Value);
        Assert.IsFalse(AreaScriptHost.Admission.Actions.Contains(ScrCatalogue.Action.GetStoryState) || AreaScriptHost.Admission.Actions.Contains(ScrCatalogue.Action.SetStoryState));
        Assert.IsTrue(AreaScriptHost.Admission.IsSubsetOf(ScrAdmissionSet.S3));
        Assert.ThrowsExactly<InvalidOperationException>(() => f.Host.StoryState());
    }

    [TestMethod]
    public void The_pc_leads_a_following_speaker_and_is_the_only_pc()
    {
        using var f = Create();
        var npc = f.Host.Object(f.Speaker);
        Assert.IsNull(f.Host.Leader(npc));
        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.SetMapChange(f.Session.Version, f.Map, MapChangeKey.ForEntity(f.Entity, AreaConversationState.FollowProperty), 1).Disposition);
        Assert.AreEqual(f.Host.Pc, f.Host.Leader(npc));
        Assert.IsTrue(f.Host.IsPc(f.Host.Pc));
        Assert.IsFalse(f.Host.IsPc(npc));
        Assert.AreEqual(f.Host.Pc, f.Host.Player());
        Assert.IsFalse(f.Host.IsJilted(npc));
    }

    // Small programs run through the dispatcher with the fixture host, so the effects are the dispatcher's own.
    private static class Program
    {
        internal static ScrProgram SetFlag(int flag) => Build((8, flag), (1, 0));
        internal static ScrProgram RemoveAndClear(int flag) => Build((9, flag), (5, 0), (1, 0));
        internal static ScrProgram SetGlobal(int flag) => Build((100, flag), (1, 0));

        private static ScrProgram Build(params (int Action, int Operand)[] lines)
        {
            var bytes = new byte[64 + lines.Length * 132];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(52), lines.Length);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(56), lines.Length);
            for (var i = 0; i < lines.Length; i++)
            {
                var at = 64 + i * 132 + 44;
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at), lines[i].Action);
                bytes[at + 4] = 3;
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at + 12), lines[i].Operand);
            }
            return ScrProgram.Read(bytes).Program!;
        }
    }

    /// <summary>Wraps the fixture host so a run uses a given program and slot.</summary>
    private sealed class FakeEffectsRun(AreaScriptHost inner, ScrObjectRef npc)
    {
        internal ScrRunResult Run(ScrProgram program, ScrSlot slot) => ScrDispatcher.RunObject(new(ScrPoint.Dialog, npc, inner.Pc, null), new With(inner, program, slot));

        private sealed class With(AreaScriptHost inner, ScrProgram program, ScrSlot slot) : IScrHost
        {
            public ScrSlot? Slot(ScrObjectRef attachee, int point) => slot;
            public ScrScriptLookup Script(int number) => ScrScriptLookup.Of(program);
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
    }
}
