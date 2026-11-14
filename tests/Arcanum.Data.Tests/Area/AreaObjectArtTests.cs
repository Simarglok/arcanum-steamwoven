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
        (AreaMappingRole.Portal, 7) => "Door7U1.art extra words",
        (AreaMappingRole.Portal, 1008) => "Wind8U1.art",
        (AreaMappingRole.ItemGround, 1043) => "plate.art",
        (AreaMappingRole.ItemGround, 3) => "bad/name.art",
        (AreaMappingRole.ItemInventory, 1043) => "plate_inv.art",
        (AreaMappingRole.ItemInventory, 2304) => "helm_inv.art",
        (AreaMappingRole.ItemPaper, 3000) => "gold_paper.art",
        (AreaMappingRole.ItemPaper, 1002) => "arrow_paper.art",
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
    public void ADoorOrWindowNamesItsPortalFileByTheBuildersRule()
    {
        // ARC-SCRIPT-1778: number bits 19..27 (a window adds 1001), the first name token, rotation bits 11..13, frame bits 14..18, one group; the busted bit 9 turns the sixth-from-last letter into D.
        var door = AreaObjectArt.Resolve((3u << 28) | (7u << 19) | (1u << 10) | (3u << 14) | (2u << 11) | (1u << 4), Name);
        Assert.AreEqual(new AreaObjectArtPath("art/portal/Door7U1.art", (3u << 28) | (7u << 19) | (1u << 10) | (3u << 14) | (2u << 11) | (1u << 4), 2, 0, 3, 1), door.Path);
        Assert.AreEqual("art/portal/Wind8U1.art", AreaObjectArt.Resolve((3u << 28) | (7u << 19), Name).Path!.Path, "a window (bit 10 clear) names the entry number + 1001");
        var busted = AreaObjectArt.Resolve((3u << 28) | (7u << 19) | (1u << 10) | 0x200u, Name);
        Assert.AreEqual("art/portal/Door7D1.art", busted.Path?.Path, "the busted bit turns the U of Door7U1.art into D");
        Assert.AreEqual(AreaObjectArtStatus.Unsupported, AreaObjectArt.Resolve((3u << 28) | (7u << 19) | (1u << 10) | (1u << 6), Name).Status, "bits 6..8 are not admitted");
        Assert.AreEqual(AreaObjectArtStatus.UnnamedNumber, AreaObjectArt.Resolve((3u << 28) | (9u << 19) | (1u << 10), Name).Status);
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

    [TestMethod]
    public void ItemInventoryArtNamesItsFileInTheInventoryTable()
    {
        // ARC-UI-032 (0x4EC290): place bits 12..13 = 1; the same number as a ground art id: 3 + 20 × (2 + 50 × 1) = 1043.
        var aid = 0x60000000u | 1u | (2u << 6) | (3u << 17) | (1u << 12) | (2u << 4);
        var inventory = AreaObjectArt.ResolveItemInventory(aid, Name);
        Assert.AreEqual(new AreaObjectArtPath("art/item/plate_inv.art", aid & ~0x30u, 0, 0, 0, 2), inventory.Path);
        Assert.AreEqual("art/item/item_inven.mes", AreaObjectArt.MappingPath(AreaMappingRole.ItemInventory));
        // Armour (type 2) with coverage 1: 4 + 20 × (0 + 50 × 2) + 20 × (5 × 1 + 10) = 2304.
        Assert.AreEqual("art/item/helm_inv.art",
            AreaObjectArt.ResolveItemInventory(0x60000000u | 2u | (4u << 17) | (1u << 14) | (1u << 12), Name).Path!.Path);
        Assert.AreEqual(AreaObjectArtStatus.Unsupported, AreaObjectArt.ResolveItemInventory(aid & ~(3u << 12), Name).Status, "a ground art id");
        Assert.AreEqual(AreaObjectArtStatus.Unsupported, AreaObjectArt.ResolveItemInventory(aid | (3u << 12), Name).Status, "a schematic art id");
        Assert.AreEqual(AreaObjectArtStatus.OtherClass, AreaObjectArt.ResolveItemInventory(0x40000000u, Name).Status);
        Assert.AreEqual(AreaObjectArtStatus.UnnamedNumber, AreaObjectArt.ResolveItemInventory(0x60000000u | (1u << 12) | (9u << 17), Name).Status);
        Assert.AreEqual(AreaObjectArtStatus.Unsupported, AreaObjectArt.Resolve(aid, Name).Status, "the area owner keeps refusing inventory art");
    }

    [TestMethod]
    public void ItemPaperArtNamesItsFileInThePaperTable()
    {
        // ARC-UI-1884 (0x4EC290): place bits 12..13 = 2; gold is number 0 of type 3: 0 + 20 × (0 + 50 × 3) = 3000, ammunition type 2: 2 + 20 × 50 = 1002.
        var gold = AreaObjectArt.ResolveItemPaper(0x60002003u, Name);
        Assert.AreEqual(new AreaObjectArtPath("art/item/gold_paper.art", 0x60002003u, 0, 0, 0, 0), gold.Path);
        Assert.AreEqual("art/item/arrow_paper.art", AreaObjectArt.ResolveItemPaper(0x60002001u | (2u << 17), Name).Path!.Path);
        Assert.AreEqual("art/item/item_paper.mes", AreaObjectArt.MappingPath(AreaMappingRole.ItemPaper));
        Assert.AreEqual(AreaObjectArtStatus.Unsupported, AreaObjectArt.ResolveItemPaper(0x60001003u, Name).Status, "an inventory art id");
        Assert.AreEqual(AreaObjectArtStatus.Unsupported, AreaObjectArt.ResolveItemInventory(0x60002003u, Name).Status, "the inventory resolver keeps leaving the paper place out");
        Assert.AreEqual(AreaObjectArtStatus.OtherClass, AreaObjectArt.ResolveItemPaper(0x40002003u, Name).Status);
        Assert.AreEqual(AreaObjectArtStatus.UnnamedNumber, AreaObjectArt.ResolveItemPaper(0x60002003u | (9u << 17), Name).Status);
    }
}
