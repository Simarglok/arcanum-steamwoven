using Arcanum.Data.Area;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Area;

/// <summary>ARC-RENDER-033: the owner's art names of classes 4, 6 and 7, shared with the viewer (invented tables).</summary>
[TestClass]
public sealed class AreaObjectArtTests
{
    private static string? Name(AreaMappingRole role, int key) => (role, key) switch
    {
        (AreaMappingRole.Scenery, 2005) => "rubble.art",
        (AreaMappingRole.Container, 1003) => "chest.art",
        (AreaMappingRole.ItemGround, 1043) => "plate.art",
        (AreaMappingRole.ItemGround, 3) => "bad/name.art",
        _ => null,
    };

    [TestMethod]
    public void EachClassNamesItsFileAndSelection()
    {
        var scenery = AreaObjectArt.Resolve(0x40000000u | (2u << 6) | (5u << 19) | (3u << 11) | (4u << 14) | (1u << 4), Name);
        Assert.AreEqual(new AreaObjectArtPath("art/scenery/rubble.art", 0x40000000u | (2u << 6) | (5u << 19) | (3u << 11) | (4u << 14) | (1u << 4), 3, 3, 4, 1),
            scenery.Path);
        Assert.AreEqual("art/container/chest.art", AreaObjectArt.Resolve(0x70000000u | (1u << 6) | (3u << 19), Name).Path!.Path);
        // Item: number 3 + 20 × (subtype 2 + 50 × type 1) = 1043; frame and rotation are always 0, the lookup drops bits 4..5.
        var item = AreaObjectArt.Resolve(0x60000000u | 1u | (2u << 6) | (3u << 17) | (2u << 4), Name);
        Assert.AreEqual(("art/item/plate.art", 0, 0, 2, 0x60000000u | 1u | (2u << 6) | (3u << 17)),
            (item.Path!.Path, item.Path.Frame, item.Path.Group, item.Path.Palette, item.Path.Lookup));
    }

    [TestMethod]
    public void OtherClassesUnnamedNumbersAndUnsafeNamesAreRefused()
    {
        Assert.AreEqual(AreaObjectArtStatus.OtherClass, AreaObjectArt.Resolve(0x20000000u, Name).Status);
        Assert.AreEqual(AreaObjectArtStatus.UnnamedNumber, AreaObjectArt.Resolve(0x40000000u, Name).Status);
        Assert.AreEqual(AreaObjectArtStatus.Unsupported, AreaObjectArt.Resolve(0x60000000u | (1u << 12), Name).Status);
        Assert.AreEqual(AreaObjectArtStatus.UnsafeName, AreaObjectArt.Resolve(0x60000000u | (3u << 17), Name).Status);
        Assert.IsNotNull(AreaObjectArt.ReadNameTable("{3}{sword.art}\n"u8));
        Assert.IsNull(AreaObjectArt.ReadNameTable(""u8), "an empty table");
    }
}
