using System.Buffers.Binary;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-SCRIPT-2859 (TQ25, ledger LK1–LK3): a follower's leader-killing script (attachment point 20, the loop of the kill step 0x45DA20) through the use-script host (<see cref="AreaUseScriptHost.RunLeaderKilling"/>) over an
/// invented content stack and map (no original data): the dead NPC is the triggerer, the follower the attachee (its slot is written back) and the PC leader the extra object; the answer is the caller's to ignore. The opt-in
/// check admits the installed script 1014 whole under the dying set.
/// </summary>
[TestClass]
public sealed class AreaLeaderKillingScriptTests
{
    private const byte Triggerer = 0, Extra = 12, Number = 3, GlobalFlag = 4, Local = 2;
    private const int PcType = 15, NpcType = 16, FollowerType = 14, SeenFlag = 2371;

    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);

    // The follower rows have another object type (14) than the dead NPC (16) only so that a script can tell the two roles apart by type.
    private static AreaObjectRecord Row(int index, int script, int point = ScrPoint.LeaderKilling, int type = NpcType)
    {
        var slots = new List<AreaSparseElement> { new(point, [0, 0, script]) };
        var fields = new List<AreaFieldValue>
        {
            new(31, AreaPresence.Array, AreaValueOrigin.Instance, null, slots, null),
            new(18, AreaPresence.Value, AreaValueOrigin.Instance, 0u, [], null),
        };
        return new(Key(index), 0, type, 0, "p", "p", "r", "s", AreaScope.Core, null, 10 + index, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], fields, true, true, true);
    }

    private readonly record struct Clause(int Type, params (byte Kind, int Value)[] Operands);
    private static (byte, int) N(int value) => (Number, value);
    private static (Clause, Clause, Clause) If(Clause condition, Clause onTrue, Clause onFalse) => (condition, onTrue, onFalse);
    private static (Clause, Clause, Clause) Do(Clause action) => (new(0), action, new(0));

    private static byte[] Program(params (Clause Condition, Clause OnTrue, Clause OnFalse)[] lines)
    {
        var bytes = new byte[64 + lines.Length * 132];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(52), lines.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(56), lines.Length);
        for (var line = 0; line < lines.Length; line++)
            foreach (var (offset, clause) in new[] { (0, lines[line].Condition), (44, lines[line].OnTrue), (88, lines[line].OnFalse) })
            {
                var slot = bytes.AsSpan(64 + line * 132 + offset, 44);
                BinaryPrimitives.WriteInt32LittleEndian(slot, clause.Type);
                for (var i = 0; i < clause.Operands.Length; i++) { slot[4 + i] = clause.Operands[i].Kind; BinaryPrimitives.WriteInt32LittleEndian(slot[(12 + 4 * i)..], clause.Operands[i].Value); }
            }
        return bytes;
    }

    private static void WithContent(Action<ScriptContent> test)
    {
        var root = Directory.CreateTempSubdirectory("arc-script-2859-");
        try
        {
            void Write(string relative, byte[] bytes)
            {
                var path = Path.Combine(root.FullName, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, bytes);
            }
            Write("tig.dat", AreaOwnerFixture.Dat());
            Write("modules/Syn.dat", AreaOwnerFixture.Dat());
            // 921: only when the extra object is a PC (type 15) and the triggerer the dead NPC (type 16, not the follower's 14) does it set global flag 2371 and the attachee's local flag 1; the answer is skip default.
            Write("scr/00921roles.scr", Program(
                Do(new(ScrCatalogue.Action.GetObjectType, (Extra, 0), (Local, 0))),
                Do(new(ScrCatalogue.Action.GetObjectType, (Triggerer, 0), (Local, 1))),
                If(new(ScrCatalogue.Condition.Equal, (Local, 0), N(PcType)), new(ScrCatalogue.Action.DoNothing), new(ScrCatalogue.Action.Goto, N(6))),
                If(new(ScrCatalogue.Condition.Equal, (Local, 1), N(NpcType)), new(ScrCatalogue.Action.DoNothing), new(ScrCatalogue.Action.Goto, N(6))),
                Do(new(ScrCatalogue.Action.Assign, (GlobalFlag, SeenFlag), N(1))),
                Do(new(ScrCatalogue.Action.SetLocalFlag, N(1))),
                Do(new(ScrCatalogue.Action.ReturnSkipDefault))));
            using var content = ScriptContent.Open(ContentMountPlan.Discover(root.FullName, "Syn").Plan!).Content!;
            test(content);
        }
        finally { root.Delete(true); }
    }

    private static AreaUseScriptHost Host(GameSessionOwner session, ScriptContent content, params AreaObjectRecord[] shown) =>
        new(session, 1, shown, [], content, new AreaScriptWorld(() => null, _ => false, _ => null, () => null));

    private static bool Flag(GameSessionOwner session, int flag) =>
        session.Flags.Read(new(new(Guid.NewGuid()), session.FlagVersion, new FlagId(flag))).Value == true;

    [TestMethod]
    public void The_dead_npc_is_the_triggerer_the_follower_the_attachee_and_the_pc_leader_the_extra_object()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var victim = Row(1, 0, ScrPoint.Heartbeat);
            var follower = Row(2, 921, type: FollowerType);
            var host = Host(session, content, victim, follower);
            var ran = host.RunLeaderKilling(AreaUseScriptHost.EntityId(follower), AreaUseScriptHost.EntityId(victim), host.Pc, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.AreEqual((ScrOutcome.SkipDefault, 921), (ran.Run!.Outcome, ran.Run.Script));
            Assert.IsTrue(Flag(session, SeenFlag), "the extra object is the PC and the triggerer the NPC");
            var slot = host.Slot(new ScrObjectRef(1, AreaUseScriptHost.EntityId(follower)), ScrPoint.LeaderKilling)!.Value;
            Assert.AreNotEqual(0u, slot.LocalFlags, "the follower's own slot took the local flag: it is the attachee");
            Assert.IsNull(host.Slot(new ScrObjectRef(1, AreaUseScriptHost.EntityId(victim)), ScrPoint.LeaderKilling), "the victim runs nothing of its own");
        });
    }

    [TestMethod]
    public void A_different_extra_object_changes_what_the_script_sees()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var victim = Row(1, 0, ScrPoint.Heartbeat);
            var follower = Row(2, 921, type: FollowerType);
            var host = Host(session, content, victim, follower);
            host.RunLeaderKilling(AreaUseScriptHost.EntityId(follower), AreaUseScriptHost.EntityId(victim), new ScrObjectRef(1, AreaUseScriptHost.EntityId(victim)), session.Rules.Scripts);
            Assert.IsFalse(Flag(session, SeenFlag), "an NPC extra object is not the PC leader");
        });
    }

    [TestMethod]
    public void A_follower_with_no_script_at_the_point_or_off_the_shown_map_runs_nothing()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var victim = Row(1, 0, ScrPoint.Heartbeat);
            var plain = Row(2, 0, ScrPoint.Heartbeat);
            var host = Host(session, content, victim, plain);
            var none = host.RunLeaderKilling(AreaUseScriptHost.EntityId(plain), AreaUseScriptHost.EntityId(victim), host.Pc, session.Rules.Scripts);
            Assert.IsNull(none.Run);
            var away = host.RunLeaderKilling("oid:elsewhere", AreaUseScriptHost.EntityId(victim), host.Pc, session.Rules.Scripts);
            Assert.IsNull(away.Run);
            Assert.IsFalse(Flag(session, SeenFlag));
        });
    }

    [TestMethod, TestCategory("RealData")]
    public void The_installed_leader_killing_script_1014_is_admitted_whole_by_the_dying_set()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in leader-killing check; this is not a synthetic pass.");
        using var content = ConversationSessionFixture.Scripts(root);
        var read = content.Script(1014);
        Assert.AreEqual(ScriptContentStatus.Read, read.Status, read.Detail);
        var admission = ScrAdmission.Admit(read.Program!, AreaUseScriptHost.DyingAdmission);
        Assert.IsTrue(admission.IsAdmitted, $"script 1014 is admitted whole by the dying set: {admission.Refusal}");
    }
}
