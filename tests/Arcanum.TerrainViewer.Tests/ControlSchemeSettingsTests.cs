using Arcanum.Input;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

[TestClass]
public sealed class ControlSchemeSettingsTests
{
    [TestMethod]
    public void DefaultsAreOffAndOriginalIsAlwaysActive()
    {
        var d = ControlSchemeSettings.Default;
        Assert.AreEqual(PadScheme.Off, d.Pad); Assert.AreEqual(TouchScheme.Off, d.Touch); Assert.AreEqual(PromptStyle.Auto, d.Prompts);
        foreach (var pad in Enum.GetValues<PadScheme>()) foreach (var touch in Enum.GetValues<TouchScheme>())
            Assert.IsTrue((d with { Pad = pad, Touch = touch }).Validated().OriginalActive);
    }

    [TestMethod]
    public void EveryCombinationRoundTrips()
    {
        foreach (var pad in Enum.GetValues<PadScheme>()) foreach (var touch in Enum.GetValues<TouchScheme>()) foreach (var style in Enum.GetValues<PromptStyle>())
        {
            var s = new ControlSchemeSettings(1, pad, touch, style);
            Assert.AreEqual(s, ControlSchemeSettingsCodec.DeserializeOrDefault(ControlSchemeSettingsCodec.Serialize(s)));
        }
        Assert.AreEqual("{\"version\":1,\"pad\":\"pointer\",\"touch\":\"touchpad\",\"prompts\":\"steamdeck\"}",
            ControlSchemeSettingsCodec.Serialize(new(1, PadScheme.Pointer, TouchScheme.TouchPad, PromptStyle.SteamDeck)));
    }

    [TestMethod]
    public void CorruptOrForeignTextGivesDefaults()
    {
        foreach (var text in new string?[] { null, "", "not json", "[]", "{}", "{\"version\":2,\"pad\":\"direct\"}", "{\"version\":\"1\"}", "{\"version\":1.5}",
            new string(' ', ControlSchemeSettingsCodec.MaximumBytes + 1) })
            Assert.AreEqual(ControlSchemeSettings.Default, ControlSchemeSettingsCodec.DeserializeOrDefault(text), text);
    }

    [TestMethod]
    public void UnknownValueOfOneFieldFallsBackAndKeepsTheOthers()
    {
        var s = ControlSchemeSettingsCodec.DeserializeOrDefault("{\"version\":1,\"pad\":\"gyro\",\"touch\":\"TouchPad\",\"prompts\":7}");
        Assert.AreEqual(new ControlSchemeSettings(1, PadScheme.Off, TouchScheme.TouchPad, PromptStyle.Auto), s);
    }

    [TestMethod]
    public void OutOfRangeEnumValuesAreValidatedAway()
    {
        var s = new ControlSchemeSettings(1, (PadScheme)9, (TouchScheme)9, (PromptStyle)9).Validated();
        Assert.AreEqual(ControlSchemeSettings.Default, s);
        Assert.AreEqual(ControlSchemeSettings.Default, new ControlSchemeSettings(3, PadScheme.Direct).Validated());
    }
}
