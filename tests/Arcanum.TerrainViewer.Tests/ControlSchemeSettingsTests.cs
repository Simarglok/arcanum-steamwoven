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
        foreach (var text in new string?[] { null, "", "not json", "[]", "{}", "{\"version\":2,\"pad\":\"direct\"}", "{\"version\":\"1\"}", "{\"version\":1.5}", "{\"version\":1,\"pad\":\"\uD800\"}", "{\"version\":1,\"pad\":\"\\uD800\\\"}",
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

    [TestMethod]
    public void The_one_controls_line_maps_to_the_pad_and_touch_fields_and_back()
    {
        var start = new ControlSchemeSettings(Prompts: PromptStyle.Nintendo);
        Assert.AreEqual(ControlChoice.Original, start.Choice);
        Assert.AreEqual((PadScheme.Direct, TouchScheme.Off, PromptStyle.Nintendo), (start.WithChoice(ControlChoice.Direct).Pad, start.WithChoice(ControlChoice.Direct).Touch, start.WithChoice(ControlChoice.Direct).Prompts), "the prompt style stays");
        Assert.AreEqual((PadScheme.Pointer, TouchScheme.Off), (start.WithChoice(ControlChoice.Pointer).Pad, start.WithChoice(ControlChoice.Pointer).Touch));
        Assert.AreEqual((PadScheme.Off, TouchScheme.TouchPad), (start.WithChoice(ControlChoice.TouchPad).Pad, start.WithChoice(ControlChoice.TouchPad).Touch), "scheme C uses the pad itself, so no pad scheme beside it");
        foreach (var choice in Enum.GetValues<ControlChoice>()) Assert.AreEqual(choice, start.WithChoice(choice).Choice, choice.ToString());
        Assert.AreEqual(ControlChoice.TouchPad, new ControlSchemeSettings(Pad: PadScheme.Direct, Touch: TouchScheme.TouchPad).Choice, "a file with both on reads as C");
        Assert.AreEqual(ControlChoice.Original, start.WithChoice(ControlChoice.Pointer).WithChoice(ControlChoice.Original).Choice);
        Assert.IsTrue(start.WithChoice(ControlChoice.TouchPad).OriginalActive, "the original mouse and keyboard stay live in every choice");
    }
}
