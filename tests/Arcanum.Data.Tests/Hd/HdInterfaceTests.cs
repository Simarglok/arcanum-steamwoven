using System.IO.Compression;
using System.Text;
using Arcanum.Data.Hd;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Hd;

/// <summary>ARC-DEV-242: the interface slice kind, a slice's own alpha policy, the interface ART key, the redraw lock and fit, the import
/// plan and the pack writer. Synthetic frames only: no original pixel is involved.</summary>
[TestClass]
public sealed class HdInterfaceTests
{
    private string _root = null!;

    [TestInitialize]
    public void Create() => Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "arc-hdi-" + Guid.NewGuid().ToString("N")));

    [TestCleanup]
    public void Remove()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static HdRecipeResult Parse(string json) => HdRecipeReader.Parse(Encoding.UTF8.GetBytes(json));

    private static HdLegacyRaster Pattern(int width, int height, int seed = 1, bool holes = true)
    {
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var at = (y * width + x) * 4;
            if (holes && (x * 5 + y * 3 + seed) % 11 == 0) continue;
            var v = (byte)(60 + 120 * (0.5 + 0.5 * Math.Sin(x * 0.7 + seed) * Math.Cos(y * 0.5)));
            rgba[at] = v; rgba[at + 1] = (byte)(v / 2 + 40); rgba[at + 2] = (byte)(255 - v); rgba[at + 3] = 255;
        }
        return new(width, height, rgba);
    }

    // A smooth, non-periodic picture (bilinear value noise on a 6-texel grid): registration needs something that does not repeat.
    private static HdLegacyRaster Smooth(int width, int height, int seed)
    {
        var random = new Random(seed);
        int gw = width / 6 + 2, gh = height / 6 + 2;
        var grid = new double[gw * gh * 3];
        for (var i = 0; i < grid.Length; i++) grid[i] = random.Next(30, 226);
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            double fx = x / 6.0, fy = y / 6.0; int x0 = (int)fx, y0 = (int)fy; var tx = fx - x0; var ty = fy - y0;
            for (var c = 0; c < 3; c++)
            {
                var top = grid[(y0 * gw + x0) * 3 + c] * (1 - tx) + grid[(y0 * gw + x0 + 1) * 3 + c] * tx;
                var bottom = grid[((y0 + 1) * gw + x0) * 3 + c] * (1 - tx) + grid[((y0 + 1) * gw + x0 + 1) * 3 + c] * tx;
                rgba[(y * width + x) * 4 + c] = (byte)(top * (1 - ty) + bottom * ty);
            }
            rgba[(y * width + x) * 4 + 3] = 255;
        }
        return new(width, height, rgba);
    }

    private static HdInterfaceArt Art(string name, params HdLegacyRaster[] frames)
        => new(name, HdInterfaceKey.Identity(name, Encoding.ASCII.GetBytes("payload:" + name + new string('.', 200))), frames);

    private static float[] Rgb(HdLegacyRaster raster)
    {
        var rgb = new float[raster.Width * raster.Height * 3];
        for (var i = 0; i < raster.Width * raster.Height; i++) for (var c = 0; c < 3; c++) rgb[i * 3 + c] = raster.Rgba[i * 4 + c];
        return rgb;
    }

    // ---- recipe: interface kind and per-slice alpha ----

    [TestMethod]
    public void The_interface_kind_and_a_slice_alpha_parse_and_the_documented_example_has_that_shape()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "formats", "examples", "hd-recipe-interface-redraw-4x.json");
        var example = HdRecipeReader.Parse(File.ReadAllBytes(Path.GetFullPath(path)));
        Assert.AreEqual(HdRecipeStatus.Parsed, example.Status, example.Detail);
        var slice = example.Recipe!.Slices.Single();
        CollectionAssert.AreEqual(new[] { HdSliceKind.Interface }, slice.Kinds.ToArray());
        Assert.AreEqual(new HdAlphaPolicy(HdAlphaMode.Legacy, 1000), slice.Alpha);
        Assert.AreEqual(HdMethodKind.External, example.Recipe.Method.Kind);

        const string mixed = """{"schema":1,"kind":"arcanum-steamwoven-hd-recipe","id":"m","scale":4,"slices":[{"name":"bodies","map":"m","kinds":["item"]},{"name":"ui","map":"u","kinds":["interface"],"alpha":{"mode":"legacy"}}],"method":{"kind":"external","tool":"t","toolVersion":"1","nativeScale":4,"downscale":"none"},"alpha":{"mode":"soft","minIoUPermille":950}}""";
        var parsed = Parse(mixed).Recipe!;
        Assert.IsNull(parsed.Slices[0].Alpha);
        Assert.AreEqual(HdAlphaMode.Legacy, parsed.Slices[1].Alpha!.Mode);
        Assert.AreEqual(HdAlphaMode.Soft, parsed.Alpha.Mode);

        Assert.AreEqual(HdRecipeStatus.InvalidField, Parse(mixed.Replace("\"alpha\":{\"mode\":\"legacy\"}", "\"alpha\":{\"mode\":\"legacy\",\"minIoUPermille\":950}")).Status, "legacy takes no IoU");
        Assert.AreEqual(HdRecipeStatus.InvalidField, Parse(mixed.Replace("\"alpha\":{\"mode\":\"legacy\"}", "\"alpha\":{\"mode\":\"soft\"}")).Status, "soft needs its IoU");
    }

    // ---- pack: the slice's policy decides the alpha check ----

    [TestMethod]
    public void A_legacy_slice_holds_interface_frames_to_exact_alpha_while_the_soft_slice_beside_it_keeps_its_looser_rule()
    {
        const string recipe = """{"schema":1,"kind":"arcanum-steamwoven-hd-recipe","id":"mix","scale":2,"slices":[{"name":"bodies","map":"m","kinds":["item"]},{"name":"ui","map":"u","kinds":["interface"],"alpha":{"mode":"legacy"}}],"method":{"kind":"external","tool":"t","toolVersion":"1","nativeScale":2,"downscale":"none"},"alpha":{"mode":"soft","minIoUPermille":900}}""";
        var pack = new SyntheticHdPack(Path.Combine(_root, "mix"), 2, recipe);
        var ui = SyntheticHdPack.Legacy("panel", 8, 6, seed: 3);
        var body = SyntheticHdPack.Legacy("body", 8, 6, seed: 4);
        // A hole filled by one HD texel (alpha 0 -> 255 at a hole's corner) is within one Legacy texel: soft accepts it, legacy does not.
        byte[] Softened(SyntheticHdPack.Frame frame)
        {
            var hd = SyntheticHdPack.Hd(frame, 2, tint: 10);
            for (var i = 0; i < frame.Width * frame.Height; i++)
                if (frame.Rgba[i * 4 + 3] == 0) { var x = (i % frame.Width) * 2; var y = (i / frame.Width) * 2; hd[(y * frame.Width * 2 + x) * 4 + 3] = 255; }
            return hd;
        }
        pack.Add(ui, Softened(ui), slice: "ui");
        pack.Add(body, Softened(body), slice: "bodies");
        pack.Write();
        var opened = HdPack.Open(pack.Directory).Pack!;
        var uiResult = opened.ResolveSprite(ui.Query, ui.Rgba);
        Assert.AreEqual((HdFrameStatus.Invalid, HdInvalidReason.AlphaMismatch), (uiResult.Status, uiResult.Invalid), "the interface slice is Legacy-exact");
        Assert.AreEqual(HdFrameStatus.Hd, opened.ResolveSprite(body.Query, body.Rgba).Status, "the sprite slice is soft");

        var exact = new SyntheticHdPack(Path.Combine(_root, "exact"), 2, recipe);
        exact.Add(ui, SyntheticHdPack.Hd(ui, 2, tint: 10), slice: "ui");
        exact.Write();
        Assert.AreEqual(HdFrameStatus.Hd, HdPack.Open(exact.Directory).Pack!.ResolveSprite(ui.Query, ui.Rgba).Status);
    }

    // ---- key ----

    [TestMethod]
    public void The_interface_key_is_the_whole_file_group_zero_palette_zero_and_the_lowercase_path_hash()
    {
        Assert.AreEqual("art/interface/intbotom.art", HdInterfaceKey.Path("IntBotom.ART"));
        Assert.AreEqual("art/interface/intbotom.art", HdInterfaceKey.Path("art\\interface\\IntBotom.ART"));
        Assert.AreEqual(HdInterfaceKey.NameSha256("INTBOTOM.art"), HdInterfaceKey.NameSha256("IntBotom.ART"));
        var payload = new byte[] { 1, 2, 3, 4 };
        var identity = HdInterfaceKey.Identity("A.ART", payload);
        Assert.AreEqual(4, identity.ViewLength);
        Assert.AreEqual(HdPackPath.Sha256(payload), identity.PayloadSha256);
        var frame = Pattern(3, 2);
        var query = HdInterfaceKey.Query(identity, 5, 3, 2, frame.Rgba);
        Assert.AreEqual(new HdSpriteKey(identity.PayloadSha256, 4, 0, 5, 0), query.Key);
        Assert.AreEqual(identity.NameSha256, query.NameSha256);
        Assert.AreEqual(HdPackPath.Sha256(frame.Rgba), query.Legacy.RgbaSha256);
        Assert.ThrowsExactly<ArgumentException>(() => HdInterfaceKey.Query(identity, 0, 3, 3, frame.Rgba));
    }

    // ---- lock ----

    [TestMethod]
    public void The_shading_lock_pulls_the_redraws_luma_and_chroma_back_to_the_legacy_frame_and_the_alpha_is_the_parents()
    {
        var legacy = Pattern(10, 8, holes: true);
        var mask = Enumerable.Range(0, 80).Select(i => legacy.Rgba[i * 4 + 3] > 0).ToArray();
        // A "redraw": the nearest 4x replica, too bright and tinted, with texture noise inside each texel.
        var hd = new float[40 * 32 * 3];
        for (var y = 0; y < 32; y++)
        for (var x = 0; x < 40; x++)
        {
            var parent = ((y / 4) * 10 + x / 4) * 4;
            var noise = (x * 7 + y * 13) % 5 - 2;
            hd[(y * 40 + x) * 3] = Math.Clamp(legacy.Rgba[parent] + 40 + noise, 0, 255);
            hd[(y * 40 + x) * 3 + 1] = Math.Clamp(legacy.Rgba[parent + 1] + 40 + noise, 0, 255);
            hd[(y * 40 + x) * 3 + 2] = Math.Clamp(legacy.Rgba[parent + 2] + 10 + noise, 0, 255);
        }
        double Error(float[] image)
        {
            double sum = 0; var count = 0;
            for (var j = 0; j < 8; j++)
            for (var i = 0; i < 10; i++)
            {
                if (!mask[j * 10 + i]) continue;
                for (var c = 0; c < 3; c++)
                {
                    double mean = 0;
                    for (var dy = 0; dy < 4; dy++) for (var dx = 0; dx < 4; dx++) mean += image[((j * 4 + dy) * 40 + i * 4 + dx) * 3 + c] / 16;
                    sum += Math.Abs(mean - legacy.Rgba[(j * 10 + i) * 4 + c]); count++;
                }
            }
            return sum / count;
        }
        var before = Error(hd);
        var locked = HdRedrawLock.ShadingLock(hd, Rgb(legacy), mask, 10, 8, 4);
        Assert.IsTrue(before > 20, $"setup: {before}");
        Assert.IsTrue(Error(locked) < 2.5, $"locked error {Error(locked)}");
        Assert.IsTrue(locked.All(v => v is >= 0 and <= 255));

        var rgba = HdRedrawLock.LegacyAlpha(locked, legacy.Rgba, 10, 8, 4);
        for (var y = 0; y < 32; y++)
        for (var x = 0; x < 40; x++) Assert.AreEqual(legacy.Rgba[((y / 4) * 10 + x / 4) * 4 + 3], rgba[(y * 40 + x) * 4 + 3]);
        Assert.ThrowsExactly<ArgumentException>(() => HdRedrawLock.ShadingLock(hd, Rgb(legacy), mask, 10, 7, 4));
    }

    [TestMethod]
    public void A_derived_frame_is_the_redrawn_base_plus_the_enlarged_legacy_difference()
    {
        var a = Pattern(6, 6, seed: 1, holes: false); var b = Pattern(6, 6, seed: 2, holes: false);
        var baseRgb = Enumerable.Repeat(100f, 24 * 24 * 3).ToArray();
        var derived = HdRedrawLock.AddLegacyDifference(baseRgb, Rgb(b), Rgb(a), 6, 6, 4);
        // At a texel centre the bicubic enlargement of a smooth-enough difference is within a few levels of the difference itself.
        var difference = (Rgb(b)[(3 * 6 + 3) * 3] - Rgb(a)[(3 * 6 + 3) * 3]);
        Assert.AreEqual(Math.Clamp(100 + difference, 0, 255), derived[((3 * 4 + 1) * 24 + 3 * 4 + 1) * 3], 40, "same sign and scale");
        Assert.IsTrue(derived.All(v => v is >= 0 and <= 255));
        CollectionAssert.AreEqual(baseRgb, HdRedrawLock.AddLegacyDifference(baseRgb, Rgb(a), Rgb(a), 6, 6, 4), "no difference, no change");
    }

    // ---- fit ----

    [TestMethod]
    public void The_fit_finds_the_scale_and_offset_of_a_redraw_with_free_space_around_the_sheet()
    {
        var legacy = Smooth(48, 32, seed: 5);
        var sheet = HdRedrawSheet.Build([legacy], 8);
        // The "redraw": the sheet enlarged 6x (bicubic), then placed on a bigger flat canvas, shifted.
        var big = HdRaster.UpBicubic(ToSheetRgb(sheet), sheet.Width, sheet.Height, 3, 6);
        int bw = sheet.Width * 6, bh = sheet.Height * 6, canvasW = bw + 120, canvasH = bh + 60, ox = 70, oy = 20;
        var canvas = new float[canvasW * canvasH * 3];
        Array.Fill(canvas, 128f);
        for (var y = 0; y < bh; y++) Array.Copy(big, y * bw * 3, canvas, ((y + oy) * canvasW + ox) * 3, bw * 3);
        var fit = HdRedrawFit.Fit(new HdRgbImage(canvasW, canvasH, canvas), sheet).Single();
        Assert.AreEqual(6.0, fit.Sx, 0.3); Assert.AreEqual(6.0, fit.Sy, 0.3);
        Assert.AreEqual(ox, fit.Tx, 12); Assert.AreEqual(oy, fit.Ty, 12); // scale and offset trade off a little; the extraction below is the real check
        Assert.IsTrue(fit.Ncc > 0.9, $"ncc {fit.Ncc}");

        // The "redraw" is the sheet's luma, so compare with the enlarged Legacy luma.
        var referenceLuma = HdRaster.UpBicubic(HdRaster.Luma(Rgb(legacy), 48 * 32), 48, 32, 1, 4);
        double MeanError(HdFitTransform transform)
        {
            var extracted = HdRedrawFit.Extract(new HdRgbImage(canvasW, canvasH, canvas), sheet.Frames[0], transform, 4);
            double error = 0;
            for (var i = 0; i < referenceLuma.Length; i++) error += Math.Abs(extracted[i * 3] - referenceLuma[i]);
            return error / referenceLuma.Length;
        }
        Assert.IsTrue(MeanError(new(6, 6, ox, oy, 1)) < 3, "extraction with the true transform");
        Assert.IsTrue(MeanError(fit) < 3, $"extraction with the fitted transform {MeanError(fit)}");
    }

    private static float[] ToSheetRgb(HdRedrawSheet sheet)
    {
        // The sheet's luma as grey RGB: the fit only looks at luma.
        var rgb = new float[sheet.Width * sheet.Height * 3];
        for (var i = 0; i < sheet.Luma.Length; i++) { rgb[i * 3] = sheet.Luma[i]; rgb[i * 3 + 1] = sheet.Luma[i]; rgb[i * 3 + 2] = sheet.Luma[i]; }
        return rgb;
    }

    [TestMethod]
    public void A_grid_sheet_places_frames_row_by_row_in_equal_cells()
    {
        var frames = Enumerable.Range(0, 5).Select(i => Pattern(6 + i % 2, 4, seed: i)).ToArray();
        var sheet = HdRedrawSheet.Build(frames, 2, columns: 3);
        Assert.AreEqual((3 * 11, 2 * 8), (sheet.Width, sheet.Height));
        Assert.AreEqual(new HdSheetFrame(2, 2, 6, 4), sheet.Frames[0]);
        Assert.AreEqual(new HdSheetFrame(11 + 2, 2, 7, 4), sheet.Frames[1]);
        Assert.AreEqual(new HdSheetFrame(2, 8 + 2, 7, 4), sheet.Frames[3]);
        var row = HdRedrawSheet.Build(frames[..2], 2);
        Assert.AreEqual((10 + 11, 8), (row.Width, row.Height), "one row: each cell as wide as its own frame");
    }

    // ---- png ----

    [TestMethod]
    public void The_redraw_png_reader_takes_rgb_rgba_and_grey_and_refuses_palette_and_interlaced_files()
    {
        var rgba = new byte[] { 10, 20, 30, 255, 200, 100, 50, 0 };
        var rgbaPng = HdPng.EncodeRgba(2, 1, rgba);
        var (status, image, _) = HdImagePng.Decode(rgbaPng, 100);
        Assert.AreEqual(HdPngStatus.Decoded, status);
        CollectionAssert.AreEqual(new float[] { 10, 20, 30, 200, 100, 50 }, image!.Rgb);

        (HdPngStatus, HdRgbImage?, string?) Decode(int colourType, int depth, int interlace, byte[] row, byte[]? plte = null)
        {
            using var data = new MemoryStream();
            using (var z = new ZLibStream(data, CompressionLevel.Optimal, leaveOpen: true)) { z.WriteByte(0); z.Write(row); }
            using var png = new MemoryStream();
            png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
            Span<byte> ihdr = stackalloc byte[13];
            ihdr[3] = 2; ihdr[7] = 1; ihdr[8] = (byte)depth; ihdr[9] = (byte)colourType; ihdr[12] = (byte)interlace;
            HdPng.WriteChunk(png, "IHDR", ihdr);
            if (plte is not null) HdPng.WriteChunk(png, "PLTE", plte);
            HdPng.WriteChunk(png, "IDAT", data.ToArray());
            HdPng.WriteChunk(png, "IEND", []);
            return HdImagePng.Decode(png.ToArray(), 100);
        }
        var rgb = Decode(2, 8, 0, [1, 2, 3, 4, 5, 6]);
        Assert.AreEqual(HdPngStatus.Decoded, rgb.Item1);
        CollectionAssert.AreEqual(new float[] { 1, 2, 3, 4, 5, 6 }, rgb.Item2!.Rgb);
        var grey16 = Decode(0, 16, 0, [0xFF, 0xFF, 0x00, 0x00]);
        CollectionAssert.AreEqual(new float[] { 255, 255, 255, 0, 0, 0 }, grey16.Item2!.Rgb);
        Assert.AreEqual(HdPngStatus.Unsupported, Decode(3, 8, 0, [0, 1], [0, 0, 0, 9, 9, 9]).Item1);
        Assert.AreEqual(HdPngStatus.Unsupported, Decode(2, 8, 1, [1, 2, 3, 4, 5, 6]).Item1);
        Assert.AreEqual(HdPngStatus.TooLarge, HdImagePng.Decode(rgbaPng, 1).Item1);
        Assert.AreEqual(HdPngStatus.Malformed, HdImagePng.Decode(rgbaPng[..20], 100).Item1);
    }

    // ---- plan ----

    [TestMethod]
    public void The_plan_parser_takes_the_documented_shape_and_refuses_everything_else()
    {
        const string good = """{"schema":1,"kind":"arcanum-steamwoven-hd-interface-plan","packId":"ui-4x","scale":4,"items":[{"image":"a.png","mode":"sheet","pad":6,"coarse":3,"columns":2,"tiles":[{"art":"NextBut.ART","frame":0}],"derived":[{"art":"NextBut.ART","frame":1,"from":0}]},{"mode":"ready","images":["x.png","y.png"],"lock":false,"tiles":[{"art":"A.ART","frame":0},{"art":"B.ART","frame":0}]}]}""";
        var plan = HdInterfacePlan.Parse(Encoding.UTF8.GetBytes(good));
        Assert.AreEqual(("ui-4x", 4, 2), (plan.PackId, plan.Scale, plan.Items.Count));
        Assert.AreEqual((6, 3.0, 2), (plan.Items[0].Pad, plan.Items[0].Coarse, plan.Items[0].Columns));
        Assert.AreEqual(new HdInterfaceFrameRef("NextBut.ART", 1), plan.Items[0].Derived.Single().Frame);
        Assert.IsTrue(plan.Items[1].Ready && !plan.Items[1].Lock);
        foreach (var bad in new[]
        {
            good.Replace("\"schema\":1", "\"schema\":2"), good.Replace("ui-4x", "Ui 4x"), good.Replace("\"scale\":4", "\"scale\":1"),
            good.Replace("\"mode\":\"sheet\"", "\"mode\":\"fancy\""), good.Replace("\"from\":0", "\"from\":3"), good.Replace("\"images\":[\"x.png\",\"y.png\"]", "\"images\":[\"x.png\"]"),
            "{}", "[", """{"schema":1,"kind":"arcanum-steamwoven-hd-interface-plan","packId":"p","items":[]}""",
        })
            Assert.ThrowsExactly<FormatException>(() => HdInterfacePlan.Parse(Encoding.UTF8.GetBytes(bad)));
    }

    // ---- build and pack writer ----

    [TestMethod]
    public void A_failed_rewrite_leaves_the_live_pack_intact_and_a_good_one_removes_only_what_the_old_manifest_alone_listed()
    {
        var panel = Pattern(4, 3, seed: 5);
        var one = Art("Int Bar.ART", panel); var two = Art("int_bar.ART", panel);
        HdBuiltInterfaceFrame Frame(HdInterfaceArt art, byte tint) => new(art, 0, 4, 3, HdRedrawLock.LegacyAlpha(Enumerable.Repeat((float)tint, 16 * 12 * 3).ToArray(), panel.Rgba, 4, 3, 4), new(1, 1, 0, 0, 1));
        var directory = Path.Combine(_root, "rewrite");
        Assert.AreEqual(HdInterfaceWriteStatus.Written, HdInterfacePackWriter.Write(directory, "p", 4, [Frame(one, 10)], DateTimeOffset.UnixEpoch).Status);
        var live = HdPack.Open(directory).Pack!;
        var oldFile = live.Entries.Single().File;

        // Two arts whose cleaned names collide are refused before anything is written; the live pack still verifies.
        var clash = HdInterfacePackWriter.Write(directory, "p", 4, [Frame(one, 20), Frame(two, 20)], DateTimeOffset.UnixEpoch);
        Assert.AreEqual(HdFrameStatus.Hd, HdPack.Open(directory).Pack!.ResolveSprite(HdInterfaceKey.Query(one.Identity, 0, 4, 3, panel.Rgba), panel.Rgba).Status);
        Assert.IsTrue(HdPack.Open(directory).Pack!.VerifyIntegrity().Intact, clash.Status + " " + clash.Detail);
        Assert.AreEqual(0, Directory.GetFiles(directory, "*.tmp").Length);

        // A good rewrite swaps the frame; the old file (named by its content) is gone, the new one is listed.
        Assert.AreEqual(HdInterfaceWriteStatus.Written, HdInterfacePackWriter.Write(directory, "p", 4, [Frame(one, 90)], DateTimeOffset.UnixEpoch).Status);
        var next = HdPack.Open(directory).Pack!;
        Assert.AreNotEqual(oldFile, next.Entries.Single().File);
        Assert.IsFalse(File.Exists(Path.Combine(directory, oldFile.Replace('/', Path.DirectorySeparatorChar))));
        Assert.IsTrue(next.VerifyIntegrity().Intact);

        // The same frame again keeps its (content-named) file: nothing the new manifest lists is deleted.
        Assert.AreEqual(HdInterfaceWriteStatus.Written, HdInterfacePackWriter.Write(directory, "p", 4, [Frame(one, 90)], DateTimeOffset.UnixEpoch).Status);
        Assert.IsTrue(HdPack.Open(directory).Pack!.VerifyIntegrity().Intact);
    }

    [TestMethod]
    public void A_redraw_png_with_a_corrupt_checksum_is_refused_and_a_ready_item_takes_no_derived_frames_or_bad_pad()
    {
        var png = HdPng.EncodeRgba(2, 1, new byte[] { 1, 2, 3, 255, 4, 5, 6, 255 });
        Assert.AreEqual(HdPngStatus.Decoded, HdImagePng.Decode(png, 100).Status);
        var broken = (byte[])png.Clone();
        broken[png.Length - 12 - 4 - 1] ^= 0xFF; // inside the IDAT data's Adler-32: the chunk CRC fails too, either way a refusal
        Assert.AreEqual(HdPngStatus.Malformed, HdImagePng.Decode(broken, 100).Status);

        var ready = "{\"schema\":1,\"kind\":\"arcanum-steamwoven-hd-interface-plan\",\"packId\":\"p\",\"items\":[{\"mode\":\"ready\",\"images\":[\"a.png\"],\"tiles\":[{\"art\":\"A.ART\",\"frame\":0}],\"derived\":[{\"art\":\"A.ART\",\"frame\":1,\"from\":0}]}]}";
        Assert.ThrowsExactly<FormatException>(() => HdInterfacePlan.Parse(Encoding.UTF8.GetBytes(ready)));
        var pad = "{\"schema\":1,\"kind\":\"arcanum-steamwoven-hd-interface-plan\",\"packId\":\"p\",\"items\":[{\"image\":\"a.png\",\"pad\":-1,\"tiles\":[{\"art\":\"A.ART\",\"frame\":0}]}]}";
        Assert.ThrowsExactly<FormatException>(() => HdInterfacePlan.Parse(Encoding.UTF8.GetBytes(pad)));
    }


    private static HdInterfacePlan ReadyPlan(string packId, params (string Art, string Image)[] tiles)
        => new(packId, 4, [new("", false, 8, [.. tiles.Select(t => new HdInterfaceFrameRef(t.Art, 0))], 0, [], 0, [.. tiles.Select(t => t.Image)], Lock: false)]);

    [TestMethod]
    public void A_ready_image_gets_the_legacy_alpha_and_a_wrong_size_is_a_reported_problem_not_a_write()
    {
        var art = Art("A.ART", Pattern(6, 5));
        var good = new HdRgbImage(24, 20, Enumerable.Repeat(90f, 24 * 20 * 3).ToArray());
        var result = HdInterfaceImport.Build(ReadyPlan("p", ("A.ART", "good")), n => string.Equals(n, "A.ART", StringComparison.OrdinalIgnoreCase) ? art : null, _ => good);
        Assert.IsTrue(result.Ok, string.Join(";", result.Problems));
        var frame = result.Frames.Single();
        Assert.AreEqual((24, 20 * 24 * 4), (frame.Width * 4, frame.Rgba.Length - 0 + 0 == 0 ? 0 : 20 * 24 * 4));
        for (var y = 0; y < 20; y++)
        for (var x = 0; x < 24; x++) Assert.AreEqual(art.Frames[0].Rgba[((y / 4) * 6 + x / 4) * 4 + 3], frame.Rgba[(y * 24 + x) * 4 + 3]);

        var wrong = HdInterfaceImport.Build(ReadyPlan("p", ("A.ART", "w")), _ => art, _ => new HdRgbImage(10, 10, new float[300]));
        Assert.IsFalse(wrong.Ok); StringAssert.Contains(wrong.Problems.Single(), "needs 24x20");
        var missing = HdInterfaceImport.Build(ReadyPlan("p", ("Z.ART", "w")), _ => null, _ => good);
        StringAssert.Contains(missing.Problems.Single(), "not in the game folder");
        var twice = HdInterfaceImport.Build(ReadyPlan("p", ("A.ART", "a"), ("A.ART", "b")), _ => art, _ => good);
        StringAssert.Contains(twice.Problems.Single(), "made twice");
    }

    [TestMethod]
    public void The_sheet_import_recovers_a_redraw_and_the_written_pack_binds_the_frame_by_its_interface_key()
    {
        var legacy = Smooth(40, 28, seed: 7);
        var art = Art("Panel.ART", legacy);
        var sheet = HdRedrawSheet.Build([legacy], 8);
        var up = HdRaster.UpBicubic(ToSheetRgb(sheet), sheet.Width, sheet.Height, 3, 5);
        var redraw = new HdRgbImage(sheet.Width * 5, sheet.Height * 5, up);
        var plan = new HdInterfacePlan("synthetic-ui", 4, [new("r.png", false, 8, [new("Panel.ART", 0)], 0, [])]);
        var built = HdInterfaceImport.Build(plan, _ => art, _ => redraw);
        Assert.IsTrue(built.Ok, string.Join(";", built.Problems));

        var directory = Path.Combine(_root, "ui-pack");
        var written = HdInterfacePackWriter.Write(directory, "synthetic-ui", 4, built.Frames, new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero));
        Assert.AreEqual(HdInterfaceWriteStatus.Written, written.Status, written.Detail);
        var pack = HdPack.Open(directory).Pack!;
        Assert.AreEqual((4, 1), (pack.Scale, pack.SpriteCount));
        Assert.AreEqual(HdAlphaMode.Legacy, pack.Recipe.Slices.Single().Alpha!.Mode);
        var resolved = pack.ResolveSprite(HdInterfaceKey.Query(art.Identity, 0, 40, 28, legacy.Rgba), legacy.Rgba);
        Assert.AreEqual(HdFrameStatus.Hd, resolved.Status, resolved.Invalid.ToString());
        Assert.AreEqual(160 * 112 * 4, resolved.Rgba!.Length);
        // The ART of another install (different payload hash) is a source mismatch by its name hash.
        var other = HdInterfaceKey.Identity("Panel.ART", new byte[200]);
        Assert.AreEqual(HdFrameStatus.SourceMismatch, pack.ResolveSprite(HdInterfaceKey.Query(other, 0, 40, 28, legacy.Rgba), legacy.Rgba).Status);
    }

    [TestMethod]
    public void An_exact_redraw_covers_its_frame_and_keeps_the_legacy_alpha_and_shading()
    {
        var legacy = Smooth(30, 20, seed: 9);
        var art = Art("Strip.ART", legacy);
        var redraw = new HdRgbImage(30 * 6, 20 * 6, HdRaster.UpBicubic(Rgb(legacy), 30, 20, 3, 6));
        var plan = new HdInterfacePlan("p", 4, [new("s.png", true, 8, [new("Strip.ART", 0)], 0, [])]);
        var built = HdInterfaceImport.Build(plan, _ => art, _ => redraw);
        Assert.IsTrue(built.Ok, string.Join(";", built.Problems));
        var frame = built.Frames.Single();
        double error = 0;
        var reference = HdRaster.UpBicubic(Rgb(legacy), 30, 20, 3, 4);
        for (var i = 0; i < 120 * 80; i++) for (var c = 0; c < 3; c++) error += Math.Abs(frame.Rgba[i * 4 + c] - reference[i * 3 + c]);
        Assert.IsTrue(error / (120 * 80 * 3) < 3, $"error {error / (120 * 80 * 3)}");
        Assert.IsTrue(Enumerable.Range(0, 120 * 80).All(i => frame.Rgba[i * 4 + 3] == 255));

        var box = HdRedrawFit.ContentBox(new HdRgbImage(10, 10, Enumerable.Range(0, 300).Select(i => i / 3 % 10 is >= 2 and < 8 && i / 30 is >= 3 and < 9 ? 50f : 255f).ToArray()));
        Assert.AreEqual((2, 3, 8, 9), box);
    }

    [TestMethod]
    public void An_existing_pack_gets_the_interface_slice_added_then_replaced_and_keeps_its_own_entries_and_marks()
    {
        var pack = new SyntheticHdPack(Path.Combine(_root, "sprites"), 4);
        var body = SyntheticHdPack.Legacy("body", 5, 4, seed: 2);
        pack.Add(body, SyntheticHdPack.Hd(body, 4, tint: 20));
        pack.Manifest["review"] = "approved";
        pack.Write();
        var before = HdPack.Open(pack.Directory).Pack!;

        var panel = Pattern(6, 5, seed: 3);
        var art = Art("Panel.ART", panel);
        HdBuiltInterfaceFrame Frame(byte tint) => new(art, 0, 6, 5, HdRedrawLock.LegacyAlpha(Enumerable.Repeat((float)tint, 24 * 20 * 3).ToArray(), panel.Rgba, 6, 5, 4), new(1, 1, 0, 0, 1));
        var first = HdInterfacePackWriter.Write(pack.Directory, "ignored-for-existing", 4, [Frame(50)], DateTimeOffset.UnixEpoch);
        Assert.AreEqual((HdInterfaceWriteStatus.Written, 1, 0), (first.Status, first.Entries, first.Replaced), first.Detail);
        var second = HdInterfacePackWriter.Write(pack.Directory, "ignored-for-existing", 4, [Frame(70)], DateTimeOffset.UnixEpoch);
        Assert.AreEqual((HdInterfaceWriteStatus.Written, 1, 1), (second.Status, second.Entries, second.Replaced), second.Detail);

        var after = HdPack.Open(pack.Directory).Pack!;
        Assert.AreEqual(2, after.SpriteCount);
        Assert.AreEqual(HdReviewStatus.Approved, after.Review);
        Assert.AreEqual("synthetic-pack", after.Id);
        Assert.AreNotEqual(before.Identity.ManifestSha256, after.Identity.ManifestSha256);
        Assert.AreEqual(HdFrameStatus.Hd, after.ResolveSprite(body.Query, body.Rgba).Status, "the old sprite still binds");
        Assert.AreEqual(HdFrameStatus.Hd, after.ResolveSprite(HdInterfaceKey.Query(art.Identity, 0, 6, 5, panel.Rgba), panel.Rgba).Status);
        Assert.AreEqual(70, after.ResolveSprite(HdInterfaceKey.Query(art.Identity, 0, 6, 5, panel.Rgba), panel.Rgba).Rgba![0], "the replaced frame is the second one");
    }

    [TestMethod]
    public void The_writer_refuses_a_scale_mismatch_and_a_damaged_pack_and_writes_nothing()
    {
        var pack = new SyntheticHdPack(Path.Combine(_root, "two"), 2);
        var body = SyntheticHdPack.Legacy("body", 4, 4, seed: 2);
        pack.Add(body);
        pack.Write();
        var panel = Pattern(4, 4);
        var frame = new HdBuiltInterfaceFrame(Art("P.ART", panel), 0, 4, 4, new byte[16 * 16 * 4], new(1, 1, 0, 0, 1));
        var manifestBefore = File.ReadAllBytes(Path.Combine(pack.Directory, "manifest.json"));
        Assert.AreEqual(HdInterfaceWriteStatus.RecipeConflict, HdInterfacePackWriter.Write(pack.Directory, "x", 4, [frame], DateTimeOffset.UnixEpoch).Status);
        CollectionAssert.AreEqual(manifestBefore, File.ReadAllBytes(Path.Combine(pack.Directory, "manifest.json")));

        // An unaccepted scale writes nothing at all (no half-made pack to delete by hand).
        var none = Path.Combine(_root, "none");
        Assert.AreEqual(HdInterfaceWriteStatus.RecipeConflict, HdInterfacePackWriter.Write(none, "x", 3, [frame], DateTimeOffset.UnixEpoch).Status);
        Assert.IsFalse(Directory.Exists(none));

        File.WriteAllText(Path.Combine(pack.Directory, "manifest.json"), "{ not json");
        Assert.AreEqual(HdInterfaceWriteStatus.InvalidPack, HdInterfacePackWriter.Write(pack.Directory, "x", 2, [frame], DateTimeOffset.UnixEpoch).Status);
    }
}
