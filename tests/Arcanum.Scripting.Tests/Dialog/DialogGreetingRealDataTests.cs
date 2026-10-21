using System.Text;
using Arcanum.Data;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Data.Reputations;
using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Dialog.DialogConversationTests;

namespace Arcanum.Scripting.Tests.Dialog;

/// <summary>
/// Opt-in census of ARC-WORLD-058 (<c>DAT_ROOT</c>, the game folder): the reputation greeting entries, the reaction lines of every
/// class and race block, and every installed <c>g:</c> node through the conversation with the installed texts. Only counts are
/// compared and printed, never original text.
/// </summary>
[TestClass]
public sealed class DialogGreetingRealDataTests
{
    [TestMethod, TestCategory("RealData"), TestCategory("DlgRealData")]
    public void The_installed_greetings_resolve()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in greeting check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var files = GeneratedDialogFiles.Load(stack).Files!;
        var table = ReputationTable.Load(stack).Table!;

        // The 70 reputation greeting entries: the same numbers in all four files, each a reputation of the table.
        var numbers = table.Greetings[0]!.Messages.Select(message => message.Number).ToArray();
        Assert.AreEqual(70, numbers.Length);
        foreach (var file in table.Greetings) CollectionAssert.AreEqual(numbers, file!.Messages.Select(message => message.Number).ToArray());
        Assert.IsTrue(numbers.All(number => table.Find(number) is not null), "every entry is a reputation's");
        CollectionAssert.AreEqual(new[] { 27, 28, 28, 27 }, table.Greetings.Select(file => file!.Messages.Count(message => message.Bytes.Length > 0)).ToArray(),
            "the others are empty, so the reaction line follows");
        foreach (var number in numbers)
        {
            var greeter = new DialogGreetingTests.FakeGreeter(DialogGreetingTests.Facts() with { Category = DialogReactionCategory.Love },
                new(files, table.Greetings, null), [1, 0], DialogRuleProfile.Steamwoven, [number]);
            var line = Start(Synthetic, greeter).NpcLine!;
            var expected = table.Greetings[0]!.Find(number)!.Bytes.Length > 0 ? DialogLineOriginKind.Reputation : DialogLineOriginKind.Generated;
            Assert.AreEqual(expected, line.Origin!.Kind, $"reputation {number}");
        }

        // Every class block (social class 0..10) and race block (PC race 0..7 against NPC race 0) the reaction line reads holds an
        // entry for all four gender pairs and the dumb files.
        var lines = 0;
        foreach (var category in Enum.GetValues<DialogReactionCategory>())
            foreach (var met in new[] { false, true })
                foreach (var (npcMale, pcMale) in new[] { (true, true), (true, false), (false, false), (false, true) })
                    for (var variant = -1; variant <= 10; variant++)
                    {
                        var facts = DialogGreetingTests.Facts() with
                        {
                            Category = category, MetBefore = met, NpcMale = npcMale, PcMale = pcMale, NpcDumb = variant < 0,
                            NpcSocialClass = Math.Max(0, variant), PcRace = Math.Clamp(variant, 0, 7),
                        };
                        var line = Start(Synthetic, new DialogGreetingTests.FakeGreeter(facts, new(files, table.Greetings, null), [100], DialogRuleProfile.Steamwoven, [])).NpcLine!;
                        Assert.AreEqual(DialogLineOriginKind.Generated, line.Origin!.Kind, $"{category} {met} {npcMale} {pcMale} {variant}");
                        Assert.AreNotEqual(string.Empty, line.Text);
                        lines++;
                    }
        Assert.AreEqual(7 * 2 * 4 * 12, lines);

        // Every installed g: node, started at that node, in both settings.
        var outcomes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var key in stack.Keys().Where(k => k.EndsWith(".dlg", StringComparison.Ordinal)))
        {
            var document = DlgReader.Read(stack.Read(stack.Resolve(key).Winner!).Bytes!, new DlgReaderOptions { Mode = DlgReadMode.Original });
            foreach (var record in document.Records.Where(record => record.Role == DlgRecordRole.Npc && record.Text.Bytes.Count >= 2
                && record.Text.Bytes[0] is (byte)'g' or (byte)'G' && record.Text.Bytes[1] == (byte)':'))
                foreach (var rules in DialogRuleProfile.All)
                {
                    var greeter = new DialogGreetingTests.FakeGreeter(DialogGreetingTests.Facts(), new(files, table.Greetings, null), [100], rules, []);
                    var conversation = Start(document, record.Id, greeter);
                    var outcome = conversation.Failure is { } failure ? "failure " + failure.Kind
                        : conversation.Frontier is { } frontier ? "frontier " + frontier.Kind
                        : conversation.NpcLine?.Origin is { } origin ? "line " + origin.Kind : "node " + conversation.Phase;
                    outcomes[rules.Name + " " + outcome] = outcomes.GetValueOrDefault(rules.Name + " " + outcome) + 1;
                }
        }
        foreach (var (outcome, count) in outcomes) Console.WriteLine($"{outcome}: {count}");
        Assert.AreEqual(246 * 2, outcomes.Values.Sum());
        // The census ports are minimal: an action's own refusal (a command or an unknown global flag of the synthetic owner) and
        // the static frontiers of the nodes' replies and actions stop some before their greeting; none of them is a greeting's.
        foreach (var rules in DialogRuleProfile.All)
        {
            Assert.AreEqual(57, outcomes.GetValueOrDefault(rules.Name + " line Generated"), "a generated reaction line");
            Assert.AreEqual(1, outcomes.GetValueOrDefault(rules.Name + " node AwaitingSelection"), "slot 9 continues at its node");
        }
        Assert.IsTrue(outcomes.Keys.Where(outcome => outcome.Contains("failure", StringComparison.Ordinal))
            .All(outcome => outcome.EndsWith("CommandRejected", StringComparison.Ordinal) || outcome.EndsWith("GlobalFlagFailed", StringComparison.Ordinal)),
            "no greeting fails");
        Assert.IsFalse(outcomes.Keys.Any(outcome => outcome.Contains("MissingLine", StringComparison.Ordinal) || outcome.Contains("Empty", StringComparison.Ordinal)),
            "every neutral, not-met line exists");
    }

    private static readonly DlgDocument Synthetic = Read("{10}{g:}{}{}{}{}{}{11}{reply}{}{1}{}{0}{}");

    private static DialogConversation Start(DlgDocument document, IDialogGreetingPort port) => Start(document, 10, port);

    private static DialogConversation Start(DlgDocument document, int entry, IDialogGreetingPort port)
    {
        var ports = new Ports();
        return DialogConversation.Start(document,
            new DialogConversationRequest(new DialogNodeId(entry),
                new DialogParticipants(new DialogId(700), new DialogParticipantId(800), new DialogParticipantId(900)),
                new DialogScriptLine(0), new DialogPcFacts(DialogPcGender.Male, 8, false, 0), new DialogSpeakerNames("Ada", "Bram")),
            new DialogConversationPorts(ports, ports, Owner(), new World(), Greeting: port));
    }
}
