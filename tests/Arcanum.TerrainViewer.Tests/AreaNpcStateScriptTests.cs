using System.Buffers.Binary;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-SCRIPT-1163 (E5-a, ledger NS1–NS8): the NPC state actions through the use-script host's heartbeat run over an invented content stack and map (no original data): critter disband lets a follower go through
/// the session's party, set and adjust reaction go to the AI host's delegate, the standpoints and the teleport map are the session's object fields that the AI host's route facts read, change script writes an
/// attachment slot as a map change, and change art number is refused (no host applies it).</summary>
[TestClass]
public sealed class AreaNpcStateScriptTests
{
    private const byte Attachee = 1, Number = 3, Player = 21;

    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);

    private static AreaObjectRecord Row(int index, int script, int dialog = 0)
    {
        var slots = new List<AreaSparseElement> { new(ScrPoint.Heartbeat, [0, 0, script]) };
        if (dialog != 0) slots.Insert(0, new(ScrPoint.Dialog, [0, 0, dialog]));
        var fields = new List<AreaFieldValue>
        {
            new(31, AreaPresence.Array, AreaValueOrigin.Instance, null, slots, null),
            new(18, AreaPresence.Value, AreaValueOrigin.Instance, 0u, [], null),
        };
        return new(Key(index), 0, 16, 0, "p", "p", "r", "s", AreaScope.Core, null, 10 + index, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], fields, true, true, true);
    }

    private readonly record struct Clause(int Type, params (byte Kind, int Value)[] Operands);
    private static (byte, int) N(int value) => (Number, value);
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

    private static readonly Clause Run = new(ScrCatalogue.Action.ReturnRunDefault);

    private static void WithContent(Action<ScriptContent> test)
    {
        var root = Directory.CreateTempSubdirectory("arc-script-1163-");
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
            Write("scr/00820disband.scr", Program(Do(new(ScrCatalogue.Action.CritterDisband, (Attachee, -1))), Do(Run)));
            Write("scr/00821reaction.scr", Program(Do(new(ScrCatalogue.Action.SetReaction, (Attachee, -1), (Player, 0), N(60))),
                Do(new(ScrCatalogue.Action.AdjustReaction, (Attachee, -1), (Player, 0), N(-15))), Do(Run)));
            Write("scr/00822stand.scr", Program(Do(new(ScrCatalogue.Action.SetDayStandpoint, (Attachee, -1), N(480), N(521))),
                Do(new(ScrCatalogue.Action.SetNightStandpointEx, (Attachee, -1), N(-3), N(7), N(5012))), Do(Run)));
            Write("scr/00823own.scr", Program(Do(new(ScrCatalogue.Action.ChangeScript, (Attachee, -1), N(ScrPoint.Heartbeat), N(820))), Do(Run)));
            Write("scr/00824dialog.scr", Program(Do(new(ScrCatalogue.Action.ChangeScript, (Attachee, -1), N(ScrPoint.Dialog), N(1200))), Do(Run)));
            Write("scr/00826lowmap.scr", Program(Do(new(ScrCatalogue.Action.SetDayStandpointEx, (Attachee, -1), N(10), N(20), N(0))), Do(Run)));
            Write("scr/00825art.scr", Program(Do(new(ScrCatalogue.Action.ChangeArtNumber, (Attachee, -1), N(4))), Do(Run)));
            using var content = ScriptContent.Open(ContentMountPlan.Discover(root.FullName, "Syn").Plan!).Content!;
            test(content);
        }
        finally { root.Delete(true); }
    }

    private static AreaUseScriptHost Host(GameSessionOwner session, ScriptContent content, AreaScriptWorld? world, params AreaObjectRecord[] shown) =>
        new(session, 1, shown, [], content, world ?? new AreaScriptWorld(() => null, _ => false, _ => null, () => null));

    [TestMethod]
    public void A_disband_lets_a_follower_go_and_leaves_a_free_npc_as_it_is()
    {
        // NS1 (0x45DFC0, force 1): the party's leave; the NPC is free afterwards, a second disband changes nothing.
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var npc = Row(1, 820);
            var entity = AreaUseScriptHost.EntityId(npc);
            Assert.AreEqual(GameSessionDisposition.Changed, session.ForceJoinParty(session.Version, entity).Disposition);
            Assert.IsTrue(session.QueryParty().Followers.Any(f => f.Entity == entity));
            var host = Host(session, content, null, npc);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts).Outcome);
            Assert.IsFalse(session.QueryParty().Followers.Any(f => f.Entity == entity), "no longer a follower");
            var version = session.Version;
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts).Outcome);
            Assert.AreEqual(version, session.Version, "a free NPC: nothing changes");
        });
    }

    [TestMethod]
    public void Set_and_adjust_reaction_go_to_the_ai_hosts_delegate_in_order_and_without_it_the_run_is_left_unapplied()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var npc = Row(1, 821);
            var asked = new List<(string Npc, int Value, bool Set)>();
            var host = Host(session, content, new AreaScriptWorld(() => null, _ => false, _ => null, () => null, ScriptReaction: (who, value, set) => { asked.Add((who, value, set)); return "ok"; }), npc);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts).Outcome);
            var entity = AreaUseScriptHost.EntityId(npc);
            CollectionAssert.AreEqual(new[] { (entity, 60, true), (entity, -15, false) }, asked);
            Assert.AreEqual("ok", host.LastReaction);
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, Host(session, content, null, npc).RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts).Outcome, "no AI host");
        });
    }

    [TestMethod]
    public void The_standpoints_are_the_sessions_fields_and_the_extended_one_writes_the_teleport_map()
    {
        // NS3: the day standpoint (480, 521); the night one (−3, 7) with the sign extension in its high word (y | x >> 31 = −1); the extended action's map 5012 is teleport map 13 (5012 − 4999).
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var npc = Row(1, 822);
            var entity = AreaUseScriptHost.EntityId(npc);
            Assert.IsNull(AreaUseScriptHost.Standpoint(session, 1, entity, day: true));
            Assert.IsNull(session.QueryTeleportMap(entity));
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, Host(session, content, null, npc).RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts).Outcome);
            Assert.AreEqual(new AreaRouteLocation(480, 521), AreaUseScriptHost.Standpoint(session, 1, entity, day: true));
            Assert.AreEqual(new AreaRouteLocation(unchecked((uint)-3), uint.MaxValue), AreaUseScriptHost.Standpoint(session, 1, entity, day: false));
            Assert.AreEqual(13, session.QueryTeleportMap(entity));
        });
    }

    [TestMethod]
    public void An_extended_standpoint_with_a_map_the_session_cannot_keep_is_refused_before_anything_applies()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var npc = Row(1, 826);
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, Host(session, content, null, npc).RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts).Outcome);
            Assert.IsNull(AreaUseScriptHost.Standpoint(session, 1, AreaUseScriptHost.EntityId(npc), day: true), "the standpoint was not written either");
        });
    }

    [TestMethod]
    public void Change_script_of_its_own_slot_takes_effect_when_the_run_ends()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var npc = Row(1, 823);
            var host = Host(session, content, null, npc);
            var obj = new ScrObjectRef(1, AreaUseScriptHost.EntityId(npc));
            Assert.AreEqual(823, host.Slot(obj, ScrPoint.Heartbeat)?.Script);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts).Outcome);
            Assert.AreEqual(820, host.Slot(obj, ScrPoint.Heartbeat)?.Script, "the next heartbeat runs script 820");
        });
    }

    [TestMethod]
    public void Change_script_of_another_slot_is_kept_only_where_the_object_has_the_element()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var plain = Row(1, 824);
            var talker = Row(2, 824, dialog: 1100);
            var rules = session.Rules.Scripts;
            Assert.AreEqual(AreaScriptedUseOutcome.EffectsRejected, Host(session, content, null, plain).RunHeartbeat(plain, ScrPoint.Heartbeat, rules).Outcome, "no dialog element: left unapplied before anything applies");
            var host = Host(session, content, null, talker);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, host.RunHeartbeat(talker, ScrPoint.Heartbeat, rules).Outcome);
            Assert.AreEqual(1200, host.Slot(new ScrObjectRef(1, AreaUseScriptHost.EntityId(talker)), ScrPoint.Dialog)?.Script);
        });
    }

    [TestMethod]
    public void Change_art_number_is_refused_because_no_host_applies_it()
    {
        WithContent(content =>
        {
            using var session = ConversationSessionFixture.Create();
            var npc = Row(1, 825);
            Assert.AreEqual(AreaScriptedUseOutcome.Refused, Host(session, content, null, npc).RunHeartbeat(npc, ScrPoint.Heartbeat, session.Rules.Scripts).Outcome);
        });
    }
}
