using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Arcanum.Data.Hd;

public enum HdInterfaceWriteStatus { Written, InvalidPack, RecipeConflict, WriteFailed, VerifyFailed }

/// <param name="Detail">The refusal's reason, or the pack's identity on success.</param>
public sealed record HdInterfaceWriteResult(HdInterfaceWriteStatus Status, int Entries, int Replaced, string? Detail = null);

/// <summary>
/// ARC-DEV-242: writes interface frames into an HD pack, as the <c>interface</c> slice (Legacy alpha, its own policy beside the
/// recipe's). An existing pack gets the slice added or its entries replaced (every other entry, the recipe's method and the pack's review
/// stay; the manifest becomes a new identity); a missing one is created with an <c>external</c> recipe. Frame files first, the manifest last
/// (written to a temporary file and renamed), then the pack is opened and its integrity read again.
/// </summary>
public static class HdInterfacePackWriter
{
    public const string SliceName = "interface";
    public const string BuilderName = "arcanum hd-interface-import";

    private static readonly JsonSerializerOptions Compact = new() { WriteIndented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static HdInterfaceWriteResult Write(string directory, string packId, int scale, IReadOnlyList<HdBuiltInterfaceFrame> frames, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory); ArgumentNullException.ThrowIfNull(frames);
        var manifestPath = Path.Combine(directory, HdPack.ManifestFile);
        try
        {
            JsonObject manifest, recipe;
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
                if (slice is null)
                    recipe["slices"]!.AsArray().Add(SliceNode());
            }
            else
            {
                recipe = NewRecipe(packId, scale);
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

            var entries = manifest["entries"]!.AsArray();
            var replaced = 0;
            for (var i = entries.Count - 1; i >= 0; i--)
                if ((string?)entries[i]!["slice"] == SliceName) { entries.RemoveAt(i); replaced++; }
            var taken = new HashSet<string>(entries.Select(e => (string)e!["file"]!), StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var frame in frames.OrderBy(f => f.Art.FileName, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.Frame))
            {
                var file = $"interface/{Sanitise(Path.GetFileNameWithoutExtension(frame.Art.FileName))}-f{frame.Frame}.png";
                if (!seen.Add(file) || taken.Contains(file)) return new(HdInterfaceWriteStatus.RecipeConflict, 0, 0, $"two frames would be written to {file}");
                var png = HdPng.EncodeRgba(frame.Width * scale, frame.Height * scale, frame.Rgba);
                var target = HdPackPath.Full(directory, file);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, png);
                var legacyRgba = frame.Art.Frames[frame.Frame].Rgba;
                entries.Add(new JsonObject
                {
                    ["kind"] = "sprite", ["file"] = file, ["sha256"] = HdPackPath.Sha256(png), ["bytes"] = png.Length, ["slice"] = SliceName,
                    ["art"] = new JsonObject { ["sha256"] = frame.Art.Identity.PayloadSha256, ["view"] = frame.Art.Identity.ViewLength, ["nameSha256"] = frame.Art.Identity.NameSha256 },
                    ["group"] = 0, ["frame"] = frame.Frame, ["palette"] = 0,
                    ["legacy"] = new JsonObject { ["width"] = frame.Width, ["height"] = frame.Height, ["rgbaSha256"] = HdPackPath.Sha256(legacyRgba) },
                    ["review"] = "unreviewed",
                });
            }

            var recipeFile = (string)manifest["recipe"]!["file"]!;
            var recipeBytes = Encoding.UTF8.GetBytes(recipe.ToJsonString(Compact));
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(HdPackPath.Full(directory, recipeFile), recipeBytes);
            manifest["recipe"]!["sha256"] = HdPackPath.Sha256(recipeBytes);
            var temporary = Path.Combine(directory, $".manifest-interface-{Guid.NewGuid():N}.tmp");
            File.WriteAllBytes(temporary, Encoding.UTF8.GetBytes(manifest.ToJsonString(Compact)));
            File.Move(temporary, manifestPath, overwrite: true);

            var check = HdPack.Open(directory);
            if (check.Status != HdPackOpenStatus.Opened) return new(HdInterfaceWriteStatus.VerifyFailed, frames.Count, replaced, $"{check.Status} {check.Detail}");
            var integrity = check.Pack!.VerifyIntegrity();
            return integrity.Intact
                ? new(HdInterfaceWriteStatus.Written, frames.Count, replaced, $"{check.Pack.Id} {check.Pack.Scale}x manifest {check.Pack.Identity.ManifestSha256}")
                : new(HdInterfaceWriteStatus.VerifyFailed, frames.Count, replaced, $"{integrity.Valid}/{integrity.Entries} entries intact");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return new(HdInterfaceWriteStatus.WriteFailed, 0, 0, error.GetType().Name);
        }
    }

    private static JsonObject SliceNode() => new()
    {
        ["name"] = SliceName, ["map"] = "interface-redraw", ["kinds"] = new JsonArray("interface"),
        ["alpha"] = new JsonObject { ["mode"] = "legacy" },
    };

    private static JsonObject NewRecipe(string packId, int scale) => new()
    {
        ["schema"] = 1, ["kind"] = HdRecipeReader.Kind, ["id"] = packId, ["scale"] = scale,
        ["slices"] = new JsonArray(SliceNode()),
        ["method"] = new JsonObject
        {
            ["kind"] = "external", ["tool"] = "interface-redraw-import", ["toolVersion"] = "1", ["nativeScale"] = scale, ["downscale"] = "none",
            ["parameters"] = new JsonObject { ["fit"] = "luma-ncc", ["lock"] = "shading-colour", ["alpha"] = "legacy" },
        },
        ["alpha"] = new JsonObject { ["mode"] = "legacy" },
    };

    private static string Sanitise(string name)
    {
        var text = new string([.. name.ToLowerInvariant().Select(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-' ? c : '_')]);
        return text.Length is > 0 and <= 48 ? text : text[..Math.Min(text.Length, 48)] is { Length: > 0 } cut ? cut : "art";
    }
}
