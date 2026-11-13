using System.Text;
using Arcanum.Data;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Scripting.Tests.Dialog;

/// <summary>
/// ARC-SCRIPT-1732 opt-in check (<c>DAT_ROOT</c>, the game folder; ledger HLR1): the census of the installed Player replies that start with <c>h:</c> (AC3), the generated files
/// holding every line the healer talk reads, and an installed healer's dialog end to end with the installation's lines. Only counts and shapes are compared, never text.
/// </summary>
[TestClass]
public sealed class DialogHealerRealDataTests
{
    private sealed class Healer : IDialogHealerPort
    {
        public List<string> Commands { get; } = [];
        public DialogHealerFacts Facts(DialogHealerQuery query) => new(5, true, 4, 1, false, true);
        public string? SkillName(int skill) => "Heal";
        public string? SpellName(int spell) => $"Spell{spell}";
        public DialogCommandResult Execute(HealerSkillCommand command) { Commands.Add($"skill:{command.Skill}"); return DialogCommandResult.Applied; }
        public DialogCommandResult Execute(HealerSpellCommand command) { Commands.Add($"spell:{command.Spell}"); return DialogCommandResult.Applied; }
    }

    private sealed class Gold : IDialogGoldPort, IDialogPcGoldPort
    {
        public List<int> Paid { get; } = [];
        public int Total(DialogGoldQuery query) => 10000;
        public int PcGold(DialogGoldQuery query) => 10000;
        public DialogCommandResult Execute(GoldCommand command) { Paid.Add(command.Amount); return DialogCommandResult.Applied; }
    }

    private static ContentStack Open()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in healer check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        return ContentStack.Open(discovery.Plan!).Stack!;
    }

    private static string TextOf(DlgRecord record) => Encoding.Latin1.GetString(record.Text.Bytes.ToArray());

    [TestMethod, TestCategory("RealData"), TestCategory("DlgRealData")]
    public void Every_installed_h_reply_is_exactly_the_marker_and_the_files_hold_the_talks_lines()
    {
        using var stack = Open();
        int replies = 0, files = 0, exact = 0, noAction = 0;
        foreach (var key in stack.Keys().Where(k => k.EndsWith(".dlg", StringComparison.Ordinal)))
        {
            var document = DlgReader.Read(stack.Read(stack.Resolve(key).Winner!).Bytes!, new DlgReaderOptions { Mode = DlgReadMode.Original });
            var any = false;
            foreach (var record in document.Records.Where(r => r.Role == DlgRecordRole.Player))
            {
                var text = TextOf(record);
                if (text.Length < 2 || char.ToLowerInvariant(text[0]) != 'h' || text[1] != ':') continue;
                replies++;
                any = true;
                if (text.Length == 2) exact++;
                if (record.Action.Bytes.All(b => b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')) noAction++;
            }
            if (any) files++;
        }
        Console.WriteLine($"HEALER|replies {replies}|files {files}|exact {exact}|without action {noAction}");
        Assert.AreEqual(replies, exact, "every installed h: reply is exactly the marker (a longer text is the reply's own text, 0x416C10)");
        Assert.AreEqual(replies, noAction, "no installed h: reply has an action: none can hold a dialog transfer or a window, which the reply leaves out of its subset");
        Assert.AreEqual((52, 15), (replies, files));

        var generated = GeneratedDialogFiles.Load(stack).Files!;
        foreach (var file in Enum.GetValues<GeneratedDialogFile>().Where(f => GeneratedDialogFiles.Names[(int)f].StartsWith("gd_cls_m", StringComparison.Ordinal)
                     || GeneratedDialogFiles.Names[(int)f].StartsWith("gd_cls_f", StringComparison.Ordinal)))
        {
            // The NPC class files: the menu line 14000 and the done line 15000 of the NPC's social class 0 (a class block of 50 each) and the price line 1000, 2000.
            foreach (var group in new[] { 1000, 2000, 14000, 15000 })
                Assert.IsTrue(generated.RunLength(file, group, group + 49) > 0, $"{file} {group}");
        }
        foreach (var file in new[] { GeneratedDialogFile.PcToMale, GeneratedDialogFile.PcToFemale })
            foreach (var (first, last) in new[] { (1, 99), (100, 199), (700, 799), (800, 899), (900, 999), (1000, 1099), (1100, 1199), (1200, 1299) })
                Assert.IsTrue(generated.RunLength(file, first, last) > 0, $"{file} {first}");
    }

    /// <summary>
    /// The admission census (AC3): every NPC node of every installed DLG that lists an <c>h:</c> reply is started with the healer port and the permissive ports of the other
    /// tests; the replies it offers are counted as admitted (the healer transition) or blocked (an unsupported interaction), by file and reply record. A reply no node offers
    /// at its start (its gate needs state these ports do not give) is counted as not offered here.
    /// </summary>
    [TestMethod, TestCategory("RealData"), TestCategory("DlgRealData")]
    public void Every_installed_h_reply_a_node_offers_is_admitted()
    {
        using var stack = Open();
        var files = GeneratedDialogFiles.Load(stack).Files!;
        var greeter = new DialogGreetingTests.FakeGreeter(new DialogGreetingFacts(true, true, false, 0, 0, 0, DialogReactionCategory.Neutral, true),
            new DialogGreetingSources(files, new MesFile?[4], null), [], DialogRuleProfile.Original, []);
        var ports = new DialogConversationTests.Ports();
        var gold = new Gold();
        int replies = 0, admitted = 0, blocked = 0, notOffered = 0;
        foreach (var key in stack.Keys().Where(k => k.EndsWith(".dlg", StringComparison.Ordinal)))
        {
            var document = DlgReader.Read(stack.Read(stack.Resolve(key).Winner!).Bytes!, new DlgReaderOptions { Mode = DlgReadMode.Original });
            var records = document.Records.Where(r => r.Role == DlgRecordRole.Player && TextOf(r).StartsWith("h:", StringComparison.OrdinalIgnoreCase)).Select(r => r.Id).ToHashSet();
            if (records.Count == 0) continue;
            var seen = new Dictionary<int, bool>();
            foreach (var npc in document.Records.Where(r => r.Role == DlgRecordRole.Npc))
            {
                var attempt = DialogConversation.Start(document,
                    new DialogConversationRequest(new DialogNodeId(npc.Id), new DialogParticipants(new DialogId(1), new DialogParticipantId(800), new DialogParticipantId(900)),
                        new DialogScriptLine(0), new DialogPcFacts(DialogPcGender.Male, 8, false, 0), new DialogSpeakerNames("Ada", "Bram")),
                    new DialogConversationPorts(ports, ports, DialogConversationTests.Owner(), new DialogConversationTests.World(), Gold: gold, Greeting: greeter, Purse: gold, Healer: new Healer()));
                if (attempt.Phase != DialogConversationPhase.AwaitingSelection) continue;
                foreach (var option in attempt.Options.Where(o => o.Marker is { Letter: 'h' } && records.Contains(o.ResponseId.Value)))
                    if (option.Block is { } block) Assert.AreEqual(DialogOptionBlockKind.TargetUnsupported, block.Kind, $"{key} {option.ResponseId.Value}: only the reply's way-out node may be outside the subset");
                foreach (var option in attempt.Options.Where(o => o.Marker is { Letter: 'h' } && records.Contains(o.ResponseId.Value)))
                    seen[option.ResponseId.Value] = seen.GetValueOrDefault(option.ResponseId.Value) | option.Block is null && option.Transition == DialogOptionTransition.Healer;
            }
            replies += records.Count;
            admitted += seen.Count(pair => pair.Value);
            blocked += seen.Count(pair => !pair.Value);
            notOffered += records.Count - seen.Count;
        }
        Console.WriteLine($"HEALER-ADMISSION|replies {replies}|admitted {admitted}|blocked {blocked}|not offered at a node's start {notOffered}");
        // The exceptions (AC3): 11 replies of two dialogs (the physician's 01280docroberts: 2, 02816gaylin: 9) lead out of the talk to a node that holds an operation this test's ports do not
        // admit (the way out, not the h: reply itself); the other 41 are admitted (9) or gated at every node's start by state these ports do not give (32). Counts only.
        Assert.AreEqual((52, 9, 11, 32), (replies, admitted, blocked, notOffered));
    }

    /// <summary>
    /// An installed healer's dialog end to end (the first installed DLG whose node offers an <c>h:</c> reply), with the installation's generated-dialog files: the reply opens the menu
    /// (NPC line 14000), a spell of the first submenu is quoted with the number in the line's %d filled, the payment and the spell run, line 15000 follows. Counts and shapes only.
    /// </summary>
    [TestMethod, TestCategory("RealData"), TestCategory("DlgRealData")]
    public void An_installed_healer_talk_runs_to_the_spell_with_the_installed_lines()
    {
        using var stack = Open();
        var files = GeneratedDialogFiles.Load(stack).Files!;
        var greeter = new DialogGreetingTests.FakeGreeter(new DialogGreetingFacts(true, true, false, 0, 0, 0, DialogReactionCategory.Neutral, true),
            new DialogGreetingSources(files, new MesFile?[4], null), [], DialogRuleProfile.Original, []);
        var ports = new DialogConversationTests.Ports();
        var healer = new Healer();
        var gold = new Gold();
        DialogConversation? conversation = null;
        foreach (var key in stack.Keys().Where(k => k.EndsWith(".dlg", StringComparison.Ordinal)))
        {
            var document = DlgReader.Read(stack.Read(stack.Resolve(key).Winner!).Bytes!, new DlgReaderOptions { Mode = DlgReadMode.Original });
            if (!document.Records.Any(r => r.Role == DlgRecordRole.Player && TextOf(r).StartsWith("h:", StringComparison.OrdinalIgnoreCase))) continue;
            foreach (var npc in document.Records.Where(r => r.Role == DlgRecordRole.Npc))
            {
                var attempt = DialogConversation.Start(document,
                    new DialogConversationRequest(new DialogNodeId(npc.Id), new DialogParticipants(new DialogId(1), new DialogParticipantId(800), new DialogParticipantId(900)),
                        new DialogScriptLine(0), new DialogPcFacts(DialogPcGender.Male, 8, false, 0), new DialogSpeakerNames("Ada", "Bram")),
                    new DialogConversationPorts(ports, ports, DialogConversationTests.Owner(), new DialogConversationTests.World(), Gold: gold, Greeting: greeter, Purse: gold, Healer: healer));
                if (attempt.Phase == DialogConversationPhase.AwaitingSelection && attempt.Options.Any(o => o.Transition == DialogOptionTransition.Healer))
                {
                    conversation = attempt;
                    break;
                }
            }
            if (conversation is not null) break;
        }
        Assert.IsNotNull(conversation, "an installed NPC node offers a healer reply");
        Assert.AreEqual(DialogConversationSelectStatus.Continued, conversation.Select(conversation.Options.First(o => o.Transition == DialogOptionTransition.Healer).Token).Status);
        var menu = conversation.Options;
        Assert.AreEqual(5, menu.Count, "the service, the two submenus, the spell 64 and the way out");
        Assert.IsTrue(menu.Take(4).All(o => o.Transition == DialogOptionTransition.HealerStep && o.Text.Length > 0));
        Assert.IsTrue(conversation.NpcLine!.Text.Length > 0 && conversation.NpcLine.Origin is { Kind: DialogLineOriginKind.Generated or DialogLineOriginKind.Override, Number: >= 14000 and <= 14049 or 10013 });
        // The first submenu: the spells 60 and 62, each option the PC's line 1000..1099 with the spell's name filled.
        conversation.Select(menu[1].Token);
        Assert.AreEqual(3, conversation.Options.Count);
        Assert.IsTrue(conversation.Options[0].Text.Contains("Spell60", StringComparison.Ordinal), "the spell's name fills the line's %s");
        conversation.Select(conversation.Options[0].Token);
        Assert.IsTrue(conversation.NpcLine!.Text.Length > 0 && !conversation.NpcLine.Text.Contains('%') && conversation.NpcLine.Text.Any(char.IsAsciiDigit), "the quote holds its price");
        Assert.AreEqual(2, conversation.Options.Count);
        conversation.Select(conversation.Options[0].Token);
        Assert.AreEqual(1, gold.Paid.Count);
        Assert.IsTrue(gold.Paid[0] is <= -80 and >= -200, "the base 100 on the reaction scale");
        CollectionAssert.AreEqual(new[] { "spell:60" }, healer.Commands);
        Assert.AreEqual(1, conversation.Options.Count);
        Assert.AreEqual(DialogConversationPhase.AwaitingSelection, conversation.Phase);
        Console.WriteLine($"HEALER-TALK|menu options {menu.Count}|price {-gold.Paid[0]}");
    }
}
