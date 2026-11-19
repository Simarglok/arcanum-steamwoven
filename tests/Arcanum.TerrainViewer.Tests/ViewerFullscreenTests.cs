using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-2771: which fullscreen kind each platform gets.</summary>
[TestClass]
public sealed class ViewerFullscreenTests
{
    [TestMethod]
    public void MacOsGetsTheExclusiveModeSoTheDockAndMenuBarStayHidden()
    {
        Assert.IsTrue(ViewerFullscreen.UsesExclusiveMode("macOS"));
        foreach (var other in new[] { "Windows", "Linux", "iOS", "Android", "macos", "", null }) Assert.IsFalse(ViewerFullscreen.UsesExclusiveMode(other), other ?? "null");
    }
}
