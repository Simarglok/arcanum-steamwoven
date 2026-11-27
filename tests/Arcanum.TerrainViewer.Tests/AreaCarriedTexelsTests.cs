using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-RENDER-3427: the textures of the carried lights' lattice stay while the lattice fits them.</summary>
[TestClass]
public sealed class AreaCarriedTexelsTests
{
    [TestMethod]
    public void TheFirstTexturesHoldAWholeStep()
        => Assert.AreEqual((16, 32), AreaCarriedTexels.Size(0, 0, 4, 17));

    [TestMethod]
    public void ALatticeThatFitsKeepsTheTextures()
    {
        // A walking light's lattice changes by a column or a row; none of these sizes asks for other textures.
        foreach (var (columns, rows) in new[] { (3, 3), (4, 3), (3, 4), (4, 4), (16, 16), (0, 0) })
            Assert.AreEqual((16, 16), AreaCarriedTexels.Size(16, 16, columns, rows), $"{columns}x{rows}");
    }

    [TestMethod]
    public void ASideGrowsWhenTheLatticeOutgrowsIt()
        => Assert.AreEqual((32, 48), AreaCarriedTexels.Size(32, 16, 5, 33));

    [TestMethod]
    public void ASideIsGivenBackOnlyWhenItIsFarLargerThanNeeded()
    {
        Assert.AreEqual((64, 64), AreaCarriedTexels.Size(64, 64, 1, 16), "four times the need stays");
        Assert.AreEqual((16, 32), AreaCarriedTexels.Size(80, 144, 4, 17), "more than four times shrinks");
    }

    [TestMethod]
    public void ANegativeLatticeIsRefused()
        => Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AreaCarriedTexels.Size(16, 16, -1, 4));

    [TestMethod]
    public void TheRowsLieInTheTopLeftAndTheRestIsZero()
    {
        var lattice = Enumerable.Range(1, 2 * 2 * 4).Select(value => (byte)value).ToArray();
        var texels = AreaCarriedTexels.Lay(lattice, 2, 2, 3, 3);
        var expected = new byte[3 * 3 * 4];
        lattice.AsSpan(0, 8).CopyTo(expected);
        lattice.AsSpan(8, 8).CopyTo(expected.AsSpan(12));
        CollectionAssert.AreEqual(expected, texels);
    }

    [TestMethod]
    public void ALatticeThatDoesNotFitOrDoesNotMatchItsSizeIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => AreaCarriedTexels.Lay(new byte[16], 2, 2, 1, 2));
        Assert.ThrowsExactly<ArgumentException>(() => AreaCarriedTexels.Lay(new byte[16], 2, 2, 2, 1));
        Assert.ThrowsExactly<ArgumentException>(() => AreaCarriedTexels.Lay(new byte[12], 2, 2, 2, 2));
    }
}
