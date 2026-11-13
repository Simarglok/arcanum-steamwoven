using Arcanum.Data;
using Arcanum.Data.Area;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Skills;

namespace Arcanum.World;

/// <summary>
/// The SCR dispatcher's host for the AREA viewer's dialog NPCs (ARC-SCRIPT-009): it reads a speaker's dialog slot
/// (the area's effective attachment, overridden by the session's map changes), the game's script content (ARC-SCRIPT-011:
/// the universal content stack, as for every map), the session's global flags and the follower standing, and it applies a run's effects to the session. Only the dialog
/// point of the map's speakers is modelled; everything the viewer does not model answers "no" (no generator, no trap,
/// alive and free, not jilted).
/// </summary>
public sealed class AreaScriptHost : IScrHost, IScrSkillQueries, IScrObjectQueries, IScrWorldQueries, IScrObjectStateQueries, IScrAreaQueries, IScrStoreQueries, IScrReactionQueries, IScrSpellQueries, IScrStoryQueries, IScrItemQueries, IScrVitalQueries, IScrCritterQueries, IScrCritterStateQueries, IScrCritterFieldQueries
{
    /// <summary>The PC as the scripts see it; the viewer has one.</summary>
    public const string PcCode = SessionEventKinds.PlayerEntity;
    /// <summary>Map-change keys of a speaker's dialog slot, besides <see cref="AreaConversationState.LocalFlagsProperty"/>.</summary>
    public const string CountersProperty = "dialog.counters", ScriptProperty = "dialog.script";

    /// <summary>
    /// The set the viewer runs: <see cref="ScrAdmissionSet.S3"/> (with the session's story-state number since ARC-SCRIPT-019), plus the three
    /// sound actions of <see cref="ScrAdmissionSet.S7"/> (ARC-SCRIPT-040), plus call script in and remove script call of
    /// <see cref="ScrAdmissionSet.S5"/> since the viewer pumps the session's event queue (ARC-WORLD-039), plus set auto-level
    /// scheme of <see cref="ScrAdmissionSet.S8"/> (ARC-SCRIPT-016), plus start schematic UI of <see cref="ScrAdmissionSet.S9"/>
    /// (ARC-TECHNOLOGY-005), plus the reading actions of <see cref="ScrAdmissionSet.S10"/> (ARC-UI-049). The actions of
    /// <see cref="ScrAdmissionSet.S12"/> (ARC-SCRIPT-018) are left out: a call from a dialog-point run would need the runner's dialog
    /// gates (E6) for its callee, assign object could change the dialog's NPC, which the conversation host takes from the click,
    /// and get object type needs object queries this host does not answer. Plus the quest, area, rumor and reputation actions of
    /// <see cref="ScrAdmissionSet.S13"/> (ARC-SCRIPT-019). ARC-SCRIPT-1241: plus fade (103) of <see cref="ScrAdmissionSet.S35"/>, which jumps the game clock and hands its movie to the viewer
    /// (<see cref="TakeFades"/>; Kerghan's dialog script 1105 plays movies 2, 3 and 6).
    /// </summary>
    public static ScrAdmissionSet Admission { get; } = new("S3-area-sound-calls-scheme-schematic-reading-story", ScrAdmissionSet.S3.Conditions.Concat(ScrAdmissionSet.StoryConditions),
        ScrAdmissionSet.S3.Actions
            .Concat([ScrCatalogue.Action.SetPcQuestState, ScrCatalogue.Action.MarkMapLocation, ScrCatalogue.Action.SetRumor,
                ScrCatalogue.Action.AddReputation, ScrCatalogue.Action.RemoveReputation, ScrCatalogue.Action.PlaySound, ScrCatalogue.Action.PlaySoundOn, ScrCatalogue.Action.PlaySoundScheme,
                ScrCatalogue.Action.CallScriptIn, ScrCatalogue.Action.RemoveScriptCall, ScrCatalogue.Action.SetAutoLevelScheme,
                ScrCatalogue.Action.StartSchematicUi, ScrCatalogue.Action.ShowBook, ScrCatalogue.Action.ShowPicture, ScrCatalogue.Action.ShowPlaque, .. ScrAdmissionSet.StoryActions, ScrCatalogue.Action.Fade]),
        ScrAdmissionSet.S3.ValueKinds.Concat(ScrAdmissionSet.StoreValueKinds), ScrAdmissionSet.S3.FocusKinds);

    /// <summary>ARC-SKILLS-009: <see cref="Admission"/> plus get skill (47) of <see cref="ScrAdmissionSet.S11"/>, the set a run uses while
    /// the session has a hero (the PC's levels are the hero's; without one a script that reads a skill is refused, not run on a made-up
    /// level). Use item with skill (79) stays out: no live script of the installed data uses it (census ARC-RES-045: 2 lines, none live).</summary>
    public static ScrAdmissionSet SkillAdmission { get; } = new("S3-area-sound-calls-scheme-schematic-reading-skill", Admission.Conditions,
        Admission.Actions.Append(ScrCatalogue.Action.GetSkill), Admission.ValueKinds, Admission.FocusKinds);

    /// <summary>
    /// ARC-SCRIPT-050 (slice 2 of the script trigger audit): <see cref="Admission"/> plus what the shown map's use host answers for a talk
    /// (<see cref="QueryHost"/>): the guard conditions of <see cref="ScrAdmissionSet.S4"/> (PC quest state, holds an item named, is named,
    /// wields an item named, has a reputation), obj is dead (22) and obj is switched off (27) of <see cref="ScrAdmissionSet.S18"/>, random (37) of
    /// <see cref="ScrAdmissionSet.S15"/>, float line (23) and get area (84) of <see cref="ScrAdmissionSet.S19"/>, and (ARC-DEV-178, B19) get reaction
    /// (29) of <see cref="ScrAdmissionSet.S23"/>. ARC-MAGIC-030 (MT21b): plus the spell conditions body spell (14), influenced by spell (35), knows spell (48) and mastered spell college (49)
    /// and give spell mastery in college (112) of <see cref="ScrAdmissionSet.S31"/>, which the use host answers and this host writes. ARC-SCRIPT-1563 (GT194–GT196): plus damage (60), apply unresistable damage (138) and heal hit points (75) of
    /// <see cref="ScrAdmissionSet.S30"/>, which <see cref="Apply"/> hands to the use host's session path (a talk's poison script 1059, a deferred call's poison chain). ARC-SCRIPT-1620 (TI6): plus attack (36) of <see cref="ScrAdmissionSet.S33"/>, which <see cref="Apply"/> hands to the use host's AI path (the PC or an NPC of the shown map as the attacker).
    /// ARC-SCRIPT-1733 (CS1–CS12): plus obj is animal (37), obj is in combat (31) and the nine critter-state conditions of <see cref="ScrAdmissionSet.CritterStateConditions"/>, which the use host answers; ARC-SCRIPT-1772: plus the critter getters (38, 39, 89) of <see cref="ScrAdmissionSet.CritterFieldActions"/> (the writes, toggle invulnerability and rename, stay out of the talk set: this host applies no object state besides its own).
    /// </summary>
    public static ScrAdmissionSet TalkAdmission { get; } = WithQueries("S3-area-talk-queries", Admission);

    /// <summary>ARC-SCRIPT-050: <see cref="SkillAdmission"/> with the commands of <see cref="TalkAdmission"/>.</summary>
    public static ScrAdmissionSet SkillTalkAdmission { get; } = WithQueries("S3-area-talk-queries-skill", SkillAdmission);

    private static ScrAdmissionSet WithQueries(string id, ScrAdmissionSet set) => new(id,
        set.Conditions.Concat(ScrAdmissionSet.ObjectQueryConditions).Concat(ScrAdmissionSet.ObjectStateQueryConditions).Concat(ScrAdmissionSet.SpellQueryConditions)
            .Concat(ScrAdmissionSet.CritterQueryConditions).Concat(ScrAdmissionSet.CritterStateConditions),
        set.Actions.Concat(ScrAdmissionSet.CritterFieldActions).Concat([ScrCatalogue.Action.Random, ScrCatalogue.Action.FloatLine, ScrCatalogue.Action.GetArea, ScrCatalogue.Action.GetReaction, ScrCatalogue.Action.GiveSpellMastery,
            ScrCatalogue.Action.Damage, ScrCatalogue.Action.DamageUnresistable, ScrCatalogue.Action.HealHitPoints, ScrCatalogue.Action.Attack]), set.ValueKinds, set.FocusKinds);

    /// <summary>The set a run of this host uses: <see cref="SkillAdmission"/> with a session hero, else <see cref="Admission"/>; ARC-SCRIPT-050:
    /// with the query commands of <see cref="TalkAdmission"/> while <see cref="QueryHost"/> gives a use host of this host's map.</summary>
    public ScrAdmissionSet RunAdmission => (_session.QueryHero() is null, Queries is null) switch
    {
        (true, true) => Admission,
        (false, true) => SkillAdmission,
        (true, false) => TalkAdmission,
        (false, false) => SkillTalkAdmission,
    };

    /// <summary>ARC-DEV-081 (MIG5): the set a run on <paramref name="attachee"/> uses: <see cref="RunAdmission"/>, except for a companion carried here
    /// (<see cref="AreaConversationHost.AdmitVisitor"/>), which keeps the narrower set the visitor host of ARC-WORLD-048 ran with
    /// (<see cref="SkillAdmission"/> with a hero, else <see cref="Admission"/>): the shown map's use host holds no row and no seed holder of an
    /// object of another map, so a query about it would be answered wrongly instead of being refused.</summary>
    public ScrAdmissionSet AdmissionFor(ScrObjectRef? attachee) =>
        attachee is { } obj && _state.IsCarried(obj.Code) ? (_session.QueryHero() is null ? Admission : SkillAdmission) : RunAdmission;

    /// <summary>ARC-MAGIC-041 (TI8): the set a deferred call of the event queue runs under: <see cref="AdmissionFor"/> plus wields an item named (21, which <see cref="WieldedItemName"/> answers for the hero without a use host) and use ammunition units (135
    /// script uses (<see cref="Apply"/> spends the cells through the session). A dialog script does not get it. ARC-SCRIPT-1482 (ledger WE2, WE4): plus the magick-tech adjustment (91, the ratio of a worn item for the hero, <see cref="PowerRatio"/>) and
    /// play script eye candy (120, presentation only), and ARC-SCRIPT-1586 get hit points and get fatigue points (107, 108, <see cref="FatiguePoints"/>), which the deferred poison scripts of the wield-on items use; their damage (60, 138) and heal (75) come from <see cref="AdmissionFor"/> (ARC-SCRIPT-1563: the talk set holds them while the shown
    /// map's use host answers, and <see cref="Apply"/> hands them to it).</summary>
    public ScrAdmissionSet CallAdmissionFor(ScrObjectRef? attachee) => AmmoSets.GetOrAdd(AdmissionFor(attachee), set => new(set.Id + "-ammo",
        set.Conditions.Append(ScrCatalogue.Condition.IsWieldingItem),
        set.Actions.Append(ScrCatalogue.Action.UseAmmoUnits).Append(ScrCatalogue.Action.MagickTechAdjustment).Append(ScrCatalogue.Action.PlayScriptEyeCandy)
            .Append(ScrCatalogue.Action.GetHitPoints).Append(ScrCatalogue.Action.GetFatiguePoints),
        set.ValueKinds, set.FocusKinds, set.CounterIndexWraps));

    /// <summary>ARC-SCRIPT-1586 (ledger VT1, VT2, PZ11): get hit points, the current and the maximum of the PC as the session's life gives them, or of what the shown map's use host answers while it answers (an NPC of that map); null for any other object (the run stops as not modelled).</summary>
    public (int Current, int Maximum)? HitPoints(ScrObjectRef obj) => Queries is not null ? Answering.HitPoints(obj)
        : IsPc(obj) && _session.QueryHeroLife() is { } life ? (life.Status.HitPoints, life.Status.MaxHitPoints) : null;

    /// <summary>ARC-SCRIPT-1586 (VT2): get fatigue points, as <see cref="HitPoints"/>; the deferred wield-on poison scripts 2677, 2679, 2681 and 2682 read the wearer's fatigue after their first damage.</summary>
    public (int Current, int Maximum)? FatiguePoints(ScrObjectRef obj) => Queries is not null ? Answering.FatiguePoints(obj)
        : IsPc(obj) && _session.QueryHeroLife() is { } life ? (life.Status.Fatigue, life.Status.MaxFatigue) : null;

    /// <summary>ARC-SCRIPT-1482 (WE2): a worn item's power ratio for the PC (<see cref="GameSessionOwner.QueryWornPowerRatio"/>), the item found by its entity alone (a deferred call's item role keeps the map it was scheduled on after the hero travelled, as every role but the PC's and its followers' does); for any other owner the shown map's use host answers (<see cref="AreaUseScriptHost.PowerRatio"/>: ARC-SCRIPT-1662, 100 for a container or scenery owner) while it answers; null otherwise (the run stops, as for an object the host does not know).</summary>
    public int? PowerRatio(ScrObjectRef item, ScrObjectRef owner) => owner == Pc ? _session.QueryWornPowerRatio(item.Code) : Queries?.PowerRatio(item, owner);

    /// <summary>No set of this host holds the identified condition: the host knows no object's identified state.</summary>
    public bool? IsIdentified(ScrObjectRef obj) => null;

    /// <summary>No set of this host holds the rewielding condition: no rewield runs here.</summary>
    public bool ItemsBeingRewielded() => false;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<ScrAdmissionSet, ScrAdmissionSet> AmmoSets = new();

    /// <summary>
    /// ARC-SCRIPT-050: the shown map's use host (<see cref="AreaUseScriptHost"/>), which answers this host's object, world and area
    /// questions as it answers a use or heartbeat run's: the objects' items, names and reputations, the session's random draws labelled
    /// <see cref="AreaUseScriptHost.RandomLabel"/>, the critters around an object at their live cells, and the shown map's area. It is used
    /// only while its shown map and the PC's map are this host's map, and not for a run on a carried companion (<see cref="AdmissionFor"/>). Null: none.
    /// </summary>
    public Func<AreaUseScriptHost?>? QueryHost { get; set; }

    // The use host's PC is the session actor's: its map must be this host's too, or float line would never find this host's PC.
    private AreaUseScriptHost? Queries => QueryHost?.Invoke() is { } host && host.ShownMap == _map.RuntimeMap && host.Pc == Pc ? host : null;

    /// <summary>ARC-DEV-191 (GA2): the area of the PC's map as the shown map's use host answers it (what get area stores for the PC), null without such a host of this map
    /// or when it cannot answer (outside map mode, the start map's sector unread); the dialog's <c>ia</c> condition.</summary>
    public int? PcMapArea() => Queries is { } host ? host.AreaOf(host.Pc) : null;

    private AreaUseScriptHost Answering => Queries ?? throw new InvalidOperationException("AREA_SCRIPT_QUERIES_UNAVAILABLE: the run's set holds a query command and no use host of this map answers it.");

    public int QuestState(ScrObjectRef obj, int quest) => Answering.QuestState(obj, quest);
    public bool HoldsItemNamed(ScrObjectRef obj, int name) => Answering.HoldsItemNamed(obj, name);
    public int NameOf(ScrObjectRef obj) => Answering.NameOf(obj);
    public bool IsMindControlled(ScrObjectRef obj) => Answering.IsMindControlled(obj);
    public int? WieldedItemName(ScrObjectRef obj, int location) => Queries is not null ? Answering.WieldedItemName(obj, location) : IsPc(obj) ? HeroWielded(location) : null;

    // ARC-MAGIC-041 (TI8): the wear question of a deferred call when no use host of this map answers (the powered shield's timer must not die
    // because the shown map changed): the hero's own kit and carried items, as the use host reads them (AreaUseScriptHost.WieldedItemName, SG4).
    private int? HeroWielded(int location)
    {
        var worn = _session.QueryEquipment().Worn.FirstOrDefault(item => (int)item.Slot == location);
        if (worn is null) return null;
        return _session.QueryHeroKit().Where(item => item.Entity == worn.Entity).Select(item => (int?)item.Kind.Name)
            .Concat(_session.QueryPcInventory().Where(item => item.Entity == worn.Entity).Select(item => (int?)item.Kind.Name))
            .Concat(_session.QueryMadeItems().Where(item => item.Entity == worn.Entity).Select(item => (int?)item.Kind.Name)).FirstOrDefault();
    }
    public bool HasReputation(ScrObjectRef obj, int reputation) => Answering.HasReputation(obj, reputation);
    public int? ObjectType(ScrObjectRef obj) => Answering.ObjectType(obj);
    public IReadOnlyList<ScrObjectRef> SceneryInVicinity(ScrObjectRef centre) => Answering.SceneryInVicinity(centre);
    public IReadOnlyList<ScrObjectRef> CrittersInVicinity(ScrObjectRef centre) => Answering.CrittersInVicinity(centre);
    public (long X, long Y)? Location(ScrObjectRef obj) => Answering.Location(obj);
    public bool? IsDead(ScrObjectRef obj) => Answering.IsDead(obj);
    public bool? IsSwitchedOff(ScrObjectRef obj) => Answering.IsSwitchedOff(obj);
    public int Random(int low, int high) => Answering.Random(low, high);
    public int? AreaOf(ScrObjectRef obj) => Answering.AreaOf(obj);
    public int? SpellFlags(ScrObjectRef obj) => Answering.SpellFlags(obj);
    // ARC-SCRIPT-1733: the critter-state conditions (CS1–CS9) and, with them, obj is animal and obj is in combat are the shown map's use host's answers, as the other object queries are.
    public bool? IsAnimal(ScrObjectRef obj) => Answering.IsAnimal(obj);
    public bool? IsInCombat(ScrObjectRef obj) => Answering.IsInCombat(obj);
    public bool? MetPcBefore(ScrObjectRef npc, ScrObjectRef pc) => Answering.MetPcBefore(npc, pc);
    public bool? IsInvisible(ScrObjectRef obj) => Answering.IsInvisible(obj);
    public bool? IsSurrendered(ScrObjectRef obj) => Answering.IsSurrendered(obj);
    public bool? IsInDialog(ScrObjectRef obj) => Answering.IsInDialog(obj);
    public bool? CanSee(ScrObjectRef observer, ScrObjectRef target) => Answering.CanSee(observer, target);
    public bool? CanHear(ScrObjectRef listener, ScrObjectRef source) => Answering.CanHear(listener, source);
    public bool? IsInvulnerable(ScrObjectRef obj) => Answering.IsInvulnerable(obj);
    public bool? IsUndead(ScrObjectRef obj) => Answering.IsUndead(obj);
    public bool? IsProwling(ScrObjectRef obj) => Answering.IsProwling(obj);
    // ARC-SCRIPT-1772: the critter getters are the shown map's use host's answers too.
    public int? SocialClassOf(ScrObjectRef obj) => Answering.SocialClassOf(obj);
    public int? OriginOf(ScrObjectRef obj) => Answering.OriginOf(obj);
    public int? FactionOf(ScrObjectRef obj) => Answering.FactionOf(obj);
    public bool? IsInfluencedBy(ScrObjectRef obj, int spell) => Answering.IsInfluencedBy(obj, spell);
    public bool? KnowsSpell(ScrObjectRef obj, int spell) => Answering.KnowsSpell(obj, spell);
    public int? MasteredCollege(ScrObjectRef obj) => Answering.MasteredCollege(obj);

    /// <summary>ARC-DEV-178 (B19, GR1): the standard effective reaction (0x4C0CC0) of one of this host's speakers toward the PC, read as the dialog's
    /// reaction conditions read it (the session's reaction with the speaker's and the PC's facts, the enemy list included); null (the dispatch ends
    /// as not modelled) for any other pair, whose facts this host does not hold.</summary>
    public int? ReactionOf(ScrObjectRef npc, ScrObjectRef pc) => IsPc(pc) && Speaker(npc) is { } speaker ? _state.Reaction(speaker.Index) : null;

    private readonly List<ScrSound> _sounds = [];
    private readonly List<AreaSchematicScriptOpen> _schematics = [];
    private readonly List<ScrReadingRequest> _readings = [];
    private readonly List<AreaScriptFloat> _floats = [];
    private readonly List<AreaScriptFade> _fades = [];

    /// <summary>ARC-DEV-324: queues a line for the viewer to float over <paramref name="speaker"/> as a script's float line does (the generic greeting, which is a
    /// floating line and no window).</summary>
    internal void Float(ScrObjectRef speaker, string text) => _floats.Add(new(speaker, text));

    /// <summary>ARC-SCRIPT-050: the float lines of the runs applied since the last call, in order, each with its text, for the viewer to
    /// float over its speaker.</summary>
    public IReadOnlyList<AreaScriptFloat> TakeFloats()
    {
        var floats = _floats.ToArray();
        _floats.Clear();
        return floats;
    }

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

    /// <summary>ARC-SCRIPT-1241: the fades (103) of the runs applied since the last call that ask for a movie, in order, for the viewer to play (their clock jumps are done; a fade with no
    /// movie and a sound is in <see cref="TakeSounds"/>).</summary>
    public IReadOnlyList<AreaScriptFade> TakeFades()
    {
        var fades = _fades.ToArray();
        _fades.Clear();
        return fades;
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
    private readonly AreaConversationProfile _profile;

    /// <param name="npcSheet">ARC-SKILLS-009: an NPC's sheet and effects as its map data holds them, by entity id, for get skill; null for
    /// an object that is not such an NPC (its level is 0).</param>
    /// <param name="content">The game's scripts; null when they could not be loaded, and then every script is missing.
    /// The host borrows it.</param>
    /// <param name="npcAdvancement">ARC-SCRIPT-016: an NPC's advancement as its map data holds it (sheet and scheme), by entity id, or
    /// null for an object that is not such an NPC; a scheme request for one is ignored, as the original ignores an object that is
    /// neither a PC nor an NPC. Without it only the PC's scheme changes.</param>
    /// <param name="profile">ARC-SCRIPT-050: the talks' PC facts and names, which a float line's text uses (null: the diagnostic profile).</param>
    public AreaScriptHost(GameSessionOwner session, SessionMapId map, IReadOnlyList<AreaSpeaker> speakers, AreaConversationState state, ScriptContent? content,
        Func<string, SessionNpcAdvancement?>? npcAdvancement = null, Func<string, (SessionNpcAdvancement Loaded, AdvancementModifiers Modifiers)?>? npcSheet = null,
        AreaConversationProfile? profile = null)
    {
        _session = session; _map = map; _speakers = speakers; _state = state; _content = content; _npcAdvancement = npcAdvancement; _npcSheet = npcSheet;
        _profile = profile ?? AreaConversationProfile.Diagnostic;
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
        // ARC-DEV-081: a carried companion's slot is its home map's key (AreaConversationState.MapOf).
        var stateMap = _state.MapOfEntity(attachee.Code);
        long? Read(string property) => _session.QueryObjectChange(stateMap, MapChangeKey.ForEntity(attachee.Code, property));
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

    /// <summary>ARC-SCRIPT-019: the session's story-state number.</summary>
    public int StoryState() => _session.QueryStoryState();

    /// <summary>ARC-DEV-100 (GV1–GV4): the session's global variables and the hero's variables and flags, which <see cref="Admission"/> admits.
    /// A session without a hero reads 0 for the PC's.</summary>
    public int GlobalVariable(int index) => _session.QueryGlobalVariable(index);
    // ARC-DEV-265: the session holds the hero's arrays alone; another PC reads 0 (SessionScriptLimits.IsHero).
    public int PcVariable(ScrObjectRef pc, int index) => SessionScriptLimits.IsHero(pc) ? _session.QueryPcVariable(index) : 0;
    public bool PcFlag(ScrObjectRef pc, int index) => SessionScriptLimits.IsHero(pc) && _session.QueryPcFlag(index);
    public bool IsMonsterGenerator(ScrObjectRef attachee) => false;
    // ARC-DEV-304 (SU15, E6): the dialog gates of an NPC of this map: dead as the session holds it (map data or killed), so point 9 of a dead NPC whose dialog file
    // lacks the death-speech flag 0x8 runs default; surrender and mind control are not modelled here (normal).
    public ScrDialogState DialogState(ScrObjectRef attachee) =>
        !IsPc(attachee) && Speaker(attachee) is not null && _session.QueryNpcDead(attachee.Code) ? ScrDialogState.Dead : ScrDialogState.Normal;
    public ScrTrapCheck CheckTrapBeforeUse(ScrObjectRef triggerer, ScrObjectRef attachee) => ScrTrapCheck.NoTrap;
    public bool IsTrapObject(ScrObjectRef attachee) => false;
    public ScrObjectRef? Player() => Pc;
    public ScrObjectRef? Leader(ScrObjectRef critter) => Speaker(critter) is { } speaker && _state.Follows(speaker.Index) ? Pc : null;
    public bool IsJilted(ScrObjectRef critter) => false;
    public bool IsPc(ScrObjectRef critter) => critter == Pc;

    /// <summary>ARC-SCRIPT-1165 (E5-c): the effect rule of a blessing or curse number, which a dialog script's add blessing (25) or add curse (27) needs; see <see cref="AreaUseScriptHost.BlessingEffects"/>.</summary>
    public Func<SessionBlessingKind, int, SessionEffectDefinition?>? BlessingEffects { get; set; }

    // SQ1–SQ12 (ARC-SCRIPT-1165): the story and time questions, from the session.
    public ScrGameTime? Time() => AreaScriptStoryQueries.Time(_session);
    public int GlobalQuestState(int quest) => AreaScriptStoryQueries.GlobalQuestState(_session, quest);
    public bool? RumorKnown(ScrObjectRef obj, int rumor) => AreaScriptStoryQueries.RumorKnown(_session, IsPc(obj), rumor);
    public bool RumorQuelled(int rumor) => AreaScriptStoryQueries.RumorQuelled(_session, rumor);
    public bool? HasBlessing(ScrObjectRef obj, int number, bool curse) => AreaScriptStoryQueries.HasBlessing(_session, IsPc(obj), number, curse);


    /// <summary>ARC-SKILLS-009 (SX1, SX2): the PC's effective level from the session hero (0 without one; the run's set then has no get
    /// skill); an NPC of this host's map whose map data the viewer reads: from the sheet the session saved for it, else that map data,
    /// with the map data's effects; 0 for any other object (an NPC whose map data is not read, a saved sheet included, or an object that
    /// names another map, as a deferred call's role can).</summary>
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
        // The viewer's set admits no use item with skill; a trap object is not modelled.
        if (effects.DestroyTrapObject is not null || effects.SkillUses.Count > 0) return false;
        // ARC-SCRIPT-1563: a script's damage and heal go through the shown map's use host (the talk set holds them only while it answers); one it cannot apply refuses the run whole, before anything applies.
        foreach (var request in effects.ObjectStates)
            if (request.Change is ScrObjectStateChange.Damage or ScrObjectStateChange.DamageUnresistable && Queries is not { CanApplyScriptDamage: true }
                || request.Change == ScrObjectStateChange.HealHitPoints && Queries is not { CanApplyScriptHeal: true }
                // ARC-SCRIPT-1620 (TI6): a script's attack goes through the use host's AI path too; an attacker it cannot apply (an object, VB2-2 gap d) refuses the run whole.
                || request.Change == ScrObjectStateChange.Attack && (Queries is not { CanApplyScriptAttack: true } || Queries.AttackRefusal(request) is not null)) return false;
        // ARC-SCRIPT-050 (FL2, FL4): each float line's text, read before anything applies. A DLG that is missing or unreadable floats nothing,
        // as the original's load fails; a line the NPC-line step would do more for (an action field, generated text) is not modelled and
        // leaves the run unapplied.
        var floats = new List<AreaScriptFloat>();
        foreach (var line in effects.Floats)
        {
            if (_content?.Dialog(line.Script, _session.Rules.Data) is not { Status: ScriptContentStatus.Read, Document: { } document }) continue;
            var printed = ScrPrintedLine.Read(document, line.Line, _profile.Pc.Gender != Arcanum.Scripting.Dialog.DialogPcGender.Male, _profile.Names);
            if (printed.Status != ScrPrintedLineStatus.Shown) return false;
            floats.Add(new(line.Speaker, printed.Text));
        }
        _floats.AddRange(floats);
        // The effect rule of every blessing or curse the run adds is known before anything applies (a missing rule refuses the run whole).
        if (!AreaScriptStory.BlessingRulesResolve(_session, effects, IsPc, BlessingEffects)) return false;
        foreach (var write in effects.GlobalFlags)
        {
            var flags = _session.Flags;
            var receipt = flags.Write(new GlobalFlagWriteRequest(new GlobalFlagRequestId(Guid.NewGuid()), flags.Version, new FlagId(write.Index), write.Value));
            if (receipt.Disposition is not (GlobalFlagWriteDisposition.Applied or GlobalFlagWriteDisposition.NoChange)) return false;
        }
        // ARC-DEV-100: the global variables and the hero's variables and flags.
        if (!_session.ApplyScriptStores(effects)) return false;
        // ARC-SCRIPT-019: the story-state number and the quest, area, rumor and reputation requests, for the PC.
        if (!AreaScriptStory.Apply(_session, effects, IsPc, _state.AddAward, BlessingEffects)) return false;
        // ARC-SCRIPT-040: scheme pairs are the session's request, in action order; one the session drops (sound locked, as the
        // original drops it) or refuses (a negative id, a project guard) changes nothing and stops nothing. Effect sounds wait
        // for the viewer. Like the flags, they stay applied when a later step is refused (the original applies as it goes).
        foreach (var sound in effects.Sounds)
            if (sound.Kind == ScrSoundKind.Scheme) _session.PlaySoundScheme(_session.Version, sound.Id, sound.Ambient);
            else _sounds.Add(sound);
        // ARC-MAGIC-041 (TI8): each use ammunition units request in action order, before the deferred calls (the powered shield's call spends a
        // cell and then schedules the next call). The original ignores the answer of the unit use: a holder with too few units loses none and
        // the script goes on, and so it does for an object the session holds no ammunition for (not the hero, not an NPC it lists): nothing is
        // spent. Only a session limit (the record budget, the revision) stops the later steps, as a refused call request does.
        foreach (var use in effects.AmmoUses)
        {
            var holder = IsPc(use.Holder) ? SessionAmmoHolder.Hero : new SessionAmmoHolder(use.Holder.Code);
            var spent = _session.SpendAmmo(_session.Version, holder, use.AmmoType, use.Count);
            if (spent.Disposition == GameSessionDisposition.Rejected
                && spent.Failure is GameSessionFailure.BudgetExceeded or GameSessionFailure.RevisionExhausted) return false;
        }
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
        // ARC-MAGIC-030 (SC5): each give spell mastery request in action order, for the PC or an NPC of this map whose advancement the viewer reads; any other object is ignored (the
        // original does nothing for an object that is not a PC or NPC). The spell-book rule decides; a session that cannot take the change stops the later steps.
        // ARC-SCRIPT-1563 (GT194–GT196): a script's damage and heal apply here too, in the action order of the requests: the use host's session path (victim, amount, type, unresistable; the victim's taking-damage script is asked, GT198).
        // The dialog opens after this (X11: the dialog action ends the run), so a talk's damage lands before the window.
        // The use host is taken once: the damage's own effects (a victim's script, a death) may move what QueryHost answers.
        var damageHost = Queries;
        foreach (var request in effects.ObjectStates)
        {
            if (request.Change is ScrObjectStateChange.Damage or ScrObjectStateChange.DamageUnresistable) { damageHost!.ApplyScriptDamage(request); LastDamage = damageHost.LastDamage; continue; }
            if (request.Change == ScrObjectStateChange.HealHitPoints) { damageHost!.ApplyScriptHeal(request); LastHeal = damageHost.LastHeal; continue; }
            if (request.Change == ScrObjectStateChange.Attack) { damageHost!.ApplyScriptAttack(request); LastAttack = damageHost.LastAttack; continue; }
            if (request.Change != ScrObjectStateChange.GiveSpellMastery) continue;
            if (request.Object == Pc) { if (!AreaScriptSpells.GiveMastery(_session, null, request.Value)) return false; }
            else if (request.Object.Map == _map.RuntimeMap && _npcAdvancement?.Invoke(request.Object.Code) is { } npc && !AreaScriptSpells.GiveMastery(_session, npc, request.Value)) return false;
        }
        // ARC-TECHNOLOGY-005 (SW8, TC2 both fixed): each start schematic UI request in action order, resolved now (the PC, or an NPC
        // and its PC leader); it changes no session state, so it neither stops nor is stopped by a later step.
        foreach (var request in effects.Schematics)
            if (ResolveSchematic(request.Object, IsPc(request.Object), Leader(request.Object)) is { } open) _schematics.Add(open);
        // ARC-UI-049: a reading action shows its document to the PC only (0x4EE290); it changes no session state either.
        foreach (var request in effects.Readings)
            if (IsPc(request.Viewer)) _readings.Add(request);
        // ARC-SCRIPT-1241 (MV5): each fade's clock jump, then its movie for the viewer, or (no movie) its sound as an effect sound; the later steps stop when the clock refuses.
        var asked = new List<AreaScriptFade>();
        var fadesApplied = AreaScriptFades.Apply(_session, effects, asked);
        foreach (var fade in asked)   // the fades before a refused jump keep their movie or sound, as their clock jumps stay
            if (fade.Movie != 0) _fades.Add(fade); else _sounds.Add(new(ScrSoundKind.Effect, fade.Sound));
        if (!fadesApplied) return false;
        if (effects.Writeback is not { } writeback) return true;
        if (writeback.Source != ScrSource.Object || writeback.Point != ScrPoint.Dialog || writeback.Attachee is not { } attachee
            || Speaker(attachee) is not { } speaker || LoadedSlot(speaker) is not { } loaded) return false;
        var stateMap = _state.MapOfEntity(attachee.Code);
        // ARC-DEV-096: a part back at its loaded value is cleared where the object's record sits, which may be another map than the speaker's state map.
        MapChangeEdit Edit(string property, long value, long baseline)
        {
            var key = MapChangeKey.ForEntity(attachee.Code, property);
            return value == baseline ? new(_session.QueryObjectRecordMap(key) ?? stateMap, key, null) : new(stateMap, key, value);
        }
        var result = _session.ApplyChanges(_session.Version, null,
        [
            Edit(ScriptProperty, writeback.Slot.Script, loaded.Script),
            Edit(AreaConversationState.LocalFlagsProperty, writeback.Slot.LocalFlags, loaded.LocalFlags),
            Edit(CountersProperty, writeback.Slot.Counters, loaded.Counters),
        ]);
        return result.Disposition != GameSessionDisposition.Rejected;
    }

    /// <summary>ARC-SCRIPT-1563: what the viewer answered to the last script damage a run of this host applied (test and smoke seam; null before any).</summary>
    public string? LastDamage { get; private set; }

    /// <summary>ARC-SCRIPT-1563: what the viewer answered to the last script heal a run of this host applied (test and smoke seam; null before any).</summary>
    public string? LastHeal { get; private set; }

    /// <summary>ARC-SCRIPT-1620: what the AI host answered to the last script attack a run of this host applied (the stop of the event's plan; test and smoke seam; null before any).</summary>
    public string? LastAttack { get; private set; }

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

/// <summary>ARC-SCRIPT-050: one float line a run applied: the object it floats over and its text.</summary>
public readonly record struct AreaScriptFloat(ScrObjectRef Speaker, string Text);

/// <summary>ARC-TECHNOLOGY-005: how a start schematic UI request resolved.</summary>
public enum AreaSchematicScriptStatus
{
    /// <summary>The window opens: the PC's own (crafter and viewer the PC), or an NPC crafter's with its PC leader as viewer.</summary>
    Open = 0,
}

/// <summary>ARC-TECHNOLOGY-005: one resolved start schematic UI request: the crafter, the viewer and the outcome.</summary>
public readonly record struct AreaSchematicScriptOpen(ScrObjectRef Crafter, ScrObjectRef Viewer, AreaSchematicScriptStatus Status);
