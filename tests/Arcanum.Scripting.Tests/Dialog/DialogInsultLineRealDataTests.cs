using System.Text;
using Arcanum.Data;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Scripting.Tests.Dialog;

/// <summary>
/// ARC-SCRIPT-1505 opt-in check (<c>DAT_ROOT</c>, the game folder): every installed NPC node that is exactly <c>i:</c> is admitted and says the race line 1000, the
/// generated NPC class files hold that group, and the installed data holds no NPC <c>m:</c> or <c>r:</c> node (the forms this slice leaves out). Only counts and
/// shapes are compared, never text.
/// </summary>
[TestClass]
public sealed class DialogInsultLineRealDataTests
{
    [TestMethod, TestCategory("RealData"), TestCategory("DlgRealData")]
    public void Every_installed_npc_i_node_says_the_race_line_1000_and_no_npc_m_or_r_node_exists()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in insult-line check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var generated = GeneratedDialogFiles.Load(stack).Files!;
        var greeter = new DialogGreetingTests.FakeGreeter(new DialogGreetingFacts(true, true, false, 0, 0, 0, DialogReactionCategory.Neutral, true),
            new DialogGreetingSources(generated, new MesFile?[4], null), [], DialogRuleProfile.Original, []);

        int nodes = 0, files = 0, spoken = 0, moneyNodes = 0, rumorNodes = 0;
        foreach (var key in stack.Keys().Where(k => k.EndsWith(".dlg", StringComparison.Ordinal)))
        {
            var document = DlgReader.Read(stack.Read(stack.Resolve(key).Winner!).Bytes!, new DlgReaderOptions { Mode = DlgReadMode.Original });
            var any = false;
            foreach (var record in document.Records.Where(r => r.Role == DlgRecordRole.Npc))
            {
                var text = Encoding.Latin1.GetString(record.Text.Bytes.ToArray());
                if (text.Length >= 2 && char.ToLowerInvariant(text[0]) == 'm' && text[1] == ':') moneyNodes++;
                if (text.Length >= 2 && char.ToLowerInvariant(text[0]) == 'r' && text[1] == ':') rumorNodes++;
                if (text.Length != 2 || char.ToLowerInvariant(text[0]) != 'i' || text[1] != ':') continue;
                nodes++;
                any = true;
                var ports = new DialogConversationTests.Ports();
                var attempt = DialogConversation.Start(document,
                    new DialogConversationRequest(new DialogNodeId(record.Id), new DialogParticipants(new DialogId(1), new DialogParticipantId(800), new DialogParticipantId(900)),
                        new DialogScriptLine(0), new DialogPcFacts(DialogPcGender.Male, 8, false, 0), new DialogSpeakerNames("Ada", "Bram")),
                    new DialogConversationPorts(ports, ports, DialogConversationTests.Owner(), new DialogConversationTests.World(), Greeting: greeter));
                // The line itself is never the reason a node stops here; a later field outside the subset (an action, a reply) still may.
                if (attempt.Phase == DialogConversationPhase.AtFrontier) Assert.AreNotEqual(DialogFrontierKind.UnsupportedGeneratedText, attempt.Frontier!.Kind, $"{key} {record.Id}");
                if (attempt.Phase is not (DialogConversationPhase.AwaitingSelection or DialogConversationPhase.Ended)) continue;
                spoken++;
                Assert.IsTrue(attempt.NpcLine!.Origin is { Kind: DialogLineOriginKind.Generated or DialogLineOriginKind.MissingLine or DialogLineOriginKind.Empty }, $"{key} {record.Id}");
                if (attempt.NpcLine.Origin!.Kind == DialogLineOriginKind.Generated) Assert.IsTrue(attempt.NpcLine.Origin.Number is >= 1000 and < 2000, $"{key} {record.Id}: the race group 1000 (+ 50 x the PC's race block)");
            }
            if (any) files++;
        }
        Console.WriteLine($"INSULT-LINE|NPC i: nodes {nodes}|files {files}|spoken {spoken}|NPC m: {moneyNodes}|NPC r: {rumorNodes}");
        Assert.AreEqual((15, 9), (nodes, files), "15 NPC nodes in 9 files");
        Assert.AreEqual(13, spoken, "13 of the 15 nodes run to their line (the other two stop at a later field outside the subset)");
        Assert.AreEqual((0, 0), (moneyNodes, rumorNodes), "no installed NPC node starts with m: or r:");

        foreach (var file in Enum.GetValues<GeneratedDialogFile>().Where(f => GeneratedDialogFiles.Names[(int)f].StartsWith("gd_rce_m", StringComparison.Ordinal)
                     || GeneratedDialogFiles.Names[(int)f].StartsWith("gd_rce_f", StringComparison.Ordinal)))
            Assert.IsTrue(generated.RunLength(file, 1000, 1049) > 0, $"{file} holds the race line 1000");
    }
}
