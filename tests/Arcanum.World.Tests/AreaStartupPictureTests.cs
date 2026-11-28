using System.Buffers.Binary;
using Arcanum.Data.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.World.Tests;

/// <summary>ARC-UI-3501: the start-up loading picture (docs/research/startup-loading-picture.md): the rotation by the saved <c>splash</c> number,
/// the three tries, and the picture's place on the original's 800 × 600 screen. The bitmaps are synthetic.</summary>
[TestClass]
public sealed class AreaStartupPictureTests
{
    private static readonly string[] Three = ["art/splash/splash1.bmp", "art/splash/splash2.bmp", "art/splash/splash3.bmp"];

    /// <summary>A 24-bit bitmap whose stored rows carry their row number (from the top of the picture) in the red channel.</summary>
    private static byte[] Bitmap(int width, int height, bool topDown = false, short bits = 24, int compression = 0)
    {
        var stride = (width * bits + 31) / 32 * 4;
        var palette = bits <= 8 ? 4 * (1 << bits) : 0;
        var file = new byte[54 + palette + stride * height];
        file[0] = (byte)'B'; file[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(10), 54 + palette);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(22), topDown ? -height : height);
        BinaryPrimitives.WriteInt16LittleEndian(file.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(file.AsSpan(28), bits);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(30), compression);
        if (bits == 24)
            for (var stored = 0; stored < height; stored++)
                for (var x = 0; x < width; x++)
                    file[54 + stored * stride + x * 3 + 2] = (byte)(1 + (topDown ? stored : height - 1 - stored) % 200);
        return file;
    }

    private static (int R, int G, int B, int A) Pixel(byte[] screen, int x, int y)
    {
        var at = (y * AreaStartupPicture.ScreenWidth + x) * 4;
        return (screen[at], screen[at + 1], screen[at + 2], screen[at + 3]);
    }

    [TestMethod, TestCategory("RealData")]
    public void The_installed_game_has_three_start_up_pictures_that_can_be_drawn()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var discovery = ContentMountPlan.Discover(root);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var paths = AreaStartupPicture.Paths(stack);
        CollectionAssert.AreEqual(Three, paths.ToArray());
        foreach (var path in paths) Assert.IsNotNull(AreaStartupPicture.Compose(AreaStartupPicture.Read(stack, path)!), path);
    }

    [TestMethod]
    [DataRow(null, 0u)]
    [DataRow("", 0u)]
    [DataRow("2", 2u)]
    [DataRow(" \t+7 and more", 7u)]
    [DataRow("-1", uint.MaxValue)]
    [DataRow("4294967299", 3u)]
    [DataRow("x3", 0u)]
    public void The_saved_number_reads_like_an_integer_and_wraps(string? value, uint expected)
        => Assert.AreEqual(expected, AreaStartupPicture.Counter(value));

    [TestMethod]
    public void Each_start_takes_the_next_picture_in_name_order_and_wraps()
    {
        var file = Bitmap(800, 400);
        var counter = AreaStartupPicture.Counter(null);
        var shown = new List<(string?, int?)>();
        for (var start = 0; start < 4; start++)
        {
            var choice = AreaStartupPicture.Choose(Three, counter, _ => file);
            shown.Add((choice.Path, choice.NextCounter));
            counter = (uint)choice.NextCounter!.Value;
        }
        // The saved number is the index plus one, not wrapped: after the last picture it is 3 and the next start takes index 0.
        CollectionAssert.AreEqual(new (string?, int?)[] { (Three[0], 1), (Three[1], 2), (Three[2], 3), (Three[0], 1) }, shown);
        Assert.AreEqual(Three[uint.MaxValue % 3], AreaStartupPicture.Choose(Three, uint.MaxValue, _ => file).Path, "the number is used unsigned");
    }

    [TestMethod]
    public void A_file_that_cannot_be_drawn_gives_way_to_the_next_for_three_tries()
    {
        var file = Bitmap(800, 400);
        var second = AreaStartupPicture.Choose(Three, 2, path => path == Three[2] ? null : file);
        Assert.AreEqual((Three[0], 1), (second.Path, second.NextCounter!.Value));
        var asked = new List<string>();
        var none = AreaStartupPicture.Choose(Three, 1, path => { asked.Add(path); return null; });
        Assert.IsNull(none.Path);
        Assert.IsNull(none.Screen);
        Assert.AreEqual(1, none.NextCounter, "the last file tried was index 0");
        CollectionAssert.AreEqual(new[] { Three[1], Three[2], Three[0] }, asked);
        var tries = 0;
        AreaStartupPicture.Choose(["art/splash/only.bmp"], 0, _ => { tries++; return null; });
        Assert.AreEqual(AreaStartupPicture.Attempts, tries, "one file is tried three times");
    }

    [TestMethod]
    public void Without_a_picture_nothing_is_drawn_and_the_number_stays()
    {
        var choice = AreaStartupPicture.Choose([], 5, _ => throw new AssertFailedException("nothing to read"));
        Assert.AreEqual(new AreaStartupPictureChoice(null, null, null), choice);
    }

    [TestMethod]
    public void The_picture_stands_in_the_play_view_between_black_bands()
    {
        var screen = AreaStartupPicture.Compose(Bitmap(800, 400))!;
        Assert.AreEqual(800 * 600 * 4, screen.Length);
        Assert.AreEqual((0, 0, 0, 255), Pixel(screen, 0, 40));
        Assert.AreEqual((1, 0, 0, 255), Pixel(screen, 0, 41), "the picture's top row");
        Assert.AreEqual((200, 0, 0, 255), Pixel(screen, 799, 440), "the picture's last row");
        Assert.AreEqual((0, 0, 0, 255), Pixel(screen, 799, 441));
        Assert.AreEqual((0, 0, 0, 255), Pixel(screen, 0, 599));
    }

    [TestMethod]
    public void A_smaller_picture_keeps_the_left_edge_and_its_stored_direction_sets_its_height()
    {
        var bottomUp = AreaStartupPicture.Compose(Bitmap(100, 100))!;
        Assert.AreEqual((0, 0, 0, 255), Pixel(bottomUp, 0, 190));
        Assert.AreEqual((1, 0, 0, 255), Pixel(bottomUp, 0, 191), "centred: 41 + (400 - 100) / 2");
        Assert.AreEqual((0, 0, 0, 255), Pixel(bottomUp, 100, 191), "nothing right of the picture");
        var topDown = AreaStartupPicture.Compose(Bitmap(100, 100, topDown: true))!;
        Assert.AreEqual((1, 0, 0, 255), Pixel(topDown, 0, 41), "a top-down file starts at the view's top");
    }

    [TestMethod]
    public void Only_uncompressed_8_or_24_bit_bitmaps_that_fit_the_view_are_drawn()
    {
        Assert.IsNotNull(AreaStartupPicture.Compose(Bitmap(800, 400, bits: 8)));
        Assert.IsNull(AreaStartupPicture.Compose(Bitmap(800, 400, bits: 4)));
        Assert.IsNull(AreaStartupPicture.Compose(Bitmap(800, 400, compression: 1)));
        Assert.IsNull(AreaStartupPicture.Compose(Bitmap(801, 400)));
        Assert.IsNull(AreaStartupPicture.Compose(Bitmap(800, 401)));
        Assert.IsNull(AreaStartupPicture.Compose(Bitmap(800, 400).AsSpan(0, 1000)));
        Assert.IsNull(AreaStartupPicture.Compose([1, 2, 3]));
    }
}
