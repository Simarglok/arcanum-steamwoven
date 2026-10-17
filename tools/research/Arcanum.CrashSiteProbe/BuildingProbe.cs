using System.Buffers.Binary;
using System.Text.Json;
using Arcanum.Data;

namespace Arcanum.CrashSiteProbe;

// ARC-RES-029 research only: the first building the player can reach from the Crash
// Site, and how its walls, doors, windows and roofs are stored and behave. Independently
// authored from the pinned CE locators cited in docs/research/first-building-evidence.md;
// no CE code or table is reused. No renderer, movement, door action or roof state is
// implemented. Only numbers, booleans and SHA-256 digests leave the probe.
internal static partial class Program
{
    private const int StartSectorX = 1452;
    private const int StartSectorY = 1290;
    // Start-map catalogue start point (map 5000), inside the start sector.
    private const int StartTileX = 92958;
    private const int StartTileY = 82592;
    private const string ExteriorSectorPrefix = "maps/Arcanum1-024-fixed/";
    private const long BuildingSectorId = 87375742379; // (1451,1302)
    private const int RoofCellsPerSide = 16;
    private const uint InvalidArt = uint.MaxValue;
    // Type-specific serialized slots follow the 36 common slots.
    private const int WallFlagsField = 36;
    private const int PortalFlagsField = 36;
    private const int PortalLockDifficultyField = 37;
    private const int PortalKeyField = 38;
    private const int PortalNotifyField = 39;
    private const int SpellFlagsSlot = 19;
    private const uint PassWalledSpellFlag = 0x0010_0000;
    // Wall p-pieces the pinned traversal code treats as openings: the first six are
    // window pieces, the rest doorway pieces that never block a step themselves.
    private static readonly int[] WindowWallPieces = [10, 13, 14, 17, 18, 19];
    private static readonly int[] DoorwayWallPieces = [22, 25, 26, 29, 30, 31, 32];

    internal enum RoofPiece { NorthWestOutside, West, North, NorthWestInside, SouthWestOutside, SouthWestInside, NorthEastInside, NorthEastOutside, Center, SouthEastOutside, South, East, SouthEastInside }

    internal readonly record struct PortalArt(int ArtClass, int Number, bool Door, int Frame, int Rotation, int EdgeRotation, bool Busted, int Palette, int LowFlags);
    internal readonly record struct RoofArt(int ArtClass, int Number, int Frame, bool Flipped, RoofPiece Piece, bool Fill, bool Fade, int Palette);

    internal static PortalArt DecodePortalArt(uint aid)
    {
        var rotation = (int)((aid >> 11) & 7);
        // Portal type bit: 0 window, 1 door. Bit 9 is the busted (damaged) marker.
        return new((int)(aid >> 28), (int)((aid >> 19) & 511), ((aid >> 10) & 1) == 1, (int)((aid >> 14) & 31), rotation, rotation | 1, (aid & 0x200) != 0, (int)((aid >> 4) & 3), (int)(aid & 0xF));
    }

    internal static RoofArt DecodeRoofArt(uint aid)
    {
        var frame = (int)((aid >> 14) & 31);
        var flipped = (aid & 1) != 0;
        var piece = frame + (flipped ? 9 : 0);
        if (aid >> 28 != 10 || piece > (int)RoofPiece.SouthEastInside) throw new InvalidDataException("ROOF_ART_SHAPE");
        return new((int)(aid >> 28), (int)((aid >> 19) & 511), frame, flipped, (RoofPiece)piece, ((aid >> 13) & 1) != 0, ((aid >> 12) & 1) != 0, (int)((aid >> 4) & 3));
    }

    /// <summary>Roof slot of a tile inside its sector: one slot per 4x4 tile block.</summary>
    internal static int RoofSlot(int localX, int localY) => (localX >> 2) + RoofCellsPerSide * (localY >> 2);

    // Each piece is the union (inside corners) or intersection (outside corners) of edge
    // bands inside its 4x4 block (row = y mod 4, column = x mod 4): west keeps columns 0-2,
    // east 2-3, north rows 2-3, south rows 0-2. Straight edges are one band, the centre all
    // sixteen tiles. Piece names follow the iso view (+x screen lower left, +y lower
    // right): a west edge lies at the building's largest x, a north edge at its smallest y.
    internal static bool RoofBand(RoofPiece piece, int row, int column)
    {
        bool w = column <= 2, e = column >= 2, n = row >= 2, s = row <= 2;
        return piece switch
        {
            RoofPiece.NorthWestOutside => n && w,
            RoofPiece.West => w,
            RoofPiece.North => n,
            RoofPiece.NorthWestInside => n || w,
            RoofPiece.SouthWestOutside => s && w,
            RoofPiece.SouthWestInside => s || w,
            RoofPiece.NorthEastInside => n || e,
            RoofPiece.NorthEastOutside => n && e,
            RoofPiece.Center => true,
            RoofPiece.SouthEastOutside => s && e,
            RoofPiece.South => s,
            RoofPiece.East => e,
            RoofPiece.SouthEastInside => s || e,
            _ => throw new InvalidDataException("ROOF_PIECE"),
        };
    }

    /// <summary>Coverage reads the block that holds (x+3, y+3) and requires an indoor
    /// tile and an unfilled roof. Fade does not change coverage when faded roofs count.</summary>
    internal static bool RoofCovers(bool indoor, RoofArt? roofAtOffset, int x, int y) =>
        indoor && roofAtOffset is { Fill: false } roof && RoofBand(roof.Piece, (y + 3) & 3, (x + 3) & 3);

    /// <summary>The fill (hide) decision for the block under the player's own tile, no offset.</summary>
    internal static bool RoofFillDecision(RoofPiece piece, int x, int y) => RoofBand(piece, y & 3, x & 3);

    // A flood that reaches a block by moving in a cardinal direction does not change an
    // edge piece whose outer side faces the flood: that is another roof's near edge, so the
    // flood stays inside one roof. Rotation 1 is -x, 3 is +y, 5 is +x, 7 is -y.
    internal static bool RoofFloodStops(int arrivedByRotation, RoofPiece piece) => arrivedByRotation switch
    {
        1 => piece is RoofPiece.NorthWestOutside or RoofPiece.West or RoofPiece.SouthWestOutside,
        3 => piece is RoofPiece.NorthWestOutside or RoofPiece.North or RoofPiece.NorthEastOutside,
        5 => piece is RoofPiece.SouthEastOutside or RoofPiece.East or RoofPiece.NorthEastOutside,
        7 => piece is RoofPiece.SouthWestOutside or RoofPiece.South or RoofPiece.SouthEastOutside,
        _ => false,
    };

    /// <summary>Applies one fill change from a block and floods four-neighbour blocks.
    /// Blocks are keyed by their world origin (multiples of four).</summary>
    internal static void RoofFlood(Dictionary<(int X, int Y), RoofArt> roofs, (int X, int Y) block, bool fill, int arrivedBy)
    {
        if (!roofs.TryGetValue(block, out var roof) || roof.Fill == fill) return;
        if (arrivedBy != -1 && RoofFloodStops(arrivedBy, roof.Piece)) return;
        roofs[block] = roof with { Fill = fill };
        RoofFlood(roofs, (block.X + 4, block.Y), fill, 5);
        RoofFlood(roofs, (block.X - 4, block.Y), fill, 1);
        RoofFlood(roofs, (block.X, block.Y + 4), fill, 3);
        RoofFlood(roofs, (block.X, block.Y - 4), fill, 7);
    }

    /// <summary>Door open frames: windows have closed 0 and open 1; doors whose cardinal
    /// rotation is 0 or 6 animate through 4..6, the others through 1..3; frame 0 is closed.</summary>
    internal static (int First, int Last) PortalOpenFrames(bool door, int rotation)
    {
        if (!door) return (1, 1);
        var cardinal = rotation & ~1;
        return cardinal is 0 or 6 ? (4, 6) : (1, 3);
    }

    internal enum DoorAdmission { Open, Busted, ScriptRefused, Jammed, MagicallyHeld, IndoorToOutdoor, Locked }

    /// <summary>The player character's admission to open a closed door it walks into.
    /// Hours 7..21 are day. A missing key means no key in the party.</summary>
    internal static DoorAdmission AdmitPlayerDoor(uint portalFlags, bool scriptAllows, bool indoorToOutdoor, int hour, bool hasKey)
    {
        if ((portalFlags & 0x80) != 0) return DoorAdmission.Busted;
        if (!scriptAllows) return DoorAdmission.ScriptRefused;
        if ((portalFlags & 0x02) != 0) return DoorAdmission.Jammed;
        if ((portalFlags & 0x04) != 0) return DoorAdmission.MagicallyHeld;
        if ((portalFlags & 0x10) == 0 && indoorToOutdoor) return DoorAdmission.IndoorToOutdoor;
        var day = hour is >= 7 and <= 21;
        var locked = (portalFlags & 0x08) == 0 && (portalFlags & 0x01) != 0 &&
            !((portalFlags & 0x20) != 0 && !day) && !((portalFlags & 0x40) != 0 && day);
        return locked && !hasKey ? DoorAdmission.Locked : DoorAdmission.Open;
    }

    internal static bool? TileIndoor(uint raw) => (raw >> 28) switch
    {
        0 => ((raw >> 8) & 1) == 0,
        11 => ((raw >> 25) & 1) == 0,
        _ => null,
    };

    internal readonly record struct EdgeObject(int X, int Y, int Type, int EdgeRotation, int Piece);

    /// <summary>Wall and portal objects on one cardinal edge: those on the cell facing the
    /// edge, and those on the neighbour facing back across it.</summary>
    internal static IReadOnlyList<EdgeObject> EdgeObjects(IReadOnlyList<EdgeObject> objects, (int X, int Y) cell, int rotation)
    {
        var next = CaveStep(cell, rotation) ?? throw new InvalidDataException("BUILDING_EDGE_NOT_CARDINAL");
        return objects.Where(o => (o.X == cell.X && o.Y == cell.Y && o.EdgeRotation == rotation) || (o.X == next.X && o.Y == next.Y && o.EdgeRotation == (rotation + 4) % 8)).ToArray();
    }

    /// <summary>Room walls: solid and window wall pieces and every portal (doors treated
    /// as closed) separate rooms; doorway wall pieces do not.</summary>
    internal static bool EdgeSeparatesRooms(IReadOnlyList<EdgeObject> edge) =>
        edge.Any(o => o.Type == 1 || (o.Type == 0 && !DoorwayWallPieces.Contains(o.Piece)));

    private sealed record BuildingCensus(long Id, int X, int Y, int Ordinal, int? Townmap, int Walls, int Portals, int RoofCells, int Distance);

    private static int ProbeBuildingResearch(string[] args)
    {
        const int fixedArgs = 6;
        if (args.Length != fixedArgs + CaveArchives.Length * 2 || args[0] != "--game-root" || args[2] != "--prototype-root" || args[4] != "--mobile")
            throw new ArgumentException("BUILDING_USAGE");
        var archivePaths = new string[CaveArchives.Length];
        for (var i = 0; i < CaveArchives.Length; i++)
        {
            if (args[fixedArgs + i * 2] != "--" + CaveArchives[i].Role) throw new ArgumentException("BUILDING_USAGE");
            archivePaths[i] = args[fixedArgs + 1 + i * 2];
        }
        var gameRoot = args[1]; var prototypeRoot = args[3]; var mobilePath = args[5];
        var before = archivePaths.Select(InputFingerprint.Create).ToArray();
        for (var i = 0; i < CaveArchives.Length; i++)
            if (before[i] != new InputFingerprint(CaveArchives[i].Bytes, CaveArchives[i].Sha256)) throw new InvalidDataException("BUILDING_ARCHIVE_IDENTITY");
        var mobileBefore = InputFingerprint.Create(mobilePath);
        if (mobileBefore != new InputFingerprint(AreaMobileBytes, AreaMobileSha)) throw new InvalidDataException("BUILDING_AGGREGATE_IDENTITY");

        var options = new DatReaderOptions { MaxArchiveBytes = checked((uint)CaveArchives.Max(a => a.Bytes)), MaxStoredEntryBytes = MaxDecodedSectorBytes, MaxDecodedEntryBytes = MaxDecodedSectorBytes };
        var archives = archivePaths.Select(p => DatArchive.Open(p, options)).ToArray();
        var prototypeInputs = new Dictionary<string, InputFingerprint>(StringComparer.Ordinal);
        try
        {
            var module = archives[0];
            // 1. Every exterior sector: walls, portals and non-empty roof slots.
            var census = new List<BuildingCensus>(); byte[]? selectedBytes = null; DatEntry? selectedEntry = null;
            // Sectors come from the module only: no other archive may carry this map folder.
            if (archives.Skip(1).Any(a => a.Entries.Any(e => AreaName(e).StartsWith(ExteriorSectorPrefix, StringComparison.OrdinalIgnoreCase))))
                throw new InvalidDataException("BUILDING_SECTOR_SHADOWED");
            var sectorEntries = module.Entries.Where(e => AreaName(e).StartsWith(ExteriorSectorPrefix, StringComparison.Ordinal) && AreaName(e).EndsWith(".sec", StringComparison.Ordinal)).OrderBy(e => e.Ordinal).ToArray();
            foreach (var entry in sectorEntries)
            {
                var id = long.Parse(Path.GetFileNameWithoutExtension(AreaName(entry)), System.Globalization.CultureInfo.InvariantCulture);
                var x = checked((int)(id % 67108864)); var y = checked((int)(id / 67108864));
                var bytes = module.ReadEntry(entry); var slice = SectorSlice.Parse(bytes, false); var framing = SectorFraming.Parse(bytes, slice.LightCount);
                int? townmap = framing.MetadataAndSoundBytes == 24 ? BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(framing.MetadataAndSoundStart)) : null;
                var instances = ObjectWire.ParseWindow(bytes.AsSpan(framing.ObjectStart, framing.ObjectBytes), slice.TrailingInt32);
                var roofs = RoofWords(bytes, framing).Count(w => w != InvalidArt);
                census.Add(new(id, x, y, entry.Ordinal, townmap, instances.Count(i => i.Type == 0), instances.Count(i => i.Type == 1), roofs, Math.Max(Math.Abs(x - StartSectorX), Math.Abs(y - StartSectorY))));
                if (id == BuildingSectorId) { selectedBytes = bytes; selectedEntry = entry; }
            }
            if (census.Count != 621 || census.Select(c => c.Id).Distinct().Count() != 621) throw new InvalidDataException("BUILDING_SURVEY_SCOPE_CHANGED");
            var exteriorBytes = File.ReadAllBytes(mobilePath);
            var exterior = FrameAggregate(exteriorBytes, AreaMobileBytes);
            var aggregateWallsPortals = exterior.Count(r => r.Instance.Type is 0 or 1);

            // Selection policy: the nearest sector (Chebyshev, in sectors) from the start
            // sector that holds at least one portal and one roof slot. It must be unique.
            var buildings = census.Where(c => c.Portals > 0 && c.RoofCells > 0).OrderBy(c => c.Distance).ThenBy(c => c.Id).ToArray();
            var nearest = buildings.Where(c => c.Distance == buildings[0].Distance).ToArray();
            if (nearest.Length != 1 || nearest[0].Id != BuildingSectorId || selectedBytes is null || selectedEntry is null) throw new InvalidDataException("BUILDING_SELECTION_CHANGED");
            var chosen = nearest[0];
            var ring = Enumerable.Range(-1, 3).SelectMany(dy => Enumerable.Range(-1, 3).Select(dx => (X: chosen.X + dx, Y: chosen.Y + dy))).Where(p => p != (chosen.X, chosen.Y))
                .Select(p => census.SingleOrDefault(c => c.X == p.X && c.Y == p.Y) is { } c ? new { p.X, p.Y, present = true, c.Walls, c.Portals, roof_cells = c.RoofCells } : new { p.X, p.Y, present = false, Walls = 0, Portals = 0, roof_cells = 0 }).ToArray();
            var towns = census.Where(c => c.Townmap is not null and not 0 and not 2 && c.Walls + c.Portals + c.RoofCells > 0).GroupBy(c => c.Townmap!.Value)
                .Select(g => new { townmap = g.Key, nearest_distance = g.Min(c => c.Distance), sectors = g.Count(), walls = g.Sum(c => c.Walls), portals = g.Sum(c => c.Portals), roof_cells = g.Sum(c => c.RoofCells) })
                .OrderBy(t => t.nearest_distance).ThenBy(t => t.townmap).ToArray();
            if (towns.Length == 0) throw new InvalidDataException("BUILDING_NO_TOWN");

            // 2. The selected sector in detail.
            var sx = chosen.X; var sy = chosen.Y; var ox = sx * 64; var oy = sy * 64;
            var sector = selectedBytes; var sslice = SectorSlice.Parse(sector, false); var sframing = SectorFraming.Parse(sector, sslice.LightCount);
            var tiles = new uint[64 * 64];
            for (var i = 0; i < tiles.Length; i++) tiles[i] = BinaryPrimitives.ReadUInt32LittleEndian(sector.AsSpan(sframing.TileStart + 4 * i));
            bool? Indoor(int x, int y) => x < ox || x >= ox + 64 || y < oy || y >= oy + 64 ? null : TileIndoor(tiles[(x - ox) + 64 * (y - oy)]);
            var instancesSelected = ObjectWire.ParseWindow(sector.AsSpan(sframing.ObjectStart, sframing.ObjectBytes), sslice.TrailingInt32);
            var prototypes = ObjectWire.ReadReferencedPrototypesFromDirectory(prototypeRoot, instancesSelected.Select(v => v.PrototypeId).ToArray());
            Remember(prototypes, prototypeInputs);
            var objectWindow = sector.AsSpan(sframing.ObjectStart).ToArray();
            int Field(IReadOnlyDictionary<int, int> numeric, int slot) => numeric.TryGetValue(slot, out var v) ? v : throw new InvalidDataException("BUILDING_FIELD_ABSENT");
            var objects = ObjectWire.ResolveEffectiveInstances(instancesSelected, prototypes.Prototypes).Select(value =>
            {
                var proto = prototypes.Prototypes.Single(p => p.Id.Matches(value.Instance.PrototypeId));
                if (value.Instance.Location is not { } loc) throw new InvalidDataException("BUILDING_LOCATION_ABSENT");
                var (wx, wy) = RebaseSectorLocation(loc, sx, sy);
                return (Value: value, Proto: proto, X: wx, Y: wy, Numeric: EffectiveNumeric(value.Instance, proto), Scripts: EffectiveScripts(objectWindow, value.Instance, proto));
            }).ToArray();
            var walls = objects.Where(o => o.Value.Instance.Type == 0).ToArray();
            var portals = objects.Where(o => o.Value.Instance.Type == 1).ToArray();
            var edgeObjects = walls.Select(w => { var a = DecodeWallArt((uint)w.Value.CurrentAid); return new EdgeObject(w.X, w.Y, 0, a.EdgeRotation, a.Piece); })
                .Concat(portals.Select(p => new EdgeObject(p.X, p.Y, 1, DecodePortalArt((uint)p.Value.CurrentAid).EdgeRotation, -1))).ToArray();

            // Rooms: four-neighbour components of indoor tiles inside the sector.
            var room = new Dictionary<(int X, int Y), int>(); var rooms = new List<object>();
            for (var y = oy; y < oy + 64; y++)
                for (var x = ox; x < ox + 64; x++)
                {
                    if (Indoor(x, y) != true || room.ContainsKey((x, y))) continue;
                    var id = rooms.Count; var queue = new Queue<(int X, int Y)>(); queue.Enqueue((x, y)); room[(x, y)] = id; var members = new List<(int X, int Y)>();
                    while (queue.TryDequeue(out var cell))
                    {
                        members.Add(cell);
                        foreach (var r in new[] { 1, 3, 5, 7 })
                        {
                            var next = CaveStep(cell, r)!.Value;
                            if (Indoor(next.X, next.Y) != true || room.ContainsKey(next) || EdgeSeparatesRooms(EdgeObjects(edgeObjects, cell, r))) continue;
                            room[next] = id; queue.Enqueue(next);
                        }
                    }
                    rooms.Add(new { room = id, tiles = members.Count, min_x = members.Min(m => m.X), min_y = members.Min(m => m.Y), max_x = members.Max(m => m.X), max_y = members.Max(m => m.Y) });
                }

            // Roofs: slot -> art, then coverage, fill decisions and the flood each makes.
            var roofWords = RoofWords(sector, sframing);
            var roofBlocks = new Dictionary<(int X, int Y), RoofArt>();
            for (var by = 0; by < 64; by += 4)
                for (var bx = 0; bx < 64; bx += 4)
                    if (roofWords[RoofSlot(bx, by)] is var word && word != InvalidArt) roofBlocks[(ox + bx, oy + by)] = DecodeRoofArt(word);
            RoofArt? BlockAt(int x, int y) => roofBlocks.TryGetValue((x & ~3, y & ~3), out var r) ? r : null;
            var coverage = new List<object>(); var uncoveredIndoor = new List<object>(); var offsetOutside = 0;
            var covered = 0; var indoorTiles = 0;
            for (var y = oy; y < oy + 64; y++)
                for (var x = ox; x < ox + 64; x++)
                {
                    if (Indoor(x, y) != true) continue;
                    indoorTiles++;
                    if (x + 3 >= ox + 64 || y + 3 >= oy + 64) { offsetOutside++; continue; }
                    if (RoofCovers(true, BlockAt(x + 3, y + 3), x, y)) covered++; else uncoveredIndoor.Add(new { x, y, room = room.GetValueOrDefault((x, y), -1) });
                }
            var outcomes = new Dictionary<string, (int Tiles, bool Fill, IReadOnlyList<(int X, int Y)> Changed)>();
            foreach (var (cell, _) in room)
            {
                if (BlockAt(cell.X, cell.Y) is not { } block) { Count("no_block", false, []); continue; }
                var fill = RoofFillDecision(block.Piece, cell.X, cell.Y);
                var state = new Dictionary<(int X, int Y), RoofArt>(roofBlocks);
                if (block.Fill != fill) RoofFlood(state, (cell.X & ~3, cell.Y & ~3), fill, -1);
                var changed = state.Where(kv => kv.Value.Fill != roofBlocks[kv.Key].Fill).Select(kv => kv.Key).Order().ToArray();
                Count((fill ? "fill:" : "keep:") + string.Join(';', changed.Select(c => $"{c.X},{c.Y}")), fill, changed);
            }
            // Tiles under a roof block that are not indoor: where standing would refill (show) it.
            var underRoofOutside = roofBlocks.Keys.SelectMany(b => Enumerable.Range(0, 16).Select(i => (X: b.X + i % 4, Y: b.Y + i / 4))).Where(c => Indoor(c.X, c.Y) != true).ToArray();
            var underRoofOutsideHiding = underRoofOutside.Count(c => RoofFillDecision(roofBlocks[(c.X & ~3, c.Y & ~3)].Piece, c.X, c.Y));
            // From a fully hidden roof, how many blocks each outside step under it restores.
            var restoreCounts = underRoofOutside.Select(c =>
            {
                var state = roofBlocks.ToDictionary(kv => kv.Key, kv => kv.Value with { Fill = true });
                RoofFlood(state, (c.X & ~3, c.Y & ~3), false, -1);
                return state.Count(kv => !kv.Value.Fill);
            }).GroupBy(n => n).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count());
            void Count(string key, bool fill, IReadOnlyList<(int X, int Y)> changed) =>
                outcomes[key] = outcomes.TryGetValue(key, out var o) ? o with { Tiles = o.Tiles + 1 } : (1, fill, changed);

            // Portals: art, state, flags, opening walls, both sides of the edge.
            object PortalRow((ResolvedInstance Value, PrototypeInfo Proto, int X, int Y, IReadOnlyDictionary<int, int> Numeric, IReadOnlyList<ScriptAttachment> Scripts) p)
            {
                var art = DecodePortalArt((uint)p.Value.CurrentAid);
                if (art.ArtClass != 3) throw new InvalidDataException("BUILDING_PORTAL_ART_CLASS");
                var across = CaveStep((p.X, p.Y), art.EdgeRotation)!.Value;
                var edge = EdgeObjects(edgeObjects, (p.X, p.Y), art.EdgeRotation);
                var flags = (uint)Field(p.Numeric, PortalFlagsField);
                var hereIndoor = Indoor(p.X, p.Y); var acrossIndoor = Indoor(across.X, across.Y);
                return new
                {
                    p.X, p.Y, prototype = p.Value.Instance.PrototypeId.AidValue(), current_aid_inherited = p.Value.CurrentAidInherited, art,
                    open = art.Frame != 0, open_frames = PortalOpenFrames(art.Door, art.Rotation),
                    portal_flags = flags, locked = (flags & 0x01) != 0, jammed = (flags & 0x02) != 0, magically_held = (flags & 0x04) != 0, never_locked = (flags & 0x08) != 0,
                    always_locked = (flags & 0x10) != 0, locked_day = (flags & 0x20) != 0, locked_night = (flags & 0x40) != 0, busted = (flags & 0x80) != 0, sticky = (flags & 0x100) != 0,
                    lock_difficulty = Field(p.Numeric, PortalLockDifficultyField), key_id = Field(p.Numeric, PortalKeyField), notify_npc = Field(p.Numeric, PortalNotifyField),
                    object_flags = p.Value.EffectiveFlags, no_block = (p.Value.EffectiveFlags & 0x400) != 0, see_through = (p.Value.EffectiveFlags & 0x10) != 0, shoot_through = (p.Value.EffectiveFlags & 0x20) != 0,
                    off = (p.Value.EffectiveFlags & 0x2) != 0, dont_draw = (p.Value.EffectiveFlags & 0x100) != 0, scripts = p.Scripts,
                    edge = new
                    {
                        rotation = art.EdgeRotation, across_x = across.X, across_y = across.Y,
                        here_indoor = hereIndoor, across_indoor = acrossIndoor, here_room = room.GetValueOrDefault((p.X, p.Y), -1), across_room = room.GetValueOrDefault(across, -1),
                        walls = edge.Where(o => o.Type == 0).Select(o => new { o.X, o.Y, o.Piece, doorway = DoorwayWallPieces.Contains(o.Piece), window = WindowWallPieces.Contains(o.Piece) }).ToArray(),
                        other_portals = edge.Count(o => o.Type == 1) - 1,
                    },
                };
            }
            var portalRows = portals.Select(PortalRow).ToArray();
            // The player's admission at each door, from both sides, by day and by night,
            // without a key and with no unlock script attached.
            var doorAdmission = portals.Where(p => DecodePortalArt((uint)p.Value.CurrentAid).Door).Select(p =>
            {
                var art = DecodePortalArt((uint)p.Value.CurrentAid);
                var flags = (uint)Field(p.Numeric, PortalFlagsField);
                var across = CaveStep((p.X, p.Y), art.EdgeRotation)!.Value;
                var unlockScript = p.Scripts.Any(s => s.sap == 3 && s.script != 0);
                // Leaving as the original's traversal and door goal both see it (PortalLeaves, ARC-RES-049):
                // the original swaps the two tiles for a mover from across, where the pinned CE source
                // copies one into both and never counts that side as leaving.
                // The source steps by the raw ART rotation; it equals the edge for odd rotations.
                if (art.Rotation != art.EdgeRotation || Indoor(p.X, p.Y) is null || Indoor(across.X, across.Y) is null) throw new InvalidDataException("BUILDING_DOOR_EDGE_NOT_ADMITTED");
                bool Leaving(bool fromPortalTile) => PortalLeaves(art.Rotation, fromPortalTile, Indoor(p.X, p.Y) == true, Indoor(across.X, across.Y) == true);
                return new
                {
                    p.X, p.Y, unlock_script = unlockScript,
                    enter_day = AdmitPlayerDoor(flags, true, Leaving(false), 12, false).ToString(), enter_night = AdmitPlayerDoor(flags, true, Leaving(false), 2, false).ToString(),
                    exit_day = AdmitPlayerDoor(flags, true, Leaving(true), 12, false).ToString(), exit_night = AdmitPlayerDoor(flags, true, Leaving(true), 2, false).ToString(),
                };
            }).ToArray();
            var wallArt = walls.Select(w => DecodeWallArt((uint)w.Value.CurrentAid)).ToArray();
            var wallCensus = new
            {
                walls = walls.Length, prototypes = walls.Select(w => w.Value.Instance.PrototypeId.AidValue()).Distinct().Order().ToArray(),
                art_classes = wallArt.GroupBy(a => a.ArtClass).ToDictionary(g => g.Key, g => g.Count()),
                numbers = wallArt.GroupBy(a => a.Number).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
                pieces = wallArt.GroupBy(a => a.Piece).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
                edge_rotations = wallArt.GroupBy(a => a.EdgeRotation).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
                window_pieces = wallArt.Count(a => WindowWallPieces.Contains(a.Piece)), doorway_pieces = wallArt.Count(a => DoorwayWallPieces.Contains(a.Piece)),
                damaged = walls.Count(w => ((uint)w.Value.CurrentAid & 0x480) != 0),
                wall_flags = walls.GroupBy(w => Field(w.Numeric, WallFlagsField)).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
                passwalled = walls.Count(w => ((uint)w.Numeric.GetValueOrDefault(SpellFlagsSlot) & PassWalledSpellFlag) != 0),
                object_flags = walls.GroupBy(w => w.Value.EffectiveFlags).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
                with_scripts = walls.Count(w => w.Scripts.Any(s => s.script != 0)),
                rows = walls.Select(w => { var a = DecodeWallArt((uint)w.Value.CurrentAid); return new { w.X, w.Y, a.Piece, edge = a.EdgeRotation, a.Variation, flags = w.Value.EffectiveFlags, window = WindowWallPieces.Contains(a.Piece), doorway = DoorwayWallPieces.Contains(a.Piece) }; }).OrderBy(r => r.Y).ThenBy(r => r.X).ToArray(),
            };

            // Art chains: portal names (first token, windows offset by 1001) and roof names
            // (entry order). Every mapping and art file must resolve exactly once.
            (int Archive, DatEntry Entry, byte[] Bytes) Unique(string path)
            {
                var hits = archives.SelectMany((a, i) => a.Entries.Where(e => AreaName(e).Equals(path, StringComparison.OrdinalIgnoreCase)).Select(e => (Archive: i, Entry: e))).ToArray();
                if (hits.Length != 1) throw new InvalidDataException("BUILDING_RESOURCE_MISSING_OR_AMBIGUOUS");
                return (hits[0].Archive, hits[0].Entry, archives[hits[0].Archive].ReadEntry(hits[0].Entry));
            }
            object ArtFile(string path, int group, int frame)
            {
                var (index, entry, bytes) = Unique(path);
                var meta = ArtDecoder.Inspect(bytes, new ArtDecoderOptions { MaxInputBytes = MaxDecodedSectorBytes });
                var selected = group < meta.Header.PhysicalGroupCount && frame < meta.Header.FramesPerGroup ? meta.GetFrame(group, frame) : null;
                return new { archive = CaveArchives[index].Role, entry.Ordinal, bytes = bytes.Length, sha256 = AreaHash(bytes), frames_per_group = meta.Header.FramesPerGroup, physical_groups = meta.Header.PhysicalGroupCount, meta.Header.ActionFrame, selected = selected is null ? null : new { group, frame, selected.Width, selected.Height, selected.HotX, selected.HotY } };
            }
            var portalMesFile = Unique("art/portal/portal.mes");
            var portalNames = ObjectArt.ParseMes(portalMesFile.Bytes);
            var portalArt = portals.Select(p => DecodePortalArt((uint)p.Value.CurrentAid)).Distinct().Select(a =>
            {
                var key = a.Number + (a.Door ? 0 : 1001);
                if (!portalNames.TryGetValue(key, out var entry)) throw new InvalidDataException("BUILDING_PORTAL_NAME");
                var name = entry.Split(' ')[0];
                if (!ObjectArt.IsSafeRelativeArtFilename(name) || a.Busted) throw new InvalidDataException("BUILDING_PORTAL_NAME");
                // One physical group; bit 0 of the low nibble mirrors it for the other side.
                return new { key, art = a, mirrored = (a.LowFlags & 1) != 0, file = ArtFile("art/portal/" + name, 0, a.Frame) };
            }).ToArray();
            var roofMesFile = Unique("art/roof/roofname.mes");
            var roofNames = ObjectArt.ParseMes(roofMesFile.Bytes);
            var roofKeysContiguous = roofNames.Keys.SequenceEqual(Enumerable.Range(0, roofNames.Count));
            var roofArt = roofBlocks.Values.Select(r => (r.Number, r.Frame)).Distinct().Order().Select(r =>
            {
                if (!roofKeysContiguous || !roofNames.TryGetValue(r.Number, out var name) || name.Length == 0 || name.Any(c => c is '/' or '\\' or < ' ' or > '~')) throw new InvalidDataException("BUILDING_ROOF_NAME");
                return new { r.Number, r.Frame, file = ArtFile("art/roof/" + name + ".art", 0, r.Frame) };
            }).ToArray();
            var shadeFile = Unique("art/roof/roofshade.mes");
            var shade = ObjectArt.ParseMes(shadeFile.Bytes);
            // Full opacity, partial opacity, full transparency: three byte values, nothing else.
            if (!shade.Keys.SequenceEqual([0, 1, 2])) throw new InvalidDataException("BUILDING_ROOF_SHADE");
            var shadeValues = shade.ToDictionary(kv => kv.Key, kv => int.TryParse(kv.Value.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var v) && v <= 255 ? v : throw new InvalidDataException("BUILDING_ROOF_SHADE"));

            // Map changes: the exterior jump list and every script attached in the sector
            // (objects, tiles, sector, mobiles located in it), plus one hop of literal calls.
            var jump = Unique(ExteriorSectorPrefix + "map.jmp");
            var jumpCount = BinaryPrimitives.ReadInt32LittleEndian(jump.Bytes);
            if (jumpCount < 0 || jump.Bytes.Length != 4 + 32L * jumpCount) throw new InvalidDataException("BUILDING_JUMP_FRAMING");
            var jumpsInSector = Enumerable.Range(0, jumpCount).Count(i =>
            {
                var loc = BinaryPrimitives.ReadInt64LittleEndian(jump.Bytes.AsSpan(4 + 32 * i + 8));
                var jx = unchecked((int)loc); var jy = unchecked((int)(loc >> 32));
                return jx >= ox && jx < ox + 64 && jy >= oy && jy < oy + 64;
            });
            bool InSector(long? loc) => loc is { } l && unchecked((int)l) >= ox && unchecked((int)l) < ox + 64 && unchecked((int)(l >> 32)) >= oy && unchecked((int)(l >> 32)) < oy + 64;
            var sectorMobiles = exterior.Where(r => InSector(r.Instance.Location)).ToArray();
            var mobilePrototypes = ObjectWire.ReadReferencedPrototypesFromDirectory(prototypeRoot, sectorMobiles.Select(r => r.Instance.PrototypeId).ToArray());
            Remember(mobilePrototypes, prototypeInputs);
            var mobileScripts = sectorMobiles.Select(r => EffectiveScripts(exteriorBytes.AsSpan(r.Offset, r.Length).ToArray(), r.Instance, mobilePrototypes.Prototypes.Single(p => p.Id.Matches(r.Instance.PrototypeId)))).ToArray();
            var mobileRows = sectorMobiles.Select((r, i) =>
            {
                var x = unchecked((int)r.Instance.Location!.Value); var y = unchecked((int)(r.Instance.Location.Value >> 32));
                return new { type = r.Instance.Type, prototype = r.Instance.PrototypeId.AidValue(), x, y, indoor = Indoor(x, y), room = room.GetValueOrDefault((x, y), -1), scripts = mobileScripts[i].Where(s => s.script != 0).ToArray() };
            }).ToArray();
            var scriptOwners = objects.SelectMany(o => o.Scripts.Where(s => s.script != 0).Select(s => new { owner = "sector_object", type = o.Value.Instance.Type, prototype = o.Value.Instance.PrototypeId.AidValue(), o.X, o.Y, room = room.GetValueOrDefault((o.X, o.Y), -1), s.sap, s.script })).ToArray();
            var tileScripts = Enumerable.Range(0, sframing.TileScriptCount).Select(i => BinaryPrimitives.ReadInt32LittleEndian(sector.AsSpan(sframing.TileScriptsStart + 4 + i * 24 + 16))).ToArray();
            int? sectorScript = sframing.SectorScriptBytes == 12 ? BinaryPrimitives.ReadInt32LittleEndian(sector.AsSpan(sframing.SectorScriptStart + 8)) : null;
            var attached = new SortedSet<int>(objects.SelectMany(o => o.Scripts.Select(s => s.script)).Concat(mobileScripts.SelectMany(s => s.Select(a => a.script))).Concat(tileScripts));
            if (sectorScript is { } ss) attached.Add(ss);
            attached.Remove(0);
            var scriptCensus = new List<object>(); var queue2 = new Queue<(int Script, int Hop)>(attached.Select(s => (s, 0))); var seen = new HashSet<int>(attached); var teleports = 0;
            while (queue2.TryDequeue(out var item))
            {
                if (scriptCensus.Count >= MaxScannedScripts) throw new InvalidDataException("BUILDING_SCRIPT_SCAN_BOUND");
                var variants = archives.SelectMany((a, i) => a.Entries.Where(e => ScriptNumber(e) == item.Script).Select(e => (Archive: i, Entry: e))).ToArray();
                var rows = variants.Select(v =>
                {
                    if (v.Entry.DeclaredOutputSize > MaxScriptBytes) throw new InvalidDataException("BUILDING_SCRIPT_BOUND");
                    var bytes = archives[v.Archive].ReadEntry(v.Entry); var c = CaveScriptCensus(bytes); teleports += c.Teleports;
                    if (item.Hop == 0) foreach (var target in c.Edges.Select(e => e.Target).Where(t => t > 0 && seen.Add(t))) queue2.Enqueue((target, 1));
                    return new { archive = CaveArchives[v.Archive].Role, sha256 = AreaHash(bytes), file_flags = c.Flags, c.Lines, teleport_actions = c.Teleports, script_edges = c.Edges, unresolved_edges = c.Unresolved };
                }).ToArray();
                scriptCensus.Add(new { script = item.Script, hop = item.Hop, variants = rows });
            }

            var looseOverrides = new[] { "data", "modules/Arcanum" }.Select(root => new
            {
                root = root == "data" ? "data" : "module-folder",
                portal_art = LooseFiles(gameRoot, root + "/art", "portal").Count(),
                roof_art = LooseFiles(gameRoot, root + "/art", "roof").Count(),
                scripts = LooseFiles(gameRoot, root, "scr").Count(f => ScriptNumberFromFile(f) is { } n && seen.Contains(n)),
            }).ToArray();
            if (looseOverrides.Any(o => o.portal_art != 0 || o.roof_art != 0 || o.scripts != 0)) throw new InvalidDataException("BUILDING_LOOSE_OVERRIDE_PRESENT");

            var after = archivePaths.Select(InputFingerprint.Create).ToArray();
            if (!after.SequenceEqual(before) || mobileBefore != InputFingerprint.Create(mobilePath) || prototypeInputs.Any(p => InputFingerprint.Create(p.Key) != p.Value))
                throw new InvalidDataException("BUILDING_INPUT_CHANGED");
            var report = new
            {
                schema_version = 1,
                archives = CaveArchives.Select((a, i) => new { role = a.Role, before[i].Bytes, before[i].Sha256 }).ToArray(),
                mobile = mobileBefore,
                prototypes = new { count = prototypeInputs.Count, aggregate_sha256 = PrototypeDigest(prototypeInputs.Values), reverified = true },
                survey = new
                {
                    sectors = census.Count, with_walls = census.Count(c => c.Walls > 0), with_portals = census.Count(c => c.Portals > 0), with_roofs = census.Count(c => c.RoofCells > 0),
                    walls = census.Sum(c => c.Walls), portals = census.Sum(c => c.Portals), roof_cells = census.Sum(c => c.RoofCells), aggregate_walls_portals = aggregateWallsPortals,
                    nearest_buildings = buildings.Take(12).Select(c => new { c.Id, c.X, c.Y, c.Townmap, c.Walls, c.Portals, roof_cells = c.RoofCells, c.Distance }).ToArray(),
                    nearest_towns = towns.Take(3).ToArray(),
                    nearest_town_sectors = census.Where(c => c.Townmap == towns[0].townmap && c.Walls + c.Portals + c.RoofCells > 0).OrderBy(c => c.Distance).ThenBy(c => c.Id).Select(c => new { c.X, c.Y, c.Walls, c.Portals, roof_cells = c.RoofCells, c.Distance }).ToArray(),
                },
                selected = new
                {
                    chosen.Id, chosen.X, chosen.Y, chosen.Townmap, chosen.Distance, ordinal = selectedEntry.Ordinal, decoded_bytes = sector.Length, sha256 = AreaHash(sector), ring,
                    objects = objects.GroupBy(o => o.Value.Instance.Type).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
                    lights = sslice.LightCount, roof_empty = sframing.RoofEmpty,
                    start_tile_chebyshev = room.Keys.Min(c => Math.Max(Math.Abs(c.X - StartTileX), Math.Abs(c.Y - StartTileY))),
                },
                walls = wallCensus, portals = portalRows, door_admission = doorAdmission, portal_art = portalArt, portal_mapping = new { archive = CaveArchives[portalMesFile.Archive].Role, sha256 = AreaHash(portalMesFile.Bytes) },
                rooms, indoor_tiles = indoorTiles,
                roofs = new
                {
                    slots = roofBlocks.Count,
                    blocks = roofBlocks.OrderBy(kv => kv.Key.Y).ThenBy(kv => kv.Key.X).Select(kv => new { kv.Key.X, kv.Key.Y, kv.Value.Number, kv.Value.Frame, kv.Value.Flipped, piece = kv.Value.Piece.ToString(), kv.Value.Fill, kv.Value.Fade, kv.Value.Palette }).ToArray(),
                    art = roofArt, mapping = new { archive = CaveArchives[roofMesFile.Archive].Role, sha256 = AreaHash(roofMesFile.Bytes), entries = roofNames.Count, contiguous = roofKeysContiguous },
                    shade = new { archive = CaveArchives[shadeFile.Archive].Role, sha256 = AreaHash(shadeFile.Bytes), values = shadeValues },
                    indoor_covered = covered, indoor_uncovered = uncoveredIndoor, indoor_offset_outside_sector = offsetOutside,
                    outside_tiles_under_roof = underRoofOutside.Length, outside_tiles_that_hide = underRoofOutsideHiding, outside_restores = restoreCounts,
                    fill_outcomes = outcomes.Select(o => new { interior_tiles = o.Value.Tiles, fill = o.Value.Fill, changed_blocks = o.Value.Changed.Select(c => new { c.X, c.Y }).ToArray() }).OrderByDescending(o => o.interior_tiles).ToArray(),
                },
                transitions = new { jump_points = jumpCount, jump_points_in_sector = jumpsInSector, jump_sha256 = AreaHash(jump.Bytes), sector_mobiles = mobileRows, script_owners = scriptOwners, tile_scripts = tileScripts, sector_script = sectorScript, scripts = scriptCensus, teleport_actions = teleports },
                loose_overrides = looseOverrides,
            };
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        finally
        {
            foreach (var archive in archives) archive.Dispose();
        }
    }

    private static uint[] RoofWords(byte[] bytes, SectorFraming framing)
    {
        var words = Enumerable.Repeat(InvalidArt, RoofCellsPerSide * RoofCellsPerSide).ToArray();
        if (framing.RoofEmpty == 0)
            for (var i = 0; i < words.Length; i++) words[i] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(framing.RoofStart + 4 + 4 * i));
        return words;
    }

    private static void BuildingSelfTest()
    {
        static void Check(bool condition, string what)
        {
            if (!condition) throw new InvalidOperationException("building self-test failed: " + what);
        }
        static void Rejects(Action action, string code)
        {
            try { action(); }
            catch (InvalidDataException error) when (error.Message == code) { return; }
            throw new InvalidOperationException("building self-test failed: " + code);
        }
        // Portal art, literal bit positions: class 3, number 5, door, frame 2, rotation 4, palette 1.
        var door = DecodePortalArt(0x3000_0000u | (5u << 19) | (2u << 14) | (4u << 11) | (1u << 10) | (1u << 4));
        Check(door == new PortalArt(3, 5, true, 2, 4, 5, false, 1, 0), "door art fields");
        var window = DecodePortalArt(0x3000_0000u | (7u << 19) | (1u << 11) | 0x200);
        Check(!window.Door && window.Busted && window.EdgeRotation == 1 && window.Frame == 0, "window art fields");
        Check(PortalOpenFrames(true, 0) == (4, 6) && PortalOpenFrames(true, 7) == (4, 6) && PortalOpenFrames(true, 2) == (1, 3) && PortalOpenFrames(true, 5) == (1, 3) && PortalOpenFrames(false, 3) == (1, 1), "open frames");
        // Roof art: number 9, frame 3 flipped -> piece 12; fill and fade bits.
        var roof = DecodeRoofArt(0xA000_0000u | (9u << 19) | (3u << 14) | (1u << 13) | (1u << 12) | (1u << 4) | 1);
        Check(roof == new RoofArt(10, 9, 3, true, RoofPiece.SouthEastInside, true, true, 1), "roof art fields");
        Check(DecodeRoofArt(0xA000_0000u | (8u << 14)).Piece == RoofPiece.Center, "centre piece");
        Rejects(() => DecodeRoofArt(0xA000_0000u | (4u << 14) | 1), "ROOF_ART_SHAPE");
        Rejects(() => DecodeRoofArt(0x4000_0000u), "ROOF_ART_SHAPE");
        Check(RoofSlot(0, 0) == 0 && RoofSlot(3, 3) == 0 && RoofSlot(4, 0) == 1 && RoofSlot(0, 4) == 16 && RoofSlot(63, 63) == 255, "roof slots");
        // Bands: tile counts per piece and the two corner kinds.
        int Count(RoofPiece p) => Enumerable.Range(0, 16).Count(i => RoofBand(p, i / 4, i % 4));
        Check(Count(RoofPiece.Center) == 16 && Count(RoofPiece.West) == 12 && Count(RoofPiece.North) == 8 && Count(RoofPiece.South) == 12 && Count(RoofPiece.East) == 8, "edge bands");
        Check(Count(RoofPiece.NorthWestOutside) == 6 && Count(RoofPiece.SouthWestInside) == 15 && Count(RoofPiece.NorthEastOutside) == 4 && Count(RoofPiece.SouthEastOutside) == 6, "corner bands");
        Check(!RoofBand(RoofPiece.SouthWestInside, 3, 3) && RoofBand(RoofPiece.NorthWestInside, 0, 0) && !RoofBand(RoofPiece.NorthWestInside, 0, 3), "inside corners");
        // Every piece as a literal 16-bit mask, bit (row * 4 + column), written from the band
        // description (west = columns 0-2, east = 2-3, north = rows 2-3, south = rows 0-2).
        var masks = new Dictionary<RoofPiece, int>
        {
            [RoofPiece.NorthWestOutside] = 0x7700, [RoofPiece.West] = 0x7777, [RoofPiece.North] = 0xFF00, [RoofPiece.NorthWestInside] = 0xFF77,
            [RoofPiece.SouthWestOutside] = 0x0777, [RoofPiece.SouthWestInside] = 0x7FFF, [RoofPiece.NorthEastInside] = 0xFFCC, [RoofPiece.NorthEastOutside] = 0xCC00,
            [RoofPiece.Center] = 0xFFFF, [RoofPiece.SouthEastOutside] = 0x0CCC, [RoofPiece.South] = 0x0FFF, [RoofPiece.East] = 0xCCCC, [RoofPiece.SouthEastInside] = 0xCFFF,
        };
        foreach (var (piece, mask) in masks)
            for (var i = 0; i < 16; i++) Check(RoofBand(piece, i / 4, i % 4) == (((mask >> i) & 1) == 1), "literal mask " + piece);
        // Coverage reads the block at +3 and needs indoor, unfilled; fill reads the tile itself.
        var centre = new RoofArt(10, 0, 8, false, RoofPiece.Center, false, false, 0);
        Check(RoofCovers(true, centre, 1, 1) && !RoofCovers(false, centre, 1, 1) && !RoofCovers(true, centre with { Fill = true }, 1, 1) && !RoofCovers(true, null, 1, 1), "coverage gates");
        var north = new RoofArt(10, 0, 2, false, RoofPiece.North, false, false, 0);
        Check(RoofCovers(true, north, 0, 0) && !RoofCovers(true, north, 0, 1) && RoofFillDecision(RoofPiece.North, 0, 2) && !RoofFillDecision(RoofPiece.North, 0, 1), "offset coverage versus fill");
        // Flood on a strip: one roof east edge (small x), centre, west edge (large x), then
        // a second roof's east edge right beside it. The flood fills the first roof only.
        var strip = new Dictionary<(int X, int Y), RoofArt>
        {
            [(0, 0)] = centre with { Piece = RoofPiece.East }, [(4, 0)] = centre, [(8, 0)] = centre with { Piece = RoofPiece.West }, [(12, 0)] = centre with { Piece = RoofPiece.East },
        };
        RoofFlood(strip, (4, 0), true, -1);
        Check(strip[(0, 0)].Fill && strip[(4, 0)].Fill && strip[(8, 0)].Fill && !strip[(12, 0)].Fill, "flood stays in one roof");
        RoofFlood(strip, (12, 0), true, -1);
        Check(strip[(12, 0)].Fill, "flood from an edge itself");
        RoofFlood(strip, (8, 0), false, -1);
        Check(!strip[(0, 0)].Fill && !strip[(4, 0)].Fill && !strip[(8, 0)].Fill && strip[(12, 0)].Fill, "unfill stays in one roof");
        Check(RoofFloodStops(3, RoofPiece.North) && !RoofFloodStops(3, RoofPiece.South) && RoofFloodStops(7, RoofPiece.South) && !RoofFloodStops(-1, RoofPiece.East), "flood stop table");
        // Door admission for the player character.
        Check(AdmitPlayerDoor(0, true, false, 12, false) == DoorAdmission.Open, "plain door");
        Check(AdmitPlayerDoor(0x01, true, false, 12, false) == DoorAdmission.Locked && AdmitPlayerDoor(0x01, true, false, 12, true) == DoorAdmission.Open, "locked needs key");
        Check(AdmitPlayerDoor(0x01, true, true, 12, false) == DoorAdmission.IndoorToOutdoor && AdmitPlayerDoor(0x11, true, true, 12, false) == DoorAdmission.Locked, "leaving a building");
        Check(AdmitPlayerDoor(0x09, true, false, 12, false) == DoorAdmission.Open && AdmitPlayerDoor(0x21, true, false, 3, false) == DoorAdmission.Open && AdmitPlayerDoor(0x21, true, false, 12, false) == DoorAdmission.Locked && AdmitPlayerDoor(0x41, true, false, 12, false) == DoorAdmission.Open, "never, day and night locks");
        foreach (var (hour, day) in new[] { (6, false), (7, true), (21, true), (22, false) })
            Check(AdmitPlayerDoor(0x21, true, false, hour, false) == (day ? DoorAdmission.Locked : DoorAdmission.Open) &&
                AdmitPlayerDoor(0x41, true, false, hour, false) == (day ? DoorAdmission.Open : DoorAdmission.Locked), "day boundary " + hour);
        Check(AdmitPlayerDoor(0x83, false, false, 12, false) == DoorAdmission.Busted && AdmitPlayerDoor(0x02, false, false, 12, true) == DoorAdmission.ScriptRefused && AdmitPlayerDoor(0x06, true, true, 12, true) == DoorAdmission.Jammed && AdmitPlayerDoor(0x04, true, true, 12, true) == DoorAdmission.MagicallyHeld, "refusal order");
        // Indoor tests and edges with a portal.
        Check(TileIndoor(0) == true && TileIndoor(1u << 8) == false && TileIndoor(0xB000_0000u) == true && TileIndoor(0xB000_0000u | (1u << 25)) == false && TileIndoor(0x1000_0000u) is null, "indoor tiles");
        var edges = new[] { new EdgeObject(10, 10, 1, 5, -1), new EdgeObject(11, 10, 0, 1, 25), new EdgeObject(10, 10, 0, 7, 0), new EdgeObject(10, 11, 0, 7, 13) };
        Check(EdgeObjects(edges, (10, 10), 5).Count == 2 && EdgeSeparatesRooms(EdgeObjects(edges, (10, 10), 5)), "portal edge separates");
        Check(EdgeObjects(edges, (10, 10), 3).Count == 1 && EdgeSeparatesRooms(EdgeObjects(edges, (10, 10), 3)), "window edge separates");
        Check(!EdgeSeparatesRooms([new EdgeObject(0, 0, 0, 1, 22)]) && EdgeSeparatesRooms([new EdgeObject(0, 0, 0, 1, 0)]), "doorway piece");
        Console.WriteLine("building-self-test: PASS (portal and roof art fields, open frames, roof slots, bands, coverage, fill flood, door admission, indoor tiles, edges)");
    }
}
