using Arcanum.Data;

namespace Arcanum.Scripting.Dialog;

/// <summary>
/// ARC-SCRIPT-1533 (ledger DIR1–DIR9): the talk of a Player <c>d:</c> or <c>x:</c> reply, the original's directions to places. The reply text is
/// <c>d:$price,areas</c> (or <c>x:</c>): the price is the number after the first <c>$</c>, the areas the comma list after it (the list reader of
/// <see cref="ParseNumberList"/>). The reply is offered only while the PC does not know one of the areas (0x416C10 → 0x419E20 → 0x4CAF50, else it is
/// left out, and does not take a slot of the five). Picking it (states 0x12 and 0x15 of 0x414810, 0x419E70) replaces the NPC line and the replies by a page of
/// at most four of the unknown areas, each named by the area table (0x4CAE90), then the more option (PC lines 600..699, the next page) or, on the last
/// page, the way out (800..899); the NPC's own line is the range 100..199 (<c>x:</c>: 500..599). Picking an area (0x41A0F0, 0x41A230) tells the way at once for a
/// price below 1 (a follower of the PC is never charged), else asks the price first, the screen of the trainer's talk (0x418A40, 0x418B30). The way is the NPC's
/// line of the direction (0x4B8D50) from the NPC's location toward the area's and the distance (0x4B96F0) in units of 3168 tiles: <c>d:</c> (0x41A150) the
/// range 200 / 300 / 400 by the distance under 2 / under 100 / more, plus 10 per direction, the area stays unknown; <c>x:</c> (0x41A290) the range 600 (under 2) or 700
/// plus 10 per direction, with the distance in the line when it is 2 or more, and the PC learns the area (0x4CAFD0, the action <c>mm</c>). The PC's class line
/// 1000 is the only way on, with the reply's own response. The talk reuses the trainer's screens (<see cref="TrainerTalk"/>).
/// </summary>
public sealed partial class DialogConversation
{
    private const int DirectionsPageSize = 4, FirstDirectionsArea = 1, LastDirectionsArea = 4095;

    // 0xC60 tiles: the unit of the distance the lines are chosen by (0x41A150, 0x41A290).
    private const long DistanceUnit = 0xC60;

    private sealed record DirectionsOffer(bool Learn, int Price, int[] Visible, int[] Listed);

    private bool DirectionsAvailable(bool learn, int price) => _ports.Directions is not null && _ports.Areas is not null && _ports.Greeting is not null
        && (!learn || _ports.World is not null)
        && (price < 1 || _ports.World is not null && _ports.Gold is not null && (_ports.Purse ?? _ports.Trainer) is not null);

    /// <summary>
    /// The arguments of a <c>d:</c> or <c>x:</c> reply (ledger DIR1): the price is the number after the first <c>$</c> (read like <c>atoi</c>), the listed areas the list after
    /// the first comma at or after it (what the pick reads), the visible ones the list after the first comma of the whole text (what the offer
    /// reads; the same list for the installed replies, ledger DIR7). The reply is outside the subset without a <c>$</c> or a comma after it (the original then reads address 1)
    /// and when an area is outside the session's 1..4095 (the original reads its known-area table with no range test).
    /// </summary>
    internal static (int Price, int[] Visible, int[] Listed)? ParseDirectionsReply(string text)
    {
        var dollar = text.IndexOf('$');
        var first = text.IndexOf(',');
        if (dollar < 0 || first < 0) return null;
        var comma = text.IndexOf(',', dollar);
        if (comma < 0) return null;
        var visible = ParseNumberList(text[(first + 1)..]);
        var listed = ParseNumberList(text[(comma + 1)..]);
        if (visible.Concat(listed).Any(area => area is < FirstDirectionsArea or > LastDirectionsArea)) return null;
        return (Atoi(text[(dollar + 1)..]), [.. visible], [.. listed]);
    }

    private DirectionsOffer? ToDirections(bool learn, (int Price, int[] Visible, int[] Listed)? parsed) =>
        parsed is { } p && DirectionsAvailable(learn, p.Price) ? new DirectionsOffer(learn, p.Price, p.Visible, p.Listed) : null;

    /// <summary>
    /// Whether the offer leaves the reply out (ledger DIR1): every area of its list is already known to the PC (0x419E20 keeps none). Null when the conversation failed.
    /// </summary>
    [OriginalFunction(0x419E20, OriginalFunctionScope.Partial)]
    private bool? DirectionsHidden(DirectionsOffer offer)
    {
        foreach (var area in offer.Visible)
        {
            if (AreaKnown(area, null) is not { } known) return null;
            if (!known) return false;
        }
        return true;
    }

    private bool? AreaKnown(int area, DialogOperationSource? source)
    {
        try
        {
            return _ports.Areas!.Known(new DialogAreaQuery(_request.Participants.PlayerCharacter, area));
        }
        catch (Exception)
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.QueryException, CurrentNode, source));
            return null;
        }
    }

    /// <summary>The reply was picked and its action has run: the first page of the unknown areas (ledger DIR2).</summary>
    private void OpenDirections(OfferPlan plan)
    {
        var record = plan.Reply.Record;
        var source = new DialogOperationSource(new DialogNodeId(record.Id), record.SerializedIndex, DlgFieldKind.Text, record.Text.PayloadOffset, 0, 2, 0);
        _talk = new TrainerTalk(plan.Reply, plan.Target, source);
        PresentAreas(0);
    }

    /// <summary>
    /// A page of the list (ledger DIR2): the listed areas the PC does not know, from <paramref name="offset"/>, at most four; the NPC's line of the range 100..199 (500..599), then
    /// the more option while areas remain (its choice is the offset of the next page), else the way out. A follower of the PC asks no price.
    /// </summary>
    [OriginalFunction(0x419E70, OriginalFunctionScope.Partial)]
    private void PresentAreas(int offset)
    {
        var talk = _talk!;
        var offer = talk.Reply.Directions!;
        var unknown = new List<int>();
        foreach (var area in offer.Listed)
        {
            if (AreaKnown(area, talk.Source) is not { } known) return;
            if (!known) unknown.Add(area);
        }
        if (!NpcRangeTalkLine(offer.Learn ? 500 : 100, offer.Learn ? 599 : 199, out var line)) return;
        // 0x45DE00: the NPC's leader is the PC; the price the pick then keeps is none.
        if (!NpcLedByPc(out var led)) return;
        var price = led ? 0 : offer.Price;
        var options = new List<TrainerOption>();
        while (options.Count < DirectionsPageSize && offset + options.Count < unknown.Count)
        {
            var area = unknown[offset + options.Count];
            string? name;
            try
            {
                name = _ports.Directions!.AreaName(area);
            }
            catch (Exception)
            {
                Fail(new DialogConversationFailure(DialogConversationFailureKind.QueryException, CurrentNode, talk.Source));
                return;
            }
            if (name is null)
            {
                // The original reads a null name here and fails.
                Fail(new DialogConversationFailure(DialogConversationFailureKind.InvalidQueryResult, CurrentNode, talk.Source));
                return;
            }
            options.Add(new TrainerOption(name, null, new TrainerChoice(TrainerChoiceKind.Area, Area: area, Price: price)));
        }
        if (offset + options.Count < unknown.Count)
        {
            if (!PcTalkLine(600, 699, classLine: false, out var more)) return;
            options.Add(more with { Choice = new TrainerChoice(TrainerChoiceKind.More, Offset: offset + options.Count) });
        }
        else
        {
            if (!PcTalkLine(800, 899, classLine: false, out var back)) return;
            options.Add(back with { Choice = new TrainerChoice(TrainerChoiceKind.Follow) });
        }
        Present(line, options);
    }

    // 0x45DE00: the leader of the NPC is the PC, as the greeting port's facts say; unknown facts mean not led.
    private bool NpcLedByPc(out bool led)
    {
        led = false;
        try
        {
            led = _ports.Greeting!.Facts(new DialogReactionQuery(_request.Participants.Npc, _request.Participants.PlayerCharacter)) is { NpcLedByPc: true };
            return true;
        }
        catch (Exception)
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.QueryException, CurrentNode, _talk!.Source));
            return false;
        }
    }

    /// <summary>An area was picked (ledger DIR3): its way at once for a price below 1, else the price screen first.</summary>
    [OriginalFunction(0x41A0F0, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x41A230, OriginalFunctionScope.Partial)]
    private void PresentArea(int area, int price)
    {
        if (price < 1)
        {
            TellWay(area);
            return;
        }
        PresentPriceScreen(price, scaled => new TrainerChoice(TrainerChoiceKind.PayDirections, Area: area, Price: scaled));
    }

    /// <summary>The way without a price (ledger DIR4): built, the area learned for <c>x:</c>, shown.</summary>
    private void TellWay(int area)
    {
        if (!PrepareWay(area, out var line, out var answer)) return;
        if (!BudgetFor(WayEffects)) return;
        if (!LearnWay(area)) return;
        Present(line, [answer with { Choice = new TrainerChoice(TrainerChoiceKind.Follow) }]);
    }

    /// <summary>The payment, then the way (ledger DIR4): too little own gold is the notice of line 2000. The way is built before the payment, so a failure there never follows a spent price.</summary>
    private void PayAndTellWay(TrainerChoice choice)
    {
        var talk = _talk!;
        if (!EnoughGold(choice.Price)) return;
        if (!PrepareWay(choice.Area, out var line, out var answer)) return;
        if (!BudgetFor(1 + WayEffects)) return;
        var pay = new GoldCommand(_request.Participants.PlayerCharacter, _request.Participants.Npc, -choice.Price);
        if (!RunTalkCommand(pay, () => _ports.Gold!.Execute(pay), talk.Source)) return;
        if (!LearnWay(choice.Area)) return;
        Present(line, [answer with { Choice = new TrainerChoice(TrainerChoiceKind.Follow) }]);
    }

    // The effects the way itself writes: the area becomes known for x: only.
    private int WayEffects => _talk!.Reply.Directions!.Learn ? 1 : 0;

    private bool BudgetFor(int count)
    {
        if ((long)_effects.Count + count <= _options.MaxEffects) return true;
        Stop(new DialogConversationFrontier(DialogFrontierKind.EffectLimit, CurrentNode), null);
        return false;
    }

    // x: makes the area known to the PC (0x4CAFD0, the action mm); d: leaves it unknown.
    private bool LearnWay(int area)
    {
        var talk = _talk!;
        if (!talk.Reply.Directions!.Learn) return true;
        var mark = new MarkAreaCommand(_request.Participants.PlayerCharacter, area);
        return RunTalkCommand(mark, () => _ports.World!.Execute(mark), talk.Source);
    }

    /// <summary>
    /// The way's NPC line and the PC's answer (ledger DIR5, DIR6; 0x41A150, 0x41A290), built without an effect: the direction and distance from the NPC to the area (the port),
    /// the distance in units of 3168 tiles choosing the range, 10 lines per direction; <c>x:</c> prints the distance into the line when it is 2 or more.
    /// </summary>
    [OriginalFunction(0x41A150, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x41A290, OriginalFunctionScope.Partial)]
    private bool PrepareWay(int area, out DialogNpcLine line, out TrainerOption answer)
    {
        line = new DialogNpcLine(CurrentNode, string.Empty);
        answer = new TrainerOption(string.Empty, null, default);
        var talk = _talk!;
        var learn = talk.Reply.Directions!.Learn;
        DialogBearing? bearing;
        try
        {
            bearing = _ports.Directions!.Bearing(_request.Participants.Npc, area);
        }
        catch (Exception)
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.QueryException, CurrentNode, talk.Source));
            return false;
        }
        if (bearing is not { Rotation: >= 0 and <= 7, Distance: >= 0 } way)
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.InvalidQueryResult, CurrentNode, talk.Source));
            return false;
        }
        var units = way.Distance / DistanceUnit;
        var first = (learn ? (units < 2 ? 600 : 700) : units < 2 ? 200 : units < 100 ? 300 : 400) + way.Rotation * 10;
        if (!NpcRangeTalkLine(first, first + 9, out line)) return false;
        // x: the line is the format of the distance (sprintf, 0x589BD0) when it is 2 or more.
        if (learn && units >= 2) line = line with { Text = FormatPrice(line.Text, (int)Math.Min(units, int.MaxValue)) };
        if (!PcTalkLine(1000, 0, classLine: true, out answer)) return false;
        return true;
    }

    // The NPC's line of a plain range (0x418780) through the greeting port's facts, files and rolls.
    private bool NpcRangeTalkLine(int first, int last, out DialogNpcLine line)
    {
        line = new DialogNpcLine(CurrentNode, string.Empty);
        if (!GenerateTalkLine((facts, sources, port) =>
                DialogGreeting.NpcRange(first, last, facts, sources, port, _request.Names, _request.Encoding, _options.MaxTextBytes), out var text, out var origin))
        {
            return false;
        }
        line = new DialogNpcLine(CurrentNode, text, origin);
        return true;
    }
}
