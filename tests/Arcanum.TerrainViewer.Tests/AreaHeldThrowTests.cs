using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-063: the lift of a held item onto the game cursor (ledger IW18, TT1) and the held cursor's image (CU7), no game data.</summary>
[TestClass]
public sealed class AreaHeldThrowTests
{
    private static SessionGridMoveItem Facts(SessionItemType type, bool noDrop = false, params int[] points) =>
        new(new SessionItemKind(type, 9000, 0, 0), noDrop, points);

    [TestMethod]
    public void AnOrdinaryItemIsLifted()
    {
        Assert.AreEqual(GameSessionFailure.None, AreaHeldThrow.Lift(Facts(SessionItemType.Generic), worn: false));
        Assert.AreEqual(GameSessionFailure.None, AreaHeldThrow.Lift(Facts(SessionItemType.Weapon, false, SessionScriptPoint.Use), worn: true));
        // A wield-off script matters only for a worn item (the session's own rule).
        Assert.AreEqual(GameSessionFailure.None, AreaHeldThrow.Lift(Facts(SessionItemType.Weapon, false, SessionScriptPoint.WieldOff), worn: false));
    }

    [TestMethod]
    public void TheLiftRefusesInTheSessionsOrder()
    {
        // Gold first (the hero's coins are a counter), then the scripts, then the no-drop flag (the remove check).
        Assert.AreEqual(GameSessionFailure.AttackUnsupported, AreaHeldThrow.Lift(Facts(SessionItemType.Gold, true, SessionScriptPoint.Throw), false));
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, AreaHeldThrow.Lift(Facts(SessionItemType.Generic, true, SessionScriptPoint.Throw), false));
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, AreaHeldThrow.Lift(Facts(SessionItemType.Generic, false, SessionScriptPoint.RemoveItem), false));
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, AreaHeldThrow.Lift(Facts(SessionItemType.Armor, false, SessionScriptPoint.WieldOff), true));
        Assert.AreEqual(GameSessionFailure.ItemNotDroppable, AreaHeldThrow.Lift(Facts(SessionItemType.Generic, true), false));
        StringAssert.Contains(AreaHeldThrow.Refusal(GameSessionFailure.ItemNotDroppable, "Ring"), "Ring cannot be dropped");
        StringAssert.Contains(AreaHeldThrow.Refusal(GameSessionFailure.AttackUnsupported, "Coins"), "NOT READY");
    }

    [TestMethod]
    public void AnAmmunitionStackTakesTheStackPathOfTheLift()
    {
        // ARC-UI-086 (QW5): the stack path runs no remove check and no remove-item event, so only the throw script refuses a stack.
        Assert.AreEqual(GameSessionFailure.None, AreaHeldThrow.Lift(Facts(SessionItemType.Ammo), false));
        Assert.AreEqual(GameSessionFailure.None, AreaHeldThrow.Lift(Facts(SessionItemType.Ammo, true, SessionScriptPoint.RemoveItem), false), "no-drop and remove-item do not apply");
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, AreaHeldThrow.Lift(Facts(SessionItemType.Ammo, false, SessionScriptPoint.Throw), false));
    }

    private static byte[] Solid(int width, int height, byte r, byte a = 255) =>
        Enumerable.Range(0, width * height).SelectMany(_ => new[] { r, (byte)0, (byte)0, a }).ToArray();

    private static byte[] Pixel(AreaInterfaceFrame frame, int x, int y) => frame.Rgba.Skip((y * frame.Width + x) * 4).Take(4).ToArray();

    [TestMethod]
    public void TheCursorArtLiesOverTheItemArtTwoPixelsInside()
    {
        // The gauntlet's hot point (0, 3): its top-left is 3 pixels above the pointer, one above the item art's top-left (2 up, 2 left).
        var cursor = new AreaInterfaceFrame(4, 5, Solid(4, 5, 200), 0, 3);
        var held = AreaHeldCursor.Compose(10, 8, Solid(10, 8, 50), cursor)!;
        Assert.AreEqual((10, 9, 2, 3), (held.Width, held.Height, held.HotX, held.HotY));
        CollectionAssert.AreEqual(new byte[] { 0, 0, 0, 0 }, Pixel(held, 0, 0), "above the item art, left of the cursor art: transparent");
        CollectionAssert.AreEqual(new byte[] { 200, 0, 0, 255 }, Pixel(held, 2, 0), "the cursor art's top-left");
        CollectionAssert.AreEqual(new byte[] { 50, 0, 0, 255 }, Pixel(held, 0, 1), "the item art's top-left");
        CollectionAssert.AreEqual(new byte[] { 200, 0, 0, 255 }, Pixel(held, 5, 4), "the cursor art over the item art");
        CollectionAssert.AreEqual(new byte[] { 50, 0, 0, 255 }, Pixel(held, 9, 8), "the item art's bottom-right");
    }

    [TestMethod]
    public void TheComposeBlendsAlphaAndRefusesTooLargeOrEmptyArt()
    {
        var half = new AreaInterfaceFrame(1, 1, [100, 0, 0, 128], 0, 0);
        var held = AreaHeldCursor.Compose(4, 4, Solid(4, 4, 0, 0), half)!;
        CollectionAssert.AreEqual(new byte[] { 100, 0, 0, 128 }, Pixel(held, 2, 2), "over transparent the cursor's own pixel");
        held = AreaHeldCursor.Compose(4, 4, Solid(4, 4, 0), half)!;
        Assert.AreEqual(255, Pixel(held, 2, 2)[3]);
        Assert.AreEqual(50, Pixel(held, 2, 2)[0]);
        var cursor = new AreaInterfaceFrame(2, 2, Solid(2, 2, 1), 0, 0);
        Assert.IsNull(AreaHeldCursor.Compose(AreaHeldCursor.MaxSide + 1, 4, Solid(AreaHeldCursor.MaxSide + 1, 4, 1), cursor), "wider than Godot allows");
        Assert.AreEqual(AreaHeldCursor.MaxSide, AreaHeldCursor.Compose(AreaHeldCursor.MaxSide, 4, Solid(AreaHeldCursor.MaxSide, 4, 1), cursor)!.Width);
        Assert.IsNull(AreaHeldCursor.Compose(0, 4, [], cursor));
        Assert.IsNull(AreaHeldCursor.Compose(2, 2, new byte[3], cursor));
    }
}
