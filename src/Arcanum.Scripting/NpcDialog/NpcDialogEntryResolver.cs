using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Arcanum.Data;
using Arcanum.Data.Area;

namespace Arcanum.Scripting.NpcDialog;

/// <summary>Bounded, synchronous query-only entry selection. No conversation or world commands.</summary>
public static class NpcDialogEntryResolver
{
    public static NpcDialogPreparation Prepare(AreaSnapshot snapshot, NpcDialogInvocation invocation, NpcDialogResourcePolicy resources,
        NpcDialogEntryLimits limits, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        try
        {
            try { limits.Validate(); } catch (ArgumentException) { throw Fail(NpcDialogStage.Invocation, NpcDialogCode.InvalidLimits); }
            cancellationToken.ThrowIfCancellationRequested();
            if (!ValidInvocation(invocation)) return new(NpcDialogAdmission.DeniedParticipants);
            if (invocation.Event != NpcDialogEvent.Dialog) return new(NpcDialogAdmission.UnsupportedEvent);
            if (invocation.Extra.HasValue) return new(NpcDialogAdmission.UnsupportedExtra);
            if (!resources.SourceVersion.Equals(invocation.SourceVersion)) throw Fail(NpcDialogStage.Resource, NpcDialogCode.WrongSource);
            var row = BindObject(snapshot, invocation);
            var attachment = BindAttachment(row, invocation, out var admission);
            if (attachment is null) return new(admission);
            var positions = new List<(Stream Stream, long Position)>();
            try
            {
                foreach (var source in resources.Sources)
                {
                    positions.Add((source.Stream, source.Stream.Position));
                    Authenticate(source, cancellationToken);
                }
                var scriptSource = resources.Sources.Single(s => s.SourceId == resources.ScriptSourceId);
                var dialogSource = resources.Sources.Single(s => s.SourceId == resources.DialogSourceId);
                CheckBinding(invocation.SourceVersion, "Script", scriptSource);
                CheckBinding(invocation.SourceVersion, "Dialog", dialogSource);
                using var scripts = DatArchive.Open(scriptSource.Stream, DatOptions(limits), leaveOpen: true);
                using var dialogs = DatArchive.Open(dialogSource.Stream, DatOptions(limits), leaveOpen: true);
                DatEntry? selected = null;
                byte[]? selectedName = null;
                foreach (var entry in scripts.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var name = entry.RawNameBytes.ToArray();
                    if (!IsScriptCandidate(name)) continue;
                    var key = CatalogueKey(name);
                    if (key != attachment.ScriptId.Value) continue;
                    if (selected is not null) throw Fail(NpcDialogStage.Resource, NpcDialogCode.AmbiguousResource);
                    selected = entry; selectedName = name;
                }
                if (selected is null) throw Fail(NpcDialogStage.Resource, NpcDialogCode.MissingResource);
                var dlgName = (byte[])selectedName!.Clone();
                "dlg"u8.CopyTo(dlgName); "dlg"u8.CopyTo(dlgName.AsSpan(dlgName.Length - 4, 3));
                DatEntry? selectedDialog = null;
                foreach (var entry in dialogs.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!entry.RawNameBytes.SequenceEqual(dlgName)) continue;
                    if (selectedDialog is not null) throw Fail(NpcDialogStage.Resource, NpcDialogCode.AmbiguousResource);
                    selectedDialog = entry;
                }
                if (selectedDialog is null) throw Fail(NpcDialogStage.Resource, NpcDialogCode.MissingResource);
                CheckSizes(selected, limits.MaxScriptBytes, limits);
                CheckSizes(selectedDialog, limits.MaxDialogBytes, limits);
                cancellationToken.ThrowIfCancellationRequested();
                var scriptBytes = scripts.ReadEntry(selected);
                var dlgBytes = dialogs.ReadEntry(selectedDialog);
                cancellationToken.ThrowIfCancellationRequested();
                var document = DlgReader.Read(dlgBytes, new() { MaxInputBytes = limits.MaxDialogBytes, MaxRecordCount = limits.MaxDialogRecords, MaxFieldBytes = limits.MaxDialogFieldBytes });
                var graph = PrepareGraph(scriptBytes, document, invocation.CallerLine.Value, limits, cancellationToken, out var count, out var capacity, out var depth);
                foreach (var source in resources.Sources) Authenticate(source, cancellationToken);
                var allEntries = scripts.Entries.Concat(dialogs.Entries).ToArray();
                var plan = new NpcDialogEntryPlan(invocation, attachment, Resource(scriptSource, selected, scriptBytes, NpcDialogResourceRole.Script, invocation.SourceVersion),
                    Resource(dialogSource, selectedDialog, dlgBytes, NpcDialogResourceRole.Dialog, invocation.SourceVersion), graph, limits with { }, count, capacity, depth,
                    document.Records.Count, document.Records.SelectMany(r => r.Fields).Max(f => f.Length), Math.Max(scripts.Entries.Count, dialogs.Entries.Count),
                    allEntries.Max(e => e.RawNameBytes.Count), Math.Max(scripts.Entries.Sum(e => (long)e.RawNameBytes.Count), dialogs.Entries.Sum(e => (long)e.RawNameBytes.Count)));
                cancellationToken.ThrowIfCancellationRequested();
                return new(NpcDialogAdmission.Admitted, plan);
            }
            finally
            {
                // Restore every borrowed handle even if another restore fails; never dispose it.
                var failed = false;
                foreach (var (stream, position) in positions) { try { stream.Position = position; } catch (Exception) { failed = true; } }
                if (failed) throw Fail(NpcDialogStage.Resource, NpcDialogCode.SourceChanged);
            }
        }
        catch (EntryFailure f) { return PreparationFailure(invocation, f); }
        catch (OperationCanceledException) { return PreparationFailure(invocation, Fail(NpcDialogStage.Invocation, NpcDialogCode.Cancelled)); }
        catch (DatLimitExceededException) { return PreparationFailure(invocation, Fail(NpcDialogStage.Resource, NpcDialogCode.Budget)); }
        catch (DlgLimitExceededException) { return PreparationFailure(invocation, Fail(NpcDialogStage.Container, NpcDialogCode.Budget)); }
        catch (DatUnsupportedEntryException) { return PreparationFailure(invocation, Fail(NpcDialogStage.Resource, NpcDialogCode.UnsupportedRepresentation, true)); }
        catch (DatUnsupportedFormatException) { return PreparationFailure(invocation, Fail(NpcDialogStage.Resource, NpcDialogCode.UnsupportedRepresentation, true)); }
        catch (DatException) { return PreparationFailure(invocation, Fail(NpcDialogStage.Resource, NpcDialogCode.MalformedContainer)); }
        catch (DlgException) { return PreparationFailure(invocation, Fail(NpcDialogStage.Container, NpcDialogCode.MalformedContainer)); }
        catch (Exception) { return PreparationFailure(invocation, Fail(NpcDialogStage.Invocation, NpcDialogCode.WrongContext)); }
    }

    public static NpcDialogResolution Resolve(NpcDialogEntryPlan plan, INpcDialogEntryQueries queries, NpcDialogEntryLimits limits,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var trace = new List<NpcDialogTraceStep>();
        var line = plan.Invocation.CallerLine.Value;
        var calls = 0;
        try
        {
            try { limits.Validate(); } catch (ArgumentException) { throw Fail(NpcDialogStage.Resolution, NpcDialogCode.InvalidLimits, line: line); }
            limits = NpcDialogEntryLimits.Stricter(plan.Limits, limits);
            if (!plan.Fits(limits)) throw Fail(NpcDialogStage.Resolution, NpcDialogCode.Budget, line: line);
            cancellationToken.ThrowIfCancellationRequested();
            CheckVersions();
            var visited = new HashSet<int>();
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (line == plan.Count) throw Fail(NpcDialogStage.Resolution, NpcDialogCode.NoEntry, true, line);
                if (trace.Count >= limits.MaxSteps) throw Fail(NpcDialogStage.Resolution, NpcDialogCode.Budget, true, line);
                if (!visited.Add(line)) throw Fail(NpcDialogStage.Resolution, NpcDialogCode.Cycle, true, line);
                var instruction = plan.Instructions[line];
                if (instruction.Frontier.HasValue) throw Fail(NpcDialogStage.Resolution, instruction.Frontier.Value, true, line);
                var status = NpcDialogQueryStatus.KnownTrue;
                if (instruction.Predicate != NpcDialogPredicateKind.True)
                {
                    var context = Context();
                    NpcDialogQueryReply reply;
                    try
                    {
                        reply = instruction.Predicate switch
                        {
                            NpcDialogPredicateKind.Following => queries.Following(context, plan.Invocation.NpcId, plan.Invocation.PcId),
                            NpcDialogPredicateKind.Jilted => queries.Jilted(context, plan.Invocation.NpcId),
                            NpcDialogPredicateKind.LocalFlag => queries.LocalFlag(context, plan.Attachment, new(instruction.Flag)),
                            _ => throw Fail(NpcDialogStage.Query, NpcDialogCode.UnsupportedCondition, true, line),
                        };
                    }
                    catch (EntryFailure) { throw; }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                    catch (Exception) { throw Fail(NpcDialogStage.Query, NpcDialogCode.QueryFailed, line: line); }
                    cancellationToken.ThrowIfCancellationRequested();
                    if (reply is null || reply.Context != context) throw Fail(NpcDialogStage.Query, NpcDialogCode.WrongContext, line: line);
                    status = reply.Status;
                    // Check versions after every boolean reply, including explicit non-values.
                    CheckVersions();
                    if (status is not NpcDialogQueryStatus.KnownTrue and not NpcDialogQueryStatus.KnownFalse)
                    {
                        trace.Add(new(new(line), instruction.Predicate, status, null, null, null));
                        throw QueryFailure(status, line);
                    }
                }
                var action = status == NpcDialogQueryStatus.KnownTrue ? instruction.True! : instruction.False!;
                trace.Add(new(new(line), instruction.Predicate, status, action.Kind,
                    action.Kind is NpcDialogActionKind.Advance or NpcDialogActionKind.Goto ? new(action.Value) : null,
                    action.Kind == NpcDialogActionKind.Dialog ? new(action.Value) : null));
                if (action.Frontier.HasValue) throw Fail(NpcDialogStage.Resolution, action.Frontier.Value, true, line);
                if (action.Kind == NpcDialogActionKind.Dialog)
                {
                    CheckVersions();
                    cancellationToken.ThrowIfCancellationRequested();
                    var ownedTrace = Array.AsReadOnly(trace.ToArray());
                    var entry = new NpcDialogResolvedEntry(plan.Invocation, plan.Attachment, plan.ScriptResource, new(line), plan.DialogResource, new(action.Value), ownedTrace);
                    return new(entry, null, null, ownedTrace);
                }
                line = action.Value;
            }
        }
        catch (EntryFailure f) { return new(null, f.Frontier ? new(f.Stage, f.Code, plan.Invocation, ToLine(f.Line)) : null, f.Frontier ? null : new(f.Stage, f.Code, plan.Invocation, ToLine(f.Line)), trace); }
        catch (OperationCanceledException) { return new(null, null, new(NpcDialogStage.Resolution, NpcDialogCode.Cancelled, plan.Invocation, new(line)), trace); }

        NpcDialogQueryContext Context()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (calls >= limits.MaxQueryCalls) throw Fail(NpcDialogStage.Query, NpcDialogCode.Budget, true, line);
            return new(plan.Invocation, plan.Attachment, new(line), ++calls);
        }
        void CheckVersions()
        {
            var context = Context();
            NpcDialogVersionReply reply;
            try { reply = queries.ReadVersions(context); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception) { throw Fail(NpcDialogStage.Query, NpcDialogCode.QueryFailed, line: line); }
            cancellationToken.ThrowIfCancellationRequested();
            if (reply is null || reply.Context != context) throw Fail(NpcDialogStage.Query, NpcDialogCode.WrongContext, line: line);
            if (reply.Status != NpcDialogVersionStatus.Known)
                throw QueryFailure(reply.Status switch { NpcDialogVersionStatus.Unknown => NpcDialogQueryStatus.Unknown, NpcDialogVersionStatus.Unavailable => NpcDialogQueryStatus.Unavailable, NpcDialogVersionStatus.Stale => NpcDialogQueryStatus.Stale, _ => NpcDialogQueryStatus.Failed }, line);
            if (!plan.Invocation.SourceVersion.Equals(reply.SourceVersion) || reply.StateVersion != plan.Invocation.StateVersion || reply.AttachmentStateVersion != plan.Invocation.AttachmentStateVersion)
                throw Fail(NpcDialogStage.Query, NpcDialogCode.QueryStale, line: line);
        }
    }

    private static bool ValidInvocation(NpcDialogInvocation i)
    {
        try
        {
            IdentityValidation.Owner(i.WorldId.Value); IdentityValidation.Owner(i.PcId.OwnerId);
            IdentityValidation.Owner(i.StateVersion.OwnerEpoch); IdentityValidation.Owner(i.AttachmentStateVersion.OwnerEpoch);
            IdentityValidation.Range(i.MapId.RuntimeMap, 1, int.MaxValue); IdentityValidation.Hash(i.MapId.ModuleSha256);
            IdentityValidation.Key(i.NpcId.AreaObjectKey); IdentityValidation.Hash(i.NpcId.RawSha256); IdentityValidation.Hash(i.NpcId.SemanticSha256);
            IdentityValidation.Range(i.PrototypeId.Number, 1, int.MaxValue); IdentityValidation.Hash(i.PrototypeId.OwnIdSha256); IdentityValidation.Hash(i.PrototypeId.InputSha256);
            return i.SourceVersion is not null && i.NpcId.WorldId == i.WorldId && i.PcId.WorldId == i.WorldId && i.NpcId.MapId == i.MapId && i.PcId.MapId == i.MapId && i.NpcId.AreaObjectKey.RuntimeMap == i.MapId.RuntimeMap;
        }
        catch (ArgumentException) { return false; }
    }
    private static AreaObjectRecord BindObject(AreaSnapshot snapshot, NpcDialogInvocation i)
    {
        if (snapshot.RuntimeMap != i.MapId.RuntimeMap || snapshot.Inputs.Count(x => x.Key.Role == AreaSourceRole.Module && x.Sha256 == i.MapId.ModuleSha256) != 1)
            throw Fail(NpcDialogStage.Invocation, NpcDialogCode.WrongContext);
        if (snapshot.Inputs.Select(x => x.Key).Distinct().Count() != snapshot.Inputs.Count) throw Fail(NpcDialogStage.Invocation, NpcDialogCode.WrongSource);
        foreach (var input in snapshot.Inputs)
        {
            var binding = i.SourceVersion.Bindings.SingleOrDefault(b => b.Role == AreaRole(input.Key));
            if (binding is null || binding.Length != input.Length || binding.Sha256 != input.Sha256) throw Fail(NpcDialogStage.Invocation, NpcDialogCode.WrongSource);
        }
        var rows = snapshot.Objects.Where(o => o.Key == i.NpcId.AreaObjectKey).ToArray();
        if (rows.Length != 1 || snapshot.Objects.Count(o => o.Key.ObjectIdSha256 == i.NpcId.AreaObjectKey.ObjectIdSha256) != 1)
            throw Fail(NpcDialogStage.Invocation, NpcDialogCode.WrongContext);
        var row = rows[0];
        if (row.Version != 119 || row.Type != 16 || row.PrototypeNumber != i.PrototypeId.Number || row.PrototypeOwnSha256 != i.PrototypeId.OwnIdSha256 ||
            row.RawSha256 != i.NpcId.RawSha256 || row.SemanticSha256 != i.NpcId.SemanticSha256 || !row.SchemaReady || !row.PlacementReady || !row.SerializedLocation.HasValue || row.WorldX is null or < 0 || row.WorldY is null or < 0 ||
            !snapshot.Inputs.Any(x => x.Key == new AreaInputKey(AreaSourceRole.Prototype, row.PrototypeNumber) && x.Sha256 == i.PrototypeId.InputSha256) ||
            !snapshot.Inputs.Any(x => x.Key.Role == (row.Key.Kind == AreaSourceKind.Mobile ? AreaSourceRole.NormalGameMobile : AreaSourceRole.Module) && x.Sha256 == row.Key.SourceSha256))
            throw Fail(NpcDialogStage.Invocation, NpcDialogCode.WrongContext);
        IdentityValidation.Hash(row.PrototypeReferenceSha256); // Raw padding need not equal the prototype's own identifier.
        return row;
    }
    public static string AreaRole(AreaInputKey key) => $"Area:{key.Role}:{key.Number}";
    private static NpcDialogAttachmentId? BindAttachment(AreaObjectRecord row, NpcDialogInvocation i, out NpcDialogAdmission admission)
    {
        admission = NpcDialogAdmission.NoDialogAttachment;
        if (row.DirectFields.Select(f => f.Slot).Distinct().Count() != row.DirectFields.Count || row.EffectiveFields.Select(f => f.Slot).Distinct().Count() != row.EffectiveFields.Count) throw Fail(NpcDialogStage.Attachment, NpcDialogCode.MalformedAttachment);
        var direct = row.DirectFields.SingleOrDefault(f => f.Slot == 31);
        var effective = row.EffectiveFields.SingleOrDefault(f => f.Slot == 31);
        if (direct is not null) ValidateField(direct);
        if (effective is not null) ValidateField(effective);
        var overridden = direct is not null && direct.Presence != AreaPresence.Absent;
        if (effective is null)
        {
            if (overridden) throw Fail(NpcDialogStage.Attachment, NpcDialogCode.MalformedAttachment);
            return null;
        }
        if (effective.Origin != (overridden ? AreaValueOrigin.Instance : AreaValueOrigin.Prototype) || direct is not null && direct.Origin != AreaValueOrigin.Instance)
            throw Fail(NpcDialogStage.Attachment, NpcDialogCode.MalformedAttachment);
        if (overridden && (direct!.Presence != effective.Presence || direct.Fingerprint != effective.Fingerprint || direct.Elements.Count != effective.Elements.Count ||
            !direct.Elements.Zip(effective.Elements).All(p => p.First.Key == p.Second.Key && p.First.Words.SequenceEqual(p.Second.Words))))
            throw Fail(NpcDialogStage.Attachment, NpcDialogCode.MalformedAttachment);
        if (effective.Presence != AreaPresence.Array) return null;
        var element = effective.Elements.SingleOrDefault(e => e.Key == 9);
        if (element is null) return null;
        if (element.Words[2] == 0) { admission = NpcDialogAdmission.ZeroScript; return null; }
        return new(i, effective, element);
    }
    private static void ValidateField(AreaFieldValue f)
    {
        if (!Enum.IsDefined(f.Origin) || !Enum.IsDefined(f.Presence) || f.Number.HasValue || f.Presence == AreaPresence.Value || f.Elements is null ||
            f.Presence != AreaPresence.Array && f.Elements.Count != 0 || f.Presence == AreaPresence.Array && f.Elements.Count == 0 ||
            f.Elements.Select(e => e.Key).Distinct().Count() != f.Elements.Count || f.Elements.Any(e => e.Key is < 0 or > 35 || e.Words.Count != 3 || e.Words.Any(w => w is < 0 or > uint.MaxValue) || e.Words[2] > 99999))
            throw Fail(NpcDialogStage.Attachment, NpcDialogCode.MalformedAttachment);
        if (f.Fingerprint is not null) IdentityValidation.Hash(f.Fingerprint);
    }
    private static void CheckBinding(NpcDialogSourceVersion version, string role, NpcDialogResourceSource source)
    {
        var binding = version.Bindings.SingleOrDefault(b => b.Role == role);
        if (binding is null || binding.InputId != source.SourceId || binding.Length != source.ExpectedLength || binding.Sha256 != source.Sha256) throw Fail(NpcDialogStage.Resource, NpcDialogCode.WrongSource);
    }
    private static void Authenticate(NpcDialogResourceSource source, CancellationToken token)
    {
        try
        {
            if (source.Stream.Length != source.ExpectedLength) throw Fail(NpcDialogStage.Resource, NpcDialogCode.SourceChanged);
            source.Stream.Position = 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[262144];
            long read = 0;
            while (read < source.ExpectedLength)
            {
                token.ThrowIfCancellationRequested();
                var n = source.Stream.Read(buffer, 0, (int)Math.Min(buffer.Length, source.ExpectedLength - read));
                if (n == 0) throw Fail(NpcDialogStage.Resource, NpcDialogCode.SourceChanged);
                hash.AppendData(buffer, 0, n); read += n;
            }
            token.ThrowIfCancellationRequested();
            if (source.Stream.Length != source.ExpectedLength || Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant() != source.Sha256) throw Fail(NpcDialogStage.Resource, NpcDialogCode.SourceChanged);
        }
        catch (EntryFailure) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { throw Fail(NpcDialogStage.Resource, NpcDialogCode.SourceChanged); }
    }
    private static DatReaderOptions DatOptions(NpcDialogEntryLimits l) => new()
    {
        MaxIndexBytes = 67108864, MaxEntryCount = l.MaxCatalogueEntries, MaxNameBytes = (uint)l.MaxCatalogueNameBytes,
        MaxTotalNameBytes = (uint)l.MaxTotalCatalogueNameBytes, MaxStoredEntryBytes = (uint)l.MaxStoredResourceBytes,
        MaxDecodedEntryBytes = (uint)Math.Max(l.MaxScriptBytes, l.MaxDialogBytes),
    };
    private static bool IsScriptCandidate(byte[] name) => name.AsSpan().StartsWith("scr\\"u8) && (name.AsSpan().EndsWith(".scr\0"u8) || name.AsSpan().EndsWith(".scr"u8));
    private static int CatalogueKey(byte[] name)
    {
        if (name.Length < 10 || name[^1] != 0 || name.AsSpan(0, name.Length - 1).Contains((byte)0) || name.Take(name.Length - 1).Any(b => b is < 32 or > 126)) throw Fail(NpcDialogStage.Resource, NpcDialogCode.InvalidCatalogueKey);
        var basename = name.AsSpan(4, name.Length - 9);
        if (basename.Contains((byte)'\\') || basename.Contains((byte)'/') || basename.Contains((byte)':')) throw Fail(NpcDialogStage.Resource, NpcDialogCode.InvalidCatalogueKey);
        var index = 0; var value = 0;
        while (index < basename.Length && basename[index] is >= (byte)'0' and <= (byte)'9')
        {
            var digit = basename[index++] - '0';
            if (value > (99999 - digit) / 10) throw Fail(NpcDialogStage.Resource, NpcDialogCode.InvalidCatalogueKey);
            value = value * 10 + digit;
        }
        if (index == 0 || value == 0) throw Fail(NpcDialogStage.Resource, NpcDialogCode.InvalidCatalogueKey);
        return value;
    }
    private static void CheckSizes(DatEntry entry, int decoded, NpcDialogEntryLimits limits)
    { if (entry.StoredSize > limits.MaxStoredResourceBytes || entry.DeclaredOutputSize > decoded) throw Fail(NpcDialogStage.Resource, NpcDialogCode.Budget); }
    private static NpcDialogResourceIdentity Resource(NpcDialogResourceSource source, DatEntry entry, byte[] bytes, NpcDialogResourceRole role, NpcDialogSourceVersion version)
        => new(source.SourceId, role, entry.Ordinal, entry.Flags, entry.Offset, entry.StoredSize, entry.DeclaredOutputSize, Digest(entry.RawNameBytes.ToArray()), Digest(bytes), version);
    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static Dictionary<int, EntryInstruction> PrepareGraph(byte[] bytes, DlgDocument dialog, int caller, NpcDialogEntryLimits limits, CancellationToken token,
        out int count, out int capacity, out int maxDepth)
    {
        if (bytes.Length < 64) throw Fail(NpcDialogStage.Container, NpcDialogCode.MalformedContainer);
        count = I32(bytes, 52); capacity = I32(bytes, 56);
        var entryCount = count;
        if (count <= 0 || capacity < count || bytes.LongLength != 64L + 132L * count) throw Fail(NpcDialogStage.Container, NpcDialogCode.MalformedContainer);
        if (count > limits.MaxScriptEntries || capacity > limits.MaxScriptEntries) throw Fail(NpcDialogStage.Container, NpcDialogCode.Budget);
        if (caller >= count) throw Fail(NpcDialogStage.Graph, NpcDialogCode.MalformedOperand, line: caller);
        if (I32(bytes, 48) != 0) throw Fail(NpcDialogStage.Container, NpcDialogCode.UnsupportedRepresentation, true, caller);
        var graph = new Dictionary<int, EntryInstruction>();
        var work = new Queue<(int Line, int Depth)>(); work.Enqueue((caller, 1)); maxDepth = 0;
        while (work.TryDequeue(out var next))
        {
            token.ThrowIfCancellationRequested();
            if (next.Line == count || graph.ContainsKey(next.Line)) continue;
            if (next.Depth > limits.MaxGraphDepth) throw Fail(NpcDialogStage.Graph, NpcDialogCode.Budget, true, next.Line);
            maxDepth = Math.Max(maxDepth, next.Depth);
            var offset = 64 + next.Line * 132;
            var type = I32(bytes, offset);
            var predicate = type switch { 0 => NpcDialogPredicateKind.True, 3 => NpcDialogPredicateKind.LocalFlag, 18 => NpcDialogPredicateKind.Following, 39 => NpcDialogPredicateKind.Jilted, _ => NpcDialogPredicateKind.Unsupported };
            NpcDialogCode? unsupported = predicate == NpcDialogPredicateKind.Unsupported ? NpcDialogCode.UnsupportedCondition : null;
            if (type == 3 && bytes[offset + 4] != 3 || type == 18 && (bytes[offset + 4] != 1 || bytes[offset + 5] != 0) || type == 39 && bytes[offset + 4] != 1) unsupported = NpcDialogCode.UnsupportedOperand;
            var flag = type == 3 && unsupported is null ? I32(bytes, offset + 12) : 0;
            if (flag is < 0 or > 31) throw Fail(NpcDialogStage.Graph, NpcDialogCode.MalformedOperand, line: next.Line);
            if (unsupported.HasValue) { graph.Add(next.Line, new(predicate, flag, null, null, unsupported)); continue; }
            var yes = Action(offset + 44, next.Line);
            var no = type == 0 ? null : Action(offset + 88, next.Line);
            graph.Add(next.Line, new(predicate, flag, yes, no));
            foreach (var action in new[] { yes, no })
                if (action is { Kind: NpcDialogActionKind.Advance or NpcDialogActionKind.Goto }) work.Enqueue((action.Value, next.Depth + 1));
        }
        return graph;

        EntryAction Action(int offset, int line)
        {
            var type = I32(bytes, offset);
            if (type == 0) return new(NpcDialogActionKind.Advance, line + 1);
            if (type is not 3 and not 4) return new(NpcDialogActionKind.Unsupported, 0, type == 7 ? NpcDialogCode.UnsupportedCallback : NpcDialogCode.UnsupportedAction);
            if (bytes[offset + 4] != 3) return new(NpcDialogActionKind.Unsupported, 0, NpcDialogCode.UnsupportedOperand);
            var value = I32(bytes, offset + 12);
            if (type == 3)
            {
                if (value < 0 || value >= entryCount) throw Fail(NpcDialogStage.Graph, NpcDialogCode.MalformedOperand, line: line);
                return new(NpcDialogActionKind.Goto, value);
            }
            if (value <= 0) throw Fail(NpcDialogStage.Graph, NpcDialogCode.MalformedOperand, line: line);
            if (!dialog.TryGetRecord(value, out var record)) throw Fail(NpcDialogStage.Graph, NpcDialogCode.MissingNode, line: line);
            if (record!.Role != DlgRecordRole.Npc) throw Fail(NpcDialogStage.Graph, NpcDialogCode.WrongNodeRole, line: line);
            return new(NpcDialogActionKind.Dialog, value);
        }
    }
    private static int I32(byte[] bytes, int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, 4));
    private static EntryFailure Fail(NpcDialogStage stage, NpcDialogCode code, bool frontier = false, int? line = null) => new(stage, code, frontier, line);
    private static EntryFailure QueryFailure(NpcDialogQueryStatus status, int line) => Fail(NpcDialogStage.Query,
        status switch { NpcDialogQueryStatus.Unknown => NpcDialogCode.QueryUnknown, NpcDialogQueryStatus.Unavailable => NpcDialogCode.QueryUnavailable, NpcDialogQueryStatus.Stale => NpcDialogCode.QueryStale, _ => NpcDialogCode.QueryFailed },
        status is NpcDialogQueryStatus.Unknown or NpcDialogQueryStatus.Unavailable, line);
    private static NpcDialogScrLine? ToLine(int? line) => line.HasValue ? new(line.Value) : null;
    private static NpcDialogPreparation PreparationFailure(NpcDialogInvocation invocation, EntryFailure f) => new(NpcDialogAdmission.Rejected, frontier: f.Frontier ? new(f.Stage, f.Code, invocation, ToLine(f.Line)) : null, error: f.Frontier ? null : new(f.Stage, f.Code, invocation, ToLine(f.Line)));
}
