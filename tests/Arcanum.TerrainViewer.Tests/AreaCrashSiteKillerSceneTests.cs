using Arcanum.Scripting.Dialog;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Session;
using Arcanum.Scripting.Scr;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-SCRIPT-2760 / #2761: the Crash Site scene of the Molochean Hand agent (dialog script 1714) and Virgil (dialog script 1324, switched to 1715 for the scene). Opt-in (DAT_ROOT, the game folder): the resumed
/// dialog script of the agent calls Virgil's dialog point, and the conversation that opens belongs to Virgil, not to the agent whose conversation just closed (0x568480 runs the conversation NPC's point 9; the dialog
/// action's attachee is the speaker). Structural identifiers only; no original text is asserted or printed.
/// </summary>
[TestClass]
public sealed class AreaCrashSiteKillerSceneTests
{
    [TestMethod, TestCategory("RealData")]
    public void A_resumed_dialog_script_that_calls_another_npcs_dialog_point_opens_the_conversation_with_that_npc_and_the_chain_goes_on()
    {
        if (Environment.GetEnvironmentVariable("DAT_ROOT") is not { Length: > 0 } root) { Assert.Inconclusive("DAT_ROOT is required for the opt-in killer scene check; this is not a synthetic pass."); return; }
        using var content = ConversationSessionFixture.Scripts(root);
        var result = AreaLoadOperation.Execute(1, TerrainViewerArguments.Parse(["--mode", "crash-site-area", "--module", Path.Combine(root, "modules", "Arcanum.dat"),
            "--resource", Path.Combine(root, "arcanum2.dat"), "--source-id", "map-test", "--actor-resource", Path.Combine(root, "arcanum1.dat"),
            "--game-root", root, "--area-map", "Arcanum1-024-fixed"]), default);
        var snapshot = result.Area!.Snapshot;
        using var session = ConversationSessionFixture.Create(snapshot.RuntimeMap);
        // The agent stands in a Context sector row of the window (the map's resident neighbourhood), so the host is told its speakers as the viewer's map mode tells it.
        var agentRow = snapshot.Objects.Single(r => r.Type == 16 && AreaUseScriptHost.LoadedSlot(r, ScrPoint.Heartbeat) is { Script: 1717 });
        Assert.AreEqual(1714, AreaUseScriptHost.LoadedSlot(agentRow, ScrPoint.Dialog)!.Value.Script);
        var found = AreaConversationHost.FindSpeakers(snapshot);
        var virgilFound = found.Single(s => s.ScriptId == 1324);
        var speakers = new[] { virgilFound with { Index = 0 }, new AreaSpeaker(1, agentRow, 1714, agentRow.WorldX!.Value, agentRow.WorldY!.Value) };
        var host = new AreaConversationHost(snapshot, speakers, content, session);
        var virgil = host.Speakers[0];
        var agent = host.Speakers[1];
        // The PC stands at the agent and Virgil beside it (the scene: Virgil follows the hero), so the agent's vicinity loop finds him.
        var at = new Arcanum.Simulation.Movement.CellCoord(checked((int)agent.WorldX), checked((int)agent.WorldY));
        var virgilRef = new ScrObjectRef(snapshot.RuntimeMap, AreaConversationState.EntityId(virgil.Row));
        var use = new AreaUseScriptHost(session, snapshot.RuntimeMap, snapshot.Objects, [], content,
            new AreaScriptWorld(() => at, _ => false, _ => null, () => null, CritterCell: obj => obj == virgilRef ? new(at.X + 1, at.Y) : null));
        host.Scripts.QueryHost = () => use;

        // The scene's start: the tile script's flag 2320 is set, the agent's heartbeat (1717) switches it on and, with the PC within 4 tiles, calls its dialog point: the agent's own conversation, node 1.
        var flags = session.Flags;
        Assert.AreEqual(GlobalFlagWriteDisposition.Applied, flags.Write(new GlobalFlagWriteRequest(new GlobalFlagRequestId(Guid.NewGuid()), flags.Version, new FlagId(2320), true)).Disposition);
        var beat = use.RunHeartbeat(agentRow, ScrPoint.Heartbeat, session.Rules.Scripts);
        Assert.AreEqual(AreaScriptedUseOutcome.NoTeleport, beat.Outcome, beat.Detail);
        Assert.AreEqual(new ScrDialogStart(use.Pc, new ScrObjectRef(snapshot.RuntimeMap, AreaConversationState.EntityId(agentRow)), 1714, 0, 1), beat.Dialog);
        var opening = host.BeginScripted(agent, beat.Dialog!);
        Assert.AreEqual(AreaConversationStartStatus.Started, opening.Status, opening.Detail);
        Assert.AreSame(agent, opening.Speaker);
        Assert.AreEqual("script 1714 SCR line 0 -> DLG node 1", opening.Detail);

        // The agent's reply "-2" (Virgil in the party): its conversation ends, the dialog slot of the agent resumes at line 2, which calls Virgil's point 9 at line 42; Virgil switches his script to 1715 and calls it.
        var whisper = host.Resume(agent, new DialogScriptLine(2));
        Assert.AreEqual(AreaConversationStartStatus.Started, whisper.Status, whisper.Detail);
        Assert.AreEqual("script 1715 SCR line 0 -> DLG node 1", whisper.Detail);
        Assert.AreSame(virgil, whisper.Speaker, "the conversation is Virgil's, whoever resumed");
        Assert.AreEqual(1715, whisper.DialogScript);
        Assert.AreEqual(1715, use.Slot(virgilRef, ScrPoint.Dialog)!.Value.Script, "Virgil's dialog slot holds the scene's script");

        // Virgil's conversation: node 1, then 5, then 11; the player's last reply returns to Virgil's script (line 2), not to the agent's.
        var conversation = whisper.Conversation!;
        var nodes = new List<int>();
        while (conversation.Phase == DialogConversationPhase.AwaitingSelection && nodes.Count < 8)
        {
            nodes.Add(conversation.CurrentNode.Value);
            Assert.IsTrue(conversation.Options.Any(o => o.Block is null), $"node {conversation.CurrentNode.Value} offers a reply");
            conversation.Select(conversation.Options.First(o => o.Block is null).Token);
        }
        CollectionAssert.AreEqual(new[] { 1, 5, 11 }, nodes);
        Assert.AreEqual(new DialogConversationEnd(DialogEndKind.ReturnToScript, new DialogScriptLine(2)), conversation.End);

        // Virgil's slot (1715) at line 2 finds the agent and runs its point 9 at line 8: the agent's node 100 opens with the agent.
        var turn = host.Resume(whisper.Speaker!, conversation.End!.ScriptLine!.Value);
        Assert.AreEqual(AreaConversationStartStatus.Started, turn.Status, turn.Detail);
        Assert.AreEqual("script 1714 SCR line 8 -> DLG node 100", turn.Detail);
        Assert.AreSame(agent, turn.Speaker);
    }
}
