using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Simulation;
using Arcanum.Simulation.Party;

namespace Arcanum.World;

public enum AreaSleepFilterStatus { Ready, ModuleOutsideGameRoot, MountFailed, StackFailed, FileMissing, EmptyFiller }

/// <summary>
/// ARC-WORLD-2310 (ledger BM5f, init 0x4F69D0): the sleeping speech filter's words, read from the player's own <c>mes\text_filter_sleep.mes</c> (message 0 the filler, messages
/// 1, 2, … up to the first missing number the kept words). The original reads the file at game start and ends the game when it is missing; the host here reports a typed
/// status instead and sends no order for a speaker the filter would apply to.
/// </summary>
public static class AreaSleepSpeechFilter
{
    public const string FilterPath = "mes/text_filter_sleep.mes";

    /// <summary>The filter from the parsed file (synthetic or real); a message that does not decode is read as empty. The filler (message 0) must have a character.</summary>
    [OriginalFunction(0x4F69D0, OriginalFunctionScope.Partial)]
    public static (AreaSleepFilterStatus Status, SleepSpeechFilter? Filter) Parse(MesFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        string Text(int number) => file.Find(number) is { } message
            && MesText.Decode(message, AreaHoverIndex.TextProfile) is { Status: MesDecodeStatus.Decoded } decoded ? decoded.Text ?? "" : "";
        var filler = Text(0);
        if (filler.Length == 0) return (AreaSleepFilterStatus.EmptyFiller, null);
        var words = new List<string>();
        for (var number = 1; file.Find(number) is not null; number++) words.Add(Text(number));
        return (AreaSleepFilterStatus.Ready, new SleepSpeechFilter(filler, words));
    }

    /// <summary>Loads the file through the module's game folder (the rule of <see cref="AreaBroadcastKeywords.Load"/>), from the sources of the game-start phase.</summary>
    public static (AreaSleepFilterStatus Status, SleepSpeechFilter? Filter, string Detail) Load(string modulePath)
    {
        ArgumentNullException.ThrowIfNull(modulePath);
        var found = AreaContentRoot.Discover(modulePath);
        if (found.Status != AreaContentRootStatus.Ready)
            return (found.Status == AreaContentRootStatus.MountFailed ? AreaSleepFilterStatus.MountFailed : AreaSleepFilterStatus.ModuleOutsideGameRoot,
                null, found.Detail ?? found.Status.ToString());
        var opened = ContentStack.OpenShared(found.Plan!);
        if (opened.Status != ContentStackStatus.Ready) return (AreaSleepFilterStatus.StackFailed, null, opened.Status.ToString());
        using var stack = opened.Stack!;
        var options = new MesReaderOptions();
        if (ContentPhase.Candidate(stack, FilterPath, ContentLoadPhase.GameStart) is not { } source || source.Length > options.MaxBytes
            || stack.Read(source) is not { Status: ContentReadStatus.Read } read || MesFile.Parse(read.Bytes!, options) is not { Status: MesParseStatus.Parsed } parsed)
            return (AreaSleepFilterStatus.FileMissing, null, $"{FilterPath} not readable");
        var (status, filter) = Parse(parsed.File!);
        return (status, filter, filter is null ? status.ToString() : $"{filter.KeepWords.Count} kept words");
    }
}
