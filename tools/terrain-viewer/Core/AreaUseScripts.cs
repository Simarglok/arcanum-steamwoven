using Arcanum.Data.Area;
using Arcanum.Data.Maps;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-WORLD-081: what the world actions of a use, tile or sector script read from the viewer (set S15): the PC's live cell (the session's
/// actor placement follows the walking actor only at a save or a map change), whether a shown row is destroyed or switched off in the session
/// (the original's object lists skip it), the item a script's prototype number names (<see cref="AreaPrototypeItems.ScriptItem"/>), and the
/// shown map as the create action's placement reads it (null when there is none).
/// </summary>
public sealed record AreaScriptWorld(Func<CellCoord?> PcCell, Func<AreaObjectRecord, bool> Gone, Func<int, AreaPrototypeItem?> Item, Func<IDropMap?> DropMap);

/// <summary>ARC-WORLD-081: one create object a run applied: the prototype number as the script named it, the object it was created next to,
/// and the session's result (<see cref="InventoryMutationResult.Item"/> the new item; none under the Steamwoven limit).</summary>
public sealed record AreaScriptCreation(int Prototype, ScrObjectRef Near, InventoryMutationResult Result);

/// <summary>What a scripted use did: ran with a teleport of the player (<see cref="Teleport"/>), ran without one, was refused
/// by the dispatcher or by the session, found no script content, or asked for a teleport the caller cannot take (then
/// nothing of the run is applied).</summary>
public enum AreaScriptedUseOutcome { Teleport, NoTeleport, Refused, EffectsRejected, ContentUnavailable, TeleportRefused }

/// <summary>A scripted use: the outcome, the first teleport of the player the run asked for, the printed lines the viewer
/// shows, the dispatcher's result and a detail (identifiers and statuses only).</summary>
public sealed record AreaScriptedUse(AreaScriptedUseOutcome Outcome, ScrTeleport? Teleport, IReadOnlyList<string> Lines, ScrRunResult? Run, string Detail);

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
public sealed class AreaUseScriptHost : IScrHost, IScrObjectQueries, IScrWorldQueries
{
    /// <summary>Map-change keys of an object's use slot.</summary>
    public const string LocalFlagsProperty = "use.lf", CountersProperty = "use.counters", ScriptProperty = "use.script";
    /// <summary>ARC-SKILLS-010: the catching-thief point (11) whose slot an NPC's run keeps as map changes too, under <c>scr11.*</c>.</summary>
    public const int CatchingThiefPoint = 11;
    private const string ScriptPart = "script", LocalFlagsPart = "lf", CountersPart = "counters";

    /// <summary>The map-change property of a slot's part at a point the host writes back: <c>use.*</c> for the use point,
    /// <c>scr&lt;point&gt;.*</c> for the catching-thief point.</summary>
    public static string SlotProperty(int point, string part) => point == ScrPoint.Use ? "use." + part : $"scr{point}.{part}";
    /// <summary>The spell flag the original reads as mind-controlled (field 20, bit 27; ledger SG3).</summary>
    public const uint MindControlledFlag = 0x0800_0000;

    /// <summary>
    /// The set a use click runs: <see cref="ScrAdmissionSet.S4"/> (with the session's story-state number since ARC-SCRIPT-019)
    /// without the dialog action (a use click does not open a conversation here), plus the reading actions of
    /// <see cref="ScrAdmissionSet.S10"/> (ARC-UI-049: a plaque, book or picture a use, tile or sector script shows), plus call script,
    /// assign object, get object type and the object variables of <see cref="ScrAdmissionSet.S12"/> (ARC-SCRIPT-018), plus the quest,
    /// area, rumor and reputation actions of <see cref="ScrAdmissionSet.S13"/> (ARC-SCRIPT-019), plus the world actions of
    /// <see cref="ScrAdmissionSet.S15"/> (ARC-WORLD-081): random, loop for and loop end over the scenery in the vicinity, create object and a
    /// counter operand with any index.
    /// </summary>
    public static ScrAdmissionSet Admission { get; } = new("S4-area-use-reading-objects-story-world", ScrAdmissionSet.S4.Conditions,
        ScrAdmissionSet.S4.Actions.Where(a => a is not ScrCatalogue.Action.Dialog)
            .Concat([ScrCatalogue.Action.ShowBook, ScrCatalogue.Action.ShowPicture, ScrCatalogue.Action.ShowPlaque,
                ScrCatalogue.Action.CallScript, ScrCatalogue.Action.AssignObject, ScrCatalogue.Action.GetObjectType,
                ScrCatalogue.Action.SetPcQuestState, ScrCatalogue.Action.MarkMapLocation, ScrCatalogue.Action.SetRumor,
                ScrCatalogue.Action.AddReputation, ScrCatalogue.Action.RemoveReputation,
                ScrCatalogue.Action.Random, ScrCatalogue.Action.LoopFor, ScrCatalogue.Action.LoopEnd, ScrCatalogue.Action.CreateObject]),
        ScrAdmissionSet.S4.ValueKinds,
        ScrAdmissionSet.S4.FocusKinds.Concat([ScrFocusKind.LocalObject, ScrFocusKind.CurrentLoopedObject, ScrFocusKind.EverySceneryInVicinity]),
        counterIndexWraps: true);

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
    // ARC-WORLD-081: the shown map's scenery in snapshot order, for the vicinity.
    private readonly List<(string Entity, AreaObjectRecord Row)> _scenery = [];
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

    /// <summary>ARC-WORLD-081 (PG5): a draw from the session's generator, labelled <see cref="RandomLabel"/>.</summary>
    public int Random(int low, int high)
    {
        var roll = _session.Roll(RandomLabel, low, high);
        return roll.Roll?.Value ?? throw new InvalidOperationException($"AREA_USE_SCRIPT_RANDOM_REFUSED {roll.Failure}");
    }

    // An object's tile: the PC's live cell (the session's placement without a world), a row's world cell.
    private CellCoord? Place(ScrObjectRef obj)
    {
        if (IsPc(obj)) return _world?.PcCell() ?? _session.QueryActor().Cell;
        return Row(obj) is { WorldX: { } x, WorldY: { } y } && x is >= int.MinValue and <= int.MaxValue && y is >= int.MinValue and <= int.MaxValue
            ? new CellCoord((int)x, (int)y) : null;
    }

    public ScrObjectRef Pc => new(_session.QueryActor().Map.RuntimeMap, AreaScriptHost.PcCode);

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

    public ScrSlot? Slot(ScrObjectRef attachee, int point)
    {
        if (Row(attachee) is not { } row || LoadedSlot(row, point) is not { } loaded) return null;
        if (point != ScrPoint.Use && point != CatchingThiefPoint) return loaded;
        long? Read(string property) => _session.QueryMapChange(new SessionMapId(attachee.Map), MapChangeKey.ForEntity(attachee.Code, property));
        return new(Read(SlotProperty(point, ScriptPart)) is { } script ? checked((int)script) : loaded.Script,
            Read(SlotProperty(point, LocalFlagsPart)) is { } flags ? checked((uint)flags) : loaded.LocalFlags,
            Read(SlotProperty(point, CountersPart)) is { } counters ? checked((uint)counters) : loaded.Counters);
    }

    /// <summary>
    /// ARC-COMBAT-011: whether the object's use script is a trap (0x4BBDD0, ledger OD6): the script of its point-1 slot as the
    /// session holds it, when it is not 0 and its file's header flags carry 0x1 (non-magical) or 0x2 (magical); a script the
    /// content does not hold or cannot read is none, as the original's header read fails. Null without script content (unknown).
    /// </summary>
    public bool? IsTrap(AreaObjectRecord row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (_content is null) return null;
        if (LoadedSlot(row, ScrPoint.Use) is not { } loaded) return false;
        // Only the script number is read (a use writes it back under the row's entity); a stored number outside the catalogue's
        // range names no script the content holds, so it is no trap, like a script the original cannot load.
        var written = _session.QueryMapChange(new SessionMapId(row.Key.RuntimeMap), MapChangeKey.ForEntity(EntityId(row), ScriptProperty));
        var script = written is { } number ? (number is >= 0 and <= ScrCatalogue.MaxScriptNumber ? (int)number : 0) : loaded.Script;
        if (script == 0) return false;
        return Script(script) is { Status: ScrScriptStatus.Found, Program: { } program } &&
            (program.FileFlags & (ScrCatalogue.FileFlag.NonmagicalTrap | ScrCatalogue.FileFlag.MagicalTrap)) != 0;
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
    public bool IsMonsterGenerator(ScrObjectRef attachee) => false;
    public ScrDialogState DialogState(ScrObjectRef attachee) => ScrDialogState.Normal;
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
    public int NameOf(ScrObjectRef obj) => Row(obj) is { } row && Number(row, NameSlot) is { } name ? unchecked((int)name) : 0;

    public bool IsMindControlled(ScrObjectRef obj) => Row(obj) is { } row && Number(row, SpellFlagsSlot) is { } flags && (unchecked((uint)flags) & MindControlledFlag) != 0;

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
        ArgumentNullException.ThrowIfNull(row); ArgumentNullException.ThrowIfNull(rules);
        if (_content is null) return new(AreaScriptedUseOutcome.ContentUnavailable, null, [], null, "no script content");
        if (!_content.VerifySeen()) return new(AreaScriptedUseOutcome.ContentUnavailable, null, [], null, "a script or dialog file changed since it was first read");
        var entity = EntityId(row);
        if (row.Key.RuntimeMap != _shownMap) return new(AreaScriptedUseOutcome.Refused, null, [], null, "the object is not on the shown map");
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
    /// triggerer and no extra object; its slot is kept as map changes (<see cref="SlotProperty"/>). A teleport is not modelled from it and
    /// leaves the run unapplied. The caller reads its outcome: anything but "run default" stops the theft's consequences.
    /// </summary>
    public AreaScriptedUse RunCatchingThief(AreaObjectRecord row, ScrRuleProfile rules)
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
            var run = ScrDispatcher.RunObject(new(CatchingThiefPoint, attachee, Pc, null), this, new() { Rules = rules, Admission = Admission });
            return Finish(run, _ => "a teleport from a catching-thief script is not modelled", writeback => WriteObjectSlot(writeback, attachee));
        }
        finally { _clicked = null; }
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
    /// script's changes under the original's rule (ledger X3).
    /// </summary>
    public AreaScriptedUse RunPlaced(MapPlacedScript placed, ScrRuleProfile rules, Func<ScrTeleport, string?>? teleportRefusal = null)
    {
        ArgumentNullException.ThrowIfNull(placed); ArgumentNullException.ThrowIfNull(rules);
        if (placed.Trigger is not (MapTransitionTrigger.TileEntry or MapTransitionTrigger.SectorEntry)) throw new ArgumentException("Not a tile or sector script.", nameof(placed));
        if (_content is null) return new(AreaScriptedUseOutcome.ContentUnavailable, null, [], null, "no script content");
        // A step runs often: unlike a click it checks only the files this run reads (the check after the run), not every
        // file seen so far.
        var slot = PlacedSlot(placed);
        var options = new ScrRunOptions { Rules = rules, Admission = Admission };
        var run = placed.Trigger == MapTransitionTrigger.TileEntry
            ? ScrDispatcher.RunTile(new(slot, Pc), this, options)
            : ScrDispatcher.RunSector(new(slot, Pc), this, options);
        var source = placed.Trigger == MapTransitionTrigger.TileEntry ? ScrSource.Tile : ScrSource.Sector;
        return Finish(run, teleportRefusal, writeback => WritePlacedSlot(writeback, placed, source));
    }

    private AreaScriptedUse Finish(ScrRunResult run, Func<ScrTeleport, string?>? teleportRefusal, Func<ScrWriteback, bool> write)
    {
        if (run.Outcome == ScrOutcome.Refused)
            return new(AreaScriptedUseOutcome.Refused, null, [], run, $"script {run.Script} SCR line {run.Refusal!.Line}: {run.Refusal.Code} type {run.Refusal.Type}");
        // ARC-SCRIPT-018: a call that ended the dispatch (an unreadable callee, the call bound) applied nothing: not a run.
        if (run.Stop is ScrStop.UnreadableScript or ScrStop.CallDepth)
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
        if (!Apply(effects, write)) return new(AreaScriptedUseOutcome.EffectsRejected, null, lines, run, $"script {run.Script} {run.Stop}: the session refused its effects");
        var teleport = effects.Teleports.FirstOrDefault();
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
        if (effects.Creations.Count > 0) _session.AttachItemWeights(prototype => _world!.Item(prototype - AreaPrototypeItems.PrototypeOffset)?.Weight);
        foreach (var request in effects.Creations)
        {
            var item = _world!.Item(request.Prototype)!;
            var result = _session.CreateGroundItem(_session.Version, item.Kind, new SessionMapId(_shownMap), Place(request.Near)!.Value, _world.DropMap()!,
                OwnerOf(request.Near));
            _created.Add(new(request.Prototype, request.Near, result));
            if (result.Disposition == GameSessionDisposition.Rejected) return false;
        }
        return effects.Writeback is not { } writeback || writeSlot(writeback);
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
        if (writeback.Source != ScrSource.Object || writeback.Point is not (ScrPoint.Use or CatchingThiefPoint) || writeback.Attachee != attachee
            || Row(attachee) is not { } row || LoadedSlot(row, writeback.Point) is not { } loaded) return false;
        var map = new SessionMapId(attachee.Map);
        MapChangeEdit Edit(string property, long value, long baseline) => new(map, MapChangeKey.ForEntity(attachee.Code, property), value == baseline ? null : value);
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
