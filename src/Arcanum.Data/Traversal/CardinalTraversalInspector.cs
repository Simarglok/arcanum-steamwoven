using Arcanum.Data.StaticObjects;
using Arcanum.Data.Terrain;

namespace Arcanum.Data.Traversal;

/// <summary>Construction through existing bounded readers; no attestation/report/permission-array factory.</summary>
public static class CardinalTraversalInspector
{
    private const string SectorPath = "maps\\Arcanum1-024-fixed\\86570436012.sec";
    private const string NamePath = "art\\tile\\tilename.mes";

    public static CardinalTraversalSnapshot InspectSector(StaticObjectSource source, StaticObjectPrototypeSource prototypes,
        string moduleSourceId, string resourceSourceId, string prototypeSourceId, TerrainSectorSelector selector,
        TerrainTileNameSource? tileNameSource, long evidenceEpoch, TerrainLoaderOptions? terrainOptions = null,
        StaticObjectLoaderOptions? staticOptions = null)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(prototypes); ArgumentNullException.ThrowIfNull(selector);
        CardinalTraversalRules.ValidateId(moduleSourceId, nameof(moduleSourceId));
        CardinalTraversalRules.ValidateId(resourceSourceId, nameof(resourceSourceId));
        CardinalTraversalRules.ValidateId(prototypeSourceId, nameof(prototypeSourceId));
        if (evidenceEpoch < 0) throw new ArgumentOutOfRangeException(nameof(evidenceEpoch));
        var terrainLimits = terrainOptions ?? new TerrainLoaderOptions(); terrainLimits.Validate();
        var staticLimits = staticOptions ?? new StaticObjectLoaderOptions(); staticLimits.Validate();
        return Sanitize(() =>
        {
            ValidateSelection(source, selector, tileNameSource, resourceSourceId);
            var entry = TerrainLoader.SelectModule(source.ModuleArchive, selector);
            var terrain = TerrainObstructionInspector.InspectSector(source.ModuleArchive, moduleSourceId, selector, tileNameSource, terrainLimits);
            var inspection = StaticObjectLoader.InspectForObstruction(source, prototypes, staticLimits);
            var ordinary = StaticObstructionInspector.Create(inspection, entry.Ordinal, moduleSourceId, prototypeSourceId, null);
            return Build(CardinalTraversalInputKind.Structural, moduleSourceId, resourceSourceId, prototypeSourceId,
                selector, entry, terrain, inspection, ordinary, null, evidenceEpoch);
        });
    }

    public static CardinalTraversalSnapshot ValidateOriginalData(string modulePath, string resourcePath, string prototypeDirectory,
        long evidenceEpoch, TerrainLoaderOptions? terrainOptions = null, StaticObjectLoaderOptions? staticOptions = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(modulePath); ArgumentException.ThrowIfNullOrEmpty(resourcePath); ArgumentException.ThrowIfNullOrEmpty(prototypeDirectory);
        if (evidenceEpoch < 0) throw new ArgumentOutOfRangeException(nameof(evidenceEpoch));
        var terrainLimits = terrainOptions ?? new TerrainLoaderOptions(); terrainLimits.Validate();
        var staticLimits = staticOptions ?? new StaticObjectLoaderOptions(); staticLimits.Validate();
        return Sanitize(() => StaticObjectLoader.WithValidatedOriginalData(modulePath, resourcePath, prototypeDirectory, staticLimits,
            (source, _, validation) =>
            {
                var selector = new TerrainSectorSelector(SectorPath, CardinalTraversalRules.ProfileId, 1452, 1290,
                    86570436012, 92928, 82560, 64, 64, new(463, 2, 47291));
                var entry = TerrainLoader.SelectModule(source.ModuleArchive, selector);
                var terrain = TerrainObstructionInspector.InspectSector(source.ModuleArchive, "CRASH_SITE_MODULE", selector,
                    new(source.ResourceArchive, "CRASH_SITE_RESOURCE", NamePath, TerrainPathProfile.CeNonWindowsMacOs), terrainLimits);
                var identity = new StaticObstructionInputIdentity(validation);
                var ordinary = StaticObstructionInspector.Create(validation.Inspection, entry.Ordinal, "CRASH_SITE_MODULE", "CRASH_SITE_PROTOTYPES", identity);
                // Full terrain/MES/census/static projection and digest happen INSIDE the loader's final hash guard.
                return Build(CardinalTraversalInputKind.AuthenticatedOriginal, "CRASH_SITE_MODULE", "CRASH_SITE_RESOURCE", "CRASH_SITE_PROTOTYPES",
                    selector, entry, terrain, validation.Inspection, ordinary, identity, evidenceEpoch);
            }));
    }

    private static void ValidateSelection(StaticObjectSource source, TerrainSectorSelector selector, TerrainTileNameSource? names, string resourceId)
    {
        if (source.ModuleEntryPath != SectorPath || selector.ModuleEntryPath != SectorPath || selector.SectorX != 1452 || selector.SectorY != 1290 ||
            selector.PackedSectorId != 86570436012 || selector.OriginX != 92928 || selector.OriginY != 82560 || selector.Width != 64 || selector.Height != 64 ||
            (names is not null && (!ReferenceEquals(names.Archive, source.ResourceArchive) || names.SourceId != resourceId ||
                names.MesEntryPath.Replace('/', '\\').ToLowerInvariant() != NamePath.ToLowerInvariant() || names.PathProfile != TerrainPathProfile.CeNonWindowsMacOs)))
            throw new CardinalTraversalInvocationException(CardinalTraversalInvocationCode.InvalidSelection, CardinalTraversalInvocationStage.Selection);
    }

    internal static CardinalTraversalSnapshot Build(CardinalTraversalInputKind kind, string moduleId, string resourceId, string prototypeId,
        TerrainSectorSelector selector, DatEntry entry, TerrainObstructionSectorReport terrain, StaticObjectInspection inspection,
        StaticObstructionSectorReport ordinary, StaticObstructionInputIdentity? original, long epoch)
    {
        var words = terrain.CopyTileWords(); var mask = terrain.CopyBlockWords();
        var cells = words.Select((raw, ordinal) => new CardinalTerrainCell(raw, mask?[ordinal / 32],
            raw >> 28 == 0 ? (int)((raw >> 7) & 3) : null,
            raw >> 28 == 0 ? TerrainTileNameRuleTable.CategoryIndex(raw) : null,
            raw >> 28 == 0 ? (int)((raw >> 22) & 63) : null, terrain.SelectedNameKey(ordinal),
            terrain.GetCell(ordinal % 64, ordinal / 64))).ToArray();
        var census = inspection.TraversalRecords.OrderBy(r => r.SourceIndex).Select(r => new CardinalTraversalCensusRecord(r)).ToArray();
        var complete = inspection.CensusStatus == StaticObjectCensusStatus.Complete &&
            terrain.TerminalRecordCount == inspection.TerminalRecordCount && inspection.TerminalRecordCount == census.Length &&
            inspection.Records.Count == census.Length && ordinary.Records.Count == census.Length &&
            census.Select(r => r.SourceIndex).SequenceEqual(Enumerable.Range(0, census.Length));
        var membership = complete && census.All(r => r.ObjectType == 3 && r.LocationStatus == CardinalTraversalLocationStatus.InRange &&
            r.EffectiveFlags is uint flags && (flags & 0x2000) == 0) &&
            census.All(row => ordinary.Records.Any(r => r.SourceIndex == row.SourceIndex && r.ObjectType == row.ObjectType &&
                r.PrototypeAid == row.PrototypeAid && r.EffectiveFlags == row.EffectiveFlags && r.FlagsSource == row.FlagsSource &&
                r.LocationStatus == StaticObstructionLocationStatus.InRange && r.Destination is StaticObstructionLocalCell c &&
                row.LocalCell == new CardinalTraversalCell(c.X, c.Y)));
        var sector = new CardinalTraversalSectorIdentity(selector, entry);
        var digest = CardinalTraversalEncoding.EvidenceDigest(kind, moduleId, resourceId, prototypeId, sector,
            terrain.SelectedNameEntry, original, words, mask, cells, inspection.TerminalRecordCount, census, terrain.TileScriptCount, terrain.SectorScriptBytes);
        var identity = new CardinalTraversalInputIdentity(kind, moduleId, resourceId, prototypeId, sector, terrain.SelectedNameEntry, original, digest);
        return new(identity, epoch, cells, ordinary, inspection.TerminalRecordCount, terrain.TileScriptCount, terrain.SectorScriptBytes, census, complete, membership);
    }

    // Internal so guard-negative fixtures exercise the same invocation boundary as both public factories.
    internal static T Sanitize<T>(Func<T> operation)
    {
        try { return operation(); }
        catch (CardinalTraversalInvocationException) { throw; }
        catch (StaticObjectException e)
        {
            var stage = e.Stage switch
            {
                StaticObjectFailureStage.Selector => CardinalTraversalInvocationStage.Selection,
                StaticObjectFailureStage.FingerprintInput => CardinalTraversalInvocationStage.Fingerprint,
                StaticObjectFailureStage.Prototype => CardinalTraversalInvocationStage.Prototypes,
                StaticObjectFailureStage.SectorFraming => CardinalTraversalInvocationStage.Terrain,
                _ => CardinalTraversalInvocationStage.Records
            };
            var code = e.Code == StaticObjectErrorCode.LimitExceeded ? CardinalTraversalInvocationCode.LimitExceeded :
                e.Stage == StaticObjectFailureStage.FingerprintInput ?
                    e.Code == StaticObjectErrorCode.InvalidSelector ? CardinalTraversalInvocationCode.FingerprintMismatch :
                    e.Message is "An immutable input changed during validation." or "A caller-selected immutable input changed before its production read."
                        ? CardinalTraversalInvocationCode.InputChanged : CardinalTraversalInvocationCode.InputUnavailable :
                e.Code is StaticObjectErrorCode.InvalidSelector or StaticObjectErrorCode.MissingModule or StaticObjectErrorCode.AmbiguousModule
                    ? CardinalTraversalInvocationCode.InvalidSelection : CardinalTraversalInvocationCode.MalformedSource;
            throw new CardinalTraversalInvocationException(code, stage, e.SourceIndex, e.Offset);
        }
        catch (TerrainException e)
        {
            throw new CardinalTraversalInvocationException(e.Code == TerrainErrorCode.LimitExceeded ? CardinalTraversalInvocationCode.LimitExceeded :
                e.Code is TerrainErrorCode.InvalidSelector or TerrainErrorCode.MissingModuleEntry or TerrainErrorCode.AmbiguousModuleEntry or TerrainErrorCode.EntryIdentityMismatch
                    ? CardinalTraversalInvocationCode.InvalidSelection : CardinalTraversalInvocationCode.MalformedSource, CardinalTraversalInvocationStage.Terrain);
        }
        catch (DatException) { throw new CardinalTraversalInvocationException(CardinalTraversalInvocationCode.MalformedSource, CardinalTraversalInvocationStage.Selection); }
        catch (IOException) { throw new CardinalTraversalInvocationException(CardinalTraversalInvocationCode.InputUnavailable, CardinalTraversalInvocationStage.Selection); }
        catch (UnauthorizedAccessException) { throw new CardinalTraversalInvocationException(CardinalTraversalInvocationCode.InputUnavailable, CardinalTraversalInvocationStage.Selection); }
        catch (ObjectDisposedException) { throw new CardinalTraversalInvocationException(CardinalTraversalInvocationCode.InputUnavailable, CardinalTraversalInvocationStage.Selection); }
    }
}
