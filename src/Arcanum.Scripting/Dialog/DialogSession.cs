using Arcanum.Data;

namespace Arcanum.Scripting.Dialog;

internal sealed record DialogResponsePlan(
    DlgRecord Record,
    DialogResponseEligibility Eligibility,
    LocalFlagOperationPlan? Condition,
    IReadOnlyList<DialogOperationPlan> Action,
    DialogTransitionKind TransitionKind,
    DlgRecord? Target,
    IReadOnlyList<DialogOperationPlan> TargetAction);

public static class DialogTransitionRuntime
{
    public static DialogStartResult Start(
        DlgDocument document,
        DialogStartRequest request,
        IDialogStateQueryPort queries,
        IDialogCommandPort commands,
        DialogRuntimeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Participants);
        ArgumentNullException.ThrowIfNull(request.Eligibility);
        ArgumentNullException.ThrowIfNull(queries);
        ArgumentNullException.ThrowIfNull(commands);
        var effectiveOptions = options ?? new DialogRuntimeOptions();
        effectiveOptions.Validate();

        if (!document.TryGetRecord(request.Entry.Value, out var entry) || entry is null)
        {
            return Failed(new DialogFailure(DialogFailureKind.InvalidEntry, RecordId: request.Entry));
        }
        if (entry.Role != DlgRecordRole.Npc)
        {
            return Failed(new DialogFailure(DialogFailureKind.WrongEntryRole, RecordId: request.Entry));
        }

        var responses = ConsecutiveResponses(document, entry);
        if (responses.Count > effectiveOptions.MaxResponses)
        {
            return Failed(new DialogFailure(DialogFailureKind.ResponseLimitExceeded, RecordId: request.Entry));
        }
        if (!TryEligibility(responses, request.Eligibility, out var eligibilityById))
        {
            return Failed(new DialogFailure(DialogFailureKind.InvalidEligibility, RecordId: request.Entry));
        }

        try
        {
            var budget = new StepBudget(effectiveOptions);
            var cache = new Dictionary<(int SerializedIndex, DlgFieldKind Kind), ParsedField>();
            var sourceAction = ParseField(entry, entry.Action, condition: false, effectiveOptions, cache, budget);
            var plans = new List<DialogResponsePlan>();
            foreach (var response in responses)
            {
                var condition = response.Condition.Length == 0
                    ? null
                    : RequireCondition(ParseField(response, response.Condition, condition: true, effectiveOptions, cache, budget));
                var action = ParseField(response, response.Action, condition: false, effectiveOptions, cache, budget).Operations;
                var kind = response.ResponseKind!.Value;
                DlgRecord? target = null;
                IReadOnlyList<DialogOperationPlan> targetAction = Array.Empty<DialogOperationPlan>();
                if (kind == DlgResponseKind.Direct)
                {
                    if (!document.TryGetRecord(response.ResponseValue!.Value, out target) || target is null)
                    {
                        throw StaticFailure(DialogFailureKind.MissingTarget, response, response.Response);
                    }
                    if (target.Role != DlgRecordRole.Npc)
                    {
                        throw StaticFailure(DialogFailureKind.WrongTargetRole, response, response.Response);
                    }
                    targetAction = ParseField(target, target.Action, condition: false, effectiveOptions, cache, budget).Operations;
                }
                plans.Add(new DialogResponsePlan(
                    response,
                    eligibilityById[response.Id],
                    condition,
                    action,
                    kind switch
                    {
                        DlgResponseKind.Direct => DialogTransitionKind.Direct,
                        DlgResponseKind.Exit => DialogTransitionKind.Exit,
                        _ => DialogTransitionKind.Special,
                    },
                    target,
                    targetAction));
            }
            var session = new DialogSession(request, entry, plans, queries, commands);
            if (!session.Execute(sourceAction.Operations))
            {
                return new DialogStartResult(session, DialogStartStatus.Failed, session.Failure);
            }
            if (!session.RefreshInitialOffers())
            {
                return new DialogStartResult(session, DialogStartStatus.Failed, session.Failure);
            }
            return new DialogStartResult(session, DialogStartStatus.AwaitingSelection, null);
        }
        catch (DialogOperationParseException exception)
        {
            return Failed(new DialogFailure(exception.Kind, exception.OperationSource));
        }
    }

    private static DialogStartResult Failed(DialogFailure failure)
    {
        var session = DialogSession.Failed(failure);
        return new DialogStartResult(session, DialogStartStatus.Failed, failure);
    }

    private static IReadOnlyList<DlgRecord> ConsecutiveResponses(DlgDocument document, DlgRecord entry)
    {
        var result = new List<DlgRecord>();
        for (var index = entry.SerializedIndex + 1; index < document.Records.Count; index++)
        {
            var next = document.Records[index];
            if (next.Role == DlgRecordRole.Npc)
            {
                break;
            }
            result.Add(next);
        }
        return result;
    }

    private static bool TryEligibility(
        IReadOnlyList<DlgRecord> responses,
        IReadOnlyList<DialogResponseEligibility> eligibility,
        out Dictionary<int, DialogResponseEligibility> result)
    {
        result = new Dictionary<int, DialogResponseEligibility>();
        foreach (var item in eligibility)
        {
            if (!result.TryAdd(item.Response.Value, item))
            {
                return false;
            }
        }
        if (result.Count != responses.Count)
        {
            return false;
        }
        foreach (var response in responses)
        {
            if (!result.ContainsKey(response.Id))
            {
                return false;
            }
        }
        return true;
    }

    private static ParsedField ParseField(
        DlgRecord record,
        DlgField field,
        bool condition,
        DialogRuntimeOptions options,
        IDictionary<(int SerializedIndex, DlgFieldKind Kind), ParsedField> cache,
        StepBudget budget)
    {
        var key = (record.SerializedIndex, field.Kind);
        if (cache.TryGetValue(key, out var parsed))
        {
            return parsed;
        }
        budget.AdmitField(record, field);
        if (field.Length == 0)
        {
            parsed = new ParsedField(Array.Empty<DialogOperationPlan>(), 0);
        }
        else
        {
            parsed = DialogOperationParser.Parse(record, field, condition, options, budget.AdmitOperation);
        }
        cache.Add(key, parsed);
        return parsed;
    }

    private static LocalFlagOperationPlan RequireCondition(ParsedField parsed)
    {
        if (parsed.Operations.Count != 1 || parsed.Operations[0] is not LocalFlagOperationPlan condition)
        {
            var source = parsed.Operations.Count == 0
                ? new DialogOperationSource(new DialogNodeId(0), 0, DlgFieldKind.Condition, 0, 0, 0, 0)
                : parsed.Operations[0].Source;
            throw new DialogOperationParseException(DialogFailureKind.InvalidOperation, source);
        }
        return condition;
    }

    private sealed class StepBudget(DialogRuntimeOptions options)
    {
        private long _bytes;
        private long _operations;

        internal void AdmitField(DlgRecord record, DlgField field)
        {
            _bytes += field.Length;
            if (_bytes > options.MaxSemanticInputBytes)
            {
                throw new DialogOperationParseException(
                    DialogFailureKind.SemanticInputLimitExceeded,
                    new DialogOperationSource(
                        new DialogNodeId(record.Id),
                        record.SerializedIndex,
                        field.Kind,
                        field.PayloadOffset,
                        0,
                        field.Length,
                        0));
            }
        }

        internal void AdmitOperation(DialogOperationSource source)
        {
            _operations++;
            if (_operations > options.MaxOperationsPerTransition)
            {
                throw new DialogOperationParseException(DialogFailureKind.OperationLimitExceeded, source);
            }
        }
    }

    private static DialogOperationParseException StaticFailure(DialogFailureKind kind, DlgRecord record, DlgField field) =>
        new(kind, new DialogOperationSource(
            new DialogNodeId(record.Id), record.SerializedIndex, field.Kind, field.PayloadOffset, 0, 0, 0));
}

public sealed class DialogSession
{
    private readonly DialogStartRequest? _request;
    private readonly DlgRecord? _entry;
    private readonly IReadOnlyList<DialogResponsePlan> _responses;
    private readonly IDialogStateQueryPort? _queries;
    private readonly IDialogCommandPort? _commands;
    private readonly List<DialogCommandAttempt> _attempts = [];
    private IReadOnlyList<DialogResponseOffer> _offers = Array.Empty<DialogResponseOffer>();
    private int _generation;

    private DialogSession(DialogFailure failure)
    {
        Phase = DialogSessionPhase.Failed;
        Failure = failure;
        _responses = Array.Empty<DialogResponsePlan>();
    }

    internal DialogSession(
        DialogStartRequest request,
        DlgRecord entry,
        IReadOnlyList<DialogResponsePlan> responses,
        IDialogStateQueryPort queries,
        IDialogCommandPort commands)
    {
        _request = request;
        _entry = entry;
        _responses = responses;
        _queries = queries;
        _commands = commands;
        CurrentNodeId = new DialogNodeId(entry.Id);
        Phase = DialogSessionPhase.AwaitingSelection;
    }

    public DialogSessionPhase Phase { get; private set; }
    public DialogNodeId CurrentNodeId { get; private set; }
    public IReadOnlyList<DialogResponseOffer> Offers => _offers;
    public IReadOnlyList<DialogCommandAttempt> CommandAttempts => _attempts.AsReadOnly();
    public DialogFailure? Failure { get; private set; }

    internal static DialogSession Failed(DialogFailure failure) => new(failure);

    public DialogOfferRefreshResult RefreshOffers()
    {
        if (Phase != DialogSessionPhase.AwaitingSelection)
        {
            return new DialogOfferRefreshResult(DialogOfferRefreshStatus.WrongPhase, Failure);
        }
        _generation++;
        return RebuildOffers(null)
            ? new DialogOfferRefreshResult(DialogOfferRefreshStatus.Updated, null)
            : new DialogOfferRefreshResult(DialogOfferRefreshStatus.Failed, Failure);
    }

    public DialogSelectionResult Select(DialogOfferToken token)
    {
        if (Phase != DialogSessionPhase.AwaitingSelection)
        {
            return new DialogSelectionResult(DialogSelectionStatus.WrongPhase, Failure);
        }
        var offer = _offers.FirstOrDefault(current => current.Token == token);
        if (offer is null)
        {
            return new DialogSelectionResult(DialogSelectionStatus.NotOffered, null);
        }
        var plan = _responses.Single(response => response.Record.Id == token.Response.Value);
        if (plan.TransitionKind != DialogTransitionKind.Direct)
        {
            return new DialogSelectionResult(DialogSelectionStatus.UnsupportedTransition, null);
        }
        if (!IsGateOpen(plan))
        {
            if (Phase == DialogSessionPhase.Failed)
            {
                return new DialogSelectionResult(DialogSelectionStatus.Failed, Failure);
            }
            _generation++;
            if (!RebuildOffers(new Dictionary<int, bool> { [plan.Record.Id] = false }))
            {
                return new DialogSelectionResult(DialogSelectionStatus.Failed, Failure);
            }
            return new DialogSelectionResult(DialogSelectionStatus.StaleSelection, null);
        }
        if (!Execute(plan.Action) || !Execute(plan.TargetAction))
        {
            return new DialogSelectionResult(DialogSelectionStatus.Failed, Failure);
        }
        CurrentNodeId = new DialogNodeId(plan.Target!.Id);
        Phase = DialogSessionPhase.CompletedAtFrontier;
        return new DialogSelectionResult(DialogSelectionStatus.Completed, null);
    }

    internal bool Execute(IReadOnlyList<DialogOperationPlan> plans)
    {
        foreach (var plan in plans)
        {
            var command = ToCommand(plan);
            DialogCommandResult result;
            try
            {
                result = command switch
                {
                    LocalFlagWriteCommand localFlag => _commands!.Execute(localFlag),
                    QuestStateCommand quest => _commands!.Execute(quest),
                    RumorLogCommand rumor => _commands!.Execute(rumor),
                    _ => throw new InvalidOperationException("Unknown dialog command."),
                };
            }
            catch (Exception exception)
            {
                _attempts.Add(new DialogCommandAttempt(command, plan.Source, _attempts.Count, DialogCommandAttemptOutcome.Indeterminate));
                Fail(new DialogFailure(DialogFailureKind.CommandException, plan.Source, Exception: exception));
                return false;
            }
            if (result != DialogCommandResult.Applied)
            {
                _attempts.Add(new DialogCommandAttempt(command, plan.Source, _attempts.Count, DialogCommandAttemptOutcome.Rejected));
                Fail(new DialogFailure(DialogFailureKind.CommandRejected, plan.Source));
                return false;
            }
            _attempts.Add(new DialogCommandAttempt(command, plan.Source, _attempts.Count, DialogCommandAttemptOutcome.Applied));
        }
        return true;
    }

    internal bool RefreshInitialOffers() => RebuildOffers(null);

    private bool RebuildOffers(IReadOnlyDictionary<int, bool>? cachedConditions)
    {
        var offers = new List<DialogResponseOffer>();
        foreach (var plan in _responses)
        {
            if (!plan.Eligibility.GenderEligible || !plan.Eligibility.IntelligenceEligible)
            {
                continue;
            }
            var open = cachedConditions is not null && cachedConditions.TryGetValue(plan.Record.Id, out var cached)
                ? cached
                : IsGateOpen(plan);
            if (Phase == DialogSessionPhase.Failed)
            {
                return false;
            }
            if (!open)
            {
                continue;
            }
            offers.Add(new DialogResponseOffer(
                new DialogResponseId(plan.Record.Id),
                plan.Record.SerializedIndex,
                plan.TransitionKind,
                plan.Target is null ? null : new DialogNodeId(plan.Target.Id),
                plan.TransitionKind == DialogTransitionKind.Special ? plan.Record.ResponseValue : null,
                new DialogOfferToken(new DialogResponseId(plan.Record.Id), _generation)));
        }
        _offers = offers.AsReadOnly();
        return true;
    }

    private bool IsGateOpen(DialogResponsePlan plan)
    {
        if (!plan.Eligibility.GenderEligible || !plan.Eligibility.IntelligenceEligible || plan.Condition is null)
        {
            return plan.Eligibility.GenderEligible && plan.Eligibility.IntelligenceEligible;
        }
        LocalFlagReadResult result;
        try
        {
            result = _queries!.Read(new LocalFlagQuery(_request!.Participants.Dialog, _request.Participants.Npc, plan.Condition.Index));
        }
        catch (Exception exception)
        {
            Fail(new DialogFailure(DialogFailureKind.QueryException, plan.Condition.Source, Exception: exception));
            return false;
        }
        if (!result.IsValidFactoryValue ||
            (result.Status == LocalFlagReadStatus.Found && result.Value is not (0 or 1)) ||
            (result.Status == LocalFlagReadStatus.Missing && result.Value != 0) ||
            result.Status is not (LocalFlagReadStatus.Found or LocalFlagReadStatus.Missing))
        {
            Fail(new DialogFailure(DialogFailureKind.InvalidQueryResult, plan.Condition.Source));
            return false;
        }
        return result.Status == LocalFlagReadStatus.Found && result.Value == plan.Condition.Value;
    }

    private DialogCommand ToCommand(DialogOperationPlan plan) => plan switch
    {
        LocalFlagOperationPlan localFlag => new LocalFlagWriteCommand(
            _request!.Participants.Dialog, _request.Participants.Npc, localFlag.Index, localFlag.Value),
        QuestOperationPlan quest => new QuestStateCommand(
            _request!.Participants.PlayerCharacter, quest.QuestId, quest.State, _request.Participants.Npc),
        RumorOperationPlan rumor => new RumorLogCommand(_request!.Participants.PlayerCharacter, rumor.RumorId),
        _ => throw new InvalidOperationException("Unknown dialog operation."),
    };

    private void Fail(DialogFailure failure)
    {
        Failure = failure;
        Phase = DialogSessionPhase.Failed;
        _offers = Array.Empty<DialogResponseOffer>();
    }
}
