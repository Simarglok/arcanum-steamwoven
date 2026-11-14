using Arcanum.Data;

namespace Arcanum.Scripting.Dialog;

/// <summary>
/// ARC-SCRIPT-1535 (ledger INS3): the NPC lines <c>m:$price,node</c> and <c>r:$price,list</c> (0x416840 → 0x418A40, 0x418FC0). After the node's action ran, an NPC
/// node whose text starts with <c>m:</c> opens the price screen of the trainer's talk (<see cref="PresentPriceScreen"/>): yes pays and follows the <c>node</c> number
/// like a reply's response, no follows the node's own response. A node whose text starts with <c>r:</c> runs the rumor draw (<see cref="OpenRumor"/>) with the
/// node's own response in the place of a reply's. Both return 0 in the original, so no reply of the node is built; the screens take over the node as the
/// Player <c>t:</c> and <c>r:</c> replies do (<see cref="TrainerTalk"/>). A follow that is itself an <c>m:</c> or <c>r:</c> node is planned when the way is taken
/// (ARC-SCRIPT-1746), so chains and cycles run and the turn limit bounds them; every other follow is planned with the node.
/// </summary>
public sealed partial class DialogConversation
{
    // Where a screen's way out leads: a node (planned ahead, so a pick never half-runs), a script line or the end of the talk (0x417590: positive, negative, zero).
    // A target that is itself an m: or r: node is planned lazily (ARC-SCRIPT-1746): Target stays null and Pending names the node, planned when the way is taken.
    private sealed record FollowPlan(DialogOptionTransition Transition, StepPlan? Target, DialogScriptLine? Line, DlgRecord? Pending = null);

    // The numbers of an NPC marker line after parsing: m: the price and the yes node, r: the price and the rumor list.
    private sealed record NpcTalkSource(char Letter, int Price, int Node, int[] Rumors);

    /// <summary>A planned <c>m:</c> or <c>r:</c> node: the node's own response (no, and the way on after a rumor) and, for <c>m:</c>, the yes follow.</summary>
    private sealed record NpcTalkPlan(NpcTalkSource Source, FollowPlan Own, FollowPlan? Yes);

    private bool PriceTalkAvailable => _ports.Greeting is not null && _ports.World is not null && _ports.Gold is not null && (_ports.Purse ?? _ports.Trainer) is not null;

    /// <summary>
    /// The numbers of an NPC <c>m:</c> text (ledger INS3): the price is the number after the first <c>$</c>, the node the number after the first comma from there
    /// (both read like <c>atoi</c>). Without a <c>$</c> or a comma after it the original reads address 1, and a negative price would move gold the other way: both
    /// are outside the subset.
    /// </summary>
    internal static (int Price, int Node)? ParseNpcPriceLine(string text)
    {
        var dollar = text.IndexOf('$');
        if (dollar < 0) return null;
        var comma = text.IndexOf(',', dollar + 1);
        if (comma < 0) return null;
        var price = Atoi(text[(dollar + 1)..]);
        return price < 0 ? null : (price, Atoi(text[(comma + 1)..]));
    }

    // The marker line as the ports of this conversation can run it: null leaves the node a frontier (no ports, or arguments outside the subset).
    private NpcTalkSource? ReadNpcTalk(char letter, IReadOnlyList<byte> raw)
    {
        // Only ASCII matters to the readers (the digits, `$`, `,` and `-`); any other byte stands for a character that is none of them.
        var text = string.Create(raw.Count, raw, static (span, bytes) =>
        {
            for (var at = 0; at < span.Length; at++) span[at] = bytes[at] < 0x80 ? (char)bytes[at] : '?';
        });
        if (letter == 'm')
        {
            return PriceTalkAvailable && ParseNpcPriceLine(text) is { } priced ? new NpcTalkSource('m', priced.Price, priced.Node, []) : null;
        }
        return ToOffer(ParseRumorReply(text)) is { } offer && RumorAvailable(offer) ? new NpcTalkSource('r', offer.Price, 0, offer.Rumors) : null;
    }

    private NpcTalkPlan? PlanNpcTalk(DlgRecord npc, NpcTalkSource source, DialogNodeId node, out DialogConversationFrontier? frontier, out DialogConversationFailure? failure)
    {
        frontier = null;
        var own = PlanFollow(npc.ResponseValue ?? 0, node, out frontier, out failure);
        if (own is null) return null;
        if (source.Letter != 'm') return new NpcTalkPlan(source, own, null);
        var yes = PlanFollow(source.Node, node, out frontier, out failure);
        return yes is null ? null : new NpcTalkPlan(source, own, yes);
    }

    // The follow of a response value (0x417590): the node numbered so (an NPC node), the script line of its negation, or the end. A node that is not an m: or r: line is planned now; an m: or r: line is planned when the way is taken.
    private FollowPlan? PlanFollow(int value, DialogNodeId node, out DialogConversationFrontier? frontier, out DialogConversationFailure? failure)
    {
        frontier = null;
        failure = null;
        if (value == 0) return new FollowPlan(DialogOptionTransition.Exit, null, null);
        if (value < 0)
        {
            if (value == int.MinValue)
            {
                failure = new DialogConversationFailure(DialogConversationFailureKind.InvalidOperation, node);
                return null;
            }
            return new FollowPlan(DialogOptionTransition.ScriptLine, null, new DialogScriptLine(-value));
        }
        if (!_document.TryGetRecord(value, out var target) || target is null)
        {
            failure = new DialogConversationFailure(DialogConversationFailureKind.MissingTarget, node);
            return null;
        }
        if (target.Role != DlgRecordRole.Npc)
        {
            failure = new DialogConversationFailure(DialogConversationFailureKind.WrongTargetRole, node);
            return null;
        }
        if (IsTalkNode(target)) return new FollowPlan(DialogOptionTransition.Direct, null, null, target);
        var plan = PlanStep(target, out frontier, out failure);
        return plan is null ? null : new FollowPlan(DialogOptionTransition.Direct, plan, null);
    }

    // An NPC node whose text is an m: or r: line these ports can run (the same test PlanStep makes): its plan would plan its own follows in turn, so it is left to be planned when entered.
    private bool IsTalkNode(DlgRecord target)
    {
        var raw = target.Text.Bytes;
        var markerText = raw.Count >= 2 && raw[0] < 0x80 && raw[1] == (byte)':' ? $"{(char)raw[0]}:{(raw.Count > 2 ? "+" : string.Empty)}" : string.Empty;
        return DialogText.Marker(markerText, out _) is { Letter: 'm' or 'r' } marker && ReadNpcTalk(marker.Letter, raw) is not null;
    }

    // The step a planned follow enters: planned now, or (an m: or r: node) planned here; null after the conversation stopped at what the node cannot run.
    private StepPlan? ResolveTarget(StepPlan? target, DlgRecord? pending)
    {
        if (target is not null || pending is null) return target;
        var plan = PlanStep(pending, out var frontier, out var failure);
        if (plan is null) Stop(frontier, failure);
        return plan;
    }

    /// <summary>The node's action ran and its text is a planned <c>m:</c> or <c>r:</c> line: the screen replaces the node's line and replies (ledger INS3).</summary>
    private void OpenNpcTalk(StepPlan plan, NpcTalkPlan talk)
    {
        var record = plan.Npc;
        var source = new DialogOperationSource(new DialogNodeId(record.Id), record.SerializedIndex, DlgFieldKind.Text, record.Text.PayloadOffset, 0, 2, 0);
        // The node's own response stands where a reply's response stands for the screens (the no answer, the notices and the way on after a rumor).
        var reply = new ReplyPlan(record, 0, 0, [], DialogOptionTextKind.Generated, string.Empty, new DialogGeneratedMarker(talk.Source.Letter),
            talk.Own.Transition, talk.Own.Target?.Npc ?? talk.Own.Pending, talk.Own.Line, null, null, null, Rumor: talk.Source.Letter == 'r' ? new RumorOffer(talk.Source.Price, talk.Source.Rumors) : null);
        _talk = new TrainerTalk(reply, talk.Own.Target, source, talk.Yes);
        if (talk.Source.Letter == 'r')
        {
            OpenRumor(new OfferPlan(reply, talk.Own.Target));
            return;
        }
        PresentPriceScreen(talk.Source.Price, price => new TrainerChoice(TrainerChoiceKind.PayFollow, Price: price));
    }

    /// <summary>Yes on the price screen of an <c>m:</c> node (ledger INS3, DTR6): the PC's own gold must cover the price (else the notice of line 2000), then the gold goes to the NPC and the node's yes follow runs.</summary>
    private void PayAndFollow(TrainerChoice choice)
    {
        var talk = _talk!;
        var yes = talk.Yes!;
        if (!EnoughGold(choice.Price)) return;
        // The budgets are checked before the payment, so a limit never stops after a spent price; so is the plan of an m: or r: node that follows.
        var target = ResolveTarget(yes.Target, yes.Pending);
        if (target is null && yes.Pending is not null) return;
        if (!Admit(target, choice.Price > 0 ? 1 : 0)) return;
        if (choice.Price > 0)
        {
            var pay = new GoldCommand(_request.Participants.PlayerCharacter, _request.Participants.Npc, -choice.Price);
            if (!RunTalkCommand(pay, () => _ports.Gold!.Execute(pay), talk.Source)) return;
        }
        Follow(yes.Transition, target, yes.Line);
    }
}
