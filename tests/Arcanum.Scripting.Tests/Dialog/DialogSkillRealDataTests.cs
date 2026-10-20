using System.Text;
using System.Text.RegularExpressions;
using Arcanum.Data;
using Arcanum.Data.Content;
using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Scripting.Tests.Dialog;

/// <summary>
/// Opt-in census of ARC-SKILLS-004 (<c>DAT_ROOT</c>, the game folder): the skill operations PS, HA, SK, TR (conditions) and TR,
/// ET (actions) in every installed DLG the content stack resolves, read in original mode. Every one parses on its own under the
/// skill grammar, and a field with one parses up to it unless an operation outside the subset comes first. Only counts are
/// compared and printed, never original text.
/// </summary>
[TestClass]
public sealed partial class DialogSkillRealDataTests
{
    private static readonly DialogConversationOptions Options = new();

    [GeneratedRegex("(?<![A-Za-z])(ps|ha|sk|tr|et)", RegexOptions.IgnoreCase)]
    private static partial Regex SkillOperation();

    // One skill operation with its operands as the source reads them (PS/HA one number, SK/TR/ET up to two).
    [GeneratedRegex(@"\G(?:(?:ps|ha)[ \t]*[-+]?\d+|(?:sk|tr|et)[ \t]*[-+]?\d+(?:[ \t]*[-+]?\d+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex Operands();

    [TestMethod, TestCategory("RealData"), TestCategory("DlgRealData")]
    public void Every_installed_skill_operation_parses_under_the_skill_grammar()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in DLG check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var files = new SortedDictionary<string, HashSet<string>>(StringComparer.Ordinal);
        int fieldsWhole = 0, fieldsStopped = 0, alone = 0, refusedAlone = 0;
        var stoppedAt = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var key in stack.Keys().Where(k => k.EndsWith(".dlg", StringComparison.Ordinal)))
        {
            var document = DlgReader.Read(stack.Read(stack.Resolve(key).Winner!).Bytes!, new DlgReaderOptions { Mode = DlgReadMode.Original });
            foreach (var record in document.Records)
                foreach (var kind in new[] { DlgFieldKind.Condition, DlgFieldKind.Action })
                {
                    if (record.Role == DlgRecordRole.Npc && kind == DlgFieldKind.Condition) continue;
                    var field = record.Fields[(int)kind];
                    var text = Encoding.Latin1.GetString([.. field.Bytes]);
                    var matches = SkillOperation().Matches(text);
                    if (matches.Count == 0) continue;
                    var role = kind == DlgFieldKind.Condition ? ConversationField.PlayerCondition
                        : record.Role == DlgRecordRole.Npc ? ConversationField.NpcAction : ConversationField.PlayerAction;
                    foreach (Match match in matches)
                    {
                        var name = $"{role}/{match.Value.ToUpperInvariant()}";
                        counts[name] = counts.GetValueOrDefault(name) + 1;
                        (files.TryGetValue(name, out var set) ? set : files[name] = []).Add(key);
                        // The operation alone, with its operands.
                        var operands = Operands().Match(text, match.Index);
                        var end = operands.Success ? match.Index + operands.Length : match.Index + 2;
                        alone++;
                        if (!ParsesAlone(text[match.Index..end], role)) refusedAlone++;
                    }
                    try
                    {
                        DialogConversationGrammar.Parse(record, field, role, Options, party: true, skills: true);
                        fieldsWhole++;
                    }
                    catch (DialogOperationParseException exception)
                    {
                        // A stop is at an operation outside the subset (money, other codes) or after JO/ET, never at a skill operation.
                        fieldsStopped++;
                        var start = exception.OperationSource.OperationOffsetInField;
                        var code = start + 2 <= text.Length ? text.Substring(start, 2).ToUpperInvariant() : "?";
                        stoppedAt[code] = stoppedAt.GetValueOrDefault(code) + 1;
                        Assert.IsFalse(code is "PS" or "HA" or "SK" or "TR" or "ET" && exception.Kind != DialogFailureKind.UnsupportedOperation,
                            $"{key} record {record.Id}: a skill operation refused as {exception.Kind}");
                    }
                }
        }
        foreach (var (name, count) in counts) Console.WriteLine($"{name}: {count} in {files[name].Count} files");
        Console.WriteLine($"alone {alone} refused {refusedAlone}; fields whole {fieldsWhole} stopped {fieldsStopped} at {string.Join(", ", stoppedAt.Select(p => $"{p.Key} {p.Value}"))}");
        Assert.AreEqual(0, refusedAlone, "every installed skill operation parses on its own");
        // First observation 2026-10-21 (709 files).
        CollectionAssert.AreEqual(new[]
        {
            "NpcAction/TR 62", "PlayerAction/ET 118", "PlayerAction/TR 53", "PlayerCondition/HA 354", "PlayerCondition/PS 275",
            "PlayerCondition/SK 440", "PlayerCondition/TR 937",
        }, counts.Select(p => $"{p.Key} {p.Value}").ToArray());
        // 1,215 fields with a skill operation parse whole; 367 stop at an operation outside the subset (money 121, rumors 84, …).
        Assert.AreEqual((1215, 367, 121), (fieldsWhole, fieldsStopped, stoppedAt.GetValueOrDefault("$$")));
    }

    private static bool ParsesAlone(string operation, ConversationField role)
    {
        var condition = role == ConversationField.PlayerCondition ? operation : "";
        var action = role == ConversationField.PlayerCondition ? "" : operation;
        var document = DlgReader.Read(Encoding.Latin1.GetBytes($"{{1}}{{x}}{{}}{{{(role == ConversationField.NpcAction ? "" : "1")}}}{{{condition}}}{{{(role == ConversationField.NpcAction ? "" : "0")}}}{{{action}}}"));
        var record = document.Records[0];
        try
        {
            DialogConversationGrammar.Parse(record, record.Fields[(int)(role == ConversationField.PlayerCondition ? DlgFieldKind.Condition : DlgFieldKind.Action)],
                role, Options, party: true, skills: true);
            return true;
        }
        catch (DialogOperationParseException)
        {
            return false;
        }
    }
}
