using Arcanum.Data;

namespace Arcanum.Scripting.Dialog;

/// <summary>
/// ARC-SCRIPT-1304 (ledger RMR1–RMR9): the talk of a Player <c>r:</c> reply, the original's paid rumor. Picking the reply (state 8 of 0x414810) draws one of the
/// rumors its list names that is neither quelled nor known by the PC (0x418FC0); none left is the NPC's line 7000, a price below 1 tells the rumor at
/// once, a positive one asks for the price first (0x418A40, the screen of the trainer's talk) and tells it after the payment (0x418B30, state 9). Telling
/// (0x4190E0) is the rumor's text in the NPC's mouth, the rumor written to the PC's logbook and the PC's class line 1000 as the only way on.
/// The talk reuses the trainer's screens (<see cref="TrainerTalk"/>): both are the original's own overlay of the dialog state.
/// </summary>
public sealed partial class DialogConversation
{
    private const int FirstRumorNumber = 1000, LastRumorNumber = 2999, NoRumorGroup = 7000;

    private sealed record RumorOffer(int Price, int[] Rumors);

    private bool RumorAvailable(RumorOffer offer) => _ports.Rumors is not null && _ports.Greeting is not null
        && (offer.Price < 1 || _ports.World is not null && _ports.Gold is not null && (_ports.Purse ?? _ports.Trainer) is not null);

    /// <summary>
    /// The arguments after the marker of an <c>r:</c> reply (ledger RMR2): the price is the number after the first <c>$</c> (read like <c>atoi</c>), the
    /// rumors the list after the first comma from there (<see cref="ParseNumberList"/>: comma-separated numbers or <c>a-b</c> ranges, at most 100). The reply is
    /// outside the subset without a <c>$</c> or a comma after it (the original then reads address 1) and when a rumor is outside the session's 1000..2999
    /// (it reads its tables unchecked there). Other characters around the numbers (the installed replies wrap them in parentheses) do not matter.
    /// </summary>
    internal static (int Price, int[] Rumors)? ParseRumorReply(string text)
    {
        var dollar = text.IndexOf('$');
        if (dollar < 0) return null;
        var comma = text.IndexOf(',', dollar);
        if (comma < 0) return null;
        var rumors = ParseNumberList(text[(comma + 1)..]);
        if (rumors.Any(number => number is < FirstRumorNumber or > LastRumorNumber)) return null;
        return (Atoi(text[(dollar + 1)..]), [.. rumors]);
    }

    private static RumorOffer? ToOffer((int Price, int[] Rumors)? parsed) => parsed is { } p ? new RumorOffer(p.Price, p.Rumors) : null;

    /// <summary>The <c>r:</c> reply was picked and its action has run: the rumor is drawn (ledger RMR3, RMR4).</summary>
    private void OpenRumor(OfferPlan plan)
    {
        var record = plan.Reply.Record;
        var source = new DialogOperationSource(new DialogNodeId(record.Id), record.SerializedIndex, DlgFieldKind.Text, record.Text.PayloadOffset, 0, 2, 0);
        _talk = new TrainerTalk(plan.Reply, plan.Target, source);
        var offer = plan.Reply.Rumor!;
        var candidates = new List<int>();
        foreach (var rumor in offer.Rumors)
        {
            bool open;
            try
            {
                // 0x418FC0 keeps a rumor that is not quelled (0x4C5650) and not known by the PC (0x4C58D0), in that order.
                open = !_ports.Rumors!.IsQuelled(rumor) && !_ports.Rumors.IsKnown(new DialogRumorQuery(_request.Participants.PlayerCharacter, rumor));
            }
            catch (Exception)
            {
                Fail(new DialogConversationFailure(DialogConversationFailureKind.QueryException, CurrentNode, source));
                return;
            }
            if (open) candidates.Add(rumor);
        }
        if (candidates.Count == 0)
        {
            PresentNotice(NoRumorGroup, classAnswer: false);
            return;
        }
        if (!DrawRumor(candidates.Count, source, out var index)) return;
        var chosen = candidates[index];
        if (offer.Price < 1)
        {
            Tell(chosen);
            return;
        }
        PresentRumorQuote(chosen, offer.Price);
    }

    // The candidate's index: 0x4CEF00 with equal bounds answers without drawing.
    private bool DrawRumor(int count, DialogOperationSource source, out int index)
    {
        index = 0;
        if (count == 1) return true;
        int? rolled;
        try
        {
            rolled = _ports.Greeting!.Roll(DialogGreetingRoll.Rumor, 0, count - 1);
        }
        catch (Exception)
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.QueryException, CurrentNode, source));
            return false;
        }
        if (rolled is not { } value || value < 0 || value >= count)
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.InvalidQueryResult, CurrentNode, source));
            return false;
        }
        index = value;
        return true;
    }

    /// <summary>The price screen (ledger RMR5): the same as the trainer's, with the reply's price on the reaction scale; yes pays and tells the drawn rumor.</summary>
    private void PresentRumorQuote(int rumor, int basePrice)
    {
        if (ReadReactionValue(_talk!.Source) is not { } reaction) return;
        var price = ReactionPrice(reaction, basePrice);
        if (!NpcTalkLine(QuoteGroup, price, out var line)) return;
        if (!PcTalkLine(1, 99, classLine: false, out var yes) || !PcTalkLine(100, 199, classLine: false, out var no)) return;
        Present(line, [yes with { Choice = new TrainerChoice(TrainerChoiceKind.PayRumor, Price: price, Rumor: rumor) }, no with { Choice = new TrainerChoice(TrainerChoiceKind.Follow) }]);
    }

    /// <summary>The payment, then the tale (ledger RMR6): too little own gold is the notice of line 2000, else the gold goes to the NPC and the rumor is told.
    /// The tale is built before the payment, so a failure there never follows a spent price (nothing between the payment and the telling can fail in the original).</summary>
    private void PayAndTell(TrainerChoice choice)
    {
        var talk = _talk!;
        if (!EnoughGold(choice.Price)) return;
        if (!PrepareTale(choice.Rumor, out var line, out var answer)) return;
        if ((long)_effects.Count + 2 > _options.MaxEffects)
        {
            Stop(new DialogConversationFrontier(DialogFrontierKind.EffectLimit, CurrentNode), null);
            return;
        }
        var pay = new GoldCommand(_request.Participants.PlayerCharacter, _request.Participants.Npc, -choice.Price);
        if (!RunTalkCommand(pay, () => _ports.Gold!.Execute(pay), talk.Source)) return;
        WriteAndShow(choice.Rumor, line, answer);
    }

    /// <summary>
    /// Telling the rumor without a price (ledger RMR7, RMR8, RMR9): the NPC's line is the rumor's text, the rumor is written to the PC's logbook (a known one
    /// stays as it is), and the only answer is the PC's class line 1000 with the reply's own response. The effect budget is checked before the write.
    /// </summary>
    private void Tell(int rumor)
    {
        if (!PrepareTale(rumor, out var line, out var answer)) return;
        if ((long)_effects.Count + 1 > _options.MaxEffects)
        {
            Stop(new DialogConversationFrontier(DialogFrontierKind.EffectLimit, CurrentNode), null);
            return;
        }
        WriteAndShow(rumor, line, answer);
    }

    // The tale's NPC line and the PC's answer, built without an effect.
    private bool PrepareTale(int rumor, out DialogNpcLine line, out TrainerOption answer)
    {
        line = new DialogNpcLine(CurrentNode, string.Empty);
        answer = new TrainerOption(string.Empty, null, default);
        if (!GenerateTalkLine((facts, sources, port) => DialogGreeting.RumorTale(rumor, facts, sources, port, _request.Names, _request.Encoding, _options.MaxTextBytes),
                out var text, out var origin))
        {
            return false;
        }
        line = new DialogNpcLine(CurrentNode, text, origin);
        return PcTalkLine(1000, 0, classLine: true, out answer);
    }

    private void WriteAndShow(int rumor, DialogNpcLine line, TrainerOption answer)
    {
        var learn = new RumorLogCommand(_request.Participants.PlayerCharacter, rumor);
        if (!RunTalkCommand(learn, () => _ports.Commands.Execute(learn), _talk!.Source)) return;
        Present(line, [answer with { Choice = new TrainerChoice(TrainerChoiceKind.Follow) }]);
    }
}
