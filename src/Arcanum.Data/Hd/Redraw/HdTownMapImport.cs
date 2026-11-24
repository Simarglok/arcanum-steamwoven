using System.Text.Json;
using Arcanum.Data.Characters;

namespace Arcanum.Data.Hd;

/// <summary>One line of a town-map plan: the original's tile (<c>townmap/ashbury/ashbury000748.bmp</c>) and the local image that redraws it (path relative to the plan file).</summary>
public sealed record HdTownMapPlanItem(string Bmp, string Image);

/// <summary>
/// ARC-RENDER-3106: the local plan of a town-map import (kind <c>arcanum-steamwoven-hd-townmap-plan</c>, schema 1): the pack id, the scale and the items.
/// <c>hd_pack.py townmap-plan</c> writes it from the tiles of the town render (<c>tools/hd_pack/town_render.py</c>); the plan names no original pixel.
/// </summary>
public sealed record HdTownMapPlan(string PackId, int Scale, IReadOnlyList<HdTownMapPlanItem> Items)
{
    public const string Kind = "arcanum-steamwoven-hd-townmap-plan";

    public static HdTownMapPlan Parse(ReadOnlySpan<byte> json)
    {
        try
        {
            using var document = JsonDocument.Parse(json.ToArray());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.GetProperty("kind").GetString() != Kind || root.GetProperty("schema").GetInt32() != 1)
                throw new FormatException("not a town-map plan (kind, schema 1)");
            var packId = root.GetProperty("packId").GetString() ?? throw new FormatException("packId");
            var scale = root.GetProperty("scale").GetInt32();
            var items = root.GetProperty("items").EnumerateArray()
                .Select(item => new HdTownMapPlanItem(item.GetProperty("bmp").GetString() ?? throw new FormatException("bmp"),
                    item.GetProperty("image").GetString() ?? throw new FormatException("image"))).ToArray();
            if (items.Length == 0) throw new FormatException("no items");
            return new(packId, scale, items);
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        { throw new FormatException("town-map plan: " + error.Message, error); }
    }
}

public sealed record HdTownMapBuild(IReadOnlyList<HdBuiltTownTile> Tiles, IReadOnlyList<string> Problems) { public bool Ok => Problems.Count == 0; }

/// <summary>ARC-RENDER-3106: builds the pack's town-map tiles: each redraw must be exactly <c>scale ×</c> the size of the Legacy tile it redraws (64 × 32, so 256 × 128 at a 4× pack: render scale 1.0)
/// and is taken as it is (no registration or lock: a rendered town picture's own pixels are the entry), written fully opaque. A town has thousands of tiles, so every image is read, checked and let go
/// here, and read again when the writer asks for its pixels (<see cref="HdBuiltTownTile.Redraw"/>): only one decoded tile is held at a time.</summary>
public static class HdTownMapImport
{
    public static HdTownMapBuild Build(HdTownMapPlan plan, Func<string, byte[]?> legacyBitmap, Func<string, HdRgbImage?> image)
    {
        ArgumentNullException.ThrowIfNull(plan); ArgumentNullException.ThrowIfNull(legacyBitmap); ArgumentNullException.ThrowIfNull(image);
        var built = new List<HdBuiltTownTile>(); var problems = new List<string>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in plan.Items)
        {
            if (!HdTownMapKey.TryPath(item.Bmp, out var path)) { problems.Add($"{item.Bmp}: not a town-map tile path (townmap/<town>/<town>NNNNNN.bmp)"); continue; }
            if (!seen.Add(path)) { problems.Add($"{path}: listed twice"); continue; }
            var bmp = legacyBitmap(path);
            if (bmp is null) { problems.Add($"{path}: the original tile is not in the game"); continue; }
            var legacy = PortraitBitmap.Decode(bmp);
            if (legacy is null) { problems.Add($"{path}: the original tile is not one the reader takes"); continue; }
            if (legacy.Width != HdTownMapKey.TileWidth || legacy.Height != HdTownMapKey.TileHeight)
            { problems.Add($"{path}: the original tile is {legacy.Width}x{legacy.Height}, not {HdTownMapKey.TileWidth}x{HdTownMapKey.TileHeight}"); continue; }
            var (width, height) = (legacy.Width * plan.Scale, legacy.Height * plan.Scale);
            var imagePath = item.Image;
            byte[]? Redraw()
            {
                var redraw = image(imagePath);
                if (redraw is null || redraw.Width != width || redraw.Height != height) return null;
                var rgba = new byte[width * height * 4];
                for (var i = 0; i < width * height; i++)
                {
                    for (var c = 0; c < 3; c++) rgba[i * 4 + c] = (byte)Math.Clamp((int)Math.Round(redraw.Rgb[i * 3 + c]), 0, 255);
                    rgba[i * 4 + 3] = byte.MaxValue;
                }
                return rgba;
            }
            var checkedImage = image(imagePath);
            if (checkedImage is null) { problems.Add($"{path}: image {Path.GetFileName(imagePath)} not read"); continue; }
            if (checkedImage.Width != width || checkedImage.Height != height)
            { problems.Add($"{path}: image is {checkedImage.Width}x{checkedImage.Height}, expected {width}x{height}"); continue; }
            built.Add(new(path, legacy.Width, legacy.Height, legacy.Rgba, Redraw));
        }
        return new(built, problems);
    }
}
