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
using Arcanum.Simulation;

namespace Arcanum.World;

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
    /// <summary>ARC-WORLD-043: the session refused to open the NPC's reaction conversation (its record budget); the dialog
    /// script's effects are applied, the conversation did not start.</summary>
    ReactionRejected,
}

/// <param name="Party">ARC-WORLD-016: the session party port of a roster companion's conversation (its
/// <see cref="SessionDialogPartyPort.LastRefusal"/> names a refused JO/UW), else null.</param>
/// <param name="Document">ARC-UI-043: the DLG the conversation runs on (its NPC lines' condition fields name their voices).</param>
/// <param name="DialogScript">ARC-UI-043: the script number of that DLG (0 without a conversation).</param>
/// <param name="GreetingLine">ARC-DEV-324: for <see cref="AreaConversationStartStatus.GenericGreeting"/> of a talk request, the line the original floats over the NPC
/// (no window opens; the host has queued it on <see cref="AreaScriptHost.TakeFloats"/>), or null when the greeting said nothing.</param>
public sealed record AreaConversationStart(AreaConversationStartStatus Status, DialogConversation? Conversation, AreaSpeaker? Speaker, string Detail,
    SessionDialogPartyPort? Party = null, DlgDocument? Document = null, int DialogScript = 0, string? GreetingLine = null)
{
    /// <summary>ARC-UI-043: the voice identity of the NPC line <paramref name="line"/> of this conversation (audio study SN21):
    /// the DLG script number and the number in the line's condition field; −1 without a document, line or record, or with an
    /// empty condition field.</summary>
    public int VoiceIdentity(DialogNpcLine? line) => line?.Origin switch
    {
        // ARC-WORLD-058: an override line speaks with its own record's voice (0x418870); generated and reputation lines have none.
        { Kind: DialogLineOriginKind.Override } origin => Greeting?.Overrides is { } overrides && Greeting.OverrideScriptNumber is { } script
            && overrides.TryGetRecord(origin.Number, out var own) && own is not null
            ? SoundCatalog.SpeechIdentity(script, SoundCatalog.SpeechConditionField(own.Condition.Bytes)) : -1,
        not null => -1,
        _ => Document is null || line is null || !Document.TryGetRecord(line.Node.Value, out var record) || record is null ? -1
            : SoundCatalog.SpeechIdentity(DialogScript, SoundCatalog.SpeechConditionField(record.Condition.Bytes)),
    };

    /// <summary>ARC-WORLD-058: the greeting port of this conversation (its override dialog), or null without one.</summary>
    public AreaGreeting? Greeting { get; init; }
}

/// <summary>
/// Opens a bounded conversation with an area NPC: effective SAP_DIALOG → SCR dispatcher (ARC-SCRIPT-009) →
/// DLG member of the dialog action → <see cref="DialogConversation"/>. Dialog consequences live in the caller's
/// <see cref="GameSessionOwner"/> (ARC-SCRIPT-006), so they persist across talks, map changes and snapshots.
/// The host owns neither the session nor the script content. Scripts and dialogs come from the game's universal content
/// stack (ARC-SCRIPT-011, <see cref="ScriptContent"/>): the original's name for each script number and the winning copy,
/// the same for every map. Synchronous, single-threaded; Begin and Resume read the files every time.
/// ARC-WORLD-043: as the original's dialog start and end (0x412FD0, 0x413280), every started conversation opens the NPC's
/// reaction conversation slot in the session (<see cref="GameSessionOwner.BeginReactionConversation"/>, which uses the hero's armed "force good reaction" fate, ARC-WORLD-054) before
/// its first effect, and the caller closes it with <see cref="EndReaction"/> on every exit path, before a resumed script runs.
/// </summary>
public sealed class AreaConversationHost
{
    private readonly AreaSnapshot _snapshot;
    private readonly SessionMapId _map;
    private readonly GameSessionOwner _session;
    private readonly ScriptContent? _content;
    private readonly string _contentDetail;
    private readonly AreaInventoryHost? _inventory;
    private readonly bool _party;
    private readonly AreaConversationProfile _baseProfile;
    private readonly Func<AreaSpeaker, AreaCompanionFacts?> _companions;
    private readonly Func<string, SessionNpcAdvancement?>? _npcAdvancement;
    private readonly Func<string, (SessionNpcAdvancement Loaded, Arcanum.Simulation.Character.AdvancementModifiers Modifiers)?>? _npcSheet;
    // ARC-DEV-081 (MIG5): the speakers of the shown map (the first _ownCount of Speakers) and the companions carried here that were admitted
    // after them (AdmitVisitor), each with the follow facts it was admitted with; a visitor whose stored dialog state cannot be read is
    // remembered as refused (until a new session rebinds the host).
    private readonly List<AreaSpeaker> _speakers;
    private readonly int _ownCount;
    private readonly Dictionary<AreaObjectKey, AreaCompanionFacts> _visitorFacts = [];
    private readonly Dictionary<string, string> _refusedVisitors = new(StringComparer.Ordinal);
    // The inventory host as given (Rebind filters it again for the map).
    private readonly AreaInventoryHost? _givenInventory;
    // ARC-WORLD-043: the speaker whose reaction conversation slot this host opened and has not closed yet.
    private int? _reactionOpen;

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
    /// <param name="npcSheet">ARC-SKILLS-009: an NPC's sheet and effects from its map data, for a script's get skill
    /// (<see cref="AreaScriptHost.SkillLevel"/>); null: an NPC's level is 0.</param>
    public AreaConversationHost(AreaSnapshot snapshot, ScriptContent? content, GameSessionOwner session,
        AreaConversationProfile? profile = null, AreaInventoryHost? inventory = null, bool party = false, string contentDetail = "no script content",
        Func<AreaSpeaker, AreaCompanionFacts?>? companions = null, Func<string, SessionNpcAdvancement?>? npcAdvancement = null,
        Func<string, (SessionNpcAdvancement Loaded, Arcanum.Simulation.Character.AdvancementModifiers Modifiers)?>? npcSheet = null)
        : this(snapshot ?? throw new ArgumentNullException(nameof(snapshot)), new SessionMapId(snapshot.RuntimeMap), FindSpeakers(snapshot), content, session,
            profile, inventory, party, contentDetail, companions, npcAdvancement, npcSheet)
    {
    }

    /// <summary>Test seam: the given speakers instead of the snapshot's dialog NPCs.</summary>
    internal AreaConversationHost(AreaSnapshot snapshot, IReadOnlyList<AreaSpeaker> speakers, ScriptContent? content, GameSessionOwner session)
        : this(snapshot ?? throw new ArgumentNullException(nameof(snapshot)), new SessionMapId(snapshot.RuntimeMap),
            speakers ?? throw new ArgumentNullException(nameof(speakers)), content, session, null, null, false, "no script content", null, null)
    {
    }

    /// <summary>Test seam (ARC-DEV-081): the given speakers listed on <paramref name="shown"/>, a map other than the snapshot's when a test needs
    /// a companion carried to another map, with the party wired as the viewer wires it in map mode.</summary>
    internal AreaConversationHost(AreaSnapshot snapshot, SessionMapId shown, IReadOnlyList<AreaSpeaker> speakers, ScriptContent? content, GameSessionOwner session,
        bool party, AreaInventoryHost? inventory = null)
        : this(snapshot ?? throw new ArgumentNullException(nameof(snapshot)), shown, speakers ?? throw new ArgumentNullException(nameof(speakers)), content, session,
            null, inventory, party, "no script content", null, null)
    {
    }

    private AreaConversationHost(AreaSnapshot snapshot, SessionMapId map, IReadOnlyList<AreaSpeaker> speakers, ScriptContent? content, GameSessionOwner session,
        AreaConversationProfile? profile, AreaInventoryHost? inventory, bool party, string contentDetail,
        Func<AreaSpeaker, AreaCompanionFacts?>? companions, Func<string, SessionNpcAdvancement?>? npcAdvancement,
        Func<string, (SessionNpcAdvancement Loaded, Arcanum.Simulation.Character.AdvancementModifiers Modifiers)?>? npcSheet = null)
    {
        _npcAdvancement = npcAdvancement;
        _npcSheet = npcSheet;
        _companions = companions ?? AreaCompanionRoster.For;
        _snapshot = snapshot;
        _map = map;
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _content = content;
        _contentDetail = contentDetail ?? throw new ArgumentNullException(nameof(contentDetail));
        // ARC-WORLD-027: a session with a hero talks with the hero's own gender, intelligence and race (DLG gates);
        // the given profile keeps the names, reaction and reach. Rebind applies the same rule to the new session.
        _baseProfile = profile ?? AreaConversationProfile.Diagnostic;
        Profile = session.QueryHeroDialogFacts() is { } hero ? _baseProfile with { Pc = hero } : _baseProfile;
        _inventory = inventory is not null && inventory.Covers(map.RuntimeMap) ? inventory : null;
        _givenInventory = inventory;
        _speakers = [.. speakers];
        _ownCount = _speakers.Count;
        _party = party;
        State = new AreaConversationState(session, map,
            Speakers.Select(speaker => AreaConversationState.EntityId(speaker.Row)).ToArray(), Profile.InitialReaction,
            party ? Speakers.Where(speaker => _companions(speaker) is not null).Select(speaker => speaker.Index).ToHashSet() : null,
            Speakers.Select(speaker => Scope(speaker.Row, session)).ToArray());
        Scripts = new AreaScriptHost(session, map, _speakers, State, content, npcAdvancement, npcSheet, Profile);
    }

    /// <summary>The shown map: where every speaker's object stands for the scripts and where its own dialog state lives, except a carried
    /// companion's (<see cref="StateMap"/>).</summary>
    public SessionMapId Map => _map;

    /// <summary>
    /// ARC-DEV-081 (MIG5): the map whose entity keys hold <paramref name="speaker"/>'s dialog state (its local flags, script slot, counters
    /// and follow consent): the shown map for its own speakers, a carried companion's home map for a speaker admitted by
    /// <see cref="AdmitVisitor"/> (rows OL31–OL33 of the companion orders study: the original keeps one object wherever it stands; its
    /// state stays on the record's map until the object directory, MIG6).
    /// </summary>
    public SessionMapId StateMap(AreaSpeaker speaker) => State.MapOf(speaker.Index);

    /// <summary>True for a speaker <see cref="AdmitVisitor"/> listed after the map's own: a companion carried here, whose presence is the
    /// viewer's companion group's, not the map's data.</summary>
    public bool IsCarried(AreaSpeaker speaker) => speaker.Index >= _ownCount && speaker.Index < _speakers.Count && _speakers[speaker.Index] == speaker;

    /// <summary>
    /// ARC-DEV-081 (MIG5, was ARC-WORLD-048's visitor host): lists a companion carried to the shown map as one more speaker of this host,
    /// after the map's own and the ones admitted before, so the one host talks to every speaker standing here. A companion this host
    /// already lists is returned as it is. The speaker's object stands on the shown map for the scripts and the talk; its dialog
    /// state stays under its entity on <paramref name="template"/>'s home map (<see cref="StateMap"/>), so a talk where it visits reads
    /// and writes the state a talk at home does; JO, WA, UW and LV go through the session party with the template's follow facts. Null
    /// when the host is not party-wired (no companion talks then) or the session's state under the entity cannot be read (a damaged
    /// save): <paramref name="refusal"/> names it that once, and the host keeps refusing it (a later call returns null with no refusal)
    /// until the host is rebound over another session.
    /// </summary>
    public AreaSpeaker? AdmitVisitor(AreaCompanionTemplate template, out string? refusal)
    {
        ArgumentNullException.ThrowIfNull(template);
        refusal = null;
        // One object, one speaker: a row of the same entity id counts as listed too (the id, not the row's key, names its dialog state).
        if (_speakers.FirstOrDefault(listed => listed.Row.Key == template.Key || AreaConversationState.EntityId(listed.Row) == template.Entity) is { } known) return known;
        if (!_party) return null;
        if (_refusedVisitors.ContainsKey(template.Entity)) return null;
        var speaker = template.Speaker with { Index = _speakers.Count };
        try { State.Admit(speaker.Index, template.Entity, Scope(speaker.Row, _session), template.Home); }
        catch (ArgumentException exception)
        {
            refusal = exception.Message;
            _refusedVisitors[template.Entity] = refusal;
            return null;
        }
        _speakers.Add(speaker);
        _visitorFacts[template.Key] = template.Facts;
        return speaker;
    }

    // The follow facts of a speaker that can join the party: the ones a carried companion was admitted with, else the host's own rule.
    private AreaCompanionFacts? FactsOf(AreaSpeaker speaker) =>
        _visitorFacts.TryGetValue(speaker.Row.Key, out var facts) ? facts : _companions(speaker);

    /// <summary>True when roster speakers talk through the session party (ARC-WORLD-016).</summary>
    public bool PartyWired => _party;

    /// <summary>
    /// A host over another session (a quick load replaced the area session): same area, content and given profile; the
    /// PC facts follow the new session's hero (ARC-WORLD-027). It starts with no reaction conversation open (ARC-WORLD-043:
    /// the viewer cannot quick-load during a talk, whose panel takes every key).
    /// Throws <see cref="ArgumentException"/> when that session holds dialog values the state ports cannot read.
    /// </summary>
    public AreaConversationHost Rebind(GameSessionOwner session)
    {
        // ARC-DEV-081: the companions carried here are admitted again over the new session when asked (their stored state is read then).
        var host = new AreaConversationHost(_snapshot, _map, _speakers.Take(_ownCount).ToArray(), _content, session, _baseProfile, _givenInventory, _party,
            _contentDetail, _companions, _npcAdvancement, _npcSheet).KeepingGreetingTexts(_greetingTexts);
        // ARC-SCRIPT-050: the shown map's use host still answers the talk's queries (it follows the viewer's session).
        host.Scripts.QueryHost = Scripts.QueryHost;
        host.GoldKindSource = GoldKindSource;
        host.HeroAttack = HeroAttack;
        host.HeroAttackAvailable = HeroAttackAvailable;
        host.NpcHostile = NpcHostile;
        host.KillNpc = KillNpc;
        return host;
    }

    /// <summary>True when negative IN goes to the session inventory.</summary>
    public bool InventoryWired => _inventory is not null;

    public AreaConversationProfile Profile { get; }
    /// <summary>The map's dialog NPCs in object order, then the companions carried here (<see cref="AdmitVisitor"/>): a speaker's index is its
    /// position, and means something only in this host (its dialog state is indexed by it).</summary>
    public IReadOnlyList<AreaSpeaker> Speakers => _speakers;
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

    /// <summary>ARC-WORLD-044: an NPC's origin (field 0x123) and faction (field 0x124), its effective fields' slots 80 and 81;
    /// 0 when absent or not an NPC. They pick the reputation effects of its reaction to the PC.</summary>
    public static (int Origin, int Faction) Scope(AreaObjectRecord row) =>
        row.Type == 16 ? (Field(row, OriginSlot), Field(row, FactionSlot)) : (0, 0);

    /// <summary>ARC-SCRIPT-049: <see cref="Scope(AreaObjectRecord)"/> with the faction a script's set faction gave the NPC
    /// (<see cref="AreaUseScriptHost.Faction"/>).</summary>
    public static (int Origin, int Faction) Scope(AreaObjectRecord row, GameSessionOwner session)
    {
        var (origin, faction) = Scope(row);
        if (row.Type != 16) return (origin, faction);
        // ARC-DEV-230: the origin the dialog action or gave the NPC (SessionNpcOrigin.Property), else its map data's.
        var set = session.QueryMapChange(new SessionMapId(row.Key.RuntimeMap), SessionNpcOrigin.Key(AreaConversationState.EntityId(row)));
        return (set is { } value ? unchecked((int)value) : origin, AreaUseScriptHost.Faction(session, row, faction));
    }

    private const int OriginSlot = 80, FactionSlot = 81;

    private static int Field(AreaObjectRecord row, int slot) =>
        row.EffectiveFields.FirstOrDefault(field => field.Slot == slot) is { Presence: AreaPresence.Value, Number: { } n } && n is >= int.MinValue and <= int.MaxValue ? (int)n : 0;

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
        SchematicCrafter = null;
        if (speaker is null) return new(AreaConversationStartStatus.NoSpeaker, null, null, "no dialog NPC within reach");
        return Run(speaker, 0, token);
    }

    /// <summary>
    /// ARC-SCRIPT-009: continues the speaker's dialog script after a conversation ended with "return to script"
    /// (<see cref="DialogEndKind.ReturnToScript"/>): line N for a response of −N, the line after the dialog action for
    /// 0. The script may start another conversation, which is returned like <see cref="Begin"/>.
    /// </summary>
    [OriginalFunction(0x568480, OriginalFunctionScope.Partial)]
    public AreaConversationStart Resume(AreaSpeaker speaker, DialogScriptLine line, CancellationToken token = default)
    {
        SchematicCrafter = null;
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
            new() { Rules = _session.Rules.Scripts, Admission = Scripts.AdmissionFor(Scripts.Local(call.Attachee)) });
        LastCall = result;
        if (_content.Changed) { detail = $"script {call.Script}: a script file changed since it was first read"; return false; }
        if (result.Outcome == ScrOutcome.Refused)
        {
            detail = $"script {call.Script} SCR line {result.Refusal!.Line}: {result.Refusal.Code} type {result.Refusal.Type}";
            return false;
        }
        if (result.Stop == ScrStop.NotModelled) { detail = $"script {call.Script}: NotModelled (get area on the start map, GA3)"; return false; }
        if (result.Effects.Dialog is not null) { detail = $"script {call.Script}: a call that starts a conversation is not run by the viewer"; return false; }
        if (!Scripts.Apply(result.Effects)) { detail = $"script {call.Script} {result.Stop}: the session refused its effects"; return false; }
        detail = $"script {call.Script} {result.Stop} at SCR line {result.StopLine}";
        return true;
    }

    /// <summary>
    /// ARC-WORLD-043: closes the reaction conversation slot a started conversation opened (the original's dialog end, 0x413280,
    /// on every exit: a reply that ends the talk, the player's close, a frontier or failure, teardown, a map change): the
    /// session moves the value to the remembered slot, so the NPC has met the PC and the next talk's ME 1 holds. False when no
    /// slot is open, or when the session refused the end (not expected: the begin already made the record); the refusal is
    /// <see cref="ReactionFailure"/> and the host no longer counts the slot as open (the next begin of that NPC overwrites it).
    /// </summary>
    [OriginalFunction(0x413280, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x5678D0, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x5679C0, OriginalFunctionScope.Partial)]
    public bool EndReaction()
    {
        if (_reactionOpen is not { } speaker) { ReactionFailure = GameSessionFailure.None; return false; }
        _reactionOpen = null;
        var ended = State.EndReaction(speaker);
        ReactionFailure = ended.Disposition == GameSessionDisposition.Rejected ? ended.Failure : GameSessionFailure.None;
        return ReactionFailure == GameSessionFailure.None;
    }

    /// <summary>The session's refusal of the last <see cref="EndReaction"/>, else <see cref="GameSessionFailure.None"/>.</summary>
    public GameSessionFailure ReactionFailure { get; private set; }

    /// <summary>The speaker whose reaction conversation slot is open, or null (ARC-WORLD-043).</summary>
    public int? ReactionOpen => _reactionOpen;

    /// <summary>The last deferred call run of <see cref="RunCall"/>.</summary>
    public ScrRunResult? LastCall { get; private set; }

    /// <summary>The last dialog script run of <see cref="Begin"/> or <see cref="Resume"/> (outcome, stop, trace).</summary>
    public ScrRunResult? LastRun { get; private set; }

    /// <summary>ARC-DEV-304 (SU15): whether the speaker's dialog script file has the death-speech flag 0x8, the one that lets a dead NPC run its own dialog (without it point 9 of a dead
    /// NPC runs default); null when the script cannot be read.</summary>
    public bool? DeathSpeech(AreaSpeaker speaker)
    {
        ArgumentNullException.ThrowIfNull(speaker);
        // The live dialog slot, as the dispatcher reads it (the session's script change over the map data's).
        if (_content is null || Scripts.Slot(Scripts.Object(speaker), ScrPoint.Dialog) is not { } slot) return null;
        return _content.Script(slot.Script).Program is { } program ? (program.FileFlags & ScrCatalogue.FileFlag.DeathSpeech) != 0 : null;
    }

    /// <summary>The dispatcher's host over this area and session.</summary>
    public AreaScriptHost Scripts { get; }

    // Talk request and resumption run SAP_DIALOG through the SCR dispatcher (ARC-SCRIPT-009): run default is the
    // original's generic greeting (not modelled here), a dialog action starts the DLG of the run's script.
    private AreaConversationStart Run(AreaSpeaker speaker, int line, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // ARC-WORLD-043: a talk whose end the caller missed is ended before any script runs, as the original ends a dialog
        // before the caller's script continues.
        EndReaction();
        if (_content is null) return new(AreaConversationStartStatus.ContentUnavailable, null, speaker, _contentDetail);
        // ARC-SCRIPT-011: one content never mixes two versions of a file; every file read before is checked again, and once
        // one changed nothing more runs from it.
        if (!_content.VerifySeen()) return new(AreaConversationStartStatus.SourceChanged, null, speaker, "a script or dialog file changed since it was first read");
        var slot = AreaScriptHost.LoadedSlot(speaker);
        State.Bind(speaker.Index, slot?.LocalFlags ?? 0, slot?.Counters ?? 0);
        var result = ScrDispatcher.RunObject(new(ScrPoint.Dialog, Scripts.Object(speaker), Scripts.Pc, null, line), Scripts,
            new() { Rules = _session.Rules.Scripts, Admission = Scripts.AdmissionFor(Scripts.Object(speaker)) });
        LastRun = result;
        if (_content.Changed) return new(AreaConversationStartStatus.SourceChanged, null, speaker, $"script {result.Script}: a script file changed since it was first read");
        if (result.Outcome == ScrOutcome.Refused)
            return new(AreaConversationStartStatus.EntryNotAdmitted, null, speaker,
                $"script {result.Script} SCR line {result.Refusal!.Line}: {result.Refusal.Code} type {result.Refusal.Type}");
        // ARC-SCRIPT-050: a question the viewer cannot answer (get area on the start map) ended the dispatch with nothing applied.
        if (result.Stop == ScrStop.NotModelled)
            return new(AreaConversationStartStatus.EntryNotAdmitted, null, speaker, $"script {result.Script}: NotModelled (get area on the start map, GA3)");
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
                ? GenericGreeting(speaker, line, result)
                : new(AreaConversationStartStatus.ScriptEnded, null, speaker, $"script {result.Script} {result.Stop} at SCR line {result.StopLine}: skip default");
        if (loaded.Status != AreaConversationStartStatus.Started) return new(loaded.Status, null, speaker, loaded.Detail);
        return Open(speaker, dialog, loaded.Document!);
    }

    // ARC-DEV-324 (B24, GT219): a talk request whose dialog point ran default shows the generic greeting: the greeting builder's line over the NPC as a floating line (0x57CC70
    // calls 0x5681C0, which builds the text with 0x4132A0), no window. A resumed dialog script (line != 0) is no talk request and says nothing.
    [OriginalFunction(0x5681C0, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x57CC70, OriginalFunctionScope.Partial)]
    private AreaConversationStart GenericGreeting(AreaSpeaker speaker, int line, ScrRunResult result)
    {
        var detail = $"script {result.Script?.ToString() ?? "none"} {result.Stop}: run default";
        if (line != 0) return new(AreaConversationStartStatus.GenericGreeting, null, speaker, detail + " (a resumed script: no greeting)");
        // The state the greeting's facts read holds this host's speakers only.
        if (!Speakers.Contains(speaker)) return new(AreaConversationStartStatus.GenericGreeting, null, speaker, detail + " (not a speaker of this host: nothing said)");
        DialogGenericGreetingResult said;
        try
        {
            var greeting = Greeting(speaker);
            if (greeting is null) return new(AreaConversationStartStatus.GenericGreeting, null, speaker, detail + " (the generic greeting's texts did not load: nothing said)");
            said = DialogGenericGreeting.Say(greeting, new DialogReactionQuery(AreaConversationState.Npc(speaker.Index), AreaConversationState.PcParticipant), Profile.Names,
                DialogTextEncoding.Windows1252, DialogConversationOptions.HardMaxTextBytes);
        }
        // The greeting is a courtesy of the talk, not its gate: a texts, facts or session failure says nothing and never escapes the host (typed failure at the boundary).
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new(AreaConversationStartStatus.GenericGreeting, null, speaker, detail + $" (the generic greeting failed: {exception.GetType().Name}: nothing said)");
        }
        if (said.Status != DialogGenericGreetingStatus.Said) return new(AreaConversationStartStatus.GenericGreeting, null, speaker, detail + $" (the generic greeting says nothing: {said.Status})");
        Scripts.Float(Scripts.Object(speaker), said.Text);
        return new(AreaConversationStartStatus.GenericGreeting, null, speaker, detail + $" (the generic greeting: {said.Origin!.Kind} {said.Origin.Number})", GreetingLine: said.Text);
    }

    /// <summary>
    /// ARC-SCRIPT-046: opens the conversation a tile script, or a script it called, asked for with its dialog action (0x447282: the DLG
    /// of the running script at the action's node, the PC with the action's attachee): <paramref name="speaker"/> is that attachee and
    /// the run's effects are applied already (<see cref="AreaUseScriptHost.RunPlaced"/>), so no dialog script runs. Which local flags
    /// the DLG's LF reads and writes for such a start is not read in the executable (level F0; the action stored the callee's into the
    /// speaker's use slot, X11), so a DLG with any LF operation is refused (<see cref="AreaConversationStartStatus.EntryNotAdmitted"/>)
    /// rather than run on the speaker's dialog slot. A "return to script" end has no dialog script of the speaker to resume (the
    /// caller does not resume it). ARC-SCRIPT-047: a start at the dialog point (a heartbeat's call-ex of the speaker's dialog point, ledger
    /// TG5) stored the flags in the speaker's dialog slot, the one the DLG's LF reads, so its DLG may use LF; its "return to script" end
    /// resumes the speaker's dialog slot as a talk's does (<see cref="Resume"/>, the original's dialog UI runs point 9 of the NPC).
    /// </summary>
    public AreaConversationStart BeginScripted(AreaSpeaker speaker, ScrDialogStart dialog, CancellationToken token = default)
    {
        SchematicCrafter = null;
        ArgumentNullException.ThrowIfNull(speaker); ArgumentNullException.ThrowIfNull(dialog);
        token.ThrowIfCancellationRequested();
        EndReaction();
        if (_content is null) return new(AreaConversationStartStatus.ContentUnavailable, null, speaker, _contentDetail);
        if (!_content.VerifySeen()) return new(AreaConversationStartStatus.SourceChanged, null, speaker, "a script or dialog file changed since it was first read");
        var slot = AreaScriptHost.LoadedSlot(speaker);
        State.Bind(speaker.Index, slot?.LocalFlags ?? 0, slot?.Counters ?? 0);
        var loaded = AreaScriptHost.LoadDialog(_content, dialog.Script, dialog.Node, _session.Rules.Data);
        if (loaded.Status != AreaConversationStartStatus.Started) return new(loaded.Status, null, speaker, loaded.Detail);
        if (dialog.Point != ScrPoint.Dialog && UsesLocalFlags(loaded.Document!))
            return new(AreaConversationStartStatus.EntryNotAdmitted, null, speaker, $"script {dialog.Script}: the DLG of a scripted conversation reads local flags or counters (not modelled)");
        token.ThrowIfCancellationRequested();
        return Open(speaker, dialog, loaded.Document!);
    }

    // ARC-SCRIPT-046: whether any condition or action field of the DLG holds an LF operation (operations are comma-separated, the
    // opcode their first two letters, case-insensitive, as the conversation grammar reads them).
    private static bool UsesLocalFlags(DlgDocument document) => document.Records.Any(record => new[] { record.Condition, record.Action }
        .Any(field => System.Text.Encoding.Latin1.GetString(field.Bytes.ToArray()).Split(',')
            .Any(operation => operation.TrimStart(' ', '\t').StartsWith("lf", StringComparison.OrdinalIgnoreCase)
                // ARC-DEV-196: the LC counters are the same attachment's as the flags; which attachment a talk started at another point reads is unread in the exe (X11), so it is refused like LF.
                || operation.TrimStart(' ', '\t').StartsWith("lc", StringComparison.OrdinalIgnoreCase))));

    [OriginalFunction(0x412FD0, OriginalFunctionScope.Partial)]
    private AreaConversationStart Open(AreaSpeaker speaker, ScrDialogStart dialog, DlgDocument document)
    {
        var participants = new DialogParticipants(new DialogId(dialog.Script), AreaConversationState.PcParticipant, AreaConversationState.Npc(speaker.Index));
        var party = PartyPort(speaker);
        // ARC-WORLD-043: the original starts the reaction conversation before the dialog's first NPC line and action (0x412FD0).
        if (State.BeginReaction(speaker.Index).Disposition == GameSessionDisposition.Rejected)
            return new(AreaConversationStartStatus.ReactionRejected, null, speaker, $"script {dialog.Script}: the session refused the NPC's reaction conversation");
        _reactionOpen = speaker.Index;
        var greeting = Greeting(speaker);
        var conversation = DialogConversation.Start(document,
            new DialogConversationRequest(new DialogNodeId(dialog.Node), participants, new DialogScriptLine(dialog.Line), Profile.Pc, Profile.Names),
            new DialogConversationPorts(State, State, _session.Flags, State, party, InventoryPort(), State,
                // ARC-SKILLS-004: the skill gates read the session hero; the diagnostic PC has no skills, so they stay unadmitted.
                _session.QueryHero() is null ? null : new SessionDialogSkillPort(_session, AreaConversationState.PcParticipant), State,
                // ARC-WORLD-054: FP gives the session hero a fate point; without a hero it stays unadmitted.
                _session.QueryHero() is null ? null : new SessionDialogFatePort(_session, AreaConversationState.PcParticipant),
                // ARC-WORLD-044: RP reads and changes the session PC's reputations.
                new SessionDialogReputationPort(_session, AreaConversationState.PcParticipant),
                // ARC-TECHNOLOGY-006: SU asks for the schematic window with the speaker as crafter; the host opens it once the talk ended.
                new SchematicPort(this),
                // ARC-WORLD-058: an NPC g: greeting, when the module's greeting texts load.
                greeting,
                // ARC-UI-050: a b: reply asks for the barter window with the speaker as merchant; it needs the session hero.
                _session.QueryHero() is null ? null : new BarterPort(this),
                // ARC-DEV-152: gv, pv and pf read and write the session's global variables and the hero's variables and flags; with the
                // diagnostic PC (no hero) the PC cells read 0 and a PC write is ignored (SA14), the global variables work as with a hero.
                new SessionDialogScriptStorePort(_session, AreaConversationState.PcParticipant),
                // ARC-DEV-178: xp gives the session hero the experience of a quest experience level; the award's follower steps are the host's
                // (TakeAwards), as for a completed quest. Without a hero or the experience tables it stays unadmitted, as before.
                _session.QueryHero() is null || _session.Experience is null ? null : new SessionDialogExperiencePort(_session, AreaConversationState.PcParticipant, State.AddAward),
                // ARC-DEV-179: $$ reads and moves the hero's and the followers' gold and the speaker's; it needs the session hero and the inventory
                // wiring (the NPCs' piles live in the map seeds); without them it stays unadmitted.
                _session.QueryHero() is null || _inventory is null ? null
                    : _inventory.GoldDialogPort(_session, SpeakerEntity, GoldKindSource?.Invoke() ?? new(SessionItemType.Gold, AreaBarterData.GoldPrototype, 0, 0),
                        _session.Rules.Dialog.ShortPayerPaysNothing),
                // ARC-DEV-180: qu, qa and qb read the quest log the QU action writes (the session's, which has the one PC).
                new SessionDialogQuestPort(_session, AreaConversationState.PcParticipant),
                // ARC-DEV-190: co runs the attack event of the hero on the speaker through the viewer's AI host; without that host (no AI) it stays unadmitted.
                HeroAttack is null || HeroAttackAvailable?.Invoke() == false ? null : new CombatPort(this),
                // ARC-DEV-191: ss reads and raises the game's story state, ar asks the session's known areas (the set mm writes), ia the area of the PC's map.
                new SessionDialogStoryPort(_session, AreaConversationState.PcParticipant),
                new SessionDialogAreaPort(_session, AreaConversationState.PcParticipant),
                // The map's area is answered by the shown map's use host (map mode only; the PC and its map do not change during a talk); without an answer ia stays unadmitted.
                Scripts.PcMapArea() is null ? null : new MapAreaPort(this),
                // ARC-DEV-192: ch, pe, le, al, ma, ta and na read the session hero's stats; the diagnostic PC has none, so they stay unadmitted.
                _session.QueryHero() is null ? null : new SessionDialogStatPort(_session, AreaConversationState.PcParticipant),
                // ARC-DEV-193: the reply step's hostile check after an action string; without the AI host (no AI) the check is not made.
                NpcHostile is null || HeroAttackAvailable?.Invoke() == false ? null : new HostilePort(this),
                // ARC-DEV-194: in and ni read the item lists of the hero, its followers, the speaker and the speaker's substitute container; they need the session hero and the inventory wiring.
                _session.QueryHero() is null || _inventory is null ? null : _inventory.ItemHoldDialogPort(_session, SpeakerEntity),
                // ARC-DEV-199: the non-negative IN action hands the hero's item to the speaker; it needs the same wiring.
                _session.QueryHero() is null || _inventory is null ? null : _inventory.TakeItemDialogPort(_session, SpeakerEntity),
                // ARC-DEV-195: fo and wt read the speaker's party record, wa, lv, so and sc change it (a wait gets the roster's timer facts); the session party needs no hero.
                CompanionPort(speaker),
                // ARC-DEV-196: lc reads and writes the speaker's dialog-attachment counters, kept beside its local flags.
                State,
                // ARC-DEV-197: sc reads the session hero's college ranks; the diagnostic PC has no spell book, so it stays unadmitted.
                _session.QueryHero() is null ? null : new SessionDialogCollegePort(_session, AreaConversationState.PcParticipant),
                // ARC-DEV-227: pa reads the name and the mind-controlled flag of the hero's followers from their stored records (the inventory wiring's resolver, a travelling companion's home map included).
                _inventory is null ? null : new SessionDialogFollowerNamePort(_session, AreaConversationState.PcParticipant, FollowerFacts),
                // ARC-DEV-227: nk kills the speaker with no killer; the viewer supplies the kill (none without it: the action stays unadmitted).
                KillNpc is null ? null : new KillPort(this),
                // ARC-UI-088: the c-e window (the speaker's character sheet) opens once the talk ended; the item windows (ii, ri) are not built, so their replies stay
                // blocked (ARC-DEV-229). ARC-DEV-230: np queues a newspaper issue in the session's queue, or sets the speaker's origin.
                _session.QueryHero() is null ? null : new WindowPort(this),
                new NewspaperPort(_session),
                new OriginPort(this)));
        return new(AreaConversationStartStatus.Started, conversation, speaker, $"script {dialog.Script} SCR line {dialog.Line} -> DLG node {dialog.Node}", party,
            document, dialog.Script) { Greeting = greeting };
    }

    /// <summary>ARC-WORLD-058: the module's greeting texts, read through the script content's stack at the first conversation
    /// of this host (a <see cref="Rebind"/> keeps them); null before.</summary>
    public AreaGreetingTexts? GreetingTexts => _greetingTexts;

    private AreaGreetingTexts? _greetingTexts;

    private AreaConversationHost KeepingGreetingTexts(AreaGreetingTexts? texts) { _greetingTexts = texts; return this; }

    // The greeting port of a speaker, or null (a g: node then stays a frontier) when the texts did not load.
    private AreaGreeting? Greeting(AreaSpeaker speaker)
    {
        _greetingTexts ??= AreaGreetingTexts.Load(_content!.Plan);
        if (_greetingTexts.Status != AreaGreetingTextsStatus.Ready) return null;
        // The override dialog is read with the session's current data-fix rules, as the conversation's own DLG.
        var overrides = AreaGreeting.OverrideScript(speaker) is { } script && _content!.Dialog(script, _session.Rules.Data) is { Status: ScriptContentStatus.Read } read
            ? read.Document : null;
        return new AreaGreeting(_session, speaker, State, Profile.Pc, _greetingTexts, overrides);
    }

    /// <summary>ARC-DEV-195: the companion port of a speaker: the party record of its entity in the session; a wait timer is scheduled for a roster speaker (its wait index and the hero's side), none for another.</summary>
    private SessionDialogCompanionPort CompanionPort(AreaSpeaker speaker)
    {
        var npc = AreaConversationState.Npc(speaker.Index);
        var entity = AreaConversationState.EntityId(speaker.Row);
        return new SessionDialogCompanionPort(_session, AreaConversationState.PcParticipant,
            participant => participant == npc ? entity : null,
            participant => participant == npc && FactsOf(speaker) is { } facts ? AreaCompanionRoster.WaitFacts(facts, _session.QueryHeroPartySide()) : null);
    }

    /// <summary>The session party port for a roster speaker when the party is wired; facts are read at each call.</summary>
    private SessionDialogPartyPort? PartyPort(AreaSpeaker speaker)
    {
        if (!_party || FactsOf(speaker) is not { } facts) return null;
        var npc = AreaConversationState.Npc(speaker.Index);
        var entity = AreaConversationState.EntityId(speaker.Row);
        return new SessionDialogPartyPort(_session, AreaConversationState.PcParticipant,
            participant => participant == npc ? entity : null,
            participant => participant == npc ? AreaCompanionRoster.Facts(facts, State.Alignment, State.Reaction(speaker.Index),
                AreaAdvancement.Level(_session, entity, facts.Level), _session.QueryHeroPartySide()) : null);
    }

    // ARC-DEV-081: negative IN goes to the session by the speaker's entity, a carried companion's too: the session finds its seed holder
    // in the maps attached (the resolver's home maps included) and refuses an entity it holds none for (UnknownHolder).
    /// <summary>ARC-DEV-179: where the kind of a new gold pile (the gold prototype with its stored name) is read when the talk starts, or null
    /// to use the prototype alone; the viewer sets it from its barter data, which loads in the background.</summary>
    public Func<SessionItemKind?>? GoldKindSource { get; set; }

    /// <summary>
    /// ARC-DEV-190: the attack event of the hero on the NPC with the given entity (<see cref="AreaAiHost.HeroAttacked"/>: loudness 1, no flags), run by
    /// the viewer's AI host; false when it cannot run (no AI host). Null: the <c>co</c> action stays unadmitted (the conversation stops there).
    /// </summary>
    public Func<string, bool>? HeroAttack { get; set; }

    /// <summary>ARC-DEV-190: whether <see cref="HeroAttack"/> can run now (the viewer: the AI host exists); read when a talk starts. False: the <c>co</c> action
    /// stays unadmitted for that talk, so it is never offered and then refused. Null: always available.</summary>
    public Func<bool>? HeroAttackAvailable { get; set; }

    /// <summary>ARC-DEV-193: whether the NPC with the given entity is alive and kill-on-sight toward the hero now (<see cref="AreaAiHost.HostileToHero"/>);
    /// read by the reply step after an action string (HR1–HR3). Offered with <see cref="HeroAttackAvailable"/> (the AI host exists); without it the
    /// check is not made.</summary>
    public Func<string, bool>? NpcHostile { get; set; }

    internal IDialogInventoryPort? InventoryPort() => _inventory?.DialogPort(_session, SpeakerEntity);

    /// <summary>ARC-TECHNOLOGY-006: the NPC crafter the last SU asked for (its entity), set when the port applied it; the host opens the
    /// schematic window with it after the conversation ended with <see cref="DialogEndKind.SchematicWindow"/>.</summary>
    public string? SchematicCrafter { get; private set; }

    /// <summary>Takes the crafter the last SU asked for, once.</summary>
    public string? TakeSchematicCrafter()
    {
        var crafter = SchematicCrafter;
        SchematicCrafter = null;
        return crafter;
    }

    // SU (ledger NC1): the original's open refuses a dead crafter or viewer; the session knows both lives.
    private sealed class SchematicPort(AreaConversationHost host) : IDialogSchematicPort
    {
        public DialogCommandResult Execute(OpenSchematicsCommand command)
        {
            if (command.PlayerCharacter != AreaConversationState.PcParticipant || host.SpeakerEntity(command.Npc) is not { } entity) return DialogCommandResult.Rejected;
            if (host._session.QueryNpcDead(entity) || host._session.QueryHeroLife() is { Status.IsDead: true }) return DialogCommandResult.Rejected;
            host.SchematicCrafter = entity;
            return DialogCommandResult.Applied;
        }
    }

    // ia (ledger DCC7, GA2): the area of the PC's map, read from the shown map's use host at each call.
    private sealed class MapAreaPort(AreaConversationHost host) : IDialogMapAreaPort
    {
        public int? Area(DialogParticipantId playerCharacter) => playerCharacter == AreaConversationState.PcParticipant ? host.Scripts.PcMapArea() : null;
    }

    // The reply step's hostile check (ledger HR1–HR3): the speaker is alive and kill-on-sight toward the hero, asked of the shown map's AI host.
    private sealed class HostilePort(AreaConversationHost host) : IDialogHostilePort
    {
        public bool Hostile(DialogHostileQuery query) =>
            query.PlayerCharacter == AreaConversationState.PcParticipant && host.SpeakerEntity(query.Npc) is { } entity && host.NpcHostile is { } hostile && hostile(entity);
    }

    // nk (ledger PN4): the speaker is killed with no killer (0x45D900), as a script's kill action does; the talk goes on.
    private sealed class KillPort(AreaConversationHost host) : IDialogKillPort
    {
        public DialogCommandResult Execute(KillNpcCommand command)
        {
            if (command.PlayerCharacter != AreaConversationState.PcParticipant || host.SpeakerEntity(command.Npc) is not { } entity || host.KillNpc is not { } kill)
                return DialogCommandResult.Rejected;
            return kill(entity) ? DialogCommandResult.Applied : DialogCommandResult.Rejected;
        }
    }

    // np (ledger NQ1–NQ3): the session's newspaper queue takes the issue by the original's rule; an issue already queued is the rule's own no-change outcome.
    private sealed class NewspaperPort(GameSessionOwner session) : IDialogNewspaperPort
    {
        public DialogCommandResult Execute(QueueNewspaperCommand command) =>
            session.QueueNewspaper(session.Version, command.Issue, command.Front).Disposition == GameSessionDisposition.Rejected ? DialogCommandResult.Rejected : DialogCommandResult.Applied;
    }

    // or (ledger NQ6): the speaker's origin is the session's set origin (npc.origin on the speaker's map), cleared when it equals the map data's; the live talk's reputation scope follows.
    private sealed class OriginPort(AreaConversationHost host) : IDialogOriginPort
    {
        [OriginalFunction(0x45D2D0, OriginalFunctionScope.Full)]
        public DialogCommandResult Execute(SetOriginCommand command)
        {
            var at = command.Npc.Value - 1000;
            if (command.PlayerCharacter != AreaConversationState.PcParticipant || at < 0 || at >= host._speakers.Count) return DialogCommandResult.Rejected;
            var index = (int)at;
            var row = host._speakers[index].Row;
            // The original sets the field only on an NPC (0x45D2D0 tests the object type) and does nothing for another object.
            if (row.Type != 16) return DialogCommandResult.Applied;
            var loaded = Scope(row).Origin;
            var result = host._session.ApplyChanges(host._session.Version, null,
                [new(new SessionMapId(row.Key.RuntimeMap), SessionNpcOrigin.Key(AreaConversationState.EntityId(row)), command.Origin == loaded ? null : command.Origin)]);
            if (result.Disposition == GameSessionDisposition.Rejected) return DialogCommandResult.Rejected;
            host.State.SetOrigin(index, command.Origin);
            return DialogCommandResult.Applied;
        }
    }

    /// <summary>ARC-DEV-227: the viewer's kill of the NPC with the given entity on the shown map (a death with no killer, <c>AreaScriptWorld.Kill</c>'s); false when it cannot be killed.
    /// Null: the <c>nk</c> action stays unadmitted.</summary>
    public Func<string, bool>? KillNpc { get; set; }

    private (int Name, bool MindControlled)? FollowerFacts(string entity) => _inventory?.FollowerNameFacts(_session, entity);

    // co (ledger SU13, CA2): the attack event of the hero on the speaker; the conversation asks once, after the action string.
    private sealed class CombatPort(AreaConversationHost host) : IDialogCombatPort
    {
        public DialogCommandResult Execute(StartCombatCommand command)
        {
            if (command.PlayerCharacter != AreaConversationState.PcParticipant || host.SpeakerEntity(command.Npc) is not { } entity || host.HeroAttack is not { } attack)
                return DialogCommandResult.Rejected;
            return attack(entity) ? DialogCommandResult.Applied : DialogCommandResult.Rejected;
        }
    }

    /// <summary>ARC-UI-050: the merchant the last b: reply asked for (its entity and speaker index), set when the port applied it; the
    /// host opens the barter window with it after the conversation ended with <see cref="DialogEndKind.BarterWindow"/>.</summary>
    public (string Entity, int Speaker)? BarterMerchant { get; private set; }

    /// <summary>Takes the merchant the last b: reply asked for, once.</summary>
    public (string Entity, int Speaker)? TakeBarterMerchant()
    {
        var merchant = BarterMerchant;
        BarterMerchant = null;
        return merchant;
    }

    // b: (ledger BT7): the original's open refuses only a dead or missing PC (0x572370); the partner is not checked.
    private sealed class BarterPort(AreaConversationHost host) : IDialogBarterPort
    {
        public DialogCommandResult Execute(OpenBarterCommand command)
        {
            if (command.PlayerCharacter != AreaConversationState.PcParticipant || host.SpeakerEntity(command.Npc) is not { } entity) return DialogCommandResult.Rejected;
            if (host._session.QueryHero() is null || host._session.QueryHeroLife() is { Status.IsDead: true }) return DialogCommandResult.Rejected;
            host.BarterMerchant = (entity, (int)(command.Npc.Value - 1000));
            return DialogCommandResult.Applied;
        }
    }

    /// <summary>ARC-UI-088: the speaker whose character sheet the last c-e reply asked for (its entity and speaker index), set when the port applied
    /// it; the host opens the NPC sheet with it after the conversation ended with <see cref="DialogEndKind.NpcSheetWindow"/>.</summary>
    public (string Entity, int Speaker)? NpcSheetSpeaker { get; private set; }

    /// <summary>Takes the speaker the last c-e reply asked for, once.</summary>
    public (string Entity, int Speaker)? TakeNpcSheetSpeaker()
    {
        var speaker = NpcSheetSpeaker;
        NpcSheetSpeaker = null;
        return speaker;
    }

    // c-e (ledger WN1, WN2): the sheet of the dialog's NPC opens for the local PC (mode 2); only the NPC sheet is a window the viewer builds.
    private sealed class WindowPort(AreaConversationHost host) : IDialogWindowPort
    {
        public bool Offers(DialogWindowKind kind) => kind == DialogWindowKind.NpcSheet;

        public DialogCommandResult Execute(OpenWindowCommand command)
        {
            if (command.Window != DialogWindowKind.NpcSheet || command.PlayerCharacter != AreaConversationState.PcParticipant
                || host.SpeakerEntity(command.Npc) is not { } entity) return DialogCommandResult.Rejected;
            host.NpcSheetSpeaker = (entity, (int)(command.Npc.Value - 1000));
            return DialogCommandResult.Applied;
        }
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
/// 0 and no effects; both carry the session PC's reputation sum for the speaker's origin and faction (ARC-WORLD-044,
/// <see cref="PcFactsFor"/>). Since ARC-WORLD-043 the host opens the speaker's reaction conversation slot for each
/// conversation and closes it at its end, so changes during a talk land in that slot and ME reads the session's "met
/// before". Still PROJECT diagnostic, in memory only and
/// lost with this object: PC alignment, items given (IN) when the host has no session inventory (ARC-UI-004 wires it),
/// and areas marked (MM); they wait for their own owners.
/// </summary>
public sealed class AreaConversationState : IDialogStateQueryPort, IDialogCommandPort, IDialogWorldPort, IDialogRumorPort, IDialogMetBeforePort, IDialogCounterPort
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
    private readonly List<string> _entities;
    // ARC-DEV-081: the dialog state map of a carried companion's speaker (its home map); a speaker not here has the shown map's.
    private readonly Dictionary<int, SessionMapId> _stateMaps = [];
    private readonly Dictionary<int, uint> _baseline = [];
    private readonly Dictionary<int, uint> _baselineCounters = [];
    private readonly HashSet<int> _areas = [];
    private readonly List<int> _items = [];
    private readonly Guid _stateEpoch = Guid.NewGuid();
    private readonly Guid _attachmentEpoch = Guid.NewGuid();
    private readonly int _initialReaction;
    private readonly HashSet<int> _party;
    private readonly List<SessionExperienceAward> _awards = [];
    private readonly List<(int Origin, int Faction)> _scopes;

    /// <summary>ARC-WORLD-036: the experience awards the talk's commands gave (a completed quest) since the last call, in order;
    /// the host passes their follower steps on (<see cref="AreaAdvancement.LevelFollowers"/>).</summary>
    /// <summary>ARC-SCRIPT-019: an award of a dialog script's quest action (<see cref="AreaScriptHost"/>), queued with the commands' own.</summary>
    internal void AddAward(SessionExperienceAward award) => _awards.Add(award ?? throw new ArgumentNullException(nameof(award)));

    public IReadOnlyList<SessionExperienceAward> TakeAwards()
    {
        var awards = _awards.ToArray();
        _awards.Clear();
        return awards;
    }

    /// <param name="speakerEntities">Stable session entity id of each speaker index (see <see cref="EntityId"/>).</param>
    /// <param name="partySpeakers">ARC-WORLD-016: speaker indexes whose "following" is the session party standing
    /// (<see cref="SessionCompanion.LedByPc"/>, true while waiting too) instead of the diagnostic follow key.</param>
    /// <param name="scopes">ARC-WORLD-044: each speaker's origin and faction (its NPC fields), which select the reputation
    /// effects of its reaction; 0 and 0 when not given.</param>
    public AreaConversationState(GameSessionOwner session, SessionMapId map, IReadOnlyList<string> speakerEntities, int initialReaction,
        IReadOnlySet<int>? partySpeakers = null, IReadOnlyList<(int Origin, int Faction)>? scopes = null)
    {
        _scopes = scopes?.ToList() ?? [];
        if (scopes is not null && _scopes.Count != speakerEntities.Count) throw new ArgumentException("One scope per speaker.", nameof(scopes));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        ArgumentNullException.ThrowIfNull(speakerEntities);
        _map = map; _entities = [.. speakerEntities]; _initialReaction = initialReaction;
        _party = partySpeakers?.ToHashSet() ?? [];
        if (!ReactionRules.IsValid(NpcFacts())) throw new ArgumentOutOfRangeException(nameof(initialReaction));
        // A restored session may carry any long under these keys; refuse values this port cannot read.
        foreach (var entity in _entities) ValidateStored(session, map, entity);
    }

    private static void ValidateStored(GameSessionOwner session, SessionMapId map, string entity)
    {
        var lf = session.QueryObjectChange(map, MapChangeKey.ForEntity(entity, LocalFlagsProperty));
        var follow = session.QueryMapChange(map, MapChangeKey.ForEntity(entity, FollowProperty));
        var counters = session.QueryObjectChange(map, MapChangeKey.ForEntity(entity, AreaScriptHost.CountersProperty));
        var script = session.QueryObjectChange(map, MapChangeKey.ForEntity(entity, AreaScriptHost.ScriptProperty));
        if (lf is < 0 or > uint.MaxValue || follow is not (null or 1)
            || counters is < 0 or > uint.MaxValue || script is < int.MinValue or > int.MaxValue)
            throw new ArgumentException("AREA_SESSION_DIALOG_STATE_INVALID", nameof(session));
    }

    /// <summary>
    /// ARC-DEV-081 (MIG5): adds a speaker after the ones listed, <paramref name="index"/> being the next free one, whose dialog state lives
    /// under <paramref name="entity"/> on <paramref name="stateMap"/> (a carried companion's home map) and whose "following" is the
    /// session party standing. Throws <see cref="ArgumentException"/>, adding nothing, when the session holds values under that
    /// entity this port cannot read.
    /// </summary>
    internal void Admit(int index, string entity, (int Origin, int Faction) scope, SessionMapId stateMap)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (index != _entities.Count) throw new ArgumentOutOfRangeException(nameof(index));
        ValidateStored(_session, stateMap, entity);
        while (_scopes.Count < _entities.Count) _scopes.Add((0, 0));
        _entities.Add(entity); _scopes.Add(scope); _stateMaps[index] = stateMap; _party.Add(index);
    }

    /// <summary>ARC-DEV-230: the speaker's origin after the dialog action <c>or</c>; the reputation effects of its reaction follow (a speaker index this port does not list is ignored).</summary>
    internal void SetOrigin(int speaker, int origin)
    {
        if (speaker < 0 || speaker >= _entities.Count) return;
        while (_scopes.Count < _entities.Count) _scopes.Add((0, 0));
        _scopes[speaker] = (origin, _scopes[speaker].Faction);
    }

    /// <summary>The map whose entity keys hold the speaker's dialog state: the shown map's, or a carried companion's home map.</summary>
    public SessionMapId MapOf(int speaker) => _stateMaps.TryGetValue(speaker, out var map) ? map : _map;

    /// <summary><see cref="MapOf(int)"/> of the speaker with this entity id; the shown map's for an entity this port does not list.</summary>
    internal bool IsCarried(string entity) => _entities.IndexOf(entity) is var index and >= 0 && _stateMaps.ContainsKey(index);

    internal SessionMapId MapOfEntity(string entity) => _entities.IndexOf(entity) is var index and >= 0 ? MapOf(index) : _map;

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
    // The diagnostic PC's alignment without a session hero (starts at 0, not session state).
    private int _diagnosticAlignment;

    /// <summary>PC alignment: the session hero's stored alignment (ARC-WORLD-067), else the diagnostic PC's, starting at 0.</summary>
    public int Alignment => _session.QueryHero()?.Alignment ?? _diagnosticAlignment;
    public bool Follows(int speaker) => _party.Contains(speaker)
        ? _session.QueryCompanion(_entities[speaker]).LedByPc : Read(speaker, FollowProperty) == 1;
    [OriginalFunction(0x444F10, OriginalFunctionScope.Full)]
    public bool LocalFlag(int speaker, int bit) => (LocalFlags(speaker) & (1u << bit)) != 0;
    public int Reaction(int speaker) =>
        _session.QueryReaction(_entities[speaker], NpcFacts(), PcFactsFor(speaker)).View?.Effective ?? throw new InvalidOperationException("AREA_SESSION_REACTION_INVALID");

    /// <summary>The diagnostic NPC side of every speaker's reaction.</summary>
    public ReactionNpcFacts NpcFacts() => new(_initialReaction, CharacterRace.Human, false, false);

    /// <summary>The PC side: the session hero's facts, else <see cref="DiagnosticPc"/>.</summary>
    public ReactionPcFacts PcFacts => PcFactsFor(-1);

    /// <summary>ARC-WORLD-044: the PC side toward a speaker: the hero's facts, else <see cref="DiagnosticPc"/>, with the session's
    /// reputation sum for the speaker's origin and faction (0 and 0 for an unknown speaker index).</summary>
    public ReactionPcFacts PcFactsFor(int speaker)
    {
        var (origin, faction) = speaker >= 0 && speaker < _scopes.Count ? _scopes[speaker] : (0, 0);
        return _session.QueryHeroReactionFacts(origin, faction)
            ?? DiagnosticPc with { ReputationAdjustment = (int)Math.Clamp(_session.QueryReputationReaction(origin, faction), int.MinValue, int.MaxValue) };
    }

    /// <summary>Records the loaded effective SAP_DIALOG attachment flags as the speaker's baseline.</summary>
    internal void Bind(int speaker, uint loadedFlags, uint loadedCounters = 0)
    {
        _baseline[speaker] = loadedFlags;
        _baselineCounters[speaker] = loadedCounters;
    }

    private uint Counters(int speaker) => Read(speaker, AreaScriptHost.CountersProperty) is { } value ? checked((uint)value) : _baselineCounters.GetValueOrDefault(speaker);

    /// <summary>ARC-DEV-196 (KC1): the byte counter <paramref name="query"/> names in the speaker's dialog attachment, the session's value or the loaded one.</summary>
    [OriginalFunction(0x444FD0, OriginalFunctionScope.Full)]
    public int Read(DialogCounterQuery query) => (int)((Counters(Speaker(query.Npc)) >> (8 * query.Index)) & 0xFF);

    /// <summary>ARC-DEV-196 (KC2): stores the low eight bits of the value in the counter; a value equal to the loaded one clears the session entry, as the flags do.</summary>
    [OriginalFunction(0x445020, OriginalFunctionScope.Full)]
    public DialogCommandResult Execute(CounterWriteCommand command)
    {
        var speaker = Speaker(command.Npc);
        var shift = 8 * command.Index;
        var counters = (Counters(speaker) & ~(0xFFu << shift)) | ((uint)(command.Value & 0xFF) << shift);
        return Result(Write(speaker, AreaScriptHost.CountersProperty, counters, _baselineCounters.GetValueOrDefault(speaker)));
    }

    private uint LocalFlags(int speaker) => Read(speaker, LocalFlagsProperty) is { } value ? checked((uint)value) : _baseline.GetValueOrDefault(speaker);

    private int Speaker(DialogParticipantId npc)
    {
        var speaker = checked((int)(npc.Value - 1000));
        if (speaker < 0 || speaker >= _entities.Count) throw new ArgumentOutOfRangeException(nameof(npc));
        return speaker;
    }

    private MapChangeKey Key(int speaker, string property) => MapChangeKey.ForEntity(_entities[speaker], property);
    // ARC-DEV-096: the dialog slot is an attachment of the object (found by its identity), the follow mark a field of the map's.
    private long? Read(int speaker, string property) => property == FollowProperty ? _session.QueryMapChange(MapOf(speaker), Key(speaker, property))
        : _session.QueryObjectChange(MapOf(speaker), Key(speaker, property));

    // A value equal to the loaded baseline clears the key, so an untouched NPC has no session entry.
    // ARC-DEV-096: a clear of the dialog slot finds the object's record wherever it was written (a use host's call-ex may have moved it to its map).
    private GameSessionMutationResult Write(int speaker, string property, long value, long baseline) => value == baseline
        ? _session.ClearMapChange(_session.Version, property == FollowProperty ? MapOf(speaker) : _session.QueryObjectRecordMap(Key(speaker, property)) ?? MapOf(speaker), Key(speaker, property))
        : _session.SetMapChange(_session.Version, MapOf(speaker), Key(speaker, property), value);

    private static DialogCommandResult Result(GameSessionMutationResult result) =>
        result.Disposition == GameSessionDisposition.Rejected ? DialogCommandResult.Rejected : DialogCommandResult.Applied;

    public LocalFlagReadResult Read(LocalFlagQuery query) =>
        LocalFlagReadResult.Found(LocalFlag(Speaker(query.Npc), query.Index) ? 1 : 0);

    [OriginalFunction(0x444F60, OriginalFunctionScope.Full)]
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
                _session.SetReaction(_session.Version, entity, NpcFacts(), PcFactsFor(speaker), AcceptedQuestReaction),
            SessionQuestState.Completed => _session.AdjustReaction(_session.Version, entity, NpcFacts(), PcFactsFor(speaker), CompletedQuestReactionBonus),
            _ => null,
        };
        // The quest change is already committed: a refused reaction write is a partial effect, reported as indeterminate.
        if (written?.Disposition == GameSessionDisposition.Rejected) throw new InvalidOperationException("AREA_SESSION_QUEST_REACTION_REJECTED");
        return DialogCommandResult.Applied;
    }

    public DialogCommandResult Execute(RumorLogCommand command) => Result(_session.LearnRumor(_session.Version, command.RumorId));

    public DialogCommandResult Execute(QuellRumorCommand command) => Result(_session.QuellRumor(_session.Version, command.RumorId));

    /// <summary>ARC-DEV-171: the RU condition reads the session's rumors (the PC's known ones; the session has one PC, so the query's participant is not needed); the RQ condition
    /// its quelled set.</summary>
    public bool IsKnown(DialogRumorQuery query) => _session.QueryRumor(query.Rumor) is not null;

    public bool IsQuelled(int rumor) => _session.QueryRumorQuelled(rumor);

    public DialogReactionReadResult ReadReaction(DialogReactionQuery query) => DialogReactionReadResult.Of(Reaction(Speaker(query.Npc)));

    /// <summary>ARC-WORLD-043: ME reads whether the speaker remembers the PC (its remembered slot; the open conversation does
    /// not count).</summary>
    public bool MetBefore(DialogReactionQuery query) => MetBefore(Speaker(query.Npc));

    /// <summary>Whether the speaker has met the PC before (the session's remembered reaction slot).</summary>
    public bool MetBefore(int speaker) =>
        _session.QueryReaction(_entities[speaker], NpcFacts(), PcFactsFor(speaker)).View?.MetBefore ?? throw new InvalidOperationException("AREA_SESSION_REACTION_INVALID");

    /// <summary>ARC-WORLD-043: opens the speaker's reaction conversation slot (no "force good reaction" fate point: fate points
    /// are not session state).</summary>
    internal SessionReactionResult BeginReaction(int speaker) =>
        _session.BeginReactionConversation(_session.Version, _entities[speaker], NpcFacts());

    /// <summary>ARC-WORLD-043: closes the speaker's reaction conversation slot; the value moves to the remembered slot.</summary>
    internal SessionReactionResult EndReaction(int speaker) =>
        _session.EndReactionConversation(_session.Version, _entities[speaker], NpcFacts());

    public DialogCommandResult Execute(ReactionCommand command)
    {
        var entity = _entities[Speaker(command.Npc)];
        var result = command.Mode == DialogReactionMode.Target
            ? _session.SetReaction(_session.Version, entity, NpcFacts(), PcFactsFor(Speaker(command.Npc)), command.Value)
            : _session.AdjustReaction(_session.Version, entity, NpcFacts(), PcFactsFor(Speaker(command.Npc)), command.Value);
        return result.Disposition == GameSessionDisposition.Rejected ? DialogCommandResult.Rejected : DialogCommandResult.Applied;
    }

    public DialogCommandResult Execute(GiveItemCommand command) { _items.Add(command.ItemName); return DialogCommandResult.Applied; }
    /// <summary>MM (ledger WT2): an area of the session's range becomes known to the PC in the session (<see cref="GameSessionOwner.DiscoverArea"/>: the world map shows it,
    /// the <c>ar</c> condition reads it, ARC-DEV-191); a known area is a successful no-change. A number outside 1..4095 (area 0 included), which the session cannot hold,
    /// is only recorded here as before.</summary>
    public DialogCommandResult Execute(MarkAreaCommand command)
    {
        _areas.Add(command.Area);
        if (!SessionStoryLimits.IsArea(command.Area)) return DialogCommandResult.Applied;
        return _session.DiscoverArea(_session.Version, command.Area).Disposition == GameSessionDisposition.Rejected ? DialogCommandResult.Rejected : DialogCommandResult.Applied;
    }

    /// <summary>AL: with a session hero its stored alignment changes (clamped to −1000..1000) and the followers are armed in one
    /// revision (ARC-WORLD-067, <see cref="GameSessionOwner.ChangeHeroAlignment"/>); without one the diagnostic PC's alignment changes
    /// after the followers are armed (ARC-WORLD-045, OL17). A refusal leaves the alignment as it was.</summary>
    public DialogCommandResult Execute(AlignmentCommand command)
    {
        long next = command.Mode == DialogReactionMode.Target ? command.Value : (long)Alignment + command.Value;
        if (_session.QueryHero() is not null)
            return _session.ChangeHeroAlignment(_session.Version, next).Disposition == GameSessionDisposition.Rejected
                ? DialogCommandResult.Rejected : DialogCommandResult.Applied;
        if (next is < int.MinValue or > int.MaxValue) return DialogCommandResult.Rejected;
        var value = (int)next;
        if (_session.RecordPcAlignmentChange(_session.Version, Alignment, value).Disposition == GameSessionDisposition.Rejected) return DialogCommandResult.Rejected;
        _diagnosticAlignment = value;
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
            DialogOptionTextKind.Generated when option.Text.Length > 0 => option.Text,
            DialogOptionTextKind.Generated => $"[generated '{option.Marker!.Value.Letter}:' phrase; generic text not loaded]",
            _ when option.Text.Length > 0 => option.Text,
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
        DialogConversationPhase.Ended when conversation.End!.Kind == DialogEndKind.Insulted => "[the insult ended the dialog: the NPC's generated reply is not loaded]",
        DialogConversationPhase.Ended when conversation.End!.Kind == DialogEndKind.HostileRefusal => "[the NPC turned hostile and ended the dialog: its generated reply is not loaded]",
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
            ReactionCommand reaction => reaction.Mode == DialogReactionMode.Target ? $"RE target {reaction.Value}"
                : $"RE adjust {reaction.Value:+0;-0;+0}{(receipt.Source.FieldKind == DlgFieldKind.Text ? " (insult)" : string.Empty)}",
            GiveItemCommand item => $"IN item name {item.ItemName} NPC -> PC",
            TakeItemCommand take => $"IN item name {take.ItemName} PC -> NPC",
            CounterWriteCommand counter => $"LC {counter.Index} = {counter.Value}",
            KillNpcCommand => "NK NPC killed",
            CompanionCommand companion => companion.Action switch { DialogCompanionAction.Wait => "WA NPC waits", DialogCompanionAction.Leave => "LV NPC leaves the party", DialogCompanionAction.SpreadOut => "SO spread out", _ => "SC spread out cleared" },
            MarkAreaCommand area => $"MM area {area.Area} known",
            AlignmentCommand alignment => alignment.Mode == DialogReactionMode.Target ? $"AL set {alignment.Value}" : $"AL adjust {alignment.Value:+0;-0;+0}",
            SkillTrainingCommand training => $"TR skill {training.Skill} -> tier {training.Training}",
            FatePointCommand => "FP fate point +1",
            JoinCommand => "JO follow request",
            ReputationCommand reputation => $"RP {(reputation.Add ? "add" : "remove")} {reputation.Reputation}",
            ScriptStoreCommand store => $"{store.Store switch { DialogScriptStore.GlobalVariable => "GV", DialogScriptStore.PcVariable => "PV", _ => "PF" }} {store.Index} = {store.Value}",
            _ => receipt.Command.GetType().Name,
        };
        return $"#{receipt.Ordinal} turn {receipt.Turn}: {command} [{receipt.Outcome}]";
    }

    private static string Where(DialogOperationSource? source) => source is { } s ? $", record {s.RecordId.Value} {s.FieldKind} op {s.OperationOrdinal}" : string.Empty;
}
