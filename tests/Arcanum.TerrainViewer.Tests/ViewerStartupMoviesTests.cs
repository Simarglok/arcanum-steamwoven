using Arcanum.TerrainViewer.Core;

namespace Arcanum.TerrainViewer.Tests;

[TestClass]
public sealed class ViewerStartupMoviesTests
{
    [TestMethod]
    public void TheSkipFlagIsTakenOutOfTheUserArguments()
    {
        var alone = new List<string> { ViewerStartupMovies.SkipFlag };
        Assert.IsTrue(ViewerStartupMovies.TakeSkip(alone));
        Assert.AreEqual(0, alone.Count, "a start with only the flag stays a start from the game folder");
        var mixed = new List<string> { "--module", "m.dat", ViewerStartupMovies.SkipFlag, "--main-menu", "true", ViewerStartupMovies.SkipFlag };
        Assert.IsTrue(ViewerStartupMovies.TakeSkip(mixed));
        CollectionAssert.AreEqual(new[] { "--module", "m.dat", "--main-menu", "true" }, mixed);
        Assert.IsFalse(ViewerStartupMovies.TakeSkip(mixed), "without the flag the movies play");
    }
}
