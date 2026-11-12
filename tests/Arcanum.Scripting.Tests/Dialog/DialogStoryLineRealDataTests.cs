using System.Text;
using Arcanum.Data;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Scripting.Tests.Dialog;

/// <summary>
/// ARC-SCRIPT-1321 opt-in check (<c>DAT_ROOT</c>, the game folder): every installed Player reply that starts with <c>c:</c> is offered as a story reply, the story
/// files hold lines for the story states the replies are reached at, and an installed story talk runs end to end. Only counts and shapes are compared, never text.
/// </summary>
[TestClass]
public sealed class DialogStoryLineRealDataTests
{
    [TestMethod, TestCategory("RealData"), TestCategory("DlgRealData")]
    public void Every_installed_story_reply_is_admitted_and_an_installed_story_talk_runs_with_the_installed_lines()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in story check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var generated = GeneratedDialogFiles.Load(stack).Files!;
        var greeter = new DialogGreetingTests.FakeGreeter(new DialogGreetingFacts(true, true, false, 0, 1, 0, DialogReactionCategory.Neutral, true),
            new DialogGreetingSources(generated, new MesFile?[4], null), [], DialogRuleProfile.Original, []);

        int replies = 0, files = 0, plain = 0, offered = 0, talks = 0, withLine = 0;
        foreach (var key in stack.Keys().Where(k => k.EndsWith(".dlg", StringComparison.Ordinal)))
        {
            var document = DlgReader.Read(stack.Read(stack.Resolve(key).Winner!).Bytes!, new DlgReaderOptions { Mode = DlgReadMode.Original });
            var any = false;
            foreach (var record in document.Records.Where(r => r.Role == DlgRecordRole.Player))
            {
                var text = Encoding.Latin1.GetString(record.Text.Bytes.ToArray());
                if (text.Length < 2 || char.ToLowerInvariant(text[0]) != 'c' || text[1] != ':') continue;
                replies++;
                any = true;
                if (text.Length == 2) plain++;
            }
            if (!any) continue;
            files++;
            foreach (var npc in document.Records.Where(r => r.Role == DlgRecordRole.Npc))
            {
                var ports = new DialogConversationTests.Ports();
                var attempt = DialogConversation.Start(document,
                    new DialogConversationRequest(new DialogNodeId(npc.Id), new DialogParticipants(new DialogId(1), new DialogParticipantId(800), new DialogParticipantId(900)),
                        new DialogScriptLine(0), new DialogPcFacts(DialogPcGender.Male, 8, false, 0), new DialogSpeakerNames("Ada", "Bram")),
                    new DialogConversationPorts(ports, ports, DialogConversationTests.Owner(), new DialogConversationTests.World(), Story: new DialogStoryAreaTests.Story(1),
                        Greeting: greeter));
                if (attempt.Phase != DialogConversationPhase.AwaitingSelection) continue;
                // No installed c: reply is refused for its own shape (a transfer or window action in the same reply).
                Assert.IsFalse(attempt.Options.Any(o => o.Marker is { Letter: 'c' } && o.Block is { Kind: DialogOptionBlockKind.UnsupportedInteraction }), $"{key} {npc.Id}");
                var option = attempt.Options.FirstOrDefault(o => o.Transition == DialogOptionTransition.Story && o.Block is null);
                if (option is null) continue;
                offered++;
                if (talks >= 40) continue;
                talks++;
                Assert.AreEqual(DialogConversationSelectStatus.Continued, attempt.Select(option.Token).Status, $"{key} {npc.Id}");
                Assert.AreEqual(1, attempt.Options.Count, "the only way on is the PC class line");
                Assert.IsTrue(attempt.NpcLine!.Origin is { Kind: DialogLineOriginKind.Generated or DialogLineOriginKind.MissingLine or DialogLineOriginKind.Empty });
                if (attempt.NpcLine.Origin!.Kind == DialogLineOriginKind.Generated && attempt.NpcLine.Text.Length > 0) withLine++;
            }
        }
        Console.WriteLine($"STORY|replies {replies}|files {files}|plain {plain}|offered nodes {offered}|talks {talks}|with an installed line {withLine}");
        Assert.AreEqual((212, 41, 212), (replies, files, plain), "212 replies in 41 files, every one exactly `c:`");
        Assert.IsTrue(talks > 0 && withLine > 0, "an installed NPC node offers a story reply and tells an installed story line");

        // The story files: how many (race, state) lines exist for the male-to-male file.
        var stories = new[] { GeneratedDialogFile.StoryMaleToMale, GeneratedDialogFile.StoryMaleToFemale, GeneratedDialogFile.StoryFemaleToFemale, GeneratedDialogFile.StoryFemaleToMale };
        foreach (var file in stories)
        {
            var mes = generated.File(file)!;
            var numbers = mes.Messages.Select(m => m.Number).ToArray();
            // 25 story states (0..24) of 11 races (0..10) each: the line is numbered race + 100 x state.
            Assert.AreEqual(275, numbers.Length, file.ToString());
            Assert.IsTrue(numbers.All(n => n / DialogGreeting.StoryStateStride <= 24 && n % DialogGreeting.StoryStateStride <= 10), file.ToString());
            Console.WriteLine($"STORY-FILE|{file}|entries {numbers.Length}|{numbers.Min()}..{numbers.Max()}|states {string.Join(",", numbers.Select(n => n / DialogGreeting.StoryStateStride).Distinct().Order())}|races {string.Join(",", numbers.Select(n => n % DialogGreeting.StoryStateStride).Distinct().Order())}");
        }
    }
}
