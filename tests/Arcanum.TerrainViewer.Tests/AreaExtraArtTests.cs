using System.Text;
using Arcanum.Data.Content;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-RENDER-033: art ids no T0 body drew, read from an invented game folder's content stack (no original data).</summary>
[TestClass]
public sealed class AreaExtraArtTests
{
    // Item 3 (type 0, subtype 0, number 3) names sword.art; scenery 2005 (bits 6..10 = 2, bits 19..27 = 5) names rubble.art;
    // scenery 2006 names a file the stack does not hold.
    private const uint Sword = 0x60000000u | (3u << 17), Rubble = 0x40000000u | (2u << 6) | (5u << 19), Lost = 0x40000000u | (2u << 6) | (6u << 19);
    private const uint Unnamed = 0x60000000u | (9u << 17);

    private static void WithStack(Action<ContentStack> test)
    {
        var root = Directory.CreateTempSubdirectory("arc-render-033-");
        try
        {
            File.WriteAllBytes(Path.Combine(root.FullName, "tig.dat"), AreaOwnerFixture.Dat());
            Directory.CreateDirectory(Path.Combine(root.FullName, "modules"));
            File.WriteAllBytes(Path.Combine(root.FullName, "modules", "Syn.dat"), AreaOwnerFixture.Dat(
                ("art\\item\\item_ground.mes", Encoding.ASCII.GetBytes("{3}{sword.art}\n")),
                ("art\\item\\sword.art", AreaOwnerFixture.Art(6, 4, 2, 3, true)),
                ("art\\scenery\\rubble.art", AreaOwnerFixture.Art(10, 8, 5, 7, true)),
                ("art\\scenery\\scenery.mes", Encoding.ASCII.GetBytes("{2005}{rubble.art}\n{2006}{lost.art}\n"))));
            var opened = ContentStack.Open(ContentMountPlan.Discover(root.FullName, "Syn").Plan!);
            Assert.AreEqual(ContentStackStatus.Ready, opened.Status, opened.Detail);
            test(opened.Stack!);
        }
        finally { root.Delete(true); }
    }

    [TestMethod]
    public void AnItemsGroundArtAndASceneryArtAreReadOnce()
    {
        WithStack(stack =>
        {
            using var art = new AreaExtraArt(stack);
            var sword = art.Load(Sword);
            Assert.AreEqual(AreaExtraArtStatus.Ready, sword.Status, sword.Detail);
            var frame = sword.Frame!;
            Assert.AreEqual((6, 4, 2, 3, Sword), (frame.Visual.Width, frame.Visual.Height, frame.Visual.HotX, frame.Visual.HotY, frame.Visual.RawArt));
            Assert.AreEqual(6 * 4 * 4, frame.Frame.Rgba.Length);
            Assert.AreEqual(AreaExtraArt.VisualKey(Sword), frame.Visual.Key);
            Assert.AreSame(sword, art.Load(Sword), "read once");
            var rubble = art.Load(Rubble);
            Assert.AreEqual((AreaExtraArtStatus.Ready, 10, 8), (rubble.Status, rubble.Frame!.Visual.Width, rubble.Frame.Visual.Height));
            Assert.AreNotEqual(frame.Key, rubble.Frame.Key);
            Assert.AreEqual(2, art.Loads.Count);
        });
    }

    [TestMethod]
    public void ArtThatDoesNotResolveOrReadIsReportedNotGuessed()
    {
        WithStack(stack =>
        {
            using var art = new AreaExtraArt(stack, new(MaxArts: 3));
            Assert.AreEqual(AreaExtraArtStatus.Unresolved, art.Load(Unnamed).Status, "a number the table does not name");
            Assert.AreEqual(AreaExtraArtStatus.MissingFile, art.Load(Lost).Status);
            Assert.AreEqual(AreaExtraArtStatus.Unresolved, art.Load(0x20000000u).Status, "a critter art is no class this reads");
            Assert.AreEqual(AreaExtraArtStatus.LimitExceeded, art.Load(Sword).Status, "past the art id limit");
        });
    }
}
