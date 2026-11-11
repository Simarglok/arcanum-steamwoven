using System.Buffers.Binary;
using System.Security.Cryptography;
using Arcanum.Data.StaticObjects;

namespace Arcanum.Data.Area;

/// <summary>Serialized owner of authenticated reads, numeric snapshots and one revocable
/// selected-frame lease or frame batch. No DAT, encoded ART or RGBA payload is cached between operations.</summary>
public sealed partial class AreaDataOwner : IDisposable
{
    private readonly object _gate = new();
    private readonly AreaInputSet _inputs;
    private readonly AreaSourcePlan _plan;
    private readonly AreaLoaderOptions _limits;
    private readonly AreaBudget _budget;
    private readonly Dictionary<AreaInputKey, BudgetStream> _streams = [];
    private readonly Dictionary<AreaInputKey, DatArchive> _archives = [];
    private readonly Dictionary<AreaInputKey, Dictionary<string, DatEntry[]>> _indexes = [];
    private readonly Dictionary<(AreaInputKey, int), AreaEntryIdentity> _entryIdentities = [];
    private readonly Dictionary<DatEntry, string> _names = [];
    // Loose resource inputs by the virtual path each holds (ARC-DATA-018).
    private readonly Dictionary<string, AreaInputKey> _loose = new(StringComparer.Ordinal);
    private readonly object _identity = new();
    private bool _disposed;
    // Set when Open has just authenticated every input; the first Inspect uses that as its
    // opening (I0) authentication instead of hashing every input again (ARC-RENDER-009).
    private bool _openAuthenticated;
    private int _generation;
    private AreaFrameLease? _lease;
    private AreaSnapshot? _published;
    private long _publishedBytes;
    private long _leaseBytes;
    private static readonly StaticObjectLoaderOptions WireLimits = new();

    private AreaDataOwner(AreaInputSet inputs, AreaSourcePlan plan, AreaLoaderOptions limits, AreaBudget budget)
    { _inputs = inputs; _plan = plan; _limits = limits; _budget = budget; }

    public static AreaDataOwner Open(AreaInputSet inputs, AreaSourcePlan plan, AreaLoaderOptions limits)
    {
        ArgumentNullException.ThrowIfNull(inputs); ArgumentNullException.ThrowIfNull(plan); ArgumentNullException.ThrowIfNull(limits);
        // The fixed admission controller is the bootstrap guard. Charge its
        // graph, owner/locks, input wrappers, dictionary growth and Open's sorted
        // identity projection before constructing the owner or those wrappers.
        var budget = new AreaBudget(limits);
        AreaDataOwner? owner = null;
        try
        {
            limits.Validate();
            // Bound the source graph before sorting, wrapping or authenticating
            // any input. Four non-prototype roles plus the admitted prototypes
            // define this loader's source-count envelope, not a heap estimate.
            // ARC-DATA-018 adds the plan's own resource archives and loose resource files.
            // ARC-DATA-028 adds the map patch, ARC-DATA-032 a window's terrain templates.
            if (inputs.Inputs.Count > 5L + limits.MaxPrototypes + limits.MaxResourceInputs + limits.MaxTerrainTemplates ||
                plan.Identities.Count > 5L + limits.MaxPrototypes + limits.MaxResourceInputs + limits.MaxTerrainTemplates ||
                plan.Identities.Count(i => i.Key.Role == AreaSourceRole.TerrainTemplate) > limits.MaxTerrainTemplates ||
                plan.Identities.Count(i => i.Key.Role == AreaSourceRole.Prototype) > limits.MaxPrototypes ||
                plan.Identities.Count(i => i.Key.Role is AreaSourceRole.Archive or AreaSourceRole.Loose) > limits.MaxResourceInputs ||
                plan.Mappings.Count > Enum.GetValues<AreaMappingRole>().Length || plan.ArtBindings.Count > limits.MaxVisuals)
                throw Fail(AreaErrorCode.BudgetExceeded, AreaStage.Input);
            if (plan.Identities.Any(i => !Enum.IsDefined(i.Key.Role)))
                throw Fail(AreaErrorCode.InvalidSelection, AreaStage.Input);
            // A map patch input exactly when the plan binds one, and never on a pinned profile.
            if (plan.Identities.Any(i => i.Key.Role == AreaSourceRole.MapPatch && i.Key.Number != 0) ||
                (plan.MapPatch is not null) != plan.Identities.Any(i => i.Key.Role == AreaSourceRole.MapPatch) || plan.Original && plan.MapPatch is not null)
                throw Fail(AreaErrorCode.InvalidSelection, AreaStage.Input);
            // ARC-DATA-032: a window is bounded by the present-sector limit before anything is built from it.
            if (plan.Window is { } bounded && (bounded.Sectors is null || bounded.Sectors.Any(s => s is null)))
                throw Fail(AreaErrorCode.InvalidSelection, AreaStage.Input);
            if (plan.Window is { } large && large.Sectors.Count > limits.MaxPresentSectors) throw Fail(AreaErrorCode.BudgetExceeded, AreaStage.Input);
            // A window exactly for the window selection, never on a pinned profile; its sectors are distinct,
            // core or context, and its templates are exactly the plan's template inputs.
            if ((plan.Window is not null) != (plan.Selection == AreaSectorSelection.Window) || plan.Original && plan.Window is not null ||
                plan.Window is { } window && (window.Sectors.Count == 0 || window.Sectors.Select(s => s.Coordinate).Distinct().Count() != window.Sectors.Count ||
                    window.Sectors.Any(s => s.Scope is not (AreaScope.Core or AreaScope.Context) || s.Template is { Role: not AreaSourceRole.TerrainTemplate } ||
                        (s.Template is null) != (s.TemplatePath is null) || s.TemplatePath is { } path && (!AreaSourcePlan.LoosePath(path) || !path.StartsWith("terrain/", StringComparison.Ordinal))) ||
                    !window.Sectors.Any(s => s.Scope == AreaScope.Core) ||
                    !window.Sectors.Where(s => s.Template is not null).Select(s => s.Template!.Value).ToHashSet()
                        .SetEquals(plan.Identities.Where(i => i.Key.Role == AreaSourceRole.TerrainTemplate).Select(i => i.Key))) ||
                plan.Window is null && plan.Identities.Any(i => i.Key.Role == AreaSourceRole.TerrainTemplate))
                throw Fail(AreaErrorCode.InvalidSelection, AreaStage.Input);
            // A stack order is the whole resource search: every archive or loose input is in it.
            if (plan.Identities.Any(i => i.Key.Role is AreaSourceRole.Archive or AreaSourceRole.Loose && !plan.ResourceOrder.Any(s => s.Key == i.Key)))
                throw Fail(AreaErrorCode.InvalidSelection, AreaStage.Input);
            budget.Retain(checked(65536 + inputs.Inputs.Count * 1024L));
            owner = new AreaDataOwner(inputs, plan, limits, budget);
            if (inputs.Inputs.Count != plan.Identities.Count) throw Fail(AreaErrorCode.MissingSource, AreaStage.Input);
            foreach (var identity in OrderedInputs(plan.Identities))
            {
                var input = inputs.Inputs.SingleOrDefault(i => i.Key == identity.Key) ?? throw Fail(AreaErrorCode.MissingSource, AreaStage.Input);
                if (!input.Stream.CanRead || !input.Stream.CanSeek) throw Fail(AreaErrorCode.MissingSource, AreaStage.Input);
                if (identity.Key.Role == AreaSourceRole.Prototype && identity.Length > 1048576 ||
                    identity.Key.Role == AreaSourceRole.NormalGameMobile && identity.Length > limits.MaxAggregateBytes ||
                    identity.Key.Role == AreaSourceRole.MapPatch && identity.Length > Maps.MapPatchFile.MaxBytes ||
                    identity.Key.Role is AreaSourceRole.Module or AreaSourceRole.Resource1 or AreaSourceRole.Resource2 or AreaSourceRole.Archive && identity.Length > 536870912 ||
                    identity.Key.Role is AreaSourceRole.Loose or AreaSourceRole.TerrainTemplate && identity.Length > EntryCap(plan))
                    throw Fail(AreaErrorCode.BudgetExceeded, AreaStage.Input);
                // AreaPathStream's caller-created FileStream can lazily allocate
                // its single 64 KiB read buffer on our first small read. It stays
                // referenced through _inputs, not through Authenticate's scratch.
                if (input.Stream is AreaPathStream) budget.Retain(65536 + 32);
                var stream = new BudgetStream(input.Stream, owner._budget);
                owner._streams.Add(identity.Key, stream);
                owner.Authenticate(identity, default);
                if (identity.Key.Role == AreaSourceRole.Loose)
                {
                    var path = plan.ResourceOrder.Single(s => s.Key == identity.Key).Path!;
                    owner._loose.Add(path, identity.Key);
                    owner._entryIdentities.Add((identity.Key, 0), LooseIdentity(identity.Key, path, identity.Length));
                }
                if (identity.Key.Role is AreaSourceRole.Module or AreaSourceRole.Resource1 or AreaSourceRole.Resource2 or AreaSourceRole.Archive)
                {
                    owner.ReserveArchive(stream);
                    owner._archives.Add(identity.Key, DatArchive.Open(stream, new DatReaderOptions
                    {
                        MaxArchiveBytes = 536870912, MaxIndexBytes = 67108864, MaxEntryCount = 131072,
                        MaxStoredEntryBytes = (uint)EntryCap(plan), MaxDecodedEntryBytes = (uint)EntryCap(plan),
                        MaxNameBytes = 4096, MaxTotalNameBytes = 67108864
                    }, leaveOpen: true));
                    owner._indexes.Add(identity.Key, owner._archives[identity.Key].Entries.GroupBy(owner.Name, StringComparer.Ordinal)
                        .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal));
                    foreach (var entry in owner._archives[identity.Key].Entries)
                        owner._entryIdentities.Add((identity.Key, entry.Ordinal), owner.EntryIdentity(identity.Key, entry));
                }
            }
            if (!owner._archives.ContainsKey(new(AreaSourceRole.Module))) throw Fail(AreaErrorCode.MissingSource, AreaStage.Input);
            owner._openAuthenticated = true;
            return owner;
        }
        catch (Exception error) when (error is DatException or IOException or UnauthorizedAccessException or OverflowException)
        { DisposeInputs(owner, inputs); throw Fail(AreaErrorCode.MalformedRecord, AreaStage.Input); }
        catch { DisposeInputs(owner, inputs); throw; }

        static void DisposeInputs(AreaDataOwner? owner, AreaInputSet inputs)
        {
            if (owner is not null) owner.Dispose();
            else foreach (var input in inputs.Inputs) if (!input.LeaveOpen) input.Stream.Dispose();
        }
    }

    private static AreaInputIdentity[] OrderedInputs(IReadOnlyList<AreaInputIdentity> identities)
    {
        // One copied reference array, no generic comparer, selector or sort-helper
        // factory. Move only strictly greater keys so equal keys keep encounter order.
        var ordered = new AreaInputIdentity[identities.Count];
        for (var i = 0; i < ordered.Length; i++) ordered[i] = identities[i];
        for (var i = 1; i < ordered.Length; i++)
        {
            var identity = ordered[i];
            var key = identity.Key;
            var j = i;
            while (j > 0)
            {
                var previous = ordered[j - 1].Key;
                if ((int)previous.Role < (int)key.Role ||
                    previous.Role == key.Role && previous.Number <= key.Number) break;
                ordered[j] = ordered[j - 1];
                j--;
            }
            ordered[j] = identity;
        }
        return ordered;
    }

    public AreaBudgetUsage Usage { get { lock (_gate) { return _budget.Usage; } } }
    /// <summary>ARC-DATA-029: the largest retained ownership so far (the limits census).</summary>
    internal long RetainedHighWater { get { lock (_gate) { return _budget.RetainedHighWater; } } }
    public AreaBudgetRejection? FirstBudgetRejection { get { lock (_gate) { return _budget.FirstRejection; } } }
    public IReadOnlyList<AreaBudgetPhaseUsage> PhaseHistory { get { lock (_gate) { return _budget.History; } } }
    // Unsynchronized, for stream callbacks in tests that run on parallel authentication workers.
    internal AreaBudgetPhase CurrentPhase => _budget.PhaseId;
    public AreaSnapshot? PublishedSnapshot { get { lock (_gate) { return _published; } } }

    public AreaInspection Inspect(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            Alive();
            if (_lease is not null || _batch is not null) throw Fail(AreaErrorCode.LeaseLimit, AreaStage.Publication);
            _budget.BeginPhase(AreaBudgetPhase.I0);
            using var output = new AreaOwnership(_budget);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Right after Open the whole-input hashes are fresh: I0 then keeps only the cheap
                // path binding and length checks; I7 still hashes every input after the build.
                if (_openAuthenticated) { _openAuthenticated = false; CheckBindings(); }
                else AuthenticateAll(cancellationToken);
                BeginEntryMemo();
                AreaInspection inspection;
                _sharedFrames = new();
                _residentStaged = _limits.ResidentVisuals is null ? null : [];
                try { inspection = Build(cancellationToken, output); }
                finally { _missingMasks = null; _artHolders = null; _objectTiles = null; _sharedFrames = null; } // build-local (ARC-DATA-023/024)
                EndEntryMemo();
                _budget.BeginPhase(AreaBudgetPhase.I7);
                AuthenticateAll(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                // ARC-DEV-375: the records this build derived were derived inside a bracket whose closing authentication has now passed.
                if (inspection.IsComplete && _residentStaged is { Count: > 0 } staged) _limits.ResidentVisuals!.Commit(staged);
                if (inspection.IsComplete)
                {
                    var oldBytes = _publishedBytes;
                    _generation++; _published = inspection.Snapshot; _published!.MarkStrictlyPublished();
                    _publishedBytes = output.Detach();
                    // The owner no longer references the old publication. Caller
                    // metadata remains valid but does not refund allocation work.
                    _budget.Release(oldBytes);
                }
                else output.Dispose();
                return new(inspection.Snapshot, inspection.Issues, _budget.Usage);
            }
            catch (AreaException error) { output.Dispose(); return new(null, [error.Issue], _budget.Usage); }
            catch (StaticObjectException error)
            { output.Dispose(); return new(null, [new(error.Code == StaticObjectErrorCode.UnsupportedVersion ? AreaErrorCode.UnsupportedVersionOrSchema : error.Code == StaticObjectErrorCode.LimitExceeded ? AreaErrorCode.BudgetExceeded : AreaErrorCode.MalformedRecord, AreaStage.Aggregate, error.Offset, error.SourceIndex)], _budget.Usage); }
            catch (Exception error) when (error is DatException or IOException or UnauthorizedAccessException or OverflowException or ArgumentOutOfRangeException)
            { output.Dispose(); return new(null, [new(AreaErrorCode.MalformedRecord, AreaStage.Input)], _budget.Usage); }
            finally { _residentStaged = null; EndEntryMemo(); _budget.EndFrame(); }
        }
    }
    public AreaSnapshot LoadStrict(CancellationToken cancellationToken = default)
    {
        var result = Inspect(cancellationToken);
        if (!result.IsComplete) throw new AreaIncompleteException(result);
        return result.Snapshot!;
    }

    public AreaFrameLease AcquireSelectedFrame(AreaSnapshot snapshot, AreaVisualKey visual, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            Alive(); cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(snapshot.Owner, _identity) || !ReferenceEquals(snapshot, _published) || snapshot.Generation != _generation)
                throw Fail(AreaErrorCode.SnapshotMismatch, AreaStage.Frame);
            if (_lease is not null || _batch is not null) throw Fail(AreaErrorCode.LeaseLimit, AreaStage.Frame);
            var descriptor = snapshot.Visuals.SingleOrDefault(v => v.Key == visual) ?? throw Fail(AreaErrorCode.InvalidSelection, AreaStage.Frame);
            _budget.BeginPhase(AreaBudgetPhase.F);
            try
            {
                var frame = DecodeDescriptor(descriptor, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                var held = checked(frame.Rgba.Count + 8192L);
                _budget.Hold(held);
                _leaseBytes = held;
                _lease = new AreaFrameLease(this, frame, descriptor);
                return _lease;
            }
            finally { _budget.EndFrame(); }
        }
    }

    internal byte[] CopyPixels(AreaFrameLease lease, ArtFrame frame)
    {
        lock (_gate)
        {
            Alive(); if (!ReferenceEquals(_lease, lease)) throw new ObjectDisposedException(nameof(AreaFrameLease));
            _budget.BeginPhase(AreaBudgetPhase.C);
            _budget.Context = new(AreaBudgetCallsite.ConsumerCopy);
            _budget.Copy(frame.Rgba.Count);
            return frame.Rgba.ToArray();
        }
    }
    internal byte Pixel(AreaFrameLease lease, ArtFrame frame, int index)
    {
        lock (_gate)
        {
            Alive(); if (!ReferenceEquals(_lease, lease)) throw new ObjectDisposedException(nameof(AreaFrameLease));
            return frame.Rgba[index];
        }
    }
    internal void Release(AreaFrameLease lease)
    { lock (_gate) { if (ReferenceEquals(_lease, lease)) { lease.Revoke(); _lease = null; _budget.Release(_leaseBytes); _leaseBytes = 0; } } }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true; _lease?.Revoke(); _lease = null; _batch?.Revoke(); _batch = null; EndEntryMemo();
            _budget.Release(_leaseBytes); _leaseBytes = 0;
            foreach (var archive in _archives.Values) archive.Dispose();
            _archives.Clear(); _indexes.Clear(); _entryIdentities.Clear(); _names.Clear(); _loose.Clear(); _streams.Clear(); _published = null;
            _budget.Release(_publishedBytes); _publishedBytes = 0;
            // Empty dictionary capacity is still owned by this disposed object;
            // don't claim GC reclamation or refund the persistent index bound.
            foreach (var input in _inputs.Inputs) if (!input.LeaveOpen) input.Stream.Dispose();
        }
    }
    /// <summary>Largest archive entry (sector, mapping or ART) the owner reads: 16 MiB, as whole maps hold larger
    /// ART (ARC-DATA-019); one rule for every plan (ARC-DATA-036).</summary>
    internal static long EntryCap(AreaSourcePlan plan) => 16L * 1048576;
    private void Alive() { if (_disposed) throw new ObjectDisposedException(nameof(AreaDataOwner)); }
    private static AreaException Fail(AreaErrorCode code, AreaStage stage, long? offset = null, int? index = null) => new(new(code, stage, offset, index));
    private AreaInputIdentity Identity(AreaInputKey key) => _plan.Identities.SingleOrDefault(i => i.Key == key) ?? throw Fail(AreaErrorCode.MissingSource, AreaStage.Input);
    private DatArchive Archive(AreaInputKey key) => _archives.TryGetValue(key, out var archive) ? archive : throw Fail(AreaErrorCode.MissingSource, AreaStage.Input);

    private void AuthenticateAll(CancellationToken token) => AuthenticateMany(_plan.Identities, token);

    // Whole-input authentication of several held streams (I7, a batch's Complete, a later Inspect's I0).
    // Inputs under AreaLoaderOptions.ParallelAuthenticationMinBytes are authenticated one by one,
    // in order. Large ones (ARC-RENDER-009) are admitted, path- and length-checked in order, then read
    // and hashed in parallel (only the thread-safe read counters change meanwhile), and their verdicts,
    // position resets and closing path checks follow in the same order, so the first failing input in
    // plan order is reported. Parallel reads call no owner member: a stream callback that re-enters the
    // owner's gate from a worker would wait for the operation holding it.
    private void AuthenticateMany(IReadOnlyList<AreaInputIdentity> identities, CancellationToken token)
    {
        var large = identities.Where(identity => identity.Length >= _limits.ParallelAuthenticationMinBytes).ToArray();
        if (large.Length < 2) { foreach (var identity in identities) Authenticate(identity, token); return; }
        foreach (var identity in identities) if (identity.Length < _limits.ParallelAuthenticationMinBytes) Authenticate(identity, token);
        var scratches = new List<AreaOwnership>();
        try
        {
            var jobs = new (AreaInputIdentity Identity, AreaInput Input, BudgetStream Stream)[large.Length];
            for (var index = 0; index < large.Length; index++)
            {
                var identity = large[index];
                _budget.Context = new(AreaBudgetCallsite.Authentication, identity.Key);
                var scratch = new AreaOwnership(_budget); scratches.Add(scratch);
                scratch.Allocate(65536 + 4096);
                var input = _inputs.Inputs.Single(i => i.Key == identity.Key);
                if (input.Stream is AreaPathStream pathStream) pathStream.Check();
                var stream = _streams[identity.Key];
                if (stream.Length != identity.Length) throw Fail(AreaErrorCode.InputIdentityMismatch, AreaStage.Input);
                jobs[index] = (identity, input, stream);
            }
            // Workers share one context: a read rejection during the parallel reads names the pass, not an input.
            _budget.Context = new(AreaBudgetCallsite.Authentication);
            var hashes = new string?[jobs.Length]; var failures = new Exception?[jobs.Length];
            Parallel.For(0, jobs.Length, new ParallelOptions { MaxDegreeOfParallelism = Math.Min(jobs.Length, Environment.ProcessorCount) }, index =>
            {
                try { hashes[index] = HashInput(jobs[index].Stream, jobs[index].Identity.Length, token); }
                catch (Exception error) { failures[index] = error; }
            });
            for (var index = 0; index < jobs.Length; index++)
            {
                if (failures[index] is { } failure) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
                var (identity, input, stream) = jobs[index];
                if (stream.Length != identity.Length || hashes[index] != identity.Sha256)
                    throw Fail(AreaErrorCode.InputIdentityMismatch, AreaStage.Input);
                stream.Position = 0;
                if (input.Stream is AreaPathStream checkedPath) checkedPath.Check();
            }
        }
        finally { foreach (var scratch in scratches) scratch.Dispose(); }
    }
    private void CheckBindings()
    {
        foreach (var identity in _plan.Identities)
        {
            _budget.Context = new(AreaBudgetCallsite.Authentication, identity.Key);
            // Admitted like Authenticate's scratch, which also precedes its path checks.
            using var scratch = new AreaOwnership(_budget); scratch.Allocate(4096);
            if (_inputs.Inputs.Single(i => i.Key == identity.Key).Stream is AreaPathStream pathStream) pathStream.Check();
            if (_streams[identity.Key].Length != identity.Length) throw Fail(AreaErrorCode.InputIdentityMismatch, AreaStage.Input);
        }
    }
    private static string HashInput(Stream stream, long length, CancellationToken token)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[65536]; stream.Position = 0; long read = 0;
        while (read < length)
        {
            token.ThrowIfCancellationRequested();
            var size = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, length - read));
            if (size == 0) throw Fail(AreaErrorCode.InputChangedDuringRead, AreaStage.Input);
            read = checked(read + size); hash.AppendData(buffer, 0, size);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
    private void Authenticate(AreaInputIdentity identity, CancellationToken token)
    {
        _budget.Context = new(AreaBudgetCallsite.Authentication, identity.Key);
        using var scratch = new AreaOwnership(_budget);
        // Also precedes path checks, lookup closures and bounded failure graphs.
        // The persistent path read buffer was separately admitted during Open.
        scratch.Allocate(65536 + 4096);
        var input = _inputs.Inputs.Single(i => i.Key == identity.Key);
        if (input.Stream is AreaPathStream pathStream) pathStream.Check();
        var stream = _streams[identity.Key];
        if (stream.Length != identity.Length) throw Fail(AreaErrorCode.InputIdentityMismatch, AreaStage.Input);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[65536]; stream.Position = 0; long read = 0;
        while (read < identity.Length)
        {
            token.ThrowIfCancellationRequested();
            var size = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, identity.Length - read));
            if (size == 0) throw Fail(AreaErrorCode.InputChangedDuringRead, AreaStage.Input);
            read = checked(read + size); hash.AppendData(buffer, 0, size);
        }
        if (stream.Length != identity.Length || Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant() != identity.Sha256)
            throw Fail(AreaErrorCode.InputIdentityMismatch, AreaStage.Input);
        stream.Position = 0;
        if (input.Stream is AreaPathStream checkedPath) checkedPath.Check();
    }
    private void ReserveArchive(BudgetStream stream)
    {
        _budget.Context = new(AreaBudgetCallsite.ArchiveIndex, _budget.Context.Source);
        Span<byte> footer = stackalloc byte[28]; stream.Seek(-28, SeekOrigin.End); stream.ReadExactly(footer);
        var tail = BinaryPrimitives.ReadUInt32LittleEndian(footer[24..]);
        if (tail < 32 || tail > 67108864 || tail > stream.Length) throw Fail(AreaErrorCode.MalformedRecord, AreaStage.Input);
        stream.Position = stream.Length - tail;
        Span<byte> count = stackalloc byte[4]; stream.ReadExactly(count);
        var entries = BinaryPrimitives.ReadUInt32LittleEndian(count);
        if (entries > 131072) throw Fail(AreaErrorCode.BudgetExceeded, AreaStage.Input);
        _budget.Retain(checked(tail * 6L + entries * 1024L + 65536));
    }
    private byte[] ReadEntry(AreaEntryIdentity identity, CancellationToken token = default, AreaOwnership? ownership = null)
    {
        _budget.Context = new(AreaBudgetCallsite.Entry, identity.Source, identity.Ordinal, identity.Offset);
        if (identity.Source.Role == AreaSourceRole.Loose) return ReadLoose(identity, token, ownership);
        var archive = Archive(identity.Source);
        if (identity.Ordinal < 0 || identity.Ordinal >= archive.Entries.Count) throw Fail(AreaErrorCode.InvalidSelection, AreaStage.Input);
        var entry = archive.Entries[identity.Ordinal];
        if (EntryIdentity(identity.Source, entry) != identity) throw Fail(AreaErrorCode.InputIdentityMismatch, AreaStage.Input);
        if (entry.StoredSize > EntryCap(_plan) || entry.DeclaredOutputSize > EntryCap(_plan)) throw Fail(AreaErrorCode.BudgetExceeded, AreaStage.Input);
        // Shared DAT admission charges the stored array before its single read,
        // then the full structure-dependent inflate envelope before decoding.
        return archive.ReadEntry(entry, (IDatEntryAdmission?)ownership ?? _budget, token);
    }
    /// <summary>A loose resource is one whole input; its entry identity is fixed at Open
    /// (<see cref="LooseIdentity"/>) and its bytes must hash to the input's identity.</summary>
    private byte[] ReadLoose(AreaEntryIdentity identity, CancellationToken token, AreaOwnership? ownership)
    {
        if (!_entryIdentities.TryGetValue((identity.Source, 0), out var known)) throw Fail(AreaErrorCode.MissingSource, AreaStage.Input);
        if (known != identity) throw Fail(AreaErrorCode.InputIdentityMismatch, AreaStage.Input);
        var input = Identity(identity.Source); token.ThrowIfCancellationRequested();
        if (input.Length > EntryCap(_plan)) throw Fail(AreaErrorCode.BudgetExceeded, AreaStage.Input);
        ((IDatEntryAdmission?)ownership ?? _budget).Admit(checked(input.Length + 1024));
        var buffer = new byte[checked((int)input.Length)];
        var stream = _streams[identity.Source]; stream.Position = 0; stream.ReadExactly(buffer);
        if (Objects.ObjectWireReader.Hash(buffer) != input.Sha256) throw Fail(AreaErrorCode.InputChangedDuringRead, AreaStage.Input);
        return buffer;
    }
    /// <summary>The entry identity of a loose resource: ordinal, flags and offset 0, stored and decoded
    /// size its length, name digest the SHA-256 of its virtual path's ASCII bytes.</summary>
    internal static AreaEntryIdentity LooseIdentity(AreaInputKey key, string path, long length) =>
        new(key, 0, 0, checked((uint)length), checked((uint)length), 0, Objects.ObjectWireReader.Hash(System.Text.Encoding.ASCII.GetBytes(path)));
    private byte[] ReadFile(AreaInputKey key, CancellationToken token, AreaOwnership ownership)
    {
        _budget.Context = new(AreaBudgetCallsite.File, key);
        var identity = Identity(key); token.ThrowIfCancellationRequested();
        ownership.Allocate(checked(identity.Length + 1024));
        var buffer = new byte[checked((int)identity.Length)];
        var stream = _streams[key]; stream.Position = 0; stream.ReadExactly(buffer);
        if (Objects.ObjectWireReader.Hash(buffer) != identity.Sha256) throw Fail(AreaErrorCode.InputChangedDuringRead, AreaStage.Input);
        return buffer;
    }
    private AreaEntryIdentity EntryIdentity(AreaInputKey source, DatEntry entry)
    {
        // Entries and their raw names are immutable archive-owned values. This
        // reuses numeric/name locators only, never payload or source authentication.
        return _entryIdentities.TryGetValue((source, entry.Ordinal), out var identity) ? identity :
            new(source, entry.Ordinal, entry.Flags, entry.StoredSize, entry.DeclaredOutputSize, entry.Offset, Objects.ObjectWireReader.Hash(entry.RawNameBytes.ToArray()));
    }
}

/// <summary>No raw pixel view escapes this revocable lease. Explicit copies are separately
/// charged and become caller-owned; disposing the provider cannot revoke a caller's copy.</summary>
public sealed class AreaFrameLease : IDisposable
{
    private readonly AreaDataOwner _owner;
    private ArtFrame? _frame;
    internal AreaFrameLease(AreaDataOwner owner, ArtFrame frame, AreaVisualRecord descriptor) { _owner = owner; _frame = frame; Descriptor = descriptor; }
    public AreaVisualRecord Descriptor { get; }
    public int PixelByteCount => Live().Rgba.Count;
    public byte ReadPixelByte(int index) => _owner.Pixel(this, Live(), index);
    public byte[] CopyRgba() => _owner.CopyPixels(this, Live());
    private ArtFrame Live() => _frame ?? throw new ObjectDisposedException(nameof(AreaFrameLease));
    internal void Revoke() => _frame = null;
    public void Dispose() => _owner.Release(this);
}

internal sealed class BudgetStream(Stream inner, AreaBudget budget) : Stream
{
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => inner.Position = value; }
    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset); ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset > buffer.Length || count > buffer.Length - offset) throw new ArgumentException("Invalid buffer range.");
        budget.ReadReserve(count); var n = inner.Read(buffer, offset, count); budget.ReadActual(n, count); return n;
    }
    public override int Read(Span<byte> buffer)
    { budget.ReadReserve(buffer.Length); var n = inner.Read(buffer); budget.ReadActual(n, buffer.Length); return n; }
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
