using Arcanum.Input;
using GamepadLab.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-GPAD-1374 (concept §0.1 to §0.4): the controller foundation the viewer hosts: the device in hand, its glyph family, the run-time
/// switch of the scheme choice and the prompt line. Godot-free.</summary>
[TestClass]
public sealed class ControllerFoundationTests
{
    private const string Bad = "mouse|keyboard|click|Esc|Enter";

    private static readonly Dictionary<int, PadIdentity> Pads = new()
    {
        [3] = new("Xbox Wireless Controller", "030000005e040000130b000001050000"),
        [7] = new("DualSense Wireless Controller", "030000004c050000e60c000000000000"),
        [9] = new("Pro Controller", "030000007e0500000920000000000000"),
    };

    private static ControllerFoundation Make(ControlSchemeSettings? settings = null, bool deck = false)
        => new(id => Pads.GetValueOrDefault(id), deck, settings);

    private static void Use(ControllerFoundation foundation, DeviceSignal signal, double settle = .3)
    {
        foundation.Observe(signal);
        foundation.Update(signal.Time + settle);
    }

    private static void AssertNoMouseOrKeyboardGlyph(string line)
    {
        foreach (var word in Bad.Split('|')) Assert.IsFalse(line.Contains(word, StringComparison.OrdinalIgnoreCase), $"'{line}' names '{word}'");
    }

    [TestMethod]
    public void The_mouse_and_keyboard_show_no_prompt_line_so_the_original_is_unchanged()
    {
        var foundation = Make(new(Pad: PadScheme.Direct, Touch: TouchScheme.TouchPad));
        Assert.AreEqual(ActiveDevice.KeyboardMouse, foundation.Device);
        Assert.AreEqual(GlyphFamily.KeyboardMouse, foundation.Family);
        foreach (var context in Enum.GetValues<PromptContext>()) Assert.AreEqual(string.Empty, foundation.Strip(context), context.ToString());
        Use(foundation, DeviceSignal.Key(1));
        Use(foundation, DeviceSignal.MouseClick(2, new(10, 10)));
        Assert.AreEqual(string.Empty, foundation.Strip(PromptContext.Modal));
        Assert.IsTrue(foundation.Settings.OriginalActive);
    }

    [TestMethod]
    public void The_prompt_line_follows_the_device_in_hand_and_never_names_the_mouse_or_keyboard()
    {
        var foundation = Make();
        Use(foundation, DeviceSignal.PadButton(1, 3));
        Assert.AreEqual((ActiveDevice.Pad, 3, GlyphFamily.Xbox), (foundation.Device, foundation.PadDeviceId, foundation.Family), "device 3 is a pad like any other");
        var line = foundation.Strip(PromptContext.Modal);
        StringAssert.StartsWith(line, "Xbox pad");
        AssertNoMouseOrKeyboardGlyph(line);

        Use(foundation, DeviceSignal.Touch(5, new(100, 100)));
        Assert.AreEqual(ActiveDevice.Touch, foundation.Device);
        line = foundation.Strip(PromptContext.Play);
        StringAssert.StartsWith(line, "Touch");
        AssertNoMouseOrKeyboardGlyph(line);

        Use(foundation, DeviceSignal.Key(9));
        Assert.AreEqual((ActiveDevice.KeyboardMouse, string.Empty), (foundation.Device, foundation.Strip(PromptContext.Play)), "a key takes the line away again");
    }

    [TestMethod]
    public void The_family_follows_the_pad_in_hand_and_the_manual_override()
    {
        var foundation = Make();
        Use(foundation, DeviceSignal.PadButton(1, 7));
        Assert.AreEqual(GlyphFamily.PlayStation, foundation.Family);
        StringAssert.StartsWith(foundation.Strip(PromptContext.Play), "PlayStation pad");
        Use(foundation, DeviceSignal.PadButton(2, 9));
        Assert.AreEqual(GlyphFamily.Nintendo, foundation.Family, "a second pad that is used takes the line");
        Use(foundation, DeviceSignal.PadButton(3, 3));
        Assert.AreEqual(GlyphFamily.Xbox, foundation.Family);
        foundation.Apply(new(Prompts: PromptStyle.PlayStation));
        Assert.AreEqual((GlyphFamily.PlayStation, ActiveDevice.Pad), (foundation.Family, foundation.Device), "the override wins at once and the device stays");
        foundation.Apply(new(Prompts: PromptStyle.Auto));
        Assert.AreEqual(GlyphFamily.Xbox, foundation.Family);
    }

    [TestMethod]
    public void A_pad_behind_steam_input_on_a_deck_host_is_the_decks()
    {
        var foundation = Make(deck: true);
        Use(foundation, DeviceSignal.PadButton(1, 3));
        Assert.AreEqual(GlyphFamily.SteamDeck, foundation.Family);
        StringAssert.StartsWith(foundation.Strip(PromptContext.Modal), "Steam Deck pad");
    }

    [TestMethod]
    public void A_switch_of_the_scheme_choice_applies_at_once_and_changes_no_device_or_window()
    {
        var foundation = Make();
        var changes = 0;
        foundation.Changed += () => changes++;
        Use(foundation, DeviceSignal.PadButton(1, 3));
        var afterDevice = changes;
        Assert.AreEqual(1, afterDevice, "the device change is announced once");
        StringAssert.Contains(foundation.Strip(PromptContext.Modal), "original controls");
        foundation.Apply(new(Pad: PadScheme.Direct));
        Assert.AreEqual(afterDevice + 1, changes);
        StringAssert.Contains(foundation.Strip(PromptContext.Modal), "A Direct");
        foundation.Apply(new(Pad: PadScheme.Pointer));
        StringAssert.Contains(foundation.Strip(PromptContext.Modal), "B Pointer");
        foundation.Apply(new(Pad: PadScheme.Pointer));
        Assert.AreEqual(afterDevice + 2, changes, "the same choice again is no change");
        Assert.AreEqual(ActiveDevice.Pad, foundation.Device, "a switch never moves the active device");
        Use(foundation, DeviceSignal.Touch(5, new(1, 1)));
        StringAssert.Contains(foundation.Strip(PromptContext.Modal), "original controls");
        foundation.Apply(new(Pad: PadScheme.Pointer, Touch: TouchScheme.TouchPad));
        StringAssert.Contains(foundation.Strip(PromptContext.Modal), "C Touch+pad");
        // Scheme C alone (the Controls line's value): a pad in hand is named with it, as the pad is part of C.
        foundation.Apply(new ControlSchemeSettings().WithChoice(ControlChoice.TouchPad));
        Use(foundation, DeviceSignal.PadButton(9, 3));
        Assert.AreEqual("Xbox pad - C Touch+pad", foundation.Strip(PromptContext.Modal));
        // A damaged choice is validated, never kept.
        foundation.Apply(new(Pad: (PadScheme)9, Touch: (TouchScheme)9, Prompts: (PromptStyle)9));
        Assert.AreEqual(ControlSchemeSettings.Default, foundation.Settings);
    }

    [TestMethod]
    public void A_scheme_with_prompts_lists_them_with_the_glyphs_of_the_pad_in_hand_and_the_remaps()
    {
        var entries = new[]
        {
            new PromptEntry("confirm", PromptContext.Modal, "Confirm", ControllerButtons.A),
            new PromptEntry("back", PromptContext.Modal, "Back", ControllerButtons.B),
            new PromptEntry("zoom", PromptContext.Modal, "Zoom in", Trigger: PromptTrigger.Right),
            new PromptEntry("talk", PromptContext.Target, "Talk", ControllerButtons.A),
            new PromptEntry("tap", PromptContext.Modal, "Press", Touch: "Tap"),
        };
        var bindings = new PromptBindings(entries);
        var settings = new ControlSchemeSettings(Pad: PadScheme.Direct, Touch: TouchScheme.TouchPad);
        string Line(ActiveDevice device, GlyphFamily family, PromptContext context, PromptBindings? pad = null) =>
            PromptStrip.Compose(device, family, settings, context, pad ?? bindings, bindings);

        Assert.AreEqual("A Confirm | B Back | RT Zoom in", Line(ActiveDevice.Pad, GlyphFamily.Xbox, PromptContext.Modal));
        Assert.AreEqual("Cross Confirm | Circle Back | R2 Zoom in", Line(ActiveDevice.Pad, GlyphFamily.PlayStation, PromptContext.Modal));
        Assert.AreEqual("B Confirm | A Back | ZR Zoom in", Line(ActiveDevice.Pad, GlyphFamily.Nintendo, PromptContext.Modal), "Nintendo's bottom button is B");
        Assert.AreEqual("A Talk", Line(ActiveDevice.Pad, GlyphFamily.SteamDeck, PromptContext.Target), "the near-target prompt is its own context");
        Assert.AreEqual(string.Empty, Line(ActiveDevice.KeyboardMouse, GlyphFamily.KeyboardMouse, PromptContext.Modal));
        Assert.AreEqual("Tap Press", Line(ActiveDevice.Touch, GlyphFamily.Xbox, PromptContext.Modal), "touch lists gestures, never a pad glyph");
        foreach (var family in Enum.GetValues<GlyphFamily>())
            AssertNoMouseOrKeyboardGlyph(Line(ActiveDevice.Pad, family, PromptContext.Modal) + Line(ActiveDevice.Touch, family, PromptContext.Modal));
        // A remap rebinds the pad button by the prompt's id; a bad remap (no button, two buttons) and an unknown id change nothing.
        var remapped = bindings.Remapped(new Dictionary<string, ControllerButtons>
        {
            ["confirm"] = ControllerButtons.X, ["back"] = ControllerButtons.None, ["zoom"] = ControllerButtons.Y, ["nobody"] = ControllerButtons.LB,
        });
        Assert.AreEqual("X Confirm | B Back | RT Zoom in", Line(ActiveDevice.Pad, GlyphFamily.Xbox, PromptContext.Modal, remapped));
        Assert.AreEqual("Square Confirm | Circle Back | R2 Zoom in", Line(ActiveDevice.Pad, GlyphFamily.PlayStation, PromptContext.Modal, remapped));
        Assert.AreEqual("A Confirm | B Back | RT Zoom in", Line(ActiveDevice.Pad, GlyphFamily.Xbox, PromptContext.Modal,
            bindings.Remapped(new Dictionary<string, ControllerButtons> { ["confirm"] = ControllerButtons.A | ControllerButtons.B })), "two buttons are no remap");
        Assert.AreSame(bindings, bindings.Remapped(null));
        Assert.AreEqual("Xbox pad - A Direct", PromptStrip.Compose(ActiveDevice.Pad, GlyphFamily.Xbox, settings, PromptContext.Play, PromptBindings.Empty, bindings.Remapped(null)),
            "a scheme with no prompt in the context names the device and the scheme, not a button");
    }

    [TestMethod]
    public void No_scheme_has_prompts_until_its_behaviour_is_built()
    {
        foreach (var scheme in Enum.GetValues<PadScheme>()) Assert.AreEqual(0, PromptBindings.ForPad(scheme).Entries.Count, scheme + " promises no button");
        foreach (var scheme in Enum.GetValues<TouchScheme>()) Assert.AreEqual(0, PromptBindings.ForTouch(scheme).Entries.Count, scheme + " promises no gesture");
    }

    [TestMethod]
    public void The_pointer_from_pad_rule_keeps_the_pad_prompts_and_a_set_remap_survives_a_switch()
    {
        var foundation = Make();
        Use(foundation, DeviceSignal.PadButton(1, 3));
        // A trackpad or gyro mapped to the mouse within 2 s of pad use: the cursor shows, the pad prompts stay.
        foundation.Observe(DeviceSignal.MouseMotion(1.5, new(200, 200), new(40, 0)));
        foundation.Update(2);
        Assert.AreEqual(ActiveDevice.Pad, foundation.Device);
        Assert.IsTrue(foundation.CursorVisible(2));
        foundation.SetRemap(new Dictionary<string, ControllerButtons> { ["confirm"] = ControllerButtons.X });
        foundation.Apply(new(Pad: PadScheme.Pointer));
        StringAssert.StartsWith(foundation.Strip(PromptContext.Modal), "Xbox pad");
        Assert.IsFalse(foundation.Strip(PromptContext.Modal).Contains("mouse", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Bad_arguments_are_refused()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new ControllerFoundation(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => Make().Apply(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => new PromptBindings(null!));
        Assert.AreEqual(PromptBindings.MaximumEntries, new PromptBindings(Enumerable.Range(0, 200).Select(i => new PromptEntry("e" + i, PromptContext.Play, "x"))).Entries.Count);
    }
}
