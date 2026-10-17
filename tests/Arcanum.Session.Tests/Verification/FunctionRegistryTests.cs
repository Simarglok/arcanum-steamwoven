using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Arcanum.Data.Fixes;
using Arcanum.Scripting.Scr;
using Arcanum.Simulation;
using Arcanum.Tests.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Session.Tests.Verification;

/// <summary>
/// ARC-DEV-008: the function registry (<c>docs/verification/functions.csv</c>), the <c>OriginalFunction</c> attribute, the
/// oracle receipts, the rule-mode switch registry and the count of lines that still name the reconstruction stay consistent.
/// </summary>
[TestClass]
public sealed partial class FunctionRegistryTests
{
    private const string ExeSha256 = "9bd80511f8663a41a25a20a701959e394e0065e4ffc68a8dc69fe19c23678d52";
    private const string Header = "address,name,domain,status,reason,level,evidence,method";
    private static readonly string[] Statuses = ["full", "partial", "not implemented", "out of scope"];
    private static readonly Assembly[] Implementations =
        [typeof(DataFixProfile).Assembly, typeof(OriginalFunctionAttribute).Assembly, typeof(ScrRuleProfile).Assembly, typeof(GameRules).Assembly];

    public TestContext TestContext { get; set; } = null!;

    private sealed record Row(uint Address, string Key, string Name, string Domain, string Status, string Reason, string Level, string Evidence, string Method)
    {
        public bool Implemented => Status is "full" or "partial";
    }

    private sealed record Marked(uint Address, string Scope, MethodInfo Method);

    [TestMethod]
    public void RegistryRowsAreWellFormed()
    {
        var rows = Rows();
        Assert.IsTrue(rows.Count > 0);
        Assert.AreEqual(rows.Count, rows.Select(r => r.Address).Distinct().Count(), "one row per address");
        CollectionAssert.AreEqual(rows.Select(r => r.Address).Order().ToList(), rows.Select(r => r.Address).ToList(), "rows sorted by address");
        foreach (var row in rows)
        {
            // Steamwoven's own names: lower-case words, never a reconstruction's snake_case identifier.
            Assert.IsTrue(OwnName().IsMatch(row.Name), $"{row.Key}: name '{row.Name}' is not a plain lower-case name");
            Assert.IsTrue(OwnName().IsMatch(row.Domain), $"{row.Key}: domain '{row.Domain}'");
            CollectionAssert.Contains(Statuses, row.Status, $"{row.Key}: status");
            Assert.AreEqual(row.Status == "full", row.Reason.Length == 0, $"{row.Key}: a reason is required exactly when the status is not full");
            Assert.IsTrue(Level().IsMatch(row.Level), $"{row.Key}: level {row.Level}");
            Assert.AreEqual(row.Implemented, row.Method.Length > 0, $"{row.Key}: an implemented row names its method, another names none");
            var file = row.Evidence.Split('#')[0];
            Assert.IsTrue(file.Length > 0 && File.Exists(Path.Combine(Root(), file)), $"{row.Key}: evidence {row.Evidence} does not exist");
        }
    }

    [TestMethod]
    public void EveryMarkedMethodHasItsRowAndEveryImplementedRowItsMethod()
    {
        var rows = Rows().ToDictionary(r => r.Address);
        var marked = MarkedMethods();
        Assert.AreEqual(marked.Count, marked.Select(m => m.Address).Distinct().Count(), "one marked method per address");
        foreach (var mark in marked)
        {
            var name = MethodIlHash.Name(mark.Method);
            Assert.IsTrue(rows.TryGetValue(mark.Address, out var row), $"{name} is marked 0x{mark.Address:X} but the registry has no row for it");
            Assert.IsTrue(row.Implemented, $"0x{mark.Address:X} is {row.Status} in the registry but {name} is marked");
            Assert.AreEqual(row.Status, mark.Scope.ToLowerInvariant(), $"0x{mark.Address:X}: registry status and attribute scope");
            Assert.AreEqual(row.Method, name, $"0x{mark.Address:X}: registry method");
        }
        var markedAddresses = marked.Select(m => m.Address).ToHashSet();
        foreach (var row in rows.Values.Where(r => r.Implemented))
            Assert.IsTrue(markedAddresses.Contains(row.Address), $"{row.Key} is {row.Status} but no method carries OriginalFunction(0x{row.Key})");
        // The reflection scan covers the assemblies this project references; an attribute anywhere else in the tracked C#
        // sources (Presentation, Input, the viewer, tools) would escape it, so the sources are read too.
        foreach (var file in TrackedFiles(Root(), ["src", "tools", "experiments"]).Where(f => f.EndsWith(".cs", StringComparison.Ordinal)))
        {
            var path = Path.Combine(Root(), file);
            if (!File.Exists(path) || file.EndsWith("OriginalFunctionAttribute.cs", StringComparison.Ordinal)) continue;
            foreach (Match match in SourceMark().Matches(File.ReadAllText(path)))
                Assert.IsTrue(markedAddresses.Contains(Convert.ToUInt32(match.Groups[1].Value, 16)),
                    $"{file} marks 0x{match.Groups[1].Value} in an assembly the registry test does not load; add the assembly to {nameof(Implementations)}");
        }
    }

    [TestMethod]
    public void EveryRuleModeSwitchHasALedgerId()
    {
        var registry = SwitchRegistry();
        var switches = typeof(GameRules).GetProperties()
            .Where(p => p.PropertyType.GetProperty("Original", BindingFlags.Public | BindingFlags.Static) is not null
                || p.PropertyType.GetField("Original", BindingFlags.Public | BindingFlags.Static) is not null)
            .SelectMany(p => p.PropertyType.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(s => s.PropertyType == typeof(bool))
                .Select(s => (Profile: p.PropertyType.Name, Switch: s.Name)))
            .ToList();
        Assert.IsTrue(switches.Count >= 16, "the profiles of GameRules expose their switches");
        foreach (var (profile, name) in switches)
        {
            Assert.IsTrue(registry.TryGetValue((profile, name), out var ledger), $"{profile}.{name} has no row in the rule-mode registry");
            Assert.IsTrue(LedgerId().IsMatch(ledger), $"{profile}.{name} has no ledger ID in the rule-mode registry ('{ledger}')");
        }
        foreach (var key in registry.Keys)
            Assert.IsTrue(switches.Contains(key), $"the rule-mode registry lists {key.Item1}.{key.Item2}, which the code does not have");
    }

    [TestMethod]
    public void EveryF3RowMatchesItsOracleReceipt()
    {
        var marked = MarkedMethods().ToDictionary(m => m.Address);
        var rows = Rows();
        var f3 = rows.Where(r => r.Level == "F3").ToList();
        var receipts = Path.Combine(Root(), "docs", "verification", "receipts");
        foreach (var file in Directory.Exists(receipts) ? Directory.GetFiles(receipts, "*.json") : [])
            Assert.IsTrue(f3.Any(r => r.Key == Path.GetFileNameWithoutExtension(file)), $"receipt {Path.GetFileName(file)} has no F3 row");
        foreach (var row in f3)
        {
            Assert.IsTrue(row.Implemented, $"{row.Key}: an F3 row is implemented");
            var path = Path.Combine(receipts, row.Key + ".json");
            Assert.AreEqual($"docs/verification/receipts/{row.Key}.json", row.Evidence, $"{row.Key}: an F3 row's evidence is its receipt");
            Assert.IsTrue(File.Exists(path), $"{row.Key}: no oracle receipt; run ./arcanum oracle --function 0x{row.Key}");
            using var receipt = JsonDocument.Parse(File.ReadAllBytes(path));
            var r = receipt.RootElement;
            Assert.AreEqual(1, r.GetProperty("format").GetInt32());
            Assert.AreEqual($"0x{row.Key}", r.GetProperty("function").GetString(), $"{row.Key}: receipt function");
            Assert.AreEqual(row.Method, r.GetProperty("method").GetString(), $"{row.Key}: receipt method");
            Assert.AreEqual("pass", r.GetProperty("result").GetString(), $"{row.Key}: the receipt records a failed run");
            Assert.IsTrue(r.GetProperty("inputs").GetInt32() >= 10_000, $"{row.Key}: fewer than 10,000 inputs");
            var planted = r.GetProperty("mutantsPlanted").GetInt32();
            Assert.IsTrue(planted >= 2 && r.GetProperty("mutantsCaught").GetInt32() == planted, $"{row.Key}: mutants");
            Assert.AreEqual(ExeSha256, r.GetProperty("exeSha256").GetString(), $"{row.Key}: receipt executable");
            var method = marked[row.Address].Method;
            var configuration = MethodIlHash.Configuration(method);
            if (r.GetProperty("configuration").GetString() != configuration)
                Assert.Inconclusive($"{row.Key}: the receipt was written from a {r.GetProperty("configuration").GetString()} build, this is {configuration}");
            Assert.AreEqual(r.GetProperty("ilSha256").GetString(), MethodIlHash.Compute(method),
                $"{row.Key}: {row.Method} changed since its oracle receipt; run ./arcanum oracle --function 0x{row.Key} and commit the receipt");
        }
    }

    [TestMethod]
    public void LinesNamingTheReconstructionStayWithinTheBaseline()
    {
        var root = Root();
        var baseline = File.ReadAllLines(Path.Combine(root, "docs", "verification", "ce-references.csv"))
            .Skip(1).Where(l => l.Length > 0).Select(l => l.Split(',')).ToDictionary(p => p[0], p => int.Parse(p[1]));
        var counts = CountReconstructionLines(root);
        CollectionAssert.AreEquivalent(baseline.Keys.ToList(), counts.Keys.ToList(), "baseline areas");
        foreach (var (area, count) in counts.OrderBy(p => p.Key, StringComparer.Ordinal))
            TestContext.WriteLine($"{area}: {count} lines (baseline {baseline[area]})");
        var above = counts.Where(p => p.Value > baseline[p.Key]).Select(p => $"{p.Key} {p.Value} > {baseline[p.Key]}").ToList();
        Assert.AreEqual(0, above.Count, "more lines name the reconstruction than the baseline allows (new work cites the executable): " + string.Join(", ", above));
        // A ratchet: lines removed are not credit for later additions, so the baseline drops in the same change.
        var below = counts.Where(p => p.Value < baseline[p.Key]).Select(p => $"{p.Key} {p.Value}").ToList();
        Assert.AreEqual(0, below.Count, "fewer lines name the reconstruction than the baseline; lower docs/verification/ce-references.csv to: " + string.Join(", ", below));
    }

    /// <summary>Per area, the lines of tracked text files that name the reconstruction: its repository name, its pinned commit,
    /// the word CE (also as an identifier prefix such as <c>CE_</c>) or a Markdown reference label <c>[ce]</c>.</summary>
    internal static Dictionary<string, int> CountReconstructionLines(string root)
    {
        string[] areas = ["AGENTS.md", "CLAUDE.md", "docs", "src", "tests", "tools"];
        var counts = areas.ToDictionary(a => a, _ => 0);
        foreach (var file in TrackedFiles(root, areas))
        {
            if (file.EndsWith(".lock.json", StringComparison.Ordinal) || file.EndsWith(".lock", StringComparison.Ordinal)) continue;
            var path = Path.Combine(root, file);
            if (!File.Exists(path)) continue; // deleted in the working tree, not yet in the index
            var bytes = File.ReadAllBytes(path);
            if (bytes.AsSpan(0, Math.Min(bytes.Length, 8192)).IndexOf((byte)0) >= 0) continue;
            var area = file.Contains('/') ? file[..file.IndexOf('/')] : file;
            counts[area] += Encoding.UTF8.GetString(bytes).Split('\n').Count(line => Reconstruction().IsMatch(line));
        }
        return counts;
    }

    private static IEnumerable<string> TrackedFiles(string root, string[] areas)
    {
        var start = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { "-C", root, "ls-files", "-z", "--" }.Concat(areas)) start.ArgumentList.Add(argument);
        using var git = Process.Start(start) ?? throw new InvalidOperationException("git did not start");
        var output = git.StandardOutput.ReadToEnd();
        git.WaitForExit();
        Assert.AreEqual(0, git.ExitCode, "git ls-files failed: " + git.StandardError.ReadToEnd());
        return output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    private static List<Row> Rows()
    {
        var lines = File.ReadAllLines(Path.Combine(Root(), "docs", "verification", "functions.csv")).Where(l => l.Length > 0).ToList();
        Assert.AreEqual(Header, lines[0], "registry header");
        return lines.Skip(1).Select(line =>
        {
            var f = Csv(line);
            Assert.AreEqual(8, f.Count, $"registry line '{line}'");
            Assert.IsTrue(AddressPattern().IsMatch(f[0]), $"address {f[0]} (0x and upper-case hex)");
            var address = Convert.ToUInt32(f[0][2..], 16);
            return new Row(address, f[0][2..], f[1], f[2], f[3], f[4], f[5], f[6], f[7]);
        }).ToList();
    }

    private static List<Marked> MarkedMethods() => Implementations
        .SelectMany(a => a.GetTypes())
        .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        .SelectMany(m => m.GetCustomAttributesData().Where(a => a.AttributeType.Name == "OriginalFunctionAttribute")
            .Select(a => new Marked((uint)a.ConstructorArguments[0].Value!, Enum.GetName(a.ConstructorArguments[1].ArgumentType, a.ConstructorArguments[1].Value!)!, m)))
        .ToList();

    /// <summary>(profile, switch) → ledger ID from the "Implemented switches" table of docs/world/rule-modes.md.</summary>
    private static Dictionary<(string, string), string> SwitchRegistry()
    {
        var text = File.ReadAllLines(Path.Combine(Root(), "docs", "world", "rule-modes.md"));
        var start = Array.FindIndex(text, l => l.StartsWith("| Domain | Profile | Switch | Ledger ID |", StringComparison.Ordinal));
        Assert.IsTrue(start >= 0, "rule-mode registry table");
        var result = new Dictionary<(string, string), string>();
        foreach (var line in text.Skip(start + 2).TakeWhile(l => l.StartsWith('|')))
        {
            var cells = line.Split('|').Select(c => c.Trim()).ToArray();
            result.Add((cells[2].Trim('`'), cells[3].Trim('`')), cells[4]);
        }
        return result;
    }

    private static List<string> Csv(string line)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted && c == '"' && i + 1 < line.Length && line[i + 1] == '"') { field.Append('"'); i++; }
            else if (c == '"') quoted = !quoted;
            else if (c == ',' && !quoted) { fields.Add(field.ToString()); field.Clear(); }
            else field.Append(c);
        }
        fields.Add(field.ToString());
        return fields;
    }

    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Arcanum.Steamwoven.sln"))) return directory.FullName;
        throw new InvalidOperationException("repository root (Arcanum.Steamwoven.sln) not found");
    }

    [GeneratedRegex("^0x[0-9A-F]{6,8}$")]
    private static partial Regex AddressPattern();

    [GeneratedRegex("^[a-z0-9][a-z0-9 '-]*$")]
    private static partial Regex OwnName();

    [GeneratedRegex("^F[0-4]$")]
    private static partial Regex Level();

    [GeneratedRegex(@"(?i:arcanum-ce)|(?i:a7ff41b)|(?<![A-Za-z0-9_])CE(?![A-Za-z0-9])|(?i:\[ce\])")]
    private static partial Regex Reconstruction();

    [GeneratedRegex(@"OriginalFunction(?:Attribute)?\s*\(\s*(?:address\s*:\s*)?0x([0-9A-Fa-f]+)")]
    private static partial Regex SourceMark();

    // A ledger ID names a row: letters and a number (A2, OQ2, DF1, reputation A2, X3, tile).
    [GeneratedRegex(@"[A-Za-z]+\d")]
    private static partial Regex LedgerId();
}
