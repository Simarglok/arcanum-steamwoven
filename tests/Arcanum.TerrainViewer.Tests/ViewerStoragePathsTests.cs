using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-DEV-032: an automated run never uses the player's settings or saves.</summary>
[TestClass]
public sealed class ViewerStoragePathsTests
{
    private const string User = "user://world_diagnostic_zoom_v1.json";

    [TestMethod]
    public void A_test_path_wins_an_automated_run_is_isolated_and_only_an_ordinary_run_uses_the_players_file()
    {
        var asked = 0;
        string Isolated() { asked++; return "/tmp/isolated/world_diagnostic_zoom_v1.json"; }
        Assert.AreEqual("/tmp/test/zoom.cfg", ViewerStoragePaths.Choose("/tmp/test/zoom.cfg", true, User, Isolated));
        Assert.AreEqual("/tmp/test/zoom.cfg", ViewerStoragePaths.Choose("/tmp/test/zoom.cfg", false, User, Isolated));
        Assert.AreEqual(0, asked, "a test path needs no isolated folder");
        Assert.AreEqual("/tmp/isolated/world_diagnostic_zoom_v1.json", ViewerStoragePaths.Choose(null, true, User, Isolated));
        Assert.AreEqual("/tmp/isolated/world_diagnostic_zoom_v1.json", ViewerStoragePaths.Choose("  ", true, User, Isolated), "a blank test path is none");
        Assert.AreEqual(User, ViewerStoragePaths.Choose(null, false, User, Isolated));
        Assert.AreEqual(User, ViewerStoragePaths.Choose("", false, User, Isolated));
    }

    [TestMethod]
    public void The_isolated_file_keeps_the_players_file_name()
    {
        Assert.AreEqual("world_diagnostic_zoom_v1.json", ViewerStoragePaths.FileName(User));
        Assert.AreEqual("world_diagnostic_input_v1.json", ViewerStoragePaths.FileName("user://world_diagnostic_input_v1.json"));
        Assert.ThrowsExactly<ArgumentException>(() => ViewerStoragePaths.FileName("user://saves/"));
        Assert.AreEqual("saves/map", ViewerStoragePaths.RelativeUserPath("user://saves/map"));
        Assert.AreEqual("saves", ViewerStoragePaths.RelativeUserPath("user://saves"));
        Assert.ThrowsExactly<ArgumentException>(() => ViewerStoragePaths.RelativeUserPath("res://saves"));
        Assert.ThrowsExactly<ArgumentException>(() => ViewerStoragePaths.RelativeUserPath("user://"));
    }
}
