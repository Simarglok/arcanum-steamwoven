namespace Arcanum.Scripting.GlobalFlags;

/// <summary>Finite, synchronous in-memory GF owner. The host must serialize all calls.</summary>
public sealed class GlobalFlagOwner : IGlobalFlagOwner, IDisposable
{
    private readonly Dictionary<FlagId, bool?> _values;
    private long _revision;
    private bool _failed;
    private bool _disposed;

    private GlobalFlagOwner(GlobalFlagBinding binding, long revision, Dictionary<FlagId, bool?> values)
    {
        Binding = binding;
        _revision = revision;
        _values = values;
    }

    public GlobalFlagBinding Binding { get; }
    public GlobalFlagVersion Version => new(Binding.Identity, Binding.Epoch, _revision);

    public static GlobalFlagCreationResult Create(GlobalFlagOwnerDescriptor? descriptor)
    {
        if (descriptor is null) return Rejected(GlobalFlagReason.InvalidDescriptor);
        var reason = GlobalFlagValidation.Version(new(descriptor.Identity, descriptor.Epoch, descriptor.InitialRevision));
        if (reason != GlobalFlagReason.None) return Rejected(reason);
        if (descriptor.Domain is null || descriptor.Domain.Count <= 0) return Rejected(GlobalFlagReason.InvalidDomain);
        if (descriptor.Bootstrap is null) return Rejected(GlobalFlagReason.InvalidBootstrap);
        var domainCount = descriptor.Domain.Count;
        var bootstrapCount = descriptor.Bootstrap.Count;
        if (domainCount > GlobalFlagLimits.MaxFlags || bootstrapCount > GlobalFlagLimits.MaxFlags || bootstrapCount < 0)
            return Rejected(GlobalFlagReason.BudgetExceeded);

        // Copy bounded inputs before publication. No partial owner escapes a failed bootstrap.
        var values = new Dictionary<FlagId, bool?>();
        for (var index = 0; index < domainCount; index++)
        {
            var flag = descriptor.Domain[index];
            if (flag.Value < 0) return Rejected(GlobalFlagReason.InvalidFlag);
            if (!values.TryAdd(flag, null)) return Rejected(GlobalFlagReason.DuplicateFlag);
        }
        var seen = new HashSet<FlagId>();
        for (var index = 0; index < bootstrapCount; index++)
        {
            var item = descriptor.Bootstrap[index];
            if (!values.ContainsKey(item.Flag)) return Rejected(GlobalFlagReason.InvalidBootstrap);
            if (!seen.Add(item.Flag)) return Rejected(GlobalFlagReason.DuplicateBootstrap);
            values[item.Flag] = item.Value;
        }
        var domain = values.Keys.OrderBy(flag => flag.Value).ToArray();
        return new(new GlobalFlagOwner(new(descriptor.Identity, descriptor.Epoch, domain), descriptor.InitialRevision, values), GlobalFlagReason.None);
    }

    public GlobalFlagReadResult Read(GlobalFlagReadRequest request)
    {
        var reason = Check(request.RequestId, request.Expected, request.Flag);
        if (reason != GlobalFlagReason.None)
            return new(request.RequestId, request.Expected, request.Flag, GlobalFlagValidation.Status(reason), null, Version, reason);
        var value = _values[request.Flag];
        return new(request.RequestId, request.Expected, request.Flag,
            value.HasValue ? GlobalFlagReadStatus.Known : GlobalFlagReadStatus.Unknown,
            value, Version, value.HasValue ? GlobalFlagReason.None : GlobalFlagReason.MissingBootstrapValue);
    }

    public GlobalFlagWriteReceipt Write(GlobalFlagWriteRequest request)
    {
        var reason = Check(request.RequestId, request.Expected, request.Flag);
        if (reason != GlobalFlagReason.None) return Receipt(request, GlobalFlagWriteDisposition.Rejected, reason);
        if (_values[request.Flag] == request.Value) return Receipt(request, GlobalFlagWriteDisposition.NoChange, GlobalFlagReason.None);
        if (_revision == GlobalFlagLimits.MaxRevision)
            return Receipt(request, GlobalFlagWriteDisposition.Rejected, GlobalFlagReason.RevisionExhausted);

        // Allocate the receipt before committing, so allocation failure cannot leave an unreceipted write.
        var receipt = new GlobalFlagWriteReceipt(request.RequestId, GlobalFlagOperation.Write, request.Expected,
            request.Flag, request.Value, new(Binding.Identity, Binding.Epoch, _revision + 1),
            GlobalFlagWriteDisposition.Applied, GlobalFlagReason.None);
        // All refusal paths precede both updates. No callbacks, allocation or external calls in the commit.
        _values[request.Flag] = request.Value;
        _revision++;
        return receipt;
    }

    /// <summary>Copies data, not a live view. Capture remains diagnostic after failure/disposal.</summary>
    public GlobalFlagSnapshot CaptureSnapshot() => new(Binding, Version,
        Binding.Domain.Select(flag => new GlobalFlagEntry(flag, _values[flag])).ToArray());

    /// <summary>Terminal host-reported failure; preserves flag state/revision, with no exception payload.</summary>
    public void MarkFailed() => _failed = true;
    public void Dispose() => _disposed = true;

    private GlobalFlagReason Check(GlobalFlagRequestId requestId, GlobalFlagVersion expected, FlagId flag)
    {
        var reason = GlobalFlagValidation.Request(Binding, requestId, expected, flag);
        if (reason != GlobalFlagReason.None) return reason;
        if (_disposed) return GlobalFlagReason.OwnerDisposed;
        if (_failed) return GlobalFlagReason.OwnerFailed;
        if (expected.Revision != _revision) return GlobalFlagReason.StaleRevision;
        return GlobalFlagReason.None;
    }

    private GlobalFlagWriteReceipt Receipt(GlobalFlagWriteRequest request, GlobalFlagWriteDisposition disposition, GlobalFlagReason reason) =>
        new(request.RequestId, GlobalFlagOperation.Write, request.Expected, request.Flag, request.Value, Version, disposition, reason);

    private static GlobalFlagCreationResult Rejected(GlobalFlagReason reason) => new(null, reason);
}

/// <summary>Detached historical data. Reading checks its own version, never live-owner freshness/lifecycle.</summary>
public sealed class GlobalFlagSnapshot
{
    private readonly Dictionary<FlagId, bool?> _values;

    internal GlobalFlagSnapshot(GlobalFlagBinding binding, GlobalFlagVersion version, GlobalFlagEntry[] entries)
    {
        Binding = binding;
        Version = version;
        Entries = Array.AsReadOnly(entries);
        _values = entries.ToDictionary(entry => entry.Flag, entry => entry.Value);
    }

    public GlobalFlagBinding Binding { get; }
    public GlobalFlagVersion Version { get; }
    public IReadOnlyList<GlobalFlagEntry> Entries { get; }

    public GlobalFlagReadResult Read(GlobalFlagReadRequest request)
    {
        var reason = GlobalFlagValidation.Request(Binding, request.RequestId, request.Expected, request.Flag);
        if (reason == GlobalFlagReason.None && request.Expected.Revision != Version.Revision)
            reason = GlobalFlagReason.StaleRevision;
        if (reason != GlobalFlagReason.None)
            return new(request.RequestId, request.Expected, request.Flag, GlobalFlagValidation.Status(reason), null, Version, reason);
        var value = _values[request.Flag];
        return new(request.RequestId, request.Expected, request.Flag,
            value.HasValue ? GlobalFlagReadStatus.Known : GlobalFlagReadStatus.Unknown,
            value, Version, value.HasValue ? GlobalFlagReason.None : GlobalFlagReason.MissingBootstrapValue);
    }
}
