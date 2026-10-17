using System.Buffers.Binary;
using System.Text.Json;
using Arcanum.Data;

namespace Arcanum.CrashSiteProbe;

// ARC-RES-047 research only: walking versus running (frame rate by Speed, ART frame
// offsets, the resulting time per tile) for the viewer's human body, and which windows
// of the start map a walker can jump through at T0. Independently authored from the
// pinned CE locators cited in docs/research/locomotion-evidence.md, checked against the
// original executable where noted there; no CE code or table is reused. No movement,
// animation or admission is implemented. Only numbers, booleans and SHA-256 digests
// leave the probe.
internal static partial class Program
{
    // The viewer's HumanFixed walk ART (docs/research/actor-visual-evidence.md).
    private const int HumanWalkOrdinal = 2668;
    private const int StartHourDay = 15;
    private const int NightHour = 2;
    private static readonly int[] SolidStopPieces = [9, 11, 12, 15, 16, 20];

    internal enum LocomotionAnim { Walk = 1, StealthWalk = 3, Run = 6 }

    /// <summary>Frames per second of a walking or running human (not dwarf or halfling)
    /// at the given Speed. <paramref name="ceSpeedEight"/> selects the pinned CE reading,
    /// where Speed 8 keeps the ART's own rate; the original executable returns the middle
    /// value instead.</summary>
    internal static int LocomotionFps(LocomotionAnim anim, int speed, int artFps, bool ceSpeedEight)
    {
        var (mid, low, high) = anim switch
        {
            LocomotionAnim.Walk => (17, 6, 30),
            LocomotionAnim.StealthWalk => (10, 4, 16),
            LocomotionAnim.Run => (20, 8, 30),
            _ => throw new ArgumentOutOfRangeException(nameof(anim)),
        };
        if (speed < 8) return low + speed * (mid - low) / 8;
        if (speed == 8) return ceSpeedEight ? artFps : mid;
        return speed < 30 ? mid + speed * (high - mid) / 30 : high;
    }

    /// <summary>Real milliseconds per frame: 1000 / fps, clamped to 30..800.</summary>
    internal static int FrameMilliseconds(int fps) => Math.Clamp(1000 / fps, 30, 800);

    /// <summary>Screen offset of one tile step in rotation 0..7 (the vector the move
    /// subtracts from the offsets when the location advances).</summary>
    internal static (int X, int Y) TileStepPixels(int rotation) => rotation switch
    {
        0 => (0, -40), 1 => (40, -20), 2 => (80, 0), 3 => (40, 20),
        4 => (0, 40), 5 => (-40, 20), 6 => (-80, 0), 7 => (-40, -20),
        _ => throw new ArgumentOutOfRangeException(nameof(rotation)),
    };

    /// <summary>Tile steps covered by one ART cycle whose frame offsets add up to
    /// (dx, dy), projected on the step direction of the rotation.</summary>
    internal static double StepsPerCycle(int rotation, int dx, int dy)
    {
        var (sx, sy) = TileStepPixels(rotation);
        return (dx * (double)sx + dy * (double)sy) / (sx * sx + sy * sy);
    }

    internal enum WindowJump { NoPortal, Busted, Open, ScriptUnknown, Jammed, MagicallyHeld, IndoorToOutdoor, Locked, SolidPiece }

    /// <summary>Whether the player character may jump a window edge, before any script runs.
    /// A solid piece on the edge stops it; with no portal on the window's tile it jumps;
    /// otherwise the jump goal's own open attempt decides as for a door, whether or not the
    /// window is already open.</summary>
    internal static WindowJump AdmitWindowJump(bool solidPiece, bool portal, uint portalFlags, bool unlockScript, bool leaving, int hour)
    {
        if (solidPiece) return WindowJump.SolidPiece;
        if (!portal) return WindowJump.NoPortal;
        if ((portalFlags & 0x80) != 0) return WindowJump.Busted;
        if (unlockScript) return WindowJump.ScriptUnknown;
        var door = AdmitPlayerDoor(portalFlags, true, leaving, hour, false);
        return door switch
        {
            DoorAdmission.Jammed => WindowJump.Jammed,
            DoorAdmission.MagicallyHeld => WindowJump.MagicallyHeld,
            DoorAdmission.IndoorToOutdoor => WindowJump.IndoorToOutdoor,
            DoorAdmission.Locked => WindowJump.Locked,
            _ => WindowJump.Open,
        };
    }

    /// <summary>Leaving a building through a closed locked portal, as the original 1.0.7.4 sees it
    /// (ARC-RES-049): both open attempts on the step must say so. Each compares the portal's tile
    /// with the tile one step along its raw rotation and leaves when the first is indoor and the
    /// second is not; it exchanges the two (the original really swaps them; CE copies one into
    /// both) when its direction is opposite the rotation or next to opposite. The traversal attempt
    /// passes the step's edge direction, so it exchanges them for a walker from across; the jump or
    /// door goal passes the direction from the walker to the portal, 5 on the portal's own tile, so
    /// a portal of rotation 0..2 is tested in reverse there and the two never agree.
    /// <paramref name="acrossIndoor"/> is the tile one step along the raw rotation: the tile across
    /// the edge for an odd rotation, the diagonal tile for an even one.</summary>
    internal static bool JumpLeaves(int portalRotation, bool fromPortalTile, bool portalTileIndoor, bool acrossIndoor)
    {
        bool Leaves(int dir) =>
            (dir + 4) % 8 == portalRotation || (dir + 5) % 8 == portalRotation || (dir + 3) % 8 == portalRotation
                ? acrossIndoor && !portalTileIndoor
                : portalTileIndoor && !acrossIndoor;
        var edge = portalRotation | 1;
        var traversal = Leaves(fromPortalTile ? edge : (edge + 4) % 8);
        var goal = Leaves(fromPortalTile ? 5 : (edge + 4) % 8);
        return traversal && goal;
    }

    internal static bool Jumps(WindowJump admission) => admission is WindowJump.NoPortal or WindowJump.Busted or WindowJump.Open or WindowJump.IndoorToOutdoor;

    /// <summary>Animation letter of a critter ART file name: 'a' + animation number.</summary>
    internal static string CritterArtForAnim(string walkName, int anim)
    {
        if (!walkName.StartsWith("art/critter/", StringComparison.OrdinalIgnoreCase) || !walkName.EndsWith("b.art", StringComparison.OrdinalIgnoreCase) || anim is < 0 or > 25)
            throw new InvalidDataException("LOCOMOTION_CRITTER_NAME");
        return walkName[..^5] + (char)('a' + anim) + ".art";
    }

    private sealed record WindowEdge(int X, int Y, int Rotation, long Sector);

    private static int ProbeLocomotion(string[] args)
    {
        const int fixedArgs = 4;
        if (args.Length != fixedArgs + CaveArchives.Length * 2 || args[0] != "--prototype-root" || args[2] != "--mobile")
            throw new ArgumentException("LOCOMOTION_USAGE");
        var archivePaths = new string[CaveArchives.Length];
        for (var i = 0; i < CaveArchives.Length; i++)
        {
            if (args[fixedArgs + i * 2] != "--" + CaveArchives[i].Role) throw new ArgumentException("LOCOMOTION_USAGE");
            archivePaths[i] = args[fixedArgs + 1 + i * 2];
        }
        var prototypeRoot = args[1]; var mobilePath = args[3];
        var before = archivePaths.Select(InputFingerprint.Create).ToArray();
        for (var i = 0; i < CaveArchives.Length; i++)
            if (before[i] != new InputFingerprint(CaveArchives[i].Bytes, CaveArchives[i].Sha256)) throw new InvalidDataException("LOCOMOTION_ARCHIVE_IDENTITY");
        var mobileBefore = InputFingerprint.Create(mobilePath);
        if (mobileBefore != new InputFingerprint(AreaMobileBytes, AreaMobileSha)) throw new InvalidDataException("LOCOMOTION_AGGREGATE_IDENTITY");

        var options = new DatReaderOptions { MaxArchiveBytes = checked((uint)CaveArchives.Max(a => a.Bytes)), MaxStoredEntryBytes = MaxDecodedSectorBytes, MaxDecodedEntryBytes = MaxDecodedSectorBytes };
        var archives = archivePaths.Select(p => DatArchive.Open(p, options)).ToArray();
        var prototypeInputs = new Dictionary<string, InputFingerprint>(StringComparer.Ordinal);
        try
        {
            // 1. Human walk, run and vault ART: rate, frames, action frame, offsets per cycle.
            (int Archive, DatEntry Entry, byte[] Bytes) Unique(string path)
            {
                var hits = archives.SelectMany((a, i) => a.Entries.Where(e => AreaName(e).Equals(path, StringComparison.OrdinalIgnoreCase)).Select(e => (Archive: i, Entry: e))).ToArray();
                if (hits.Length != 1) throw new InvalidDataException("LOCOMOTION_RESOURCE_MISSING_OR_AMBIGUOUS");
                return (hits[0].Archive, hits[0].Entry, archives[hits[0].Archive].ReadEntry(hits[0].Entry));
            }
            var walkEntries = archives[1].Entries.Where(e => e.Ordinal == HumanWalkOrdinal).ToArray();
            if (walkEntries.Length != 1) throw new InvalidDataException("LOCOMOTION_WALK_ORDINAL");
            var walkName = AreaName(walkEntries[0]);
            object ArtRow(string label, int anim)
            {
                var (index, entry, bytes) = Unique(CritterArtForAnim(walkName, anim));
                var meta = ArtDecoder.Inspect(bytes, new ArtDecoderOptions { MaxInputBytes = MaxDecodedSectorBytes });
                var frames = meta.Header.FramesPerGroup; var fps = meta.Header.FramesPerSecond;
                var groups = Enumerable.Range(0, meta.Header.PhysicalGroupCount).Select(g =>
                {
                    var dx = 0; var dy = 0;
                    for (var f = 0; f < frames; f++) { var frame = meta.GetFrame(g, f); dx += frame.OffsetX; dy += frame.OffsetY; }
                    var steps = StepsPerCycle(g, dx, dy);
                    return new { group = g, dx, dy, tile_steps_per_cycle = Math.Round(steps, 4) };
                }).ToArray();
                return new { label, anim, archive = CaveArchives[index].Role, entry.Ordinal, bytes = bytes.Length, sha256 = AreaHash(bytes), frames_per_group = frames, physical_groups = meta.Header.PhysicalGroupCount, art_fps = fps, action_frame = meta.Header.ActionFrame, groups };
            }
            var walk = ArtRow("walk", 1); var run = ArtRow("run", 6); var vault = ArtRow("vault", 13);
            JsonElement Element(object value) => JsonSerializer.SerializeToElement(value);
            var walkJson = Element(walk); var runJson = Element(run); var vaultJson = Element(vault);
            object Timing(JsonElement art, LocomotionAnim anim, int speed, bool ce)
            {
                var frames = art.GetProperty("frames_per_group").GetInt32(); var artFps = art.GetProperty("art_fps").GetInt32();
                var fps = LocomotionFps(anim, speed, artFps, ce); var ms = FrameMilliseconds(fps);
                var perRotation = art.GetProperty("groups").EnumerateArray().Select(g =>
                {
                    var steps = g.GetProperty("tile_steps_per_cycle").GetDouble();
                    return new { rotation = g.GetProperty("group").GetInt32(), ms_per_tile = steps > 0 ? Math.Round(frames * ms / steps, 1) : (double?)null };
                }).ToArray();
                return new { anim = anim.ToString(), speed, ce_reading = ce, fps, frame_ms = ms, cycle_ms = frames * ms, per_rotation = perRotation };
            }
            var timings = new[] { 7, 8, 9, 10, 12 }.SelectMany(speed => new[]
            {
                Timing(walkJson, LocomotionAnim.Walk, speed, false), Timing(runJson, LocomotionAnim.Run, speed, false),
            }).Concat([Timing(walkJson, LocomotionAnim.Walk, 8, true), Timing(runJson, LocomotionAnim.Run, 8, true)]).ToArray();
            var vaultFrames = vaultJson.GetProperty("frames_per_group").GetInt32(); var vaultFps = vaultJson.GetProperty("art_fps").GetInt32();
            var vaultTiming = new { frame_ms = 1000 / vaultFps, total_ms = vaultFrames * (1000 / vaultFps), action_frame = vaultJson.GetProperty("action_frame").GetInt32() };

            // 2. Every exterior sector: window wall pieces, portals on their tiles, admission.
            var module = archives[0];
            if (archives.Skip(1).Any(a => a.Entries.Any(e => AreaName(e).StartsWith(ExteriorSectorPrefix, StringComparison.OrdinalIgnoreCase))))
                throw new InvalidDataException("LOCOMOTION_SECTOR_SHADOWED");
            var sectorEntries = module.Entries.Where(e => AreaName(e).StartsWith(ExteriorSectorPrefix, StringComparison.Ordinal) && AreaName(e).EndsWith(".sec", StringComparison.Ordinal)).OrderBy(e => e.Ordinal).ToArray();
            if (sectorEntries.Length != 621) throw new InvalidDataException("LOCOMOTION_SURVEY_SCOPE_CHANGED");
            var tiles = new Dictionary<(int X, int Y), uint[]>();
            var walls = new List<(int X, int Y, CaveWallArt Art, long Sector)>();
            var portals = new List<(int X, int Y, PortalArt Art, uint Flags, bool UnlockScript, long Sector)>();
            foreach (var entry in sectorEntries)
            {
                var id = long.Parse(Path.GetFileNameWithoutExtension(AreaName(entry)), System.Globalization.CultureInfo.InvariantCulture);
                var sx = checked((int)(id % 67108864)); var sy = checked((int)(id / 67108864));
                var bytes = module.ReadEntry(entry); var slice = SectorSlice.Parse(bytes, false); var framing = SectorFraming.Parse(bytes, slice.LightCount);
                var tileWords = new uint[64 * 64];
                for (var i = 0; i < tileWords.Length; i++) tileWords[i] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(framing.TileStart + 4 * i));
                tiles[(sx, sy)] = tileWords;
                var instances = ObjectWire.ParseWindow(bytes.AsSpan(framing.ObjectStart, framing.ObjectBytes), slice.TrailingInt32).Where(i => i.Type is 0 or 1).ToArray();
                if (instances.Length == 0) continue;
                var prototypes = ObjectWire.ReadReferencedPrototypesFromDirectory(prototypeRoot, instances.Select(v => v.PrototypeId).ToArray());
                Remember(prototypes, prototypeInputs);
                var objectWindow = bytes.AsSpan(framing.ObjectStart).ToArray();
                foreach (var value in ObjectWire.ResolveEffectiveInstances(instances, prototypes.Prototypes))
                {
                    var proto = prototypes.Prototypes.Single(p => p.Id.Matches(value.Instance.PrototypeId));
                    if (value.Instance.Location is not { } loc) throw new InvalidDataException("LOCOMOTION_LOCATION_ABSENT");
                    var (wx, wy) = RebaseSectorLocation(loc, sx, sy);
                    if (value.Instance.Type == 0) { walls.Add((wx, wy, DecodeWallArt((uint)value.CurrentAid), id)); continue; }
                    var numeric = EffectiveNumeric(value.Instance, proto);
                    if (!numeric.TryGetValue(PortalFlagsField, out var flags)) throw new InvalidDataException("LOCOMOTION_PORTAL_FLAGS_ABSENT");
                    var unlock = EffectiveScripts(objectWindow, value.Instance, proto).Any(s => s.sap == 3 && s.script != 0);
                    portals.Add((wx, wy, DecodePortalArt((uint)value.CurrentAid), (uint)flags, unlock, id));
                }
            }
            var exterior = FrameAggregate(File.ReadAllBytes(mobilePath), AreaMobileBytes);
            if (exterior.Any(r => r.Instance.Type is 0 or 1)) throw new InvalidDataException("LOCOMOTION_AGGREGATE_WALLS");
            bool? Indoor(int x, int y) => tiles.TryGetValue((x >> 6, y >> 6), out var words) ? TileIndoor(words[(x & 63) + 64 * (y & 63)]) : null;

            // A window edge is a cardinal edge with a window piece on either side of it.
            var edges = new Dictionary<(int X, int Y, int Rotation), long>();
            foreach (var w in walls.Where(w => WindowWallPieces.Contains(w.Art.Piece)))
            {
                var key = (w.X, w.Y, w.Art.EdgeRotation);
                var across = CaveStep((w.X, w.Y), w.Art.EdgeRotation)!.Value;
                var back = (across.X, across.Y, (w.Art.EdgeRotation + 4) % 8);
                if (!edges.ContainsKey(back)) edges.TryAdd(key, w.Sector);
            }
            var rows = edges.Select(kv =>
            {
                var (x, y, r) = kv.Key;
                var across = CaveStep((x, y), r)!.Value;
                var sides = walls.Where(w => (w.X == x && w.Y == y && w.Art.EdgeRotation == r) || (w.X == across.X && w.Y == across.Y && w.Art.EdgeRotation == (r + 4) % 8)).ToArray();
                var solid = sides.Any(w => SolidStopPieces.Contains(w.Art.Piece));
                // The jump takes the first portal on the tile that holds the window piece.
                var windowTile = sides.First(w => WindowWallPieces.Contains(w.Art.Piece));
                var tilePortals = portals.Where(p => p.X == windowTile.X && p.Y == windowTile.Y).ToArray();
                var onEdge = portals.Where(p => (p.X == x && p.Y == y && p.Art.EdgeRotation == r) || (p.X == across.X && p.Y == across.Y && p.Art.EdgeRotation == (r + 4) % 8)).ToArray();
                var portal = tilePortals.FirstOrDefault();
                var hasPortal = tilePortals.Length > 0;
                // Leaving, as the traversal and the jump goal both see it (JumpLeaves). The portal is on the
                // window's tile, so the window side is the portal side.
                bool Leaving(bool fromWindowTile)
                {
                    if (!hasPortal) return false;
                    if (portal.Art.Rotation != portal.Art.EdgeRotation) throw new InvalidDataException("LOCOMOTION_PORTAL_EDGE_NOT_ADMITTED");
                    var portalAcross = CaveStep((portal.X, portal.Y), portal.Art.Rotation)!.Value;
                    var here = Indoor(portal.X, portal.Y) ?? throw new InvalidDataException("LOCOMOTION_PORTAL_TILE_UNKNOWN");
                    var there = Indoor(portalAcross.X, portalAcross.Y) ?? throw new InvalidDataException("LOCOMOTION_PORTAL_TILE_UNKNOWN");
                    return JumpLeaves(portal.Art.Rotation, fromWindowTile, here, there);
                }
                WindowJump Admit(bool fromWindowTile, int hour) => AdmitWindowJump(solid, hasPortal, hasPortal ? portal.Flags : 0, hasPortal && portal.UnlockScript, Leaving(fromWindowTile), hour);
                return new
                {
                    x, y, rotation = r, across_x = across.X, across_y = across.Y, sector = kv.Value,
                    pieces = sides.Select(w => w.Art.Piece).Order().ToArray(), solid,
                    here_indoor = Indoor(x, y), across_indoor = Indoor(across.X, across.Y),
                    tile_portals = tilePortals.Length, edge_portals = onEdge.Length,
                    portal_door = hasPortal ? portal.Art.Door : (bool?)null, portal_flags = hasPortal ? portal.Flags : (uint?)null, portal_open = hasPortal ? portal.Art.Frame != 0 : (bool?)null,
                    day_here = Admit(true, StartHourDay).ToString(), day_across = Admit(false, StartHourDay).ToString(),
                    night_here = Admit(true, NightHour).ToString(), night_across = Admit(false, NightHour).ToString(),
                    jumpable_day = Jumps(Admit(true, StartHourDay)) || Jumps(Admit(false, StartHourDay)),
                    distance_from_start = Math.Max(Math.Abs(x - StartTileX), Math.Abs(y - StartTileY)),
                };
            }).OrderBy(r => r.distance_from_start).ThenBy(r => r.y).ThenBy(r => r.x).ToArray();
            var jumpable = rows.Where(r => r.jumpable_day).ToArray();
            var house = rows.Where(r => r.sector == BuildingSectorId).ToArray();

            // ARC-RES-049: closed locked doors whose lock the leaving shortcut can skip (not busted, jammed, held,
            // never-locked or always-locked, no unlock script), by the original's leaving test from each side, against the pinned CE
            // reading (no leaving from across; on the portal's own tile rotation 0..2 never). The test steps by the raw
            // rotation, so an even rotation reads the diagonal tile.
            static (int X, int Y) Along((int X, int Y) c, int r) => r switch
            {
                0 => (c.X - 1, c.Y - 1), 1 => (c.X - 1, c.Y), 2 => (c.X - 1, c.Y + 1), 3 => (c.X, c.Y + 1),
                4 => (c.X + 1, c.Y + 1), 5 => (c.X + 1, c.Y), 6 => (c.X + 1, c.Y - 1), _ => (c.X, c.Y - 1),
            };
            var lockedDoors = portals.Where(p => p.Art.Door && p.Art.Frame == 0 && !p.Art.Busted && !p.UnlockScript && (p.Flags & 0x1) != 0 && (p.Flags & 0x9E) == 0)
                .Select(p => (p.Art.Rotation, Here: Indoor(p.X, p.Y), Along: Indoor(Along((p.X, p.Y), p.Art.Rotation).X, Along((p.X, p.Y), p.Art.Rotation).Y))).ToArray();
            var resolvedDoors = lockedDoors.Where(d => d.Here is not null && d.Along is not null).Select(d => (d.Rotation, Here: d.Here!.Value, Along: d.Along!.Value)).ToArray();
            var doors = new
            {
                locked = lockedDoors.Length, unresolved = lockedDoors.Length - resolvedDoors.Length,
                by_rotation = resolvedDoors.GroupBy(d => d.Rotation).OrderBy(g => g.Key).ToDictionary(g => g.Key.ToString(), g => g.Count()),
                leaving_from_portal_tile = resolvedDoors.Count(d => JumpLeaves(d.Rotation, true, d.Here, d.Along)),
                leaving_from_across = resolvedDoors.Count(d => JumpLeaves(d.Rotation, false, d.Here, d.Along)),
                ce_leaving_from_portal_tile = resolvedDoors.Count(d => d.Rotation > 2 && d.Here && !d.Along),
                portal_tile_indoor_rotation_0_to_2 = resolvedDoors.Count(d => d.Rotation <= 2 && d.Here && !d.Along),
            };

            var after = archivePaths.Select(InputFingerprint.Create).ToArray();
            if (!before.SequenceEqual(after) || InputFingerprint.Create(mobilePath) != mobileBefore) throw new InvalidDataException("LOCOMOTION_INPUT_CHANGED");
            foreach (var (path, fingerprint) in prototypeInputs)
                if (InputFingerprint.Create(path) != fingerprint) throw new InvalidDataException("LOCOMOTION_PROTOTYPE_CHANGED");

            var report = new
            {
                task = "ARC-RES-047",
                inputs = new { archives = before, mobile = mobileBefore, prototypes = prototypeInputs.Count, prototype_digest = PrototypeDigest(prototypeInputs.Values) },
                art = new { walk, run, vault },
                timing = timings,
                vault_timing = vaultTiming,
                windows = new
                {
                    sectors = sectorEntries.Length, walls = walls.Count, portals = portals.Count, window_pieces = walls.Count(w => WindowWallPieces.Contains(w.Art.Piece)),
                    window_portals = portals.Count(p => !p.Art.Door), window_edges = rows.Length,
                    admission_day_here = rows.GroupBy(r => r.day_here).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count()),
                    admission_day_across = rows.GroupBy(r => r.day_across).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count()),
                    admission_night_here = rows.GroupBy(r => r.night_here).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count()),
                    tile_portal_counts = rows.GroupBy(r => r.tile_portals).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
                    edge_portal_counts = rows.GroupBy(r => r.edge_portals).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
                    portal_flags = rows.Where(r => r.portal_flags is not null).GroupBy(r => r.portal_flags!.Value).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
                    jumpable_day = jumpable.Length,
                    jumpable_sectors = jumpable.Select(r => r.sector).Distinct().Count(),
                    nearest_jumpable = jumpable.Take(5).ToArray(),
                    house_sector = new { sector = BuildingSectorId, edges = house },
                },
                locked_doors = doors,
            };
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine(json);
            Console.Error.WriteLine("locomotion report sha256 " + AreaHash(System.Text.Encoding.UTF8.GetBytes(json)));
            return 0;
        }
        finally
        {
            foreach (var archive in archives) archive.Dispose();
        }
    }

    private static void LocomotionSelfTest()
    {
        static void Check(bool condition, string label) { if (!condition) throw new InvalidDataException("LOCOMOTION_SELF_TEST " + label); }
        // Rates: below 8 interpolate from the low value, 8 is the middle value (CE: ART
        // rate), above 8 add Speed/30 of the upper span, 30 and more the high value.
        Check(LocomotionFps(LocomotionAnim.Walk, 0, 99, false) == 6 && LocomotionFps(LocomotionAnim.Walk, 4, 99, false) == 11 && LocomotionFps(LocomotionAnim.Walk, 7, 99, false) == 15, "walk below 8");
        Check(LocomotionFps(LocomotionAnim.Walk, 8, 99, false) == 17 && LocomotionFps(LocomotionAnim.Walk, 8, 99, true) == 99, "speed 8 original and CE");
        Check(LocomotionFps(LocomotionAnim.Walk, 9, 99, false) == 20 && LocomotionFps(LocomotionAnim.Walk, 29, 99, false) == 29 && LocomotionFps(LocomotionAnim.Walk, 30, 99, false) == 30 && LocomotionFps(LocomotionAnim.Walk, 90, 99, false) == 30, "walk above 8");
        Check(LocomotionFps(LocomotionAnim.Run, 7, 0, false) == 18 && LocomotionFps(LocomotionAnim.Run, 8, 0, false) == 20 && LocomotionFps(LocomotionAnim.Run, 9, 0, false) == 23 && LocomotionFps(LocomotionAnim.Run, 30, 0, false) == 30, "run");
        Check(LocomotionFps(LocomotionAnim.StealthWalk, 8, 0, false) == 10 && LocomotionFps(LocomotionAnim.StealthWalk, 15, 0, false) == 13, "stealth");
        Check(FrameMilliseconds(17) == 58 && FrameMilliseconds(40) == 30 && FrameMilliseconds(1) == 800, "frame clamp");
        // One tile step per rotation, and the projection of a cycle on it.
        Check(TileStepPixels(1) == (40, -20) && TileStepPixels(6) == (-80, 0) && Enumerable.Range(0, 8).All(r => TileStepPixels(r) == (-TileStepPixels((r + 4) % 8).X, -TileStepPixels((r + 4) % 8).Y)), "tile steps");
        Check(StepsPerCycle(1, 80, -40) == 2 && StepsPerCycle(2, 80, 0) == 1 && StepsPerCycle(3, -40, -20) == -1 && StepsPerCycle(0, 0, -20) == 0.5, "steps per cycle");
        // Window admission order.
        Check(AdmitWindowJump(true, false, 0, false, false, 15) == WindowJump.SolidPiece, "solid first");
        Check(AdmitWindowJump(false, false, 0x02, true, false, 15) == WindowJump.NoPortal, "no portal");
        Check(AdmitWindowJump(false, true, 0x82, true, false, 15) == WindowJump.Busted && AdmitWindowJump(false, true, 0x02, true, false, 15) == WindowJump.ScriptUnknown, "busted, then script");
        Check(AdmitWindowJump(false, true, 0x02, false, false, 15) == WindowJump.Jammed && AdmitWindowJump(false, true, 0x04, false, false, 15) == WindowJump.MagicallyHeld, "jammed, held");
        Check(AdmitWindowJump(false, true, 0x01, false, true, 15) == WindowJump.IndoorToOutdoor && AdmitWindowJump(false, true, 0x01, false, false, 15) == WindowJump.Locked, "lock and leaving");
        Check(AdmitWindowJump(false, true, 0x21, false, false, 2) == WindowJump.Open && AdmitWindowJump(false, true, 0x21, false, false, 15) == WindowJump.Locked, "locked by day");
        Check(Jumps(WindowJump.NoPortal) && Jumps(WindowJump.Open) && Jumps(WindowJump.IndoorToOutdoor) && !Jumps(WindowJump.Jammed) && !Jumps(WindowJump.ScriptUnknown) && !Jumps(WindowJump.SolidPiece), "jumps");
        Check(!JumpLeaves(1, true, true, false) && JumpLeaves(3, true, true, false) && JumpLeaves(5, true, true, false) && JumpLeaves(7, true, true, false), "leaving from the portal tile");
        Check(!JumpLeaves(3, false, true, false) && !JumpLeaves(5, false, true, false) && !JumpLeaves(5, true, false, false) && !JumpLeaves(5, true, true, true), "not indoor to outdoor");
        // ARC-RES-049: from across the original exchanges the tiles, so a walker leaving from the far side passes;
        // on the portal's own tile rotation 1 passes the reversed goal test only, which the traversal refuses.
        Check(JumpLeaves(1, false, false, true) && JumpLeaves(3, false, false, true) && JumpLeaves(7, false, false, true) && !JumpLeaves(1, true, false, true), "leaving from across");
        Check(CritterArtForAnim("art/critter/x/xyzb.art", 6) == "art/critter/x/xyzg.art" && CritterArtForAnim("art/critter/x/xyzb.art", 13) == "art/critter/x/xyzn.art", "anim letter");
        Console.WriteLine("locomotion-self-test: PASS (speed frame rates, frame clamp, tile steps, cycle projection, window admission, jump leaving direction (original), anim letters)");
    }
}
