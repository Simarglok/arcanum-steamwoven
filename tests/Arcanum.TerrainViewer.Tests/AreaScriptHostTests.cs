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

    [TestMethod]
    public void Stored_counters_and_number_override_the_loaded_slot_and_bad_values_are_refused()
    {
        using var f = Create();
        var npc = f.Host.Object(f.Speaker);
        f.Session.SetMapChange(f.Session.Version, f.Map, MapChangeKey.ForEntity(f.Entity, AreaScriptHost.CountersProperty), 0x09);
        f.Session.SetMapChange(f.Session.Version, f.Map, MapChangeKey.ForEntity(f.Entity, AreaScriptHost.ScriptProperty), 778);
        Assert.AreEqual(new ScrSlot(778, 4, 0x09), f.Host.Slot(npc, ScrPoint.Dialog));
        // A session holding a counter word or script number the port cannot read is refused when the state is built.
        foreach (var (property, value) in new[] { (AreaScriptHost.CountersProperty, -1L), (AreaScriptHost.CountersProperty, 1L << 32), (AreaScriptHost.ScriptProperty, 1L << 31) })
        {
            using var g = Create();
            g.Session.SetMapChange(g.Session.Version, g.Map, MapChangeKey.ForEntity(g.Entity, property), value);
            Assert.ThrowsExactly<ArgumentException>(() => new AreaConversationState(g.Session, g.Map, [g.Entity], 50), $"{property} {value}");
        }
    }

    [TestMethod]
    public void Apply_refuses_what_the_viewer_cannot_store()
    {
        using var f = Create();
        var npc = f.Host.Object(f.Speaker);
        // Story state is not bound; a writeback for another object is not this host's.
        Assert.IsFalse(f.Host.Apply(new FakeEffectsRun(f.Host, npc, story: 3).Run(Program.SetStory(5), new(777, 4, 0x0201)).Effects));
        Assert.IsNull(f.Change(AreaConversationState.LocalFlagsProperty));
        var other = new ScrObjectRef(f.Map.RuntimeMap, "oid:other");
        var foreign = new FakeEffectsRun(f.Host, other).Run(Program.SetFlag(1), new(777, 4, 0x0201));
        Assert.IsNotNull(foreign.Effects.Writeback);
        Assert.IsFalse(f.Host.Apply(foreign.Effects));
        Assert.IsNull(f.Session.QueryMapChange(f.Map, MapChangeKey.ForEntity(other.Code, AreaConversationState.LocalFlagsProperty)));
    }

    [TestMethod]
    public void Scripts_and_dialogs_are_found_by_number_once_and_a_dialog_needs_an_npc_node()
    {
        var directory = Directory.CreateTempSubdirectory("arc-script-009-");
        try
        {
            var program = Program.SetFlag(0).Lines.Count == 2 ? ProgramBytes() : throw new InvalidOperationException();
            var scripts = Path.Combine(directory.FullName, "scripts.dat");
            File.WriteAllBytes(scripts, AreaOwnerFixture.Dat(("scr\\00777Test.scr", program), ("scr\\00778Twice.scr", program), ("scr\\00778Again.scr", program), ("scr\\x.scr", program)));
            using var f = Create();
            var host = new AreaScriptHost(f.Session, f.Map, [f.Speaker], f.State, scripts);
            Assert.AreEqual(ScrScriptStatus.Found, host.Script(777).Status);
            Assert.AreEqual(ScrScriptStatus.Unreadable, host.Script(778).Status, "two members for one number");
            Assert.AreEqual(ScrScriptStatus.Missing, host.Script(779).Status);
            Assert.AreEqual(ScrScriptStatus.Missing, host.Script(0).Status);
            var dialogs = Path.Combine(directory.FullName, "dialogs.dat");
            var dlg = System.Text.Encoding.Latin1.GetBytes("{1}{Hello.}{}{}{}{}{}{2}{Bye.}{}{1}{}{0}{}");
            File.WriteAllBytes(dialogs, AreaOwnerFixture.Dat(("dlg\\00777Test.dlg", dlg), ("dlg\\00778A.dlg", dlg), ("dlg\\00778B.dlg", dlg)));
            Assert.AreEqual(AreaConversationStartStatus.Started, AreaScriptHost.LoadDialog(dialogs, 777, 1).Status);
            Assert.AreEqual(AreaConversationStartStatus.DialogResourceMismatch, AreaScriptHost.LoadDialog(dialogs, 777, 2).Status, "node 2 is a PC reply");
            Assert.AreEqual(AreaConversationStartStatus.DialogResourceMismatch, AreaScriptHost.LoadDialog(dialogs, 777, 9).Status);
            Assert.AreEqual(AreaConversationStartStatus.DialogResourceMismatch, AreaScriptHost.LoadDialog(dialogs, 778, 1).Status, "two members for one number");
            Assert.AreEqual(AreaConversationStartStatus.DialogResourceMismatch, AreaScriptHost.LoadDialog(dialogs, 779, 1).Status);
        }
        finally { directory.Delete(true); }

        static byte[] ProgramBytes()
        {
            var bytes = new byte[64 + 132];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(52), 1);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(56), 1);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(64 + 44), ScrCatalogue.Action.ReturnRunDefault);
            return bytes;
        }
    }

    // Small programs run through the dispatcher with the fixture host, so the effects are the dispatcher's own.
    private static class Program
    {
        internal static ScrProgram SetFlag(int flag) => Build((8, flag), (1, 0));
        internal static ScrProgram RemoveAndClear(int flag) => Build((9, flag), (5, 0), (1, 0));
        internal static ScrProgram SetGlobal(int flag) => Build((100, flag), (1, 0));
        internal static ScrProgram SetStory(int value) => Build((43, value), (1, 0));

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
    private sealed class FakeEffectsRun(AreaScriptHost inner, ScrObjectRef npc, int? story = null)
    {
        internal ScrRunResult Run(ScrProgram program, ScrSlot slot) => ScrDispatcher.RunObject(new(ScrPoint.Dialog, npc, inner.Pc, null), new With(inner, program, slot, story));

        private sealed class With(AreaScriptHost inner, ScrProgram program, ScrSlot slot, int? story) : IScrHost
        {
            public ScrSlot? Slot(ScrObjectRef attachee, int point) => slot;
            public ScrScriptLookup Script(int number) => ScrScriptLookup.Of(program);
            public bool GlobalFlag(int index) => inner.GlobalFlag(index);
            public int StoryState() => story ?? inner.StoryState();
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
