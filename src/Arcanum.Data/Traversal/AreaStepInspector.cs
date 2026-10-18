using Arcanum.Data.Content;
using System.Security.Cryptography;
using System.Text;
using Arcanum.Data.Area;
using Arcanum.Data.Terrain;

namespace Arcanum.Data.Traversal;

/// <summary>Construction of whole-core T0 step evidence from a strict Area snapshot and explicit rule sources.</summary>
public static class AreaStepInspector
{
    private const string OriginalAreaProfile = "arc-data-009-complete-file-effective-source-v1";
    private const string OriginalCaveProfile = "arc-world-009-cave-file-effective-source-v1";
    // Window/opening wall pieces (docs/research/cave-transition-evidence.md T7); every other piece is a solid edge.
    private static readonly int[] OpeningWallPieces = [10, 13, 14, 17, 18, 19, 22, 25, 26, 29, 30, 31, 32];
    private const string TileNamePath = "art/tile/tilename.mes", EffectPath = "rules/effect.mes", GameEffectPath = "rules/gameeffect.mes", PatchPath = "rules/map.patch";
    // Pinned payload identities of the authenticated original profile (docs/research/area-passability-closure.md).
    private static readonly IReadOnlyDictionary<string, (long Length, string Sha256)> OriginalPayloads = new Dictionary<string, (long, string)>
    {
        [TileNamePath] = (2999, "1edd402a3d7b786c01e0500f1534c76525cae1cd83fc67df274afd036d6a9627"),
        [EffectPath] = (17497, "8ac43304c1e3ff2b351124acb7403c5de79165963f6ea0a83fa4c4d3bdb0b6b2"),
        [GameEffectPath] = (417, "f8c921b72da9b004aedf9a34c8a1d4f6bbaa86abd1168d234cd811b66ead0a85"),
        [PatchPath] = (5779, "2bed0dfca7dc6620c84a1f5aea286a392e98adf6de9f7b6296fcc2d5a3cadf28"),
    };
    private const int ObjectTypeFlagsSlot = 18, HpPointsSlot = 26, HpAdjustmentSlot = 27, HpDamageSlot = 28, StatBaseSlot = 38, EffectsSlot = 46;
    private const uint FlagDestroyedOrOff = 0x3, FlagNoBlock = 0x400, FlagInventory = 0x1000, SpellPassWalled = 0x100000;
    private const int SpellFlagsSlot = 19;

    /// <summary>Structural construction. Every archive is caller-owned and only read. The patch archive is optional
    /// (absent means no patch source in this structural selection); every other rule resource must occur exactly once.</summary>
    public static AreaStepSnapshot Inspect(AreaSnapshot area, DatArchive module, DatArchive tileNameArchive, DatArchive effectArchive,
        DatArchive? patchArchive, ReadOnlyMemory<byte> mobileFile, long evidenceEpoch)
    {
        ArgumentNullException.ThrowIfNull(area); ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(tileNameArchive); ArgumentNullException.ThrowIfNull(effectArchive);
        if (evidenceEpoch < 0) throw new ArgumentOutOfRangeException(nameof(evidenceEpoch));
        var stage = new StageCursor();
        return Sanitize(stage, () => Build(stage, AreaStepInputKind.Structural, area, module, ArchiveRules(module, tileNameArchive, effectArchive, patchArchive, null),
            mobileFile, evidenceEpoch));
    }

    /// <summary>Whole-map evidence (ARC-WORLD-018) for a strictly published whole-map snapshot
    /// (ARC-DATA-019): the sectors from the module archive, the rule resources through the content
    /// stack in the phase the original reads them (ARC-DATA-027, exe-checked: tile names and effects
    /// at game start, so from the first base source; the module's game effects at module load and
    /// map.patch when a map opens, so the stack winners; a loose file is read like an archive member),
    /// and the mobile file from the map's aggregate. Walls and portals use the whole-map
    /// edge rules (<see cref="WallPieceRule"/>, <see cref="PortalEdgeRule"/>).</summary>
    public static AreaStepSnapshot InspectWholeMap(AreaSnapshot area, ContentStack stack, string? aggregatePath, long evidenceEpoch)
    {
        ArgumentNullException.ThrowIfNull(area); ArgumentNullException.ThrowIfNull(stack);
        if (evidenceEpoch < 0) throw new ArgumentOutOfRangeException(nameof(evidenceEpoch));
        var stage = new StageCursor();
        return Sanitize(stage, () =>
        {
            if (!area.Profile.StartsWith(WholeMapProfilePrefix, StringComparison.Ordinal)) throw Fail(AreaStepInvocationCode.InvalidSelection, AreaStepInvocationStage.Selection);
            var module = stack.Plan.SearchOrder.SingleOrDefault(s => s.Role == ContentSourceRole.ModuleArchive) ?? throw Fail(AreaStepInvocationCode.InvalidSelection, AreaStepInvocationStage.Selection);
            byte[]? Rule(string path, bool required)
            {
                var phase = path is TileNamePath or EffectPath ? ContentLoadPhase.GameStart : ContentLoadPhase.ModuleLoaded;
                if (ContentPhase.Candidate(stack, path, phase) is not { } candidate)
                    return required ? throw Fail(AreaStepInvocationCode.InvalidSelection, AreaStepInvocationStage.Rules) : null;
                if (candidate.Length > AreaStepRules.MaxMesBytes) throw Fail(AreaStepInvocationCode.LimitExceeded, AreaStepInvocationStage.Rules);
                var read = stack.Read(candidate);
                if (read.Status == ContentReadStatus.TooLarge || read.Bytes?.Length > AreaStepRules.MaxMesBytes) throw Fail(AreaStepInvocationCode.LimitExceeded, AreaStepInvocationStage.Rules);
                return read.Status == ContentReadStatus.Read ? read.Bytes! : throw Fail(AreaStepInvocationCode.InputUnavailable, AreaStepInvocationStage.Rules);
            }
            var mobile = Array.Empty<byte>();
            if (aggregatePath is not null)
            {
                var winner = stack.Resolve(aggregatePath).Winner ?? throw Fail(AreaStepInvocationCode.InputUnavailable, AreaStepInvocationStage.Selection);
                var read = stack.Read(winner);
                mobile = read.Status == ContentReadStatus.Read ? read.Bytes! : throw Fail(AreaStepInvocationCode.InputUnavailable, AreaStepInvocationStage.Selection);
            }
            using var moduleArchive = DatArchive.Open(module.Path);
            return Build(stage, AreaStepInputKind.Structural, area, moduleArchive, Rule, mobile, evidenceEpoch);
        });
    }

    /// <summary>Authenticated original Crash Site profile: the strict original Area snapshot, its nine core sectors
    /// (or the cave profile's single sector (32,31)), and pinned tile-name, effect, module-effect and map.patch
    /// payloads read from the explicitly named archives.</summary>
    public static AreaStepSnapshot ValidateOriginalData(AreaSnapshot area, string modulePath, string tileNameResourcePath,
        string effectResourcePath, string patchArchivePath, string mobilePath, long evidenceEpoch)
    {
        ArgumentNullException.ThrowIfNull(area);
        ArgumentException.ThrowIfNullOrEmpty(modulePath); ArgumentException.ThrowIfNullOrEmpty(tileNameResourcePath);
        ArgumentException.ThrowIfNullOrEmpty(effectResourcePath); ArgumentException.ThrowIfNullOrEmpty(patchArchivePath);
        ArgumentException.ThrowIfNullOrEmpty(mobilePath);
        if (evidenceEpoch < 0) throw new ArgumentOutOfRangeException(nameof(evidenceEpoch));
        var stage = new StageCursor();
        return Sanitize(stage, () =>
        {
            if (area.Profile is not (OriginalAreaProfile or OriginalCaveProfile)) throw Fail(AreaStepInvocationCode.InvalidSelection, AreaStepInvocationStage.Selection);
            var core = area.Sectors.Where(s => s.Scope == AreaScope.Core && s.Present).Select(s => (s.Coordinate.X, s.Coordinate.Y)).ToHashSet();
            var expected = area.Profile == OriginalCaveProfile ? new HashSet<(long, long)> { (32, 31) }
                : (from x in new long[] { 1451, 1452, 1453 } from y in new long[] { 1289, 1290, 1291 } select (x, y)).ToHashSet();
            if (!core.SetEquals(expected)) throw Fail(AreaStepInvocationCode.InvalidSelection, AreaStepInvocationStage.Selection);
            // Bound the read by the authenticated Area identity before reading the whole mobile file.
            var mobileInput = area.Inputs.SingleOrDefault(i => i.Key.Role == AreaSourceRole.NormalGameMobile)
                ?? throw Fail(AreaStepInvocationCode.InvalidSelection, AreaStepInvocationStage.Selection);
            if (new FileInfo(mobilePath).Length != mobileInput.Length) throw Fail(AreaStepInvocationCode.FingerprintMismatch, AreaStepInvocationStage.Fingerprint);
            var mobile = File.ReadAllBytes(mobilePath);
            using var module = DatArchive.Open(modulePath);
            using var names = DatArchive.Open(tileNameResourcePath);
            using var effects = DatArchive.Open(effectResourcePath);
            using var patch = DatArchive.Open(patchArchivePath);
            var snapshot = Build(stage, AreaStepInputKind.AuthenticatedOriginal, area, module, ArchiveRules(module, names, effects, patch, null), mobile, evidenceEpoch);
            foreach (var (path, pin) in OriginalPayloads)
                if (!snapshot.InputIdentity.Sources.Any(s => s.Role == path && s.Length == pin.Length && s.Sha256 == pin.Sha256))
                    throw Fail(AreaStepInvocationCode.FingerprintMismatch, AreaStepInvocationStage.Fingerprint);
            return snapshot;
        });
    }

    /// <summary>The rule resources of the pinned and structural profiles: each one exactly once in its named archive (the
    /// game effects in the module's, unless given); a missing optional resource or archive is null.</summary>
    private static Func<string, bool, byte[]?> ArchiveRules(DatArchive module, DatArchive tileNameArchive, DatArchive effectArchive,
        DatArchive? patchArchive, DatArchive? gameEffectArchive) => (path, required) =>
    {
        var archive = path switch
        {
            TileNamePath => tileNameArchive, EffectPath => effectArchive, GameEffectPath => gameEffectArchive ?? module, PatchPath => patchArchive,
            _ => throw new ArgumentOutOfRangeException(nameof(path)),
        };
        if (archive is null) return null;
        var hits = archive.Entries.Where(e => Name(e) == path).ToArray();
        if (hits.Length == 0 && !required) return null;
        if (hits.Length != 1) throw Fail(AreaStepInvocationCode.InvalidSelection, AreaStepInvocationStage.Rules);
        if (hits[0].DeclaredOutputSize > AreaStepRules.MaxMesBytes) throw Fail(AreaStepInvocationCode.LimitExceeded, AreaStepInvocationStage.Rules);
        return archive.ReadEntry(hits[0]);
    };

    private static AreaStepSnapshot Build(StageCursor stage, AreaStepInputKind kind, AreaSnapshot area, DatArchive module,
        Func<string, bool, byte[]?> readRule, ReadOnlyMemory<byte> mobileFile, long epoch)
    {
        stage.Current = AreaStepInvocationStage.Selection;
        // Only a strict-complete publication: an inspected snapshot with issues may omit unlocated objects.
        if (!area.StrictlyPublished) throw Fail(AreaStepInvocationCode.InvalidSelection, AreaStepInvocationStage.Selection);
        if (area.Inputs.GroupBy(i => i.Key).Any(g => g.Count() != 1)) throw Fail(AreaStepInvocationCode.InvalidSelection, AreaStepInvocationStage.Selection);
        var sectors = area.Sectors.Where(s => s.Scope == AreaScope.Core && s.Present).ToArray();
        if (sectors.Length == 0) throw Fail(AreaStepInvocationCode.InvalidSelection, AreaStepInvocationStage.Selection);
        var moduleInput = area.Inputs.SingleOrDefault(i => i.Key.Role == AreaSourceRole.Module) ?? throw Fail(AreaStepInvocationCode.InvalidSelection, AreaStepInvocationStage.Selection);
        var mobileInput = area.Inputs.SingleOrDefault(i => i.Key.Role == AreaSourceRole.NormalGameMobile);
        var mobileSha = Sha(mobileFile.Span);
        if (mobileInput is not null && (mobileInput.Length != mobileFile.Length || mobileInput.Sha256 != mobileSha))
            throw Fail(AreaStepInvocationCode.FingerprintMismatch, AreaStepInvocationStage.Fingerprint);

        // Rule sources: each consumed resource exactly once in its named archive; module effects and the patch are optional.
        // Area inputs are bound by their authenticated identities; the opened archives are bound by payload hashes below.
        var sources = area.Inputs.Select(i => new AreaStepSourceIdentity($"area-{i.Key.Role}-{i.Key.Number}".ToLowerInvariant(), i.Length, i.Sha256)).ToList();
        sources.Add(new("mobile-file", mobileFile.Length, mobileSha));
        stage.Current = AreaStepInvocationStage.Rules;
        byte[]? Resource(string path, bool required)
        {
            var bytes = readRule(path, required);
            if (bytes is null) return required ? throw Fail(AreaStepInvocationCode.InvalidSelection, AreaStepInvocationStage.Rules) : null;
            sources.Add(new(path, bytes.Length, Sha(bytes)));
            return bytes;
        }
        var tileTable = AreaStepRules.TileNameTable(AreaStepRules.ParseMes(Resource(TileNamePath, true)));
        var effects = new Dictionary<int, IReadOnlyList<AreaStepRules.EffectChange>?>();
        foreach (var (key, value) in AreaStepRules.ParseMes(Resource(EffectPath, true)).Where(r => r.Key is >= 50 and < 400))
            effects[key] = AreaStepRules.ParseEffect(value);
        if (Resource(GameEffectPath, false) is { } gameEffects)
            foreach (var (key, value) in AreaStepRules.ParseMes(gameEffects).Where(r => r.Key is >= 0 and < 50))
                effects[key] = AreaStepRules.ParseEffect(value);
        IReadOnlyList<AreaStepRules.PatchTarget> patchTargets = [];
        var patchMalformed = 0;
        if (Resource(PatchPath, false) is { } patch)
        {
            // ARC-DATA-028: a whole-map plan's owner applied the patch to the objects; only lines it could not model are a gap.
            if (area.MapPatch is { } applied) patchMalformed = applied.HasUnmodelledLines ? 1 : 0;
            else patchTargets = AreaStepRules.PatchTargets(Encoding.Latin1.GetString(patch), out patchMalformed);
        }

        stage.Current = AreaStepInvocationStage.Terrain;
        var terrain = new Dictionary<AreaStepCell, AreaStepTerrainEvidence>();
        var terrainByCell = area.Terrain.GroupBy(t => t.Key.Sector).ToDictionary(g => g.Key, g => g.ToArray());
        foreach (var sector in sectors)
        {
            var entries = module.Entries.Where(e => e.Ordinal == sector.DatOrdinal).ToArray();
            if (entries.Length != 1 || sector.PayloadSha256 is null) throw Fail(AreaStepInvocationCode.InvalidSelection, AreaStepInvocationStage.Terrain);
            var entry = entries[0];
            if (Sha(module.ReadEntry(entry)) != sector.PayloadSha256) throw Fail(AreaStepInvocationCode.FingerprintMismatch, AreaStepInvocationStage.Terrain);
            var selector = new TerrainSectorSelector(Encoding.ASCII.GetString(entry.RawNameBytes.ToArray()).TrimEnd('\0'), "area",
                checked((int)sector.Coordinate.X), checked((int)sector.Coordinate.Y), sector.Key.Packed,
                checked((int)sector.Coordinate.OriginX), checked((int)sector.Coordinate.OriginY), 64, 64,
                new(entry.Ordinal, entry.Flags, entry.DeclaredOutputSize));
            var report = TerrainObstructionInspector.InspectSector(module, "AREA_MODULE", selector, null);
            var words = report.CopyTileWords(); var mask = report.CopyBlockWords();
            var records = terrainByCell.GetValueOrDefault(sector.Key, []);
            if (records.Length != 4096 || records.Select(r => r.LocalX + 64 * r.LocalY).Distinct().Count() != 4096 ||
                records.Any(r => r.LocalX is < 0 or > 63 || r.LocalY is < 0 or > 63 || words[r.LocalX + 64 * r.LocalY] != r.RawArt ||
                    r.WorldX != sector.Coordinate.OriginX + r.LocalX || r.WorldY != sector.Coordinate.OriginY + r.LocalY))
                throw Fail(AreaStepInvocationCode.FingerprintMismatch, AreaStepInvocationStage.Terrain);
            // The block list is serialized only from sector version 0xAA0004; older versions keep the zeroed cache list.
            var maskConsistent = (mask is not null) == (sector.Placeholder >= 0xAA0004);
            foreach (var r in records)
            {
                var cell = new AreaStepCell(r.WorldX, r.WorldY); var ordinal = r.LocalX + 64 * r.LocalY;
                terrain[cell] = !maskConsistent ? Unknown(cell, r.RawArt, "MASK_VERSION_MISMATCH") :
                    mask is not null && (mask[ordinal / 32] & (1u << (ordinal % 32))) != 0 ? Supported(cell, r.RawArt, true, AreaStepTerrainSource.SerializedMask, "SERIALIZED_BLOCK_MASK") :
                    (r.RawArt >> 28) switch
                    {
                        11 => Supported(cell, r.RawArt, (r.RawArt & 1) == 0, AreaStepTerrainSource.FacadeBit, "FACADE_WALKABILITY_BIT"),
                        0 when tileTable is null => Unknown(cell, r.RawArt, "TILE_NAME_TABLE_UNSUPPORTED"),
                        0 => AreaStepRules.TileBlocks(r.RawArt, tileTable) is { } blocks
                            ? Supported(cell, r.RawArt, blocks, AreaStepTerrainSource.TileName, "TILE_NAME_FLAGS")
                            : Unknown(cell, r.RawArt, "TILE_NAME_MAPPING_UNRESOLVED"),
                        _ => Unknown(cell, r.RawArt, "TILE_ART_CLASS_UNSUPPORTED")
                    };
            }
        }

        stage.Current = AreaStepInvocationStage.Objects;
        var wholeMap = area.Profile.StartsWith(WholeMapProfilePrefix, StringComparison.Ordinal);
        var admitted = sectors.Select(s => (s.Coordinate.X, s.Coordinate.Y)).ToHashSet();
        var objects = new Dictionary<AreaStepCell, List<AreaStepObjectEvaluation>>();
        var walls = new Dictionary<AreaStepCell, List<AreaStepWallEdge>>();
        foreach (var o in area.Objects)
        {
            if (o.WorldX is not { } x || o.WorldY is not { } y)
            {
                if (o.Scope == AreaScope.Core) throw Fail(AreaStepInvocationCode.MalformedSource, AreaStepInvocationStage.Objects);
                continue;
            }
            if (!admitted.Contains((x >> 6, y >> 6))) continue;
            ReadOnlySpan<byte> record = default;
            if (o.Key.Kind == AreaSourceKind.Mobile)
            {
                if (o.Key.Offset < 0 || o.Key.Offset + o.Key.Length > mobileFile.Length ||
                    Sha(mobileFile.Span.Slice((int)o.Key.Offset, o.Key.Length)) != o.RawSha256)
                    throw Fail(AreaStepInvocationCode.FingerprintMismatch, AreaStepInvocationStage.Objects);
                record = mobileFile.Span.Slice((int)o.Key.Offset, o.Key.Length);
            }
            var cell = new AreaStepCell(x, y);
            if (!objects.TryGetValue(cell, out var list)) objects[cell] = list = [];
            var evaluation = wholeMap && o.Type == 1 ? PortalEvaluation(o, cell, record, patchTargets, patchMalformed) : Evaluate(o, cell, record, patchTargets, patchMalformed, effects);
            list.Add(evaluation);
            if (evaluation.Reason is WallEdgeReason or PortalEdgeReason && Number(o, 0) is { } aid)
            {
                // A wall or portal sits on one tile edge: its rotation rounded up to odd. The tile itself stays walkable.
                var art = unchecked((uint)aid);
                if (!walls.TryGetValue(cell, out var edges)) walls[cell] = edges = [];
                var piece = (int)((art >> 14) & 63);
                var (rule, reason) = !wholeMap ? (AreaStepEdgeRule.Legacy, (string?)null)
                    : o.Type == 1 ? PortalEdgeRule(art, Number(o, PortalFlagsSlot), HasUnlockScript(o))
                    : WallPieceRule(piece);
                edges.Add(new(o.Key, cell, (int)((art >> 11) & 7) | 1, o.Type == 0 && OpeningWallPieces.Contains(piece), rule, reason,
                    rule == AreaStepEdgeRule.Leaving ? (int)((art >> 11) & 7) : -1));
            }
        }

        var frozenObjects = objects.ToDictionary(p => p.Key, p => p.Value.OrderBy(e => e.Key.Kind).ThenBy(e => e.Key.SourceIndex).ThenBy(e => e.Key.Offset).ToArray());
        var frozenWalls = walls.ToDictionary(p => p.Key, p => p.Value.OrderBy(e => e.Key.SourceIndex).ThenBy(e => e.Key.Offset).ToArray());
        var digest = Digest(kind, area.Profile, sources, admitted, terrain, frozenObjects, frozenWalls);
        return new(new(kind, area.Profile, sources, digest), epoch, admitted, terrain, frozenObjects, frozenWalls);
    }

    internal static AreaStepObjectEvaluation Evaluate(AreaObjectRecord o, AreaStepCell cell, ReadOnlySpan<byte> record,
        IReadOnlyList<AreaStepRules.PatchTarget> patch, int patchMalformed, IReadOnlyDictionary<int, IReadOnlyList<AreaStepRules.EffectChange>?> effects)
    {
        AreaStepObjectEvaluation Unknown(string reason) => new(o.Key, cell, o.Type, CardinalTraversalCoverage.Unknown, CardinalTraversalApplicability.Unknown, null, reason);
        AreaStepObjectEvaluation Excluded(string reason) => new(o.Key, cell, o.Type, CardinalTraversalCoverage.Supported, CardinalTraversalApplicability.NotApplicable, null, reason);
        AreaStepObjectEvaluation Disposed(bool blocks, string reason, long? min = null, long? max = null) => new(o.Key, cell, o.Type, CardinalTraversalCoverage.Supported,
            CardinalTraversalApplicability.Applicable, blocks ? CardinalTraversalDisposition.Blocked : CardinalTraversalDisposition.Open, reason, min, max);

        if (o.MapPatch is { Unmodelled.Count: > 0 }) return Unknown("MAP_PATCH_CHANGE_UNMODELLED");
        if (o.Key.Kind == AreaSourceKind.Mobile)
        {
            // map.patch is applied to mobiles before sectors load; an unmodeled patch on this record is Unknown.
            if (patchMalformed != 0) return Unknown("MAP_PATCH_LINE_UNSUPPORTED");
            foreach (var target in patch)
                if (record.IndexOf(target.TextOrder) >= 0 || record.IndexOf(target.MixedEndian) >= 0) return Unknown("MAP_PATCH_TARGETS_OBJECT");
        }
        if (o.Type == 1) return Unknown("WALL_OR_PORTAL_UNSUPPORTED");
        if (Number(o, ObjectTypeFlagsSlot) is not { } rawFlags) return Unknown("FLAGS_UNRESOLVED");
        var flags = unchecked((uint)rawFlags);
        if (o.Type == 0)
        {
            // Walls are edge components, never destination blockers. A wall whose own spell flags carry
            // pass-wall (0x100000, slot 19) is not a blocker in the source traversal, so it is not registered.
            if ((flags & FlagDestroyedOrOff) != 0) return Excluded("DESTROYED_OR_OFF_NOT_LISTED");
            if (Number(o, SpellFlagsSlot) is not { } spell) return Unknown("WALL_SPELL_FLAGS_UNRESOLVED");
            if ((unchecked((uint)spell) & SpellPassWalled) != 0) return Excluded("WALL_PASSWALLED_NOT_LISTED");
            return Number(o, 0) is { } aid && unchecked((uint)aid) >> 28 == 1 ? Excluded(WallEdgeReason) : Unknown("WALL_ART_UNRESOLVED");
        }
        if ((flags & FlagInventory) != 0) return Excluded("INVENTORY_NOT_LISTED");
        if ((flags & FlagDestroyedOrOff) != 0) return Excluded("DESTROYED_OR_OFF_NOT_LISTED");
        if (o.Type is >= 5 and <= 14) return Excluded("ITEM_TYPE_NOT_QUERIED");
        if (o.Type == 15) return Unknown("PC_RECORD_UNSUPPORTED");
        if (o.Type == 16)
        {
            if (Critter(o) is not { } critter) return Unknown("CRITTER_STATS_UNRESOLVED");
            var life = AreaStepRules.Evaluate(critter, effects);
            return life.Life switch
            {
                AreaStepRules.Life.Alive => Disposed(true, life.Reason, life.Minimum, life.Maximum),
                AreaStepRules.Life.Dead => Disposed(false, life.Reason, life.Minimum, life.Maximum),
                _ => Unknown(life.Reason)
            };
        }
        if (o.Type is not (2 or 3 or 4 or 17)) return Unknown("OBJECT_TYPE_UNSUPPORTED");
        return (flags & FlagNoBlock) != 0 ? Disposed(false, "NO_BLOCK_FLAG") : Disposed(true, "OBJECT_BLOCKS");
    }

    internal const string WallEdgeReason = "WALL_EDGE_COMPONENT";
    internal const string PortalEdgeReason = "PORTAL_EDGE_COMPONENT";
    internal const string LockedLeavingOnlyReason = "PORTAL_LOCKED_LEAVING_ONLY";
    private const string WholeMapProfilePrefix = "arc-data-019-";
    private const int PortalFlagsSlot = 36, ScriptsSlot = 31, UnlockScriptSap = 3;
    private static readonly int[] WindowWallPieces = [10, 13, 14, 17, 18, 19];
    private static readonly int[] DoorwayWallPieces = [22, 25, 26, 29, 30, 31, 32];

    /// <summary>Whole-map wall rule (docs/research/first-building-evidence.md, the source's
    /// traversal): a doorway piece never blocks, the portal on its edge decides; a window piece
    /// blocks a diagonal step and makes a cardinal one a window jump for a critter walker
    /// (ARC-WORLD-021); every other piece blocks.</summary>
    internal static (AreaStepEdgeRule Rule, string Reason) WallPieceRule(int piece) =>
        DoorwayWallPieces.Contains(piece) ? (AreaStepEdgeRule.Pass, "DOORWAY_PIECE_PORTAL_DECIDES")
        : WindowWallPieces.Contains(piece) ? (AreaStepEdgeRule.WindowJump, "WINDOW_PIECE_JUMP")
        : (AreaStepEdgeRule.Block, "WALL_PIECE_BLOCKS");

    /// <summary>A portal's edge for the player at T0 (the source's portal open attempt and
    /// lock test, ledger LV1–LV5 and docs/research/first-building-evidence.md): open or busted passes; an unlock script (SAP 3) is Unknown; jammed or
    /// magically held blocks; otherwise the portal is locked only when its LOCKED bit is set and it
    /// is not never-locked, and a by-day or by-night bit can unlock it by the hour (Unknown, the
    /// static evidence has no clock). A locked portal needs its key (none is held at T0) unless the
    /// walker leaves a building, which always-locked disables: always-locked blocks (or is Unknown by
    /// the hour); otherwise the step decides (<see cref="AreaStepEdgeRule.Leaving"/>, ARC-RES-049): a
    /// leaving step passes, any other is blocked by a plain lock and Unknown by an hour lock. Doors and
    /// windows follow the same attempt; a window's wall piece then makes the step a jump.</summary>
    internal static (AreaStepEdgeRule Rule, string Reason) PortalEdgeRule(uint art, long? portalFlags, bool unlockScript)
    {
        var frame = (art >> 14) & 31;
        if (portalFlags is not { } raw) return (AreaStepEdgeRule.Unknown, "PORTAL_FLAGS_UNRESOLVED");
        var flags = unchecked((uint)raw);
        if ((art & 0x200) != 0 || (flags & 0x80) != 0) return (AreaStepEdgeRule.Pass, "PORTAL_BUSTED");
        if (frame != 0) return (AreaStepEdgeRule.Pass, "PORTAL_OPEN");
        if (unlockScript) return (AreaStepEdgeRule.Unknown, "PORTAL_UNLOCK_SCRIPT");
        if ((flags & 0x6) != 0) return (AreaStepEdgeRule.Block, "PORTAL_JAMMED_OR_HELD");
        if ((flags & 0x8) != 0 || (flags & 0x1) == 0) return (AreaStepEdgeRule.Pass, "PORTAL_UNLOCKED_OPENS_ON_WALK");
        var alwaysLocked = (flags & 0x10) != 0;
        if ((flags & 0x60) != 0) return (alwaysLocked ? AreaStepEdgeRule.Unknown : AreaStepEdgeRule.Leaving, "PORTAL_LOCK_DEPENDS_ON_HOUR");
        if (alwaysLocked) return (AreaStepEdgeRule.Block, "PORTAL_ALWAYS_LOCKED_NO_KEY");
        return (AreaStepEdgeRule.Leaving, LockedLeavingOnlyReason);
    }

    private static AreaStepObjectEvaluation PortalEvaluation(AreaObjectRecord o, AreaStepCell cell, ReadOnlySpan<byte> record,
        IReadOnlyList<AreaStepRules.PatchTarget> patch, int patchMalformed)
    {
        AreaStepObjectEvaluation Unknown(string reason) => new(o.Key, cell, o.Type, CardinalTraversalCoverage.Unknown, CardinalTraversalApplicability.Unknown, null, reason);
        if (o.MapPatch is { Unmodelled.Count: > 0 }) return Unknown("MAP_PATCH_CHANGE_UNMODELLED");
        if (PatchGap(o, record, patch, patchMalformed) is { } gap) return Unknown(gap);
        if (Number(o, ObjectTypeFlagsSlot) is not { } flags) return Unknown("FLAGS_UNRESOLVED");
        if ((unchecked((uint)flags) & FlagDestroyedOrOff) != 0)
            return new(o.Key, cell, o.Type, CardinalTraversalCoverage.Supported, CardinalTraversalApplicability.NotApplicable, null, "DESTROYED_OR_OFF_NOT_LISTED");
        return Number(o, 0) is { } aid && unchecked((uint)aid) >> 28 == 3
            ? new(o.Key, cell, o.Type, CardinalTraversalCoverage.Supported, CardinalTraversalApplicability.NotApplicable, null, PortalEdgeReason)
            : Unknown("PORTAL_ART_UNRESOLVED");
    }

    /// <summary>A script attached at the unlock slot (SAP 3) with a non-zero script number; an
    /// attachment too short to read counts as one (fail closed).</summary>
    private static bool HasUnlockScript(AreaObjectRecord o) =>
        o.EffectiveFields.FirstOrDefault(f => f.Slot == ScriptsSlot) is { Presence: AreaPresence.Array } scripts &&
        scripts.Elements.Any(e => e.Key == UnlockScriptSap && (e.Words.Count < 3 || e.Words[2] != 0));

    /// <summary>map.patch is applied to mobiles before sectors load; an unmodeled patch line or a
    /// patch that targets this mobile record leaves its evidence Unknown.</summary>
    private static string? PatchGap(AreaObjectRecord o, ReadOnlySpan<byte> record, IReadOnlyList<AreaStepRules.PatchTarget> patch, int patchMalformed)
    {
        if (o.Key.Kind != AreaSourceKind.Mobile) return null;
        if (patchMalformed != 0) return "MAP_PATCH_LINE_UNSUPPORTED";
        foreach (var target in patch)
            if (record.IndexOf(target.TextOrder) >= 0 || record.IndexOf(target.MixedEndian) >= 0) return "MAP_PATCH_TARGETS_OBJECT";
        return null;
    }

    private static long? Number(AreaObjectRecord o, int slot) =>
        o.EffectiveFields.FirstOrDefault(f => f.Slot == slot) is { Presence: AreaPresence.Value, Number: { } n } ? n : null;

    private static AreaStepRules.Critter? Critter(AreaObjectRecord o)
    {
        var stats = o.EffectiveFields.FirstOrDefault(f => f.Slot == StatBaseSlot);
        if (stats is not { Presence: AreaPresence.Array } || stats.Elements.Count != AreaStepRules.StatCount ||
            !stats.Elements.Select(e => e.Key).SequenceEqual(Enumerable.Range(0, AreaStepRules.StatCount)) || stats.Elements.Any(e => e.Words.Count != 1)) return null;
        var effectField = o.EffectiveFields.FirstOrDefault(f => f.Slot == EffectsSlot);
        int[] indices;
        if (effectField is null or { Presence: AreaPresence.Null or AreaPresence.Empty }) indices = [];
        else if (effectField is { Presence: AreaPresence.Array } a && a.Elements.All(e => e.Words.Count == 1) &&
            a.Elements.Select(e => e.Key).SequenceEqual(Enumerable.Range(0, a.Elements.Count)))
            indices = [.. a.Elements.Select(e => unchecked((int)e.Words[0]))];
        else return null;
        if (Number(o, HpPointsSlot) is not { } points || Number(o, HpAdjustmentSlot) is not { } adjustment || Number(o, HpDamageSlot) is not { } damage) return null;
        return new([.. stats.Elements.Select(e => unchecked((int)e.Words[0]))], indices, points, adjustment, damage);
    }

    private static AreaStepTerrainEvidence Supported(AreaStepCell cell, uint raw, bool blocks, AreaStepTerrainSource source, string reason) =>
        new(cell, raw, CardinalTraversalCoverage.Supported, blocks ? CardinalTraversalDisposition.Blocked : CardinalTraversalDisposition.Open, source, reason);
    private static AreaStepTerrainEvidence Unknown(AreaStepCell cell, uint raw, string reason) =>
        new(cell, raw, CardinalTraversalCoverage.Unknown, null, null, reason);

    private static string Digest(AreaStepInputKind kind, string profile, IEnumerable<AreaStepSourceIdentity> sources, IEnumerable<(long X, long Y)> admitted,
        IReadOnlyDictionary<AreaStepCell, AreaStepTerrainEvidence> terrain, IReadOnlyDictionary<AreaStepCell, AreaStepObjectEvaluation[]> objects,
        IReadOnlyDictionary<AreaStepCell, AreaStepWallEdge[]> walls)
    {
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        w.Write("AREA_STEP_EVIDENCE_V1"); w.Write(AreaStepRules.ProfileId); w.Write((int)kind); w.Write(profile);
        foreach (var s in sources.OrderBy(s => s.Role, StringComparer.Ordinal)) { w.Write(s.Role); w.Write(s.Length); w.Write(s.Sha256); }
        foreach (var (x, y) in admitted.OrderBy(s => s.Y).ThenBy(s => s.X)) { w.Write(x); w.Write(y); }
        foreach (var t in terrain.Values.OrderBy(t => t.Cell.Y).ThenBy(t => t.Cell.X))
        {
            w.Write(t.Cell.X); w.Write(t.Cell.Y); w.Write(t.RawArt); w.Write((int)t.Coverage);
            w.Write(t.Disposition is { } d ? (int)d : -1); w.Write(t.Source is { } s ? (int)s : -1); w.Write(t.ReasonCode);
        }
        foreach (var e in objects.OrderBy(p => p.Key.Y).ThenBy(p => p.Key.X).SelectMany(p => p.Value))
        {
            w.Write(e.Cell.X); w.Write(e.Cell.Y); w.Write(e.Key.SourceSha256); w.Write(e.Key.RuntimeMap); w.Write((int)e.Key.Kind);
            w.Write(e.Key.Sector ?? -1); w.Write(e.Key.SourceIndex); w.Write(e.Key.Offset); w.Write(e.Key.ObjectIdSha256); w.Write(e.Key.Length);
            w.Write(e.ObjectType); w.Write((int)e.Coverage); w.Write((int)e.Applicability); w.Write(e.Disposition is { } d ? (int)d : -1);
            w.Write(e.Reason); w.Write(e.HpMinimum ?? long.MinValue); w.Write(e.HpMaximum ?? long.MinValue);
        }
        // Wall edges are appended only when present, so evidence without walls keeps its earlier digest.
        if (walls.Count != 0)
        {
            w.Write("WALL_EDGES_V1");
            foreach (var e in walls.OrderBy(p => p.Key.Y).ThenBy(p => p.Key.X).SelectMany(p => p.Value))
            { w.Write(e.Cell.X); w.Write(e.Cell.Y); w.Write((int)e.Key.Kind); w.Write(e.Key.SourceIndex); w.Write(e.Key.Offset); w.Write(e.Edge); w.Write(e.Opening); }
            // Stated edge rules (whole maps) are appended only when present, so pinned digests stay as they were.
            if (walls.Values.SelectMany(v => v).Any(e => e.Rule != AreaStepEdgeRule.Legacy))
            {
                w.Write("EDGE_RULES_V1");
                foreach (var e in walls.OrderBy(p => p.Key.Y).ThenBy(p => p.Key.X).SelectMany(p => p.Value))
                {
                    w.Write((int)e.Rule); w.Write(e.Reason ?? "");
                    // ARC-RES-049: a leaving portal also states the raw rotation its test steps by.
                    if (e.Rule == AreaStepEdgeRule.Leaving) w.Write(e.RawRotation);
                }
            }
        }
        w.Flush();
        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    private static string Name(DatEntry e) => Encoding.ASCII.GetString(e.RawNameBytes.ToArray()).TrimEnd('\0').Replace('\\', '/').ToLowerInvariant();
    private static string Sha(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static AreaStepInvocationException Fail(AreaStepInvocationCode code, AreaStepInvocationStage stage) => new(code, stage);

    private sealed class StageCursor { internal AreaStepInvocationStage Current = AreaStepInvocationStage.Selection; }

    // Every failure becomes a code plus the stage that was running; no message, path or inner exception survives.
    private static T Sanitize<T>(StageCursor stage, Func<T> operation)
    {
        try { return operation(); }
        catch (AreaStepInvocationException) { throw; }
        catch (TerrainException e) { throw Fail(e.Code == TerrainErrorCode.LimitExceeded ? AreaStepInvocationCode.LimitExceeded : AreaStepInvocationCode.MalformedSource, stage.Current); }
        catch (DatLimitExceededException) { throw Fail(AreaStepInvocationCode.LimitExceeded, stage.Current); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ObjectDisposedException)
        { throw Fail(AreaStepInvocationCode.InputUnavailable, stage.Current); }
        catch (Exception e) when (e is DatException or OverflowException or InvalidOperationException or ArgumentException or FormatException)
        { throw Fail(AreaStepInvocationCode.MalformedSource, stage.Current); }
    }
}
