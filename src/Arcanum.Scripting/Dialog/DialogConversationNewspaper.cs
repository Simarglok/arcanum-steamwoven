using Arcanum.Data;

namespace Arcanum.Scripting.Dialog;

/// <summary>
/// ARC-UI-1739 (ledger NP1–NP8): the talk of a Player <c>p:</c> reply, the original's newspaper vendor. Picking the reply (state 0x19 of 0x414810, 0x41A520) runs its action, then the vendor's greeting (the NPC line 4200..4299) replaces the NPC line and the
/// replies without leaving the node, with four ways on: buy the shown issue (PC line 2200..2299), hear its headline (2000..2099, state 0x1a, 0x41A620), see the back issues (2100..2199, state 0x1b, 0x41A700) and leave (800..899). An issue is bought on the price
/// screen of the other talks (0x418A40 with the base price 2, state 0x1c), paid (0x418B30) and then given (state 0x1d, 0x41A8C0: a new newspaper item whose written start line is the issue, in the PC's pack, 0x4BF210): the NPC's class line 15000 and the PC's
/// class line 1000 close it. The issue queue is the session's (<see cref="DialogNewspaperIssues"/>), the headlines the newspaper file's.
/// </summary>
public sealed partial class DialogConversation
{
    // The price the vendor asks before the reaction scale (0x41A880 passes 2 to 0x418A40) and the class group of the NPC's line when the paper is given (0x41A8C0).
    private const int NewspaperBasePrice = 2, NewspaperGivenGroup = 15000;

    private bool NewspaperAvailable => _ports.NewspaperStand is not null && _ports.Greeting is not null && _ports.World is not null && _ports.Gold is not null && (_ports.Purse ?? _ports.Trainer) is not null;

    /// <summary>The <c>p:</c> reply was picked and its action has run: the vendor's greeting opens (ledger NP1).</summary>
    [OriginalFunction(0x41A520, OriginalFunctionScope.Partial)]
    private void OpenNewspaper(OfferPlan plan)
    {
        var record = plan.Reply.Record;
        var source = new DialogOperationSource(new DialogNodeId(record.Id), record.SerializedIndex, DlgFieldKind.Text, record.Text.PayloadOffset, 0, 2, 0);
        _talk = new TrainerTalk(plan.Reply, plan.Target, source);
        PresentNewspaperGreeting();
    }

    // The queue as the session holds it; a throwing port fails the conversation, and so does more history than the table's four reserved words.
    private bool ReadNewspaper(out DialogNewspaperIssues issues)
    {
        issues = new DialogNewspaperIssues(0, []);
        DialogNewspaperIssues? read;
        try
        {
            read = _ports.NewspaperStand!.Issues();
        }
        catch (Exception)
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.QueryException, CurrentNode, _talk?.Source));
            return false;
        }
        if (read?.Back is not { Count: <= 4 })
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.InvalidQueryResult, CurrentNode, _talk?.Source));
            return false;
        }
        issues = read;
        return true;
    }

    private bool ReadHeadline(int issue, out string headline)
    {
        headline = string.Empty;
        string? read;
        try
        {
            read = _ports.NewspaperStand!.Headline(issue);
        }
        catch (Exception)
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.QueryException, CurrentNode, _talk?.Source));
            return false;
        }
        if (read is null)
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.InvalidQueryResult, CurrentNode, _talk?.Source));
            return false;
        }
        headline = read;
        return true;
    }

    /// <summary>
    /// A headline followed by the NPC's line of a range (0x41A620): a headline that does not end in a full stop, a question mark or an exclamation mark gets a full stop, then one space (an empty headline adds nothing).
    /// </summary>
    internal static string PunctuatedHeadline(string headline) => Arcanum.Data.Written.WrittenTexts.PunctuatedHeadline(headline);

    // The NPC's plain line of a range (0x418780), behind a headline when one is given (the text the message hook gave, then the line).
    private bool NewspaperNpcLine(int first, int last, string? headline, out DialogNpcLine line)
    {
        line = new DialogNpcLine(CurrentNode, string.Empty);
        if (!GenerateTalkLine((facts, sources, port) => DialogGreeting.NpcRange(first, last, facts, sources, port, _request.Names, _request.Encoding, _options.MaxTextBytes), out var text, out var origin))
        {
            return false;
        }
        line = new DialogNpcLine(CurrentNode, headline is null ? text : PunctuatedHeadline(headline) + text, origin);
        return true;
    }

    /// <summary>The greeting (ledger NP1): the NPC's line 4200..4299 and the four ways on; the buying one names the issue shown now.</summary>
    private void PresentNewspaperGreeting()
    {
        if (!ReadNewspaper(out var issues) || !NewspaperNpcLine(4200, 4299, null, out var line)) return;
        if (!PcTalkLine(2200, 2299, classLine: false, out var buy) || !PcTalkLine(2000, 2099, classLine: false, out var headline) ||
            !PcTalkLine(2100, 2199, classLine: false, out var back) || !PcTalkLine(800, 899, classLine: false, out var leave))
        {
            return;
        }
        Present(line, [buy with { Choice = new TrainerChoice(TrainerChoiceKind.NewsBuy, Issue: issues.Head) },
            headline with { Choice = new TrainerChoice(TrainerChoiceKind.NewsHeadline) },
            back with { Choice = new TrainerChoice(TrainerChoiceKind.NewsBack) },
            leave with { Choice = new TrainerChoice(TrainerChoiceKind.Follow) }]);
    }

    /// <summary>The headline of the shown issue (ledger NP2, state 0x1a): the NPC says it with a closing full stop and a line of 4300..4399; yes (PC line 1..99) goes to the price, no (100..199) leaves.</summary>
    [OriginalFunction(0x41A620, OriginalFunctionScope.Partial)]
    private void PresentNewspaperHeadline()
    {
        if (!ReadNewspaper(out var issues) || !ReadHeadline(issues.Head, out var headline) || !NewspaperNpcLine(4300, 4399, headline, out var line)) return;
        if (!PcTalkLine(1, 99, classLine: false, out var yes) || !PcTalkLine(100, 199, classLine: false, out var no)) return;
        Present(line, [yes with { Choice = new TrainerChoice(TrainerChoiceKind.NewsBuy, Issue: issues.Head) }, no with { Choice = new TrainerChoice(TrainerChoiceKind.Follow) }]);
    }

    /// <summary>
    /// The back issues (ledger NP3, state 0x1b): the issues in the table's reserved words that are not −1, in table order, each an option with its headline as the text; the NPC's line is 4500..4599 and the way out the PC's 800..899.
    /// None gives the line 4400..4499 and the single answer 600..699.
    /// </summary>
    [OriginalFunction(0x41A700, OriginalFunctionScope.Partial)]
    private void PresentNewspaperBack()
    {
        if (!ReadNewspaper(out var issues)) return;
        if (issues.Back.Count == 0)
        {
            if (!NewspaperNpcLine(4400, 4499, null, out var none) || !PcTalkLine(600, 699, classLine: false, out var answer)) return;
            Present(none, [answer with { Choice = new TrainerChoice(TrainerChoiceKind.Follow) }]);
            return;
        }
        if (!NewspaperNpcLine(4500, 4599, null, out var line)) return;
        var options = new List<TrainerOption>();
        foreach (var issue in issues.Back)
        {
            if (!ReadHeadline(issue, out var headline)) return;
            options.Add(new TrainerOption(headline, null, new TrainerChoice(TrainerChoiceKind.NewsBuy, Issue: issue)));
        }
        if (!PcTalkLine(800, 899, classLine: false, out var leave)) return;
        options.Add(leave with { Choice = new TrainerChoice(TrainerChoiceKind.Follow) });
        Present(line, options);
    }

    /// <summary>The price of an issue (ledger NP4, state 0x1c): the base price 2 on the reaction scale, the price screen of the other talks; yes pays and gives the issue.</summary>
    [OriginalFunction(0x41A880, OriginalFunctionScope.Partial)]
    private void PresentNewspaperQuote(int issue) => PresentPriceScreen(NewspaperBasePrice, price => new TrainerChoice(TrainerChoiceKind.PayNews, Price: price, Issue: issue));

    /// <summary>
    /// The payment and the printed issue (ledger NP5, NP6; 0x418B30, state 0x1d 0x41A8C0): too little own gold is the notice of line 2000; otherwise the gold goes to the NPC and the issue is given, then the NPC's class line 15000 and
    /// the PC's class line 1000 close the screen.
    /// </summary>
    [OriginalFunction(0x41A8C0, OriginalFunctionScope.Partial)]
    private void PayAndGiveNewspaper(TrainerChoice choice)
    {
        var talk = _talk!;
        if (!EnoughGold(choice.Price)) return;
        if ((long)_effects.Count + 2 > _options.MaxEffects)
        {
            Stop(new DialogConversationFrontier(DialogFrontierKind.EffectLimit, CurrentNode), null);
            return;
        }
        // The paper is made before the gold is taken, so a host that cannot make it (or write its issue) costs the PC nothing; the order is not visible in the original's screens.
        var give = new GiveNewspaperCommand(_request.Participants.Npc, _request.Participants.PlayerCharacter, choice.Issue);
        if (!RunTalkCommand(give, () => _ports.NewspaperStand!.Execute(give), talk.Source)) return;
        var pay = new GoldCommand(_request.Participants.PlayerCharacter, _request.Participants.Npc, -choice.Price);
        if (!RunTalkCommand(pay, () => _ports.Gold!.Execute(pay), talk.Source)) return;
        if (!NpcTalkLine(NewspaperGivenGroup, null, out var line) || !PcTalkLine(1000, 0, classLine: true, out var answer)) return;
        Present(line, [answer with { Choice = new TrainerChoice(TrainerChoiceKind.Follow) }]);
    }
}
