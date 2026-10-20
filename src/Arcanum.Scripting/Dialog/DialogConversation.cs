using Arcanum.Data;
using Arcanum.Scripting.GlobalFlags;

namespace Arcanum.Scripting.Dialog;

/// <summary>
/// ARC-SCRIPT-005 multi-turn NPC conversation over one immutable <see cref="DlgDocument"/>: NPC entry action,
/// eligible replies, selection, reply action, then continue, exit to SCR or stop at a typed frontier.
/// Synchronous and not thread-safe; the host serializes calls and every externally mutated owner.
/// </summary>
public sealed class DialogConversation
{
    private const int LowIntelligence = 4;
    private const int MaxEligible = 5;

    /// <summary>ARC-WORLD-043: the reaction change of an insult (a Player <c>i:</c> reply), 0x4191E0.</summary>
    public const int InsultReaction = -10;

    /// <summary>ARC-WORLD-043: an insult ends the talk when the NPC's effective reaction is then at most this value.</summary>
    public const int InsultEndsAtOrBelow = 20;

    private readonly DlgDocument _document;
    private readonly DialogConversationRequest _request;
    private readonly DialogConversationPorts _ports;
    private readonly DialogConversationOptions _options;
    private readonly DlgRecord[] _sorted;
    private readonly Guid _identity = Guid.NewGuid();
    private readonly List<DialogEffectReceipt> _effects = [];
    private StepPlan? _step;
    private IReadOnlyList<DialogOption> _offers = Array.Empty<DialogOption>();
    private Dictionary<int, OfferPlan> _offerPlans = new();
    private int _generation;

    private DialogConversation(DlgDocument document, DialogConversationRequest request, DialogConversationPorts ports, DialogConversationOptions options)
    {
        _document = document;
        _request = request;
        _ports = ports;
        _options = options;
        _sorted = document.Records.OrderBy(record => record.Id).ToArray();
        CurrentNode = request.Entry;
    }

    public DialogConversationPhase Phase { get; private set; } = DialogConversationPhase.AwaitingSelection;
    public int Turn { get; private set; }
    public DialogNodeId CurrentNode { get; private set; }
    public DialogNpcLine? NpcLine { get; private set; }
    public IReadOnlyList<DialogOption> Options => _offers;
    public IReadOnlyList<DialogEffectReceipt> Effects => _effects.AsReadOnly();
    public DialogConversationEnd? End { get; private set; }
    public DialogConversationFrontier? Frontier { get; private set; }
    public DialogConversationFailure? Failure { get; private set; }

    public static DialogConversation Start(
        DlgDocument document,
        DialogConversationRequest request,
        DialogConversationPorts ports,
        DialogConversationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Participants);
        ArgumentNullException.ThrowIfNull(request.Pc);
        ArgumentNullException.ThrowIfNull(request.Names);
        ArgumentNullException.ThrowIfNull(request.Names.PcName);
        ArgumentNullException.ThrowIfNull(request.Names.NpcName);
        ArgumentNullException.ThrowIfNull(ports);
        ArgumentNullException.ThrowIfNull(ports.LocalFlags);
        ArgumentNullException.ThrowIfNull(ports.Commands);
        if (!Enum.IsDefined(request.Pc.Gender)) throw new ArgumentOutOfRangeException(nameof(request), "Unknown PC gender.");
        if (!Enum.IsDefined(request.Encoding)) throw new ArgumentOutOfRangeException(nameof(request), "Unknown text encoding.");
        if (request.CallerLine.Value is < 0 or int.MaxValue) throw new ArgumentOutOfRangeException(nameof(request), "Caller SCR line must allow a next line.");
        var effective = options ?? new DialogConversationOptions();
        effective.Validate();

        var conversation = new DialogConversation(document, request, ports, effective);
        if (!document.TryGetRecord(request.Entry.Value, out var entry) || entry is null)
        {
            conversation.Fail(new DialogConversationFailure(DialogConversationFailureKind.InvalidEntry, request.Entry));
            return conversation;
        }
        if (entry.Role != DlgRecordRole.Npc)
        {
            conversation.Fail(new DialogConversationFailure(DialogConversationFailureKind.WrongEntryRole, request.Entry));
            return conversation;
        }
        var plan = conversation.PlanStep(entry, out var frontier, out var failure);
        if (plan is null)
        {
            conversation.Stop(frontier, failure);
            return conversation;
        }
        if (conversation.Admit(plan, 0))
        {
            conversation.Enter(plan);
        }
        return conversation;
    }

    /// <summary>Re-evaluates the current reply gates without effects. Old tokens become NotOffered.</summary>
    public DialogConversationStepResult RefreshOptions()
    {
        if (Phase != DialogConversationPhase.AwaitingSelection)
        {
            return new(DialogConversationSelectStatus.WrongPhase);
        }
        _generation++;
        BuildOffers(_step!);
        return Result();
    }

    public DialogConversationStepResult Select(DialogOptionToken token)
    {
        if (Phase != DialogConversationPhase.AwaitingSelection)
        {
            return new(DialogConversationSelectStatus.WrongPhase);
        }
        var offer = _offers.FirstOrDefault(current => current.Token == token);
        if (offer is null)
        {
            return new(DialogConversationSelectStatus.NotOffered);
        }
        if (offer.Block is not null)
        {
            return new(DialogConversationSelectStatus.UnsupportedTransition);
        }
        OfferPlan? plan = null;
        if (offer.TextKind != DialogOptionTextKind.Fallback)
        {
            plan = _offerPlans[offer.ResponseId.Value];
            // Gates are re-read immediately before effects; a changed gate rebuilds the offers instead.
            var open = IsEligible(plan.Reply);
            if (open is null)
            {
                return Result();
            }
            if (open == false)
            {
                _generation++;
                BuildOffers(_step!);
                return Phase == DialogConversationPhase.AwaitingSelection ? new(DialogConversationSelectStatus.StaleSelection) : Result();
            }
            var pending = plan.Reply.Action!.Count + (plan.Reply.Insult is null ? 0 : 1);
            if (!Admit(plan.Target, pending))
            {
                return Result();
            }
        }

        // The token is consumed before the first effect; nothing below can offer it again.
        _offers = Array.Empty<DialogOption>();
        if (plan is not null && !Execute(plan.Reply.Action!))
        {
            return Result();
        }
        // The insult rule runs after the reply's own action and before its transition (0x413130, 0x414810).
        if (plan?.Reply.Insult is { } insult && !Insult(insult))
        {
            return Result();
        }
        switch (offer.Transition)
        {
            case DialogOptionTransition.Direct:
            case DialogOptionTransition.Join:
                Enter(plan!.Target!);
                return Result();
            case DialogOptionTransition.FinalLine:
                Turn++;
                CurrentNode = new DialogNodeId(plan!.Target!.Npc.Id);
                NpcLine = new DialogNpcLine(CurrentNode, plan.Target.NpcText);
                if (Execute(plan.Target.Action))
                {
                    Finish(new DialogConversationEnd(DialogEndKind.NpcFinalLine, null));
                }
                return Result();
            case DialogOptionTransition.Exit:
                Finish(new DialogConversationEnd(DialogEndKind.ReturnToScript, new DialogScriptLine(_request.CallerLine.Value + 1)));
                return Result();
            default:
                Finish(new DialogConversationEnd(DialogEndKind.ReturnToScript, offer.ScriptLine));
                return Result();
        }
    }

    /// <summary>Host-initiated close of an awaiting dialog. No reply effect runs and no SCR continuation is implied.</summary>
    public bool Close()
    {
        if (Phase != DialogConversationPhase.AwaitingSelection)
        {
            return false;
        }
        Finish(new DialogConversationEnd(DialogEndKind.ClosedByHost, null));
        return true;
    }

    private DialogConversationStepResult Result() => new(Phase switch
    {
        DialogConversationPhase.AwaitingSelection => DialogConversationSelectStatus.Continued,
        DialogConversationPhase.Ended => DialogConversationSelectStatus.Ended,
        DialogConversationPhase.AtFrontier => DialogConversationSelectStatus.AtFrontier,
        _ => DialogConversationSelectStatus.Failed,
    });

    /// <summary>Turn and effect budgets for entering <paramref name="next"/> after <paramref name="pending"/> reply effects.</summary>
    private bool Admit(StepPlan? next, int pending)
    {
        var node = next is null ? CurrentNode : new DialogNodeId(next.Npc.Id);
        if (next is not null && Turn >= _options.MaxTurns)
        {
            Stop(new DialogConversationFrontier(DialogFrontierKind.TurnLimit, node), null);
            return false;
        }
        if ((long)_effects.Count + pending + (next?.Action.Count ?? 0) > _options.MaxEffects)
        {
            Stop(new DialogConversationFrontier(DialogFrontierKind.EffectLimit, node), null);
            return false;
        }
        return true;
    }

    private void Enter(StepPlan plan)
    {
        Turn++;
        _step = plan;
        CurrentNode = new DialogNodeId(plan.Npc.Id);
        NpcLine = new DialogNpcLine(CurrentNode, plan.NpcText);
        if (Execute(plan.Action))
        {
            BuildOffers(plan);
        }
    }

    /// <summary>Source order with the source cap: stop after five eligible replies; later rows are not queried.</summary>
    private void BuildOffers(StepPlan step)
    {
        var offers = new List<DialogOption>();
        var plans = new Dictionary<int, OfferPlan>();
        foreach (var reply in step.Replies)
        {
            if (offers.Count == MaxEligible)
            {
                break;
            }
            var open = IsEligible(reply);
            if (open is null)
            {
                return;
            }
            if (open == false)
            {
                continue;
            }
            var block = reply.Block;
            StepPlan? target = null;
            if (block is null && reply.Target is not null)
            {
                // One level of pure look-ahead: a reply whose next step cannot run is shown blocked, never half-run.
                target = PlanStep(reply.Target, out var targetFrontier, out var targetFailure, reply.Transition != DialogOptionTransition.FinalLine);
                if (target is null)
                {
                    block = new DialogOptionBlock(
                        targetFailure is null ? DialogOptionBlockKind.TargetUnsupported : DialogOptionBlockKind.TargetInvalid,
                        targetFrontier?.Source ?? targetFailure?.Source);
                }
            }
            var response = new DialogResponseId(reply.Record.Id);
            plans.Add(reply.Record.Id, new OfferPlan(reply, target));
            offers.Add(new DialogOption(
                offers.Count + 1,
                response,
                reply.Record.SerializedIndex,
                reply.TextKind,
                reply.Text,
                reply.Marker,
                reply.Transition,
                reply.Target is null ? null : new DialogNodeId(reply.Target.Id),
                reply.ScriptLine,
                new DialogOptionToken(_identity, Turn, _generation, response),
                block));
        }
        if (offers.Count == 0)
        {
            var response = new DialogResponseId(0);
            offers.Add(new DialogOption(1, response, -1, DialogOptionTextKind.Fallback, string.Empty, null,
                DialogOptionTransition.Exit, null, null, new DialogOptionToken(_identity, Turn, _generation, response), null));
        }
        _offers = offers.AsReadOnly();
        _offerPlans = plans;
    }

    /// <summary>Gender, then effective IQ, then the condition conjunction in order. Null means the session stopped.</summary>
    private bool? IsEligible(ReplyPlan reply)
    {
        if (reply.Gender >= 0 && reply.Gender != (int)_request.Pc.Gender)
        {
            return false;
        }
        var intelligence = _request.Pc.Intelligence > LowIntelligence && _request.Pc.IsDumb ? 1 : _request.Pc.Intelligence;
        var passes = reply.Iq < 0 ? intelligence <= -(long)reply.Iq : intelligence >= reply.Iq;
        if (!passes)
        {
            return false;
        }
        foreach (var operation in reply.Condition)
        {
            var result = operation switch
            {
                ConversationLocalFlag local => ReadLocal(local),
                ConversationGlobalFlag global => ReadGlobal(global),
                ConversationReactionGate reaction => ReadReaction(reaction),
                ConversationWaiting waiting => ReadWaiting(waiting),
                ConversationMetBefore met => ReadMetBefore(met),
                // RA n>0: the PC race is n-1; RA n<=0: the PC race is not -n-1.
                ConversationRaceGate race => race.Selector > 0
                    ? _request.Pc.Race + 1L == race.Selector
                    : _request.Pc.Race + 1L != -(long)race.Selector,
                _ => throw new InvalidOperationException("Unadmitted condition."),
            };
            if (result != true)
            {
                return result;
            }
        }
        return true;
    }

    private bool? ReadLocal(ConversationLocalFlag operation)
    {
        LocalFlagReadResult result;
        try
        {
            result = _ports.LocalFlags.Read(new LocalFlagQuery(_request.Participants.Dialog, _request.Participants.Npc, operation.Index));
        }
        catch (Exception)
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.QueryException, CurrentNode, operation.Source));
            return null;
        }
        if (!result.IsValidFactoryValue ||
            result.Status is not (LocalFlagReadStatus.Found or LocalFlagReadStatus.Missing) ||
            (result.Status == LocalFlagReadStatus.Found && result.Value is not (0 or 1)) ||
            (result.Status == LocalFlagReadStatus.Missing && result.Value != 0))
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.InvalidQueryResult, CurrentNode, operation.Source));
            return null;
        }
        return result.Status == LocalFlagReadStatus.Found && result.Value == operation.Value;
    }

    private bool? ReadGlobal(ConversationGlobalFlag operation)
    {
        var owner = _ports.GlobalFlags!;
        var read = owner.Read(new GlobalFlagReadRequest(new GlobalFlagRequestId(Guid.NewGuid()), owner.Version, new FlagId(operation.Flag)));
        switch (read.Status)
        {
            case GlobalFlagReadStatus.Known:
                return read.Value == operation.Value;
            case GlobalFlagReadStatus.Unknown:
            case GlobalFlagReadStatus.Unavailable:
                Stop(new DialogConversationFrontier(DialogFrontierKind.GlobalFlagUnknown, CurrentNode, operation.Source), null);
                return null;
            default:
                Fail(new DialogConversationFailure(DialogConversationFailureKind.GlobalFlagFailed, CurrentNode, operation.Source, GlobalFlagReason: read.Reason));
                return null;
        }
    }

    /// <summary>RE t>=0: reaction at least t; RE t&lt;0: reaction at most -t (both inclusive).</summary>
    private bool? ReadReaction(ConversationReactionGate operation) =>
        ReadReactionValue(operation.Source) is not { } value ? null
            : operation.Threshold < 0 ? value <= -(long)operation.Threshold : value >= operation.Threshold;

    /// <summary>The NPC's effective reaction to the PC; null when the session stopped (unknown is a frontier).</summary>
    private int? ReadReactionValue(DialogOperationSource source)
    {
        DialogReactionReadResult result;
        try
        {
            result = _ports.World!.ReadReaction(new DialogReactionQuery(_request.Participants.Npc, _request.Participants.PlayerCharacter));
        }
        catch (Exception)
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.QueryException, CurrentNode, source));
            return null;
        }
        if (!result.Known)
        {
            Stop(new DialogConversationFrontier(DialogFrontierKind.ReactionUnknown, CurrentNode, source), null);
            return null;
        }
        return result.Value;
    }

    /// <summary>
    /// ME 1: the NPC has met the PC before; ME 0: it has not. The original's condition (0x4150D0) asks the met-before
    /// function (0x4C0C40), which leaves out the conversation in progress.
    /// </summary>
    [OriginalFunction(0x4150D0, OriginalFunctionScope.Partial)]
    private bool? ReadMetBefore(ConversationMetBefore operation)
    {
        bool met;
        try
        {
            met = _ports.MetBefore!.MetBefore(new DialogReactionQuery(_request.Participants.Npc, _request.Participants.PlayerCharacter));
        }
        catch (Exception)
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.QueryException, CurrentNode, operation.Source));
            return null;
        }
        return met == operation.Value;
    }

    /// <summary>
    /// The insult of a picked Player <c>i:</c> reply (0x4191E0): the NPC's reaction changes by <see cref="InsultReaction"/>
    /// through the reaction port (the PC's adjustment effects and skips are the owner's), then the effective reaction is read;
    /// at most <see cref="InsultEndsAtOrBelow"/> the NPC ends the talk with a generated line (not loaded,
    /// <see cref="DialogEndKind.Insulted"/>), otherwise the reply's own transition follows. False when the talk ended or stopped.
    /// </summary>
    [OriginalFunction(0x4191E0, OriginalFunctionScope.Partial)]
    private bool Insult(DialogOperationSource source)
    {
        if (!Execute([new ConversationReaction(source, DialogReactionMode.Adjust, InsultReaction)]) || ReadReactionValue(source) is not { } value)
        {
            return false;
        }
        if (value <= InsultEndsAtOrBelow)
        {
            Finish(new DialogConversationEnd(DialogEndKind.Insulted, null));
            return false;
        }
        return true;
    }

    /// <summary>WA 1: the NPC is waiting for the PC; WA 0: it is not.</summary>
    private bool? ReadWaiting(ConversationWaiting operation)
    {
        bool waiting;
        try
        {
            waiting = _ports.Party!.IsWaiting(new DialogPartyQuery(_request.Participants.Npc, _request.Participants.PlayerCharacter));
        }
        catch (Exception)
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.QueryException, CurrentNode, operation.Source));
            return null;
        }
        return waiting == operation.Value;
    }

    private bool Execute(IReadOnlyList<ConversationOperation> operations)
    {
        foreach (var operation in operations)
        {
            if (operation is ConversationFinalLine)
            {
                // Control transfer, not a command: the caller enters the final line after this field.
                continue;
            }
            if (operation is ConversationJoin or ConversationUnwait)
            {
                DialogCommand join = operation is ConversationJoin
                    ? new JoinCommand(_request.Participants.Npc, _request.Participants.PlayerCharacter)
                    : new UnwaitCommand(_request.Participants.Npc, _request.Participants.PlayerCharacter);
                DialogJoinResult joined;
                try
                {
                    // A party port takes JO in place of the world port.
                    joined = join switch
                    {
                        JoinCommand command when _ports.Party is { } party => party.Execute(command),
                        JoinCommand command => _ports.World!.Execute(command),
                        _ => _ports.Party!.Execute((UnwaitCommand)join),
                    };
                }
                catch (Exception)
                {
                    Record(join, operation.Source, DialogCommandAttemptOutcome.Indeterminate);
                    Fail(new DialogConversationFailure(DialogConversationFailureKind.CommandIndeterminate, CurrentNode, operation.Source));
                    return false;
                }
                if (joined == DialogJoinResult.Accepted)
                {
                    Record(join, operation.Source, DialogCommandAttemptOutcome.Applied);
                    continue;
                }
                Record(join, operation.Source, DialogCommandAttemptOutcome.Rejected);
                if (joined == DialogJoinResult.Refused)
                {
                    Stop(new DialogConversationFrontier(DialogFrontierKind.JoinRefused, CurrentNode, operation.Source), null);
                }
                else
                {
                    Fail(new DialogConversationFailure(DialogConversationFailureKind.CommandRejected, CurrentNode, operation.Source));
                }
                return false;
            }
            if (operation is ConversationGlobalFlag global)
            {
                var owner = _ports.GlobalFlags!;
                var receipt = owner.Write(new GlobalFlagWriteRequest(new GlobalFlagRequestId(Guid.NewGuid()), owner.Version, new FlagId(global.Flag), global.Value));
                var command = new GlobalFlagWriteCommand(global.Flag, global.Value);
                if (receipt.Disposition is GlobalFlagWriteDisposition.Applied or GlobalFlagWriteDisposition.NoChange)
                {
                    Record(command, operation.Source, DialogCommandAttemptOutcome.Applied, receipt);
                    continue;
                }
                var indeterminate = receipt.Disposition == GlobalFlagWriteDisposition.Indeterminate;
                Record(command, operation.Source, indeterminate ? DialogCommandAttemptOutcome.Indeterminate : DialogCommandAttemptOutcome.Rejected, receipt);
                Fail(new DialogConversationFailure(
                    indeterminate ? DialogConversationFailureKind.CommandIndeterminate : DialogConversationFailureKind.CommandRejected,
                    CurrentNode, operation.Source, GlobalFlagReason: receipt.Reason));
                return false;
            }

            DialogCommand typed = operation switch
            {
                ConversationLocalFlag local => new LocalFlagWriteCommand(_request.Participants.Dialog, _request.Participants.Npc, local.Index, local.Value),
                ConversationQuest quest => new QuestStateCommand(_request.Participants.PlayerCharacter, quest.QuestId, quest.State, _request.Participants.Npc),
                ConversationRumor rumor => new RumorLogCommand(_request.Participants.PlayerCharacter, rumor.RumorId),
                ConversationQuellRumor quell => new QuellRumorCommand(quell.RumorId),
                ConversationReaction reaction => new ReactionCommand(_request.Participants.Npc, _request.Participants.PlayerCharacter, reaction.Mode, reaction.Value),
                ConversationGiveItem item => new GiveItemCommand(_request.Participants.Npc, _request.Participants.PlayerCharacter, item.ItemName),
                ConversationMarkArea area => new MarkAreaCommand(_request.Participants.PlayerCharacter, area.Area),
                ConversationAlignment alignment => new AlignmentCommand(_request.Participants.PlayerCharacter, alignment.Mode, alignment.Value),
                _ => throw new InvalidOperationException("Unadmitted action."),
            };
            DialogCommandResult result;
            try
            {
                result = typed switch
                {
                    LocalFlagWriteCommand local => _ports.Commands.Execute(local),
                    QuestStateCommand quest => _ports.Commands.Execute(quest),
                    RumorLogCommand rumor => _ports.Commands.Execute(rumor),
                    QuellRumorCommand quell => _ports.Rumors!.Execute(quell),
                    ReactionCommand reaction => _ports.World!.Execute(reaction),
                    GiveItemCommand item => _ports.Inventory is { } inventory ? inventory.Execute(item) : _ports.World!.Execute(item),
                    MarkAreaCommand area => _ports.World!.Execute(area),
                    AlignmentCommand alignment => _ports.World!.Execute(alignment),
                    _ => throw new InvalidOperationException("Unadmitted command."),
                };
            }
            catch (Exception)
            {
                Record(typed, operation.Source, DialogCommandAttemptOutcome.Indeterminate);
                Fail(new DialogConversationFailure(DialogConversationFailureKind.CommandIndeterminate, CurrentNode, operation.Source));
                return false;
            }
            if (result != DialogCommandResult.Applied)
            {
                Record(typed, operation.Source, DialogCommandAttemptOutcome.Rejected);
                Fail(new DialogConversationFailure(DialogConversationFailureKind.CommandRejected, CurrentNode, operation.Source));
                return false;
            }
            Record(typed, operation.Source, DialogCommandAttemptOutcome.Applied);
        }
        return true;
    }

    private void Record(DialogCommand command, DialogOperationSource source, DialogCommandAttemptOutcome outcome, GlobalFlagWriteReceipt? receipt = null) =>
        _effects.Add(new DialogEffectReceipt(_effects.Count, Turn, command, source, outcome, receipt));

    private void Finish(DialogConversationEnd end)
    {
        End = end;
        Phase = DialogConversationPhase.Ended;
        _offers = Array.Empty<DialogOption>();
    }

    private void Stop(DialogConversationFrontier? frontier, DialogConversationFailure? failure)
    {
        if (failure is not null)
        {
            Fail(failure);
            return;
        }
        Frontier = frontier;
        Phase = DialogConversationPhase.AtFrontier;
        _offers = Array.Empty<DialogOption>();
    }

    private void Fail(DialogConversationFailure failure)
    {
        Failure = failure;
        Phase = DialogConversationPhase.Failed;
        _offers = Array.Empty<DialogOption>();
    }

    // ---- Static planning: pure, no port calls, run before any effect of the step it admits. ----

    private StepPlan? PlanStep(DlgRecord npc, out DialogConversationFrontier? frontier, out DialogConversationFailure? failure, bool withReplies = true)
    {
        frontier = null;
        failure = null;
        var node = new DialogNodeId(npc.Id);
        try
        {
            var action = DialogConversationGrammar.Parse(npc, npc.Action, ConversationField.NpcAction, _options, _ports.Party is not null);
            if (MissingCapability(action) is { } missing)
            {
                frontier = new DialogConversationFrontier(DialogFrontierKind.CapabilityUnavailable, node, missing);
                return null;
            }
            // The NPC line addresses the PC: the alternate field is its female-PC variant.
            var bytes = _request.Pc.Gender == DialogPcGender.Male ? npc.Text : npc.AlternateText;
            var textFailure = DialogText.Decode(bytes.Bytes, _request.Encoding, _options.MaxTextBytes, out var text);
            if (textFailure != DialogTextFailureKind.None)
            {
                failure = new DialogConversationFailure(DialogConversationFailureKind.InvalidText, node, TextFailure: textFailure);
                return null;
            }
            // The source tests NPC markers on the record text whatever the PC gender. A marker is ASCII,
            // so the raw prefix decides it even when a later byte of that text would not decode.
            var raw = npc.Text.Bytes;
            var markerText = raw.Count >= 2 && raw[0] < 0x80 && raw[1] == (byte)':'
                ? $"{(char)raw[0]}:{(raw.Count > 2 ? "+" : string.Empty)}" : string.Empty;
            var marker = DialogText.Marker(markerText, out var exact);
            if (marker is { Letter: 'g' or 'm' or 'r' } || marker is { Letter: 'i' } && exact)
            {
                frontier = new DialogConversationFrontier(DialogFrontierKind.UnsupportedGeneratedText, node);
                return null;
            }
            if (!CheckTokens(text, node, null, out frontier, out failure))
            {
                return null;
            }

            if (!withReplies)
            {
                return new StepPlan(npc, action, DialogText.Substitute(text, _request.Names), Array.Empty<ReplyPlan>());
            }
            var group = Group(npc, out var orderMatches);
            if (group.Count > _options.MaxStructuralReplies)
            {
                failure = new DialogConversationFailure(DialogConversationFailureKind.StructuralReplyLimit, node);
                return null;
            }
            if (!orderMatches)
            {
                frontier = new DialogConversationFrontier(DialogFrontierKind.GroupOrderMismatch, node);
                return null;
            }
            var replies = new List<ReplyPlan>(group.Count);
            foreach (var record in group)
            {
                var reply = PlanReply(record, node, out frontier, out failure);
                if (reply is null)
                {
                    return null;
                }
                replies.Add(reply);
            }
            return new StepPlan(npc, action, DialogText.Substitute(text, _request.Names), replies.AsReadOnly());
        }
        catch (DialogOperationParseException exception)
        {
            if (exception.Kind == DialogFailureKind.UnsupportedOperation)
            {
                frontier = new DialogConversationFrontier(DialogFrontierKind.UnsupportedOperation, node, exception.OperationSource);
            }
            else
            {
                failure = new DialogConversationFailure(
                    exception.Kind == DialogFailureKind.InvalidOperation ? DialogConversationFailureKind.InvalidOperation : DialogConversationFailureKind.OperationLimit,
                    node, exception.OperationSource);
            }
            return null;
        }
    }

    /// <summary>Plans one Player record. Condition grammar failures propagate to the step; action failures block only this reply.</summary>
    private ReplyPlan? PlanReply(DlgRecord record, DialogNodeId node, out DialogConversationFrontier? frontier, out DialogConversationFailure? failure)
    {
        frontier = null;
        failure = null;
        var response = new DialogResponseId(record.Id);
        var gender = ParseGender(record);
        if (gender is null)
        {
            failure = new DialogConversationFailure(DialogConversationFailureKind.InvalidOperation, node,
                new DialogOperationSource(new DialogNodeId(record.Id), record.SerializedIndex, DlgFieldKind.AlternateText,
                    record.AlternateText.PayloadOffset, 0, record.AlternateText.Length, 0), response);
            return null;
        }
        var condition = DialogConversationGrammar.Parse(record, record.Condition, ConversationField.PlayerCondition, _options, _ports.Party is not null);
        if (MissingCapability(condition) is { } missing)
        {
            frontier = new DialogConversationFrontier(DialogFrontierKind.CapabilityUnavailable, node, missing, response);
            return null;
        }
        var textFailure = DialogText.Decode(record.Text.Bytes, _request.Encoding, _options.MaxTextBytes, out var text);
        if (textFailure != DialogTextFailureKind.None)
        {
            failure = new DialogConversationFailure(DialogConversationFailureKind.InvalidText, node, Response: response, TextFailure: textFailure);
            return null;
        }

        var kind = DialogOptionTextKind.Record;
        DialogGeneratedMarker? marker = null;
        DialogOptionBlock? block = null;
        DialogOperationSource? insult = null;
        var found = DialogText.Marker(text, out var exact);
        if (found is { Letter: 'd' or 'q' or 'u' or 'x' })
        {
            // Their visibility depends on world, quest or skill state this runtime cannot read.
            frontier = new DialogConversationFrontier(DialogFrontierKind.UnsupportedGeneratedText, node, Response: response);
            return null;
        }
        if (found is { Letter: 'a' or 'e' or 'f' or 'k' or 'n' or 's' or 'w' or 'y' } && exact)
        {
            kind = DialogOptionTextKind.Generated;
            marker = found;
            text = string.Empty;
        }
        else if (found is { Letter: 'i' } && exact)
        {
            // ARC-WORLD-043: the original's insult reply (exactly "i:", either case, 0x416C10): a generated phrase that keeps the
            // reply's action and transition and runs the insult rule in between. The marker itself is its source.
            kind = DialogOptionTextKind.Generated;
            marker = found;
            text = string.Empty;
            insult = new DialogOperationSource(new DialogNodeId(record.Id), record.SerializedIndex, DlgFieldKind.Text,
                record.Text.PayloadOffset, 0, 2, 0);
        }
        else if (found is { Letter: 'b' or 'h' or 'i' or 'l' or 'p' } && exact || found is { Letter: 'c' or 'r' or 't' or 'z' })
        {
            kind = DialogOptionTextKind.Generated;
            marker = found;
            text = string.Empty;
            block = new DialogOptionBlock(DialogOptionBlockKind.UnsupportedInteraction);
        }
        else
        {
            if (!CheckTokens(text, node, response, out frontier, out failure))
            {
                return null;
            }
            text = DialogText.Substitute(text, _request.Names);
        }

        var value = record.ResponseValue ?? 0;
        var transition = DialogOptionTransition.Exit;
        DlgRecord? target = null;
        DialogScriptLine? line = null;
        if (block is not null)
        {
            transition = DialogOptionTransition.Unsupported;
        }
        else if (value > 0)
        {
            transition = DialogOptionTransition.Direct;
            if (!_document.TryGetRecord(value, out target) || target is null)
            {
                failure = new DialogConversationFailure(DialogConversationFailureKind.MissingTarget, node, Response: response);
                return null;
            }
            if (target.Role != DlgRecordRole.Npc)
            {
                failure = new DialogConversationFailure(DialogConversationFailureKind.WrongTargetRole, node, Response: response);
                return null;
            }
        }
        else if (value < 0)
        {
            if (value == int.MinValue)
            {
                failure = new DialogConversationFailure(DialogConversationFailureKind.InvalidOperation, node, Response: response);
                return null;
            }
            transition = DialogOptionTransition.ScriptLine;
            line = new DialogScriptLine(-value);
        }

        IReadOnlyList<ConversationOperation>? action = null;
        if (block is null)
        {
            try
            {
                action = DialogConversationGrammar.Parse(record, record.Action, ConversationField.PlayerAction, _options, _ports.Party is not null);
                if (MissingCapability(action) is { } actionMissing)
                {
                    block = new DialogOptionBlock(DialogOptionBlockKind.UnsupportedOperation, actionMissing);
                }
            }
            catch (DialogOperationParseException exception)
            {
                block = new DialogOptionBlock(
                    exception.Kind == DialogFailureKind.UnsupportedOperation ? DialogOptionBlockKind.UnsupportedOperation : DialogOptionBlockKind.InvalidOperation,
                    exception.OperationSource);
            }
        }
        if (block is null && insult is not null)
        {
            if (_ports.World is null)
            {
                block = new DialogOptionBlock(DialogOptionBlockKind.UnsupportedOperation, insult);
            }
            else if (action is { Count: > 0 } && action[^1] is ConversationJoin or ConversationUnwait or ConversationFinalLine)
            {
                // A transfer that ends the action skips the insult in the original (0x413130); not modelled here.
                block = new DialogOptionBlock(DialogOptionBlockKind.UnsupportedInteraction, action[^1].Source);
            }
        }
        if (block is null && action is { Count: > 0 } && action[^1] is ConversationJoin or ConversationUnwait or ConversationFinalLine)
        {
            // JO/UW/FL override the response: the source skips the response transition after them.
            var continuation = action[^1] switch
            {
                ConversationJoin join => join.Node,
                ConversationUnwait unwait => unwait.Node,
                _ => ((ConversationFinalLine)action[^1]).Node,
            };
            if (!_document.TryGetRecord(continuation, out var next) || next is null || next.Role != DlgRecordRole.Npc)
            {
                block = new DialogOptionBlock(DialogOptionBlockKind.TargetInvalid, action[^1].Source);
            }
            else
            {
                transition = action[^1] is ConversationFinalLine ? DialogOptionTransition.FinalLine : DialogOptionTransition.Join;
                target = next;
                line = null;
            }
        }
        return new ReplyPlan(record, gender.Value, record.IqValue!.Value, condition, kind, text, marker, transition, target, line, action, block,
            block is null ? insult : null);
    }

    private static bool CheckTokens(string text, DialogNodeId node, DialogResponseId? response, out DialogConversationFrontier? frontier, out DialogConversationFailure? failure)
    {
        frontier = null;
        failure = null;
        var tokenFailure = DialogText.ValidateTokens(text);
        if (tokenFailure == DialogTextFailureKind.UnsupportedToken)
        {
            frontier = new DialogConversationFrontier(DialogFrontierKind.UnsupportedTextToken, node, Response: response);
        }
        else if (tokenFailure != DialogTextFailureKind.None)
        {
            failure = new DialogConversationFailure(DialogConversationFailureKind.InvalidText, node, Response: response, TextFailure: tokenFailure);
        }
        return tokenFailure == DialogTextFailureKind.None;
    }

    /// <summary>Player gender selector: blank is any, otherwise exactly 0 (female) or 1 (male).</summary>
    private static int? ParseGender(DlgRecord record)
    {
        var bytes = record.AlternateText.Bytes;
        var start = 0;
        var end = bytes.Count;
        while (start < end && bytes[start] is (byte)' ' or (byte)'\t') start++;
        while (end > start && bytes[end - 1] is (byte)' ' or (byte)'\t') end--;
        if (start == end) return -1;
        if (end - start == 1 && bytes[start] is (byte)'0' or (byte)'1') return bytes[start] - (byte)'0';
        return null;
    }

    private DialogOperationSource? MissingCapability(IReadOnlyList<ConversationOperation> operations) => operations.FirstOrDefault(operation => operation switch
    {
        ConversationGlobalFlag => _ports.GlobalFlags is null,
        ConversationReactionGate or ConversationReaction or ConversationMarkArea or ConversationAlignment => _ports.World is null,
        ConversationGiveItem => _ports.World is null && _ports.Inventory is null,
        ConversationJoin => _ports.World is null && _ports.Party is null,
        ConversationWaiting or ConversationUnwait => _ports.Party is null,
        ConversationMetBefore => _ports.MetBefore is null,
        ConversationQuellRumor => _ports.Rumors is null,
        _ => false,
    })?.Source;

    /// <summary>Numeric-ID group (the source view); it must equal the serialized run after the same NPC.</summary>
    private IReadOnlyList<DlgRecord> Group(DlgRecord npc, out bool orderMatches)
    {
        var sorted = new List<DlgRecord>();
        var position = Array.BinarySearch(_sorted, npc, Comparer<DlgRecord>.Create((left, right) => left.Id.CompareTo(right.Id)));
        for (var index = position + 1; index < _sorted.Length && _sorted[index].Role == DlgRecordRole.Player; index++)
        {
            sorted.Add(_sorted[index]);
            if (sorted.Count > _options.MaxStructuralReplies)
            {
                orderMatches = true;
                return sorted;
            }
        }
        var serialized = npc.SerializedIndex + 1;
        foreach (var record in sorted)
        {
            if (serialized >= _document.Records.Count || !ReferenceEquals(_document.Records[serialized], record))
            {
                orderMatches = false;
                return sorted;
            }
            serialized++;
        }
        orderMatches = serialized >= _document.Records.Count || _document.Records[serialized].Role == DlgRecordRole.Npc;
        return sorted;
    }

    private sealed record StepPlan(DlgRecord Npc, IReadOnlyList<ConversationOperation> Action, string NpcText, IReadOnlyList<ReplyPlan> Replies);

    private sealed record ReplyPlan(
        DlgRecord Record,
        int Gender,
        int Iq,
        IReadOnlyList<ConversationOperation> Condition,
        DialogOptionTextKind TextKind,
        string Text,
        DialogGeneratedMarker? Marker,
        DialogOptionTransition Transition,
        DlgRecord? Target,
        DialogScriptLine? ScriptLine,
        IReadOnlyList<ConversationOperation>? Action,
        DialogOptionBlock? Block,
        DialogOperationSource? Insult);

    private sealed record OfferPlan(ReplyPlan Reply, StepPlan? Target);
}
