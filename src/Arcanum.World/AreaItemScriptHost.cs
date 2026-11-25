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
/// male PC without a hero) and the hero's player name (ARC-SCRIPT-2787; the placeholder without a hero); ARC-SCRIPT-2811: <c>@npcname@</c> is the attachee item's shown name and <c>@pcname@</c> of an NPC triggerer
/// its not-yet-met description, read through the shown map's use host (<see cref="AreaUseScriptHost.ObjectName"/>); a line whose object has no row or no name source is unsupported, so the session refuses that transfer. Everything else the
/// viewer does not model answers "no". It only reads.
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
        // ARC-SCRIPT-2432 (IV40): a critter's attachment is the use host's reading (the loaded element under the session's map changes: a change script of an earlier move may have replaced a number); an item's is the loaded row's.
        if (_shown?.Invoke() is { } shown && shown.IsCritterRow(attachee)) return shown.Slot(attachee, point);
        var map = _maps.FirstOrDefault(candidate => candidate.RuntimeMap == attachee.Map);
        if (map is null || !map.TryGetRow(attachee.Code, out var row)) return null;
        var element = row.EffectiveFields.SingleOrDefault(field => field.Slot == 31)?.Elements.SingleOrDefault(e => e.Key == point);
        if (element is not { Words.Count: 3 } || element.Words[2] is < 0 or > ScrCatalogue.MaxScriptNumber || element.Words[0] is < 0 or > uint.MaxValue
            || element.Words[1] is < 0 or > uint.MaxValue) return null;
        return new((int)element.Words[2], (uint)element.Words[0], (uint)element.Words[1]);
    }

    /// <summary>ARC-SCRIPT-2335 (IV2): the containers around the item's own tile, from the shown map's rows.</summary>
    public IReadOnlyList<ScrObjectRef>? ContainersInVicinity(ScrObjectRef item) => _shown?.Invoke()?.ContainersNearItem(item);

    /// <summary>ARC-SCRIPT-2376 (IV25, PG3): the scenery around the item's own tile, from the shown map's rows.</summary>
    public IReadOnlyList<ScrObjectRef>? SceneryInVicinity(ScrObjectRef item) => _shown?.Invoke()?.SceneryNearItem(item);

    /// <summary>ARC-SCRIPT-2376 (IV25, TG4): the PCs and NPCs around the item's own tile, from the shown map's rows and the live cells.</summary>
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

    /// <summary>ARC-SCRIPT-2375 (IV30): the item's own name number (field 22) in the loaded maps' rows, without a script's rename; null for an item no loaded row names (a made item).</summary>
    public int? ItemNameInData(ScrObjectRef item)
    {
        var map = _maps.FirstOrDefault(candidate => candidate.RuntimeMap == item.Map);
        return map is not null && map.TryGetRow(item.Code, out var row) ? AreaUseScriptHost.NameOf(row) : null;
    }

    /// <summary>ARC-SCRIPT-2426 (IV36–IV39): the location field of an object as the shown map's use host places it (the hero's cell, a critter's live tile, a row's tile, an item's own stale tile); null for an object it has no row or place for.</summary>
    public (long X, long Y)? LocationOf(ScrObjectRef obj) => _shown?.Invoke()?.LocationOfObject(obj);

    /// <summary>ARC-SCRIPT-2377 (IV45, CQ1): an NPC's social class from the shown map's rows (0 for the hero and any other object type, as the use host reads it); null for an object that host has no row for.</summary>
    public int? SocialClassOf(ScrObjectRef obj) => _shown?.Invoke() is { } host && Known(host, obj) ? host.SocialClassOf(obj) : null;

    /// <summary>ARC-SCRIPT-2377 (IV45, CQ2): an NPC's origin as it is now from the shown map's use host (0 for the hero and any other object type); null for an object that host has no row for.</summary>
    public int? OriginOf(ScrObjectRef obj) => _shown?.Invoke() is { } host && Known(host, obj) ? host.OriginOf(obj) : null;

    /// <summary>ARC-SCRIPT-2432 (IV40): the use host's map changes for a change script of a critter of the shown map; null for any other object.</summary>
    public IReadOnlyList<MapChangeEdit>? PlanObjectSlot(ScrWriteback write) => _shown?.Invoke()?.PlanCritterSlot(write);

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

    /// <summary>ARC-SCRIPT-2375 (IV28): the session's story-state number. The session's run host answers its own reads (the move's earlier runs may have raised it), so the session does not ask this.</summary>
    public int StoryState() => _session.QueryStoryState();
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
    public bool SameObject(ScrObjectRef a, ScrObjectRef b) => AreaScriptHost.SameCritter(a, b);
    public bool IsJilted(ScrObjectRef critter) => !IsPc(critter) && _session.QueryCompanion(critter.Code).Jilted;

    public SessionPrintedLine PrintedLine(int script, int line) => ReadLine(script, line, npcTriggerer: false, null, null);

    /// <summary>
    /// ARC-AI-1655 (ledger IS2): a line for a run whose triggerer is an NPC: the original shows the text or the alternate text by the triggerer's gender, and the host does not read an NPC's gender, so only a line whose two texts
    /// agree is shown (the hero's gender then does not matter); a line whose texts differ is <see cref="SessionPrintedLineStatus.Unsupported"/> and the session refuses the run.
    /// </summary>
    public SessionPrintedLine PrintedLineForNpc(int script, int line) => ReadLine(script, line, npcTriggerer: true, null, null);

    /// <summary>ARC-SCRIPT-2811 (ledger NM4): the line with the objects of its context, so a name token of it can be read (see <see cref="ReadLine"/>).</summary>
    public SessionPrintedLine PrintedLineOf(int script, int line, ScrObjectRef? triggerer, ScrObjectRef? attachee, bool npcTriggerer) => ReadLine(script, line, npcTriggerer, triggerer, attachee);

    /// <summary>
    /// The line's text for the run. A name token needs its object: <c>@pcname@</c> is the triggerer's name, the hero's player name unless an NPC triggers (then the NPC's not-yet-met description, observer none, ledger NM4) and <c>@npcname@</c> the
    /// attachee's shown name (an item the loaded maps hold a row of, its own observer, identified when the session marked it); the shown map's use host reads those names (<see cref="AreaUseScriptHost.ObjectName"/>). A token whose object or name cannot be
    /// read is <see cref="SessionPrintedLineStatus.Unsupported"/>, so the session refuses the run.
    /// </summary>
    private SessionPrintedLine ReadLine(int script, int line, bool npcTriggerer, ScrObjectRef? triggerer, ScrObjectRef? attachee)
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
        var text = read.Document!.TryGetRecord(line, out var record) && record is not null ? System.Text.Encoding.Latin1.GetString((alternate ? record.AlternateText : record.Text).Bytes.ToArray()) : string.Empty;
        string? pcName = null, npcName = null;
        // ARC-SCRIPT-2811 (0x44B390 → 0x416B00, NM4): an NPC triggerer's @pcname@ is its not-yet-met description (no observer).
        if (npcTriggerer && text.Contains("@pcname@", StringComparison.OrdinalIgnoreCase) && (triggerer is not { } who || !IsHero(who)))
        {
            if (triggerer is not { } npc || _shown?.Invoke()?.LineName(npc, Arcanum.Data.Messages.CritterRecognition.NotRecognized, false) is not { } unmet)
                return new(SessionPrintedLineStatus.Unsupported, string.Empty, $"line {line} names the triggering NPC");
            pcName = unmet;
        }
        // The attachee is its own observer: an item by its shown name (identified when the session marked it), the hero by its player name.
        if (text.Contains("@npcname@", StringComparison.OrdinalIgnoreCase))
        {
            if (attachee is { } hero && IsHero(hero)) npcName = AreaDialogNames.PcName(_session);
            else if (attachee is not { } item || AttacheeName(item) is not { } named) return new(SessionPrintedLineStatus.Unsupported, string.Empty, $"line {line} names the NPC");
            else npcName = named;
        }
        var printed = ScrPrintedLine.Read(read.Document!, line, alternate, AreaDialogNames.Of(_session, AreaConversationProfile.Diagnostic.Names, pcName, npcName));
        if (printed.Status != ScrPrintedLineStatus.Shown) return new(SessionPrintedLineStatus.Unsupported, string.Empty, printed.Detail);
        return new(SessionPrintedLineStatus.Shown, printed.Text, printed.Detail);
    }

    private static bool IsHero(ScrObjectRef obj) => obj.Code == AreaScriptHost.PcCode;

    /// <summary>ARC-SCRIPT-2811 (NM3, NM4): the attachee's shown name (empty when it has no text); null for an object no loaded row names (a made item) or when the shown map's use host has no name source.</summary>
    private string? AttacheeName(ScrObjectRef attachee)
    {
        if (_shown?.Invoke() is not { } host) return null;
        var map = _maps.FirstOrDefault(candidate => candidate.RuntimeMap == attachee.Map);
        return map is not null && map.TryGetRow(attachee.Code, out var row)
            ? host.LineName(row, Arcanum.Data.Messages.CritterRecognition.Recognized, _session.QueryItemIdentified(attachee.Code))
            : host.LineName(attachee, Arcanum.Data.Messages.CritterRecognition.Recognized, false);
    }
}
