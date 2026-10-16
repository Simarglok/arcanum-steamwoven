using System.Buffers.Binary;
using System.Text.Json;
using Arcanum.Data;

namespace Arcanum.CrashSiteProbe;

// ARC-WORLD-009 research only: how the Crash Site cave transition fires and where it
// leads. Independently authored from the pinned CE locators cited in
// docs/research/cave-transition-evidence.md; no CE code or structure is reused.
// No script VM, no movement, no map lifecycle: it decodes the two literal script
// files, the two attached trigger objects, both arrival cells and the cave walls.
internal static partial class Program
{
    private const int CaveCatalogue = 5078;
    private const int ExteriorCatalogue = 5000;
    private const long ExteriorTriggerSector = 86570436011;
    private const long CaveSector = 2080374816;
    private const int CaveMobileBytes = 6_698;
    private const string CaveMobileSha = "aa29827de32c5cb6bf7aa03ea049f9f01d7698b003097172cd26ab80c9658318";
    private const int ScriptHeaderBytes = 64;
    private const int ScriptLineBytes = 132;
    private const int MaxScriptBytes = 64 * 1024;
    // Pinned whole-archive identities, in this exact order. The script scan reads only
    // DAT indexes plus the two selected script entries.
    private static readonly (string Role, long Bytes, string Sha256)[] CaveArchives =
    [
        ("module", ExpectedModuleBytes, ExpectedModuleSha256),
        ("resource1", 447_470_816, "6dd9d10ef0932e02fc88f0eb5367b1bc37275db51d60bd68ac9bd7ef2d478576"),
        ("resource2", ExpectedResourceBytes, ExpectedResourceSha256),
        ("resource3", 9_605_011, "cbadd76bb3df798fa25dcc05871af1ea751c1164fb1426dccc520cd7d9edc499"),
        ("resource4", 447_614, "975d43854fca8b7dd74e2b12ef2ff3fe6d3c0f99dc541151d33001469f89710c"),
        ("patch0", 18_804, "d9a3313f71d78115dcac489296bf3d8a5801dd55b20267bd6017b7a898364bce"),
    ];
    // Window/opening wall pieces are the p-piece values the pinned traversal code
    // treats separately; every other wall piece is a solid edge.
    private static readonly int[] OpeningWallPieces = [10, 13, 14, 17, 18, 19, 22, 25, 26, 29, 30, 31, 32];

    internal sealed record CaveOperand(int Kind, int Value);
    internal sealed record CaveAction(int Type, IReadOnlyList<CaveOperand> Operands);
    internal sealed record CaveScriptLine(int Line, int Condition, CaveAction True, CaveAction False);
    internal sealed record CaveScript(uint FileFlags, int Capacity, IReadOnlyList<CaveScriptLine> Lines);
    internal readonly record struct CaveWallArt(int ArtClass, int Number, int Piece, int Rotation, int EdgeRotation, int Variation);

    // Operand counts are fixed per admitted condition/action type. Unused slots hold
    // editor memory and are never interpreted. Anything else is refused, not guessed.
    private static readonly IReadOnlyDictionary<int, int> AdmittedActions = new Dictionary<int, int>
    {
        [0] = 0,  // do nothing
        [1] = 0,  // return and skip default
        [2] = 0,  // return and run default
        [44] = 4, // teleport: focus object, map, x, y
    };

    internal static CaveScript DecodeCaveScript(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < ScriptHeaderBytes || bytes.Length > MaxScriptBytes) throw new InvalidDataException("CAVE_SCRIPT_BOUND");
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(bytes[48..]);
        var count = BinaryPrimitives.ReadInt32LittleEndian(bytes[52..]);
        var capacity = BinaryPrimitives.ReadInt32LittleEndian(bytes[56..]);
        if (count < 0 || capacity < count || bytes.Length != ScriptHeaderBytes + (long)count * ScriptLineBytes) throw new InvalidDataException("CAVE_SCRIPT_FRAMING");
        var lines = new List<CaveScriptLine>();
        for (var line = 0; line < count; line++)
        {
            var row = bytes.Slice(ScriptHeaderBytes + line * ScriptLineBytes, ScriptLineBytes);
            var condition = BinaryPrimitives.ReadInt32LittleEndian(row);
            if (condition != 0) throw new InvalidDataException("CAVE_SCRIPT_CONDITION_NOT_ADMITTED");
            lines.Add(new(line, condition, Action(row.Slice(44, 44)), Action(row.Slice(88, 44))));
        }
        return new(flags, capacity, lines);

        static CaveAction Action(ReadOnlySpan<byte> slot)
        {
            var type = BinaryPrimitives.ReadInt32LittleEndian(slot);
            if (!AdmittedActions.TryGetValue(type, out var used)) throw new InvalidDataException("CAVE_SCRIPT_ACTION_NOT_ADMITTED");
            var operands = new CaveOperand[used];
            for (var i = 0; i < used; i++) operands[i] = new(slot[4 + i], BinaryPrimitives.ReadInt32LittleEndian(slot[(12 + 4 * i)..]));
            return new(type, operands);
        }
    }

    /// <summary>Checks the one admitted shape: line0 literal teleport of the triggerer, line1 a return.</summary>
    internal static (int Map, int X, int Y, int Return) CaveTeleportShape(CaveScript script)
    {
        if (script.FileFlags != 0 || script.Lines.Count != 2) throw new InvalidDataException("CAVE_SCRIPT_SHAPE");
        var teleport = script.Lines[0].True; var tail = script.Lines[1].True;
        if (teleport.Type != 44 || script.Lines[0].False.Type != 0 || script.Lines[1].False.Type != 0 || tail.Type is not (1 or 2))
            throw new InvalidDataException("CAVE_SCRIPT_SHAPE");
        // Focus kind 0 is the triggerer; kind 3 is a literal number.
        if (teleport.Operands[0].Kind != 0 || teleport.Operands.Skip(1).Any(o => o.Kind != 3)) throw new InvalidDataException("CAVE_SCRIPT_COMPUTED_OPERAND");
        return (teleport.Operands[1].Value, teleport.Operands[2].Value, teleport.Operands[3].Value, tail.Type);
    }

    internal static CaveWallArt DecodeWallArt(uint aid)
    {
        var rotation = (int)((aid >> 11) & 7);
        return new((int)(aid >> 28), (int)((aid >> 20) & 255), (int)((aid >> 14) & 63), rotation, rotation | 1, (int)((aid >> 8) & 3));
    }

    // Name selector used for a map's game-generated mobile aggregate: ASCII letters
    // are shifted by 13 with the pinned source's wrap (one letter short of a full
    // alphabet), then the whole selector is reversed. Comparison is ordinal-ignore-case
    // because the installed file system preserves only the lower-case spelling.
    internal static string CaveAggregateSelector(string name)
    {
        var chars = name.Select(c => c switch
        {
            >= 'A' and <= 'Z' => (char)(c + 13 > 'Z' ? c + 13 - 24 : c + 13),
            >= 'a' and <= 'z' => (char)(c + 13 > 'z' ? c + 13 - 24 : c + 13),
            _ => c,
        }).ToArray();
        Array.Reverse(chars);
        return new string(chars);
    }

    private static (int X, int Y)? CaveStep((int X, int Y) cell, int rotation) => rotation switch
    {
        1 => (cell.X - 1, cell.Y),
        3 => (cell.X, cell.Y + 1),
        5 => (cell.X + 1, cell.Y),
        7 => (cell.X, cell.Y - 1),
        _ => null,
    };

    private static int ProbeCaveTransition(string[] args)
    {
        if (args.Length != 6 + CaveArchives.Length * 2 || args[0] != "--prototype-root" || args[2] != "--mobile" || args[4] != "--cave-mobile")
            throw new ArgumentException("CAVE_USAGE");
        var archivePaths = new string[CaveArchives.Length];
        for (var i = 0; i < CaveArchives.Length; i++)
        {
            if (args[6 + i * 2] != "--" + CaveArchives[i].Role) throw new ArgumentException("CAVE_USAGE");
            archivePaths[i] = args[7 + i * 2];
        }
        var prototypeRoot = args[1]; var mobilePath = args[3]; var cavePath = args[5];
        var before = archivePaths.Select(InputFingerprint.Create).ToArray();
        for (var i = 0; i < CaveArchives.Length; i++)
            if (before[i] != new InputFingerprint(CaveArchives[i].Bytes, CaveArchives[i].Sha256)) throw new InvalidDataException("CAVE_ARCHIVE_IDENTITY");
        var mobileBefore = InputFingerprint.Create(mobilePath); var caveBefore = InputFingerprint.Create(cavePath);
        if (mobileBefore != new InputFingerprint(AreaMobileBytes, AreaMobileSha) || caveBefore != new InputFingerprint(CaveMobileBytes, CaveMobileSha))
            throw new InvalidDataException("CAVE_AGGREGATE_IDENTITY");

        // Index caps follow the largest pinned archive; entry caps stay at the bounded 1 MiB.
        var options = new DatReaderOptions { MaxArchiveBytes = checked((uint)CaveArchives.Max(a => a.Bytes)), MaxStoredEntryBytes = MaxDecodedSectorBytes, MaxDecodedEntryBytes = MaxDecodedSectorBytes };
        var archives = archivePaths.Select(p => DatArchive.Open(p, options)).ToArray();
        try
        {
            var module = archives[0];
            // Map catalogue rows: only numeric fields leave the probe.
            var catalogue = ObjectArt.ParseMes(module.ReadEntry(AreaUnique(module, "rules/maplist.mes")));
            object Row(int key, string aggregatePath)
            {
                var fields = catalogue[key].Split(',').Select(f => f.Trim()).ToArray();
                var name = fields[0];
                if (name.Length == 0 || name.Any(c => c > 127 || c is '/' or '\\' or '\0') || name.Contains("..", StringComparison.Ordinal)) throw new InvalidDataException("CAVE_UNSAFE_SELECTOR");
                var tags = fields.Skip(3).Select(f => f.Split(':', 2)).ToDictionary(p => p[0].Trim(), p => p.Length == 2 ? p[1].Trim() : "");
                return new
                {
                    catalogue = key, runtime = key - 4999,
                    start_x = int.Parse(fields[1], System.Globalization.CultureInfo.InvariantCulture), start_y = int.Parse(fields[2], System.Globalization.CultureInfo.InvariantCulture),
                    tags, selector_matches_aggregate = string.Equals(CaveAggregateSelector(name), Path.GetFileName(aggregatePath), StringComparison.OrdinalIgnoreCase),
                };
            }
            var caveName = catalogue[CaveCatalogue].Split(',')[0].Trim(); var cavePrefix = "maps/" + caveName + "/";
            var rows = new[] { Row(ExteriorCatalogue, mobilePath), Row(CaveCatalogue, cavePath) };

            // Normal-game cave counterpart: exact EOF framing and a semantic bijection
            // with the module's loose cave mobiles.
            var caveBytes = File.ReadAllBytes(cavePath);
            var aggregate = FrameAggregate(caveBytes, CaveMobileBytes);
            var loose = module.Entries.Where(e => AreaName(e).StartsWith(cavePrefix, StringComparison.OrdinalIgnoreCase) && AreaName(e).EndsWith(".mob", StringComparison.Ordinal)).OrderBy(e => e.Ordinal).ToArray();
            var expected = new Dictionary<string, int>();
            foreach (var entry in loose)
            {
                var bytes = module.ReadEntry(entry); var instance = ObjectWire.ParseNext(bytes, out var consumed);
                if (consumed != bytes.Length) throw new InvalidDataException("CAVE_LOOSE_TRAILING_BYTES");
                var key = SemanticHash(bytes, instance); expected[key] = expected.GetValueOrDefault(key) + 1;
            }
            var actual = aggregate.GroupBy(r => SemanticHash(caveBytes.AsSpan(r.Offset, r.Length).ToArray(), r.Instance)).ToDictionary(g => g.Key, g => g.Count());
            var bijection = expected.Keys.Union(actual.Keys).Count(k => expected.GetValueOrDefault(k) != actual.GetValueOrDefault(k));
            var exact = aggregate.Count(r => loose.Any(e => AreaHash(module.ReadEntry(e)) == r.Sha256));

            // Script variants across every pinned archive; exactly one each is required.
            var scripts = new List<object>(); var shapes = new Dictionary<int, (int Map, int X, int Y, int Return)>();
            foreach (var number in new[] { 2793, 1532 })
            {
                var variants = archives.SelectMany((a, i) => a.Entries.Where(e => ScriptNumber(e) == number).Select(e => (Archive: i, Entry: e))).ToArray();
                if (variants.Length != 1) throw new InvalidDataException("CAVE_SCRIPT_MISSING_OR_AMBIGUOUS");
                var (index, entry) = variants[0];
                if (entry.DeclaredOutputSize > MaxScriptBytes) throw new InvalidDataException("CAVE_SCRIPT_BOUND");
                var bytes = archives[index].ReadEntry(entry); var decoded = DecodeCaveScript(bytes); shapes[number] = CaveTeleportShape(decoded);
                scripts.Add(new { script = number, archive = CaveArchives[index].Role, entry.Ordinal, stored_bytes = entry.StoredSize, decoded_bytes = bytes.Length, sha256 = AreaHash(bytes), file_flags = decoded.FileFlags, decoded.Capacity, lines = decoded.Lines });
            }

            // The two sectors that carry the triggers and both arrival cells.
            var exterior = CaveSectorObjects(module, prototypeRoot, "maps/" + catalogue[ExteriorCatalogue].Split(',')[0].Trim() + "/", ExteriorTriggerSector);
            var cave = CaveSectorObjects(module, prototypeRoot, cavePrefix, CaveSector);
            object Trigger((IReadOnlyList<CaveObject> Objects, byte[] Bytes, SectorFraming Framing) sector, int script)
            {
                var hits = sector.Objects.Where(o => o.Scripts.Any(s => s.script == script)).ToArray();
                if (hits.Length != 1) throw new InvalidDataException("CAVE_TRIGGER_MISSING_OR_AMBIGUOUS");
                var o = hits[0];
                return new
                {
                    o.SourceIndex, o.Type, o.Prototype, o.X, o.Y, art_class = (uint)o.Aid >> 28, o.Flags,
                    dont_draw = (o.Flags & 0x100) != 0, invisible = (o.Flags & 0x200) != 0, click_through = (o.Flags & 0x800) != 0, off = (o.Flags & 0x2) != 0, no_block = (o.Flags & 0x400) != 0,
                    scripts = o.Scripts, other_scripted_objects = sector.Objects.Count(x => x.Scripts.Count > 0 && x != o),
                };
            }
            var triggers = new[] { Trigger(exterior, 2793), Trigger(cave, 1532) };

            // Terrain blocking at the arrival cells by the written F1 rule of
            // area-passability-closure.md: mask bit, then facade bit0, then tile-name flags.
            var tileNames = ObjectArt.ParseMes(archives[2].ReadEntry(AreaUnique(archives[2], "art/tile/tilename.mes")));
            bool? TerrainBlocks(bool mask, uint raw)
            {
                if (mask) return true;
                if (raw >> 28 == 11) return (raw & 1) == 0;
                if (raw >> 28 != 0) return null;
                var category = ((raw >> 8) & 1, (raw >> 7) & 1) switch { (1, 1) => 0, (1, 0) => 1, (0, 1) => 2, _ => 3 };
                if (!tileNames.TryGetValue(category * 100 + (int)((raw >> 22) & 63), out var value)) return null;
                var slash = value.IndexOf('/');
                if (slash < 0) return value.Length >= 3 ? false : null;
                return value[(slash + 1)..].TakeWhile(c => c != ' ').Any(c => c is 'b' or 'f');
            }
            object Arrival((IReadOnlyList<CaveObject> Objects, byte[] Bytes, SectorFraming Framing) sector, long packed, int x, int y)
            {
                var sx = (int)(packed & 67108863); var sy = (int)(packed >> 26);
                var inside = x >= sx * 64 && x < sx * 64 + 64 && y >= sy * 64 && y < sy * 64 + 64;
                if (!inside) throw new InvalidDataException("CAVE_ARRIVAL_OUTSIDE_SECTOR");
                var ordinal = (x - sx * 64) + 64 * (y - sy * 64);
                var mask = TerrainSerializedBlockMaskBit(sector.Bytes.AsSpan(sector.Framing.BlockMaskStart, sector.Framing.BlockMaskBytes), ordinal);
                var tile = BinaryPrimitives.ReadUInt32LittleEndian(sector.Bytes.AsSpan(sector.Framing.TileStart + 4 * ordinal));
                var here = sector.Objects.Where(o => o.X == x && o.Y == y).ToArray();
                var edges = new[] { 1, 3, 5, 7 }.Select(r =>
                {
                    var leave = here.Where(o => o.Type == 0 && DecodeWallArt((uint)o.Aid).EdgeRotation == r).ToArray();
                    var next = CaveStep((x, y), r)!.Value;
                    var enter = sector.Objects.Where(o => o.Type == 0 && o.X == next.X && o.Y == next.Y && DecodeWallArt((uint)o.Aid).EdgeRotation == (r + 4) % 8).ToArray();
                    return new { rotation = r, leaving_walls = leave.Length, entering_walls = enter.Length, opening_pieces = leave.Concat(enter).Count(o => OpeningWallPieces.Contains(DecodeWallArt((uint)o.Aid).Piece)) };
                }).ToArray();
                return new { x, y, sector = packed, ordinal, block_mask_set = mask, tile_art_class = tile >> 28, terrain_blocks = TerrainBlocks(mask, tile), objects = here.Select(o => new { o.SourceIndex, o.Type, o.Prototype, o.Flags, wall = o.Type == 0 ? DecodeWallArt((uint)o.Aid) : (CaveWallArt?)null }).ToArray(), cardinal_edges = edges };
            }
            var arrivals = new[]
            {
                Arrival(cave, CaveSector, shapes[2793].X, shapes[2793].Y),
                Arrival(exterior, ExteriorTriggerSector, shapes[1532].X, shapes[1532].Y),
            };
            if (shapes[2793].Map != CaveCatalogue || shapes[1532].Map != ExteriorCatalogue) throw new InvalidDataException("CAVE_DESTINATION_CHANGED");

            var walls = cave.Objects.Where(o => o.Type == 0).ToArray();
            var wallArt = walls.Select(o => DecodeWallArt((uint)o.Aid)).ToArray();
            var census = new
            {
                walls = walls.Length, prototypes = walls.Select(o => o.Prototype).Distinct().Order().ToArray(),
                art_classes = wallArt.GroupBy(a => a.ArtClass).ToDictionary(g => g.Key, g => g.Count()),
                numbers = wallArt.GroupBy(a => a.Number).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
                pieces = wallArt.GroupBy(a => a.Piece).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
                edge_rotations = wallArt.GroupBy(a => a.EdgeRotation).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
                variations = wallArt.GroupBy(a => a.Variation).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
                opening_pieces = wallArt.Count(a => OpeningWallPieces.Contains(a.Piece)),
                flags = walls.GroupBy(o => o.Flags).ToDictionary(g => g.Key, g => g.Count()),
                shared_tiles = walls.GroupBy(o => (o.X, o.Y)).Count(g => g.Count() > 1),
                scenery = cave.Objects.Count(o => o.Type == 3), other = cave.Objects.Count(o => o.Type is not 0 and not 3),
            };

            var report = new
            {
                schema_version = 1,
                archives = CaveArchives.Select((a, i) => new { role = a.Role, before[i].Bytes, before[i].Sha256 }).ToArray(),
                mobile = mobileBefore, cave_mobile = caveBefore, catalogue = rows,
                cave_aggregate = new { records = aggregate.Count, loose = loose.Length, semantic_mismatch = bijection, byte_exact_records = exact },
                scripts, triggers, arrivals, wall_census = census,
                distances = new
                {
                    exterior_trigger_to_return = Math.Max(Math.Abs(92910 - shapes[1532].X), Math.Abs(82598 - shapes[1532].Y)),
                    cave_trigger_to_arrival = Math.Max(Math.Abs(2093 - shapes[2793].X), Math.Abs(2019 - shapes[2793].Y)),
                },
            };
            if (bijection != 0) throw new InvalidDataException("CAVE_AGGREGATE_BIJECTION");
            var after = archivePaths.Select(InputFingerprint.Create).ToArray();
            if (!after.SequenceEqual(before) || mobileBefore != InputFingerprint.Create(mobilePath) || caveBefore != InputFingerprint.Create(cavePath)) throw new InvalidDataException("CAVE_INPUT_CHANGED");
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        finally
        {
            foreach (var archive in archives) archive.Dispose();
        }
    }

    private sealed record CaveObject(int SourceIndex, int Type, int Prototype, int X, int Y, int Aid, uint Flags, IReadOnlyList<ScriptAttachment> Scripts);

    private static int? ScriptNumber(DatEntry entry)
    {
        var name = AreaName(entry);
        if (!name.StartsWith("scr/", StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".scr", StringComparison.OrdinalIgnoreCase)) return null;
        var digits = new string(name[4..].TakeWhile(char.IsAsciiDigit).ToArray());
        return digits.Length is > 0 and <= 6 ? int.Parse(digits, System.Globalization.CultureInfo.InvariantCulture) : null;
    }

    private static bool TerrainSerializedBlockMaskBit(ReadOnlySpan<byte> mask, int ordinal)
    {
        if (mask.Length != 512 || ordinal is < 0 or >= 4096) throw new InvalidDataException("CAVE_BLOCK_MASK");
        return (BinaryPrimitives.ReadUInt32LittleEndian(mask[(ordinal / 32 * 4)..]) & (1u << (ordinal % 32))) != 0;
    }

    private static (IReadOnlyList<CaveObject> Objects, byte[] Bytes, SectorFraming Framing) CaveSectorObjects(DatArchive module, string prototypeRoot, string prefix, long packed)
    {
        var name = prefix + packed.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".sec";
        var entry = AreaUnique(module, name);
        var x = checked((int)(packed & 67108863)); var y = checked((int)(packed >> 26));
        var bytes = module.ReadEntry(entry); var slice = SectorSlice.Parse(bytes, false); var framing = SectorFraming.Parse(bytes, slice.LightCount);
        var instances = ObjectWire.ParseWindow(bytes.AsSpan(framing.ObjectStart, framing.ObjectBytes), slice.TrailingInt32);
        var prototypes = ObjectWire.ReadReferencedPrototypesFromDirectory(prototypeRoot, instances.Select(v => v.PrototypeId).ToArray());
        var objects = ObjectWire.ResolveEffectiveInstances(instances, prototypes.Prototypes).Select(value =>
        {
            var proto = prototypes.Prototypes.Single(p => p.Id.Matches(value.Instance.PrototypeId));
            if (value.Instance.Location is not { } loc) throw new InvalidDataException("CAVE_STATIC_LOCATION_ABSENT");
            var (wx, wy) = RebaseSectorLocation(loc, x, y);
            return new CaveObject(value.SourceIndex, value.Instance.Type, value.Instance.PrototypeId.AidValue(), wx, wy, value.CurrentAid, value.EffectiveFlags,
                EffectiveScripts(bytes.AsSpan(framing.ObjectStart).ToArray(), value.Instance, proto));
        }).ToArray();
        return (objects, bytes, framing);
    }

    private static void CaveTransitionSelfTest()
    {
        static void Rejects(Action action, string code)
        {
            try { action(); }
            catch (InvalidDataException error) when (error.Message == code) { return; }
            throw new InvalidOperationException("cave self-test failed: " + code);
        }
        // Literal wire offsets, independent of the decoder's constants.
        static byte[] Script(int map, int x, int y, int tail, int lines = 2)
        {
            var bytes = new byte[64 + lines * 132];
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(52), lines);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(56), 10);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(108), 44);
            bytes[112] = 0; bytes[113] = 3; bytes[114] = 3; bytes[115] = 3; bytes[116] = 0xE5; // kind slot 4 is editor noise
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(120), 12345); // focus value is ignored for the triggerer
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(124), map);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(128), x);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(132), y);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(136), -77); // unused operand value
            if (lines > 1) BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(64 + 132 + 44), tail);
            return bytes;
        }
        var shape = CaveTeleportShape(DecodeCaveScript(Script(5078, 2087, 2018, 2)));
        if (shape != (5078, 2087, 2018, 2)) throw new InvalidOperationException("cave self-test failed: literal teleport");
        Rejects(() => DecodeCaveScript(Script(1, 2, 3, 2).AsSpan(0, 64 + 131)), "CAVE_SCRIPT_FRAMING");
        Rejects(() => DecodeCaveScript(Script(1, 2, 3, 9)), "CAVE_SCRIPT_ACTION_NOT_ADMITTED");
        Rejects(() => CaveTeleportShape(DecodeCaveScript(Script(1, 2, 3, 2, lines: 1))), "CAVE_SCRIPT_SHAPE");
        var computed = Script(5078, 2087, 2018, 1); computed[114] = 4; // x read from a global flag slot
        Rejects(() => CaveTeleportShape(DecodeCaveScript(computed)), "CAVE_SCRIPT_COMPUTED_OPERAND");
        var focus = Script(5078, 2087, 2018, 1); focus[112] = 1; // attachee instead of triggerer
        Rejects(() => CaveTeleportShape(DecodeCaveScript(focus)), "CAVE_SCRIPT_COMPUTED_OPERAND");
        var condition = Script(5078, 2087, 2018, 1); BinaryPrimitives.WriteInt32LittleEndian(condition.AsSpan(64), 43);
        Rejects(() => DecodeCaveScript(condition), "CAVE_SCRIPT_CONDITION_NOT_ADMITTED");
        // Wall art: class1, number 11, piece 0, rotation 5 (edge 5), variation 0.
        if (DecodeWallArt(0x10b02800) != new CaveWallArt(1, 11, 0, 5, 5, 0) || DecodeWallArt(0x10b01a11).EdgeRotation != 3 || DecodeWallArt(0x10b01a11).Rotation != 3 ||
            DecodeWallArt(0x1000_0000 | (2u << 11)).EdgeRotation != 3 || DecodeWallArt(0x1000_0000 | (13u << 14)).Piece != 13)
            throw new InvalidOperationException("cave self-test failed: wall art fields");
        // Aggregate selector: pinned wrap and reversal, on synthetic names only.
        if (CaveAggregateSelector("Abz-9") != "9-ooN" || CaveAggregateSelector("nN") != "Cc" || CaveAggregateSelector("m") != "z")
            throw new InvalidOperationException("cave self-test failed: aggregate selector");
        if (CaveStep((10, 10), 1) != (9, 10) || CaveStep((10, 10), 3) != (10, 11) || CaveStep((10, 10), 5) != (11, 10) || CaveStep((10, 10), 7) != (10, 9) || CaveStep((10, 10), 0) is not null)
            throw new InvalidOperationException("cave self-test failed: cardinal steps");
        Console.WriteLine("cave-transition-self-test: PASS (literal teleport shape, computed/focus/condition refusals, framing, wall art fields, aggregate selector, cardinal steps)");
    }
}
