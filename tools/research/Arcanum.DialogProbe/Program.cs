using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Arcanum.Data;

namespace Arcanum.DialogProbe;

/// <summary>
/// Read-only, identifier-only inspection of one explicitly selected DLG DAT entry.
/// This is a research harness, not a runtime dialog reader or interpreter.
/// </summary>
internal static class Program
{
    private const int MaxEntryBytes = 1 * 1024 * 1024;
    private const int MaxDlgBytes = 512 * 1024;
    private const int MaxRecords = 10_000;
    private const int FieldsPerRecord = 7;
    private const int MaxFieldBytes = 4 * 1024;
    private const int MaxTraversalNodes = 256;
    private const int MaxExaminedReferences = 1_024;
    private const string DlgPrefix = "dlg\\";

    private static int Main(string[] args)
    {
        try
        {
            if (args.SequenceEqual(["--self-test"]))
            {
                SelfTest();
                Console.WriteLine("self-test: PASS (bounded seven-field DLG grammar, reference classification, and zero-read selector rejections)");
                return 0;
            }

            if (args.Length != 4 || args[0] != "--module" || args[2] != "--resource")
            {
                throw new ArgumentException("usage: --self-test | --module PATH --resource RAW_DLG_ID");
            }

            Probe(args[1], args[3]);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"dialog-probe: FAIL: {exception.GetType().Name}: {exception.Message}");
            return 1;
        }
    }

    private static void Probe(string archivePath, string rawResource)
    {
        var expectedName = ParseResourceSelector(rawResource);
        var before = InputFingerprint.Create(archivePath);
        using var archive = DatArchive.Open(archivePath, BoundedOptions());
        var result = InspectArchive(archive, expectedName);
        var after = InputFingerprint.Create(archivePath);
        if (before != after)
        {
            throw new InvalidDataException("The archive changed during the read-only probe.");
        }

        var entry = result.Entry;
        var document = result.Document;
        var graph = result.Graph;
        Console.WriteLine($"input bytes={before.Bytes} sha256={before.Sha256}");
        Console.WriteLine($"resource ordinal={entry.Ordinal} raw_id_sha256={Sha256(expectedName)} flags=0x{entry.Flags:x8} stored_bytes={entry.StoredSize} decoded_bytes={entry.DeclaredOutputSize} payload_sha256={Sha256(result.Payload)}");
        Console.WriteLine($"grammar records={document.Records.Count} npc_records={document.Records.Count(record => record.Role == Role.Npc)} player_records={document.Records.Count(record => record.Role == Role.Player)} empty_fields={document.EmptyFields} comment_lines={document.CommentLines} crlf={document.CrLf} lf={document.Lf} ascii_operational_fields=true");
        Console.WriteLine($"lengths field0={document.FieldLengths[0]} field1={document.FieldLengths[1]} field2={document.FieldLengths[2]} field3={document.FieldLengths[3]} field4={document.FieldLengths[4]} field5={document.FieldLengths[5]} field6={document.FieldLengths[6]}");
        Console.WriteLine($"operations conditions={FormatCounts(document.ConditionOpcodes)} actions={FormatCounts(document.ActionOpcodes)}");
        Console.WriteLine($"edges direct={graph.Direct} exit={graph.Exit} special={graph.Special} dangling={graph.Dangling} ambiguous={graph.Ambiguous} unresolved={graph.Unresolved} callbacks={graph.Callbacks}");
        Console.WriteLine($"research-selected-start node_id={graph.StartId?.ToString() ?? "none"} rationale={graph.StartRationale} included_nodes={graph.IncludedNodes} examined_references={graph.ExaminedReferences} frontier={graph.Frontier}");
        foreach (var row in graph.Rows)
        {
            Console.WriteLine($"slice node_id={row.NodeId} role={row.Role.ToString().ToLowerInvariant()} edge_status={row.EdgeStatus} condition={row.ConditionClass} action={row.ActionClass} required_service={row.RequiredService} note={row.Note}");
        }
        Console.WriteLine("integrity before_after_match=true");
    }

    private static ProbeResult InspectArchive(DatArchive archive, byte[] expectedName)
    {
        var matches = archive.Entries.Where(entry => RawNameEquals(entry.RawNameBytes, expectedName)).ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidDataException("The exact raw DLG identifier is missing or duplicated; no payload was read.");
        }

        var entry = matches[0];
        if (entry.Flags is not (0x001 or 0x002) || entry.StoredSize == 0 || entry.StoredSize > MaxEntryBytes ||
            entry.DeclaredOutputSize == 0 || entry.DeclaredOutputSize > MaxDlgBytes)
        {
            throw new InvalidDataException("The selected DLG entry violates the bounded read policy; no payload was read.");
        }

        var payload = archive.ReadEntry(entry);
        var document = DlgDocument.Parse(payload);
        var graph = GraphSummary.Create(document);
        return new ProbeResult(entry, payload, document, graph);
    }

    private static DatReaderOptions BoundedOptions() => new()
    {
        MaxStoredEntryBytes = MaxEntryBytes,
        MaxDecodedEntryBytes = MaxEntryBytes,
        MaxEntryCount = 200_000,
        MaxNameBytes = 4 * 1024,
        MaxTotalNameBytes = 16 * 1024 * 1024,
    };

    private static byte[] ParseResourceSelector(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 255 || !value.StartsWith(DlgPrefix, StringComparison.OrdinalIgnoreCase) ||
            !value.EndsWith(".dlg", StringComparison.OrdinalIgnoreCase) || value.Length == DlgPrefix.Length + ".dlg".Length ||
            value.Any(character => character is < ' ' or > '~') || value.Contains('\0') || value.Contains('/') ||
            value.Contains("..", StringComparison.Ordinal) || value.Contains(':') || value.EndsWith("\\", StringComparison.Ordinal) ||
            value[DlgPrefix.Length..].Contains('\\'))
        {
            throw new ArgumentException("Resource must be one exact printable-ASCII non-traversing raw dlg\\basename.dlg identifier.");
        }

        var bytes = Encoding.ASCII.GetBytes(value);
        return bytes;
    }

    private static bool RawNameEquals(IReadOnlyList<byte> raw, ReadOnlySpan<byte> expected) =>
        raw.Count == expected.Length + 1 && raw[^1] == 0 && raw.Take(expected.Length).SequenceEqual(expected.ToArray());

    private static string Sha256(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string FormatCounts(SortedDictionary<string, int> values) =>
        values.Count == 0 ? "none" : string.Join(',', values.Select(pair => $"{pair.Key}:{pair.Value}"));

    private static void SelfTest()
    {
        var valid = Bytes("// synthetic comment\r\n{100}{npc}{female}{}{}{200}{}\n{200}{player}{0}{5}{}{300}{}\n{300}{npc}{}{}{}{0}{}\n");
        var document = DlgDocument.Parse(valid);
        Assert(document.Records.Count == 3 && document.Records[0].Role == Role.Npc && document.Records[1].Role == Role.Player, "valid seven-field fixture did not parse");
        Assert(document.CommentLines == 1 && document.CrLf == 1 && document.Lf == 3, "comment/newline aggregates did not parse");
        var graph = GraphSummary.Create(document);
        Assert(graph.Direct == 1 && graph.StartId == 100, "valid direct edge/start selection failed");

        AssertRejected(() => DlgDocument.Parse(Bytes("{1}{x}")), "too few fields");
        AssertRejected(() => DlgDocument.Parse(Bytes("{1}{x}{}{1}{}{0}{}{extra}")), "too many fields");
        AssertRejected(() => DlgDocument.Parse(Bytes("{1{x}{}{1}{}{0}{}")), "nested brace");
        AssertRejected(() => DlgDocument.Parse(Bytes("{1}{x}{}{1}{}{0}")), "missing field");
        AssertRejected(() => DlgDocument.Parse(Bytes("{1}{x}{}{1}{}{0}{}{")), "open brace");
        AssertRejected(() => DlgDocument.Parse([ (byte)'{', 0, (byte)'}' ]), "NUL");
        AssertRejected(() => DlgDocument.Parse(Bytes("{not-id}{x}{}{1}{}{0}{}")), "invalid id");
        AssertRejected(() => DlgDocument.Parse(Bytes("{999999999999999999999}{x}{}{1}{}{0}{}")), "overflow id");
        AssertRejected(() => DlgDocument.Parse(Bytes("{1}{x}{}{0}{}{0}{}")), "zero role discriminator");
        AssertRejected(() => DlgDocument.Parse(Bytes("{1}{x}{}{1}{}{not-int}{}")), "invalid response");
        AssertRejected(() => DlgDocument.Parse(Bytes("// eof comment")), "EOF comment without record");
        AssertRejected(() => DlgDocument.Parse(Bytes("{" + new string('x', MaxFieldBytes + 1) + "}{}{}{1}{}{0}{}")), "field cap");
        AssertRejected(() => DlgDocument.Parse(Enumerable.Range(1, MaxRecords + 1).SelectMany(id => Bytes($"{{{id}}}{{x}}{{}}{{1}}{{}}{{0}}{{}}")).ToArray()), "record cap");
        AssertRejected(() => DlgDocument.Parse(new byte[MaxDlgBytes + 1]), "payload cap");

        AssertRejected(() => DlgDocument.Parse(Bytes("{1}{x}{}{1}{}{0}{} {1}{x}{}{1}{}{0}{}")), "duplicate id");
        var dangling = DlgDocument.Parse(Bytes("{1}{x}{}{1}{}{9}{} {2}{x}{}{}{}{0}{}"));
        Assert(GraphSummary.Create(dangling).Dangling == 1, "dangling target was not classified");
        var special = DlgDocument.Parse(Bytes("{1}{x}{}{1}{}{-9}{} {2}{x}{}{}{}{0}{}"));
        Assert(GraphSummary.Create(special).Special == 1, "negative special transition was not classified");
        var cycle = DlgDocument.Parse(Bytes("{1}{x}{}{1}{}{2}{} {2}{x}{}{}{}{0}{} {3}{x}{}{1}{}{2}{}"));
        Assert(GraphSummary.Create(cycle).Frontier == "cycle", "cycle-shaped references did not stop before reselecting an included node");
        var unsupported = DlgDocument.Parse(Bytes("{1}{x}{}{1}{??}{2}{} {2}{x}{}{}{}{0}{}"));
        Assert(GraphSummary.Create(unsupported).Unresolved == 1, "unsupported condition opcode was not fail-closed");
        var callback = DlgDocument.Parse(Bytes("{1}{x}{}{1}{}{2}{} {2}{x}{}{}{}{0}{SC}"));
        Assert(GraphSummary.Create(callback).Callbacks == 0, "target callback fixture must not classify a player callback");
        callback = DlgDocument.Parse(Bytes("{1}{x}{}{1}{}{2}{} {2}{x}{}{1}{}{0}{SC}"));
        Assert(GraphSummary.Create(callback).Callbacks == 1 && GraphSummary.Create(callback).Unresolved >= 1, "callback action was not a distinct fail-closed frontier");

        var opaqueText = DlgDocument.Parse([ (byte)'{', (byte)'1', (byte)'}', (byte)'{', 0xe9, (byte)'}', (byte)'{', (byte)'}', (byte)'{', (byte)'1', (byte)'}', (byte)'{', (byte)'}', (byte)'{', (byte)'0', (byte)'}', (byte)'{', (byte)'}' ]);
        Assert(opaqueText.Records.Count == 1, "opaque non-ASCII text bytes were not preserved structurally");
        var nodeCap = DlgDocument.Parse(Bytes("{1}{npc}{}{}{}{0}{}" + string.Concat(Enumerable.Range(2, MaxTraversalNodes + 2).Select(id => $"{{{id}}}{{player}}{{}}{{1}}{{}}{{{id + 10_000}}}{{}}")) + string.Concat(Enumerable.Range(2, MaxTraversalNodes + 2).Select(id => $"{{{id + 10_000}}}{{npc}}{{}}{{}}{{}}{{0}}{{}}"))));
        var cappedGraph = GraphSummary.Create(nodeCap);
        Assert(cappedGraph.IncludedNodes == MaxTraversalNodes && cappedGraph.Frontier == "traversal-node-cap", "node traversal cap was not enforced incrementally");

        AssertSelectorRejectsWithoutPayload("dlg\\..\\x.dlg");
        AssertSelectorRejectsWithoutPayload("dlg\\folder\\x.dlg");
        AssertSelectorRejectsWithoutPayload("dlg/x.dlg");
        AssertSelectorRejectsWithoutPayload("C:dlg\\x.dlg");
        AssertSelectorRejectsWithoutPayload("dlg\\x/../y.dlg");
        AssertSelectorRejectsWithoutPayload("dlg\\x.txt");
        AssertSelectorRejectsWithoutPayload("dlg\\.dlg");
        AssertSelectorRejectsWithoutPayload("dlg\\é.dlg");
        AssertSyntheticArchiveSelection();
    }

    private static void AssertSyntheticArchiveSelection()
    {
        var payload = Bytes("{1}{x}{}{1}{}{0}{}");
        var validName = Name("dlg\\fixture.dlg");
        var expectedName = ParseResourceSelector("dlg\\fixture.dlg");
        var duplicate = CreateSyntheticDat(new SyntheticDatEntry(validName, payload), new SyntheticDatEntry(validName, payload));
        WithSyntheticArchive(duplicate, (archive, stream) =>
        {
            AssertRejectedWithReason(() => InspectArchive(archive, expectedName), "duplicate archive selector", "The exact raw DLG identifier is missing or duplicated");
            AssertPayloadReads(archive, stream);
        });
        var oversized = CreateSyntheticDat(new SyntheticDatEntry(validName, new byte[MaxDlgBytes + 1]));
        WithSyntheticArchive(oversized, (archive, stream) =>
        {
            AssertRejectedWithReason(() => InspectArchive(archive, expectedName), "oversized selected DLG entry", "The selected DLG entry violates the bounded read policy");
            AssertPayloadReads(archive, stream);
        });
        var malformed = CreateSyntheticDat(new SyntheticDatEntry(validName, Bytes("{1}{x}")));
        WithSyntheticArchive(malformed, (archive, stream) =>
        {
            AssertRejectedWithReason(() => InspectArchive(archive, expectedName), "malformed DLG through archive probe path", "DLG record is missing a field opening brace");
            Assert(stream.PayloadBytesRead(archive.Entries.Single()) > 0, "malformed DLG fixture did not exercise a production payload read");
        });
        var overflow = CreateSyntheticDat(new SyntheticDatEntry(validName, Bytes("{1}{x}{}{1}{}{999999999999999999999}{}")));
        WithSyntheticArchive(overflow, (archive, stream) =>
        {
            AssertRejectedWithReason(() => InspectArchive(archive, expectedName), "response overflow through archive probe path", "DLG response value exceeds signed 32-bit range");
            Assert(stream.PayloadBytesRead(archive.Entries.Single()) > 0, "response overflow fixture did not exercise a production payload read");
        });
        var signedExtrema = CreateSyntheticDat(new SyntheticDatEntry(validName, Bytes("{-2147483648}{npc}{}{}{}{2147483647}{} {2147483647}{player}{}{1}{}{-2147483648}{}")));
        WithSyntheticArchive(signedExtrema, (archive, stream) =>
        {
            var result = InspectArchive(archive, expectedName);
            Assert(result.Document.Records.Select(record => record.Id).OrderBy(id => id).SequenceEqual([int.MinValue, int.MaxValue]) && result.Document.Records.Select(record => record.Response).OrderBy(value => value).SequenceEqual([int.MinValue, int.MaxValue]), "signed 32-bit extrema were not accepted through the production archive probe path");
            Assert(stream.PayloadBytesRead(archive.Entries.Single()) > 0, "signed-extrema fixture did not exercise a production payload read");
        });
        var belowMinimum = CreateSyntheticDat(new SyntheticDatEntry(validName, Bytes("{-2147483649}{x}{}{1}{}{0}{}")));
        WithSyntheticArchive(belowMinimum, (archive, stream) =>
        {
            AssertRejectedWithReason(() => InspectArchive(archive, expectedName), "below signed minimum through archive probe path", "DLG record ID exceeds signed 32-bit range");
            Assert(stream.PayloadBytesRead(archive.Entries.Single()) > 0, "below-minimum fixture did not exercise a production payload read");
        });
        var aboveMaximum = CreateSyntheticDat(new SyntheticDatEntry(validName, Bytes("{2147483648}{x}{}{1}{}{0}{}")));
        WithSyntheticArchive(aboveMaximum, (archive, stream) =>
        {
            AssertRejectedWithReason(() => InspectArchive(archive, expectedName), "above signed maximum through archive probe path", "DLG record ID exceeds signed 32-bit range");
            Assert(stream.PayloadBytesRead(archive.Entries.Single()) > 0, "above-maximum fixture did not exercise a production payload read");
        });
        var fieldLimit = CreateSyntheticDat(new SyntheticDatEntry(validName, Bytes("{" + new string('x', MaxFieldBytes + 1) + "}{}{}{1}{}{0}{}")));
        WithSyntheticArchive(fieldLimit, (archive, stream) =>
        {
            AssertRejectedWithReason(() => InspectArchive(archive, expectedName), "field limit through archive probe path", "DLG field exceeds the 4 KiB policy cap");
            Assert(stream.PayloadBytesRead(archive.Entries.Single()) > 0, "field-limit fixture did not exercise a production payload read");
        });
        var duplicateRecord = CreateSyntheticDat(new SyntheticDatEntry(validName, Bytes("{1}{x}{}{1}{}{0}{} {1}{x}{}{1}{}{0}{}")));
        WithSyntheticArchive(duplicateRecord, (archive, stream) =>
        {
            AssertRejectedWithReason(() => InspectArchive(archive, expectedName), "duplicate DLG record through archive probe path", "DLG record IDs must be unique");
            Assert(stream.PayloadBytesRead(archive.Entries.Single()) > 0, "duplicate-record fixture did not exercise a production payload read");
        });
        var dangling = CreateSyntheticDat(new SyntheticDatEntry(validName, Bytes("{1}{npc}{}{}{}{0}{} {2}{player}{}{1}{}{99}{}")));
        WithSyntheticArchive(dangling, (archive, stream) =>
        {
            var result = InspectArchive(archive, expectedName);
            Assert(result.Graph.Dangling == 1 && result.Graph.Frontier == "dangling" && result.Graph.Rows.Any(row => row.EdgeStatus == "dangling"), "dangling reference did not produce the distinct production-path dangling outcome");
            Assert(stream.PayloadBytesRead(archive.Entries.Single()) > 0, "dangling fixture did not exercise a production payload read");
        });
        var unresolved = CreateSyntheticDat(new SyntheticDatEntry(validName, Bytes("{1}{npc}{}{}{}{0}{} {2}{player}{}{1}{}{3}{} {3}{player}{}{1}{}{0}{}")));
        WithSyntheticArchive(unresolved, (archive, stream) =>
        {
            var result = InspectArchive(archive, expectedName);
            Assert(result.Graph.Unresolved == 1 && result.Graph.Frontier == "unresolved" && result.Graph.Rows.Any(row => row.EdgeStatus == "unresolved"), "wrong-role target did not produce the distinct production-path unresolved outcome");
            Assert(stream.PayloadBytesRead(archive.Entries.Single()) > 0, "unresolved fixture did not exercise a production payload read");
        });
        var callback = CreateSyntheticDat(new SyntheticDatEntry(validName, Bytes("{1}{npc}{}{}{}{0}{} {2}{player}{}{1}{}{0}{SC}")));
        WithSyntheticArchive(callback, (archive, stream) =>
        {
            var result = InspectArchive(archive, expectedName);
            Assert(result.Graph.Callbacks == 1 && result.Graph.Frontier == "callback" && result.Graph.Rows.Any(row => row.EdgeStatus == "callback"), "callback was not classified by the production archive probe path");
            Assert(stream.PayloadBytesRead(archive.Entries.Single()) > 0, "callback classification fixture did not exercise a production payload read");
        });
        var unsupported = CreateSyntheticDat(new SyntheticDatEntry(validName, Bytes("{1}{npc}{}{}{}{0}{??}")));
        WithSyntheticArchive(unsupported, (archive, stream) =>
        {
            var result = InspectArchive(archive, expectedName);
            Assert(result.Graph.Frontier == "unsupported-operation", "unsupported start operation was not a production probe frontier");
            Assert(stream.PayloadBytesRead(archive.Entries.Single()) > 0, "unsupported fixture did not exercise a production payload read");
        });
        var recordLimit = CreateSyntheticDat(new SyntheticDatEntry(validName, Enumerable.Range(1, MaxRecords + 1).SelectMany(id => Bytes($"{{{id}}}{{x}}{{}}{{1}}{{}}{{0}}{{}}")).ToArray()));
        WithSyntheticArchive(recordLimit, (archive, stream) =>
        {
            AssertRejectedWithReason(() => InspectArchive(archive, expectedName), "record limit through archive probe path", "DLG record count exceeds the 10,000 policy cap");
            Assert(stream.PayloadBytesRead(archive.Entries.Single()) > 0, "record-limit fixture did not exercise a production payload read");
        });
        var traversalCap = CreateSyntheticDat(new SyntheticDatEntry(validName, Bytes("{1}{npc}{}{}{}{0}{}" + string.Concat(Enumerable.Range(2, MaxTraversalNodes + 2).Select(id => $"{{{id}}}{{player}}{{}}{{1}}{{}}{{{id + 10_000}}}{{}}")) + string.Concat(Enumerable.Range(2, MaxTraversalNodes + 2).Select(id => $"{{{id + 10_000}}}{{npc}}{{}}{{}}{{}}{{0}}{{}}")))));
        WithSyntheticArchive(traversalCap, (archive, stream) =>
        {
            var result = InspectArchive(archive, expectedName);
            Assert(result.Graph.IncludedNodes == MaxTraversalNodes && result.Graph.Frontier == "traversal-node-cap", "traversal cap was not enforced by the production archive probe path");
            Assert(stream.PayloadBytesRead(archive.Entries.Single()) > 0, "traversal-cap fixture did not exercise a production payload read");
        });
        var cycle = CreateSyntheticDat(new SyntheticDatEntry(validName, Bytes("{1}{npc}{}{}{}{0}{} {2}{player}{}{1}{}{1}{}")));
        WithSyntheticArchive(cycle, (archive, stream) =>
        {
            var result = InspectArchive(archive, expectedName);
            Assert(result.Graph.Frontier == "cycle" && result.Graph.Rows.Any(row => row.EdgeStatus == "unresolved"), "cycle-shaped reference did not produce the distinct production-path cycle stop");
            Assert(stream.PayloadBytesRead(archive.Entries.Single()) > 0, "cycle fixture did not exercise a production payload read");
        });
        var callbackBeforeDirect = CreateSyntheticDat(new SyntheticDatEntry(validName, Bytes("{1}{npc}{}{}{}{0}{} {2}{player}{}{1}{}{3}{SC} {3}{player}{}{1}{}{4}{} {4}{npc}{}{}{}{0}{}")));
        WithSyntheticArchive(callbackBeforeDirect, (archive, stream) =>
        {
            var result = InspectArchive(archive, expectedName);
            Assert(result.Graph.Frontier == "callback" && result.Graph.Rows.Count == 2 && result.Graph.Rows.All(row => row.NodeId != 3 && row.NodeId != 4) && result.Graph.Rows.All(row => row.EdgeStatus != "direct-included"), "a callback before a direct response allowed a later target to be selected");
            Assert(stream.PayloadBytesRead(archive.Entries.Single()) > 0, "callback-before-direct fixture did not exercise a production payload read");
        });
        var positiveTraversal = CreateSyntheticDat(new SyntheticDatEntry(validName, Bytes("{100}{npc}{}{}{}{0}{} {200}{player}{}{1}{}{300}{} {210}{player}{}{1}{}{250}{} {220}{player}{}{1}{}{250}{} {250}{npc}{}{}{}{0}{} {260}{player}{}{1}{}{400}{} {300}{npc}{}{}{}{0}{} {400}{npc}{}{}{}{0}{}")));
        WithSyntheticArchive(positiveTraversal, (archive, stream) =>
        {
            var result = InspectArchive(archive, expectedName);
            var chosen = result.Graph.Rows.Single(row => row.EdgeStatus == "direct-included");
            Assert(result.Graph.StartId == 100 && chosen.NodeId == 210 && result.Graph.Rows.Any(row => row.NodeId == 250 && row.EdgeStatus == "included-target") && result.Graph.Frontier == "second-decision-frontier" && result.Graph.Rows.Any(row => row.NodeId == 260 && row.EdgeStatus == "frontier"), "positive production traversal did not select smallest target then response ID and reach its second frontier");
            Assert(stream.PayloadBytesRead(archive.Entries.Single()) > 0, "positive traversal fixture did not exercise a production payload read");
        });
    }

    private static void AssertSelectorRejectsWithoutPayload(string selector)
    {
        var fixture = CreateSyntheticDat(new SyntheticDatEntry(Name("dlg\\fixture.dlg"), Bytes("{1}{x}{}{1}{}{0}{}")));
        WithSyntheticArchive(fixture, (archive, stream) =>
        {
            var rejected = false;
            try { ParseResourceSelector(selector); }
            catch (ArgumentException) { rejected = true; }
            catch (Exception exception) { throw new InvalidOperationException($"selector {selector} produced {exception.GetType().Name}; expected ArgumentException."); }
            if (!rejected) throw new InvalidOperationException($"selector {selector} was accepted.");
            AssertPayloadReads(archive, stream);
        });
    }

    private static void WithSyntheticArchive(byte[] bytes, Action<DatArchive, TrackingReadStream> action)
    {
        using var stream = new TrackingReadStream(bytes);
        using var archive = DatArchive.Open(stream, new DatReaderOptions { MaxArchiveBytes = 2 * 1024 * 1024, MaxIndexBytes = 16 * 1024, MaxEntryCount = 16, MaxNameBytes = 512, MaxTotalNameBytes = 2 * 1024, MaxStoredEntryBytes = MaxEntryBytes, MaxDecodedEntryBytes = MaxEntryBytes }, leaveOpen: true);
        stream.ResetReads();
        action(archive, stream);
    }

    private static void AssertPayloadReads(DatArchive archive, TrackingReadStream stream)
    {
        foreach (var entry in archive.Entries)
        {
            Assert(stream.PayloadBytesRead(entry) == 0, "rejected archive/name selector performed a payload read");
        }
    }

    private static byte[] CreateSyntheticDat(params SyntheticDatEntry[] entries)
    {
        using var archive = new MemoryStream();
        var stored = new List<byte[]>();
        var offsets = new List<uint>();
        foreach (var entry in entries)
        {
            offsets.Add(checked((uint)archive.Position));
            stored.Add(entry.Compressed ? Compress(entry.Payload) : entry.Payload);
            archive.Write(stored[^1]);
        }
        using var index = new MemoryStream();
        WriteU32(index, checked((uint)entries.Length));
        for (var i = 0; i < entries.Length; i++)
        {
            WriteU32(index, checked((uint)entries[i].Name.Length));
            index.Write(entries[i].Name);
            index.Write(new byte[4]);
            WriteU32(index, entries[i].Compressed ? 2u : 1u);
            WriteU32(index, checked((uint)entries[i].Payload.Length));
            WriteU32(index, checked((uint)stored[i].Length));
            WriteU32(index, offsets[i]);
        }
        index.Position = 0;
        index.CopyTo(archive);
        archive.Write(new byte[16]);
        archive.Write("1TAD"u8);
        WriteU32(archive, checked((uint)entries.Sum(entry => entry.Name.Length)));
        WriteU32(archive, checked((uint)(index.Length + 28)));
        return archive.ToArray();
    }

    private static byte[] Compress(byte[] input)
    {
        using var output = new MemoryStream();
        using (var zip = new ZLibStream(output, CompressionLevel.SmallestSize, true)) zip.Write(input);
        return output.ToArray();
    }

    private static void WriteU32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static byte[] Name(string value) => Encoding.ASCII.GetBytes(value + "\0");
    private static byte[] Bytes(string value) => Encoding.ASCII.GetBytes(value);
    private static void Assert(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void AssertRejected(Action action, string label)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        catch (Exception exception) { throw new InvalidOperationException($"{label} produced {exception.GetType().Name}; expected InvalidDataException."); }
        throw new InvalidOperationException($"{label} was accepted.");
    }

    private static void AssertRejectedWithReason(Action action, string label, string expectedReason)
    {
        try { action(); }
        catch (InvalidDataException exception) when (exception.Message.StartsWith(expectedReason, StringComparison.Ordinal)) { return; }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"{label} produced {exception.GetType().Name}: {exception.Message}; expected InvalidDataException beginning '{expectedReason}'.");
        }
        throw new InvalidOperationException($"{label} was accepted.");
    }

    private sealed record SyntheticDatEntry(byte[] Name, byte[] Payload, bool Compressed = false);

    private sealed record ProbeResult(DatEntry Entry, byte[] Payload, DlgDocument Document, GraphSummary Graph);

    private sealed class TrackingReadStream(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        private readonly List<(long Start, int Count)> _reads = [];
        public override int Read(Span<byte> buffer)
        {
            var start = Position;
            var count = base.Read(buffer);
            if (count != 0) _reads.Add((start, count));
            return count;
        }
        public void ResetReads() => _reads.Clear();
        public long PayloadBytesRead(DatEntry entry)
        {
            var start = (long)entry.Offset;
            var end = checked(start + entry.StoredSize);
            return _reads.Sum(read => Math.Max(0, Math.Min(read.Start + read.Count, end) - Math.Max(read.Start, start)));
        }
    }

    private sealed record InputFingerprint(long Bytes, string Sha256)
    {
        public static InputFingerprint Create(string path)
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return new InputFingerprint(stream.Length, Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());
        }
    }

    private enum Role { Npc, Player }

    private sealed record DlgRecord(int Id, Role Role, int Response, bool Unsupported, bool Callback, string ConditionClass, string ActionClass);

    private sealed class DlgDocument
    {
        public required List<DlgRecord> Records { get; init; }
        public required long[] FieldLengths { get; init; }
        public required int EmptyFields { get; init; }
        public required int CommentLines { get; init; }
        public required int CrLf { get; init; }
        public required int Lf { get; init; }
        public required SortedDictionary<string, int> ConditionOpcodes { get; init; }
        public required SortedDictionary<string, int> ActionOpcodes { get; init; }

        public static DlgDocument Parse(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length == 0 || bytes.Length > MaxDlgBytes) throw new InvalidDataException("DLG payload is empty or exceeds the 512 KiB policy cap.");
            var cursor = new Cursor(bytes);
            var records = new List<DlgRecord>();
            var ids = new HashSet<int>();
            var lengths = new long[FieldsPerRecord];
            var conditions = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var actions = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var empty = 0;
            while (cursor.SkipOutside())
            {
                if (records.Count == MaxRecords) throw new InvalidDataException("DLG record count exceeds the 10,000 policy cap.");
                var fields = new Field[FieldsPerRecord];
                for (var field = 0; field < FieldsPerRecord; field++)
                {
                    fields[field] = cursor.Field();
                    lengths[field] = checked(lengths[field] + fields[field].Length);
                    if (fields[field].Length == 0) empty++;
                }
                var id = ParseInteger(fields[0], "record ID", allowEmpty: false);
                if (!ids.Add(id)) throw new InvalidDataException("DLG record IDs must be unique in the bounded subset.");
                var iq = ParseInteger(fields[3], "IQ discriminator", allowEmpty: true);
                if (fields[3].Length != 0 && iq == 0) throw new InvalidDataException("DLG IQ discriminator is zero but not blank.");
                var response = ParseInteger(fields[5], "response value", allowEmpty: true);
                var role = iq == 0 ? Role.Npc : Role.Player;
                var conditionClass = ClassifyOperations(fields[4], conditions, out var unsupportedCondition, out var callbackCondition);
                var actionClass = ClassifyOperations(fields[6], actions, out var unsupportedAction, out var callbackAction);
                records.Add(new DlgRecord(id, role, response, unsupportedCondition || unsupportedAction, callbackCondition || callbackAction, conditionClass, actionClass));
            }
            if (records.Count == 0) throw new InvalidDataException("DLG contains no complete records.");
            return new DlgDocument { Records = records, FieldLengths = lengths, EmptyFields = empty, CommentLines = cursor.CommentLines, CrLf = cursor.CrLf, Lf = cursor.Lf, ConditionOpcodes = conditions, ActionOpcodes = actions };
        }

        private static int ParseInteger(Field field, string label, bool allowEmpty)
        {
            if (field.Length == 0)
            {
                if (allowEmpty) return 0;
                throw new InvalidDataException($"DLG {label} is empty.");
            }
            try
            {
                long magnitude = 0;
                var negative = false;
                var index = 0;
                if (field.Bytes[0] == (byte)'-') { negative = true; index++; }
                if (index == field.Length) throw new InvalidDataException($"DLG {label} is not a decimal integer.");
                for (; index < field.Length; index++)
                {
                    var valueByte = field.Bytes[index];
                    if (valueByte is < (byte)'0' or > (byte)'9') throw new InvalidDataException($"DLG {label} is not ASCII decimal.");
                    var digit = valueByte - (byte)'0';
                    var limit = negative ? 2_147_483_648L : int.MaxValue;
                    if (magnitude > (limit - digit) / 10) throw new OverflowException();
                    magnitude = (magnitude * 10) + digit;
                }
                return negative
                    ? magnitude == 2_147_483_648L ? int.MinValue : -(int)magnitude
                    : (int)magnitude;
            }
            catch (OverflowException exception)
            {
                throw new InvalidDataException($"DLG {label} exceeds signed 32-bit range.", exception);
            }
        }

        private static string ClassifyOperations(Field field, SortedDictionary<string, int> counts, out bool unsupported, out bool callback)
        {
            unsupported = false;
            callback = false;
            if (field.Length == 0) return "none";
            if (field.Bytes.Any(byteValue => byteValue is < 0x20 or > 0x7e)) throw new InvalidDataException("Operational DLG field is not printable ASCII.");
            var opcodes = new SortedSet<string>(StringComparer.Ordinal);
            for (var index = 0; index + 1 < field.Length; index++)
            {
                if (!IsAsciiLetter(field.Bytes[index]) || !IsAsciiLetter(field.Bytes[index + 1])) continue;
                var opcode = string.Create(2, (field.Bytes[index], field.Bytes[index + 1]), static (destination, state) =>
                {
                    destination[0] = char.ToUpperInvariant((char)state.Item1);
                    destination[1] = char.ToUpperInvariant((char)state.Item2);
                });
                opcodes.Add(opcode);
                counts[opcode] = counts.GetValueOrDefault(opcode) + 1;
                index++;
            }
            callback = opcodes.Contains("SC");
            // The physical probe intentionally does not interpret condition/action
            // vocabulary. Any nonempty operation is a fail-closed unsupported
            // boundary; SC is additionally named as a callback-shaped boundary.
            unsupported = !callback;
            return callback ? "callback" : "unsupported";
        }

        private static bool IsAsciiLetter(byte value) => value is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z';
    }

    private sealed record Field(byte[] Bytes)
    {
        public int Length => Bytes.Length;
    }

    private ref struct Cursor
    {
        private readonly ReadOnlySpan<byte> _bytes;
        private int _offset;
        public int CommentLines { get; private set; }
        public int CrLf { get; private set; }
        public int Lf { get; private set; }
        public Cursor(ReadOnlySpan<byte> bytes) { _bytes = bytes; }

        public bool SkipOutside()
        {
            while (_offset < _bytes.Length)
            {
                var value = _bytes[_offset++];
                if (value == (byte)'{')
                {
                    _offset--;
                    return true;
                }
                if (value == (byte)'}') throw new InvalidDataException("DLG has an unmatched right brace outside a record.");
                if (value == 0) throw new InvalidDataException("DLG contains an embedded NUL.");
                if (value == (byte)'/' && _offset < _bytes.Length && _bytes[_offset] == (byte)'/')
                {
                    _offset++;
                    CommentLines++;
                    while (_offset < _bytes.Length && _bytes[_offset] is not (byte)'\r' and not (byte)'\n') _offset++;
                    if (_offset == _bytes.Length) return false;
                    CountNewline(_bytes[_offset++]);
                    continue;
                }
                if (value is (byte)'\r' or (byte)'\n') CountNewline(value);
                else if (value is < 0x20 or > 0x7e) throw new InvalidDataException("DLG contains non-ASCII structural bytes.");
            }
            return false;
        }

        public Field Field()
        {
            if (_offset >= _bytes.Length || _bytes[_offset++] != (byte)'{')
            {
                throw new InvalidDataException("DLG record is missing a field opening brace.");
            }
            var start = _offset;
            while (_offset < _bytes.Length)
            {
                var value = _bytes[_offset++];
                if (value == (byte)'}') return new Field(_bytes.Slice(start, _offset - start - 1).ToArray());
                if (value is (byte)'{' or 0) throw new InvalidDataException("DLG field contains nested brace or NUL.");
                if (_offset - start > MaxFieldBytes) throw new InvalidDataException("DLG field exceeds the 4 KiB policy cap.");
                if (value is (byte)'\r' or (byte)'\n') CountNewline(value);
                // Fields 1, 2, and 3 may contain opaque text bytes. Their exact
                // encoding is deliberately not inferred by this physical parser;
                // field-specific operational validation happens after parsing.
            }
            throw new InvalidDataException("DLG field is missing its closing brace.");
        }

        private void CountNewline() => CountNewline(_bytes[_offset - 1]);
        private void CountNewline(byte value)
        {
            if (value == (byte)'\r')
            {
                if (_offset < _bytes.Length && _bytes[_offset] == (byte)'\n') _offset++;
                CrLf++;
            }
            else Lf++;
        }
    }

    private sealed record SliceRow(int NodeId, Role Role, string EdgeStatus, string ConditionClass, string ActionClass, string RequiredService, string Note);

    private sealed record GraphSummary(int Direct, int Exit, int Special, int Dangling, int Ambiguous, int Unresolved, int Callbacks, int? StartId, string StartRationale, int IncludedNodes, int ExaminedReferences, string Frontier, IReadOnlyList<SliceRow> Rows)
    {
        public static GraphSummary Create(DlgDocument document)
        {
            var byId = document.Records.GroupBy(record => record.Id).ToDictionary(group => group.Key, group => group.ToArray());
            var direct = 0; var exit = 0; var special = 0; var dangling = 0; var ambiguous = 0; var unresolved = 0; var callbacks = 0;
            foreach (var record in document.Records.Where(record => record.Role == Role.Player))
            {
                if (record.Callback) { callbacks++; unresolved++; continue; }
                if (record.Unsupported) { unresolved++; continue; }
                if (record.Response == 0) { exit++; continue; }
                if (record.Response < 0) { special++; continue; }
                if (!byId.TryGetValue(record.Response, out var targets)) { dangling++; continue; }
                if (targets.Length != 1) { ambiguous++; continue; }
                if (targets[0].Role != Role.Npc || targets[0].Unsupported || targets[0].Callback) { unresolved++; continue; }
                direct++;
            }

            var ordered = document.Records.OrderBy(record => record.Id).ToArray();
            var startIndex = Array.FindIndex(ordered, record => record.Role == Role.Npc);
            if (startIndex < 0)
            {
                return new GraphSummary(direct, exit, special, dangling, ambiguous, unresolved, callbacks, null, "none", 0, 0, "no-npc", []);
            }

            var state = new TraversalState(ordered, byId);
            var start = ordered[startIndex];
            if (!state.Include(start, "included-start"))
            {
                return state.ToSummary(direct, exit, special, dangling, ambiguous, unresolved, callbacks, start.Id, "lowest numeric NPC record; structural selection only, not runtime entry", "traversal-node-cap");
            }
            if (start.Callback || start.Unsupported)
            {
                state.ReplaceStatus(start.Id, start.Callback ? "callback" : "unsupported", "unsupported", "unsupported", "unsupported operation; no branch is selected");
                return state.ToSummary(direct, exit, special, dangling, ambiguous, unresolved, callbacks, start.Id, "lowest numeric NPC record; structural selection only, not runtime entry", start.Callback ? "callback" : "unsupported-operation");
            }

            var candidates = state.InspectFrontier(startIndex, "first");
            if (state.Capped) return state.ToSummary(direct, exit, special, dangling, ambiguous, unresolved, callbacks, start.Id, "lowest numeric NPC record; structural selection only, not runtime entry", state.NodeCapped ? "traversal-node-cap" : "traversal-reference-cap");
            if (state.FixedStop is not null) return state.ToSummary(direct, exit, special, dangling, ambiguous, unresolved, callbacks, start.Id, "lowest numeric NPC record; structural selection only, not runtime entry", state.FixedStop);
            var chosen = candidates.OrderBy(candidate => candidate.Response).ThenBy(candidate => candidate.Id).FirstOrDefault();
            if (chosen is null)
            {
                return state.ToSummary(direct, exit, special, dangling, ambiguous, unresolved, callbacks, start.Id, "lowest numeric NPC record; structural selection only, not runtime entry", "first-decision-frontier");
            }

            state.ReplaceStatus(chosen.Id, "direct-included", "none", "none", "transition", "positive serialized target; source-backed transition form, target structural role checked");
            var target = byId[chosen.Response][0];
            if (!state.Include(target, "included-target"))
            {
                return state.ToSummary(direct, exit, special, dangling, ambiguous, unresolved, callbacks, start.Id, "lowest numeric NPC record; structural selection only, not runtime entry", "traversal-node-cap");
            }
            var targetIndex = Array.FindIndex(ordered, record => record.Id == target.Id);
            state.InspectFrontier(targetIndex, "second");
            var frontier = state.Capped ? (state.NodeCapped ? "traversal-node-cap" : "traversal-reference-cap") : state.FixedStop ?? "second-decision-frontier";
            return state.ToSummary(direct, exit, special, dangling, ambiguous, unresolved, callbacks, start.Id, "lowest numeric NPC record; structural selection only, not runtime entry", frontier);
        }

        private sealed class TraversalState(IReadOnlyList<DlgRecord> ordered, IReadOnlyDictionary<int, DlgRecord[]> byId)
        {
            private readonly HashSet<int> _included = [];
            private readonly List<SliceRow> _rows = [];
            public int Examined { get; private set; }
            public bool NodeCapped { get; private set; }
            public bool ReferenceCapped { get; private set; }
            public bool Capped => NodeCapped || ReferenceCapped;
            public string? FixedStop { get; private set; }

            public void StopAt(string classification) => FixedStop ??= classification;

            public bool Include(DlgRecord record, string status)
            {
                if (!_included.Add(record.Id)) return true;
                if (_included.Count > MaxTraversalNodes)
                {
                    _included.Remove(record.Id);
                    NodeCapped = true;
                    return false;
                }
                _rows.Add(new SliceRow(record.Id, record.Role, status, record.ConditionClass, record.ActionClass, "none", "no operation service admitted"));
                return true;
            }

            public void ReplaceStatus(int id, string status, string condition, string action, string note)
                => ReplaceStatus(id, status, condition, action, "unsupported", note);

            public void ReplaceStatus(int id, string status, string condition, string action, string requiredService, string note)
            {
                var index = _rows.FindIndex(row => row.NodeId == id);
                if (index >= 0) _rows[index] = _rows[index] with { EdgeStatus = status, ConditionClass = condition, ActionClass = action, RequiredService = requiredService, Note = note };
            }

            public List<DlgRecord> InspectFrontier(int npcIndex, string stage)
            {
                var candidates = new List<DlgRecord>();
                for (var index = npcIndex + 1; index < ordered.Count && ordered[index].Role == Role.Player; index++)
                {
                    if (Examined == MaxExaminedReferences) { ReferenceCapped = true; break; }
                    Examined++;
                    var response = ordered[index];
                    if (!Include(response, "frontier")) break;
                    if (response.Callback)
                    {
                        ReplaceStatus(response.Id, "callback", "unsupported", "callback", "callback-shaped operation; requires an unimplemented SCR/event bridge");
                        StopAt("callback");
                        break;
                    }
                    if (response.Unsupported)
                    {
                        ReplaceStatus(response.Id, "unsupported", "unsupported", "unsupported", "uninterpreted condition/action; no query or command service is admitted");
                        StopAt("unsupported-operation");
                        break;
                    }
                    if (response.Response == 0) { ReplaceStatus(response.Id, "exit", "none", "none", "zero serialized response is source-backed exit form"); StopAt("exit"); break; }
                    if (response.Response < 0) { ReplaceStatus(response.Id, "special", "none", "none", "negative serialized response is source-backed special form; semantics unknown"); StopAt("special"); break; }
                    if (!byId.TryGetValue(response.Response, out var targets)) { ReplaceStatus(response.Id, "dangling", "none", "none", "positive target is absent"); StopAt("dangling"); break; }
                    if (targets.Length != 1) { ReplaceStatus(response.Id, "ambiguous", "none", "none", "positive target is non-unique"); StopAt("ambiguous"); break; }
                    if (targets[0].Role != Role.Npc || targets[0].Unsupported || targets[0].Callback)
                    {
                        ReplaceStatus(response.Id, "unresolved", "none", "none", "target is structurally invalid or stops at an unsupported operation");
                        StopAt("unresolved");
                        break;
                    }
                    if (_included.Contains(response.Response))
                    {
                        ReplaceStatus(response.Id, "unresolved", "none", "none", "positive target revisits an included structural node (cycle)");
                        StopAt("cycle");
                        break;
                    }
                    candidates.Add(response);
                }
                return candidates;
            }

            public GraphSummary ToSummary(int direct, int exit, int special, int dangling, int ambiguous, int unresolved, int callbacks, int startId, string rationale, string frontier) =>
                new(direct, exit, special, dangling, ambiguous, unresolved, callbacks, startId, rationale, _included.Count, Examined, frontier, _rows);
        }
    }
}
