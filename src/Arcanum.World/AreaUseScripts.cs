using Arcanum.Data.Area;
using Arcanum.Data.Maps;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;

namespace Arcanum.World;

/// <summary>
/// ARC-WORLD-081: what the world actions of a use, tile or sector script read from the viewer (set S15): the PC's live cell (the session's
/// actor placement follows the walking actor only at a save or a map change), whether a shown row is destroyed or switched off in the session
/// (the original's object lists skip it), the item a script's prototype number names (<see cref="AreaPrototypeItems.ScriptItem"/>), and the
/// shown map as the create action's placement reads it (null when there is none). ARC-WORLD-114: <see cref="Kill"/> applies a script's
/// kill (58) to an NPC of the shown map, a death with no killer, and answers whether the session took it; <see cref="CanKill"/> says
/// beforehand whether it can (the NPC's combat values read; null: always). Without <see cref="Kill"/>, or for an NPC it cannot kill, a
/// kill leaves the run unapplied. ARC-SCRIPT-047: <see cref="CritterCell"/> is an NPC's live cell when it is not where its map data puts
/// it (an NPC the AI moved, a companion; null: its row's cell), for the critters in a run's vicinity. ARC-SCRIPT-050: <see cref="MapArea"/> is
/// the shown map's map-list area and whether it is the start map, for get area (84); null when the viewer has no map-list row for it (outside
/// map mode), and then get area is not answered. ARC-SCRIPT-052: <see cref="PeaceFacts"/> are an NPC's facts for a script's stop attacking
/// (109; null: the session's defaults) and <see cref="Stopped"/> is the host's part of each NPC's stop (its animation interrupt), run after
/// the session took it. ARC-DEV-178 (B19): <see cref="StartMapAreaAt"/> is the area at a tile of the start map (<see cref="AreaStartMapAreas"/>) for get
/// area when the shown map is the start map with map-list area 0; null (or an answer of null): not answered. ARC-DEV-231 (MIG13 S14a, GT158–GT161):
/// <see cref="ScriptWalk"/> starts a script's walk to (69) or run to (74) of an NPC of the shown map to a cell (the AI host's goal 7 / 55 starter) and answers what happened;
/// without it a run with a walk or run action is left unapplied. ARC-DEV-233 (S14b, GT162): <see cref="HealFatigue"/> takes an amount of fatigue damage off an NPC of the shown map through the session's life
/// step (whose get-up starts for a critter that comes round) and answers whether the session took it; without it a run with a heal fatigue is left unapplied. <see cref="ScriptAnimate"/> starts a script's perform animation (62, goal 0 or 60) of an NPC of the shown map in the AI host and
/// answers what happened; without it a run with a perform animation is left unapplied. ARC-DEV-234 (S14c, GT164): <see cref="ScriptSteal"/> starts a script's try to steal 100 coins (115) of an NPC of the shown map
/// from another NPC or the PC (<see cref="AreaScriptWorld"/> passes the victim as the PC's scheduler id) and answers what happened; without it a run with a steal is left unapplied. ARC-DEV-236 (S14e, GT187): <see cref="ScriptProne"/> makes an NPC of the shown map lie down (a level-4 interrupt of its runs, then the fall art) and answers what happened; without it a run with a become prone is left unapplied. ARC-DEV-241 (S14f, GT183/GT184): <see cref="ScriptCast"/> (caster, target, spell, free, unresistable) casts a script's spell at an NPC of the shown map or the PC by an NPC of the shown map or by none, and answers what happened; without it a run with a cast is left unapplied. ARC-DEV-243 (S14g, GT194–GT197): <see cref="ScriptDamage"/> (victim, amount, damage type, unresistable) deals a script's damage to an NPC of the shown map or the PC and <see cref="ScriptHeal"/> (victim, amount) heals its hit points; each answers what happened, and without it a run with the action is left unapplied.
/// </summary>
public sealed record AreaScriptWorld(Func<CellCoord?> PcCell, Func<AreaObjectRecord, bool> Gone, Func<int, AreaPrototypeItem?> Item, Func<IDropMap?> DropMap,
    Func<ScrObjectRef, bool>? Kill = null, Func<ScrObjectRef, bool>? CanKill = null, Func<ScrObjectRef, CellCoord?>? CritterCell = null,
    Func<(int Area, bool StartMap)?>? MapArea = null, Func<string, SessionPeaceFacts>? PeaceFacts = null, Action<SessionPeaceStep>? Stopped = null,
    Func<CellCoord, int?>? StartMapAreaAt = null, Func<string, CellCoord, bool, string>? ScriptWalk = null,
    Func<ScrObjectRef, int, bool>? HealFatigue = null, Func<string, int, string>? ScriptAnimate = null, Func<string, string, string>? ScriptSteal = null, Func<string, string>? ScriptProne = null,
    Func<string?, string, int, bool, bool, string>? ScriptCast = null, Func<string, int, int, bool, string>? ScriptDamage = null, Func<string, int, string>? ScriptHeal = null,
    Func<string, bool>? ScriptDamageAsk = null);

/// <summary>ARC-WORLD-081: one create object a run applied: the prototype number as the script named it, the object it was created next to,
/// and the session's result (<see cref="InventoryMutationResult.Item"/> the new item; none under the Steamwoven limit).</summary>
public sealed record AreaScriptCreation(int Prototype, ScrObjectRef Near, InventoryMutationResult Result);

/// <summary>What a scripted use did: ran with a teleport of the player (<see cref="Teleport"/>), ran without one, was refused
/// by the dispatcher or by the session, found no script content, or asked for a teleport the caller cannot take (then
/// nothing of the run is applied).</summary>
public enum AreaScriptedUseOutcome { Teleport, NoTeleport, Refused, EffectsRejected, ContentUnavailable, TeleportRefused }

/// <summary>A scripted use: the outcome, the first teleport of the player the run asked for, the printed lines the viewer
/// shows, the dispatcher's result and a detail (identifiers and statuses only).</summary>
public sealed record AreaScriptedUse(AreaScriptedUseOutcome Outcome, ScrTeleport? Teleport, IReadOnlyList<string> Lines, ScrRunResult? Run, string Detail)
{
    /// <summary>ARC-WORLD-110: the trap meeting before the use script (null when the object carries no trap or no trap host is set).
    /// When it cancels the use (<see cref="AreaTrapMeeting.Cancels"/>) the script did not run: the outcome is
    /// <see cref="AreaScriptedUseOutcome.NoTeleport"/> with no run.</summary>
    public AreaTrapMeeting? Trap { get; init; }

    /// <summary>ARC-SCRIPT-046: the conversation a tile or sector script asked for (its own or a called script's dialog action, which
    /// the original starts between the PC and the action's attachee, an NPC of the shown map), with the run's other effects applied and
    /// no teleport; the caller opens it. Null for every other run.</summary>
    public ScrDialogStart? Dialog { get; init; }
}

/// <summary>
/// ARC-SCRIPT-015 (plan slice S4, viewer part): the AREA viewer's SCR host for a use click on an object whose use script can
/// reach a teleport, and since ARC-SCRIPT-017 for the tile and sector scripts the player enters (<see cref="RunPlaced"/>). The attachee is a T0 object of the shown map (any row of its snapshot, by <see cref="EntityId"/>; an
/// item or holder of another loaded map through its <see cref="AreaInventoryMap"/> rows); its use slot is the row's
/// effective attachment, overridden by the session's map changes <c>use.lf</c>, <c>use.counters</c> and
/// <c>use.script</c>. It answers the S4 guards (<see cref="IScrObjectQueries"/>) from the session: the PC's quest log and the
/// quests' global states, the hero's kit, carried and worn items, the holders' current lists, and from the rows the T0 name and
/// spell flags.
/// The PC's reputations are the session's (ARC-WORLD-044); no other object has one. Everything else the viewer does not model
/// answers "no", as for the dialog and item hosts.
/// </summary>
public sealed class AreaUseScriptHost : IScrHost, IScrObjectQueries, IScrWorldQueries, IScrObjectStateQueries, IScrAreaQueries, IScrStoreQueries, IScrStatQueries
{
    /// <summary>Map-change keys of an object's use slot.</summary>
    public const string LocalFlagsProperty = "use.lf", CountersProperty = "use.counters", ScriptProperty = "use.script";
    /// <summary>ARC-SKILLS-010: the catching-thief point (11) whose slot an NPC's run keeps as map changes too, under <c>scr11.*</c>.</summary>
    public const int CatchingThiefPoint = 11;
    /// <summary>ARC-WORLD-114: the map-change key of an NPC a script's toggle state (56) switched off (value 1; absent while it is as its
    /// map data has it). The original flips object flag 0x2; an NPC switched off is neither drawn nor in the object lists, and its AI
    /// passes stop at the gate (<see cref="SwitchedOff"/>).</summary>
    public const string OffProperty = "obj.off";
    /// <summary>ARC-SCRIPT-049: the map-change key of the faction a script's set faction (134) gave an NPC (absent while it has its map
    /// data's; HW4). The original writes the NPC's faction field (0x124), which the save keeps; <see cref="Faction"/> reads it back.</summary>
    public const string FactionProperty = "npc.faction";
    /// <summary>ARC-SCRIPT-052: the map-change key of an NPC a script's destroy (68) took out of the world (value 1). The original sets object
    /// flag 0x1 (TD2); a destroyed NPC is neither drawn nor in the object lists, and the view no longer reaches it (<see cref="Destroyed"/>).</summary>
    public const string DestroyedProperty = "obj.destroyed";
    private const int NpcType = 16, DestroyedObjectFlag = 0x1, OffObjectFlag = 0x2;
    private const string ScriptPart = "script", LocalFlagsPart = "lf", CountersPart = "counters";

    /// <summary>The map-change property of a slot's part at a point the host writes back: <c>use.*</c> for the use point,
    /// <c>scr&lt;point&gt;.*</c> for the catching-thief point and (ARC-WORLD-114) the first-heartbeat and heartbeat points, and
    /// (ARC-SCRIPT-047) the dialog point's <c>dialog.*</c>, the keys the dialog host keeps a speaker's dialog slot under
    /// (<see cref="AreaScriptHost"/>), which a heartbeat's call-ex of the dialog point writes.</summary>
    public static string SlotProperty(int point, string part) => point switch
    {
        ScrPoint.Use => "use." + part,
        ScrPoint.Dialog => "dialog." + part,
        _ => $"scr{point}.{part}",
    };
    /// <summary>The spell flag the original reads as mind-controlled (field 20, bit 27; ledger SG3).</summary>
    public const uint MindControlledFlag = 0x0800_0000;

    /// <summary>
    /// The set a use click runs: <see cref="ScrAdmissionSet.S4"/> (with the session's story-state number since ARC-SCRIPT-019)
    /// without the dialog action (a use click does not open a conversation here), plus the reading actions of
    /// <see cref="ScrAdmissionSet.S10"/> (ARC-UI-049: a plaque, book or picture a use, tile or sector script shows), plus call script,
    /// assign object, get object type and the object variables of <see cref="ScrAdmissionSet.S12"/> (ARC-SCRIPT-018), plus the quest,
    /// area, rumor and reputation actions of <see cref="ScrAdmissionSet.S13"/> (ARC-SCRIPT-019), plus the world actions of
    /// <see cref="ScrAdmissionSet.S15"/> (ARC-WORLD-081): random, loop for and loop end over the scenery in the vicinity, create object and a
    /// counter operand with any index. ARC-DEV-157 (bug B23): plus get stat (33) of <see cref="ScrAdmissionSet.S22"/>, answered for the PC only
    /// (<see cref="EffectiveStat"/>). ARC-DEV-205 (bug B22): plus unfog town map (113) of <see cref="ScrAdmissionSet.S24"/>, which reveals the town map
    /// in the session (<see cref="AreaScriptStory"/>); the wreck's sector script 3036 and one use script carry it (the census counts two scripts).
    /// </summary>
    public static ScrAdmissionSet Admission { get; } = new("S4-area-use-reading-objects-story-world", ScrAdmissionSet.S4.Conditions,
        ScrAdmissionSet.S4.Actions.Where(a => a is not ScrCatalogue.Action.Dialog)
            .Concat([ScrCatalogue.Action.ShowBook, ScrCatalogue.Action.ShowPicture, ScrCatalogue.Action.ShowPlaque,
                ScrCatalogue.Action.CallScript, ScrCatalogue.Action.AssignObject, ScrCatalogue.Action.GetObjectType,
                ScrCatalogue.Action.SetPcQuestState, ScrCatalogue.Action.MarkMapLocation, ScrCatalogue.Action.SetRumor,
                ScrCatalogue.Action.AddReputation, ScrCatalogue.Action.RemoveReputation,
                ScrCatalogue.Action.Random, ScrCatalogue.Action.LoopFor, ScrCatalogue.Action.LoopEnd, ScrCatalogue.Action.CreateObject, ScrCatalogue.Action.GetStat,
                ScrCatalogue.Action.UnfogTownMap]),
        ScrAdmissionSet.S4.ValueKinds.Concat(ScrAdmissionSet.StoreValueKinds),
        ScrAdmissionSet.S4.FocusKinds.Concat([ScrFocusKind.LocalObject, ScrFocusKind.CurrentLoopedObject, ScrFocusKind.EverySceneryInVicinity]),
        counterIndexWraps: true);

    /// <summary>ARC-DEV-243 (S14g): damage (60), apply unresistable damage (138) and heal hit points (75), which the placed and heartbeat sets admit.</summary>
    private static readonly int[] DamageActions = [ScrCatalogue.Action.Damage, ScrCatalogue.Action.DamageUnresistable, ScrCatalogue.Action.HealHitPoints];

    /// <summary>ARC-DEV-241 (S14f): the six cast actions (48, 61, 118, 121, 122, 136) the placed and heartbeat sets admit.</summary>
    private static readonly int[] CastActions = [ScrCatalogue.Action.CastSpell, ScrCatalogue.Action.CastSpellOn, ScrCatalogue.Action.CastFreeSpell, ScrCatalogue.Action.CastUnresistableSpellBy,
        ScrCatalogue.Action.CastFreeUnresistableSpell, ScrCatalogue.Action.CastUnresistableSpellOn];

    /// <summary>
    /// ARC-SCRIPT-046: the set a tile or sector script runs (<see cref="RunPlaced"/>): <see cref="Admission"/> with the dialog action, which
    /// such a run, or a script it calls, uses to start a conversation of the PC with an NPC (<see cref="AreaScriptedUse.Dialog"/>).
    /// ARC-SCRIPT-052 (audit slice 8, the first group of the tile scripts' refusals): plus call-ex (81), by which a tile an NPC steps on runs
    /// his own dialog point (Virgil's talk at the altar, 1328), destroy (68) and stop attacking (109) of <see cref="ScrAdmissionSet.S20"/>,
    /// which the story tiles apply to the NPC that steps on them. ARC-DEV-157 (bug B20): plus the focus kinds everyone (8) and anyone (9) in
    /// the vicinity of <see cref="ScrAdmissionSet.S17"/> in actions (the loop over the critters around the tile, 443 refused tile rows in the
    /// audit), which <see cref="CrittersInVicinity"/> answers.
    /// </summary>
    public static ScrAdmissionSet PlacedAdmission { get; } = new("S4-area-placed-dialog-actor", Admission.Conditions,
        Admission.Actions.Concat([ScrCatalogue.Action.Dialog, ScrCatalogue.Action.CallScriptEx, ScrCatalogue.Action.Destroy, ScrCatalogue.Action.StopAttacking,
            ScrCatalogue.Action.WalkTo, ScrCatalogue.Action.RunTo, ScrCatalogue.Action.HealFatigue, ScrCatalogue.Action.PerformAnimation, ScrCatalogue.Action.StealCoins, ScrCatalogue.Action.BecomeProne, .. CastActions, .. DamageActions]),
        Admission.ValueKinds, Admission.FocusKinds.Concat([ScrFocusKind.EveryoneInVicinity, ScrFocusKind.AnyoneInVicinity]), counterIndexWraps: true);

    /// <summary>
    /// ARC-WORLD-114: the set an NPC's first-heartbeat (10) and heartbeat (19) scripts run from the AI tick (<see cref="RunHeartbeat"/>):
    /// <see cref="ScrAdmissionSet.S2"/> (control flow, local and global flags, counters, arithmetic, story state, remove and change this
    /// script) with the single-object focus kinds of <see cref="ScrAdmissionSet.S3"/>, plus toggle state (56) and kill (58) of
    /// <see cref="ScrAdmissionSet.S16"/>. ARC-SCRIPT-047: plus what a heartbeat needs to open an NPC's talk with the PC once the PC is near
    /// (1100, ledger TG3–TG6): loop for and loop end over everyone or anyone in the vicinity (<see cref="ScrAdmissionSet.S17"/>) with the
    /// current looped object, get object type, call-ex (of the NPC's dialog point among others) and the dialog action, which in a call-ex
    /// of the dialog point asks for the talk (<see cref="AreaScriptedUse.Dialog"/>; a heartbeat's own dialog action has the NPC as its
    /// triggerer and starts nothing). A heartbeat runs every few seconds for every NPC near the PC, so the set stays this narrow; a
    /// script outside it is refused and the tick goes on as without a script. ARC-SCRIPT-049 (audit slice 1): plus the world questions the
    /// installed heartbeats ask first: the conditions following (18), is named (20), is dead (22), is switched off (27) and has a reputation
    /// (33), and the actions call script in (54, through the session's queue), distance between (71) and set faction (134) of
    /// <see cref="ScrAdmissionSet.S18"/>.
    /// </summary>
    public static ScrAdmissionSet HeartbeatAdmission { get; } = new("S2-area-heartbeat-state-kill-talk-world",
        ScrAdmissionSet.S2.Conditions.Concat([ScrCatalogue.Condition.FollowingPc, ScrCatalogue.Condition.IsNamed, ScrCatalogue.Condition.IsDead,
            ScrCatalogue.Condition.IsSwitchedOff, ScrCatalogue.Condition.HasReputation]),
        ScrAdmissionSet.S2.Actions.Concat([ScrCatalogue.Action.ToggleState, ScrCatalogue.Action.Kill, ScrCatalogue.Action.LoopFor, ScrCatalogue.Action.LoopEnd,
            ScrCatalogue.Action.GetObjectType, ScrCatalogue.Action.CallScriptEx, ScrCatalogue.Action.Dialog, ScrCatalogue.Action.CallScriptIn,
            ScrCatalogue.Action.DistanceBetween, ScrCatalogue.Action.SetFaction, ScrCatalogue.Action.WalkTo, ScrCatalogue.Action.RunTo,
            ScrCatalogue.Action.HealFatigue, ScrCatalogue.Action.PerformAnimation, ScrCatalogue.Action.StealCoins, ScrCatalogue.Action.BecomeProne, .. CastActions, .. DamageActions]),
        ScrAdmissionSet.S2.ValueKinds.Concat(ScrAdmissionSet.StoreValueKinds),
        ScrAdmissionSet.S3.FocusKinds.Concat([ScrFocusKind.CurrentLoopedObject, ScrFocusKind.EveryoneInVicinity, ScrFocusKind.AnyoneInVicinity]));

    /// <summary>ARC-WORLD-114: whether a script switched the NPC <paramref name="entity"/> of map <paramref name="map"/> off
    /// (<see cref="OffProperty"/>).</summary>
    public static bool SwitchedOff(GameSessionOwner session, int map, string entity) =>
        (session ?? throw new ArgumentNullException(nameof(session))).QueryMapChange(new SessionMapId(map), MapChangeKey.ForEntity(entity, OffProperty)) == 1;

    /// <summary>ARC-SCRIPT-052: whether a script destroyed the NPC <paramref name="entity"/> of map <paramref name="map"/>
    /// (<see cref="DestroyedProperty"/>).</summary>
    public static bool Destroyed(GameSessionOwner session, int map, string entity) =>
        (session ?? throw new ArgumentNullException(nameof(session))).QueryMapChange(new SessionMapId(map), MapChangeKey.ForEntity(entity, DestroyedProperty)) == 1;

    /// <summary>ARC-SCRIPT-049: the faction of the NPC <paramref name="row"/> now: the one a script's set faction gave it
    /// (<see cref="FactionProperty"/>), else <paramref name="loaded"/>, its map data's.</summary>
    public static int Faction(GameSessionOwner session, AreaObjectRecord row, int loaded)
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(row);
        return session.QueryMapChange(new SessionMapId(row.Key.RuntimeMap), MapChangeKey.ForEntity(EntityId(row), FactionProperty)) is { } set
            ? unchecked((int)set) : loaded;
    }

    /// <summary>ARC-WORLD-081: the label of a script's random draws on the session's generator.</summary>
    public const string RandomLabel = "scr.random";

    private readonly List<ScrReadingRequest> _readings = [];

    /// <summary>ARC-UI-049: the reading actions of the runs applied since the last call whose viewer is the PC, in order.</summary>
    public IReadOnlyList<ScrReadingRequest> TakeReadings()
    {
        var readings = _readings.ToArray();
        _readings.Clear();
        return readings;
    }

    /// <summary>
    /// The session entity of a row: a mobile's own id (<see cref="AreaConversationState.EntityId"/>, as the inventory and
    /// dialog hosts name it); a sector object's sector and window ordinal, because sector objects carry a null id and so
    /// share one id digest (the transition reader and the area owner key them the same way).
    /// </summary>
    public static string EntityId(AreaObjectRecord row)
    {
        ArgumentNullException.ThrowIfNull(row);
        // A static row without a sector is not a use site here (the transfers match statics by sector and ordinal).
        return row.Key.Kind == AreaSourceKind.Static && row.Key.Sector is { } sector ? $"sector:{sector}:{row.Key.SourceIndex}" : AreaConversationState.EntityId(row);
    }

    private readonly GameSessionOwner _session;
    private readonly IReadOnlyCollection<AreaInventoryMap> _maps;
    private readonly ScriptContent? _content;
    private readonly int _shownMap;
    private readonly List<SessionExperienceAward> _awards = [];

    /// <summary>ARC-SCRIPT-019: the experience awards the runs' completed quests gave since the last call, in order; the viewer passes
    /// their follower steps and level-up messages on.</summary>
    public IReadOnlyList<SessionExperienceAward> TakeAwards()
    {
        var awards = _awards.ToArray();
        _awards.Clear();
        return awards;
    }
    private readonly Dictionary<string, AreaObjectRecord> _shown = new(StringComparer.Ordinal);
    // ARC-WORLD-081: the shown map's scenery in snapshot order, for the vicinity; ARC-SCRIPT-047: its NPCs likewise.
    private readonly List<(string Entity, AreaObjectRecord Row)> _scenery = [], _npcs = [];
    private readonly AreaScriptWorld? _world;
    private readonly List<AreaScriptCreation> _created = [];
    private (string Entity, AreaObjectRecord Row)? _clicked;

    /// <summary>ARC-WORLD-081: the create objects of the runs applied since the last call, in order.</summary>
    public IReadOnlyList<AreaScriptCreation> TakeCreations()
    {
        var created = _created.ToArray();
        _created.Clear();
        return created;
    }

    /// <param name="shownMap">The shown map's runtime number.</param>
    /// <param name="shown">The shown map's object rows (its snapshot's): any of them can be the attachee or a guard's object.</param>
    /// <param name="maps">The loaded maps' inventory rows (items and holders), for objects of other maps.</param>
    /// <param name="content">The game's scripts; null when they could not be loaded. The host borrows it.</param>
    /// <param name="world">ARC-WORLD-081: the viewer's answers for the world actions; without it the vicinity is empty, a create object is
    /// not applied (the run's effects are refused) and random still draws.</param>
    public AreaUseScriptHost(GameSessionOwner session, int shownMap, IEnumerable<AreaObjectRecord> shown, IReadOnlyCollection<AreaInventoryMap> maps,
        ScriptContent? content, AreaScriptWorld? world = null)
    {
        _world = world;
        _session = session ?? throw new ArgumentNullException(nameof(session));
        ArgumentNullException.ThrowIfNull(shown);
        _maps = maps ?? throw new ArgumentNullException(nameof(maps));
        _content = content;
        _shownMap = shownMap;
        // A mobile id repeated by several rows keeps the first; the clicked row answers for its own (Run).
        foreach (var row in shown)
        {
            _shown.TryAdd(EntityId(row), row);
            if (row.Type == SceneryType && row.WorldX is not null && row.WorldY is not null) _scenery.Add((EntityId(row), row));
            if (row.Type == NpcType && row.WorldX is not null && row.WorldY is not null && _shown[EntityId(row)] == row) _npcs.Add((EntityId(row), row));
        }
    }

    private const int SceneryType = 3;

    /// <summary>
    /// ARC-WORLD-081 (PG3): the shown map's scenery within <see cref="ScrWorld.VicinityReach"/> tiles of the centre on both axes, not
    /// destroyed or switched off, in snapshot order; the PC's place is its live cell. Empty for a centre of another map or without a place.
    /// </summary>
    public IReadOnlyList<ScrObjectRef> SceneryInVicinity(ScrObjectRef centre)
    {
        if (centre.Map != _shownMap || Place(centre) is not { } at) return [];
        return _scenery.Where(s => Math.Abs(s.Row.WorldX!.Value - at.X) <= ScrWorld.VicinityReach && Math.Abs(s.Row.WorldY!.Value - at.Y) <= ScrWorld.VicinityReach
                && _world?.Gone(s.Row) != true)
            .Select(s => new ScrObjectRef(_shownMap, s.Entity)).ToArray();
    }

    /// <summary>
    /// ARC-SCRIPT-047 (TG4): the shown map's PCs and NPCs within <see cref="ScrWorld.VicinityReach"/> tiles of the centre on both axes, not
    /// destroyed or switched off, the centre itself and the dead included: the NPC rows in snapshot order at their live cells
    /// (<see cref="AreaScriptWorld.CritterCell"/>, else the row's), then the PC at its live cell (the original's list order is not read).
    /// Empty for a centre of another map or without a place.
    /// </summary>
    public IReadOnlyList<ScrObjectRef> CrittersInVicinity(ScrObjectRef centre)
    {
        if (centre.Map != _shownMap || Place(centre) is not { } at) return [];
        static bool Near(CellCoord cell, CellCoord at) => Math.Abs((long)cell.X - at.X) <= ScrWorld.VicinityReach && Math.Abs((long)cell.Y - at.Y) <= ScrWorld.VicinityReach;
        // Off in the play mask (0x5E2F88 = 3): destroyed or switched off in the map data (object flags 0x1, 0x2) or gone in the session.
        var list = _npcs.Where(n => ((Number(n.Row, ObjectFlagsSlot) ?? 0) & (DestroyedObjectFlag | OffObjectFlag)) == 0 && _world?.Gone(n.Row) != true && Place(new ScrObjectRef(_shownMap, n.Entity)) is { } cell && Near(cell, at))
            .Select(n => new ScrObjectRef(_shownMap, n.Entity)).ToList();
        if (Place(Pc) is { } pc && Near(pc, at)) list.Add(Pc);
        return list;
    }

    /// <summary>
    /// ARC-SCRIPT-050 (GA2, GA3): what get area stores for any object of a run on the shown map, the current map of the original: the shown
    /// map's map-list area when it is not 0, else 0 unless the shown map is the start map, where the original places the object by its
    /// location (the nearest area whose town map is the object's sector's, 0x4CB630; ARC-DEV-178, <see cref="AreaScriptWorld.StartMapAreaAt"/>).
    /// Null (the dispatch ends as not modelled) when the world gives no map-list row (outside map mode), and on the start map when the object has
    /// no place here or the world cannot answer for its sector.
    /// </summary>
    public int? AreaOf(ScrObjectRef obj) => _world?.MapArea?.Invoke() is not { } map ? null
        : map.Area != 0 ? map.Area
        : !map.StartMap ? 0
        : Place(obj) is { } cell ? _world.StartMapAreaAt?.Invoke(cell) : null;

    /// <summary>ARC-DEV-157 (GS1, GS3): the PC's stat by the original's number from the session hero (<see cref="GameSessionOwner.QueryHeroScriptStat"/>);
    /// null (the dispatch ends as not modelled) for an NPC or any other object, whose stats this host does not read, and for a stat the session does
    /// not hold.</summary>
    public int? EffectiveStat(ScrObjectRef obj, int stat) => IsPc(obj) ? _session.QueryHeroScriptStat(stat) : null;

    /// <summary>ARC-DEV-157 (GS2): the PC's dumb background (55, 74, 75); false for any other object.</summary>
    public bool IsDumbByBackground(ScrObjectRef obj) => IsPc(obj) && _session.QueryHeroDumbBackground();

    /// <summary>ARC-WORLD-081 (PG5): a draw from the session's generator, labelled <see cref="RandomLabel"/>.</summary>
    public int Random(int low, int high)
    {
        var roll = _session.Roll(RandomLabel, low, high);
        return roll.Roll?.Value ?? throw new InvalidOperationException($"AREA_USE_SCRIPT_RANDOM_REFUSED {roll.Failure}");
    }

    /// <summary>ARC-SCRIPT-049 (HW3): an object's tile as the vicinity places it (the PC's and an NPC's live cell, a row's world cell); null
    /// without one.</summary>
    public (long X, long Y)? Location(ScrObjectRef obj) => Place(obj) is { } cell ? (cell.X, cell.Y) : null;

    /// <summary>ARC-SCRIPT-049 (HW1): the PC's and an NPC's death as the session holds it (an NPC: its map data or a kill); null for any
    /// other object (an object's hit points are not read here) and for an object of no loaded row.</summary>
    public bool? IsDead(ScrObjectRef obj)
    {
        if (IsPc(obj)) return _session.QueryHeroLife()?.Status.IsDead ?? false;
        return Row(obj) is { Type: NpcType } ? _session.QueryNpcDead(obj.Code) : null;
    }

    /// <summary>ARC-SCRIPT-049 (HW2): the "off" object flag of the row's map data, or for an NPC of the shown map a script's toggle state
    /// (<see cref="OffProperty"/>); the PC is never off; null for an object of no loaded row.</summary>
    public bool? IsSwitchedOff(ScrObjectRef obj)
    {
        if (IsPc(obj)) return false;
        if (Row(obj) is not { } row) return null;
        return ((Number(row, ObjectFlagsSlot) ?? 0) & OffObjectFlag) != 0 || row.Type == NpcType && obj.Map == _shownMap && SwitchedOff(_session, obj.Map, obj.Code);
    }

    // An object's tile: the PC's live cell (the session's placement without a world), an NPC's live cell (ARC-SCRIPT-047), a row's world cell.
    private CellCoord? Place(ScrObjectRef obj)
    {
        if (IsPc(obj)) return _world?.PcCell() ?? _session.QueryActor().Cell;
        if (obj.Map == _shownMap && _world?.CritterCell?.Invoke(obj) is { } live) return live;
        return Row(obj) is { WorldX: { } x, WorldY: { } y } && x is >= int.MinValue and <= int.MaxValue && y is >= int.MinValue and <= int.MaxValue
            ? new CellCoord((int)x, (int)y) : null;
    }

    public ScrObjectRef Pc => new(_session.QueryActor().Map.RuntimeMap, AreaScriptHost.PcCode);

    /// <summary>ARC-SCRIPT-050: the shown map's runtime number (the dialog host borrows this host's answers only for its own map).</summary>
    public int ShownMap => _shownMap;

    private AreaObjectRecord? Row(ScrObjectRef obj)
    {
        if (obj.Map == _shownMap && _clicked is { } clicked && clicked.Entity == obj.Code) return clicked.Row;
        if (obj.Map == _shownMap && _shown.TryGetValue(obj.Code, out var shown)) return shown;
        return _maps.FirstOrDefault(map => map.RuntimeMap == obj.Map) is { } map && map.TryGetRow(obj.Code, out var row) ? row : null;
    }

    /// <summary>The slot an object's row loaded for the point, or null.</summary>
    public static ScrSlot? LoadedSlot(AreaObjectRecord row, int point)
    {
        ArgumentNullException.ThrowIfNull(row);
        var element = row.EffectiveFields.SingleOrDefault(field => field.Slot == 31)?.Elements.SingleOrDefault(e => e.Key == point);
        if (element is not { Words.Count: 3 } || element.Words[2] is < 0 or > ScrCatalogue.MaxScriptNumber || element.Words[0] is < 0 or > uint.MaxValue
            || element.Words[1] is < 0 or > uint.MaxValue) return null;
        return new((int)element.Words[2], (uint)element.Words[0], (uint)element.Words[1]);
    }

    /// <summary>The slot of the object at the point. The use slot is the loaded one under the session's map changes, and since
    /// ARC-WORLD-110 its script is 0 once the trap state removed the trap it names (docs/world/trap-state-runtime.md).</summary>
    public ScrSlot? Slot(ScrObjectRef attachee, int point)
    {
        if (Row(attachee) is not { } row || LoadedSlot(row, point) is not { } loaded) return null;
        if (!KeptAsMapChanges(point)) return loaded;
        long? Read(string property) => _session.QueryObjectChange(new SessionMapId(attachee.Map), MapChangeKey.ForEntity(attachee.Code, property));
        var slot = new ScrSlot(Read(SlotProperty(point, ScriptPart)) is { } script ? checked((int)script) : loaded.Script,
            Read(SlotProperty(point, LocalFlagsPart)) is { } flags ? checked((uint)flags) : loaded.LocalFlags,
            Read(SlotProperty(point, CountersPart)) is { } counters ? checked((uint)counters) : loaded.Counters);
        return point == ScrPoint.Use && TrapStateOf(row, attachee.Code, slot) is { Removed: true } ? slot with { Script = 0 } : slot;
    }

    // The points whose slot a run writes back as map changes (SlotProperty).
    private static bool KeptAsMapChanges(int point) => point is ScrPoint.Use or ScrPoint.Dialog or CatchingThiefPoint or ScrPoint.FirstHeartbeat or ScrPoint.Heartbeat or ScrPoint.TakingDamage;

    // The trap's view of the use slot: the loaded one under the session's script and counters; null without a use slot or for a stored
    // script number outside the catalogue's range (it names no script the content holds).
    private ScrSlot? TrapSlot(AreaObjectRecord row, string entity)
    {
        if (LoadedSlot(row, ScrPoint.Use) is not { } loaded) return null;
        long? Read(string part) => _session.QueryObjectChange(new SessionMapId(row.Key.RuntimeMap), MapChangeKey.ForEntity(entity, SlotProperty(ScrPoint.Use, part)));
        var script = Read(ScriptPart) ?? loaded.Script;
        if (script is < 0 or > ScrCatalogue.MaxScriptNumber) return null;
        return new((int)script, loaded.LocalFlags, Read(CountersPart) is { } counters ? unchecked((uint)counters) : loaded.Counters);
    }

    /// <summary>ARC-WORLD-110: a trap's session object: the row's map and its entity (<see cref="EntityId"/>).</summary>
    public static SessionTrapped Trapped(AreaObjectRecord row) => new(new SessionMapId((row ?? throw new ArgumentNullException(nameof(row))).Key.RuntimeMap), EntityId(row));

    /// <summary>
    /// ARC-WORLD-110: the trap baseline of a row (<see cref="SessionTrapBaseline"/>): a trap object (type 17) or another object, its use
    /// slot as the session's map changes hold it (the script a use wrote back, before the trap state's removal), the script file's header
    /// flags (0 for a script the content does not hold) and the row's object flags (slot 18). Null without script content, without a
    /// use slot, or for a stored number outside the catalogue's range.
    /// </summary>
    public SessionTrapBaseline? TrapBaseline(AreaObjectRecord row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return TrapSlot(row, EntityId(row)) is { } slot ? Baseline(row, slot) : null;
    }

    private SessionTrapBaseline? Baseline(AreaObjectRecord row, ScrSlot slot)
    {
        if (_content is null || slot.Script is < 0 or > ScrCatalogue.MaxScriptNumber) return null;
        var header = slot.Script != 0 && Script(slot.Script) is { Status: ScrScriptStatus.Found, Program: { } program } ? program.FileFlags : 0;
        return new(row.Type == TrapObjectType ? Arcanum.Simulation.Traps.TrapCarrier.TrapObject : Arcanum.Simulation.Traps.TrapCarrier.TrappedObject,
            slot.Script, header, unchecked((int)slot.Counters), unchecked((int)(Number(row, ObjectFlagsSlot) ?? 0)));
    }

    /// <summary>ARC-WORLD-110: the row's trap state now (<see cref="GameSessionOwner.QueryTrap"/> over <see cref="TrapBaseline"/>), or
    /// null without a baseline.</summary>
    public SessionTrapState? TrapState(AreaObjectRecord row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var entity = EntityId(row);
        return TrapSlot(row, entity) is { } slot ? TrapStateOf(row, entity, slot) : null;
    }

    // The trap state over the written slot; without a recorded trap state the baseline alone (no session call).
    private SessionTrapState? TrapStateOf(AreaObjectRecord row, string entity, ScrSlot slot)
    {
        if (Baseline(row, slot) is not { } baseline) return null;
        var map = new SessionMapId(row.Key.RuntimeMap);
        if (_session.QueryMapChange(map, MapChangeKey.ForEntity(entity, GameSessionOwner.TrapStateProperty)) is null)
            return new(Arcanum.Simulation.Traps.TrapRules.Classify(baseline.Script, baseline.HeaderFlags), baseline.Script, baseline.ObjectFlags, false);
        return _session.QueryTrap(new(map, entity), baseline);
    }

    private const int TrapObjectType = 17, ObjectFlagsSlot = 18;

    /// <summary>
    /// ARC-COMBAT-011: whether the object's use script is a trap (0x4BBDD0, ledger OD6): the script of its point-1 slot as the
    /// session holds it, when it is not 0 and its file's header flags carry 0x1 (non-magical) or 0x2 (magical); a script the
    /// content does not hold or cannot read is none, as the original's header read fails. Since ARC-WORLD-110 a trap the trap state
    /// removed is none. Null without script content (unknown).
    /// </summary>
    public bool? IsTrap(AreaObjectRecord row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (_content is null) return null;
        // A stored number outside the catalogue's range names no script the content holds, so it is no trap, like a script the
        // original cannot load.
        return TrapState(row) is { Trapped: true };
    }

    public ScrScriptLookup Script(int number) => AreaScriptHost.Lookup(_content, number);

    public bool GlobalFlag(int index)
    {
        var flags = _session.Flags;
        var read = flags.Read(new GlobalFlagReadRequest(new GlobalFlagRequestId(Guid.NewGuid()), flags.Version, new FlagId(index)));
        return read.Status == GlobalFlagReadStatus.Known && read.Value is { } value ? value : throw new InvalidOperationException("AREA_USE_SCRIPT_FLAG_UNREADABLE");
    }

    /// <summary>ARC-SCRIPT-019: the session's story-state number.</summary>
    public int StoryState() => _session.QueryStoryState();

    /// <summary>ARC-DEV-100 (GV1–GV4): the session's global variables and the hero's variables and flags, which the sets of this host admit
    /// (the use, tile, sector and heartbeat runs). A session without a hero reads 0 for the PC's.</summary>
    public int GlobalVariable(int index) => _session.QueryGlobalVariable(index);
    public int PcVariable(int index) => _session.QueryPcVariable(index);
    public bool PcFlag(int index) => _session.QueryPcFlag(index);
    public bool IsMonsterGenerator(ScrObjectRef attachee) => false;
    // ARC-SCRIPT-047: the dialog gates of a call-ex of an NPC's dialog point (E6): dead as the session holds it (map data or killed);
    // surrender and mind control are not modelled here (normal).
    public ScrDialogState DialogState(ScrObjectRef attachee) =>
        !IsPc(attachee) && attachee.Map == _shownMap && Row(attachee) is { Type: NpcType } && _session.QueryNpcDead(attachee.Code) ? ScrDialogState.Dead : ScrDialogState.Normal;
    public ScrTrapCheck CheckTrapBeforeUse(ScrObjectRef triggerer, ScrObjectRef attachee) => ScrTrapCheck.NoTrap;
    public bool IsTrapObject(ScrObjectRef attachee) => false;
    public ScrObjectRef? Player() => Pc;
    public bool IsPc(ScrObjectRef critter) => critter.Code == AreaScriptHost.PcCode;
    public ScrObjectRef? Leader(ScrObjectRef critter) => !IsPc(critter) && _session.QueryCompanion(critter.Code).LedByPc ? Pc : null;
    public bool IsJilted(ScrObjectRef critter) => !IsPc(critter) && _session.QueryCompanion(critter.Code).Jilted;

    // SG1: the PC's own entry (botched reads as 6); any other object the quest's global state. A number outside the quest
    // log's range (1000..1999) names no quest the session holds and reads 0; no installed script asks one (the real-data check).
    public int QuestState(ScrObjectRef obj, int quest)
    {
        if (!SessionStoryLimits.IsQuest(quest)) return 0;
        if (!IsPc(obj)) return (int)_session.QueryGlobalQuest(quest);
        return _session.QueryQuest(quest) is { } entry ? entry.Botched ? (int)SessionQuestState.Botched : (int)entry.State : 0;
    }

    // SG2: a direct item of the object's current list (the hero: kit and carried items; a holder: its list now).
    public bool HoldsItemNamed(ScrObjectRef obj, int name) => Items(obj).Any(item => item.Name == name);

    // SG3: the object's own name number (field 22) and spell flags (field 20) of any type, from its row's effective fields.
    // The PC has no row: its name number is not modelled and reads 0, its spell flags none.
    public int NameOf(ScrObjectRef obj) => Row(obj) is { } row ? NameOf(row) : 0;

    public bool IsMindControlled(ScrObjectRef obj) => Row(obj) is { } row && IsMindControlled(row);

    /// <summary>SG3 (and the dialog's PA condition, ARC-DEV-227): a row's own name number (field 22), 0 when it holds none.</summary>
    public static int NameOf(AreaObjectRecord row) => Number(row, NameSlot) is { } name ? unchecked((int)name) : 0;

    /// <summary>SG3 (and the dialog's PA condition, ARC-DEV-227): whether a row's own spell flags (field 20) carry the mind-controlled bit.</summary>
    public static bool IsMindControlled(AreaObjectRecord row) => Number(row, SpellFlagsSlot) is { } flags && (unchecked((uint)flags) & MindControlledFlag) != 0;

    private const int SpellFlagsSlot = 19, NameSlot = 21;

    private static long? Number(AreaObjectRecord row, int slot) =>
        row.EffectiveFields.FirstOrDefault(field => field.Slot == slot) is { Presence: AreaPresence.Value, Number: { } n } && n is >= int.MinValue and <= uint.MaxValue ? n : null;

    // SG4: the item in that inventory location (the hero's worn slots; a holder's items by their location).
    public int? WieldedItemName(ScrObjectRef obj, int location) => Items(obj).FirstOrDefault(item => item.Location == location)?.Name;

    /// <summary>SG5: only a PC has reputations; the session PC's (ARC-WORLD-044).</summary>
    public bool HasReputation(ScrObjectRef obj, int reputation) => IsPc(obj) && _session.HeroHasReputation(reputation);

    /// <summary>OV3: a row's type field; the PC (no row) is type 15.</summary>
    public int? ObjectType(ScrObjectRef obj) => IsPc(obj) ? PcType : Row(obj)?.Type;

    private const int PcType = 15;

    private sealed record HeldItem(int Name, int? Location);

    private IEnumerable<HeldItem> Items(ScrObjectRef obj)
    {
        if (IsPc(obj))
        {
            var worn = _session.QueryEquipment().Worn.ToDictionary(item => item.Entity, item => (int)item.Slot, StringComparer.Ordinal);
            int? Location(string entity) => worn.TryGetValue(entity, out var slot) ? slot : null;
            return _session.QueryHeroKit().Select(item => new HeldItem(item.Kind.Name, Location(item.Entity)))
                .Concat(_session.QueryPcInventory().Select(item => new HeldItem(item.Kind.Name, Location(item.Entity)))).ToArray();
        }
        var holder = _session.QueryMapInventory(new SessionMapId(obj.Map))?.Holders.FirstOrDefault(h => h.Entity == obj.Code);
        return holder is null ? [] : holder.Items.Select(item => new HeldItem(item.Facts.Kind.Name, item.Facts.InventoryLocation)).ToArray();
    }

    /// <summary>
    /// Runs the object's use script with the PC as the triggerer and applies its effects: the global flags, the deferred
    /// calls, then the object's use slot as map changes (a value equal to the loaded one clears its key). The first teleport of
    /// the player is returned for the caller's transfer; <paramref name="teleportRefusal"/> names why the caller cannot take
    /// it (or null), and then the run is not applied. Another object's teleport is not modelled and leaves the run unapplied.
    /// Effects apply as the run goes: a step the session refuses leaves the earlier ones applied, as for a dialog script.
    /// Printed lines are read for the caller to show; effect sounds are not played here.
    /// </summary>
    public AreaScriptedUse Run(AreaObjectRecord row, ScrRuleProfile rules, Func<ScrTeleport, string?>? teleportRefusal = null)
    {
        var used = RunUse(row, rules, teleportRefusal, out var trap);
        return trap is null ? used : used with { Trap = trap };
    }

    /// <summary>
    /// ARC-WORLD-110: the trap meeting of a use (<see cref="AreaTraps.Use"/>), asked by <see cref="Run"/> before the use script: null
    /// lets the use go on as before (the object carries no trap, or no trap host is set).
    /// </summary>
    public Func<AreaObjectRecord, AreaTrapMeeting?>? Traps { get; set; }

    private AreaScriptedUse RunUse(AreaObjectRecord row, ScrRuleProfile rules, Func<ScrTeleport, string?>? teleportRefusal, out AreaTrapMeeting? trap)
    {
        trap = null;
        ArgumentNullException.ThrowIfNull(row); ArgumentNullException.ThrowIfNull(rules);
        if (_content is null) return new(AreaScriptedUseOutcome.ContentUnavailable, null, [], null, "no script content");
        if (!_content.VerifySeen()) return new(AreaScriptedUseOutcome.ContentUnavailable, null, [], null, "a script or dialog file changed since it was first read");
        var entity = EntityId(row);
        if (row.Key.RuntimeMap != _shownMap) return new(AreaScriptedUseOutcome.Refused, null, [], null, "the object is not on the shown map");
        // The object entry point's spot and trap run (E2, E4) come first; a trap spotted now or a native run fired ends the use (skip
        // default), and an ordinary trap script runs below with the spot decided.
        trap = Traps?.Invoke(row);
        if (trap is { Cancels: true }) return new(AreaScriptedUseOutcome.NoTeleport, null, [], null, "trap: " + trap.Describe());
        // The clicked row answers for its entity during this run, whichever copy of a repeated id the index kept.
        _clicked = (entity, row);
        try
        {
            var attachee = new ScrObjectRef(_shownMap, entity);
            var run = ScrDispatcher.RunObject(new(ScrPoint.Use, attachee, Pc, null), this, new() { Rules = rules, Admission = Admission });
            return Finish(run, teleportRefusal, writeback => WriteObjectSlot(writeback, attachee));
        }
        finally { _clicked = null; }
    }

    /// <summary>
    /// ARC-SKILLS-010: an NPC's catching-thief script (point 11, PP7) after a noticed pick-pocket failure, with the PC (the thief) as the
    /// triggerer (ARC-DEV-246: or <paramref name="thief"/>, an NPC thief of the shown map) and no extra object; its slot is kept as map changes (<see cref="SlotProperty"/>). A teleport is not modelled from it and
    /// leaves the run unapplied. The caller reads its outcome: anything but "run default" stops the theft's consequences.
    /// </summary>
    public AreaScriptedUse RunCatchingThief(AreaObjectRecord row, ScrRuleProfile rules, ScrObjectRef? thief = null)
    {
        ArgumentNullException.ThrowIfNull(row); ArgumentNullException.ThrowIfNull(rules);
        if (_content is null) return new(AreaScriptedUseOutcome.ContentUnavailable, null, [], null, "no script content");
        if (!_content.VerifySeen()) return new(AreaScriptedUseOutcome.ContentUnavailable, null, [], null, "a script or dialog file changed since it was first read");
        var entity = EntityId(row);
        if (row.Key.RuntimeMap != _shownMap) return new(AreaScriptedUseOutcome.Refused, null, [], null, "the object is not on the shown map");
        _clicked = (entity, row);
        try
        {
            var attachee = new ScrObjectRef(_shownMap, entity);
            var run = ScrDispatcher.RunObject(new(CatchingThiefPoint, attachee, thief ?? Pc, null), this, new() { Rules = rules, Admission = Admission });
            return Finish(run, _ => "a teleport from a catching-thief script is not modelled", writeback => WriteObjectSlot(writeback, attachee));
        }
        finally { _clicked = null; }
    }

    /// <summary>
    /// ARC-WORLD-114: an NPC's first-heartbeat (10) or heartbeat (19) script, which the AI tick runs with the NPC as the attachee and
    /// the triggerer (0x4AD200), under <see cref="HeartbeatAdmission"/>; its slot is kept as map changes (<see cref="SlotProperty"/>).
    /// Its toggle state switches an NPC of the shown map off and back on (<see cref="OffProperty"/>), its kill goes to the world's
    /// <see cref="AreaScriptWorld.Kill"/>; either for another object, or switching on an NPC its map data has off, leaves the run
    /// unapplied. The caller reads the outcome: anything but "run default" stops the tick's pass.
    /// </summary>
    public AreaScriptedUse RunHeartbeat(AreaObjectRecord row, int point, ScrRuleProfile rules)
    {
        ArgumentNullException.ThrowIfNull(row); ArgumentNullException.ThrowIfNull(rules);
        if (point is not (ScrPoint.FirstHeartbeat or ScrPoint.Heartbeat)) throw new ArgumentOutOfRangeException(nameof(point), "Not a heartbeat point.");
        if (_content is null) return new(AreaScriptedUseOutcome.ContentUnavailable, null, [], null, "no script content");
        // A tick runs often: as for a step, only the files this run reads are checked (after the run, in Finish).
        var entity = EntityId(row);
        if (row.Key.RuntimeMap != _shownMap || row.Type != NpcType) return new(AreaScriptedUseOutcome.Refused, null, [], null, "not an NPC of the shown map");
        _clicked = (entity, row);
        try
        {
            var npc = new ScrObjectRef(_shownMap, entity);
            var run = ScrDispatcher.RunObject(new(point, npc, npc, null), this, new() { Rules = rules, Admission = HeartbeatAdmission });
            return Finish(run, _ => "a teleport from a heartbeat script is not modelled", writeback => WriteObjectSlot(writeback, npc));
        }
        finally { _clicked = null; }
    }

    // ARC-DEV-254 (GT198): the original's guard (global 0x5FC26C) is set around the point-0x17 script call only; a damage dealt while it runs skips the script.
    private bool _inTakingDamage;

    /// <summary>
    /// ARC-DEV-254 (MIG13 S14j, GT198–GT203): the taking-damage script (attachment point 0x17, 23) of <paramref name="victim"/>, an object of the shown map (any type: the original runs it for portals, containers, scenery and items too), run by the
    /// damage apply 0x4B4390 before the destroyed, invulnerable and wall gates and before the resistance step. The triggerer is the damage's attacker (<paramref name="attacker"/>, null for a damage without one: a script's own damage,
    /// the null handle), the extra object none, line 0, under <see cref="HeartbeatAdmission"/>; its slot is kept as map changes (<see cref="SlotProperty"/>). A nested damage dealt while the script runs (the guard, which brackets the script
    /// call only) skips it: no run. The caller reads <see cref="TakingDamageVetoes"/>. An object that is not a row of the shown map, or has no slot at the point, has no script (the step goes on).
    /// </summary>
    public AreaScriptedUse RunTakingDamage(string victim, ScrObjectRef? attacker, ScrRuleProfile rules)
    {
        ArgumentNullException.ThrowIfNull(victim); ArgumentNullException.ThrowIfNull(rules);
        if (_inTakingDamage) return new(AreaScriptedUseOutcome.NoTeleport, null, [], null, "nested damage: the guard skips the script");
        var attachee = new ScrObjectRef(_shownMap, victim);
        if (Row(attachee) is not { } row || Slot(attachee, ScrPoint.TakingDamage) is not { Script: > 0 }) return new(AreaScriptedUseOutcome.NoTeleport, null, [], null, "no script at the point");
        if (_content is null) return new(AreaScriptedUseOutcome.ContentUnavailable, null, [], null, "no script content");
        var previous = _clicked;
        _clicked = (victim, row);
        _inTakingDamage = true;
        try
        {
            var run = ScrDispatcher.RunObject(new(ScrPoint.TakingDamage, attachee, attacker, null), this, new() { Rules = rules, Admission = HeartbeatAdmission });
            return Finish(run, _ => "a teleport from a taking-damage script is not modelled", writeback => WriteObjectSlot(writeback, attachee));
        }
        finally { _inTakingDamage = false; _clicked = previous; }
    }

    /// <summary>
    /// ARC-DEV-254 (GT199): whether a taking-damage run (<see cref="RunTakingDamage"/>) cancels the damage: the dispatcher's "skip default" (return 0 of 0x441980; a script that runs past its last line ends the same way). A script number whose file the content lacks, a trap script and a script that runs past its last line skip the default, as
    /// the original does (GT199). A run that was not applied (refused by the admission, effects the viewer cannot apply, an unreadable or changed file, a requested teleport) cancels nothing: the damage goes on. A dialog the script asks for is not started.
    /// </summary>
    public static bool TakingDamageVetoes(AreaScriptedUse ran)
    {
        ArgumentNullException.ThrowIfNull(ran);
        return ran.Outcome is AreaScriptedUseOutcome.NoTeleport && ran.Run is { } run && run.Outcome == ScrOutcome.SkipDefault;
    }

    /// <summary>
    /// ARC-SCRIPT-020: an item's use script (point 1, item use 0x462CC0; ledger IU rows) with the PC as the user and no target, before
    /// the use goes on: the run's outcome decides (run default lets the use go on, skip default stops it). The item is a row of a
    /// loaded map's inventory (<see cref="AreaInventoryMap"/>), named by its origin map and entity; its use slot is kept as map
    /// changes there. A teleport is not modelled from an item's use and leaves the run unapplied.
    /// </summary>
    public AreaScriptedUse RunItemUse(int originMap, string entity, ScrRuleProfile rules)
    {
        ArgumentNullException.ThrowIfNull(entity); ArgumentNullException.ThrowIfNull(rules);
        if (_content is null) return new(AreaScriptedUseOutcome.ContentUnavailable, null, [], null, "no script content");
        if (!_content.VerifySeen()) return new(AreaScriptedUseOutcome.ContentUnavailable, null, [], null, "a script or dialog file changed since it was first read");
        var item = new ScrObjectRef(originMap, entity);
        if (Row(item) is null) return new(AreaScriptedUseOutcome.Refused, null, [], null, "the item is not a row of a loaded map");
        var run = ScrDispatcher.RunObject(new(ScrPoint.Use, item, Pc, null), this, new() { Rules = rules, Admission = Admission });
        return Finish(run, _ => "a teleport from an item's use is not modelled", writeback => WriteObjectSlot(writeback, item));
    }

    /// <summary>Map-change keys of a tile or sector script's slot, on its sector: <c>tile.N.script</c>, <c>tile.N.lf</c>,
    /// <c>tile.N.counters</c> for the tile index N, <c>sector.script</c>, <c>sector.lf</c>, <c>sector.counters</c>.</summary>
    public static string PlacedProperty(MapPlacedScript placed, string part)
    {
        ArgumentNullException.ThrowIfNull(placed);
        return placed.Trigger == MapTransitionTrigger.TileEntry ? $"tile.{placed.Tile}.{part}" : $"sector.{part}";
    }

    public const string PlacedScriptPart = "script", PlacedLocalFlagsPart = "lf", PlacedCountersPart = "counters";

    /// <summary>A tile or sector script's slot now: the sector file's attachment, overridden by the session's map changes on
    /// its sector (<see cref="PlacedProperty"/>) of the shown map.</summary>
    public ScrSlot PlacedSlot(MapPlacedScript placed)
    {
        ArgumentNullException.ThrowIfNull(placed);
        long? Read(string part) => _session.QueryMapChange(new SessionMapId(_shownMap), MapChangeKey.ForSector(placed.Sector.Id, PlacedProperty(placed, part)));
        return new(Read(PlacedScriptPart) is { } script ? checked((int)script) : placed.Script,
            Read(PlacedLocalFlagsPart) is { } flags ? checked((uint)flags) : placed.LocalFlags,
            Read(PlacedCountersPart) is { } counters ? checked((uint)counters) : placed.Counters);
    }

    /// <summary>
    /// ARC-SCRIPT-017: runs a tile script (the PC entered its tile) or a sector script (the PC entered its sector) of the
    /// shown map, from line 0 as a use with the PC as the triggerer and no attachee, and applies its effects as
    /// <see cref="Run"/> does. A changed slot is written back as map changes on its sector; the dispatcher discards a tile
    /// script's changes under the original's rule (ledger X3). ARC-SCRIPT-046: a tile script runs for an NPC of the shown map that
    /// entered its tile as well (<paramref name="triggerer"/>; ledger TR3: for a PC or an NPC), and a dialog action of the run is
    /// handed to the caller (<see cref="AreaScriptedUse.Dialog"/>, set <see cref="PlacedAdmission"/>).
    /// </summary>
    public AreaScriptedUse RunPlaced(MapPlacedScript placed, ScrRuleProfile rules, Func<ScrTeleport, string?>? teleportRefusal = null, ScrObjectRef? triggerer = null)
    {
        ArgumentNullException.ThrowIfNull(placed); ArgumentNullException.ThrowIfNull(rules);
        if (placed.Trigger is not (MapTransitionTrigger.TileEntry or MapTransitionTrigger.SectorEntry)) throw new ArgumentException("Not a tile or sector script.", nameof(placed));
        // The original runs a sector script for the PC only.
        if (triggerer is not null && placed.Trigger != MapTransitionTrigger.TileEntry) throw new ArgumentException("A sector script runs for the PC only.", nameof(triggerer));
        if (_content is null) return new(AreaScriptedUseOutcome.ContentUnavailable, null, [], null, "no script content");
        if (triggerer is { } npc && (npc.Map != _shownMap || Row(npc) is not { Type: NpcType }))
            return new(AreaScriptedUseOutcome.Refused, null, [], null, "the triggerer is not an NPC of the shown map");
        // A step runs often: unlike a click it checks only the files this run reads (the check after the run), not every
        // file seen so far.
        var slot = PlacedSlot(placed);
        var options = new ScrRunOptions { Rules = rules, Admission = PlacedAdmission };
        var run = placed.Trigger == MapTransitionTrigger.TileEntry
            ? ScrDispatcher.RunTile(new(slot, triggerer ?? Pc), this, options)
            : ScrDispatcher.RunSector(new(slot, Pc), this, options);
        var source = placed.Trigger == MapTransitionTrigger.TileEntry ? ScrSource.Tile : ScrSource.Sector;
        return Finish(run, teleportRefusal, writeback => WritePlacedSlot(writeback, placed, source));
    }

    private AreaScriptedUse Finish(ScrRunResult run, Func<ScrTeleport, string?>? teleportRefusal, Func<ScrWriteback, bool> write)
    {
        if (run.Outcome == ScrOutcome.Refused)
            return new(AreaScriptedUseOutcome.Refused, null, [], run, $"script {run.Script} SCR line {run.Refusal!.Line}: {run.Refusal.Code} type {run.Refusal.Type}");
        // ARC-SCRIPT-018: a call that ended the dispatch (an unreadable callee, the call bound) applied nothing: not a run.
        // ARC-DEV-157: likewise a question the host cannot answer (get stat of an NPC or of a stat the session does not hold, GS3), which the dispatch
        // ends as not modelled with nothing applied: reported as refused, not as a silent default.
        if (run.Stop is ScrStop.UnreadableScript or ScrStop.CallDepth or ScrStop.NotModelled)
            return new(AreaScriptedUseOutcome.Refused, null, [], run, $"script {run.Script}: {run.Stop}");
        if (_content!.Changed) return new(AreaScriptedUseOutcome.ContentUnavailable, null, [], run, $"script {run.Script}: a script file changed since it was first read");
        var effects = run.Effects;
        if (effects.Teleports.FirstOrDefault(t => !IsPc(t.Object)) is { } other)
            return new(AreaScriptedUseOutcome.EffectsRejected, null, [], run, $"script {run.Script}: a teleport of {other.Object.Code} is not modelled");
        // A teleport the caller cannot take (a map the mode cannot open, an arrival not admitted) leaves the use undone.
        if (effects.Teleports.FirstOrDefault() is { } asked && teleportRefusal?.Invoke(asked) is { } refusal)
            return new(AreaScriptedUseOutcome.TeleportRefused, asked, [], run, refusal);
        var lines = new List<string>();
        foreach (var line in effects.Lines)
        {
            if (_content.Dialog(line.Script, _session.Rules.Data) is not { Status: ScriptContentStatus.Read, Document: { } document }) continue;
            var printed = ScrPrintedLine.Read(document, line.Line, false, AreaConversationProfile.Diagnostic.Names);
            if (printed.Status == ScrPrintedLineStatus.Shown) lines.Add(printed.Text);
        }
        // ARC-WORLD-081: a create object the viewer cannot apply (no world, a prototype that is no item, an object without a place on the shown
        // map) leaves the whole run unapplied.
        if (Creatable(effects) is { } refusedCreation)
            return new(AreaScriptedUseOutcome.EffectsRejected, null, lines, run, $"script {run.Script}: create object {refusedCreation}");
        // ARC-WORLD-114: likewise a toggle state or kill the viewer cannot apply.
        if (ObjectStateRefusal(effects) is { } refusedState)
            return new(AreaScriptedUseOutcome.EffectsRejected, null, lines, run, $"script {run.Script}: {refusedState}");
        // ARC-SCRIPT-046: a conversation goes to an NPC of the shown map and not with a teleport (not modelled together); such a run
        // applies nothing and counts as refused, so a tile's unconditional teleport still fires as before (ARC-WORLD-031).
        if (effects.Dialog is { } dialog && (dialog.Npc is not { } speaker || speaker.Map != _shownMap || Row(speaker) is not { Type: NpcType } || effects.Teleports.Count > 0))
            return new(AreaScriptedUseOutcome.Refused, null, lines, run, $"script {run.Script}: a conversation of script {dialog.Script} with "
                + (dialog.Npc?.Code ?? "no NPC") + (effects.Teleports.Count > 0 ? " and a teleport" : "") + " is not modelled");
        // ARC-SCRIPT-052: a conversation with an NPC destroyed (before or by this run) is not modelled (the view no longer has him).
        if (effects.Dialog is { Npc: { } talker } && (AlreadyDestroyed(talker)
            || effects.ObjectStates.Any(r => r.Change == ScrObjectStateChange.Destroy && r.Object == talker)))
            return new(AreaScriptedUseOutcome.EffectsRejected, null, lines, run, $"script {run.Script}: a conversation with the destroyed {talker.Code} is not modelled");
        // A slot a callee's dialog stored, or (ARC-SCRIPT-047) a call-ex run changed, is kept as the object's map changes of that point, which
        // need its loaded element; a point the host does not keep, or an object without the element (the original writes its sparse
        // attachment array), leaves the run unapplied, checked before anything applies.
        if (effects.SlotWrites.FirstOrDefault(w => w.Source != ScrSource.Object || !KeptAsMapChanges(w.Point) || w.Attachee is not { } owner
            || Row(owner) is not { } held || LoadedSlot(held, w.Point) is null) is { } unkept)
            return new(AreaScriptedUseOutcome.EffectsRejected, null, lines, run, $"script {run.Script}: the point {unkept.Point} slot of {unkept.Attachee?.Code ?? "no object"} a callee changed is not kept (no loaded element)");
        if (!Apply(effects, write)) return new(AreaScriptedUseOutcome.EffectsRejected, null, lines, run, $"script {run.Script} {run.Stop}: the session refused its effects");
        var teleport = effects.Teleports.FirstOrDefault();
        if (effects.Dialog is { } talk)
            return new(AreaScriptedUseOutcome.NoTeleport, null, lines, run, $"script {run.Script} {run.Stop} at SCR line {run.StopLine}: conversation of script {talk.Script} node {talk.Node}")
            { Dialog = talk };
        return teleport is null
            ? new(AreaScriptedUseOutcome.NoTeleport, null, lines, run, $"script {run.Script} {run.Stop} at SCR line {run.StopLine}: no teleport")
            : new(AreaScriptedUseOutcome.Teleport, teleport, lines, run, $"script {run.Script}: teleport to {teleport.CatalogueMap} ({teleport.X},{teleport.Y})");
    }

    private bool Apply(ScrEffects effects, Func<ScrWriteback, bool> writeSlot)
    {
        if (effects.DestroyTrapObject is not null) return false;
        foreach (var write in effects.GlobalFlags)
        {
            var flags = _session.Flags;
            var receipt = flags.Write(new GlobalFlagWriteRequest(new GlobalFlagRequestId(Guid.NewGuid()), flags.Version, new FlagId(write.Index), write.Value));
            if (receipt.Disposition is not (GlobalFlagWriteDisposition.Applied or GlobalFlagWriteDisposition.NoChange)) return false;
        }
        // ARC-DEV-100: the global variables and the hero's variables and flags.
        if (!_session.ApplyScriptStores(effects)) return false;
        // ARC-SCRIPT-019: the story-state number and the quest, area, rumor and reputation requests, for the PC.
        if (!AreaScriptStory.Apply(_session, effects, IsPc, _awards.Add)) return false;
        // As for a dialog script (AreaScriptHost.Apply): scheme pairs go to the session, applied steps stay applied when a later
        // one is refused (the original applies as it goes); effect sounds are not played by a use click.
        foreach (var sound in effects.Sounds)
            if (sound.Kind == ScrSoundKind.Scheme) _session.PlaySoundScheme(_session.Version, sound.Id, sound.Ambient);
        foreach (var request in effects.Calls)
        {
            var queued = request.Kind == ScrCallKind.Schedule
                ? _session.ScheduleScriptCall(_session.Version, new(request.Script, request.Line, AreaScriptHost.Role(request.Triggerer), AreaScriptHost.Role(request.Attachee)), request.DelayMs)
                : _session.RemoveScriptCall(_session.Version, request.Script, AreaScriptHost.Role(request.Attachee));
            if (queued.Disposition == GameSessionDisposition.Rejected) return false;
        }
        // ARC-UI-049: a reading action shows its document to the PC only; it changes no session state.
        foreach (var request in effects.Readings)
            if (IsPc(request.Viewer)) _readings.Add(request);
        // ARC-WORLD-081: each create object adds its item to the ground next to its object (Creatable checked them); under Steamwoven a patch
        // at the limit takes no more (no change).
        // An item a script creates has its prototype's weight (not saved, so attached before the creations).
        if (effects.Creations.Count > 0)
        {
            _session.AttachItemWeights(prototype => _world!.Item(prototype - AreaPrototypeItems.PrototypeOffset)?.Weight);
            // ARC-DEV-097 (MIG8, SA4): and their prototypes' script arrays, which an item a script creates reads until its first write.
            _session.AttachItemScripts(prototype => _world!.Item(prototype - AreaPrototypeItems.PrototypeOffset)?.Scripts);
        }
        foreach (var request in effects.Creations)
        {
            var item = _world!.Item(request.Prototype)!;
            var result = _session.CreateGroundItem(_session.Version, item.Kind, new SessionMapId(_shownMap), Place(request.Near)!.Value, _world.DropMap()!,
                OwnerOf(request.Near));
            _created.Add(new(request.Prototype, request.Near, result));
            if (result.Disposition == GameSessionDisposition.Rejected) return false;
        }
        // ARC-WORLD-114: toggle state and kill in action order (ObjectStateRefusal checked them); ARC-SCRIPT-052: destroy and stop attacking.
        foreach (var request in effects.ObjectStates)
        {
            if (request.Change == ScrObjectStateChange.Kill) { if (!_world!.Kill!(request.Object)) return false; continue; }
            if (request.Change == ScrObjectStateChange.Destroy)
            {
                // TD2: an NPC already destroyed is left as it is.
                if (AlreadyDestroyed(request.Object)) continue;
                var destroyed = _session.ApplyChanges(_session.Version, null,
                    [new(new SessionMapId(request.Object.Map), MapChangeKey.ForEntity(request.Object.Code, DestroyedProperty), 1)]);
                if (destroyed.Disposition == GameSessionDisposition.Rejected) return false;
                continue;
            }
            if (request.Change == ScrObjectStateChange.StopAttacking) { if (!StopAttacking(request.Object)) return false; continue; }
            if (request.Change == ScrObjectStateChange.PerformAnimation)
            {
                // GT155 has no type test and GT173 makes a non-critter art ignore the action; the PC and other maps are not modelled: such a target is left as it is, the run's other effects apply.
                if (ShownNpc(request.Object)) LastAnimation = _world!.ScriptAnimate!(request.Object.Code, request.Value);
                continue;
            }
            if (request.Change is ScrObjectStateChange.Damage or ScrObjectStateChange.DamageUnresistable)
            {
                // GT194: an NPC of the shown map, the PC, or (ARC-DEV-255, GT195) a door, window, container, scenery or trap of the shown map; any other object is left as it is, and a destroyed
                // object takes none (object flags 0x400001, GT195).
                LastDamage = null;
                if (IsPc(request.Object) || (ShownNpc(request.Object) || ShownDamageable(request.Object)) && !AlreadyDestroyed(request.Object))
                    LastDamage = _world!.ScriptDamage!(IsPc(request.Object) ? Arcanum.Simulation.Ai.AiSchedulerRules.Pc : request.Object.Code, request.Value, request.Value2,
                        request.Change == ScrObjectStateChange.DamageUnresistable);
                // ARC-DEV-261 (GT198): the taking-damage script comes before the destroyed gate, so a destroyed object is asked all the same (its answer cannot change the outcome: it takes none).
                else if (ShownNpc(request.Object) || ShownDamageable(request.Object)) _world!.ScriptDamageAsk?.Invoke(request.Object.Code);
                continue;
            }
            if (request.Change == ScrObjectStateChange.HealHitPoints)
            {
                // GT196: a living PC or NPC of the shown map; any other object does nothing in the original.
                LastHeal = null;
                if (IsPc(request.Object) || ShownNpc(request.Object) && !AlreadyDestroyed(request.Object))
                    LastHeal = _world!.ScriptHeal!(IsPc(request.Object) ? Arcanum.Simulation.Ai.AiSchedulerRules.Pc : request.Object.Code, request.Value);
                continue;
            }
            if (request.Change == ScrObjectStateChange.CastSpell)
            {
                // GT183/GT184: the target an NPC of the shown map or the PC; the caster an NPC of the shown map, the PC (ARC-DEV-244: the hero's casting goal) or none (a missing caster handle, 61 and 136). Another map's
                // object and a non-NPC caster are left as they are.
                LastCast = null;
                var free = (request.Value2 & (int)ScrCastFlags.Free) != 0;
                var unresistable = (request.Value2 & (int)ScrCastFlags.Unresistable) != 0;
                if ((IsPc(request.Object) || ShownNpc(request.Object)) && (request.Other is not { } by || IsPc(by) || ShownNpc(by)))
                    LastCast = _world!.ScriptCast!(request.Other is { } caster ? IsPc(caster) ? Arcanum.Simulation.Ai.AiSchedulerRules.Pc : caster.Code : null, IsPc(request.Object) ? Arcanum.Simulation.Ai.AiSchedulerRules.Pc : request.Object.Code, request.Value, free, unresistable);
                continue;
            }
            if (request.Change == ScrObjectStateChange.BecomeProne)
            {
                // GT187: an NPC of the shown map; the PC and any other object are left as they are (the run's other effects apply).
                if (ShownNpc(request.Object)) LastProne = _world!.ScriptProne!(request.Object.Code);
                continue;
            }
            if (request.Change == ScrObjectStateChange.StealCoins)
            {
                // GT164: a thief that is an NPC of the shown map; the victim an NPC of the shown map or the PC; any other pair is left as it is. The thief's type is not checked in the original.
                if (ShownNpc(request.Object) && request.Other is { } victim && (IsPc(victim) || ShownNpc(victim)))
                    LastSteal = _world!.ScriptSteal!(request.Object.Code, IsPc(victim) ? Arcanum.Simulation.Ai.AiSchedulerRules.Pc : victim.Code);
                continue;
            }
            if (request.Change == ScrObjectStateChange.HealFatigue)
            {
                // GT162: a negative amount raises the damage in the original (not modelled); the session's refusal does not stop the run (GT154).
                if (request.Value > 0 && ShownNpc(request.Object)) _world!.HealFatigue!(request.Object, request.Value);
                continue;
            }
            if (request.Change is ScrObjectStateChange.WalkTo or ScrObjectStateChange.RunTo)
            {
                // GT154: fire and forget. GT158: a negative X gives the original's Y = -1 (an off-map tile) and a negative Y is off the map too: nothing is started (a project choice, the walk's result is never seen).
                if (request.Value >= 0 && request.Value2 >= 0 && ShownNpc(request.Object))
                    LastWalk = _world!.ScriptWalk!(request.Object.Code, new CellCoord(request.Value, request.Value2), request.Change == ScrObjectStateChange.RunTo);
                continue;
            }
            if (request.Change == ScrObjectStateChange.SetFaction)
            {
                // HW4: an NPC only (ObjectStateRefusal checked the map); a faction equal to its map data's clears the key.
                if (IsPc(request.Object) || Row(request.Object) is not { Type: NpcType } npc) continue;
                var loaded = AreaConversationHost.Scope(npc).Faction;
                var set = _session.ApplyChanges(_session.Version, null, [new(new SessionMapId(request.Object.Map),
                    MapChangeKey.ForEntity(request.Object.Code, FactionProperty), request.Value == loaded ? null : request.Value)]);
                if (set.Disposition == GameSessionDisposition.Rejected) return false;
                continue;
            }
            var key = MapChangeKey.ForEntity(request.Object.Code, OffProperty);
            var toggled = _session.ApplyChanges(_session.Version, null,
                [new(new SessionMapId(request.Object.Map), key, SwitchedOff(_session, request.Object.Map, request.Object.Code) ? null : 1)]);
            if (toggled.Disposition == GameSessionDisposition.Rejected) return false;
        }
        // ARC-SCRIPT-046: the slots a called script's dialog action stored (an object's use slot), and (ARC-SCRIPT-047) the slots call-ex
        // runs changed, as map changes.
        foreach (var stored in effects.SlotWrites)
            if (stored.Attachee is not { } owner || !WriteObjectSlot(stored, owner)) return false;
        return effects.Writeback is not { } writeback || writeSlot(writeback);
    }

    /// <summary>ARC-DEV-231: what the AI host answered to the last walk to or run to a run applied (smoke and test seam; null before any).</summary>
    public string? LastWalk { get; private set; }

    /// <summary>ARC-DEV-233: what the AI host answered to the last perform animation a run applied (smoke and test seam; null before any).</summary>
    public string? LastAnimation { get; private set; }

    /// <summary>ARC-DEV-234: what the AI host answered to the last try to steal 100 coins a run applied (smoke and test seam; null before any).</summary>
    public string? LastSteal { get; private set; }

    /// <summary>ARC-DEV-243: what the viewer answered to the last script damage a run applied (smoke and test seam; null before any or when nothing was applied).</summary>
    public string? LastDamage { get; private set; }

    /// <summary>ARC-DEV-243: what the viewer answered to the last script heal of hit points a run applied (smoke and test seam).</summary>
    public string? LastHeal { get; private set; }

    /// <summary>ARC-DEV-241: what the AI host answered to the last script cast a run applied (smoke and test seam; null before any).</summary>
    public string? LastCast { get; private set; }

    /// <summary>ARC-DEV-236: what the AI host answered to the last become prone a run applied (smoke and test seam; null before any).</summary>
    public string? LastProne { get; private set; }

    // ARC-DEV-233: an NPC of the shown map, the only target the goal-starting actions (62, 69, 74, 76) are applied to; the PC, another map's object and a non-NPC are left as they are.
    private bool ShownNpc(ScrObjectRef obj) => obj.Map == _shownMap && !IsPc(obj) && Row(obj) is { Type: NpcType };

    // ARC-DEV-255 (GT195): a portal, container, scenery or trap of the shown map, the objects a script's damage reaches besides the PC and an NPC.
    private bool ShownDamageable(ScrObjectRef obj) => obj.Map == _shownMap && !IsPc(obj) && Row(obj) is { Type: Arcanum.Data.Combat.CombatFieldReader.PortalType or Arcanum.Data.Combat.CombatFieldReader.ContainerType
        or Arcanum.Data.Combat.CombatFieldReader.SceneryType or Arcanum.Data.Combat.CombatFieldReader.TrapType };

    // ARC-SCRIPT-052 (TD2): destroyed in its map data (object flag 0x1) or by a script (DestroyedProperty).
    private bool AlreadyDestroyed(ScrObjectRef obj) =>
        Row(obj) is { } row && ((Number(row, ObjectFlagsSlot) ?? 0) & DestroyedObjectFlag) != 0 || Destroyed(_session, obj.Map, obj.Code);

    // ARC-SCRIPT-052 (TD3): the NPCs a stop attacking of the object reaches (the PC's followers for the PC) with the world's facts for each.
    private Dictionary<string, SessionPeaceFacts> StopFacts(ScrObjectRef obj)
    {
        var npcs = IsPc(obj) ? _session.QueryParty().Followers.Select(f => f.Entity).ToArray() : [obj.Code];
        return npcs.ToDictionary(npc => npc, npc => _world?.PeaceFacts?.Invoke(npc) ?? new SessionPeaceFacts(), StringComparer.Ordinal);
    }

    // The session refuses a stop (InvalidDangerFacts) for an NPC with a focus on the hero's side and no reaction facts, or with a focus while in
    // combat mode and no combat-mode facts: such a run is left unapplied before anything applies.
    private string? StopRefusal(ScrObjectRef obj)
    {
        foreach (var (npc, facts) in StopFacts(obj))
        {
            if (_session.QueryDanger(npc).Focus is not { } focus) continue;
            if ((focus.IsHero || _session.QueryCompanion(focus.Entity!).LedByPc) && facts.Reaction is null)
                return $"stop attacking of {npc}, which fights the hero's side, needs its reaction facts";
            if (_session.QueryCombatMode(SessionCombatant.Critter(npc)) && facts.CombatMode?.Facts is null)
                return $"stop attacking of {npc}, in combat mode, needs its combat-mode facts";
        }
        return null;
    }

    // ARC-SCRIPT-052 (TD3): the session's stop attacking for the PC (each follower) or an NPC, with the world's facts for each NPC it reaches,
    // then the host's part of each step.
    private bool StopAttacking(ScrObjectRef obj)
    {
        var hero = IsPc(obj);
        var facts = StopFacts(obj);
        var stop = _session.StopAttacking(_session.Version, new SessionStopAttackingRequest
            { Combatant = hero ? SessionCombatant.Hero : SessionCombatant.Critter(obj.Code), Facts = facts });
        if (stop.Disposition == GameSessionDisposition.Rejected) return false;
        foreach (var step in stop.Steps) _world?.Stopped?.Invoke(step);
        return true;
    }

    // ARC-WORLD-114: the first toggle state or kill of the run the viewer cannot apply, named; null when every one can be. Both act on an NPC
    // of the shown map only; a kill needs the world's kill, and an NPC its map data has off is never switched on (its T0 body is not
    // drawn at all). ARC-SCRIPT-052: destroy takes an NPC of the shown map out of the world unless it follows the PC (the party removal is not
    // read) or has a destroy-point script (the destroy event is audit slice 4); stop attacking acts on the PC or an NPC of the shown map.
    private string? ObjectStateRefusal(ScrEffects effects)
    {
        foreach (var request in effects.ObjectStates)
        {
            if (request.Change == ScrObjectStateChange.StopAttacking)
            {
                if (!IsPc(request.Object) && (request.Object.Map != _shownMap || Row(request.Object) is not { Type: NpcType }))
                    return $"stop attacking of {request.Object.Code} is not modelled (only the PC or an NPC of the shown map)";
                if (StopRefusal(request.Object) is { } refused) return refused;
                continue;
            }
            if (request.Change == ScrObjectStateChange.Destroy)
            {
                if (request.Object.Map != _shownMap || IsPc(request.Object) || Row(request.Object) is not { Type: NpcType } npc)
                    return $"destroy of {request.Object.Code} is not modelled (only an NPC of the shown map)";
                // TD2: one destroyed in its map data, or by an earlier run, is left as it is.
                if (AlreadyDestroyed(request.Object)) continue;
                if (Leader(request.Object) is not null) return $"destroy of {request.Object.Code}, a follower of the PC, is not modelled";
                if (LoadedSlot(npc, ScrPoint.Destroy) is { Script: not 0 } own) return $"destroy of {request.Object.Code} runs its destroy script {own.Script}, which is not modelled";
                continue;
            }
            if (request.Change == ScrObjectStateChange.PerformAnimation)
            {
                if (_world?.ScriptAnimate is null) return "perform animation needs the AI host";
                continue;
            }
            if (request.Change is ScrObjectStateChange.Damage or ScrObjectStateChange.DamageUnresistable)
            {
                if (_world?.ScriptDamage is null) return "damage needs the viewer's session";
                continue;
            }
            if (request.Change == ScrObjectStateChange.HealHitPoints)
            {
                if (_world?.ScriptHeal is null) return "heal hit points needs the viewer's session";
                continue;
            }
            if (request.Change == ScrObjectStateChange.CastSpell)
            {
                if (_world?.ScriptCast is null) return "cast spell needs the AI host";
                continue;
            }
            if (request.Change == ScrObjectStateChange.BecomeProne)
            {
                if (_world?.ScriptProne is null) return "become prone needs the AI host";
                continue;
            }
            if (request.Change == ScrObjectStateChange.StealCoins)
            {
                if (_world?.ScriptSteal is null) return "steal needs the AI host";
                continue;
            }
            if (request.Change == ScrObjectStateChange.HealFatigue)
            {
                if (_world?.HealFatigue is null) return "heal fatigue needs the session's life step";
                continue;
            }
            if (request.Change is ScrObjectStateChange.WalkTo or ScrObjectStateChange.RunTo)
            {
                // ARC-DEV-231: the walk and the run of an NPC of the shown map that the AI host drives; any other target is left as it is (ShownNpc), the run's other effects apply.
                var walkName = request.Change == ScrObjectStateChange.WalkTo ? "walk to" : "run to";
                if (_world?.ScriptWalk is null) return $"{walkName} needs the AI host";
                continue;
            }
            // ARC-SCRIPT-049 (HW4): set faction changes an NPC and leaves any other object as it is; an NPC of another map is not kept.
            if (request.Change == ScrObjectStateChange.SetFaction)
            {
                // An object of no loaded row may be an NPC as well: not modelled either.
                if (!IsPc(request.Object) && Row(request.Object) is null or { Type: NpcType } && request.Object.Map != _shownMap)
                    return $"set faction of {request.Object.Code} is not modelled (only an NPC of the shown map)";
                if (!IsPc(request.Object) && Row(request.Object) is null)
                    return $"set faction of {request.Object.Code} is not modelled (no loaded row)";
                continue;
            }
            var name = request.Change == ScrObjectStateChange.Kill ? "kill" : "toggle state";
            if (request.Object.Map != _shownMap || IsPc(request.Object) || Row(request.Object) is not { Type: NpcType } row)
                return $"{name} of {request.Object.Code} is not modelled (only an NPC of the shown map)";
            if (request.Change == ScrObjectStateChange.Kill && (_world?.Kill is null || _world.CanKill?.Invoke(request.Object) == false))
                return $"kill of {request.Object.Code} needs its combat values";
            if (request.Change == ScrObjectStateChange.ToggleOff && ((Number(row, ObjectFlagsSlot) ?? 0) & OffObjectFlag) != 0)
                return $"toggle state of {request.Object.Code}, off in its map data, is not modelled";
        }
        return null;
    }

    // The first create object of the run the viewer or the session cannot apply, named; null when every one can be, so a run is never left
    // half applied by a creation.
    private string? Creatable(ScrEffects effects)
    {
        if (effects.Creations.Count == 0) return null;
        if (_world is null || _world.DropMap() is null) return "needs the shown map";
        foreach (var request in effects.Creations)
        {
            if (_world.Item(request.Prototype) is not { } item) return $"{request.Prototype}: no item prototype";
            if (request.Near.Map != _shownMap || Place(request.Near) is not { } near) return $"{request.Prototype}: {request.Near.Code} has no place on the shown map";
            if (_session.QueryCreateGroundItemRefusal(item.Kind, new SessionMapId(_shownMap), near, effects.Creations.Count) is { } refused)
                return $"{request.Prototype}: the session would refuse it ({refused})";
        }
        return null;
    }

    // The walk's owner beside the object a creation names (PG2): the PC and its companions are the PC's side, another NPC an NPC, any other
    // object an object.
    private DropOwner OwnerOf(ScrObjectRef near)
    {
        if (IsPc(near) || Leader(near) is not null) return DropOwner.PcSide;
        return Row(near) is { Type: 15 or 16 } ? DropOwner.Npc : DropOwner.Object;
    }

    private bool WriteObjectSlot(ScrWriteback writeback, ScrObjectRef attachee)
    {
        if (writeback.Source != ScrSource.Object || !KeptAsMapChanges(writeback.Point) || writeback.Attachee != attachee
            || Row(attachee) is not { } row || LoadedSlot(row, writeback.Point) is not { } loaded) return false;
        var map = new SessionMapId(attachee.Map);
        // ARC-DEV-096: a part back at its loaded value is cleared where the object's record sits, which may be another map than the attachee's.
        MapChangeEdit Edit(string property, long value, long baseline)
        {
            var key = MapChangeKey.ForEntity(attachee.Code, property);
            return value == baseline ? new(_session.QueryObjectRecordMap(key) ?? map, key, null) : new(map, key, value);
        }
        var point = writeback.Point;
        var result = _session.ApplyChanges(_session.Version, null,
        [
            Edit(SlotProperty(point, ScriptPart), writeback.Slot.Script, loaded.Script),
            Edit(SlotProperty(point, LocalFlagsPart), writeback.Slot.LocalFlags, loaded.LocalFlags),
            Edit(SlotProperty(point, CountersPart), writeback.Slot.Counters, loaded.Counters),
        ]);
        return result.Disposition != GameSessionDisposition.Rejected;
    }

    private bool WritePlacedSlot(ScrWriteback writeback, MapPlacedScript placed, ScrSource source)
    {
        if (writeback.Source != source || writeback.Attachee is not null || writeback.Point != ScrPoint.Use) return false;
        var map = new SessionMapId(_shownMap);
        MapChangeEdit Edit(string part, long value, long baseline) =>
            new(map, MapChangeKey.ForSector(placed.Sector.Id, PlacedProperty(placed, part)), value == baseline ? null : value);
        var result = _session.ApplyChanges(_session.Version, null,
        [
            Edit(PlacedScriptPart, writeback.Slot.Script, placed.Script),
            Edit(PlacedLocalFlagsPart, writeback.Slot.LocalFlags, placed.LocalFlags),
            Edit(PlacedCountersPart, writeback.Slot.Counters, placed.Counters),
        ]);
        return result.Disposition != GameSessionDisposition.Rejected;
    }
}
