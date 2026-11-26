namespace Arcanum.Scripting.GlobalFlags;

// Transport value types: default/invalid values are rejected at every public operation boundary.
public readonly record struct WorldId(Guid Value);
public readonly record struct ModuleId(Guid Value);
public readonly record struct FlagId(int Value);
public readonly record struct OwnerEpoch(Guid Value);
public readonly record struct GlobalFlagRequestId(Guid Value);
public readonly record struct GlobalFlagIdentity(WorldId World, ModuleId Module);
public readonly record struct GlobalFlagVersion(GlobalFlagIdentity Identity, OwnerEpoch Epoch, long Revision);
public readonly record struct GlobalFlagBootstrapValue(FlagId Flag, bool Value);
public readonly record struct GlobalFlagEntry(FlagId Flag, bool? Value);

public static class GlobalFlagLimits
{
    public const int MaxFlags = 4096;
    public const long MaxRevision = long.MaxValue;
}

public enum GlobalFlagReason
{
    None,
    InvalidIdentity,
    InvalidEpoch,
    InvalidRevision,
    InvalidRequest,
    InvalidFlag,
    InvalidDomain,
    DuplicateFlag,
    InvalidBootstrap,
    DuplicateBootstrap,
    BudgetExceeded,
    WrongWorld,
    WrongModule,
    FlagNotAdmitted,
    StaleEpoch,
    StaleRevision,
    MissingBootstrapValue,
    OwnerUnavailable,
    OwnerDisposed,
    OwnerFailed,
    RevisionExhausted,
    InvalidDescriptor,
    UnsupportedSchema,
    UnsupportedOperation,
    InvalidArity,
    NonAscii,
    InvalidOperand,
    OperandOverflow,
    InvalidBoolean,
    InvalidOwnerResult,
}

public enum GlobalFlagReadStatus { Known, Unknown, Unavailable, Failed, Stale }
public enum GlobalFlagWriteDisposition { Applied, NoChange, Rejected, Indeterminate }
public enum GlobalFlagOperation { Read, Compare, Write }

public readonly record struct GlobalFlagReadRequest(
    GlobalFlagRequestId RequestId, GlobalFlagVersion Expected, FlagId Flag);
public readonly record struct GlobalFlagWriteRequest(
    GlobalFlagRequestId RequestId, GlobalFlagVersion Expected, FlagId Flag, bool Value);

public sealed record GlobalFlagReadResult(
    GlobalFlagRequestId RequestId,
    GlobalFlagVersion Expected,
    FlagId? Flag,
    GlobalFlagReadStatus Status,
    bool? Value,
    GlobalFlagVersion? ResultVersion,
    GlobalFlagReason Reason);

public sealed record GlobalFlagWriteReceipt(
    GlobalFlagRequestId RequestId,
    GlobalFlagOperation Operation,
    GlobalFlagVersion Expected,
    FlagId? Flag,
    bool? Value,
    GlobalFlagVersion? ResultVersion,
    GlobalFlagWriteDisposition Disposition,
    GlobalFlagReason Reason);

/// <summary>Explicit finite bootstrap, not an import of game defaults. Missing bits stay Unknown.</summary>
public sealed record GlobalFlagOwnerDescriptor(
    GlobalFlagIdentity Identity,
    OwnerEpoch Epoch,
    long InitialRevision,
    IReadOnlyList<FlagId> Domain,
    IReadOnlyList<GlobalFlagBootstrapValue> Bootstrap);

public sealed record GlobalFlagCreationResult(GlobalFlagOwner? Owner, GlobalFlagReason Reason);

/// <summary>Immutable owner metadata. Construction is restricted to validated bootstrap.</summary>
public sealed class GlobalFlagBinding
{
    private readonly HashSet<FlagId> _domain;

    internal GlobalFlagBinding(GlobalFlagIdentity identity, OwnerEpoch epoch, FlagId[] domain)
    {
        Identity = identity;
        Epoch = epoch;
        Domain = Array.AsReadOnly(domain);
        _domain = new HashSet<FlagId>(domain);
    }

    public GlobalFlagIdentity Identity { get; }
    public OwnerEpoch Epoch { get; }
    public IReadOnlyList<FlagId> Domain { get; }
    public bool Admits(FlagId flag) => _domain.Contains(flag);
}

/// <summary>
/// Calls and input collections must be serialized by the host. Rejected guarantees no effects.
/// External implementations must preserve request/version binding and bounded execution.
/// </summary>
public interface IGlobalFlagOwner
{
    GlobalFlagBinding Binding { get; }
    GlobalFlagReadResult Read(GlobalFlagReadRequest request);
    GlobalFlagWriteReceipt Write(GlobalFlagWriteRequest request);
}

/// <summary>An owner that also reports its current version, the expected value for the next read or write.</summary>
public interface IVersionedGlobalFlagOwner : IGlobalFlagOwner
{
    GlobalFlagVersion Version { get; }
}

/// <summary>
/// Original new-game GF state (ARC-SCRIPT-006): the source keeps 100 32-bit words of global flags and zeroes
/// all of them on init and reset, so flags 0..3199 are admitted and known false at a new game.
/// </summary>
public static class OriginalGlobalFlags
{
    public const int Words = 100;
    public const int Count = Words * 32;

    public static GlobalFlagOwnerDescriptor NewGame(GlobalFlagIdentity identity, OwnerEpoch epoch)
    {
        var domain = new FlagId[Count];
        var bootstrap = new GlobalFlagBootstrapValue[Count];
        for (var flag = 0; flag < Count; flag++)
        {
            domain[flag] = new FlagId(flag);
            bootstrap[flag] = new GlobalFlagBootstrapValue(new FlagId(flag), false);
        }
        return new GlobalFlagOwnerDescriptor(identity, epoch, 0, domain, bootstrap);
    }
}

internal static class GlobalFlagValidation
{
    internal static GlobalFlagReason Version(GlobalFlagVersion version)
    {
        if (version.Identity.World.Value == Guid.Empty || version.Identity.Module.Value == Guid.Empty)
            return GlobalFlagReason.InvalidIdentity;
        if (version.Epoch.Value == Guid.Empty) return GlobalFlagReason.InvalidEpoch;
        if (version.Revision < 0) return GlobalFlagReason.InvalidRevision;
        return GlobalFlagReason.None;
    }

    internal static GlobalFlagReason Request(
        GlobalFlagBinding binding, GlobalFlagRequestId requestId, GlobalFlagVersion expected, FlagId flag)
    {
        if (requestId.Value == Guid.Empty) return GlobalFlagReason.InvalidRequest;
        var reason = Version(expected);
        if (reason != GlobalFlagReason.None) return reason;
        if (flag.Value < 0) return GlobalFlagReason.InvalidFlag;
        if (expected.Identity.World != binding.Identity.World) return GlobalFlagReason.WrongWorld;
        if (expected.Identity.Module != binding.Identity.Module) return GlobalFlagReason.WrongModule;
        if (!binding.Admits(flag)) return GlobalFlagReason.FlagNotAdmitted;
        if (expected.Epoch != binding.Epoch) return GlobalFlagReason.StaleEpoch;
        return GlobalFlagReason.None;
    }

    internal static GlobalFlagReadStatus Status(GlobalFlagReason reason) => reason switch
    {
        GlobalFlagReason.MissingBootstrapValue => GlobalFlagReadStatus.Unknown,
        GlobalFlagReason.OwnerUnavailable or GlobalFlagReason.OwnerDisposed => GlobalFlagReadStatus.Unavailable,
        GlobalFlagReason.StaleEpoch or GlobalFlagReason.StaleRevision => GlobalFlagReadStatus.Stale,
        _ => GlobalFlagReadStatus.Failed,
    };
}
