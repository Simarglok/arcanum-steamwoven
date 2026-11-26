using System.Numerics;
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

    // The rule in big integers, written here on its own: floor((p + 1/2 - origin) / zoom - start) scaled to the source.
    private static int Exact(float origin, float zoom, int destination, int length, int sourceStart, int sourceSize, bool reflected, int pixel)
    {
        static (BigInteger N, BigInteger D) Of(float value)
        {
            var bits = BitConverter.SingleToUInt32Bits(value); var exponent = (int)((bits >> 23) & 255);
            BigInteger mantissa = bits & 0x7fffff; if (exponent != 0) mantissa += 1 << 23;
            var power = exponent == 0 ? -149 : exponent - 150; if ((bits & 0x80000000) != 0) mantissa = -mantissa;
            return power >= 0 ? (mantissa << power, 1) : (mantissa, BigInteger.One << -power);
        }
        var (on, od) = Of(origin); var (zn, zd) = Of(zoom);
        var numerator = ((2 * (BigInteger)pixel + 1) * od * zd - 2 * on * zd - 2 * od * zn * destination) * sourceSize;
        var denominator = 2 * od * zn * length;
        if (numerator < 0 || numerator >= denominator * sourceSize) return -1;
        var index = (int)(numerator / denominator);
        return sourceStart + (reflected ? sourceSize - 1 - index : index);
    }

    [TestMethod]
    public void NarrowAndWideRulesMatchTheExactRationalAndTheOutsideTestNeverHidesAPixel()
    {
        var random = new Random(29);
        // Origins and zooms on both sides of the 128-bit path: small fractions, tiny and huge values, subnormals.
        var origins = new[] { 0f, -0f, 1e-3f, -1e-7f, 1e-20f, float.Epsilon, 0.1f, 1234.5678f, -98765.43f, 4_000_000f, 5e9f, -3e12f, 1e30f };
        var zooms = new[] { 1f, 2f, .5f, 1.2651956f, .1f, 3.7f, 1e-6f, 1e-12f, 5e9f, float.Epsilon, 1e30f };
        var narrow = 0; var wide = 0; var hidden = 0;
        for (var trial = 0; trial < 6000; trial++)
        {
            var origin = trial % 2 == 0 ? origins[random.Next(origins.Length)] : random.Next(-5000, 5000) + (float)random.NextDouble();
            var zoom = trial % 3 == 0 ? zooms[random.Next(zooms.Length)] : (float)(random.NextDouble() * 4 + 0.05);
            var destination = random.Next(-8000, 8000); var length = trial % 11 == 0 ? random.Next(1, int.MaxValue) : random.Next(1, 600);
            var sourceStart = random.Next(0, 500); var sourceSize = trial % 7 == 0 ? 65534 - sourceStart : random.Next(1, 600);
            var reflected = random.Next(2) == 1;
            var axis = new AreaNearestAxis(origin, zoom, destination, length, sourceStart, sourceSize, reflected);
            var low = Math.Clamp((double)origin + (double)zoom * destination, -1e8, 1e8);
            var high = Math.Clamp((double)origin + (double)zoom * ((long)destination + length), -1e8, 1e8);
            var pixels = new[] { 0, 1, -1, (int)low - 2, (int)low - 1, (int)low, (int)low + 1, (int)high - 1, (int)high, (int)high + 1, (int)high + 2,
                random.Next(-20000, 20000), int.MaxValue, int.MinValue };
            var start = pixels[random.Next(pixels.Length - 2)];
            var key = axis.RowKey(start);
            _ = key.Wide is null ? narrow++ : wide++;
            foreach (var pixel in pixels)
            {
                var expected = Exact(origin, zoom, destination, length, sourceStart, sourceSize, reflected, pixel);
                Assert.AreEqual(expected, axis.SourceIndex(pixel), $"trial {trial} {origin:R} {zoom:R} pixel {pixel}");
                if (AreaNearestAxis.CertainlyOutside(origin, zoom, destination, length, pixel))
                {
                    hidden++;
                    Assert.AreEqual(-1, expected, $"trial {trial} {origin:R} {zoom:R} pixel {pixel} is inside");
                }
                var offset = (long)pixel - start;
                if (offset is >= -100000 and <= 100000) Assert.AreEqual(expected, key.SourceIndex((int)offset), $"trial {trial} row offset {offset}");
            }
            Assert.AreEqual(key, new AreaNearestAxis(origin, zoom, destination, length, sourceStart, sourceSize, reflected).RowKey(start), $"trial {trial}");
        }
        Assert.IsTrue(narrow > 1000 && wide > 100 && hidden > 1000, $"both paths and the outside test ran: {narrow}/{wide}/{hidden}");
    }

    [TestMethod]
    public void TheNarrowPathAllocatesNothing()
    {
        var sum = 0;
        void Run()
        {
            for (var i = 0; i < 2000; i++)
            {
                var axis = new AreaNearestAxis(1234.5678f + i, 1.2651956f, i, 78, 0, 78, false);
                sum += axis.SourceIndex(1300 + i) + axis.RowKey(1200).SourceIndex(100);
            }
        }
        Run();
        var before = GC.GetAllocatedBytesForCurrentThread();
        Run();
        Assert.AreEqual(0, GC.GetAllocatedBytesForCurrentThread() - before, $"sum {sum}");
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
