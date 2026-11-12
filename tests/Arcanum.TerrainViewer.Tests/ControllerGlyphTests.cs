using Arcanum.Input;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

[TestClass]
public sealed class ControllerGlyphTests
{
    private static string Guid(int vendor, int product = 0x1234) =>
        "0300" + "0000" + $"{vendor & 0xff:x2}{vendor >> 8:x2}" + "0000" + $"{product & 0xff:x2}{product >> 8:x2}" + "0000" + "0000" + "0000";

    [TestMethod]
    public void GuidVendorIsReadLittleEndian()
    {
        Assert.AreEqual(0x045e, GlyphFamilyDetector.VendorOf(Guid(0x045e)));
        Assert.AreEqual(0x28de, GlyphFamilyDetector.VendorOf(Guid(0x28de)));
        foreach (var bad in new string?[] { null, "", "abc", new string('g', 32), new string('0', 31) }) Assert.AreEqual(-1, GlyphFamilyDetector.VendorOf(bad));
    }

    [TestMethod]
    public void FamiliesFromNamesAndGuids()
    {
        (string? name, string? guid, GlyphFamily family)[] rows =
        [
            ("Xbox Series Controller", Guid(0x045e), GlyphFamily.Xbox),
            ("XInput Gamepad", null, GlyphFamily.Xbox),
            ("PS5 Controller", null, GlyphFamily.PlayStation),
            ("Wireless Controller", Guid(0x054c), GlyphFamily.PlayStation),
            ("DualSense Wireless Controller", null, GlyphFamily.PlayStation),
            ("Nintendo Switch Pro Controller", null, GlyphFamily.Nintendo),
            ("Gamepad", Guid(0x057e), GlyphFamily.Nintendo),
            ("Steam Deck Controller", null, GlyphFamily.SteamDeck),
            ("anything", Guid(0x28de), GlyphFamily.SteamDeck),
            ("Unknown pad", "not a guid", GlyphFamily.Xbox),
            (null, null, GlyphFamily.Xbox)
        ];
        foreach (var (name, guid, family) in rows) Assert.AreEqual(family, GlyphFamilyDetector.Detect(name, guid), $"{name}/{guid}");
    }

    [TestMethod]
    public void SteamInputVirtualPadOnADeckHostIsTheDeck()
    {
        Assert.AreEqual(GlyphFamily.SteamDeck, GlyphFamilyDetector.Detect("Xbox 360 Controller", Guid(0x045e), hostIsSteamDeck: true));
        Assert.AreEqual(GlyphFamily.Xbox, GlyphFamilyDetector.Detect("Xbox 360 Controller", Guid(0x045e), hostIsSteamDeck: false));
        Assert.AreEqual(GlyphFamily.PlayStation, GlyphFamilyDetector.Detect("PS5 Controller", null, hostIsSteamDeck: true), "a real external pad keeps its own family");
    }

    [TestMethod]
    public void ManualOverrideWinsOverDetection()
    {
        Assert.AreEqual(GlyphFamily.Nintendo, GlyphFamilyDetector.Resolve(PromptStyle.Nintendo, "Xbox Controller", Guid(0x045e)));
        Assert.AreEqual(GlyphFamily.PlayStation, GlyphFamilyDetector.Resolve(PromptStyle.Auto, "DualSense", null));
        Assert.AreEqual(GlyphFamily.SteamDeck, GlyphFamilyDetector.Resolve(PromptStyle.SteamDeck, "DualSense", null));
    }

    [TestMethod]
    public void LabelsFollowThePhysicalPositionPerFamily()
    {
        Assert.AreEqual("A", ControllerGlyphs.Label(GlyphFamily.Xbox, ControllerButtons.A));
        Assert.AreEqual("Cross", ControllerGlyphs.Label(GlyphFamily.PlayStation, ControllerButtons.A));
        Assert.AreEqual("B", ControllerGlyphs.Label(GlyphFamily.Nintendo, ControllerButtons.A), "Nintendo's bottom button is B");
        Assert.AreEqual("A", ControllerGlyphs.Label(GlyphFamily.Nintendo, ControllerButtons.B), "Nintendo's right button is A");
        Assert.AreEqual("Options", ControllerGlyphs.Label(GlyphFamily.PlayStation, ControllerButtons.Menu));
        Assert.AreEqual("R1", ControllerGlyphs.Label(GlyphFamily.SteamDeck, ControllerButtons.RB));
        Assert.AreEqual("ZR", ControllerGlyphs.Trigger(GlyphFamily.Nintendo, true));
        Assert.AreEqual("LT", ControllerGlyphs.Trigger(GlyphFamily.Xbox, false));
        Assert.AreEqual(string.Empty, ControllerGlyphs.Label(GlyphFamily.KeyboardMouse, ControllerButtons.A));
        foreach (var family in new[] { GlyphFamily.Xbox, GlyphFamily.PlayStation, GlyphFamily.Nintendo, GlyphFamily.SteamDeck })
        {
            var names = new[] { ControllerButtons.A, ControllerButtons.B, ControllerButtons.X, ControllerButtons.Y }.Select(b => ControllerGlyphs.Label(family, b)).ToArray();
            Assert.AreEqual(4, names.Distinct().Count(), family.ToString());
        }
    }
}
