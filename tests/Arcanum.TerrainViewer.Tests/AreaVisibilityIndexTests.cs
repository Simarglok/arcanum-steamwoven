using Arcanum.Data.Terrain;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

[TestClass]
public sealed class AreaVisibilityIndexTests
{
    // Every command whose destination meets the view (closed rectangles, so edge contact
    // counts) is a candidate, candidates are ascending, and a large view returns all.
    [TestMethod]
    public void CandidatesAreAnAscendingSupersetOfTheCommandsMeetingTheView()
    {
        var random = new Random(17);
        var rects = Enumerable.Range(0, 3000).Select(_ => new TerrainRect(random.Next(-20_000, 20_000), random.Next(-12_000, 12_000), random.Next(0, 1200), random.Next(0, 600))).ToArray();
        var index = new AreaVisibilityIndex(rects);
        for (var trial = 0; trial < 400; trial++)
        {
            double left = random.Next(-22_000, 22_000) + random.NextDouble(), top = random.Next(-14_000, 14_000) + random.NextDouble();
            double right = left + random.Next(0, 4000) + random.NextDouble(), bottom = top + random.Next(0, 2500) + random.NextDouble();
            var candidates = index.Candidates(left, top, right, bottom);
            CollectionAssert.AreEqual(candidates.OrderBy(i => i).ToArray(), candidates.ToArray(), "ascending");
            Assert.AreEqual(candidates.Count, candidates.Distinct().Count(), "distinct");
            var set = candidates.ToHashSet();
            for (var i = 0; i < rects.Length; i++)
            {
                var r = rects[i];
                var meets = r.X <= right && r.X + r.Width >= left && r.Y <= bottom && r.Y + r.Height >= top;
                if (meets) Assert.IsTrue(set.Contains(i), $"trial {trial}: command {i} {r} meets ({left},{top})-({right},{bottom}) but is missing");
            }
        }
        Assert.AreEqual(rects.Length, index.Candidates(-1e6, -1e6, 1e6, 1e6).Count, "a view over most cells returns every command");
        Assert.AreEqual(rects.Length, index.Candidates(double.NaN, 0, 1, 1).Count, "a non-finite view never skips commands");
        Assert.AreEqual(0, index.Candidates(10, 10, 5, 20).Count, "an empty view has no candidates");
    }
}
