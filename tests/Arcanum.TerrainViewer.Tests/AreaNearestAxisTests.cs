using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

[TestClass]
public sealed class AreaNearestAxisTests
{
    [TestMethod]
    public void RowKeysAreExactAndInvariantUnderIntegerTranslation()
    {
        var random = new Random(11);
        var zooms = new[] { .125f, .25f, .5f, .8f, 1f, 1.25f, 1.2651956f, 2f, 3f, 8f };
        for (var trial = 0; trial < 3000; trial++)
        {
            var zoom = zooms[random.Next(zooms.Length)];
            var origin = random.Next(-2000, 2000) + (trial % 3 == 0 ? 0f : random.Next(0, 64) / 64f);
            var destination = random.Next(-5000, 5000); var length = random.Next(1, 200);
            var sourceSize = random.Next(1, 200); var sourceStart = random.Next(0, 50); var reflected = random.Next(2) == 1;
            var axis = new AreaNearestAxis(origin, zoom, destination, length, sourceStart, sourceSize, reflected);
            var start = (int)Math.Floor((double)origin + (double)zoom * destination) - 1;
            var width = (int)Math.Ceiling((double)origin + (double)zoom * ((long)destination + length)) + 1 - start;
            var key = axis.RowKey(start);
            var row = Enumerable.Range(0, width).Select(i => axis.SourceIndex(start + i)).ToArray();
            CollectionAssert.AreEqual(row, Enumerable.Range(0, width).Select(key.SourceIndex).ToArray(), $"trial {trial}");
            // The same item after an integer camera pan has the same key and row.
            var shift = random.Next(-300, 300);
            var moved = new AreaNearestAxis(origin + shift, zoom, destination, length, sourceStart, sourceSize, reflected);
            Assert.AreEqual(key, moved.RowKey(start + shift), $"trial {trial}");
        }
        // Different tiles at an integer (pixel-snapped) origin share one row whenever
        // zoom x destination has the same fractional phase: 78-wide tiles at zoom 2 and 0.5.
        foreach (var (zoom, d1, d2) in new[] { (2f, 0, 160), (2f, -37, 4041), (.5f, 4, 84), (.5f, -2, 2) })
        {
            var first = new AreaNearestAxis(321f, zoom, d1, 78, 0, 78, false); var second = new AreaNearestAxis(321f, zoom, d2, 78, 0, 78, false);
            int Start(int d) => (int)Math.Floor(321.0 + (double)zoom * d) - 1;
            Assert.AreEqual(first.RowKey(Start(d1)), second.RowKey(Start(d2)), $"zoom {zoom} {d1}/{d2}");
        }
        Assert.AreNotEqual(new AreaNearestAxis(321f, .5f, 4, 78, 0, 78, false).RowKey((int)Math.Floor(321.0 + 2) - 1),
            new AreaNearestAxis(321f, .5f, 5, 78, 0, 78, false).RowKey((int)Math.Floor(321.0 + 2.5) - 1));
        // A sub-pixel translation is a different row, not a cache hit.
        var a = new AreaNearestAxis(100f, .5f, 0, 8, 2, 8, false); var b = new AreaNearestAxis(100.5f, .5f, 0, 8, 2, 8, false);
        Assert.AreNotEqual(a.RowKey(99), b.RowKey(99));
    }

    [TestMethod]
    public void HalfZoomTiesHaveLiteralDiscreteReflectionAndHalfOpenOwnership()
    {
        var normal = new AreaNearestAxis(100,.5f,0,8,2,8,false);
        var reflected = new AreaNearestAxis(100,.5f,0,8,2,8,true);
        CollectionAssert.AreEqual(new[]{-1,3,5,7,9,-1}, Enumerable.Range(99,6).Select(normal.SourceIndex).ToArray());
        CollectionAssert.AreEqual(new[]{-1,8,6,4,2,-1}, Enumerable.Range(99,6).Select(reflected.SourceIndex).ToArray());
        Assert.AreEqual(3,new AreaNearestAxis(99.75f,.5f,0,8,2,8,false).SourceIndex(100));
        Assert.AreEqual(2,new AreaNearestAxis(100.5f,.5f,0,8,2,8,false).SourceIndex(100));
    }

    [TestMethod]
    public void SignedOriginsAndNonUnitDestinationScaleHaveIndependentLiteralIndices()
    {
        var axis=new AreaNearestAxis(-3.25f,2,-7,13,4,9,false);
        CollectionAssert.AreEqual(new[]{4,5,6,7,8,9,10,11,12},
            new[]{-17,-14,-11,-8,-5,-2,1,4,7}.Select(axis.SourceIndex).ToArray());
        Assert.AreEqual(-1,axis.SourceIndex(-18)); Assert.AreEqual(-1,axis.SourceIndex(9));
    }

    [TestMethod]
    public void ActualSinglePrecisionInputsAreNotDecimalApproximations()
    {
        // 0.1f lies strictly ABOVE rational 1/10. At center0.5, five source
        // texels therefore select3, not the decimal-tie index4.
        Assert.AreEqual(3,new AreaNearestAxis(.1f,1,0,1,0,10,false).SourceIndex(0));
        Assert.AreEqual(6,new AreaNearestAxis(.1f,1,0,1,0,10,true).SourceIndex(0));
    }

    [TestMethod]
    public void InvalidTransformsAndUnencodableSourceDomainsFail()
    {
        foreach(var zoom in new[]{0f,-1f,float.NaN,float.PositiveInfinity})
            Assert.ThrowsExactly<InvalidOperationException>(()=>new AreaNearestAxis(0,zoom,0,8,0,8,false));
        Assert.ThrowsExactly<InvalidOperationException>(()=>new AreaNearestAxis(0,1,0,0,0,8,false));
        Assert.ThrowsExactly<InvalidOperationException>(()=>new AreaNearestAxis(0,1,0,8,65530,8,false));
    }
}
