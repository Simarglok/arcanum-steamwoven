using Arcanum.Input;
using GamepadLab.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

[TestClass]
public sealed class ActiveDeviceTrackerTests
{
    private static Vec2 P(double x, double y) => new(x, y);

    [TestMethod]
    public void PadButtonBecomesActiveOnlyAfterTheHysteresis()
    {
        var t = new ActiveDeviceTracker();
        t.Observe(DeviceSignal.PadButton(10, 4));
        Assert.AreEqual(ActiveDevice.KeyboardMouse, t.Active);
        t.Update(10.2); Assert.AreEqual(ActiveDevice.KeyboardMouse, t.Active);
        t.Update(10.25); Assert.AreEqual(ActiveDevice.Pad, t.Active);
        Assert.AreEqual(4, t.LastPadDeviceId, "device 0 is never assumed");
    }

    [TestMethod]
    public void StickDriftBelowTheDeadzoneIsIgnoredButAFullStickCounts()
    {
        var t = new ActiveDeviceTracker();
        for (var i = 0; i < 100; i++) t.Observe(DeviceSignal.PadAxis(i * .01, .2 + (i % 2) * .05));
        t.Update(5); Assert.AreEqual(ActiveDevice.KeyboardMouse, t.Active);
        Assert.AreEqual(-1, t.LastPadDeviceId, "drift leaves no trace");
        t.Observe(DeviceSignal.PadAxis(5, -.9, 2)); t.Update(5.3);
        Assert.AreEqual(ActiveDevice.Pad, t.Active); Assert.AreEqual(2, t.LastPadDeviceId);
        foreach (var bad in new[] { double.NaN, double.PositiveInfinity })
        {
            var fresh = new ActiveDeviceTracker();
            fresh.Observe(DeviceSignal.PadAxis(6, bad)); fresh.Update(7);
            Assert.AreEqual(ActiveDevice.KeyboardMouse, fresh.Active, bad.ToString());
        }
    }

    [TestMethod]
    public void MouseMotionNeedsTwelvePixelsWithinOneHundredMilliseconds()
    {
        var t = new ActiveDeviceTracker(initial: ActiveDevice.Pad);
        // Slow drift: 1 px every 50 ms never exceeds 12 px in a 100 ms window.
        for (var i = 0; i < 40; i++) t.Observe(DeviceSignal.MouseMotion(i * .05, P(i, 0), P(1, 0)));
        t.Update(5); Assert.AreEqual(ActiveDevice.Pad, t.Active);
        // A quick flick: 15 px in 60 ms.
        t.Observe(DeviceSignal.MouseMotion(6, P(100, 0), P(5, 0)));
        t.Observe(DeviceSignal.MouseMotion(6.03, P(110, 0), P(5, 0)));
        t.Observe(DeviceSignal.MouseMotion(6.06, P(115, 0), P(5, 0)));
        t.Update(6.4); Assert.AreEqual(ActiveDevice.KeyboardMouse, t.Active);
    }

    [TestMethod]
    public void ClickAndKeyActivateTheMouseAndKeyboard()
    {
        foreach (var signal in new[] { DeviceSignal.Key(1), DeviceSignal.MouseClick(1, P(10, 10)) })
        {
            var t = new ActiveDeviceTracker(initial: ActiveDevice.Pad);
            t.Observe(signal); t.Update(1.25);
            Assert.AreEqual(ActiveDevice.KeyboardMouse, t.Active);
        }
    }

    [TestMethod]
    public void TrackpadMotionAfterPadUseKeepsPadPromptsAndShowsTheCursor()
    {
        var t = new ActiveDeviceTracker();
        t.Observe(DeviceSignal.PadButton(1)); t.Update(1.3);
        Assert.AreEqual(ActiveDevice.Pad, t.Active); Assert.IsFalse(t.CursorVisible(1.3));
        t.Observe(DeviceSignal.MouseMotion(2, P(0, 0), P(30, 0))); t.Update(2.5);
        Assert.AreEqual(ActiveDevice.Pad, t.Active);
        Assert.IsTrue(t.CursorVisible(2.5));
        Assert.IsFalse(t.CursorVisible(5.1), "the cursor fades after the pointer-from-pad time");
        // Motion after the 2 s window, with no click or key, is the mouse.
        t.Observe(DeviceSignal.MouseMotion(3.5, P(30, 0), P(30, 0))); t.Update(3.8);
        Assert.AreEqual(ActiveDevice.KeyboardMouse, t.Active);
    }

    [TestMethod]
    public void ClickOrKeyDuringPointerFromPadStillSwitches()
    {
        var t = new ActiveDeviceTracker();
        t.Observe(DeviceSignal.PadButton(1)); t.Update(1.3);
        t.Observe(DeviceSignal.Key(1.5)); t.Update(1.8);
        Assert.AreEqual(ActiveDevice.KeyboardMouse, t.Active);
    }

    [TestMethod]
    public void HysteresisDropsAFlickerBackToTheActiveDevice()
    {
        var t = new ActiveDeviceTracker();
        var changes = new List<(ActiveDevice, ActiveDevice)>();
        t.Changed += (a, b) => changes.Add((a, b));
        t.Observe(DeviceSignal.PadButton(1));
        t.Observe(DeviceSignal.Key(1.1)); // back to the active device within 250 ms: the pending pad change is dropped
        t.Update(2);
        Assert.AreEqual(ActiveDevice.KeyboardMouse, t.Active); Assert.AreEqual(0, changes.Count);
        t.Observe(DeviceSignal.PadButton(3)); t.Observe(DeviceSignal.PadButton(3.3));
        Assert.AreEqual(ActiveDevice.Pad, t.Active);
        CollectionAssert.AreEqual(new[] { (ActiveDevice.KeyboardMouse, ActiveDevice.Pad) }, changes);
    }

    [TestMethod]
    public void TouchIsActiveAndAJumpedClickWithNoMotionIsEmulatedTouch()
    {
        var t = new ActiveDeviceTracker();
        t.Observe(DeviceSignal.Touch(1, P(5, 5))); t.Update(1.3);
        Assert.AreEqual(ActiveDevice.Touch, t.Active);

        var e = new ActiveDeviceTracker();
        e.Observe(DeviceSignal.MouseMotion(0, P(10, 10), P(1, 0)));
        e.Observe(DeviceSignal.MouseClick(0.1, P(10, 10))); // an ordinary click at the cursor: mouse
        e.Update(0.5); Assert.AreEqual(ActiveDevice.KeyboardMouse, e.Active);
        e.Observe(DeviceSignal.MouseClick(2, P(600, 300))); // a click far away with no motion before it: touch
        e.Update(2.3); Assert.AreEqual(ActiveDevice.Touch, e.Active);
        e.Observe(DeviceSignal.MouseMotion(4, P(600, 300), P(40, 0))); e.Update(4.3);
        Assert.AreEqual(ActiveDevice.KeyboardMouse, e.Active, "real motion returns to the mouse");

        var off = new ActiveDeviceTracker(new ActiveDeviceOptions(EmulatedTouchClicks: false));
        off.Observe(DeviceSignal.MouseClick(0, P(0, 0))); off.Observe(DeviceSignal.MouseClick(1, P(500, 500))); off.Update(2);
        Assert.AreEqual(ActiveDevice.KeyboardMouse, off.Active);
    }

    [TestMethod]
    public void TheFirstClickOfASessionIsTheMouse()
    {
        var t = new ActiveDeviceTracker(initial: ActiveDevice.Pad);
        t.Observe(DeviceSignal.MouseClick(1, P(900, 400))); t.Update(1.3);
        Assert.AreEqual(ActiveDevice.KeyboardMouse, t.Active);
    }

    [TestMethod]
    public void AMouseEchoOfARealTouchDoesNotFlipTheDevice()
    {
        var t = new ActiveDeviceTracker(initial: ActiveDevice.Pad);
        t.Observe(DeviceSignal.Touch(1, P(5, 5)));
        t.Observe(DeviceSignal.MouseClick(1.01, P(5, 5)));
        t.Observe(DeviceSignal.MouseMotion(1.02, P(60, 5), P(55, 0))); // an echoed drag
        t.Update(1.3); Assert.AreEqual(ActiveDevice.Touch, t.Active);
        t.Update(2); Assert.AreEqual(ActiveDevice.Touch, t.Active);
        t.Observe(DeviceSignal.MouseMotion(3, P(100, 5), P(40, 0))); t.Update(3.3);
        Assert.AreEqual(ActiveDevice.KeyboardMouse, t.Active, "after the echo window the mouse is the mouse");
    }

    [TestMethod]
    public void AChangeThatStoodLongEnoughSurvivesALateUpdate()
    {
        var t = new ActiveDeviceTracker();
        t.Observe(DeviceSignal.PadButton(1)); t.Observe(DeviceSignal.Key(1.26)); // no Update between them
        Assert.AreEqual(ActiveDevice.Pad, t.Active, "the pad change had stood 260 ms");
    }

    [TestMethod]
    public void BadTimesAndPositionsAreIgnoredAndAClockResetDoesNotFakeMotion()
    {
        var t = new ActiveDeviceTracker(initial: ActiveDevice.Pad);
        t.Observe(DeviceSignal.MouseMotion(double.NaN, P(0, 0), P(50, 0)));
        t.Observe(DeviceSignal.MouseMotion(1, P(double.PositiveInfinity, 0), P(50, 0)));
        t.Update(5); Assert.AreEqual(ActiveDevice.Pad, t.Active);
        for (var i = 0; i < 20; i++) t.Observe(DeviceSignal.MouseMotion(100 - i, P(i, 0), P(1, 0))); // the clock runs backwards
        t.Update(200); Assert.AreEqual(ActiveDevice.Pad, t.Active);
    }
}
