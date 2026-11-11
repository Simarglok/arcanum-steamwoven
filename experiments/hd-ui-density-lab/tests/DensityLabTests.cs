using Arcanum.Experiments.HdUiDensityLab.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Experiments.HdUiDensityLab.Tests;

[TestClass]
public sealed class DensityScaleTests
{
    // The table of docs/rendering/hd-interface-density.md, section "One scale from density".
    [TestMethod]
    [DataRow(HostKind.MacOS, 2.0, 0, 3024, 1964, 3.0, ScaleSource.Density)]
    [DataRow(HostKind.MacOS, 2.0, 0, 5120, 2880, 3.0, ScaleSource.Density)]
    [DataRow(HostKind.Windows, 1.0, 96, 1920, 1080, 1.5, ScaleSource.Density)]
    [DataRow(HostKind.Windows, 1.0, 96, 2560, 1440, 1.5, ScaleSource.Density)]
    [DataRow(HostKind.Windows, 1.0, 240, 3840, 2160, 3.6, ScaleSource.Fit)]
    [DataRow(HostKind.LinuxX11, 1.0, 216, 1280, 800, 1.3333333333333333, ScaleSource.Fit)]
    public void DocumentTable(HostKind host, double reported, int dpi, int w, int h, double expected, ScaleSource source)
    {
        var decision = DensityScale.Decide(new DisplayReading(host, reported, dpi, w, h), 100);
        Assert.AreEqual(expected, decision.Scale, 1e-9);
        Assert.AreEqual(source, decision.Source);
    }

    [TestMethod]
    public void OsScalePerHost()
    {
        Assert.AreEqual(2.0, DensityScale.OsScale(new DisplayReading(HostKind.MacOS, 2, 144, 1, 1)));
        Assert.AreEqual(1.25, DensityScale.OsScale(new DisplayReading(HostKind.Windows, 1, 120, 1, 1)));
        Assert.AreEqual(1.75, DensityScale.OsScale(new DisplayReading(HostKind.LinuxWayland, 1.75, 96, 1, 1)));
        Assert.AreEqual(1.0, DensityScale.OsScale(new DisplayReading(HostKind.LinuxX11, 1, 216, 1, 1)), "X11 has no OS scale; EDID DPI is not used");
        Assert.AreEqual(1.0, DensityScale.OsScale(new DisplayReading(HostKind.SteamOS, 1, 216, 1, 1)));
        Assert.AreEqual(1.0, DensityScale.OsScale(new DisplayReading(HostKind.Windows, 1, 0, 1, 1)), "a missing DPI clamps to 1");
    }

    [TestMethod]
    public void SteamOsFillsTheScreenAndOnlyShrinks()
    {
        var handheld = new DisplayReading(HostKind.SteamOS, 1, 216, 1280, 800);
        Assert.AreEqual(800 / 600.0, DensityScale.Decide(handheld, 100).Scale, 1e-9);
        Assert.AreEqual(800 / 600.0, DensityScale.Decide(handheld, 200).Scale, 1e-9);
        Assert.AreEqual(800 / 600.0 * 0.75, DensityScale.Decide(handheld, 75).Scale, 1e-9);
        var tv = new DisplayReading(HostKind.SteamOS, 1, 50, 3840, 2160);
        Assert.AreEqual(3.6, DensityScale.Decide(tv, 100).Scale, 1e-9);
    }

    [TestMethod]
    public void PlayerScaleAndForcedScale()
    {
        var reading = new DisplayReading(HostKind.Windows, 1, 96, 2560, 1440);
        Assert.AreEqual(2.25, DensityScale.Decide(reading, 150).Scale, 1e-9);
        Assert.AreEqual(2.4, DensityScale.Decide(reading, 300).Scale, 1e-9);
        Assert.AreEqual(0.75, DensityScale.Decide(reading, 50).Scale, 1e-9);
        var forced = DensityScale.Decide(reading, 100, 1.75);
        Assert.AreEqual(1.75, forced.Scale);
        Assert.AreEqual(ScaleSource.Forced, forced.Source);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DensityScale.Decide(reading, 49));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DensityScale.Decide(reading, 100, 0.1));
    }
}

[TestClass]
public sealed class DeviceGridTests
{
    private static readonly double[] Scales = [1, 1.25, 1.5, 1.75, 2, 2.5, 3, 3.75, 4];

    [TestMethod]
    public void LinesNeverVanishAndRoundHalfUp()
    {
        Assert.AreEqual(1, DeviceGrid.Line(0.2, 1));
        Assert.AreEqual(1, DeviceGrid.Line(1, 1.25));
        Assert.AreEqual(2, DeviceGrid.Line(1, 1.5));
        Assert.AreEqual(3, DeviceGrid.Line(1, 2.5));
        Assert.AreEqual(4, DeviceGrid.Line(1, 3.75));
    }

    [TestMethod]
    public void NeighboursShareEdges()
    {
        foreach (var s in Scales)
        {
            for (var col = 0; col < 9; col++)
            {
                var a = DeviceGrid.Place(new UnitRect(258 + col * 48, 40, 48, 44), s, 7, 3);
                var b = DeviceGrid.Place(new UnitRect(258 + (col + 1) * 48, 40, 48, 44), s, 7, 3);
                Assert.AreEqual(a.Right, b.X, $"s={s} col={col}");
            }
        }
    }

    [TestMethod]
    public void FrameBandsAreEqualAndTileTheBorder()
    {
        foreach (var s in Scales)
        {
            var ops = new List<ChromeOp>();
            var outer = DeviceGrid.Place(new UnitRect(0, 0, 800, 400), s, 0, 0);
            var inner = Chrome.Frame(ops, outer, Chrome.PanelFrame, s);
            var widths = Chrome.BandWidths(Chrome.PanelFrame, s);
            Assert.AreEqual(1, widths.Distinct().Count(), $"s={s}: equal bands stay equal");
            Assert.AreEqual(widths.Sum(), inner.X - outer.X, $"s={s}");
            Assert.AreEqual(outer.Right - widths.Sum(), inner.Right, $"s={s}");
            long area = ops.Sum(o => (long)o.Rect.Width * o.Rect.Height);
            Assert.AreEqual((long)outer.Width * outer.Height - (long)inner.Width * inner.Height, area, $"s={s}: no overlap, no gap");
        }
    }

    [TestMethod]
    public void FrameSkipsBandsThatDoNotFit()
    {
        var ops = new List<ChromeOp>();
        var inner = Chrome.Frame(ops, new DeviceRect(0, 0, 5, 40), Chrome.PanelFrame, 1);
        Assert.AreEqual(8, ops.Count, "two bands fit in 5 px");
        Assert.AreEqual(new DeviceRect(2, 2, 1, 36), inner);
    }
}

[TestClass]
public sealed class MockScreenTests
{
    [TestMethod]
    public void StripsAnchorToEdgesAndWindowSitsBetween()
    {
        foreach (var s in new[] { 1.0, 1.5, 2.25, 3.0 })
        {
            var width = DeviceGrid.Round(1200 * s);
            var height = DeviceGrid.Round(700 * s);
            var model = MockScreen.Build(width, height, s, Interaction.None);
            Assert.AreEqual(0, model.TopStripOuter.Y);
            Assert.AreEqual(DeviceGrid.Edge(800, s), model.TopStripOuter.Width);
            Assert.AreEqual((width - DeviceGrid.Edge(800, s)) / 2, model.TopStripOuter.X);
            Assert.AreEqual(height, model.PlayArea.Bottom + DeviceGrid.Edge(MockScreen.BottomStripUnits, s));
            Assert.IsTrue(model.WindowOuter.Y >= model.PlayArea.Y && model.WindowOuter.Bottom <= model.PlayArea.Bottom, $"s={s}");
            Assert.IsTrue(model.Ops.All(o => o.Rect.X >= 0 && o.Rect.Y >= 0 && o.Rect.Right <= width && o.Rect.Bottom <= height), $"s={s}");
        }
    }

    [TestMethod]
    public void ButtonStatesChangeFaceAndBevel()
    {
        Token FaceOf(Interaction ia, string id)
        {
            var model = MockScreen.Build(1200, 800, 1, ia);
            var button = model.Buttons.Single(b => b.Id == id).Rect;
            return model.Ops.Last(o => o.Rect.Contains(button.X + button.Width / 2, button.Y + button.Height / 2)).Token;
        }

        Assert.AreEqual(Token.Face, FaceOf(Interaction.None, "inventory"));
        Assert.AreEqual(Token.FaceHover, FaceOf(Interaction.None with { Hover = "inventory" }, "inventory"));
        Assert.AreEqual(Token.FaceDown, FaceOf(Interaction.None with { Down = "inventory" }, "inventory"));
        Assert.AreEqual(Token.FaceDisabled, FaceOf(Interaction.None with { Hover = "map", Down = "map" }, "map"));
    }

    [TestMethod]
    public void LensFollowsItsStyleInDevicePixels()
    {
        var style = new LensStyle(9, 45, 2);
        var lens = MockScreen.Build(1500, 900, 1.5, new Interaction(null, null, style)).Lenses.Single();
        Assert.AreEqual(DeviceGrid.Line(52, 1.5), lens.OuterRadiusPx);
        Assert.AreEqual(DeviceGrid.Line(9, 1.5), lens.RingPx);
        Assert.AreEqual(45, lens.LightAngleDegrees);
        Assert.AreEqual(2, lens.GlassIndex);
    }
}
