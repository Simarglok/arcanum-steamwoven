using Arcanum.Data.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.World.Tests;

/// <summary>ARC-RENDER-1170: the movie table <c>movies\movies.mes</c> (docs/research/bink-movies.md section 1): a number gives its row's file
/// name, 0 and unknown numbers play nothing, and only a plain <c>.bik</c> file name is accepted. The synthetic rows follow the real table's
/// shape; the opt-in real-data test reads the installed one.</summary>
[TestClass]
public sealed class AreaMovieTableTests
{
    private static AreaMovieTable Table() => AreaMovieTable.From(number => number switch
    {
        0 => "reserved - do not use", 1 => "00069.bik", 2 => "Movie_2.BIK", 3 => "../escape.bik", 4 => "notmovie.txt", 5 => "", 6 => "dir/x.bik", _ => null,
    }, "synthetic");

    [TestMethod]
    public void A_number_gives_its_rows_file_name_and_the_rest_play_nothing()
    {
        var table = Table();
        Assert.AreEqual("00069.bik", table.Name(1));
        Assert.AreEqual("Movie_2.BIK", table.Name(2));
        Assert.IsNull(table.Name(0), "movie 0 is reserved");
        Assert.IsNull(table.Name(-1));
        Assert.IsNull(table.Name(99), "a number with no row plays nothing");
        Assert.IsNull(table.Name(3), "a name that climbs out of the folder");
        Assert.IsNull(table.Name(4), "not a .bik file");
        Assert.IsNull(table.Name(5));
        Assert.IsNull(table.Name(6), "a name with a folder");
        Assert.IsTrue(table.Loaded);
    }

    [TestMethod]
    public void A_missing_table_plays_nothing_and_says_why()
    {
        var none = AreaMovieTable.None("no game folder");
        Assert.IsFalse(none.Loaded);
        Assert.IsNull(none.Name(1));
        Assert.AreEqual("no game folder", none.Detail);
    }

    [TestMethod, TestCategory("RealData")]
    public void The_installed_table_names_the_eight_movies_and_their_files_are_found()
    {
        var root = Environment.GetEnvironmentVariable("DAT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("DAT_ROOT is required.");
        var discovery = ContentMountPlan.Discover(root);
        using var stack = ContentStack.Open(discovery.Plan!).Stack!;
        var table = AreaMovieTable.Load(stack);
        Assert.IsTrue(table.Loaded, table.Detail);
        var cache = Path.Combine(Path.GetTempPath(), "arc-movie-cache-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            for (var number = 1; number <= 8; number++)
            {
                var path = table.Path(stack, number, cache);
                Console.WriteLine($"ARC-RENDER-1170 movie {number} name={table.Name(number)} found={path is not null}");
                Assert.IsNotNull(table.Name(number), $"movie {number} has a row");
                Assert.IsNotNull(path, $"movie {number} resolves");
                Assert.IsTrue(File.Exists(path));
            }
            Assert.IsNull(table.Name(0));
            Assert.IsNull(table.Name(9));
        }
        finally { if (Directory.Exists(cache)) Directory.Delete(cache, recursive: true); }
    }
}
