using Arcanum.Data.Characters;
using Arcanum.Data.Scripts;
using Arcanum.Scripting.Dialog;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Scripting.Scr;
using Arcanum.Session;

namespace Arcanum.World;

/// <summary>
/// ARC-SCRIPT-013: the AREA viewer's host for an item's insert-item script on a session transfer. It reads an item's
/// attachment from its area row (the effective script array of the loaded maps' seeds; the session refuses a run that
/// would change it, so the loaded slot is always current), the game's scripts and DLGs (ARC-SCRIPT-011 content, data
/// fixes of the session's rules), the session's global flags, the player (<see cref="AreaScriptHost.PcCode"/> on the
/// actor's map) and the session party for the follower conditions. A printed line uses the hero's gender (the diagnostic
/// male PC without a hero) and the diagnostic PC name; a line naming <c>@npcname@</c> is unsupported (whose name it is
/// on an item run is not read), so the session refuses that transfer. Everything else the viewer does not model answers "no". It only reads.
/// </summary>
public sealed class AreaItemScriptHost : ISessionItemScriptHost, ISessionItemWorldHost
{
    private readonly GameSessionOwner _session;
    private readonly IReadOnlyCollection<AreaInventoryMap> _maps;
    private readonly ScriptContent? _content;
    private readonly Func<int, EffectRule?>? _effectRules;
    private readonly Func<AreaUseScriptHost?>? _shown;
    private const int ObjectFlagsSlot = 18;

    /// <param name="content">The game's scripts; null when they could not be loaded, and then every script is missing and
    /// no line is printed. The host borrows it.</param>
    /// <param name="effectRules">ARC-DEV-366: the effect rule of a number (the hero's effect tables, <c>rules/effect.mes</c> and the module's
    /// <c>gameeffect.mes</c>), which an add-effect action of an item script names; null: no number has a rule, and the session refuses such a
    /// script, as before.</param>
    /// <param name="shownScripts">ARC-SCRIPT-2335: the use-script host of the shown map (its rows, art files and container lock identities), which answers the world commands of an insert-item script (<see cref="ISessionItemWorldHost"/>); null,
    /// or one that returns null: those commands cannot be answered and the session refuses such a move.</param>
    public AreaItemScriptHost(GameSessionOwner session, IReadOnlyCollection<AreaInventoryMap> maps, ScriptContent? content, Func<int, EffectRule?>? effectRules = null, Func<AreaUseScriptHost?>? shownScripts = null)
    {
        _shown = shownScripts;
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _maps = maps ?? throw new ArgumentNullException(nameof(maps));
        _content = content;
        _effectRules = effectRules;
    }

    /// <summary>ARC-DEV-366: the rule of an effect number from the effect tables (the tech books' effects 168 to 175, ledger XA8), or null without
    /// tables, for a number the tables do not hold or one the session refuses; the session then refuses the move.</summary>
    public SessionEffectDefinition? EffectDefinition(int number)
    {
        try { return _effectRules?.Invoke(number) is { } rule ? SessionEffectDefinition.FromRule(rule) : null; }
        catch (ArgumentException) { return null; }
    }

    /// <summary>The attachment a seed object's row loaded for the point: flags, counters and script of its effective
    /// script element, or null when it has none (a well-formed element has three words).</summary>
    public ScrSlot? Slot(ScrObjectRef attachee, int point)
    {
        var map = _maps.FirstOrDefault(candidate => candidate.RuntimeMap == attachee.Map);
        if (map is null || !map.TryGetRow(attachee.Code, out var row)) return null;
        var element = row.EffectiveFields.SingleOrDefault(field => field.Slot == 31)?.Elements.SingleOrDefault(e => e.Key == point);
        if (element is not { Words.Count: 3 } || element.Words[2] is < 0 or > ScrCatalogue.MaxScriptNumber || element.Words[0] is < 0 or > uint.MaxValue
            || element.Words[1] is < 0 or > uint.MaxValue) return null;
        return new((int)element.Words[2], (uint)element.Words[0], (uint)element.Words[1]);
    }

    /// <summary>ARC-SCRIPT-2335 (IV2): the containers around the item's own tile, from the shown map's rows.</summary>
    public IReadOnlyList<ScrObjectRef>? ContainersInVicinity(ScrObjectRef item) => _shown?.Invoke()?.ContainersNearItem(item);

    /// <summary>ARC-SCRIPT-2376 (IV18, PG3): the scenery around the item's own tile, from the shown map's rows.</summary>
    public IReadOnlyList<ScrObjectRef>? SceneryInVicinity(ScrObjectRef item) => _shown?.Invoke()?.SceneryNearItem(item);

    /// <summary>ARC-SCRIPT-2376 (IV18, TG4): the PCs and NPCs around the item's own tile, from the shown map's rows and the live cells.</summary>
    public IReadOnlyList<ScrObjectRef>? CrittersInVicinity(ScrObjectRef item) => _shown?.Invoke()?.CrittersNearItem(item);

    /// <summary>ARC-SCRIPT-2336 (FK8): the triggerer's direct followers from the shown map's use host (the hero's party followers; an NPC leader's session followers, the NPC being on the shown map). A leader the shown map has no row for makes the run unanswerable (null), as the name test does.</summary>
    public IReadOnlyList<ScrObjectRef>? FollowersOf(ScrObjectRef leader) => _shown?.Invoke() is { } host && Known(host, leader) ? host.FollowersOf(leader) : null;

    /// <summary>ARC-SCRIPT-2335 (IV3): an object's name number from the shown map's rows (the hero reads 0, as in the use host).</summary>
    public int? NameOf(ScrObjectRef obj) => _shown?.Invoke() is { } host && Known(host, obj) ? host.NameOf(obj) : null;

    /// <summary>ARC-SCRIPT-2335 (IV3): whether the object is mind-controlled by its row's spell flags.</summary>
    public bool? IsMindControlled(ScrObjectRef obj) => _shown?.Invoke() is { } host && Known(host, obj) ? host.IsMindControlled(obj) : null;

    // The hero has no row (its name is not modelled and reads 0, as in the use host), any other object needs the row the use host reads.
    private bool Known(AreaUseScriptHost host, ScrObjectRef obj) => host.IsPc(obj) || host.KnowsObject(obj);

    /// <summary>ARC-SCRIPT-2347 (IV13, SG1): the quest state the use host reads (the hero's own entry, any other object the quest's global state); it needs no row, so only a missing shown map leaves it unanswered.</summary>
    public int? QuestState(ScrObjectRef obj, int quest) => _shown?.Invoke() is { } host ? host.QuestState(obj, quest) : null;

    /// <summary>ARC-SCRIPT-2347 (IV13, OV3): an object's type field from the shown map's rows (the hero is 15).</summary>
    public int? ObjectType(ScrObjectRef obj) => _shown?.Invoke() is { } host && Known(host, obj) ? host.ObjectType(obj) : null;

    /// <summary>ARC-SCRIPT-2347 (IV14, HW1): the death of the hero or of an NPC the use host holds a row of, as it reads it; null for any other object.</summary>
    public bool? IsDead(ScrObjectRef obj) => _shown?.Invoke()?.IsDead(obj);

    /// <summary>
    /// ARC-SCRIPT-2346 (IV9): the item's off flag (bit 0x2 of the object flags, slot 18) in the loaded maps' rows; false for an item the session made (<c>made:n</c>: the original creates it on); null for an item no loaded row names. Only
    /// the data's bit: a script's toggle is the session's field (<see cref="GameSessionOwner.QueryItemOff"/>).
    /// </summary>
    public bool? ItemOffInData(ScrObjectRef item)
    {
        if (item.Code.StartsWith("made:", StringComparison.Ordinal)) return false;
        var map = _maps.FirstOrDefault(candidate => candidate.RuntimeMap == item.Map);
        if (map is null || !map.TryGetRow(item.Code, out var row)) return null;
        return row.EffectiveFields.FirstOrDefault(field => field.Slot == ObjectFlagsSlot) is { Presence: Arcanum.Data.Area.AreaPresence.Value, Number: { } flags } ? (flags & AreaUseScriptHost.OffObjectFlagBit) != 0 : false;
    }

    /// <summary>ARC-SCRIPT-2335 (IV5): the use host's plan of a change art number.</summary>
    public SessionObjectArtPlan? ArtPlan(ScrObjectRef obj, int number) => _shown?.Invoke()?.PlanObjectArt(obj, number);

    /// <summary>ARC-MAGIC-042: whether the host has the game's scripts (without them every script is missing).</summary>
    public bool HasContent => _content is not null;

    public ScrScriptLookup Script(int number) => AreaScriptHost.Lookup(_content, number);

    public bool GlobalFlag(int index)
    {
        var flags = _session.Flags;
        var read = flags.Read(new GlobalFlagReadRequest(new GlobalFlagRequestId(Guid.NewGuid()), flags.Version, new FlagId(index)));
        return read.Status == GlobalFlagReadStatus.Known && read.Value is { } value ? value : throw new InvalidOperationException("AREA_ITEM_SCRIPT_FLAG_UNREADABLE");
    }

    /// <summary>Never asked: <see cref="SessionItemScripts.Admission"/> leaves out the story-state actions.</summary>
    public int StoryState() => throw new InvalidOperationException("AREA_ITEM_SCRIPT_STORY_STATE_NOT_BOUND");
    public bool IsMonsterGenerator(ScrObjectRef attachee) => false;
    public ScrDialogState DialogState(ScrObjectRef attachee) => ScrDialogState.Normal;
    public ScrTrapCheck CheckTrapBeforeUse(ScrObjectRef triggerer, ScrObjectRef attachee) => ScrTrapCheck.NoTrap;
    public bool IsTrapObject(ScrObjectRef attachee) => false;
    public ScrObjectRef? Player() => new ScrObjectRef(_session.QueryActor().Map.RuntimeMap, AreaScriptHost.PcCode);
    public bool IsPc(ScrObjectRef critter) => critter.Code == AreaScriptHost.PcCode;
    // ARC-SCRIPT-1842 (0x45DE00): the session's leader, the PC or an NPC leader named on the critter's map.
    public ScrObjectRef? Leader(ScrObjectRef critter) => IsPc(critter) ? null : _session.QueryLeader(critter.Code) switch
    {
        null => null,
        AreaScriptHost.PcCode => Player(),
        var leader => new ScrObjectRef(critter.Map, leader),
    };
    public bool IsJilted(ScrObjectRef critter) => !IsPc(critter) && _session.QueryCompanion(critter.Code).Jilted;

    public SessionPrintedLine PrintedLine(int script, int line) => ReadLine(script, line, npcTriggerer: false);

    /// <summary>
    /// ARC-AI-1655 (ledger IS2): a line for a run whose triggerer is an NPC: the original shows the text or the alternate text by the triggerer's gender, and the host does not read an NPC's gender, so only a line whose two texts
    /// agree is shown (the hero's gender then does not matter); a line whose texts differ is <see cref="SessionPrintedLineStatus.Unsupported"/> and the session refuses the run.
    /// </summary>
    public SessionPrintedLine PrintedLineForNpc(int script, int line) => ReadLine(script, line, npcTriggerer: true);

    private SessionPrintedLine ReadLine(int script, int line, bool npcTriggerer)
    {
        if (_content is null) return new(SessionPrintedLineStatus.NoDialog, string.Empty, "no script content");
        var read = _content.Dialog(script, _session.Rules.Data);
        switch (read.Status)
        {
            case ScriptContentStatus.Read: break;
            // One content never mixes two versions of a file (ARC-SCRIPT-011).
            case ScriptContentStatus.Changed: return new(SessionPrintedLineStatus.Unsupported, string.Empty, $"dialog {script} changed since it was first read");
            // The original's dialog load fails and nothing is printed (ledger IS2); a DLG without records opens nothing either.
            case ScriptContentStatus.NoName or ScriptContentStatus.NotFound or ScriptContentStatus.Empty:
                return new(SessionPrintedLineStatus.NoDialog, string.Empty, $"dialog {script}: {read.Status}");
            // A file this reader refuses may still load in the original: not shown and not skipped.
            default: return new(SessionPrintedLineStatus.Unsupported, string.Empty, $"dialog {script}: {read.Status}");
        }
        if (npcTriggerer && read.Document!.TryGetRecord(line, out var both) && both is not null && !both.Text.Bytes.SequenceEqual(both.AlternateText.Bytes))
            return new(SessionPrintedLineStatus.Unsupported, string.Empty, $"line {line} differs by gender and the NPC's gender is not read");
        var alternate = !npcTriggerer && (_session.QueryHeroDialogFacts() ?? AreaConversationProfile.Diagnostic.Pc).Gender != DialogPcGender.Male;
        var printed = ScrPrintedLine.Read(read.Document!, line, alternate, AreaConversationProfile.Diagnostic.Names);
        if (printed.Status != ScrPrintedLineStatus.Shown) return new(SessionPrintedLineStatus.Unsupported, string.Empty, printed.Detail);
        if (read.Document!.TryGetRecord(line, out var record) && record is not null &&
            System.Text.Encoding.Latin1.GetString((alternate ? record.AlternateText : record.Text).Bytes.ToArray()).Contains("@npcname@", StringComparison.OrdinalIgnoreCase))
            return new(SessionPrintedLineStatus.Unsupported, string.Empty, $"line {line} names the NPC");
        return new(SessionPrintedLineStatus.Shown, printed.Text, printed.Detail);
    }
}
