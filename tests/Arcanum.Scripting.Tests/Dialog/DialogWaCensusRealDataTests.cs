using System.Text;
using Arcanum.Data;
using Arcanum.Data.Content;
using Arcanum.Data.Scripts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Scripting.Tests.Dialog;

/// <summary>
/// Opt-in census of ARC-SCRIPT-1136 (<c>DAT_ROOT</c>, the game folder): which installed Player replies with the <c>wa</c> action can be offered to an NPC the PC does not lead (ledger CF8 to CF10).
/// The walk starts at the NPC's own script (the file of the same name as the DLG; a talk starts every engine event at line 0, ledger X-rows of the dispatcher) and takes, for a free NPC, the false branch of
/// every "the attachee follows the triggerer" test (condition 18); every other condition may go either way (an upper bound: world state, local flags and counters are not followed). A dialog action emits its
/// node; the talk then resumes at the next line, or at the line a negative response names. Inside the DLG a reply is followed unless its condition holds <c>fo 1</c>, <c>wa 1</c> or a <c>pa</c> test (it needs a
/// leader or a waiting NPC). Only counts are compared and printed, never original text. Over-approximation on purpose: a count of 0 would close the issue as unreachable, a larger one says the behaviour is
/// worth building; the installed data reaches it (a free NPC that still holds its dialog script after a leave).
/// </summary>
[TestClass]
public sealed class DialogWaCensusRealDataTests
{
    private static readonly char[] Separators = [',', ';', ' ', ':', '(', ')', '\t', '\n', '\r'];

    private static string Text(DlgField field) => Encoding.Latin1.GetString(field.Bytes.ToArray());

    // The codes of a condition or action field with the number that follows each (the source's scan: skip to a letter, two characters, then the operands).
    private static IEnumerable<(string Code, int? Operand)> Codes(string text)
    {
        var tokens = text.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (!char.IsLetter(token[0])) continue;
            // "fo 1" and "fo1" both read as the code with the operand.
            var code = new string(token.TakeWhile(char.IsLetter).ToArray()).ToLowerInvariant();
            var digits = token[code.Length..];
            if (digits.Length == 0 && i + 1 < tokens.Length) digits = tokens[i + 1];
            yield return (code, int.TryParse(digits, out var number) ? number : null);
        }
    }

    private static bool NeedsALeader(string condition) => Codes(condition).Any(c => c is ("fo", 1) or ("wa", 1) or ("pa", _));

    private static bool HasWaAction(string action) => Codes(action).Any(c => c.Code == "wa");

    // ScrCatalogue: condition 18 is "the first object's leader is the second object", focus kind 1 the attachee, 0 the triggerer, 21 the player.
    private static bool FalseForAFreeNpc(ScrClause condition) => condition.Type == 18 && condition.Kind(0) == 1 && condition.Kind(1) is 0 or 21;

    // The dialog nodes a talk with a free NPC can start at, with the nodes of the talk's own resumptions followed (the closure over the lines the DLG hands back).
    private static HashSet<int> StartNodes(ScrProgram program, DlgDocument document)
    {
        var nodes = new HashSet<int>();
        var starts = new Queue<int>();
        var seenStarts = new HashSet<int>();
        void Start(int line)
        {
            if (line >= 0 && line < program.Count && seenStarts.Add(line)) starts.Enqueue(line);
        }

        Start(0);
        var replies = Replies(document);
        var expanded = new HashSet<int>();
        while (true)
        {
            while (starts.Count > 0)
            {
                var seen = new HashSet<int>();
                var work = new Queue<int>();
                work.Enqueue(starts.Dequeue());
                while (work.Count > 0)
                {
                    var i = work.Dequeue();
                    if (i < 0 || i >= program.Count || !seen.Add(i)) continue;
                    var line = program.Lines[i];
                    var branches = line.Condition.Type == 0 ? [line.OnTrue]
                        : FalseForAFreeNpc(line.Condition) ? [line.OnFalse] : new[] { line.OnTrue, line.OnFalse };
                    foreach (var action in branches)
                    {
                        switch (action.Type)
                        {
                            case ScrCatalogue.Action.ReturnSkipDefault or ScrCatalogue.Action.ReturnRunDefault: break;
                            case ScrCatalogue.Action.Goto:
                                if (action.Kind(0) == (byte)ScrValueKind.Number) work.Enqueue(action.Value(0)); else work.Enqueue(i + 1);
                                break;
                            case ScrCatalogue.Action.Dialog:
                                if (action.Kind(0) == (byte)ScrValueKind.Number) nodes.Add(action.Value(0));
                                Start(i + 1);
                                break;
                            default: work.Enqueue(i + 1); break;
                        }
                    }
                }
            }

            // The DLG hands the talk back to the script at a negative response's line.
            var grown = false;
            foreach (var node in Reached(replies, nodes).Where(expanded.Add))
                foreach (var reply in replies[node].Where(r => !NeedsALeader(Text(r.Condition)) && r.ResponseKind == DlgResponseKind.Special && r.ResponseValue is < 0))
                {
                    var line = -reply.ResponseValue!.Value;
                    if (line < program.Count && seenStarts.Add(line)) { starts.Enqueue(line); grown = true; }
                }
            if (!grown) break;
        }

        return nodes;
    }

    private static Dictionary<int, List<DlgRecord>> Replies(DlgDocument document)
    {
        var replies = new Dictionary<int, List<DlgRecord>>();
        int? current = null;
        foreach (var record in document.Records)
        {
            if (record.Role == DlgRecordRole.Npc) { current = record.Id; replies[record.Id] = []; }
            else if (current is { } owner) replies[owner].Add(record);
        }

        return replies;
    }

    // The NPC nodes reachable from the start nodes through replies a free NPC can be offered.
    private static HashSet<int> Reached(Dictionary<int, List<DlgRecord>> replies, IEnumerable<int> starts)
    {
        var reached = new HashSet<int>();
        var queue = new Queue<int>();
        foreach (var start in starts.Where(replies.ContainsKey)) if (reached.Add(start)) queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            foreach (var reply in replies[node])
            {
                if (NeedsALeader(Text(reply.Condition))) continue;
                if (reply.ResponseKind == DlgResponseKind.Direct && reply.ResponseValue is { } target && replies.ContainsKey(target) && reached.Add(target)) queue.Enqueue(target);
            }
        }

        return reached;
    }

    [TestMethod, TestCategory("RealData"), TestCategory("DlgRealData")]
    public void A_wa_reply_can_reach_an_npc_with_no_leader_in_the_installed_dialogs()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in wa census; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var keys = stack.Keys().ToList();
        var scripts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys.Where(k => k.EndsWith(".scr", StringComparison.Ordinal))) scripts.TryAdd(Path.GetFileNameWithoutExtension(key), key);

        int replies = 0, files = 0, noScript = 0, staticallyReached = 0, reached = 0, reachedFiles = 0, repliesWithOperand = 0;
        foreach (var key in keys.Where(k => k.EndsWith(".dlg", StringComparison.Ordinal)))
        {
            var document = DlgReader.Read(stack.Read(stack.Resolve(key).Winner!).Bytes!, new DlgReaderOptions { Mode = DlgReadMode.Original });
            var waReplies = document.Records.Where(r => r.Role == DlgRecordRole.Player && HasWaAction(Text(r.Action))).ToList();
            if (waReplies.Count == 0) continue;
            files++;
            replies += waReplies.Count;
            repliesWithOperand += waReplies.Count(r => Codes(Text(r.Action)).Any(c => c is { Code: "wa", Operand: not null }));
            var byNode = Replies(document);
            // The static bound: from every NPC line no reply targets, without the script.
            var targeted = document.Records.Where(r => r.Role == DlgRecordRole.Player && r.ResponseKind == DlgResponseKind.Direct && r.ResponseValue is not null)
                .Select(r => r.ResponseValue!.Value).ToHashSet();
            var roots = byNode.Keys.Where(node => !targeted.Contains(node)).ToList();
            var staticSet = Reached(byNode, roots);
            staticallyReached += waReplies.Count(w => staticSet.Contains(byNode.First(kv => kv.Value.Contains(w)).Key));

            if (!scripts.TryGetValue(Path.GetFileNameWithoutExtension(key), out var scriptKey)) { noScript++; continue; }
            var program = ScrProgram.Read(stack.Read(stack.Resolve(scriptKey).Winner!).Bytes!).Program!;
            var starts = StartNodes(program, document);
            var set = Reached(byNode, starts);
            var count = waReplies.Count(w => set.Contains(byNode.First(kv => kv.Value.Contains(w)).Key));
            reached += count;
            if (count > 0) reachedFiles++;
        }

        Console.WriteLine($"WA-CENSUS|wa replies {replies}|files {files}|statically reached {staticallyReached}|no script {noScript}|reached from the talk {reached}|files {reachedFiles}");
        Assert.AreEqual((47, 32, 0), (replies, files, repliesWithOperand), "every installed wa reply is the operand-less action");
        Assert.AreEqual(20, staticallyReached, "the upper bound without the scripts");
        Assert.AreEqual(0, noScript, "every DLG with a wa reply has its script of the same name");
        Assert.AreEqual((5, 3), (reached, reachedFiles), "the replies a free NPC can be offered from its script's talk: a leaderless NPC that still holds the dialog script, as Surly Virgil after leaving");
    }
}
