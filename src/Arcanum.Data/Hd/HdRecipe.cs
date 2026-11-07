using System.Text.Json;

namespace Arcanum.Data.Hd;

public enum HdAlphaMode { Legacy, Soft }
public enum HdDownscale { None, Box }
/// <summary>What a recipe slice selects. Which kinds a renderer binds is the renderer's
/// rule (ARC-RENDER-013: characters and objects first; walls, facades, tiles and terrain
/// chunks stay Legacy until their stage); the recipe only names what the build makes.
/// <see cref="Interface"/> (ARC-DEV-242) is interface ART (<c>art/interface</c>: panels, strips,
/// buttons, gauges, icons) keyed by <see cref="HdInterfaceKey"/>.</summary>
public enum HdSliceKind { Player, Critter, Npc, Scenery, Container, Item, Wall, Facade, Tile, TerrainChunk, Interface }

public enum HdMethodKind
{
    /// <summary>A neural network run by an external tool: model files, explicit tile and
    /// TTA are pinned because they change the output (ARC-RES-033).</summary>
    Network,
    /// <summary>A generative model that draws new detail (diffusion and similar): base model
    /// files, adapters (LoRA and similar), seed, prompt and sampler parameters are pinned
    /// for provenance; a rerun need not reproduce the pixels.</summary>
    Generative,
    /// <summary>A deterministic algorithm run by an external tool (no model).</summary>
    Algorithm,
    /// <summary>Frames made by any other process (another pipeline, hand work); only the
    /// producer and its version are recorded.</summary>
    External,
}
public sealed record HdModelFile(string Name, string Sha256);
/// <summary>A model adapter applied on top of the base model (LoRA and similar). Only its
/// name and hash are recorded; an adapter trained on Arcanum art is itself derived from it
/// and, like the pack, stays outside the repository.</summary>
public sealed record HdAdapter(string Name, string Sha256, string Weight);
/// <summary>The upscale method, recorded for provenance and repeatability. The format does
/// not prefer any method: the pack is checked by what it contains (size, hashes, alpha
/// mask), not by how it was made. The examples under docs/formats/examples are the methods
/// ARC-RES-033 measured, not defaults.</summary>
/// <param name="NativeScale">The method's own factor: the pack scale with
/// <see cref="HdDownscale.None"/>, or a multiple k·scale (k ≥ 2) followed by an offline box
/// downscale to the pack scale.</param>
/// <param name="TileSize">The explicit model tile in input pixels; required for a network,
/// optional for a generative model. Automatic tiling (0) is refused because it depends on
/// the GPU's memory; the build refuses any frame or padded chunk with a side larger than
/// this (one tile per input).</param>
/// <param name="Parameters">Any further method parameters, as name → value labels.</param>
/// <param name="Seed">Generative only: the base seed.</param>
/// <param name="Prompt">Generative only: the text prompt (and <see cref="NegativePrompt"/>).</param>
public sealed record HdMethod(HdMethodKind Kind, string Tool, string ToolVersion, string? ToolSha256, string? Model, string? Licence,
    IReadOnlyList<HdModelFile> ModelFiles, int NativeScale, HdDownscale Downscale, int? TileSize, bool Tta,
    IReadOnlyDictionary<string, string> Parameters, IReadOnlyList<HdAdapter> Adapters, long? Seed = null, string? Prompt = null,
    string? NegativePrompt = null);
/// <summary>Optional colour preparation before the method runs (needed by networks, which
/// otherwise see transparent black around a sprite and leave a dark halo, ARC-RES-033).</summary>
/// <param name="EdgeExtend">Pixels by which opaque colours are bled into the transparent area
/// before the colour pass; the rest of the transparent area takes the mean opaque colour.</param>
public sealed record HdColourPreparation(int EdgeExtend, string Fill);
/// <param name="MinSoftIoUPermille">Soft alpha only: the least intersection-over-union, in
/// thousandths, of the HD mask (alpha ≥ 128) with the nearest Legacy mask.</param>
public sealed record HdAlphaPolicy(HdAlphaMode Mode, int MinSoftIoUPermille);
/// <param name="Alpha">ARC-DEV-242: this slice's own alpha policy; null takes the recipe's. A pack of
/// soft-edged sprites and Legacy-alpha interface art (exact parent-texel alpha) needs both.</param>
public sealed record HdSlice(string Name, string Map, IReadOnlyList<HdSliceKind> Kinds, HdAlphaPolicy? Alpha = null);
/// <param name="Padding">Source pixels of neighbour context around each chunk, upscaled
/// with it as one tile and cropped away (ARC-RES-033: at least 32).</param>
public sealed record HdTerrainRecipe(int ChunkSize, int Padding);
/// <summary>How to build an HD pack on a device: what to select and exactly which
/// method, tool, model and parameters to run. It holds no pixels and no paths and may
/// be kept in the repository. Two builds of one recipe on different GPUs need not give
/// equal pixels (ARC-RES-033); each build is its own pack identity.</summary>
public sealed record HdRecipe(string Id, int Scale, IReadOnlyList<HdSlice> Slices, HdMethod Method,
    HdColourPreparation? Rgb, HdAlphaPolicy Alpha, HdTerrainRecipe? Terrain);

public enum HdRecipeStatus { Parsed, TooLarge, Malformed, UnsupportedSchema, InvalidField }
public sealed record HdRecipeResult(HdRecipeStatus Status, HdRecipe? Recipe, string? Detail = null);

public static class HdRecipeReader
{
    public const string Kind = "arcanum-steamwoven-hd-recipe";
    public const int MaxSlices = 64;
    public const int MaxTileSize = 4096;
    public const int TerrainChunkSize = 512;
    public const int MinTerrainPadding = 32;
    public const int MinSoftIoUPermille = 900;
    public const int MaxParameters = 32;
    public const int MaxAdapters = 16;
    public const int MaxPromptLength = 4000;

    private static readonly Dictionary<string, HdSliceKind> Kinds = new(StringComparer.Ordinal)
    {
        ["player"] = HdSliceKind.Player, ["critter"] = HdSliceKind.Critter, ["npc"] = HdSliceKind.Npc,
        ["scenery"] = HdSliceKind.Scenery, ["container"] = HdSliceKind.Container, ["item"] = HdSliceKind.Item,
        ["wall"] = HdSliceKind.Wall, ["facade"] = HdSliceKind.Facade, ["tile"] = HdSliceKind.Tile,
        ["terrainChunk"] = HdSliceKind.TerrainChunk, ["interface"] = HdSliceKind.Interface,
    };
    private static readonly Dictionary<string, HdMethodKind> MethodKinds = new(StringComparer.Ordinal)
    { ["network"] = HdMethodKind.Network, ["generative"] = HdMethodKind.Generative, ["algorithm"] = HdMethodKind.Algorithm, ["external"] = HdMethodKind.External };
    private static readonly Dictionary<string, HdDownscale> Downscales = new(StringComparer.Ordinal) { ["none"] = HdDownscale.None, ["box"] = HdDownscale.Box };
    private static readonly Dictionary<string, HdAlphaMode> AlphaModes = new(StringComparer.Ordinal) { ["legacy"] = HdAlphaMode.Legacy, ["soft"] = HdAlphaMode.Soft };

    public static HdRecipeResult Parse(ReadOnlySpan<byte> json, HdPackOptions? options = null)
    {
        options ??= new(); options.Validate();
        if (json.Length > options.MaxRecipeBytes) return new(HdRecipeStatus.TooLarge, null, $"{json.Length} bytes");
        try
        {
            using var document = JsonDocument.Parse(json.ToArray(), HdJson.Options);
            return new(HdRecipeStatus.Parsed, Read(document.RootElement, options));
        }
        catch (JsonException) { return new(HdRecipeStatus.Malformed, null, "not JSON"); }
        catch (HdFormatException error)
        {
            return new(error.Error switch
            {
                HdFormatError.UnsupportedSchema => HdRecipeStatus.UnsupportedSchema,
                HdFormatError.Malformed => HdRecipeStatus.Malformed,
                _ => HdRecipeStatus.InvalidField,
            }, null, error.Message);
        }
    }

    private static HdRecipe Read(JsonElement element, HdPackOptions options)
    {
        var root = HdJson.Object(element, "recipe", ["schema", "kind", "id", "scale", "slices", "method", "alpha"], "rgb", "terrain");
        HdJson.Schema(root, Kind);
        var id = HdJson.Matching(root["id"], "recipe.id", HdJson.IdPattern());
        var scale = HdJson.Int(root["scale"], "recipe.scale", 2, 16);
        if (!options.Scales.Contains(scale)) throw HdJson.Invalid("recipe.scale", "is not an accepted scale");

        var slices = HdJson.Array(root["slices"], "recipe.slices", 1, MaxSlices).Select((slice, i) =>
        {
            var path = $"recipe.slices[{i}]";
            var fields = HdJson.Object(slice, path, ["name", "map", "kinds"], "alpha");
            var kinds = HdJson.Array(fields["kinds"], path + ".kinds", 1, Kinds.Count)
                .Select((kind, k) => HdJson.Enum(kind, $"{path}.kinds[{k}]", Kinds)).ToArray();
            if (kinds.Distinct().Count() != kinds.Length) throw HdJson.Invalid(path + ".kinds", "has duplicates");
            return new HdSlice(HdJson.Matching(fields["name"], path + ".name", HdJson.IdPattern()),
                HdJson.Matching(fields["map"], path + ".map", HdJson.IdPattern()), kinds,
                fields.TryGetValue("alpha", out var sliceAlpha) ? ReadAlpha(sliceAlpha, path + ".alpha") : null);
        }).ToArray();
        if (slices.Select(s => s.Name).Distinct(StringComparer.Ordinal).Count() != slices.Length)
            throw HdJson.Invalid("recipe.slices", "has duplicate names");

        var method = ReadMethod(root["method"], scale);

        HdColourPreparation? colour = null;
        if (root.TryGetValue("rgb", out var rgbElement))
        {
            var rgb = HdJson.Object(rgbElement, "recipe.rgb", ["edgeExtend", "fill"]);
            var fill = HdJson.String(rgb["fill"], "recipe.rgb.fill");
            if (fill != "mean") throw HdJson.Invalid("recipe.rgb.fill", "must be mean");
            colour = new(HdJson.Int(rgb["edgeExtend"], "recipe.rgb.edgeExtend", 1, 64), fill);
        }

        var alpha = ReadAlpha(root["alpha"], "recipe.alpha");

        HdTerrainRecipe? terrain = null;
        if (root.TryGetValue("terrain", out var t))
        {
            var fields = HdJson.Object(t, "recipe.terrain", ["chunkSize", "padding"]);
            terrain = new(HdJson.Int(fields["chunkSize"], "recipe.terrain.chunkSize", TerrainChunkSize, TerrainChunkSize),
                HdJson.Int(fields["padding"], "recipe.terrain.padding", MinTerrainPadding, 256));
        }
        var wantsTerrain = slices.Any(s => s.Kinds.Contains(HdSliceKind.TerrainChunk));
        if (wantsTerrain != terrain is not null) throw HdJson.Invalid("recipe.terrain", "must be present exactly when a slice selects terrainChunk");
        if (terrain is not null && method.TileSize is { } tileSize && tileSize < terrain.ChunkSize + 2 * terrain.Padding)
            throw HdJson.Invalid("recipe.method.tileSize", "must hold a padded terrain chunk as one tile");
        return new(id, scale, slices, method, colour, alpha, terrain);
    }

    private static HdAlphaPolicy ReadAlpha(JsonElement element, string path)
    {
        var a = HdJson.Object(element, path, ["mode"], "minIoUPermille");
        var mode = HdJson.Enum(a["mode"], path + ".mode", AlphaModes);
        if (mode == HdAlphaMode.Legacy)
        {
            if (a.ContainsKey("minIoUPermille")) throw HdJson.Invalid(path + ".minIoUPermille", "applies only to soft alpha");
            return new(mode, 1000);
        }
        if (!a.TryGetValue("minIoUPermille", out var min)) throw HdJson.Invalid(path + ".minIoUPermille", "missing");
        return new(mode, HdJson.Int(min, path + ".minIoUPermille", MinSoftIoUPermille, 1000));
    }

    private static HdMethod ReadMethod(JsonElement element, int scale)
    {
        const string path = "recipe.method";
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("kind", out var kindElement)) throw HdJson.Invalid(path + ".kind", "missing");
        var kind = HdJson.Enum(kindElement, path + ".kind", MethodKinds);
        string[] common = ["kind", "tool", "toolVersion", "nativeScale", "downscale"];
        var m = kind switch
        {
            HdMethodKind.Network => HdJson.Object(element, path, [.. common, "toolSha256", "model", "licence", "modelFiles", "tileSize", "tta"], "parameters"),
            HdMethodKind.Generative => HdJson.Object(element, path, [.. common, "model", "licence", "modelFiles"],
                "toolSha256", "tileSize", "adapters", "seed", "prompt", "negativePrompt", "parameters"),
            HdMethodKind.Algorithm => HdJson.Object(element, path, [.. common, "toolSha256"], "licence", "parameters"),
            _ => HdJson.Object(element, path, common, "toolSha256", "licence", "parameters"),
        };
        var native = HdJson.Int(m["nativeScale"], path + ".nativeScale", 2, 64);
        var downscale = HdJson.Enum(m["downscale"], path + ".downscale", Downscales);
        if (!(native == scale && downscale == HdDownscale.None) && !(native > scale && native % scale == 0 && downscale == HdDownscale.Box))
            throw HdJson.Invalid(path + ".nativeScale", "must equal scale with downscale none, or a multiple of scale with downscale box");
        IReadOnlyList<HdModelFile> files = [];
        int? tile = null;
        if (kind is HdMethodKind.Network or HdMethodKind.Generative)
        {
            files = HdJson.Array(m["modelFiles"], path + ".modelFiles", 1, 8).Select((file, i) =>
            {
                var at = $"{path}.modelFiles[{i}]";
                var f = HdJson.Object(file, at, ["name", "sha256"]);
                return new HdModelFile(HdJson.Matching(f["name"], at + ".name", HdJson.FileNamePattern()), HdJson.Digest(f["sha256"], at + ".sha256"));
            }).ToArray();
            if (files.Select(f => f.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count)
                throw HdJson.Invalid(path + ".modelFiles", "has duplicate names");
            if (m.TryGetValue("tileSize", out var t)) tile = HdJson.Int(t, path + ".tileSize", 1, MaxTileSize);
            if (kind == HdMethodKind.Network && HdJson.Bool(m["tta"], path + ".tta")) throw HdJson.Invalid(path + ".tta", "must be false");
        }
        IReadOnlyList<HdAdapter> adapters = !m.TryGetValue("adapters", out var adapterArray) ? [] :
            HdJson.Array(adapterArray, path + ".adapters", 1, MaxAdapters).Select((adapter, i) =>
            {
                var at = $"{path}.adapters[{i}]";
                var a = HdJson.Object(adapter, at, ["name", "sha256", "weight"]);
                return new HdAdapter(HdJson.Matching(a["name"], at + ".name", HdJson.FileNamePattern()), HdJson.Digest(a["sha256"], at + ".sha256"),
                    HdJson.Label(a["weight"], at + ".weight"));
            }).ToArray();
        if (adapters.Select(a => a.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != adapters.Count)
            throw HdJson.Invalid(path + ".adapters", "has duplicate names");
        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (m.TryGetValue("parameters", out var p))
        {
            if (p.ValueKind != JsonValueKind.Object) throw HdJson.Invalid(path + ".parameters", "must be an object");
            foreach (var (name, value) in HdJson.Properties(p, path + ".parameters"))
            {
                var at = path + ".parameters." + name;
                if (!HdJson.ParameterPattern().IsMatch(name)) throw HdJson.Invalid(at, "has an invalid name");
                if (!parameters.TryAdd(name, HdJson.Label(value, at))) throw HdJson.Invalid(at, "duplicate property");
                if (parameters.Count > MaxParameters) throw HdJson.Invalid(path + ".parameters", $"must have at most {MaxParameters} entries");
            }
        }
        string? Optional(string name, Func<JsonElement, string, string> read) => m.TryGetValue(name, out var v) ? read(v, path + "." + name) : null;
        return new(kind, HdJson.Label(m["tool"], path + ".tool"), HdJson.Label(m["toolVersion"], path + ".toolVersion"),
            Optional("toolSha256", HdJson.Digest), Optional("model", HdJson.Label), Optional("licence", HdJson.Label),
            files, native, downscale, tile, false, parameters, adapters,
            m.TryGetValue("seed", out var seed) ? HdJson.Long(seed, path + ".seed", 0, long.MaxValue) : null,
            Optional("prompt", (e, at) => HdJson.Text(e, at, MaxPromptLength)), Optional("negativePrompt", (e, at) => HdJson.Text(e, at, MaxPromptLength)));
    }
}
