using System.Text.Json;
using Arcanum.Data.Characters;

namespace Arcanum.Data.Hd;

/// <summary>One line of a world-map plan: the original's bitmap (<c>worldmap/smallmapchunks001.bmp</c>, <c>worldmap/map_zoomed.bmp</c>) and the local image that redraws it (path relative to the plan file).</summary>
public sealed record HdMapPlanItem(string Bmp, string Image);

/// <summary>
/// ARC-RENDER-3079: the local plan of a world-map import (kind <c>arcanum-steamwoven-hd-map-plan</c>, schema 1): the pack id, the scale and the items.
/// <c>hd_pack.py maps-plan</c> writes it from a delivery folder; the plan names no original pixel. Which entry kind an item takes follows from the size of the
/// original bitmap it redraws (250 × 250: <c>worldmap</c>, 365 × 365: <c>map-zoomed</c>).
/// </summary>
public sealed record HdMapPlan(string PackId, int Scale, IReadOnlyList<HdMapPlanItem> Items)
{
    public const string Kind = "arcanum-steamwoven-hd-map-plan";

    public static HdMapPlan Parse(ReadOnlySpan<byte> json)
    {
        try
        {
            using var document = JsonDocument.Parse(json.ToArray());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.GetProperty("kind").GetString() != Kind || root.GetProperty("schema").GetInt32() != 1)
                throw new FormatException("not a world-map plan (kind, schema 1)");
            var packId = root.GetProperty("packId").GetString() ?? throw new FormatException("packId");
            var scale = root.GetProperty("scale").GetInt32();
            var items = root.GetProperty("items").EnumerateArray()
                .Select(item => new HdMapPlanItem(item.GetProperty("bmp").GetString() ?? throw new FormatException("bmp"),
                    item.GetProperty("image").GetString() ?? throw new FormatException("image"))).ToArray();
            if (items.Length == 0) throw new FormatException("no items");
            return new(packId, scale, items);
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        { throw new FormatException("world-map plan: " + error.Message, error); }
    }
}

public sealed record HdMapBuild(IReadOnlyList<HdBuiltMapBitmap> Maps, IReadOnlyList<string> Problems) { public bool Ok => Problems.Count == 0; }

/// <summary>ARC-RENDER-3079: builds the pack's world-map frames: each redraw must be exactly <c>scale ×</c> the size of the Legacy bitmap it redraws (so a multiple of 250 for a tile,
/// of 365 for the continent picture) and is taken as it is (no registration or lock: a map is a whole picture, the redraw's own pixels are the owner's choice), written fully opaque.</summary>
public static class HdMapImport
{
    public static HdMapBuild Build(HdMapPlan plan, Func<string, byte[]?> legacyBitmap, Func<string, HdRgbImage?> image)
    {
        ArgumentNullException.ThrowIfNull(plan); ArgumentNullException.ThrowIfNull(legacyBitmap); ArgumentNullException.ThrowIfNull(image);
        var built = new List<HdBuiltMapBitmap>(); var problems = new List<string>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in plan.Items)
        {
            if (!HdMapKey.TryPath(item.Bmp, out var path)) { problems.Add($"{item.Bmp}: not a world-map bitmap path (worldmap/<name>.bmp)"); continue; }
            if (!seen.Add(path)) { problems.Add($"{path}: listed twice"); continue; }
            var bmp = legacyBitmap(path);
            if (bmp is null) { problems.Add($"{path}: the original bitmap is not in the game"); continue; }
            var legacy = PortraitBitmap.Decode(bmp);
            if (legacy is null) { problems.Add($"{path}: the original bitmap is not one the reader takes"); continue; }
            if (HdMapKey.KindOf(legacy.Width, legacy.Height) is not { } kind) { problems.Add($"{path}: the original bitmap is {legacy.Width}x{legacy.Height}, not a world-map tile (250x250) or the continent picture (365x365)"); continue; }
            var redraw = image(item.Image);
            if (redraw is null) { problems.Add($"{path}: image {Path.GetFileName(item.Image)} not read"); continue; }
            if (redraw.Width != legacy.Width * plan.Scale || redraw.Height != legacy.Height * plan.Scale)
            { problems.Add($"{path}: image is {redraw.Width}x{redraw.Height}, expected {legacy.Width * plan.Scale}x{legacy.Height * plan.Scale}"); continue; }
            var rgba = new byte[redraw.Width * redraw.Height * 4];
            for (var i = 0; i < redraw.Width * redraw.Height; i++)
            {
                for (var c = 0; c < 3; c++) rgba[i * 4 + c] = (byte)Math.Clamp((int)Math.Round(redraw.Rgb[i * 3 + c]), 0, 255);
                rgba[i * 4 + 3] = byte.MaxValue;
            }
            built.Add(new(kind, path, legacy.Width, legacy.Height, legacy.Rgba, rgba));
        }
        return new(built, problems);
    }
}
