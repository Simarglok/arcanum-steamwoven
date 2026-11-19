using System.Text;
using System.Text.RegularExpressions;
using Arcanum.Data;
using Arcanum.Data.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Scripting.Tests.Dialog;

/// <summary>
/// Opt-in census of ARC-SCRIPT-2790 (<c>DAT_ROOT</c>, the game folder): the Player replies whose action string holds a final line <c>fl N</c> to a node that is not an NPC record of the same DLG.
/// A node absent from the DLG is no error in the original (the line stays blank, the string goes on, the dialog ends; ledger FM1) and the reply is offered; a node that is a Player record stays
/// greyed as <c>TargetInvalid</c> until the original's use of such a record is read (follow-up issue). Only counts and ids are printed, never original text.
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
                }
            }
        }

        Console.WriteLine($"FL-CENSUS|final lines {transfers}|bad targets {rows.Count}");
        foreach (var row in rows) Console.WriteLine($"FL-CENSUS-ROW|{row}");
        var missingRows = rows.Where(row => row.Contains("|missing|", StringComparison.Ordinal)).ToList();
        Assert.AreEqual(24, missingRows.Count, "replies whose final line node is absent from the DLG (offered now, blank line)");
        Assert.AreEqual(14, rows.Count - missingRows.Count, "replies whose final line node is a Player record (still refused)");
        Assert.IsTrue(rows.Contains("02792fahrkus.dlg|reply 42|fl 179|missing|has co True"), "the owner's report 2026-11-19_08-20-50: Fahrkus' kill reply");
    }
}
