using System.Security.Cryptography;
using System.Text.Json;

namespace Arcanum.Data.Hd;

/// <summary>One opened, immutable HD pack (docs/formats/hd-pack.md). It is portable: every
/// path inside is relative, nothing names an OS, GPU or user folder, so the same directory
/// works wherever it is copied. Frames are bound per device by <see cref="ResolveSprite"/>,
/// which checks the HD frame against this installation's Legacy frame; nothing is compared
/// with reference HD hashes. Safe for concurrent use.</summary>
public sealed class HdPack
{
    public const string Kind = "arcanum-steamwoven-hd-pack";
    public const string ManifestFile = "manifest.json";

    private readonly Dictionary<HdSpriteKey, HdPackEntry> _sprites;
    // ART name hash -> the ART payload hashes the pack was built from under that name.
    private readonly Dictionary<string, HashSet<string>> _spriteArts;
    // ARC-RENDER-016: terrain chunk entries by chunk coordinates (one per slice that has it).
    private readonly Dictionary<(int X, int Y), HdPackEntry[]> _chunks;
    private readonly Dictionary<int, string> _chunkTiles; // entry index -> tile list digest
    private readonly HdPackOptions _options;

    private HdPack(string directory, HdPackIdentity identity, HdReviewStatus review, string recipeFile, string recipeSha256, HdRecipe recipe,
        HdBuildRecord build, IReadOnlyList<HdPackEntry> entries, HdPackOptions options)
    {
        Directory = directory; Identity = identity; Review = review; RecipeFile = recipeFile; RecipeSha256 = recipeSha256; Recipe = recipe;
        Build = build; Entries = entries; _options = options;
        _sprites = entries.Where(e => e.Kind is HdEntryKind.Sprite or HdEntryKind.Portrait).ToDictionary(e => e.Key!.Value);
        _spriteArts = entries.Where(e => e.Kind is HdEntryKind.Sprite or HdEntryKind.Portrait).GroupBy(e => e.NameSha256!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Key!.Value.ArtSha256).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
        var chunks = entries.Where(e => e.Kind == HdEntryKind.TerrainChunk).ToArray();
        _chunks = chunks.GroupBy(e => (e.ChunkX!.Value, e.ChunkY!.Value)).ToDictionary(g => g.Key, g => g.ToArray());
        _chunkTiles = chunks.ToDictionary(e => e.Index, e => HdChunkKey.TilesDigest(e.TileRgbaSha256!));
    }

    /// <summary>The full local directory the pack was opened from (runtime only; never
    /// written into a pack).</summary>
    public string Directory { get; }
    public HdPackIdentity Identity { get; }
    public string Id => Identity.Id;
    public int Scale => Identity.Scale;
    public HdReviewStatus Review { get; }
    public string RecipeFile { get; }
    public string RecipeSha256 { get; }
    public HdRecipe Recipe { get; }
    public HdBuildRecord Build { get; }
    public IReadOnlyList<HdPackEntry> Entries { get; }
    internal HdPackOptions Options => _options;
    public int SpriteCount => _sprites.Count;
    /// <summary>Terrain chunk entries, bound by <see cref="ResolveTerrainChunk"/> (ARC-RENDER-016).</summary>
    public int TerrainChunkCount => Entries.Count - _sprites.Count;
    /// <summary>Decoded HD RGBA bytes of all sprite entries (scale² · w · h · 4 each).</summary>
    public long SpriteRgbaBytes => _sprites.Values.Sum(e => (long)Scale * Scale * e.Legacy.Width * e.Legacy.Height * 4);

    /// <summary>ARC-UI-122: an optional local 16:9 main-menu plate, independent of sprite dimensions.</summary>
    public HdPngImage? ReadMainMenuBackdrop() => ReadMainMenuBackdropReport().Image;

    /// <summary>ARC-DEV-343: the same read with the outcome named, so a log can say which file of which pack was used (no path).</summary>
    public HdMainMenuBackdropRead ReadMainMenuBackdropReport()
    {
        if (Review == HdReviewStatus.Rejected) return new(null, "PackRejected", null, 0, 0);
        var bytes = ReadFile(Directory, "main-menu-16x9.png", 40 * 1024 * 1024, out _);
        if (bytes is null) return new(null, "NoFile", null, 0, 0);
        var sha12 = HdPackPath.Sha256(bytes)[..12];
        if (HdPng.ReadHeader(bytes).Header is not { } header) return new(null, "BadHeader", sha12, 0, 0);
        if (header.Width > 3840 || (long)header.Width * 9 != (long)header.Height * 16) return new(null, "WrongSize", sha12, header.Width, header.Height);
        var image = HdPng.Decode(bytes, 3840 * 2160).Image;
        if (image is null) return new(null, "BadImage", sha12, header.Width, header.Height);
        for (var i = 3; i < image.Rgba.Length; i += 4)
            if (image.Rgba[i] != byte.MaxValue) return new(null, "NotOpaque", sha12, header.Width, header.Height);
        return new(image, "Used", sha12, header.Width, header.Height);
    }

    public static HdPackOpenResult Open(string directory, HdPackOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        options ??= new(); options.Validate();
        var full = Path.GetFullPath(directory);
        if (!System.IO.Directory.Exists(full)) return new(HdPackOpenStatus.NotFound, null, "pack directory");
        var manifest = ReadFile(full, ManifestFile, options.MaxManifestBytes, out var manifestStatus);
        if (manifest is null) return new(manifestStatus, null, ManifestFile);
        try
        {
            using var document = JsonDocument.Parse(manifest, HdJson.Options);
            return Parse(full, manifest, document.RootElement, options);
        }
        catch (JsonException) { return new(HdPackOpenStatus.Malformed, null, "manifest is not JSON"); }
        catch (HdFormatException error)
        {
            return new(error.Error switch
            {
                HdFormatError.UnsupportedSchema => HdPackOpenStatus.UnsupportedSchema,
                HdFormatError.InvalidPath => HdPackOpenStatus.InvalidPath,
                HdFormatError.DuplicateFile => HdPackOpenStatus.DuplicateFile,
                HdFormatError.DuplicateKey => HdPackOpenStatus.DuplicateKey,
                HdFormatError.TooManyEntries => HdPackOpenStatus.TooManyEntries,
                HdFormatError.Malformed => HdPackOpenStatus.Malformed,
                _ => HdPackOpenStatus.InvalidField,
            }, null, error.Message);
        }
    }

    /// <summary>Lists packs at the given locations, in location order and, inside a
    /// collection, in ordinal directory-name order. Hidden directories (leading '.', such as
    /// an import's staging directory) are skipped. A missing location yields nothing; at most
    /// <see cref="HdPackOptions.MaxPacksPerCollection"/> packs are opened per collection.
    /// Every listed pack carries its own open result; one broken pack hides nothing else.</summary>
    public static IReadOnlyList<HdDiscoveredPack> Discover(IReadOnlyList<HdPackLocation> locations, HdPackOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(locations);
        options ??= new(); options.Validate();
        var found = new List<HdDiscoveredPack>();
        foreach (var location in locations)
        {
            ArgumentNullException.ThrowIfNull(location);
            ArgumentException.ThrowIfNullOrEmpty(location.Path);
            var root = Path.GetFullPath(location.Path);
            if (location.Kind == HdPackLocationKind.Pack)
            {
                if (System.IO.Directory.Exists(root)) found.Add(new(location, root, Open(root, options)));
                continue;
            }
            if (!System.IO.Directory.Exists(root)) continue;
            string[] children;
            try { children = System.IO.Directory.GetDirectories(root); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { continue; }
            // A manifest that is a (possibly dangling) link is listed, as unreadable.
            foreach (var child in children.Where(c => !Path.GetFileName(c).StartsWith('.')
                         && (File.Exists(Path.Combine(c, ManifestFile)) || new FileInfo(Path.Combine(c, ManifestFile)).LinkTarget is not null))
                         .Order(StringComparer.Ordinal).Take(options.MaxPacksPerCollection))
                found.Add(new(location, child, Open(child, options)));
        }
        return found;
    }

    /// <summary>Binds one frame key to its HD frame on this device, or says why that frame
    /// draws Legacy. <paramref name="legacyRgba"/> is this installation's decoded Legacy frame
    /// for the query (exactly w × h × 4 bytes hashing to the query's
    /// <see cref="HdLegacyFrame.RgbaSha256"/>); its alpha is the reference of the mask check.
    /// Never throws for pack content; throws only for a query that contradicts its own
    /// pixels (a caller error).</summary>
    public HdFrameResult ResolveSprite(HdSpriteQuery query, ReadOnlySpan<byte> legacyRgba)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Chunk is not null) throw new ArgumentException("A terrain chunk question is resolved by ResolveTerrainChunk.", nameof(query));
        return Resolve(query, legacyRgba, review: false, out _);
    }

    /// <summary>ARC-RENDER-016: binds one composed level-0 terrain chunk to its HD chunk, or
    /// says why it draws Legacy. A chunk entry matches when its coordinates, its composed Legacy
    /// frame (size and RGBA SHA-256) and its tile list all equal the question; entries of other
    /// slices at the same coordinates (another map) are skipped. No entry at those coordinates is
    /// <see cref="HdFrameStatus.FrameMissing"/>; entries there that all differ are
    /// <see cref="HdFrameStatus.SourceMismatch"/> (other tiles: a mod or another version). The
    /// file, PNG, size and mask checks are those of a sprite. <paramref name="legacyRgba"/> is
    /// the composed chunk this installation draws.</summary>
    public HdFrameResult ResolveTerrainChunk(HdSpriteQuery query, ReadOnlySpan<byte> legacyRgba)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Chunk is not { } chunk) throw new ArgumentException("Not a terrain chunk question.", nameof(query));
        CheckLegacy(query, legacyRgba);
        if (Review == HdReviewStatus.Rejected) return HdFrameResult.Refused(HdFrameStatus.PackRejected, Scale);
        if (!_chunks.TryGetValue((chunk.X, chunk.Y), out var candidates)) return HdFrameResult.Refused(HdFrameStatus.FrameMissing, Scale);
        var entry = candidates.FirstOrDefault(e => e.Legacy == query.Legacy && _chunkTiles[e.Index] == chunk.TilesSha256);
        if (entry is null) return HdFrameResult.Refused(HdFrameStatus.SourceMismatch, Scale);
        if (entry.Review == HdReviewStatus.Rejected) return HdFrameResult.Refused(HdFrameStatus.FrameRejected, Scale);
        return Bind(entry, legacyRgba, review: false, out _);
    }

    private static void CheckLegacy(HdSpriteQuery query, ReadOnlySpan<byte> legacyRgba)
    {
        ArgumentNullException.ThrowIfNull(query.Legacy);
        if (query.Legacy.Width <= 0 || query.Legacy.Height <= 0 || legacyRgba.Length != (long)query.Legacy.Width * query.Legacy.Height * 4)
            throw new ArgumentException("Legacy RGBA must be exactly width × height × 4 bytes.", nameof(legacyRgba));
        if (HdPackPath.Sha256(legacyRgba) != query.Legacy.RgbaSha256)
            throw new ArgumentException("Legacy RGBA does not hash to the query's RgbaSha256.", nameof(legacyRgba));
    }

    /// <summary>The before/after review's binding (ARC-RENDER-015): as
    /// <see cref="ResolveSprite"/>, but review marks do not refuse a frame (a rejected frame
    /// can be looked at again) and a failed alpha check still returns the pixels, with
    /// <paramref name="alphaMatches"/> false, so the reviewer sees why the game would refuse
    /// it. Never used to draw the game.</summary>
    internal HdFrameResult ResolveForReview(HdSpriteQuery query, ReadOnlySpan<byte> legacyRgba, out bool alphaMatches)
        => Resolve(query, legacyRgba, review: true, out alphaMatches);

    private HdFrameResult Resolve(HdSpriteQuery query, ReadOnlySpan<byte> legacyRgba, bool review, out bool alphaMatches)
    {
        alphaMatches = false;
        ArgumentNullException.ThrowIfNull(query);
        CheckLegacy(query, legacyRgba);
        var entry = Lookup(query, review, out var refused);
        return entry is null ? refused! : Bind(entry, legacyRgba, review, out alphaMatches);
    }

    // The key lookup shared by every sprite binding: the pack's review, the entry, its Legacy frame and its own review (none of it reads a file).
    private HdPackEntry? Lookup(HdSpriteQuery query, bool review, out HdFrameResult? refused)
    {
        refused = null;
        if (!review && Review == HdReviewStatus.Rejected) { refused = HdFrameResult.Refused(HdFrameStatus.PackRejected, Scale); return null; }
        if (!_sprites.TryGetValue(query.Key, out var entry))
        {
            // The pack knows this ART name but was built from other bytes (a mod or another
            // version): changed. Otherwise, including another frame of a known ART: missing.
            var changed = _spriteArts.TryGetValue(query.NameSha256, out var arts) && !arts.Contains(query.Key.ArtSha256);
            refused = HdFrameResult.Refused(changed ? HdFrameStatus.SourceMismatch : HdFrameStatus.FrameMissing, Scale);
            return null;
        }
        if (entry.Legacy != query.Legacy) { refused = HdFrameResult.Refused(HdFrameStatus.SourceMismatch, Scale); return null; }
        if (!review && entry.Review == HdReviewStatus.Rejected) { refused = HdFrameResult.Refused(HdFrameStatus.FrameRejected, Scale); return null; }
        return entry;
    }

    /// <summary>ARC-DEV-376: the SHA-256 of the PNG file the pack binds to <paramref name="query"/> (the key the optimized texture cache is named by), or
    /// null when the frame would not bind at all (pack or frame rejected, key absent, Legacy frame differs) or when its pack file is gone, is a link or
    /// no longer has the manifest's byte size (one stat; the file is neither read nor hashed), so a damaged pack is noticed with a cache present too.</summary>
    public string? SpriteFileSha256(HdSpriteQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Chunk is not null || Lookup(query, review: false, out _) is not { } entry) return null;
        try
        {
            if (HdPackPath.HasLink(Directory, entry.File)) return null;
            var info = new FileInfo(HdPackPath.Full(Directory, entry.File));
            return info.Exists && info.Length == entry.Bytes ? entry.Sha256 : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException) { return null; }
    }

    /// <summary>ARC-DEV-376: the checks of <see cref="ResolveSprite"/> for a frame whose HD pixels are already decoded (the level 0 of an optimized
    /// texture): the key, the Legacy frame, the review marks, the size and the alpha rule. It reads no file. The caller vouches that
    /// <paramref name="hdRgba"/> are the pixels of the PNG whose SHA-256 <see cref="SpriteFileSha256"/> returned (the cache is named by it), so
    /// <see cref="HdInvalidReason.FileMissing"/>, hash and PNG problems cannot arise here. ARC-DEV-382: with <paramref name="verdicts"/> a pass of the alpha rule
    /// is remembered by (PNG, Legacy frame, policy, rule version) and a later call for the same triple skips the per-texel pass (the Legacy pixels are still
    /// hashed against the query, about 1 ms for an 800 × 600 frame).</summary>
    public HdPreparedResult CheckPrepared(HdSpriteQuery query, ReadOnlySpan<byte> legacyRgba, ReadOnlySpan<byte> hdRgba, HdOptimizedCache? verdicts = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Chunk is not null) throw new ArgumentException("A terrain chunk question is not prepared.", nameof(query));
        CheckLegacy(query, legacyRgba);
        var entry = Lookup(query, review: false, out var refused);
        if (entry is null) return new(refused!.Status, refused.Invalid, 0, 0);
        var (hdWidth, hdHeight) = ((long)Scale * entry.Legacy.Width, (long)Scale * entry.Legacy.Height);
        if (hdWidth * hdHeight > _options.MaxHdFramePixels) return new(HdFrameStatus.Invalid, HdInvalidReason.TooLarge, 0, 0);
        if (hdRgba.Length != hdWidth * hdHeight * 4) return new(HdFrameStatus.Invalid, HdInvalidReason.SizeMismatch, 0, 0);
        var policy = AlphaOf(entry);
        // Everything the pass depends on besides the two pixel buffers (whose hashes are the PNG's and the Legacy frame's) is in the key; the manifest's byte size ties it to the file the manifest describes.
        var policyKey = $"{policy.Mode}:{policy.MinSoftIoUPermille}:{entry.Legacy.Width}x{entry.Legacy.Height}x{Scale}:{entry.Bytes}";
        if (verdicts is null || !verdicts.AlphaVerdictKnown(entry.Sha256, entry.Legacy.RgbaSha256, policyKey))
        {
            if (!AlphaMatches(hdRgba, legacyRgba, entry.Legacy.Width, entry.Legacy.Height, policy)) return new(HdFrameStatus.Invalid, HdInvalidReason.AlphaMismatch, 0, 0);
            verdicts?.RecordAlphaVerdict(entry.Sha256, entry.Legacy.RgbaSha256, policyKey);
        }
        return new(HdFrameStatus.Hd, HdInvalidReason.None, (int)hdWidth, (int)hdHeight);
    }

    /// <summary>ARC-DEV-376: the sprite entries of the slices that hold interface art (the ones the viewer draws through the optimized cache).</summary>
    public IEnumerable<HdPackEntry> InterfaceSprites
    {
        get
        {
            var slices = Recipe.Slices.Where(s => s.Kinds.Contains(HdSliceKind.Interface)).Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
            return Entries.Where(e => (e.Kind is HdEntryKind.Sprite or HdEntryKind.Portrait) && slices.Contains(e.Slice));
        }
    }

    /// <summary>ARC-DEV-376: the decoded RGBA of one entry's PNG after the file, size, SHA-256 and PNG checks of <see cref="ResolveSprite"/>, without the
    /// Legacy checks (the converter has no Legacy frame). Null with <paramref name="problem"/> when the file does not pass.</summary>
    public byte[]? DecodeFrame(HdPackEntry entry, out HdInvalidReason problem)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var (hdWidth, hdHeight) = ((long)Scale * entry.Legacy.Width, (long)Scale * entry.Legacy.Height);
        if (hdWidth * hdHeight > _options.MaxHdFramePixels) { problem = HdInvalidReason.TooLarge; return null; }
        var bytes = ReadFrameFile(entry, out problem);
        if (bytes is null) return null;
        var header = HdPng.ReadHeader(bytes);
        if (header.Status != HdPngStatus.Decoded) { problem = PngReason(header.Status); return null; }
        if (header.Header!.Width != hdWidth || header.Header.Height != hdHeight) { problem = HdInvalidReason.SizeMismatch; return null; }
        var image = HdPng.Decode(bytes, _options.MaxHdFramePixels);
        if (image.Status != HdPngStatus.Decoded) { problem = PngReason(image.Status); return null; }
        problem = HdInvalidReason.None;
        return image.Image!.Rgba;
    }

    // The file, PNG, size and mask checks shared by sprites and terrain chunks.
    private HdFrameResult Bind(HdPackEntry entry, ReadOnlySpan<byte> legacyRgba, bool review, out bool alphaMatches)
    {
        alphaMatches = false;
        var (hdWidth, hdHeight) = ((long)Scale * entry.Legacy.Width, (long)Scale * entry.Legacy.Height);
        if (hdWidth * hdHeight > _options.MaxHdFramePixels) return Invalid(HdInvalidReason.TooLarge);
        var bytes = ReadFrameFile(entry, out var fileProblem);
        if (bytes is null) return Invalid(fileProblem);
        var header = HdPng.ReadHeader(bytes);
        if (header.Status != HdPngStatus.Decoded) return Invalid(PngReason(header.Status));
        if (header.Header!.Width != hdWidth || header.Header.Height != hdHeight) return Invalid(HdInvalidReason.SizeMismatch);
        var image = HdPng.Decode(bytes, _options.MaxHdFramePixels);
        if (image.Status != HdPngStatus.Decoded) return Invalid(PngReason(image.Status));
        alphaMatches = AlphaMatches(image.Image!.Rgba, legacyRgba, entry.Legacy.Width, entry.Legacy.Height, AlphaOf(entry));
        if (!alphaMatches && !review) return Invalid(HdInvalidReason.AlphaMismatch);
        return new(HdFrameStatus.Hd, HdInvalidReason.None, Scale, (int)hdWidth, (int)hdHeight, image.Image.Rgba);
    }

    /// <summary>Reads every entry file (no Legacy data needed): presence, size, SHA-256 and
    /// PNG header size. Used by import and by a pack check; the per-frame mask check against
    /// the installation happens in <see cref="ResolveSprite"/>.</summary>
    public HdIntegrityReport VerifyIntegrity(CancellationToken token = default)
    {
        var counts = new Dictionary<HdInvalidReason, int>();
        var problems = new List<HdIntegrityProblem>();
        var valid = 0;
        foreach (var entry in Entries)
        {
            if (token.IsCancellationRequested) return new(Entries.Count, valid, counts, problems, true);
            var reason = CheckFile(entry);
            if (reason == HdInvalidReason.None) { valid++; continue; }
            counts[reason] = counts.GetValueOrDefault(reason) + 1;
            if (problems.Count < _options.MaxReportedProblems) problems.Add(new(entry.Index, entry.File, reason));
        }
        return new(Entries.Count, valid, counts, problems, false);
    }

    internal HdInvalidReason CheckFile(HdPackEntry entry)
    {
        if ((long)Scale * entry.Legacy.Width * Scale * entry.Legacy.Height > _options.MaxHdFramePixels) return HdInvalidReason.TooLarge;
        var bytes = ReadFrameFile(entry, out var problem);
        if (bytes is null) return problem;
        var header = HdPng.ReadHeader(bytes);
        if (header.Status != HdPngStatus.Decoded) return PngReason(header.Status);
        return header.Header!.Width == (long)Scale * entry.Legacy.Width && header.Header.Height == (long)Scale * entry.Legacy.Height
            ? HdInvalidReason.None : HdInvalidReason.SizeMismatch;
    }

    private HdFrameResult Invalid(HdInvalidReason reason) => HdFrameResult.Refused(HdFrameStatus.Invalid, Scale, reason);

    private static HdInvalidReason PngReason(HdPngStatus status) => status switch
    {
        HdPngStatus.Unsupported => HdInvalidReason.PngUnsupported,
        HdPngStatus.TooLarge => HdInvalidReason.TooLarge,
        _ => HdInvalidReason.PngMalformed,
    };

    // Legacy alpha: every HD texel has exactly its parent Legacy texel's alpha (hard edge).
    // Soft alpha (the default recipe): no HD alpha farther than one Legacy texel from the
    // Legacy silhouette (parent or one of its 8 neighbours opaque), and the HD mask
    // (alpha >= 128) and the nearest Legacy mask have IoU >= the recipe's minimum.
    // Legacy-smooth alpha (ARC-UI-111): the Legacy silhouette with an anti-aliased edge. The rules are in HdAlphaCheck.
    // ARC-DEV-242: a slice may carry its own policy (interface art keeps Legacy alpha beside soft sprites).
    // ARC-UI-113: an entry may carry its own "masked" mode (interface art whose alpha is the owner's mask, a subset of the Legacy one).
    private HdAlphaPolicy AlphaOf(HdPackEntry entry)
        => entry.AlphaMasked ? new(HdAlphaMode.Masked, 1000) : Recipe.Slices.FirstOrDefault(s => string.Equals(s.Name, entry.Slice, StringComparison.Ordinal))?.Alpha ?? Recipe.Alpha;

    private bool AlphaMatches(ReadOnlySpan<byte> hd, ReadOnlySpan<byte> legacy, int width, int height, HdAlphaPolicy policy)
        => HdAlphaCheck.Matches(hd, legacy, width, height, Scale, policy);

    private byte[]? ReadFrameFile(HdPackEntry entry, out HdInvalidReason problem)
    {
        var bytes = ReadFile(Directory, entry.File, _options.MaxFrameFileBytes, out var status);
        if (bytes is null)
        {
            problem = status switch
            {
                HdPackOpenStatus.NotFound => HdInvalidReason.FileMissing,
                HdPackOpenStatus.TooLarge => HdInvalidReason.FileSize,
                _ => HdInvalidReason.FileUnreadable,
            };
            return null;
        }
        problem = bytes.LongLength != entry.Bytes ? HdInvalidReason.FileSize
            : HdPackPath.Sha256(bytes) != entry.Sha256 ? HdInvalidReason.HashMismatch : HdInvalidReason.None;
        return problem == HdInvalidReason.None ? bytes : null;
    }

    /// <summary>Reads a pack file by its validated relative path. Refuses (as unreadable) a
    /// file or any directory below the pack root that is a symbolic link or junction, so a
    /// pack cannot reach outside itself.</summary>
    internal static byte[]? ReadFile(string root, string relative, long maxBytes, out HdPackOpenStatus status)
    {
        var path = HdPackPath.Full(root, relative);
        try
        {
            // The link check comes first: File.Exists follows links, so a dangling link is
            // a link (unreadable), not a missing file.
            if (HdPackPath.HasLink(root, relative)) { status = HdPackOpenStatus.Unreadable; return null; }
            if (!File.Exists(path)) { status = HdPackOpenStatus.NotFound; return null; }
            // Every pack file is a non-empty regular file. A FIFO or device reports length 0
            // without being opened (opening a FIFO would block), so it is refused here.
            var length = new FileInfo(path).Length;
            if (length == 0) { status = HdPackOpenStatus.Unreadable; return null; }
            if (length > maxBytes) { status = HdPackOpenStatus.TooLarge; return null; }
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > maxBytes) { status = HdPackOpenStatus.TooLarge; return null; }
            var bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1) { status = HdPackOpenStatus.Unreadable; return null; }
            status = HdPackOpenStatus.Opened;
            return bytes;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            status = HdPackOpenStatus.Unreadable;
            return null;
        }
    }

    private static readonly Dictionary<string, HdReviewStatus> Reviews = new(StringComparer.Ordinal)
    { ["unreviewed"] = HdReviewStatus.Unreviewed, ["approved"] = HdReviewStatus.Approved, ["rejected"] = HdReviewStatus.Rejected };
    // ARC-UI-113: the one entry-level alpha mode ("masked"); the value says whether the entry is masked.
    private static readonly Dictionary<string, bool> EntryAlphas = new(StringComparer.Ordinal) { ["masked"] = true };
    private static readonly Dictionary<string, HdEntryKind> EntryKinds = new(StringComparer.Ordinal)
    { ["sprite"] = HdEntryKind.Sprite, ["terrainChunk"] = HdEntryKind.TerrainChunk, ["portrait"] = HdEntryKind.Portrait };

    internal static HdPackOpenResult Parse(string directory, byte[] manifestBytes, JsonElement element, HdPackOptions options)
    {
        var root = HdJson.Object(element, "pack", ["schema", "kind", "id", "scale", "review", "recipe", "build", "entries"]);
        HdJson.Schema(root, Kind);
        var id = HdJson.Matching(root["id"], "pack.id", HdJson.IdPattern());
        var scale = HdJson.Int(root["scale"], "pack.scale", 2, 16);
        if (!options.Scales.Contains(scale)) throw HdJson.Invalid("pack.scale", "is not an accepted scale");
        var review = HdJson.Enum(root["review"], "pack.review", Reviews);

        var r = HdJson.Object(root["recipe"], "pack.recipe", ["file", "sha256"]);
        var recipeFile = HdPackPath.Validate(HdJson.String(r["file"], "pack.recipe.file"), "pack.recipe.file", ".json");
        if (recipeFile == ManifestFile) throw new HdFormatException(HdFormatError.InvalidPath, "pack.recipe.file is the manifest");
        var recipeSha = HdJson.Digest(r["sha256"], "pack.recipe.sha256");

        var b = HdJson.Object(root["build"], "pack.build", ["builder", "builderVersion", "createdUtc", "gpu", "os"]);
        var build = new HdBuildRecord(HdJson.Label(b["builder"], "pack.build.builder"), HdJson.Label(b["builderVersion"], "pack.build.builderVersion"),
            HdJson.Utc(b["createdUtc"], "pack.build.createdUtc"), HdJson.Label(b["gpu"], "pack.build.gpu"), HdJson.Label(b["os"], "pack.build.os"));

        if (root["entries"].ValueKind != JsonValueKind.Array) throw HdJson.Invalid("pack.entries", "must be an array");
        if (root["entries"].GetArrayLength() > options.MaxEntries)
            throw new HdFormatException(HdFormatError.TooManyEntries, $"{root["entries"].GetArrayLength()} entries");

        // The recipe is read only after the manifest's own shape is valid.
        var recipeBytes = ReadFile(directory, recipeFile, options.MaxRecipeBytes, out var recipeStatus);
        if (recipeBytes is null)
            return new(recipeStatus == HdPackOpenStatus.NotFound ? HdPackOpenStatus.RecipeMissing : recipeStatus, null, recipeFile);
        if (HdPackPath.Sha256(recipeBytes) != recipeSha) return new(HdPackOpenStatus.RecipeMismatch, null, recipeFile);
        var recipe = HdRecipeReader.Parse(recipeBytes, options);
        if (recipe.Status != HdRecipeStatus.Parsed) return new(HdPackOpenStatus.RecipeInvalid, null, recipe.Detail);
        if (recipe.Recipe!.Scale != scale) return new(HdPackOpenStatus.RecipeInvalid, null, "recipe scale differs from pack scale");

        var slices = recipe.Recipe.Slices.ToDictionary(s => s.Name, StringComparer.Ordinal);
        var files = new HashSet<string>(StringComparer.Ordinal) { ManifestFile, recipeFile };
        var keys = new HashSet<HdSpriteKey>();
        var chunks = new HashSet<(string, int, int)>();
        var entries = new List<HdPackEntry>(root["entries"].GetArrayLength());
        var index = 0;
        foreach (var item in root["entries"].EnumerateArray())
        {
            var entry = ParseEntry(item, index, recipe.Recipe, slices);
            if (!files.Add(entry.File)) throw new HdFormatException(HdFormatError.DuplicateFile, $"pack.entries[{index}].file");
            if (entry.Key is { } key && !keys.Add(key)) throw new HdFormatException(HdFormatError.DuplicateKey, $"pack.entries[{index}]");
            if (entry.Kind == HdEntryKind.TerrainChunk && !chunks.Add((entry.Slice, entry.ChunkX!.Value, entry.ChunkY!.Value)))
                throw new HdFormatException(HdFormatError.DuplicateKey, $"pack.entries[{index}] repeats a chunk of its slice");
            entries.Add(entry);
            index++;
        }
        var identity = new HdPackIdentity(id, scale, HdPackPath.Sha256(manifestBytes));
        return new(HdPackOpenStatus.Opened, new HdPack(directory, identity, review, recipeFile, recipeSha, recipe.Recipe, build, entries, options));
    }

    private static HdPackEntry ParseEntry(JsonElement item, int index, HdRecipe recipe, IReadOnlyDictionary<string, HdSlice> slices)
    {
        var path = $"pack.entries[{index}]";
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("kind", out var kindElement))
            throw HdJson.Invalid(path + ".kind", "missing");
        var kind = HdJson.Enum(kindElement, path + ".kind", EntryKinds);
        string[] common = ["kind", "file", "sha256", "bytes", "slice", "legacy", "review"];
        var e = kind switch
        {
            HdEntryKind.Sprite => HdJson.Object(item, path, [.. common, "art", "group", "frame", "palette"], "alpha"),
            HdEntryKind.Portrait => HdJson.Object(item, path, [.. common, "portrait"]),
            _ => HdJson.Object(item, path, [.. common, "chunk", "tiles"]),
        };
        var file = HdPackPath.Validate(HdJson.String(e["file"], path + ".file"), path + ".file", ".png");
        var sha = HdJson.Digest(e["sha256"], path + ".sha256");
        var bytes = HdJson.Long(e["bytes"], path + ".bytes", 1, long.MaxValue);
        var sliceName = HdJson.Matching(e["slice"], path + ".slice", HdJson.IdPattern());
        if (!slices.TryGetValue(sliceName, out var slice)) throw HdJson.Invalid(path + ".slice", "is not a recipe slice");
        var review = HdJson.Enum(e["review"], path + ".review", Reviews);
        var l = HdJson.Object(e["legacy"], path + ".legacy", ["width", "height", "rgbaSha256"]);
        var legacy = new HdLegacyFrame(HdJson.Int(l["width"], path + ".legacy.width", 1, 65535),
            HdJson.Int(l["height"], path + ".legacy.height", 1, 65535), HdJson.Digest(l["rgbaSha256"], path + ".legacy.rgbaSha256"));

        if (kind == HdEntryKind.Portrait)
        {
            // ARC-UI-2865: a portrait redraw is interface art: its slice is one that holds interface art, so the slice's Legacy alpha rule applies (the bitmap is opaque, and so must the redraw be).
            if (!slice.Kinds.Contains(HdSliceKind.Interface)) throw HdJson.Invalid(path + ".slice", "does not select interface");
            var p = HdJson.Object(e["portrait"], path + ".portrait", ["path"]);
            var text = HdJson.String(p["path"], path + ".portrait.path");
            if (!HdPortraitKey.TryPath(text, out var portraitPath) || portraitPath != text) throw HdJson.Invalid(path + ".portrait.path", "is not portrait/<name>.bmp (lowercase [a-z0-9_-], at most 48 characters)");
            return new(index, kind, file, sha, bytes, sliceName, HdPortraitKey.Key(portraitPath), HdPortraitKey.NameSha256(portraitPath), legacy, review, PortraitPath: portraitPath);
        }

        if (kind == HdEntryKind.Sprite)
        {
            if (slice.Kinds.All(k => k == HdSliceKind.TerrainChunk)) throw HdJson.Invalid(path + ".slice", "selects no sprite kind");
            var art = HdJson.Object(e["art"], path + ".art", ["sha256", "view", "nameSha256"]);
            var key = new HdSpriteKey(HdJson.Digest(art["sha256"], path + ".art.sha256"), HdJson.Int(art["view"], path + ".art.view", 132, int.MaxValue),
                HdJson.Int(e["group"], path + ".group", 0, 7), HdJson.Int(e["frame"], path + ".frame", 0, 4095), HdJson.Int(e["palette"], path + ".palette", 0, 3));
            var masked = e.TryGetValue("alpha", out var alphaElement) && HdJson.Enum(alphaElement, path + ".alpha", EntryAlphas);
            if (masked && !slice.Kinds.Contains(HdSliceKind.Interface)) throw HdJson.Invalid(path + ".alpha", "applies only to an interface slice");
            return new(index, kind, file, sha, bytes, sliceName, key, HdJson.Digest(art["nameSha256"], path + ".art.nameSha256"), legacy, review, AlphaMasked: masked);
        }

        if (!slice.Kinds.Contains(HdSliceKind.TerrainChunk)) throw HdJson.Invalid(path + ".slice", "does not select terrainChunk");
        var c = HdJson.Object(e["chunk"], path + ".chunk", ["x", "y", "size", "padding"]);
        const int coordinate = 1 << 20;
        var x = HdJson.Int(c["x"], path + ".chunk.x", -coordinate, coordinate);
        var y = HdJson.Int(c["y"], path + ".chunk.y", -coordinate, coordinate);
        var size = HdJson.Int(c["size"], path + ".chunk.size", 1, 65535);
        var padding = HdJson.Int(c["padding"], path + ".chunk.padding", 0, 65535);
        if (size != recipe.Terrain!.ChunkSize || padding != recipe.Terrain.Padding) throw HdJson.Invalid(path + ".chunk", "differs from the recipe's terrain");
        if (legacy.Width != size || legacy.Height != size) throw HdJson.Invalid(path + ".legacy", "must be the chunk size");
        var tiles = HdJson.Array(e["tiles"], path + ".tiles", 1, 4096).Select((t, i) => HdJson.Digest(t, $"{path}.tiles[{i}]")).ToArray();
        return new(index, kind, file, sha, bytes, sliceName, null, null, legacy, review, x, y, padding, tiles);
    }
}
