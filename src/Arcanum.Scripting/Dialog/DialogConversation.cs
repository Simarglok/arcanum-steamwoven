using Arcanum.Data;
using Arcanum.Scripting.GlobalFlags;

namespace Arcanum.Scripting.Dialog;

/// <summary>
/// ARC-SCRIPT-005 multi-turn NPC conversation over one immutable <see cref="DlgDocument"/>: NPC entry action,
/// eligible replies, selection, reply action, then continue, exit to SCR or stop at a typed frontier.
/// Synchronous and not thread-safe; the host serializes calls and every externally mutated owner.
/// </summary>
public sealed partial class DialogConversation
{
    private const int LowIntelligence = 4;
    // The skill port's bounds and the expert-teacher gate (the source's training minimum of an expert, 0x4C69C0).
    private const int MaxSkillLevel = 20, MaxSkillTraining = 3, ExpertMinimumLevel = 9;
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
    private readonly Dictionary<int, (string Text, DialogLineOrigin? Origin)> _phrases = new();

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
        if (_talk is not null)
        {
            EmitTalkOptions();
            return Result();
        }
        BuildOffers(_step!);
        return Result();
    }

    [OriginalFunction(0x414810, OriginalFunctionScope.Partial)]
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
        if (_talk is not null)
        {
            return SelectTalk(offer);
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
            var pending = plan.Reply.Action!.Count + (plan.Reply.Insult is null ? 0 : 1) + (plan.Reply.Barter is null ? 0 : 1) + (plan.Reply.WorldMap is null ? 0 : 1);
            if (!Admit(plan.Target, pending))
            {
                return Result();
            }
        }

        // The token is consumed before the first effect; nothing below can offer it again.
        _offers = Array.Empty<DialogOption>();
        DialogOperationSource? replyCombat = null;
        if (plan is not null)
        {
            var ran = ExecuteOperations(plan.Reply.Action!, out replyCombat);
            // The attack event of a co ends the string; an FL in it speaks its final line first (below, CA2).
            if (ran && offer.Transition != DialogOptionTransition.FinalLine && replyCombat is { } combatSource)
            {
                ran = StartCombat(combatSource);
            }
            if (!ran)
            {
                return Result();
            }
        }
        // The hostile check follows the action string and precedes everything else of the reply (0x413130, ledger HR1–HR3): only the
        // strings that return 1 get to it, so a reply that moved the dialog itself by FL, JO, UW, ET or SU skips it.
        if (offer.Transition is not (DialogOptionTransition.Join or DialogOptionTransition.ExpertTeacher or DialogOptionTransition.FinalLine
                or DialogOptionTransition.Schematics or DialogOptionTransition.NpcSheet or DialogOptionTransition.ItemIdentify or DialogOptionTransition.ItemRepair)
            && !PassesHostileCheck())
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
            case DialogOptionTransition.ExpertTeacher:
                Enter(plan!.Target!);
                return Result();
            case DialogOptionTransition.FinalLine:
                Turn++;
                if (plan!.Target is null)
                {
                    // ARC-SCRIPT-2790 (FLM1): the node is not in the DLG; the blank final line has no text and no action (the original shows a single space and ends the talk). The talk ends here, so the turn limit (which Admit tests only for a next step) is not checked.
                    CurrentNode = new DialogNodeId(plan.Reply.Action!.OfType<ConversationFinalLine>().Last().Node);
                    NpcLine = new DialogNpcLine(CurrentNode, string.Empty);
                    if (replyCombat is not { } missingCombat || StartCombat(missingCombat))
                    {
                        Finish(new DialogConversationEnd(DialogEndKind.NpcFinalLine, null));
                    }
                    return Result();
                }
                CurrentNode = new DialogNodeId(plan.Target.Npc.Id);
                NpcLine = new DialogNpcLine(CurrentNode, plan.Target.NpcText);
                if (Execute(plan.Target.Action) && (replyCombat is not { } finalCombat || StartCombat(finalCombat)))
                {
                    Finish(new DialogConversationEnd(DialogEndKind.NpcFinalLine, null));
                }
                return Result();
            case DialogOptionTransition.Schematics:
                Finish(new DialogConversationEnd(DialogEndKind.SchematicWindow, null));
                return Result();
            case DialogOptionTransition.NpcSheet:
                Finish(new DialogConversationEnd(DialogEndKind.NpcSheetWindow, null));
                return Result();
            case DialogOptionTransition.ItemIdentify:
                Finish(new DialogConversationEnd(DialogEndKind.ItemIdentifyWindow, null));
                return Result();
            case DialogOptionTransition.ItemRepair:
                Finish(new DialogConversationEnd(DialogEndKind.ItemRepairWindow, null));
                return Result();
            case DialogOptionTransition.Trainer:
                OpenTalk(plan!);
                return Result();
            case DialogOptionTransition.Rumor:
                OpenRumor(plan!);
                return Result();
            case DialogOptionTransition.Story:
                OpenStory(plan!);
                return Result();
            case DialogOptionTransition.Healer:
                OpenHealer(plan!);
                return Result();
            case DialogOptionTransition.Directions:
                OpenDirections(plan!);
                return Result();
            case DialogOptionTransition.Newspaper:
                OpenNewspaper(plan!);
                return Result();
            case DialogOptionTransition.Barter:
                if (OpenBarter(plan!.Reply.Barter!.Value))
                {
                    Finish(new DialogConversationEnd(DialogEndKind.BarterWindow, null));
                }
                return Result();
            case DialogOptionTransition.WorldMap:
                if (OpenWorldMap(plan!.Reply.WorldMap!.Value))
                {
                    Finish(new DialogConversationEnd(DialogEndKind.WorldMapWindow, null));
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

    /// <summary>Turn and effect budgets for entering <paramref name="next"/> after <paramref name="pending"/> reply effects. A greeting
    /// that may continue at or float a slot node (ARC-WORLD-058) also reserves that node's turn and its largest action, so a
    /// slot never stops at a budget after the greeting's own actions ran.</summary>
    private bool Admit(StepPlan? next, int pending)
    {
        var node = next is null ? CurrentNode : new DialogNodeId(next.Npc.Id);
        var slotNodes = next?.Greeting is { } greeting ? greeting.Jumps.Values.Concat(greeting.Floats.Values).ToArray() : [];
        var slotTurn = slotNodes.Length == 0 ? 0 : 1;
        var slotActions = slotNodes.Length == 0 ? 0 : slotNodes.Max(plan => plan.Action.Count);
        if (next is not null && (long)Turn + slotTurn >= _options.MaxTurns)
        {
            Stop(new DialogConversationFrontier(DialogFrontierKind.TurnLimit, node), null);
            return false;
        }
        if ((long)_effects.Count + pending + (next?.Action.Count ?? 0) + slotActions > _options.MaxEffects)
        {
            Stop(new DialogConversationFrontier(DialogFrontierKind.EffectLimit, node), null);
            return false;
        }
        return true;
    }

    private void Enter(StepPlan plan)
    {
        Turn++;
        _phrases.Clear();
        _step = plan;
        var previous = NpcLine?.Text ?? string.Empty;
        CurrentNode = new DialogNodeId(plan.Npc.Id);
        NpcLine = new DialogNpcLine(CurrentNode, plan.NpcText);
        if (!Execute(plan.Action))
        {
            return;
        }
        // The original runs the node's actions before it tests the node's text for a marker (0x416840).
        if (plan.Greeting is { } greeting)
        {
            Greet(plan, greeting, previous);
            return;
        }
        if (plan.InsultLine)
        {
            // ARC-SCRIPT-1505 (ledger INS1): the insulted line, the NPC's race line 1000; the node's replies follow as for a plain line.
            if (!NpcRaceTalkLine(InsultGroup, out var insulted))
            {
                return;
            }
            NpcLine = insulted;
        }
        if (plan.Talk is { } talk)
        {
            // ARC-SCRIPT-1535 (ledger INS3): an m: or r: node hands the node to the price screen or the rumor draw; no reply of the node is built (0x416840 returns 0).
            OpenNpcTalk(plan, talk);
            return;
        }
        BuildOffers(plan);
    }

    /// <summary>
    /// ARC-WORLD-058: the greeting of a <c>g:</c> node (0x416840 → 0x419260). A line keeps the node's replies, or ends the talk
    /// (slots 1–4 unless the NPC's social class is 2); a jump slot continues at its node as an ordinary entry; a floating slot
    /// says its node's line and ends the talk.
    /// </summary>
    [OriginalFunction(0x416840, OriginalFunctionScope.Partial)]
    private void Greet(StepPlan plan, GreetingPlan greeting, string previous)
    {
        var query = new DialogReactionQuery(_request.Participants.Npc, _request.Participants.PlayerCharacter);
        var port = _ports.Greeting!;
        GreetingOutcome outcome;
        try
        {
            var facts = port.Facts(query);
            if (facts is null)
            {
                Stop(new DialogConversationFrontier(DialogFrontierKind.GreetingFactsUnknown, CurrentNode), null);
                return;
            }
            var sources = port.Sources(query);
            if (!Enum.IsDefined(facts.Category) || sources?.Files is null || sources.ReputationLines is null || port.Rules is null)
            {
                Fail(new DialogConversationFailure(DialogConversationFailureKind.InvalidQueryResult, CurrentNode));
                return;
            }
            outcome = DialogGreeting.Resolve(greeting.Slots, facts, sources, port, query, _request.Names, _request.Encoding, _options.MaxTextBytes);
        }
        catch (DialogGreeting.InvalidGreetingRoll)
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.InvalidQueryResult, CurrentNode));
            return;
        }
        catch (Exception)
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.QueryException, CurrentNode));
            return;
        }
        switch (outcome.Kind)
        {
            case GreetingOutcomeKind.Line:
                NpcLine = new DialogNpcLine(CurrentNode, outcome.KeepsLine ? previous : outcome.Text, outcome.Origin);
                if (outcome.Ends)
                {
                    Finish(new DialogConversationEnd(DialogEndKind.NpcFinalLine, null));
                }
                else
                {
                    BuildOffers(plan);
                }
                return;
            case GreetingOutcomeKind.Jump:
                var jump = greeting.Jumps[outcome.Node];
                if (Admit(jump, 0))
                {
                    Enter(jump);
                }
                return;
            case GreetingOutcomeKind.Float:
                var floating = greeting.Floats[outcome.Node];
                if (!Admit(floating, 0))
                {
                    return;
                }
                Turn++;
                CurrentNode = new DialogNodeId(floating.Npc.Id);
                NpcLine = new DialogNpcLine(CurrentNode, floating.NpcText);
                if (Execute(floating.Action))
                {
                    Finish(new DialogConversationEnd(DialogEndKind.NpcFinalLine, null));
                }
                return;
            default:
                Fail(new DialogConversationFailure(outcome.Failure, CurrentNode, TextFailure: outcome.TextFailure));
                return;
        }
    }

    /// <summary>Source order with the source cap: stop after five eligible replies; later rows are not queried. A generated
    /// phrase is picked as its reply is offered, in that order (ARC-SCRIPT-043, 0x414E60 → 0x416C10), and kept for the turn: a
    /// rebuild of the same node's offers shows the phrase it showed (the original rebuilds with the random state it saved).</summary>
    [OriginalFunction(0x414E60, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x414F50, OriginalFunctionScope.Partial)]
    private void BuildOffers(StepPlan step)
    {
        var offers = new List<DialogOption>();
        var plans = new Dictionary<int, OfferPlan>();
        // The source takes the first five eligible records (0x414F50) before the reply builder (0x416C10) drops one, so a dropped q: reply still counts.
        var eligible = 0;
        foreach (var planned in step.Replies)
        {
            if (eligible == MaxEligible)
            {
                break;
            }
            var open = IsEligible(planned);
            if (open is null)
            {
                return;
            }
            if (open == false)
            {
                continue;
            }
            eligible++;
            var reply = planned;
            if (planned.QuestReply is not null)
            {
                if (!ResolveQuestReply(planned, step, out var resolved))
                {
                    return;
                }
                if (resolved is null)
                {
                    continue;
                }
                reply = resolved;
            }
            // ARC-SCRIPT-1534: the original's reply builder returns before any state is built for the repair service reply, so it takes a slot of the five and is not shown.
            if (reply.NeverOffered)
            {
                continue;
            }
            // ARC-SCRIPT-1798 (ledger DSV1, DSV2): a skill service reply the NPC cannot do for the PC (0x4AE570 answers non-zero) is left out the same way.
            if (reply.ServiceFilter is { } service)
            {
                var offered = ServiceOffered(service);
                if (offered is null)
                {
                    return;
                }
                if (offered == false)
                {
                    continue;
                }
            }
            // A directions reply the PC has nothing to ask for is left out whatever its action holds (0x416C10 returns before the states are built).
            if (reply.DirectionsFilter is { } directions)
            {
                var hidden = DirectionsHidden(directions);
                if (hidden is null)
                {
                    return;
                }
                if (hidden == true)
                {
                    continue;
                }
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
            var text = reply.Text;
            DialogLineOrigin? origin = null;
            if (reply.Phrase && !PickPhrase(reply.Record.Id, reply.Marker!.Value.Letter, out text, out origin))
            {
                return;
            }
            if (reply.ServiceFilter is { } named && !ServiceText(named, text, out text))
            {
                return;
            }
            var response = new DialogResponseId(reply.Record.Id);
            plans.Add(reply.Record.Id, new OfferPlan(reply, target));
            offers.Add(new DialogOption(
                offers.Count + 1,
                response,
                reply.Record.SerializedIndex,
                reply.TextKind,
                text,
                reply.Marker,
                reply.Trainer is not null ? DialogOptionTransition.Trainer : reply.Rumor is not null ? DialogOptionTransition.Rumor : reply.Story ? DialogOptionTransition.Story
                    : reply.Directions is not null ? DialogOptionTransition.Directions : reply.Newspaper ? DialogOptionTransition.Newspaper : reply.Healer ? DialogOptionTransition.Healer : reply.Transition,
                reply.Target is null ? null : new DialogNodeId(reply.Target.Id),
                reply.ScriptLine,
                new DialogOptionToken(_identity, Turn, _generation, response),
                block,
                origin));
        }
        if (offers.Count == 0)
        {
            if (!PickPhrase(FarewellPhrase, null, out var text, out var origin))
            {
                return;
            }
            var response = new DialogResponseId(0);
            offers.Add(new DialogOption(1, response, -1, DialogOptionTextKind.Fallback, text, null,
                DialogOptionTransition.Exit, null, null, new DialogOptionToken(_identity, Turn, _generation, response), null, origin));
        }
        _offers = offers.AsReadOnly();
        _offerPlans = plans;
    }

    // The phrase key of the no-reply farewell (reply ids are record ids, never negative).
    private const int FarewellPhrase = -1;

    /// <summary>
    /// ARC-SCRIPT-043: the phrase of an offered generated reply (<paramref name="letter"/>) or of the farewell (null), through the
    /// greeting port's facts, files and rolls (<see cref="DialogGreeting.Phrase"/>), once per reply and turn. Without a greeting
    /// port, or when the port does not know the facts, the text stays empty (the option still works). False when the
    /// conversation failed: a broken port result, a refused roll or an undecodable line.
    /// </summary>
    private bool PickPhrase(int key, char? letter, out string text, out DialogLineOrigin? origin)
    {
        text = string.Empty;
        origin = null;
        if (_phrases.TryGetValue(key, out var kept))
        {
            (text, origin) = kept;
            return true;
        }
        if (_ports.Greeting is not { } port)
        {
            return true;
        }
        var query = new DialogReactionQuery(_request.Participants.Npc, _request.Participants.PlayerCharacter);
        GreetingOutcome? outcome;
        try
        {
            if (port.Facts(query) is not { } facts)
            {
                return true;
            }
            var sources = port.Sources(query);
            if (sources?.Files is null || port.Rules is null)
            {
                Fail(new DialogConversationFailure(DialogConversationFailureKind.InvalidQueryResult, CurrentNode));
                return false;
            }
            var storyState = 0;
            if (letter == 'c' && !ReadStoryState(out storyState)) return false;
            outcome = DialogGreeting.Phrase(letter, PcIntelligence() <= LowIntelligence, facts, sources, port, _request.Names, _request.Encoding,
                _options.MaxTextBytes, storyState);
        }
        catch (DialogGreeting.InvalidGreetingRoll)
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.InvalidQueryResult, CurrentNode));
            return false;
        }
        catch (Exception)
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.QueryException, CurrentNode));
            return false;
        }
        if (outcome is { Kind: not GreetingOutcomeKind.Line })
        {
            Fail(new DialogConversationFailure(outcome.Failure, CurrentNode, TextFailure: outcome.TextFailure));
            return false;
        }
        if (outcome is not null)
        {
            text = outcome.Text;
            origin = outcome.Origin;
        }
        _phrases[key] = (text, origin);
        return true;
    }

    // The PC's intelligence as the reply gates read it: a dumb PC counts 1 (the original's dumb test 0x45FC00 is then true).
    private int PcIntelligence() => _request.Pc.Intelligence > LowIntelligence && _request.Pc.IsDumb ? 1 : _request.Pc.Intelligence;

    /// <summary>Gender, then effective IQ, then the condition conjunction in order. Null means the session stopped.</summary>
    private bool? IsEligible(ReplyPlan reply)
    {
        if (reply.Gender >= 0 && reply.Gender != (int)_request.Pc.Gender)
        {
            return false;
        }
        var intelligence = PcIntelligence();
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
                ConversationCounter counter => ReadCounter(counter),
                ConversationCollegeGate college => ReadCollege(college),
                ConversationFollowerGate follower => ReadFollower(follower),
                ConversationGlobalFlag global => ReadGlobal(global),
                ConversationReactionGate reaction => ReadReaction(reaction),
                ConversationWaiting waiting => ReadWaiting(waiting),
                ConversationCompanionGate companion => ReadCompanion(companion),
                ConversationMetBefore or ConversationReputation => ReadPcMemory(operation),
                ConversationSkillGate skill => ReadSkill(skill),
                ConversationScriptStore store => ReadStore(store),
                ConversationGoldGate gold => ReadGold(gold),
                ConversationQuestGate quest => ReadQuest(quest),
                ConversationStoryGate or ConversationKnownAreaGate or ConversationMapAreaGate => ReadWorldFact(operation),
                ConversationStatGate stat => ReadStat(stat),
                ConversationItemGate item => ReadItem(item),
                ConversationRumorGate or ConversationQuellGate => ReadRumor(operation),
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

    /// <summary>
    /// SC as a condition (0x4150D0 case 36 → the rank getter 0x4B1AB0, ledger DCC10): the PC's rank in the college against the second operand v: above 0 the rank must be at least v, at 0 or below
    /// at most −v (so v = 0 asks for rank 0, unlike the stat conditions' minimum of 0).
    /// </summary>
    private bool? ReadCollege(ConversationCollegeGate gate)
    {
        int? read;
        try
        {
            read = _ports.Colleges!.Rank(new DialogCollegeQuery(_request.Participants.PlayerCharacter, gate.College));
        }
        catch (Exception)
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.QueryException, CurrentNode, gate.Source));
            return null;
        }
        if (read is not { } rank || rank < 0)
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.InvalidQueryResult, CurrentNode, gate.Source));
            return null;
        }
        return gate.Threshold > 0 ? rank >= gate.Threshold : rank <= -(long)gate.Threshold;
    }

    /// <summary>
    /// PA as a condition (0x4150D0 case 26, ledger DCC8, PN1): the operand's magnitude is a name number; the condition holds when some direct follower of the PC that is not mind-controlled
    /// carries it, and a negative operand when none does.
    /// </summary>
    private bool? ReadFollower(ConversationFollowerGate gate)
    {
        bool found;
        try
        {
            found = _ports.FollowerNames!.HasFollowerNamed(new DialogFollowerNameQuery(_request.Participants.PlayerCharacter, gate.Name));
        }
        catch (Exception)
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.QueryException, CurrentNode, gate.Source));
            return null;
        }
        return gate.Absent ? !found : found;
    }

    /// <summary>LC as a condition (0x4150D0 case 14): the NPC's counter equals the second operand; a counter the owner cannot read (outside 0..255) fails the conversation.</summary>
    private bool? ReadCounter(ConversationCounter operation)
    {
        int value;
        try
        {
            value = _ports.Counters!.Read(new DialogCounterQuery(_request.Participants.Npc, operation.Index));
        }
        catch (Exception)
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.QueryException, CurrentNode, operation.Source));
            return null;
        }
        if (value is < 0 or > 255)
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.InvalidQueryResult, CurrentNode, operation.Source));
            return null;
        }
        return value == operation.Value;
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
    /// SU (ARC-TECHNOLOGY-006, ledger NC1): the action interpreter's case (0x415BA0, registered with <see cref="Reputation"/>, one method
    /// per address) asks for the schematic window with the NPC as crafter and the PC as viewer; its operand is read and ignored, the
    /// rest of the field still runs, and the reply's target node is not entered (the caller's transition ends the talk).
    /// </summary>
    private OpenSchematicsCommand SchematicsCommand() =>
        new(_request.Participants.Npc, _request.Participants.PlayerCharacter);

    /// <summary>
    /// The ME and RP branches of the original's condition (0x4150D0). ME 1: the NPC has met the PC before; ME 0: it has not
    /// (the met-before function 0x4C0C40 leaves out the conversation in progress). RP n (ARC-WORLD-044): n > 0 holds when the
    /// PC has reputation n, n ≤ 0 when it lacks −n.
    /// </summary>
    [OriginalFunction(0x4150D0, OriginalFunctionScope.Partial)]
    private bool? ReadPcMemory(ConversationOperation operation)
    {
        var pc = _request.Participants.PlayerCharacter;
        try
        {
            if (operation is ConversationMetBefore met) return _ports.MetBefore!.MetBefore(new DialogReactionQuery(_request.Participants.Npc, pc)) == met.Value;
            var reputation = ((ConversationReputation)operation).Value;
            return reputation > 0
                ? _ports.Reputations!.HasReputation(new DialogReputationQuery(pc, reputation))
                : !_ports.Reputations!.HasReputation(new DialogReputationQuery(pc, -reputation));
        }
        catch (Exception)
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.QueryException, CurrentNode, operation.Source));
            return null;
        }
    }

    /// <summary>
    /// The ru and rq branches of the original's condition (0x4150D0, cases 17 and 18, ledger RU1–RU2). RU n: n ≥ 0 holds when the PC knows
    /// rumor n (0x4C58D0), n &lt; 0 when it does not know rumor −n. RQ n: the same with the quelled mark (0x4C5650). The grammar admits
    /// only 1000..2999 (and the negatives). Registered with <see cref="ReadPcMemory"/> (one method per address).
    /// </summary>
    private bool? ReadRumor(ConversationOperation operation)
    {
        var selector = operation is ConversationRumorGate known ? known.Selector : ((ConversationQuellGate)operation).Selector;
        var rumor = Math.Abs(selector);
        try
        {
            var holds = operation is ConversationRumorGate
                ? _ports.Rumors!.IsKnown(new DialogRumorQuery(_request.Participants.PlayerCharacter, rumor))
                : _ports.Rumors!.IsQuelled(rumor);
            return selector > 0 ? holds : !holds;
        }
        catch (Exception)
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.QueryException, CurrentNode, operation.Source));
            return null;
        }
    }

    /// <summary>
    /// The gv, pv and pf branches of the original's condition (0x4150D0, ledger DG1, DG2): the second operand is compared for equality
    /// with the global variable (0x444C80), the dialog PC's variable (0x444DB0) or the dialog PC's flag (0x444E30, 0 or 1). Registered with
    /// <see cref="ReadPcMemory"/> (one method per address).
    /// </summary>
    private bool? ReadStore(ConversationScriptStore operation)
    {
        int value;
        try
        {
            value = _ports.Stores!.Read(new DialogScriptStoreQuery(_request.Participants.PlayerCharacter, operation.Store, operation.Index));
        }
        catch (Exception)
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.QueryException, CurrentNode, operation.Source));
            return null;
        }
        if (operation.Store == DialogScriptStore.PcFlag && value is not (0 or 1))
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.InvalidQueryResult, CurrentNode, operation.Source));
            return null;
        }
        return value == operation.Value;
    }

    /// <summary>
    /// The <c>$$</c> branch of the original's condition (0x4150D0 case 10, ledger DCC5): the signed rule on the gold of the PC and its direct
    /// followers (<see cref="IDialogGoldPort.Total"/>): n ≥ 0 holds at total ≥ n, n &lt; 0 at total ≤ −n, both inclusive. Registered with
    /// <see cref="ReadPcMemory"/> (one method per address).
    /// </summary>
    private bool? ReadGold(ConversationGoldGate operation)
    {
        int total;
        try
        {
            total = _ports.Gold!.Total(new DialogGoldQuery(_request.Participants.PlayerCharacter));
        }
        catch (Exception)
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.QueryException, CurrentNode, operation.Source));
            return null;
        }
        return operation.Threshold < 0 ? total <= -(long)operation.Threshold : total >= operation.Threshold;
    }

    /// <summary>
    /// The <c>qu</c>, <c>qa</c> and <c>qb</c> branches of the original's condition (0x4150D0 cases 8, 24 and 21, ledger DCC4): the PC's state of the
    /// quest (<see cref="IDialogQuestPort.State"/>, the getter 0x4C4CB0) equals, is at least or is at most the second operand (signed compares).
    /// Registered with <see cref="ReadPcMemory"/> (one method per address).
    /// </summary>
    private bool? ReadQuest(ConversationQuestGate operation)
    {
        int state;
        try
        {
            state = _ports.Quests!.State(new DialogQuestQuery(_request.Participants.PlayerCharacter, operation.QuestId));
        }
        catch (Exception)
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.QueryException, CurrentNode, operation.Source));
            return null;
        }
        return operation.Compare switch
        {
            ConversationQuestCompare.Equal => state == operation.State,
            ConversationQuestCompare.AtLeast => state >= operation.State,
            _ => state <= operation.State,
        };
    }

    /// <summary>
    /// The <c>ss</c>, <c>ar</c> and <c>ia</c> branches of the original's condition (0x4150D0 cases 27, 33 and 35, ledger DCC7): the signed rule on the story state
    /// (0x445090), the PC's known area (0x4CAF50; n &gt; 0 known, n ≤ 0 not known) and the area of the PC's map (0x4CB6A0; n &gt; 0 equal, n ≤ 0 different).
    /// </summary>
    private bool? ReadWorldFact(ConversationOperation operation)
    {
        try
        {
            switch (operation)
            {
                case ConversationStoryGate story:
                {
                    var state = _ports.Story!.State();
                    return story.Threshold < 0 ? state <= -(long)story.Threshold : state >= story.Threshold;
                }
                case ConversationKnownAreaGate area:
                    return _ports.Areas!.Known(new DialogAreaQuery(_request.Participants.PlayerCharacter, area.Area)) == area.MustBeKnown;
                default:
                {
                    var gate = (ConversationMapAreaGate)operation;
                    if (_ports.MapArea!.Area(_request.Participants.PlayerCharacter) is not { } current)
                    {
                        Fail(new DialogConversationFailure(DialogConversationFailureKind.InvalidQueryResult, CurrentNode, operation.Source));
                        return null;
                    }
                    return (current == gate.Area) == gate.MustEqual;
                }
            }
        }
        catch (Exception)
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.QueryException, CurrentNode, operation.Source));
            return null;
        }
    }

    /// <summary>
    /// The <c>in</c> and <c>ni</c> branches of the original's condition (0x4150D0 cases 11 and 23, ledger DCC6, IH1–IH2): <c>in n</c> (n ≥ 0) holds when the PC or one of its followers holds
    /// an item named n, <c>in −n</c> when the NPC or its substitute container does; <c>ni</c> is the exact negation of both.
    /// </summary>
    private bool? ReadItem(ConversationItemGate gate)
    {
        bool held;
        try
        {
            var query = new DialogItemQuery(_request.Participants.Npc, _request.Participants.PlayerCharacter, gate.Name);
            held = gate.NpcSide ? _ports.ItemHolds!.NpcHolds(query) : _ports.ItemHolds!.PartyHolds(query);
        }
        catch (Exception)
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.QueryException, CurrentNode, gate.Source));
            return null;
        }
        return held != gate.Negated;
    }

    /// <summary>
    /// The <c>ch</c>, <c>pe</c>, <c>le</c>, <c>al</c>, <c>ma</c>, <c>ta</c> and <c>na</c> branches of the original's condition (0x4150D0, ledger DCC2, DCC3): the PC's stat
    /// from the getter 0x4B0490 against the first operand n. The signed rule: n ≥ 0 holds at value ≥ n, n &lt; 0 at value ≤ −n (<c>ta</c> applies it to the negated
    /// aptitude). <c>na</c>: n ≥ 0 holds at alignment ≥ −n, n &lt; 0 at alignment ≤ n.
    /// </summary>
    private bool? ReadStat(ConversationStatGate gate)
    {
        int? read;
        try
        {
            read = _ports.Stats!.Stat(new DialogStatQuery(_request.Participants.PlayerCharacter, gate.Stat));
        }
        catch (Exception)
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.QueryException, CurrentNode, gate.Source));
            return null;
        }
        if (read is not { } stat)
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.InvalidQueryResult, CurrentNode, gate.Source));
            return null;
        }
        long n = gate.Threshold;
        return gate.Rule switch
        {
            ConversationStatRule.AlignmentNegatedOperand => n >= 0 ? stat >= -n : stat <= n,
            ConversationStatRule.Negated => n >= 0 ? -(long)stat >= n : -(long)stat <= -n,
            _ => n >= 0 ? stat >= n : stat <= -n,
        };
    }

    /// <summary>The gv, pv and pf branches of the original's action (0x415BA0, ledger DG3, DG4): the cell takes the second operand.
    /// Registered with <see cref="Reputation"/> (one method per address).</summary>
    private ScriptStoreCommand Store(ConversationScriptStore operation) =>
        new(_request.Participants.PlayerCharacter, operation.Store, operation.Index, operation.Value);

    /// <summary>The RP branch of the original's action (0x415BA0): n > 0 adds reputation n to the PC, n ≤ 0 removes −n.</summary>
    [OriginalFunction(0x415BA0, OriginalFunctionScope.Partial)]
    private ReputationCommand Reputation(ConversationReputation operation) =>
        new(_request.Participants.PlayerCharacter, operation.Value > 0 ? operation.Value : -operation.Value, operation.Value > 0);

    /// <summary>
    /// The reply step's hostile check (0x413130, ledger HR1–HR3, ARC-DEV-193): after the picked reply's action string returned 1 (no FL, JO, UW,
    /// ET or SU in it) the hostility port is asked whether the NPC is alive and kill-on-sight toward the PC now; when it is, the NPC speaks a
    /// generated line (race group 1000, ARC-SCRIPT-1505) and the talk ends as <see cref="DialogEndKind.HostileRefusal"/> before the insult and the
    /// reply's transition. Without the port the check is not made. False when the talk ended or failed.
    /// </summary>
    [OriginalFunction(0x413130, OriginalFunctionScope.Partial)]
    private bool PassesHostileCheck()
    {
        if (_ports.Hostile is not { } port)
        {
            return true;
        }
        bool hostile;
        try
        {
            hostile = port.Hostile(new DialogHostileQuery(_request.Participants.Npc, _request.Participants.PlayerCharacter));
        }
        catch (Exception)
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.QueryException, CurrentNode));
            return false;
        }
        if (!hostile)
        {
            return true;
        }
        if (!SpeakLastWords())
        {
            return false;
        }
        Finish(new DialogConversationEnd(DialogEndKind.HostileRefusal, null));
        return false;
    }

    /// <summary>
    /// ARC-SCRIPT-1505 (ledger INS2, HR3): the NPC's last words after an insult that ends the talk or a refusal to talk on, its race line 1000
    /// (no replies, end state 4). Without the greeting port, or when its facts are unknown, the line stays as it was. False when the line could not be picked (the conversation failed).
    /// </summary>
    private bool SpeakLastWords()
    {
        if (_ports.Greeting is null)
        {
            return true;
        }
        if (!NpcRaceTalkLine(InsultGroup, out var last))
        {
            return false;
        }
        // A host whose facts are unknown picks no line (origin null): the line stays as it was rather than going blank.
        if (last.Origin is not null)
        {
            NpcLine = last;
        }
        return true;
    }

    /// <summary>
    /// The insult of a picked Player <c>i:</c> reply (0x4191E0): the NPC's reaction changes by <see cref="InsultReaction"/>
    /// through the reaction port (the PC's adjustment effects and skips are the owner's), then the effective reaction is read;
    /// at most <see cref="InsultEndsAtOrBelow"/> the NPC ends the talk with its generated race line 1000 (ARC-SCRIPT-1505, with the greeting port;
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
            if (!SpeakLastWords())
            {
                return false;
            }
            Finish(new DialogConversationEnd(DialogEndKind.Insulted, null));
            return false;
        }
        return true;
    }

    /// <summary>FO and WT (0x4150D0 cases 19 and 29): the NPC's leader is the PC, or the NPC is jilted; the operand 1 asks for it, 0 for the opposite.</summary>
    private bool? ReadCompanion(ConversationCompanionGate operation)
    {
        bool state;
        try
        {
            var query = new DialogPartyQuery(_request.Participants.Npc, _request.Participants.PlayerCharacter);
            state = operation.Fact == DialogCompanionFact.LedByPc ? _ports.Companion!.LedByPc(query) : _ports.Companion!.Jilted(query);
        }
        catch (Exception)
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.QueryException, CurrentNode, operation.Source));
            return null;
        }
        return state == operation.Value;
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

    /// <summary>
    /// PS, HA, SK and TR (0x4150D0): the PC's effective level (training) in the skill against the threshold: at least it when
    /// it is not negative, at most its absolute value otherwise.
    /// </summary>
    private bool? ReadSkill(ConversationSkillGate operation)
    {
        var value = ReadSkill(operation.Skill, operation.Training, operation.Source);
        if (value is not { } read) return null;
        return operation.Threshold < 0 ? read <= -(long)operation.Threshold : read >= operation.Threshold;
    }

    // A level 0..20 or a training 0..3 from the skill port; anything else is an invalid answer.
    private int? ReadSkill(int skill, bool training, DialogOperationSource source)
    {
        int value;
        try
        {
            var query = new DialogSkillQuery(_request.Participants.PlayerCharacter, skill);
            value = training ? _ports.Skills!.Training(query) : _ports.Skills!.Level(query);
        }
        catch (Exception)
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.QueryException, CurrentNode, source));
            return null;
        }
        if (value < 0 || value > (training ? MaxSkillTraining : MaxSkillLevel))
        {
            Fail(new DialogConversationFailure(DialogConversationFailureKind.InvalidQueryResult, CurrentNode, source));
            return null;
        }
        return value;
    }

    /// <summary>The operations of one condition or action field with the ports this conversation has (a capability without its port stays outside the subset).</summary>
    private IReadOnlyList<ConversationOperation> ParseField(DlgRecord record, DlgField field, ConversationField role) =>
        DialogConversationGrammar.Parse(record, field, role, _options, _ports.Party is not null, _ports.Skills is not null, _ports.Fate is not null,
            _ports.Reputations is not null, _ports.Schematics is not null, _ports.Stores is not null, _ports.Experience is not null, _ports.Gold is not null,
            _ports.Quests is not null, _ports.Combat is not null, _ports.Story is not null, _ports.Areas is not null, _ports.MapArea is not null, _ports.Stats is not null, _ports.ItemHolds is not null, _ports.ItemTake is not null, _ports.Companion is not null, _ports.Counters is not null, _ports.Colleges is not null, _ports.FollowerNames is not null, _ports.Kill is not null, _ports.Windows is not null, _ports.Newspaper is not null, _ports.Origins is not null);

    /// <summary>One action string (0x415BA0): its operations in order, then, when one was <c>co</c>, the attack event (ledger SU13, CA2).</summary>
    /// <summary>A window action (SU, the NPC sheet's, ii and ri): it sets the pending window code and ends the talk for a window (ledger NC1, WN1).</summary>
    private static bool OpensWindow(ConversationOperation operation) => operation is ConversationSchematics or ConversationWindow;

    private bool Execute(IReadOnlyList<ConversationOperation> operations) =>
        ExecuteOperations(operations, out var combat) && (combat is not { } source || StartCombat(source));

    /// <summary>The operations of an action string without the string's end: <paramref name="combat"/> is the first <c>co</c>, whose attack
    /// event the caller runs at the string's end (a final line in the same string runs before it).</summary>
    private bool ExecuteOperations(IReadOnlyList<ConversationOperation> operations, out DialogOperationSource? combat)
    {
        combat = null;
        foreach (var operation in operations)
        {
            if (operation is ConversationCombat)
            {
                // CO only sets a flag (0x415ECC); the event follows the string.
                combat ??= operation.Source;
                continue;
            }
            if (operation is ConversationExpertTeacher teacher)
            {
                // ET (0x415BA0): a level of at least the expert minimum (9) and any training let the dialog continue at the
                // node; otherwise the source speaks a generated refusal.
                if (ReadSkill(teacher.Skill, false, operation.Source) is not { } level || ReadSkill(teacher.Skill, true, operation.Source) is not { } trained)
                {
                    return false;
                }
                if (level < ExpertMinimumLevel || trained < 1)
                {
                    Stop(new DialogConversationFrontier(DialogFrontierKind.ExpertTeacherRefused, CurrentNode, operation.Source), null);
                    return false;
                }
                continue;
            }
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
            if (operation is ConversationReputation reputation)
            {
                var command = Reputation(reputation);
                DialogReputationResult changed;
                try
                {
                    changed = _ports.Reputations!.Execute(command);
                }
                catch (Exception)
                {
                    Record(command, operation.Source, DialogCommandAttemptOutcome.Indeterminate);
                    Fail(new DialogConversationFailure(DialogConversationFailureKind.CommandIndeterminate, CurrentNode, operation.Source));
                    return false;
                }
                // A skip is the owner's choice for a number it does not hold (A5): recorded, and the dialog goes on.
                Record(command, operation.Source, changed switch
                {
                    DialogReputationResult.Applied => DialogCommandAttemptOutcome.Applied,
                    DialogReputationResult.Skipped => DialogCommandAttemptOutcome.Skipped,
                    _ => DialogCommandAttemptOutcome.Rejected,
                });
                if (changed is DialogReputationResult.Applied or DialogReputationResult.Skipped) continue;
                Fail(new DialogConversationFailure(DialogConversationFailureKind.CommandRejected, CurrentNode, operation.Source));
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
                ConversationCounter counter => new CounterWriteCommand(_request.Participants.Npc, counter.Index, counter.Value & 0xFF),
                ConversationLocalFlag local => new LocalFlagWriteCommand(_request.Participants.Dialog, _request.Participants.Npc, local.Index, local.Value),
                ConversationQuest quest => new QuestStateCommand(_request.Participants.PlayerCharacter, quest.QuestId, quest.State, _request.Participants.Npc),
                ConversationRumor rumor => new RumorLogCommand(_request.Participants.PlayerCharacter, rumor.RumorId),
                ConversationQuellRumor quell => new QuellRumorCommand(quell.RumorId),
                ConversationReaction reaction => new ReactionCommand(_request.Participants.Npc, _request.Participants.PlayerCharacter, reaction.Mode, reaction.Value),
                ConversationGiveItem item => new GiveItemCommand(_request.Participants.Npc, _request.Participants.PlayerCharacter, item.ItemName),
                ConversationMarkArea area => new MarkAreaCommand(_request.Participants.PlayerCharacter, area.Area),
                ConversationAlignment alignment => new AlignmentCommand(_request.Participants.PlayerCharacter, alignment.Mode, alignment.Value),
                ConversationSkillTraining training => new SkillTrainingCommand(_request.Participants.PlayerCharacter, training.Skill, training.Training),
                ConversationFatePoint => new FatePointCommand(_request.Participants.PlayerCharacter),
                ConversationExperience experience => new QuestExperienceCommand(_request.Participants.PlayerCharacter, experience.Level),
                ConversationStoryState story => new StoryStateCommand(_request.Participants.PlayerCharacter, story.Value),
                ConversationTakeItem take => new TakeItemCommand(_request.Participants.Npc, _request.Participants.PlayerCharacter, take.ItemName),
                ConversationKill => new KillNpcCommand(_request.Participants.Npc, _request.Participants.PlayerCharacter),
                ConversationNewspaper newspaper => new QueueNewspaperCommand(newspaper.Issue, newspaper.Front),
                ConversationOrigin origin => new SetOriginCommand(_request.Participants.Npc, _request.Participants.PlayerCharacter, origin.Origin),
                ConversationCompanionAction companion => new CompanionCommand(_request.Participants.Npc, _request.Participants.PlayerCharacter, companion.Action),
                ConversationGold gold => new GoldCommand(_request.Participants.PlayerCharacter, _request.Participants.Npc, gold.Amount),
                ConversationSchematics => SchematicsCommand(),
                ConversationWindow window => new OpenWindowCommand(_request.Participants.Npc, _request.Participants.PlayerCharacter, window.Kind),
                ConversationScriptStore store => Store(store),
                _ => throw new InvalidOperationException("Unadmitted action."),
            };
            DialogCommandResult result;
            try
            {
                result = typed switch
                {
                    LocalFlagWriteCommand local => _ports.Commands.Execute(local),
                    CounterWriteCommand counter => _ports.Counters!.Execute(counter),
                    QuestStateCommand quest => _ports.Commands.Execute(quest),
                    RumorLogCommand rumor => _ports.Commands.Execute(rumor),
                    QuellRumorCommand quell => _ports.Rumors!.Execute(quell),
                    ReactionCommand reaction => _ports.World!.Execute(reaction),
                    GiveItemCommand item => _ports.Inventory is { } inventory ? inventory.Execute(item) : _ports.World!.Execute(item),
                    TakeItemCommand take => _ports.ItemTake!.Execute(take),
                    CompanionCommand companion => _ports.Companion!.Execute(companion),
                    KillNpcCommand kill => _ports.Kill!.Execute(kill),
                    QueueNewspaperCommand newspaper => _ports.Newspaper!.Execute(newspaper),
                    SetOriginCommand origin => _ports.Origins!.Execute(origin),
                    MarkAreaCommand area => _ports.World!.Execute(area),
                    AlignmentCommand alignment => _ports.World!.Execute(alignment),
                    SkillTrainingCommand training => _ports.Skills!.Execute(training),
                    FatePointCommand fate => _ports.Fate!.Execute(fate),
                    QuestExperienceCommand experience => _ports.Experience!.Execute(experience),
                    GoldCommand gold => _ports.Gold!.Execute(gold),
                    StoryStateCommand story => _ports.Story!.Execute(story),
                    OpenSchematicsCommand schematics => _ports.Schematics!.Execute(schematics),
                    OpenWindowCommand window => _ports.Windows!.Execute(window),
                    ScriptStoreCommand store => _ports.Stores!.Execute(store),
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

    // ARC-DEV-190: the attack event the string's co asked for (0x416703 → 0x4A9650, the PC on the NPC, loudness 1, flags 0).
    private bool StartCombat(DialogOperationSource source)
    {
        var command = new StartCombatCommand(_request.Participants.Npc, _request.Participants.PlayerCharacter);
        DialogCommandResult result;
        try
        {
            result = _ports.Combat!.Execute(command);
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

    // ARC-UI-050: the barter open after the reply's action (0x413130 state 3 → 0x567E30 → 0x572240).
    private bool OpenBarter(DialogOperationSource source)
    {
        var command = new OpenBarterCommand(_request.Participants.Npc, _request.Participants.PlayerCharacter);
        DialogCommandResult result;
        try
        {
            result = _ports.Barter!.Execute(command);
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

    // ARC-SCRIPT-1358: the world map open after the reply's action (0x413130 state 30 → dialog state 6 → 0x567E30 → 0x560760).
    private bool OpenWorldMap(DialogOperationSource source)
    {
        var command = new OpenWorldMapCommand(_request.Participants.Npc, _request.Participants.PlayerCharacter);
        DialogCommandResult result;
        try
        {
            result = _ports.WorldMap!.Execute(command);
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

    private void Record(DialogCommand command, DialogOperationSource source, DialogCommandAttemptOutcome outcome, GlobalFlagWriteReceipt? receipt = null) =>
        _effects.Add(new DialogEffectReceipt(_effects.Count, Turn, command, source, outcome, receipt));

    private void Finish(DialogConversationEnd end)
    {
        End = end;
        Phase = DialogConversationPhase.Ended;
        _offers = Array.Empty<DialogOption>();
        _talk = null;
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

    private StepPlan? PlanStep(DlgRecord npc, out DialogConversationFrontier? frontier, out DialogConversationFailure? failure, bool withReplies = true,
        bool nested = false)
    {
        frontier = null;
        failure = null;
        var node = new DialogNodeId(npc.Id);
        try
        {
            var action = ParseField(npc, npc.Action, ConversationField.NpcAction);
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
            GreetingPlan? greeting = null;
            var insultLine = false;
            if (marker is { Letter: 'g' } && _ports.Greeting is not null && withReplies && !nested)
            {
                // ARC-WORLD-058: the greeting's slot targets are planned now, so a slot never half-runs a node.
                greeting = PlanGreeting(npc, raw, node, out frontier, out failure);
                if (greeting is null)
                {
                    return null;
                }
                text = string.Empty;
            }
            else if (marker is { Letter: 'i' } && exact && _ports.Greeting is not null && withReplies && !nested)
            {
                // ARC-SCRIPT-1505 (ledger INS1): an NPC node that is exactly "i:" says the NPC's race line 1000 (0x416840 → 0x4185F0), picked when the node is entered.
                insultLine = true;
                text = string.Empty;
            }
            else if (marker is { Letter: 'm' or 'r' } && withReplies && !nested && ReadNpcTalk(marker.Value.Letter, raw) is { } npcSource)
            {
                // ARC-SCRIPT-1535 (ledger INS3): the m: and r: lines; the follows are planned now, so a pick never half-runs a node, and the node's own replies are not built.
                var talkPlan = PlanNpcTalk(npc, npcSource, node, out frontier, out failure);
                if (talkPlan is null)
                {
                    return null;
                }
                return new StepPlan(npc, action, string.Empty, Array.Empty<ReplyPlan>(), Talk: talkPlan);
            }
            else if (marker is { Letter: 'g' or 'm' or 'r' } || marker is { Letter: 'i' } && exact)
            {
                frontier = new DialogConversationFrontier(DialogFrontierKind.UnsupportedGeneratedText, node);
                return null;
            }
            else if (!CheckTokens(text, node, null, out frontier, out failure))
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
            return new StepPlan(npc, action, DialogText.Substitute(text, _request.Names), replies.AsReadOnly(), greeting, insultLine);
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
    private ReplyPlan? PlanReply(DlgRecord record, DialogNodeId node, out DialogConversationFrontier? frontier, out DialogConversationFailure? failure,
        DlgRecord? gates = null, bool nestedQuest = false)
    {
        frontier = null;
        failure = null;
        // ARC-SCRIPT-1532: a q: reply's chosen record gives the text, response and action; the gates (gender, IQ, condition) stay the q: record's (0x416C10 only recurses for the record).
        var gate = gates ?? record;
        var response = new DialogResponseId(record.Id);
        var gender = ParseGender(gate);
        if (gender is null)
        {
            failure = new DialogConversationFailure(DialogConversationFailureKind.InvalidOperation, node,
                new DialogOperationSource(new DialogNodeId(gate.Id), gate.SerializedIndex, DlgFieldKind.AlternateText,
                    gate.AlternateText.PayloadOffset, 0, gate.AlternateText.Length, 0), response);
            return null;
        }
        var condition = ParseField(gate, gate.Condition, ConversationField.PlayerCondition);
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
        DialogOperationSource? barter = null;
        DialogOperationSource? worldMap = null;
        int[]? trainer = null;
        RumorOffer? rumor = null;
        var story = false;
        var healer = false;
        TrainerChoice? service = null;
        var newspaper = false;
        DirectionsOffer? directions = null;
        var found = DialogText.Marker(text, out var exact);
        if (found is { Letter: 'd' or 'x' })
        {
            // ARC-SCRIPT-1533: the directions replies (0x416C10): with the directions port, the known-area port and the lines the reply is built; its visibility (an area the PC does
            // not know) is read when the offers are built. Without them it stays a static frontier, as the other replies whose visibility depends on state this runtime cannot read.
            directions = ToDirections(found is { Letter: 'x' }, ParseDirectionsReply(text));
        }
        if (found is { Letter: 'q' } && !nestedQuest && QuestReplyAvailable && ParseQuestReply(text) is { } questReply)
        {
            // ARC-SCRIPT-1532 (ledger DQR1): the reply is chosen when the offers are built (BuildOffers → ResolveQuestReply): the record it shows depends on the PC's quest state.
            return new ReplyPlan(record, gender.Value, record.IqValue!.Value, condition, DialogOptionTextKind.Record, string.Empty, null, DialogOptionTransition.Exit, null, null, null, null,
                null, QuestReply: questReply);
        }
        if (found is { Letter: 'u' } && IsUnreachableServiceReply(text))
        {
            // ARC-SCRIPT-1534 (ledger DSV1), ARC-SCRIPT-1798 (DSV4): the repair service reply "u:12" and the pick-locks reply "u:14" are never offered by the original (0x416C10 → 0x4AE570 cases 12
            // and 14 refuse a target that is no item (portal or container for 14), and the target is the PC): it is planned as a reply that is counted among the five eligible ones and then left out, as a directions reply with nothing to ask for.
            return new ReplyPlan(record, gender.Value, record.IqValue!.Value, condition, DialogOptionTextKind.Record, string.Empty, null, DialogOptionTransition.Exit, null, null, null, null,
                null, NeverOffered: true);
        }
        if (found is { Letter: 'q' } || found is { Letter: 'u' } && !ServiceAvailable || found is { Letter: 'd' or 'x' } && directions is null)
        {
            // Their visibility depends on world, quest or skill state this runtime cannot read.
            frontier = new DialogConversationFrontier(DialogFrontierKind.UnsupportedGeneratedText, node, Response: response);
            return null;
        }
        // ARC-SCRIPT-043: an exact marker's text is a generated phrase, picked when the reply is offered (BuildOffers).
        var phrase = exact && found is { Letter: 'a' or 'b' or 'e' or 'f' or 'h' or 'i' or 'k' or 'l' or 'n' or 'p' or 's' or 'w' or 'y' };
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
        else if (found is { Letter: 'b' } && exact && _ports.Barter is not null)
        {
            // ARC-UI-050: the original's barter reply (exactly "b:", 0x416C10 → 0x4182D0): a generated phrase (ARC-SCRIPT-043) whose pick runs
            // the reply's action and then opens the barter window (state 3, 0x567E30); its response value is not followed.
            kind = DialogOptionTextKind.Generated;
            marker = found;
            text = string.Empty;
            barter = new DialogOperationSource(new DialogNodeId(record.Id), record.SerializedIndex, DlgFieldKind.Text,
                record.Text.PayloadOffset, 0, 2, 0);
        }
        else if (found is { Letter: 'l' } && exact && _ports.WorldMap is not null)
        {
            // ARC-SCRIPT-1358 (ledger VA5): the original's world map reply (exactly "l:", 0x416C10): a generated phrase (PC lines 2300..2399) whose
            // pick sets state 30 (0x414810), so the dialog ends and the world map opens (0x567E30, 0x560760); the reply's response is not followed.
            kind = DialogOptionTextKind.Generated;
            marker = found;
            text = string.Empty;
            worldMap = new DialogOperationSource(new DialogNodeId(record.Id), record.SerializedIndex, DlgFieldKind.Text,
                record.Text.PayloadOffset, 0, 2, 0);
        }
        else if (found is { Letter: 't' } && TrainerAvailable && ParseTrainerSkills(text) is { } skills)
        {
            // ARC-SCRIPT-1150 (ledger DTR1): the original's trainer reply (the prefix "t:", either case, 0x416C10): a generated phrase (PC lines
            // 500..599) whose pick runs the reply's action and then opens the skill menu of the numbers after the marker (state 5, 0x414810).
            kind = DialogOptionTextKind.Generated;
            marker = found;
            text = string.Empty;
            phrase = true;
            trainer = skills;
        }
        else if (found is { Letter: 'r' } && ToOffer(ParseRumorReply(text)) is { } offer && RumorAvailable(offer))
        {
            // ARC-SCRIPT-1304 (ledger RMR1): the original's rumor reply (the prefix "r:", either case, 0x416C10): a generated phrase (the PC's class line
            // 2000) whose pick runs the reply's action and then draws one rumor of the list after the marker (state 8, 0x414810).
            kind = DialogOptionTextKind.Generated;
            marker = found;
            text = string.Empty;
            phrase = true;
            rumor = offer;
        }
        else if (directions is not null)
        {
            // ARC-SCRIPT-1533 (ledger DIR1): the original's directions reply (the prefix "d:" or "x:", either case, 0x416C10): a generated phrase (PC lines 1300..1399, 1400..1499) whose
            // pick runs the reply's action and then lists the areas of the text the PC does not know (states 0x12 and 0x15, 0x414810); the reply's response is followed on leaving the talk.
            kind = DialogOptionTextKind.Generated;
            marker = found;
            text = string.Empty;
            phrase = true;
        }
        else if (found is { Letter: 'c' } && StoryAvailable)
        {
            // ARC-SCRIPT-1321 (ledger STY1): the original's story reply (the prefix "c:", either case, 0x416C10): a generated phrase (the single PC line 1700 + the story
            // state, 0x418460) whose pick runs the reply's action and then tells the story line (state 0x18, 0x414810); the text after the marker is not read.
            kind = DialogOptionTextKind.Generated;
            marker = found;
            text = string.Empty;
            phrase = true;
            story = true;
        }
        else if (found is { Letter: 'p' } && exact && NewspaperAvailable)
        {
            // ARC-UI-1739 (ledger NP1): the original's newspaper reply (exactly "p:", either case, 0x416C10): a generated phrase (PC lines 1900..1999) whose pick runs the reply's action and then
            // opens the vendor's greeting (state 0x19, 0x414810); the reply's response is followed when the player leaves the talk.
            kind = DialogOptionTextKind.Generated;
            marker = found;
            text = string.Empty;
            phrase = true;
            newspaper = true;
        }
        else if (found is { Letter: 'h' } && exact && HealerAvailable)
        {
            // ARC-SCRIPT-1732 (ledger HLR1): the original's healer reply (exactly "h:", 0x416C10): a generated phrase (PC lines 700..799) whose pick runs the reply's action
            // and then opens the healer menu (state 11, 0x414810 → 0x419830).
            kind = DialogOptionTextKind.Generated;
            marker = found;
            text = string.Empty;
            phrase = true;
            healer = true;
        }
        else if (found is { Letter: 'u' or 'z' } && ServiceAvailable)
        {
            // ARC-SCRIPT-1798 (ledger DSV1, DSV3): the original's skill service reply (the prefix "u:", N by atoi) and spell reply (the prefix "z:", N by atoi), either case, 0x416C10: a generated
            // phrase (PC lines 900..999 and 1000..1099 with the skill's or spell's name) whose pick runs the reply's action and then offers the skill or spell (states 0xE and 0xF, 0x414810 → 0x419B50, 0x419C30).
            var number = ReadAtoi(text, 2);
            kind = DialogOptionTextKind.Generated;
            marker = found;
            text = string.Empty;
            phrase = true;
            healer = true;
            service = found is { Letter: 'u' } ? new TrainerChoice(TrainerChoiceKind.HealService, number) : new TrainerChoice(TrainerChoiceKind.HealSpell, Spell: number);
        }
        else if (found is { Letter: 'b' or 'h' or 'l' or 'p' } && exact || found is { Letter: 'c' or 'r' or 't' or 'z' })
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
        DialogConversationFailure? targetFailure = null;
        DlgRecord? target = null;
        DialogScriptLine? line = null;
        if (block is not null)
        {
            transition = DialogOptionTransition.Unsupported;
        }
        else if (barter is not null)
        {
            transition = DialogOptionTransition.Barter;
        }
        else if (worldMap is not null)
        {
            transition = DialogOptionTransition.WorldMap;
        }
        else if (value > 0)
        {
            transition = DialogOptionTransition.Direct;
            // A missing or non-NPC target fails the step, unless the reply's action holds SU, whose reply never enters it (checked
            // after the action is parsed below).
            if (!_document.TryGetRecord(value, out target) || target is null)
            {
                targetFailure = new DialogConversationFailure(DialogConversationFailureKind.MissingTarget, node, Response: response);
            }
            else if (target.Role != DlgRecordRole.Npc)
            {
                targetFailure = new DialogConversationFailure(DialogConversationFailureKind.WrongTargetRole, node, Response: response);
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
                action = ParseField(record, record.Action, ConversationField.PlayerAction);
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
        if (targetFailure is not null)
        {
            if (block is not null || action?.Any(OpensWindow) != true)
            {
                failure = targetFailure;
                return null;
            }
            target = null;
        }
        // The dialog transfer of the action, if any: the grammar lets only a co follow it, after an FL (ARC-DEV-190).
        var transfer = action?.LastOrDefault(operation => operation is ConversationJoin or ConversationUnwait or ConversationFinalLine or ConversationExpertTeacher);
        if (block is null && insult is not null)
        {
            if (_ports.World is null)
            {
                block = new DialogOptionBlock(DialogOptionBlockKind.UnsupportedOperation, insult);
            }
            else if (transfer is not null)
            {
                // An action that moves the dialog itself skips the insult in the original (0x413130; read for FL); the
                // transfer tails are kept out of this subset rather than modelled.
                block = new DialogOptionBlock(DialogOptionBlockKind.UnsupportedInteraction, transfer.Source);
            }
        }
        if (block is null && (trainer is not null || rumor is not null || story || healer || newspaper || directions is not null) && (transfer is not null || action?.Any(OpensWindow) == true))
        {
            // A dialog transfer or a window in the same reply moves the dialog itself: outside this subset, as for b:.
            block = new DialogOptionBlock(DialogOptionBlockKind.UnsupportedInteraction, transfer?.Source ?? action!.First(OpensWindow).Source);
        }
        if (block is null && transfer is not null)
        {
            // JO/UW/FL/ET override the response: the source skips the response transition after them.
            var continuation = transfer switch
            {
                ConversationJoin join => join.Node,
                ConversationUnwait unwait => unwait.Node,
                ConversationExpertTeacher teacher => teacher.Node,
                _ => ((ConversationFinalLine)transfer).Node,
            };
            if (transfer is ConversationFinalLine && (!_document.TryGetRecord(continuation, out var absent) || absent is null))
            {
                // ARC-SCRIPT-2790 (0x415BA0 case 3, 0x413A30, 0x416840, 0x416AB0; ledger FLM1): an FL whose node is not in the DLG is no error in the original:
                // the line lookup fails, the line stays a single space with no voice, and the string goes on (a co still runs); the dialog ends with that blank
                // final line. The reply stays offered, with no target (the select step reads the node from the action).
                transition = DialogOptionTransition.FinalLine;
                target = null;
                line = null;
            }
            else if (!_document.TryGetRecord(continuation, out var next) || next is null || next.Role != DlgRecordRole.Npc)
            {
                block = new DialogOptionBlock(DialogOptionBlockKind.TargetInvalid, transfer.Source);
            }
            else
            {
                transition = transfer switch
                {
                    ConversationFinalLine => DialogOptionTransition.FinalLine,
                    ConversationExpertTeacher => DialogOptionTransition.ExpertTeacher,
                    _ => DialogOptionTransition.Join,
                };
                target = next;
                line = null;
            }
        }
        if (block is null && barter is not null && action is not null && action.Any(operation => operation is ConversationJoin or ConversationUnwait or
                ConversationFinalLine or ConversationExpertTeacher or ConversationSchematics or ConversationWindow))
        {
            // A dialog transfer or the schematic window in the same reply is outside this subset.
            block = new DialogOptionBlock(DialogOptionBlockKind.UnsupportedInteraction, barter);
        }
        if (block is null && worldMap is not null && action is not null && action.Any(operation => operation is ConversationJoin or ConversationUnwait or
                ConversationFinalLine or ConversationExpertTeacher or ConversationSchematics or ConversationWindow))
        {
            // A dialog transfer or a window in the same reply is outside this subset.
            block = new DialogOptionBlock(DialogOptionBlockKind.UnsupportedInteraction, worldMap);
        }
        if (block is null && action is not null && action.LastOrDefault(OpensWindow) is { } schematics)
        {
            // SU (0x415BA0) sets the pending window code and every action of the field still runs; the reply's target node is never
            // entered. The last window action of the field decides the code (ARC-DEV-229: the NPC sheet's, ii and ri set 5, 8 and 9 the same way).
            // A dialog transfer (JO/UW/FL/ET) or an insult in the same reply is outside this subset.
            if (insult is not null || action.Any(operation => operation is ConversationJoin or ConversationUnwait or ConversationFinalLine or ConversationExpertTeacher))
            {
                block = new DialogOptionBlock(DialogOptionBlockKind.UnsupportedInteraction, schematics.Source);
            }
            else
            {
                transition = schematics is ConversationWindow window
                    ? window.Kind switch
                    {
                        DialogWindowKind.NpcSheet => DialogOptionTransition.NpcSheet,
                        DialogWindowKind.ItemIdentify => DialogOptionTransition.ItemIdentify,
                        _ => DialogOptionTransition.ItemRepair,
                    }
                    : DialogOptionTransition.Schematics;
                target = null;
                line = null;
            }
        }
        return new ReplyPlan(record, gender.Value, gate.IqValue!.Value, condition, kind, text, marker, transition, target, line, action, block,
            block is null ? insult : null, block is null ? barter : null, block is null ? worldMap : null, phrase, block is null ? trainer : null, block is null ? rumor : null, block is null && story,
            Directions: block is null ? directions : null, DirectionsFilter: directions, Newspaper: block is null && newspaper, Healer: block is null && healer, Service: block is null ? service : null, ServiceFilter: service);
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
        ConversationTakeItem => _ports.ItemTake is null,
        ConversationCompanionGate or ConversationCompanionAction => _ports.Companion is null,
        ConversationCounter => _ports.Counters is null,
        ConversationCollegeGate => _ports.Colleges is null,
        ConversationFollowerGate => _ports.FollowerNames is null,
        ConversationKill => _ports.Kill is null,
        ConversationNewspaper => _ports.Newspaper is null,
        ConversationOrigin => _ports.Origins is null,
        ConversationJoin => _ports.World is null && _ports.Party is null,
        ConversationWaiting or ConversationUnwait => _ports.Party is null,
        ConversationMetBefore => _ports.MetBefore is null,
        ConversationReputation => _ports.Reputations is null,
        ConversationQuellRumor or ConversationRumorGate or ConversationQuellGate => _ports.Rumors is null,
        ConversationSkillGate or ConversationSkillTraining or ConversationExpertTeacher => _ports.Skills is null,
        ConversationFatePoint => _ports.Fate is null,
        ConversationExperience => _ports.Experience is null,
        ConversationGold or ConversationGoldGate => _ports.Gold is null,
        ConversationQuestGate => _ports.Quests is null,
        ConversationCombat => _ports.Combat is null,
        ConversationStoryGate or ConversationStoryState => _ports.Story is null,
        ConversationKnownAreaGate => _ports.Areas is null,
        ConversationMapAreaGate => _ports.MapArea is null,
        ConversationStatGate => _ports.Stats is null,
        ConversationItemGate => _ports.ItemHolds is null,
        ConversationSchematics => _ports.Schematics is null,
        ConversationWindow window => _ports.Windows?.Offers(window.Kind) != true,
        ConversationScriptStore => _ports.Stores is null,
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

    private sealed record StepPlan(DlgRecord Npc, IReadOnlyList<ConversationOperation> Action, string NpcText, IReadOnlyList<ReplyPlan> Replies,
        GreetingPlan? Greeting = null, bool InsultLine = false, NpcTalkPlan? Talk = null);

    /// <summary>A greeting's slots and the planned nodes of its jump and floating slots.</summary>
    private sealed record GreetingPlan(DialogGreetingSlot[] Slots, IReadOnlyDictionary<int, StepPlan> Jumps, IReadOnlyDictionary<int, StepPlan> Floats);

    /// <summary>
    /// The slots of a <c>g:</c> node (its record text after the marker) and the plans of the nodes they name: a jump node with its
    /// replies, a floating node without. A named node that is missing, no NPC node, itself a greeting or not plannable makes the
    /// greeting node fail or stop like a reply target.
    /// </summary>
    private GreetingPlan? PlanGreeting(DlgRecord npc, IReadOnlyList<byte> raw, DialogNodeId node, out DialogConversationFrontier? frontier,
        out DialogConversationFailure? failure)
    {
        frontier = null;
        failure = null;
        var slots = DialogGreeting.ParseSlots(System.Text.Encoding.Latin1.GetString(raw.Skip(2).ToArray()));
        var jumps = new Dictionary<int, StepPlan>();
        var floats = new Dictionary<int, StepPlan>();
        foreach (var slot in slots)
        {
            if (slot.Kind is not (DialogGreetingSlotKind.Jump or DialogGreetingSlotKind.Float)) continue;
            var into = slot.Kind == DialogGreetingSlotKind.Jump ? jumps : floats;
            if (into.ContainsKey(slot.Node)) continue;
            if (!_document.TryGetRecord(slot.Node, out var target) || target is null)
            {
                failure = new DialogConversationFailure(DialogConversationFailureKind.MissingTarget, node);
                return null;
            }
            if (target.Role != DlgRecordRole.Npc)
            {
                failure = new DialogConversationFailure(DialogConversationFailureKind.WrongTargetRole, node);
                return null;
            }
            var plan = PlanStep(target, out frontier, out failure, slot.Kind == DialogGreetingSlotKind.Jump, nested: true);
            if (plan is null)
            {
                return null;
            }
            into.Add(slot.Node, plan);
        }
        return new GreetingPlan(slots, jumps, floats);
    }

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
        DialogOperationSource? Insult,
        DialogOperationSource? Barter = null,
        DialogOperationSource? WorldMap = null,
        bool Phrase = false,
        int[]? Trainer = null,
        RumorOffer? Rumor = null,
        bool Story = false,
        int? QuestReply = null,
        DirectionsOffer? Directions = null,
        DirectionsOffer? DirectionsFilter = null,
        bool Newspaper = false,
        bool Healer = false,
        bool NeverOffered = false,
        TrainerChoice? Service = null,
        TrainerChoice? ServiceFilter = null);

    private sealed record OfferPlan(ReplyPlan Reply, StepPlan? Target);
}
