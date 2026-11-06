using System.Text;
using System.Text.RegularExpressions;
using Arcanum.Data;
using Arcanum.Data.Content;
using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Scripting.Tests.Dialog;

/// <summary>
/// Opt-in census of ARC-DEV-152 (<c>DAT_ROOT</c>, the game folder): the GV, PV and PF operations in every installed DLG the content
/// stack resolves, read in original mode, by field role; how many fall outside the subset (a PF value other than 0 or 1, an index outside
/// the session's stores); and how many fields that hold one parse whole with the store port and how many stop at one of them. Only counts
/// are compared and printed, never original text.
/// </summary>
[TestClass]
public sealed partial class DialogScriptStoreRealDataTests
{
    private static readonly DialogConversationOptions Options = new();

    [GeneratedRegex(@"(?<![A-Za-z])(gv|pv|pf)[ \t]*(-?\d+)[ \t]+(-?\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex StoreOperation();

    [GeneratedRegex("(?<![A-Za-z])(gv|pv|pf)", RegexOptions.IgnoreCase)]
    private static partial Regex StoreCode();

    [TestMethod, TestCategory("RealData"), TestCategory("DlgRealData")]
    public void Every_installed_store_operation_is_counted_and_parses_under_the_store_grammar()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in DLG check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var files = new HashSet<string>(StringComparer.Ordinal);
        int codes = 0, flagValuesOutside = 0, indexesOutside = 0, fieldsWhole = 0, fieldsStopped = 0, stoppedAtStore = 0;
        foreach (var key in stack.Keys().Where(k => k.EndsWith(".dlg", StringComparison.Ordinal)))
        {
            var document = DlgReader.Read(stack.Read(stack.Resolve(key).Winner!).Bytes!, new DlgReaderOptions { Mode = DlgReadMode.Original });
            foreach (var record in document.Records)
            {
                foreach (var (field, role) in new[]
                {
                    (record.Condition, record.Role == DlgRecordRole.Player ? ConversationField.PlayerCondition : (ConversationField?)null),
                    (record.Action, record.Role == DlgRecordRole.Npc ? ConversationField.NpcAction : ConversationField.PlayerAction),
                })
                {
                    var text = Encoding.Latin1.GetString([.. field.Bytes]);
                    var found = StoreCode().Matches(text).Count;
                    if (found == 0) continue;
                    codes += found;
                    files.Add(key);
                    var side = role?.ToString() ?? "NpcCondition";
                    foreach (Match match in StoreOperation().Matches(text))
                    {
                        var code = match.Groups[1].Value.ToUpperInvariant();
                        counts[$"{side}/{code}"] = counts.GetValueOrDefault($"{side}/{code}") + 1;
                        var index = long.Parse(match.Groups[2].Value);
                        var value = long.Parse(match.Groups[3].Value);
                        if (code == "PF" && value is not (0 or 1)) flagValuesOutside++;
                        if (index < 0 || index >= (code == "GV" ? 2000 : 65536)) indexesOutside++;
                    }
                    if (role is not { } parsed) continue;
                    try
                    {
                        DialogConversationGrammar.Parse(record, field, parsed, Options, party: true, skills: true, fate: true, reputations: true,
                            schematics: true, stores: true);
                        fieldsWhole++;
                    }
                    catch (DialogOperationParseException exception)
                    {
                        fieldsStopped++;
                        var start = exception.OperationSource.OperationOffsetInField;
                        if (start + 2 <= text.Length && StoreCode().IsMatch(text.Substring(start, 2))) stoppedAtStore++;
                    }
                }
            }
        }
        foreach (var (name, count) in counts) Console.WriteLine($"{name}: {count}");
        Console.WriteLine($"files {files.Count}; codes {codes}; PF values outside 0/1 {flagValuesOutside}; indexes outside {indexesOutside}; "
            + $"fields whole {fieldsWhole} stopped {fieldsStopped} at a store operation {stoppedAtStore}");
        // First observation 2026-11-06 (GOG 1.0.7.4): the installed dialogs use GV only, every one well formed and inside the stores;
        // no PV or PF is installed. 469 fields stop at an operation outside the subset, none at a store operation.
        CollectionAssert.AreEqual(new[] { "NpcAction/GV 11", "PlayerAction/GV 650", "PlayerCondition/GV 624" },
            counts.Select(p => $"{p.Key} {p.Value}").ToArray());
        Assert.AreEqual((32, 1285, 0, 0, 0), (files.Count, codes, flagValuesOutside, indexesOutside, stoppedAtStore));
        Assert.AreEqual((757, 469), (fieldsWhole, fieldsStopped));
    }
}
