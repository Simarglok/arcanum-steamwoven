using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Arcanum.Data;

// Independent bounded research instrumentation, not an NPC loader or SCR VM.
// Layouts are explicit external evidence inputs, never inferred by scanning IDs.
namespace Arcanum.NpcDialogProbe;

internal sealed record Layout(string[] Fields, int[] GroupLengths);
internal sealed record Manifest(string Module, string Aggregate, string Prototype, string Scripts, string Dialogs,
    string ObjectHash, Dictionary<int, Layout> Layouts, bool? Following, bool? Jilted);
internal sealed record Binding(uint Flags, uint Counters, int Script);
internal sealed record ObjectRecord(int Start, int End, int Type, int PrototypeId, string ObjectHash,
    long? Location, int? Description, Dictionary<int, Binding>? Scripts);
internal sealed class ProbeFailure(string code) : Exception(code);

internal static class Program
{
    internal static void Require(bool condition, string code)
    {
        if (!condition) throw new ProbeFailure(code);
    }
    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string Fingerprint(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
    internal static int Main(string[] args)
    {
        try
        {
            if (args.SequenceEqual(new[] { "--self-test" })) { SelfTests.Run(); return 0; }
            Require(args.Length == 3 && args[0] == "--original" && args[1] == "--manifest", "ExplicitOptInRequired");
            var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(args[2]))!;
            Require(manifest is not null, "InvalidManifest");
            var result = Run(manifest!);
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (ProbeFailure e) { Console.Error.WriteLine(e.Message); return 1; }
        catch (Exception) { Console.Error.WriteLine("InputOrFormatFailure"); return 1; } // Never leak original text or paths.
    }
    internal static byte[] BoundedFile(string path, int cap)
    {
        using var stream = File.OpenRead(path);
        Require(stream.Length <= cap, "FileLimit");
        var bytes = new byte[(int)stream.Length]; stream.ReadExactly(bytes); return bytes;
    }
    internal static byte[] Unique(DatArchive archive, string raw, uint cap)
    {
        Require(raw.Length > 0 && raw.All(c => c is >= ' ' and <= '~') && !raw.Contains('/') &&
            !raw.Split('\\').Any(p => p.Length == 0 || p is "." or ".."), "InvalidResourceSelector");
        var key = Encoding.ASCII.GetBytes(raw + "\0");
        var matches = archive.Entries.Where(e => e.RawNameBytes.SequenceEqual(key)).ToArray();
        Require(matches.Length == 1, "MissingOrAmbiguousResource");
        Require(matches[0].StoredSize <= cap && matches[0].DeclaredOutputSize <= cap, "PayloadLimit");
        return archive.ReadEntry(matches[0]);
    }
    private static object Run(Manifest m)
    {
        ValidateObjectId(m.ObjectHash);
        var paths = new[] { m.Module, m.Aggregate, m.Prototype, m.Scripts, m.Dialogs }.Distinct().ToArray();
        var before = paths.Select(Fingerprint).ToArray();
        using var module = DatArchive.Open(m.Module);
        var mapBytes = Unique(module, "Rules\\MapList.mes", 1024 * 1024);
        Require(mapBytes.All(b => b < 128), "MapEncodingUnknown");
        var maps = Regex.Matches(Encoding.ASCII.GetString(mapBytes), @"\{([0-9]+)\}\s*\{([^}]*)\}")
            .Where(v => v.Groups[2].Value.Contains("Type: START_MAP", StringComparison.Ordinal)).ToArray();
        Require(maps.Length == 1, "AmbiguousStartMap");
        var map = maps[0].Groups[2].Value.Split(',')[0].Trim();
        Require(Path.GetFileName(m.Aggregate) == Obfuscate(map).ToLowerInvariant(), "WrongAggregateContext");
        var aggregate = BoundedFile(m.Aggregate, 32 * 1024 * 1024);
        var records = Objects(aggregate, m.Layouts, true, false);
        var npc = SelectNpc(records, m.ObjectHash);
        Require(npc.Location.HasValue && npc.Description.HasValue, "MissingIdentityContext");
        var descriptions = Unique(module, "mes\\gamedesc.mes", 1024 * 1024);
        Require(descriptions.All(b => b < 128), "DescriptionEncodingUnknown");
        var names = Regex.Matches(Encoding.ASCII.GetString(descriptions), @"\{([0-9]+)\}\s*\{([^}]*)\}")
            .Where(v => int.Parse(v.Groups[1].Value, CultureInfo.InvariantCulture) == npc.Description).ToArray();
        Require(names.Length == 1, "MissingOrAmbiguousDescription");
        var prototypeBytes = BoundedFile(m.Prototype, 2 * 1024 * 1024);
        var prototypes = Objects(prototypeBytes, m.Layouts, false, true);
        Require(prototypes.Count == 1 && prototypes[0].Type == npc.Type && prototypes[0].PrototypeId == npc.PrototypeId,
            "WrongPrototype");
        // Whole-field replacement; an absent event in an overridden array is not inherited per slot.
        var binding = DialogBinding(npc.Scripts, prototypes[0].Scripts);
        Require(binding.Script > 0 && binding.Script <= 99999, "InvalidScriptId");
        using var scripts = DatArchive.Open(m.Scripts);
        var prefix = Encoding.ASCII.GetBytes($"scr\\{binding.Script:D5}");
        var candidates = scripts.Entries.Where(e => e.RawNameBytes.Take(prefix.Length).SequenceEqual(prefix) &&
            Encoding.ASCII.GetString(e.RawNameBytes.ToArray()).EndsWith(".scr\0", StringComparison.Ordinal)).ToArray();
        Require(candidates.Length == 1, "MissingOrAmbiguousScriptCatalogue");
        var scrName = Encoding.ASCII.GetString(candidates[0].RawNameBytes.ToArray()[..^1]);
        var scr = Unique(scripts, scrName, 1024 * 1024);
        var entry = Resolve(scr, binding.Flags, m.Following, m.Jilted);
        using var dialogs = DatArchive.Open(m.Dialogs);
        var dlgName = "dlg" + scrName[3..^3] + "dlg";
        var dlg = Unique(dialogs, dlgName, 512 * 1024);
        var document = DlgReader.Read(dlg);
        var group = Group(document, entry.Node, false);
        var sorted = Group(document, entry.Node, true);
        Require(group.Select(r => r.Id).SequenceEqual(sorted.Select(r => r.Id)), "ReplyOrderMismatch");
        var targetIds = group.Where(r => r.ResponseKind == DlgResponseKind.Direct).Select(r => r.ResponseValue!.Value).Distinct().ToArray();
        foreach (var id in targetIds) Require(document.TryGetRecord(id, out var target) && target!.Role == DlgRecordRole.Npc, "WrongOrMissingTarget");
        Require(targetIds.Length == 1, "AmbiguousSelectedEdge");
        document.TryGetRecord(entry.Node, out var start); document.TryGetRecord(targetIds[0], out var destination);
        var observed = new[] { start! }.Concat(group).Append(destination!).ToArray();
        var after = paths.Select(Fingerprint).ToArray();
        Require(before.SequenceEqual(after), "InputChanged");
        return new
        {
            schema = 1, category = "ObservedFieldsAndConditionalSourceTrace", originalExecution = "NOT_VERIFIED",
            sourcePolicy = "ExplicitSelectedArchivesAndBaseAggregate_NoSaveOrPatchPrecedenceClaim",
            inputs = before, mapListHash = Hash(mapBytes), descriptionResourceHash = Hash(descriptions),
            nameHash = Hash(Encoding.ASCII.GetBytes(names[0].Groups[2].Value)), prototypeHash = Hash(prototypeBytes),
            aggregateHash = Hash(aggregate), aggregateBytes = aggregate.Length, records = records.Count,
            npc = new { npc.ObjectHash, npc.PrototypeId, npc.Start, npc.End, locationPresent = npc.Location.HasValue,
                locationHash = Hash(BitConverter.GetBytes(npc.Location!.Value)),
                descriptionId = npc.Description, scriptArrayOrigin = npc.Scripts is null ? "Prototype" : "Instance" },
            attachment = new { eventId = 9, binding.Script, headerHash = Hash(BitConverter.GetBytes(binding.Flags).Concat(BitConverter.GetBytes(binding.Counters)).ToArray()) },
            scriptHash = Hash(scr), scriptHeaderHash = Hash(scr[..8]), scriptEntries = (scr.Length - 64) / 132,
            entry.ScriptLine, entry.Node, dlgHash = Hash(dlg), dlgRecords = document.Records.Count,
            replyIds = group.Select(r => r.Id), targetIds, orderEquivalent = true,
            fields = observed.Select(r => new { r.Id, role = r.Role.ToString(),
                conditionHash = Hash(r.Condition.Bytes.ToArray()), actionHash = Hash(r.Action.Bytes.ToArray()),
                conditionBytes = r.Condition.Length, actionBytes = r.Action.Length,
                textBytes = r.Text.Length, alternateBytes = r.AlternateText.Length,
                highTextBytes = r.Text.Bytes.Concat(r.AlternateText.Bytes).Count(b => b >= 128),
                substitutions = Tokens(r.Text.Bytes).Concat(Tokens(r.AlternateText.Bytes)).ToArray() }),
            integrityUnchanged = true, runtimeAdmission = "NOT_EVALUATED_ReadOnlyStructuralEvidence"
        };
    }
    internal static void ValidateObjectId(string value) => Require(Regex.IsMatch(value, "^[0-9a-f]{64}$"), "InvalidObjectId");
    internal static ObjectRecord SelectNpc(IEnumerable<ObjectRecord> records, string hash)
    {
        ValidateObjectId(hash);
        var found = records.Where(r => r.ObjectHash == hash).ToArray();
        Require(found.Length == 1 && found[0].Type == 16, "MissingAmbiguousOrWrongRoleObject");
        return found[0];
    }
    internal static Binding DialogBinding(Dictionary<int, Binding>? instance, Dictionary<int, Binding>? prototype)
    {
        var effective = instance ?? prototype;
        Require(effective is not null && effective.ContainsKey(9), "MissingDialogAttachment");
        return effective![9];
    }
    internal static string Obfuscate(string value)
    {
        // Declarative character transform from the pinned map naming specification.
        return new string(value.Select(c => c is >= 'a' and <= 'z' ? (char)(c + 13 > 'z' ? c - 11 : c + 13)
            : c is >= 'A' and <= 'Z' ? (char)(c + 13 > 'Z' ? c - 11 : c + 13) : c).Reverse().ToArray());
    }
    internal static string[] Tokens(IEnumerable<byte> bytes)
    {
        var data = bytes.ToArray(); Require(data.All(b => b < 128), "TextEncodingUnknown");
        var text = Encoding.ASCII.GetString(data);
        var matches = Regex.Matches(text, "@([^@]*)@");
        Require(text.Count(c => c == '@') == matches.Count * 2, "UnmatchedSubstitution");
        return matches.Select(m => m.Groups[1].Value.ToLowerInvariant() switch
        { "pcname" => "PC_NAME", "npcname" => "NPC_NAME", _ => "UNKNOWN_TOKEN" }).ToArray();
    }
    // Research analysis only, not a formatter or runtime eligibility service.
    internal static bool Eligibility(int genderSelector, int pcGender, int iqGate, int pcIq, bool dumb, int lowIq)
    {
        Require(iqGate != 0 && iqGate != int.MinValue, "InvalidPlayerIq");
        var effective = dumb && pcIq > lowIq ? 1 : pcIq;
        return (genderSelector == -1 || genderSelector == pcGender) &&
            (iqGate < 0 ? effective <= -iqGate : effective >= iqGate);
    }
    internal static byte[] NpcVariant(byte[] ordinary, byte[] alternate, bool pcIsCritter, bool pcIsMale)
        => pcIsCritter && !pcIsMale ? alternate : ordinary;
    internal static DlgRecord[] Group(DlgDocument document, int node, bool numeric)
    {
        Require(node > 0 && document.TryGetRecord(node, out var record) && record!.Role == DlgRecordRole.Npc, "InvalidNpcStart");
        var rows = numeric ? document.Records.OrderBy(r => r.Id).ToArray() : document.Records.ToArray();
        var index = Array.FindIndex(rows, r => r.Id == node);
        return rows.Skip(index + 1).TakeWhile(r => r.Role == DlgRecordRole.Player).ToArray();
    }
    internal static (int ScriptLine, int Node) Resolve(byte[] bytes, uint flags, bool? following, bool? jilted)
    {
        Require(bytes.Length >= 64 && bytes.Length <= 1024 * 1024, "ScriptEnvelope");
        var count = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(52));
        var capacity = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(56));
        Require(count > 0 && count <= 4096 && capacity >= count && capacity <= 4096 && bytes.Length == 64 + count * 132, "ScriptCount");
        int I(int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset));
        var line = 0; var seen = new HashSet<int>();
        for (var step = 0; step < 128; step++)
        {
            Require(line >= 0 && line < count && seen.Add(line), "ScriptCycleOrEntryBounds");
            var offset = 64 + line * 132; var type = I(offset);
            bool condition;
            switch (type)
            {
                case 0: condition = true; break; // No active operands; unused serialized slots are opaque.
                case 3:
                    Require(bytes[offset + 4] == 3, "UnsupportedValueRepresentation");
                    var flag = I(offset + 12); Require(flag is >= 0 and < 32, "InvalidFlag");
                    condition = (flags & (1u << flag)) != 0; break;
                case 18:
                    Require(bytes[offset + 4] == 1 && bytes[offset + 5] == 0, "WrongParticipants");
                    Require(following.HasValue, "FollowingQueryFrontier"); condition = following!.Value; break;
                case 39:
                    Require(bytes[offset + 4] == 1, "WrongParticipants");
                    Require(jilted.HasValue, "JiltedQueryFrontier"); condition = jilted!.Value; break;
                default: throw new ProbeFailure("UnsupportedConditionFrontier");
            }
            var action = offset + (condition ? 44 : 88); var operation = I(action);
            if (operation == 0) { line++; continue; }
            if (operation is 3 or 4)
            {
                Require(bytes[action + 4] == 3, "UnsupportedValueRepresentation");
                var value = I(action + 12);
                if (operation == 4) { Require(value > 0, "InvalidDlgNode"); return (line, value); }
                line = value; continue;
            }
            throw new ProbeFailure("UnsupportedActionFrontier");
        }
        throw new ProbeFailure("ScriptTraversalLimit");
    }
    internal static List<ObjectRecord> Objects(byte[] bytes, Dictionary<int, Layout> layouts, bool aggregate, bool prototype)
    {
        var cursor = new Cursor(bytes); if (aggregate) cursor.Take(16);
        var rows = new List<ObjectRecord>();
        while (cursor.Offset < bytes.Length)
        {
            Require(rows.Count < 16384, "ObjectCountLimit");
            var start = cursor.Offset; Require(cursor.Int() == 119, "ObjectVersion");
            var parent = cursor.Id(prototype); var id = cursor.Id(false); var type = cursor.Int();
            Require(type is >= 0 and <= 17 && layouts.TryGetValue(type, out _), "UnknownObjectLayout");
            Require(prototype ? parent.Kind == -1 && id.Kind == 1 : parent.Kind == 1 && id.Kind == 2, "MalformedObjectIdentity");
            var layout = layouts[type];
            Require(layout.Fields.Length <= 160 && layout.GroupLengths.Length is >= 3 and <= 5 &&
                layout.GroupLengths.All(n => n is > 0 and <= 32) && layout.GroupLengths.Sum() == layout.Fields.Length, "InvalidLayout");
            var count = prototype ? -1 : cursor.Short(); var present = new List<int>(); var baseIndex = 0;
            foreach (var length in layout.GroupLengths)
            {
                var mask = cursor.UInt(); Require(length == 32 || (mask >> length) == 0, "ReservedMaskBit");
                for (var bit = 0; bit < length; bit++) if ((mask & (1u << bit)) != 0) present.Add(baseIndex + bit);
                baseIndex += length;
            }
            Require(prototype || count == present.Count, "FieldCountMismatch");
            long? location = null; int? description = null; Dictionary<int, Binding>? scripts = null;
            for (var i = 0; i < layout.Fields.Length; i++)
            {
                if (!prototype && !present.Contains(i)) continue;
                var value = cursor.Field(layout.Fields[i]);
                if (i == 1) { Require(layout.Fields[i] == "p64", "LocationLayout"); location = (long?)value; }
                if (i == 22) { Require(layout.Fields[i] == "i", "DescriptionLayout"); description = (int?)value; }
                if (i == 31) { Require(layout.Fields[i] == "ascript", "AttachmentLayout"); scripts = value as Dictionary<int, Binding> ?? new(); }
            }
            rows.Add(new(start, cursor.Offset, type, prototype ? id.Number : parent.Number, Hash(id.Bytes), location, description, scripts));
            if (prototype) { Require(cursor.Offset == bytes.Length, "PrototypeTrailingBytes"); break; }
        }
        Require(rows.Count > 0, "EmptyObjects"); return rows;
    }
}

internal sealed class Cursor(byte[] bytes)
{
    public int Offset { get; private set; }
    public byte[] Take(int count)
    {
        Program.Require(count >= 0 && count <= bytes.Length - Offset, "TruncatedRecord");
        var result = bytes.AsSpan(Offset, count).ToArray(); Offset += count; return result;
    }
    public short Short() => BinaryPrimitives.ReadInt16LittleEndian(Take(2));
    public int Int() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
    public uint UInt() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
    public bool Present() { var v = Take(1)[0]; Program.Require(v <= 1, "InvalidPresence"); return v == 1; }
    public (short Kind, int Number, byte[] Bytes) Id(bool blocked)
    {
        var b = Take(24); var kind = BinaryPrimitives.ReadInt16LittleEndian(b);
        Program.Require(kind is >= 0 and <= 3 || blocked && kind == -1, "MalformedObjectIdentity");
        return (kind, BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(8)), b);
    }
    public object? Field(string kind)
    {
        if (kind == "i") return Int();
        Program.Require(kind is "p64" or "poid" or "s" or "ai32" or "au32" or "ai64" or "au64" or "ascript" or "aquest" or "aoid", "UnknownFieldLayout");
        if (!Present()) return null;
        if (kind == "p64") return BinaryPrimitives.ReadInt64LittleEndian(Take(8));
        if (kind == "poid") return Id(false);
        if (kind == "s") { var n = Int(); Program.Require(n is >= 0 and <= 4096, "StringLimit"); Program.Require(Take(n + 1)[^1] == 0, "StringTerminator"); return null; }
        var size = Int(); var count = Int(); _ = Int();
        var expected = kind switch { "ai32" or "au32" => 4, "ai64" or "au64" => 8, "ascript" => 12, "aquest" => 16, "aoid" => 24, _ => 0 };
        Program.Require(size == expected && count is >= 0 and <= 4096, "ArrayEnvelope");
        var payload = Take(size * count); var words = Int(); Program.Require(words is >= 1 and <= 128, "ArrayBitmapLimit");
        var keys = new List<int>();
        for (var w = 0; w < words; w++) { var bits = UInt(); for (var bit = 0; bit < 32; bit++) if ((bits & (1u << bit)) != 0) keys.Add(w * 32 + bit); }
        Program.Require(keys.Count == count, "ArrayBitmapPopulation");
        if (kind != "ascript") return null;
        var result = new Dictionary<int, Binding>();
        for (var i = 0; i < count; i++)
        {
            Program.Require(keys[i] < 36, "InvalidAttachmentEvent");
            var data = payload.AsSpan(i * 12, 12);
            var script = BinaryPrimitives.ReadInt32LittleEndian(data[8..]);
            Program.Require(script is >= 0 and <= 99999, "InvalidScriptId");
            result.Add(keys[i], new(BinaryPrimitives.ReadUInt32LittleEndian(data), BinaryPrimitives.ReadUInt32LittleEndian(data[4..]), script));
        }
        return result;
    }
}
