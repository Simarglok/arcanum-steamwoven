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
public sealed class AreaItemScriptHost : ISessionItemScriptHost
{
    private readonly GameSessionOwner _session;
    private readonly IReadOnlyCollection<AreaInventoryMap> _maps;
    private readonly ScriptContent? _content;
    private readonly Func<int, EffectRule?>? _effectRules;

    /// <param name="content">The game's scripts; null when they could not be loaded, and then every script is missing and
    /// no line is printed. The host borrows it.</param>
    /// <param name="effectRules">ARC-DEV-366: the effect rule of a number (the hero's effect tables, <c>rules/effect.mes</c> and the module's
    /// <c>gameeffect.mes</c>), which an add-effect action of an item script names; null: no number has a rule, and the session refuses such a
    /// script, as before.</param>
    public AreaItemScriptHost(GameSessionOwner session, IReadOnlyCollection<AreaInventoryMap> maps, ScriptContent? content, Func<int, EffectRule?>? effectRules = null)
    {
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
    public ScrObjectRef? Leader(ScrObjectRef critter) => !IsPc(critter) && _session.QueryCompanion(critter.Code).LedByPc ? Player() : null;
    public bool IsJilted(ScrObjectRef critter) => !IsPc(critter) && _session.QueryCompanion(critter.Code).Jilted;

    public SessionPrintedLine PrintedLine(int script, int line)
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
        var alternate = (_session.QueryHeroDialogFacts() ?? AreaConversationProfile.Diagnostic.Pc).Gender != DialogPcGender.Male;
        var printed = ScrPrintedLine.Read(read.Document!, line, alternate, AreaConversationProfile.Diagnostic.Names);
        if (printed.Status != ScrPrintedLineStatus.Shown) return new(SessionPrintedLineStatus.Unsupported, string.Empty, printed.Detail);
        if (read.Document!.TryGetRecord(line, out var record) && record is not null &&
            System.Text.Encoding.Latin1.GetString((alternate ? record.AlternateText : record.Text).Bytes.ToArray()).Contains("@npcname@", StringComparison.OrdinalIgnoreCase))
            return new(SessionPrintedLineStatus.Unsupported, string.Empty, $"line {line} names the NPC");
        return new(SessionPrintedLineStatus.Shown, printed.Text, printed.Detail);
    }
}
