using Arcanum.Data.Content;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-DEV-046: a start from the stored game folder opens map mode on the main map with the main menu.</summary>
[TestClass]
public sealed class GameFolderStartTests
{
    [TestMethod]
    public void A_game_folder_start_is_map_mode_on_the_main_map_with_the_main_menu()
    {
        var root = Path.Combine(Path.GetTempPath(), "Arcanum Folder");
        var arguments = TerrainViewerArguments.ForGameFolder(new(root, Path.Combine(root, "modules", "Arcanum.dat"),
            Path.Combine(root, "arcanum1.dat"), Path.Combine(root, "arcanum2.dat")));
        Assert.AreEqual(TerrainViewerMode.Area, arguments.Mode);
        Assert.AreEqual((root, TerrainViewerArguments.GameFolderStartMap, true), (arguments.GameRoot, arguments.AreaMapName, arguments.MainMenu));
        Assert.AreEqual((Path.Combine(root, "modules", "Arcanum.dat"), Path.Combine(root, "arcanum2.dat"), Path.Combine(root, "arcanum1.dat")),
            (arguments.ModulePath, arguments.ResourcePath, arguments.ActorResourcePath));
        Assert.AreEqual(TerrainViewerArguments.GameFolderSourceId, arguments.SourceId);
        Assert.IsNull(arguments.Hero);
        Assert.IsNull(arguments.PrototypePath);
        Assert.Throws<ArgumentException>(() => TerrainViewerArguments.ForGameFolder(new(" ", "m", "a", "t")), "the same checks as the command line");
    }
}
