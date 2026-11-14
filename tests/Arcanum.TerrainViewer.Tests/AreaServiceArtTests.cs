using Arcanum.Simulation.Equipment;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-1765: the item identify and repair windows' original layout (ledger IS2, IW20, IW26–IW27: the own window's frame with the NPC's image at half size and its quote line, no boxes;
/// no game data).</summary>
[TestClass]
public sealed class AreaServiceArtTests
{
    private static AreaInventoryView View() => new([], null, null, [])
    {
        Grid = [new AreaGridItem("axe", 0, 1, 3, "Axe"), new AreaGridItem("ring", 11, 1, 1, "Ring")],
        Doll = [new AreaDollSlot(EquipmentSlot.Helmet, "helm", "Helm")],
    };

    [TestMethod]
    public void TheBackgroundIsTheOwnWindowsAndTheArtsAreRead()
    {
        CollectionAssert.Contains(AreaInventoryArtContent.Numbers.ToArray(), AreaServiceArtLayout.BackgroundArt);
        CollectionAssert.Contains(AreaInventoryArtContent.Numbers.ToArray(), AreaInventoryHole.InventoryArt);
        Assert.AreEqual(233, AreaInventoryHole.Art(AreaInventoryMode.Own), "the round view of modes 0, 5 and 6");
        Assert.AreEqual((11, 9), AreaInventoryHole.Corner(AreaInventoryMode.Own));
    }

    [TestMethod]
    public void TheImageIsAtItsCornerAtHalfItsSize()
    {
        Assert.AreEqual((141, 36), AreaServiceArtLayout.Image);
        Assert.AreEqual((141f, 36f, 40f, 21f), AreaServiceArtLayout.ImageRect(80, 43), "an integer halving of the art's size");
        Assert.AreEqual((141f, 36f, 0f, 0f), AreaServiceArtLayout.ImageRect(1, 1));
    }

    [TestMethod]
    public void TheQuoteLineAndTheImageLieInsideThePdollBackgroundAndClearTheRoundViewAndTheHelmet()
    {
        // The frame is 358 wide on the left (the background); the quote line ends inside it and below the round view's square (11 + 89, 9 + 89).
        var (qx, qy, qw, qh) = AreaServiceArtLayout.QuoteLine;
        Assert.IsTrue(qx + qw <= 358 && qy + qh <= 400, "the quote line is inside the background");
        var (hx, hy) = AreaInventoryHole.Corner(AreaInventoryMode.Own);
        Assert.IsTrue(qx >= hx + AreaInventoryHole.Side, "the quote line starts right of the round view");
        // An image art of up to 128 × 128 pixels (half size 64) ends inside the quote line's columns and above the doll's helmet slot (y 107).
        var helmet = AreaInventoryDrag.SlotRects[EquipmentSlot.Helmet];
        var image = AreaServiceArtLayout.ImageRect(128, 128);
        Assert.IsTrue(image.X + image.Width <= qx + qw && image.Y + image.Height <= helmet.Y, "a 128 × 128 art at half size does not reach the doll's helmet slot");
    }

    [TestMethod]
    public void AnItemUnderThePointIsNamedAndTheBoxesNameNone()
    {
        var view = View();
        Assert.AreEqual("axe", AreaServiceArtLayout.ItemAt(view, AreaInventoryDrag.GridX + 5, AreaInventoryDrag.GridY + 70), "the axe's third cell");
        Assert.AreEqual("ring", AreaServiceArtLayout.ItemAt(view, AreaInventoryDrag.GridX + 32 + 5, AreaInventoryDrag.GridY + 32 + 5));
        Assert.IsNull(AreaServiceArtLayout.ItemAt(view, AreaInventoryDrag.GridX + 5 * 32, AreaInventoryDrag.GridY + 5 * 32), "an empty cell");
        var helmet = AreaInventoryDrag.SlotRects[EquipmentSlot.Helmet];
        Assert.AreEqual("helm", AreaServiceArtLayout.ItemAt(view, helmet.X + 3, helmet.Y + 3), "a worn item is named too");
        Assert.IsNull(AreaServiceArtLayout.ItemAt(view, AreaInventoryDrag.IconX + 3, AreaInventoryDrag.DropIconY + 3), "the drop box is not made in modes 5 and 6");
        Assert.IsNull(AreaServiceArtLayout.ItemAt(view, 5, 380), "outside the grid and the slots");
    }

    [TestMethod]
    public void TheRememberedItemsCellsAreFilledWithTheTooHeavyWeaponsColour()
    {
        var view = View();
        Assert.AreEqual((AreaInventoryDrag.GridX, AreaInventoryDrag.GridY, 32f, 96f), AreaServiceArtLayout.SelectedRect(view, "axe"));
        Assert.AreEqual((AreaInventoryDrag.GridX + 32, AreaInventoryDrag.GridY + 32, 32f, 32f), AreaServiceArtLayout.SelectedRect(view, "ring"));
        Assert.IsNull(AreaServiceArtLayout.SelectedRect(view, "helm"), "a worn item has no grid cells to fill");
        Assert.IsNull(AreaServiceArtLayout.SelectedRect(view, null));
        Assert.IsNull(AreaServiceArtLayout.SelectedRect(view, "gone"));
        Assert.AreEqual(AreaLootArtLayout.TooHeavyFill, AreaServiceArtLayout.SelectedFill);
        Assert.AreEqual((0x40, 0, 0), AreaServiceArtLayout.SelectedFill);
    }

    // ARC-UI-1820: the generated interface of the two windows is the own window's recipes of `PDoll` (223), `Inventor` (221) and the round view's lens (233); these checks tie the service
    // window's own pictures (the NPC's image and quote line) to the plate of the `PDoll` recipe they lie on.
    private static ChromeRecipe Recipe(string file) => InterfaceChromeRecipes.All.Single(recipe => recipe.File == file);

    [TestMethod]
    public void ThePdollRecipePaintsTheNamePlateTheImageAndTheQuoteLineLieOn()
    {
        var plate = Recipe("pdoll.art").Parts.OfType<ChromeSunk>().Single(part => (part.X, part.Y, part.Width, part.Height) == AreaServiceArtLayout.NamePlate);
        var (px, py, pw, ph) = AreaServiceArtLayout.NamePlate;
        var (qx, qy, qw, _) = AreaServiceArtLayout.QuoteLine;
        Assert.AreEqual(ChromeMaterial.Pit, plate.Fill);
        Assert.IsTrue(AreaServiceArtLayout.Image.X >= px + 2 && AreaServiceArtLayout.Image.Y >= py + 2, "the image's corner is inside the plate's edge");
        Assert.IsTrue(qx >= px + 2 && qy >= py + 2 && qx + qw <= px + pw - 2, "the quote line's columns and top are inside the plate");
        // The first three lines of the quote (up to 15 pixels each, the window's 12 pixel font) lie on the plate; a longer quote runs on the frame's wood below it, as in the original (the rectangle is 65 tall, the plate 55).
        Assert.IsTrue(qy + 3 * 15 <= py + ph, "three lines fit the plate");
    }

    [TestMethod]
    public void TheImageOfAnArtThatFitsThePlateIsInsideItAndALargerOneIsToldApart()
    {
        // The plate (124..347 across, 25..80 down) with a two unit margin leaves 204 columns right of the image's corner (141) and 42 rows below it (36): arts up to 409 × 85 pixels at half size.
        Assert.IsTrue(AreaServiceArtLayout.ImageFitsNamePlate(80, 80), "an 80 × 80 portrait");
        Assert.IsTrue(AreaServiceArtLayout.ImageFitsNamePlate(409, 85));
        Assert.IsFalse(AreaServiceArtLayout.ImageFitsNamePlate(80, 86), "one pixel row too many at half size");
        Assert.IsFalse(AreaServiceArtLayout.ImageFitsNamePlate(410, 40), "one column too many at half size");
        Assert.IsFalse(AreaServiceArtLayout.ImageFitsNamePlate(128, 128), "a 64 × 64 image reaches below the plate");
    }

    [TestMethod]
    public void NoKeptPictureOfThePdollRecipeCutsIntoTheNamePlateAndEveryPartLiesInsideTheFrame()
    {
        var recipe = Recipe("pdoll.art");
        const int w = 358, h = 400;
        var (px, py, pw, ph) = AreaServiceArtLayout.NamePlate;
        foreach (var keep in recipe.Parts.OfType<ChromeKeep>())
        {
            Assert.IsTrue(keep.X >= 0 && keep.Y >= 0 && keep.X + keep.Width <= w && keep.Y + keep.Height <= h, $"a kept picture at ({keep.X}, {keep.Y}) lies inside the frame");
            var overlaps = keep.X < px + pw && px < keep.X + keep.Width && keep.Y < py + ph && py < keep.Y + keep.Height;
            Assert.IsFalse(overlaps, $"the kept picture at ({keep.X}, {keep.Y}) does not cut into the name plate");
        }
        // The doll grid's pit runs off the frame's bottom edge on purpose (the original's box has no bottom edge between its two curved corners, S3); no plate leaves the frame on another side.
        foreach (var sunk in recipe.Parts.OfType<ChromeSunk>())
        {
            var runsOffBottom = sunk.Y + sunk.Height > h;
            Assert.IsTrue(sunk.X >= 0 && sunk.Y >= 0 && sunk.X + sunk.Width <= w, $"the plate at ({sunk.X}, {sunk.Y}) lies inside the frame's sides");
            Assert.IsTrue(!runsOffBottom || (sunk.X, sunk.Y, sunk.Width, sunk.Height) == (18, 100, 330, 310), $"only the doll grid's pit runs off the bottom edge, not the plate at ({sunk.X}, {sunk.Y})");
        }
    }

    [TestMethod]
    public void TheFramesOfBothWindowsHaveGeneratedRecipesWithEveryPartInsideTheFrameAndPaintDeterministicallyAtWholeAndFractionalScales()
    {
        var frames = new[] { ("pdoll.art", 358, 400), ("inventor.art", 442, 400), ("lns_papr.art", 89, 89), (InterfaceChromeRecipes.ArrangeButtonFile, 28, 27) };
        foreach (var (file, w, h) in frames)
        {
            var recipe = Recipe(file);
            foreach (var part in recipe.Parts)
            {
                (double, double, double, double) box = part switch
                {
                    ChromeSunk p => (p.X, p.Y, p.Width, p.Height),
                    ChromeKeep p => (p.X, p.Y, p.Width, p.Height),
                    ChromeGrid p => (p.X, p.Y, p.Columns * p.Cell, p.Rows * p.Cell),
                    ChromeRoundView p => (p.CenterX - p.Radius, p.CenterY - p.Radius, 2 * p.Radius, 2 * p.Radius),
                    ChromeRivet p => (p.X - p.Radius, p.Y - p.Radius, 2 * p.Radius, 2 * p.Radius),
                    _ => (0d, 0d, 0d, 0d),
                };
                var (x, y, pw0, ph0) = box;
                // The doll grid's pit runs off the bottom edge on purpose (see the plate test below).
                var bottomSlack = file == "pdoll.art" && part is ChromeSunk { Y: 100, Height: 310 } ? 10 : 0;
                Assert.IsTrue(x >= 0 && y >= 0 && x + pw0 <= w + 1e-9 && y + ph0 <= h + bottomSlack + 1e-9, $"{file}: a {part.GetType().Name} at ({x}, {y}, {pw0} x {ph0}) lies inside {w} x {h}");
            }
            var legacy = new byte[w * h * 4];
            for (var i = 0; i < legacy.Length; i += 4) (legacy[i], legacy[i + 1], legacy[i + 2], legacy[i + 3]) = (90, 60, 30, 255);
            foreach (var scale in new[] { 1.0, 1.345, 2.0, 2.5 })
            {
                var first = InterfaceChromeRenderer.Render(recipe, w, h, legacy, [], 0, scale);
                var (pw, ph) = InterfaceChromeRenderer.PixelSize(w, h, scale);
                Assert.AreEqual(pw * ph * 4, first.Length, $"{file} at {scale}");
                CollectionAssert.AreEqual(first, InterfaceChromeRenderer.Render(recipe, w, h, legacy, [], 0, scale), $"{file} is deterministic at {scale}");
            }
        }
    }

    [TestMethod]
    public void TheArrangeButtonKeepsItsDarkBallInAGeneratedRimAndSinksOnItsSecondFrame()
    {
        // A synthetic 28 x 27 round button (the art 340's size): a dark olive disc inside a gold outline, transparent corners.
        const int w = 28, h = 27;
        var legacy = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            double dx = (x + 0.5 - w / 2.0) / (w / 2.0), dy = (y + 0.5 - h / 2.0) / (h / 2.0), d = Math.Sqrt(dx * dx + dy * dy);
            var i = (y * w + x) * 4;
            if (d >= 1) continue;
            (legacy[i], legacy[i + 1], legacy[i + 2], legacy[i + 3]) = d < .78 ? ((byte)40, (byte)36, (byte)8, (byte)255) : ((byte)200, (byte)150, (byte)40, (byte)255);
        }
        var recipe = Recipe(InterfaceChromeRecipes.ArrangeButtonFile);
        Assert.AreEqual(ChromeKind.Button, recipe.Kind);
        Assert.IsTrue(recipe.KeepFace, "the ball has no glyph: its own picture stays");
        foreach (var scale in new[] { 1.0, 1.345, 2.0, 2.5 })
        {
            var first = InterfaceChromeRenderer.Render(recipe, w, h, legacy, [], 0, scale);
            var (pw, ph) = InterfaceChromeRenderer.PixelSize(w, h, scale);
            Assert.AreEqual(pw * ph * 4, first.Length, $"at {scale}");
            CollectionAssert.AreEqual(first, InterfaceChromeRenderer.Render(recipe, w, h, legacy, [], 0, scale), $"deterministic at {scale}");
            Assert.AreEqual(0, first[3], $"the corner is outside the round silhouette at {scale}");
            var centre = (R: first[((ph / 2) * pw + pw / 2) * 4], A: first[((ph / 2) * pw + pw / 2) * 4 + 3]);
            Assert.IsTrue(centre.A == 255 && centre.R < 90, $"the dark ball stays inside the rim at {scale}: R {centre.R}");
            CollectionAssert.AreNotEqual(first, InterfaceChromeRenderer.Render(recipe, w, h, legacy, [], 1, scale), $"the second frame is sunk at {scale}");
        }
    }
}
