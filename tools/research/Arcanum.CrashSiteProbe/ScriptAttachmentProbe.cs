using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Arcanum.Data;
using Arcanum.Data.Content;

namespace Arcanum.CrashSiteProbe;

// ARC-RES-037 research only: where the installed module attaches a given set of scripts
// (sector objects, mobiles, game-generated aggregates, tile and sector attachments,
// prototypes, terrain templates), a lenient scan of every DLG for writes of given global
// flags (including the files the strict DLG reader refuses), and what the call-ex actions
// reachable from given root scripts run. Independently written from the pinned CE locators
// cited in docs/research/virgil-script-attachments.md; no CE code or structure is reused.
// Only numbers, booleans, file names of the module's own map folders and SHA-256 digests
// leave the probe; MES and DLG text stays in memory.
internal static partial class Program
{
    private const int SaNameSlot = 21, SaDescriptionSlot = 22, SaCallExAction = 81, SaMaxTargets = 64;

    // A sector file name is its key: x in the low 26 bits, y above them.
    internal static (int? X, int? Y) SaSector(string file)
    {
        var stem = Path.GetFileNameWithoutExtension(file);
        if (!long.TryParse(stem, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var key)) return (null, null);
        return ((int)(key & 0x3FFFFFF), (int)(key >> 26));
    }

    internal sealed record SaHit(string Map, string Kind, string File, int? SourceIndex, int? Type, int? Prototype, int Sap, int Script, int? Name, int? Description, int? X, int? Y);

    // A lenient GF writer scan over raw DLG text: an ASCII letter pair "GF" (any case) as an
    // operation code, then optional spaces and the flag number. It reads every file, including
    // ones the strict reader refuses, and over-reports rather than misses (a GF in a
    // condition field or text is counted too).
    internal static List<(int Offset, int Flag, int? Value)> SaLenientFlagWrites(ReadOnlySpan<byte> bytes, IReadOnlySet<int> flags)
    {
        var hits = new List<(int, int, int?)>();
        for (var i = 0; i + 2 < bytes.Length; i++)
        {
            if ((bytes[i] | 0x20) != 'g' || (bytes[i + 1] | 0x20) != 'f') continue;
            if (i > 0 && char.IsAsciiLetter((char)bytes[i - 1])) continue;
            var after = bytes[(i + 2)..];
            var flag = CcAtoi(after);
            if (!flags.Contains(flag.Value)) continue;
            var j = 0;
            while (j < after.Length && CcIsSpace(after[j])) j++;
            if (j >= after.Length || after[j] is < (byte)'0' or > (byte)'9') continue;
            var value = CcSecondOperand(after);
            hits.Add((i, flag.Value, value.Value));
        }
        return hits;
    }

    private static int ProbeScriptAttachments(string[] args)
    {
        // --game-root PATH --scripts a,b --flags f,g --roots r,s [--module NAME]
        if (args.Length is not (8 or 10) || args[0] != "--game-root" || args[2] != "--scripts" || args[4] != "--flags" || args[6] != "--roots" || (args.Length == 10 && args[8] != "--module"))
            throw new ArgumentException("ATTACH_USAGE");
        static HashSet<int> Numbers(string list)
        {
            var values = list.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(v => int.TryParse(v, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) && n > 0 ? n : throw new ArgumentException("ATTACH_USAGE")).ToHashSet();
            return values.Count is > 0 and <= SaMaxTargets ? values : throw new ArgumentException("ATTACH_USAGE");
        }
        var targets = Numbers(args[3]); var flags = Numbers(args[5]); var roots = Numbers(args[7]);
        var discovery = ContentMountPlan.Discover(args[1], args.Length == 10 ? args[9] : ContentMountPlan.DefaultModule);
        if (discovery.Status != ContentMountStatus.Ready) throw new InvalidDataException("ATTACH_MOUNT_" + discovery.Status);
        var opened = ContentStack.Open(discovery.Plan!);
        if (opened.Status != ContentStackStatus.Ready) throw new InvalidDataException("ATTACH_STACK_" + opened.Status);
        using var stack = opened.Stack!;
        var keys = CensusKeys(stack);
        var refusals = new CensusTally();
        byte[]? Read(ContentCandidate candidate, string role)
        {
            var result = stack.Read(candidate);
            if (result.Status == ContentReadStatus.Read) return result.Bytes!;
            refusals.Add($"{role}|ATTACH_READ_{result.Status}");
            return null;
        }

        // Prototypes by number, for objects that inherit their scripts.
        var prototypes = new Dictionary<int, PrototypeInfo>();
        var hits = new List<SaHit>();
        foreach (var (key, winner) in keys.Where(k => k.Key.StartsWith("proto/", StringComparison.Ordinal) && k.Key.EndsWith(".pro", StringComparison.Ordinal)))
        {
            if (Read(winner, "prototype") is not { } bytes) continue;
            PrototypeInfo proto;
            try { proto = ObjectWire.ParsePrototype(bytes); } catch (Exception e) when (e is InvalidDataException or OverflowException or ArgumentException) { refusals.Add("prototype|parse"); continue; }
            var number = proto.Id.AidValue();
            if (!prototypes.TryAdd(number, proto)) refusals.Add("prototype|duplicate_number");
            int? Num(int slot) => proto.NumericFields.TryGetValue(slot, out var v) ? v : null;
            foreach (var s in proto.Scripts.Where(s => targets.Contains(s.script)))
                hits.Add(new("", "prototype", key, null, proto.Type, number, s.sap, s.script, Num(SaNameSlot), Num(SaDescriptionSlot), null, null));
        }

        var unresolvedPrototypes = 0;
        void Objects(string map, string kind, string file, ReadOnlySpan<byte> window, IEnumerable<InstanceInfo> instances)
        {
            foreach (var instance in instances)
            {
                PrototypeInfo? proto = instance.PrototypeId.Type == 1 && prototypes.TryGetValue(instance.PrototypeId.AidValue(), out var p) ? p : null;
                if (proto is null && !instance.ScriptsOverridden) { unresolvedPrototypes++; continue; }
                var scripts = instance.ScriptsOverridden ? DecodeScripts(window, instance.Arrays) : proto!.Scripts;
                foreach (var s in scripts.Where(s => targets.Contains(s.script)))
                {
                    int? Num(int slot) => instance.NumericFields.TryGetValue(slot, out var v) ? v : proto is not null && proto.NumericFields.TryGetValue(slot, out var w) ? w : null;
                    int? x = instance.Location is { } loc ? unchecked((int)loc) : null, y = instance.Location is { } loc2 ? unchecked((int)(loc2 >> 32)) : null;
                    hits.Add(new(map, kind, file, instance.SourceIndex, instance.Type, proto is null ? null : instance.PrototypeId.AidValue(), s.sap, s.script, Num(SaNameSlot), Num(SaDescriptionSlot), x, y));
                }
            }
        }
        void Sector(string map, string kind, string file, byte[] bytes)
        {
            if (bytes.Length > CensusMaxSectorBytes) { refusals.Add($"{kind}|bytes"); return; }
            SectorSlice slice; SectorFraming framing;
            try { slice = SectorSlice.Parse(bytes, false); framing = SectorFraming.Parse(bytes, slice.LightCount); }
            catch (InvalidDataException) { refusals.Add($"{kind}|framing"); return; }
            // Tile attachments: count word, then 24-byte nodes whose fifth word is the script; the sector script is three words.
            for (var i = 0; i < framing.TileScriptCount; i++)
            {
                var script = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(framing.TileScriptsStart + 4 + i * 24 + 16));
                // The node's second word is the tile within the sector (x + 64 * y); report the world tile.
                var tile = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(framing.TileScriptsStart + 4 + i * 24 + 4));
                var (sx, sy) = SaSector(file);
                if (targets.Contains(script)) hits.Add(new(map, kind + "-tile", file, i, null, null, -1, script, null, null,
                    sx is { } ax ? ax * 64 + (tile & 63) : null, sy is { } ay ? ay * 64 + ((tile >> 6) & 63) : null));
            }
            if (framing.SectorScriptBytes == 12 && BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(framing.SectorScriptStart + 8)) is var sectorScript && targets.Contains(sectorScript))
                hits.Add(new(map, kind + "-script", file, null, null, null, -1, sectorScript, null, null, null, null));
            try
            {
                var window = bytes.AsSpan(framing.ObjectStart, framing.ObjectBytes);
                Objects(map, kind + "-object", file, window, ObjectWire.ParseWindow(window, slice.TrailingInt32));
            }
            catch (Exception e) when (e is InvalidDataException or OverflowException or ArgumentException) { refusals.Add($"{kind}|objects"); }
        }

        var maps = 0; var sectors = 0; var mobiles = 0; var aggregates = 0; var templates = 0;
        foreach (var (key, winner) in keys.Where(k => k.Key.StartsWith("maps/", StringComparison.Ordinal) && k.Key.Count(c => c == '/') == 2))
        {
            var parts = key.Split('/'); var map = parts[1]; var file = parts[2];
            if (file.EndsWith(".sec", StringComparison.Ordinal)) { if (Read(winner, "sector") is { } b) { sectors++; Sector(map, "sector", file, b); } }
            else if (file.EndsWith(".mob", StringComparison.Ordinal) && Read(winner, "mobile") is { } m)
            {
                mobiles++;
                try { var instance = ObjectWire.ParseNext(m, out _); Objects(map, "mobile", file, m, [instance]); }
                catch (Exception e) when (e is InvalidDataException or OverflowException or ArgumentException) { refusals.Add("mobile|parse"); }
            }
        }
        maps = keys.Keys.Where(k => k.StartsWith("maps/", StringComparison.Ordinal) && k.Count(c => c == '/') == 2).Select(k => k.Split('/')[1]).Distinct().Count();
        var mapNames = keys.Keys.Where(k => k.StartsWith("maps/", StringComparison.Ordinal) && k.Count(c => c == '/') == 2).Select(k => k.Split('/')[1]).Distinct().ToArray();
        foreach (var (key, winner) in keys.Where(k => k.Key.StartsWith("maps/", StringComparison.Ordinal) && k.Key.Count(c => c == '/') == 1))
        {
            if (Read(winner, "aggregate") is not { } bytes) continue;
            var owner = mapNames.FirstOrDefault(n => string.Equals(CaveAggregateSelector(n), key[5..], StringComparison.OrdinalIgnoreCase)) ?? "?";
            try
            {
                foreach (var record in FrameAggregate(bytes, CensusMaxAggregateBytes))
                    Objects(owner, "aggregate", "<aggregate>", bytes.AsSpan(record.Offset, record.Length), [record.Instance]);
                aggregates++;
            }
            catch (Exception e) when (e is InvalidDataException or OverflowException or ArgumentException) { refusals.Add("aggregate|parse"); }
        }
        foreach (var (key, winner) in keys.Where(k => k.Key.StartsWith("terrain/", StringComparison.Ordinal) && k.Key.EndsWith(".sec", StringComparison.Ordinal)))
            if (Read(winner, "template") is { } b) { templates++; Sector("<terrain>", "template", key, b); }

        // DLG GF writers, strict and lenient.
        var dialogs = 0; var refusedDialogs = 0; var flagHits = new List<object>();
        foreach (var (key, winner) in keys.Where(k => k.Key.StartsWith("dlg/", StringComparison.Ordinal) && k.Key.EndsWith(".dlg", StringComparison.Ordinal)))
        {
            if (Read(winner, "dlg") is not { } bytes) continue;
            dialogs++;
            var strict = true;
            try { _ = DlgReader.Read(bytes); } catch (Exception e) when (e is DlgException or InvalidDataException) { strict = false; refusedDialogs++; }
            foreach (var (offset, flag, value) in SaLenientFlagWrites(bytes, flags))
                flagHits.Add(new { dlg = key, strict_reader_accepts = strict, offset, flag, value });
        }

        // Call-ex actions reachable from the roots: the attachee and triggerer focus, and the attachment point it runs.
        var scriptBytes = new Dictionary<int, byte[]>();
        foreach (var (key, winner) in keys.Where(k => k.Key.StartsWith("scr/", StringComparison.Ordinal) && k.Key.EndsWith(".scr", StringComparison.Ordinal)))
            if (ScriptNumberFromFile(key[4..]) is { } n && Read(winner, "scr") is { } b) scriptBytes.TryAdd(n, b);
        (IReadOnlyList<CaveScriptEdge>, int)? Census(int n)
        {
            if (!scriptBytes.TryGetValue(n, out var b)) return null;
            try { var c = CaveScriptCensus(b); return (c.Edges, c.Unresolved); } catch (InvalidDataException) { return null; }
        }
        var (reached, unresolved, missing) = AlReach(roots, Census);
        var callEx = reached.SelectMany(n => AlActionsAndConditions(scriptBytes[n]).Where(x => !x.Condition && x.Type == SaCallExAction)
            .Select(x => new { script = n, line = x.Line, attachee_focus = (int)x.Kinds[0], attachee_index = x.Values[0], sap_kind = (int)x.Kinds[1], sap = x.Values[1],
                line_kind = (int)x.Kinds[2], start_line = x.Values[2], triggerer_focus = (int)x.Kinds[3] })).ToArray();

        // Map catalogue (rules\MapList.mes): an unbroken run of entries from 5000, the name before the first comma.
        var catalogue = new SortedDictionary<int, string>();
        if (stack.Resolve("rules/maplist.mes") is { Status: ContentResolutionStatus.Resolved } listed && Read(listed.Winner!, "maplist") is { } listBytes &&
            Arcanum.Data.Messages.MesFile.Parse(listBytes) is { Status: Arcanum.Data.Messages.MesParseStatus.Parsed, File: { } listFile })
            for (var n = 5000; listFile.Find(n) is { } entry; n++) catalogue[n] = Encoding.Latin1.GetString(entry.Bytes.Span).Split(',')[0].Trim();
        int? CatalogueOf(string map) => catalogue.Where(c => string.Equals(c.Value, map, StringComparison.OrdinalIgnoreCase)).Select(c => (int?)c.Key).FirstOrDefault();
        var hitMaps = hits.Select(h => h.Map).Where(m => m.Length > 0 && m[0] != '<').Distinct().Order(StringComparer.Ordinal)
            .Select(m => new { map = m, catalogue = CatalogueOf(m) }).ToArray();

        var report = new
        {
            module = stack.Plan.ModuleName, keys = keys.Count, targets = targets.Order().ToArray(), flags = flags.Order().ToArray(), roots = roots.Order().ToArray(),
            scanned = new { maps, sectors, mobiles, aggregates, terrain_templates = templates, prototypes = prototypes.Count, dialogs, dialogs_refused_by_strict_reader = refusedDialogs, unresolved_prototypes = unresolvedPrototypes },
            hits = hits.OrderBy(h => h.Script).ThenBy(h => h.Map, StringComparer.Ordinal).ThenBy(h => h.Kind, StringComparer.Ordinal).ThenBy(h => h.File, StringComparer.Ordinal).ThenBy(h => h.SourceIndex)
                .Select(h => new { script = h.Script, map = h.Map, kind = h.Kind, file = h.File, source_index = h.SourceIndex, type = h.Type, prototype = h.Prototype, sap = h.Sap, name = h.Name, description = h.Description, x = h.X, y = h.Y }).ToArray(),
            hit_maps = hitMaps, catalogue_entries = catalogue.Count,
            catalogue_lookup = new[] { 5000, 5023, 5078 }.Select(n => new { catalogue = n, map = catalogue.GetValueOrDefault(n) }).ToArray(),
            target_lines = targets.Order().Where(scriptBytes.ContainsKey).Select(n => new { script = n, lines = AlDecode(scriptBytes[n]) }).ToArray(),
            dialog_flag_writes = flagHits,
            reach = new { reached = reached.ToArray(), unresolved_edges = unresolved, missing = missing.ToArray(), call_ex = callEx },
            refusals = refusals.Snapshot(),
        };
        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    private static void ScriptAttachmentSelfTest()
    {
        static void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException("attach self-test failed: " + label); }
        var flags = new HashSet<int> { 2593 };
        var hits = SaLenientFlagWrites(Encoding.ASCII.GetBytes("{1}{x}{ gf 2593 1, GF 7 1, XGF 2593 1, Gf2593 0, gf  x2593}"), flags);
        Check(hits.Count == 2 && hits[0].Flag == 2593 && hits[0].Value == 1 && hits[1].Value == 0, "lenient GF scan");
        Check(SaLenientFlagWrites(Encoding.ASCII.GetBytes("GF 25930 1"), flags).Count == 0, "different flag");
        Check(SaSector("19797116386.sec") == (1506, 295) && SaSector("x.sec") == (null, null), "sector key");
        Console.WriteLine("script-attachments self-test: PASS (lenient GF scan, sector key)");
    }
}
