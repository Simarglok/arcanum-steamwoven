using System.Text;
using Arcanum.Data;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Scripting.Tests.Dialog;

/// <summary>
/// ARC-SCRIPT-1358 opt-in check (<c>DAT_ROOT</c>, the game folder): Virgil's altar node 250 of DLG 1324 offers its `l:` reply as the world-map reply, and the
/// installed `l:` replies are counted (a shape only, never text).
/// </summary>
[TestClass]
public sealed class DialogWorldMapRealDataTests
{
    [TestMethod, TestCategory("RealData"), TestCategory("DlgRealData")]
    public void Virgils_altar_node_offers_the_world_map_reply_and_ends_the_talk_for_the_map()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in world map check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var generated = GeneratedDialogFiles.Load(stack).Files!;
        var greeter = new DialogGreetingTests.FakeGreeter(new DialogGreetingFacts(true, true, false, 0, 1, 0, DialogReactionCategory.Neutral, true),
            new DialogGreetingSources(generated, new MesFile?[4], null), [], DialogRuleProfile.Original, []);

        int replies = 0, files = 0, offered = 0, blocked = 0;
        var virgil = false;
        foreach (var key in stack.Keys().Where(k => k.EndsWith(".dlg", StringComparison.Ordinal)))
        {
            var document = DlgReader.Read(stack.Read(stack.Resolve(key).Winner!).Bytes!, new DlgReaderOptions { Mode = DlgReadMode.Original });
            if (!document.Records.Any(r => r.Role == DlgRecordRole.Player && Encoding.Latin1.GetString(r.Text.Bytes.ToArray()).Equals("l:", StringComparison.OrdinalIgnoreCase))) continue;
            files++;
            replies += document.Records.Count(r => r.Role == DlgRecordRole.Player && Encoding.Latin1.GetString(r.Text.Bytes.ToArray()).Equals("l:", StringComparison.OrdinalIgnoreCase));
            foreach (var npc in document.Records.Where(r => r.Role == DlgRecordRole.Npc))
            {
                var ports = new DialogConversationTests.Ports();
                var attempt = DialogConversation.Start(document,
                    new DialogConversationRequest(new DialogNodeId(npc.Id), new DialogParticipants(new DialogId(1), new DialogParticipantId(800), new DialogParticipantId(900)),
                        new DialogScriptLine(0), new DialogPcFacts(DialogPcGender.Male, 8, false, 0), new DialogSpeakerNames("Ada", "Bram")),
                    new DialogConversationPorts(ports, ports, DialogConversationTests.Owner(), new DialogConversationTests.World(), Greeting: greeter,
                        WorldMap: new DialogWorldMapTests.WorldMap()));
                if (attempt.Phase != DialogConversationPhase.AwaitingSelection) continue;
                blocked += attempt.Options.Count(o => o.Marker is { Letter: 'l' } && o.Block is not null);
                var option = attempt.Options.FirstOrDefault(o => o.Transition == DialogOptionTransition.WorldMap && o.Block is null);
                if (option is null) continue;
                offered++;
                if (!key.Contains("01324", StringComparison.Ordinal) || npc.Id != 250) continue;
                // Virgil's altar node: the picked `l:` reply ends the talk for the world map with no script line to resume.
                Assert.AreEqual(DialogConversationSelectStatus.Ended, attempt.Select(option.Token).Status);
                Assert.AreEqual(new DialogConversationEnd(DialogEndKind.WorldMapWindow, null), attempt.End);
                Assert.IsTrue(attempt.Effects.Any(e => e.Command is OpenWorldMapCommand));
                virgil = true;
            }
        }
        Console.WriteLine($"WORLDMAP|files {files}|`l:` replies {replies}|offered nodes {offered}|blocked {blocked}");
        Assert.IsTrue(virgil, "DLG 1324 node 250 offers and takes the world map reply");
        Assert.IsTrue(files > 0 && offered > 0, "an installed NPC node offers the world map reply");
        Assert.AreEqual(0, blocked, "no installed `l:` reply is refused for its own shape");
    }
}
