using System.Text;
using System.Text.RegularExpressions;
using Arcanum.Data;
using Arcanum.Data.Content;
using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Scripting.Tests.Dialog;

/// <summary>
/// Opt-in census of ARC-WORLD-054 (<c>DAT_ROOT</c>, the game folder): the FP action in every installed DLG the content stack
/// resolves, read in original mode. None carries an operand, and every field with one parses up to it under the fate grammar
/// (a stop is at an operation outside the subset, never at FP). Only counts are compared and printed, never original text.
/// </summary>
[TestClass]
public sealed partial class DialogFateRealDataTests
{
    private static readonly DialogConversationOptions Options = new();

    [GeneratedRegex("(?<![A-Za-z])fp", RegexOptions.IgnoreCase)]
    private static partial Regex FateOperation();

    [TestMethod, TestCategory("RealData"), TestCategory("DlgRealData")]
    public void Every_installed_fp_action_parses_under_the_fate_grammar()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in DLG check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var files = new HashSet<string>(StringComparer.Ordinal);
        int withOperand = 0, inConditions = 0, fieldsWhole = 0, fieldsStopped = 0, stoppedAtFp = 0;
        foreach (var key in stack.Keys().Where(k => k.EndsWith(".dlg", StringComparison.Ordinal)))
        {
            var document = DlgReader.Read(stack.Read(stack.Resolve(key).Winner!).Bytes!, new DlgReaderOptions { Mode = DlgReadMode.Original });
            foreach (var record in document.Records)
            {
                inConditions += FateOperation().Matches(Encoding.Latin1.GetString([.. record.Condition.Bytes])).Count;
                var field = record.Action;
                var text = Encoding.Latin1.GetString([.. field.Bytes]);
                var matches = FateOperation().Matches(text);
                if (matches.Count == 0) continue;
                var role = record.Role == DlgRecordRole.Npc ? ConversationField.NpcAction : ConversationField.PlayerAction;
                counts[role.ToString()] = counts.GetValueOrDefault(role.ToString()) + matches.Count;
                files.Add(key);
                foreach (Match match in matches)
                {
                    var rest = text[(match.Index + 2)..].TrimStart(' ', '\t');
                    if (rest.Length > 0 && (char.IsDigit(rest[0]) || rest[0] is '-' or '+')) withOperand++;
                }
                try
                {
                    DialogConversationGrammar.Parse(record, field, role, Options, party: true, skills: true, fate: true);
                    fieldsWhole++;
                }
                catch (DialogOperationParseException exception)
                {
                    fieldsStopped++;
                    var start = exception.OperationSource.OperationOffsetInField;
                    if (start + 2 <= text.Length && text.Substring(start, 2).Equals("fp", StringComparison.OrdinalIgnoreCase)) stoppedAtFp++;
                }
            }
        }
        foreach (var (name, count) in counts) Console.WriteLine($"{name}/FP: {count}");
        Console.WriteLine($"files {files.Count}; with an operand {withOperand}; in conditions {inConditions}; fields whole {fieldsWhole} stopped {fieldsStopped} at FP {stoppedAtFp}");
        // First observation 2026-10-21 (709 files).
        CollectionAssert.AreEqual(new[] { "NpcAction 17", "PlayerAction 27" }, counts.Select(p => $"{p.Key} {p.Value}").ToArray());
        Assert.AreEqual((19, 0, 0, 0), (files.Count, withOperand, inConditions, stoppedAtFp));
        // 13 fields with FP parse whole; 31 stop at an operation outside the subset before or after it.
        Assert.AreEqual((13, 31), (fieldsWhole, fieldsStopped));
    }
}
