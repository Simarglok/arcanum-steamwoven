using System.Buffers.Binary;
using Arcanum.Data;

namespace Arcanum.StaticSceneryProbe;

/// <summary>
/// Synthetic-only diagnostic for the bounded scenery placement policy recorded
/// in docs/research/scenery-placement.md. It is not a production renderer or an
/// original-runtime comparison.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (!args.SequenceEqual(["--self-test"]))
            {
                throw new ArgumentException("usage: --self-test");
            }

            AssertProjectionAndAnchor();
            AssertHotspotAndArtOffsetDistinction();
            AssertFlipAndTransparency();
            AssertOverlapAndStableTie();
            Console.WriteLine("static-scenery self-test: PASS (synthetic anchor, hotspot/offset distinction, flip, transparency, overlap, and stable tie policy)");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"static-scenery-probe: FAIL: {exception.GetType().Name}: {exception.Message}");
            return 1;
        }
    }

    private static void AssertProjectionAndAnchor()
    {
        var anchor = Placement.ComputeAnchor(localX: 6, localY: 1, instanceOffsetX: -21, instanceOffsetY: 8);
        Require(anchor == new Point(-221, 148), "independently stated projected anchor for synthetic local=(6,1), instance offset=(-21,8)");
    }

    private static void AssertHotspotAndArtOffsetDistinction()
    {
        var frame = Decode(BuildArt(width: 4, height: 3, hotX: 1, hotY: 2, artOffsetX: 9, artOffsetY: -4,
            pixels: [1, 0, 0, 0, 0, 2, 0, 0, 0, 0, 3, 0]));
        var placement = Placement.For(anchor: new Point(100, 50), frame);

        // The expected rectangle is authored independently from the helper:
        // (100 - 1, 50 - 2) with the original frame dimensions. The deliberately
        // nonzero ART offsets must remain metadata, not a second translation.
        Require(placement.TopLeft == new Point(99, 48) && placement.Width == 4 && placement.Height == 3,
            "hotspot-derived destination without ART offset translation");
        Require(frame.Frame.OffsetX == 9 && frame.Frame.OffsetY == -4, "synthetic nonzero ART offset retained as metadata");
    }

    private static void AssertFlipAndTransparency()
    {
        var frame = Decode(BuildArt(width: 4, height: 3, hotX: 1, hotY: 2, artOffsetX: 9, artOffsetY: -4,
            pixels: [1, 0, 0, 0, 0, 2, 0, 0, 0, 0, 3, 0]));
        var placement = Placement.For(anchor: new Point(100, 50), frame);
        const byte transparentDestinationSentinel = 17;
        var normal = new Canvas(128, 96, transparentDestinationSentinel);
        normal.Draw(frame, placement, flipX: false);
        Require(normal.RedAt(99, 48) == 31 && normal.RedAt(100, 49) == 63 && normal.RedAt(101, 50) == 95,
            "unflipped opaque asymmetric landmarks overwrite the destination sentinel");
        Require(normal.RedAt(102, 48) == transparentDestinationSentinel && normal.RedAt(99, 49) == transparentDestinationSentinel,
            "transparent source index leaves named nonzero destination sentinels unchanged");

        var flipped = new Canvas(128, 96, transparentDestinationSentinel);
        flipped.Draw(frame, placement, flipX: true);
        Require(flipped.RedAt(102, 48) == 31 && flipped.RedAt(101, 49) == 63 && flipped.RedAt(100, 50) == 95,
            "horizontally flipped opaque asymmetric landmarks overwrite the destination sentinel");
        Require(flipped.RedAt(99, 48) == transparentDestinationSentinel && flipped.RedAt(102, 49) == transparentDestinationSentinel,
            "flipped transparent source index leaves named nonzero destination sentinels unchanged");
        Require(placement.TopLeft == new Point(99, 48), "flip preserves diagnostic anchor and destination rectangle");
    }

    private static void AssertOverlapAndStableTie()
    {
        var rear = Decode(BuildArt(width: 3, height: 3, hotX: 1, hotY: 2, artOffsetX: 0, artOffsetY: 0,
            pixels: [0, 0, 0, 0, 0, 0, 0, 0, 1]));
        var front = Decode(BuildArt(width: 3, height: 3, hotX: 1, hotY: 2, artOffsetX: 0, artOffsetY: 0,
            pixels: [0, 0, 0, 0, 0, 0, 0, 0, 3]));
        var anchor = new Point(20, 20);
        var commands = new[]
        {
            new Command(SourceIndex: 9, Placement: Placement.For(anchor, front), Frame: front),
            new Command(SourceIndex: 7, Placement: Placement.For(anchor, rear), Frame: rear),
        };

        var canvas = new Canvas(64, 64);
        foreach (var command in commands.OrderBy(command => command.Placement.Anchor.Y)
                     .ThenBy(command => command.Placement.Anchor.X).ThenBy(command => command.SourceIndex))
        {
            canvas.Draw(command.Frame, command.Placement, flipX: false);
        }

        // Both opaque landmarks target (21,20). The fixed source-index tie
        // policy draws 7 then 9, so the latter's independently authored red=95 wins.
        Require(canvas.RedAt(21, 20) == 95, "synthetic overlap pair and source-index stable tie result");
    }

    private static ArtFrame Decode(byte[] bytes) => ArtDecoder.Decode(bytes, new ArtFrameSelection(0, 0, 0));

    private static byte[] BuildArt(int width, int height, int hotX, int hotY, int artOffsetX, int artOffsetY, byte[] pixels)
    {
        if (pixels.Length != checked(width * height)) throw new ArgumentException("Synthetic pixel count must equal width times height.", nameof(pixels));
        const int HeaderBytes = 132;
        const int PaletteBytes = 1024;
        const int FrameHeaderBytes = 28;
        var bytes = new byte[checked(HeaderBytes + PaletteBytes + FrameHeaderBytes + pixels.Length)];
        WriteU32(bytes, 0, 1); // One physical group.
        WriteI32(bytes, 8, 8); // 8 bpp.
        WriteU32(bytes, 12, 1); // Palette 0 present.
        WriteI32(bytes, 32, 1); // One frame per group.
        SetPalette(bytes, index: 1, red: 31);
        SetPalette(bytes, index: 2, red: 63);
        SetPalette(bytes, index: 3, red: 95);
        var frame = HeaderBytes + PaletteBytes;
        WriteI32(bytes, frame, width);
        WriteI32(bytes, frame + 4, height);
        WriteI32(bytes, frame + 8, pixels.Length); // Raw payload branch.
        WriteI32(bytes, frame + 12, hotX);
        WriteI32(bytes, frame + 16, hotY);
        WriteI32(bytes, frame + 20, artOffsetX);
        WriteI32(bytes, frame + 24, artOffsetY);
        pixels.CopyTo(bytes.AsSpan(frame + FrameHeaderBytes));
        return bytes;
    }

    private static void SetPalette(byte[] bytes, int index, byte red)
    {
        var offset = 132 + (index * 4);
        bytes[offset] = 0;
        bytes[offset + 1] = 0;
        bytes[offset + 2] = red;
        bytes[offset + 3] = 255;
    }

    private static void WriteI32(byte[] bytes, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, sizeof(int)), value);
    private static void WriteU32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, sizeof(uint)), value);
    private static void Require(bool value, string label) { if (!value) throw new InvalidOperationException($"Assertion failed: {label}."); }

    private readonly record struct Point(int X, int Y);
    private readonly record struct Placement(Point Anchor, Point TopLeft, int Width, int Height)
    {
        public static Point ComputeAnchor(int localX, int localY, int instanceOffsetX, int instanceOffsetY) =>
            new(checked((40 * checked(localY - localX)) + instanceOffsetX), checked((20 * checked(localY + localX)) + instanceOffsetY));

        public static Placement For(Point anchor, ArtFrame frame) =>
            new(anchor, new Point(checked(anchor.X - frame.Frame.HotX), checked(anchor.Y - frame.Frame.HotY)), frame.Width, frame.Height);
    }

    private sealed record Command(int SourceIndex, Placement Placement, ArtFrame Frame);

    private sealed class Canvas
    {
        private readonly byte[] _red;
        private readonly int _width;

        public Canvas(int width, int height, byte backgroundRed = 0)
        {
            _width = width;
            _red = new byte[checked(width * height)];
            Array.Fill(_red, backgroundRed);
        }

        public void Draw(ArtFrame frame, Placement placement, bool flipX)
        {
            for (var y = 0; y < frame.Height; y++)
            {
                for (var destinationX = 0; destinationX < frame.Width; destinationX++)
                {
                    var sourceX = flipX ? frame.Width - destinationX - 1 : destinationX;
                    var rgbaOffset = checked(4 * ((y * frame.Width) + sourceX));
                    if (frame.Rgba[rgbaOffset + 3] == 0) continue;
                    var x = placement.TopLeft.X + destinationX;
                    var outputY = placement.TopLeft.Y + y;
                    if (x >= 0 && x < _width && outputY >= 0 && outputY < _red.Length / _width) _red[(outputY * _width) + x] = frame.Rgba[rgbaOffset];
                }
            }
        }

        public byte RedAt(int x, int y) => _red[(y * _width) + x];
    }
}
