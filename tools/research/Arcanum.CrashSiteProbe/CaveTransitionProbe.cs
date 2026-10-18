using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arcanum.Data;

namespace Arcanum.CrashSiteProbe;

// ARC-WORLD-009 research only: how the Crash Site cave transition fires and where it
// leads. Independently authored from the source locators cited in
// docs/research/cave-transition-evidence.md; no code or structure is reused.
// No script VM, no movement, no map lifecycle: it decodes the two literal script
// files, the two attached trigger objects, every other script that the trigger
// sector or the cave attaches, both trigger and arrival cells, and the cave walls.
// Only numbers, booleans and SHA-256 digests leave the probe.
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
    private const int MaxScannedScripts = 64;
    private const int BedSceneryType = 22;
    private const int TeleportFacadeSceneryType = 20;
    // Pinned whole-archive identities, in this exact order. Script lookups read only
    // DAT indexes plus the selected script entries.
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
    // Teleport and fade-and-teleport. Script-number edges: call-script, call-in and
    // call-at (operand 0), change-this-script (operand 0) and change-script (operand 2).
    // Call-ex (81) runs a focus object's own attachment and is always unresolved.
    private static readonly int[] TeleportActions = [44, 102];
    private static readonly IReadOnlyDictionary<int, int> ScriptEdgeOperand = new Dictionary<int, int> { [7] = 0, [54] = 0, [55] = 0, [6] = 0, [99] = 2 };
    private const int CallExAction = 81;

    internal sealed record CaveOperand(int Kind, int Value);
    internal sealed record CaveAction(int Type, IReadOnlyList<CaveOperand> Operands);
    internal sealed record CaveScriptLine(int Line, int Condition, CaveAction True, CaveAction False);
    internal sealed record CaveScript(uint FileFlags, int Capacity, IReadOnlyList<CaveScriptLine> Lines);
    internal readonly record struct CaveWallArt(int ArtClass, int Number, int Piece, int Rotation, int EdgeRotation, int Variation);
    internal readonly record struct CaveWall(int X, int Y, uint Aid);
    internal readonly record struct CaveEdge(int Rotation, int Leaving, int Entering, int Opening);

    // Operand counts are fixed per admitted action type. Unused slots hold editor
    // memory and are never interpreted. Anything else is refused, not guessed.
    private static readonly IReadOnlyDictionary<int, int> AdmittedActions = new Dictionary<int, int>
    {
        [0] = 0,  // do nothing
        [1] = 0,  // return and skip default
        [2] = 0,  // return and run default
        [44] = 4, // teleport: focus object, map, x, y
    };

    private static (uint Flags, int Count, int Capacity) CaveScriptFraming(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < ScriptHeaderBytes || bytes.Length > MaxScriptBytes) throw new InvalidDataException("CAVE_SCRIPT_BOUND");
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(bytes[48..]);
        var count = BinaryPrimitives.ReadInt32LittleEndian(bytes[52..]);
        var capacity = BinaryPrimitives.ReadInt32LittleEndian(bytes[56..]);
        if (count < 0 || capacity < count || bytes.Length != ScriptHeaderBytes + (long)count * ScriptLineBytes) throw new InvalidDataException("CAVE_SCRIPT_FRAMING");
        return (flags, count, capacity);
    }

    internal static CaveScript DecodeCaveScript(ReadOnlySpan<byte> bytes)
    {
        var (flags, count, capacity) = CaveScriptFraming(bytes);
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

    /// <summary>Checks the one admitted shape: line0 literal teleport of the triggerer, line1 a return.
    /// File flags must be zero, which also rules out both trap flags.</summary>
    internal static (int Map, int X, int Y, int Return) CaveTeleportShape(CaveScript script)
    {
        if (script.FileFlags != 0 || script.Lines.Count != 2) throw new InvalidDataException("CAVE_SCRIPT_SHAPE");
        var teleport = script.Lines[0].True; var tail = script.Lines[1].True;
        if (teleport.Type != 44 || script.Lines[0].False.Type != 0 || script.Lines[1].False.Type != 0 || tail.Type is not (1 or 2))
            throw new InvalidDataException("CAVE_SCRIPT_SHAPE");
        // Focus kind 0 is the triggerer; value kind 3 is a literal number.
        if (teleport.Operands[0].Kind != 0 || teleport.Operands.Skip(1).Any(o => o.Kind != 3)) throw new InvalidDataException("CAVE_SCRIPT_COMPUTED_OPERAND");
        return (teleport.Operands[1].Value, teleport.Operands[2].Value, teleport.Operands[3].Value, tail.Type);
    }

    internal readonly record struct CaveScriptEdge(int Action, int Target);

    /// <summary>Census of any other script: action types per branch only. A script-number
    /// edge with a literal operand (kind 3) is reported with its action; a computed one,
    /// and every call-ex, is counted as unresolved.</summary>
    internal static (uint Flags, int Lines, int Teleports, IReadOnlyList<CaveScriptEdge> Edges, int Unresolved) CaveScriptCensus(ReadOnlySpan<byte> bytes)
    {
        var (flags, count, _) = CaveScriptFraming(bytes);
        var teleports = 0; var unresolved = 0; var edges = new List<CaveScriptEdge>();
        for (var line = 0; line < count; line++)
            foreach (var offset in new[] { 44, 88 })
            {
                var slot = bytes.Slice(ScriptHeaderBytes + line * ScriptLineBytes + offset, 44);
                var type = BinaryPrimitives.ReadInt32LittleEndian(slot);
                if (TeleportActions.Contains(type)) teleports++;
                if (type == CallExAction) { unresolved++; continue; }
                if (!ScriptEdgeOperand.TryGetValue(type, out var operand)) continue;
                if (slot[4 + operand] == 3) edges.Add(new(type, BinaryPrimitives.ReadInt32LittleEndian(slot[(12 + 4 * operand)..]))); else unresolved++;
            }
        return (flags, count, teleports, edges, unresolved);
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

    /// <summary>One cardinal edge: walls on the cell facing the edge, and walls on the
    /// neighbour facing back across it. The neighbour must lie inside the given sector.</summary>
    internal static CaveEdge CaveEdgeWalls(IReadOnlyList<CaveWall> walls, (int X, int Y) cell, int rotation, (int X0, int Y0) sector)
    {
        var next = CaveStep(cell, rotation) ?? throw new InvalidDataException("CAVE_EDGE_NOT_CARDINAL");
        if (next.X < sector.X0 || next.X >= sector.X0 + 64 || next.Y < sector.Y0 || next.Y >= sector.Y0 + 64) throw new InvalidDataException("CAVE_NEIGHBOUR_OUTSIDE_SECTOR");
        var leave = walls.Where(w => w.X == cell.X && w.Y == cell.Y && DecodeWallArt(w.Aid).EdgeRotation == rotation).ToArray();
        var enter = walls.Where(w => w.X == next.X && w.Y == next.Y && DecodeWallArt(w.Aid).EdgeRotation == (rotation + 4) % 8).ToArray();
        return new(rotation, leave.Length, enter.Length, leave.Concat(enter).Count(w => OpeningWallPieces.Contains(DecodeWallArt(w.Aid).Piece)));
    }

    /// <summary>F1 of area-passability-closure.md: mask bit, else facade bit 0, else tile-name flags.</summary>
    internal static bool? CaveTerrainBlocks(bool mask, uint raw, string[][]? categories)
    {
        if (mask) return true;
        if (raw >> 28 == 11) return (raw & 1) == 0;
        if (raw >> 28 != 0 || categories is null) return null;
        return ClosureTileBlocks(raw, categories);
    }

    private static int ProbeCaveTransition(string[] args)
    {
        const int fixedArgs = 8;
        if (args.Length != fixedArgs + CaveArchives.Length * 2 || args[0] != "--game-root" || args[2] != "--prototype-root" || args[4] != "--mobile" || args[6] != "--cave-mobile")
            throw new ArgumentException("CAVE_USAGE");
        var archivePaths = new string[CaveArchives.Length];
        for (var i = 0; i < CaveArchives.Length; i++)
        {
            if (args[fixedArgs + i * 2] != "--" + CaveArchives[i].Role) throw new ArgumentException("CAVE_USAGE");
            archivePaths[i] = args[fixedArgs + 1 + i * 2];
        }
        var gameRoot = args[1]; var prototypeRoot = args[3]; var mobilePath = args[5]; var cavePath = args[7];
        var before = archivePaths.Select(InputFingerprint.Create).ToArray();
        for (var i = 0; i < CaveArchives.Length; i++)
            if (before[i] != new InputFingerprint(CaveArchives[i].Bytes, CaveArchives[i].Sha256)) throw new InvalidDataException("CAVE_ARCHIVE_IDENTITY");
        var mobileBefore = InputFingerprint.Create(mobilePath); var caveBefore = InputFingerprint.Create(cavePath);
        if (mobileBefore != new InputFingerprint(AreaMobileBytes, AreaMobileSha) || caveBefore != new InputFingerprint(CaveMobileBytes, CaveMobileSha))
            throw new InvalidDataException("CAVE_AGGREGATE_IDENTITY");

        // Index caps follow the largest pinned archive; entry caps stay at the bounded 1 MiB.
        var options = new DatReaderOptions { MaxArchiveBytes = checked((uint)CaveArchives.Max(a => a.Bytes)), MaxStoredEntryBytes = MaxDecodedSectorBytes, MaxDecodedEntryBytes = MaxDecodedSectorBytes };
        var archives = archivePaths.Select(p => DatArchive.Open(p, options)).ToArray();
        var prototypeInputs = new Dictionary<string, InputFingerprint>(StringComparer.Ordinal);
        try
        {
            var module = archives[0];
            // Map catalogue: the runtime number is the row's position in an unbroken run
            // from 5000, so every key up to the cave must be present.
            var catalogue = ObjectArt.ParseMes(module.ReadEntry(AreaUnique(module, "rules/maplist.mes")));
            var run = 0; while (catalogue.ContainsKey(ExteriorCatalogue + run)) run++;
            if (run <= CaveCatalogue - ExteriorCatalogue) throw new InvalidDataException("CAVE_CATALOGUE_SEQUENCE");
            string Name(int key)
            {
                var name = catalogue[key].Split(',')[0].Trim();
                if (name.Length == 0 || name.Any(c => c > 127 || c is '/' or '\\' or '\0') || name.Contains("..", StringComparison.Ordinal)) throw new InvalidDataException("CAVE_UNSAFE_SELECTOR");
                return name;
            }
            object Row(int key, string aggregatePath)
            {
                var fields = catalogue[key].Split(',').Select(f => f.Trim()).ToArray();
                if (fields.Length < 3) throw new InvalidDataException("CAVE_CATALOGUE_ROW");
                var tags = fields.Skip(3).Select(f => f.Split(':', 2)).ToDictionary(p => p[0].Trim(), p => p.Length == 2 ? p[1].Trim() : "", StringComparer.OrdinalIgnoreCase);
                int? Number(string tag) => tags.TryGetValue(tag, out var v) ? int.Parse(v, System.Globalization.CultureInfo.InvariantCulture) : null;
                return new
                {
                    catalogue = key, runtime = key - ExteriorCatalogue + 1,
                    start_x = int.Parse(fields[1], System.Globalization.CultureInfo.InvariantCulture), start_y = int.Parse(fields[2], System.Globalization.CultureInfo.InvariantCulture),
                    start_map = tags.TryGetValue("Type", out var type) && type.Equals("START_MAP", StringComparison.OrdinalIgnoreCase),
                    world_map = Number("WorldMap"), area = Number("Area"),
                    selector_matches_aggregate = string.Equals(CaveAggregateSelector(Name(key)), Path.GetFileName(aggregatePath), StringComparison.OrdinalIgnoreCase),
                };
            }
            var cavePrefix = "maps/" + Name(CaveCatalogue).ToLowerInvariant() + "/";
            var exteriorPrefix = "maps/" + Name(ExteriorCatalogue).ToLowerInvariant() + "/";
            var rows = new[] { Row(ExteriorCatalogue, mobilePath), Row(CaveCatalogue, cavePath) };

            // Normal-game cave counterpart: exact EOF framing and a semantic bijection
            // with the module's loose cave mobiles (one folder level, as the game lists them).
            var caveBytes = File.ReadAllBytes(cavePath);
            var aggregate = FrameAggregate(caveBytes, CaveMobileBytes);
            bool LooseCaveMobile(DatEntry e)
            {
                var name = AreaName(e).ToLowerInvariant();
                return name.StartsWith(cavePrefix, StringComparison.Ordinal) && name.EndsWith(".mob", StringComparison.Ordinal) && !name[cavePrefix.Length..].Contains('/');
            }
            var loose = module.Entries.Where(LooseCaveMobile).OrderBy(e => e.Ordinal).ToArray();
            var expected = new Dictionary<string, int>(); var looseHashes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in loose)
            {
                var bytes = module.ReadEntry(entry); var instance = ObjectWire.ParseNext(bytes, out var consumed);
                if (consumed != bytes.Length) throw new InvalidDataException("CAVE_LOOSE_TRAILING_BYTES");
                var key = SemanticHash(bytes, instance); expected[key] = expected.GetValueOrDefault(key) + 1; looseHashes.Add(AreaHash(bytes));
            }
            var actual = aggregate.GroupBy(r => SemanticHash(caveBytes.AsSpan(r.Offset, r.Length).ToArray(), r.Instance)).ToDictionary(g => g.Key, g => g.Count());
            var bijection = expected.Keys.Union(actual.Keys).Count(k => expected.GetValueOrDefault(k) != actual.GetValueOrDefault(k));
            if (bijection != 0) throw new InvalidDataException("CAVE_AGGREGATE_BIJECTION");
            var exact = aggregate.Count(r => looseHashes.Contains(r.Sha256));
            var caveMobiles = Resolve(caveBytes, aggregate);
            var exteriorBytes = File.ReadAllBytes(mobilePath);
            var exteriorAggregate = FrameAggregate(exteriorBytes, AreaMobileBytes);

            // The two sectors that carry the triggers and both arrival cells.
            var exterior = CaveSectorObjects(module, prototypeRoot, exteriorPrefix, ExteriorTriggerSector, prototypeInputs);
            var cave = CaveSectorObjects(module, prototypeRoot, cavePrefix, CaveSector, prototypeInputs);

            (int Archive, DatEntry Entry, byte[] Bytes)? Script(int number)
            {
                var variants = archives.SelectMany((a, i) => a.Entries.Where(e => ScriptNumber(e) == number).Select(e => (Archive: i, Entry: e))).ToArray();
                if (variants.Length == 0) return null;
                if (variants.Length != 1) throw new InvalidDataException("CAVE_SCRIPT_AMBIGUOUS");
                if (variants[0].Entry.DeclaredOutputSize > MaxScriptBytes) throw new InvalidDataException("CAVE_SCRIPT_BOUND");
                return (variants[0].Archive, variants[0].Entry, archives[variants[0].Archive].ReadEntry(variants[0].Entry));
            }

            // The two transition scripts: exactly one variant each, one admitted shape.
            var scripts = new List<object>(); var shapes = new Dictionary<int, (int Map, int X, int Y, int Return)>();
            foreach (var number in new[] { 2793, 1532 })
            {
                var (index, entry, bytes) = Script(number) ?? throw new InvalidDataException("CAVE_SCRIPT_MISSING");
                var decoded = DecodeCaveScript(bytes); shapes[number] = CaveTeleportShape(decoded);
                scripts.Add(new { script = number, archive = CaveArchives[index].Role, entry.Ordinal, stored_bytes = entry.StoredSize, decoded_bytes = bytes.Length, sha256 = AreaHash(bytes), file_flags = decoded.FileFlags, decoded.Capacity, lines = decoded.Lines });
            }
            if (shapes[2793].Map != CaveCatalogue || shapes[1532].Map != ExteriorCatalogue) throw new InvalidDataException("CAVE_DESTINATION_CHANGED");

            // Every other script attached in the trigger sector (objects and tiles) or
            // anywhere in the cave (objects, tiles, aggregate), plus one hop of literal calls.
            var attached = new SortedSet<int>();
            foreach (var sector in new[] { exterior, cave })
            {
                foreach (var o in sector.Objects) foreach (var s in o.Scripts) attached.Add(s.script);
                foreach (var script in sector.TileScripts) attached.Add(script);
            }
            foreach (var m in caveMobiles) foreach (var s in m.Scripts) attached.Add(s.script);
            attached.Remove(0); attached.Remove(2793); attached.Remove(1532);
            var census = new List<object>(); var queue = new Queue<(int Script, int Hop)>(attached.Select(s => (s, 0))); var seen = new HashSet<int>(attached);
            while (queue.TryDequeue(out var item))
            {
                if (census.Count >= MaxScannedScripts) throw new InvalidDataException("CAVE_SCRIPT_SCAN_BOUND");
                var found = Script(item.Script);
                if (found is null) { census.Add(new { script = item.Script, hop = item.Hop, present = false }); continue; }
                var c = CaveScriptCensus(found.Value.Bytes);
                if (c.Teleports != 0) throw new InvalidDataException("CAVE_UNEXPECTED_TELEPORT");
                census.Add(new { script = item.Script, hop = item.Hop, present = true, sha256 = AreaHash(found.Value.Bytes), file_flags = c.Flags, lines = c.Lines, teleport_actions = c.Teleports, script_edges = c.Edges, unresolved_edges = c.Unresolved });
                if (item.Hop == 0) foreach (var target in c.Edges.Select(e => e.Target).Where(t => t > 0 && seen.Add(t) && t is not 2793 and not 1532)) queue.Enqueue((target, 1));
            }

            // Terrain at trigger and arrival cells, by the written F1 rule.
            var tileNames = ObjectArt.ParseMes(archives[2].ReadEntry(AreaUnique(archives[2], "art/tile/tilename.mes")));
            var categories = ClosureTileNameCategories(tileNames);
            object Cell(CaveSectorData sector, IReadOnlyList<(int X, int Y)> aggregateCells, long packed, int x, int y)
            {
                var sx = (int)(packed & 67108863); var sy = (int)(packed >> 26);
                if (x < sx * 64 || x >= sx * 64 + 64 || y < sy * 64 || y >= sy * 64 + 64) throw new InvalidDataException("CAVE_CELL_OUTSIDE_SECTOR");
                var ordinal = (x - sx * 64) + 64 * (y - sy * 64);
                var maskBytes = sector.Bytes.AsSpan(sector.Framing.BlockMaskStart, sector.Framing.BlockMaskBytes);
                if (maskBytes.Length != 512) throw new InvalidDataException("CAVE_BLOCK_MASK");
                var mask = PassabilityProbe.IsBlocked(maskBytes, ordinal);
                var tile = BinaryPrimitives.ReadUInt32LittleEndian(sector.Bytes.AsSpan(sector.Framing.TileStart + 4 * ordinal));
                var here = sector.Objects.Where(o => o.X == x && o.Y == y).ToArray();
                var walls = sector.Objects.Where(o => o.Type == 0).Select(o => new CaveWall(o.X, o.Y, (uint)o.Aid)).ToArray();
                return new
                {
                    x, y, sector = packed, ordinal, block_mask_set = mask, tile_art_class = tile >> 28, terrain_blocks = CaveTerrainBlocks(mask, tile, categories),
                    sector_objects = here.Select(o => new { o.SourceIndex, o.Type, o.Prototype, o.Flags, wall = o.Type == 0 ? DecodeWallArt((uint)o.Aid) : (CaveWallArt?)null }).ToArray(),
                    aggregate_objects = aggregateCells.Count(c => c.X == x && c.Y == y),
                    cardinal_edges = new[] { 1, 3, 5, 7 }.Select(r => CaveEdgeWalls(walls, (x, y), r, (sx * 64, sy * 64))).ToArray(),
                };
            }
            var caveCells = caveMobiles.Where(m => m.Cell.HasValue).Select(m => m.Cell!.Value).ToArray();
            var exteriorCells = exteriorAggregate.Where(r => r.Instance.Location.HasValue).Select(r => (unchecked((int)r.Instance.Location!.Value), unchecked((int)(r.Instance.Location.Value >> 32)))).ToArray();

            object Trigger(CaveSectorData sector, IReadOnlyList<(int X, int Y)> cells, long packed, int script, out (int X, int Y) at)
            {
                var hits = sector.Objects.Where(o => o.Scripts.Any(s => s.script == script)).ToArray();
                if (hits.Length != 1 || hits[0].Scripts.Count != 1 || hits[0].Scripts[0].sap != 1) throw new InvalidDataException("CAVE_TRIGGER_SHAPE");
                var o = hits[0]; at = (o.X, o.Y);
                var sceneryType = (int)(((uint)o.Aid >> 6) & 31);
                if (o.Type != 3 || (uint)o.Aid >> 28 != 4 || sceneryType == BedSceneryType) throw new InvalidDataException("CAVE_TRIGGER_SHAPE");
                return new
                {
                    o.SourceIndex, o.Type, o.Prototype, o.X, o.Y, art_class = (uint)o.Aid >> 28, scenery_type = sceneryType, teleport_facade = sceneryType == TeleportFacadeSceneryType, o.Flags,
                    dont_draw = (o.Flags & 0x100) != 0, invisible = (o.Flags & 0x200) != 0, click_through = (o.Flags & 0x800) != 0, off = (o.Flags & 0x2) != 0, no_block = (o.Flags & 0x400) != 0,
                    o.Scripts, cell = Cell(sector, cells, packed, o.X, o.Y),
                };
            }
            var triggers = new[] { Trigger(exterior, exteriorCells, ExteriorTriggerSector, 2793, out var exteriorTrigger), Trigger(cave, caveCells, CaveSector, 1532, out var caveTrigger) };
            var arrivals = new[]
            {
                Cell(cave, caveCells, CaveSector, shapes[2793].X, shapes[2793].Y),
                Cell(exterior, exteriorCells, ExteriorTriggerSector, shapes[1532].X, shapes[1532].Y),
            };

            var wallObjects = cave.Objects.Where(o => o.Type == 0).ToArray();
            var wallArt = wallObjects.Select(o => DecodeWallArt((uint)o.Aid)).ToArray();
            var wallCensus = new
            {
                walls = wallObjects.Length, prototypes = wallObjects.Select(o => o.Prototype).Distinct().Order().ToArray(),
                art_classes = wallArt.GroupBy(a => a.ArtClass).ToDictionary(g => g.Key, g => g.Count()),
                numbers = wallArt.GroupBy(a => a.Number).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
                pieces = wallArt.GroupBy(a => a.Piece).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
                edge_rotations = wallArt.GroupBy(a => a.EdgeRotation).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
                variations = wallArt.GroupBy(a => a.Variation).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
                opening_pieces = wallArt.Count(a => OpeningWallPieces.Contains(a.Piece)),
                flags = wallObjects.GroupBy(o => o.Flags).ToDictionary(g => g.Key, g => g.Count()),
                shared_tiles = wallObjects.GroupBy(o => (o.X, o.Y)).Count(g => g.Count() > 1),
                scenery = cave.Objects.Count(o => o.Type == 3), other = cave.Objects.Count(o => o.Type is not 0 and not 3),
                aggregate_types = caveMobiles.GroupBy(m => m.Type).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
            };

            // Loose files the game would also search: the data folder and the module folder.
            var looseOverrides = new[] { "data", "modules/Arcanum" }.Select(root => new
            {
                root = root == "data" ? "data" : "module-folder",
                scripts = LooseFiles(gameRoot, root, "scr").Count(f => ScriptNumberFromFile(f) is { } n && (n is 2793 or 1532 || seen.Contains(n))),
                map_catalogue = LooseFiles(gameRoot, root, "rules").Count(f => f.Equals("maplist.mes", StringComparison.OrdinalIgnoreCase)),
                tile_names = LooseFiles(gameRoot, root + "/art", "tile").Count(f => f.Equals("tilename.mes", StringComparison.OrdinalIgnoreCase)),
            }).ToArray();
            if (looseOverrides.Any(o => o.scripts != 0 || o.map_catalogue != 0 || o.tile_names != 0)) throw new InvalidDataException("CAVE_LOOSE_OVERRIDE_PRESENT");

            var after = archivePaths.Select(InputFingerprint.Create).ToArray();
            if (!after.SequenceEqual(before) || mobileBefore != InputFingerprint.Create(mobilePath) || caveBefore != InputFingerprint.Create(cavePath) ||
                prototypeInputs.Any(p => InputFingerprint.Create(p.Key) != p.Value))
                throw new InvalidDataException("CAVE_INPUT_CHANGED");
            var report = new
            {
                schema_version = 2,
                archives = CaveArchives.Select((a, i) => new { role = a.Role, before[i].Bytes, before[i].Sha256 }).ToArray(),
                mobile = mobileBefore, cave_mobile = caveBefore,
                prototypes = new { count = prototypeInputs.Count, aggregate_sha256 = PrototypeDigest(prototypeInputs.Values), reverified = true },
                catalogue = new { contiguous_rows_from_5000 = run, rows },
                cave_aggregate = new { records = aggregate.Count, loose = loose.Length, semantic_mismatch = bijection, byte_exact_records = exact },
                scripts, other_scripts = census, triggers, arrivals, wall_census = wallCensus, loose_overrides = looseOverrides,
                distances = new
                {
                    exterior_trigger_to_return = Math.Max(Math.Abs(exteriorTrigger.X - shapes[1532].X), Math.Abs(exteriorTrigger.Y - shapes[1532].Y)),
                    cave_trigger_to_arrival = Math.Max(Math.Abs(caveTrigger.X - shapes[2793].X), Math.Abs(caveTrigger.Y - shapes[2793].Y)),
                },
            };
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            return 0;

            IReadOnlyList<CaveMobile> Resolve(byte[] bytes, IReadOnlyList<AreaRecord> records)
            {
                var prototypes = ObjectWire.ReadReferencedPrototypesFromDirectory(prototypeRoot, records.Select(r => r.Instance.PrototypeId).ToArray());
                Remember(prototypes, prototypeInputs);
                return records.Select(r =>
                {
                    var proto = prototypes.Prototypes.Single(p => p.Id.Matches(r.Instance.PrototypeId));
                    (int, int)? cell = r.Instance.Location is { } loc ? (unchecked((int)loc), unchecked((int)(loc >> 32))) : null;
                    return new CaveMobile(r.Instance.Type, cell, EffectiveScripts(bytes.AsSpan(r.Offset, r.Length).ToArray(), r.Instance, proto));
                }).ToArray();
            }
        }
        finally
        {
            foreach (var archive in archives) archive.Dispose();
        }
    }

    private sealed record CaveObject(int SourceIndex, int Type, int Prototype, int X, int Y, int Aid, uint Flags, IReadOnlyList<ScriptAttachment> Scripts);
    private sealed record CaveMobile(int Type, (int X, int Y)? Cell, IReadOnlyList<ScriptAttachment> Scripts);
    private sealed record CaveSectorData(IReadOnlyList<CaveObject> Objects, IReadOnlyList<int> TileScripts, byte[] Bytes, SectorFraming Framing);

    private static void Remember(PrototypeReadResult read, Dictionary<string, InputFingerprint> inputs)
    {
        foreach (var input in read.Inputs)
            if (inputs.TryGetValue(input.Path, out var known) ? known != input.Fingerprint.Fingerprint : !inputs.TryAdd(input.Path, input.Fingerprint.Fingerprint))
                throw new InvalidDataException("CAVE_PROTOTYPE_CHANGED");
    }

    private static string PrototypeDigest(IEnumerable<InputFingerprint> fingerprints) =>
        AreaHash(Encoding.ASCII.GetBytes(string.Join('\n', fingerprints.Select(f => $"{f.Bytes}:{f.Sha256}").Order(StringComparer.Ordinal))));

    private static int? ScriptNumber(DatEntry entry)
    {
        var name = AreaName(entry);
        if (!name.StartsWith("scr/", StringComparison.OrdinalIgnoreCase) || name[4..].Contains('/')) return null;
        return ScriptNumberFromFile(name[4..]);
    }

    private static int? ScriptNumberFromFile(string file)
    {
        if (!file.EndsWith(".scr", StringComparison.OrdinalIgnoreCase)) return null;
        var digits = new string(file.TakeWhile(char.IsAsciiDigit).ToArray());
        return digits.Length is > 0 and <= 6 ? int.Parse(digits, System.Globalization.CultureInfo.InvariantCulture) : null;
    }

    // One folder level, case-insensitive names, no recursion and no symlink following.
    private static IEnumerable<string> LooseFiles(string gameRoot, string relative, string folder)
    {
        IEnumerable<DirectoryInfo> level = Directory.Exists(gameRoot) ? [new DirectoryInfo(gameRoot)] : [];
        foreach (var part in relative.Split('/').Append(folder))
            level = level.SelectMany(d => d.EnumerateDirectories().Where(c => c.Name.Equals(part, StringComparison.OrdinalIgnoreCase) && c.LinkTarget is null)).ToArray();
        return level.SelectMany(d => d.EnumerateFiles().Where(f => f.LinkTarget is null).Select(f => f.Name)).ToArray();
    }

    private static CaveSectorData CaveSectorObjects(DatArchive module, string prototypeRoot, string prefix, long packed, Dictionary<string, InputFingerprint> prototypeInputs)
    {
        var entry = AreaUnique(module, prefix + packed.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".sec");
        var x = checked((int)(packed & 67108863)); var y = checked((int)(packed >> 26));
        var bytes = module.ReadEntry(entry); var slice = SectorSlice.Parse(bytes, false); var framing = SectorFraming.Parse(bytes, slice.LightCount);
        var instances = ObjectWire.ParseWindow(bytes.AsSpan(framing.ObjectStart, framing.ObjectBytes), slice.TrailingInt32);
        var prototypes = ObjectWire.ReadReferencedPrototypesFromDirectory(prototypeRoot, instances.Select(v => v.PrototypeId).ToArray());
        Remember(prototypes, prototypeInputs);
        var objects = ObjectWire.ResolveEffectiveInstances(instances, prototypes.Prototypes).Select(value =>
        {
            var proto = prototypes.Prototypes.Single(p => p.Id.Matches(value.Instance.PrototypeId));
            if (value.Instance.Location is not { } loc) throw new InvalidDataException("CAVE_STATIC_LOCATION_ABSENT");
            var (wx, wy) = RebaseSectorLocation(loc, x, y);
            return new CaveObject(value.SourceIndex, value.Instance.Type, value.Instance.PrototypeId.AidValue(), wx, wy, value.CurrentAid, value.EffectiveFlags,
                EffectiveScripts(bytes.AsSpan(framing.ObjectStart).ToArray(), value.Instance, proto));
        }).ToArray();
        // Tile attachments: count word, then 24-byte nodes whose fifth word is the script.
        var tiles = Enumerable.Range(0, framing.TileScriptCount)
            .Select(i => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(framing.TileScriptsStart + 4 + i * 24 + 16))).ToArray();
        return new(objects, tiles, bytes, framing);
    }

    private static void CaveTransitionSelfTest()
    {
        static void Rejects(Action action, string code)
        {
            try { action(); }
            catch (InvalidDataException error) when (error.Message == code) { return; }
            throw new InvalidOperationException("cave self-test failed: " + code);
        }
        static void Check(bool condition, string what)
        {
            if (!condition) throw new InvalidOperationException("cave self-test failed: " + what);
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
        Check(CaveTeleportShape(DecodeCaveScript(Script(5078, 2087, 2018, 2))) == (5078, 2087, 2018, 2), "literal teleport");
        Rejects(() => DecodeCaveScript(Script(1, 2, 3, 2).AsSpan(0, 64 + 131)), "CAVE_SCRIPT_FRAMING");
        var capacity = Script(1, 2, 3, 2); BinaryPrimitives.WriteInt32LittleEndian(capacity.AsSpan(56), 1);
        Rejects(() => DecodeCaveScript(capacity), "CAVE_SCRIPT_FRAMING");
        Rejects(() => DecodeCaveScript(Script(1, 2, 3, 9)), "CAVE_SCRIPT_ACTION_NOT_ADMITTED");
        Rejects(() => CaveTeleportShape(DecodeCaveScript(Script(1, 2, 3, 2, lines: 1))), "CAVE_SCRIPT_SHAPE");
        Rejects(() => CaveTeleportShape(DecodeCaveScript(Script(1, 2, 3, 0))), "CAVE_SCRIPT_SHAPE"); // do-nothing tail
        var trap = Script(1, 2, 3, 1); trap[48] = 1; // nonmagical trap file flag at offset 48
        Rejects(() => CaveTeleportShape(DecodeCaveScript(trap)), "CAVE_SCRIPT_SHAPE");
        var otherwise = Script(1, 2, 3, 1); BinaryPrimitives.WriteInt32LittleEndian(otherwise.AsSpan(64 + 88), 2); // false branch acts
        Rejects(() => CaveTeleportShape(DecodeCaveScript(otherwise)), "CAVE_SCRIPT_SHAPE");
        var computed = Script(5078, 2087, 2018, 1); computed[114] = 4; // x read from a global flag slot
        Rejects(() => CaveTeleportShape(DecodeCaveScript(computed)), "CAVE_SCRIPT_COMPUTED_OPERAND");
        var focus = Script(5078, 2087, 2018, 1); focus[112] = 1; // attachee instead of triggerer
        Rejects(() => CaveTeleportShape(DecodeCaveScript(focus)), "CAVE_SCRIPT_COMPUTED_OPERAND");
        var condition = Script(5078, 2087, 2018, 1); BinaryPrimitives.WriteInt32LittleEndian(condition.AsSpan(64), 43);
        Rejects(() => DecodeCaveScript(condition), "CAVE_SCRIPT_CONDITION_NOT_ADMITTED");
        // Census: a teleport on a false branch, a literal and a computed call.
        var other = new byte[64 + 2 * 132];
        BinaryPrimitives.WriteInt32LittleEndian(other.AsSpan(52), 2); BinaryPrimitives.WriteInt32LittleEndian(other.AsSpan(56), 2);
        BinaryPrimitives.WriteInt32LittleEndian(other.AsSpan(64 + 88), 102);
        BinaryPrimitives.WriteInt32LittleEndian(other.AsSpan(64 + 132 + 44), 7); other[64 + 132 + 48] = 3; BinaryPrimitives.WriteInt32LittleEndian(other.AsSpan(64 + 132 + 56), 1893);
        BinaryPrimitives.WriteInt32LittleEndian(other.AsSpan(64 + 132 + 88), 81); other[64 + 132 + 92] = 3; // call-ex: literal focus is still unresolved
        BinaryPrimitives.WriteInt32LittleEndian(other.AsSpan(64 + 44), 99); other[64 + 44 + 6] = 3; BinaryPrimitives.WriteInt32LittleEndian(other.AsSpan(64 + 44 + 20), 2884); // change-script target in operand 2
        var census = CaveScriptCensus(other);
        Check(census.Teleports == 1 && census.Edges.SequenceEqual([new CaveScriptEdge(99, 2884), new CaveScriptEdge(7, 1893)]) && census.Unresolved == 1 && census.Lines == 2, "script census");
        // Wall art: class1, number 11, piece 0, rotation 5 (edge 5), variation 0.
        Check(DecodeWallArt(0x10b02800) == new CaveWallArt(1, 11, 0, 5, 5, 0) && DecodeWallArt(0x10b01a11).EdgeRotation == 3 && DecodeWallArt(0x10b01a11).Rotation == 3 &&
            DecodeWallArt(0x1000_0000 | (2u << 11)).EdgeRotation == 3 && DecodeWallArt(0x1000_0000 | (13u << 14)).Piece == 13, "wall art fields");
        // Edges: a wall on the cell facing y-1, a neighbour's wall facing back across x+1, an opening piece.
        static uint Wall(int rotation, int piece = 0) => 0x1000_0000u | ((uint)piece << 14) | ((uint)rotation << 11);
        var walls = new[] { new CaveWall(10, 10, Wall(6)), new CaveWall(11, 10, Wall(1, 13)), new CaveWall(10, 11, Wall(5)), new CaveWall(9, 10, Wall(3)) };
        Check(CaveEdgeWalls(walls, (10, 10), 7, (0, 0)) == new CaveEdge(7, 1, 0, 0), "leaving edge");
        Check(CaveEdgeWalls(walls, (10, 10), 5, (0, 0)) == new CaveEdge(5, 0, 1, 1), "entering opening edge");
        Check(CaveEdgeWalls(walls, (10, 10), 3, (0, 0)) == new CaveEdge(3, 0, 0, 0), "neighbour facing the wrong edge");
        Check(CaveEdgeWalls(walls, (10, 10), 1, (0, 0)) == new CaveEdge(1, 0, 0, 0), "neighbour facing sideways");
        Rejects(() => CaveEdgeWalls(walls, (63, 10), 5, (0, 0)), "CAVE_NEIGHBOUR_OUTSIDE_SECTOR");
        // Terrain: mask wins, facade bit 0 clear blocks, class 0 uses rank within its category.
        var names = new SortedDictionary<int, string> { [0] = "aaa", [100] = "bbb/b", [105] = "ccc", [200] = "ddd", [300] = "eee" };
        var categories = ClosureTileNameCategories(names);
        Check(CaveTerrainBlocks(true, 0, categories) == true && CaveTerrainBlocks(false, 0xB000_0000, categories) == true && CaveTerrainBlocks(false, 0xB000_0001, categories) == false, "mask and facade");
        Check(CaveTerrainBlocks(false, 1u << 8, categories) == true && CaveTerrainBlocks(false, (1u << 8) | (1u << 22), categories) == false, "outdoor rank");
        Check(CaveTerrainBlocks(false, (1u << 8) | (2u << 22), categories) is null && CaveTerrainBlocks(false, 0x2000_0000, categories) is null, "unknown rank and class");
        // Aggregate selector: pinned wrap and reversal, on synthetic names only.
        Check(CaveAggregateSelector("Abz-9") == "9-ooN" && CaveAggregateSelector("nN") == "Cc" && CaveAggregateSelector("m") == "z", "aggregate selector");
        Check(CaveStep((10, 10), 1) == (9, 10) && CaveStep((10, 10), 3) == (10, 11) && CaveStep((10, 10), 5) == (11, 10) && CaveStep((10, 10), 7) == (10, 9) && CaveStep((10, 10), 0) is null, "cardinal steps");
        Check(ScriptNumberFromFile("02793Name.scr") == 2793 && ScriptNumberFromFile("x.scr") is null && ScriptNumberFromFile("01532.dlg") is null, "script file numbers");
        Console.WriteLine("cave-transition-self-test: PASS (teleport shape and refusals, framing, census, wall fields and edges, terrain rule, aggregate selector, cardinal steps)");
    }
}
