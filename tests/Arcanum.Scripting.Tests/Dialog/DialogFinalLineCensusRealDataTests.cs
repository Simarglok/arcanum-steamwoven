using System.Text;
using System.Text.RegularExpressions;
using Arcanum.Data;
using Arcanum.Data.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Scripting.Tests.Dialog;

/// <summary>
/// Opt-in census of ARC-SCRIPT-2790 (<c>DAT_ROOT</c>, the game folder): the Player replies whose action string holds a final line <c>fl N</c> to a node that is not an NPC record of the same DLG.
/// A node absent from the DLG is no error in the original (the line stays blank, the string goes on, the dialog ends; ledger FLM1) and the reply is offered; a node that is a Player record is used the same way (ARC-SCRIPT-2804, ledger FLM2: its text is the NPC's line, its action runs) unless its action holds a dialog transfer. Only counts and ids are printed, never original text.
/// </summary>
[TestClass]
public sealed class DialogFinalLineCensusRealDataTests
{
    // The final line code with its node operand: fl N.
    private static readonly Regex Transfer = new(@"(?<![a-z])(?<code>fl)\s*(?<first>-?\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [TestMethod, TestCategory("RealData"), TestCategory("DlgRealData")]
    public void Final_line_replies_with_a_bad_target_are_counted_in_the_installed_dialogs()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in transfer-target census; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var rows = new List<string>();
        var shapes = new List<string>();
        int transfers = 0;
        foreach (var key in stack.Keys().Where(k => k.EndsWith(".dlg", StringComparison.Ordinal)))
        {
            var document = DlgReader.Read(stack.Read(stack.Resolve(key).Winner!).Bytes!, new DlgReaderOptions { Mode = DlgReadMode.Original });
            foreach (var reply in document.Records.Where(r => r.Role == DlgRecordRole.Player))
            {
                var action = Encoding.Latin1.GetString(reply.Action.Bytes.ToArray());
                foreach (Match match in Transfer.Matches(action))
                {
                    var code = match.Groups["code"].Value.ToLowerInvariant();
                    if (!int.TryParse(match.Groups["first"].Value, out var target)) continue;
                    transfers++;
                    var missing = !document.TryGetRecord(target, out var next) || next is null;
                    var notNpc = !missing && next!.Role != DlgRecordRole.Npc;
                    if (missing || notNpc) rows.Add($"{Path.GetFileName(key)}|reply {reply.Id}|{code} {target}|{(missing ? "missing" : "not an NPC record")}|has co {action.Contains("co", StringComparison.OrdinalIgnoreCase)}");
                    if (notNpc) shapes.Add($"{Path.GetFileName(key)}|reply {reply.Id}|{code} {target}|{Shape(next!)}");
                }
            }
        }

        Console.WriteLine($"FL-CENSUS|final lines {transfers}|bad targets {rows.Count}");
        foreach (var row in rows) Console.WriteLine($"FL-CENSUS-ROW|{row}");
        foreach (var shape in shapes) Console.WriteLine($"FL-CENSUS-PLAYER-SHAPE|{shape}");
        var missingRows = rows.Where(row => row.Contains("|missing|", StringComparison.Ordinal)).ToList();
        Assert.AreEqual(24, missingRows.Count, "replies whose final line node is absent from the DLG (offered now, blank line)");
        Assert.AreEqual(14, rows.Count - missingRows.Count, "replies whose final line node is a Player record (planned as the final line since ARC-SCRIPT-2804 unless the record's own action holds a dialog transfer)");
        // The Player record's own action runs as an NPC line's does: a dialog transfer inside it (a nested fl or jo) is outside the subset (Stringy Pete's record 256 holds an fl, Vollinger's record 246 a jo).
        var nested = shapes.Where(shape => Regex.IsMatch(shape.Split("|codes [")[1], @"(^|[ \[])(fl|jo|uw|et)([ \]])")).Select(shape => shape.Split('|')[0]).Distinct().Order().ToArray();
        CollectionAssert.AreEqual(new[] { "01110stringy_pete.dlg", "01650vollinger.dlg" }, nested, "the Player records whose own action holds a dialog transfer");
        Assert.AreEqual(14, shapes.Count);
        Assert.AreEqual(4, shapes.Count(shape => Regex.IsMatch(shape.Split("|codes [")[1], @"(^|[ \[])(fl|jo|uw|et)([ \]])")), "refused replies; the other 10 hold no nested transfer");
        Assert.IsTrue(rows.Contains("02792fahrkus.dlg|reply 42|fl 179|missing|has co True"), "the owner's report 2026-11-19_08-20-50: Fahrkus' kill reply");
    }

    [TestMethod, TestCategory("RealData"), TestCategory("DlgRealData")]
    public void Transfer_replies_with_a_bad_response_target_are_counted_in_the_installed_dialogs()
    {
        // ARC-SCRIPT-2807 (ledger FLM3): the reply's own response number is skipped after a dialog transfer in its action (0x413130), so a missing or non-NPC response record is no failure for such a reply.
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in response-target census; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var rows = new List<string>();
        int withTransfer = 0;
        int badResponseAll = 0;
        foreach (var key in stack.Keys().Where(k => k.EndsWith(".dlg", StringComparison.Ordinal)))
        {
            var document = DlgReader.Read(stack.Read(stack.Resolve(key).Winner!).Bytes!, new DlgReaderOptions { Mode = DlgReadMode.Original });
            foreach (var reply in document.Records.Where(r => r.Role == DlgRecordRole.Player))
            {
                var value = reply.ResponseValue ?? 0;
                var badResponse = value > 0 && (!document.TryGetRecord(value, out var response) || response is null || response.Role != DlgRecordRole.Npc);
                if (badResponse) badResponseAll++;
                var action = Encoding.Latin1.GetString(reply.Action.Bytes.ToArray());
                var matches = Regex.Matches(action, @"(?<![a-z])(?<code>fl|jo|uw|et)\s*(?<first>-?\d+)", RegexOptions.IgnoreCase);
                if (matches.Count > 0) withTransfer++;
                if (matches.Count == 0 || !badResponse) continue;
                var codes = string.Join('+', matches.Select(m => m.Groups["code"].Value.ToLowerInvariant()));
                var missing = !document.TryGetRecord(value, out _);
                rows.Add($"{Path.GetFileName(key)}|reply {reply.Id}|response {value} {(missing ? "missing" : "not an NPC record")}|{codes}");
            }
        }

        Console.WriteLine($"RESPONSE-CENSUS|replies with a transfer {withTransfer}|bad response targets of all replies {badResponseAll}|transfer replies with a bad response target {rows.Count}");
        foreach (var row in rows) Console.WriteLine($"RESPONSE-CENSUS-ROW|{row}");
        Assert.AreEqual(3076, withTransfer, "replies whose action holds fl, jo, uw or et with a number");
        Assert.AreEqual(2, badResponseAll, "replies of all kinds whose response number names no NPC record");
        CollectionAssert.AreEqual(new[] { "01718theo_brightstart.dlg|reply 66|response 73 not an NPC record|et" }, rows, "the only installed transfer reply with a bad response record (an expert teacher reply; no fl reply has one)");
    }

    // Only the structure of a Player record used as a final line: the text marker letter, whether the text, gender and action fields are empty, and the action's code words (never the original text).
    private static string Shape(DlgRecord record)
    {
        var text = record.Text.Bytes;
        var marker = text.Count >= 2 && text[0] < 0x80 && text[1] == (byte)':' ? ((char)text[0]).ToString() : "-";
        var action = Encoding.Latin1.GetString(record.Action.Bytes.ToArray());
        var codes = string.Join(' ', Regex.Matches(action, @"(?<![a-z])[a-z]{2}(?![a-z])", RegexOptions.IgnoreCase).Select(m => m.Value.ToLowerInvariant()));
        return $"role {record.Role}|marker {marker}|text empty {text.Count == 0}|gender field empty {record.AlternateText.Bytes.Count == 0}|action empty {record.Action.Bytes.Count == 0}|codes [{codes}]";
    }
}
