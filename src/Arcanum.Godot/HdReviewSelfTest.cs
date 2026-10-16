using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Arcanum.Data;
using Arcanum.Data.Hd;
using Godot;

namespace Arcanum.Godot;

// ARC-RENDER-015 self-test: a synthetic 2× pack built in a temporary directory from the
// synthetic DAT's static ART (no original pixel), then the review state, the canvas HD
// layer, the pair capture and a manifest mark.
internal static class HdReviewSelfTest
{
    private const string Recipe = """
        {"schema":1,"kind":"arcanum-steamwoven-hd-recipe","id":"selftest","scale":2,
         "slices":[{"name":"bodies","map":"synthetic-map","kinds":["critter","scenery"]}],
         "method":{"kind":"external","tool":"selftest-replica","toolVersion":"1","nativeScale":2,"downscale":"none"},
         "alpha":{"mode":"legacy"}}
        """;

    internal static string Run(DatArchive archive, IReadOnlyList<ViewerEntry> entries)
    {
        var root = Path.Combine(Path.GetTempPath(), "arc-hd-review-selftest-" + Guid.NewGuid().ToString("N"));
        try
        {
            var art = archive.ReadEntry(entries[0].Entry);
            var frames = new[] { ArtDecoder.Decode(art, new(0, 0, 2), ViewerState.DecoderOptions), ArtDecoder.Decode(art, new(0, 1, 2), ViewerState.DecoderOptions) };
            Require(frames.All(f => f.EffectivePaletteSlot == 2), "synthetic pack source palette");
            var hd = frames.Select(f => Replica(f)).ToArray();
            var pack = Opened(WritePack(Path.Combine(root, "pack"), art, HdReviewState.NameSha256(entries[0].Entry), frames, hd));
            Require(pack.Recipe.Method.Kind == HdMethodKind.External && pack.Scale == 2, "any method kind opens for review");

            // Review state: lookup by ART hash, stale results dropped, cached answers at once.
            var state = new HdReviewState();
            state.SetPack(pack);
            var snapshot = new SelectedArtSnapshot(new EntryIdentity(1, 1, entries[0].Entry.Ordinal), art, ArtDecoder.Inspect(art, ViewerState.DecoderOptions));
            Require(state.TryCached(snapshot, 0, 0, 2, _ => true) is null, "no cached answer before the ART hash is known");
            state.Request(snapshot, 0, 0, 2);
            var first = state.WaitForResult(TimeSpan.FromSeconds(10));
            Require(first is { Entry.Index: 0, Frame: { Status: HdReviewFrameStatus.Compared, AlphaMatches: true } } && ReferenceEquals(state.Current, first),
                "review lookup binds the shown frame to its pack frame");
            Require(first!.Frame!.Hd!.Rgba!.AsSpan().SequenceEqual(hd[0]) && first.Frame.Legacy!.Rgba.SequenceEqual(frames[0].Rgba), "review pair pixels");
            state.Request(snapshot, 0, 1, 2);
            state.Invalidate();
            Require(state.WaitForResult(TimeSpan.FromMilliseconds(300)) is null && state.Current is null, "a superseded review lookup is dropped");
            state.Request(snapshot, 0, 1, 2);
            Require(state.WaitForResult(TimeSpan.FromSeconds(10)) is { Entry.Index: 1 }, "second pack frame");
            Require(state.TryCached(snapshot, 0, 0, 2, _ => true) is { Entry.Index: 0, Cached: true, Frame: null }, "cached pair answers on the main thread");
            Require(state.TryCached(snapshot, 0, 0, 0, _ => true) is { Entry: null, Cached: false }, "a frame the pack lacks answers at once");
            Require(state.TryCached(snapshot, 0, 0, 2, _ => false) is null, "an uncached pair needs a lookup");
            var other = archive.ReadEntry(entries[1].Entry);
            state.Request(new SelectedArtSnapshot(new EntryIdentity(1, 2, entries[1].Entry.Ordinal), other, ArtDecoder.Inspect(other, ViewerState.DecoderOptions)), 0, 0, 2);
            Require(state.WaitForResult(TimeSpan.FromSeconds(10)) is { Entry: null }, "another ART has no pack frame");

            // Flip clock: original first, then HD.
            state.SetMode(HdViewMode.Flip);
            state.SetFlipSeconds(1.0);
            Require(state.CanvasView == FrameView.Legacy && !state.AdvanceFlip(0.5) && state.AdvanceFlip(0.6) && state.CanvasView == FrameView.Hd, "flip alternates original then HD");
            state.SetMode(HdViewMode.Split);
            Require(state.CanvasView == FrameView.Split && !state.AdvanceFlip(5), "split view does not flip");
            RequireThrows<ArgumentOutOfRangeException>(() => state.SetFlipSeconds(0), "flip period limit");

            // HD texture takes the Legacy frame's place; the canvas swaps and clears it.
            using var hdTexture = HdReviewCapture.CreateHdTexture(first.Frame.Hd, frames[0].Width, frames[0].Height);
            using var hdImage = hdTexture.GetImage();
            Require(hdTexture.GetSize() == new Vector2I(3, 2) && hdImage.GetSize() == new Vector2I(6, 4) && hdImage.GetData().AsSpan().SequenceEqual(hd[0]),
                "HD texture keeps HD pixels at the Legacy size");
            RequireThrows<ArgumentException>(() => HdReviewCapture.CreateHdTexture(first.Frame.Hd, 2, 2), "HD texture refuses a size that is not scale × Legacy");
            using var legacyTexture = ViewerApp.CreateFrameTexture(frames[0]);
            using var nextTexture = ViewerApp.CreateFrameTexture(frames[1]);
            var canvas = new FrameCanvas { CustomMinimumSize = Vector2.Zero, Size = new Vector2(90, 50) };
            var layout = new FrameCanvasGroupLayout([new FrameCanvasFrame(3, 2, 0, 0), new FrameCanvasFrame(2, 2, 0, 0)]);
            canvas.ResetAsset(legacyTexture, FrameHotspot.Zero, layout);
            canvas.SetHd(hdTexture);
            Require(ReferenceEquals(canvas.DisplayedTexture, legacyTexture), "Legacy view draws Legacy");
            canvas.SetView(FrameView.Hd);
            Require(ReferenceEquals(canvas.DisplayedTexture, hdTexture), "HD view draws HD");
            canvas.SetCaption("HD 2×");
            Require(canvas.Caption == "HD 2×", "canvas caption");
            RequireThrows<InvalidOperationException>(() => canvas.CommitFramePublication(nextTexture, FrameHotspot.Zero, layout, false, false,
                () => throw new InvalidOperationException("injected")), "injected publication failure");
            Require(ReferenceEquals(canvas.HdTexture, hdTexture) && ReferenceEquals(canvas.PublishedTexture, legacyTexture), "failed publication restores the HD frame");
            canvas.CommitFramePublication(nextTexture, FrameHotspot.Zero, layout, false, false);
            Require(canvas.HdTexture is null && ReferenceEquals(canvas.DisplayedTexture, nextTexture), "a new Legacy frame never shows the previous HD frame");
            canvas.SetHd(hdTexture);
            canvas.ClearAsset();
            Require(canvas.HdTexture is null && canvas.DisplayedTexture is null, "clearing the asset clears HD");
            RequireThrows<ArgumentOutOfRangeException>(() => canvas.SetView((FrameView)9), "undefined canvas view");
            canvas.Free();

            // Pair capture: nearest Legacy ×2, a gap of 8, HD; written as a PNG without a path in its name.
            using var legacyImage = legacyTexture.GetImage();
            using var pair = HdReviewCapture.ComposePair(legacyImage, hdImage, 2);
            Require(pair.GetSize() == new Vector2I(6 * 2 + 8, 4), "pair size");
            var pairBytes = pair.GetData();
            var replicated = Replica(frames[0], 0);
            for (var y = 0; y < 4; y++)
            {
                Require(pairBytes.AsSpan(y * 20 * 4, 6 * 4).SequenceEqual(replicated.AsSpan(y * 6 * 4, 6 * 4)), "pair left is the nearest Legacy replica");
                Require(pairBytes.AsSpan((y * 20 + 14) * 4, 6 * 4).SequenceEqual(hd[0].AsSpan(y * 6 * 4, 6 * 4)), "pair right is the HD frame");
                Require(pairBytes.AsSpan((y * 20 + 6) * 4, 8 * 4).ToArray().All(b => b == 0), "pair gap is transparent");
            }
            var name = HdReviewCapture.FileName(HdReviewCapture.Subject(first.Entry, pack, null, 0, 0, 2), "pair", new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc));
            Require(name == "selftest-pack-e000000-20261016-120000-000-pair.png", "capture file name names the pack frame, not a path");
            Require(HdReviewCapture.Subject(null, null, 7, 1, 2, 3) == "art7-g1f2p3", "capture name without a pack");
            var saved = HdReviewCapture.Save(pair, Path.Combine(root, "captures"), name);
            using var loaded = Image.LoadFromFile(Path.Combine(root, "captures", saved));
            Require(loaded.GetSize() == pair.GetSize(), "pair capture written as PNG");
            var capturesDefault = HdReviewCapture.DefaultDirectory();
            Require(capturesDefault.EndsWith(Path.Combine("local", "hd-review"), StringComparison.Ordinal) || capturesDefault.EndsWith("hd-review", StringComparison.Ordinal),
                "default capture folder is local");

            // A mark is written into the manifest; the shown result keeps its pair with the new mark.
            state.SetPack(pack);
            state.Request(snapshot, 0, 0, 2);
            Require(state.WaitForResult(TimeSpan.FromSeconds(10)) is { Entry.Index: 0 }, "lookup before marking");
            var marked = HdPackReview.Save(pack, new Dictionary<int, HdReviewStatus> { [0] = HdReviewStatus.Rejected });
            Require(marked.Status == HdReviewSaveStatus.Saved, "mark saved");
            state.ReplacePack(marked.Pack!);
            Require(state.Current is { Entry.Review: HdReviewStatus.Rejected, Frame.Entry.Review: HdReviewStatus.Rejected } && state.Index!.Count(HdReviewStatus.Rejected) == 1,
                "review state follows the mark");
            Require(Opened(Path.Combine(root, "pack")).Entries[0].Review == HdReviewStatus.Rejected, "mark is in the manifest on disk");
            state.Request(snapshot, 0, 0, 2);
            Require(state.WaitForResult(TimeSpan.FromSeconds(10)) is { Frame.Status: HdReviewFrameStatus.Compared }, "a rejected frame stays reviewable");
            return "HD review: pack lookup, stale suppression, cache, flip/split, HD canvas layer, pair capture and manifest marks.";
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    // Nearest 2× replica; with tint, opaque odd columns change so HD differs from Legacy.
    private static byte[] Replica(ArtFrame frame, int tint)
    {
        var legacy = frame.Rgba.ToArray();
        var (w, h) = (frame.Width * 2, frame.Height * 2);
        var rgba = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var to = (y * w + x) * 4;
            Array.Copy(legacy, ((y / 2) * frame.Width + x / 2) * 4, rgba, to, 4);
            if (rgba[to + 3] != 0 && x % 2 == 1) rgba[to] = (byte)(rgba[to] + tint);
        }
        return rgba;
    }

    private static byte[] Replica(ArtFrame frame) => Replica(frame, 40);

    private static string WritePack(string directory, byte[] art, string nameSha, IReadOnlyList<ArtFrame> frames, IReadOnlyList<byte[]> hd)
    {
        Directory.CreateDirectory(Path.Combine(directory, "frames"));
        var recipe = Encoding.UTF8.GetBytes(Recipe);
        File.WriteAllBytes(Path.Combine(directory, "recipe.json"), recipe);
        var list = new JsonArray();
        for (var i = 0; i < frames.Count; i++)
        {
            var png = HdPng.EncodeRgba(frames[i].Width * 2, frames[i].Height * 2, hd[i]);
            File.WriteAllBytes(Path.Combine(directory, "frames", $"{i}.png"), png);
            list.Add(new JsonObject
            {
                ["kind"] = "sprite", ["file"] = $"frames/{i}.png", ["sha256"] = Sha(png), ["bytes"] = png.Length, ["slice"] = "bodies",
                ["art"] = new JsonObject { ["sha256"] = Sha(art), ["view"] = art.Length, ["nameSha256"] = nameSha },
                ["group"] = 0, ["frame"] = frames[i].FrameIndex, ["palette"] = frames[i].EffectivePaletteSlot,
                ["legacy"] = new JsonObject { ["width"] = frames[i].Width, ["height"] = frames[i].Height, ["rgbaSha256"] = Sha(frames[i].Rgba.ToArray()) },
                ["review"] = "unreviewed",
            });
        }
        var manifest = new JsonObject
        {
            ["schema"] = 1, ["kind"] = "arcanum-steamwoven-hd-pack", ["id"] = "selftest-pack", ["scale"] = 2, ["review"] = "unreviewed",
            ["recipe"] = new JsonObject { ["file"] = "recipe.json", ["sha256"] = Sha(recipe) },
            ["build"] = new JsonObject { ["builder"] = "selftest", ["builderVersion"] = "1", ["createdUtc"] = "2026-10-16T12:00:00Z", ["gpu"] = "none", ["os"] = "any" },
            ["entries"] = list,
        };
        File.WriteAllText(Path.Combine(directory, "manifest.json"), manifest.ToJsonString());
        return directory;
    }

    private static HdPack Opened(string directory)
    {
        var opened = HdPack.Open(directory);
        Require(opened.Status == HdPackOpenStatus.Opened, $"synthetic pack opens ({opened.Status}: {opened.Detail})");
        return opened.Pack!;
    }

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void Require(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"Self-test failed: {name}.");
    }

    private static void RequireThrows<TException>(Action action, string name) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"Self-test failed: {name}.");
    }
}
