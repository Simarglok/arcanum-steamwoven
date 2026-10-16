namespace Arcanum.Data;

public sealed class DlgReaderOptions
{
    public int MaxInputBytes { get; init; } = 512 * 1024;
    public int MaxRecordCount { get; init; } = 10_000;
    public int MaxFieldBytes { get; init; } = 4 * 1024;
    /// <summary><see cref="DlgReadMode.Strict"/> (default) refuses anything outside the
    /// documented record grammar; <see cref="DlgReadMode.Original"/> reads as the original game
    /// does (ARC-DATA-017).</summary>
    public DlgReadMode Mode { get; init; } = DlgReadMode.Strict;

    internal void Validate()
    {
        if (MaxInputBytes <= 0 || MaxRecordCount <= 0 || MaxFieldBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(DlgReaderOptions), "All reader limits must be positive.");
        }
        if (!Enum.IsDefined(Mode))
        {
            throw new ArgumentOutOfRangeException(nameof(DlgReaderOptions), "The DLG read mode is not defined.");
        }
    }
}

public enum DlgReadMode
{
    /// <summary>Records of seven adjacent brace fields, strict decimal numbers, unique ids.</summary>
    Strict,
    /// <summary>As the pinned CE <c>dialog_parse_field</c>/<c>dialog_parse_entry</c>: every byte
    /// between fields is skipped (a <c>//</c> comment to the end of its line), fields flow
    /// regardless of lines, numbers are read like C <c>atoi</c>, and reading stops at the first
    /// record the original stops at, keeping the records before it.</summary>
    Original,
}

/// <summary>Why an <see cref="DlgReadMode.Original"/> read ended.</summary>
public enum DlgStopReason
{
    EndOfInput,
    /// <summary>The input ended inside a record; that record is dropped.</summary>
    IncompleteRecord,
    /// <summary>An IQ field that is not empty but reads as 0; the original stops here.</summary>
    ZeroIq,
    /// <summary>A response field starting with <c>#</c>; the original stops here.</summary>
    HashResponse,
}

public enum DlgFieldKind
{
    Id,
    Text,
    AlternateText,
    Iq,
    Condition,
    Response,
    Action,
}

public enum DlgRecordRole
{
    Npc,
    Player,
}

public enum DlgResponseKind
{
    Exit,
    Direct,
    Special,
}

public class DlgException : Exception
{
    public DlgException(string message, int offset, int? recordIndex = null, DlgFieldKind? fieldKind = null)
        : base(message)
    {
        Offset = offset;
        RecordIndex = recordIndex;
        FieldKind = fieldKind;
    }

    public int Offset { get; }
    public int? RecordIndex { get; }
    public DlgFieldKind? FieldKind { get; }
}

public class DlgFormatException : DlgException
{
    public DlgFormatException(string message, int offset, int? recordIndex = null, DlgFieldKind? fieldKind = null)
        : base(message, offset, recordIndex, fieldKind) { }
}

public sealed class DlgUnsupportedFormatException : DlgFormatException
{
    public DlgUnsupportedFormatException(string message, int offset, int? recordIndex = null, DlgFieldKind? fieldKind = null)
        : base(message, offset, recordIndex, fieldKind) { }
}

public sealed class DlgLimitExceededException : DlgException
{
    public DlgLimitExceededException(string message, int offset, int? recordIndex = null, DlgFieldKind? fieldKind = null)
        : base(message, offset, recordIndex, fieldKind) { }
}

public sealed class DlgField
{
    internal DlgField(DlgFieldKind kind, int payloadOffset, byte[] bytes)
    {
        Kind = kind;
        PayloadOffset = payloadOffset;
        Bytes = Array.AsReadOnly(bytes);
    }

    public DlgFieldKind Kind { get; }
    public int PayloadOffset { get; }
    public int Length => Bytes.Count;
    public IReadOnlyList<byte> Bytes { get; }
}

public sealed class DlgRecord
{
    internal DlgRecord(int serializedIndex, int id, DlgRecordRole role, int? iqValue, int? responseValue,
        DlgResponseKind? responseKind, DlgField[] fields)
    {
        SerializedIndex = serializedIndex;
        Id = id;
        Role = role;
        IqValue = iqValue;
        ResponseValue = responseValue;
        ResponseKind = responseKind;
        Fields = Array.AsReadOnly(fields);
    }

    public int SerializedIndex { get; }
    public int Id { get; }
    public DlgRecordRole Role { get; }
    public int? IqValue { get; }
    public int? ResponseValue { get; }
    public DlgResponseKind? ResponseKind { get; }
    public IReadOnlyList<DlgField> Fields { get; }
    public DlgField IdField => Fields[(int)DlgFieldKind.Id];
    public DlgField Text => Fields[(int)DlgFieldKind.Text];
    public DlgField AlternateText => Fields[(int)DlgFieldKind.AlternateText];
    public DlgField Iq => Fields[(int)DlgFieldKind.Iq];
    public DlgField Condition => Fields[(int)DlgFieldKind.Condition];
    public DlgField Response => Fields[(int)DlgFieldKind.Response];
    public DlgField Action => Fields[(int)DlgFieldKind.Action];
}

public sealed class DlgDocument
{
    private readonly IReadOnlyDictionary<int, DlgRecord> _recordsById;

    internal DlgDocument(int payloadLength, DlgRecord[] records, Dictionary<int, DlgRecord> recordsById,
        DlgReadMode mode = DlgReadMode.Strict, DlgStopReason stopReason = DlgStopReason.EndOfInput, int skippedBytes = 0, int duplicateIds = 0)
    {
        PayloadLength = payloadLength;
        Records = Array.AsReadOnly(records);
        _recordsById = new System.Collections.ObjectModel.ReadOnlyDictionary<int, DlgRecord>(recordsById);
        Mode = mode; StopReason = stopReason; SkippedBytes = skippedBytes; DuplicateIds = duplicateIds;
    }

    public int PayloadLength { get; }
    public DlgReadMode Mode { get; }
    /// <summary>Always <see cref="DlgStopReason.EndOfInput"/> in strict mode.</summary>
    public DlgStopReason StopReason { get; }
    /// <summary>Bytes outside fields that are neither white space nor a <c>//</c> comment; the
    /// original skips them. Always 0 in strict mode.</summary>
    public int SkippedBytes { get; }
    /// <summary>Records whose id repeats an earlier one. The original keeps both and its lookup
    /// may find either; <see cref="TryGetRecord"/> returns the first. Always 0 in strict mode.</summary>
    public int DuplicateIds { get; }
    public IReadOnlyList<DlgRecord> Records { get; }

    public bool TryGetRecord(int id, out DlgRecord? record)
    {
        if (_recordsById.TryGetValue(id, out var found))
        {
            record = found;
            return true;
        }

        record = null;
        return false;
    }
}

public enum DlgReferenceStatus
{
    Valid,
    MissingTarget,
    WrongTargetRole,
}

public sealed class DlgReference
{
    internal DlgReference(int sourceRecordId, int? responseValue, DlgResponseKind responseKind, int? targetId, DlgReferenceStatus status)
    {
        SourceRecordId = sourceRecordId;
        ResponseValue = responseValue;
        ResponseKind = responseKind;
        TargetId = targetId;
        Status = status;
    }

    public int SourceRecordId { get; }
    public int? ResponseValue { get; }
    public DlgResponseKind ResponseKind { get; }
    public int? TargetId { get; }
    public DlgReferenceStatus Status { get; }
}

public sealed class DlgReferenceValidationResult
{
    internal DlgReferenceValidationResult(DlgReference[] references)
    {
        References = Array.AsReadOnly(references);
        IsValid = references.All(reference => reference.Status == DlgReferenceStatus.Valid);
    }

    public IReadOnlyList<DlgReference> References { get; }
    public bool IsValid { get; }
}
