using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Arcanum.Data.Content;

namespace Arcanum.ContentProbe;

// Read-only research report for ARC-DATA-011. It prints source paths, member names,
// sizes and SHA-256 digests only; it never prints payload bytes.
internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.SequenceEqual(["--self-test"])) return SelfTest();
            if (args.Length is 2 or 4 && args[0] == "--game-root" && (args.Length == 2 || args[2] == "--module"))
                return Report(args[1], args.Length == 4 ? args[3] : ContentMountPlan.DefaultModule, Console.Out);
            Console.Error.WriteLine("usage: --self-test | --game-root PATH [--module NAME]");
            return 2;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"content-probe: FAIL: {error.GetType().Name}: {error.Message}");
            return 1;
        }
    }

    private static int Report(string gameRoot, string module, TextWriter output)
    {
        var discovery = ContentMountPlan.Discover(gameRoot, module);
        if (discovery.Status != ContentMountStatus.Ready) throw new InvalidDataException($"MOUNT_{discovery.Status} {discovery.Detail}");
        var plan = discovery.Plan!;
        var before = plan.SearchOrder.Where(s => s.Kind == ContentSourceKind.Archive).Select(s => Fingerprint(s.Path)).ToArray();
        var opened = ContentStack.Open(plan);
        if (opened.Status != ContentStackStatus.Ready) throw new InvalidDataException($"STACK_{opened.Status} {Relative(plan, opened.Detail ?? "")}");
        using var stack = opened.Stack!;

        output.WriteLine($"module: {plan.ModuleName}");
        output.WriteLine();
        output.WriteLine("| search | role | source | bytes | sha256 |");
        output.WriteLine("|---:|---|---|---:|---|");
        var archiveIndex = 0;
        for (var i = 0; i < plan.SearchOrder.Count; i++)
        {
            var source = plan.SearchOrder[i];
            var fingerprint = source.Kind == ContentSourceKind.Archive ? before[archiveIndex++] : default;
            output.WriteLine(source.Kind == ContentSourceKind.Archive
                ? $"| {i + 1} | {Role(source)} | `{Relative(plan, source.Path)}` | {fingerprint.Bytes:N0} | `{fingerprint.Sha256}` |"
                : $"| {i + 1} | {Role(source)} | `{Relative(plan, source.Path)}/` | folder | |");
        }
        output.WriteLine();
        output.WriteLine($"keys: {stack.KeyCount:N0}; excluded TIGCache files: {stack.ExcludedCacheFiles}; skipped non-ASCII loose names: {stack.SkippedLooseFiles}");

        var overrides = stack.Overrides();
        output.WriteLine($"overridden paths: {overrides.Count}");
        output.WriteLine();
        output.WriteLine("| chain (winner first) | paths | identical payloads |");
        output.WriteLine("|---|---:|---:|");
        var rows = overrides.Select(o =>
        {
            var digests = o.Candidates.Select(c =>
            {
                var read = stack.Read(c);
                if (read.Status != ContentReadStatus.Read) throw new InvalidDataException($"READ_{read.Status} {o.Key}");
                return (Length: read.Bytes!.Length, Sha: Sha(read.Bytes));
            }).ToArray();
            return (Override: o, Chain: string.Join(" > ", o.Candidates.Select(c => Relative(plan, c.Source.Path))), Digests: digests);
        }).ToArray();
        foreach (var group in rows.GroupBy(r => r.Chain).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal))
            output.WriteLine($"| `{group.Key}` | {group.Count()} | {group.Count(r => r.Digests.All(d => d.Sha == r.Digests[0].Sha))} |");
        output.WriteLine();
        output.WriteLine("| path | winner | shadowed | payload |");
        output.WriteLine("|---|---|---|---|");
        foreach (var row in rows)
        {
            string Copy(int index)
            {
                var candidate = row.Override.Candidates[index];
                var place = candidate.Entry is { } entry ? $"`{Relative(plan, candidate.Source.Path)}`#{entry.Ordinal}" : $"`{Relative(plan, candidate.FilePath!)}`";
                return $"{place} ({row.Digests[index].Length:N0} B)";
            }
            var same = row.Digests.All(d => d.Sha == row.Digests[0].Sha);
            output.WriteLine($"| `{row.Override.Key}` | {Copy(0)} | {string.Join(", ", Enumerable.Range(1, row.Digests.Length - 1).Select(Copy))} | {(same ? "identical" : "differs")} |");
        }

        var after = plan.SearchOrder.Where(s => s.Kind == ContentSourceKind.Archive).Select(s => Fingerprint(s.Path)).ToArray();
        if (!before.SequenceEqual(after)) throw new InvalidDataException("INPUT_CHANGED");
        return 0;
    }

    private static int SelfTest()
    {
        var root = Path.Combine(Path.GetTempPath(), "arc-content-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            Archive(Path.Combine(root, "tig.dat"));
            Archive(Path.Combine(root, "arcanum1.dat"), ("rules\\a.mes", "old"), ("rules\\b.mes", "same"), ("rules\\c.mes", "base"));
            Directory.CreateDirectory(Path.Combine(root, "data", "rules"));
            File.WriteAllText(Path.Combine(root, "data", "rules", "c.mes"), "loose");
            Archive(Path.Combine(root, "Arcanum2.dat"), ("rules\\a.mes", "new"), ("rules\\b.mes", "same"));
            Archive(Path.Combine(root, "Modules", "Synthetic.dat"));
            Archive(Path.Combine(root, "Modules", "Synthetic.patch0"), ("rules\\b.mes", "patched"));
            var writer = new StringWriter();
            Report(root, "Synthetic", writer);
            var text = writer.ToString();
            string[] expected =
            [
                "| 1 | patch0 | `modules/synthetic.patch0` |",
                "| 4 | base[1] | `arcanum2.dat` |",
                "overridden paths: 3",
                "| `data > arcanum1.dat` | 1 | 0 |",
                "| `rules/c.mes` | `data/rules/c.mes` (5 B) | `arcanum1.dat`#2 (4 B) | differs |",
                "| `arcanum2.dat > arcanum1.dat` | 1 | 0 |",
                "| `modules/synthetic.patch0 > arcanum2.dat > arcanum1.dat` | 1 | 0 |",
                "| `rules/a.mes` | `arcanum2.dat`#0 (3 B) | `arcanum1.dat`#0 (3 B) | differs |",
            ];
            foreach (var line in expected)
                if (!text.Contains(line, StringComparison.Ordinal)) throw new InvalidOperationException($"self-test missing: {line}");
            Console.WriteLine("content-probe self-test: PASS (mount order, archive and loose chains, payload comparison)");
            return 0;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void Archive(string path, params (string Name, string Text)[] entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var output = new MemoryStream();
        var offsets = entries.Select(e => { var at = (uint)output.Position; output.Write(Encoding.ASCII.GetBytes(e.Text)); return at; }).ToArray();
        using var index = new MemoryStream();
        U32(index, (uint)entries.Length);
        for (var i = 0; i < entries.Length; i++)
        {
            var name = Encoding.ASCII.GetBytes(entries[i].Name + "\0");
            U32(index, (uint)name.Length); index.Write(name); index.Write(new byte[4]);
            U32(index, 1); U32(index, (uint)entries[i].Text.Length); U32(index, (uint)entries[i].Text.Length); U32(index, offsets[i]);
        }
        index.Position = 0; index.CopyTo(output);
        output.Write(new byte[16]); output.Write("1TAD"u8);
        U32(output, (uint)entries.Sum(e => e.Name.Length + 1)); U32(output, (uint)(index.Length + 28));
        File.WriteAllBytes(path, output.ToArray());
    }

    private static void U32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static string Role(ContentSource source) => source.Role switch
    {
        ContentSourceRole.ModulePatch => $"patch{source.Number}",
        ContentSourceRole.BaseArchive => $"base[{source.Number}]",
        ContentSourceRole.ModuleDirectory => "module folder",
        ContentSourceRole.ModuleArchive => "module",
        ContentSourceRole.DataDirectory => "data folder",
        ContentSourceRole.GameRoot => "game folder",
        _ => "tig",
    };

    private static string Relative(ContentMountPlan plan, string path)
        => Path.GetRelativePath(plan.GameRoot, path).Replace('\\', '/').ToLowerInvariant();

    private readonly record struct InputFingerprint(long Bytes, string Sha256);

    private static InputFingerprint Fingerprint(string path)
    {
        using var stream = File.OpenRead(path);
        return new(stream.Length, Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());
    }

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
