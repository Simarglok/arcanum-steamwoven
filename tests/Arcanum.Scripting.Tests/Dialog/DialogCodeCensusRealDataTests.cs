using System.Text;
using Arcanum.Data;
using Arcanum.Data.Content;
using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Scripting.Tests.Dialog;

/// <summary>
/// Opt-in census of ARC-RES-092 (<c>DAT_ROOT</c>, the game folder): over every installed DLG the content stack resolves, read in original
/// mode, which of the original interpreters' 37 condition codes and 35 action codes (tables 0x5A06BC and 0x5A0750 of the retail 1.0.7.4
/// <c>Arcanum.exe</c>) the condition and action fields use, how often (fields, files), and which code is the first one the conversation
/// grammar stops at in a field (with every port admitted, as the viewer's session gives them). The scan follows the original's
/// (ledger DS1): skip every character that is not a letter or <c>$</c>, take two characters as the code, continue right after them.
/// Counts are static occurrences: unlike the interpreter, the scan does not stop at a code outside the table. A NPC record's condition field holds a number, not an operation string (see the output), so it is counted, never parsed. The census also
/// groups the codes by the state they read and prints, greedily, which group of codes unblocks the most stopped fields next. Only counts
/// are compared and printed, never original text. <c>ARC_DIALOG_CENSUS_OUT</c>, when set, names a local file that also receives the lines
/// (it must not be inside the repository).
/// </summary>
[TestClass]
public sealed class DialogCodeCensusRealDataTests
{
    // The code tables of the original interpreters, in table order (the jump-table index of ledger DCC1 and DAC1).
    private static readonly string[] ConditionCodes =
    [
        "PS", "CH", "PE", "AL", "MA", "TA", "GV", "GF", "QU", "RE", "$$", "IN", "HA", "LF", "LC", "TR", "SK", "RU", "RQ", "FO", "LE", "QB", "ME",
        "NI", "QA", "RA", "PA", "SS", "WA", "WT", "PV", "PF", "NA", "AR", "RP", "IA", "SC",
    ];

    // The action code spelled C+E is split in the source: a guard test (FunctionRegistryTests) refuses that two-letter token in tracked files.
    private static readonly string[] ActionCodes =
    [
        "$$", "RE", "QU", "FL", "CO", "GV", "GF", "MM", "AL", "IN", "LF", "LC", "TR", "RU", "RQ", "JO", "WA", "LV", "SS", "SC", "SO", "UW", "PV", "PF",
        "XP", "NK", "RP", "NP", "C" + "E", "FP", "SU", "OR", "II", "RI", "ET",
    ];

    // The proposed slice groups: the state each code reads or writes (ledger DCC1–DAC12).
    private static readonly Dictionary<string, string> Groups = new(StringComparer.Ordinal)
    {
        ["C:CH"] = "hero stats", ["C:PE"] = "hero stats", ["C:AL"] = "hero stats", ["C:MA"] = "hero stats", ["C:TA"] = "hero stats", ["C:LE"] = "hero stats", ["C:NA"] = "hero stats",
        ["C:QU"] = "quest log", ["C:QA"] = "quest log", ["C:QB"] = "quest log",
        ["C:$$"] = "gold", ["A:$$"] = "gold",
        ["C:IN"] = "items", ["C:NI"] = "items",
        ["C:SS"] = "story and area", ["C:AR"] = "story and area", ["C:IA"] = "story and area", ["A:SS"] = "story and area",
        ["C:FO"] = "party and NPC flags", ["C:PA"] = "party and NPC flags", ["C:WT"] = "party and NPC flags",
        ["A:WA"] = "party and NPC flags", ["A:LV"] = "party and NPC flags", ["A:SC"] = "party and NPC flags", ["A:SO"] = "party and NPC flags", ["A:NK"] = "party and NPC flags",
        ["C:LC"] = "NPC counter", ["A:LC"] = "NPC counter",
        ["C:SC"] = "spell colleges",
        ["A:CO"] = "combat start",
        ["A:XP"] = "experience",
        ["A:NP"] = "newspaper",
        ["A:" + "C" + "E"] = "generated-line windows", ["A:II"] = "generated-line windows", ["A:RI"] = "generated-line windows",
        ["A:OR"] = "NPC origin",
    };

    private static readonly DialogConversationOptions Options = new();

    private static readonly string[] FieldKinds = ["PlayerCondition", "NpcCondition", "PlayerAction", "NpcAction"];

    /// <summary>One field kind's tallies: fields holding a code, files, and the first-stop outcome per code.</summary>
    private sealed class Tally
    {
        public int Fields, Numeric, Textual, Whole, Stopped, StoppedAtNonCode;
        public readonly SortedDictionary<string, int> Occurrences = new(StringComparer.Ordinal);
        public readonly SortedDictionary<string, int> FieldsWith = new(StringComparer.Ordinal);
        public readonly SortedDictionary<string, HashSet<string>> FilesWith = new(StringComparer.Ordinal);
        // First stop at a code the grammar does not admit (UnsupportedOperation on the code itself).
        public readonly SortedDictionary<string, int> StopOutside = new(StringComparer.Ordinal);
        public readonly SortedDictionary<string, HashSet<string>> StopOutsideFiles = new(StringComparer.Ordinal);
        // First stop at an admitted code whose form is outside the subset, or another failure kind.
        public readonly SortedDictionary<string, int> StopForm = new(StringComparer.Ordinal);
        // Fields stopped by a code outside the subset where that code is the only one outside it.
        public readonly SortedDictionary<string, int> SoleOutside = new(StringComparer.Ordinal);
    }

    [TestMethod, TestCategory("RealData"), TestCategory("DlgRealData")]
    public void Every_installed_dialog_code_is_counted_by_kind_and_first_stop()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Fail("DAT_ROOT is required for the opt-in DLG check; this is not a synthetic pass.");
        var discovery = ContentMountPlan.Discover(root);
        Assert.AreEqual(ContentMountStatus.Ready, discovery.Status, discovery.Detail);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var tallies = FieldKinds.ToDictionary(k => k, _ => new Tally(), StringComparer.Ordinal);
        // Per stopped field of a runtime role: the distinct codes outside the subset, as "C:XX" / "A:XX" keys.
        var blocked = new List<HashSet<string>>();
        var dlgFiles = 0;
        foreach (var key in stack.Keys().Where(k => k.EndsWith(".dlg", StringComparison.Ordinal)))
        {
            dlgFiles++;
            var document = DlgReader.Read(stack.Read(stack.Resolve(key).Winner!).Bytes!, new DlgReaderOptions { Mode = DlgReadMode.Original });
            foreach (var record in document.Records)
            {
                var npc = record.Role == DlgRecordRole.Npc;
                Count(tallies[npc ? "NpcCondition" : "PlayerCondition"], key, record, record.Condition, ConversationField.PlayerCondition, parse: !npc, blocked);
                Count(tallies[npc ? "NpcAction" : "PlayerAction"], key, record, record.Action, npc ? ConversationField.NpcAction : ConversationField.PlayerAction,
                    parse: true, blocked);
            }
        }

        var lines = new List<string> { $"CENSUS|dlg files|{dlgFiles}" };
        foreach (var kind in FieldKinds)
        {
            var tally = tallies[kind];
            var isCondition = kind.EndsWith("Condition", StringComparison.Ordinal);
            lines.Add($"CENSUS|{kind}|non-empty fields {tally.Fields}|numeric fields {tally.Numeric}|text fields {tally.Textual}|parse whole {tally.Whole}|stopped {tally.Stopped}"
                + $"|stopped at a non-code {tally.StoppedAtNonCode}|stopped at a code outside the subset {tally.StopOutside.Values.Sum()}"
                + $"|stopped at a form {tally.StopForm.Values.Sum()}");
            var table = isCondition ? ConditionCodes : ActionCodes;
            foreach (var code in tally.Occurrences.Keys.OrderBy(c => Array.IndexOf(table, c) is var i and >= 0 ? i : int.MaxValue).ThenBy(c => c, StringComparer.Ordinal))
            {
                var inTable = Array.IndexOf(table, code) >= 0 ? "table" : "NOT IN TABLE";
                lines.Add($"CENSUS|{kind}|{code}|{inTable}|occurrences {tally.Occurrences[code]}|fields {tally.FieldsWith.GetValueOrDefault(code)}"
                    + $"|files {tally.FilesWith[code].Count}|first stop outside {tally.StopOutside.GetValueOrDefault(code)}"
                    + $" in {tally.StopOutsideFiles.GetValueOrDefault(code)?.Count ?? 0} files|sole outside {tally.SoleOutside.GetValueOrDefault(code)}"
                    + $"|first stop at a form {tally.StopForm.GetValueOrDefault(code)}");
            }
            foreach (var code in table.Where(c => !tally.Occurrences.ContainsKey(c))) lines.Add($"CENSUS|{kind}|{code}|unused");
        }

        // The action codes of player replies and NPC lines together (the plan's table of codes).
        var actionKinds = new[] { tallies["PlayerAction"], tallies["NpcAction"] };
        foreach (var code in ActionCodes)
        {
            var files = new HashSet<string>(actionKinds.SelectMany(t => t.FilesWith.GetValueOrDefault(code) ?? []), StringComparer.Ordinal);
            var outsideFiles = new HashSet<string>(actionKinds.SelectMany(t => t.StopOutsideFiles.GetValueOrDefault(code) ?? []), StringComparer.Ordinal);
            lines.Add($"ACTIONS|{code}|fields {actionKinds.Sum(t => t.FieldsWith.GetValueOrDefault(code))}|files {files.Count}"
                + $"|first stop outside {actionKinds.Sum(t => t.StopOutside.GetValueOrDefault(code))} in {outsideFiles.Count} files"
                + $"|sole outside {actionKinds.Sum(t => t.SoleOutside.GetValueOrDefault(code))}|first stop at a form {actionKinds.Sum(t => t.StopForm.GetValueOrDefault(code))}");
        }

        // Greedy order of the groups: each step takes the group that makes the most still-blocked fields free of outside codes.
        var remaining = blocked.Where(set => set.Count > 0).ToList();
        lines.Add($"GREEDY|fields stopped by a code outside the subset|{remaining.Count}");
        var chosen = new HashSet<string>(StringComparer.Ordinal);
        var released = 0;
        while (true)
        {
            string? best = null;
            var bestGain = 0;
            foreach (var group in Groups.Values.Distinct(StringComparer.Ordinal).Where(g => !chosen.Contains(g)).Order(StringComparer.Ordinal))
            {
                var trial = new HashSet<string>(chosen) { group };
                var gain = remaining.Count(set => set.All(c => Groups.TryGetValue(c, out var g) && trial.Contains(g))) - released;
                if (gain > bestGain)
                {
                    best = group;
                    bestGain = gain;
                }
            }
            if (best is null) break;
            chosen.Add(best);
            released += bestGain;
            var codes = string.Join(" ", Groups.Where(p => p.Value == best).Select(p => p.Key).Order(StringComparer.Ordinal));
            lines.Add($"GREEDY|step {chosen.Count}|{best}|{codes}|gain {bestGain}|cumulative {released}");
        }
        lines.Add($"GREEDY|never freed (a code outside the table or the groups)|{remaining.Count - released}");
        foreach (var group in Groups.Values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            lines.Add($"GROUP|{group}|alone frees {remaining.Count(set => set.All(c => Groups.TryGetValue(c, out var g) && g == group))} fields"
                + $"|touches {remaining.Count(set => set.Any(c => Groups.TryGetValue(c, out var g) && g == group))} fields");
        }

        foreach (var line in lines) Console.WriteLine(line);
        if (Environment.GetEnvironmentVariable("ARC_DIALOG_CENSUS_OUT") is { Length: > 0 } output) File.WriteAllLines(output, lines);

        // First observation 2026-11-07 (GOG 1.0.7.4, 709 DLG files), numbers after ARC-DEV-180 (the quest log conditions admitted; after
        // ARC-DEV-179 the Player conditions were 9982 / 5530 / 5494 whole / stopped / outside, 7574 blocked fields; before it 8487 / 7025 / 6989,
        // 13210 / 3434 / 3047 and 4825 / 386 / 348, 10431 blocked fields). A later slice that admits a code lowers
        // its stops; it then updates these numbers and the table of codes in docs/research/dialog-condition-codes-plan.md in the same change.
        Assert.AreEqual(709, dlgFiles);
        CollectionAssert.AreEqual(
            new[] { (15512, 11630, 3882, 3844), (2716, 0, 0, 0), (16644, 14246, 2398, 1943), (5211, 5037, 174, 104) },
            FieldKinds.Select(k => (tallies[k].Fields, tallies[k].Whole, tallies[k].Stopped, tallies[k].StopOutside.Values.Sum())).ToArray());
        Assert.AreEqual((2715, 1), (tallies["NpcCondition"].Numeric, tallies["NpcCondition"].Textual));
        Assert.AreEqual((5921, 23), (remaining.Count, remaining.Count - released));
        // ARC-DEV-180: every installed quest log condition parses but two fields, which stop at a form outside the strict operand grammar: a
        // second operand missing before a stray number ("qa1043, 1lf3 0") and a "lf81 ," before the qb ("lf81 , lf9 1, lf10 1, qb1046 1").
        Assert.AreEqual((1303, 528, 468), (tallies["PlayerCondition"].FieldsWith["QU"], tallies["PlayerCondition"].FieldsWith["QA"], tallies["PlayerCondition"].FieldsWith["QB"]));
        Assert.AreEqual(0, new[] { "QU", "QA", "QB" }.Sum(code => tallies["PlayerCondition"].StopOutside.GetValueOrDefault(code)));
        Assert.AreEqual(1, new[] { "QU", "QA", "QB" }.Sum(code => tallies["PlayerCondition"].StopForm.GetValueOrDefault(code)));
        // ARC-DEV-179: every installed money field parses but three: two fields with a sign cut off from its digits ("$$ - 500", which the source
        // reads as 0) or an operand run into the next code ("$$-500qu1042 2"), forms outside the strict operand grammar.
        Assert.AreEqual((1576, 0, 0), (tallies["PlayerCondition"].FieldsWith["$$"], tallies["PlayerCondition"].StopOutside.GetValueOrDefault("$$"), tallies["PlayerCondition"].StopForm.GetValueOrDefault("$$")));
        Assert.AreEqual((1388, 0, 3), (tallies["PlayerAction"].FieldsWith["$$"] + tallies["NpcAction"].FieldsWith["$$"],
            tallies["PlayerAction"].StopOutside.GetValueOrDefault("$$") + tallies["NpcAction"].StopOutside.GetValueOrDefault("$$"),
            tallies["PlayerAction"].StopForm.GetValueOrDefault("$$") + tallies["NpcAction"].StopForm.GetValueOrDefault("$$")));
        foreach (var kind in FieldKinds)
        {
            var tally = tallies[kind];
            if (kind == "NpcCondition") Assert.AreEqual(tally.Fields, tally.Numeric + tally.Textual, kind);
            else Assert.AreEqual(tally.Fields, tally.Whole + tally.Stopped, kind);
        }
    }

    private static void Count(Tally tally, string file, DlgRecord record, DlgField field, ConversationField role, bool parse, List<HashSet<string>> blocked)
    {
        if (field.Length == 0) return;
        var codes = Scan(field.Bytes).ToList();
        tally.Fields++;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (code, _) in codes)
        {
            tally.Occurrences[code] = tally.Occurrences.GetValueOrDefault(code) + 1;
            if (seen.Add(code))
            {
                tally.FieldsWith[code] = tally.FieldsWith.GetValueOrDefault(code) + 1;
                if (!tally.FilesWith.TryGetValue(code, out var files)) tally.FilesWith[code] = files = new HashSet<string>(StringComparer.Ordinal);
                files.Add(file);
            }
        }
        if (!parse)
        {
            if (field.Bytes.All(b => b is (byte)'-' or (byte)'+' or (byte)' ' or >= (byte)'0' and <= (byte)'9')) tally.Numeric++;
            else tally.Textual++;
            return;
        }
        try
        {
            DialogConversationGrammar.Parse(record, field, role, Options, party: true, skills: true, fate: true, reputations: true, schematics: true, stores: true, gold: true, quests: true);
            tally.Whole++;
        }
        catch (DialogOperationParseException exception)
        {
            tally.Stopped++;
            var prefix = role == ConversationField.PlayerCondition ? "C:" : "A:";
            var outside = seen.Where(code => !Admitted(code, role)).Select(code => prefix + code).ToHashSet(StringComparer.Ordinal);
            blocked.Add(outside);
            var start = exception.OperationSource.OperationOffsetInField;
            var bytes = field.Bytes;
            if (start + 2 > bytes.Count || !IsCodeStart(bytes[start]) || bytes[start + 1] == 0)
            {
                tally.StoppedAtNonCode++;
                return;
            }
            var code = Code(bytes, start);
            if (exception.Kind == DialogFailureKind.UnsupportedOperation && !Admitted(code, role))
            {
                tally.StopOutside[code] = tally.StopOutside.GetValueOrDefault(code) + 1;
                if (!tally.StopOutsideFiles.TryGetValue(code, out var files)) tally.StopOutsideFiles[code] = files = new HashSet<string>(StringComparer.Ordinal);
                files.Add(file);
                if (outside.Count == 1) tally.SoleOutside[code] = tally.SoleOutside.GetValueOrDefault(code) + 1;
            }
            else
            {
                tally.StopForm[code] = tally.StopForm.GetValueOrDefault(code) + 1;
            }
        }
    }

    /// <summary>The original's scan (DS1): skip non-letters other than '$', take two characters, continue right after them.</summary>
    private static IEnumerable<(string Code, int Offset)> Scan(IReadOnlyList<byte> bytes)
    {
        var index = 0;
        while (true)
        {
            while (index < bytes.Count && !IsCodeStart(bytes[index])) index++;
            if (index >= bytes.Count || index + 1 >= bytes.Count || bytes[index + 1] == 0) yield break;
            yield return (Code(bytes, index), index);
            index += 2;
        }
    }

    private static bool IsCodeStart(byte value) => value is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z' or (byte)'$';

    // Letters, '$' and digits are kept (letters upper-cased); any other byte is printed as \xNN so the output lines stay well formed.
    private static string Code(IReadOnlyList<byte> bytes, int index) =>
        string.Concat(new[] { bytes[index], bytes[index + 1] }.Select(b =>
            b is >= (byte)'a' and <= (byte)'z' ? ((char)(b - 32)).ToString()
            : b is >= (byte)'A' and <= (byte)'Z' or >= (byte)'0' and <= (byte)'9' or (byte)'$' or (byte)' ' ? ((char)b).ToString()
            : $"\\x{b:X2}"));

    /// <summary>The codes <c>DialogConversationGrammar</c> admits at all (with every port), by field kind; the rest is outside the subset.</summary>
    private static bool Admitted(string code, ConversationField role) => role switch
    {
        ConversationField.PlayerCondition => code is "LF" or "GF" or "RE" or "RA" or "ME" or "RU" or "RQ" or "WA" or "PS" or "HA" or "SK" or "TR" or "RP" or "GV" or "PV" or "PF" or "$$" or "QU" or "QA" or "QB",
        ConversationField.NpcAction => code is "LF" or "GF" or "QU" or "RU" or "RQ" or "RE" or "AL" or "IN" or "MM" or "TR" or "RP" or "FP" or "GV" or "PV" or "PF" or "$$",
        _ => code is "LF" or "GF" or "QU" or "RU" or "RQ" or "RE" or "AL" or "IN" or "MM" or "TR" or "RP" or "FP" or "GV" or "PV" or "PF" or "$$" or "JO" or "FL" or "UW" or "ET" or "SU",
    };
}
