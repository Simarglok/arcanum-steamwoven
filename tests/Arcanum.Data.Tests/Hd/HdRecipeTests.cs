using System.Text;
using Arcanum.Data.Hd;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Hd;

[TestClass]
public sealed class HdRecipeTests
{
    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Arcanum.Steamwoven.sln"))) return dir.FullName;
        throw new InvalidOperationException("repository root not found");
    }

    private static HdRecipeResult Parse(string json, HdPackOptions? options = null) => HdRecipeReader.Parse(Encoding.UTF8.GetBytes(json), options);

    private static HdRecipe Example(string name)
    {
        var result = HdRecipeReader.Parse(File.ReadAllBytes(Path.Combine(RepositoryRoot(), "docs", "formats", "examples", name)));
        Assert.AreEqual(HdRecipeStatus.Parsed, result.Status, $"{name}: {result.Detail}");
        return result.Recipe!;
    }

    [TestMethod]
    public void Documented_example_recipes_parse_and_the_4x_soft_edge_one_has_the_decided_shape()
    {
        var bodies = new[] { HdSliceKind.Player, HdSliceKind.Critter, HdSliceKind.Npc, HdSliceKind.Scenery, HdSliceKind.Container, HdSliceKind.Item };
        var soft = Example("hd-recipe-x4plus-4x-soft.json");
        Assert.AreEqual(4, soft.Scale);
        Assert.AreEqual(new HdAlphaPolicy(HdAlphaMode.Soft, 950), soft.Alpha);
        Assert.AreEqual((HdMethodKind.Network, "realesrgan-x4plus", 4, HdDownscale.None, (int?)600, false),
            (soft.Method.Kind, soft.Method.Model, soft.Method.NativeScale, soft.Method.Downscale, soft.Method.TileSize, soft.Method.Tta));
        Assert.AreEqual(new HdColourPreparation(8, "mean"), soft.Rgb);
        Assert.IsNull(soft.Terrain);
        CollectionAssert.AreEqual(bodies, soft.Slices.Single().Kinds.ToArray());

        var av3 = Example("hd-recipe-av3-2x.json");
        Assert.AreEqual((2, HdAlphaMode.Legacy, "realesr-animevideov3-x2", 2), (av3.Scale, av3.Alpha.Mode, av3.Method.Model, av3.Method.ModelFiles.Count));

        var hq2x = Example("hd-recipe-hq2x.json");
        Assert.AreEqual((HdMethodKind.Algorithm, (string?)null, (int?)null, 0), (hq2x.Method.Kind, hq2x.Method.Model, hq2x.Method.TileSize, hq2x.Method.ModelFiles.Count));
        Assert.AreEqual("hq2X", hq2x.Method.Parameters["magnifyMethod"]);
        Assert.IsNull(hq2x.Rgb);

        var generative = Example("hd-recipe-generative-4x.json");
        var m = generative.Method;
        Assert.AreEqual((HdMethodKind.Generative, 4, 2, 1, 20261016L), (m.Kind, generative.Scale, m.ModelFiles.Count, m.Adapters.Count, m.Seed));
        Assert.AreEqual(("example-arcanum-style-lora.safetensors", "0.8"), (m.Adapters[0].Name, m.Adapters[0].Weight));
        Assert.IsNotNull(m.Prompt); Assert.IsNotNull(m.NegativePrompt);
        Assert.AreEqual("0.35", m.Parameters["denoise"]);
    }

    [TestMethod]
    public void Generative_provenance_is_bounded_and_carries_no_paths()
    {
        const string head = """{"schema":1,"kind":"arcanum-steamwoven-hd-recipe","id":"g","scale":4,"slices":[{"name":"s","map":"m","kinds":["item"]}],"alpha":{"mode":"soft","minIoUPermille":950},"method":{"kind":"generative","tool":"t","toolVersion":"1","model":"base","licence":"l","nativeScale":4,"downscale":"none","modelFiles":[{"name":"base.safetensors","sha256":"0000000000000000000000000000000000000000000000000000000000000001"}]""";
        HdRecipeStatus With(string extra) => Parse(head + extra + "}}").Status;
        Assert.AreEqual(HdRecipeStatus.Parsed, With(""), "seed, prompt and adapters are optional");
        Assert.AreEqual(HdRecipeStatus.Parsed, With(""","prompt":"Стиль Arcanum, рисованный спрайт 🎨","seed":0"""), "any language in a prompt");
        Assert.AreEqual(HdRecipeStatus.InvalidField, With(",\"prompt\":\"line\\nbreak\""), "control character");
        Assert.AreEqual(HdRecipeStatus.InvalidField, With(",\"prompt\":\"\""), "empty prompt");
        Assert.AreEqual(HdRecipeStatus.InvalidField, With(",\"prompt\":\"" + new string('a', 4001) + "\""), "prompt too long");
        Assert.AreEqual(HdRecipeStatus.InvalidField, With(""","seed":-1"""), "negative seed");
        Assert.AreEqual(HdRecipeStatus.InvalidField, With(""","adapters":[{"name":"../lora.safetensors","sha256":"0000000000000000000000000000000000000000000000000000000000000002","weight":"1"}]"""), "path in adapter");
        Assert.AreEqual(HdRecipeStatus.InvalidField, With(""","adapters":[]"""), "empty adapter list");
        Assert.AreEqual(HdRecipeStatus.InvalidField, With(""","tta":false"""), "TTA is a network pin");
        Assert.AreEqual(HdRecipeStatus.InvalidField, Parse(head.Replace(""","modelFiles":[{"name":"base.safetensors","sha256":"0000000000000000000000000000000000000000000000000000000000000001"}]""", "") + "}}").Status, "base model hash required");
    }

    [TestMethod]
    public void Method_kinds_require_exactly_their_own_pins()
    {
        const string head = """{"schema":1,"kind":"arcanum-steamwoven-hd-recipe","id":"m","scale":2,"slices":[{"name":"s","map":"m","kinds":["item"]}],"alpha":{"mode":"legacy"},"method":""";
        const string sha = "0000000000000000000000000000000000000000000000000000000000000003";
        HdRecipeStatus Method(string body) => Parse(head + body.Replace("SHA", sha) + "}").Status;
        Assert.AreEqual(HdRecipeStatus.Parsed, Method("""{"kind":"external","tool":"hand-painted","toolVersion":"2026-10","nativeScale":2,"downscale":"none"}"""));
        Assert.AreEqual(HdRecipeStatus.Parsed, Method("""{"kind":"external","tool":"other-pipeline","toolVersion":"3","nativeScale":8,"downscale":"box","parameters":{"steps":"30"}}"""));
        Assert.AreEqual(HdRecipeStatus.Parsed, Method("""{"kind":"algorithm","tool":"t","toolVersion":"1","toolSha256":"SHA","nativeScale":2,"downscale":"none"}"""));
        Assert.AreEqual(HdRecipeStatus.InvalidField, Method("""{"kind":"algorithm","tool":"t","toolVersion":"1","nativeScale":2,"downscale":"none"}"""), "algorithm without tool hash");
        Assert.AreEqual(HdRecipeStatus.InvalidField, Method("""{"kind":"algorithm","tool":"t","toolVersion":"1","toolSha256":"SHA","nativeScale":2,"downscale":"none","tileSize":64}"""), "tile on an algorithm");
        Assert.AreEqual(HdRecipeStatus.InvalidField, Method("""{"kind":"external","tool":"t","toolVersion":"1","nativeScale":5,"downscale":"box"}"""), "5 is not a multiple of 2");
    }

    [TestMethod]
    public void Method_parameters_are_bounded_labels()
    {
        const string head = """{"schema":1,"kind":"arcanum-steamwoven-hd-recipe","id":"m","scale":2,"slices":[{"name":"s","map":"m","kinds":["item"]}],"alpha":{"mode":"legacy"},"method":{"kind":"external","tool":"t","toolVersion":"1","nativeScale":2,"downscale":"none","parameters":""";
        HdRecipeStatus Parameters(string body) => Parse(head + body + "}}").Status;
        Assert.AreEqual(HdRecipeStatus.Parsed, Parameters("""{"strength":"0.5","seed":"7"}"""));
        Assert.AreEqual(HdRecipeStatus.InvalidField, Parameters("""{"Strength":"0.5"}"""), "uppercase first letter");
        Assert.AreEqual(HdRecipeStatus.InvalidField, Parameters("""{"seed":7}"""), "number value");
        Assert.AreEqual(HdRecipeStatus.InvalidField, Parameters("""{"model":"/opt/models/x.bin"}"""), "path value");
        Assert.AreEqual(HdRecipeStatus.InvalidField, Parameters("""{"a":"1","a":"2"}"""), "duplicate");
        Assert.AreEqual(HdRecipeStatus.InvalidField, Parameters("{" + string.Join(",", Enumerable.Range(0, 33).Select(i => $"\"p{i}\":\"1\"")) + "}"), "33 entries");
    }

    [TestMethod]
    public void Recipe_pins_explicit_tile_no_tta_and_a_consistent_model_scale()
    {
        var good = SyntheticHdPack.Recipe;
        Assert.AreEqual(HdRecipeStatus.Parsed, Parse(good).Status);
        foreach (var (from, to, why) in new[]
        {
            ("\"tileSize\":512", "\"tileSize\":0", "automatic tile"),
            ("\"tileSize\":512", "\"tileSize\":4097", "tile above limit"),
            ("\"tta\":false", "\"tta\":true", "TTA"),
            ("\"nativeScale\":2", "\"nativeScale\":4", "x4 without box"),
            ("\"downscale\":\"none\"", "\"downscale\":\"box\"", "box without a larger native scale"),
            ("\"nativeScale\":2,\"downscale\":\"none\"", "\"nativeScale\":3,\"downscale\":\"box\"", "native scale not a multiple"),
            ("\"model\":\"synthetic-x2\",", "", "network without model"),
            ("\"tileSize\":512,", "", "network without tile"),
            ("\"kind\":\"network\"", "\"kind\":\"magic\"", "unknown method kind"),
            ("\"edgeExtend\":8", "\"edgeExtend\":0", "no bleed"),
            ("\"fill\":\"mean\"", "\"fill\":\"black\"", "black fill"),
            ("\"tool\":\"synthetic-upscaler\"", "\"tool\":\"/Users/me/bin/upscaler\"", "path in label"),
            ("\"tool\":\"synthetic-upscaler\"", "\"tool\":\"C:\\\\tools\\\\up\"", "Windows path in label"),
            ("\"name\":\"synthetic-x2.bin\"", "\"name\":\"../x.bin\"", "path in model file"),
            ("\"kinds\":[\"critter\",\"scenery\"]", "\"kinds\":[\"critter\",\"critter\"]", "duplicate kind"),
            ("\"kinds\":[\"critter\",\"scenery\"]", "\"kinds\":[\"monster\"]", "unknown kind"),
            ("\"kinds\":[\"critter\",\"scenery\"]", "\"kinds\":[\"terrainChunk\"]", "terrain kind without terrain"),
            ("\"alpha\":{\"mode\":\"legacy\"}", "\"alpha\":{\"mode\":\"soft\"}", "soft without threshold"),
            ("\"alpha\":{\"mode\":\"legacy\"}", "\"alpha\":{\"mode\":\"soft\",\"minIoUPermille\":899}", "soft threshold too low"),
            ("\"alpha\":{\"mode\":\"legacy\"}", "\"alpha\":{\"mode\":\"legacy\",\"minIoUPermille\":950}", "threshold on legacy alpha"),
            ("\"id\":\"synthetic-av3\"", "\"id\":\"Synthetic\"", "uppercase id"),
            ("\"id\":\"synthetic-av3\"", "\"id\":\"synthetic-av3\",\"extra\":1", "unknown property"),
            ("\"id\":\"synthetic-av3\"", "\"id\":\"synthetic-av3\",\"id\":\"other\"", "duplicate property"),
            ("\"tileSize\":512", "\"tileSize\":512.0", "non-integer"),
            ("\"scale\":2", "\"scale\":3", "scale not accepted"),
        })
        {
            var text = good.Replace(from, to);
            Assert.AreNotEqual(good, text, why);
            Assert.AreEqual(HdRecipeStatus.InvalidField, Parse(text).Status, why);
        }
        Assert.AreEqual(HdRecipeStatus.UnsupportedSchema, Parse(good.Replace("\"schema\":1", "\"schema\":2")).Status);
        Assert.AreEqual(HdRecipeStatus.UnsupportedSchema, Parse(good.Replace("hd-recipe", "hd-pack")).Status);
        Assert.AreEqual(HdRecipeStatus.Malformed, Parse(good[..^5]).Status);
        Assert.AreEqual(HdRecipeStatus.TooLarge, Parse(good, new HdPackOptions { MaxRecipeBytes = 100 }).Status);
    }

    [TestMethod]
    public void Soft_alpha_and_padded_terrain_chunks_are_explicit_opt_ins()
    {
        var soft = Parse(SyntheticHdPack.Recipe.Replace("\"alpha\":{\"mode\":\"legacy\"}", "\"alpha\":{\"mode\":\"soft\",\"minIoUPermille\":950}"));
        Assert.AreEqual(HdRecipeStatus.Parsed, soft.Status, soft.Detail);
        Assert.AreEqual(new HdAlphaPolicy(HdAlphaMode.Soft, 950), soft.Recipe!.Alpha);

        var withTerrain = SyntheticHdPack.Recipe.Replace("\"kinds\":[\"critter\",\"scenery\"]", "\"kinds\":[\"terrainChunk\"]")
            .Replace("\"alpha\":{\"mode\":\"legacy\"}", "\"alpha\":{\"mode\":\"legacy\"},\"terrain\":{\"chunkSize\":512,\"padding\":32}");
        var terrain = Parse(withTerrain);
        Assert.AreEqual(HdRecipeStatus.Parsed, terrain.Status, terrain.Detail);
        Assert.AreEqual(new HdTerrainRecipe(512, 32), terrain.Recipe!.Terrain);
        Assert.AreEqual(HdRecipeStatus.InvalidField, Parse(withTerrain.Replace("\"padding\":32", "\"padding\":16")).Status, "padding below 32");
        Assert.AreEqual(HdRecipeStatus.InvalidField, Parse(withTerrain.Replace("\"chunkSize\":512", "\"chunkSize\":256")).Status, "chunk not 512");
        Assert.AreEqual(HdRecipeStatus.InvalidField, Parse(withTerrain.Replace("\"kinds\":[\"terrainChunk\"]", "\"kinds\":[\"critter\"]")).Status,
            "terrain section without a terrain slice");
    }
}
