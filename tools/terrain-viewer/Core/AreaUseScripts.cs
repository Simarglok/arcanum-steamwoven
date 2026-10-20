using Arcanum.Data.Area;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Scripting.Scr;
using Arcanum.Session;

namespace Arcanum.TerrainViewer.Core;

/// <summary>What a scripted use did: ran with a teleport of the player (<see cref="Teleport"/>), ran without one, was refused
/// by the dispatcher or by the session, found no script content, or asked for a teleport the caller cannot take (then
/// nothing of the run is applied).</summary>
public enum AreaScriptedUseOutcome { Teleport, NoTeleport, Refused, EffectsRejected, ContentUnavailable, TeleportRefused }

/// <summary>A scripted use: the outcome, the first teleport of the player the run asked for, the printed lines the viewer
/// shows, the dispatcher's result and a detail (identifiers and statuses only).</summary>
public sealed record AreaScriptedUse(AreaScriptedUseOutcome Outcome, ScrTeleport? Teleport, IReadOnlyList<string> Lines, ScrRunResult? Run, string Detail);

/// <summary>
/// ARC-SCRIPT-015 (plan slice S4, viewer part): the AREA viewer's SCR host for a use click on an object whose use script can
/// reach a teleport. The attachee is a T0 object of the shown map (any row of its snapshot, by <see cref="EntityId"/>; an
/// item or holder of another loaded map through its <see cref="AreaInventoryMap"/> rows); its use slot is the row's
/// effective attachment, overridden by the session's map changes <c>use.lf</c>, <c>use.counters</c> and
/// <c>use.script</c>. It answers the S4 guards (<see cref="IScrObjectQueries"/>) from the session: the PC's quest log and the
/// quests' global states, the hero's kit, carried and worn items, the holders' current lists, and from the rows the T0 name and
/// spell flags.
/// Reputations are not modelled: no object has one (the new-game state of the PC). Everything else the viewer does not model
/// answers "no", as for the dialog and item hosts.
/// </summary>
public sealed class AreaUseScriptHost : IScrHost, IScrObjectQueries
{
    /// <summary>Map-change keys of an object's use slot.</summary>
    public const string LocalFlagsProperty = "use.lf", CountersProperty = "use.counters", ScriptProperty = "use.script";
    /// <summary>The spell flag the original reads as mind-controlled (field 20, bit 27; ledger SG3).</summary>
    public const uint MindControlledFlag = 0x0800_0000;

    /// <summary>
    /// The set a use click runs: <see cref="ScrAdmissionSet.S4"/> without the story-state actions (the session has no
    /// story-state number) and without the dialog action (a use click does not open a conversation here).
    /// </summary>
    public static ScrAdmissionSet Admission { get; } = new("S4-area-use", ScrAdmissionSet.S4.Conditions,
        ScrAdmissionSet.S4.Actions.Where(a => a is not ScrCatalogue.Action.GetStoryState and not ScrCatalogue.Action.SetStoryState and not ScrCatalogue.Action.Dialog),
        ScrAdmissionSet.S4.ValueKinds, ScrAdmissionSet.S4.FocusKinds);

    /// <summary>
    /// The session entity of a row: a mobile's own id (<see cref="AreaConversationState.EntityId"/>, as the inventory and
    /// dialog hosts name it); a sector object's sector and window ordinal, because sector objects carry a null id and so
    /// share one id digest (the transition reader and the area owner key them the same way).
    /// </summary>
    public static string EntityId(AreaObjectRecord row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row.Key.Kind == AreaSourceKind.Static && row.Key.Sector is { } sector ? $"sector:{sector}:{row.Key.SourceIndex}" : AreaConversationState.EntityId(row);
    }

    private readonly GameSessionOwner _session;
    private readonly IReadOnlyCollection<AreaInventoryMap> _maps;
    private readonly ScriptContent? _content;
    private readonly int _shownMap;
    private readonly Dictionary<string, AreaObjectRecord> _shown = new(StringComparer.Ordinal);

    /// <param name="shownMap">The shown map's runtime number.</param>
    /// <param name="shown">The shown map's object rows (its snapshot's): any of them can be the attachee or a guard's object.</param>
    /// <param name="maps">The loaded maps' inventory rows (items and holders), for objects of other maps.</param>
    /// <param name="content">The game's scripts; null when they could not be loaded. The host borrows it.</param>
    public AreaUseScriptHost(GameSessionOwner session, int shownMap, IEnumerable<AreaObjectRecord> shown, IReadOnlyCollection<AreaInventoryMap> maps,
        ScriptContent? content)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        ArgumentNullException.ThrowIfNull(shown);
        _maps = maps ?? throw new ArgumentNullException(nameof(maps));
        _content = content;
        _shownMap = shownMap;
        // A mobile id repeated by several rows keeps the first; the clicked row answers for its own (Run).
        foreach (var row in shown) _shown.TryAdd(EntityId(row), row);
    }

    public ScrObjectRef Pc => new(_session.QueryActor().Map.RuntimeMap, AreaScriptHost.PcCode);

    private AreaObjectRecord? Row(ScrObjectRef obj)
    {
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
        if (point != ScrPoint.Use) return loaded;
        long? Read(string property) => _session.QueryMapChange(new SessionMapId(attachee.Map), MapChangeKey.ForEntity(attachee.Code, property));
        return new(Read(ScriptProperty) is { } script ? checked((int)script) : loaded.Script,
            Read(LocalFlagsProperty) is { } flags ? checked((uint)flags) : loaded.LocalFlags,
            Read(CountersProperty) is { } counters ? checked((uint)counters) : loaded.Counters);
    }

    public ScrScriptLookup Script(int number) => AreaScriptHost.Lookup(_content, number);

    public bool GlobalFlag(int index)
    {
        var flags = _session.Flags;
        var read = flags.Read(new GlobalFlagReadRequest(new GlobalFlagRequestId(Guid.NewGuid()), flags.Version, new FlagId(index)));
        return read.Status == GlobalFlagReadStatus.Known && read.Value is { } value ? value : throw new InvalidOperationException("AREA_USE_SCRIPT_FLAG_UNREADABLE");
    }

    /// <summary>Never asked: <see cref="Admission"/> leaves out the story-state actions.</summary>
    public int StoryState() => throw new InvalidOperationException("AREA_USE_SCRIPT_STORY_STATE_NOT_BOUND");
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

    /// <summary>SG5: only a PC has reputations, and the session holds none yet (a new game's PC has none).</summary>
    public bool HasReputation(ScrObjectRef obj, int reputation) => false;

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
        // The clicked row answers for its entity, whichever copy of a repeated id the index kept.
        _shown[entity] = row;
        var attachee = new ScrObjectRef(_shownMap, entity);
        var run = ScrDispatcher.RunObject(new(ScrPoint.Use, attachee, Pc, null), this, new() { Rules = rules, Admission = Admission });
        if (run.Outcome == ScrOutcome.Refused)
            return new(AreaScriptedUseOutcome.Refused, null, [], run, $"script {run.Script} SCR line {run.Refusal!.Line}: {run.Refusal.Code} type {run.Refusal.Type}");
        if (_content.Changed) return new(AreaScriptedUseOutcome.ContentUnavailable, null, [], run, $"script {run.Script}: a script file changed since it was first read");
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
        if (!Apply(effects, attachee)) return new(AreaScriptedUseOutcome.EffectsRejected, null, lines, run, $"script {run.Script} {run.Stop}: the session refused its effects");
        var teleport = effects.Teleports.FirstOrDefault();
        return teleport is null
            ? new(AreaScriptedUseOutcome.NoTeleport, null, lines, run, $"script {run.Script} {run.Stop} at SCR line {run.StopLine}: no teleport")
            : new(AreaScriptedUseOutcome.Teleport, teleport, lines, run, $"script {run.Script}: teleport to {teleport.CatalogueMap} ({teleport.X},{teleport.Y})");
    }

    private bool Apply(ScrEffects effects, ScrObjectRef attachee)
    {
        if (effects.StoryState is not null || effects.DestroyTrapObject is not null) return false;
        foreach (var write in effects.GlobalFlags)
        {
            var flags = _session.Flags;
            var receipt = flags.Write(new GlobalFlagWriteRequest(new GlobalFlagRequestId(Guid.NewGuid()), flags.Version, new FlagId(write.Index), write.Value));
            if (receipt.Disposition is not (GlobalFlagWriteDisposition.Applied or GlobalFlagWriteDisposition.NoChange)) return false;
        }
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
        if (effects.Writeback is not { } writeback) return true;
        if (writeback.Source != ScrSource.Object || writeback.Point != ScrPoint.Use || writeback.Attachee != attachee
            || Row(attachee) is not { } row || LoadedSlot(row, ScrPoint.Use) is not { } loaded) return false;
        var map = new SessionMapId(attachee.Map);
        MapChangeEdit Edit(string property, long value, long baseline) => new(map, MapChangeKey.ForEntity(attachee.Code, property), value == baseline ? null : value);
        var result = _session.ApplyChanges(_session.Version, null,
        [
            Edit(ScriptProperty, writeback.Slot.Script, loaded.Script),
            Edit(LocalFlagsProperty, writeback.Slot.LocalFlags, loaded.LocalFlags),
            Edit(CountersProperty, writeback.Slot.Counters, loaded.Counters),
        ]);
        return result.Disposition != GameSessionDisposition.Rejected;
    }
}
