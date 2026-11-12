using System.Text;
using Arcanum.Data;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Scripting.Tests.Dialog;

/// <summary>
/// ARC-SCRIPT-1304 opt-in check (<c>DAT_ROOT</c>, the game folder): every installed Player reply that starts with <c>r:</c> reads as a rumor reply (a price
/// mark, a list of rumors inside 1000..2999), the rumor files hold the text of the rumors the replies name, and an installed rumor talk runs end to end.
/// Only counts and shapes are compared, never text.
/// </summary>
[TestClass]
public sealed class DialogRumorRealDataTests
{
    [TestMethod, TestCategory("RealData"), TestCategory("DlgRealData")]
    public void Every_installed_rumor_reply_is_admitted_and_the_files_hold_the_talks_lines()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in rumor check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;

        int replies = 0, files = 0, paid = 0, listed = 0;
        var rumors = new SortedSet<int>();
        foreach (var key in stack.Keys().Where(k => k.EndsWith(".dlg", StringComparison.Ordinal)))
        {
            var document = DlgReader.Read(stack.Read(stack.Resolve(key).Winner!).Bytes!, new DlgReaderOptions { Mode = DlgReadMode.Original });
            var any = false;
            foreach (var record in document.Records.Where(r => r.Role == DlgRecordRole.Player))
            {
                var text = Encoding.Latin1.GetString(record.Text.Bytes.ToArray());
                if (text.Length < 2 || char.ToLowerInvariant(text[0]) != 'r' || text[1] != ':') continue;
                var parsed = DialogConversation.ParseRumorReply(text);
                Assert.IsNotNull(parsed, $"{key} {record.Id}: a rumor reply outside the subset");
                replies++;
                any = true;
                if (parsed.Value.Price >= 1) paid++;
                listed += parsed.Value.Rumors.Length;
                foreach (var rumor in parsed.Value.Rumors) rumors.Add(rumor);
            }
            if (any) files++;
        }
        Console.WriteLine($"RUMOR|replies {replies}|files {files}|paid {paid}|rumors listed {listed}|distinct {rumors.Count}|{rumors.Min}..{rumors.Max}");
        Assert.AreEqual((239, 32), (replies, files));
        Assert.AreEqual((6, 149), (paid, rumors.Count), "233 replies name the price 0, six the price 10; 149 distinct rumors are listed");

        var texts = RumorSpeechTexts.Load(stack);
        Assert.AreEqual(0, texts.Failures.Count);
        var present = texts.Files.Count(file => file is not null);
        // A rumor has a class-less text in the file of its speakers' genders (the installed male-to-male file holds one for each listed rumor).
        var withText = rumors.Count(rumor => texts.Files[0]?.Find(rumor * RumorSpeechTexts.MessageStride + 1) is not null);
        Console.WriteLine($"RUMOR-TEXTS|files {present}|listed rumors with a class-less male-to-male text {withText} of {rumors.Count}");
        Assert.AreEqual(4, present);
        Assert.AreEqual(121, withText, "the rumors the male-to-male file holds a class-less text for");

        var generated = GeneratedDialogFiles.Load(stack).Files!;
        foreach (var file in Enum.GetValues<GeneratedDialogFile>().Where(f => GeneratedDialogFiles.Names[(int)f].StartsWith("gd_cls_m", StringComparison.Ordinal)
                     || GeneratedDialogFiles.Names[(int)f].StartsWith("gd_cls_f", StringComparison.Ordinal)))
        {
            foreach (var group in new[] { 1000, 2000, 7000 })
                Assert.IsTrue(generated.RunLength(file, group, group + 49) > 0, $"{file} {group}");
        }
        foreach (var file in new[] { GeneratedDialogFile.PcToMale, GeneratedDialogFile.PcToFemale })
            foreach (var (first, last) in new[] { (1, 99), (100, 199), (600, 699), (2000, 2049) })
                Assert.IsTrue(generated.RunLength(file, first, last) > 0 || file == GeneratedDialogFile.PcToFemale && first == 2000, $"{file} {first}");
    }

    private sealed class Rumors : IDialogRumorPort
    {
        public DialogCommandResult Execute(QuellRumorCommand command) => DialogCommandResult.Applied;
        public bool IsKnown(DialogRumorQuery query) => false;
        public bool IsQuelled(int rumor) => false;
    }

    private sealed class Purse : IDialogPcGoldPort
    {
        public int PcGold(DialogGoldQuery query) => 1000;
    }

    private sealed class Gold : IDialogGoldPort
    {
        public List<int> Paid { get; } = [];
        public int Total(DialogGoldQuery query) => 1000;
        public DialogCommandResult Execute(GoldCommand command) { Paid.Add(command.Amount); return DialogCommandResult.Applied; }
    }

    /// <summary>
    /// An installed rumor dialog end to end (the first installed DLG whose NPC node offers an <c>r:</c> reply), with the installation's generated-dialog and
    /// rumor files: picking it tells one of the listed rumors in the NPC's words, writes it to the logbook and offers the PC's class line; a paid reply asks
    /// its price first. Counts and shapes only; no original text is asserted or printed.
    /// </summary>
    [TestMethod, TestCategory("RealData"), TestCategory("DlgRealData")]
    public void An_installed_rumor_talk_runs_to_the_logbook_with_the_installed_lines()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in rumor check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var files = GeneratedDialogFiles.Load(stack).Files!;
        var rumorTexts = RumorSpeechTexts.Load(stack);
        var greeter = new DialogGreetingTests.FakeGreeter(new DialogGreetingFacts(true, true, false, 0, 0, 0, DialogReactionCategory.Neutral, true),
            new DialogGreetingSources(files, new MesFile?[4], null, rumorTexts.Files), [], DialogRuleProfile.Original, []);

        int talks = 0, told = 0, paidTalks = 0, offeredAll = 0;
        foreach (var key in stack.Keys().Where(k => k.EndsWith(".dlg", StringComparison.Ordinal)))
        {
            var document = DlgReader.Read(stack.Read(stack.Resolve(key).Winner!).Bytes!, new DlgReaderOptions { Mode = DlgReadMode.Original });
            if (!document.Records.Any(r => r.Role == DlgRecordRole.Player && Encoding.Latin1.GetString(r.Text.Bytes.ToArray()).StartsWith("r:", StringComparison.OrdinalIgnoreCase))) continue;
            foreach (var npc in document.Records.Where(r => r.Role == DlgRecordRole.Npc))
            {
                var ports = new DialogConversationTests.Ports();
                var gold = new Gold();
                var attempt = DialogConversation.Start(document,
                    new DialogConversationRequest(new DialogNodeId(npc.Id), new DialogParticipants(new DialogId(1), new DialogParticipantId(800), new DialogParticipantId(900)),
                        new DialogScriptLine(0), new DialogPcFacts(DialogPcGender.Male, 8, false, 0), new DialogSpeakerNames("Ada", "Bram")),
                    new DialogConversationPorts(ports, ports, DialogConversationTests.Owner(), new DialogConversationTests.World(), Rumors: new Rumors(), Gold: gold,
                        Greeting: greeter, Purse: new Purse()));
                if (attempt.Phase != DialogConversationPhase.AwaitingSelection) continue;
                // No installed r: reply is refused for its own shape (a transfer or window action in the same reply, a number outside the subset).
                Assert.IsFalse(attempt.Options.Any(o => o.Marker is { Letter: 'r' } && o.Block is { Kind: DialogOptionBlockKind.UnsupportedInteraction }), $"{key} {npc.Id}");
                var option = attempt.Options.FirstOrDefault(o => o.Transition == DialogOptionTransition.Rumor && o.Block is null);
                if (option is null) continue;
                offeredAll++;
                if (talks >= 40) continue;
                talks++;
                Assert.AreEqual(DialogConversationSelectStatus.Continued, attempt.Select(option.Token).Status, $"{key} {npc.Id}");
                if (attempt.Options.Any(o => o.Transition == DialogOptionTransition.RumorStep))
                {
                    paidTalks++;
                    Assert.IsTrue(attempt.NpcLine!.Text.Length > 0 && !attempt.NpcLine.Text.Contains('%') && attempt.NpcLine.Text.Any(char.IsAsciiDigit), "the quote holds its price");
                    Assert.AreEqual(DialogConversationSelectStatus.Continued, attempt.Select(attempt.Options[0].Token).Status);
                    Assert.AreEqual(1, gold.Paid.Count);
                }
                if (attempt.Options.Count == 1 && attempt.NpcLine!.Origin is { Kind: DialogLineOriginKind.Rumor })
                {
                    told++;
                    Assert.IsTrue(attempt.NpcLine.Text.Length > 0);
                    Assert.AreEqual(1, ports.Commands.Count(c => c.StartsWith("RU:", StringComparison.Ordinal)));
                }
            }
        }
        Console.WriteLine($"RUMOR-TALK|offered {offeredAll}|talks {talks}|told {told}|paid {paidTalks}");
        Assert.IsTrue(talks > 0 && told > 0, "an installed NPC node offers a rumor reply and tells a rumor");
    }
}
