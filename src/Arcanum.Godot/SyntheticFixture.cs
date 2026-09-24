using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Arcanum.Data;
using Arcanum.Presentation.Animation;
using Godot;

namespace Arcanum.Godot;

internal static class SyntheticFixture
{
    private const int ArtHeaderBytes = 132;
    private const int PaletteBytes = 1024;
    private const int FrameHeaderBytes = 28;

    internal static byte[] CreateDat() => CreateDat(
        new DatFixtureEntry("duplicate.art", CreateStaticArt()),
        new DatFixtureEntry("duplicate.art", CreateDirectionalArt()),
        new DatFixtureEntry("fallback.art", CreateStaticArt()),
        new DatFixtureEntry("missing-zero.art", CreateMissingZeroArt()),
        new DatFixtureEntry("malformed.art", CreateStaticArt()[..^1]),
        new DatFixtureEntry("compressed.art", CreateStaticArt(), Zlib: true),
        new DatFixtureEntry("unsupported.art", CreateUnsupportedArt()),
        new DatFixtureEntry("not-art.txt", [1, 2, 3]));

    internal static byte[] CreateOversizeDat() => CreateDat(
        new DatFixtureEntry("oversize.art", new byte[checked((int)ViewerState.EntryCapBytes + 1)]));

    internal static byte[] CreateStaticArt() => CreateArt(
        staticFlag: true,
        palettes: new Dictionary<int, byte[]>
        {
            [0] = Palette((1, 3, 2, 1), (2, 6, 5, 4), (3, 9, 8, 7), (4, 12, 11, 10), (5, 15, 14, 13)),
            [2] = Palette((1, 33, 32, 31), (2, 36, 35, 34), (3, 39, 38, 37), (4, 42, 41, 40), (5, 45, 44, 43)),
        },
        new ArtFixtureFrame(3, 2, [0, 1, 2, 3, 4, 5]),
        new ArtFixtureFrame(2, 2, [0x84, 1, 2, 3, 4]));

    internal static byte[] CreateMissingZeroArt() => CreateArt(
        staticFlag: true,
        palettes: new Dictionary<int, byte[]> { [2] = Palette((1, 33, 32, 31), (2, 36, 35, 34)) },
        new ArtFixtureFrame(2, 1, [1, 2]));

    internal static byte[] CreateSingleFrameStaticArt() => CreateArt(
        staticFlag: true,
        palettes: new Dictionary<int, byte[]> { [0] = Palette((1, 3, 2, 1), (2, 6, 5, 4)) },
        new ArtFixtureFrame(2, 1, [1, 2]));

    private static byte[] CreateUnsupportedArt()
    {
        var art = CreateStaticArt();
        WriteI32(art, 8, 7);
        return art;
    }

    private static byte[] CreateDirectionalArt()
    {
        var frames = new List<ArtFixtureFrame>();
        for (var group = 0; group < 8; group++)
        {
            // The first frame has a fixture-authored colour marker at its raw-hotspot
            // anchor. The second is deliberately unequal and has a negative hotspot.
            frames.Add(new ArtFixtureFrame(3, 3,
                [1, 2, 3, 2, 4, 3, 1, 2, 3], HotX: 1, HotY: 1, OffsetX: group - 4, OffsetY: 4 - group));
            frames.Add(new ArtFixtureFrame(4, 2,
                [4, 2, 3, 1, 2, 3, 1, 2], HotX: -1, HotY: 0, OffsetX: -group, OffsetY: group));
        }

        return CreateArt(staticFlag: false, new Dictionary<int, byte[]>
        {
            [0] = Palette((1, 3, 2, 1), (2, 6, 5, 4), (3, 9, 8, 7), (4, 12, 11, 10)),
            [1] = Palette((1, 63, 62, 61), (2, 66, 65, 64), (3, 69, 68, 67), (4, 72, 71, 70)),
        }, frames.ToArray());
    }

    private static byte[] CreateArt(bool staticFlag, IReadOnlyDictionary<int, byte[]> palettes, params ArtFixtureFrame[] frames)
    {
        var groups = staticFlag ? 1 : 8;
        if (frames.Length == 0 || frames.Length % groups != 0)
        {
            throw new ArgumentException("ART fixture frames must fill every physical group.", nameof(frames));
        }

        var framesPerGroup = frames.Length / groups;
        var paletteBytes = palettes.Count * PaletteBytes;
        var payloadBytes = frames.Sum(frame => frame.Payload.Length);
        var tableOffset = ArtHeaderBytes + paletteBytes;
        var payloadOffset = tableOffset + (frames.Length * FrameHeaderBytes);
        var art = new byte[payloadOffset + payloadBytes];
        WriteI32(art, 0, staticFlag ? 1 : 0);
        WriteI32(art, 4, 12);
        WriteI32(art, 8, 8);
        WriteI32(art, 32, framesPerGroup);

        var paletteCursor = ArtHeaderBytes;
        for (var slot = 0; slot <= ViewerState.MaxRequestedPaletteSlot; slot++)
        {
            if (!palettes.TryGetValue(slot, out var palette))
            {
                continue;
            }

            if (palette.Length != PaletteBytes)
            {
                throw new ArgumentException("ART fixture palettes must contain 1,024 bytes.", nameof(palettes));
            }

            WriteI32(art, 12 + (slot * 4), slot + 1);
            palette.CopyTo(art, paletteCursor);
            paletteCursor += PaletteBytes;
        }

        var payloadCursor = payloadOffset;
        for (var index = 0; index < frames.Length; index++)
        {
            var frame = frames[index];
            var frameOffset = tableOffset + (index * FrameHeaderBytes);
            WriteI32(art, frameOffset, frame.Width);
            WriteI32(art, frameOffset + 4, frame.Height);
            WriteI32(art, frameOffset + 8, frame.Payload.Length);
            WriteI32(art, frameOffset + 12, frame.HotX);
            WriteI32(art, frameOffset + 16, frame.HotY);
            WriteI32(art, frameOffset + 20, frame.OffsetX);
            WriteI32(art, frameOffset + 24, frame.OffsetY);
            frame.Payload.CopyTo(art, payloadCursor);
            payloadCursor += frame.Payload.Length;
        }

        return art;
    }

    private static byte[] CreateDat(params DatFixtureEntry[] entries)
    {
        var storedEntries = entries.Select(entry => new StoredDatFixtureEntry(entry, entry.Zlib ? Zlib(entry.Payload) : entry.Payload)).ToArray();
        using var output = new MemoryStream();
        var offsets = new uint[entries.Length];
        for (var index = 0; index < entries.Length; index++)
        {
            offsets[index] = checked((uint)output.Position);
            output.Write(storedEntries[index].StoredPayload);
        }

        using var indexStream = new MemoryStream();
        WriteU32(indexStream, checked((uint)entries.Length));
        foreach (var (entry, ordinal) in entries.Select((entry, ordinal) => (entry, ordinal)))
        {
            var name = Encoding.ASCII.GetBytes(entry.Name + "\0");
            WriteU32(indexStream, checked((uint)name.Length));
            indexStream.Write(name);
            indexStream.Write(new byte[4]);
            WriteU32(indexStream, entry.Zlib ? 2u : 1u);
            WriteU32(indexStream, checked((uint)entry.Payload.Length));
            WriteU32(indexStream, checked((uint)storedEntries[ordinal].StoredPayload.Length));
            WriteU32(indexStream, offsets[ordinal]);
        }

        indexStream.Position = 0;
        indexStream.CopyTo(output);
        output.Write(new byte[16]);
        output.Write("1TAD"u8);
        WriteU32(output, checked((uint)entries.Sum(entry => entry.Name.Length + 1)));
        WriteU32(output, checked((uint)(indexStream.Length + 28)));
        return output.ToArray();
    }

    private static byte[] Zlib(byte[] payload)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(payload);
        }

        return output.ToArray();
    }

    private static byte[] Palette(params (int Index, byte Blue, byte Green, byte Red)[] entries)
    {
        var palette = new byte[PaletteBytes];
        foreach (var entry in entries)
        {
            var offset = entry.Index * 4;
            palette[offset] = entry.Blue;
            palette[offset + 1] = entry.Green;
            palette[offset + 2] = entry.Red;
        }

        return palette;
    }

    private static void WriteI32(byte[] bytes, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, sizeof(int)), value);

    private static void WriteU32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private sealed record ArtFixtureFrame(int Width, int Height, byte[] Payload, int HotX = 0, int HotY = 0, int OffsetX = 0, int OffsetY = 0);
    private sealed record DatFixtureEntry(string Name, byte[] Payload, bool Zlib = false);
    private sealed record StoredDatFixtureEntry(DatFixtureEntry Entry, byte[] StoredPayload);
}

internal static class GodotSelfTest
{
    internal static string Run()
    {
        var dat = SyntheticFixture.CreateDat();
        using var archive = DatArchive.Open(new MemoryStream(dat), ViewerState.ReaderOptions, leaveOpen: true);
        var entries = ViewerState.ArtEntries(archive);
        Require(entries.Count == 7 && archive.Entries.Count == 8, "synthetic DAT/ART entry count");
        Require(entries[0].DisplayName == "duplicate.art [ordinal 0]" && entries[1].DisplayName == "duplicate.art [ordinal 1]", "duplicate ordinal identity");

        var raw = ArtDecoder.Decode(archive.ReadEntry(entries[0].Entry), new ArtFrameSelection(0, 0, 2), ViewerState.DecoderOptions);
        var inspected = ArtDecoder.Inspect(archive.ReadEntry(entries[1].Entry), ViewerState.DecoderOptions);
        var expected = new byte[]
        {
            0, 0, 0, 0, 31, 32, 33, 255, 34, 35, 36, 255,
            37, 38, 39, 255, 40, 41, 42, 255, 43, 44, 45, 255,
        };
        Require(raw.Width == 3 && raw.Height == 2 && raw.Frame.EncodedPayloadLength == 6, "raw frame metadata");
        Require(inspected.Header.FramesPerSecond == 12 && inspected.Frames.Count == 16 && inspected.GetFrame(7, 1).Width == 4, "read-only animation metadata inspection");
        Require(raw.Rgba.SequenceEqual(expected), "decoder raw RGBA byte order or orientation");

        var rle = ArtDecoder.Decode(archive.ReadEntry(entries[0].Entry), new ArtFrameSelection(0, 1, 2), ViewerState.DecoderOptions);
        var directional = ArtDecoder.Decode(archive.ReadEntry(entries[1].Entry), new ArtFrameSelection(7, 1, 1), ViewerState.DecoderOptions);
        Require(rle.Width == 2 && rle.Height == 2 && rle.Frame.EncodedPayloadLength == 5, "RLE frame selection");
        Require(directional.GroupIndex == 7 && directional.FrameIndex == 1 && directional.Header.PhysicalGroupCount == 8, "physical group selection");
        var anchoredDirectional = ArtDecoder.Decode(archive.ReadEntry(entries[1].Entry), new ArtFrameSelection(0, 0, 0), ViewerState.DecoderOptions);
        Require(anchoredDirectional.Width == 3 && anchoredDirectional.Height == 3 && anchoredDirectional.Frame.HotX == 1 && anchoredDirectional.Frame.HotY == 1 &&
                directional.Width == 4 && directional.Height == 2 && directional.Frame.HotX == -1 && directional.Frame.HotY == 0,
            "unequal positive and negative synthetic hotspots");
        Require(anchoredDirectional.Rgba.Skip(checked(((1 * anchoredDirectional.Width) + 1) * 4)).Take(4).SequenceEqual(new byte[] { 10, 11, 12, 255 }),
            "fixture-authored common-anchor colour marker");

        RequireThrows<ArtFormatException>(() => ArtDecoder.Decode(archive.ReadEntry(entries[3].Entry), new ArtFrameSelection(0, 0, 0), ViewerState.DecoderOptions), "missing slot zero");
        var groupSelector = new SpinBox();
        var frameSelector = new SpinBox();
        var paletteSelector = new SpinBox();
        ViewerState.ResetEntrySelectors(groupSelector, frameSelector, paletteSelector);
        Require(groupSelector.MaxValue == 0 && frameSelector.MaxValue == 0 && paletteSelector.MaxValue == 0, "no-selection selectors are neutral");
        ViewerState.ConfigureEntrySelectors(groupSelector, frameSelector, paletteSelector,
            ArtDecoder.Inspect(archive.ReadEntry(entries[3].Entry), ViewerState.DecoderOptions));
        Require(paletteSelector.MaxValue == ViewerState.MaxRequestedPaletteSlot, "viewer palette zero absent selector recovery");
        groupSelector.Free();
        frameSelector.Free();
        paletteSelector.Free();
        var recovered = ArtDecoder.Decode(archive.ReadEntry(entries[3].Entry), new ArtFrameSelection(0, 0, 2), ViewerState.DecoderOptions);
        Require(recovered.EffectivePaletteSlot == 2 && ViewerState.MaxRequestedPaletteSlot == 3, "palette zero absent recovery slots 0-3");
        RequireThrows<ArtFormatException>(() => ArtDecoder.Decode(archive.ReadEntry(entries[4].Entry), new ArtFrameSelection(0, 0, 0), ViewerState.DecoderOptions), "malformed synthetic ART");
        Require(entries[5].Entry.Flags == 2 && ArtDecoder.Decode(archive.ReadEntry(entries[5].Entry), new ArtFrameSelection(0, 0, 2), ViewerState.DecoderOptions).Rgba.SequenceEqual(expected), "zlib DAT payload");
        RequireThrows<ArtUnsupportedFormatException>(() => ArtDecoder.Decode(archive.ReadEntry(entries[6].Entry), new ArtFrameSelection(0, 0, 0), ViewerState.DecoderOptions), "unsupported synthetic ART");
        Require(ArtDecoder.Decode(archive.ReadEntry(entries[0].Entry), new ArtFrameSelection(0, 0, 2), ViewerState.DecoderOptions).Rgba.SequenceEqual(expected), "valid-invalid-valid recovery");

        using var oversize = DatArchive.Open(new MemoryStream(SyntheticFixture.CreateOversizeDat()), ViewerState.ReaderOptions, leaveOpen: true);
        RequireThrows<DatLimitExceededException>(() => oversize.ReadEntry(oversize.Entries[0]), "oversize viewer entry limit");

        VerifyViewerSessionPublicationBoundary(archive, entries);

        using var image = Image.CreateFromData(raw.Width, raw.Height, false, Image.Format.Rgba8, raw.Rgba.ToArray());
        using var texture = ViewerApp.CreateFrameTexture(raw);
        using var textureImage = texture.GetImage();
        Require(image.GetData().SequenceEqual(expected) && textureImage.GetData().SequenceEqual(expected), "Godot Image/ImageTexture bytes");
        Require(texture.GetSize() == new Vector2I(3, 2), "texture aspect");
        var topLeft = textureImage.GetPixel(0, 0);
        var topRight = textureImage.GetPixel(2, 0);
        var bottomLeft = textureImage.GetPixel(0, 1);
        Require(Mathf.IsZeroApprox(topLeft.A) && Mathf.IsEqualApprox(topRight.R, 34f / 255f) && Mathf.IsEqualApprox(bottomLeft.R, 37f / 255f), "top-left row-major orientation");

        var canvas = new FrameCanvas { CustomMinimumSize = Vector2.Zero, Size = new Vector2(90, 50) };
        Require(canvas.TextureFilter == Control.TextureFilterEnum.Nearest && !canvas.HasTexture && Mathf.IsEqualApprox(canvas.Zoom, 1f), "FrameCanvas nearest initial state");
        var layout = new FrameCanvasGroupLayout([
            new FrameCanvasFrame(3, 2, 2, 1),
            new FrameCanvasFrame(4, 5, -3, 4),
        ]);
        Require(layout.RawHotspotBounds == new FrameCanvasBounds(-2, -4, 7, 1), "FrameCanvas raw signed-hotspot group bounds");
        canvas.ResetAsset(texture, new FrameHotspot(2, 1), layout);
        Require(canvas.HasAsset && canvas.Alignment == FrameAlignment.RawHotspot && canvas.AnchorMarkerVisible && canvas.GetFrameTopLeftWorld() == new Vector2(-2, -1), "FrameCanvas raw hotspot placement");
        Require(canvas.GetScreenPositionForWorld(Vector2.Zero) == new Vector2(45, 25) && canvas.GetScreenPositionForWorld(canvas.GetFrameTopLeftWorld()) == new Vector2(43, 24), "FrameCanvas common anchor transform");
        canvas.AdjustZoom(2f, new Vector2(45, 25));
        var manualPan = canvas.Pan;
        Require(canvas.HasTexture && Mathf.IsEqualApprox(canvas.Zoom, 2f) && !canvas.FitMode, "FrameCanvas zoom state");
        canvas.UpdateFrame(texture, new FrameHotspot(-3, 4));
        Require(canvas.Pan == manualPan && Mathf.IsEqualApprox(canvas.Zoom, 2f) && canvas.GetFrameTopLeftWorld() == new Vector2(3, -4), "FrameCanvas frame update preserves manual viewport");
        canvas.SetFit();
        Require(canvas.FitMode && Mathf.IsEqualApprox(canvas.Zoom, 10f) && canvas.Pan == new Vector2(-25, 15), "FrameCanvas stable raw group fit");
        canvas.UpdateFrame(texture, new FrameHotspot(2, 1));
        Require(canvas.FitMode && Mathf.IsEqualApprox(canvas.Zoom, 10f) && canvas.Pan == new Vector2(-25, 15), "FrameCanvas frame update preserves fitted viewport");
        var replacementLayout = new FrameCanvasGroupLayout([new FrameCanvasFrame(10, 2, 0, 0)]);
        canvas.UpdateGroupLayout(replacementLayout);
        Require(canvas.FitMode && Mathf.IsEqualApprox(canvas.Zoom, 9f) && canvas.Pan == new Vector2(-45, -9), "FrameCanvas group update refits once against stable bounds");
        canvas.Size = new Vector2(180, 100);
        canvas.SetFit();
        Require(canvas.FitMode && Mathf.IsEqualApprox(canvas.Zoom, 18f) && canvas.Pan == new Vector2(-90, -18), "FrameCanvas fit recomputes for resized stable bounds");
        canvas.ResetOneToOne();
        canvas.AdjustZoom(2f, new Vector2(30, 40));
        manualPan = canvas.Pan;
        canvas.UpdateGroupLayout(layout);
        Require(!canvas.FitMode && Mathf.IsEqualApprox(canvas.Zoom, 2f) && canvas.Pan == manualPan, "FrameCanvas group update preserves manual viewport");
        canvas.SetAlignment(FrameAlignment.Centered);
        Require(canvas.GetFrameTopLeftWorld() == new Vector2(-1.5f, -1f), "FrameCanvas centered inspection mode");
        using var centeredImage = Image.CreateFromData(3, 5, false, Image.Format.Rgba8, new byte[3 * 5 * 4]);
        using var centeredTexture = ImageTexture.CreateFromImage(centeredImage);
        var centeredLayout = new FrameCanvasGroupLayout([new FrameCanvasFrame(3, 5, FrameHotspot.Zero)]);
        Require(centeredLayout.CenteredBounds == new FrameCanvasWorldBounds(-3, -5, 3, 5), "FrameCanvas exact centered odd-size bounds");
        canvas.Size = new Vector2(90, 50);
        canvas.ResetAsset(centeredTexture, FrameHotspot.Zero, centeredLayout);
        canvas.SetFit();
        var centeredTopLeft = canvas.GetScreenPositionForWorld(canvas.GetFrameTopLeftWorld());
        var centeredBottomRight = centeredTopLeft + (centeredTexture.GetSize() * canvas.Zoom);
        Require(centeredTopLeft == new Vector2(30, 0) && centeredBottomRight == new Vector2(60, 50) &&
                (centeredTopLeft + centeredBottomRight) / 2f == new Vector2(45, 25),
            "FrameCanvas centered odd-size fit matches rendered extents and canvas center");
        var largeLayout = new FrameCanvasGroupLayout([
            new FrameCanvasFrame(1, 1, 0, 0),
            new FrameCanvasFrame(1, 1, -999_999, 0),
        ]);
        canvas.Size = new Vector2(320, 200);
        canvas.ResetAsset(texture, FrameHotspot.Zero, largeLayout);
        canvas.SetAlignment(FrameAlignment.RawHotspot);
        canvas.SetFit();
        Require(canvas.FitMode && Mathf.IsEqualApprox(canvas.Zoom, 0.00032f), "FrameCanvas large legal group fit has no arbitrary zoom floor");
        var fittedZoom = canvas.Zoom;
        var fittedPan = canvas.Pan;
        canvas.SetAnchorMarkerVisible(false);
        Require(!canvas.AnchorMarkerVisible, "FrameCanvas anchor marker toggle");
        Require(Mathf.IsEqualApprox(FrameCanvas.CalculateFitZoom(new Vector2(30, 20), new FrameCanvasBounds(-2, -4, 7, 1)) ?? 0f, 10f / 3f), "FrameCanvas bounds fit calculation");
        canvas.ClearFrame();
        Require(!canvas.HasTexture && canvas.HasAsset && canvas.FitMode && Mathf.IsEqualApprox(canvas.Zoom, fittedZoom) && canvas.Pan == fittedPan, "FrameCanvas clear-frame preserves viewport transform");
        canvas.ClearAsset();
        Require(!canvas.HasTexture && !canvas.HasAsset && !canvas.FitMode && Mathf.IsEqualApprox(canvas.Zoom, 1f), "FrameCanvas clear-asset reset state");
        RequireThrows<FrameCanvasGeometryLimitException>(() => new FrameCanvasGroupLayout([new FrameCanvasFrame(1, 1, int.MinValue, 0)]), "FrameCanvas geometry precision limit");
        canvas.Free();

        return "Godot synthetic DAT/ART self-test passed: duplicate ordinals, raw/RLE, unequal signed hotspots and common-anchor marker, queue stale suppression/recovery, palettes/fallback, limits, RGBA8 texture, and FrameCanvas raw signed-hotspot geometry.";
    }


    private static void VerifyViewerSessionPublicationBoundary(DatArchive archive, IReadOnlyList<ViewerEntry> entries)
    {
        var animatedEntry = entries[1].Entry;
        var animatedBytes = archive.ReadEntry(animatedEntry);
        var animatedMetadata = ArtDecoder.Inspect(animatedBytes, ViewerState.DecoderOptions);
        VerifySupersededCompletion(animatedEntry, animatedBytes, animatedMetadata, false);
        VerifySupersededCompletion(animatedEntry, animatedBytes, animatedMetadata, true);
        VerifyManualGroupPaletteAndStop(animatedEntry, animatedBytes, animatedMetadata);
        VerifyActiveLoadReplacementAndClose(entries[0].Entry, entries[1].Entry, animatedBytes, animatedMetadata);
        VerifyRealArchiveReplacementAndClose(entries[1].Entry);
        VerifyArchiveFailureRecovery(animatedEntry, animatedBytes, animatedMetadata);

        var staticEntry = entries[0].Entry;
        var staticBytes = SyntheticFixture.CreateSingleFrameStaticArt();
        var staticMetadata = ArtDecoder.Inspect(staticBytes, ViewerState.DecoderOptions);
        using var coordinator = new ViewerWorkCoordinator(work => work switch
        {
            LoadEntryViewerWorkRequest load => new ViewerEntryLoaded(load, staticBytes, staticMetadata),
            DecodeFrameViewerWorkRequest decode => new ViewerFrameDecoded(decode, ArtDecoder.Decode(staticBytes,
                new ArtFrameSelection(decode.Target.Group, decode.Target.Frame, decode.Target.Palette), ViewerState.DecoderOptions)),
            _ => throw new InvalidOperationException("Unexpected static integration work."),
        });
        using var session = new ViewerSession(coordinator);
        using var sink = new RecordingPublicationSink();
        session.SelectEntry(staticEntry);
        WaitForCoordinator(coordinator, 1);
        session.DrainCompletions(sink);
        WaitForCoordinator(coordinator, 2);
        session.DrainCompletions(sink);
        var uploads = sink.PublishedFrames;
        session.Timeline.Play();
        session.Advance(10, 0, 0);
        session.DrainCompletions(sink);
        Require(sink.PublishedFrames == uploads && !session.Timeline.IsPlaying,
            "static and paused ticks submit zero additional uploads");

        VerifyPausedAnimatedZeroWork(animatedEntry, animatedBytes, animatedMetadata);

        VerifyCurrentPublicationFailure(animatedEntry, animatedBytes, animatedMetadata);
        VerifySelectionFailureRecovery(entries[0].Entry, entries[4].Entry, staticBytes, staticMetadata);
    }

    private static void VerifySupersededCompletion(DatEntry entry, byte[] bytes, ArtMetadata metadata, bool failFirstDecode)
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var decodeCalls = 0;
        using var coordinator = new ViewerWorkCoordinator(work => work switch
        {
            LoadEntryViewerWorkRequest load => new ViewerEntryLoaded(load, bytes, metadata),
            DecodeFrameViewerWorkRequest decode => ExecuteControlledDecode(decode, bytes, ref decodeCalls, started, release, failFirstDecode),
            _ => throw new InvalidOperationException("Unexpected animated integration work."),
        });
        using var session = new ViewerSession(coordinator);
        using var sink = new RecordingPublicationSink();
        session.SelectEntry(entry);
        WaitForCoordinator(coordinator, 1);
        session.DrainCompletions(sink);
        Require(started.Wait(TimeSpan.FromSeconds(2)), "production coordinator starts active decode");
        session.Timeline.Play();
        session.Advance(1d / metadata.Header.FramesPerSecond, 0, 0);
        session.Advance(1d / metadata.Header.FramesPerSecond, 0, 0);
        release.Set();
        WaitForCoordinator(coordinator, 3);
        session.DrainCompletions(sink);
        Require(sink.EntryCommits == 1 && sink.PublishedFrames == 1 && sink.Failures == 0 && sink.AllCallbacksOnDrainThread &&
                sink.PublishedTargets.Single() == session.DesiredTarget && sink.CreatedResources == 1 && sink.DisposedResources == 0 &&
                sink.HasCommittedCanvas && sink.AssetResets == 1 && sink.MetadataUpdates == 1 && sink.StatusUpdates >= 2,
            failFirstDecode ? "stale active decode error has no status or pause side effect" : "stale active decode creates no resources or status");
        Require(!failFirstDecode || session.Timeline.IsPlaying,
            "stale active decode error leaves the newer playback target running");
        Require(coordinator.MaximumObservedConcurrency == 1 && coordinator.ActiveOperations == 0 && coordinator.PendingCount == 0,
            "single read inspect decode coordinator concurrency bound");
    }

    private static void VerifyManualGroupPaletteAndStop(DatEntry entry, byte[] bytes, ArtMetadata metadata)
    {
        VerifyManualPublication(entry, bytes, metadata,
            session => { session.Timeline.Seek(1); session.RequestTarget(0, 0); }, 0, 1, 0,
            FrameCanvasPublicationEffect.UpdateFrame, "manual frame");
        VerifyManualPublication(entry, bytes, metadata,
            session => session.RequestTarget(1, 0), 1, 0, 0,
            FrameCanvasPublicationEffect.UpdateGroupLayoutAndFrame, "manual group");
        VerifyManualPublication(entry, bytes, metadata,
            session => session.RequestTarget(0, 1), 0, 0, 1,
            FrameCanvasPublicationEffect.UpdateFrame, "manual palette");
        VerifyStopPublication(entry, bytes, metadata);
    }

    private static void VerifyStopPublication(DatEntry entry, byte[] bytes, ArtMetadata metadata)
    {
        using var coordinator = new ViewerWorkCoordinator(work => work switch
        {
            LoadEntryViewerWorkRequest load => new ViewerEntryLoaded(load, bytes, metadata),
            DecodeFrameViewerWorkRequest decode => new ViewerFrameDecoded(decode, ArtDecoder.Decode(bytes,
                new ArtFrameSelection(decode.Target.Group, decode.Target.Frame, decode.Target.Palette), ViewerState.DecoderOptions)),
            _ => throw new InvalidOperationException("Unexpected Stop publication work."),
        });
        using var session = new ViewerSession(coordinator);
        using var sink = new RecordingPublicationSink();
        session.SelectEntry(entry, sink);
        WaitForCoordinator(coordinator, 1);
        session.DrainCompletions(sink);
        WaitForCoordinator(coordinator, 2);
        session.DrainCompletions(sink);
        session.Timeline.Seek(1);
        session.RequestTarget(0, 0);
        WaitForCoordinator(coordinator, 3);
        session.DrainCompletions(sink);
        session.Stop(0, 0);
        WaitForCoordinator(coordinator, 4);
        session.DrainCompletions(sink);
        var stopped = sink.PublishedTargets.Last();
        Require(stopped == session.DesiredTarget && stopped.Group == 0 && stopped.Frame == 0 && stopped.Palette == 0 &&
                sink.PublishedFrames == 3 && sink.AssetResets == 1 && sink.FrameUpdates == 2 && sink.HasCommittedCanvas &&
                coordinator.MaximumObservedConcurrency == 1,
            "Stop publishes paused frame zero through the production canvas transaction");
    }

    private static void VerifyPausedAnimatedZeroWork(DatEntry entry, byte[] bytes, ArtMetadata metadata)
    {
        using var coordinator = new ViewerWorkCoordinator(work => work switch
        {
            LoadEntryViewerWorkRequest load => new ViewerEntryLoaded(load, bytes, metadata),
            DecodeFrameViewerWorkRequest decode => new ViewerFrameDecoded(decode, ArtDecoder.Decode(bytes,
                new ArtFrameSelection(decode.Target.Group, decode.Target.Frame, decode.Target.Palette), ViewerState.DecoderOptions)),
            _ => throw new InvalidOperationException("Unexpected paused animated integration work."),
        });
        using var session = new ViewerSession(coordinator);
        using var sink = new RecordingPublicationSink();
        session.SelectEntry(entry, sink);
        WaitForCoordinator(coordinator, 1);
        session.DrainCompletions(sink);
        WaitForCoordinator(coordinator, 2);
        session.DrainCompletions(sink);
        Require(session.Timeline.FrameCount > 1 && !session.Timeline.IsPlaying, "animated selection begins paused");
        var submitted = coordinator.SubmittedOperations;
        var completed = coordinator.CompletedOperations;
        var resources = sink.CreatedResources;
        var uploads = sink.PublishedFrames;
        for (var index = 0; index < 8; index++)
        {
            session.Advance(1, 0, 0);
            session.DrainCompletions(sink);
        }

        Require(coordinator.SubmittedOperations == submitted && coordinator.CompletedOperations == completed &&
                sink.CreatedResources == resources && sink.PublishedFrames == uploads,
            "paused multi-frame ticks submit no request, completion, resource, upload, or publication");
    }

    private static void VerifyManualPublication(DatEntry entry, byte[] bytes, ArtMetadata metadata,
        Action<ViewerSession> changeTarget, int group, int frame, int palette, FrameCanvasPublicationEffect expectedEffect, string name)
    {
        using var coordinator = new ViewerWorkCoordinator(work => work switch
        {
            LoadEntryViewerWorkRequest load => new ViewerEntryLoaded(load, bytes, metadata),
            DecodeFrameViewerWorkRequest decode => new ViewerFrameDecoded(decode, ArtDecoder.Decode(bytes,
                new ArtFrameSelection(decode.Target.Group, decode.Target.Frame, decode.Target.Palette), ViewerState.DecoderOptions)),
            _ => throw new InvalidOperationException("Unexpected manual publication work."),
        });
        using var session = new ViewerSession(coordinator);
        using var sink = new RecordingPublicationSink();
        session.SelectEntry(entry, sink);
        WaitForCoordinator(coordinator, 1);
        session.DrainCompletions(sink);
        WaitForCoordinator(coordinator, 2);
        session.DrainCompletions(sink);
        changeTarget(session);
        WaitForCoordinator(coordinator, 3);
        session.DrainCompletions(sink);
        var published = sink.PublishedTargets.Last();
        Require(published == session.DesiredTarget && published.Group == group && published.Frame == frame && published.Palette == palette,
            $"{name} publishes its exact target");
        Require(sink.EntryCommits == 1 && sink.PublishedFrames == 2 && sink.AssetResets == 1,
            $"{name} commits only its initial asset and latest frame");
        Require(sink.PublicationEffects.Last() == expectedEffect && sink.HasCommittedCanvas && sink.MetadataUpdates == 2 &&
                coordinator.MaximumObservedConcurrency == 1,
            $"{name} updates the expected production canvas effect");
    }

    private static void VerifyActiveLoadReplacementAndClose(DatEntry first, DatEntry replacement, byte[] bytes, ArtMetadata metadata)
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var coordinator = new ViewerWorkCoordinator(work => work switch
        {
            LoadEntryViewerWorkRequest load => ExecuteControlledLoad(load, bytes, metadata, started, release),
            _ => throw new InvalidOperationException("Unexpected active-load integration work."),
        });
        using var session = new ViewerSession(coordinator);
        using var sink = new RecordingPublicationSink();
        session.SelectEntry(first, sink);
        Require(started.Wait(TimeSpan.FromSeconds(2)), "production coordinator starts active Read Inspect");
        session.SelectEntry(replacement, sink);
        session.Close();
        release.Set();
        WaitForCoordinator(coordinator, 1);
        session.DrainCompletions(sink);
        Require(sink.EntryCommits == 0 && sink.PublishedFrames == 0 && coordinator.MaximumObservedConcurrency == 1 &&
                coordinator.ActiveOperations == 0 && coordinator.PendingCount == 0 && sink.SelectionInvalidations == 2,
            "entry replacement and close suppress active Read Inspect result with serialized cleanup");
    }

    private static void VerifyRealArchiveReplacementAndClose(DatEntry entry)
    {
        VerifyRealArchiveReplacement(entry);
        VerifyRealArchiveClose(entry);
    }

    private static void VerifyRealArchiveReplacement(DatEntry entry)
    {
        var path = Path.Combine(OS.GetUserDataDir(), $"arcanum-viewer-{Guid.NewGuid():N}.dat");
        File.WriteAllBytes(path, SyntheticFixture.CreateDat());
        try
        {
            using var started = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            using var coordinator = new ViewerWorkCoordinator(beforeTestExecution: work =>
            {
                if (work is LoadEntryViewerWorkRequest)
                {
                    started.Set();
                    if (!release.Wait(TimeSpan.FromSeconds(2))) throw new TimeoutException("Synthetic real archive Read Inspect barrier timed out.");
                }
            });
            using var session = new ViewerSession(coordinator);
            using var sink = new RecordingPublicationSink();
            session.OpenArchive(path, sink);
            WaitForCoordinator(coordinator, 1);
            session.DrainCompletions(sink);
            session.SelectEntry(entry, sink);
            Require(started.Wait(TimeSpan.FromSeconds(2)) && coordinator.ArchiveDisposals == 0,
                "active real DAT Read Inspect retains its archive");
            session.OpenArchive(path, sink);
            release.Set();
            WaitForCoordinator(coordinator, 3);
            session.DrainCompletions(sink);
            session.SelectEntry(sink.LastArchiveEntries.Single(candidate => candidate.Entry.Ordinal == entry.Ordinal).Entry, sink);
            WaitForCoordinator(coordinator, 4);
            session.DrainCompletions(sink);
            Require(session.Selected is not null && session.DesiredTarget is not null,
                "replacement archive selection commits a usable target before decode");
            WaitForCoordinator(coordinator, 5);
            session.DrainCompletions(sink);
            Require(sink.EntryCommits == 1 && sink.PublishedFrames == 1 && coordinator.MaximumObservedConcurrency == 1 &&
                    coordinator.ArchiveDisposals == 1 && coordinator.ActiveOperations == 0 && coordinator.PendingCount == 0,
                "real DAT replacement disposes A after active Read Inspect and B remains usable");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void VerifyRealArchiveClose(DatEntry entry)
    {
        var path = Path.Combine(OS.GetUserDataDir(), $"arcanum-viewer-{Guid.NewGuid():N}.dat");
        File.WriteAllBytes(path, SyntheticFixture.CreateDat());
        try
        {
            using var started = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            using var coordinator = new ViewerWorkCoordinator(beforeTestExecution: work =>
            {
                if (work is LoadEntryViewerWorkRequest)
                {
                    started.Set();
                    if (!release.Wait(TimeSpan.FromSeconds(2))) throw new TimeoutException("Synthetic real archive close barrier timed out.");
                }
            });
            using var session = new ViewerSession(coordinator);
            using var sink = new RecordingPublicationSink();
            session.OpenArchive(path, sink);
            WaitForCoordinator(coordinator, 1);
            session.DrainCompletions(sink);
            session.SelectEntry(entry, sink);
            Require(started.Wait(TimeSpan.FromSeconds(2)) && coordinator.ArchiveDisposals == 0,
                "active real DAT Read Inspect retains archive before close");
            session.Close();
            release.Set();
            WaitForCoordinator(coordinator, 2);
            session.DrainCompletions(sink);
            Require(sink.EntryCommits == 0 && sink.PublishedFrames == 0 && coordinator.MaximumObservedConcurrency == 1 &&
                    coordinator.ArchiveDisposals == 1 && coordinator.ActiveOperations == 0 && coordinator.PendingCount == 0,
                "real DAT close defers archive disposal until active Read Inspect completes");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void VerifyArchiveFailureRecovery(DatEntry entry, byte[] bytes, ArtMetadata metadata)
    {
        var opens = 0;
        using var coordinator = new ViewerWorkCoordinator(work => work switch
        {
            OpenArchiveViewerWorkRequest open when Interlocked.Increment(ref opens) == 2 =>
                throw new DatException("Synthetic invalid DAT."),
            OpenArchiveViewerWorkRequest open => new ViewerArchiveOpened(open, Array.Empty<ViewerEntry>()),
            LoadEntryViewerWorkRequest load => new ViewerEntryLoaded(load, bytes, metadata),
            DecodeFrameViewerWorkRequest decode => new ViewerFrameDecoded(decode, ArtDecoder.Decode(bytes,
                new ArtFrameSelection(decode.Target.Group, decode.Target.Frame, decode.Target.Palette), ViewerState.DecoderOptions)),
            _ => throw new InvalidOperationException("Unexpected archive-recovery integration work."),
        });
        using var session = new ViewerSession(coordinator);
        using var sink = new RecordingPublicationSink();
        session.OpenArchive("valid.dat", sink);
        WaitForCoordinator(coordinator, 1);
        session.DrainCompletions(sink);
        session.SelectEntry(entry, sink);
        WaitForCoordinator(coordinator, 2);
        session.DrainCompletions(sink);
        WaitForCoordinator(coordinator, 3);
        session.DrainCompletions(sink);
        session.OpenArchive("invalid.dat", sink);
        WaitForCoordinator(coordinator, 4);
        session.DrainCompletions(sink);
        Require(sink.Failures == 1 && session.Selected is null && session.Timeline.FrameCount == 1 && !session.Timeline.CanPlay &&
                sink.ClearedEntryLists >= 3 && sink.HasNeutralSelection && !sink.HasCommittedCanvas,
            "invalid DAT clears committed metadata, canvas, selectors and disabled timeline state");
        session.OpenArchive("recovered.dat", sink);
        WaitForCoordinator(coordinator, 5);
        session.DrainCompletions(sink);
        session.SelectEntry(entry, sink);
        WaitForCoordinator(coordinator, 6);
        session.DrainCompletions(sink);
        WaitForCoordinator(coordinator, 7);
        session.DrainCompletions(sink);
        Require(sink.Failures == 1 && sink.ClearedEntryLists >= 4 && sink.EntryCommits == 2 && sink.PublishedFrames == 2 &&
                sink.HasValidSelection && sink.HasCommittedCanvas && sink.AllCallbacksOnDrainThread,
            "valid invalid valid DAT recovery republishes only the valid replacement asset on the main thread");
    }

    private static void VerifyCurrentPublicationFailure(DatEntry entry, byte[] bytes, ArtMetadata metadata)
    {
        using var coordinator = new ViewerWorkCoordinator(work => work switch
        {
            LoadEntryViewerWorkRequest load => new ViewerEntryLoaded(load, bytes, metadata),
            DecodeFrameViewerWorkRequest decode => new ViewerFrameDecoded(decode, ArtDecoder.Decode(bytes,
                new ArtFrameSelection(decode.Target.Group, decode.Target.Frame, decode.Target.Palette), ViewerState.DecoderOptions)),
            _ => throw new InvalidOperationException("Unexpected publication-failure integration work."),
        });
        using var session = new ViewerSession(coordinator);
        using var sink = new RecordingPublicationSink(failOnPublicationAttempt: 1);
        session.SelectEntry(entry, sink);
        WaitForCoordinator(coordinator, 1);
        session.DrainCompletions(sink);
        WaitForCoordinator(coordinator, 2);
        session.DrainCompletions(sink);
        session.Timeline.Play();
        session.Advance(1d / metadata.Header.FramesPerSecond, 0, 0);
        WaitForCoordinator(coordinator, 3);
        session.DrainCompletions(sink);
        Require(sink.PublishedFrames == 1 && sink.Failures == 1 && sink.CreatedResources == 2 && sink.DisposedResources == 1 &&
                sink.HasRestoredPriorCanvas && sink.AssetResets == 1 && sink.FrameUpdates == 0 && !session.Timeline.IsPlaying,
            "post-canvas current publication failure rolls back to the coherent prior canvas and pauses once");
        session.Advance(1, 0, 0);
        session.DrainCompletions(sink);
        Require(coordinator.CompletedOperations == 3 && sink.Failures == 1,
            "failed current publication has no tick retry storm");
        session.RequestTarget(0, 1);
        WaitForCoordinator(coordinator, 4);
        session.DrainCompletions(sink);
        Require(sink.PublishedFrames == 2 && sink.PublishedTargets.Last().Palette == 1 && sink.CreatedResources == 3 && sink.DisposedResources == 2 &&
                sink.FrameUpdates == 1 && sink.MetadataUpdates == 2 && sink.StatusUpdates >= 4,
            "new exact target recovers after transactional main-thread publication failure");
    }

    private static void VerifySelectionFailureRecovery(DatEntry validEntry, DatEntry invalidEntry, byte[] bytes, ArtMetadata metadata)
    {
        var loads = 0;
        using var coordinator = new ViewerWorkCoordinator(work => work switch
        {
            LoadEntryViewerWorkRequest load when Interlocked.Increment(ref loads) == 2 =>
                throw new ArtFormatException("Synthetic invalid entry."),
            LoadEntryViewerWorkRequest load => new ViewerEntryLoaded(load, bytes, metadata),
            DecodeFrameViewerWorkRequest decode => new ViewerFrameDecoded(decode, ArtDecoder.Decode(bytes,
                new ArtFrameSelection(decode.Target.Group, decode.Target.Frame, decode.Target.Palette), ViewerState.DecoderOptions)),
            _ => throw new InvalidOperationException("Unexpected selection-recovery integration work."),
        });
        using var session = new ViewerSession(coordinator);
        using var sink = new RecordingPublicationSink();
        session.SelectEntry(validEntry, sink);
        WaitForCoordinator(coordinator, 1);
        session.DrainCompletions(sink);
        WaitForCoordinator(coordinator, 2);
        session.DrainCompletions(sink);
        session.SelectEntry(invalidEntry, sink);
        WaitForCoordinator(coordinator, 3);
        session.DrainCompletions(sink);
        Require(sink.Failures == 1 && session.Selected is null && session.Timeline.FrameCount == 1 && !session.Timeline.CanPlay &&
                sink.SelectionInvalidations >= 2 && sink.HasNeutralSelection && !sink.HasCommittedCanvas,
            "invalid entry clears prior metadata, canvas and timeline selection before recovery");
        session.SelectEntry(validEntry, sink);
        WaitForCoordinator(coordinator, 4);
        session.DrainCompletions(sink);
        WaitForCoordinator(coordinator, 5);
        session.DrainCompletions(sink);
        Require(sink.EntryCommits == 2 && sink.PublishedFrames == 2 && sink.HasValidSelection && sink.HasCommittedCanvas && sink.AllCallbacksOnDrainThread,
            "valid invalid valid entry recovery republishes only the replacement asset and metadata");
    }

    private static ViewerWorkCompletion ExecuteControlledDecode(DecodeFrameViewerWorkRequest decode, byte[] bytes, ref int calls,
        ManualResetEventSlim started, ManualResetEventSlim release, bool failFirstDecode)
    {
        if (Interlocked.Increment(ref calls) == 1)
        {
            started.Set();
            if (!release.Wait(TimeSpan.FromSeconds(2))) throw new TimeoutException("Synthetic decode barrier timed out.");
            if (failFirstDecode) throw new ArtFormatException("Synthetic stale decode failure.");
        }
        return new ViewerFrameDecoded(decode, ArtDecoder.Decode(bytes,
            new ArtFrameSelection(decode.Target.Group, decode.Target.Frame, decode.Target.Palette), ViewerState.DecoderOptions));
    }

    private static ViewerWorkCompletion ExecuteControlledLoad(LoadEntryViewerWorkRequest load, byte[] bytes, ArtMetadata metadata,
        ManualResetEventSlim started, ManualResetEventSlim release)
    {
        started.Set();
        if (!release.Wait(TimeSpan.FromSeconds(2))) throw new TimeoutException("Synthetic Read Inspect barrier timed out.");
        return new ViewerEntryLoaded(load, bytes, metadata);
    }

    private static void WaitForCoordinator(ViewerWorkCoordinator coordinator, int completedOperations)
    {
        if (!SpinWait.SpinUntil(() => coordinator.CompletedOperations >= completedOperations && coordinator.ActiveOperations == 0 && coordinator.PendingCount == 0, TimeSpan.FromSeconds(2)))
        {
            throw new InvalidOperationException($"Self-test failed: bounded coordinator completion target {completedOperations}, actual {coordinator.CompletedOperations}, active {coordinator.ActiveOperations}, pending {coordinator.PendingCount}.");
        }
    }

    // This sink intentionally executes the same texture/canvas ownership transaction
    // as ViewerApp. It records production effects rather than treating a callback
    // counter as evidence that a frame was rendered.
    private sealed class RecordingPublicationSink : IViewerPublicationSink, IViewerFramePublicationObserver, IDisposable
    {
        private readonly int _drainThread = System.Environment.CurrentManagedThreadId;
        private readonly int? _failOnPublicationAttempt;
        private readonly FrameCanvas _canvas = new() { CustomMinimumSize = Vector2.Zero, Size = new Vector2(90, 50) };
        private readonly ViewerPublicationState _publication = new();
        private readonly SpinBox _group = new();
        private readonly SpinBox _frame = new();
        private readonly SpinBox _palette = new();
        private readonly Label _metadata = new();
        private readonly Label _rate = new();
        private readonly ViewerSelectionUi _selectionUi;
        private readonly List<FrameTarget> _publishedTargets = [];
        private readonly List<FrameCanvasPublicationEffect> _publicationEffects = [];
        private CanvasSnapshot? _priorCanvas;
        private int _publishAttempts;

        internal RecordingPublicationSink(int? failOnPublicationAttempt = null)
        {
            _failOnPublicationAttempt = failOnPublicationAttempt;
            _selectionUi = new ViewerSelectionUi(_group, _frame, _palette, _metadata, _rate);
            _selectionUi.Clear(new AnimationTimeline(1, 0));
        }

        internal int EntryCommits { get; private set; }
        internal int PublishedFrames { get; private set; }
        internal int Failures { get; private set; }
        internal int SelectionInvalidations { get; private set; }
        internal int ClearedEntryLists { get; private set; }
        internal int CreatedResources { get; private set; }
        internal int DisposedResources { get; private set; }
        internal int MetadataUpdates { get; private set; }
        internal int StatusUpdates { get; private set; }
        internal int AssetResets => _publicationEffects.Count(effect => effect == FrameCanvasPublicationEffect.ResetAsset);
        internal int FrameUpdates => _publicationEffects.Count(effect => effect == FrameCanvasPublicationEffect.UpdateFrame);
        internal int GroupUpdates => _publicationEffects.Count(effect => effect == FrameCanvasPublicationEffect.UpdateGroupLayoutAndFrame);
        internal IReadOnlyList<FrameTarget> PublishedTargets => _publishedTargets;
        internal IReadOnlyList<FrameCanvasPublicationEffect> PublicationEffects => _publicationEffects;
        internal IReadOnlyList<ViewerEntry> LastArchiveEntries { get; private set; } = Array.Empty<ViewerEntry>();
        internal bool AllCallbacksOnDrainThread { get; private set; } = true;
        internal bool HasCommittedCanvas => _canvas.HasAsset && _canvas.HasTexture && _publication.Texture is not null &&
            ReferenceEquals(_canvas.PublishedTexture, _publication.Texture);
        internal bool HasRestoredPriorCanvas => _priorCanvas is not null && _priorCanvas.Matches(_canvas) && HasCommittedCanvas;
        internal bool HasNeutralSelection => _group.MaxValue == 0 && _group.Value == 0 &&
            _frame.MaxValue == 0 && _frame.Value == 0 && _palette.MaxValue == 0 && _palette.Value == 0 &&
            _metadata.Text == "No frame" && _rate.Text == "source 0 fps; effective blocked fps; override none";
        internal bool HasValidSelection => _group.Value == 0 && _frame.Value == 0 && _palette.Value == 0 &&
            _palette.MaxValue == ViewerState.MaxRequestedPaletteSlot && _metadata.Text != "No frame" &&
            _rate.Text.StartsWith("source ", StringComparison.Ordinal) && _rate.Text.Contains("effective ", StringComparison.Ordinal);

        public void SelectionInvalidated(bool clearEntries)
        {
            CheckThread();
            _publication.Clear(_canvas);
            _selectionUi.Clear(new AnimationTimeline(1, 0));
            SelectionInvalidations++;
            if (clearEntries) ClearedEntryLists++;
        }

        public void ArchiveOpened(IReadOnlyList<ViewerEntry> entries)
        {
            CheckThread();
            LastArchiveEntries = entries;
            StatusUpdates++;
        }

        public void EntryCommitted(SelectedArtSnapshot snapshot)
        {
            CheckThread();
            _publication.Clear(_canvas);
            _selectionUi.Commit(snapshot, new AnimationTimeline(snapshot.Metadata.Header.FramesPerGroup, snapshot.Metadata.Header.FramesPerSecond));
            EntryCommits++;
            StatusUpdates++;
        }

        public void PublishFrame(SelectedArtSnapshot snapshot, FrameTarget target, ArtFrame frame)
        {
            CheckThread();
            var injectFailure = _publishAttempts++ == _failOnPublicationAttempt;
            var effect = ViewerFramePublication.Commit(_canvas, _publication, snapshot, target, frame,
                injectFailure ? () => throw new FrameCanvasGeometryLimitException("Synthetic post-canvas publication failure.") : null, this);
            _publicationEffects.Add(effect);
            _publishedTargets.Add(target);
            PublishedFrames++;
            _selectionUi.PublishFrame(frame);
            _priorCanvas ??= CanvasSnapshot.Capture(_canvas);
            MetadataUpdates++;
            StatusUpdates++;
        }

        public void PublishFailure(Exception exception)
        {
            CheckThread();
            Failures++;
            StatusUpdates++;
        }

        public void ReplacementCreated() => CreatedResources++;
        public void ReplacementDiscarded() => DisposedResources++;
        public void PriorTextureDisposed() => DisposedResources++;

        public void Dispose()
        {
            _publication.Clear(_canvas);
            _canvas.Free();
            _group.Free();
            _frame.Free();
            _palette.Free();
            _metadata.Free();
            _rate.Free();
        }

        private void CheckThread() => AllCallbacksOnDrainThread &= System.Environment.CurrentManagedThreadId == _drainThread;

        private sealed record CanvasSnapshot(Texture2D? Texture, FrameCanvasGroupLayout? Layout, FrameHotspot Hotspot,
            Vector2 Pan, float Zoom, bool Fit)
        {
            internal static CanvasSnapshot Capture(FrameCanvas canvas) => new(canvas.PublishedTexture, canvas.PublishedGroupLayout,
                canvas.Hotspot, canvas.Pan, canvas.Zoom, canvas.FitMode);

            internal bool Matches(FrameCanvas canvas) => ReferenceEquals(Texture, canvas.PublishedTexture) &&
                ReferenceEquals(Layout, canvas.PublishedGroupLayout) && Hotspot == canvas.Hotspot && Pan == canvas.Pan &&
                Mathf.IsEqualApprox(Zoom, canvas.Zoom) && Fit == canvas.FitMode;
        }
    }

    private static void RequireThrows<TException>(Action action, string name) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Self-test failed: {name}.");
    }

    private static void Require(bool condition, string name)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Self-test failed: {name}.");
        }
    }

}