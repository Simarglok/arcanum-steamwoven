namespace Arcanum.Scripting.GlobalFlags;

/// <summary>
/// Validates one typed GF descriptor before dispatch. This is not connected to the DLG parser/session.
/// A missing owner is explicit Unavailable, never false or a successful write.
/// </summary>
public sealed class GlobalFlagAdapter
{
    private readonly GlobalFlagBinding _binding;
    private readonly IGlobalFlagOwner? _owner;

    public GlobalFlagAdapter(GlobalFlagBinding binding, IGlobalFlagOwner? owner)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (owner is not null && !ReferenceEquals(binding, owner.Binding))
            throw new ArgumentException("The adapter requires the owner's exact validated binding.", nameof(owner));
        _binding = binding;
        _owner = owner;
    }

    public GlobalFlagConditionResult Compare(
        GlobalFlagRequestId requestId, GlobalFlagVersion expected, GlobalFlagOperationDescriptor? descriptor)
    {
        var reason = Prepare(requestId, expected, descriptor, out var flag, out var value);
        if (reason == GlobalFlagReason.None && _owner is null) reason = GlobalFlagReason.OwnerUnavailable;
        if (reason != GlobalFlagReason.None)
            return new(new(requestId, expected, flag, GlobalFlagValidation.Status(reason), null, null, reason), null);

        GlobalFlagReadResult read;
        try
        {
            read = _owner!.Read(new(requestId, expected, flag!.Value));
        }
        catch (Exception)
        {
            // Read-only port exceptions carry no arbitrary exception text into diagnostics.
            return new(new(requestId, expected, flag, GlobalFlagReadStatus.Failed, null, null, GlobalFlagReason.OwnerFailed), null);
        }
        if (!ValidRead(read, requestId, expected, flag!.Value))
            return new(new(requestId, expected, flag, GlobalFlagReadStatus.Failed, null, null, GlobalFlagReason.InvalidOwnerResult), null);
        return new(read, read.Status == GlobalFlagReadStatus.Known ? read.Value == value : null);
    }

    public GlobalFlagWriteReceipt Write(
        GlobalFlagRequestId requestId, GlobalFlagVersion expected, GlobalFlagOperationDescriptor? descriptor)
    {
        var reason = Prepare(requestId, expected, descriptor, out var flag, out var value);
        if (reason == GlobalFlagReason.None && _owner is null) reason = GlobalFlagReason.OwnerUnavailable;
        if (reason != GlobalFlagReason.None)
            return new(requestId, GlobalFlagOperation.Write, expected, flag, null, null, GlobalFlagWriteDisposition.Rejected, reason);
        try
        {
            var receipt = _owner!.Write(new(requestId, expected, flag!.Value, value));
            return ValidReceipt(receipt, requestId, expected, flag.Value, value)
                ? receipt
                : new(requestId, GlobalFlagOperation.Write, expected, flag, value, null,
                    GlobalFlagWriteDisposition.Indeterminate, GlobalFlagReason.InvalidOwnerResult);
        }
        catch (Exception)
        {
            // An arbitrary external port's exception cannot prove no effect. Production owner has no callbacks.
            return new(requestId, GlobalFlagOperation.Write, expected, flag, value, null,
                GlobalFlagWriteDisposition.Indeterminate, GlobalFlagReason.OwnerFailed);
        }
    }

    private GlobalFlagReason Prepare(
        GlobalFlagRequestId requestId, GlobalFlagVersion expected, GlobalFlagOperationDescriptor? descriptor,
        out FlagId? flag, out bool value)
    {
        flag = null;
        value = false;
        if (requestId.Value == Guid.Empty) return GlobalFlagReason.InvalidRequest;
        var reason = GlobalFlagValidation.Version(expected);
        if (reason != GlobalFlagReason.None) return reason;
        if (descriptor is null || descriptor.Opcode is null || descriptor.Operands is null)
            return GlobalFlagReason.InvalidDescriptor;
        if (descriptor.Schema != GlobalFlagLimits.DescriptorSchema) return GlobalFlagReason.UnsupportedSchema;
        if (descriptor.Opcode.Length != 2) return GlobalFlagReason.UnsupportedOperation;
        if (descriptor.Opcode.Any(c => c > 127)) return GlobalFlagReason.NonAscii;
        if (!descriptor.Opcode.Equals("GF", StringComparison.OrdinalIgnoreCase)) return GlobalFlagReason.UnsupportedOperation;
        if (descriptor.Operands.Count != 2) return GlobalFlagReason.InvalidArity;
        reason = Number(descriptor.Operands[0], out var index);
        if (reason != GlobalFlagReason.None) return reason;
        reason = Number(descriptor.Operands[1], out var bit);
        if (reason != GlobalFlagReason.None) return reason;
        if (bit is not (0 or 1)) return GlobalFlagReason.InvalidBoolean;
        var admitted = new FlagId(index);
        reason = GlobalFlagValidation.Request(_binding, requestId, expected, admitted);
        if (reason != GlobalFlagReason.None) return reason;
        flag = admitted;
        value = bit == 1;
        return GlobalFlagReason.None;
    }

    private bool ValidVersion(GlobalFlagVersion? version) => version is null ||
        (GlobalFlagValidation.Version(version.Value) == GlobalFlagReason.None &&
         version.Value.Identity == _binding.Identity && version.Value.Epoch == _binding.Epoch);

    private bool ValidRead(GlobalFlagReadResult? read, GlobalFlagRequestId requestId, GlobalFlagVersion expected, FlagId flag)
    {
        if (read is null || read.RequestId != requestId || read.Expected != expected || read.Flag != flag ||
            !Enum.IsDefined(read.Status) || !Enum.IsDefined(read.Reason) || !ValidVersion(read.ResultVersion)) return false;
        return read.Status switch
        {
            GlobalFlagReadStatus.Known => read.Value.HasValue && read.Reason == GlobalFlagReason.None && read.ResultVersion == expected,
            GlobalFlagReadStatus.Unknown => read.Value is null && read.Reason == GlobalFlagReason.MissingBootstrapValue && read.ResultVersion == expected,
            GlobalFlagReadStatus.Unavailable => read.Value is null && read.Reason is GlobalFlagReason.OwnerUnavailable or GlobalFlagReason.OwnerDisposed,
            GlobalFlagReadStatus.Stale => read.Value is null && read.Reason is GlobalFlagReason.StaleEpoch or GlobalFlagReason.StaleRevision,
            GlobalFlagReadStatus.Failed => read.Value is null && read.Reason != GlobalFlagReason.None,
            _ => false,
        };
    }

    private bool ValidReceipt(GlobalFlagWriteReceipt? receipt, GlobalFlagRequestId requestId, GlobalFlagVersion expected, FlagId flag, bool value)
    {
        if (receipt is null || receipt.RequestId != requestId || receipt.Operation != GlobalFlagOperation.Write ||
            receipt.Expected != expected || receipt.Flag != flag || receipt.Value != value ||
            !Enum.IsDefined(receipt.Disposition) || !Enum.IsDefined(receipt.Reason) || !ValidVersion(receipt.ResultVersion)) return false;
        return receipt.Disposition switch
        {
            GlobalFlagWriteDisposition.Applied => expected.Revision < GlobalFlagLimits.MaxRevision &&
                receipt.ResultVersion == expected with { Revision = expected.Revision + 1 } && receipt.Reason == GlobalFlagReason.None,
            GlobalFlagWriteDisposition.NoChange => receipt.ResultVersion == expected && receipt.Reason == GlobalFlagReason.None,
            GlobalFlagWriteDisposition.Rejected or GlobalFlagWriteDisposition.Indeterminate => receipt.Reason != GlobalFlagReason.None,
            _ => false,
        };
    }

    private static GlobalFlagReason Number(string? token, out int value)
    {
        value = 0;
        if (token is null || token.Length == 0) return GlobalFlagReason.InvalidOperand;
        if (token.Length > GlobalFlagLimits.MaxOperandCharacters) return GlobalFlagReason.BudgetExceeded;
        foreach (var character in token)
        {
            if (character > 127) return GlobalFlagReason.NonAscii;
            if (character is < '0' or > '9') return GlobalFlagReason.InvalidOperand;
            var digit = character - '0';
            if (value > (int.MaxValue - digit) / 10) return GlobalFlagReason.OperandOverflow;
            value = value * 10 + digit;
        }
        return GlobalFlagReason.None;
    }
}
