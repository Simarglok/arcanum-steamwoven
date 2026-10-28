using System.Text;
using Arcanum.Data;
using Arcanum.Data.Messages;
using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Dialog.DialogConversationTests;

namespace Arcanum.Scripting.Tests.Dialog;

/// <summary>ARC-SCRIPT-043: the phrases of generated Player options as read in the executable (ledger "Generated Player
/// phrases"), over invented generated-dialog files whose every entry's text names its own number and file.</summary>
[TestClass]
public sealed class DialogPhraseTests
{
    [TestMethod]
    public void Every_exact_marker_picks_its_range_from_the_pc_file_of_the_npc_gender()
    {
        var cases = new (string Marker, int First, int Last)[]
        {
            ("e:", 400, 499), ("f:", 800, 899), ("h:", 700, 799), ("k:", 1500, 1599), ("l:", 2300, 2399), ("n:", 100, 199),
            ("p:", 1900, 1999), ("s:", 200, 299), ("w:", 1800, 1899), ("y:", 1, 99), ("b:", 300, 399), ("E:", 400, 499), ("K:", 1500, 1599),
        };
        foreach (var (marker, first, last) in cases)
        {
            var greeter = Greeter(Facts());
            var option = Start(Reply(marker), greeter).Options.Single();
            Assert.AreEqual((DialogOptionTextKind.Generated, $"{first} pc2m"), (option.TextKind, option.Text), marker);
            Assert.AreEqual(new DialogLineOrigin(DialogLineOriginKind.Generated, first, GeneratedDialogFile.PcToMale), option.TextOrigin, marker);
            Assert.AreEqual((DialogGreetingRoll.Phrase, 0, 0), greeter.Asked.Single(), $"{marker}: one stored entry in {first}..{last}");
        }
        var female = Start(Reply("e:"), Greeter(Facts() with { NpcMale = false, PcMale = false })).Options.Single();
        Assert.AreEqual(new DialogLineOrigin(DialogLineOriginKind.Generated, 400, GeneratedDialogFile.PcToFemale), female.TextOrigin,
            "the listener's gender chooses the file, not the PC's");
        var dumb = Start(Reply("e:"), Greeter(Facts()), pc: new DialogPcFacts(DialogPcGender.Male, 4, false, 0)).Options.Single();
        Assert.AreEqual(new DialogLineOrigin(DialogLineOriginKind.Generated, 400, GeneratedDialogFile.DumbPcToMale), dumb.TextOrigin);
        var forced = Start(Reply("e:"), Greeter(Facts()), pc: new DialogPcFacts(DialogPcGender.Male, 9, true, 0)).Options.Single();
        Assert.AreEqual(GeneratedDialogFile.DumbPcToMale, forced.TextOrigin!.File, "a dumb PC counts as intelligence 1");
    }

    [TestMethod]
    public void A_barter_phrase_comes_from_the_follower_block_when_the_pc_leads_the_npc()
    {
        var option = Start(Reply("b:"), Greeter(Facts() with { NpcLedByPc = true })).Options.Single();
        Assert.AreEqual("1600 pc2m", option.Text);
        Assert.IsNotNull(option.Block, "without a barter port the reply stays blocked, but shows its phrase");
    }

    [TestMethod]
    public void Class_phrases_start_at_the_group_plus_fifty_per_social_class_also_for_a_dumb_pc()
    {
        var greeter = Greeter(Facts() with { NpcSocialClass = 3 });
        var ask = Start(Reply("a:"), greeter).Options.Single();
        Assert.AreEqual(new DialogLineOrigin(DialogLineOriginKind.Generated, 1150, GeneratedDialogFile.ClassPcToMale), ask.TextOrigin);
        Assert.AreEqual((DialogGreetingRoll.Phrase, 0, 0), greeter.Asked.Single());
        var insult = Start(Reply("i:"), Greeter(Facts() with { NpcSocialClass = 3, NpcMale = false })).Options.Single();
        Assert.AreEqual(new DialogLineOrigin(DialogLineOriginKind.Generated, 3150, GeneratedDialogFile.ClassPcToFemale), insult.TextOrigin);
        Assert.AreEqual("3150 cls_pc2f", insult.Text);
        var dumb = Start(Reply("a:"), Greeter(Facts() with { NpcSocialClass = 3 }), pc: new DialogPcFacts(DialogPcGender.Male, 2, false, 0)).Options.Single();
        Assert.AreEqual(new DialogLineOrigin(DialogLineOriginKind.Generated, 1150, GeneratedDialogFile.ClassDumbPcToMale), dumb.TextOrigin);
    }

    [TestMethod]
    public void The_farewell_offered_when_no_reply_qualifies_is_a_goodbye_phrase()
    {
        var option = Start("{11}{reply}{}{9}{}{0}{}", Greeter(Facts())).Options.Single();
        Assert.AreEqual((DialogOptionTextKind.Fallback, "400 pc2m"), (option.TextKind, option.Text));
    }

    [TestMethod]
    public void Phrases_are_picked_in_offer_order_and_kept_when_the_offers_are_rebuilt()
    {
        var files = Files(GeneratedDialogFile.PcToMale, [400, 401, 402, 800, 801]);
        var greeter = new DialogGreetingTests.FakeGreeter(Facts(), new DialogGreetingSources(files, new MesFile?[4], null), [], DialogRuleProfile.Steamwoven, [], [2, 1]);
        var conversation = Start(Reply("e:") + "{12}{f:}{}{1}{}{0}{}", greeter);
        Assert.AreEqual("402 pc2m|801 pc2m", string.Join("|", conversation.Options.Select(option => option.Text)));
        Assert.AreEqual(2, greeter.Asked.Count);
        conversation.RefreshOptions();
        Assert.AreEqual("402 pc2m|801 pc2m", string.Join("|", conversation.Options.Select(option => option.Text)), "the turn keeps its phrases");
        Assert.AreEqual(2, greeter.Asked.Count, "a rebuild rolls nothing");
    }

    [TestMethod]
    public void The_original_pick_spans_gaps_and_shows_the_missing_line()
    {
        var files = Files(GeneratedDialogFile.PcToMale, [400, 401, 403]);
        var original = new DialogGreetingTests.FakeGreeter(Facts(), new DialogGreetingSources(files, new MesFile?[4], null), [], DialogRuleProfile.Original, [], [2]);
        var option = Start(Reply("e:"), original).Options.Single();
        Assert.AreEqual((DialogGreetingRoll.Phrase, 0, 2), original.Asked.Single(), "the run 400, 401 counts 2 entries");
        Assert.AreEqual(DialogLineOriginKind.MissingLine, option.TextOrigin!.Kind, "400 + 2 names the gap at 402");
        Assert.AreEqual(@"[missing generated line 402 in mes\gd_pc2m.mes]", option.Text);
    }

    [TestMethod]
    public void Names_are_substituted_in_a_phrase()
    {
        var files = GeneratedDialogFiles.Create(Enumerable.Range(0, GeneratedDialogFiles.Count)
            .Select(index => index == (int)GeneratedDialogFile.PcToMale ? Mes("{400}{Farewell, @npcname@, from @pcname@.}") : null).ToArray());
        var greeter = new DialogGreetingTests.FakeGreeter(Facts(), new DialogGreetingSources(files, new MesFile?[4], null), [], DialogRuleProfile.Steamwoven, []);
        Assert.AreEqual("Farewell, Bram, from Ada.", Start(Reply("e:"), greeter).Options.Single().Text);
    }

    [TestMethod]
    public void Without_a_phrase_the_text_stays_empty_and_the_reply_keeps_working()
    {
        var noPort = Start(Reply("e:"), null);
        Assert.AreEqual((DialogOptionTextKind.Generated, string.Empty, null), (noPort.Options.Single().TextKind, noPort.Options.Single().Text, noPort.Options.Single().TextOrigin));
        var unknown = Greeter(null);
        Assert.AreEqual(string.Empty, Start(Reply("e:"), unknown).Options.Single().Text, "facts the port does not know give no phrase");
        Assert.AreEqual(0, unknown.Asked.Count);
        var absent = new DialogGreetingTests.FakeGreeter(Facts(), new DialogGreetingSources(GeneratedDialogFiles.Create(new MesFile?[GeneratedDialogFiles.Count]),
            new MesFile?[4], null), [], DialogRuleProfile.Steamwoven, []);
        var empty = Start(Reply("e:"), absent).Options.Single();
        Assert.AreEqual((string.Empty, DialogLineOriginKind.Empty), (empty.Text, empty.TextOrigin!.Kind), "an absent file gives no line");
        var prefix = Greeter(Facts());
        Assert.AreEqual(string.Empty, Start(Reply("t: 5"), prefix).Options.Single().Text, "a prefix marker keeps its placeholder here");
        Assert.AreEqual(0, prefix.Asked.Count);
        var exit = Start(Reply("e:"), Greeter(Facts()));
        Assert.AreEqual(DialogConversationSelectStatus.Ended, exit.Select(exit.Options.Single().Token).Status);
    }

    [TestMethod]
    public void A_refused_roll_or_a_broken_port_fails_the_conversation()
    {
        var refusing = Greeter(Facts());
        refusing.Refuse = true;
        Assert.AreEqual(DialogConversationFailureKind.InvalidQueryResult, Start(Reply("e:"), refusing).Failure!.Kind);
        var throwing = Greeter(Facts());
        throwing.OnFacts = () => throw new InvalidOperationException();
        Assert.AreEqual(DialogConversationFailureKind.QueryException, Start(Reply("e:"), throwing).Failure!.Kind);
        var badText = new DialogGreetingTests.FakeGreeter(Facts(), new DialogGreetingSources(Files(GeneratedDialogFile.PcToMale, [400], "\u0001"), new MesFile?[4], null),
            [], DialogRuleProfile.Steamwoven, []);
        Assert.AreEqual(DialogConversationFailureKind.InvalidText, Start(Reply("e:"), badText).Failure!.Kind);
    }

    [TestMethod]
    public void The_pc_line_file_follows_class_lines_npc_gender_and_dumbness()
    {
        Assert.AreEqual(GeneratedDialogFile.PcToMale, GeneratedDialogFiles.PcLineFile(false, true, false));
        Assert.AreEqual(GeneratedDialogFile.PcToFemale, GeneratedDialogFiles.PcLineFile(false, false, false));
        Assert.AreEqual(GeneratedDialogFile.DumbPcToFemale, GeneratedDialogFiles.PcLineFile(false, false, true));
        Assert.AreEqual(GeneratedDialogFile.ClassPcToMale, GeneratedDialogFiles.PcLineFile(true, true, false));
        Assert.AreEqual(GeneratedDialogFile.ClassDumbPcToFemale, GeneratedDialogFiles.PcLineFile(true, false, true));
    }

    private static DialogGreetingFacts Facts() => DialogGreetingTests.Facts();

    private static string Reply(string text) => $"{{11}}{{{text}}}{{}}{{1}}{{}}{{0}}{{}}";

    private static DialogConversation Start(string replies, IDialogGreetingPort? port, DialogPcFacts? pc = null)
    {
        var ports = new Ports();
        return DialogConversation.Start(Read("{10}{Hello.}{}{}{}{}{}" + replies),
            new DialogConversationRequest(new DialogNodeId(10),
                new DialogParticipants(new DialogId(700), new DialogParticipantId(800), new DialogParticipantId(900)),
                new DialogScriptLine(0), pc ?? new DialogPcFacts(DialogPcGender.Male, 8, false, 0), new DialogSpeakerNames("Ada", "Bram")),
            new DialogConversationPorts(ports, ports, Owner(), new World(), Greeting: port));
    }

    private static DialogGreetingTests.FakeGreeter Greeter(DialogGreetingFacts? facts) =>
        new(facts, new DialogGreetingSources(NumberedFiles(), new MesFile?[4], null), [], DialogRuleProfile.Steamwoven, []);

    /// <summary>Every file holds the first entry of every phrase range and class block, its text the number and the file.</summary>
    private static GeneratedDialogFiles NumberedFiles()
    {
        var numbers = new SortedSet<int> { 1, 100, 200, 300, 400, 700, 800, 1500, 1600, 1800, 1900, 2300 };
        for (var block = 0; block <= 10; block++)
        {
            numbers.Add(1000 + 50 * block);
            numbers.Add(3000 + 50 * block);
        }
        return GeneratedDialogFiles.Create(Enumerable.Range(0, GeneratedDialogFiles.Count).Select(index =>
        {
            var name = GeneratedDialogFiles.Names[index].Replace("gd_", string.Empty);
            return (MesFile?)Mes(string.Concat(numbers.Select(number => $"{{{number}}}{{{number} {name}}}")));
        }).ToArray());
    }

    private static GeneratedDialogFiles Files(GeneratedDialogFile file, int[] numbers, string? text = null)
    {
        var all = new MesFile?[GeneratedDialogFiles.Count];
        all[(int)file] = Mes(string.Concat(numbers.Select(number => $"{{{number}}}{{{text ?? $"{number} pc2m"}}}")));
        return GeneratedDialogFiles.Create(all);
    }

    private static MesFile Mes(string text) => MesFile.Parse(Encoding.Latin1.GetBytes(text)).File!;
}
