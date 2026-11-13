using Arcanum.Data;

namespace Arcanum.Scripting.Dialog;

/// <summary>
/// ARC-SCRIPT-1150 (ledger DTR1–DTR9): the talk of a Player <c>t:</c> reply, the original's trainer menu. Picking the reply (state 5 of 0x414810)
/// replaces the NPC's line and the replies without leaving the node: a menu of the listed skills and the way out (0x418CA0), a skill's answer
/// (0x418DE0: already trained, cannot be taught, or the price of 0x418A40), the payment (0x418B30) and the training (0x418F30). Every answer is
/// one of the generated lines of the class groups 1000 to 6000 and the PC lines 1..99, 100..199, 600..699 and 800..899.
/// </summary>
public sealed partial class DialogConversation
{
    // The first-tier training this talk sells for gold (0x418F30 sets tier 1 through the setters 0x4C6170 and 0x4C6850), and the price it asks for,
    // before the reaction scale (0x418DE0 passes 100 to 0x418A40).
    private const int TrainerTier = 1, TrainerBasePrice = 100, MaxTrainerSkill = 15;

    // The NPC line groups of the talk (0x418CA0: 3000, 0x418A40: 1000, 0x418C40: 2000, 4000, 5000, 0x418F30: 6000) and the PC answers.
    private const int MenuGroup = 3000, QuoteGroup = 1000, NoGoldGroup = 2000, TrainedGroup = 4000, RefusedGroup = 5000, DoneGroup = 6000;

    // The race group of the NPC's insulted line and last words (0x416840, 0x4191E0, 0x413130 call the race generator with 1000).
    private const int InsultGroup = 1000;

    private sealed record TrainerTalk(ReplyPlan Reply, StepPlan? Target, DialogOperationSource Source, FollowPlan? Yes = null);

    private enum TrainerChoiceKind { Skill, Pay, PayRumor, Follow, Area, More, PayDirections, PayFollow, HealMenu, HealService, HealSpell, PayHealService, PayHealSpell }

    private readonly record struct TrainerChoice(TrainerChoiceKind Kind, int Skill = 0, int Price = 0, int Rumor = 0, int Area = 0, int Offset = 0, int Spell = 0);

    private sealed record TrainerOption(string Text, DialogLineOrigin? Origin, TrainerChoice Choice);

    private TrainerTalk? _talk;
    private List<TrainerOption> _talkOptions = [];

    private bool TrainerAvailable => _ports.Trainer is not null && _ports.Skills is not null && _ports.Gold is not null && _ports.World is not null;

    /// <summary>
    /// The numbers after the marker of a <c>t:</c> reply (the text the original's list reader gets): comma-separated numbers or
    /// <c>a-b</c> ranges, at most 100, read like <c>atoi</c> (saturating where the CRT wraps: a number outside 0..15 is refused either way); the whole reply is outside the subset when a number is no skill (0..15) or the menu
    /// (the listed skills and the way out) would not fit the five options the original keeps (it writes past its arrays then).
    /// </summary>
    internal static int[]? ParseTrainerSkills(string text)
    {
        var numbers = ParseNumberList(text.Length >= 2 ? text[2..] : string.Empty);
        if (numbers.Count + 1 > MaxEligible || numbers.Any(number => number is < 0 or > MaxTrainerSkill)) return null;
        return [.. numbers];
    }

    /// <summary>The original's list reader (ledger DTR2): the trainer's skills and, later, the rumor range of <c>r:</c>.</summary>
    internal static List<int> ParseNumberList(string text)
    {
        var result = new List<int>();
        var rest = text;
        while (true)
        {
            var comma = rest.IndexOf(',');
            var part = comma < 0 ? rest : rest[..comma];
            var dash = part.IndexOf('-');
            if (dash < 0)
            {
                result.Add(Atoi(part));
            }
            else
            {
                var low = Atoi(part[..dash]);
                var high = Atoi(part[(dash + 1)..]);
                for (var number = low; number <= high && result.Count < 100; number++)
                {
                    result.Add(number);
                    if (number == int.MaxValue) break;
                }
            }
            if (comma < 0 || result.Count > 99) return result;
            rest = rest[(comma + 1)..];
        }
    }

    // atoi: leading white space, an optional sign, digits (saturating at the 32-bit range); no digits is 0.
    private static int Atoi(string text)
    {
        var at = 0;
        while (at < text.Length && text[at] is ' ' or '\t' or '\n' or '\r' or '\v' or '\f') at++;
        var negative = false;
        if (at < text.Length && text[at] is '+' or '-') negative = text[at++] == '-';
        long value = 0;
        while (at < text.Length && char.IsAsciiDigit(text[at]))
        {
            value = Math.Min(value * 10 + (text[at++] - '0'), 1L << 31);
        }
        value = negative ? -value : value;
        return (int)Math.Clamp(value, int.MinValue, int.MaxValue);
    }

    /// <summary>
    /// The reaction scale of the price (ledger BT2): the standard reaction counts at most 100; the factor is 200 below 0, 200 − 2r below
    /// 50, else 120 − trunc(2r / 5); the price is the factor's share of <paramref name="price"/> (truncated). A price of 1 is shown as 2 (ledger DTR5).
    /// </summary>
    internal static int ReactionPrice(int reaction, int price)
    {
        var r = Math.Min(reaction, 100);
        var factor = r < 0 ? 200 : r < 50 ? (100 - r) * 2 : 120 - r * 2 / 5;
        var scaled = (int)((long)factor * price / 100);
        return scaled == 1 ? 2 : scaled;
    }

    /// <summary>Replaces the first <c>%d</c> of a generated line with the price as the original's printf does; <c>%%</c> is one percent sign.</summary>
    internal static string FormatPrice(string line, int price)
    {
        var builder = new System.Text.StringBuilder();
        var replaced = false;
        for (var at = 0; at < line.Length; at++)
        {
            if (line[at] == '%' && at + 1 < line.Length && line[at + 1] == '%') { builder.Append('%'); at++; }
            else if (!replaced && line[at] == '%' && at + 1 < line.Length && line[at + 1] == 'd')
            {
                builder.Append(price.ToString(System.Globalization.CultureInfo.InvariantCulture));
                replaced = true;
                at++;
            }
            else builder.Append(line[at]);
        }
        return builder.ToString();
    }

    /// <summary>The <c>t:</c> reply was picked and its action has run: the skill menu opens (ledger DTR3).</summary>
    private void OpenTalk(OfferPlan plan)
    {
        var record = plan.Reply.Record;
        var source = new DialogOperationSource(new DialogNodeId(record.Id), record.SerializedIndex, DlgFieldKind.Text, record.Text.PayloadOffset, 0, 2, 0);
        _talk = new TrainerTalk(plan.Reply, plan.Target, source);
        PresentMenu();
    }

    /// <summary>The menu (ledger DTR3): the NPC's class line 3000, one reply per listed skill and the way out (PC lines 800..899).</summary>
    private void PresentMenu()
    {
        if (!NpcTalkLine(MenuGroup, null, out var line)) return;
        var options = new List<TrainerOption>();
        foreach (var skill in _talk!.Reply.Trainer!)
        {
            string? name;
            try
            {
                name = _ports.Trainer!.SkillName(skill);
            }
            catch (Exception)
            {
                Fail(new DialogConversationFailure(DialogConversationFailureKind.QueryException, CurrentNode, _talk.Source));
                return;
            }
            options.Add(new TrainerOption(name ?? string.Empty, null, new TrainerChoice(TrainerChoiceKind.Skill, skill)));
        }
        if (!PcTalkLine(800, 899, classLine: false, out var back)) return;
        options.Add(back with { Choice = new TrainerChoice(TrainerChoiceKind.Follow) });
        Present(line, options);
    }

    /// <summary>A skill of the menu was picked (ledger DTR4).</summary>
    private DialogConversationStepResult PresentSkill(int skill)
    {
        var source = _talk!.Source;
        // Already trained: the answer is line 4000 (0x418C40).
        if (ReadSkill(skill, training: true, source) is not { } training) return Result();
        if (training != 0)
        {
            PresentNotice(TrainedGroup, classAnswer: false);
            return Result();
        }
        // The original probes the setter for the first tier and puts the tier back; the setter refuses exactly when the effective level is below the
        // tier's minimum (ledger SR1, SR3: 1), so the level answers the probe without a write.
        if (ReadSkill(skill, training: false, source) is not { } level) return Result();
        if (level < 1)
        {
            PresentNotice(RefusedGroup, classAnswer: false);
            return Result();
        }
        PresentQuote(skill);
        return Result();
    }

    /// <summary>The price screen of a skill (ledger DTR5).</summary>
    private void PresentQuote(int skill) => PresentPriceScreen(TrainerBasePrice, price => new TrainerChoice(TrainerChoiceKind.Pay, skill, price));

    /// <summary>
    /// The price screen (ledger DTR5, RMR5, DIR4): the base price on the reaction scale in the NPC's class line 1000, and the yes and no replies; <paramref name="yes"/> makes
    /// the choice of the yes reply from the shown price, the no reply leaves by the reply's response.
    /// </summary>
    private void PresentPriceScreen(int basePrice, Func<int, TrainerChoice> yes)
    {
        if (ReadReactionValue(_talk!.Source) is not { } reaction) return;
        var price = ReactionPrice(reaction, basePrice);
        if (!NpcTalkLine(QuoteGroup, price, out var line)) return;
        if (!PcTalkLine(1, 99, classLine: false, out var accept) || !PcTalkLine(100, 199, classLine: false, out var decline)) return;
        Present(line, [accept with { Choice = yes(price) }, decline with { Choice = new TrainerChoice(TrainerChoiceKind.Follow) }]);
    }

    /// <summary>A notice (ledger DTR7, DTR8): one NPC line and the single way on, the PC line 600..699 (or the class line 1000 after a training).</summary>
    private void PresentNotice(int group, bool classAnswer)
    {
        if (!NpcTalkLine(group, null, out var line)) return;
        if (!PcTalkLine(classAnswer ? 1000 : 600, classAnswer ? 0 : 699, classAnswer, out var answer)) return;
        Present(line, [answer with { Choice = new TrainerChoice(TrainerChoiceKind.Follow) }]);
    }

    private void Present(DialogNpcLine line, List<TrainerOption> options)
    {
        NpcLine = line;
        _talkOptions = options;
        _generation++;
        EmitTalkOptions();
    }

    // The offers of the current screen with this generation's tokens; the texts were picked when the screen was built.
    private void EmitTalkOptions()
    {
        var offers = new List<DialogOption>();
        for (var index = 0; index < _talkOptions.Count; index++)
        {
            var option = _talkOptions[index];
            var response = new DialogResponseId(-(index + 2));
            var follow = option.Choice.Kind == TrainerChoiceKind.Follow;
            var reply = _talk!.Reply;
            var rumor = reply.Rumor is not null;
            var directions = reply.Directions;
            var letter = _talk!.Yes is not null ? 'm' : directions is { } d ? (d.Learn ? 'x' : 'd') : rumor ? 'r' : reply.Story ? 'c' : reply.Healer ? 'h' : 't';
            offers.Add(new DialogOption(index + 1, response, -1, DialogOptionTextKind.Generated, option.Text, new DialogGeneratedMarker(letter),
                follow ? reply.Transition : directions is not null ? DialogOptionTransition.DirectionsStep : rumor ? DialogOptionTransition.RumorStep : reply.Healer ? DialogOptionTransition.HealerStep : DialogOptionTransition.TrainerStep,
                follow && reply.Target is not null ? new DialogNodeId(reply.Target.Id) : null,
                follow ? reply.ScriptLine : null,
                new DialogOptionToken(_identity, Turn, _generation, response), null, option.Origin));
        }
        _offers = offers.AsReadOnly();
        _offerPlans = new Dictionary<int, OfferPlan>();
    }

    private DialogConversationStepResult SelectTalk(DialogOption offer)
    {
        var talk = _talk!;
        var index = -offer.ResponseId.Value - 2;
        var choice = _talkOptions[index].Choice;
        // The token is consumed before the first effect, as in Select; the next screen (or the end) offers new ones.
        _offers = Array.Empty<DialogOption>();
        switch (choice.Kind)
        {
            case TrainerChoiceKind.Skill:
                return PresentSkill(choice.Skill);
            case TrainerChoiceKind.Pay:
                PayAndTrain(choice);
                return Result();
            case TrainerChoiceKind.PayRumor:
                PayAndTell(choice);
                return Result();
            case TrainerChoiceKind.HealMenu:
                PresentHealerMenu(choice.Skill);
                return Result();
            case TrainerChoiceKind.HealService:
            case TrainerChoiceKind.HealSpell:
                PresentHealerChoice(choice);
                return Result();
            case TrainerChoiceKind.PayHealService:
            case TrainerChoiceKind.PayHealSpell:
                PayAndHeal(choice);
                return Result();
            case TrainerChoiceKind.Area:
                PresentArea(choice.Area, choice.Price);
                return Result();
            case TrainerChoiceKind.More:
                PresentAreas(choice.Offset);
                return Result();
            case TrainerChoiceKind.PayDirections:
                PayAndTellWay(choice);
                return Result();
            case TrainerChoiceKind.PayFollow:
                PayAndFollow(choice);
                return Result();
            default:
                // The way out: the reply's own response (0x414810 case 0, 1 or 2 on the value the reply was built with), its action not run again.
                Follow(talk.Reply.Transition, talk.Target, talk.Reply.ScriptLine);
                return Result();
        }
    }

    // Leaves the talk by a response: the next node (entered as an ordinary step), the script line, or the end that returns to the caller's next line.
    private void Follow(DialogOptionTransition transition, StepPlan? target, DialogScriptLine? line)
    {
        switch (transition)
        {
            case DialogOptionTransition.Direct:
                if (!Admit(target, 0)) return;
                _talk = null;
                Enter(target!);
                return;
            case DialogOptionTransition.ScriptLine:
                Finish(new DialogConversationEnd(DialogEndKind.ReturnToScript, line));
                return;
            default:
                Finish(new DialogConversationEnd(DialogEndKind.ReturnToScript, new DialogScriptLine(_request.CallerLine.Value + 1)));
                return;
        }
    }

    /// <summary>The payment and the training (ledger DTR6, DTR7): the PC's own gold must cover the price (else line 2000); then the gold goes to the NPC and the PC is trained.</summary>
    private void PayAndTrain(TrainerChoice choice)
    {
        var talk = _talk!;
        if (!EnoughGold(choice.Price)) return;
        if ((long)_effects.Count + 2 > _options.MaxEffects)
        {
            Stop(new DialogConversationFrontier(DialogFrontierKind.EffectLimit, CurrentNode), null);
            return;
        }
        var pay = new GoldCommand(_request.Participants.PlayerCharacter, _request.Participants.Npc, -choice.Price);
        if (!RunTalkCommand(pay, () => _ports.Gold!.Execute(pay), talk.Source)) return;
        var train = new SkillTrainingCommand(_request.Participants.PlayerCharacter, choice.Skill, TrainerTier);
        if (!RunTalkCommand(train, () => _ports.Skills!.Execute(train), talk.Source)) return;
        PresentNotice(DoneGroup, classAnswer: true);
    }

    /// <summary>The PC's own gold against the price (ledger DTR6): too little shows the notice of line 2000 (and answers false), a failed read fails the conversation.</summary>
    private bool EnoughGold(int price)
    {
        int gold;
        try
        {
            gold = (_ports.Purse ?? _ports.Trainer)!.PcGold(new DialogGoldQuery(_request.Participants.PlayerCharacter));
        }
        catch (Exception)
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.QueryException, CurrentNode, _talk!.Source));
            return false;
        }
        if (gold >= price) return true;
        PresentNotice(NoGoldGroup, classAnswer: false);
        return false;
    }

    private bool RunTalkCommand(DialogCommand command, Func<DialogCommandResult> run, DialogOperationSource source)
    {
        DialogCommandResult result;
        try
        {
            result = run();
        }
        catch (Exception)
        {
            Record(command, source, DialogCommandAttemptOutcome.Indeterminate);
            Fail(new DialogConversationFailure(DialogConversationFailureKind.CommandIndeterminate, CurrentNode, source));
            return false;
        }
        if (result != DialogCommandResult.Applied)
        {
            Record(command, source, DialogCommandAttemptOutcome.Rejected);
            Fail(new DialogConversationFailure(DialogConversationFailureKind.CommandRejected, CurrentNode, source));
            return false;
        }
        Record(command, source, DialogCommandAttemptOutcome.Applied);
        return true;
    }

    // The NPC's line of a class group through the greeting port's facts, files and rolls (0x418480); the text stays empty without the port or its
    // facts, as the phrases of ARC-SCRIPT-043 do. <paramref name="price"/> fills the %d of the quote's line.
    private bool NpcTalkLine(int group, int? price, out DialogNpcLine line)
    {
        line = new DialogNpcLine(CurrentNode, string.Empty);
        if (!GenerateTalkLine((facts, sources, port) =>
                DialogGreeting.NpcClassGroup(group, facts, sources, port, _request.Names, _request.Encoding, _options.MaxTextBytes), out var text, out var origin))
        {
            return false;
        }
        line = new DialogNpcLine(CurrentNode, price is { } shown ? FormatPrice(text, shown) : text, origin);
        return true;
    }

    // A PC answer of a range (0x4182D0) or, after a training, the class line 1000 (0x418390).
    private bool PcTalkLine(int first, int last, bool classLine, out TrainerOption option)
    {
        option = new TrainerOption(string.Empty, null, default);
        if (!GenerateTalkLine((facts, sources, port) => classLine
                ? DialogGreeting.PcClassGroup(first, PcIntelligence() <= LowIntelligence, facts, sources, port, _request.Names, _request.Encoding, _options.MaxTextBytes)
                : DialogGreeting.PcRange(first, last, PcIntelligence() <= LowIntelligence, facts, sources, port, _request.Names, _request.Encoding, _options.MaxTextBytes),
                out var text, out var origin))
        {
            return false;
        }
        option = new TrainerOption(text, origin, default);
        return true;
    }

    // The NPC's line of a race group through the same sources (the race generator, ARC-SCRIPT-1505): the insulted line and the last words.
    private bool NpcRaceTalkLine(int group, out DialogNpcLine line)
    {
        line = new DialogNpcLine(CurrentNode, string.Empty);
        if (!GenerateTalkLine((facts, sources, port) =>
                DialogGreeting.NpcRaceGroup(group, facts, sources, port, _request.Names, _request.Encoding, _options.MaxTextBytes), out var text, out var origin))
        {
            return false;
        }
        line = new DialogNpcLine(CurrentNode, text, origin);
        return true;
    }

    private bool GenerateTalkLine(Func<DialogGreetingFacts, DialogGreetingSources, IDialogGreetingPort, GreetingOutcome> pick, out string text, out DialogLineOrigin? origin)
    {
        text = string.Empty;
        origin = null;
        if (_ports.Greeting is not { } port) return true;
        var query = new DialogReactionQuery(_request.Participants.Npc, _request.Participants.PlayerCharacter);
        GreetingOutcome outcome;
        try
        {
            if (port.Facts(query) is not { } facts) return true;
            var sources = port.Sources(query);
            if (sources?.Files is null || port.Rules is null)
            {
                Fail(new DialogConversationFailure(DialogConversationFailureKind.InvalidQueryResult, CurrentNode, _talk?.Source));
                return false;
            }
            outcome = pick(facts, sources, port);
        }
        catch (DialogGreeting.InvalidGreetingRoll)
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.InvalidQueryResult, CurrentNode, _talk?.Source));
            return false;
        }
        catch (Exception)
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.QueryException, CurrentNode, _talk?.Source));
            return false;
        }
        if (outcome.Kind != GreetingOutcomeKind.Line)
        {
            Fail(new DialogConversationFailure(outcome.Failure, CurrentNode, _talk?.Source, TextFailure: outcome.TextFailure));
            return false;
        }
        text = outcome.Text;
        origin = outcome.Origin;
        return true;
    }
}
