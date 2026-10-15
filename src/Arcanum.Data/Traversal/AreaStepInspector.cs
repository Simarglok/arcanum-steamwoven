using System.Security.Cryptography;
using System.Text;
using Arcanum.Data.Area;
using Arcanum.Data.Terrain;

namespace Arcanum.Data.Traversal;

/// <summary>Construction of whole-core T0 step evidence from a strict Area snapshot and explicit rule sources.</summary>
public static class AreaStepInspector
{
    private const string OriginalAreaProfile = "arc-data-009-complete-file-effective-source-v1";
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
    private const uint FlagDestroyedOrOff = 0x3, FlagNoBlock = 0x400, FlagInventory = 0x1000;

    /// <summary>Structural construction. Every archive is caller-owned and only read. The patch archive is optional
    /// (absent means no patch source in this structural selection); every other rule resource must occur exactly once.</summary>
    public static AreaStepSnapshot Inspect(AreaSnapshot area, DatArchive module, DatArchive tileNameArchive, DatArchive effectArchive,
        DatArchive? patchArchive, ReadOnlyMemory<byte> mobileFile, long evidenceEpoch)
    {
        ArgumentNullException.ThrowIfNull(area); ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(tileNameArchive); ArgumentNullException.ThrowIfNull(effectArchive);
        if (evidenceEpoch < 0) throw new ArgumentOutOfRangeException(nameof(evidenceEpoch));
        var stage = new StageCursor();
        return Sanitize(stage, () => Build(stage, AreaStepInputKind.Structural, area, module, tileNameArchive, effectArchive, patchArchive, mobileFile, evidenceEpoch));
    }

    /// <summary>Authenticated original Crash Site profile: the strict original Area snapshot, its nine core sectors,
    /// and pinned tile-name, effect, module-effect and map.patch payloads read from the explicitly named archives.</summary>
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
            if (area.Profile != OriginalAreaProfile) throw Fail(AreaStepInvocationCode.InvalidSelection, AreaStepInvocationStage.Selection);
            var core = area.Sectors.Where(s => s.Scope == AreaScope.Core && s.Present).Select(s => (s.Coordinate.X, s.Coordinate.Y)).ToHashSet();
            if (core.Count != 9 || !core.SetEquals(from x in new long[] { 1451, 1452, 1453 } from y in new long[] { 1289, 1290, 1291 } select (x, y)))
                throw Fail(AreaStepInvocationCode.InvalidSelection, AreaStepInvocationStage.Selection);
            // Bound the read by the authenticated Area identity before reading the whole mobile file.
            var mobileInput = area.Inputs.SingleOrDefault(i => i.Key.Role == AreaSourceRole.NormalGameMobile)
                ?? throw Fail(AreaStepInvocationCode.InvalidSelection, AreaStepInvocationStage.Selection);
            if (new FileInfo(mobilePath).Length != mobileInput.Length) throw Fail(AreaStepInvocationCode.FingerprintMismatch, AreaStepInvocationStage.Fingerprint);
            var mobile = File.ReadAllBytes(mobilePath);
            using var module = DatArchive.Open(modulePath);
            using var names = DatArchive.Open(tileNameResourcePath);
            using var effects = DatArchive.Open(effectResourcePath);
            using var patch = DatArchive.Open(patchArchivePath);
            var snapshot = Build(stage, AreaStepInputKind.AuthenticatedOriginal, area, module, names, effects, patch, mobile, evidenceEpoch);
            foreach (var (path, pin) in OriginalPayloads)
                if (!snapshot.InputIdentity.Sources.Any(s => s.Role == path && s.Length == pin.Length && s.Sha256 == pin.Sha256))
                    throw Fail(AreaStepInvocationCode.FingerprintMismatch, AreaStepInvocationStage.Fingerprint);
            return snapshot;
        });
    }

    private static AreaStepSnapshot Build(StageCursor stage, AreaStepInputKind kind, AreaSnapshot area, DatArchive module, DatArchive tileNameArchive,
        DatArchive effectArchive, DatArchive? patchArchive, ReadOnlyMemory<byte> mobileFile, long epoch)
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
        byte[]? Resource(DatArchive archive, string path, bool required)
        {
            var hits = archive.Entries.Where(e => Name(e) == path).ToArray();
            if (hits.Length == 0 && !required) return null;
            if (hits.Length != 1) throw Fail(AreaStepInvocationCode.InvalidSelection, AreaStepInvocationStage.Rules);
            if (hits[0].DeclaredOutputSize > AreaStepRules.MaxMesBytes) throw Fail(AreaStepInvocationCode.LimitExceeded, AreaStepInvocationStage.Rules);
            var bytes = archive.ReadEntry(hits[0]);
            sources.Add(new(path, bytes.Length, Sha(bytes)));
            return bytes;
        }
        var tileTable = AreaStepRules.TileNameTable(AreaStepRules.ParseMes(Resource(tileNameArchive, TileNamePath, true)));
        var effects = new Dictionary<int, IReadOnlyList<AreaStepRules.EffectChange>?>();
        foreach (var (key, value) in AreaStepRules.ParseMes(Resource(effectArchive, EffectPath, true)).Where(r => r.Key is >= 50 and < 400))
            effects[key] = AreaStepRules.ParseEffect(value);
        if (Resource(module, GameEffectPath, false) is { } gameEffects)
            foreach (var (key, value) in AreaStepRules.ParseMes(gameEffects).Where(r => r.Key is >= 0 and < 50))
                effects[key] = AreaStepRules.ParseEffect(value);
        IReadOnlyList<AreaStepRules.PatchTarget> patchTargets = [];
        var patchMalformed = 0;
        if (patchArchive is not null && Resource(patchArchive, PatchPath, false) is { } patch)
            patchTargets = AreaStepRules.PatchTargets(Encoding.Latin1.GetString(patch), out patchMalformed);

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
        var admitted = sectors.Select(s => (s.Coordinate.X, s.Coordinate.Y)).ToHashSet();
        var objects = new Dictionary<AreaStepCell, List<AreaStepObjectEvaluation>>();
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
            list.Add(Evaluate(o, cell, record, patchTargets, patchMalformed, effects));
        }

        var frozenObjects = objects.ToDictionary(p => p.Key, p => p.Value.OrderBy(e => e.Key.Kind).ThenBy(e => e.Key.SourceIndex).ThenBy(e => e.Key.Offset).ToArray());
        var digest = Digest(kind, area.Profile, sources, admitted, terrain, frozenObjects);
        return new(new(kind, area.Profile, sources, digest), epoch, admitted, terrain, frozenObjects);
    }

    internal static AreaStepObjectEvaluation Evaluate(AreaObjectRecord o, AreaStepCell cell, ReadOnlySpan<byte> record,
        IReadOnlyList<AreaStepRules.PatchTarget> patch, int patchMalformed, IReadOnlyDictionary<int, IReadOnlyList<AreaStepRules.EffectChange>?> effects)
    {
        AreaStepObjectEvaluation Unknown(string reason) => new(o.Key, cell, o.Type, CardinalTraversalCoverage.Unknown, CardinalTraversalApplicability.Unknown, null, reason);
        AreaStepObjectEvaluation Excluded(string reason) => new(o.Key, cell, o.Type, CardinalTraversalCoverage.Supported, CardinalTraversalApplicability.NotApplicable, null, reason);
        AreaStepObjectEvaluation Disposed(bool blocks, string reason, long? min = null, long? max = null) => new(o.Key, cell, o.Type, CardinalTraversalCoverage.Supported,
            CardinalTraversalApplicability.Applicable, blocks ? CardinalTraversalDisposition.Blocked : CardinalTraversalDisposition.Open, reason, min, max);

        if (o.Key.Kind == AreaSourceKind.Mobile)
        {
            // map.patch is applied to mobiles before sectors load; an unmodeled patch on this record is Unknown.
            if (patchMalformed != 0) return Unknown("MAP_PATCH_LINE_UNSUPPORTED");
            foreach (var target in patch)
                if (record.IndexOf(target.TextOrder) >= 0 || record.IndexOf(target.MixedEndian) >= 0) return Unknown("MAP_PATCH_TARGETS_OBJECT");
        }
        if (o.Type is 0 or 1) return Unknown("WALL_OR_PORTAL_UNSUPPORTED");
        if (Number(o, ObjectTypeFlagsSlot) is not { } rawFlags) return Unknown("FLAGS_UNRESOLVED");
        var flags = unchecked((uint)rawFlags);
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
        IReadOnlyDictionary<AreaStepCell, AreaStepTerrainEvidence> terrain, IReadOnlyDictionary<AreaStepCell, AreaStepObjectEvaluation[]> objects)
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
