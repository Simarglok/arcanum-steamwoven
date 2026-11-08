using Arcanum.Input;
using GamepadLab.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-099: the pan the screen edge adds (<see cref="InputStateReducer.KeyboardCameraPan"/>) is the pan the same arrow key gives
/// through <see cref="InputStateReducer.Reduce"/>, for every direction code, inversion and zoom.</summary>
[TestClass]
public sealed class KeyboardCameraPanTests
{
    [TestMethod]
    public void The_edge_pan_equals_the_arrow_pan_for_every_direction_inversion_and_zoom()
    {
        foreach (var invertX in new[] { false, true })
        foreach (var invertY in new[] { false, true })
        foreach (var fixedSign in new[] { false, true })
        foreach (var zoom in new[] { .5, 1, 2, 3 })
        for (var code = 0; code < 8; code++)
        {
            var (x, y) = Arcanum.World.OriginalScroll.Direction(code);
            var direction = new Vec2(x, y);
            var context = new InputContext(Delta: .0167, CameraSensitivity: 1.3, Zoom: zoom, InvertCameraX: invertX, InvertCameraY: invertY)
            { WorldZoomEnabled = true, KeyboardCameraFixedSign = fixedSign };
            var raw = new RawInputFrame(-1, default) { Keyboard = true, KeyboardCamera = direction, KeyboardCameraHeld = true };
            var arrow = new InputStateReducer().Reduce(raw, context);
            Assert.IsTrue(arrow.Accepted && arrow.CameraActive, $"code {code}");
            var edge = InputStateReducer.KeyboardCameraPan(direction, context);
            Assert.AreEqual(arrow.CameraPan.X, edge.X, 1e-12, $"x code {code} invert {invertX}/{invertY} fixed {fixedSign} zoom {zoom}");
            Assert.AreEqual(arrow.CameraPan.Y, edge.Y, 1e-12, $"y code {code} invert {invertX}/{invertY} fixed {fixedSign} zoom {zoom}");
        }
    }
}
