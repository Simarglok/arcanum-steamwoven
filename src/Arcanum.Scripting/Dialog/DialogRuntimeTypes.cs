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
    SemanticInputLimitExceeded,
    OperationLimitExceeded,
    UnsupportedOperation,
    InvalidOperation,
}
