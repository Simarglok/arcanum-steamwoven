namespace Arcanum.Data.Hd;

public enum HdReviewStatus { Unreviewed, Approved, Rejected }
public enum HdEntryKind { Sprite, TerrainChunk, Portrait, WorldMap, MapZoomed, TownMap }

/// <summary>A pack's identity: a rebuild, a review mark or any other manifest change is a
/// new identity. HD texture caches are keyed by it (ARC-RENDER-013, Budgets).</summary>
public sealed record HdPackIdentity(string Id, int Scale, string ManifestSha256);

/// <param name="Builder">The program that wrote the pack and its version.</param>
/// <param name="Gpu">The device the method ran on (ARC-RES-033: output depends on it).</param>
public sealed record HdBuildRecord(string Builder, string BuilderVersion, DateTimeOffset CreatedUtc, string Gpu, string Os);

/// <summary>The install-independent part of an AREA/WORLD frame key (ARC-RENDER-013 frame
/// key without the source role, ordinal, offset, flags and sizes of the DAT entry, which
/// differ between installations): the SHA-256 of the whole ART entry payload, the decoded
/// view length, the physical group, the frame and the effective palette slot.</summary>
public readonly record struct HdSpriteKey(string ArtSha256, int View, int Group, int Frame, int Palette);

/// <summary>The Legacy frame an HD frame was made from, checked against this device's
/// decoded frame before the HD frame is used.</summary>
public sealed record HdLegacyFrame(int Width, int Height, string RgbaSha256);

/// <param name="Key">Sprites only.</param>
/// <param name="NameSha256">Sprites only: <c>AreaEntryIdentity.NameSha256</c> of the ART
/// entry (SHA-256 of its raw name bytes). Used only to tell a changed ART
/// (<see cref="HdFrameStatus.SourceMismatch"/>) from an absent one.</param>
/// <param name="AlphaMasked">ARC-UI-113, interface sprites only (<c>"alpha":"masked"</c> in the manifest): the owner's mask replaced the
/// frame's Legacy alpha, so the alpha check is <see cref="HdAlphaMode.Masked"/> and not the slice's policy.</param>
/// <param name="PortraitPath">ARC-UI-2865, portraits only (<c>"kind":"portrait"</c>): the lowercase path of the original's bitmap this entry redraws,
/// <c>portrait/&lt;name&gt;.bmp</c> (<see cref="HdPortraitKey"/>); <see cref="Key"/> and <see cref="NameSha256"/> are derived from it.</param>
/// <param name="MapPath">ARC-RENDER-3079, world-map bitmaps only (<c>"kind":"worldmap"</c> or <c>"map-zoomed"</c>): the lowercase path of the original's bitmap this entry
/// redraws, <c>worldmap/&lt;name&gt;.bmp</c> (<see cref="HdMapKey"/>); <see cref="Key"/> and <see cref="NameSha256"/> are derived from it. ARC-RENDER-3106, town-map tiles
/// (<c>"kind":"townmap"</c>): <c>townmap/&lt;town&gt;/&lt;town&gt;NNNNNN.bmp</c> (<see cref="HdTownMapKey"/>).</param>
/// <param name="ChunkX">Terrain chunks only (reserved entry kind, never bound in this
/// slice): chunk coordinates as <c>AreaTerrainChunks</c> uses them.</param>
/// <param name="TileRgbaSha256">Terrain chunks only: Legacy RGBA SHA-256 of every tile and
/// facade frame composed into the chunk, in plan order.</param>
public sealed record HdPackEntry(int Index, HdEntryKind Kind, string File, string Sha256, long Bytes, string Slice,
    HdSpriteKey? Key, string? NameSha256, HdLegacyFrame Legacy, HdReviewStatus Review,
    int? ChunkX = null, int? ChunkY = null, int? ChunkPadding = null, IReadOnlyList<string>? TileRgbaSha256 = null, bool AlphaMasked = false, string? PortraitPath = null, string? MapPath = null)
{
    /// <summary>True for the kinds that are keyed by an <see cref="HdSpriteKey"/> and resolved by <see cref="HdPack.ResolveSprite"/>: sprites, portraits, the world-map bitmaps and the town-map tiles.</summary>
    public bool IsSpriteLike => Kind is HdEntryKind.Sprite or HdEntryKind.Portrait or HdEntryKind.WorldMap or HdEntryKind.MapZoomed or HdEntryKind.TownMap;
}

public enum HdPackOpenStatus
{
    Opened, NotFound, Unreadable, TooLarge, Malformed, UnsupportedSchema, InvalidField, InvalidPath,
    DuplicateFile, DuplicateKey, TooManyEntries, RecipeMissing, RecipeMismatch, RecipeInvalid,
}
/// <summary>An opened pack or the typed reason it was refused. Opening reads the manifest
/// and the recipe only, never a frame file.</summary>
public sealed record HdPackOpenResult(HdPackOpenStatus Status, HdPack? Pack, string? Detail = null);

/// <summary>ARC-DEV-376: the outcome of <see cref="HdPack.CheckPrepared"/> (no pixels: the caller already holds them).</summary>
public sealed record HdPreparedResult(HdFrameStatus Status, HdInvalidReason Invalid, int Width, int Height);

/// <summary>Outcome of binding one frame key. Every status other than <see cref="Hd"/> is a
/// per-frame Legacy draw with that reason (ARC-RENDER-013, Frame binding).</summary>
public enum HdFrameStatus { Hd, PackRejected, FrameMissing, SourceMismatch, FrameRejected, Invalid }
public enum HdInvalidReason
{
    None, FileMissing, FileUnreadable, FileSize, HashMismatch, PngMalformed, PngUnsupported, TooLarge, SizeMismatch, AlphaMismatch,
}
/// <summary><see cref="Rgba"/> is exactly <c>Scale·w × Scale·h × 4</c> bytes when the status
/// is <see cref="HdFrameStatus.Hd"/>, null otherwise.</summary>
public sealed record HdFrameResult(HdFrameStatus Status, HdInvalidReason Invalid, int Scale, int Width, int Height, byte[]? Rgba)
{
    internal static HdFrameResult Refused(HdFrameStatus status, int scale, HdInvalidReason invalid = HdInvalidReason.None)
        => new(status, invalid, scale, 0, 0, null);
}

/// <summary>A device's question for one frame: the portable key, the ART name hash and the
/// Legacy frame as this installation decodes it. A terrain chunk question (ARC-RENDER-016)
/// carries <see cref="Chunk"/>; it is resolved by <see cref="HdPack.ResolveTerrainChunk"/>,
/// never by <see cref="HdPack.ResolveSprite"/>, and its <see cref="Key"/> is only a cache key
/// (<see cref="HdChunkKey.SpriteKey"/>).</summary>
public sealed record HdSpriteQuery(HdSpriteKey Key, string NameSha256, HdLegacyFrame Legacy)
{
    /// <summary>Terrain chunk questions only: the chunk and its composed tiles.</summary>
    public HdChunkKey? Chunk { get; init; }

    /// <summary>The question for one composed level-0 terrain chunk: <paramref name="legacy"/>
    /// is the composed chunk as this installation draws it (size × size), <paramref name="chunk"/>
    /// names the chunk and its tiles.</summary>
    public static HdSpriteQuery ForTerrainChunk(HdChunkKey chunk, HdLegacyFrame legacy)
    {
        ArgumentNullException.ThrowIfNull(chunk); ArgumentNullException.ThrowIfNull(legacy);
        return new(chunk.SpriteKey, "", legacy) { Chunk = chunk };
    }
}

/// <summary>ARC-RENDER-016: one terrain chunk as the scene plan composes it: its chunk
/// coordinates and the SHA-256 of its tile list (<see cref="TilesDigest"/> of the Legacy RGBA
/// SHA-256 of every composed tile and facade frame, in plan order).</summary>
public sealed record HdChunkKey(int X, int Y, string TilesSha256)
{
    /// <summary>The digest of an ordered tile hash list: SHA-256 of the lowercase hex hashes,
    /// each followed by a line feed.</summary>
    public static string TilesDigest(IEnumerable<string> tileRgbaSha256)
    {
        ArgumentNullException.ThrowIfNull(tileRgbaSha256);
        var text = string.Concat(tileRgbaSha256.Select(hash => hash + "\n"));
        return HdPackPath.Sha256(System.Text.Encoding.ASCII.GetBytes(text));
    }

    /// <summary>A cache key that never equals a sprite key (sprite views are at least 132).</summary>
    public HdSpriteKey SpriteKey => new(TilesSha256, -1, 0, 0, 0);
}

public sealed record HdIntegrityProblem(int Entry, string File, HdInvalidReason Reason);
/// <summary>Result of reading every file of a pack: present, of the declared size, with the
/// declared SHA-256 and a PNG header of exactly scale × the Legacy size.
/// <see cref="Problems"/> is capped by <see cref="HdPackOptions.MaxReportedProblems"/>;
/// <see cref="ProblemCounts"/> counts all of them.</summary>
public sealed record HdIntegrityReport(int Entries, int Valid, IReadOnlyDictionary<HdInvalidReason, int> ProblemCounts,
    IReadOnlyList<HdIntegrityProblem> Problems, bool Cancelled)
{
    public bool Intact => !Cancelled && Valid == Entries;
}

public sealed class HdPackOptions
{
    /// <summary>Accepted scales: 4 (the default recipe) and 2 (owner decision 2026-10-16,
    /// "1 + 3"); the format is written for any integer N ≥ 2.</summary>
    public IReadOnlyList<int> Scales { get; init; } = [2, 4];
    public long MaxManifestBytes { get; init; } = 256L * 1024 * 1024;
    public long MaxRecipeBytes { get; init; } = 1024 * 1024;
    /// <summary>Above the whole game's 337,513 frames for one palette per ART (ARC-RES-033).</summary>
    public int MaxEntries { get; init; } = 400_000;
    public long MaxFrameFileBytes { get; init; } = 256L * 1024 * 1024;
    /// <summary>Largest HD frame, in pixels (default 64 M = 256 MiB RGBA).</summary>
    public long MaxHdFramePixels { get; init; } = 1L << 26;
    public int MaxPacksPerCollection { get; init; } = 64;
    public int MaxReportedProblems { get; init; } = 100;

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Scales);
        if (Scales.Count == 0 || Scales.Any(s => s is < 2 or > 16)) throw new ArgumentOutOfRangeException(nameof(Scales), "Scales must be integers in 2..16.");
        if (MaxManifestBytes <= 0 || MaxRecipeBytes <= 0 || MaxEntries <= 0 || MaxFrameFileBytes <= 0 || MaxHdFramePixels <= 0
            || MaxPacksPerCollection <= 0 || MaxReportedProblems < 0)
            throw new ArgumentOutOfRangeException(nameof(HdPackOptions), "HD pack limits must be positive.");
    }
}

public enum HdPackLocationKind
{
    /// <summary>A folder of packs, one per subdirectory (the game's pack folder, or
    /// <c>local/hd-packs</c> in a checkout).</summary>
    Collection,
    /// <summary>One pack directory chosen by the user, anywhere.</summary>
    Pack,
}
public sealed record HdPackLocation(HdPackLocationKind Kind, string Path);
public sealed record HdDiscoveredPack(HdPackLocation Location, string Directory, HdPackOpenResult Result);
