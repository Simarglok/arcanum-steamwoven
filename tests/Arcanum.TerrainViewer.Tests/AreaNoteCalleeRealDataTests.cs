using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-SCRIPT-2806 (ledger TQ17, TQ18; issue #2806): opt-in real-data check (<c>DAT_ROOT</c>, the game folder) of the three notes of the loop shape (use scripts 1567, 1698, 1700, items of type 13): the use script loops over everyone near the note,
/// and for the one named Virgil (name number 6409) schedules a deferred call of its callee (1687, 1697, 1699) after 20, 10 and 90 seconds of play with the hero as the triggerer and Virgil as the attachee. Every callee runs a dialog action (type 4), which the item-use set does not admit:
/// the call is run by the deferred-call host (<see cref="AreaConversationHost.RunCall"/>) under its own set, not the item-use set, so the callee runs and offers its conversation at node 1, for a speaker of the shown map and for a companion carried there.
/// Only script numbers, node numbers and outcomes are printed or asserted, never original text. The item and the critter rows are synthetic.
/// </summary>
[TestClass]
public sealed class AreaNoteCalleeRealDataTests
{
    private const int NpcType = 16, WrittenType = 13, VirgilName = 6409;

    private static ScriptContent Content()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in note callee check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        return ScriptContent.Over(opened.Stack!);
    }

    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);

    // A critter row (type 16) named like Virgil beside the note.
    private static AreaObjectRecord Virgil(int index, long x, long y)
    {
        AreaFieldValue Value(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);
        return new(Key(index), 0, NpcType, 17102, "p", "p", "r", "s", AreaScope.Core, null, x, y, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
            [Value(0, 0x60000000), Value(19, 0), Value(21, VirgilName)], true, true, true);
    }

    private static AreaObjectRecord Note(int index, int script, long x, long y) => new(Key(index), 0, WrittenType, 0, "p", "p", "r", "s", AreaScope.Core, null, x, y,
        Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], [new AreaFieldValue(31, AreaPresence.Array, AreaValueOrigin.Instance, null, [new AreaSparseElement(1, [0, 0, script])], null)], true, true, true);

    // The reading of a note beside Virgil: the use script's run, then the deferred call it queued, fired as the event pass fires it (AreaConversationHost.RunCall with the stored roles).
    [TestMethod, TestCategory("RealData")]
    public void The_three_notes_schedule_their_callee_for_the_critter_named_Virgil_and_the_callee_opens_its_conversation_when_the_call_fires()
    {
        using var content = Content();
        foreach (var (use, callee, seconds) in new[] { (1567, 1687, 20), (1698, 1697, 10), (1700, 1699, 90) })
        {
            using var session = ConversationSessionFixture.CreateWithHero(1);
            var virgil = Virgil(2, 12, 21);
            var note = Note(1, use, 11, 20);
            var noteEntity = "oid:" + note.Key.ObjectIdSha256;
            var items = new AreaInventoryMap(1, new([], [new(noteEntity, new(new(SessionItemType.Written, 14086, 0, 0), 1, 0, 0, null, [1]), new CellCoord(11, 20))]),
                new Dictionary<string, AreaObjectRecord> { [noteEntity] = note });
            var world = new AreaScriptWorld(() => new(11, 21), _ => false, _ => null, () => null);
            var uses = new AreaUseScriptHost(session, 1, [virgil], [items], content, world);
            var read = uses.RunItemUse(1, noteEntity, session.Rules.Scripts);
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, read.Outcome, $"{use}: {read.Detail}");
            var calls = session.QueryScriptCalls();
            Assert.AreEqual(1, calls.Count, $"{use}: one deferred call queued");
            var pending = calls[0];
            var virgilEntity = AreaUseScriptHost.EntityId(virgil);
            Console.WriteLine($"NOTE-CALLEE|{use}|delay {pending.DeadlineMs - session.QueryClockSnapshot().GameTimestampMs} ms|call {pending.Call.Script} line {pending.Call.Line} triggerer={pending.Call.Triggerer?.Entity} attachee={(pending.Call.Attachee?.Entity == virgilEntity ? "virgil" : pending.Call.Attachee?.Entity)}");
            Assert.AreEqual((callee, 0), (pending.Call.Script, pending.Call.Line));
            Assert.AreEqual(SessionEventKinds.PlayerEntity, pending.Call.Triggerer?.Entity, "the reader");
            Assert.AreEqual(virgilEntity, pending.Call.Attachee?.Entity, "the critter named Virgil");
            Assert.AreEqual(seconds * 8000L, pending.DeadlineMs - session.QueryClockSnapshot().GameTimestampMs, $"{use}: the delay of the call (seconds of play, 8 game seconds each)");
        }
    }

    // The callees fired as a deferred call, for a speaker of the shown map (the use host answering the talk set) and for a companion carried from its home map (the narrow set of AdmissionFor): none is refused, each opens its conversation at node 1.
    [TestMethod, TestCategory("RealData")]
    public void The_callees_run_and_offer_their_conversation_for_a_speaker_of_the_shown_map_and_for_a_carried_companion()
    {
        using var content = Content();
        var (template, area) = AreaVisitorTalkTests.Load();
        foreach (var carried in new[] { false, true })
        {
            var map = carried ? 2 : area.RuntimeMap;
            using var session = ConversationSessionFixture.CreateWithHero(map);
            AreaConversationHost host;
            AreaSpeaker speaker;
            if (carried)
            {
                host = new AreaConversationHost(area, new SessionMapId(2), [], content, session, party: true, null);
                speaker = host.AdmitVisitor(template, out var refusal)!;
                Assert.IsNull(refusal);
                Assert.IsTrue(host.IsCarried(speaker));
            }
            else
            {
                speaker = new AreaSpeaker(0, template.Speaker.Row, template.Speaker.ScriptId, template.Speaker.WorldX, template.Speaker.WorldY);
                host = new AreaConversationHost(area, [speaker], content, session);
            }
            var pc = new SessionScriptRole(new SessionMapId(map), SessionEventKinds.PlayerEntity);
            var talker = new SessionScriptRole(new SessionMapId(map), AreaConversationState.EntityId(speaker.Row));
            var uses = new AreaUseScriptHost(session, map, [speaker.Row], [], content, new AreaScriptWorld(() => new(12, 20), _ => false, _ => null, () => null));
            host.Scripts.QueryHost = () => uses;
            foreach (var script in new[] { 1687, 1697, 1699 })
            {
                var ran = host.RunCall(new(script, 0, pc, talker), out var detail);
                Console.WriteLine($"NOTE-CALLEE|carried={carried}|{script}|ran={ran}|{detail}");
                Assert.IsTrue(ran, $"carried {carried}, script {script}: {detail}");
                Assert.AreEqual((script, 1, talker.Entity), (host.LastCallDialog?.Script, host.LastCallDialog?.Node, host.LastCallDialog?.Npc?.Code), $"carried {carried}, script {script}: {detail}");
                // The viewer opens it as a scripted conversation (BeginScriptedConversation): the DLG has no local flag or counter operation, so the scripted start admits it.
                if (script == 1687)
                {
                    // Line 1 queued the same script again 2 seconds of play on (16 game seconds), before the dialog action of line 2.
                    var again = session.QueryScriptCalls().Single(c => c.Call.Script == 1687);
                    Assert.AreEqual((0, 16000L, talker.Entity), (again.Call.Line, again.DeadlineMs - session.QueryClockSnapshot().GameTimestampMs, again.Call.Attachee?.Entity), "the polling call");
                    Assert.IsFalse(uses.GlobalFlag(2301), "the first node's action sets the flag that script 1687's line 0 tests");
                }
                var started = host.BeginScripted(speaker, host.LastCallDialog!);
                Console.WriteLine($"NOTE-CALLEE|carried={carried}|{script}|open={started.Status} {started.Detail}");
                Assert.AreEqual(AreaConversationStartStatus.Started, started.Status, started.Detail);
                host.EndReaction();
                if (script != 1687) continue;
                // Script 1687 queues itself again 2 seconds on (its line 1) while that flag is clear: the opened conversation sets it, so the next firing goes to its line 3 (skip default) and starts no conversation.
                Assert.IsTrue(uses.GlobalFlag(2301), "the conversation's first node set global flag 2301");
                Assert.IsTrue(host.RunCall(new(1687, 0, pc, talker), out detail), detail);
                Assert.IsNull(host.LastCallDialog, "the polling call ends once the conversation has opened: " + detail);
            }
        }
    }
}
