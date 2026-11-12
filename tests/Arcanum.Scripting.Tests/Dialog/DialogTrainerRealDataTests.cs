using System.Text;
using Arcanum.Data;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Scripting.Tests.Dialog;

/// <summary>
/// ARC-SCRIPT-1150 opt-in check (<c>DAT_ROOT</c>, the game folder): every installed Player reply that starts with <c>t:</c> reads as a trainer reply
/// (its numbers are skills and its menu fits), and the generated files hold every line the trainer talk reads. Only counts are compared, never text.
/// </summary>
[TestClass]
public sealed class DialogTrainerRealDataTests
{
    [TestMethod, TestCategory("RealData"), TestCategory("DlgRealData")]
    public void Every_installed_trainer_reply_is_admitted_and_the_files_hold_the_talks_lines()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in trainer check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;

        int replies = 0, files = 0, skills = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in stack.Keys().Where(k => k.EndsWith(".dlg", StringComparison.Ordinal)))
        {
            var document = DlgReader.Read(stack.Read(stack.Resolve(key).Winner!).Bytes!, new DlgReaderOptions { Mode = DlgReadMode.Original });
            var any = false;
            foreach (var record in document.Records.Where(r => r.Role == DlgRecordRole.Player))
            {
                var text = Encoding.Latin1.GetString(record.Text.Bytes.ToArray());
                if (text.Length < 2 || char.ToLowerInvariant(text[0]) != 't' || text[1] != ':') continue;
                var list = DialogConversation.ParseTrainerSkills(text);
                Assert.IsNotNull(list, $"{key} {record.Id}: a trainer reply outside the subset");
                replies++;
                skills += list.Length;
                any = true;
                seen.Add(string.Join(",", list));
            }
            if (any) files++;
        }
        Console.WriteLine($"TRAINER|replies {replies}|files {files}|skills listed {skills}|distinct lists {seen.Count}");
        Assert.AreEqual((315, 58), (replies, files));

        var generated = GeneratedDialogFiles.Load(stack).Files!;
        foreach (var file in Enum.GetValues<GeneratedDialogFile>().Where(f => GeneratedDialogFiles.Names[(int)f].StartsWith("gd_cls_m", StringComparison.Ordinal)
                     || GeneratedDialogFiles.Names[(int)f].StartsWith("gd_cls_f", StringComparison.Ordinal)))
        {
            // The NPC class files: groups 1000 to 6000 for every social class block read by the talk (class 0 at least).
            foreach (var group in new[] { 1000, 2000, 3000, 4000, 5000, 6000 })
                Assert.IsTrue(generated.RunLength(file, group, group + 49) > 0, $"{file} {group}");
            var quote = generated.Line(file, 1000).Message!;
            Assert.IsTrue(Encoding.Latin1.GetString(quote.Bytes.ToArray()).Contains("%d", StringComparison.Ordinal), $"{file}: the quote's line holds the price");
        }
        foreach (var file in new[] { GeneratedDialogFile.PcToMale, GeneratedDialogFile.PcToFemale })
            foreach (var (first, last) in new[] { (1, 99), (100, 199), (500, 599), (600, 699), (800, 899) })
                Assert.IsTrue(generated.RunLength(file, first, last) > 0, $"{file} {first}");
    }

    private sealed class RealTrainer(SkillTexts texts) : IDialogTrainerPort
    {
        public string? SkillName(int skill) => skill is < 0 or > 15 ? null
            : Encoding.Latin1.GetString((skill < 12 ? texts.BasicName(skill) : texts.TechName(skill - 12))!.Bytes.ToArray());
        public int PcGold(DialogGoldQuery query) => 1000;
    }

    private sealed class Skills : IDialogSkillPort
    {
        public List<string> Commands { get; } = [];
        public int Level(DialogSkillQuery query) => 5;
        public int Training(DialogSkillQuery query) => 0;
        public DialogCommandResult Execute(SkillTrainingCommand command) { Commands.Add($"{command.Skill}:{command.Training}"); return DialogCommandResult.Applied; }
    }

    private sealed class Gold : IDialogGoldPort
    {
        public List<int> Paid { get; } = [];
        public int Total(DialogGoldQuery query) => 1000;
        public DialogCommandResult Execute(GoldCommand command) { Paid.Add(command.Amount); return DialogCommandResult.Applied; }
    }

    /// <summary>
    /// An installed trainer's dialog end to end (the first installed DLG with a `t:` reply), with the installation's generated-dialog files and skill
    /// names: the menu lists its skills and the way out, a skill is quoted with the number in the line's %d filled, the payment and the training run.
    /// Counts and shapes only; no original text is asserted or printed.
    /// </summary>
    [TestMethod, TestCategory("RealData"), TestCategory("DlgRealData")]
    public void An_installed_trainer_talk_runs_to_the_training_with_the_installed_lines()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in trainer check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var files = GeneratedDialogFiles.Load(stack).Files!;
        var skillTexts = SkillTexts.Load(stack).Texts!;
        var greeter = new DialogGreetingTests.FakeGreeter(new DialogGreetingFacts(true, true, false, 0, 0, 0, DialogReactionCategory.Neutral, true),
            new DialogGreetingSources(files, new MesFile?[4], null), [], DialogRuleProfile.Original, []);

        var document = DlgReader.Read(stack.Read(stack.Resolve("dlg/01003shopkeeper.dlg").Winner!).Bytes!, new DlgReaderOptions { Mode = DlgReadMode.Original });
        var ports = new DialogConversationTests.Ports();
        var skills = new Skills();
        var gold = new Gold();
        DialogConversation? conversation = null;
        foreach (var npc in document.Records.Where(r => r.Role == DlgRecordRole.Npc))
        {
            var attempt = DialogConversation.Start(document,
                new DialogConversationRequest(new DialogNodeId(npc.Id), new DialogParticipants(new DialogId(1), new DialogParticipantId(800), new DialogParticipantId(900)),
                    new DialogScriptLine(0), new DialogPcFacts(DialogPcGender.Male, 8, false, 0), new DialogSpeakerNames("Ada", "Bram")),
                new DialogConversationPorts(ports, ports, DialogConversationTests.Owner(), new DialogConversationTests.World(), Skills: skills, Gold: gold,
                    Greeting: greeter, Trainer: new RealTrainer(skillTexts)));
            if (attempt.Phase == DialogConversationPhase.AwaitingSelection && attempt.Options.Any(o => o.Transition == DialogOptionTransition.Trainer))
            {
                conversation = attempt;
                break;
            }
        }
        Assert.IsNotNull(conversation, "an installed NPC node offers a trainer reply");
        Assert.AreEqual(DialogConversationSelectStatus.Continued, conversation.Select(conversation.Options.First(o => o.Transition == DialogOptionTransition.Trainer).Token).Status);
        var menu = conversation.Options;
        Assert.IsTrue(menu.Count is >= 2 and <= 5);
        Assert.IsTrue(menu.Take(menu.Count - 1).All(o => o.Transition == DialogOptionTransition.TrainerStep && o.Text.Length > 0), "the skills carry their names");
        Assert.IsTrue(conversation.NpcLine!.Text.Length > 0 && conversation.NpcLine.Origin is { Kind: DialogLineOriginKind.Generated, Number: >= 3000 and <= 3049 });
        conversation.Select(menu[0].Token);
        Assert.IsTrue(conversation.NpcLine!.Text.Length > 0 && !conversation.NpcLine.Text.Contains('%') && conversation.NpcLine.Text.Any(char.IsAsciiDigit), "the quote holds its price");
        Assert.AreEqual(2, conversation.Options.Count);
        conversation.Select(conversation.Options[0].Token);
        Assert.AreEqual(1, gold.Paid.Count);
        Assert.IsTrue(gold.Paid[0] is <= -80 and >= -200, "the base 100 on the reaction scale");
        Assert.AreEqual(1, skills.Commands.Count);
        Assert.AreEqual(1, conversation.Options.Count);
        Assert.AreEqual(DialogConversationPhase.AwaitingSelection, conversation.Phase);
        Console.WriteLine($"TRAINER-TALK|menu options {menu.Count}|price {-gold.Paid[0]}|first command {skills.Commands[0]}");
    }
}
