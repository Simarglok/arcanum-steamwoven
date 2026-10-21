using Arcanum.Data;
using Arcanum.Data.Area;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Skills;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// The SCR dispatcher's host for the AREA viewer's dialog NPCs (ARC-SCRIPT-009): it reads a speaker's dialog slot
/// (the area's effective attachment, overridden by the session's map changes), the game's script content (ARC-SCRIPT-011:
/// the universal content stack, as for every map), the session's global flags and the follower standing, and it applies a run's effects to the session. Only the dialog
/// point of the map's speakers is modelled; everything the viewer does not model answers "no" (no generator, no trap,
/// alive and free, not jilted).
/// </summary>
public sealed class AreaScriptHost : IScrHost, IScrSkillQueries
{
    /// <summary>The PC as the scripts see it; the viewer has one.</summary>
    public const string PcCode = SessionEventKinds.PlayerEntity;
    /// <summary>Map-change keys of a speaker's dialog slot, besides <see cref="AreaConversationState.LocalFlagsProperty"/>.</summary>
    public const string CountersProperty = "dialog.counters", ScriptProperty = "dialog.script";

    /// <summary>
    /// The set the viewer runs: <see cref="ScrAdmissionSet.S3"/> without get and set story state, because the session
    /// has no story-state number yet (a script that would use one is refused, not run on a made-up value), plus the three
    /// sound actions of <see cref="ScrAdmissionSet.S7"/> (ARC-SCRIPT-040), plus call script in and remove script call of
    /// <see cref="ScrAdmissionSet.S5"/> since the viewer pumps the session's event queue (ARC-WORLD-039), plus set auto-level
    /// scheme of <see cref="ScrAdmissionSet.S8"/> (ARC-SCRIPT-016), plus start schematic UI of <see cref="ScrAdmissionSet.S9"/>
    /// (ARC-TECHNOLOGY-005), plus the reading actions of <see cref="ScrAdmissionSet.S10"/> (ARC-UI-049).
    /// </summary>
    public static ScrAdmissionSet Admission { get; } = new("S3-area-sound-calls-scheme-schematic-reading", ScrAdmissionSet.S3.Conditions,
        ScrAdmissionSet.S3.Actions.Where(a => a is not ScrCatalogue.Action.GetStoryState and not ScrCatalogue.Action.SetStoryState)
            .Concat([ScrCatalogue.Action.PlaySound, ScrCatalogue.Action.PlaySoundOn, ScrCatalogue.Action.PlaySoundScheme,
                ScrCatalogue.Action.CallScriptIn, ScrCatalogue.Action.RemoveScriptCall, ScrCatalogue.Action.SetAutoLevelScheme,
                ScrCatalogue.Action.StartSchematicUi, ScrCatalogue.Action.ShowBook, ScrCatalogue.Action.ShowPicture, ScrCatalogue.Action.ShowPlaque]),
        ScrAdmissionSet.S3.ValueKinds, ScrAdmissionSet.S3.FocusKinds);

    /// <summary>ARC-SKILLS-009: <see cref="Admission"/> plus get skill (47) of <see cref="ScrAdmissionSet.S11"/>, the set a run uses while
    /// the session has a hero (the PC's levels are the hero's; without one a script that reads a skill is refused, not run on a made-up
    /// level). Use item with skill (79) stays out: no live script of the installed data uses it.</summary>
    public static ScrAdmissionSet SkillAdmission { get; } = new("S3-area-sound-calls-scheme-schematic-reading-skill", Admission.Conditions,
        Admission.Actions.Append(ScrCatalogue.Action.GetSkill), Admission.ValueKinds, Admission.FocusKinds);

    /// <summary>The set a run of this host uses: <see cref="SkillAdmission"/> with a session hero, else <see cref="Admission"/>.</summary>
    public ScrAdmissionSet RunAdmission => _session.QueryHeroSkills() is null ? Admission : SkillAdmission;

    private readonly List<ScrSound> _sounds = [];
    private readonly List<AreaSchematicScriptOpen> _schematics = [];
    private readonly List<ScrReadingRequest> _readings = [];

    /// <summary>ARC-SCRIPT-040: the effect sounds of the runs applied since the last call, in order, for the viewer to play
    /// (scheme requests go to the session in <see cref="Apply"/>).</summary>
    public IReadOnlyList<ScrSound> TakeSounds()
    {
        var sounds = _sounds.ToArray();
        _sounds.Clear();
        return sounds;
    }

    /// <summary>ARC-TECHNOLOGY-005: the schematic window requests of the runs applied since the last call, in order, each already
    /// resolved (<see cref="ResolveSchematic"/>), for the viewer to open the hero's window or report the refusal.</summary>
    public IReadOnlyList<AreaSchematicScriptOpen> TakeSchematics()
    {
        var schematics = _schematics.ToArray();
        _schematics.Clear();
        return schematics;
    }

    /// <summary>ARC-UI-049: the reading actions of the runs applied since the last call whose viewer is the PC, in order (the original
    /// opens the window for the local player only), for the viewer to show.</summary>
    public IReadOnlyList<ScrReadingRequest> TakeReadings()
    {
        var readings = _readings.ToArray();
        _readings.Clear();
        return readings;
    }

    /// <summary>ARC-SCRIPT-040: the speaker the scripts name, or null for the PC and anything else (the viewer knows where it
    /// stands now).</summary>
    public AreaSpeaker? SpeakerOf(ScrObjectRef critter) => Speaker(critter);

    private readonly GameSessionOwner _session;
    private readonly SessionMapId _map;
    private readonly IReadOnlyList<AreaSpeaker> _speakers;
    private readonly AreaConversationState _state;
    private readonly ScriptContent? _content;
    private readonly Func<string, SessionNpcAdvancement?>? _npcAdvancement;
    private readonly Func<string, (SessionNpcAdvancement Loaded, AdvancementModifiers Modifiers)?>? _npcSheet;

    /// <param name="npcSheet">ARC-SKILLS-009: an NPC's sheet and effects as its map data holds them, by entity id, for get skill; null for
    /// an object that is not such an NPC (its level is 0).</param>
    /// <param name="content">The game's scripts; null when they could not be loaded, and then every script is missing.
    /// The host borrows it.</param>
    /// <param name="npcAdvancement">ARC-SCRIPT-016: an NPC's advancement as its map data holds it (sheet and scheme), by entity id, or
    /// null for an object that is not such an NPC; a scheme request for one is ignored, as the original ignores an object that is
    /// neither a PC nor an NPC. Without it only the PC's scheme changes.</param>
    public AreaScriptHost(GameSessionOwner session, SessionMapId map, IReadOnlyList<AreaSpeaker> speakers, AreaConversationState state, ScriptContent? content,
        Func<string, SessionNpcAdvancement?>? npcAdvancement = null, Func<string, (SessionNpcAdvancement Loaded, AdvancementModifiers Modifiers)?>? npcSheet = null)
    {
        _session = session; _map = map; _speakers = speakers; _state = state; _content = content; _npcAdvancement = npcAdvancement; _npcSheet = npcSheet;
        Pc = new ScrObjectRef(map.RuntimeMap, PcCode);
    }

    public ScrObjectRef Pc { get; }

    public ScrObjectRef Object(AreaSpeaker speaker) => new(_map.RuntimeMap, AreaConversationState.EntityId(speaker.Row));

    /// <summary>The slot the area loaded for a speaker: flags, counters and script of its effective dialog element, or
    /// null when the row has none (a well-formed element has three words).</summary>
    public static ScrSlot? LoadedSlot(AreaSpeaker speaker)
    {
        var element = speaker.Row.EffectiveFields.SingleOrDefault(field => field.Slot == 31)?.Elements.SingleOrDefault(e => e.Key == ScrPoint.Dialog);
        if (element is not { Words.Count: 3 } || element.Words[2] is < 0 or > ScrCatalogue.MaxScriptNumber || element.Words[0] is < 0 or > uint.MaxValue || element.Words[1] is < 0 or > uint.MaxValue)
            return null;
        return new((int)element.Words[2], (uint)element.Words[0], (uint)element.Words[1]);
    }

    private AreaSpeaker? Speaker(ScrObjectRef critter) =>
        critter.Map == _map.RuntimeMap ? _speakers.FirstOrDefault(s => AreaConversationState.EntityId(s.Row) == critter.Code) : null;

    /// <summary>ARC-WORLD-039: a deferred call's role as this host's object. The session moves a call's player and follower
    /// roles with the PC to another map, so the PC and this host's speakers are taken on this host's map by their ids; any
    /// other object keeps its map.</summary>
    public ScrObjectRef? Local(SessionScriptRole? role) => role is not { } r ? null
        : r.Entity == PcCode || _speakers.Any(s => AreaConversationState.EntityId(s.Row) == r.Entity) ? new ScrObjectRef(_map.RuntimeMap, r.Entity)
        : new ScrObjectRef(r.Map.RuntimeMap, r.Entity);

    public ScrSlot? Slot(ScrObjectRef attachee, int point)
    {
        if (point != ScrPoint.Dialog || Speaker(attachee) is not { } speaker || LoadedSlot(speaker) is not { } loaded) return null;
        long? Read(string property) => _session.QueryMapChange(_map, MapChangeKey.ForEntity(attachee.Code, property));
        return new(Read(ScriptProperty) is { } script ? checked((int)script) : loaded.Script,
            Read(AreaConversationState.LocalFlagsProperty) is { } flags ? checked((uint)flags) : loaded.LocalFlags,
            Read(CountersProperty) is { } counters ? checked((uint)counters) : loaded.Counters);
    }

    /// <summary>The script the original names for the number (<see cref="ScriptNameIndex"/>), read from the stack's
    /// winning copy. A copy that cannot be framed, or that changed since this content first read it, is unreadable.</summary>
    public ScrScriptLookup Script(int number) => Lookup(_content, number);

    /// <summary>The lookup of <see cref="Script"/> over any content (shared with the item script host, ARC-SCRIPT-013).</summary>
    public static ScrScriptLookup Lookup(ScriptContent? content, int number)
    {
        if (content is null || number <= 0 || number > ScrCatalogue.MaxScriptNumber) return ScrScriptLookup.Missing;
        var read = content.Script(number);
        return read.Status switch
        {
            ScriptContentStatus.Read => ScrScriptLookup.Of(read.Program!),
            ScriptContentStatus.NoName or ScriptContentStatus.NotFound => ScrScriptLookup.Missing,
            _ => ScrScriptLookup.Unreadable,
        };
    }

    /// <summary>
    /// The DLG a dialog action names: the script's name with the <c>dlg</c> folder and extension (as the original
    /// derives it), read as the original reads it (ARC-SCRIPT-012) with the data fixes of <paramref name="rules"/>
    /// (ARC-DATA-022; the caller passes the session's current rules), whose <paramref name="node"/> is an NPC record. <see cref="AreaConversationStartStatus.Started"/>
    /// with the document, or the typed reason.
    /// </summary>
    public static (AreaConversationStartStatus Status, DlgDocument? Document, string Detail) LoadDialog(ScriptContent content, int script, int node,
        Arcanum.Data.Fixes.DataFixProfile rules)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(rules);
        var read = content.Dialog(script, rules);
        switch (read.Status)
        {
            case ScriptContentStatus.Read: break;
            case ScriptContentStatus.Changed: return (AreaConversationStartStatus.SourceChanged, null, read.Detail);
            case ScriptContentStatus.Invalid or ScriptContentStatus.Unreadable: return (AreaConversationStartStatus.DialogUnreadable, null, read.Detail);
            // Missing, and (ARC-SCRIPT-012) Empty: the game does not open a DLG without records either.
            default: return (AreaConversationStartStatus.DialogResourceMismatch, null, $"dialog {script}: {read.Detail}");
        }
        if (!read.Document!.TryGetRecord(node, out var record) || record!.Role != DlgRecordRole.Npc)
            return (AreaConversationStartStatus.DialogResourceMismatch, null, $"dialog {script} has no NPC node {node}");
        return (AreaConversationStartStatus.Started, read.Document, string.Empty);
    }

    public bool GlobalFlag(int index)
    {
        var flags = _session.Flags;
        var read = flags.Read(new GlobalFlagReadRequest(new GlobalFlagRequestId(Guid.NewGuid()), flags.Version, new FlagId(index)));
        return read.Status == GlobalFlagReadStatus.Known && read.Value is { } value ? value : throw new InvalidOperationException("AREA_SCRIPT_FLAG_UNREADABLE");
    }

    /// <summary>Never asked: <see cref="Admission"/> leaves out the story-state actions.</summary>
    public int StoryState() => throw new InvalidOperationException("AREA_SCRIPT_STORY_STATE_NOT_BOUND");
    public bool IsMonsterGenerator(ScrObjectRef attachee) => false;
    public ScrDialogState DialogState(ScrObjectRef attachee) => ScrDialogState.Normal;
    public ScrTrapCheck CheckTrapBeforeUse(ScrObjectRef triggerer, ScrObjectRef attachee) => ScrTrapCheck.NoTrap;
    public bool IsTrapObject(ScrObjectRef attachee) => false;
    public ScrObjectRef? Player() => Pc;
    public ScrObjectRef? Leader(ScrObjectRef critter) => Speaker(critter) is { } speaker && _state.Follows(speaker.Index) ? Pc : null;
    public bool IsJilted(ScrObjectRef critter) => false;
    public bool IsPc(ScrObjectRef critter) => critter == Pc;

    /// <summary>ARC-SKILLS-009 (SX1, SX2): the PC's effective level from the session hero (0 without one; the run's set then has no get
    /// skill); an NPC's from the sheet the session saved for it, else its map data with its effects; 0 for any other object.</summary>
    public int SkillLevel(ScrObjectRef obj, int skill)
    {
        if (skill is < 0 or >= SkillRules.BasicCount + SkillRules.TechCount) return 0;
        if (obj == Pc) return _session.QueryHeroSkills() is { } hero ? hero[(Skill)skill].Level : 0;
        return obj.Map == _map.RuntimeMap && _npcSheet?.Invoke(obj.Code) is { } npc ? _session.QueryNpcSkills(npc.Loaded, npc.Modifiers)[(Skill)skill].Level : 0;
    }

    /// <summary>
    /// Applies a run's effects: the global flags, the sound requests, the deferred calls, the scheme changes (ARC-SCRIPT-016), the schematic window requests (ARC-TECHNOLOGY-005), then
    /// the speaker's slot as map changes (a value equal to the loaded one clears its key). The session has no transaction across its flag owner and its map changes; a rejected step
    /// stops the later ones and is reported. The dialog start is the caller's.
    /// </summary>
    public bool Apply(ScrEffects effects)
    {
        ArgumentNullException.ThrowIfNull(effects);
        // The viewer's set admits no story state and no use item with skill; a trap object is not modelled.
        if (effects.StoryState is not null || effects.DestroyTrapObject is not null || effects.SkillUses.Count > 0) return false;
        foreach (var write in effects.GlobalFlags)
        {
            var flags = _session.Flags;
            var receipt = flags.Write(new GlobalFlagWriteRequest(new GlobalFlagRequestId(Guid.NewGuid()), flags.Version, new FlagId(write.Index), write.Value));
            if (receipt.Disposition is not (GlobalFlagWriteDisposition.Applied or GlobalFlagWriteDisposition.NoChange)) return false;
        }
        // ARC-SCRIPT-040: scheme pairs are the session's request, in action order; one the session drops (sound locked, as the
        // original drops it) or refuses (a negative id, a project guard) changes nothing and stops nothing. Effect sounds wait
        // for the viewer. Like the flags, they stay applied when a later step is refused (the original applies as it goes).
        foreach (var sound in effects.Sounds)
            if (sound.Kind == ScrSoundKind.Scheme) _session.PlaySoundScheme(_session.Version, sound.Id, sound.Ambient);
            else _sounds.Add(sound);
        // ARC-WORLD-039: deferred calls go to the session's queue in action order (the viewer pumps it); a refused request
        // stops the later steps, as a refused flag write does.
        foreach (var request in effects.Calls)
        {
            var queued = request.Kind == ScrCallKind.Schedule
                ? _session.ScheduleScriptCall(_session.Version, new(request.Script, request.Line, Role(request.Triggerer), Role(request.Attachee)), request.DelayMs)
                : _session.RemoveScriptCall(_session.Version, request.Script, Role(request.Attachee));
            if (queued.Disposition == GameSessionDisposition.Rejected) return false;
        }
        // ARC-SCRIPT-016: each scheme request in action order: the PC's own scheme, or an NPC's record with its map data; any other
        // object is ignored (0x4A6D90). A number the session refuses (outside 0..199, a documented deviation) or a PC without a hero
        // changes nothing and stops nothing, as the original's run goes on; a session that cannot take the change (the record budget,
        // the revision) stops the later steps, as a refused call request does.
        foreach (var request in effects.Schemes)
        {
            var target = request.Object == Pc ? SessionSchemeTarget.Hero
                : _npcAdvancement?.Invoke(request.Object.Code) is { } npc ? SessionSchemeTarget.ForNpc(npc) : null;
            if (target is not null && SchemeRefusalStops(_session.SetAutoLevelScheme(_session.Version, target, request.Scheme).Failure)) return false;
        }
        // ARC-TECHNOLOGY-005 (SW8, TC2 both fixed): each start schematic UI request in action order, resolved now (the PC, or an NPC
        // and its PC leader); it changes no session state, so it neither stops nor is stopped by a later step.
        foreach (var request in effects.Schematics)
            if (ResolveSchematic(request.Object, IsPc(request.Object), Leader(request.Object)) is { } open) _schematics.Add(open);
        // ARC-UI-049: a reading action shows its document to the PC only (0x4EE290); it changes no session state either.
        foreach (var request in effects.Readings)
            if (IsPc(request.Viewer)) _readings.Add(request);
        if (effects.Writeback is not { } writeback) return true;
        if (writeback.Source != ScrSource.Object || writeback.Point != ScrPoint.Dialog || writeback.Attachee is not { } attachee
            || Speaker(attachee) is not { } speaker || LoadedSlot(speaker) is not { } loaded) return false;
        MapChangeEdit Edit(string property, long value, long baseline) => new(_map, MapChangeKey.ForEntity(attachee.Code, property), value == baseline ? null : value);
        var result = _session.ApplyChanges(_session.Version, null,
        [
            Edit(ScriptProperty, writeback.Slot.Script, loaded.Script),
            Edit(AreaConversationState.LocalFlagsProperty, writeback.Slot.LocalFlags, loaded.LocalFlags),
            Edit(CountersProperty, writeback.Slot.Counters, loaded.Counters),
        ]);
        return result.Disposition != GameSessionDisposition.Rejected;
    }

    /// <summary>ARC-SCRIPT-016: whether a refused scheme change stops the later steps of <see cref="Apply"/>: the session could not
    /// take it (the record budget, the revision, a stale version); a number outside 0..199 or a missing hero does not.</summary>
    public static bool SchemeRefusalStops(GameSessionFailure failure) =>
        failure is not (GameSessionFailure.None or GameSessionFailure.InvalidAdvancement or GameSessionFailure.AdvancementUnavailable);

    /// <summary>
    /// ARC-TECHNOLOGY-005/006: what start schematic UI (129) does for its object (ledger SW8, NC3; TC2, both fixed, both modes): the
    /// PC opens its own window (crafter and viewer the PC); an NPC with a PC leader (the first PC up its leader chain) opens the window
    /// with the NPC as crafter and the leader as viewer, never through the original's unfilled callback; any other object (an NPC
    /// without a leader, an absent or non-critter object) opens nothing (null).
    /// </summary>
    public static AreaSchematicScriptOpen? ResolveSchematic(ScrObjectRef obj, bool isPc, ScrObjectRef? leader) =>
        isPc ? new(obj, obj, AreaSchematicScriptStatus.Open)
        : leader is { } viewer ? new(obj, viewer, AreaSchematicScriptStatus.Open) : null;

    /// <summary>A script object as a deferred call's session role (map and entity id), or null for an absent one.</summary>
    public static SessionScriptRole? Role(ScrObjectRef? obj) => obj is { } o ? new SessionScriptRole(new SessionMapId(o.Map), o.Code) : null;
}

/// <summary>ARC-TECHNOLOGY-005: how a start schematic UI request resolved.</summary>
public enum AreaSchematicScriptStatus
{
    /// <summary>The window opens: the PC's own (crafter and viewer the PC), or an NPC crafter's with its PC leader as viewer.</summary>
    Open = 0,
}

/// <summary>ARC-TECHNOLOGY-005: one resolved start schematic UI request: the crafter, the viewer and the outcome.</summary>
public readonly record struct AreaSchematicScriptOpen(ScrObjectRef Crafter, ScrObjectRef Viewer, AreaSchematicScriptStatus Status);
