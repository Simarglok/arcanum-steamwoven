using Arcanum.Data.Area;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-057 hovered-object window and highlight decisions over invented records (no original data).</summary>
[TestClass]
public sealed class AreaHoverWindowTests
{
    private static AreaObjectKey Key(int index) => new(new string('a', 64), 1, AreaSourceKind.Mobile, 1, index, index * 100L, new string((char)('a' + index), 64), 100);

    private static AreaFieldValue Field(int slot, long value) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, value, [], null);

    private static AreaObjectRecord Row(int type, uint flags = 0, int prototype = 0, long? art = null) =>
        new(Key(type), 0, type, prototype, "p", "p", "r", "s", AreaScope.Core, null, 10, 20, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally,
            [], [Field(AreaHoverIndex.FlagsSlot, flags), .. art is { } a ? new[] { Field(0, a) } : []], true, true, true);

    [TestMethod]
    public void EachObjectTypeGetsTheWindowOf0x553BE0()
    {
        var kinds = Enumerable.Range(0, 19).Select(type => AreaHoverWindow.Kind(type)).ToArray();
        CollectionAssert.AreEqual(new[]
        {
            AreaHoverWindowKind.None, AreaHoverWindowKind.Portal, AreaHoverWindowKind.Container, AreaHoverWindowKind.Scenery, AreaHoverWindowKind.None,
            AreaHoverWindowKind.Item, AreaHoverWindowKind.Item, AreaHoverWindowKind.Item, AreaHoverWindowKind.Item, AreaHoverWindowKind.Item,
            AreaHoverWindowKind.Item, AreaHoverWindowKind.Item, AreaHoverWindowKind.Item, AreaHoverWindowKind.Item, AreaHoverWindowKind.Item,
            AreaHoverWindowKind.Critter, AreaHoverWindowKind.Critter, AreaHoverWindowKind.Trap, AreaHoverWindowKind.None,
        }, kinds);
    }

    [TestMethod]
    public void OnlyVisiblePortalsContainersSceneryAndItemsAreHighlighted()
    {
        foreach (var type in new[] { 1, 2, 3, 5, 9, 14 }) Assert.IsTrue(AreaHoverWindow.Highlighted(Row(type)), $"type {type}");
        foreach (var type in new[] { 0, 4, 15, 16, 17 }) Assert.IsFalse(AreaHoverWindow.Highlighted(Row(type)), $"type {type}: no second draw (rings for critters)");
        Assert.IsFalse(AreaHoverWindow.Highlighted(Row(2, 0x100)), "OF_DONTDRAW");
        Assert.IsFalse(AreaHoverWindow.Highlighted(Row(9, 0x200)), "OF_INVISIBLE");
        Assert.IsTrue(AreaHoverWindow.Highlighted(Row(3, 0x800 | 0x4)), "other flags do not matter");
    }

    [TestMethod]
    public void ImagesFollow0x553D10()
    {
        Assert.AreEqual(AreaHoverWindow.DoorImage, AreaHoverWindow.Image(Row(1, art: 0x3000_0400)), "a door: portal art with bit 0x400");
        Assert.AreEqual(AreaHoverWindow.WindowImage, AreaHoverWindow.Image(Row(1, art: 0x3000_0000)), "a window: portal art without it");
        Assert.AreEqual(AreaHoverWindow.WindowImage, AreaHoverWindow.Image(Row(1, art: 0x4000_0400)), "another art class");
        Assert.AreEqual(AreaHoverWindow.WindowImage, AreaHoverWindow.Image(Row(1)), "no art field");
        Assert.AreEqual(835, AreaHoverWindow.Image(Row(2, prototype: 3075)), "a container by its description (prototype less 20)");
        Assert.AreEqual(435, AreaHoverWindow.Image(Row(2, prototype: 3087)), "any other container");
        Assert.AreEqual(437, AreaHoverWindow.Image(Row(3)));
        Assert.IsNull(AreaHoverWindow.Image(Row(5)), "an item's is its type icon");
        Assert.IsNull(AreaHoverWindow.Image(Row(16)), "a critter's is its portrait or its flags' or race's image");
        CollectionAssert.IsSubsetOf(new[] { 786, 436, 437, 435, 835, 375, 384 }, AreaHoverWindow.ImageNumbers.ToArray());
        CollectionAssert.IsSubsetOf(AreaHoverWindow.ImageNumbers.ToArray(), AreaMessageLayout.ArtNumbers.ToArray());
    }

    [TestMethod]
    public void TheWindowDrawsTheImageAndTheName()
    {
        Assert.IsNull(AreaHoverWindow.Window(AreaHoverWindowKind.Scenery, "", 437), "an empty scenery name draws nothing (0x555D90)");
        Assert.IsNull(AreaHoverWindow.Window(AreaHoverWindowKind.Item, "Sword", null), "an item has its item window");
        Assert.IsNull(AreaHoverWindow.Window(AreaHoverWindowKind.Trap, "Trap", null), "a trap's name is a hover text");
        var scenery = AreaHoverWindow.Window(AreaHoverWindowKind.Scenery, "Something Big", 437)!;
        Assert.IsTrue(scenery is { Kind: AreaMessageKind.Object, Icon: 437, NameBlock: true, Stored: false });
        var door = AreaHoverWindow.Window(AreaHoverWindowKind.Portal, "Door", 786)!;
        static string Of(AreaMessageDraw draw) => $"{draw.Icon?.ToString() ?? "-"} {draw.Widens}: " + string.Join(" | ", draw.Texts.Select(t => $"{t.Rect} {t.Font} '{t.Text}'"));
        Assert.AreEqual("437 False: (290, 504, 291, 55) Title 'Something Big'", Of(AreaMessageLayout.Draw(scenery)));
        Assert.AreEqual("786 False: (290, 504, 291, 19) Title 'Door'", Of(AreaMessageLayout.Draw(door)));
        Assert.AreEqual("- False: (290, 504, 291, 19) Title 'Virgil'", Of(AreaMessageLayout.Draw(AreaHoverWindow.Window(AreaHoverWindowKind.Critter, "Virgil", null)!)));

        var log = new AreaMessageLog();
        log.Post(AreaMessageEntry.Text(AreaMessageKind.Message, "Heavy"), 1_000);
        Assert.IsTrue(log.ShowItem(door), "drawn at once, never stored");
        Assert.AreSame(door, log.Shown);
        Assert.AreEqual(1, log.Count);
        log.ClearHover();
        Assert.AreEqual("Heavy", log.Shown?.Title, "leaving shows the viewed entry again (ML7)");
        Assert.ThrowsExactly<ArgumentException>(() => log.Post(door, 9_000));
        Assert.ThrowsExactly<ArgumentException>(() => AreaMessageEntry.Text(AreaMessageKind.Object, "x"));
        log.EnterPane();
        Assert.IsFalse(log.ShowItem(door), "a pane takes only the name");
    }
}
