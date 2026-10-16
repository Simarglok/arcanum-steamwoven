using System.Security.Cryptography;
using Arcanum.Data;
using Arcanum.Data.Area;
using Arcanum.Scripting.Dialog;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Scripting.NpcDialog;
using Arcanum.Session;

namespace Arcanum.TerrainViewer.Core;

/// <summary>An NPC whose effective SAP_DIALOG attachment names a nonzero script.</summary>
public sealed record AreaSpeaker(int Index, AreaObjectRecord Row, int ScriptId, long WorldX, long WorldY);

/// <summary>
/// PROJECT diagnostic profile for ARC-SCRIPT-005 conversations in the AREA viewer. None of it is original
/// new-game state: a male human PC with intelligence 8, an initial NPC→PC reaction of 50, placeholder names,
/// and follow requests accepted. Global flags are the session's original new-game flags (ARC-SCRIPT-006).
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
/// authenticated DLG member → <see cref="DialogConversation"/>. Dialog consequences live in the caller's
/// <see cref="GameSessionOwner"/> (ARC-SCRIPT-006), so they persist across talks, map changes and snapshots.
/// The host does not own the session. Synchronous, single-threaded; Begin re-authenticates the selected archives every time.
/// </summary>
public sealed class AreaConversationHost
{
    private readonly AreaSnapshot _snapshot;
    private readonly GameSessionOwner _session;
    private readonly string _scriptArchive;
    private readonly string _dialogArchive;
    private (long Length, string Sha256)? _dialogIdentity;
    private readonly NpcDialogWorldId _world = new(Guid.NewGuid());
    private readonly NpcDialogMapId _map;
    private readonly NpcDialogPcId _pc;
    private readonly AreaInventoryHost? _inventory;

    /// <param name="inventory">ARC-UI-004: when it covers this map, negative IN moves the item in the session inventory
    /// (<see cref="SessionDialogInventoryPort"/>); otherwise IN is only recorded in <see cref="AreaConversationState.Items"/>.</param>
    public AreaConversationHost(AreaSnapshot snapshot, string scriptArchive, string dialogArchive, GameSessionOwner session,
        AreaConversationProfile? profile = null, AreaInventoryHost? inventory = null)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _scriptArchive = scriptArchive ?? throw new ArgumentNullException(nameof(scriptArchive));
        _dialogArchive = dialogArchive ?? throw new ArgumentNullException(nameof(dialogArchive));
        Profile = profile ?? AreaConversationProfile.Diagnostic;
        _inventory = inventory is not null && inventory.Covers(snapshot.RuntimeMap) ? inventory : null;
        _map = new NpcDialogMapId(snapshot.RuntimeMap, snapshot.Inputs.Single(input => input.Key.Role == AreaSourceRole.Module).Sha256);
        _pc = new NpcDialogPcId(_world, _map, Guid.NewGuid());
        Speakers = FindSpeakers(snapshot);
        State = new AreaConversationState(session, new SessionMapId(snapshot.RuntimeMap),
            Speakers.Select(speaker => AreaConversationState.EntityId(speaker.Row)).ToArray(), Profile.InitialReaction);
    }

    /// <summary>
    /// A host over another session (a quick load replaced the area session): same area, archives and profile.
    /// Throws <see cref="ArgumentException"/> when that session holds dialog values the state ports cannot read.
    /// </summary>
    public AreaConversationHost Rebind(GameSessionOwner session) => new(_snapshot, _scriptArchive, _dialogArchive, session, Profile, _inventory);

    /// <summary>True when negative IN goes to the session inventory.</summary>
    public bool InventoryWired => _inventory is not null;

    public AreaConversationProfile Profile { get; }
    public IReadOnlyList<AreaSpeaker> Speakers { get; }
    /// <summary>The session's global flags: the conversation reads and writes them directly.</summary>
    public IVersionedGlobalFlagOwner GlobalFlags => _session.Flags;
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
            new DialogConversationPorts(State, State, _session.Flags, State, Inventory: _inventory?.DialogPort(_session, SpeakerEntity)));
        return new(AreaConversationStartStatus.Started, conversation, speaker, $"script {speaker.ScriptId} SCR line {entry.ScrLine.Value} -> DLG node {entry.DlgNode.Value}");
    }

    private string? SpeakerEntity(DialogParticipantId npc) =>
        npc.Value - 1000 is var index && index >= 0 && index < Speakers.Count ? AreaConversationState.EntityId(Speakers[(int)index].Row) : null;

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
/// Dialog state ports over a <see cref="GameSessionOwner"/> (ARC-SCRIPT-006). Session-owned and therefore kept across
/// talks, map changes and snapshots: global flags (through the conversation's GF port), PC quest states and known
/// rumors (the session story domain), and, per speaker, the attachment local flags, the NPC→PC reaction and the
/// follow consent as map-local entity changes on the speaker's map (absent = loaded baseline: attachment flags
/// from the area, the profile's initial reaction, not following). Still PROJECT diagnostic, in memory only and
/// lost with this object: PC alignment, items given (IN) when the host has no session inventory (ARC-UI-004 wires it),
/// and areas marked (MM); they wait for their own owners.
/// </summary>
public sealed class AreaConversationState : IDialogStateQueryPort, IDialogCommandPort, IDialogWorldPort
{
    public const string LocalFlagsProperty = "dialog.lf";
    public const string ReactionProperty = "reaction.pc";
    public const string FollowProperty = "follow.pc";
    /// <summary>Source floor for the NPC's reaction when the PC accepts its quest, and the bonus on completion.</summary>
    public const int AcceptedQuestReaction = 41;
    public const int CompletedQuestReactionBonus = 10;
    public static readonly DialogParticipantId PcParticipant = new(1);

    private readonly GameSessionOwner _session;
    private readonly SessionMapId _map;
    private readonly IReadOnlyList<string> _entities;
    private readonly Dictionary<int, uint> _baseline = [];
    private readonly HashSet<int> _areas = [];
    private readonly List<int> _items = [];
    private readonly Guid _stateEpoch = Guid.NewGuid();
    private readonly Guid _attachmentEpoch = Guid.NewGuid();
    private readonly int _initialReaction;

    /// <param name="speakerEntities">Stable session entity id of each speaker index (see <see cref="EntityId"/>).</param>
    public AreaConversationState(GameSessionOwner session, SessionMapId map, IReadOnlyList<string> speakerEntities, int initialReaction)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        ArgumentNullException.ThrowIfNull(speakerEntities);
        _map = map; _entities = speakerEntities.ToArray(); _initialReaction = initialReaction;
        // A restored session may carry any long under these keys; refuse values this port cannot read.
        foreach (var entity in _entities)
        {
            var lf = session.QueryMapChange(map, MapChangeKey.ForEntity(entity, LocalFlagsProperty));
            var reaction = session.QueryMapChange(map, MapChangeKey.ForEntity(entity, ReactionProperty));
            var follow = session.QueryMapChange(map, MapChangeKey.ForEntity(entity, FollowProperty));
            if (lf is < 0 or > uint.MaxValue || reaction is < int.MinValue or > int.MaxValue || follow is not (null or 1))
                throw new ArgumentException("AREA_SESSION_DIALOG_STATE_INVALID", nameof(session));
        }
    }

    /// <summary>Session entity id of an area object: its source object-id fingerprint.</summary>
    public static string EntityId(AreaObjectRecord row) => "oid:" + row.Key.ObjectIdSha256;

    public static DialogParticipantId Npc(int speaker) => new(1000 + speaker);
    // Both versions follow the session revision: any session change makes an earlier entry query stale.
    public NpcDialogStateVersion StateVersion => new(_stateEpoch, checked((ulong)_session.Version.Revision));
    public NpcDialogAttachmentStateVersion AttachmentVersion => new(_attachmentEpoch, checked((ulong)_session.Version.Revision));
    /// <summary>The session's quests and rumors (detached copy).</summary>
    public SessionStoryState Story => _session.QueryStory();
    public IReadOnlySet<int> Areas => _areas;
    public IReadOnlyList<int> Items => _items;
    /// <summary>PC alignment, starting at 0 in this diagnostic profile; not session state.</summary>
    public int Alignment { get; private set; }
    public bool Follows(int speaker) => Read(speaker, FollowProperty) == 1;
    public bool LocalFlag(int speaker, int bit) => (LocalFlags(speaker) & (1u << bit)) != 0;
    public int Reaction(int speaker) => Read(speaker, ReactionProperty) is { } value ? checked((int)value) : _initialReaction;

    /// <summary>Records the loaded effective SAP_DIALOG attachment flags as the speaker's baseline.</summary>
    internal void Bind(int speaker, NpcDialogAttachmentId attachment) => _baseline[speaker] = attachment.Flags;

    private uint LocalFlags(int speaker) => Read(speaker, LocalFlagsProperty) is { } value ? checked((uint)value) : _baseline.GetValueOrDefault(speaker);

    private int Speaker(DialogParticipantId npc)
    {
        var speaker = checked((int)(npc.Value - 1000));
        if (speaker < 0 || speaker >= _entities.Count) throw new ArgumentOutOfRangeException(nameof(npc));
        return speaker;
    }

    private MapChangeKey Key(int speaker, string property) => MapChangeKey.ForEntity(_entities[speaker], property);
    private long? Read(int speaker, string property) => _session.QueryMapChange(_map, Key(speaker, property));

    // A value equal to the loaded baseline clears the key, so an untouched NPC has no session entry.
    private GameSessionMutationResult Write(int speaker, string property, long value, long baseline) => value == baseline
        ? _session.ClearMapChange(_session.Version, _map, Key(speaker, property))
        : _session.SetMapChange(_session.Version, _map, Key(speaker, property), value);

    private static DialogCommandResult Result(GameSessionMutationResult result) =>
        result.Disposition == GameSessionDisposition.Rejected ? DialogCommandResult.Rejected : DialogCommandResult.Applied;

    public LocalFlagReadResult Read(LocalFlagQuery query) =>
        LocalFlagReadResult.Found(LocalFlag(Speaker(query.Npc), query.Index) ? 1 : 0);

    public DialogCommandResult Execute(LocalFlagWriteCommand command)
    {
        var speaker = Speaker(command.Npc);
        var flags = LocalFlags(speaker);
        flags = command.Value == 1 ? flags | (1u << command.Index) : flags & ~(1u << command.Index);
        return Result(Write(speaker, LocalFlagsProperty, flags, _baseline.GetValueOrDefault(speaker)));
    }

    /// <summary>
    /// QU through the session's quest rules. A quest that became Accepted lifts the NPC's reaction to at least 41 and
    /// one that became Completed adds 10 (source quest rules); experience and quest alignment need quest data and are not applied.
    /// </summary>
    public DialogCommandResult Execute(QuestStateCommand command)
    {
        var speaker = Speaker(command.NpcContext);
        if (!Enum.IsDefined((SessionQuestState)command.State)) return DialogCommandResult.Rejected;
        var result = _session.SetQuestState(_session.Version, command.QuestId, (SessionQuestState)command.State);
        if (result.Disposition != GameSessionDisposition.Changed) return Result(result);
        var reaction = Reaction(speaker);
        var target = _session.QueryQuest(command.QuestId)!.Value.Effective switch
        {
            SessionQuestState.Accepted when reaction < AcceptedQuestReaction => AcceptedQuestReaction,
            SessionQuestState.Completed => checked(reaction + CompletedQuestReactionBonus),
            _ => reaction,
        };
        // The quest change is already committed: a refused reaction write is a partial effect, reported as indeterminate.
        if (Write(speaker, ReactionProperty, target, _initialReaction).Disposition == GameSessionDisposition.Rejected)
            throw new InvalidOperationException("AREA_SESSION_QUEST_REACTION_REJECTED");
        return DialogCommandResult.Applied;
    }

    public DialogCommandResult Execute(RumorLogCommand command) => Result(_session.LearnRumor(_session.Version, command.RumorId));

    public DialogReactionReadResult ReadReaction(DialogReactionQuery query) => DialogReactionReadResult.Of(Reaction(Speaker(query.Npc)));

    public DialogCommandResult Execute(ReactionCommand command)
    {
        var speaker = Speaker(command.Npc);
        var value = command.Mode == DialogReactionMode.Target ? command.Value : checked(Reaction(speaker) + command.Value);
        return Result(Write(speaker, ReactionProperty, value, _initialReaction));
    }

    public DialogCommandResult Execute(GiveItemCommand command) { _items.Add(command.ItemName); return DialogCommandResult.Applied; }
    public DialogCommandResult Execute(MarkAreaCommand command) { _areas.Add(command.Area); return DialogCommandResult.Applied; }

    public DialogCommandResult Execute(AlignmentCommand command)
    {
        Alignment = command.Mode == DialogReactionMode.Target ? command.Value : checked(Alignment + command.Value);
        return DialogCommandResult.Applied;
    }

    /// <summary>Follow requests are accepted (diagnostic profile); the consent is session state of the NPC.</summary>
    public DialogJoinResult Execute(JoinCommand command) =>
        Write(Speaker(command.Npc), FollowProperty, 1, 0).Disposition == GameSessionDisposition.Rejected
            ? DialogJoinResult.Rejected : DialogJoinResult.Accepted;
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
