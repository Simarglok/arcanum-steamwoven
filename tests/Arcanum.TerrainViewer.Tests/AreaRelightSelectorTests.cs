using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-RENDER-3127: the relit look's light choice equals the former LINQ chain, and a call allocates nothing.</summary>
[TestClass]
public sealed class AreaRelightSelectorTests
{
    // The former AreaRelight.Upload selection, kept as the reference: reach filter, stable order by distance to the view's centre, first 32.
    private static (int[] Chosen, int InView, int Carried) Reference(RelightCandidate[] all, bool hasView, float vx, float vy, float vw, float vh, int max)
    {
        var indexed = all.Select((c, i) => (c, i)).ToArray();
        var endX = vx + vw; var endY = vy + vh; var cx = vx + vw * 0.5f; var cy = vy + vh * 0.5f;
        var reach = hasView
            ? indexed.Where(t => { var nx = Math.Clamp(t.c.X, vx, endX); var ny = Math.Clamp(t.c.Y, vy, endY); return (t.c.X - nx) * (t.c.X - nx) + (t.c.Y - ny) * (t.c.Y - ny) < t.c.Radius * t.c.Radius; }).ToArray()
            : indexed;
        var ordered = hasView ? reach.OrderBy(t => (t.c.X - cx) * (t.c.X - cx) + (t.c.Y - cy) * (t.c.Y - cy)).ToArray() : reach;
        return (ordered.Take(max).Select(t => t.i).ToArray(), reach.Length, reach.Count(t => t.c.Carried));
    }

    private static RelightCandidate[] Random(int seed, int count, bool coarse)
    {
        var random = new System.Random(seed);
        return Enumerable.Range(0, count).Select(_ => new RelightCandidate(
            coarse ? random.Next(0, 6) * 100f : (float)random.NextDouble() * 2000f, coarse ? random.Next(0, 6) * 100f : (float)random.NextDouble() * 1500f,
            50f + (float)random.NextDouble() * 200f, random.Next(4) == 0)).ToArray();
    }

    [TestMethod]
    public void ChoiceEqualsTheFormerLinqChain()
    {
        var selector = new RelightSelector(32);
        foreach (var coarse in new[] { false, true })
            for (var seed = 0; seed < 60; seed++)
            {
                var candidates = Random(seed, seed % 7 == 0 ? 0 : seed * 3, coarse);
                foreach (var hasView in new[] { true, false })
                {
                    selector.Select(candidates, hasView, 600, 400, 800, 500);
                    var (chosen, inView, carried) = Reference(candidates, hasView, 600, 400, 800, 500, 32);
                    CollectionAssert.AreEqual(chosen, selector.Chosen.ToArray(), $"seed {seed} coarse {coarse} view {hasView}");
                    Assert.AreEqual(inView, selector.InView); Assert.AreEqual(carried, selector.CarriedInView);
                }
            }
    }

    [TestMethod]
    public void ACallAllocatesNothingOnceTheSelectorExists()
    {
        var selector = new RelightSelector(32);
        var candidates = Random(7, 120, false);
        selector.Select(candidates, true, 600, 400, 800, 500);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var frame = 0; frame < 500; frame++) selector.Select(candidates, frame % 2 == 0, 600 + frame % 5, 400, 800, 500);
        Assert.AreEqual(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [TestMethod]
    public void NothingReachingTheViewChoosesNothing()
    {
        var selector = new RelightSelector(32);
        selector.Select([new RelightCandidate(5000, 5000, 100, true)], true, 0, 0, 800, 600);
        Assert.AreEqual(0, selector.Count); Assert.AreEqual(0, selector.InView); Assert.AreEqual(0, selector.CarriedInView);
    }
}
