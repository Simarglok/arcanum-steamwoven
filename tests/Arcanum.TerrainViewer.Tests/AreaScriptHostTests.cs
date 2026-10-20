using Arcanum.Data.Area;
using Arcanum.Data.Fixes;
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
        return new(session, map, speaker, state, new AreaScriptHost(session, map, [speaker], state, null));
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
        // ARC-WORLD-039: a deferred call's PC and speaker roles that moved with the PC are this host's objects again.
        var away = new SessionMapId(f.Map.RuntimeMap + 78);
        Assert.AreEqual(f.Host.Pc, f.Host.Local(new(away, AreaScriptHost.PcCode)));
        Assert.AreEqual(npc, f.Host.Local(new(away, f.Entity)));
        Assert.AreEqual(new ScrObjectRef(away.RuntimeMap, "oid:other"), f.Host.Local(new(away, "oid:other")));
        Assert.IsNull(f.Host.Local(null));
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
        Assert.IsTrue(AreaScriptHost.Admission.IsSubsetOf(ScrAdmissionSet.S7));
        Assert.IsTrue(new[] { ScrCatalogue.Action.PlaySound, ScrCatalogue.Action.PlaySoundOn, ScrCatalogue.Action.PlaySoundScheme }.All(AreaScriptHost.Admission.Actions.Contains),
            "ARC-SCRIPT-040: the sound actions run in the viewer");
        Assert.ThrowsExactly<InvalidOperationException>(() => f.Host.StoryState());
    }

    [TestMethod]
    public void Scheme_requests_go_to_the_session_and_effect_sounds_wait_for_the_viewer()
    {
        using var f = Create();
        var npc = f.Host.Object(f.Speaker);
        var run = new FakeEffectsRun(f.Host, npc).Run(Program.Sounds(), new(777, 4, 0x0201));
        Assert.AreEqual(ScrOutcome.SkipDefault, run.Outcome, run.Refusal?.Code.ToString());
        Assert.IsTrue(f.Host.Apply(run.Effects));
        Assert.AreEqual((15, 39), (f.Session.QuerySound().State.Slot0, f.Session.QuerySound().State.Slot1), "play sound scheme is the session's request");
        CollectionAssert.AreEqual(new[]
        {
            new ScrSound(ScrSoundKind.Effect, 3008),
            new ScrSound(ScrSoundKind.OnObject, 4010, 0, npc),
            new ScrSound(ScrSoundKind.OnObject, 4011, 0, f.Host.Pc),
        }, f.Host.TakeSounds().ToArray());
        Assert.AreEqual(0, f.Host.TakeSounds().Count, "taken once");
        Assert.AreEqual(f.Speaker, f.Host.SpeakerOf(npc));
        Assert.IsNull(f.Host.SpeakerOf(f.Host.Pc), "the PC's cell is the viewer's");
        Assert.IsTrue(f.Host.Apply(run.Effects), "the same request again changes nothing and stops nothing");
        Assert.AreEqual(3, f.Host.TakeSounds().Count);
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
    public void The_dialog_read_applies_the_data_fixes_of_the_rules_it_is_given()
    {
        var root = Directory.CreateTempSubdirectory("arc-data-022-");
        try
        {
            var dlg = System.Text.Encoding.Latin1.GetBytes("{1}{Six fields.}{}{}{}{}");
            File.WriteAllBytes(Path.Combine(root.FullName, "tig.dat"), AreaOwnerFixture.Dat());
            Directory.CreateDirectory(Path.Combine(root.FullName, "modules"));
            File.WriteAllBytes(Path.Combine(root.FullName, "modules", "Syn.dat"), AreaOwnerFixture.Dat());
            Directory.CreateDirectory(Path.Combine(root.FullName, "scr")); Directory.CreateDirectory(Path.Combine(root.FullName, "dlg"));
            File.WriteAllBytes(Path.Combine(root.FullName, "scr", "00790stone.scr"), new byte[64]);
            File.WriteAllBytes(Path.Combine(root.FullName, "dlg", "00790stone.dlg"), dlg);
            var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(dlg)).ToLowerInvariant();
            var catalogue = new DlgDataFixCatalogue([new("DF1", "dlg/00790stone.dlg", sha, DlgFixOperation.CompleteLastRecord)]);
            using var content = ScriptContent.Open(Arcanum.Data.Content.ContentMountPlan.Discover(root.FullName, "Syn").Plan!, fixes: catalogue).Content!;
            Assert.AreEqual(AreaConversationStartStatus.DialogResourceMismatch, AreaScriptHost.LoadDialog(content, 790, 1, DataFixProfile.Original).Status);
            Assert.AreEqual(AreaConversationStartStatus.Started, AreaScriptHost.LoadDialog(content, 790, 1, DataFixProfile.Steamwoven).Status);
            Assert.AreEqual(AreaConversationStartStatus.DialogResourceMismatch, AreaScriptHost.LoadDialog(content, 790, 1, DataFixProfile.Original).Status,
                "the mode is read at each call");
        }
        finally { root.Delete(true); }
    }

    [TestMethod]
    public void Scripts_and_dialogs_come_from_the_game_content_stack_by_the_original_names()
    {
        var root = Directory.CreateTempSubdirectory("arc-script-011-");
        try
        {
            void Write(string relative, byte[] bytes)
            {
                var path = Path.Combine(root.FullName, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, bytes);
            }
            var program = ProgramBytes();
            var dlg = System.Text.Encoding.Latin1.GetBytes("{1}{Hello.}{}{}{}{}{}{2}{Bye.}{}{1}{}{0}{}");
            Write("tig.dat", AreaOwnerFixture.Dat());
            // The module archive's copy wins over the game folder's; the DLG is the script's name in dlg\.
            Write("modules/Syn.dat", AreaOwnerFixture.Dat(("dlg\\00777test.dlg", dlg), ("scr\\00777test.scr", program)));
            Write("scr/00777test.scr", new byte[3]);
            Write("scr/00778twice.scr", program); Write("scr/00778again.scr", program);
            Write("dlg/00778again.dlg", dlg); Write("dlg/00778twice.dlg", System.Text.Encoding.Latin1.GetBytes("{1}{nul\0}{}{}{}{}{}"));
            Write("scr/00782empty.scr", program); Write("dlg/00782empty.dlg", System.Text.Encoding.Latin1.GetBytes("{1}{Six fields.}{}{}{}{}"));
            Write("scr/00783spaced.scr", program); Write("dlg/00783spaced.dlg", System.Text.Encoding.Latin1.GetBytes("{1}{Hello.} {}{}{}{}{}\r\n\t{2}{Bye.}{}{1}{}{0}{}"));
            Write("scr/00779named.scr", program); Write("dlg/00779named.dlg", dlg);
            Write("dlg/00781other.dlg", dlg); Write("scr/00781other2.scr", program);

            Assert.AreEqual(AreaContentRootStatus.ModuleOutsideGameRoot, AreaContentRoot.OpenScripts(Path.Combine(root.FullName, "Syn.dat")).Status);
            Assert.AreEqual(AreaContentRootStatus.MountFailed, AreaContentRoot.OpenScripts(Path.Combine(root.FullName, "modules", "Absent.dat")).Status);
            var load = AreaContentRoot.OpenScripts(Path.Combine(root.FullName, "modules", "Syn.dat"));
            Assert.AreEqual((AreaContentRootStatus.Ready, "6 script numbers"), (load.Status, load.Detail));
            using var content = load.Content!;
            using var f = Create();
            var host = new AreaScriptHost(f.Session, f.Map, [f.Speaker], f.State, content);
            Assert.AreEqual(ScrScriptStatus.Found, host.Script(777).Status, "the module copy, not the game folder's three bytes");
            Assert.AreEqual(ScrScriptStatus.Found, host.Script(778).Status, "the last listed name wins, as in the original");
            Assert.AreEqual(ScrScriptStatus.Missing, host.Script(780).Status);
            Assert.AreEqual(ScrScriptStatus.Missing, host.Script(0).Status);
            Assert.AreEqual(ScrScriptStatus.Missing, f.Host.Script(777).Status, "a host without content has no script");
            Assert.AreEqual(AreaConversationStartStatus.Started, AreaScriptHost.LoadDialog(content, 777, 1, DataFixProfile.Steamwoven).Status);
            Assert.AreEqual(AreaConversationStartStatus.DialogResourceMismatch, AreaScriptHost.LoadDialog(content, 777, 2, DataFixProfile.Steamwoven).Status, "node 2 is a PC reply");
            Assert.AreEqual(AreaConversationStartStatus.DialogResourceMismatch, AreaScriptHost.LoadDialog(content, 777, 9, DataFixProfile.Steamwoven).Status);
            Assert.AreEqual(AreaConversationStartStatus.DialogUnreadable, AreaScriptHost.LoadDialog(content, 778, 1, DataFixProfile.Steamwoven).Status, "00778twice.dlg, the winning name's");
            // ARC-SCRIPT-012: the game opens no DLG without records; one with a space between fields reads.
            Assert.AreEqual((AreaConversationStartStatus.DialogResourceMismatch, "dialog 782: dlg/00782empty.dlg: no record the original keeps (IncompleteRecord)"),
                (AreaScriptHost.LoadDialog(content, 782, 1, DataFixProfile.Steamwoven).Status, AreaScriptHost.LoadDialog(content, 782, 1, DataFixProfile.Steamwoven).Detail));
            Assert.AreEqual(AreaConversationStartStatus.Started, AreaScriptHost.LoadDialog(content, 783, 1, DataFixProfile.Steamwoven).Status);
            Assert.AreEqual(AreaConversationStartStatus.DialogResourceMismatch, AreaScriptHost.LoadDialog(content, 781, 1, DataFixProfile.Steamwoven).Status, "00781other.dlg is not script 781's name");
            Assert.AreEqual(AreaConversationStartStatus.DialogResourceMismatch, AreaScriptHost.LoadDialog(content, 780, 1, DataFixProfile.Steamwoven).Status);

            // A file that changes after its first read is never mixed in: the script is unreadable, the dialog and every
            // later talk of a conversation host over that content report SourceChanged.
            Write("scr/00778twice.scr", ProgramBytes(flags: 1));
            Assert.AreEqual(ScrScriptStatus.Unreadable, host.Script(778).Status);
            Assert.AreEqual(AreaConversationStartStatus.Started, AreaScriptHost.LoadDialog(content, 779, 1, DataFixProfile.Steamwoven).Status, "first read");
            Write("dlg/00779named.dlg", System.Text.Encoding.Latin1.GetBytes("{1}{Changed.}{}{}{}{}{}"));
            Assert.AreEqual(AreaConversationStartStatus.SourceChanged, AreaScriptHost.LoadDialog(content, 779, 1, DataFixProfile.Steamwoven).Status);
            using var source = new AreaOwnerFixture(motionBodies: true);
            var conversations = new AreaConversationHost(source.Owner.LoadStrict(), content, f.Session);
            var talk = conversations.Begin(f.Speaker);
            Assert.AreEqual((AreaConversationStartStatus.SourceChanged, "a script or dialog file changed since it was first read"), (talk.Status, talk.Detail));
            Assert.IsNull(conversations.LastRun, "nothing ran");
            Assert.AreEqual(AreaConversationStartStatus.ContentUnavailable, new AreaConversationHost(source.Owner.LoadStrict(), null, f.Session).Begin(f.Speaker).Status);

            // A file read by an earlier talk is checked again before the next one runs, without being read by it.
            using var fresh = AreaContentRoot.OpenScripts(Path.Combine(root.FullName, "modules", "Syn.dat")).Content!;
            Assert.AreEqual(ScrScriptStatus.Found, new AreaScriptHost(f.Session, f.Map, [f.Speaker], f.State, fresh).Script(779).Status);
            var later = new AreaConversationHost(source.Owner.LoadStrict(), fresh, f.Session);
            Assert.AreNotEqual(AreaConversationStartStatus.SourceChanged, later.Begin(f.Speaker).Status);
            Write("scr/00779named.scr", ProgramBytes(flags: 2));
            Assert.AreEqual(AreaConversationStartStatus.SourceChanged, later.Begin(f.Speaker).Status);
            Assert.IsTrue(fresh.Changed);
        }
        finally { root.Delete(true); }

        static byte[] ProgramBytes(uint flags = 0)
        {
            var bytes = new byte[64 + 132];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes, flags);
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

        // ARC-SCRIPT-040: play sound 3008, play sound 4010 on the attachee (focus kind 1), on the triggerer (kind 0), scheme 15/39.
        internal static ScrProgram Sounds() => Lines(
            (82, [(3, 3008)]), (83, [(3, 4010), (1, 0)]), (83, [(3, 4011), (0, 0)]), (87, [(3, 15), (3, 39)]), (1, []));

        private static ScrProgram Lines(params (int Action, (byte Kind, int Value)[] Operands)[] lines)
        {
            var bytes = new byte[64 + lines.Length * 132];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(52), lines.Length);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(56), lines.Length);
            for (var i = 0; i < lines.Length; i++)
            {
                var at = 64 + i * 132 + 44;
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at), lines[i].Action);
                for (var o = 0; o < lines[i].Operands.Length; o++)
                {
                    bytes[at + 4 + o] = lines[i].Operands[o].Kind;
                    System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at + 12 + 4 * o), lines[i].Operands[o].Value);
                }
            }
            return ScrProgram.Read(bytes).Program!;
        }

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
        internal ScrRunResult Run(ScrProgram program, ScrSlot slot) => ScrDispatcher.RunObject(new(ScrPoint.Dialog, npc, inner.Pc, null), new With(inner, program, slot, story),
            new() { Admission = ScrAdmissionSet.S7 });

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
