using System.Runtime.CompilerServices;
using Arcanum.Data;
using Arcanum.Data.StaticObjects;
using Arcanum.Data.Terrain;
using Arcanum.Data.Traversal;

[assembly: InternalsVisibleTo("Arcanum.TerrainViewer.Tests")]

namespace Arcanum.TerrainViewer.Core;

// Internal-only admission seam: production always constructs the fixed original
// profile. Synthetic tests replace identity/selector admission, never DAT/ART
// parsing, decoding, retention, composition or coordinator ownership.
internal class TerrainLoadAdmission
{
    internal virtual InputFingerprint Module => new(CrashSiteProfile.ModuleSize, CrashSiteProfile.ModuleSha256);
    internal virtual InputFingerprint Resource => new(CrashSiteProfile.ResourceSize, CrashSiteProfile.ResourceSha256);
    internal virtual InputFingerprint ActorResource => new(CrashSiteProfile.ActorResourceSize, CrashSiteProfile.ActorResourceSha256);
    internal virtual InputFingerprint Prototypes => new(CrashSiteProfile.ActorPrototypeAggregateSize, CrashSiteProfile.ActorPrototypeAggregateSha256);
    internal virtual ActorDiagnosticProfile ActorProfile(TerrainViewerMode mode) => ActorDiagnosticProfile.ForMode(mode);
    internal virtual bool AuthenticateOriginalActorMetadata => true;
    internal virtual TerrainSectorSelector Selector => CrashSiteProfile.CreateSelector();
    internal virtual DatArchive OpenArchive(string path) => DatArchive.Open(path,
        new DatReaderOptions { MaxArchiveBytes = uint.MaxValue, MaxStoredEntryBytes = 1024 * 1024, MaxDecodedEntryBytes = 1024 * 1024 });
    internal virtual bool AcceptCensus(int tiles, int facades, int recovered, int lookups) =>
        (tiles, facades, recovered, lookups) == (3033, 1063, 457, 86);
    internal virtual StaticObjectInspection InspectStatics(TerrainViewerArguments arguments, DatArchive module, DatArchive resource) =>
        StaticObjectLoader.ValidateOriginalData(arguments.ModulePath, arguments.ResourcePath, arguments.PrototypePath!).Inspection;
    internal virtual CardinalTraversalSnapshot InspectTraversal(TerrainViewerArguments arguments, DatArchive module, DatArchive resource, long epoch) =>
        CardinalTraversalInspector.ValidateOriginalData(arguments.ModulePath, arguments.ResourcePath, arguments.PrototypePath!, epoch,
            policy: CardinalTraversalAdmissionPolicy.BoundedAdjacent);
    internal virtual bool AcceptTraversalIdentity(CardinalTraversalSnapshot snapshot, InputFingerprint module, InputFingerprint resource) =>
        snapshot.InputIdentity.Kind == CardinalTraversalInputKind.AuthenticatedOriginal &&
        snapshot.InputIdentity.OriginalIdentity is { } identity &&
        identity.Module.Length == module.Size && identity.Module.Sha256 == module.Sha256 &&
        identity.Resource.Length == resource.Size && identity.Resource.Sha256 == resource.Sha256 &&
        snapshot.InputIdentity.Sector.SectorX == Selector.SectorX && snapshot.InputIdentity.Sector.SectorY == Selector.SectorY;
}

// Tests may LOWER a budget to exercise the exact before-retention boundaries
// with tiny real frames. They cannot raise the production count or byte caps.
internal sealed record TerrainLoadBudget(int Terrain = 4096, int Statics = 5, int Actor = 184, int Total = 4285, long Bytes = 268435456)
{
    internal static TerrainLoadBudget Default { get; } = new();
    internal void Validate()
    {
        if (Terrain is < 0 or > 4096 || Statics is < 0 or > 5 || Actor is < 0 or > 184
            || Total is < 0 or > 4285 || Bytes is < 0 or > 268435456)
            throw new ArgumentOutOfRangeException(nameof(TerrainLoadBudget));
    }
    internal long Add(long bytes, int total, int domainCount, int domainLimit, int width, int height, TerrainViewerMode mode,
        ActorDiagnosticProfile? profile = null)
    {
        var result = TerrainCachePolicy.AddCheckedFrame(bytes, total, width, height, mode, profile);
        if (domainCount >= domainLimit || total >= Total || result > Bytes)
            throw new TerrainViewerException(new(TerrainViewerErrorCode.ResourceLimit));
        return result;
    }
}

internal sealed record TerrainLoadCheckpoint(string Domain, int RetainedCount, int TotalCount, long RgbaBytes);
