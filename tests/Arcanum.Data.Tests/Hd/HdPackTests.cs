using System.Text;
using System.Text.Json.Nodes;
using Arcanum.Data.Hd;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Hd;

[TestClass]
public sealed class HdPackTests
{
    private string _root = null!;

    [TestInitialize]
    public void Create() => Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "arc-hd-" + Guid.NewGuid().ToString("N")));

    [TestCleanup]
    public void Remove()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private SyntheticHdPack NewPack(string name = "pack", int scale = 2, string? recipe = null, string id = "synthetic-pack")
        => new(Path.Combine(_root, name), scale, recipe, id);

    private static HdPack Opened(string directory, HdPackOptions? options = null)
    {
        var result = HdPack.Open(directory, options);
        Assert.AreEqual(HdPackOpenStatus.Opened, result.Status, result.Detail);
        return result.Pack!;
    }

    [TestMethod]
    public void A_valid_frame_binds_to_exactly_its_hd_pixels_and_the_nearest_replica_is_the_identity()
    {
        var pack = NewPack();
        var critter = SyntheticHdPack.Legacy("critter", 7, 5, seed: 1, group: 3, frame: 2, palette: 1);
        var tree = SyntheticHdPack.Legacy("tree", 4, 9, seed: 2);
        var hd = SyntheticHdPack.Hd(critter, 2, tint: 40);
        pack.Add(critter, hd);
        pack.Add(tree, SyntheticHdPack.Hd(tree, 2));
        var opened = Opened(pack.Write());

        Assert.AreEqual(("synthetic-pack", 2, HdReviewStatus.Unreviewed, 2, 0), (opened.Id, opened.Scale, opened.Review, opened.SpriteCount, opened.TerrainChunkCount));
        Assert.AreEqual(SyntheticHdPack.Sha(File.ReadAllBytes(Path.Combine(pack.Directory, "manifest.json"))), opened.Identity.ManifestSha256);
        Assert.AreEqual(4L * (7 * 5 + 4 * 9) * 4, opened.SpriteRgbaBytes);
        Assert.AreEqual(("synthetic-test", "none", new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero)), (opened.Build.Builder, opened.Build.Gpu, opened.Build.CreatedUtc));

        var result = opened.ResolveSprite(critter.Query, critter.Rgba);
        Assert.AreEqual((HdFrameStatus.Hd, HdInvalidReason.None, 2, 14, 10), (result.Status, result.Invalid, result.Scale, result.Width, result.Height));
        CollectionAssert.AreEqual(hd, result.Rgba);

        // Nearest-replicated HD: every HD texel equals its parent Legacy texel (contract consequence 4).
        var identity = opened.ResolveSprite(tree.Query, tree.Rgba);
        Assert.AreEqual(HdFrameStatus.Hd, identity.Status);
        for (var y = 0; y < 18; y++)
        for (var x = 0; x < 8; x++)
            CollectionAssert.AreEqual(tree.Rgba.AsSpan(((y / 2) * 4 + x / 2) * 4, 4).ToArray(), identity.Rgba!.AsSpan((y * 8 + x) * 4, 4).ToArray());
    }

    [TestMethod]
    public void Missing_changed_and_rejected_frames_each_fall_back_with_their_own_reason()
    {
        var pack = NewPack();
        var kept = SyntheticHdPack.Legacy("kept", 5, 4, seed: 3);
        var rejected = SyntheticHdPack.Legacy("rejected", 5, 4, seed: 4);
        pack.Add(kept);
        pack.Add(rejected)["review"] = "rejected";
        var opened = Opened(pack.Write());

        Assert.AreEqual(HdFrameStatus.Hd, opened.ResolveSprite(kept.Query, kept.Rgba).Status);
        Assert.AreEqual(HdFrameStatus.FrameRejected, opened.ResolveSprite(rejected.Query, rejected.Rgba).Status);
        var absent = SyntheticHdPack.Legacy("absent", 5, 4, seed: 3);
        Assert.AreEqual(HdFrameStatus.FrameMissing, opened.ResolveSprite(absent.Query, absent.Rgba).Status);
        // Another frame of the same ART that the pack does not have: missing, not changed.
        var otherFrame = kept with { FrameIndex = 1 };
        Assert.AreEqual(HdFrameStatus.FrameMissing, opened.ResolveSprite(otherFrame.Query with { Key = otherFrame.Key }, otherFrame.Rgba).Status);

        // A mod or another version replaced the ART: same name, different payload hash.
        var modded = kept.Query with { Key = kept.Key with { ArtSha256 = SyntheticHdPack.Sha("art:modded") } };
        Assert.AreEqual(HdFrameStatus.SourceMismatch, opened.ResolveSprite(modded, kept.Rgba).Status);
        // Same key but this installation decodes different Legacy pixels or size.
        var repainted = SyntheticHdPack.Legacy("kept", 5, 4, seed: 9);
        Assert.AreEqual(HdFrameStatus.SourceMismatch, opened.ResolveSprite(repainted.Query, repainted.Rgba).Status);
        var resized = SyntheticHdPack.Legacy("kept", 6, 4, seed: 3);
        Assert.AreEqual(HdFrameStatus.SourceMismatch, opened.ResolveSprite(resized.Query, resized.Rgba).Status);

        // A query whose pixels contradict its own hash is a caller error.
        Assert.ThrowsExactly<ArgumentException>(() => opened.ResolveSprite(kept.Query, repainted.Rgba));
        Assert.ThrowsExactly<ArgumentException>(() => opened.ResolveSprite(kept.Query, kept.Rgba.AsSpan(4)));

        pack.Manifest["review"] = "rejected";
        var rejectedPack = Opened(pack.Write());
        Assert.AreEqual(HdFrameStatus.PackRejected, rejectedPack.ResolveSprite(kept.Query, kept.Rgba).Status);
        Assert.AreNotEqual(opened.Identity, rejectedPack.Identity, "a review mark is a new pack identity");
    }

    [TestMethod]
    public void Damaged_or_wrong_hd_files_are_invalid_per_frame_and_never_fail_the_pack()
    {
        var pack = NewPack();
        var cases = new List<(string Name, HdInvalidReason Reason, SyntheticHdPack.Frame Frame)>();
        void Case(string name, HdInvalidReason reason, Func<SyntheticHdPack.Frame, JsonObject> add)
        {
            var frame = SyntheticHdPack.Legacy(name, 6, 5, seed: name.Length);
            add(frame);
            cases.Add((name, reason, frame));
        }
        Case("missing", HdInvalidReason.FileMissing, f => { var e = pack.Add(f); File.Delete(Path.Combine(pack.Directory, "frames", $"{pack.Entries.Count - 1:D4}.png")); return e; });
        Case("short", HdInvalidReason.FileSize, f => { var e = pack.Add(f); e["bytes"] = (int)e["bytes"]! + 1; return e; });
        Case("tampered", HdInvalidReason.HashMismatch, f =>
        {
            var e = pack.Add(f);
            var path = Path.Combine(pack.Directory, "frames", $"{pack.Entries.Count - 1:D4}.png");
            var bytes = File.ReadAllBytes(path); bytes[^20] ^= 0x55; File.WriteAllBytes(path, bytes);
            return e;
        });
        Case("triple", HdInvalidReason.SizeMismatch, f => pack.Add(f, SyntheticHdPack.Hd(f, 3), hdWidth: 18, hdHeight: 15));
        Case("notpng", HdInvalidReason.PngMalformed, f =>
        {
            var e = pack.Add(f);
            var junk = Encoding.ASCII.GetBytes("not a png at all");
            pack.WriteFile((string)e["file"]!, junk);
            e["sha256"] = SyntheticHdPack.Sha(junk); e["bytes"] = junk.Length;
            return e;
        });
        Case("hole", HdInvalidReason.AlphaMismatch, f =>
        {
            var hd = SyntheticHdPack.Hd(f, 2);
            var opaque = Array.FindIndex(Enumerable.Range(0, hd.Length / 4).ToArray(), i => hd[i * 4 + 3] == 255);
            hd[opaque * 4 + 3] = 0; // one invented hole
            return pack.Add(f, hd);
        });
        Case("soft", HdInvalidReason.AlphaMismatch, f =>
        {
            var hd = SyntheticHdPack.Hd(f, 2);
            for (var i = 3; i < hd.Length; i += 4) if (hd[i] == 255) { hd[i] = 200; break; } // soft alpha is not the Legacy mask
            return pack.Add(f, hd);
        });
        var good = SyntheticHdPack.Legacy("good", 6, 5, seed: 1);
        pack.Add(good);
        var opened = Opened(pack.Write());

        foreach (var (name, reason, frame) in cases)
        {
            var result = opened.ResolveSprite(frame.Query, frame.Rgba);
            Assert.AreEqual((HdFrameStatus.Invalid, reason), (result.Status, result.Invalid), name);
            Assert.IsNull(result.Rgba, name);
        }
        Assert.AreEqual(HdFrameStatus.Hd, opened.ResolveSprite(good.Query, good.Rgba).Status, "one bad frame changes no other frame");

        var report = opened.VerifyIntegrity();
        Assert.AreEqual((8, 3, false), (report.Entries, report.Valid, report.Intact), "the alpha cases are intact files; masks are checked on binding");
        CollectionAssert.AreEquivalent(new[] { HdInvalidReason.FileMissing, HdInvalidReason.FileSize, HdInvalidReason.HashMismatch, HdInvalidReason.SizeMismatch, HdInvalidReason.PngMalformed },
            report.ProblemCounts.Keys.ToArray());
        Assert.AreEqual(5, report.Problems.Count);
        Assert.AreEqual(2, Opened(pack.Directory, new HdPackOptions { MaxReportedProblems = 2 }).VerifyIntegrity().Problems.Count);
        Assert.AreEqual(HdInvalidReason.TooLarge, Opened(pack.Directory, new HdPackOptions { MaxHdFramePixels = 100 }).ResolveSprite(good.Query, good.Rgba).Invalid);
        Assert.AreEqual(HdInvalidReason.FileSize, Opened(pack.Directory, new HdPackOptions { MaxFrameFileBytes = 10 }).ResolveSprite(good.Query, good.Rgba).Invalid);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.IsTrue(opened.VerifyIntegrity(cancelled.Token).Cancelled);
    }

    [TestMethod]
    public void Soft_alpha_packs_pass_the_mask_check_only_above_the_recipe_threshold()
    {
        var recipe = SyntheticHdPack.Recipe.Replace("\"alpha\":{\"mode\":\"legacy\"}", "\"alpha\":{\"mode\":\"soft\",\"minIoUPermille\":950}");
        var pack = NewPack(recipe: recipe);
        var frame = SyntheticHdPack.Legacy("soft", 20, 20, seed: 5);
        var hd = SyntheticHdPack.Hd(frame, 2);
        for (var i = 3; i < hd.Length; i += 4) if (hd[i] == 255) hd[i] = 180; // soft but same mask
        var flipped = 0;
        for (var i = 3; i < hd.Length && flipped < 10; i += 4) if (hd[i] == 0) { hd[i] = 200; flipped++; } // 10 of ~1,280 grow
        pack.Add(frame, hd);
        var shrunk = SyntheticHdPack.Legacy("shrunk", 20, 20, seed: 6);
        var thin = SyntheticHdPack.Hd(shrunk, 2);
        var removed = 0;
        for (var i = 3; i < thin.Length && removed < 200; i += 4) if (thin[i] == 255) { thin[i] = 0; removed++; } // ~16 % lost
        pack.Add(shrunk, thin);
        var opened = Opened(pack.Write());
        Assert.AreEqual(HdFrameStatus.Hd, opened.ResolveSprite(frame.Query, frame.Rgba).Status);
        Assert.AreEqual(HdInvalidReason.AlphaMismatch, opened.ResolveSprite(shrunk.Query, shrunk.Rgba).Invalid);
    }

    [TestMethod]
    public void A_4x_soft_edge_pack_binds_and_its_soft_edge_may_reach_one_legacy_texel_beyond_the_silhouette()
    {
        var recipe = SyntheticHdPack.Recipe.Replace("\"scale\":2", "\"scale\":4").Replace("\"nativeScale\":2", "\"nativeScale\":4")
            .Replace("\"alpha\":{\"mode\":\"legacy\"}", "\"alpha\":{\"mode\":\"soft\",\"minIoUPermille\":950}");
        // A 12×8 Legacy body: opaque left half, transparent right half.
        var rgba = new byte[12 * 8 * 4];
        for (var y = 0; y < 8; y++)
        for (var x = 0; x < 6; x++) { var at = (y * 12 + x) * 4; rgba[at] = (byte)(x * 40); rgba[at + 1] = (byte)(y * 30); rgba[at + 3] = 255; }
        var body = new SyntheticHdPack.Frame("body", 12, 8, 0, 0, 0, rgba);
        byte[] Soft(int reach)
        {
            var hd = SyntheticHdPack.Hd(body, 4);
            // Feather the edge: HD columns 24 .. 24+reach-1 (right of the silhouette) get partial alpha.
            for (var y = 0; y < 32; y++)
            for (var x = 24; x < 24 + reach; x++) { var at = (y * 48 + x) * 4; hd[at + 3] = (byte)(120 - (x - 24) * 10); hd[at] = 200; }
            for (var y = 0; y < 32; y++) hd[(y * 48 + 23) * 4 + 3] = 140; // the last opaque column softened
            return hd;
        }
        var near = new SyntheticHdPack(Path.Combine(_root, "near"), 4, recipe);
        near.Add(body, Soft(4));
        var nearPack = Opened(near.Write());
        var result = nearPack.ResolveSprite(body.Query, body.Rgba);
        Assert.AreEqual((HdFrameStatus.Hd, 4, 48, 32), (result.Status, result.Scale, result.Width, result.Height));

        var far = new SyntheticHdPack(Path.Combine(_root, "far"), 4, recipe);
        far.Add(body, Soft(5)); // column 28 is two Legacy texels from the silhouette
        Assert.AreEqual(HdInvalidReason.AlphaMismatch, Opened(far.Write()).ResolveSprite(body.Query, body.Rgba).Invalid);

        var hard = new SyntheticHdPack(Path.Combine(_root, "hard"), 4, recipe.Replace("\"alpha\":{\"mode\":\"soft\",\"minIoUPermille\":950}", "\"alpha\":{\"mode\":\"legacy\"}"));
        hard.Add(body, Soft(4));
        Assert.AreEqual(HdInvalidReason.AlphaMismatch, Opened(hard.Write()).ResolveSprite(body.Query, body.Rgba).Invalid, "legacy alpha means exactly the Legacy mask");
    }

    [TestMethod]
    public void Paths_must_be_portable_relative_and_inside_the_pack()
    {
        var frame = SyntheticHdPack.Legacy("f", 3, 3, seed: 1);
        foreach (var path in new[]
        {
            "../outside.png", "/abs/x.png", "C:/x.png", "frames\\x.png", "frames//x.png", "Frames/x.png", "frames/./x.png", "frames/x.PNG",
            "frames/x.png.", ".hidden/x.png", "frames/con.png", "frames/lpt1.x.png", "frames/x.jpg", "frames/a b.png", "frames/é.png",
            string.Join("/", Enumerable.Repeat("d", 8)) + "/x.png", "frames/" + new string('a', 199) + ".png",
        })
        {
            var pack = NewPack("p" + Guid.NewGuid().ToString("N"));
            pack.Add(frame, file: "frames/0.png")["file"] = path;
            Assert.AreEqual(HdPackOpenStatus.InvalidPath, HdPack.Open(pack.Write()).Status, path);
        }
        var nested = NewPack("nested");
        nested.Add(frame, file: "frames/critter/hm-fu.0_1.png");
        Assert.AreEqual(HdPackOpenStatus.Opened, HdPack.Open(nested.Write()).Status);

        var recipe = NewPack("recipe-path");
        recipe.Manifest["recipe"]!["file"] = "../recipe.json";
        Assert.AreEqual(HdPackOpenStatus.InvalidPath, HdPack.Open(recipe.Write()).Status);
        var manifestAsRecipe = NewPack("manifest-recipe");
        manifestAsRecipe.Manifest["recipe"]!["file"] = "manifest.json";
        Assert.AreEqual(HdPackOpenStatus.InvalidPath, HdPack.Open(manifestAsRecipe.Write()).Status);
    }

    [TestMethod]
    public void A_start_up_picture_is_an_opaque_16_9_file_named_after_the_original_picture()
    {
        var pack = Opened(NewPack("start-up").Write());
        Assert.AreEqual("splash/splash1-16x9.png", HdPack.StartupPictureFile("splash1"));
        Assert.IsNull(HdPack.StartupPictureFile("../splash1"));
        Assert.IsNull(HdPack.StartupPictureFile(""));
        Assert.AreEqual("NoFile", pack.ReadStartupPictureReport("splash1").Status);
        Assert.AreEqual("BadName", pack.ReadStartupPictureReport("a.b").Status);
        Directory.CreateDirectory(Path.Combine(pack.Directory, "splash"));
        var file = Path.Combine(pack.Directory, "splash", "splash1-16x9.png");
        var pixels = new byte[16 * 9 * 4];
        for (var alpha = 3; alpha < pixels.Length; alpha += 4) pixels[alpha] = 255;
        File.WriteAllBytes(file, HdPng.EncodeRgba(16, 9, pixels));
        Assert.AreEqual(("Used", 16, 9), (pack.ReadStartupPictureReport("splash1") is var read ? (read.Status, read.Width, read.Height) : default));
        File.WriteAllBytes(file, HdPng.EncodeRgba(16, 10, new byte[16 * 10 * 4]));
        Assert.AreEqual("WrongSize", pack.ReadStartupPictureReport("splash1").Status);
        File.WriteAllBytes(file, HdPng.EncodeRgba(16, 9, new byte[16 * 9 * 4]));
        Assert.AreEqual("NotOpaque", pack.ReadStartupPictureReport("splash1").Status);
    }

    [TestMethod]
    public void A_symbolic_link_inside_the_pack_is_refused()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Creating symbolic links needs privileges on Windows.");
        var outside = Path.Combine(_root, "outside");
        var pack = NewPack();
        var frame = SyntheticHdPack.Legacy("linked", 3, 3, seed: 1);
        pack.Add(frame, file: "frames/0000.png");
        Directory.CreateDirectory(outside);
        File.Move(Path.Combine(pack.Directory, "frames", "0000.png"), Path.Combine(outside, "0000.png"));
        File.CreateSymbolicLink(Path.Combine(pack.Directory, "frames", "0000.png"), Path.Combine(outside, "0000.png"));
        var opened = Opened(pack.Write());
        Assert.AreEqual(HdInvalidReason.FileUnreadable, opened.ResolveSprite(frame.Query, frame.Rgba).Invalid);

        var linkedDir = NewPack("linked-dir");
        linkedDir.Add(frame, file: "frames/0000.png");
        Directory.Move(Path.Combine(linkedDir.Directory, "frames"), Path.Combine(outside, "frames"));
        Directory.CreateSymbolicLink(Path.Combine(linkedDir.Directory, "frames"), Path.Combine(outside, "frames"));
        Assert.AreEqual(HdInvalidReason.FileUnreadable, Opened(linkedDir.Write()).ResolveSprite(frame.Query, frame.Rgba).Invalid);

        var dangling = NewPack("dangling");
        dangling.Add(frame, file: "frames/0000.png");
        File.Delete(Path.Combine(dangling.Directory, "frames", "0000.png"));
        File.CreateSymbolicLink(Path.Combine(dangling.Directory, "frames", "0000.png"), Path.Combine(outside, "absent.png"));
        Assert.AreEqual(HdInvalidReason.FileUnreadable, Opened(dangling.Write()).ResolveSprite(frame.Query, frame.Rgba).Invalid, "a dangling link is a link");

        var linkedManifest = NewPack("linked-manifest");
        linkedManifest.Add(frame);
        linkedManifest.Write();
        File.Move(Path.Combine(linkedManifest.Directory, "manifest.json"), Path.Combine(outside, "manifest.json"));
        File.CreateSymbolicLink(Path.Combine(linkedManifest.Directory, "manifest.json"), Path.Combine(outside, "manifest.json"));
        Assert.AreEqual(HdPackOpenStatus.Unreadable, HdPack.Open(linkedManifest.Directory).Status);
    }

    [TestMethod]
    public void Malformed_manifests_are_refused_with_typed_reasons()
    {
        var frame = SyntheticHdPack.Legacy("f", 3, 3, seed: 1);
        HdPackOpenStatus Status(Action<SyntheticHdPack> change, bool pinRecipe = true, HdPackOptions? options = null)
        {
            var pack = NewPack("m" + Guid.NewGuid().ToString("N"));
            pack.Add(frame);
            change(pack);
            return HdPack.Open(pack.Write(pinRecipe), options).Status;
        }
        Assert.AreEqual(HdPackOpenStatus.Opened, Status(_ => { }));
        Assert.AreEqual(HdPackOpenStatus.NotFound, HdPack.Open(Path.Combine(_root, "nothing")).Status);
        Assert.AreEqual(HdPackOpenStatus.UnsupportedSchema, Status(p => p.Manifest["schema"] = 2));
        Assert.AreEqual(HdPackOpenStatus.UnsupportedSchema, Status(p => p.Manifest["kind"] = "arcanum-steamwoven-hd-recipe"));
        Assert.AreEqual(HdPackOpenStatus.InvalidField, Status(p => p.Manifest["scale"] = 3), "only 2× and 4× are accepted by default");
        Assert.AreEqual(HdPackOpenStatus.InvalidField, Status(p => p.Manifest["id"] = "Bad.Id"));
        Assert.AreEqual(HdPackOpenStatus.InvalidField, Status(p => p.Manifest["review"] = "maybe"));
        Assert.AreEqual(HdPackOpenStatus.InvalidField, Status(p => p.Manifest["extra"] = 1));
        Assert.AreEqual(HdPackOpenStatus.InvalidField, Status(p => p.Manifest["build"]!["gpu"] = "/Users/someone/gpu"), "a path in the build record");
        Assert.AreEqual(HdPackOpenStatus.InvalidField, Status(p => p.Manifest["build"]!["createdUtc"] = "16.10.2026"));
        Assert.AreEqual(HdPackOpenStatus.InvalidField, Status(p => p.Entries[0]!["palette"] = 4));
        Assert.AreEqual(HdPackOpenStatus.InvalidField, Status(p => p.Entries[0]!["group"] = 8));
        Assert.AreEqual(HdPackOpenStatus.InvalidField, Status(p => p.Entries[0]!["art"]!["view"] = 100));
        Assert.AreEqual(HdPackOpenStatus.InvalidField, Status(p => p.Entries[0]!["art"]!["sha256"] = "ABC"));
        Assert.AreEqual(HdPackOpenStatus.InvalidField, Status(p => p.Entries[0]!["legacy"]!["width"] = 0));
        Assert.AreEqual(HdPackOpenStatus.InvalidField, Status(p => p.Entries[0]!["slice"] = "unknown-slice"));
        Assert.AreEqual(HdPackOpenStatus.InvalidField, Status(p => p.Entries[0]!["kind"] = "texture"));
        Assert.AreEqual(HdPackOpenStatus.InvalidField, Status(p => p.Entries[0]!.AsObject().Remove("review")));
        Assert.AreEqual(HdPackOpenStatus.DuplicateFile, Status(p => { p.Add(SyntheticHdPack.Legacy("g", 3, 3, seed: 2)); p.Entries[1]!["file"] = (string)p.Entries[0]!["file"]!; }));
        Assert.AreEqual(HdPackOpenStatus.DuplicateKey, Status(p => p.Add(frame, file: "frames/again.png")));
        Assert.AreEqual(HdPackOpenStatus.TooManyEntries, Status(p => p.Add(SyntheticHdPack.Legacy("g", 3, 3, seed: 2)), options: new() { MaxEntries = 1 }));
        Assert.AreEqual(HdPackOpenStatus.TooLarge, Status(_ => { }, options: new() { MaxManifestBytes = 64 }));
        Assert.AreEqual(HdPackOpenStatus.RecipeMismatch, Status(p => p.Manifest["recipe"]!["sha256"] = SyntheticHdPack.Sha("other recipe"), pinRecipe: false));
        Assert.AreEqual(HdPackOpenStatus.RecipeMissing, Status(p => p.Manifest["recipe"]!["file"] = "missing.json"));
        Assert.AreEqual(HdPackOpenStatus.RecipeInvalid, Status(p => p.RecipeText = p.RecipeText.Replace("\"tta\":false", "\"tta\":true")));
        Assert.AreEqual(HdPackOpenStatus.Opened, Status(p => p.Manifest["entries"] = new JsonArray()), "an empty pack is valid");
        Assert.AreEqual(HdPackOpenStatus.InvalidField, Status(p => p.Entries[0]!["group"] = JsonNode.Parse("-0")), "negative zero");
        foreach (var (name, bytes) in BadText())
        {
            var pack = NewPack("text-" + name);
            pack.Add(frame);
            pack.Write();
            File.WriteAllBytes(Path.Combine(pack.Directory, "manifest.json"), bytes(File.ReadAllBytes(Path.Combine(pack.Directory, "manifest.json"))));
            var opened = HdPack.Open(pack.Directory);
            Assert.AreEqual(HdPackOpenStatus.Malformed, opened.Status, name + ": " + opened.Detail);
        }
        var broken = NewPack("broken");
        broken.Write();
        File.WriteAllText(Path.Combine(broken.Directory, "manifest.json"), "{\"schema\":1,");
        Assert.AreEqual(HdPackOpenStatus.Malformed, HdPack.Open(broken.Directory).Status);
    }

    /// <summary>Manifest text that is not valid UTF-8 or holds an unpaired surrogate.</summary>
    internal static IEnumerable<(string Name, Func<byte[], byte[]> Change)> BadText()
    {
        static byte[] Replace(byte[] bytes, string from, byte[] to)
        {
            var text = Encoding.UTF8.GetBytes(from);
            var at = bytes.AsSpan().IndexOf(text);
            Assert.IsTrue(at >= 0);
            return [.. bytes.AsSpan(0, at), .. to, .. bytes.AsSpan(at + text.Length)];
        }
        yield return ("utf8-value", b => Replace(b, "\"gpu\":\"none\"", [.. "\"gpu\":\"n"u8, 0xFF, .. "ne\""u8]));
        yield return ("utf8-name", b => Replace(b, "\"gpu\":", [.. "\"g"u8, 0xFF, .. "u\":"u8]));
        yield return ("surrogate-value", b => Replace(b, "\"gpu\":\"none\"", "\"gpu\":\"n\\ud800\""u8.ToArray()));
        yield return ("surrogate-kind", b => Replace(b, "\"kind\":\"arcanum-steamwoven-hd-pack\"", "\"kind\":\"\\udfff\""u8.ToArray()));
        yield return ("bom", b => [0xEF, 0xBB, 0xBF, .. b]);
    }

    [TestMethod]
    public void Scale_is_a_pack_parameter_and_other_integer_scales_work_when_accepted()
    {
        var options = new HdPackOptions { Scales = [2, 3] };
        var pack = NewPack(scale: 3);
        var frame = SyntheticHdPack.Legacy("three", 5, 4, seed: 7);
        var hd = SyntheticHdPack.Hd(frame, 3, tint: 9);
        pack.Add(frame, hd);
        pack.Write();
        Assert.AreEqual(HdPackOpenStatus.InvalidField, HdPack.Open(pack.Directory).Status, "only 2× and 4× are accepted by default");
        var opened = Opened(pack.Directory, options);
        var result = opened.ResolveSprite(frame.Query, frame.Rgba);
        Assert.AreEqual((HdFrameStatus.Hd, 3, 15, 12), (result.Status, result.Scale, result.Width, result.Height));
        CollectionAssert.AreEqual(hd, result.Rgba);

        var mismatch = NewPack("mismatch", scale: 2, recipe: SyntheticHdPack.Recipe.Replace("\"scale\":2", "\"scale\":3").Replace("\"nativeScale\":2", "\"nativeScale\":3"));
        mismatch.Add(frame);
        Assert.AreEqual(HdPackOpenStatus.RecipeInvalid, HdPack.Open(mismatch.Write(), options).Status, "recipe and pack scales must agree");
    }

    [TestMethod]
    public void Terrain_chunk_entries_are_parsed_and_integrity_checked_like_sprites()
    {
        var recipe = SyntheticHdPack.Recipe.Replace("\"kinds\":[\"critter\",\"scenery\"]}", "\"kinds\":[\"critter\",\"scenery\"]},{\"name\":\"ground\",\"map\":\"synthetic-map\",\"kinds\":[\"terrainChunk\"]}")
            .Replace("\"alpha\":{\"mode\":\"legacy\"}", "\"alpha\":{\"mode\":\"legacy\"},\"terrain\":{\"chunkSize\":512,\"padding\":32}")
            .Replace("\"tileSize\":512", "\"tileSize\":600");
        JsonObject Chunk(SyntheticHdPack pack, int padding = 32, string slice = "ground", string file = "chunks/4_1.png")
        {
            var png = HdPng.EncodeRgba(1024, 1024, new byte[1024 * 1024 * 4]);
            pack.WriteFile(file, png);
            var entry = new JsonObject
            {
                ["kind"] = "terrainChunk", ["file"] = file, ["sha256"] = SyntheticHdPack.Sha(png), ["bytes"] = png.Length, ["slice"] = slice,
                ["chunk"] = new JsonObject { ["x"] = 4, ["y"] = 1, ["size"] = 512, ["padding"] = padding },
                ["tiles"] = new JsonArray(SyntheticHdPack.Sha("tile:a"), SyntheticHdPack.Sha("tile:b")),
                ["legacy"] = new JsonObject { ["width"] = 512, ["height"] = 512, ["rgbaSha256"] = SyntheticHdPack.Sha("chunk") },
                ["review"] = "unreviewed",
            };
            pack.Entries.Add(entry);
            return entry;
        }
        var pack = NewPack(recipe: recipe);
        var sprite = SyntheticHdPack.Legacy("s", 3, 3, seed: 1);
        pack.Add(sprite);
        Chunk(pack);
        var opened = Opened(pack.Write());
        Assert.AreEqual((1, 1), (opened.SpriteCount, opened.TerrainChunkCount));
        var chunk = opened.Entries.Single(e => e.Kind == HdEntryKind.TerrainChunk);
        Assert.AreEqual((4, 1, 32, 2), (chunk.ChunkX, chunk.ChunkY, chunk.ChunkPadding, chunk.TileRgbaSha256!.Count));
        Assert.IsTrue(opened.VerifyIntegrity().Intact, "chunk files are integrity-checked like sprites");
        Assert.AreEqual(HdFrameStatus.Hd, opened.ResolveSprite(sprite.Query, sprite.Rgba).Status);

        var badPadding = NewPack("bad-padding", recipe: recipe);
        Chunk(badPadding, padding: 16);
        Assert.AreEqual(HdPackOpenStatus.InvalidField, HdPack.Open(badPadding.Write()).Status);
        var wrongSlice = NewPack("wrong-slice", recipe: recipe);
        Chunk(wrongSlice, slice: "bodies");
        Assert.AreEqual(HdPackOpenStatus.InvalidField, HdPack.Open(wrongSlice.Write()).Status);
        var repeated = NewPack("repeated-chunk", recipe: recipe);
        Chunk(repeated);
        Chunk(repeated, file: "chunks/4_1-again.png");
        Assert.AreEqual(HdPackOpenStatus.DuplicateKey, HdPack.Open(repeated.Write()).Status, "one entry per chunk of a slice");
        var spriteInGround = NewPack("sprite-in-ground", recipe: recipe);
        spriteInGround.Add(sprite, slice: "ground");
        Assert.AreEqual(HdPackOpenStatus.InvalidField, HdPack.Open(spriteInGround.Write()).Status);
    }

    private const string ChunkRecipe = "\"kinds\":[\"critter\",\"scenery\"]},{\"name\":\"ground\",\"map\":\"synthetic-map\",\"kinds\":[\"terrainChunk\"]},{\"name\":\"cave-ground\",\"map\":\"synthetic-cave\",\"kinds\":[\"terrainChunk\"]}";

    private static string TerrainRecipe(string alpha = "{\"mode\":\"legacy\"}") => SyntheticHdPack.Recipe
        .Replace("\"kinds\":[\"critter\",\"scenery\"]}", ChunkRecipe)
        .Replace("\"alpha\":{\"mode\":\"legacy\"}", "\"alpha\":" + alpha + ",\"terrain\":{\"chunkSize\":512,\"padding\":32}")
        .Replace("\"tileSize\":512", "\"tileSize\":600");

    // A synthetic composed level-0 chunk: a pattern with a transparent corner (map edge).
    private static SyntheticHdPack.Frame LegacyChunk(int seed)
    {
        var frame = SyntheticHdPack.Legacy("chunk" + seed, 512, 512, seed);
        var rgba = frame.Rgba.ToArray();
        for (var y = 0; y < 512; y++) for (var x = 0; x < 512; x++)
        {
            var at = (y * 512 + x) * 4;
            if (x + y < 100) { rgba[at] = rgba[at + 1] = rgba[at + 2] = rgba[at + 3] = 0; }
            else { rgba[at + 3] = 255; rgba[at] = (byte)(x ^ seed); }
        }
        return frame with { Rgba = rgba };
    }

    private static JsonObject AddChunk(SyntheticHdPack pack, SyntheticHdPack.Frame legacy, int x, int y, string[] tiles, byte[]? hd = null,
        string slice = "ground", string review = "unreviewed")
    {
        var png = HdPng.EncodeRgba(512 * pack.Scale, 512 * pack.Scale, hd ?? SyntheticHdPack.Hd(legacy, pack.Scale, tint: 30));
        var file = $"chunks/{slice}_{x}_{y}.png";
        pack.WriteFile(file, png);
        var entry = new JsonObject
        {
            ["kind"] = "terrainChunk", ["file"] = file, ["sha256"] = SyntheticHdPack.Sha(png), ["bytes"] = png.Length, ["slice"] = slice,
            ["chunk"] = new JsonObject { ["x"] = x, ["y"] = y, ["size"] = 512, ["padding"] = 32 },
            ["tiles"] = new JsonArray(tiles.Select(t => (JsonNode)t).ToArray()),
            ["legacy"] = new JsonObject { ["width"] = 512, ["height"] = 512, ["rgbaSha256"] = legacy.RgbaSha256 },
            ["review"] = review,
        };
        pack.Entries.Add(entry);
        return entry;
    }

    private static HdSpriteQuery ChunkQuery(SyntheticHdPack.Frame legacy, int x, int y, string[] tiles)
        => HdSpriteQuery.ForTerrainChunk(new(x, y, HdChunkKey.TilesDigest(tiles)), new(512, 512, legacy.RgbaSha256));

    [TestMethod]
    public void A_terrain_chunk_binds_by_coordinates_composed_legacy_and_tiles_across_map_slices()
    {
        string[] tiles = [SyntheticHdPack.Sha("tile:a"), SyntheticHdPack.Sha("tile:b")];
        string[] caveTiles = [SyntheticHdPack.Sha("tile:c")];
        var exterior = LegacyChunk(1); var cave = LegacyChunk(2); var other = LegacyChunk(3);
        var pack = NewPack(recipe: TerrainRecipe());
        var hd = SyntheticHdPack.Hd(exterior, 2, tint: 30);
        AddChunk(pack, exterior, 4, 1, tiles, hd);
        AddChunk(pack, cave, 4, 1, caveTiles, slice: "cave-ground");
        AddChunk(pack, exterior, 5, 1, tiles, review: "rejected");
        var opened = Opened(pack.Write());

        var bound = opened.ResolveTerrainChunk(ChunkQuery(exterior, 4, 1, tiles), exterior.Rgba);
        Assert.AreEqual((HdFrameStatus.Hd, 1024, 1024), (bound.Status, bound.Width, bound.Height));
        CollectionAssert.AreEqual(hd, bound.Rgba, "the exact HD pixels");
        Assert.AreEqual(HdFrameStatus.Hd, opened.ResolveTerrainChunk(ChunkQuery(cave, 4, 1, caveTiles), cave.Rgba).Status, "the other map's chunk at the same coordinates");
        Assert.AreEqual(HdFrameStatus.FrameMissing, opened.ResolveTerrainChunk(ChunkQuery(exterior, 9, 9, tiles), exterior.Rgba).Status);
        Assert.AreEqual(HdFrameStatus.SourceMismatch, opened.ResolveTerrainChunk(ChunkQuery(other, 4, 1, tiles), other.Rgba).Status, "other composed pixels");
        Assert.AreEqual(HdFrameStatus.SourceMismatch, opened.ResolveTerrainChunk(ChunkQuery(exterior, 4, 1, caveTiles), exterior.Rgba).Status, "other tiles");
        Assert.AreEqual(HdFrameStatus.FrameRejected, opened.ResolveTerrainChunk(ChunkQuery(exterior, 5, 1, tiles), exterior.Rgba).Status);
        Assert.ThrowsExactly<ArgumentException>(() => opened.ResolveSprite(ChunkQuery(exterior, 4, 1, tiles), exterior.Rgba), "a chunk is not a sprite");
        var sprite = SyntheticHdPack.Legacy("s", 3, 3, seed: 1);
        Assert.ThrowsExactly<ArgumentException>(() => opened.ResolveTerrainChunk(sprite.Query, sprite.Rgba), "a sprite is not a chunk");
        Assert.ThrowsExactly<ArgumentException>(() => opened.ResolveTerrainChunk(ChunkQuery(exterior, 4, 1, tiles), cave.Rgba), "pixels that contradict the question");
    }

    [TestMethod]
    public void A_terrain_chunk_takes_the_sprite_file_size_and_alpha_checks()
    {
        string[] tiles = [SyntheticHdPack.Sha("tile:a")];
        var legacy = LegacyChunk(1);
        var pack = NewPack(recipe: TerrainRecipe());
        var grown = SyntheticHdPack.Hd(legacy, 2);
        grown[198 * 4 + 3] = 255; // HD texel (198, 0): its parent (99, 0) is transparent, next to the opaque (100, 0)
        AddChunk(pack, legacy, 0, 0, tiles, grown);
        var small = HdPng.EncodeRgba(512, 512, legacy.Rgba);
        pack.WriteFile("chunks/small.png", small);
        var entry = AddChunk(pack, legacy, 1, 0, tiles);
        entry["file"] = "chunks/small.png"; entry["sha256"] = SyntheticHdPack.Sha(small); entry["bytes"] = small.Length;
        var opened = Opened(pack.Write());
        var alpha = opened.ResolveTerrainChunk(ChunkQuery(legacy, 0, 0, tiles), legacy.Rgba);
        Assert.AreEqual((HdFrameStatus.Invalid, HdInvalidReason.AlphaMismatch), (alpha.Status, alpha.Invalid));
        var size = opened.ResolveTerrainChunk(ChunkQuery(legacy, 1, 0, tiles), legacy.Rgba);
        Assert.AreEqual((HdFrameStatus.Invalid, HdInvalidReason.SizeMismatch), (size.Status, size.Invalid));

        var soft = NewPack("soft", recipe: TerrainRecipe("{\"mode\":\"soft\",\"minIoUPermille\":950}"));
        AddChunk(soft, legacy, 0, 0, tiles, grown);
        var far = SyntheticHdPack.Hd(legacy, 2); far[3] = 255; // HD texel (0, 0): far from the silhouette
        AddChunk(soft, legacy, 1, 0, tiles, far);
        var softPack = Opened(soft.Write());
        Assert.AreEqual(HdFrameStatus.Hd, softPack.ResolveTerrainChunk(ChunkQuery(legacy, 0, 0, tiles), legacy.Rgba).Status,
            "soft alpha admits a texel next to the silhouette");
        Assert.AreEqual(HdInvalidReason.AlphaMismatch, softPack.ResolveTerrainChunk(ChunkQuery(legacy, 1, 0, tiles), legacy.Rgba).Invalid,
            "but not one farther than a Legacy texel");
    }

    [TestMethod]
    public void The_tile_digest_is_order_sensitive_and_never_collides_with_a_sprite_key()
    {
        string a = SyntheticHdPack.Sha("a"), b = SyntheticHdPack.Sha("b");
        Assert.AreNotEqual(HdChunkKey.TilesDigest([a, b]), HdChunkKey.TilesDigest([b, a]));
        var chunk = new HdChunkKey(0, 0, HdChunkKey.TilesDigest([a]));
        Assert.AreEqual(-1, chunk.SpriteKey.View, "sprite views are at least 132");
        Assert.AreNotEqual(HdSpriteQuery.ForTerrainChunk(chunk, new(512, 512, a)), HdSpriteQuery.ForTerrainChunk(chunk with { X = 1 }, new(512, 512, a)));
    }
}
