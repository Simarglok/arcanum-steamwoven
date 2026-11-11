using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

// ARC-UI-134: the world-map window's hero lens (ledger WM140, WM141). Synthetic data only.
[TestClass]
public sealed class AreaWorldMapLensTests
{
    [TestMethod]
    public void The_lens_art_is_read_with_the_others_but_the_original_look_does_not_wait_for_it()
    {
        // WM140: the build registers art 198 with the view-window mode over the rectangle at 0x5C9AB8; the art is no condition of the window.
        Assert.AreEqual(198, AreaWorldMapArtLayout.LoadedArtNumbers[^1]);
        CollectionAssert.DoesNotContain(AreaWorldMapArtLayout.ArtNumbers.ToList(), AreaWorldMapArtLayout.LensArt);
        CollectionAssert.AreEqual(AreaWorldMapArtLayout.ArtNumbers.Append(198).ToArray(), AreaWorldMapArtLayout.LoadedArtNumbers.ToArray());
    }

    [TestMethod]
    public void The_lens_square_is_the_hole_of_the_frame_art_and_the_map_view_is_clear_of_it()
    {
        // WM140: (25, 24, 89, 89) in the window, the same rectangle the logbook's and the inventory's round views use at half size.
        Assert.AreEqual((25, 24, 89), AreaWorldMapArtLayout.Lens);
        Assert.IsTrue(AreaWorldMapArtLayout.LensContains(25, 24) && !AreaWorldMapArtLayout.LensContains(AreaWorldMapArtLayout.View.X, AreaWorldMapArtLayout.View.Y + 20),
            "the lens lies left of the map view");
    }
}
