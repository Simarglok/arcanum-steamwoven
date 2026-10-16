using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Arcanum.Data.Hd;

namespace Arcanum.Data.Tests.Hd;

/// <summary>Synthetic Legacy frames and "HD" frames for pack tests. No original pixel is
/// involved: frames are generated patterns with holes, the HD frame is the nearest N×
/// replica with an optional colour change (a stand-in for an upscaler).</summary>
internal sealed class SyntheticHdPack
{
    public static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    public static string Sha(string text) => Sha(Encoding.UTF8.GetBytes(text));

    public sealed record Frame(string Name, int Width, int Height, int Group, int FrameIndex, int Palette, byte[] Rgba)
    {
        public string ArtSha256 => Sha("art:" + Name);
        public string NameSha256 => Sha("name:" + Name);
        public string RgbaSha256 => Sha(Rgba);
        public const int View = 4096;
        public HdSpriteKey Key => new(ArtSha256, View, Group, FrameIndex, Palette);
        public HdSpriteQuery Query => new(Key, NameSha256, new(Width, Height, RgbaSha256));
    }

    public static Frame Legacy(string name, int width, int height, int seed, int group = 0, int frame = 0, int palette = 0)
    {
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var at = (y * width + x) * 4;
            var hole = (x * 7 + y * 3 + seed) % 5 == 0;
            if (hole) continue; // index 0: (0,0,0,0)
            rgba[at] = (byte)(x * 31 + seed); rgba[at + 1] = (byte)(y * 17 + seed * 3); rgba[at + 2] = (byte)(x ^ y ^ seed); rgba[at + 3] = 255;
        }
        return new(name, width, height, group, frame, palette, rgba);
    }

    /// <summary>The nearest N× replica, with every opaque colour changed by
    /// <paramref name="tint"/> on subtexels (1,·) so the result differs from the replica.</summary>
    public static byte[] Hd(Frame legacy, int n, int tint = 0)
    {
        var w = legacy.Width * n; var h = legacy.Height * n;
        var rgba = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var from = ((y / n) * legacy.Width + x / n) * 4; var to = (y * w + x) * 4;
            Array.Copy(legacy.Rgba, from, rgba, to, 4);
            if (rgba[to + 3] != 0 && x % n == 1) rgba[to] = (byte)(rgba[to] + tint);
        }
        return rgba;
    }

    public const string Recipe = """
        {"schema":1,"kind":"arcanum-steamwoven-hd-recipe","id":"synthetic-av3","scale":2,
         "slices":[{"name":"bodies","map":"synthetic-map","kinds":["critter","scenery"]}],
         "method":{"kind":"network","tool":"synthetic-upscaler","toolVersion":"1","toolSha256":"0000000000000000000000000000000000000000000000000000000000000001",
           "model":"synthetic-x2","licence":"MIT","modelFiles":[{"name":"synthetic-x2.bin","sha256":"0000000000000000000000000000000000000000000000000000000000000002"}],
           "nativeScale":2,"downscale":"none","tileSize":512,"tta":false},
         "rgb":{"edgeExtend":8,"fill":"mean"},"alpha":{"mode":"legacy"}}
        """;

    public string Directory { get; }
    public int Scale { get; }
    public string RecipeText { get; set; }
    public JsonObject Manifest { get; }
    public List<Frame> Frames { get; } = [];

    public SyntheticHdPack(string directory, int scale = 2, string? recipe = null, string id = "synthetic-pack")
    {
        Directory = directory; Scale = scale;
        RecipeText = recipe ?? Recipe.Replace("\"scale\":2", $"\"scale\":{scale}").Replace("\"nativeScale\":2", $"\"nativeScale\":{scale}");
        Manifest = new JsonObject
        {
            ["schema"] = 1, ["kind"] = "arcanum-steamwoven-hd-pack", ["id"] = id, ["scale"] = scale, ["review"] = "unreviewed",
            ["recipe"] = new JsonObject { ["file"] = "recipe.json", ["sha256"] = "" },
            ["build"] = new JsonObject { ["builder"] = "synthetic-test", ["builderVersion"] = "1", ["createdUtc"] = "2026-10-16T12:00:00Z", ["gpu"] = "none", ["os"] = "any" },
            ["entries"] = new JsonArray(),
        };
    }

    public JsonArray Entries => Manifest["entries"]!.AsArray();

    /// <summary>Adds a sprite entry and writes its PNG (<paramref name="hd"/> or the tinted
    /// replica).</summary>
    public JsonObject Add(Frame frame, byte[]? hd = null, string? file = null, int? hdWidth = null, int? hdHeight = null, string slice = "bodies")
    {
        Frames.Add(frame);
        var png = HdPng.EncodeRgba(hdWidth ?? frame.Width * Scale, hdHeight ?? frame.Height * Scale, hd ?? Hd(frame, Scale, tint: 40));
        file ??= $"frames/{Entries.Count:D4}.png";
        WriteFile(file, png);
        var entry = new JsonObject
        {
            ["kind"] = "sprite", ["file"] = file, ["sha256"] = Sha(png), ["bytes"] = png.Length, ["slice"] = slice,
            ["art"] = new JsonObject { ["sha256"] = frame.ArtSha256, ["view"] = Frame.View, ["nameSha256"] = frame.NameSha256 },
            ["group"] = frame.Group, ["frame"] = frame.FrameIndex, ["palette"] = frame.Palette,
            ["legacy"] = new JsonObject { ["width"] = frame.Width, ["height"] = frame.Height, ["rgbaSha256"] = frame.RgbaSha256 },
            ["review"] = "unreviewed",
        };
        Entries.Add(entry);
        return entry;
    }

    public void WriteFile(string relative, byte[] bytes)
    {
        var path = Path.Combine([Directory, .. relative.Split('/')]);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>Writes recipe.json and manifest.json (pinning the recipe hash unless
    /// <paramref name="pinRecipe"/> is false).</summary>
    public string Write(bool pinRecipe = true)
    {
        var recipe = Encoding.UTF8.GetBytes(RecipeText);
        WriteFile("recipe.json", recipe);
        if (pinRecipe) Manifest["recipe"]!["sha256"] = Sha(recipe);
        WriteFile("manifest.json", Encoding.UTF8.GetBytes(Manifest.ToJsonString()));
        return Directory;
    }
}
