using System.Security.Cryptography;
using Arcanum.Data;
using Arcanum.Data.Area;
using Arcanum.Scripting.Dialog;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Scripting.NpcDialog;

namespace Arcanum.TerrainViewer.Core;

/// <summary>An NPC whose effective SAP_DIALOG attachment names a nonzero script.</summary>
public sealed record AreaSpeaker(int Index, AreaObjectRecord Row, int ScriptId, long WorldX, long WorldY);

/// <summary>
/// PROJECT diagnostic profile for ARC-SCRIPT-005 conversations in the AREA viewer. None of it is original
/// new-game state: a male human PC with intelligence 8, an initial NPC→PC reaction of 50, every global flag
/// known false, placeholder names, and follow requests accepted.
/// </summary>
public sealed record AreaConversationProfile(DialogPcFacts Pc, DialogSpeakerNames Names, int InitialReaction, int Reach)
{
    public static AreaConversationProfile Diagnostic { get; } = new(new DialogPcFacts(DialogPcGender.Male, 8, false, 0), new("[PC]", "[NPC]"), 50, 3);
}

public enum AreaConversationStartStatus
{
    Started,
    NoSpeaker,
    EntryNotAdmitted,
    EntryFrontier,
    EntryError,
    DialogResourceMismatch,
    DialogUnreadable,
}

public sealed record AreaConversationStart(AreaConversationStartStatus Status, DialogConversation? Conversation, AreaSpeaker? Speaker, string Detail);

/// <summary>
/// Opens a bounded conversation with an area NPC: effective SAP_DIALOG → SCR entry (ARC-SCRIPT-002 resolver) →
/// authenticated DLG member → <see cref="DialogConversation"/>. One diagnostic state persists across talks in a session.
/// Synchronous, single-threaded; Begin re-authenticates the selected archives every time.
/// </summary>
public sealed class AreaConversationHost
{
    public const int GlobalFlagDomain = GlobalFlagLimits.MaxFlags;

    private readonly AreaSnapshot _snapshot;
    private readonly string _scriptArchive;
    private readonly string _dialogArchive;
    private (long Length, string Sha256)? _dialogIdentity;
    private readonly NpcDialogWorldId _world = new(Guid.NewGuid());
    private readonly NpcDialogMapId _map;
    private readonly NpcDialogPcId _pc;

    public AreaConversationHost(AreaSnapshot snapshot, string scriptArchive, string dialogArchive, AreaConversationProfile? profile = null)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _scriptArchive = scriptArchive ?? throw new ArgumentNullException(nameof(scriptArchive));
        _dialogArchive = dialogArchive ?? throw new ArgumentNullException(nameof(dialogArchive));
        Profile = profile ?? AreaConversationProfile.Diagnostic;
        _map = new NpcDialogMapId(snapshot.RuntimeMap, snapshot.Inputs.Single(input => input.Key.Role == AreaSourceRole.Module).Sha256);
        _pc = new NpcDialogPcId(_world, _map, Guid.NewGuid());
        Speakers = FindSpeakers(snapshot);
        var created = GlobalFlagOwner.Create(new GlobalFlagOwnerDescriptor(
            new GlobalFlagIdentity(new WorldId(Guid.NewGuid()), new ModuleId(Guid.NewGuid())), new OwnerEpoch(Guid.NewGuid()), 0,
            Enumerable.Range(0, GlobalFlagDomain).Select(flag => new FlagId(flag)).ToArray(),
            Enumerable.Range(0, GlobalFlagDomain).Select(flag => new GlobalFlagBootstrapValue(new FlagId(flag), false)).ToArray()));
        GlobalFlags = created.Owner ?? throw new InvalidOperationException("GF_BOOTSTRAP_REJECTED");
        State = new AreaConversationState(Profile.InitialReaction);
    }

    public AreaConversationProfile Profile { get; }
    public IReadOnlyList<AreaSpeaker> Speakers { get; }
    public GlobalFlagOwner GlobalFlags { get; }
    public AreaConversationState State { get; }

    internal static IReadOnlyList<AreaSpeaker> FindSpeakers(AreaSnapshot snapshot)
    {
        var speakers = new List<AreaSpeaker>();
        foreach (var row in snapshot.Objects)
        {
            if (row.Type != 16 || row.Scope != AreaScope.Core || row.WorldX is not { } x || row.WorldY is not { } y) continue;
            var scripts = row.EffectiveFields.SingleOrDefault(field => field.Slot == 31);
            var dialog = scripts?.Elements.SingleOrDefault(element => element.Key == (int)NpcDialogEvent.Dialog);
            if (dialog is null || dialog.Words.Count != 3 || dialog.Words[2] is <= 0 or > 99999) continue;
            speakers.Add(new AreaSpeaker(speakers.Count, row, (int)dialog.Words[2], x, y));
        }
        return speakers.AsReadOnly();
    }

    /// <summary>The speaker whose drawn body covers a canvas point; the last (frontmost) covering command wins.</summary>
    public AreaSpeaker? SpeakerAt(IReadOnlyList<AreaDrawCommand> commands, double x, double y)
    {
        for (var index = commands.Count - 1; index >= 0; index--)
        {
            var command = commands[index];
            if (command.Identity is not { } identity || command.Additive) continue;
            var d = command.Destination;
            if (x < d.X || y < d.Y || x >= (long)d.X + d.Width || y >= (long)d.Y + d.Height) continue;
            var speaker = Speakers.FirstOrDefault(candidate => candidate.Row.Key == identity);
            if (speaker is not null) return speaker;
        }
        return null;
    }

    /// <summary>Chebyshev distance from a world cell to the speaker.</summary>
    public static long Distance(AreaSpeaker speaker, long worldX, long worldY) =>
        Math.Max(Math.Abs(speaker.WorldX - worldX), Math.Abs(speaker.WorldY - worldY));

    /// <summary>The nearest speaker within the profile's Chebyshev reach (a project diagnostic rule, not source range).</summary>
    public AreaSpeaker? Nearest(long worldX, long worldY) => Speakers
        .Select(speaker => (speaker, distance: Math.Max(Math.Abs(speaker.WorldX - worldX), Math.Abs(speaker.WorldY - worldY))))
        .Where(item => item.distance <= Profile.Reach)
        .OrderBy(item => item.distance).ThenBy(item => item.speaker.Index)
        .Select(item => item.speaker).FirstOrDefault();

    public AreaConversationStart Begin(AreaSpeaker? speaker, CancellationToken token = default)
    {
        if (speaker is null) return new(AreaConversationStartStatus.NoSpeaker, null, null, "no dialog NPC within reach");
        var row = speaker.Row;
        // The area's second resource archive is the script archive; Prepare re-authenticates both by full hash.
        var scriptIdentity = _snapshot.Inputs.SingleOrDefault(input => input.Key.Role == AreaSourceRole.Resource2);
        var prototypeIdentity = _snapshot.Inputs.SingleOrDefault(input => input.Key == new AreaInputKey(AreaSourceRole.Prototype, row.PrototypeNumber));
        if (scriptIdentity is null || prototypeIdentity is null)
            return new(AreaConversationStartStatus.EntryNotAdmitted, null, speaker, "area snapshot lacks the script or prototype input");
        _dialogIdentity ??= Fingerprint(_dialogArchive);
        var dialogIdentity = _dialogIdentity.Value;
        var scriptId = Guid.NewGuid(); var dialogId = Guid.NewGuid();
        var bindings = _snapshot.Inputs.Select(input => new NpcDialogSourceBinding(NpcDialogEntryResolver.AreaRole(input.Key), Guid.NewGuid(), input.Length, input.Sha256)).ToList();
        bindings.Add(new("Script", scriptId, scriptIdentity.Length, scriptIdentity.Sha256));
        bindings.Add(new("Dialog", dialogId, dialogIdentity.Length, dialogIdentity.Sha256));
        var version = new NpcDialogSourceVersion(Guid.NewGuid(), NpcDialogResourcePolicy.ExplicitSelectedArchives, bindings);
        var npc = new NpcDialogNpcId(_world, _map, row.Key, row.RawSha256, row.SemanticSha256);
        var prototype = new NpcDialogPrototypeId(row.PrototypeNumber, row.PrototypeOwnSha256, prototypeIdentity.Sha256);
        var invocation = new NpcDialogInvocation(_world, _map, npc, prototype, _pc, NpcDialogEvent.Dialog, new(0), version,
            State.StateVersion, State.AttachmentVersion);

        NpcDialogEntryPlan plan;
        using (var scripts = File.OpenRead(_scriptArchive))
        using (var dialogs = File.OpenRead(_dialogArchive))
        {
            var policy = new NpcDialogResourcePolicy(NpcDialogResourcePolicy.ExplicitSelectedArchives, version, scriptId, dialogId,
                [new(scriptId, scripts, scriptIdentity.Length, scriptIdentity.Sha256), new(dialogId, dialogs, dialogIdentity.Length, dialogIdentity.Sha256)]);
            var preparation = NpcDialogEntryResolver.Prepare(_snapshot, invocation, policy, new(), token);
            if (preparation.Admission != NpcDialogAdmission.Admitted || preparation.Plan is null)
                return new(AreaConversationStartStatus.EntryNotAdmitted, null, speaker,
                    $"{preparation.Admission} {preparation.Error?.Code ?? preparation.Frontier?.Code}".Trim());
            plan = preparation.Plan;
        }

        State.Bind(speaker.Index, plan.Attachment);
        var resolution = NpcDialogEntryResolver.Resolve(plan, new EntryQueries(State, speaker.Index, invocation), new(), token);
        if (resolution.Kind == NpcDialogResolutionKind.Frontier)
            return new(AreaConversationStartStatus.EntryFrontier, null, speaker, $"{resolution.Frontier!.Stage} {resolution.Frontier.Code}");
        if (resolution.Kind != NpcDialogResolutionKind.Resolved || resolution.Entry is null)
            return new(AreaConversationStartStatus.EntryError, null, speaker, $"{resolution.Error?.Stage} {resolution.Error?.Code}");
        var entry = resolution.Entry;

        DlgDocument document;
        using (var archive = DatArchive.Open(_dialogArchive, new DatReaderOptions { MaxArchiveBytes = 536870912, MaxStoredEntryBytes = 1048576, MaxDecodedEntryBytes = 1048576 }))
        {
            var member = archive.Entries.SingleOrDefault(candidate => candidate.Ordinal == entry.DialogResource.Ordinal);
            if (member is null)
                return new(AreaConversationStartStatus.DialogResourceMismatch, null, speaker, "dialog member ordinal not found");
            var bytes = archive.ReadEntry(member);
            if (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != entry.DialogResource.PayloadSha256)
                return new(AreaConversationStartStatus.DialogResourceMismatch, null, speaker, "dialog payload differs from the resolved entry");
            try { document = DlgReader.Read(bytes); }
            catch (DlgException exception) { return new(AreaConversationStartStatus.DialogUnreadable, null, speaker, exception.GetType().Name); }
        }

        var participants = new DialogParticipants(new DialogId(speaker.ScriptId), AreaConversationState.PcParticipant, AreaConversationState.Npc(speaker.Index));
        var conversation = DialogConversation.Start(document,
            new DialogConversationRequest(new DialogNodeId(entry.DlgNode.Value), participants, new DialogScriptLine(entry.ScrLine.Value), Profile.Pc, Profile.Names),
            new DialogConversationPorts(State, State, GlobalFlags, State));
        return new(AreaConversationStartStatus.Started, conversation, speaker, $"script {speaker.ScriptId} SCR line {entry.ScrLine.Value} -> DLG node {entry.DlgNode.Value}");
    }

    private static (long, string) Fingerprint(string path)
    {
        using var stream = File.OpenRead(path);
        return (stream.Length, Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());
    }

    private sealed class EntryQueries(AreaConversationState state, int speaker, NpcDialogInvocation invocation) : INpcDialogEntryQueries
    {
        public NpcDialogVersionReply ReadVersions(NpcDialogQueryContext context) =>
            new(context, NpcDialogVersionStatus.Known, invocation.SourceVersion, state.StateVersion, state.AttachmentVersion);
        public NpcDialogQueryReply Following(NpcDialogQueryContext context, NpcDialogNpcId npc, NpcDialogPcId pc) =>
            new(context, state.Follows(speaker) ? NpcDialogQueryStatus.KnownTrue : NpcDialogQueryStatus.KnownFalse);
        public NpcDialogQueryReply Jilted(NpcDialogQueryContext context, NpcDialogNpcId npc) => new(context, NpcDialogQueryStatus.KnownFalse);
        public NpcDialogQueryReply LocalFlag(NpcDialogQueryContext context, NpcDialogAttachmentId attachment, NpcDialogLocalFlag flag) =>
            new(context, state.LocalFlag(speaker, flag.Value) ? NpcDialogQueryStatus.KnownTrue : NpcDialogQueryStatus.KnownFalse);
    }
}

/// <summary>
/// In-memory PROJECT diagnostic owner for one viewer session: per-NPC attachment local flags (seeded from the
/// loaded effective attachment), NPC→PC reaction, PC alignment, quest/rumor/item/area/follower records. It does not simulate
/// inventory, party AI or the world map; it records the typed commands it accepted.
/// </summary>
public sealed class AreaConversationState : IDialogStateQueryPort, IDialogCommandPort, IDialogWorldPort
{
    public static readonly DialogParticipantId PcParticipant = new(1);
    private readonly Dictionary<int, uint> _localFlags = [];
    private readonly Dictionary<int, int> _reaction = [];
    private readonly HashSet<int> _followers = [];
    private readonly Dictionary<int, int> _quests = [];
    private readonly HashSet<int> _rumors = [];
    private readonly HashSet<int> _areas = [];
    private readonly List<int> _items = [];
    private readonly Guid _stateEpoch = Guid.NewGuid();
    private readonly Guid _attachmentEpoch = Guid.NewGuid();
    private ulong _stateRevision;
    private ulong _attachmentRevision;
    private readonly int _initialReaction;

    public AreaConversationState(int initialReaction) => _initialReaction = initialReaction;

    public static DialogParticipantId Npc(int speaker) => new(1000 + speaker);
    public NpcDialogStateVersion StateVersion => new(_stateEpoch, _stateRevision);
    public NpcDialogAttachmentStateVersion AttachmentVersion => new(_attachmentEpoch, _attachmentRevision);
    public IReadOnlyDictionary<int, int> Quests => _quests;
    public IReadOnlySet<int> Rumors => _rumors;
    public IReadOnlySet<int> Areas => _areas;
    public IReadOnlyList<int> Items => _items;
    /// <summary>PC alignment, starting at 0 in this diagnostic profile.</summary>
    public int Alignment { get; private set; }
    public bool Follows(int speaker) => _followers.Contains(speaker);
    public bool LocalFlag(int speaker, int bit) => _localFlags.TryGetValue(speaker, out var flags) && (flags & (1u << bit)) != 0;
    public int Reaction(int speaker) => _reaction.TryGetValue(speaker, out var value) ? value : _initialReaction;

    /// <summary>Seeds the attachment flags once from the loaded effective SAP_DIALOG attachment.</summary>
    internal void Bind(int speaker, NpcDialogAttachmentId attachment) => _localFlags.TryAdd(speaker, attachment.Flags);

    private static int Speaker(DialogParticipantId npc) => checked((int)(npc.Value - 1000));

    public LocalFlagReadResult Read(LocalFlagQuery query) =>
        LocalFlagReadResult.Found(LocalFlag(Speaker(query.Npc), query.Index) ? 1 : 0);

    public DialogCommandResult Execute(LocalFlagWriteCommand command)
    {
        var speaker = Speaker(command.Npc);
        var flags = _localFlags.GetValueOrDefault(speaker);
        _localFlags[speaker] = command.Value == 1 ? flags | (1u << command.Index) : flags & ~(1u << command.Index);
        _attachmentRevision++;
        return DialogCommandResult.Applied;
    }

    public DialogCommandResult Execute(QuestStateCommand command) { _quests[command.QuestId] = command.State; _stateRevision++; return DialogCommandResult.Applied; }
    public DialogCommandResult Execute(RumorLogCommand command) { _rumors.Add(command.RumorId); _stateRevision++; return DialogCommandResult.Applied; }

    public DialogReactionReadResult ReadReaction(DialogReactionQuery query) => DialogReactionReadResult.Of(Reaction(Speaker(query.Npc)));

    public DialogCommandResult Execute(ReactionCommand command)
    {
        var speaker = Speaker(command.Npc);
        var current = Reaction(speaker);
        _reaction[speaker] = command.Mode == DialogReactionMode.Target ? command.Value : checked(current + command.Value);
        _stateRevision++;
        return DialogCommandResult.Applied;
    }

    public DialogCommandResult Execute(GiveItemCommand command) { _items.Add(command.ItemName); _stateRevision++; return DialogCommandResult.Applied; }
    public DialogCommandResult Execute(MarkAreaCommand command) { _areas.Add(command.Area); _stateRevision++; return DialogCommandResult.Applied; }

    public DialogCommandResult Execute(AlignmentCommand command)
    {
        Alignment = command.Mode == DialogReactionMode.Target ? command.Value : checked(Alignment + command.Value);
        _stateRevision++;
        return DialogCommandResult.Applied;
    }

    public DialogJoinResult Execute(JoinCommand command)
    {
        _followers.Add(Speaker(command.Npc));
        _stateRevision++;
        return DialogJoinResult.Accepted;
    }
}

/// <summary>Display strings for the diagnostic panel. They carry decoded record text and typed identifiers only.</summary>
public static class AreaConversationText
{
    public static string Option(DialogOption option)
    {
        var text = option.TextKind switch
        {
            DialogOptionTextKind.Record => option.Text,
            DialogOptionTextKind.Generated => $"[generated '{option.Marker!.Value.Letter}:' phrase; generic text not loaded]",
            _ => "[no reply qualifies: generated farewell; generic text not loaded]",
        };
        var block = option.Block is { } blocked ? $"  (not supported here: {blocked.Kind}{Where(blocked.Source)})" : string.Empty;
        return $"{option.Number}. {text}{block}";
    }

    public static string Outcome(DialogConversation conversation) => conversation.Phase switch
    {
        DialogConversationPhase.Ended when conversation.End!.Kind == DialogEndKind.ReturnToScript =>
            $"[dialog ended; the caller script would continue at SCR line {conversation.End.ScriptLine!.Value.Value} (not executed)]",
        DialogConversationPhase.Ended when conversation.End!.Kind == DialogEndKind.NpcFinalLine => "[dialog ended with the NPC's final line]",
        DialogConversationPhase.Ended => "[dialog closed]",
        DialogConversationPhase.AtFrontier => $"[stopped at a frontier: {conversation.Frontier!.Kind} at node {conversation.Frontier.Node.Value}{Where(conversation.Frontier.Source)}]",
        DialogConversationPhase.Failed => $"[failed: {conversation.Failure!.Kind} at node {conversation.Failure.Node.Value}{Where(conversation.Failure.Source)}]",
        _ => string.Empty,
    };

    public static string Effect(DialogEffectReceipt receipt)
    {
        var command = receipt.Command switch
        {
            GlobalFlagWriteCommand global => $"GF {global.Flag} = {(global.Value ? 1 : 0)}{(receipt.GlobalFlag?.Disposition == GlobalFlagWriteDisposition.NoChange ? " (already set)" : string.Empty)}",
            LocalFlagWriteCommand local => $"LF {local.Index} = {local.Value}",
            QuestStateCommand quest => $"QU {quest.QuestId} -> {quest.State}",
            RumorLogCommand rumor => $"RU {rumor.RumorId} logged",
            ReactionCommand reaction => reaction.Mode == DialogReactionMode.Target ? $"RE target {reaction.Value}" : $"RE adjust {reaction.Value:+0;-0;+0}",
            GiveItemCommand item => $"IN item name {item.ItemName} NPC -> PC",
            MarkAreaCommand area => $"MM area {area.Area} known",
            AlignmentCommand alignment => alignment.Mode == DialogReactionMode.Target ? $"AL set {alignment.Value}" : $"AL adjust {alignment.Value:+0;-0;+0}",
            JoinCommand => "JO follow request",
            _ => receipt.Command.GetType().Name,
        };
        return $"#{receipt.Ordinal} turn {receipt.Turn}: {command} [{receipt.Outcome}]";
    }

    private static string Where(DialogOperationSource? source) => source is { } s ? $", record {s.RecordId.Value} {s.FieldKind} op {s.OperationOrdinal}" : string.Empty;
}
