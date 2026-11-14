using Arcanum.Data;
using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Reaction;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-SCRIPT-1863 (ledger CS4, CS5, CS10): opt-in real-data check (<c>DAT_ROOT</c>, the game folder) that installed scripts asking obj is in dialog (26) of the PC and obj can see (28) toward the PC run under the use host with the answers the session and the sight check give.
/// Only script numbers, line numbers and outcomes are printed or asserted, never original text.
/// </summary>
[TestClass]
public sealed class AreaHeroSightScriptRealDataTests
{
    private const int NpcType = 16;

    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);

    // A critter row (type 16) carrying a script in the heartbeat slot, with the complete fields the combat reader needs.
    private static AreaObjectRecord Npc(int index, int script, int point = ScrPoint.Heartbeat)
    {
        AreaFieldValue Value(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);
        AreaFieldValue Array(int slot, params long[] values) => new(slot, AreaPresence.Array, AreaValueOrigin.Instance, null, [.. values.Select((value, key) => new AreaSparseElement(key, [value]))], null);
        AreaFieldValue Empty(int slot) => new(slot, AreaPresence.Empty, AreaValueOrigin.Instance, null, [], null);
        var scripts = new AreaFieldValue(31, AreaPresence.Array, AreaValueOrigin.Instance, null, [new AreaSparseElement(point, [0, 0, script])], null);
        return new(Key(index), 0, NpcType, 28359, "p", "p", "r", "s", AreaScope.Core, null, 10 + index, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [],
        [
            Value(0, 0x60000000), Value(18, 0), Value(19, 0), Value(21, 1), Value(22, 2), Value(25, 0), Value(26, 20), Value(27, 0), Value(28, 0), Value(29, 4), Array(30), scripts,
            Value(36, 0), Value(37, 0), Array(38, 20, 20, 20, 20, 20, 20, 20, 20, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 20, 1, 0),
            Array(39, 0, 0, 0, 5, 0, 0, 0, 0, 0, 0, 0, 0), Array(40), Array(41), Value(42, 10), Value(43, 0), Value(44, 0), Value(45, 0), Empty(46), Empty(47),
            Value(57, 0), Value(63, 0), Value(69, 0), Value(71, 0), Value(74, 10), Value(75, 10), Value(81, 0), Value(82, 0), Value(84, 0), Value(85, 0), Value(89, 0), Array(92),
        ], true, true, true);
    }

    private static ScriptContent Content()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in hero sight script check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        var opened = ContentStack.Open(discovery.Plan!);
        Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
        return ScriptContent.Over(opened.Stack!);
    }

    private static string Show(string label, AreaScriptedUse ran) =>
        $"{label}: {ran.Outcome} stop {ran.Run?.StopLine} trace " + string.Join(" ", ran.Run?.Trace.Select(t => $"{t.Line}:c{t.Condition}{(t.Held ? "T" : "F")}>{t.Next?.ToString() ?? "-"}") ?? []) + " " + (ran.Run?.Refusal?.Code.ToString() ?? "");

    private static string Path(AreaScriptedUse ran) => string.Join(" ", ran.Run!.Trace.Select(t => $"{t.Line}:c{t.Condition}{(t.Held ? "T" : "F")}"));

    // Script 2602 (an NPC's heartbeat, admitted whole by the heartbeat set): line 0 asks obj can see (28) of the attachee toward the PC; false goes to line 4 (return run default), true goes on to line 1 (within range, 34).
    [TestMethod, TestCategory("RealData")]
    public void The_installed_heartbeat_2602_asks_whether_the_attachee_sees_the_PC_and_follows_the_answer()
    {
        using var content = Content();
        foreach (var (sees, expected) in new[] { (false, "0:c28F 4:c0T"), (true, "0:c28T 1:c34F 4:c0T") })
        {
            using var session = ConversationSessionFixture.CreateWithHero(1);
            var row = Npc(1, 2602);
            var asked = new List<(string Observer, string Target)>();
            var world = new AreaScriptWorld(() => null, _ => false, _ => null, () => null, CanSee: (observer, target) => { asked.Add((observer, target)); return sees; });
            var host = new AreaUseScriptHost(session, 1, [row], [], content, world);
            var ran = host.RunHeartbeat(row, ScrPoint.Heartbeat, session.Rules.Scripts);
            Console.WriteLine($"HERO-SIGHT-RUN|2602|sees {sees}|{ran.Outcome}|{Path(ran)}");
            Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, ran.Outcome, ran.Detail);
            Assert.AreEqual(expected, Path(ran), $"sees {sees}");
            CollectionAssert.AreEqual(new[] { (AreaUseScriptHost.EntityId(row), host.Pc.Code!) }, asked, "the check asks the attachee toward the PC, once");
        }
    }

    // Script 1130 (an NPC's heartbeat, admitted whole by the heartbeat set): its line 0 returns run default, so a heartbeat never reaches the lines below; they are entered here as a deferred call enters a script (ScrCallEvent at line 19). Line 19 asks obj is in dialog (26) of the PC
    // (the session's conversation slot, CS4): open goes to the return at once, closed goes on to line 20 (the attachee's own slot), line 21 and line 22.
    [TestMethod, TestCategory("RealData")]
    public void The_installed_script_1130_asks_whether_the_PC_is_in_dialog_from_line_19()
    {
        using var content = Content();
        foreach (var (open, expectedStop, expected) in new[] { (false, 22, "19:c26F 20:c26F 21:c0T 22:c5F"), (true, 19, "19:c26T") })
        {
            using var session = ConversationSessionFixture.CreateWithHero(1);
            var row = Npc(1, 1130);
            var other = Npc(2, 0);
            if (open) session.BeginReactionConversation(session.Version, AreaUseScriptHost.EntityId(other), new ReactionNpcFacts(50, CharacterRace.Human, false, false));
            var host = new AreaUseScriptHost(session, 1, [row, other], [], content, new AreaScriptWorld(() => null, _ => false, _ => null, () => null));
            var run = ScrDispatcher.RunCall(new ScrCallEvent(1130, 19, host.Pc, new ScrObjectRef(1, AreaUseScriptHost.EntityId(row))), host,
                new() { Rules = session.Rules.Scripts, Admission = AreaUseScriptHost.HeartbeatAdmission });
            var path = string.Join(" ", run.Trace.Select(t => $"{t.Line}:c{t.Condition}{(t.Held ? "T" : "F")}"));
            Console.WriteLine($"HERO-SIGHT-RUN|1130|dialog open {open}|{run.Outcome}|{path}");
            Assert.AreEqual(ScrOutcome.RunDefault, run.Outcome, run.Refusal?.Code.ToString());
            Assert.AreEqual(expectedStop, run.StopLine);
            Assert.AreEqual(expected, path, $"dialog open {open}");
        }
    }
}
