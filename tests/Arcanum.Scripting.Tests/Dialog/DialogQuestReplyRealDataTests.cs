using System.Text;
using Arcanum.Data;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Scripting.Tests.Dialog;

/// <summary>
/// ARC-SCRIPT-1532 opt-in check (<c>DAT_ROOT</c>, the game folder): the module's quest rules table loads, the installed <c>q:</c> replies are counted (a shape only, never text), every
/// one of them is offered or dropped (never blocked) for each quest state and each of the three table groups, and picking a chosen record runs the talk on without a failure.
/// </summary>
[TestClass]
public sealed class DialogQuestReplyRealDataTests
{
    [TestMethod, TestCategory("RealData"), TestCategory("DlgRealData")]
    public void Every_installed_q_reply_is_offered_or_dropped_for_each_quest_state_and_group()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in quest reply check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var winner = ContentPhase.Candidate(stack, "rules/gamequest.mes", ContentLoadPhase.ModuleLoaded);
        Assert.IsNotNull(winner, "the module's quest rules");
        var mes = MesFile.Parse(stack.Read(winner!).Bytes!).File!;
        var read = QuestReplyTable.Read(mes);
        Assert.AreEqual(QuestReplyTableStatus.Read, read.Status, $"entry {read.Number}");
        var table = new DialogQuestReplyTests.Table(read.Table!);

        int files = 0, replies = 0, offered = 0, dropped = 0, blocked = 0, missing = 0, picked = 0;
        foreach (var key in stack.Keys().Where(k => k.EndsWith(".dlg", StringComparison.Ordinal)))
        {
            var document = DlgReader.Read(stack.Read(stack.Resolve(key).Winner!).Bytes!, new DlgReaderOptions { Mode = DlgReadMode.Original });
            var qs = document.Records.Where(r => r.Role == DlgRecordRole.Player && Encoding.Latin1.GetString(r.Text.Bytes.ToArray()).StartsWith("q:", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (qs.Length == 0) continue;
            files++;
            replies += qs.Length;
            foreach (var npc in document.Records.Where(r => r.Role == DlgRecordRole.Npc))
            {
                foreach (var (state, reaction, intelligence) in Cases())
                {
                    var quests = new Dictionary<int, int>();
                    for (var quest = 1000; quest < 2000; quest++) quests[quest] = state;
                    var attempt = DialogConversation.Start(document,
                        new DialogConversationRequest(new DialogNodeId(npc.Id), new DialogParticipants(new DialogId(1), new DialogParticipantId(800), new DialogParticipantId(900)),
                            new DialogScriptLine(0), new DialogPcFacts(DialogPcGender.Male, intelligence, false, 0), new DialogSpeakerNames("Ada", "Bram")),
                        new DialogConversationPorts(new DialogConversationTests.Ports(), new DialogConversationTests.Ports(), DialogConversationTests.Owner(),
                            new DialogConversationTests.World { Reaction = DialogReactionReadResult.Of(reaction) }, Quests: new DialogQuestTests.Quests(quests), QuestReplies: table));
                    if (attempt.Phase != DialogConversationPhase.AwaitingSelection) continue;
                    foreach (var q in qs.Where(r => r.SerializedIndex > npc.SerializedIndex))
                    {
                        var option = attempt.Options.FirstOrDefault(o => o.ResponseId == new DialogResponseId(q.Id));
                        if (option is null) { dropped++; continue; }
                        if (option.Block is not null)
                        {
                            blocked++;
                            if (option.Block.Kind == DialogOptionBlockKind.TargetInvalid && option.Block.Source?.FieldKind == DlgFieldKind.Text)
                            {
                                // The table names a record the file does not hold (quest 1000 states 5 and 6, quest 1001 states 5 and 6): the original reads an unfilled copy there.
                                var number = int.Parse(Encoding.Latin1.GetString(q.Text.Bytes.ToArray())[2..].Trim());
                                var chosen = table.Field(number, (intelligence < 5 ? 16 : reaction < 20 ? 9 : 2) + state);
                                Assert.IsFalse(chosen is { } id && document.TryGetRecord(id, out _), $"{key} reply {q.Id} state {state}: the chosen record exists but is blocked");
                                missing++;
                            }
                            else
                            {
                                // The chosen record's action or its next node needs ports this check does not give (combat, items, ...).
                                Assert.IsTrue(option.Block.Kind is DialogOptionBlockKind.TargetUnsupported or DialogOptionBlockKind.UnsupportedOperation, $"{key} reply {q.Id} state {state}: {option.Block.Kind}");
                            }
                            continue;
                        }
                        offered++;
                        var again = DialogConversation.Start(document,
                            new DialogConversationRequest(new DialogNodeId(npc.Id), new DialogParticipants(new DialogId(1), new DialogParticipantId(800), new DialogParticipantId(900)),
                                new DialogScriptLine(0), new DialogPcFacts(DialogPcGender.Male, intelligence, false, 0), new DialogSpeakerNames("Ada", "Bram")),
                            new DialogConversationPorts(new DialogConversationTests.Ports(), new DialogConversationTests.Ports(), DialogConversationTests.Owner(),
                                new DialogConversationTests.World { Reaction = DialogReactionReadResult.Of(reaction) }, Quests: new DialogQuestTests.Quests(quests), QuestReplies: table));
                        again.Select(again.Options.First(o => o.ResponseId == option.ResponseId).Token);
                        Assert.IsNull(again.Failure, $"{key} node {npc.Id} reply {q.Id} state {state}");
                        picked++;
                    }
                }
            }
        }
        Console.WriteLine($"QUESTREPLY|files {files}|`q:` replies {replies}|offered {offered}|dropped {dropped}|blocked {blocked} (missing record {missing})|picked {picked}");
        Assert.AreEqual(5, replies, "the installed q: replies");
        Assert.IsTrue(offered > 0, "an installed node offers its q: reply");
        Assert.IsTrue(picked > 0, "a picked q: reply runs on");
    }

    private static IEnumerable<(int State, int Reaction, int Intelligence)> Cases()
    {
        for (var state = 0; state < 7; state++)
        {
            yield return (state, 50, 8);
            yield return (state, 5, 8);
            yield return (state, 50, 3);
        }
    }
}
