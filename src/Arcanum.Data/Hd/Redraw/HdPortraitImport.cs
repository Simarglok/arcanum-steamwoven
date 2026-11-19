using System.Text;
using System.Text.Json;
using Arcanum.Data.Characters;

namespace Arcanum.Data.Hd;

/// <summary>One line of a portrait plan: the original's bitmap (<c>portrait/hum1_b.bmp</c>) and the local image that redraws it (path relative to the plan file).</summary>
public sealed record HdPortraitPlanItem(string Bmp, string Image);

/// <summary>
/// ARC-UI-2865: the local plan of a portrait import (kind <c>arcanum-steamwoven-hd-portrait-plan</c>, schema 1): the pack id, the scale and the
/// items. <c>hd_pack.py portraits-plan</c> writes it from the Codex deliveries; the plan names no original pixel.
/// </summary>
public sealed record HdPortraitPlan(string PackId, int Scale, IReadOnlyList<HdPortraitPlanItem> Items)
{
    public const string Kind = "arcanum-steamwoven-hd-portrait-plan";

    public static HdPortraitPlan Parse(ReadOnlySpan<byte> json)
    {
        try
        {
            using var document = JsonDocument.Parse(json.ToArray());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.GetProperty("kind").GetString() != Kind || root.GetProperty("schema").GetInt32() != 1)
                throw new FormatException("not a portrait plan (kind, schema 1)");
            var packId = root.GetProperty("packId").GetString() ?? throw new FormatException("packId");
            var scale = root.GetProperty("scale").GetInt32();
            var items = root.GetProperty("items").EnumerateArray()
                .Select(item => new HdPortraitPlanItem(item.GetProperty("bmp").GetString() ?? throw new FormatException("bmp"),
                    item.GetProperty("image").GetString() ?? throw new FormatException("image"))).ToArray();
            if (items.Length == 0) throw new FormatException("no items");
            return new(packId, scale, items);
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        { throw new FormatException("portrait plan: " + error.Message, error); }
    }
}

public sealed record HdPortraitBuild(IReadOnlyList<HdBuiltPortrait> Portraits, IReadOnlyList<string> Problems) { public bool Ok => Problems.Count == 0; }

/// <summary>ARC-UI-2865: builds the pack's portrait frames: each redraw must be exactly <c>scale × </c> the size of the Legacy bitmap it redraws and is taken as it is (no registration or
/// lock: a portrait is a whole picture, the redraw's own colours are the owner's choice), written fully opaque.</summary>
public static class HdPortraitImport
{
    public static HdPortraitBuild Build(HdPortraitPlan plan, Func<string, byte[]?> legacyBitmap, Func<string, HdRgbImage?> image)
    {
        ArgumentNullException.ThrowIfNull(plan); ArgumentNullException.ThrowIfNull(legacyBitmap); ArgumentNullException.ThrowIfNull(image);
        var built = new List<HdBuiltPortrait>(); var problems = new List<string>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in plan.Items)
        {
            if (!HdPortraitKey.TryPath(item.Bmp, out var path)) { problems.Add($"{item.Bmp}: not a portrait path (portrait/<name>.bmp)"); continue; }
            if (!seen.Add(path)) { problems.Add($"{path}: listed twice"); continue; }
            var bmp = legacyBitmap(path);
            if (bmp is null) { problems.Add($"{path}: the original bitmap is not in the game"); continue; }
            var legacy = PortraitBitmap.Decode(bmp);
            if (legacy is null) { problems.Add($"{path}: the original bitmap is not one the portrait reader takes"); continue; }
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
            built.Add(new(path, legacy.Width, legacy.Height, legacy.Rgba, rgba));
        }
        return new(built, problems);
    }
}
