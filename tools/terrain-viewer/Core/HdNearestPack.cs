using System.Text;
using System.Text.Json.Nodes;
using Arcanum.Data.Hd;

namespace Arcanum.TerrainViewer.Core;

/// <summary>A diagnostic pack whose every HD texel is its parent Legacy texel (the nearest N×
/// replica, Legacy hard edge), written in the ARC-DATA-014 pack format. By ARC-RENDER-013
/// consequence 4 it must draw exactly the Legacy raster, so it tests the HD render path
/// without depending on any upscale method. It is a test fixture, not the pack build tool
/// (ARC-DEV-003): no recipe method runs, the recipe only records "nearest". Built from real
/// frames it holds derived pixels, so its directory must stay outside git (a temporary or
/// ignored local directory); tests use synthetic frames.</summary>
public static class HdNearestPack
{
    /// <summary>One frame to replicate: its HD question and its Legacy RGBA (w × h × 4 bytes
    /// hashing to the question). A terrain chunk question (ARC-RENDER-016) also carries its tile
    /// list and is written as a <c>terrainChunk</c> entry of the <c>ground</c> slice.</summary>
    public sealed record Frame(HdSpriteQuery Query, byte[] Rgba, HdReviewStatus Review = HdReviewStatus.Unreviewed, IReadOnlyList<string>? Tiles = null);

    /// <summary>A terrain chunk frame: the composed level-0 chunk, its question and its tiles.</summary>
    public static Frame Chunk(AreaTerrainChunks terrain, (int X, int Y) chunk)
    {
        var composed = terrain.Compose(chunk, 0);
        return new(HdFrameQueries.FromTerrainChunk(terrain, chunk, composed), composed, Tiles: HdFrameQueries.TerrainChunkTiles(terrain, chunk));
    }

    /// <summary>The nearest N× replica of a w × h RGBA frame, with an optional colour change
    /// on every opaque subtexel whose x offset is <paramref name="tintColumn"/> (a stand-in
    /// for an upscaler that adds detail; alpha is always the parent's).</summary>
    public static byte[] Replicate(ReadOnlySpan<byte> rgba, int width, int height, int scale, int tintColumn = -1, byte tint = 0,
        Func<int, int, byte>? parentTint = null)
    {
        if (width <= 0 || height <= 0 || scale < 1 || rgba.Length != (long)width * height * 4) throw new ArgumentException("Legacy RGBA must be width × height × 4 bytes.", nameof(rgba));
        var w = width * scale; var h = height * scale;
        var hd = new byte[checked(w * h * 4)];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var from = ((y / scale) * width + x / scale) * 4; var to = (y * w + x) * 4;
            rgba.Slice(from, 4).CopyTo(hd.AsSpan(to, 4));
            if (hd[to + 3] != 0 && x % scale == tintColumn) hd[to] = (byte)(hd[to] ^ tint);
            if (hd[to + 3] != 0 && parentTint is not null) hd[to] = (byte)(hd[to] ^ parentTint(x / scale, y / scale));
        }
        return hd;
    }

    /// <summary>Writes a pack to <paramref name="directory"/> (created; must not hold a pack
    /// yet). Duplicate questions are written once. Returns the directory.</summary>
    /// <param name="parentTint">Optional colour change as a function of the parent Legacy texel
    /// (x, y): the result is then the nearest replica of a recoloured Legacy frame, so a Legacy
    /// draw of that recoloured frame is its exact oracle (it detects a wrong crop even on
    /// uniform frames).</param>
    public static string Write(string directory, string id, int scale, IEnumerable<Frame> frames, HdReviewStatus review = HdReviewStatus.Unreviewed,
        int tintColumn = -1, byte tint = 0, Func<int, int, byte>? parentTint = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-z0-9][a-z0-9_-]{0,63}$")) throw new ArgumentException("Not a pack id.", nameof(id));
        ArgumentNullException.ThrowIfNull(frames);
        if (File.Exists(Path.Combine(directory, HdPack.ManifestFile))) throw new IOException("The directory already holds a pack.");
        var list = frames.ToArray();
        if (list.Any(frame => frame.Query.Chunk is not null && frame.Tiles is not { Count: > 0 })) throw new ArgumentException("A terrain chunk frame needs its tiles.", nameof(frames));
        Directory.CreateDirectory(directory);
        var recipe = Encoding.UTF8.GetBytes(Recipe(id, scale, list.Any(frame => frame.Query.Chunk is not null)));
        WriteFile(directory, "recipe.json", recipe);
        var entries = new JsonArray();
        // Sprites are unique by key (a key twice is a DuplicateKey pack), chunks by their whole question.
        var seen = new HashSet<object>();
        foreach (var frame in list)
        {
            if (!seen.Add(frame.Query.Chunk is null ? frame.Query.Key : frame.Query)) continue;
            var legacy = frame.Query.Legacy;
            if (HdFrameQueries.Sha256(frame.Rgba) != legacy.RgbaSha256) throw new ArgumentException("Legacy RGBA does not hash to its question.", nameof(frames));
            var png = HdPng.EncodeRgba(legacy.Width * scale, legacy.Height * scale, Replicate(frame.Rgba, legacy.Width, legacy.Height, scale, tintColumn, tint, parentTint));
            var file = $"frames/{entries.Count / 1000:D4}/{entries.Count % 1000:D3}.png";
            WriteFile(directory, file, png);
            if (frame.Query.Chunk is { } chunk)
            {
                entries.Add(new JsonObject
                {
                    ["kind"] = "terrainChunk", ["file"] = file, ["sha256"] = HdFrameQueries.Sha256(png), ["bytes"] = png.Length, ["slice"] = "ground",
                    ["chunk"] = new JsonObject { ["x"] = chunk.X, ["y"] = chunk.Y, ["size"] = AreaTerrainChunks.ChunkSize, ["padding"] = ChunkPadding },
                    ["tiles"] = new JsonArray(frame.Tiles!.Select(tile => (JsonNode)tile).ToArray()),
                    ["legacy"] = new JsonObject { ["width"] = legacy.Width, ["height"] = legacy.Height, ["rgbaSha256"] = legacy.RgbaSha256 },
                    ["review"] = ReviewName(frame.Review),
                });
                continue;
            }
            entries.Add(new JsonObject
            {
                ["kind"] = "sprite", ["file"] = file, ["sha256"] = HdFrameQueries.Sha256(png), ["bytes"] = png.Length, ["slice"] = "bodies",
                ["art"] = new JsonObject { ["sha256"] = frame.Query.Key.ArtSha256, ["view"] = frame.Query.Key.View, ["nameSha256"] = frame.Query.NameSha256 },
                ["group"] = frame.Query.Key.Group, ["frame"] = frame.Query.Key.Frame, ["palette"] = frame.Query.Key.Palette,
                ["legacy"] = new JsonObject { ["width"] = legacy.Width, ["height"] = legacy.Height, ["rgbaSha256"] = legacy.RgbaSha256 },
                ["review"] = ReviewName(frame.Review),
            });
        }
        var manifest = new JsonObject
        {
            ["schema"] = 1, ["kind"] = HdPack.Kind, ["id"] = id, ["scale"] = scale, ["review"] = ReviewName(review),
            ["recipe"] = new JsonObject { ["file"] = "recipe.json", ["sha256"] = HdFrameQueries.Sha256(recipe) },
            ["build"] = new JsonObject { ["builder"] = "terrain-viewer nearest fixture", ["builderVersion"] = "1",
                ["createdUtc"] = "2026-10-16T00:00:00Z", ["gpu"] = "none", ["os"] = "any" },
            ["entries"] = entries,
        };
        WriteFile(directory, HdPack.ManifestFile, Encoding.UTF8.GetBytes(manifest.ToJsonString()));
        return directory;
    }

    // The recorded padding of a chunk entry (the nearest replica needs no neighbour context).
    private const int ChunkPadding = 32;

    private static string Recipe(string id, int scale, bool terrain) => $$$"""
        {"schema":1,"kind":"arcanum-steamwoven-hd-recipe","id":"{{{id}}}","scale":{{{scale}}},
         "slices":[{"name":"bodies","map":"any","kinds":["player","critter","npc","scenery","container","item","wall"]}{{{(terrain ? ",{\"name\":\"ground\",\"map\":\"any\",\"kinds\":[\"terrainChunk\"]}" : "")}}}],
         "method":{"kind":"external","tool":"terrain-viewer-nearest","toolVersion":"1","nativeScale":{{{scale}}},"downscale":"none"},
         "alpha":{"mode":"legacy"}{{{(terrain ? $",\"terrain\":{{\"chunkSize\":{AreaTerrainChunks.ChunkSize},\"padding\":{ChunkPadding}}}" : "")}}}}
        """;

    private static string ReviewName(HdReviewStatus review) => review switch
    {
        HdReviewStatus.Approved => "approved", HdReviewStatus.Rejected => "rejected", _ => "unreviewed",
    };

    private static void WriteFile(string directory, string relative, byte[] bytes)
    {
        var path = Path.Combine([directory, .. relative.Split('/')]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }
}
