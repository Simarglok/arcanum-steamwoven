using System.Text;
using Arcanum.Data;
using Arcanum.Data.Messages;
using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Scripting.Tests.Dialog.DialogConversationTests;

namespace Arcanum.Scripting.Tests.Dialog;

/// <summary>ARC-WORLD-058: the NPC <c>g:</c> greeting as read in the executable (ledger "Reputation greetings"), over invented
/// generated-dialog files whose every entry's text names its own number.</summary>
[TestClass]
public sealed class DialogGreetingTests
{
    private const string Replies = "{11}{reply}{}{1}{}{0}{}";

    [TestMethod]
    public void The_slot_text_is_read_as_the_original_reads_it()
    {
        var slots = DialogGreeting.ParseSlots(" 1 300, 2 0,4 fl 310, 5 FL 7, 12 5, -3 4, 8, 9 0");
        Assert.AreEqual(new DialogGreetingSlot(DialogGreetingSlotKind.Jump, 300), slots[1]);
        Assert.AreEqual(new DialogGreetingSlot(DialogGreetingSlotKind.Off, 0), slots[2]);
        Assert.AreEqual(new DialogGreetingSlot(DialogGreetingSlotKind.Float, 310), slots[4]);
        Assert.AreEqual(new DialogGreetingSlot(DialogGreetingSlotKind.Off, 0), slots[5], "only a lowercase fl floats; FL reads as no number");
        Assert.AreEqual(new DialogGreetingSlot(DialogGreetingSlotKind.Jump, 4), slots[3], "the index is the next digit run: the sign is skipped");
        Assert.AreEqual(new DialogGreetingSlot(DialogGreetingSlotKind.Jump, 9), slots[8], "an index without a value takes the next entry's index as its value");
        Assert.AreEqual(default, slots[9], "that entry is swallowed");
        Assert.AreEqual(default, slots[6], "an index of 10 or more is ignored (no-crash rule)");
        Assert.AreEqual(default, slots[0]);
        Assert.AreEqual(new DialogGreetingSlot(DialogGreetingSlotKind.Float, 5), DialogGreeting.ParseSlots("1 fl5")[1]);
        var stopped = DialogGreeting.ParseSlots("1 300, 4 fx 1, 2 0");
        Assert.AreEqual((DialogGreetingSlotKind.Jump, DialogGreetingSlotKind.Default, DialogGreetingSlotKind.Default),
            (stopped[1].Kind, stopped[4].Kind, stopped[2].Kind), "an f without l ends the parse");
        Assert.AreEqual(default, DialogGreeting.ParseSlots("7")[7], "the text ends before the value");
        Assert.AreEqual(new DialogGreetingSlot(DialogGreetingSlotKind.Off, 0), DialogGreeting.ParseSlots("7 x")[7]);
        Assert.IsTrue(DialogGreeting.ParseSlots("").All(slot => slot.Kind == DialogGreetingSlotKind.Default));
    }

    [TestMethod]
    public void The_reaction_line_follows_category_and_met_before_with_the_class_and_race_offsets()
    {
        // Class 2: the class groups start at group + 100; PC race 3 against NPC race 1: the race groups at group + 200.
        var cases = new (DialogReactionCategory Category, bool Met, int Number, GeneratedDialogFile File)[]
        {
            (DialogReactionCategory.Love, true, 11100, GeneratedDialogFile.ClassMaleToMale),
            (DialogReactionCategory.Love, false, 8100, GeneratedDialogFile.ClassMaleToMale),
            (DialogReactionCategory.Amiable, true, 12100, GeneratedDialogFile.ClassMaleToMale),
            (DialogReactionCategory.Courteous, false, 9100, GeneratedDialogFile.ClassMaleToMale),
            (DialogReactionCategory.Neutral, true, 13100, GeneratedDialogFile.ClassMaleToMale),
            (DialogReactionCategory.Neutral, false, 10100, GeneratedDialogFile.ClassMaleToMale),
            (DialogReactionCategory.Suspicious, true, 4200, GeneratedDialogFile.RaceMaleToMale),
            (DialogReactionCategory.Dislike, false, 2200, GeneratedDialogFile.RaceMaleToMale),
            (DialogReactionCategory.Hatred, true, 5200, GeneratedDialogFile.RaceMaleToMale),
            (DialogReactionCategory.Hatred, false, 3200, GeneratedDialogFile.RaceMaleToMale),
        };
        foreach (var (category, met, number, file) in cases)
        {
            var greeter = Greeter(Facts() with { Category = category, MetBefore = met, NpcSocialClass = 2, NpcRace = 1, PcRace = 3 });
            var conversation = Start("g:", greeter);
            Assert.AreEqual($"L{number}", conversation.NpcLine!.Text, $"{category} met {met}");
            Assert.AreEqual(new DialogLineOrigin(DialogLineOriginKind.Generated, number, file), conversation.NpcLine.Origin);
            Assert.AreEqual(DialogConversationPhase.AwaitingSelection, conversation.Phase, "the reaction line keeps the node's replies");
            Assert.AreEqual("reply", conversation.Options.Single().Text);
        }
        var sameRace = Start("g:", Greeter(Facts() with { Category = DialogReactionCategory.Hatred, NpcRace = 3, PcRace = 3 }));
        Assert.AreEqual("L3000", sameRace.NpcLine!.Text, "the same race reads the first block");
    }

    [TestMethod]
    public void The_file_follows_the_npc_then_the_pc_gender_and_a_dumb_npc_reads_the_dumb_files_without_offsets()
    {
        Assert.AreEqual(GeneratedDialogFile.ClassMaleToFemale, Start("g:", Greeter(Facts() with { PcMale = false })).NpcLine!.Origin!.File);
        Assert.AreEqual(GeneratedDialogFile.ClassFemaleToMale, Start("g:", Greeter(Facts() with { NpcMale = false })).NpcLine!.Origin!.File);
        Assert.AreEqual(GeneratedDialogFile.ClassFemaleToFemale, Start("g:", Greeter(Facts() with { NpcMale = false, PcMale = false })).NpcLine!.Origin!.File);
        var dumb = Start("g:", Greeter(Facts() with { NpcDumb = true, NpcSocialClass = 4 }));
        Assert.AreEqual(new DialogLineOrigin(DialogLineOriginKind.Generated, 10000, GeneratedDialogFile.ClassDumbMaleToMale), dumb.NpcLine!.Origin);
        var dumbRace = Start("g:", Greeter(Facts() with { NpcDumb = true, Category = DialogReactionCategory.Dislike, PcRace = 2 }));
        Assert.AreEqual(new DialogLineOrigin(DialogLineOriginKind.Generated, 2000, GeneratedDialogFile.RaceDumbMaleToMale), dumbRace.NpcLine!.Origin);
    }

    [TestMethod]
    public void A_gapped_block_picks_among_stored_entries_in_steamwoven_and_spans_the_gap_in_the_original()
    {
        var files = Files((GeneratedDialogFile.ClassMaleToMale, "{9950}{prev}{10000}{a}{10001}{b}{10003}{d}"));
        var steamwoven = Greeter(Facts(), files, rolls: [ 100, 2 ]);
        Assert.AreEqual("d", Start("g:", steamwoven).NpcLine!.Text, "the third stored entry");
        CollectionAssert.AreEqual(new[] { (DialogGreetingRoll.ReputationChance, 1, 100), (DialogGreetingRoll.Line, 0, 2) }, steamwoven.Asked.ToArray());

        var original = Greeter(Facts(), files, rolls: [ 100, 2 ], DialogRuleProfile.Original);
        var missing = Start("g:", original).NpcLine!;
        Assert.AreEqual(@"[missing generated line 10002 in mes\gd_cls_m2m.mes]", missing.Text, "first + 2 is the gap");
        Assert.AreEqual(new DialogLineOrigin(DialogLineOriginKind.MissingLine, 10002, GeneratedDialogFile.ClassMaleToMale), missing.Origin);
        CollectionAssert.AreEqual(new[] { (DialogGreetingRoll.ReputationChance, 1, 100), (DialogGreetingRoll.Line, 0, 2) }, original.Asked.ToArray());

        // No entry at the start: Steamwoven says nothing; the original draws −1 or 0 and lands before the block.
        var noStart = Files((GeneratedDialogFile.ClassMaleToMale, "{9999}{prev}{10001}{b}"));
        Assert.AreEqual("b", Start("g:", Greeter(Facts(), noStart, rolls: [ 100 ])).NpcLine!.Text, "Steamwoven: 10001 is the range's one entry");
        var none = Start("g:", Greeter(Facts(), Files((GeneratedDialogFile.ClassMaleToMale, "{9999}{prev}")), rolls: [ 100 ])).NpcLine!;
        Assert.AreEqual((string.Empty, DialogLineOriginKind.Empty), (none.Text, none.Origin!.Kind), "Steamwoven: no stored entry in the range");
        var before = Greeter(Facts(), noStart, rolls: [ 100, -1 ], DialogRuleProfile.Original);
        Assert.AreEqual("prev", Start("g:", before).NpcLine!.Text, "the original's random(0, −1) of −1 reads the entry before the block");
        CollectionAssert.AreEqual(new[] { (DialogGreetingRoll.ReputationChance, 1, 100), (DialogGreetingRoll.Line, -1, 0) }, before.Asked.ToArray());
    }

    [TestMethod]
    public void An_override_line_of_the_npcs_own_dialog_wins_raw_and_by_the_pc_gender()
    {
        var overrides = Read(Record("10009", "Hello @pcname@", alternate: "Hello lady"));
        var male = Start("g:", Greeter(Facts(), overrides: overrides));
        Assert.AreEqual("Hello @pcname@", male.NpcLine!.Text, "copied raw: no name substitution");
        Assert.AreEqual(new DialogLineOrigin(DialogLineOriginKind.Override, 10009), male.NpcLine.Origin);
        Assert.AreEqual("Hello lady", Start("g:", Greeter(Facts() with { PcMale = false }, overrides: overrides)).NpcLine!.Text);
        var met = Start("g:", Greeter(Facts() with { MetBefore = true }, overrides: overrides));
        Assert.AreEqual("L13000", met.NpcLine!.Text, "13000's override is 10012, which this dialog lacks");
        var race = Start("g:", Greeter(Facts() with { Category = DialogReactionCategory.Hatred }, overrides: Read(Record("11002", "Go away"))));
        Assert.AreEqual("Go away", race.NpcLine!.Text, "race group 3000: override 10999 + 3");
        var down = Start("g:", Greeter(Facts() with { States = DialogGreetingStates.NpcDown }, overrides: Read(Record("12014", "Ugh"))));
        Assert.AreEqual("Ugh", down.NpcLine!.Text, "NPC lines 1500..1599: override 11999 + 15");
    }

    [TestMethod]
    public void Generated_lines_substitute_the_names_and_drop_other_tokens()
    {
        var files = Files((GeneratedDialogFile.ClassMaleToMale, "{10000}{Hi @PCNAME@, I am @npcname@@x@!}"));
        Assert.AreEqual("Hi Ada, I am Bram!", Start("g:", Greeter(Facts(), files)).NpcLine!.Text);
    }

    [TestMethod]
    public void The_states_come_in_the_originals_order_and_slots_one_to_four_end_the_talk_unless_class_two()
    {
        var all = DialogGreetingStates.NpcDown | DialogGreetingStates.NpcSurrendered | DialogGreetingStates.BadAssociates | DialogGreetingStates.PcInvisible
            | DialogGreetingStates.PcElementalBody | DialogGreetingStates.PcPolymorphed | DialogGreetingStates.PcShrunk | DialogGreetingStates.PcArtCodeZero
            | DialogGreetingStates.PcArtCodeSeven;
        var order = new (DialogGreetingStates State, string Line, bool Ends)[]
        {
            (DialogGreetingStates.NpcDown, "L1500", false),
            (DialogGreetingStates.NpcSurrendered, "L3000", false),
            (DialogGreetingStates.BadAssociates, "L18000", true),
            (DialogGreetingStates.PcInvisible, "L22000", true),
            (DialogGreetingStates.PcElementalBody, "L19000", true),
            (DialogGreetingStates.PcPolymorphed, "L20000", true),
            (DialogGreetingStates.PcShrunk, "L6000", false),
            (DialogGreetingStates.PcArtCodeZero, "L16000", false),
            (DialogGreetingStates.PcArtCodeSeven, "L17000", false),
        };
        var states = all;
        foreach (var (state, line, ends) in order)
        {
            var conversation = Start("g:", Greeter(Facts() with { States = states }, rolls: []));
            Assert.AreEqual(line, conversation.NpcLine!.Text, state.ToString());
            Assert.AreEqual(ends ? DialogConversationPhase.Ended : DialogConversationPhase.AwaitingSelection, conversation.Phase, state.ToString());
            if (ends) Assert.AreEqual(DialogEndKind.NpcFinalLine, conversation.End!.Kind);
            if (ends) Assert.AreEqual(DialogConversationPhase.AwaitingSelection,
                Start("g:", Greeter(Facts() with { States = states, NpcSocialClass = 2 }, rolls: [])).Phase, "class 2 keeps talking");
            states &= ~state;
        }
        var surrendered = Start("g:", Greeter(Facts() with { States = DialogGreetingStates.NpcSurrendered }, overrides: Read(Record("12029", "Mercy"))));
        Assert.AreEqual("Mercy", surrendered.NpcLine!.Text, "the surrendered line's override is 12029");
        var off = Start("g:0 0, 1 0", Greeter(Facts() with { States = DialogGreetingStates.NpcDown | DialogGreetingStates.BadAssociates }));
        Assert.AreEqual("L10000", off.NpcLine!.Text, "off slots are passed over");
        var unread = Start("g:5 0", Greeter(Facts()));
        Assert.AreEqual("L10000", unread.NpcLine!.Text, "slot 5 is never read");
    }

    [TestMethod]
    public void A_jump_slot_enters_its_node_and_a_floating_slot_says_its_node_and_ends()
    {
        const string targets = "{20}{Jumped}{}{}{}{}{QU 5 1}{21}{onward}{}{1}{}{0}{}{30}{Floating}{}{}{}{}{QU 6 1}";
        var ports = new Ports();
        var jumped = Start("g:1 20, 2 fl 30", Greeter(Facts() with { States = DialogGreetingStates.BadAssociates }), targets, ports);
        Assert.AreEqual((20, "Jumped"), (jumped.CurrentNode.Value, jumped.NpcLine!.Text));
        Assert.AreEqual("onward", jumped.Options.Single().Text, "the jump node's own replies");
        CollectionAssert.AreEqual(new[] { "QU:5:1:900" }, ports.Commands, "its action runs");

        var floatPorts = new Ports();
        var floated = Start("g:1 20, 2 fl 30", Greeter(Facts() with { States = DialogGreetingStates.PcInvisible }), targets, floatPorts);
        Assert.AreEqual((30, "Floating", DialogConversationPhase.Ended), (floated.CurrentNode.Value, floated.NpcLine!.Text, floated.Phase));
        Assert.AreEqual(DialogEndKind.NpcFinalLine, floated.End!.Kind);

        var slotNine = Start("g:9 20", Greeter(Facts()), targets);
        Assert.AreEqual("Jumped", slotNine.NpcLine!.Text, "slot 9 overrides the reputation and reaction lines");
        var nine = Start("g:9 0", Greeter(Facts()));
        Assert.AreEqual((string.Empty, DialogLineOriginKind.Empty), (nine.NpcLine!.Text, nine.NpcLine.Origin!.Kind), "nothing was said before");
        Assert.AreEqual("reply", nine.Options.Single().Text);
        // Reached from a reply, slot 9 off keeps the line the NPC said before.
        var later = DialogConversation.Start(Read("{10}{First}{}{}{}{}{}{11}{on}{}{1}{}{20}{}{20}{g:9 0}{}{}{}{}{}{21}{bye}{}{1}{}{0}{}"),
            new DialogConversationRequest(new DialogNodeId(10),
                new DialogParticipants(new DialogId(700), new DialogParticipantId(800), new DialogParticipantId(900)),
                new DialogScriptLine(0), new DialogPcFacts(DialogPcGender.Male, 8, false, 0), new DialogSpeakerNames("Ada", "Bram")),
            new DialogConversationPorts(new Ports(), new Ports(), Owner(), Greeting: Greeter(Facts())));
        later.Select(later.Options.Single().Token);
        Assert.AreEqual((20, "First"), (later.CurrentNode.Value, later.NpcLine!.Text));
    }

    [TestMethod]
    public void The_reputation_greeting_rolls_twenty_percent_then_picks_a_candidate_line_by_genders()
    {
        var reputationLines = new MesFile?[] { Mes("{1001}{m2m one}{1002}{}"), Mes("{1001}{m2f one}"), Mes("{1001}{f2f one}"), Mes("{1001}{f2m one}") };
        var greeter = Greeter(Facts() with { Category = DialogReactionCategory.Love }, rolls: [ 20, 1 ], reputationLines: reputationLines, candidates: [ 1002, 1001 ]);
        var line = Start("g:", greeter).NpcLine!;
        Assert.AreEqual(("m2m one", new DialogLineOrigin(DialogLineOriginKind.Reputation, 1001)), (line.Text, line.Origin));
        CollectionAssert.AreEqual(new[] { (DialogGreetingRoll.ReputationChance, 1, 100), (DialogGreetingRoll.ReputationPick, 0, 1) }, greeter.Asked.ToArray());
        Assert.AreEqual(DialogReactionCategory.Love, greeter.CandidateCategory);
        Assert.AreEqual("f2m one", Start("g:", Greeter(Facts() with { NpcMale = false }, rolls: [ 1, 1 ], reputationLines: reputationLines, candidates: [ 1002, 1001 ])).NpcLine!.Text);
        Assert.AreEqual("m2f one", Start("g:", Greeter(Facts() with { PcMale = false }, rolls: [ 1, 0 ], reputationLines: reputationLines, candidates: [ 1001 ])).NpcLine!.Text);

        Assert.AreEqual("L10000", Start("g:", Greeter(Facts(), rolls: [ 21 ], reputationLines: reputationLines, candidates: [ 1001 ])).NpcLine!.Text, "above 20");
        Assert.AreEqual("L10000", Start("g:", Greeter(Facts(), rolls: [ 5, 0 ], reputationLines: reputationLines, candidates: [ 1002 ])).NpcLine!.Text, "an empty entry");
        var noCandidate = Greeter(Facts(), rolls: [ 5 ], reputationLines: reputationLines);
        Assert.AreEqual("L10000", Start("g:", noCandidate).NpcLine!.Text);
        CollectionAssert.AreEqual(new[] { (DialogGreetingRoll.ReputationChance, 1, 100), (DialogGreetingRoll.Line, 0, 0) }, noCandidate.Asked.ToArray(),
            "the chance is rolled without reputations; no pick without a candidate");
    }

    [TestMethod]
    public void The_nodes_action_runs_before_the_greeting()
    {
        var ports = new Ports();
        var greeter = Greeter(Facts());
        greeter.OnFacts = () => Assert.AreEqual(1, ports.Commands.Count, "the action ran first");
        Start("g:", greeter, string.Empty, ports, action: "QU 7 1");
        Assert.AreEqual(1, greeter.FactsCalls);
    }

    [TestMethod]
    public void Missing_or_failing_greeting_inputs_stop_or_fail_the_conversation()
    {
        Assert.AreEqual(DialogFrontierKind.UnsupportedGeneratedText, Start("g:", null).Frontier!.Kind, "without the port the marker stays a frontier");
        Assert.AreEqual(DialogFrontierKind.GreetingFactsUnknown, Start("g:", Greeter(null)).Frontier!.Kind);
        var throwing = Greeter(Facts());
        throwing.OnFacts = () => throw new InvalidOperationException();
        Assert.AreEqual(DialogConversationFailureKind.QueryException, Start("g:", throwing).Failure!.Kind);
        Assert.AreEqual(DialogConversationFailureKind.InvalidQueryResult, Start("g:", Greeter(Facts(), rolls: [ 101 ])).Failure!.Kind, "a roll out of its range");
        var refused = Greeter(Facts());
        refused.Refuse = true;
        Assert.AreEqual(DialogConversationFailureKind.InvalidQueryResult, Start("g:", refused).Failure!.Kind, "a refused roll");
        Assert.AreEqual(DialogConversationFailureKind.InvalidQueryResult, Start("g:", Greeter(Facts() with { Category = (DialogReactionCategory)9 })).Failure!.Kind);
        Assert.AreEqual(DialogConversationFailureKind.MissingTarget, Start("g:1 40", Greeter(Facts())).Failure!.Kind, "a slot node that does not exist");
        Assert.AreEqual(DialogConversationFailureKind.WrongTargetRole, Start("g:1 11", Greeter(Facts())).Failure!.Kind, "a slot node that is a reply");
        Assert.AreEqual(DialogFrontierKind.UnsupportedGeneratedText, Start("g:1 20", Greeter(Facts()), "{20}{g:}{}{}{}{}{}").Frontier!.Kind,
            "a slot node that is itself a greeting");
        var absent = Start("g:", Greeter(Facts(), GeneratedDialogFiles.Create(new MesFile?[GeneratedDialogFiles.Count]), rolls: [ 100 ]));
        Assert.AreEqual((string.Empty, DialogLineOriginKind.Empty), (absent.NpcLine!.Text, absent.NpcLine.Origin!.Kind), "an absent file says nothing");
    }

    [TestMethod]
    public void Slot_nodes_reserve_their_budget_and_replies_to_greetings_are_planned_one_level()
    {
        // MaxTurns 1: a greeting that may jump needs a second turn, so it stops before its own action runs.
        var ports = new Ports();
        var limited = Start("g:9 20", Greeter(Facts()), "{20}{Jumped}{}{}{}{}{}", ports, "QU 7 1", new DialogConversationOptions { MaxTurns = 1 });
        Assert.AreEqual(DialogFrontierKind.TurnLimit, limited.Frontier!.Kind);
        Assert.AreEqual(0, ports.Commands.Count, "no half-run step");
        Assert.AreEqual(DialogConversationPhase.AwaitingSelection, Start("g:", Greeter(Facts()), options: new DialogConversationOptions { MaxTurns = 1 }).Phase,
            "a greeting without slot nodes needs no second turn");
        var effects = Start("g:9 20", Greeter(Facts()), "{20}{Jumped}{}{}{}{}{QU 5 1, QU 6 1}", new Ports(), "QU 7 1", new DialogConversationOptions { MaxEffects = 2 });
        Assert.AreEqual(DialogFrontierKind.EffectLimit, effects.Frontier!.Kind, "the slot node's actions are reserved");

        var floated = Start("g:9 fl 20", Greeter(Facts()), "{20}{Floating}{}{}{}{}{}");
        Assert.AreEqual(("Floating", DialogConversationPhase.Ended), (floated.NpcLine!.Text, floated.Phase), "slot 9 can float");
        Assert.AreEqual(DialogFrontierKind.UnsupportedGeneratedText, Start("g:9 fl 20", Greeter(Facts()), "{20}{g:}{}{}{}{}{}").Frontier!.Kind,
            "a floating node that is itself a greeting");

        // A reply whose target is a g: node: a final line cannot say a greeting; a direct one plans the greeting's slot nodes.
        var replies = Read("{10}{Hello}{}{}{}{}{}{11}{fl}{}{1}{}{20}{fl 30}{12}{direct}{}{1}{}{30}{}{13}{bad}{}{1}{}{40}{}"
            + "{20}{x}{}{}{}{}{}{30}{g:}{}{}{}{}{}{31}{r}{}{1}{}{0}{}{40}{g:1 99}{}{}{}{}{}{41}{r}{}{1}{}{0}{}");
        var conversation = Start(replies, Greeter(Facts()));
        CollectionAssert.AreEqual(new DialogOptionBlockKind?[] { DialogOptionBlockKind.TargetUnsupported, null, DialogOptionBlockKind.TargetInvalid },
            conversation.Options.Select(option => option.Block?.Kind).ToArray());
        conversation.Select(conversation.Options[1].Token);
        Assert.AreEqual((30, "L10000"), (conversation.CurrentNode.Value, conversation.NpcLine!.Text), "the direct reply greets");

        var nullCandidates = new FakeGreeter(Facts(), new DialogGreetingSources(NumberedFiles(), new MesFile?[4], null), [1], DialogRuleProfile.Steamwoven, null!);
        Assert.AreEqual(DialogConversationFailureKind.InvalidQueryResult, Start("g:", nullCandidates).Failure!.Kind);
    }

    internal static DialogGreetingFacts Facts() => new(true, true, false, 0, 0, 0, DialogReactionCategory.Neutral, false);

    private static DialogConversation Start(string greeting, IDialogGreetingPort? port, string extra = "", Ports? ports = null, string action = "",
        DialogConversationOptions? options = null) =>
        Start(Read($"{{10}}{{{greeting}}}{{}}{{}}{{}}{{}}{{{action}}}" + Replies + extra), port, ports, options);

    private static DialogConversation Start(DlgDocument document, IDialogGreetingPort? port, Ports? ports = null, DialogConversationOptions? options = null)
    {
        ports ??= new Ports();
        return DialogConversation.Start(document,
            new DialogConversationRequest(new DialogNodeId(10),
                new DialogParticipants(new DialogId(700), new DialogParticipantId(800), new DialogParticipantId(900)),
                new DialogScriptLine(0), new DialogPcFacts(DialogPcGender.Male, 8, false, 0), new DialogSpeakerNames("Ada", "Bram")),
            new DialogConversationPorts(ports, ports, Owner(), new World(), Greeting: port), options);
    }

    private static FakeGreeter Greeter(DialogGreetingFacts? facts, GeneratedDialogFiles? files = null, int[]? rolls = null, DialogRuleProfile? rules = null,
        DlgDocument? overrides = null, IReadOnlyList<MesFile?>? reputationLines = null, int[]? candidates = null) =>
        new(facts, new DialogGreetingSources(files ?? NumberedFiles(), reputationLines ?? new MesFile?[4], overrides), rolls ?? [ 100 ], rules ?? DialogRuleProfile.Steamwoven,
            candidates ?? []);

    /// <summary>Every file holds, for every group the greeting reads and every 50-block offset 0..10, its first entry, whose
    /// text names its number.</summary>
    private static GeneratedDialogFiles NumberedFiles()
    {
        var text = new StringBuilder();
        var numbers = new SortedSet<int>();
        foreach (var group in new[] { 1500, 2000, 3000, 4000, 5000, 6000, 8000, 9000, 10000, 11000, 12000, 13000, 16000, 17000, 18000, 19000, 20000, 22000 })
            for (var block = 0; block <= 10; block++) numbers.Add(group + 50 * block);
        foreach (var number in numbers) text.Append($"{{{number}}}{{L{number}}}");
        var mes = Mes(text.ToString());
        return GeneratedDialogFiles.Create(Enumerable.Repeat(mes, GeneratedDialogFiles.Count).ToArray()!);
    }

    private static GeneratedDialogFiles Files(params (GeneratedDialogFile File, string Text)[] files)
    {
        var all = new MesFile?[GeneratedDialogFiles.Count];
        foreach (var (file, text) in files) all[(int)file] = Mes(text);
        return GeneratedDialogFiles.Create(all);
    }

    private static MesFile Mes(string text) => MesFile.Parse(Encoding.ASCII.GetBytes(text)).File!;

    internal sealed class FakeGreeter(DialogGreetingFacts? facts, DialogGreetingSources sources, int[] rolls, DialogRuleProfile rules, int[] candidates)
        : IDialogGreetingPort
    {
        private readonly Queue<int> _rolls = new(rolls);
        public List<(DialogGreetingRoll, int, int)> Asked { get; } = [];
        public Action? OnFacts { get; set; }
        public bool Refuse { get; set; }
        public int FactsCalls { get; private set; }
        public DialogReactionCategory? CandidateCategory { get; private set; }
        public DialogRuleProfile Rules => rules;

        public DialogGreetingFacts? Facts(DialogReactionQuery query)
        {
            Assert.AreEqual(new DialogReactionQuery(new DialogParticipantId(900), new DialogParticipantId(800)), query);
            FactsCalls++;
            OnFacts?.Invoke();
            return facts;
        }

        public DialogGreetingSources Sources(DialogReactionQuery query) => sources;

        public IReadOnlyList<int> ReputationCandidates(DialogReactionQuery query, DialogReactionCategory category)
        {
            CandidateCategory = category;
            return candidates;
        }

        public int? Roll(DialogGreetingRoll roll, int min, int max)
        {
            Asked.Add((roll, min, max));
            if (Refuse) return null;
            return _rolls.Count > 0 ? _rolls.Dequeue() : min;
        }
    }
}
