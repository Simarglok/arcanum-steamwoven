using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Simulation.Party;

namespace Arcanum.World;

public enum AreaBroadcastKeywordsStatus { Ready, ModuleOutsideGameRoot, MountFailed, StackFailed, FileMissing }

/// <summary>
/// ARC-UI-2305 (ledger OL3, BM5): the order words of the typed broadcast line, read from the player's own <c>mes\broadcast.mes</c>
/// (the keyword messages 100, 101, … up to the first missing number) and <c>Rules\BroadCastRules.mes</c> (the type names of messages
/// 0 to 13, and for each keyword the type name of message 100 + i), as the original's init 0x4C2C10 does at game start.
/// </summary>
public static class AreaBroadcastKeywords
{
    public const string BroadcastPath = "mes/broadcast.mes", RulesPath = "rules/broadcastrules.mes";

    /// <summary>The words and their types from the two parsed files (synthetic or real); a message that does not decode is read as empty.</summary>
    public static BroadcastKeywords Parse(MesFile broadcast, MesFile rules)
    {
        ArgumentNullException.ThrowIfNull(broadcast); ArgumentNullException.ThrowIfNull(rules);
        string Text(MesFile file, int number) => file.Find(number) is { } message
            && MesText.Decode(message, AreaHoverIndex.TextProfile) is { Status: MesDecodeStatus.Decoded } decoded ? decoded.Text ?? "" : "";
        var words = new List<string>();
        for (var number = BroadcastKeywords.FirstKeyword; broadcast.Find(number) is not null; number++) words.Add(Text(broadcast, number));
        var names = Enumerable.Range(0, BroadcastKeywords.TypeCount).Select(n => Text(rules, n)).ToArray();
        var keywordTypes = Enumerable.Range(0, words.Count).Select(i => Text(rules, BroadcastKeywords.FirstKeyword + i)).ToArray();
        return BroadcastKeywords.Build(words, names, keywordTypes);
    }

    /// <summary>Loads both files through the module's game folder (the rule of <see cref="AreaLevelTexts.Load"/>), each from the sources of the
    /// game-start phase.</summary>
    public static (AreaBroadcastKeywordsStatus Status, BroadcastKeywords? Keywords, string Detail) Load(string modulePath)
    {
        ArgumentNullException.ThrowIfNull(modulePath);
        var found = AreaContentRoot.Discover(modulePath);
        if (found.Status != AreaContentRootStatus.Ready)
            return (found.Status == AreaContentRootStatus.MountFailed ? AreaBroadcastKeywordsStatus.MountFailed : AreaBroadcastKeywordsStatus.ModuleOutsideGameRoot,
                null, found.Detail ?? found.Status.ToString());
        var opened = ContentStack.OpenShared(found.Plan!);
        if (opened.Status != ContentStackStatus.Ready) return (AreaBroadcastKeywordsStatus.StackFailed, null, opened.Status.ToString());
        using var stack = opened.Stack!;
        var options = new MesReaderOptions();
        MesFile? Read(string path)
        {
            if (ContentPhase.Candidate(stack, path, ContentLoadPhase.GameStart) is not { } source || source.Length > options.MaxBytes) return null;
            var read = stack.Read(source);
            if (read.Status != ContentReadStatus.Read) return null;
            var parsed = MesFile.Parse(read.Bytes!, options);
            return parsed.Status == MesParseStatus.Parsed ? parsed.File : null;
        }
        if (Read(BroadcastPath) is not { } broadcast || Read(RulesPath) is not { } rules)
            return (AreaBroadcastKeywordsStatus.FileMissing, null, $"{BroadcastPath} or {RulesPath} not readable");
        var keywords = Parse(broadcast, rules);
        return (AreaBroadcastKeywordsStatus.Ready, keywords, $"{keywords.Count} order words");
    }
}
