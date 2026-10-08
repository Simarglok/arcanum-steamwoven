using Arcanum.Data;

namespace Arcanum.Scripting.NpcDialog;

public sealed record NpcDialogEntryLimits
{
    public int MaxScriptBytes { get; init; } = 1048576;
    public int MaxScriptEntries { get; init; } = 4096;
    public int MaxStoredResourceBytes { get; init; } = 1048576;
    public int MaxDialogBytes { get; init; } = 524288;
    public int MaxDialogRecords { get; init; } = 10000;
    public int MaxDialogFieldBytes { get; init; } = 4096;
    public int MaxCatalogueEntries { get; init; } = 1000000;
    public int MaxCatalogueNameBytes { get; init; } = 65536;
    public int MaxTotalCatalogueNameBytes { get; init; } = 67108864;
    public int MaxGraphDepth { get; init; } = 128;
    public int MaxSteps { get; init; } = 128;
    public int MaxQueryCalls { get; init; } = 128;
    public int MaxInvocationDepth { get; init; } = 1;
    internal void Validate()
    {
        var caps = new NpcDialogEntryLimits();
        foreach (var property in typeof(NpcDialogEntryLimits).GetProperties())
        {
            var value = (int)property.GetValue(this)!;
            if (value <= 0 || value > (int)property.GetValue(caps)!) throw new ArgumentOutOfRangeException(nameof(NpcDialogEntryLimits));
        }
    }
    internal static NpcDialogEntryLimits Stricter(NpcDialogEntryLimits a, NpcDialogEntryLimits b) => new()
    {
        MaxScriptBytes = Math.Min(a.MaxScriptBytes, b.MaxScriptBytes), MaxScriptEntries = Math.Min(a.MaxScriptEntries, b.MaxScriptEntries),
        MaxStoredResourceBytes = Math.Min(a.MaxStoredResourceBytes, b.MaxStoredResourceBytes), MaxDialogBytes = Math.Min(a.MaxDialogBytes, b.MaxDialogBytes),
        MaxDialogRecords = Math.Min(a.MaxDialogRecords, b.MaxDialogRecords), MaxDialogFieldBytes = Math.Min(a.MaxDialogFieldBytes, b.MaxDialogFieldBytes),
        MaxCatalogueEntries = Math.Min(a.MaxCatalogueEntries, b.MaxCatalogueEntries), MaxCatalogueNameBytes = Math.Min(a.MaxCatalogueNameBytes, b.MaxCatalogueNameBytes),
        MaxTotalCatalogueNameBytes = Math.Min(a.MaxTotalCatalogueNameBytes, b.MaxTotalCatalogueNameBytes), MaxGraphDepth = Math.Min(a.MaxGraphDepth, b.MaxGraphDepth),
        MaxSteps = Math.Min(a.MaxSteps, b.MaxSteps), MaxQueryCalls = Math.Min(a.MaxQueryCalls, b.MaxQueryCalls), MaxInvocationDepth = Math.Min(a.MaxInvocationDepth, b.MaxInvocationDepth),
    };
}
public enum NpcDialogAdmission { Admitted, NoDialogAttachment, ZeroScript, DeniedParticipants, UnsupportedEvent, UnsupportedExtra, Rejected }
public enum NpcDialogResolutionKind { Resolved, Frontier, Error }
public enum NpcDialogStage { Invocation, Attachment, Resource, Container, Graph, Query, Resolution }
public enum NpcDialogCode
{
    MissingResource, AmbiguousResource, InvalidCatalogueKey, WrongSource, SourceChanged, MalformedContainer, MalformedOperand,
    MalformedAttachment, WrongNodeRole, MissingNode, WrongContext, QueryFailed, QueryStale, QueryUnknown, QueryUnavailable,
    UnsupportedCondition, UnsupportedAction, UnsupportedOperand, UnsupportedCallback, UnsupportedRepresentation, NoEntry, Cycle, Budget, Cancelled, InvalidLimits,
}
public enum NpcDialogPredicateKind { True, LocalFlag, Following, Jilted, Unsupported }
public enum NpcDialogActionKind { Advance, Goto, Dialog, Unsupported }
public enum NpcDialogQueryStatus { KnownTrue, KnownFalse, Unknown, Unavailable, Failed, Stale }
public enum NpcDialogVersionStatus { Known, Unknown, Unavailable, Failed, Stale }
public sealed record NpcDialogQueryContext(NpcDialogInvocation Invocation, NpcDialogAttachmentId Attachment, NpcDialogScrLine Line, int RequestOrdinal);
public sealed record NpcDialogQueryReply(NpcDialogQueryContext Context, NpcDialogQueryStatus Status);
public sealed record NpcDialogVersionReply(NpcDialogQueryContext Context, NpcDialogVersionStatus Status,
    NpcDialogSourceVersion SourceVersion, NpcDialogStateVersion StateVersion, NpcDialogAttachmentStateVersion AttachmentStateVersion);
public interface INpcDialogEntryQueries
{
    NpcDialogVersionReply ReadVersions(NpcDialogQueryContext context);
    NpcDialogQueryReply Following(NpcDialogQueryContext context, NpcDialogNpcId npc, NpcDialogPcId pc);
    NpcDialogQueryReply Jilted(NpcDialogQueryContext context, NpcDialogNpcId npc);
    NpcDialogQueryReply LocalFlag(NpcDialogQueryContext context, NpcDialogAttachmentId attachment, NpcDialogLocalFlag flag);
}
public sealed record NpcDialogTraceStep(NpcDialogScrLine Line, NpcDialogPredicateKind Predicate, NpcDialogQueryStatus Disposition,
    NpcDialogActionKind? Action, NpcDialogScrLine? NextLine, NpcDialogDlgNode? Node);
public sealed record NpcDialogFrontier(NpcDialogStage Stage, NpcDialogCode Code, NpcDialogInvocation Invocation, NpcDialogScrLine? Line);
public sealed record NpcDialogError(NpcDialogStage Stage, NpcDialogCode Code, NpcDialogInvocation Invocation, NpcDialogScrLine? Line);
public enum NpcDialogResourceRole { Script, Dialog }
public sealed record NpcDialogResourceIdentity(Guid SourceId, NpcDialogResourceRole Role, int Ordinal, uint Flags, uint Offset,
    uint StoredBytes, uint OutputBytes, string NameSha256, string PayloadSha256, NpcDialogSourceVersion SourceVersion);
public sealed record NpcDialogResolvedEntry(NpcDialogInvocation Invocation, NpcDialogAttachmentId Attachment,
    NpcDialogResourceIdentity ScriptResource, NpcDialogScrLine ScrLine, NpcDialogResourceIdentity DialogResource,
    NpcDialogDlgNode DlgNode, IReadOnlyList<NpcDialogTraceStep> Trace)
{
    public NpcDialogSourceVersion SourceVersion => Invocation.SourceVersion;
    public NpcDialogStateVersion StateVersion => Invocation.StateVersion;
    public NpcDialogAttachmentStateVersion AttachmentStateVersion => Invocation.AttachmentStateVersion;
}
public sealed class NpcDialogPreparation
{
    internal NpcDialogPreparation(NpcDialogAdmission admission, NpcDialogEntryPlan? plan = null, NpcDialogFrontier? frontier = null, NpcDialogError? error = null)
    { Admission = admission; Plan = plan; Frontier = frontier; Error = error; }
    public NpcDialogAdmission Admission { get; }
    public NpcDialogEntryPlan? Plan { get; }
    public NpcDialogFrontier? Frontier { get; }
    public NpcDialogError? Error { get; }
}
public sealed class NpcDialogResolution
{
    internal NpcDialogResolution(NpcDialogResolvedEntry? entry, NpcDialogFrontier? frontier, NpcDialogError? error, IEnumerable<NpcDialogTraceStep> trace)
    { Entry = entry; Frontier = frontier; Error = error; Trace = Array.AsReadOnly(trace.ToArray()); Kind = entry is not null ? NpcDialogResolutionKind.Resolved : frontier is not null ? NpcDialogResolutionKind.Frontier : NpcDialogResolutionKind.Error; }
    public NpcDialogResolutionKind Kind { get; }
    public NpcDialogResolvedEntry? Entry { get; }
    public NpcDialogFrontier? Frontier { get; }
    public NpcDialogError? Error { get; }
    public IReadOnlyList<NpcDialogTraceStep> Trace { get; }
}
internal sealed record EntryAction(NpcDialogActionKind Kind, int Value, NpcDialogCode? Frontier = null);
internal sealed record EntryInstruction(NpcDialogPredicateKind Predicate, int Flag, EntryAction? True, EntryAction? False, NpcDialogCode? Frontier = null);
public sealed class NpcDialogEntryPlan
{
    internal NpcDialogEntryPlan(NpcDialogInvocation invocation, NpcDialogAttachmentId attachment, NpcDialogResourceIdentity script,
        NpcDialogResourceIdentity dialog, Dictionary<int, EntryInstruction> instructions, NpcDialogEntryLimits limits,
        int count, int capacity, int graphDepth, int records, int fieldBytes, int catalogueEntries, int nameBytes, long totalNameBytes)
    { Invocation = invocation; Attachment = attachment; ScriptResource = script; DialogResource = dialog; Instructions = instructions; Limits = limits; Count = count; Capacity = capacity; GraphDepth = graphDepth; DialogRecords = records; DialogFieldBytes = fieldBytes; CatalogueEntries = catalogueEntries; NameBytes = nameBytes; TotalNameBytes = totalNameBytes; }
    public NpcDialogInvocation Invocation { get; }
    public NpcDialogAttachmentId Attachment { get; }
    public NpcDialogResourceIdentity ScriptResource { get; }
    public NpcDialogResourceIdentity DialogResource { get; }
    internal IReadOnlyDictionary<int, EntryInstruction> Instructions { get; }
    internal NpcDialogEntryLimits Limits { get; }
    internal int Count { get; }
    internal int Capacity { get; }
    internal int GraphDepth { get; }
    internal int DialogRecords { get; }
    internal int DialogFieldBytes { get; }
    internal int CatalogueEntries { get; }
    internal int NameBytes { get; }
    internal long TotalNameBytes { get; }
    internal bool Fits(NpcDialogEntryLimits l) => ScriptResource.OutputBytes <= l.MaxScriptBytes && DialogResource.OutputBytes <= l.MaxDialogBytes &&
        Math.Max(ScriptResource.StoredBytes, DialogResource.StoredBytes) <= l.MaxStoredResourceBytes && Capacity <= l.MaxScriptEntries &&
        GraphDepth <= l.MaxGraphDepth && DialogRecords <= l.MaxDialogRecords && DialogFieldBytes <= l.MaxDialogFieldBytes &&
        CatalogueEntries <= l.MaxCatalogueEntries && NameBytes <= l.MaxCatalogueNameBytes && TotalNameBytes <= l.MaxTotalCatalogueNameBytes;
}
internal sealed class EntryFailure(NpcDialogStage stage, NpcDialogCode code, bool frontier = false, int? line = null) : Exception
{
    internal NpcDialogStage Stage { get; } = stage;
    internal NpcDialogCode Code { get; } = code;
    internal bool Frontier { get; } = frontier;
    internal int? Line { get; } = line;
}
