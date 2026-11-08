using System.Text.Json;
using System.Text.Json.Nodes;

namespace Arcanum.Data.Hd;

/// <summary>One decoded interface ART file as the importer needs it: its file name, its <see cref="HdInterfaceKey"/> identity
/// and the Legacy frames (group 0, palette slot 0).</summary>
public sealed record HdInterfaceArt(string FileName, HdInterfaceArtIdentity Identity, IReadOnlyList<HdLegacyRaster> Frames);

/// <summary>A frame of an interface ART: <see cref="Art"/> is the file name as <c>interface.mes</c> gives it (case ignored).</summary>
public sealed record HdInterfaceFrameRef(string Art, int Frame);

/// <param name="Image">Path of the redraw image, relative to the plan file or absolute (a local file; never in git).</param>
/// <param name="Exact">The image covers exactly the one frame of <paramref name="Tiles"/> (a stitched strip); no search for the scale
/// and offset, only the final translation refinement. Otherwise it is a sheet of <paramref name="Tiles"/> laid out as
/// <see cref="HdRedrawSheet"/> with <paramref name="Pad"/> texels.</param>
/// <param name="Columns">A sheet of a grid: tiles row by row in that many columns (0: one row).</param>
/// <param name="Coarse">&gt; 0: the lock's corrections are blurred over that many Legacy texels (the "c" variants).</param>
/// <param name="Derived">Frames made from a redrawn tile by adding the Legacy colour difference (a button's other frames).</param>
/// <param name="Ready">The images are already registered (one per tile, in order, exactly <c>scale·w × scale·h</c>): only the lock and the
/// Legacy alpha are applied. <paramref name="Image"/> then names the first of them; <paramref name="ReadyImages"/> holds all.</param>
/// <param name="Masks">ARC-UI-113: the frames (tiles and derived frames, as the plan names them) whose alpha is the owner's mask and not the
/// Legacy alpha (<c>"alphaMask"</c> or <c>"ellipse"</c> on the tile). A derived frame without its own mask takes its base tile's.</param>
public sealed record HdInterfaceItem(string Image, bool Exact, int Pad, IReadOnlyList<HdInterfaceFrameRef> Tiles, double Coarse,
    IReadOnlyList<HdDerivedFrame> Derived, int Columns = 0, IReadOnlyList<string>? ReadyImages = null, bool Lock = true,
    IReadOnlyDictionary<HdInterfaceFrameRef, HdAlphaMaskSpec>? Masks = null)
{
    public bool Ready => ReadyImages is not null;
}

/// <param name="From">The tile whose redraw is the base (its unlocked fit).</param>
public sealed record HdDerivedFrame(HdInterfaceFrameRef Frame, HdInterfaceFrameRef From);

/// <summary>The local import plan: which redraw image gives which interface frames. A plan names local files and is not committed.</summary>
public sealed record HdInterfacePlan(string PackId, int Scale, IReadOnlyList<HdInterfaceItem> Items)
{
    public const string Kind = "arcanum-steamwoven-hd-interface-plan";

    /// <summary>Parses a plan. <c>{"schema":1,"kind":…,"packId":"…","scale":4,"items":[{"image":"…","mode":"sheet"|"exact","pad":8,
    /// "coarse":0,"columns":0,"tiles":[{"art":"X.ART","frame":0,"alphaMask":"mask.png"|"ellipse":[cx,cy,rx,ry]}],"derived":[{"art":"X.ART","frame":1,"from":0}]}]}</c>.
    /// <c>alphaMask</c> and <c>ellipse</c> (ARC-UI-113, on a tile or a derived frame) replace the frame's Legacy alpha; see <see cref="HdAlphaMaskSpec"/>. Throws
    /// <see cref="FormatException"/> on a malformed plan (a tool input, not a runtime boundary).</summary>
    public static HdInterfacePlan Parse(ReadOnlySpan<byte> json)
    {
        try
        {
            var root = JsonNode.Parse(json.ToArray())?.AsObject() ?? throw new FormatException("plan is not an object");
            if ((int?)root["schema"] != 1 || (string?)root["kind"] != Kind) throw new FormatException($"plan needs schema 1 and kind {Kind}");
            var packId = (string?)root["packId"] ?? throw new FormatException("plan.packId missing");
            if (!System.Text.RegularExpressions.Regex.IsMatch(packId, "^[a-z0-9][a-z0-9_-]{0,63}$")) throw new FormatException("plan.packId is not a pack id");
            var scale = (int?)root["scale"] ?? 4;
            if (scale is < 2 or > 16) throw new FormatException("plan.scale must be 2..16");
            var items = new List<HdInterfaceItem>();
            foreach (var node in root["items"]?.AsArray() ?? throw new FormatException("plan.items missing"))
            {
                var item = node?.AsObject() ?? throw new FormatException("item is not an object");
                var mode = (string?)item["mode"] ?? "sheet";
                if (mode is not ("sheet" or "exact" or "ready")) throw new FormatException("item.mode must be sheet, exact or ready");
                var readyImages = mode == "ready" ? (item["images"]?.AsArray() ?? throw new FormatException("item.images missing")).Select(i => (string?)i ?? throw new FormatException("item.images holds a non-string")).ToArray() : null;
                var image = (string?)item["image"] ?? readyImages?.FirstOrDefault() ?? throw new FormatException("item.image missing");
                var tiles = (item["tiles"]?.AsArray() ?? throw new FormatException("item.tiles missing")).Select(Ref).ToArray();
                if (tiles.Length == 0 || (mode == "exact" && tiles.Length != 1)) throw new FormatException("item.tiles must hold one tile (exact) or more");
                if (readyImages is not null && readyImages.Length != tiles.Length) throw new FormatException("item.images must hold one image per tile");
                var pad = (int?)item["pad"] ?? 8; var columns = (int?)item["columns"] ?? 0;
                if (pad is < 0 or > 64 || columns is < 0 or > 64) throw new FormatException("item.pad and item.columns must be 0..64");
                if (mode == "ready" && item["derived"] is JsonArray { Count: > 0 }) throw new FormatException("item.derived applies to sheet and exact items, not ready ones");
                var masks = new Dictionary<HdInterfaceFrameRef, HdAlphaMaskSpec>();
                foreach (var tileNode in item["tiles"]!.AsArray())
                    if (Mask(tileNode) is { } tileMask && !masks.TryAdd(Ref(tileNode), tileMask)) throw new FormatException("item.tiles names a frame twice");
                var derived = (item["derived"]?.AsArray() ?? []).Select(d =>
                {
                    var o = d!.AsObject();
                    return new HdDerivedFrame(Ref(d), new((string)o["art"]!, (int)o["from"]!));
                }).ToArray();
                foreach (var derivedNode in item["derived"]?.AsArray() ?? [])
                    if (Mask(derivedNode) is { } derivedMask && !masks.TryAdd(Ref(derivedNode), derivedMask)) throw new FormatException("a derived frame is also a tile or named twice");
                foreach (var d in derived)
                    if (!tiles.Contains(d.From)) throw new FormatException($"derived frame {d.Frame.Art}:{d.Frame.Frame} takes its base from a tile that is not in the item");
                items.Add(new(image, mode == "exact", pad, tiles, (double?)item["coarse"] ?? 0, derived, columns, readyImages, (bool?)item["lock"] ?? true, masks.Count == 0 ? null : masks));
            }
            if (items.Count == 0) throw new FormatException("plan.items is empty");
            return new(packId, scale, items);
        }
        catch (Exception error) when (error is JsonException or InvalidCastException or InvalidOperationException or NullReferenceException)
        {
            throw new FormatException("plan is malformed: " + error.Message, error);
        }

        // ARC-UI-113: the optional alphaMask (path) or ellipse ([cx, cy, rx, ry]) of a tile or derived frame.
        static HdAlphaMaskSpec? Mask(JsonNode? node)
        {
            var o = node?.AsObject() ?? throw new FormatException("tile is not an object");
            var image = o["alphaMask"]; var ellipse = o["ellipse"];
            if (image is null && ellipse is null) return null;
            if (image is not null && ellipse is not null) throw new FormatException("a frame takes alphaMask or ellipse, not both");
            if (image is not null)
                return new(string.IsNullOrWhiteSpace((string?)image) ? throw new FormatException("alphaMask must be a path") : (string)image!, null);
            var values = (ellipse!.AsArray().Select(v => (double?)v ?? throw new FormatException("ellipse holds a non-number")).ToArray());
            if (values.Length != 4 || values.Any(v => !double.IsFinite(v)) || values[2] <= 0 || values[3] <= 0)
                throw new FormatException("ellipse must be [cx, cy, rx, ry] with rx, ry > 0");
            return new(null, values);
        }

        static HdInterfaceFrameRef Ref(JsonNode? node)
        {
            var o = node?.AsObject() ?? throw new FormatException("tile is not an object");
            return new((string?)o["art"] ?? throw new FormatException("tile.art missing"), (int?)o["frame"] ?? 0);
        }
    }
}

/// <summary>A built HD interface frame: <see cref="Rgba"/> is <c>scale·w × scale·h × 4</c> bytes with the Legacy alpha.</summary>
/// <param name="Masked">ARC-UI-113: the alpha is the owner's mask (a subset of the Legacy alpha), written as the entry's <c>"alpha":"masked"</c>.</param>
public sealed record HdBuiltInterfaceFrame(HdInterfaceArt Art, int Frame, int Width, int Height, byte[] Rgba, HdFitTransform Fit, bool Masked = false)
{
    public double Ncc => Fit.Ncc;
}

public sealed record HdInterfaceBuildResult(IReadOnlyList<HdBuiltInterfaceFrame> Frames, IReadOnlyList<string> Problems)
{
    public bool Ok => Problems.Count == 0;
}

/// <summary>
/// ARC-DEV-242: turns the owner's redraws of interface art into HD frames (ARC-RES-094 U3): register the redraw to the Legacy frames,
/// resample it to the pack scale, lock the shading and colour to the Legacy frame, take the Legacy silhouette with an anti-aliased edge (ARC-UI-111). Pure computation over
/// decoded images; the caller reads the files and the game's art.
/// </summary>
public static class HdInterfaceImport
{
    public static HdInterfaceBuildResult Build(HdInterfacePlan plan, Func<string, HdInterfaceArt?> art, Func<string, HdRgbImage?> image)
    {
        ArgumentNullException.ThrowIfNull(plan); ArgumentNullException.ThrowIfNull(art); ArgumentNullException.ThrowIfNull(image);
        var frames = new List<HdBuiltInterfaceFrame>(); var problems = new List<string>();
        var seen = new HashSet<(string, int)>();
        foreach (var item in plan.Items)
        {
            if (item.Ready) { BuildReady(item); continue; }
            var picture = image(item.Image);
            if (picture is null) { problems.Add($"image {item.Image} could not be read"); continue; }
            var arts = new List<HdInterfaceArt>(); var rasters = new List<HdLegacyRaster>(); var broken = false;
            foreach (var tile in item.Tiles)
            {
                var found = art(tile.Art);
                if (found is null) { problems.Add($"interface art {tile.Art} is not in the game folder"); broken = true; continue; }
                if (tile.Frame < 0 || tile.Frame >= found.Frames.Count) { problems.Add($"{tile.Art} has no frame {tile.Frame} ({found.Frames.Count} frames)"); broken = true; continue; }
                arts.Add(found); rasters.Add(found.Frames[tile.Frame]);
            }
            if (broken) continue;
            var sheet = HdRedrawSheet.Build(rasters, item.Exact ? 0 : item.Pad, item.Columns);
            var fits = HdRedrawFit.Fit(picture, sheet, item.Exact);
            var raw = new Dictionary<HdInterfaceFrameRef, float[]>();
            for (var i = 0; i < item.Tiles.Count; i++)
            {
                var tile = item.Tiles[i]; var legacy = rasters[i];
                var rgb = HdRedrawFit.Extract(picture, sheet.Frames[i], fits[i], plan.Scale);
                raw[tile] = rgb;
                Emit(arts[i], tile.Frame, legacy, rgb, fits[i], tile);
            }
            foreach (var derived in item.Derived)
            {
                var found = art(derived.Frame.Art);
                if (found is null || derived.Frame.Frame < 0 || derived.Frame.Frame >= found.Frames.Count) { problems.Add($"derived frame {derived.Frame.Art}:{derived.Frame.Frame} does not exist"); continue; }
                var baseIndex = item.Tiles.ToList().IndexOf(derived.From);
                var baseLegacy = rasters[baseIndex]; var target = found.Frames[derived.Frame.Frame];
                if (target.Width != baseLegacy.Width || target.Height != baseLegacy.Height) { problems.Add($"derived frame {derived.Frame.Art}:{derived.Frame.Frame} differs in size from its base"); continue; }
                var withDifference = HdRedrawLock.AddLegacyDifference(raw[derived.From], Rgb(target), Rgb(baseLegacy), target.Width, target.Height, plan.Scale);
                Emit(found, derived.Frame.Frame, target, withDifference, fits[baseIndex], derived.Frame, item.Tiles[baseIndex]);
            }

            void Emit(HdInterfaceArt owner, int frame, HdLegacyRaster legacy, float[] rgb, HdFitTransform fit, HdInterfaceFrameRef named, HdInterfaceFrameRef? inheritFrom = null)
            {
                if (!seen.Add((owner.FileName.ToLowerInvariant(), frame))) { problems.Add($"{owner.FileName} frame {frame} is made twice"); return; }
                var spec = MaskOf(item, named, inheritFrom);
                HdMaskedFrame? masked = null;
                if (spec is not null)
                {
                    masked = HdAlphaMasks.Resolve(spec, $"{owner.FileName} frame {frame}", legacy, plan.Scale, image, out var problem);
                    if (masked is null) { problems.Add(problem!); return; }
                }
                var shape = masked?.Legacy ?? legacy;
                var mask = new bool[legacy.Width * legacy.Height];
                for (var i = 0; i < mask.Length; i++) mask[i] = shape.Rgba[i * 4 + 3] > 0;
                var locked = HdRedrawLock.ShadingLock(rgb, Rgb(legacy), mask, legacy.Width, legacy.Height, plan.Scale, coarse: item.Coarse);
                frames.Add(new(owner, frame, legacy.Width, legacy.Height,
                    masked is null ? EdgeAlpha(locked, legacy, plan.Scale) : HdAlphaMasks.Compose(locked, masked, legacy, plan.Scale), fit, masked is not null));
            }
        }
        return new(frames, problems);

        void BuildReady(HdInterfaceItem item)
        {
            for (var i = 0; i < item.Tiles.Count; i++)
            {
                var tile = item.Tiles[i]; var path = item.ReadyImages![i];
                var found = art(tile.Art);
                if (found is null) { problems.Add($"interface art {tile.Art} is not in the game folder"); continue; }
                if (tile.Frame < 0 || tile.Frame >= found.Frames.Count) { problems.Add($"{tile.Art} has no frame {tile.Frame} ({found.Frames.Count} frames)"); continue; }
                var legacy = found.Frames[tile.Frame];
                var picture = image(path);
                if (picture is null) { problems.Add($"image {path} could not be read"); continue; }
                if (picture.Width != legacy.Width * plan.Scale || picture.Height != legacy.Height * plan.Scale)
                { problems.Add($"{path} is {picture.Width}x{picture.Height}, {tile.Art} frame {tile.Frame} needs {legacy.Width * plan.Scale}x{legacy.Height * plan.Scale}"); continue; }
                if (!seen.Add((found.FileName.ToLowerInvariant(), tile.Frame))) { problems.Add($"{found.FileName} frame {tile.Frame} is made twice"); continue; }
                HdMaskedFrame? masked = null;
                if (MaskOf(item, tile, null) is { } spec)
                {
                    masked = HdAlphaMasks.Resolve(spec, $"{found.FileName} frame {tile.Frame}", legacy, plan.Scale, image, out var problem);
                    if (masked is null) { problems.Add(problem!); continue; }
                }
                var rgb = picture.Rgb;
                if (item.Lock)
                {
                    var shape = masked?.Legacy ?? legacy;
                    var mask = new bool[legacy.Width * legacy.Height];
                    for (var k = 0; k < mask.Length; k++) mask[k] = shape.Rgba[k * 4 + 3] > 0;
                    rgb = HdRedrawLock.ShadingLock(rgb, Rgb(legacy), mask, legacy.Width, legacy.Height, plan.Scale, coarse: item.Coarse);
                }
                frames.Add(new(found, tile.Frame, legacy.Width, legacy.Height,
                    masked is null ? EdgeAlpha(rgb, legacy, plan.Scale) : HdAlphaMasks.Compose(rgb, masked, legacy, plan.Scale), new(1, 1, 0, 0, 1), masked is not null));
            }
        }
    }

    // ARC-UI-113: the frame's own mask, else (a derived frame) its base tile's.
    private static HdAlphaMaskSpec? MaskOf(HdInterfaceItem item, HdInterfaceFrameRef named, HdInterfaceFrameRef? inheritFrom)
        => item.Masks is { } masks && (masks.GetValueOrDefault(named) ?? (inheritFrom is { } from ? masks.GetValueOrDefault(from) : null)) is { } spec ? spec : null;

    /// <summary>ARC-UI-111: the frame's RGBA with the Legacy silhouette and an anti-aliased edge (<see cref="HdAlphaMode.LegacySmooth"/>).
    /// A frame whose smoothed alpha fails the pack's own check (a tiny, all-edge frame) takes the Legacy alpha exactly, which the
    /// same mode accepts.</summary>
    internal static byte[] EdgeAlpha(float[] rgb, HdLegacyRaster legacy, int scale)
    {
        var smooth = HdRedrawLock.SmoothAlpha(rgb, legacy.Rgba, legacy.Width, legacy.Height, scale);
        return HdAlphaCheck.Matches(smooth, legacy.Rgba, legacy.Width, legacy.Height, scale, new(HdAlphaMode.LegacySmooth, HdRecipeReader.MinSoftIoUPermille))
            ? smooth : HdRedrawLock.LegacyAlpha(rgb, legacy.Rgba, legacy.Width, legacy.Height, scale);
    }

    private static float[] Rgb(HdLegacyRaster raster)
    {
        var rgb = new float[raster.Width * raster.Height * 3];
        for (var i = 0; i < raster.Width * raster.Height; i++) for (var c = 0; c < 3; c++) rgb[i * 3 + c] = raster.Rgba[i * 4 + c];
        return rgb;
    }
}
