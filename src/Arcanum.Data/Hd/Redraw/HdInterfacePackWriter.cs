using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Arcanum.Data.Hd;

/// <summary>ARC-UI-2865: one portrait redraw ready to write: <paramref name="Path"/> is the original's bitmap (<c>portrait/hum1_b.bmp</c>), <paramref name="LegacyRgba"/> its
/// decoded pixels (<paramref name="Width"/> × <paramref name="Height"/>), <paramref name="Rgba"/> the opaque redraw at exactly the pack's scale times that size.</summary>
public sealed record HdBuiltPortrait(string Path, int Width, int Height, byte[] LegacyRgba, byte[] Rgba);

/// <summary>ARC-RENDER-3079: one world-map redraw ready to write: <paramref name="Kind"/> is <see cref="HdEntryKind.WorldMap"/> (a tile, 250 × 250) or <see cref="HdEntryKind.MapZoomed"/> (365 × 365),
/// <paramref name="Path"/> the original's bitmap (<c>worldmap/smallmapchunks001.bmp</c>), <paramref name="LegacyRgba"/> its decoded pixels (<paramref name="Width"/> × <paramref name="Height"/>),
/// <paramref name="Rgba"/> the opaque redraw at exactly the pack's scale times that size.</summary>
public sealed record HdBuiltMapBitmap(HdEntryKind Kind, string Path, int Width, int Height, byte[] LegacyRgba, byte[] Rgba);

/// <summary>ARC-RENDER-3106: one town-map tile redraw ready to write: <paramref name="Path"/> the original's tile (<c>townmap/ashbury/ashbury000748.bmp</c>), <paramref name="LegacyRgba"/> its decoded pixels
/// (64 × 32), <paramref name="Redraw"/> the opaque redraw at exactly the pack's scale times that size, produced when the writer asks (a town has thousands of tiles; they are not all held at once).</summary>
public sealed record HdBuiltTownTile(string Path, int Width, int Height, byte[] LegacyRgba, Func<byte[]?> Redraw);

public enum HdInterfaceWriteStatus { Written, InvalidPack, RecipeConflict, WriteFailed, VerifyFailed }

/// <param name="Detail">The refusal's reason, or the pack's identity on success.</param>
public sealed record HdInterfaceWriteResult(HdInterfaceWriteStatus Status, int Entries, int Replaced, string? Detail = null);

/// <summary>
/// ARC-DEV-242: writes interface frames into an HD pack, as the <c>interface</c> slice (Legacy alpha, its own policy beside the
/// recipe's). An existing pack gets the slice added or its entries replaced (every other entry, the recipe's method and the pack's review
/// stay; the manifest becomes a new identity); a missing one is created with an <c>external</c> recipe. Everything is checked first; frame files (named with their content hash, so a file the live manifest lists is never overwritten) and a changed
/// recipe (also a new file name) are written, then the manifest is renamed over the old one in one step, the files only the old manifest listed
/// are removed, and the pack is opened and its integrity read again. A failure before the rename removes what the run wrote.
/// </summary>
public static class HdInterfacePackWriter
{
    public const string SliceName = "interface";
    public const string BuilderName = "arcanum hd-interface-import";

    // ARC-UI-111: the Legacy silhouette with an anti-aliased edge (a pack written before it has "legacy", which the rewrite replaces).
    private const string SliceAlphaMode = "legacySmooth";
    private static readonly JsonSerializerOptions Compact = new() { WriteIndented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static HdInterfaceWriteResult Write(string directory, string packId, int scale, IReadOnlyList<HdBuiltInterfaceFrame> frames, DateTimeOffset now,
        IReadOnlyList<HdBuiltPortrait>? portraits = null, IReadOnlyList<HdBuiltMapBitmap>? maps = null, IReadOnlyList<HdBuiltTownTile>? townTiles = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory); ArgumentNullException.ThrowIfNull(frames);
        portraits ??= []; maps ??= []; townTiles ??= [];
        // ARC-UI-2865: a write of interface frames replaces the pack's interface sprites and leaves its portraits; a write of portraits replaces only the portraits it names (by path)
        // and leaves the sprites and every other portrait.
        // ARC-RENDER-3079: a write of world-map bitmaps likewise replaces only the ones it names, and a write of frames or portraits leaves them.
        var portraitPaths = portraits.Select(p => p.Path).ToHashSet(StringComparer.Ordinal);
        var mapPaths = maps.Select(m => m.Path).ToHashSet(StringComparer.Ordinal);
        // ARC-RENDER-3106: likewise the town-map tiles: a write names the tiles it replaces and leaves every other entry.
        var townPaths = townTiles.Select(t => t.Path).ToHashSet(StringComparer.Ordinal);
        if (!new HdPackOptions().Scales.Contains(scale)) return new(HdInterfaceWriteStatus.RecipeConflict, 0, 0, $"scale {scale} is not an accepted pack scale");
        var manifestPath = Path.Combine(directory, HdPack.ManifestFile);
        var written = new List<string>();
        try
        {
            JsonObject manifest, recipe;
            var recipeDirty = false;
            if (File.Exists(manifestPath))
            {
                var opened = HdPack.Open(directory);
                if (opened.Status != HdPackOpenStatus.Opened) return new(HdInterfaceWriteStatus.InvalidPack, 0, 0, $"{opened.Status} {opened.Detail}");
                if (opened.Pack!.Scale != scale) return new(HdInterfaceWriteStatus.RecipeConflict, 0, 0, $"pack scale {opened.Pack.Scale} differs from {scale}");
                manifest = JsonNode.Parse(File.ReadAllBytes(manifestPath))!.AsObject();
                recipe = JsonNode.Parse(File.ReadAllBytes(Path.Combine(directory, opened.Pack.RecipeFile)))!.AsObject();
                var slice = recipe["slices"]!.AsArray().FirstOrDefault(s => (string?)s!["name"] == SliceName);
                if (slice is not null && !(slice["kinds"]!.AsArray().Select(k => (string?)k).SequenceEqual(["interface"])))
                    return new(HdInterfaceWriteStatus.RecipeConflict, 0, 0, "the pack's slice 'interface' selects other kinds");
                if (slice is null) { recipe["slices"]!.AsArray().Add(SliceNode()); recipeDirty = true; }
                else if ((string?)slice["alpha"]?["mode"] != SliceAlphaMode) { slice["alpha"] = new JsonObject { ["mode"] = SliceAlphaMode }; recipeDirty = true; }
            }
            else
            {
                recipe = NewRecipe(packId, scale); recipeDirty = true;
                manifest = new JsonObject
                {
                    ["schema"] = 1, ["kind"] = HdPack.Kind, ["id"] = packId, ["scale"] = scale, ["review"] = "unreviewed",
                    ["recipe"] = new JsonObject { ["file"] = "recipe.json", ["sha256"] = "" },
                    ["build"] = new JsonObject
                    {
                        ["builder"] = BuilderName, ["builderVersion"] = "1", ["createdUtc"] = now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                        ["gpu"] = "none", ["os"] = "any",
                    },
                    ["entries"] = new JsonArray(),
                };
            }

            // Everything is checked and encoded before the first byte is written; files get names that carry their content hash, so a
            // file the live manifest lists is never overwritten.
            var entries = manifest["entries"]!.AsArray();
            var oldFiles = new List<string>(); var replaced = 0;
            for (var i = entries.Count - 1; i >= 0; i--)
            {
                var entryKind = (string?)entries[i]!["kind"];
                var remove = entryKind switch
                {
                    "portrait" => portraitPaths.Contains((string?)entries[i]!["portrait"]?["path"] ?? ""),
                    "worldmap" or "map-zoomed" => mapPaths.Contains((string?)entries[i]![entryKind]?["path"] ?? ""),
                    "townmap" => townPaths.Contains((string?)entries[i]!["townmap"]?["path"] ?? ""),
                    _ => frames.Count > 0 || (portraits.Count == 0 && maps.Count == 0 && townTiles.Count == 0),
                };
                if ((string?)entries[i]!["slice"] == SliceName && remove)
                { oldFiles.Add((string)entries[i]!["file"]!); entries.RemoveAt(i); replaced++; }
            }
            var keys = new HashSet<(string, int)>(); var files = new List<(string File, byte[] Png, JsonObject Entry)>();
            foreach (var frame in frames.OrderBy(f => f.Art.FileName, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.Frame))
            {
                if (!keys.Add((frame.Art.FileName.ToLowerInvariant(), frame.Frame))) return new(HdInterfaceWriteStatus.RecipeConflict, 0, 0, $"{frame.Art.FileName} frame {frame.Frame} is given twice");
                var png = HdPng.EncodeRgba(frame.Width * scale, frame.Height * scale, frame.Rgba);
                var sha = HdPackPath.Sha256(png);
                var file = $"interface/{Sanitise(Path.GetFileNameWithoutExtension(frame.Art.FileName))}-f{frame.Frame}-{sha[..8]}.png";
                var legacyRgba = frame.Art.Frames[frame.Frame].Rgba;
                files.Add((file, png, new JsonObject
                {
                    ["kind"] = "sprite", ["file"] = file, ["sha256"] = sha, ["bytes"] = png.Length, ["slice"] = SliceName,
                    ["art"] = new JsonObject { ["sha256"] = frame.Art.Identity.PayloadSha256, ["view"] = frame.Art.Identity.ViewLength, ["nameSha256"] = frame.Art.Identity.NameSha256 },
                    ["group"] = 0, ["frame"] = frame.Frame, ["palette"] = 0,
                    ["legacy"] = new JsonObject { ["width"] = frame.Width, ["height"] = frame.Height, ["rgbaSha256"] = HdPackPath.Sha256(legacyRgba) },
                    ["review"] = "unreviewed",
                }.WithMasked(frame.Masked)));
            }
            var portraitKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var portrait in portraits.OrderBy(p => p.Path, StringComparer.Ordinal))
            {
                if (!HdPortraitKey.TryPath(portrait.Path, out var path) || path != portrait.Path) return new(HdInterfaceWriteStatus.RecipeConflict, 0, 0, $"{portrait.Path} is not an acceptable portrait path");
                if (!portraitKeys.Add(path)) return new(HdInterfaceWriteStatus.RecipeConflict, 0, 0, $"{path} is given twice");
                if (portrait.Rgba.Length != (long)portrait.Width * scale * portrait.Height * scale * 4 || portrait.LegacyRgba.Length != portrait.Width * portrait.Height * 4)
                    return new(HdInterfaceWriteStatus.RecipeConflict, 0, 0, $"{path} has the wrong pixel count for scale {scale}");
                for (var i = 3; i < portrait.Rgba.Length; i += 4)
                    if (portrait.Rgba[i] != byte.MaxValue) return new(HdInterfaceWriteStatus.RecipeConflict, 0, 0, $"{path} is not opaque");
                var png = HdPng.EncodeRgba(portrait.Width * scale, portrait.Height * scale, portrait.Rgba);
                var sha = HdPackPath.Sha256(png);
                var file = $"interface/portrait-{Sanitise(path[HdPortraitKey.Folder.Length..^4])}-f0-{sha[..8]}.png";
                files.Add((file, png, new JsonObject
                {
                    ["kind"] = "portrait", ["file"] = file, ["sha256"] = sha, ["bytes"] = png.Length, ["slice"] = SliceName,
                    ["portrait"] = new JsonObject { ["path"] = path },
                    ["legacy"] = new JsonObject { ["width"] = portrait.Width, ["height"] = portrait.Height, ["rgbaSha256"] = HdPackPath.Sha256(portrait.LegacyRgba) },
                    ["review"] = "unreviewed",
                }));
            }
            var mapKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var map in maps.OrderBy(m => m.Path, StringComparer.Ordinal))
            {
                if (map.Kind is not (HdEntryKind.WorldMap or HdEntryKind.MapZoomed)) return new(HdInterfaceWriteStatus.RecipeConflict, 0, 0, $"{map.Path} is not a world-map entry kind");
                if (!HdMapKey.TryPath(map.Path, out var path) || path != map.Path) return new(HdInterfaceWriteStatus.RecipeConflict, 0, 0, $"{map.Path} is not an acceptable world-map bitmap path");
                if (!mapKeys.Add(path)) return new(HdInterfaceWriteStatus.RecipeConflict, 0, 0, $"{path} is given twice");
                var side = HdMapKey.LegacySide(map.Kind);
                if (map.Width != side || map.Height != side) return new(HdInterfaceWriteStatus.RecipeConflict, 0, 0, $"{path} is {map.Width}x{map.Height}, expected {side}x{side}");
                if (map.Rgba.Length != (long)map.Width * scale * map.Height * scale * 4 || map.LegacyRgba.Length != map.Width * map.Height * 4)
                    return new(HdInterfaceWriteStatus.RecipeConflict, 0, 0, $"{path} has the wrong pixel count for scale {scale}");
                for (var i = 3; i < map.Rgba.Length; i += 4)
                    if (map.Rgba[i] != byte.MaxValue) return new(HdInterfaceWriteStatus.RecipeConflict, 0, 0, $"{path} is not opaque");
                var png = HdPng.EncodeRgba(map.Width * scale, map.Height * scale, map.Rgba);
                var sha = HdPackPath.Sha256(png);
                var property = map.Kind == HdEntryKind.WorldMap ? "worldmap" : "map-zoomed";
                var file = $"interface/{property}-{Sanitise(path[HdMapKey.Folder.Length..^4])}-f0-{sha[..8]}.png";
                files.Add((file, png, new JsonObject
                {
                    ["kind"] = property, ["file"] = file, ["sha256"] = sha, ["bytes"] = png.Length, ["slice"] = SliceName,
                    [property] = new JsonObject { ["path"] = path },
                    ["legacy"] = new JsonObject { ["width"] = map.Width, ["height"] = map.Height, ["rgbaSha256"] = HdPackPath.Sha256(map.LegacyRgba) },
                    ["review"] = "unreviewed",
                }));
            }
            var townKeys = new HashSet<string>(StringComparer.Ordinal); var townStems = new HashSet<string>(StringComparer.Ordinal);
            foreach (var tile in townTiles.OrderBy(t => t.Path, StringComparer.Ordinal))
            {
                if (!HdTownMapKey.TryParse(tile.Path, out var town, out var tileIndex) || HdTownMapKey.Path(town, tileIndex) != tile.Path)
                    return new(HdInterfaceWriteStatus.RecipeConflict, 0, 0, $"{tile.Path} is not an acceptable town-map tile path");
                if (!townKeys.Add(tile.Path)) return new(HdInterfaceWriteStatus.RecipeConflict, 0, 0, $"{tile.Path} is given twice");
                // The pack file name carries the town with other characters turned into '-': two towns that come out alike must not share a tile name.
                var stem = $"{HdTownMapKey.FileTown(town)}-{tileIndex.ToString("D6", System.Globalization.CultureInfo.InvariantCulture)}";
                if (!townStems.Add(stem)) return new(HdInterfaceWriteStatus.RecipeConflict, 0, 0, $"{tile.Path} would take the file name of another tile ({stem})");
                if (tile.Width != HdTownMapKey.TileWidth || tile.Height != HdTownMapKey.TileHeight) return new(HdInterfaceWriteStatus.RecipeConflict, 0, 0, $"{tile.Path} is {tile.Width}x{tile.Height}, expected {HdTownMapKey.TileWidth}x{HdTownMapKey.TileHeight}");
                var rgba = tile.Redraw();
                if (rgba is null || rgba.Length != (long)tile.Width * scale * tile.Height * scale * 4 || tile.LegacyRgba.Length != tile.Width * tile.Height * 4)
                    return new(HdInterfaceWriteStatus.RecipeConflict, 0, 0, $"{tile.Path} has the wrong pixel count for scale {scale}");
                for (var i = 3; i < rgba.Length; i += 4)
                    if (rgba[i] != byte.MaxValue) return new(HdInterfaceWriteStatus.RecipeConflict, 0, 0, $"{tile.Path} is not opaque");
                var png = HdPng.EncodeRgba(tile.Width * scale, tile.Height * scale, rgba);
                var sha = HdPackPath.Sha256(png);
                var file = $"interface/townmap-{stem}-f0-{sha[..8]}.png";
                files.Add((file, png, new JsonObject
                {
                    ["kind"] = "townmap", ["file"] = file, ["sha256"] = sha, ["bytes"] = png.Length, ["slice"] = SliceName,
                    ["townmap"] = new JsonObject { ["path"] = tile.Path },
                    ["legacy"] = new JsonObject { ["width"] = tile.Width, ["height"] = tile.Height, ["rgbaSha256"] = HdPackPath.Sha256(tile.LegacyRgba) },
                    ["review"] = "unreviewed",
                }));
            }
            var taken = new HashSet<string>(entries.Select(e => (string)e!["file"]!), StringComparer.Ordinal);
            var fresh = new HashSet<string>(StringComparer.Ordinal);
            if (files.Any(f => taken.Contains(f.File) || !fresh.Add(f.File))) return new(HdInterfaceWriteStatus.RecipeConflict, 0, 0, "two interface frames would be written to one file");

            var recipeFile = (string)manifest["recipe"]!["file"]!; string? oldRecipeFile = null;
            Directory.CreateDirectory(directory);
            if (recipeDirty)
            {
                var recipeBytes = Encoding.UTF8.GetBytes(recipe.ToJsonString(Compact));
                var sha = HdPackPath.Sha256(recipeBytes);
                if (File.Exists(manifestPath)) { oldRecipeFile = recipeFile; recipeFile = $"recipe-{sha[..8]}.json"; }
                var target = HdPackPath.Full(directory, recipeFile);
                File.WriteAllBytes(target, recipeBytes); written.Add(target);
                manifest["recipe"]!["file"] = recipeFile; manifest["recipe"]!["sha256"] = sha;
            }
            foreach (var (file, png, entry) in files)
            {
                var target = HdPackPath.Full(directory, file);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, png); written.Add(target);
                entries.Add(entry);
            }
            var temporary = Path.Combine(directory, $".manifest-interface-{Guid.NewGuid():N}.tmp");
            File.WriteAllBytes(temporary, Encoding.UTF8.GetBytes(manifest.ToJsonString(Compact)));
            written.Add(temporary);
            File.Move(temporary, manifestPath, overwrite: true);
            written.Clear();

            // The new manifest is live: what only the old one listed is garbage.
            foreach (var old in oldFiles.Concat(oldRecipeFile is null ? [] : [oldRecipeFile]).Where(f => !fresh.Contains(f)))
                try { File.Delete(HdPackPath.Full(directory, old)); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }

            var check = HdPack.Open(directory);
            if (check.Status != HdPackOpenStatus.Opened) return new(HdInterfaceWriteStatus.VerifyFailed, frames.Count + portraits.Count + maps.Count + townTiles.Count, replaced, $"{check.Status} {check.Detail}");
            var integrity = check.Pack!.VerifyIntegrity();
            return integrity.Intact
                ? new(HdInterfaceWriteStatus.Written, frames.Count + portraits.Count + maps.Count + townTiles.Count, replaced, $"{check.Pack.Id} {check.Pack.Scale}x manifest {check.Pack.Identity.ManifestSha256}")
                : new(HdInterfaceWriteStatus.VerifyFailed, frames.Count + portraits.Count + maps.Count + townTiles.Count, replaced, $"{integrity.Valid}/{integrity.Entries} entries intact");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            // Before the manifest swap nothing live changed: remove what this run added.
            foreach (var path in written) try { File.Delete(path); } catch (Exception inner) when (inner is IOException or UnauthorizedAccessException) { }
            return new(HdInterfaceWriteStatus.WriteFailed, 0, 0, error.GetType().Name);
        }
    }

    // ARC-UI-113: a frame whose alpha is the owner's mask is marked so (the pack checks it by the masked rule, not the slice's).
    private static JsonObject WithMasked(this JsonObject entry, bool masked)
    {
        if (masked) entry["alpha"] = "masked";
        return entry;
    }

    private static JsonObject SliceNode() => new()
    {
        ["name"] = SliceName, ["map"] = "interface-redraw", ["kinds"] = new JsonArray("interface"),
        ["alpha"] = new JsonObject { ["mode"] = SliceAlphaMode },
    };

    private static JsonObject NewRecipe(string packId, int scale) => new()
    {
        ["schema"] = 1, ["kind"] = HdRecipeReader.Kind, ["id"] = packId, ["scale"] = scale,
        ["slices"] = new JsonArray(SliceNode()),
        ["method"] = new JsonObject
        {
            ["kind"] = "external", ["tool"] = "interface-redraw-import", ["toolVersion"] = "1", ["nativeScale"] = scale, ["downscale"] = "none",
            ["parameters"] = new JsonObject { ["fit"] = "luma-ncc", ["lock"] = "shading-colour", ["alpha"] = SliceAlphaMode },
        },
        ["alpha"] = new JsonObject { ["mode"] = SliceAlphaMode },
    };

    private static string Sanitise(string name)
    {
        var text = new string([.. name.ToLowerInvariant().Select(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-' ? c : '_')]);
        return text.Length is > 0 and <= 48 ? text : text[..Math.Min(text.Length, 48)] is { Length: > 0 } cut ? cut : "art";
    }
}
