using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Session.Tests.Verification;

/// <summary>
/// ARC-DEV-1106 (V4 of the function audit plan): a method whose summary names an executable function but carries no
/// <c>OriginalFunction</c> mark is invisible to the mark and row tests, so every such method has one of three states:
/// <b>marked</b> (the mark, checked against the registry by the other tests), <b>exempt</b> (a row in
/// <c>docs/verification/address-methods.csv</c> with the class <c>host</c> or <c>reference</c>) or <b>pending</b> (a row with no class:
/// a known gap that the V1/V2 slices close; the count may only go down). A method in none of the three states fails the test.
/// </summary>
public sealed partial class FunctionRegistryTests
{
    private const string AddressMethodsFile = "docs/verification/address-methods.csv";
    private const string AddressMethodsHeader = "file,member,state,class";
    private static readonly string[] AddressMethodStates = ["exempt", "pending"];
    private static readonly string[] AddressMethodClasses = ["host", "reference"];

    /// <summary>Number of <c>pending</c> rows in the file; lower it when a row is closed, never raise it (a new address-naming method is marked or exempt).</summary>
    private const int PendingCeiling = 743;

    /// <summary>A method (or, for overloads, the member name in one file) whose summary names a census function.</summary>
    private sealed record AddressMethod(string File, string Member, bool Marked, int Line, IReadOnlyList<uint> Addresses);

    [TestMethod]
    public void AddressNamedMethodDetectorFollowsItsRules()
    {
        var census = new HashSet<uint> { 0x4B7080, 0x4B7150 };
        List<AddressMethod> Scan(string source) => AddressNamedMethods("x.cs", source, census);

        // Unmarked method naming a census function.
        var found = Scan("class A {\n    /// <summary>Port of 0x4B7080.</summary>\n    public static int Roll(int x)\n    {\n        return x;\n    }\n}\n");
        Assert.AreEqual(1, found.Count);
        Assert.AreEqual("Roll", found[0].Member);
        Assert.IsFalse(found[0].Marked);
        Assert.AreEqual(3, found[0].Line);

        // The mark on its own line, with a second attribute and a multi-line mark.
        Assert.IsTrue(Scan("/// <summary>Rule 0x4b7080.</summary>\n[OriginalFunction(0x4B7080, OriginalFunctionScope.Full)]\npublic static int Roll(int x) => x;\n").Single().Marked);
        Assert.IsTrue(Scan("/// <summary>Rule 0x4B7080.</summary>\n[MethodImpl(MethodImplOptions.AggressiveInlining)]\n[OriginalFunction(0x4B7080,\n    OriginalFunctionScope.Partial)]\nstatic int Roll(int x) => x;\n").Single().Marked);

        // An address that is not a census function, a second address in the same summary, and a summary without one.
        Assert.AreEqual(0, Scan("/// <summary>Label 0x4B7081.</summary>\npublic int Roll() => 1;\n").Count);
        Assert.AreEqual(1, Scan("/// <summary>0x4B7081 and 0x4B7150.</summary>\npublic int Roll() => 1;\n").Single().Addresses.Count);
        Assert.AreEqual(0, Scan("/// <summary>Plain.</summary>\npublic int Roll() => 1;\n").Count);
        // The address only in a remark or a plain comment is not a summary.
        Assert.AreEqual(0, Scan("/// <summary>Plain.</summary>\n/// <remarks>0x4B7080</remarks>\npublic int Roll() => 1;\n").Count);

        // Types, constructors, properties, fields and enum members are not methods.
        Assert.AreEqual(0, Scan("/// <summary>0x4B7080</summary>\npublic sealed record Roll(int X);\n").Count);
        Assert.AreEqual(0, Scan("/// <summary>0x4B7080</summary>\ninternal sealed class Roll(string text)\n{\n}\n").Count);
        Assert.AreEqual(0, Scan("/// <summary>0x4B7080</summary>\npublic Roll(int x) { }\n").Count);
        Assert.AreEqual(0, Scan("/// <summary>0x4B7080</summary>\npublic int Roll => 1;\n").Count);
        Assert.AreEqual(0, Scan("/// <summary>0x4B7080</summary>\nprivate static readonly Roll Table = Make(1);\n").Count);

        // A tuple return type, a generic method and a method with no modifier.
        Assert.AreEqual("Pair", Scan("/// <summary>0x4B7080</summary>\ninternal static (int A, int B) Pair(int x) => (x, x);\n").Single().Member);
        Assert.AreEqual("Pick", Scan("/// <summary>0x4B7080</summary>\npublic static T Pick<T>(T a) => a;\n").Single().Member);
        Assert.AreEqual("Bare", Scan("/// <summary>0x4B7080</summary>\nint Bare(int x) => x;\n").Single().Member);

        // A plain comment or a preprocessor line between the doc comment and the declaration, and an attribute on the declaration's own line.
        Assert.AreEqual("Roll", Scan("/// <summary>0x4B7080</summary>\n// note\n#pragma warning disable\npublic int Roll() => 1;\n").Single().Member);
        Assert.AreEqual(4, Scan("/// <summary>0x4B7080</summary>\n// note\n[Obsolete]\npublic int Roll() => 1;\n").Single().Line);
        Assert.IsFalse(Scan("/// <summary>0x4B7080</summary>\n[Obsolete] public int Roll() => 1;\n").Single().Marked);
        Assert.IsTrue(Scan("/// <summary>0x4B7080</summary>\n[OriginalFunction(0x4B7080, OriginalFunctionScope.Full)] public int Roll() => 1;\n").Single().Marked);
        Assert.AreEqual("Roll", Scan("/// <summary>0x4B7080</summary>\n[A][B(new int[1])] public int Roll(int[] x) => 1;\n").Single().Member);
        Assert.AreEqual(0, Scan("/// <summary>0x4B7080</summary>\n[Obsolete] public sealed record Roll(int X);\n").Count);

        // An interface member and an abstract declaration have no body; a signature over several lines does.
        Assert.AreEqual(0, Scan("/// <summary>0x4B7080</summary>\nbool Roll(int x);\n").Count);
        Assert.AreEqual(0, Scan("/// <summary>0x4B7080</summary>\npublic abstract int Roll(int x);\n").Count);
        Assert.AreEqual(1, Scan("/// <summary>0x4B7080</summary>\npublic static int Roll(\n    int x,\n    int y)\n{\n    return x;\n}\n").Count);

        // Multi-line summary.
        Assert.AreEqual(1, Scan("/// <summary>\n/// first line\n/// then 0x4B7150\n/// </summary>\npublic int Roll() => 1;\n").Count);
    }

    [TestMethod]
    public void EveryAddressNamedMethodIsMarkedExemptOrPending()
    {
        var census = CensusAddresses();
        Assert.IsTrue(census.Count > 5000, "the census lists the executable's functions");
        var found = new Dictionary<(string File, string Member), AddressMethod>();
        foreach (var file in TrackedFiles(Root(), ["src", "tools", "experiments"]).Where(f => f.EndsWith(".cs", StringComparison.Ordinal)))
        {
            var path = Path.Combine(Root(), file);
            if (!File.Exists(path)) continue; // deleted in the working tree, not yet in the index
            foreach (var method in AddressNamedMethods(file, File.ReadAllText(path), census))
            {
                var key = (method.File, method.Member);
                // Overloads share one row: the member counts as marked only when every overload is.
                found[key] = found.TryGetValue(key, out var earlier) && !earlier.Marked ? earlier : method;
            }
        }

        var rows = AddressMethodRows();
        var unmarked = found.Values.Where(m => !m.Marked).ToList();
        var missing = unmarked.Where(m => !rows.ContainsKey((m.File, m.Member))).ToList();
        Assert.AreEqual(0, missing.Count,
            $"{missing.Count} method(s) name an executable function in their summary but have no OriginalFunction mark and no row in {AddressMethodsFile}: "
            + "mark the method and add its registry row, or add an exempt row (class host: it only calls a marked rule; class reference: it cites the address and implements none). "
            + string.Join("; ", missing.Take(20).Select(m => $"{m.File}:{m.Line} {m.Member}")));

        var stale = rows.Keys.Where(k => !unmarked.Any(m => (m.File, m.Member) == k)).ToList();
        Assert.AreEqual(0, stale.Count,
            $"{stale.Count} row(s) of {AddressMethodsFile} name a member that is marked now, no longer names an executable function or no longer exists: remove them. "
            + string.Join("; ", stale.Take(20).Select(k => $"{k.File} {k.Member}")));

        var pending = rows.Values.Count(r => r.State == "pending");
        Assert.AreEqual(PendingCeiling, pending, $"{pending} pending rows, {nameof(PendingCeiling)} is {PendingCeiling}: a new address-naming method is marked or exempt, never pending, and a closed row lowers the constant");
    }

    private sealed record AddressMethodRow(string File, string Member, string State, string Class);

    private static Dictionary<(string File, string Member), AddressMethodRow> AddressMethodRows()
    {
        var lines = System.IO.File.ReadAllLines(Path.Combine(Root(), AddressMethodsFile)).Where(l => l.Length > 0).ToList();
        Assert.AreEqual(AddressMethodsHeader, lines[0], "address-methods header");
        var rows = new Dictionary<(string File, string Member), AddressMethodRow>();
        AddressMethodRow? previous = null;
        foreach (var line in lines.Skip(1))
        {
            var f = Csv(line);
            Assert.AreEqual(4, f.Count, $"address-methods line '{line}'");
            var row = new AddressMethodRow(f[0], f[1], f[2], f[3]);
            CollectionAssert.Contains(AddressMethodStates, row.State, $"{row.File} {row.Member}: state");
            if (row.State == "exempt") CollectionAssert.Contains(AddressMethodClasses, row.Class, $"{row.File} {row.Member}: an exempt row names its class");
            else Assert.AreEqual(0, row.Class.Length, $"{row.File} {row.Member}: a pending row has no class");
            Assert.IsTrue(System.IO.File.Exists(Path.Combine(Root(), row.File)), $"{row.File}: no such file");
            if (previous is not null)
                Assert.IsTrue(string.CompareOrdinal(previous.File + "\u0001" + previous.Member, row.File + "\u0001" + row.Member) < 0,
                    $"{row.File} {row.Member}: rows are sorted by file and member, one row per member");
            Assert.IsTrue(rows.TryAdd((row.File, row.Member), row), $"{row.File} {row.Member}: duplicate row");
            previous = row;
        }
        return rows;
    }

    private static HashSet<uint> CensusAddresses() =>
        System.IO.File.ReadLines(Path.Combine(Root(), "docs", "verification", "census.csv")).Skip(1)
            .Where(l => l.StartsWith("0x", StringComparison.Ordinal))
            .Select(l => Convert.ToUInt32(l[2..l.IndexOf(',')], 16)).ToHashSet();

    /// <summary>The methods of one C# source whose <c>&lt;summary&gt;</c> names a function of the census, each with whether it carries an
    /// <c>OriginalFunction</c> mark. A method is a declaration with a return type and a parameter list that follows the doc comment and its
    /// attributes; types, constructors, properties and fields are left out. Line numbers count from 1 and point at the declaration.</summary>
    private static List<AddressMethod> AddressNamedMethods(string file, string source, HashSet<uint> census)
    {
        var lines = source.Split('\n');
        var found = new List<AddressMethod>();
        var i = 0;
        while (i < lines.Length)
        {
            if (!lines[i].TrimStart().StartsWith("///", StringComparison.Ordinal)) { i++; continue; }
            var j = i;
            while (j < lines.Length && lines[j].TrimStart().StartsWith("///", StringComparison.Ordinal)) j++;
            var doc = string.Join('\n', lines[i..j]);
            var attributes = new List<string>();
            var k = j;
            var declarationText = "";
            // Between the doc comment and the declaration: attributes (also over several lines), plain comments and preprocessor lines.
            while (k < lines.Length)
            {
                var trimmed = lines[k].TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith('#')) { k++; continue; }
                if (!trimmed.StartsWith('[')) break;
                var attribute = new System.Text.StringBuilder();
                var depth = 0;
                var rest = "";
                do
                {
                    var line = lines[k];
                    attribute.Append(line).Append('\n');
                    for (var c = 0; c < line.Length; c++)
                    {
                        if (line[c] == '[') depth++;
                        else if (line[c] == ']' && --depth == 0)
                        {
                            var tail = line[(c + 1)..].TrimStart();
                            if (tail.StartsWith('[')) continue; // another attribute follows on the line
                            rest = tail;
                            break;
                        }
                    }
                    k++;
                } while (depth > 0 && k < lines.Length);
                attributes.Add(attribute.ToString());
                // An attribute and the declaration on one line: the declaration is what follows the last attribute.
                if (rest.Length > 0 && !rest.StartsWith("//", StringComparison.Ordinal)) { declarationText = rest; k--; break; }
            }
            i = j;
            if (k >= lines.Length) break;
            var summary = Summary().Match(doc);
            if (!summary.Success) continue;
            var addresses = SummaryAddress().Matches(summary.Groups[1].Value)
                .Select(m => Convert.ToUInt32(m.Groups[1].Value, 16)).Where(census.Contains).Distinct().ToList();
            if (addresses.Count == 0) continue;
            var declaration = MethodDeclaration().Match(declarationText.Length > 0 ? declarationText : lines[k]);
            if (!declaration.Success || TypeKeyword().IsMatch(declaration.Groups["type"].Value) || ModifiersOnly().IsMatch(declaration.Groups["type"].Value) || !HasBody(lines, k, declarationText)) continue;
            found.Add(new AddressMethod(file, declaration.Groups["name"].Value, attributes.Any(a => a.Contains("OriginalFunction", StringComparison.Ordinal)), k + 1, addresses));
        }
        return found;
    }

    /// <summary>True when the declaration at <paramref name="start"/> (or in <paramref name="firstLine"/>, when an attribute shares its line) has a body (a block or an expression): an interface member or an abstract
    /// or extern declaration ends in a semicolon and cannot carry a mark. The signature may span several lines.</summary>
    private static bool HasBody(string[] lines, int start, string firstLine)
    {
        var text = string.Join('\n', lines[(firstLine.Length > 0 ? start + 1 : start)..Math.Min(lines.Length, start + 12)].Prepend(firstLine.Length > 0 ? firstLine : ""));
        var end = text.IndexOfAny(['{', ';']);
        var arrow = text.IndexOf("=>", StringComparison.Ordinal);
        return arrow >= 0 && (end < 0 || arrow < end) || end >= 0 && text[end] == '{';
    }

    [GeneratedRegex(@"<summary>(.*?)</summary>", RegexOptions.Singleline)]
    private static partial Regex Summary();

    [GeneratedRegex(@"0x([0-9A-Fa-f]{6})\b")]
    private static partial Regex SummaryAddress();

    [GeneratedRegex(@"^\s*(?:(?:public|private|internal|protected|static|sealed|override|virtual|async|unsafe|partial|readonly|extern|new|abstract)\s+)*(?<type>[\w<>\[\],.?()\s]+?)\s+(?<name>\w+)\s*(?:<[^>]*>)?\s*\(")]
    private static partial Regex MethodDeclaration();

    // A constructor has no return type: its modifiers would otherwise be read as one.
    [GeneratedRegex(@"^\s*(?:(?:public|private|internal|protected|static|sealed|override|virtual|async|unsafe|partial|readonly|extern|new|abstract)\s*)+$")]
    private static partial Regex ModifiersOnly();

    // A type declaration (or a statement such as "return Foo(") has a keyword where a return type would be.
    [GeneratedRegex(@"\b(?:record|class|struct|interface|enum|delegate|return|new|throw|await)\b")]
    private static partial Regex TypeKeyword();
}
