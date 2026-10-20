using Arcanum.Data;
using Arcanum.Data.Area;
using Arcanum.Data.Audio;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Dialog;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Scripting.NpcDialog;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Reaction;

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
    /// <summary>ARC-SCRIPT-009: the dialog script ran default (the original shows the generic greeting).</summary>
    GenericGreeting,
    /// <summary>ARC-SCRIPT-009: the dialog script ended with skip default and started no conversation.</summary>
    ScriptEnded,
    /// <summary>ARC-SCRIPT-009: the session refused the script's effects.</summary>
    ScriptEffectsRejected,
    /// <summary>ARC-SCRIPT-011: a script or dialog file changed since the content first read it; nothing is applied.</summary>
    SourceChanged,
    /// <summary>ARC-SCRIPT-009: a resumption found no script in the slot (it removed itself); nothing ran.</summary>
    ScriptRemoved,
    /// <summary>ARC-SCRIPT-011: the game's script content could not be loaded for this area; nothing ran.</summary>
    ContentUnavailable,
}

/// <param name="Party">ARC-WORLD-016: the session party port of a roster companion's conversation (its
/// <see cref="SessionDialogPartyPort.LastRefusal"/> names a refused JO/UW), else null.</param>
/// <param name="Document">ARC-UI-043: the DLG the conversation runs on (its NPC lines' condition fields name their voices).</param>
/// <param name="DialogScript">ARC-UI-043: the script number of that DLG (0 without a conversation).</param>
public sealed record AreaConversationStart(AreaConversationStartStatus Status, DialogConversation? Conversation, AreaSpeaker? Speaker, string Detail,
    SessionDialogPartyPort? Party = null, DlgDocument? Document = null, int DialogScript = 0)
{
    /// <summary>ARC-UI-043: the voice identity of the NPC line <paramref name="line"/> of this conversation (audio study SN21):
    /// the DLG script number and the number in the line's condition field; −1 without a document, line or record, or with an
    /// empty condition field.</summary>
    public int VoiceIdentity(DialogNpcLine? line) =>
        Document is null || line is null || !Document.TryGetRecord(line.Node.Value, out var record) || record is null ? -1
            : SoundCatalog.SpeechIdentity(DialogScript, SoundCatalog.SpeechConditionField(record.Condition.Bytes));
}

/// <summary>
/// Opens a bounded conversation with an area NPC: effective SAP_DIALOG → SCR dispatcher (ARC-SCRIPT-009) →
/// DLG member of the dialog action → <see cref="DialogConversation"/>. Dialog consequences live in the caller's
/// <see cref="GameSessionOwner"/> (ARC-SCRIPT-006), so they persist across talks, map changes and snapshots.
/// The host owns neither the session nor the script content. Scripts and dialogs come from the game's universal content
/// stack (ARC-SCRIPT-011, <see cref="ScriptContent"/>): the original's name for each script number and the winning copy,
/// the same for every map. Synchronous, single-threaded; Begin and Resume read the files every time.
/// </summary>
public sealed class AreaConversationHost
{
    private readonly AreaSnapshot _snapshot;
    private readonly GameSessionOwner _session;
    private readonly ScriptContent? _content;
    private readonly string _contentDetail;
    private readonly AreaInventoryHost? _inventory;
    private readonly bool _party;
    private readonly AreaConversationProfile _baseProfile;
    private readonly Func<AreaSpeaker, AreaCompanionFacts?> _companions;
    private readonly Func<string, SessionNpcAdvancement?>? _npcAdvancement;

    /// <param name="inventory">ARC-UI-004: when it covers this map, negative IN moves the item in the session inventory
    /// (<see cref="SessionDialogInventoryPort"/>); otherwise IN is only recorded in <see cref="AreaConversationState.Items"/>.</param>
    /// <param name="party">ARC-WORLD-016: a speaker listed by <see cref="AreaCompanionRoster"/> talks with the session party
    /// port (JO, WA, UW through <see cref="SessionDialogPartyPort"/> with its measured facts), and its "following" is the
    /// session party standing; other speakers keep the diagnostic follow key.</param>
    /// <param name="content">The game's script content (<see cref="AreaContentRoot.OpenScripts"/>), borrowed; null when it
    /// could not be loaded, and then every talk is <see cref="AreaConversationStartStatus.ContentUnavailable"/> with
    /// <paramref name="contentDetail"/>.</param>
    /// <param name="companions">ARC-WORLD-037: the follow facts of a speaker that can join the party (null when it cannot); by
    /// default the measured roster (<see cref="AreaCompanionRoster.For"/>).</param>
    /// <param name="npcAdvancement">ARC-SCRIPT-016: an NPC's stored advancement for a script's scheme change
    /// (<see cref="AreaScriptHost"/>); null: only the PC's scheme changes.</param>
    public AreaConversationHost(AreaSnapshot snapshot, ScriptContent? content, GameSessionOwner session,
        AreaConversationProfile? profile = null, AreaInventoryHost? inventory = null, bool party = false, string contentDetail = "no script content",
        Func<AreaSpeaker, AreaCompanionFacts?>? companions = null, Func<string, SessionNpcAdvancement?>? npcAdvancement = null)
    {
        _npcAdvancement = npcAdvancement;
        _companions = companions ?? AreaCompanionRoster.For;
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _content = content;
        _contentDetail = contentDetail ?? throw new ArgumentNullException(nameof(contentDetail));
        // ARC-WORLD-027: a session with a hero talks with the hero's own gender, intelligence and race (DLG gates);
        // the given profile keeps the names, reaction and reach. Rebind applies the same rule to the new session.
        _baseProfile = profile ?? AreaConversationProfile.Diagnostic;
        Profile = session.QueryHeroDialogFacts() is { } hero ? _baseProfile with { Pc = hero } : _baseProfile;
        _inventory = inventory is not null && inventory.Covers(snapshot.RuntimeMap) ? inventory : null;
        Speakers = FindSpeakers(snapshot);
        _party = party;
        State = new AreaConversationState(session, new SessionMapId(snapshot.RuntimeMap),
            Speakers.Select(speaker => AreaConversationState.EntityId(speaker.Row)).ToArray(), Profile.InitialReaction,
            party ? Speakers.Where(speaker => _companions(speaker) is not null).Select(speaker => speaker.Index).ToHashSet() : null);
        Scripts = new AreaScriptHost(session, new SessionMapId(snapshot.RuntimeMap), Speakers, State, content, npcAdvancement);
    }

    /// <summary>True when roster speakers talk through the session party (ARC-WORLD-016).</summary>
    public bool PartyWired => _party;

    /// <summary>
    /// A host over another session (a quick load replaced the area session): same area, content and given profile; the
    /// PC facts follow the new session's hero (ARC-WORLD-027).
    /// Throws <see cref="ArgumentException"/> when that session holds dialog values the state ports cannot read.
    /// </summary>
    public AreaConversationHost Rebind(GameSessionOwner session) => new(_snapshot, _content, session, _baseProfile, _inventory, _party, _contentDetail, _companions, _npcAdvancement);

    /// <summary>True when negative IN goes to the session inventory.</summary>
    public bool InventoryWired => _inventory is not null;

    public AreaConversationProfile Profile { get; }
    public IReadOnlyList<AreaSpeaker> Speakers { get; }
    /// <summary>The session's global flags: the conversation reads and writes them directly.</summary>
    public IVersionedGlobalFlagOwner GlobalFlags => _session.Flags;
    public AreaConversationState State { get; }

    /// <summary>The map's dialog NPCs in object order (the host's <see cref="Speakers"/>).</summary>
    public static IReadOnlyList<AreaSpeaker> FindSpeakers(AreaSnapshot snapshot)
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

    /// <summary>Chebyshev distance from a world cell to the speaker (at <paramref name="at"/> when given: where a moving
    /// companion stands now, ARC-WORLD-016; its T0 cell otherwise).</summary>
    public static long Distance(AreaSpeaker speaker, long worldX, long worldY, Func<AreaSpeaker, (long X, long Y)>? at = null)
    {
        var (x, y) = at?.Invoke(speaker) ?? (speaker.WorldX, speaker.WorldY);
        return Math.Max(Math.Abs(x - worldX), Math.Abs(y - worldY));
    }

    /// <summary>The nearest speaker within the profile's Chebyshev reach (a project diagnostic rule, not source range),
    /// among those <paramref name="present"/> admits (ARC-WORLD-017: a companion standing on another map is not here).</summary>
    public AreaSpeaker? Nearest(long worldX, long worldY, Func<AreaSpeaker, (long X, long Y)>? at = null, Func<AreaSpeaker, bool>? present = null) => Speakers
        .Where(speaker => present?.Invoke(speaker) ?? true)
        .Select(speaker => (speaker, distance: Distance(speaker, worldX, worldY, at)))
        .Where(item => item.distance <= Profile.Reach)
        .OrderBy(item => item.distance).ThenBy(item => item.speaker.Index)
        .Select(item => item.speaker).FirstOrDefault();

    public AreaConversationStart Begin(AreaSpeaker? speaker, CancellationToken token = default)
    {
        if (speaker is null) return new(AreaConversationStartStatus.NoSpeaker, null, null, "no dialog NPC within reach");
        return Run(speaker, 0, token);
    }

    /// <summary>
    /// ARC-SCRIPT-009: continues the speaker's dialog script after a conversation ended with "return to script"
    /// (<see cref="DialogEndKind.ReturnToScript"/>): line N for a response of −N, the line after the dialog action for
    /// 0. The script may start another conversation, which is returned like <see cref="Begin"/>.
    /// </summary>
    public AreaConversationStart Resume(AreaSpeaker speaker, DialogScriptLine line, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(speaker);
        return Run(speaker, line.Value, token);
    }

    /// <summary>
    /// ARC-WORLD-039: runs a deferred SCR call the session's event pass hands over (<see cref="ISessionScriptWorld.RunScriptCall"/>)
    /// through the SCR dispatcher with this area's script host and the session's current rules, and applies its effects.
    /// False, with the reason, when the content is missing or changed, the run is refused, the session refuses an effect,
    /// or the run would start a conversation (the viewer opens talks only on the player's request; nothing is applied).
    /// </summary>
    public bool RunCall(SessionScriptCall call, out string detail)
    {
        ArgumentNullException.ThrowIfNull(call);
        if (_content is null) { detail = _contentDetail; return false; }
        if (!_content.VerifySeen()) { detail = "a script or dialog file changed since it was first read"; return false; }
        var result = ScrDispatcher.RunCall(new(call.Script, call.Line, Scripts.Local(call.Triggerer), Scripts.Local(call.Attachee)), Scripts,
            new() { Rules = _session.Rules.Scripts, Admission = AreaScriptHost.Admission });
        LastCall = result;
        if (_content.Changed) { detail = $"script {call.Script}: a script file changed since it was first read"; return false; }
        if (result.Outcome == ScrOutcome.Refused)
        {
            detail = $"script {call.Script} SCR line {result.Refusal!.Line}: {result.Refusal.Code} type {result.Refusal.Type}";
            return false;
        }
        if (result.Effects.Dialog is not null) { detail = $"script {call.Script}: a call that starts a conversation is not run by the viewer"; return false; }
        if (!Scripts.Apply(result.Effects)) { detail = $"script {call.Script} {result.Stop}: the session refused its effects"; return false; }
        detail = $"script {call.Script} {result.Stop} at SCR line {result.StopLine}";
        return true;
    }

    /// <summary>The last deferred call run of <see cref="RunCall"/>.</summary>
    public ScrRunResult? LastCall { get; private set; }

    /// <summary>The last dialog script run of <see cref="Begin"/> or <see cref="Resume"/> (outcome, stop, trace).</summary>
    public ScrRunResult? LastRun { get; private set; }

    /// <summary>The dispatcher's host over this area and session.</summary>
    public AreaScriptHost Scripts { get; }

    // Talk request and resumption run SAP_DIALOG through the SCR dispatcher (ARC-SCRIPT-009): run default is the
    // original's generic greeting (not modelled here), a dialog action starts the DLG of the run's script.
    private AreaConversationStart Run(AreaSpeaker speaker, int line, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_content is null) return new(AreaConversationStartStatus.ContentUnavailable, null, speaker, _contentDetail);
        // ARC-SCRIPT-011: one content never mixes two versions of a file; every file read before is checked again, and once
        // one changed nothing more runs from it.
        if (!_content.VerifySeen()) return new(AreaConversationStartStatus.SourceChanged, null, speaker, "a script or dialog file changed since it was first read");
        State.Bind(speaker.Index, AreaScriptHost.LoadedSlot(speaker)?.LocalFlags ?? 0);
        var result = ScrDispatcher.RunObject(new(ScrPoint.Dialog, Scripts.Object(speaker), Scripts.Pc, null, line), Scripts,
            new() { Rules = _session.Rules.Scripts, Admission = AreaScriptHost.Admission });
        LastRun = result;
        if (_content.Changed) return new(AreaConversationStartStatus.SourceChanged, null, speaker, $"script {result.Script}: a script file changed since it was first read");
        if (result.Outcome == ScrOutcome.Refused)
            return new(AreaConversationStartStatus.EntryNotAdmitted, null, speaker,
                $"script {result.Script} SCR line {result.Refusal!.Line}: {result.Refusal.Code} type {result.Refusal.Type}");
        // The DLG is read before any effect is applied, so a changed dialog file applies nothing; a DLG that is missing or
        // unreadable leaves the script's effects applied, as they were before ARC-SCRIPT-011.
        (AreaConversationStartStatus Status, DlgDocument? Document, string Detail) loaded = (AreaConversationStartStatus.DialogResourceMismatch, null, "no dialog action");
        if (result.Effects.Dialog is { } requested)
        {
            // ARC-DATA-022: the session's current rules choose the data fixes, read at this decision.
            loaded = AreaScriptHost.LoadDialog(_content, requested.Script, requested.Node, _session.Rules.Data);
            if (loaded.Status == AreaConversationStartStatus.SourceChanged) return new(loaded.Status, null, speaker, loaded.Detail);
        }
        token.ThrowIfCancellationRequested();
        if (!Scripts.Apply(result.Effects))
            return new(AreaConversationStartStatus.ScriptEffectsRejected, null, speaker, $"script {result.Script} {result.Stop}: the session refused its effects");
        if (result.Effects.Dialog is not { } dialog)
            return line != 0 && result.Stop == ScrStop.NoScript
                ? new(AreaConversationStartStatus.ScriptRemoved, null, speaker, "the dialog slot holds no script: nothing to resume")
                : result.Outcome == ScrOutcome.RunDefault
                ? new(AreaConversationStartStatus.GenericGreeting, null, speaker, $"script {result.Script?.ToString() ?? "none"} {result.Stop}: run default (the generic greeting is not modelled)")
                : new(AreaConversationStartStatus.ScriptEnded, null, speaker, $"script {result.Script} {result.Stop} at SCR line {result.StopLine}: skip default");
        if (loaded.Status != AreaConversationStartStatus.Started) return new(loaded.Status, null, speaker, loaded.Detail);
        var document = loaded.Document;

        var participants = new DialogParticipants(new DialogId(dialog.Script), AreaConversationState.PcParticipant, AreaConversationState.Npc(speaker.Index));
        var party = PartyPort(speaker);
        var conversation = DialogConversation.Start(document!,
            new DialogConversationRequest(new DialogNodeId(dialog.Node), participants, new DialogScriptLine(dialog.Line), Profile.Pc, Profile.Names),
            new DialogConversationPorts(State, State, _session.Flags, State, party, _inventory?.DialogPort(_session, SpeakerEntity), State));
        return new(AreaConversationStartStatus.Started, conversation, speaker, $"script {dialog.Script} SCR line {dialog.Line} -> DLG node {dialog.Node}", party,
            document, dialog.Script);
    }

    /// <summary>The session party port for a roster speaker when the party is wired; facts are read at each call.</summary>
    private SessionDialogPartyPort? PartyPort(AreaSpeaker speaker)
    {
        if (!_party || _companions(speaker) is not { } facts) return null;
        var npc = AreaConversationState.Npc(speaker.Index);
        var entity = AreaConversationState.EntityId(speaker.Row);
        return new SessionDialogPartyPort(_session, AreaConversationState.PcParticipant,
            participant => participant == npc ? entity : null,
            participant => participant == npc ? AreaCompanionRoster.Facts(facts, State.Alignment, State.Reaction(speaker.Index),
                AreaAdvancement.Level(_session, entity, facts.Level)) : null);
    }

    private string? SpeakerEntity(DialogParticipantId npc) =>
        npc.Value - 1000 is var index && index >= 0 && index < Speakers.Count ? AreaConversationState.EntityId(Speakers[(int)index].Row) : null;
}

/// <summary>
/// Dialog state ports over a <see cref="GameSessionOwner"/> (ARC-SCRIPT-006). Session-owned and therefore kept across
/// talks, map changes and snapshots: global flags (through the conversation's GF port), PC quest states, known
/// rumors and quelled rumors (RQ, ARC-UI-008; the session story domain), the NPC→PC reaction (the session reaction domain, ARC-WORLD-026: the speaker's
/// record, else the profile's initial reaction as its base), and, per speaker, the attachment local flags and the
/// follow consent as map-local entity changes on the speaker's map (absent = loaded baseline: attachment flags
/// from the area, not following). Reaction facts are diagnostic: every speaker is a human, not aloof NPC with the
/// profile's initial reaction as base; the PC is the session hero when there is one, else a human with reaction modifier
/// 0 and no reputation or effects. The viewer does not open or close the reaction conversation slot yet, so every change
/// lands in the remembered slot. Still PROJECT diagnostic, in memory only and
/// lost with this object: PC alignment, items given (IN) when the host has no session inventory (ARC-UI-004 wires it),
/// and areas marked (MM); they wait for their own owners.
/// </summary>
public sealed class AreaConversationState : IDialogStateQueryPort, IDialogCommandPort, IDialogWorldPort, IDialogRumorPort
{
    public const string LocalFlagsProperty = "dialog.lf";
    public const string FollowProperty = "follow.pc";
    /// <summary>Source floor for the NPC's reaction when the PC accepts its quest, and the bonus on completion.</summary>
    public const int AcceptedQuestReaction = ReactionRules.AcceptedQuestReaction;
    public const int CompletedQuestReactionBonus = ReactionRules.CompletedQuestBonus;
    /// <summary>The PC's reaction facts in a session without a hero (diagnostic).</summary>
    public static ReactionPcFacts DiagnosticPc { get; } = new(CharacterRace.Human, 0, 0, [], [], [], false);
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
    private readonly HashSet<int> _party;
    private readonly List<SessionExperienceAward> _awards = [];

    /// <summary>ARC-WORLD-036: the experience awards the talk's commands gave (a completed quest) since the last call, in order;
    /// the host passes their follower steps on (<see cref="AreaAdvancement.LevelFollowers"/>).</summary>
    public IReadOnlyList<SessionExperienceAward> TakeAwards()
    {
        var awards = _awards.ToArray();
        _awards.Clear();
        return awards;
    }

    /// <param name="speakerEntities">Stable session entity id of each speaker index (see <see cref="EntityId"/>).</param>
    /// <param name="partySpeakers">ARC-WORLD-016: speaker indexes whose "following" is the session party standing
    /// (<see cref="SessionCompanion.LedByPc"/>, true while waiting too) instead of the diagnostic follow key.</param>
    public AreaConversationState(GameSessionOwner session, SessionMapId map, IReadOnlyList<string> speakerEntities, int initialReaction,
        IReadOnlySet<int>? partySpeakers = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        ArgumentNullException.ThrowIfNull(speakerEntities);
        _map = map; _entities = speakerEntities.ToArray(); _initialReaction = initialReaction;
        _party = partySpeakers?.ToHashSet() ?? [];
        if (!ReactionRules.IsValid(NpcFacts())) throw new ArgumentOutOfRangeException(nameof(initialReaction));
        // A restored session may carry any long under these keys; refuse values this port cannot read.
        foreach (var entity in _entities)
        {
            var lf = session.QueryMapChange(map, MapChangeKey.ForEntity(entity, LocalFlagsProperty));
            var follow = session.QueryMapChange(map, MapChangeKey.ForEntity(entity, FollowProperty));
            var counters = session.QueryMapChange(map, MapChangeKey.ForEntity(entity, AreaScriptHost.CountersProperty));
            var script = session.QueryMapChange(map, MapChangeKey.ForEntity(entity, AreaScriptHost.ScriptProperty));
            if (lf is < 0 or > uint.MaxValue || follow is not (null or 1)
                || counters is < 0 or > uint.MaxValue || script is < int.MinValue or > int.MaxValue)
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
    public bool Follows(int speaker) => _party.Contains(speaker)
        ? _session.QueryCompanion(_entities[speaker]).LedByPc : Read(speaker, FollowProperty) == 1;
    public bool LocalFlag(int speaker, int bit) => (LocalFlags(speaker) & (1u << bit)) != 0;
    public int Reaction(int speaker) =>
        _session.QueryReaction(_entities[speaker], NpcFacts(), PcFacts).View?.Effective ?? throw new InvalidOperationException("AREA_SESSION_REACTION_INVALID");

    /// <summary>The diagnostic NPC side of every speaker's reaction.</summary>
    public ReactionNpcFacts NpcFacts() => new(_initialReaction, CharacterRace.Human, false, false);

    /// <summary>The PC side: the session hero's facts, else <see cref="DiagnosticPc"/>.</summary>
    public ReactionPcFacts PcFacts => _session.QueryHeroReactionFacts() ?? DiagnosticPc;

    /// <summary>Records the loaded effective SAP_DIALOG attachment flags as the speaker's baseline.</summary>
    internal void Bind(int speaker, uint loadedFlags) => _baseline[speaker] = loadedFlags;

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
    /// one that became Completed adds 10 (source quest rules). A completion gives the quest's experience inside the session's quest
    /// rules when the session has experience data (with --hero since ARC-UI-007); quest alignment is not applied.
    /// </summary>
    public DialogCommandResult Execute(QuestStateCommand command)
    {
        var speaker = Speaker(command.NpcContext);
        if (!Enum.IsDefined((SessionQuestState)command.State)) return DialogCommandResult.Rejected;
        var result = _session.SetQuestState(_session.Version, command.QuestId, (SessionQuestState)command.State, out var award);
        if (award is not null) _awards.Add(award);
        if (result.Disposition != GameSessionDisposition.Changed) return Result(result);
        var entity = _entities[speaker];
        // Source quest rules: accepted raises the effective reaction to at least 41 (a delta), completed adds 10.
        var written = _session.QueryQuest(command.QuestId)!.Value.Effective switch
        {
            SessionQuestState.Accepted when Reaction(speaker) < AcceptedQuestReaction =>
                _session.SetReaction(_session.Version, entity, NpcFacts(), PcFacts, AcceptedQuestReaction),
            SessionQuestState.Completed => _session.AdjustReaction(_session.Version, entity, NpcFacts(), PcFacts, CompletedQuestReactionBonus),
            _ => null,
        };
        // The quest change is already committed: a refused reaction write is a partial effect, reported as indeterminate.
        if (written?.Disposition == GameSessionDisposition.Rejected) throw new InvalidOperationException("AREA_SESSION_QUEST_REACTION_REJECTED");
        return DialogCommandResult.Applied;
    }

    public DialogCommandResult Execute(RumorLogCommand command) => Result(_session.LearnRumor(_session.Version, command.RumorId));

    public DialogCommandResult Execute(QuellRumorCommand command) => Result(_session.QuellRumor(_session.Version, command.RumorId));

    public DialogReactionReadResult ReadReaction(DialogReactionQuery query) => DialogReactionReadResult.Of(Reaction(Speaker(query.Npc)));

    public DialogCommandResult Execute(ReactionCommand command)
    {
        var entity = _entities[Speaker(command.Npc)];
        var result = command.Mode == DialogReactionMode.Target
            ? _session.SetReaction(_session.Version, entity, NpcFacts(), PcFacts, command.Value)
            : _session.AdjustReaction(_session.Version, entity, NpcFacts(), PcFacts, command.Value);
        return result.Disposition == GameSessionDisposition.Rejected ? DialogCommandResult.Rejected : DialogCommandResult.Applied;
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
            QuellRumorCommand quell => $"RQ {quell.RumorId} quelled",
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
