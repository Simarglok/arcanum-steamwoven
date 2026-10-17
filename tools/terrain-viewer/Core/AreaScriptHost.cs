using Arcanum.Data;
using Arcanum.Data.Area;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Scripting.Scr;
using Arcanum.Session;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// The SCR dispatcher's host for the AREA viewer's dialog NPCs (ARC-SCRIPT-009): it reads a speaker's dialog slot
/// (the area's effective attachment, overridden by the session's map changes), the game's script content (ARC-SCRIPT-011:
/// the universal content stack, as for every map), the session's global flags and the follower standing, and it applies a run's effects to the session. Only the dialog
/// point of the map's speakers is modelled; everything the viewer does not model answers "no" (no generator, no trap,
/// alive and free, not jilted).
/// </summary>
public sealed class AreaScriptHost : IScrHost
{
    /// <summary>The PC as the scripts see it; the viewer has one.</summary>
    public const string PcCode = SessionEventKinds.PlayerEntity;
    /// <summary>Map-change keys of a speaker's dialog slot, besides <see cref="AreaConversationState.LocalFlagsProperty"/>.</summary>
    public const string CountersProperty = "dialog.counters", ScriptProperty = "dialog.script";

    /// <summary>
    /// The set the viewer runs: <see cref="ScrAdmissionSet.S3"/> without get and set story state, because the session
    /// has no story-state number yet; a script that would use one is refused, not run on a made-up value.
    /// </summary>
    public static ScrAdmissionSet Admission { get; } = new("S3-area", ScrAdmissionSet.S3.Conditions,
        ScrAdmissionSet.S3.Actions.Where(a => a is not ScrCatalogue.Action.GetStoryState and not ScrCatalogue.Action.SetStoryState),
        ScrAdmissionSet.S3.ValueKinds, ScrAdmissionSet.S3.FocusKinds);

    private readonly GameSessionOwner _session;
    private readonly SessionMapId _map;
    private readonly IReadOnlyList<AreaSpeaker> _speakers;
    private readonly AreaConversationState _state;
    private readonly ScriptContent? _content;

    /// <param name="content">The game's scripts; null when they could not be loaded, and then every script is missing.
    /// The host borrows it.</param>
    public AreaScriptHost(GameSessionOwner session, SessionMapId map, IReadOnlyList<AreaSpeaker> speakers, AreaConversationState state, ScriptContent? content)
    {
        _session = session; _map = map; _speakers = speakers; _state = state; _content = content;
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
    public ScrScriptLookup Script(int number)
    {
        if (_content is null || number <= 0 || number > ScrCatalogue.MaxScriptNumber) return ScrScriptLookup.Missing;
        var read = _content.Script(number);
        return read.Status switch
        {
            ScriptContentStatus.Read => ScrScriptLookup.Of(read.Program!),
            ScriptContentStatus.NoName or ScriptContentStatus.NotFound => ScrScriptLookup.Missing,
            _ => ScrScriptLookup.Unreadable,
        };
    }

    /// <summary>
    /// The DLG a dialog action names: the script's name with the <c>dlg</c> folder and extension (as the original
    /// derives it), read as the original reads it (ARC-SCRIPT-012), whose <paramref name="node"/> is an NPC record. <see cref="AreaConversationStartStatus.Started"/>
    /// with the document, or the typed reason.
    /// </summary>
    public static (AreaConversationStartStatus Status, DlgDocument? Document, string Detail) LoadDialog(ScriptContent content, int script, int node)
    {
        ArgumentNullException.ThrowIfNull(content);
        var read = content.Dialog(script);
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

    /// <summary>
    /// Applies a run's effects: the global flags, then the speaker's slot as map changes (a value equal to the loaded
    /// one clears its key). The session has no transaction across its flag owner and its map changes; a rejected step
    /// stops the later ones and is reported. The dialog start is the caller's.
    /// </summary>
    public bool Apply(ScrEffects effects)
    {
        ArgumentNullException.ThrowIfNull(effects);
        // The viewer's set admits neither story state nor deferred calls; a trap object is not modelled.
        if (effects.StoryState is not null || effects.DestroyTrapObject is not null || effects.Calls.Count > 0) return false;
        foreach (var write in effects.GlobalFlags)
        {
            var flags = _session.Flags;
            var receipt = flags.Write(new GlobalFlagWriteRequest(new GlobalFlagRequestId(Guid.NewGuid()), flags.Version, new FlagId(write.Index), write.Value));
            if (receipt.Disposition is not (GlobalFlagWriteDisposition.Applied or GlobalFlagWriteDisposition.NoChange)) return false;
        }
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
}
