using Arcanum.Data;

namespace Arcanum.Scripting.Dialog;

public readonly record struct DialogId(int Value);
public readonly record struct DialogNodeId(int Value);
public readonly record struct DialogResponseId(int Value);
public readonly record struct DialogParticipantId(long Value);

public sealed record DialogParticipants(
    DialogId Dialog,
    DialogParticipantId PlayerCharacter,
    DialogParticipantId Npc);

public readonly record struct DialogResponseEligibility(
    DialogResponseId Response,
    bool GenderEligible,
    bool IntelligenceEligible);

public sealed record DialogStartRequest(
    DialogNodeId Entry,
    DialogParticipants Participants,
    IReadOnlyList<DialogResponseEligibility> Eligibility);

public sealed class DialogRuntimeOptions
{
    public int MaxResponses { get; init; } = 5;
    public int MaxOperationFieldBytes { get; init; } = 4096;
    public int MaxOperationsPerField { get; init; } = 32;
    public int MaxOperationsPerTransition { get; init; } = 128;
    public int MaxSemanticInputBytes { get; init; } = 65536;

    internal void Validate()
    {
        if (MaxResponses is <= 0 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxResponses));
        }
        if (MaxOperationFieldBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxOperationFieldBytes));
        }
        if (MaxOperationsPerField <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxOperationsPerField));
        }
        if (MaxOperationsPerTransition <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxOperationsPerTransition));
        }
        if (MaxSemanticInputBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxSemanticInputBytes));
        }
    }
}

public enum LocalFlagReadStatus
{
    Found,
    Missing,
}

public readonly record struct LocalFlagReadResult
{
    private readonly bool _isFactoryValue;

    private LocalFlagReadResult(LocalFlagReadStatus status, int value)
    {
        Status = status;
        Value = value;
        _isFactoryValue = true;
    }

    public LocalFlagReadStatus Status { get; }
    public int Value { get; }

    public static LocalFlagReadResult Found(int value)
    {
        if (value is not (0 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
        return new LocalFlagReadResult(LocalFlagReadStatus.Found, value);
    }

    public static LocalFlagReadResult Missing() => new(LocalFlagReadStatus.Missing, 0);

    internal bool IsValidFactoryValue => _isFactoryValue;
}

public readonly record struct DialogOperationSource(
    DialogNodeId RecordId,
    int SerializedIndex,
    DlgFieldKind FieldKind,
    int FieldPayloadOffset,
    int OperationOffsetInField,
    int OperationLength,
    int OperationOrdinal);

public enum DialogTransitionKind
{
    Direct,
    Exit,
    Special,
}

public readonly record struct DialogOfferToken(DialogResponseId Response, int Generation);

public sealed record DialogResponseOffer(
    DialogResponseId ResponseId,
    int SerializedIndex,
    DialogTransitionKind TransitionKind,
    DialogNodeId? DirectTarget,
    int? SpecialTag,
    DialogOfferToken Token);

public enum DialogSessionPhase
{
    AwaitingSelection,
    CompletedAtFrontier,
    Failed,
}

public enum DialogStartStatus
{
    AwaitingSelection,
    Failed,
}

public enum DialogOfferRefreshStatus
{
    Updated,
    WrongPhase,
    Failed,
}

public enum DialogSelectionStatus
{
    Completed,
    StaleSelection,
    NotOffered,
    UnsupportedTransition,
    WrongPhase,
    Failed,
}

public enum DialogCommandAttemptOutcome
{
    Applied,
    Rejected,
    Indeterminate,
    /// <summary>ARC-WORLD-044: the owner skipped the command (a reputation number it does not admit); the dialog went on.</summary>
    Skipped,
}

public enum DialogFailureKind
{
    InvalidEntry,
    WrongEntryRole,
    InvalidEligibility,
    ResponseLimitExceeded,
    SemanticInputLimitExceeded,
    OperationLimitExceeded,
    UnsupportedOperation,
    InvalidOperation,
    MissingTarget,
    WrongTargetRole,
    InvalidQueryResult,
    QueryException,
    CommandRejected,
    CommandException,
}

public sealed record DialogFailure(
    DialogFailureKind Kind,
    DialogOperationSource? Source = null,
    DialogNodeId? RecordId = null,
    DialogResponseId? ResponseId = null,
    Exception? Exception = null);

public sealed record DialogCommandAttempt(
    DialogCommand Command,
    DialogOperationSource Source,
    int Ordinal,
    DialogCommandAttemptOutcome Outcome);

public sealed record DialogStartResult(
    DialogSession Session,
    DialogStartStatus Status,
    DialogFailure? Failure);

public sealed record DialogOfferRefreshResult(
    DialogOfferRefreshStatus Status,
    DialogFailure? Failure);

public sealed record DialogSelectionResult(
    DialogSelectionStatus Status,
    DialogFailure? Failure);
