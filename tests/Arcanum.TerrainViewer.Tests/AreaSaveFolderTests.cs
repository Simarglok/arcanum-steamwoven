using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-DEV-407: the save folder follows the launch map, not the loaded scene (bug reports of an empty Load Game list at the start).</summary>
[TestClass]
public sealed class AreaSaveFolderTests
{
    [TestMethod]
    public void LazyStartBeforeTheSceneStandsReadsTheMapFolder() => Assert.IsTrue(AreaSaveFolder.UsesMapFolder(null, "Arcanum1-024-fixed"));

    [TestMethod]
    public void LoadedMapUsesTheMapFolder()
    {
        Assert.IsTrue(AreaSaveFolder.UsesMapFolder("Arcanum1-024-fixed", "Arcanum1-024-fixed"));
        Assert.IsTrue(AreaSaveFolder.UsesMapFolder("shopmap", null));
    }

    [TestMethod]
    public void CrashSiteModeKeepsItsOwnFolder() => Assert.IsFalse(AreaSaveFolder.UsesMapFolder(null, null));
}
