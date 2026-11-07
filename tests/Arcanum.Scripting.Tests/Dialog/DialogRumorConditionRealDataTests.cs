using System.Text;
using System.Text.RegularExpressions;
using Arcanum.Data;
using Arcanum.Data.Content;
using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Dialog.DialogConversationTests;

namespace Arcanum.Scripting.Tests.Dialog;

/// <summary>
/// Opt-in check of ARC-DEV-171 (<c>DAT_ROOT</c>, the game folder; bug B17): the installed plane-wreck dialog (DLG 1696, read in original
/// mode) reaches its second line for a clever and for a dull hero once the RU condition is admitted, and the RU and RQ conditions of
/// every installed dialog parse whole. Only record numbers and counts are compared, never original text.
/// </summary>
[TestClass]
public sealed partial class DialogRumorConditionRealDataTests
{
    [GeneratedRegex("(?<![A-Za-z])(ru|rq)", RegexOptions.IgnoreCase)]
    private static partial Regex RumorCode();

    [TestMethod, TestCategory("RealData"), TestCategory("DlgRealData")]
    public void The_installed_wreck_dialog_offers_each_hero_a_reply_and_reaches_its_second_line()
    {
        using var stack = Open();
        var key = stack.Keys().Single(k => k.StartsWith("dlg/01696", StringComparison.Ordinal) && k.EndsWith(".dlg", StringComparison.Ordinal));
        var document = DlgReader.Read(stack.Read(stack.Resolve(key).Winner!).Bytes!, new DlgReaderOptions { Mode = DlgReadMode.Original });

        // The records of the installed file: node 1 (the remark), replies 2 (iq 5) and 3 (iq -4) to node 5, whose replies 6..9 hold RU 2015 / RU -2015.
        foreach (var (intelligence, dumb, firstReply, knows, secondReply) in new[]
        {
            (8, false, 2, true, 6), (8, false, 2, false, 7), (3, false, 3, true, 8), (3, false, 3, false, 9), (9, true, 3, false, 9),
        })
        {
            var rumors = new Rumors();
            if (knows) rumors.Known.Add(2015);
            var conversation = DialogConversation.Start(document,
                new DialogConversationRequest(new DialogNodeId(1),
                    new DialogParticipants(new DialogId(1696), new DialogParticipantId(800), new DialogParticipantId(900)),
                    new DialogScriptLine(0), new DialogPcFacts(DialogPcGender.Male, intelligence, dumb, 0), new DialogSpeakerNames("Pc", "Virgil")),
                new DialogConversationPorts(new Ports(), new Ports(), Owner(), Rumors: rumors));
            var label = $"int {intelligence} dumb {dumb} knows {knows}";
            Assert.AreEqual(DialogConversationPhase.AwaitingSelection, conversation.Phase, label);
            Assert.AreEqual(firstReply, document.Records[conversation.Options.Single().SerializedIndex].Id, label);
            Assert.IsNull(conversation.Options.Single().Block, label);
            Assert.AreEqual(DialogConversationSelectStatus.Continued, conversation.Select(conversation.Options[0].Token).Status, label);
            Assert.AreEqual(5, conversation.CurrentNode.Value, label);
            Assert.AreEqual(secondReply, document.Records[conversation.Options.Single().SerializedIndex].Id, label);
            Assert.IsNull(conversation.Options.Single().Block, label);
        }
    }

    [TestMethod, TestCategory("RealData"), TestCategory("DlgRealData")]
    public void Every_installed_ru_and_rq_condition_parses_under_the_rumor_grammar()
    {
        using var stack = Open();
        var options = new DialogConversationOptions();
        int fields = 0, whole = 0, stopped = 0;
        var atRumor = new SortedDictionary<(string File, DialogFailureKind Kind), int>();
        var files = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in stack.Keys().Where(k => k.EndsWith(".dlg", StringComparison.Ordinal)))
        {
            var document = DlgReader.Read(stack.Read(stack.Resolve(key).Winner!).Bytes!, new DlgReaderOptions { Mode = DlgReadMode.Original });
            foreach (var record in document.Records.Where(r => r.Role == DlgRecordRole.Player))
            {
                var text = Encoding.Latin1.GetString([.. record.Condition.Bytes]);
                if (!RumorCode().IsMatch(text)) continue;
                fields++;
                files.Add(key);
                try
                {
                    DialogConversationGrammar.Parse(record, record.Condition, ConversationField.PlayerCondition, options, party: true, skills: true,
                        fate: true, reputations: true, schematics: true, stores: true);
                    whole++;
                }
                catch (DialogOperationParseException exception)
                {
                    stopped++;
                    var start = exception.OperationSource.OperationOffsetInField;
                    if (start + 2 <= text.Length && RumorCode().IsMatch(text.Substring(start, 2))) atRumor[(key[..9], exception.Kind)] = atRumor.GetValueOrDefault((key[..9], exception.Kind)) + 1;
                }
            }
        }
        Console.WriteLine($"files {files.Count}; Player condition fields with ru or rq {fields}; whole {whole}; stopped {stopped}; at a rumor operation {atRumor.Values.Sum()}");
        // First observation 2026-11-07 (GOG 1.0.7.4): 72 of the 223 fields stop, only 20 of them at a rumor operation: the 19 conditions of
        // dialog 1934 that read rumor 4180 (past the session's rumors 1000..2999, valid data outside the subset) and one "rq - 1143" of
        // dialog 1906 (a space after the sign: the source's atoi reads 0, damaged data). The rest stop at another code (ch, in, ia, ...).
        CollectionAssert.AreEqual(new[] { "dlg/01906 InvalidOperation 1", "dlg/01934 UnsupportedOperation 19" },
            atRumor.Select(p => $"{p.Key.File} {p.Key.Kind} {p.Value}").ToArray());
        Assert.AreEqual((21, 223, 151, 72), (files.Count, fields, whole, stopped));
    }

    private static ContentStack Open()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in DLG check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        return ContentStack.Open(discovery.Plan!).Stack!;
    }

    private sealed class Rumors : IDialogRumorPort
    {
        public HashSet<int> Known { get; } = [];
        public DialogCommandResult Execute(QuellRumorCommand command) => DialogCommandResult.Applied;
        public bool IsKnown(DialogRumorQuery query) => Known.Contains(query.Rumor);
        public bool IsQuelled(int rumor) => false;
    }
}
