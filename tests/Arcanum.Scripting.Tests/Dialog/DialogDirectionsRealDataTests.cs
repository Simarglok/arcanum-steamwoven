using System.Text;
using Arcanum.Data;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;
using Arcanum.Data.Messages;
using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Scripting.Tests.Dialog;

/// <summary>
/// ARC-SCRIPT-1533 opt-in check (<c>DAT_ROOT</c>, the game folder): every installed Player reply that starts with <c>d:</c> or <c>x:</c> reads as a directions reply
/// (a price mark, areas inside the installed area table), the generated NPC files hold the ranges the talk reads, and an installed directions talk runs end to end.
/// Only counts and shapes are compared, never text.
/// </summary>
[TestClass]
public sealed class DialogDirectionsRealDataTests
{
    private sealed class Areas : IDialogAreaPort
    {
        public bool Known(DialogAreaQuery query) => false;
    }

    private sealed class Directions(WorldAreaTable table) : IDialogDirectionsPort
    {
        public string? AreaName(int area) => area >= 0 && area < table.Areas.Count ? table.Areas[area].Name : null;
        public DialogBearing? Bearing(DialogParticipantId npc, int area) => new(3, 50 * 0xC60L);
    }

    private sealed class Purse : IDialogPcGoldPort
    {
        public int PcGold(DialogGoldQuery query) => 1000;
    }

    private sealed class Gold : IDialogGoldPort
    {
        public int Total(DialogGoldQuery query) => 1000;
        public DialogCommandResult Execute(GoldCommand command) => DialogCommandResult.Applied;
    }

    [TestMethod, TestCategory("RealData"), TestCategory("DlgRealData")]
    public void Every_installed_directions_reply_is_admitted_and_runs_to_its_way_with_the_installed_files()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in directions check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var table = WorldAreaTable.Open(stack).Table!;
        var files = GeneratedDialogFiles.Load(stack).Files!;
        var greeter = new DialogGreetingTests.FakeGreeter(new DialogGreetingFacts(true, true, false, 0, 0, 0, DialogReactionCategory.Neutral, true),
            new DialogGreetingSources(files, new MesFile?[4], null), [], DialogRuleProfile.Original, []);

        int replies = 0, learn = 0, paid = 0, talks = 0, ran = 0;
        var distinct = new SortedSet<int>();
        foreach (var key in stack.Keys().Where(k => k.EndsWith(".dlg", StringComparison.Ordinal)))
        {
            var document = DlgReader.Read(stack.Read(stack.Resolve(key).Winner!).Bytes!, new DlgReaderOptions { Mode = DlgReadMode.Original });
            var dir = new List<DlgRecord>();
            foreach (var record in document.Records.Where(r => r.Role == DlgRecordRole.Player))
            {
                var text = Encoding.Latin1.GetString(record.Text.Bytes.ToArray());
                if (text.Length < 2 || char.ToLowerInvariant(text[0]) is not ('d' or 'x') || text[1] != ':') continue;
                var parsed = DialogConversation.ParseDirectionsReply(text);
                Assert.IsNotNull(parsed, $"{key} {record.Id}: a directions reply outside the subset");
                replies++;
                if (char.ToLowerInvariant(text[0]) == 'x') learn++;
                if (parsed.Value.Price >= 1) paid++;
                foreach (var area in parsed.Value.Listed.Concat(parsed.Value.Visible))
                {
                    distinct.Add(area);
                    Assert.IsTrue(area < table.Areas.Count && table.Areas[area].Name.Length > 0, $"{key} {record.Id}: area {area} is in the area table with a name");
                }
                dir.Add(record);
            }
            foreach (var reply in dir)
            {
                // The NPC node whose replies hold it: the last NPC record before it in the file.
                var npc = document.Records.Take(reply.SerializedIndex).Last(r => r.Role == DlgRecordRole.Npc);
                var ports = new DialogConversationTests.Ports();
                // The installed replies are gated by the NPC's local flag 0 (lf0 0): it reads 0.
                ports.Flags[0] = LocalFlagReadResult.Found(0);
                var attempt = DialogConversation.Start(document,
                    new DialogConversationRequest(new DialogNodeId(npc.Id), new DialogParticipants(new DialogId(1), new DialogParticipantId(800), new DialogParticipantId(900)),
                        new DialogScriptLine(0), new DialogPcFacts(DialogPcGender.Male, 8, false, 0), new DialogSpeakerNames("Ada", "Bram")),
                    new DialogConversationPorts(ports, ports, DialogConversationTests.Owner(), new DialogConversationTests.World(), Areas: new Areas(), Gold: new Gold(),
                        Greeting: greeter, Purse: new Purse(), Directions: new Directions(table)));
                if (attempt.Phase != DialogConversationPhase.AwaitingSelection) continue;
                var option = attempt.Options.FirstOrDefault(o => o.Transition == DialogOptionTransition.Directions && o.Block is null);
                if (option is null) continue;
                talks++;
                Assert.AreEqual(DialogConversationSelectStatus.Continued, attempt.Select(option.Token).Status, $"{key} {npc.Id}");
                Assert.IsTrue(attempt.Options.Count >= 2 && attempt.Options.Take(attempt.Options.Count - 1).All(o => o.Transition == DialogOptionTransition.DirectionsStep && o.Text.Length > 0), $"{key} {npc.Id}: areas and a way on");
                var area = attempt.Options.First();
                Assert.AreEqual(DialogConversationSelectStatus.Continued, attempt.Select(area.Token).Status);
                // A paid reply asks its price first.
                if (attempt.Options.Count == 2 && attempt.Options[0].Transition == DialogOptionTransition.DirectionsStep && attempt.NpcLine!.Text.Any(char.IsAsciiDigit))
                    Assert.AreEqual(DialogConversationSelectStatus.Continued, attempt.Select(attempt.Options[0].Token).Status);
                Assert.IsTrue(attempt.NpcLine!.Origin is { Kind: DialogLineOriginKind.Generated or DialogLineOriginKind.Override or DialogLineOriginKind.MissingLine or DialogLineOriginKind.Empty }, $"{key} {npc.Id}");
                ran++;
            }
        }
        Console.WriteLine($"DIRECTIONS|replies {replies}|x: {learn}|paid {paid}|distinct areas {distinct.Count}|talks {talks}|ran {ran}");
        Assert.AreEqual((2, 0, 0, 1), (replies, learn, paid, distinct.Count), "two installed d: replies, free, no x:, one distinct area");
        Assert.AreEqual((2, 2), (talks, ran), "both run through the page to the way");

        // The NPC files hold the ranges the talk reads: the list lines, and a line for every direction at the three distances of d: (x: has no installed reply to check).
        var npcFile = GeneratedDialogFiles.NpcLineFile(GeneratedLineGroup.Npc, true, true, false);
        Assert.IsTrue(files.RunLength(npcFile, 100, 199) > 0, "the list line 100..199");
        for (var direction = 0; direction < 8; direction++)
            foreach (var bearing in new[] { 200, 300, 400 })
                Assert.IsTrue(files.RunLength(npcFile, bearing + direction * 10, bearing + direction * 10 + 9) > 0, $"the way line {bearing + direction * 10}");
    }
}
