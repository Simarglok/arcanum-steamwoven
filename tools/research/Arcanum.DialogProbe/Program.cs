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
        var after = InputFingerprint.Create(archivePath);
        if (before != after)
        {
            throw new InvalidDataException("The archive changed during the read-only probe.");
        }

        Console.WriteLine($"input bytes={before.Bytes} sha256={before.Sha256}");
        Console.WriteLine($"resource ordinal={entry.Ordinal} raw_id_sha256={Sha256(expectedName)} flags=0x{entry.Flags:x8} stored_bytes={entry.StoredSize} decoded_bytes={entry.DeclaredOutputSize} payload_sha256={Sha256(payload)}");
        Console.WriteLine($"grammar records={document.Records.Count} npc_records={document.Records.Count(record => record.Role == Role.Npc)} player_records={document.Records.Count(record => record.Role == Role.Player)} empty_fields={document.EmptyFields} comment_lines={document.CommentLines} crlf={document.CrLf} lf={document.Lf} ascii_operational_fields=true");
        Console.WriteLine($"lengths field0={document.FieldLengths[0]} field1={document.FieldLengths[1]} field2={document.FieldLengths[2]} field3={document.FieldLengths[3]} field4={document.FieldLengths[4]} field5={document.FieldLengths[5]} field6={document.FieldLengths[6]}");
        Console.WriteLine($"operations conditions={FormatCounts(document.ConditionOpcodes)} actions={FormatCounts(document.ActionOpcodes)}");
        Console.WriteLine($"edges direct={graph.Direct} exit={graph.Exit} special={graph.Special} dangling={graph.Dangling} ambiguous={graph.Ambiguous} unresolved={graph.Unresolved} callbacks={graph.Callbacks}");
        Console.WriteLine($"research-selected-start node_id={graph.StartId?.ToString() ?? "none"} rationale={graph.StartRationale} included_nodes={graph.IncludedNodes} examined_references={graph.ExaminedReferences} frontier={graph.Frontier}");
        Console.WriteLine("integrity before_after_match=true");
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
            value.Contains('\0') || value.Contains('/') || value.Contains("..", StringComparison.Ordinal) || value.Contains(':') ||
            value.EndsWith("\\", StringComparison.Ordinal) || value[DlgPrefix.Length..].Contains('\\'))
        {
            throw new ArgumentException("Resource must be one exact non-traversing raw dlg\\basename.dlg identifier.");
        }

        var bytes = Encoding.ASCII.GetBytes(value);
        if (bytes.Any(byteValue => byteValue is < 0x20 or > 0x7e))
        {
            throw new ArgumentException("Resource identifier must be printable ASCII.");
        }
        return bytes;
    }

    private static bool RawNameEquals(IReadOnlyList<byte> raw, ReadOnlySpan<byte> expected) =>
        raw.Count == expected.Length + 1 && raw[^1] == 0 && raw.Take(expected.Length).SequenceEqual(expected.ToArray());

    private static string Sha256(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string FormatCounts(SortedDictionary<string, int> values) =>
        values.Count == 0 ? "none" : string.Join(',', values.Select(pair => $"{pair.Key}:{pair.Value}"));

    private static void SelfTest()
    {
        var valid = Bytes("// synthetic comment\r\n{100}{npc}{female}{}{qA1}{200}{aB2}\n{200}{player}{0}{5}{}{300}{}\n{300}{npc}{}{}{}{0}{}\n");
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
        AssertRejected(() => DlgDocument.Parse(Enumerable.Repeat(Bytes("{1}{x}{}{1}{}{0}{}"), MaxRecords + 1).SelectMany(bytes => bytes).ToArray()), "record cap");
        AssertRejected(() => DlgDocument.Parse(new byte[MaxDlgBytes + 1]), "payload cap");

        AssertRejected(() => DlgDocument.Parse(Bytes("{1}{x}{}{1}{}{0}{} {1}{x}{}{1}{}{0}{}")), "duplicate id");
        var dangling = DlgDocument.Parse(Bytes("{1}{x}{}{1}{}{9}{} {2}{x}{}{}{}{0}{}"));
        Assert(GraphSummary.Create(dangling).Dangling == 1, "dangling target was not classified");
        var special = DlgDocument.Parse(Bytes("{1}{x}{}{1}{}{-9}{} {2}{x}{}{}{}{0}{}"));
        Assert(GraphSummary.Create(special).Special == 1, "negative special transition was not classified");
        var cycle = DlgDocument.Parse(Bytes("{1}{x}{}{1}{}{2}{} {2}{x}{}{}{}{0}{} {3}{x}{}{1}{}{2}{}"));
        Assert(!string.IsNullOrEmpty(GraphSummary.Create(cycle).Frontier), "frontier/cycle handling did not terminate");
        var unsupported = DlgDocument.Parse(Bytes("{1}{x}{}{1}{??}{2}{} {2}{x}{}{}{}{0}{}"));
        Assert(GraphSummary.Create(unsupported).Unresolved == 1, "unsupported condition opcode was not fail-closed");

        AssertSelectorRejectsWithoutPayload("dlg\\..\\x.dlg");
        AssertSelectorRejectsWithoutPayload("dlg\\folder\\x.dlg");
        AssertSelectorRejectsWithoutPayload("dlg/x.dlg");
        AssertSelectorRejectsWithoutPayload("C:dlg\\x.dlg");
        AssertSelectorRejectsWithoutPayload("dlg\\x/../y.dlg");
        AssertSyntheticArchiveSelection();
    }

    private static void AssertSyntheticArchiveSelection()
    {
        var payload = Bytes("{1}{x}{}{1}{}{0}{}");
        var validName = Name("dlg\\fixture.dlg");
        var duplicate = CreateSyntheticDat(new SyntheticDatEntry(validName, payload), new SyntheticDatEntry(validName, payload));
        WithSyntheticArchive(duplicate, (archive, stream) =>
        {
            var matches = archive.Entries.Where(entry => RawNameEquals(entry.RawNameBytes, ParseResourceSelector("dlg\\fixture.dlg"))).ToArray();
            Assert(matches.Length == 2, "duplicate selector fixture did not duplicate");
            AssertPayloadReads(archive, stream);
        });
        var oversized = CreateSyntheticDat(new SyntheticDatEntry(validName, new byte[MaxDlgBytes + 1]));
        WithSyntheticArchive(oversized, (archive, stream) =>
        {
            var entry = archive.Entries.Single();
            Assert(entry.DeclaredOutputSize > MaxDlgBytes, "oversize fixture did not retain declared size");
            AssertPayloadReads(archive, stream);
        });
    }

    private static void AssertSelectorRejectsWithoutPayload(string selector)
    {
        var fixture = CreateSyntheticDat(new SyntheticDatEntry(Name("dlg\\fixture.dlg"), Bytes("{1}{x}{}{1}{}{0}{}")));
        WithSyntheticArchive(fixture, (archive, stream) =>
        {
            AssertRejected(() => ParseResourceSelector(selector), $"selector {selector}");
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
        catch (Exception) { return; }
        throw new InvalidOperationException($"{label} was accepted.");
    }

    private sealed record SyntheticDatEntry(byte[] Name, byte[] Payload, bool Compressed = false);

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

    private sealed record DlgRecord(int Id, Role Role, int Response, bool Unsupported, string ConditionClass, string ActionClass);

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
                var conditionClass = ClassifyOperations(fields[4], conditions, out var unsupportedCondition);
                var actionClass = ClassifyOperations(fields[6], actions, out var unsupportedAction);
                records.Add(new DlgRecord(id, role, response, unsupportedCondition || unsupportedAction, conditionClass, actionClass));
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
            var value = 0;
            var sign = 1;
            var index = 0;
            if (field.Bytes[0] == (byte)'-') { sign = -1; index++; }
            if (index == field.Length) throw new InvalidDataException($"DLG {label} is not a decimal integer.");
            for (; index < field.Length; index++)
            {
                var valueByte = field.Bytes[index];
                if (valueByte is < (byte)'0' or > (byte)'9') throw new InvalidDataException($"DLG {label} is not ASCII decimal.");
                checked { value = (value * 10) + valueByte - (byte)'0'; }
            }
            return checked(value * sign);
        }

        private static string ClassifyOperations(Field field, SortedDictionary<string, int> counts, out bool unsupported)
        {
            unsupported = false;
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
            if (opcodes.Count == 0) { unsupported = true; return "unsupported"; }
            return string.Join('+', opcodes);
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
                else if (value is < 0x20 or > 0x7e) throw new InvalidDataException("DLG field contains non-ASCII bytes.");
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

    private sealed record GraphSummary(int Direct, int Exit, int Special, int Dangling, int Ambiguous, int Unresolved, int Callbacks, int? StartId, string StartRationale, int IncludedNodes, int ExaminedReferences, string Frontier)
    {
        public static GraphSummary Create(DlgDocument document)
        {
            var byId = document.Records.GroupBy(record => record.Id).ToDictionary(group => group.Key, group => group.ToArray());
            var direct = 0; var exit = 0; var special = 0; var dangling = 0; var ambiguous = 0; var unresolved = 0; var callbacks = 0;
            foreach (var record in document.Records.Where(record => record.Role == Role.Player))
            {
                if (record.Response == 0) { exit++; continue; }
                if (record.Response < 0) { special++; continue; }
                if (!byId.TryGetValue(record.Response, out var targets)) { dangling++; continue; }
                if (targets.Length != 1) { ambiguous++; continue; }
                if (targets[0].Role != Role.Npc || record.Unsupported) { unresolved++; continue; }
                direct++;
                if (record.ActionClass.Contains("SC", StringComparison.Ordinal)) callbacks++;
            }
            var ordered = document.Records.OrderBy(record => record.Id).ToArray();
            var startIndex = Array.FindIndex(ordered, record => record.Role == Role.Npc);
            if (startIndex < 0)
            {
                return new GraphSummary(direct, exit, special, dangling, ambiguous, unresolved, callbacks, null, "none", 0, 0, "no-npc");
            }
            var start = ordered[startIndex];
            var firstFrontier = ImmediateResponses(ordered, startIndex);
            var chosen = firstFrontier.FirstOrDefault(record => !record.Unsupported && record.Response > 0 && byId.TryGetValue(record.Response, out var targets) && targets.Length == 1 && targets[0].Role == Role.Npc);
            var includedIds = new HashSet<int> { start.Id };
            foreach (var response in firstFrontier) includedIds.Add(response.Id);
            var examined = firstFrontier.Count;
            var frontier = "first-decision-frontier";
            if (chosen is not null && byId.TryGetValue(chosen.Response, out var targetMatches))
            {
                var target = targetMatches[0];
                includedIds.Add(target.Id);
                var targetIndex = Array.FindIndex(ordered, record => record.Id == target.Id);
                var secondFrontier = ImmediateResponses(ordered, targetIndex);
                foreach (var response in secondFrontier) includedIds.Add(response.Id);
                examined = checked(examined + secondFrontier.Count);
                frontier = "second-decision-frontier";
            }
            if (includedIds.Count > MaxTraversalNodes || examined > MaxExaminedReferences)
            {
                frontier = "traversal-cap";
            }
            var included = Math.Min(MaxTraversalNodes, includedIds.Count);
            examined = Math.Min(MaxExaminedReferences, examined);
            return new GraphSummary(direct, exit, special, dangling, ambiguous, unresolved, callbacks, start.Id, "lowest numeric NPC record; structural selection only, not runtime entry", included, examined, frontier);
        }

        private static List<DlgRecord> ImmediateResponses(IReadOnlyList<DlgRecord> ordered, int npcIndex)
        {
            var responses = new List<DlgRecord>();
            for (var index = npcIndex + 1; index < ordered.Count && ordered[index].Role == Role.Player; index++)
            {
                responses.Add(ordered[index]);
            }
            return responses;
        }
    }
}
