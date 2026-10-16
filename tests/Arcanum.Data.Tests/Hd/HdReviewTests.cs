using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using Arcanum.Data.Hd;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Hd;

[TestClass]
public sealed class HdReviewTests
{
    private string _root = null!;

    [TestInitialize]
    public void Create() => Directory.CreateDirectory(_root = Path.Combine(Path.GetTempPath(), "arc-hd-review-" + Guid.NewGuid().ToString("N")));

    [TestCleanup]
    public void Remove()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static HdPack Opened(string directory)
    {
        var result = HdPack.Open(directory);
        Assert.AreEqual(HdPackOpenStatus.Opened, result.Status, result.Detail);
        return result.Pack!;
    }

    private (SyntheticHdPack Pack, SyntheticHdPack.Frame[] Frames) ThreeFrames()
    {
        var pack = new SyntheticHdPack(Path.Combine(_root, "pack"));
        var frames = new[]
        {
            SyntheticHdPack.Legacy("critter", 5, 4, seed: 1, group: 2, frame: 1),
            SyntheticHdPack.Legacy("critter", 5, 4, seed: 2, group: 2, frame: 2),
            SyntheticHdPack.Legacy("tree", 3, 6, seed: 3, palette: 1),
        };
        foreach (var frame in frames) pack.Add(frame);
        pack.Write();
        return (pack, frames);
    }

    [TestMethod]
    public void Marks_are_written_into_the_manifest_and_nothing_else_changes()
    {
        var (synthetic, frames) = ThreeFrames();
        var manifestPath = Path.Combine(synthetic.Directory, "manifest.json");
        var pngBefore = Directory.GetFiles(Path.Combine(synthetic.Directory, "frames")).Order().Select(File.ReadAllBytes).ToArray();
        var pack = Opened(synthetic.Directory);

        var saved = HdPackReview.Save(pack, new Dictionary<int, HdReviewStatus> { [0] = HdReviewStatus.Approved, [2] = HdReviewStatus.Rejected });
        Assert.AreEqual(HdReviewSaveStatus.Saved, saved.Status, saved.Detail);

        var reopened = Opened(synthetic.Directory);
        Assert.AreEqual(saved.Pack!.Identity, reopened.Identity);
        Assert.AreNotEqual(pack.Identity.ManifestSha256, reopened.Identity.ManifestSha256);
        Assert.AreEqual((pack.Id, pack.Scale, pack.RecipeSha256, pack.Build, HdReviewStatus.Unreviewed), (reopened.Id, reopened.Scale, reopened.RecipeSha256, reopened.Build, reopened.Review));
        CollectionAssert.AreEqual(new[] { HdReviewStatus.Approved, HdReviewStatus.Unreviewed, HdReviewStatus.Rejected }, reopened.Entries.Select(e => e.Review).ToArray());
        for (var i = 0; i < 3; i++)
            Assert.AreEqual(pack.Entries[i] with { Review = reopened.Entries[i].Review }, reopened.Entries[i]);

        // Only the review values differ from the original JSON.
        var before = JsonNode.Parse(synthetic.Manifest.ToJsonString())!;
        before["entries"]![0]!["review"] = "approved";
        before["entries"]![2]!["review"] = "rejected";
        Assert.IsTrue(JsonNode.DeepEquals(before, JsonNode.Parse(File.ReadAllText(manifestPath))));
        // Property order and values are copied: the bytes are the compact serialization of the edited original.
        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes(before.ToJsonString()), File.ReadAllBytes(manifestPath));
        CollectionAssert.AreEqual(pngBefore.SelectMany(b => b).ToArray(),
            Directory.GetFiles(Path.Combine(synthetic.Directory, "frames")).Order().Select(File.ReadAllBytes).SelectMany(b => b).ToArray());
        Assert.AreEqual(0, Directory.GetFiles(synthetic.Directory, ".manifest-review-*").Length);

        // The game refuses the rejected frame; the review still compares nothing else away.
        Assert.AreEqual(HdFrameStatus.FrameRejected, reopened.ResolveSprite(frames[2].Query, frames[2].Rgba).Status);
        Assert.AreEqual(HdFrameStatus.Hd, reopened.ResolveSprite(frames[0].Query, frames[0].Rgba).Status);

        // The pack mark, then a no-op: nothing written.
        var packMarked = HdPackReview.Save(reopened, new Dictionary<int, HdReviewStatus>(), HdReviewStatus.Rejected);
        Assert.AreEqual(HdReviewSaveStatus.Saved, packMarked.Status);
        Assert.AreEqual(HdReviewStatus.Rejected, Opened(synthetic.Directory).Review);
        var bytes = File.ReadAllBytes(manifestPath);
        var same = HdPackReview.Save(packMarked.Pack!, new Dictionary<int, HdReviewStatus> { [0] = HdReviewStatus.Approved }, HdReviewStatus.Rejected);
        Assert.AreEqual(HdReviewSaveStatus.Unchanged, same.Status);
        Assert.AreSame(packMarked.Pack, same.Pack);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(manifestPath));

        // Back to unreviewed.
        var cleared = HdPackReview.Save(packMarked.Pack!, new Dictionary<int, HdReviewStatus> { [0] = HdReviewStatus.Unreviewed, [2] = HdReviewStatus.Unreviewed }, HdReviewStatus.Unreviewed);
        Assert.AreEqual(HdReviewSaveStatus.Saved, cleared.Status);
        Assert.IsTrue(Opened(synthetic.Directory).Entries.All(e => e.Review == HdReviewStatus.Unreviewed));
    }

    [TestMethod]
    public void A_manifest_changed_on_disk_is_never_overwritten()
    {
        var (synthetic, _) = ThreeFrames();
        var pack = Opened(synthetic.Directory);
        synthetic.Manifest["build"]!["gpu"] = "another";
        synthetic.Write();
        var external = File.ReadAllBytes(Path.Combine(synthetic.Directory, "manifest.json"));

        var result = HdPackReview.Save(pack, new Dictionary<int, HdReviewStatus> { [1] = HdReviewStatus.Approved });
        Assert.AreEqual((HdReviewSaveStatus.Changed, (HdPack?)null), (result.Status, result.Pack));
        CollectionAssert.AreEqual(external, File.ReadAllBytes(Path.Combine(synthetic.Directory, "manifest.json")));

        File.Delete(Path.Combine(synthetic.Directory, "manifest.json"));
        Assert.AreEqual(HdReviewSaveStatus.ManifestUnreadable, HdPackReview.Save(pack, new Dictionary<int, HdReviewStatus> { [1] = HdReviewStatus.Approved }).Status);
    }

    [TestMethod]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void A_refused_write_leaves_the_old_manifest_and_no_temporary_file()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Unix permissions.");
        if (Environment.UserName == "root") Assert.Inconclusive("Permissions do not stop root.");
        var (synthetic, _) = ThreeFrames();
        var pack = Opened(synthetic.Directory);
        var manifest = Path.Combine(synthetic.Directory, "manifest.json");
        var bytes = File.ReadAllBytes(manifest);
        var mode = File.GetUnixFileMode(synthetic.Directory);
        File.SetUnixFileMode(synthetic.Directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            var result = HdPackReview.Save(pack, new Dictionary<int, HdReviewStatus> { [0] = HdReviewStatus.Approved });
            Assert.AreEqual((HdReviewSaveStatus.WriteFailed, (HdPack?)null), (result.Status, result.Pack));
        }
        finally { File.SetUnixFileMode(synthetic.Directory, mode); }
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(manifest));
        Assert.AreEqual(0, Directory.GetFiles(synthetic.Directory, ".manifest-review-*").Length);
    }

    [TestMethod]
    public void Terrain_chunk_entries_survive_a_rewrite_unchanged()
    {
        var recipe = SyntheticHdPack.Recipe.Replace("\"kinds\":[\"critter\",\"scenery\"]}", "\"kinds\":[\"critter\",\"scenery\"]},{\"name\":\"ground\",\"map\":\"synthetic-map\",\"kinds\":[\"terrainChunk\"]}")
            .Replace("\"alpha\":{\"mode\":\"legacy\"}", "\"alpha\":{\"mode\":\"legacy\"},\"terrain\":{\"chunkSize\":512,\"padding\":32}")
            .Replace("\"tileSize\":512", "\"tileSize\":600");
        var synthetic = new SyntheticHdPack(Path.Combine(_root, "terrain"), recipe: recipe);
        synthetic.Add(SyntheticHdPack.Legacy("s", 3, 3, seed: 1));
        var png = HdPng.EncodeRgba(1024, 1024, new byte[1024 * 1024 * 4]);
        synthetic.WriteFile("chunks/4_1.png", png);
        synthetic.Entries.Add(new JsonObject
        {
            ["kind"] = "terrainChunk", ["file"] = "chunks/4_1.png", ["sha256"] = SyntheticHdPack.Sha(png), ["bytes"] = png.Length, ["slice"] = "ground",
            ["chunk"] = new JsonObject { ["x"] = 4, ["y"] = 1, ["size"] = 512, ["padding"] = 32 },
            ["tiles"] = new JsonArray(SyntheticHdPack.Sha("tile:a"), SyntheticHdPack.Sha("tile:b")),
            ["legacy"] = new JsonObject { ["width"] = 512, ["height"] = 512, ["rgbaSha256"] = SyntheticHdPack.Sha("chunk") },
            ["review"] = "unreviewed",
        });
        var pack = Opened(synthetic.Write());

        var saved = HdPackReview.Save(pack, new Dictionary<int, HdReviewStatus> { [0] = HdReviewStatus.Approved, [1] = HdReviewStatus.Rejected });
        Assert.AreEqual(HdReviewSaveStatus.Saved, saved.Status, saved.Detail);
        var chunk = Opened(synthetic.Directory).Entries[1];
        Assert.AreEqual((HdEntryKind.TerrainChunk, HdReviewStatus.Rejected, 4, 1, 32), (chunk.Kind, chunk.Review, chunk.ChunkX, chunk.ChunkY, chunk.ChunkPadding));
        CollectionAssert.AreEqual(pack.Entries[1].TileRgbaSha256!.ToArray(), chunk.TileRgbaSha256!.ToArray());
        Assert.AreEqual(1, new HdReviewIndex(saved.Pack!).Sprites.Count);
    }

    [TestMethod]
    public void A_generative_4x_soft_alpha_pack_is_reviewed_too()
    {
        const string generative = """
            {"schema":1,"kind":"arcanum-steamwoven-hd-recipe","id":"synthetic-generative","scale":4,
             "slices":[{"name":"bodies","map":"synthetic-map","kinds":["critter"]}],
             "method":{"kind":"generative","tool":"synthetic-diffusion","toolVersion":"1","model":"synthetic-base","licence":"test",
               "modelFiles":[{"name":"base.bin","sha256":"0000000000000000000000000000000000000000000000000000000000000003"}],
               "nativeScale":4,"downscale":"none","seed":42,"prompt":"a synthetic sprite"},
             "alpha":{"mode":"soft","minIoUPermille":900}}
            """;
        var art = Art();
        var synthetic = new SyntheticHdPack(Path.Combine(_root, "generative"), scale: 4, recipe: generative);
        var legacy = Add(synthetic, art, art.Length, frame: 0, palette: 1);
        var pack = Opened(synthetic.Write());
        Assert.AreEqual((HdMethodKind.Generative, HdAlphaMode.Soft, 4), (pack.Recipe.Method.Kind, pack.Recipe.Alpha.Mode, pack.Scale));

        var compared = HdReviewComparison.Compare(pack, pack.Entries[0], art);
        Assert.AreEqual((HdReviewFrameStatus.Compared, true, 12, 8), (compared.Status, compared.AlphaMatches, compared.Hd!.Width, compared.Hd.Height));
        CollectionAssert.AreEqual(SyntheticHdPack.Hd(legacy, 4, tint: 40), compared.Hd.Rgba);
    }

    [TestMethod]
    public void A_linked_manifest_is_not_written_through()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Creating symbolic links needs privileges on Windows.");
        var (synthetic, _) = ThreeFrames();
        var pack = Opened(synthetic.Directory);
        var outside = Path.Combine(_root, "outside.json");
        File.Move(Path.Combine(synthetic.Directory, "manifest.json"), outside);
        File.CreateSymbolicLink(Path.Combine(synthetic.Directory, "manifest.json"), outside);
        var bytes = File.ReadAllBytes(outside);

        Assert.AreEqual(HdReviewSaveStatus.ManifestUnreadable, HdPackReview.Save(pack, new Dictionary<int, HdReviewStatus> { [0] = HdReviewStatus.Approved }).Status);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(outside));
    }

    [TestMethod]
    public void Caller_errors_throw()
    {
        var (synthetic, _) = ThreeFrames();
        var pack = Opened(synthetic.Directory);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => HdPackReview.Save(pack, new Dictionary<int, HdReviewStatus> { [3] = HdReviewStatus.Approved }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => HdPackReview.Save(pack, new Dictionary<int, HdReviewStatus> { [0] = (HdReviewStatus)7 }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => HdPackReview.Save(pack, new Dictionary<int, HdReviewStatus>(), (HdReviewStatus)7));
    }

    // A static ART with palettes 0 and 1 and two raw frames; slot 2 and 3 are absent.
    private static byte[] Art(int extraBytes = 0)
    {
        (int W, int H)[] sizes = [(3, 2), (2, 3)];
        const int palettes = 2;
        var table = 132 + palettes * 1024;
        var payload = table + sizes.Length * 28;
        var bytes = new byte[payload + sizes.Sum(s => s.W * s.H) + extraBytes];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 10);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 8);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(32), sizes.Length);
        for (var slot = 0; slot < palettes; slot++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12 + slot * 4), (uint)(slot + 10));
            for (var c = 1; c < 256; c++)
            {
                var at = 132 + slot * 1024 + c * 4;
                (bytes[at], bytes[at + 1], bytes[at + 2]) = ((byte)(c * 3 + slot), (byte)(c * 5), (byte)(c * 7 + slot * 40));
            }
        }
        var cursor = payload;
        for (var f = 0; f < sizes.Length; f++)
        {
            var header = table + f * 28;
            var (w, h) = sizes[f];
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(header), w);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(header + 4), h);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(header + 8), w * h);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(header + 12), 1);
            for (var i = 0; i < w * h; i++) bytes[cursor + i] = (byte)(i % 3 == 0 ? 0 : 20 + i + f);
            cursor += w * h;
        }
        return bytes;
    }

    // A pack entry for the frame as decoded from the ART's first `view` bytes.
    private static SyntheticHdPack.Frame Add(SyntheticHdPack pack, byte[] art, int view, int frame, int palette, byte[]? hd = null, string? legacySha = null)
    {
        var decoded = ArtDecoder.Decode(art.AsSpan(0, view), new(0, frame, palette));
        var legacy = new SyntheticHdPack.Frame("synthetic.art", decoded.Width, decoded.Height, 0, frame, palette, decoded.Rgba.ToArray());
        var entry = pack.Add(legacy, hd);
        entry["art"] = new JsonObject { ["sha256"] = SyntheticHdPack.Sha(art), ["view"] = view, ["nameSha256"] = SyntheticHdPack.Sha("art\\synthetic.art\0") };
        if (legacySha is not null) entry["legacy"]!["rgbaSha256"] = legacySha;
        return legacy;
    }

    [TestMethod]
    public void A_frame_is_compared_from_its_own_view_group_frame_and_palette()
    {
        var art = Art(extraBytes: 0);
        var synthetic = new SyntheticHdPack(Path.Combine(_root, "compare"));
        var first = Add(synthetic, art, art.Length, frame: 0, palette: 1);
        Add(synthetic, art, art.Length, frame: 1, palette: 0);
        var pack = Opened(synthetic.Write());
        var index = new HdReviewIndex(pack);

        var compared = HdReviewComparison.Compare(pack, index.Find(SyntheticHdPack.Sha(art), art.Length, 0, 0, 1)!, art);
        Assert.AreEqual((HdReviewFrameStatus.Compared, true, 6, 4), (compared.Status, compared.AlphaMatches, compared.Hd!.Width, compared.Hd.Height));
        CollectionAssert.AreEqual(first.Rgba, compared.Legacy!.Rgba.ToArray());
        CollectionAssert.AreEqual(SyntheticHdPack.Hd(first, 2, tint: 40), compared.Hd.Rgba);

        // Rejected frames stay reviewable; the game refuses them.
        var rejected = HdPackReview.Save(pack, new Dictionary<int, HdReviewStatus> { [0] = HdReviewStatus.Rejected }, HdReviewStatus.Rejected).Pack!;
        Assert.AreEqual(HdReviewFrameStatus.Compared, HdReviewComparison.Compare(rejected, rejected.Entries[0], art).Status);
        Assert.AreEqual(HdFrameStatus.PackRejected, rejected.ResolveSprite(first.Query with { Key = rejected.Entries[0].Key!.Value, NameSha256 = rejected.Entries[0].NameSha256! }, first.Rgba).Status);

        // Other bytes under the same name: not the frame's ART.
        var other = art.ToArray(); other[^1] ^= 1;
        Assert.AreEqual(HdReviewFrameStatus.ArtMismatch, HdReviewComparison.Compare(pack, pack.Entries[0], other).Status);
        Assert.ThrowsExactly<ArgumentException>(() => HdReviewComparison.Compare(pack, pack.Entries[0] with { Review = HdReviewStatus.Approved }, art));
    }

    [TestMethod]
    public void Review_shows_a_failed_alpha_check_and_explains_every_refusal()
    {
        var art = Art();
        var synthetic = new SyntheticHdPack(Path.Combine(_root, "refusals"));
        var legacy = ArtDecoder.Decode(art, new(0, 0, 0));
        var frame = new SyntheticHdPack.Frame("x", legacy.Width, legacy.Height, 0, 0, 0, legacy.Rgba.ToArray());
        var blob = SyntheticHdPack.Hd(frame, 2);
        blob[3] = blob[3] == 0 ? (byte)255 : (byte)0; // one HD texel's alpha differs from its parent (legacy alpha mode)
        Add(synthetic, art, art.Length, frame: 0, palette: 0, hd: blob);
        Add(synthetic, art, art.Length, frame: 1, palette: 0, legacySha: new string('a', 64));
        var pack = Opened(synthetic.Write());

        var alpha = HdReviewComparison.Compare(pack, pack.Entries[0], art);
        Assert.AreEqual((HdReviewFrameStatus.Compared, false), (alpha.Status, alpha.AlphaMatches));
        CollectionAssert.AreEqual(blob, alpha.Hd!.Rgba);
        var game = pack.ResolveSprite(new(pack.Entries[0].Key!.Value, pack.Entries[0].NameSha256!, pack.Entries[0].Legacy), frame.Rgba);
        Assert.AreEqual((HdFrameStatus.Invalid, HdInvalidReason.AlphaMismatch), (game.Status, game.Invalid));

        var source = HdReviewComparison.Compare(pack, pack.Entries[1], art);
        Assert.AreEqual((HdReviewFrameStatus.Refused, HdFrameStatus.SourceMismatch), (source.Status, source.Hd!.Status));
        Assert.IsNotNull(source.Legacy);
    }

    [TestMethod]
    public void View_palette_and_decode_problems_are_typed()
    {
        var art = Art(extraBytes: 3);
        var view = art.Length - 3;
        var synthetic = new SyntheticHdPack(Path.Combine(_root, "views"));
        Add(synthetic, art, view, frame: 0, palette: 0);
        var palette = Add(synthetic, art, view, frame: 1, palette: 0);
        var pack = Opened(synthetic.Write());
        var index = new HdReviewIndex(pack);

        // A shorter view of the same bytes: found for the full-view frame, decoded from its own view.
        var found = index.Find(SyntheticHdPack.Sha(art), art.Length, 0, 0, 0);
        Assert.AreSame(pack.Entries[0], found);
        Assert.AreEqual(HdReviewFrameStatus.Compared, HdReviewComparison.Compare(pack, found!, art).Status);
        Assert.IsNull(index.Find(SyntheticHdPack.Sha(art), art.Length, 0, 0, 1));

        // Entry claims a view longer than the bytes / a slot that falls back / an undecodable view.
        synthetic.Entries[0]!["art"]!["view"] = art.Length + 1;
        synthetic.Entries[1]!["palette"] = 3;
        var bad = new SyntheticHdPack(Path.Combine(_root, "bad"));
        bad.Add(palette);
        bad.Entries[0]!["art"] = new JsonObject { ["sha256"] = SyntheticHdPack.Sha(art), ["view"] = 140, ["nameSha256"] = SyntheticHdPack.Sha("n") };
        var badPack = Opened(bad.Write());
        Assert.AreEqual(HdReviewFrameStatus.LegacyUndecodable, HdReviewComparison.Compare(badPack, badPack.Entries[0], art).Status);
        var changed = Opened(synthetic.Write());
        Assert.AreEqual(HdReviewFrameStatus.ViewOutOfRange, HdReviewComparison.Compare(changed, changed.Entries[0], art).Status);
        var fallback = HdReviewComparison.Compare(changed, changed.Entries[1], art);
        Assert.AreEqual((HdReviewFrameStatus.PaletteMismatch, 0), (fallback.Status, fallback.Legacy!.EffectivePaletteSlot));
    }

    [TestMethod]
    public void Index_counts_and_steps_in_manifest_order_with_a_filter()
    {
        var (synthetic, frames) = ThreeFrames();
        var pack = HdPackReview.Save(Opened(synthetic.Directory), new Dictionary<int, HdReviewStatus> { [1] = HdReviewStatus.Approved }).Pack!;
        var index = new HdReviewIndex(pack);

        Assert.AreEqual((2, 1, 0), (index.Count(HdReviewStatus.Unreviewed), index.Count(HdReviewStatus.Approved), index.Count(HdReviewStatus.Rejected)));
        Assert.AreEqual(2, index.ForArt(frames[0].ArtSha256).Count);
        Assert.AreEqual(1, index.ForName(frames[2].NameSha256).Count);
        Assert.AreEqual(0, index.ForArt(new string('0', 64)).Count);
        Assert.AreSame(pack.Entries[1], index.Find(frames[1].ArtSha256, SyntheticHdPack.Frame.View, 2, 2, 0));

        Assert.AreEqual(0, index.Step(-1, 1)!.Index);
        Assert.AreEqual(2, index.Step(-1, -1)!.Index);
        Assert.AreEqual(1, index.Step(0, 1)!.Index);
        Assert.AreEqual(0, index.Step(2, 1)!.Index);
        Assert.AreEqual(2, index.Step(0, -1)!.Index);
        Func<HdPackEntry, bool> unreviewed = e => e.Review == HdReviewStatus.Unreviewed;
        Assert.AreEqual(2, index.Step(0, 1, unreviewed)!.Index);
        Assert.AreEqual(0, index.Step(2, 1, unreviewed)!.Index);
        Assert.IsNull(index.Step(0, 1, e => e.Review == HdReviewStatus.Rejected));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => index.Step(0, 2));
    }
}
