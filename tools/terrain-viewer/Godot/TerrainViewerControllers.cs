using Arcanum.Input;
using Arcanum.TerrainViewer.Core;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-GPAD-1374 (epic #1371, concept docs/input/controller-schemes.md §0.1-§0.4): the controller foundation in the viewer. The scheme choice
// (pad scheme, touch scheme, prompt style) is the Controls line and the Prompt style line (both the Video page's since ARC-UI-1737) of the game's Options window, saved with the game settings (AreaGameSettings, the
// lines "pad scheme", "touch scheme", "prompt style"); a change applies at once. Every input event, from any pad (never assumed to be device 0),
// the mouse, the keyboard and the touch screen, goes to the foundation's active-device tracker ("last meaningful input wins", 250 ms hysteresis);
// the prompt line at the bottom of the screen names the device in hand and, once a scheme has prompts, its buttons with the glyphs of the
// pad's family. While the mouse or keyboard is active the line is empty, so the original controls are unchanged (KM-01). No scheme has behaviour
// here: the strip says which device and scheme are live, never a button that does nothing. Nothing in this file touches a window or focus.
public partial class TerrainViewerApp
{
    private ControllerFoundation? _controllers;
    private CanvasLayer? _promptLayer;
    private PanelContainer? _promptPanel;
    // ARC-UI-2968: the strip's text is drawn in the original's text font (art 229) like the hover text; without the font (no game folder) it draws nothing.
    private AreaArtTextBlock? _promptArt;
    private static readonly (byte R, byte G, byte B) PromptColour = (232, 232, 232);
    private string _promptShown = string.Empty;
    private AreaTextFonts? _promptFonts;
    /// <summary>A test's clock in seconds (the hysteresis and the pointer-from-pad windows need time to pass).</summary>
    private Func<double>? _controllerClockForSelfTest;
    private Func<int, PadIdentity>? _padIdentityForSelfTest;

    /// <summary>The Deck runs the Windows package under Proton with its own Steam Input pad, so a host flag decides its glyphs (concept §0.3).</summary>
    private static bool HostIsSteamDeck => OS.GetEnvironment("SteamDeck") == "1";

    private double ControllerNow => _controllerClockForSelfTest?.Invoke() ?? Time.GetTicksMsec() / 1000.0;

    internal string PromptStripForSelfTest => _promptShown;
    internal bool PromptStripDrawnForSelfTest => _promptPanel is { Visible: true };
    internal AreaArtTextBlock? PromptArtForSelfTest => _promptArt;

    /// <summary>The foundation, made at first use (a start, a launcher test or a self-test with no start all reach it) from the settings the host holds.</summary>
    private ControllerFoundation Controllers
    {
        get
        {
            if (_controllers is not null) return _controllers;
            _controllers = new ControllerFoundation(PadIdentityOf, HostIsSteamDeck, _gameSettings.Controls);
            GD.Print($"ARC-GPAD-1374 controllers pad={_controllers.Settings.Pad} touch={_controllers.Settings.Touch} prompts={_controllers.Settings.Prompts} steamDeckHost={HostIsSteamDeck}");
            return _controllers;
        }
    }

    /// <summary>Applies the settings the host holds (a start, a read of the settings file, a run-time switch).</summary>
    private void ConfigureControllers() => Controllers.Apply(_gameSettings.Controls);

    /// <summary>A test's end: the foundation (its tracker holds the test clock's times) is made again from the settings at its next use, and no stale strip stays.</summary>
    private void ResetControllersForSelfTest()
    {
        _controllers = null; _controllerClockForSelfTest = null; _padIdentityForSelfTest = null;
        _promptShown = string.Empty; _promptFonts = null; _textFontsForSelfTest = null;
        if (_promptPanel is not null) _promptPanel.Visible = false;
    }

    private PadIdentity PadIdentityOf(int device)
        => _padIdentityForSelfTest?.Invoke(device) ?? new PadIdentity(global::Godot.Input.GetJoyName(device), global::Godot.Input.GetJoyGuid(device));

    /// <summary>One event of any device to the tracker. Only meaningful input counts: a pad button press, a pad axis (the tracker drops drift), a
    /// key press, a mouse click or motion, a touch.</summary>
    private void ObserveControllerInput(InputEvent input)
    {
        var controllers = Controllers;
        var now = ControllerNow;
        switch (input)
        {
            case InputEventJoypadButton { Pressed: true } button: controllers.Observe(DeviceSignal.PadButton(now, button.Device)); break;
            case InputEventJoypadMotion motion: controllers.Observe(DeviceSignal.PadAxis(now, motion.AxisValue, motion.Device)); break;
            case InputEventKey { Pressed: true, Echo: false }: controllers.Observe(DeviceSignal.Key(now)); break;
            case InputEventMouseMotion motion: controllers.Observe(DeviceSignal.MouseMotion(now, new Vec2(motion.Position.X, motion.Position.Y), new Vec2(motion.Relative.X, motion.Relative.Y))); break;
            case InputEventMouseButton { Pressed: true } click: controllers.Observe(DeviceSignal.MouseClick(now, new Vec2(click.Position.X, click.Position.Y))); break;
            case InputEventScreenTouch { Pressed: true } touch: controllers.Observe(DeviceSignal.Touch(now, new Vec2(touch.Position.X, touch.Position.Y))); break;
            case InputEventScreenDrag drag: controllers.Observe(DeviceSignal.Touch(now, new Vec2(drag.Position.X, drag.Position.Y))); break;
        }
    }

    /// <summary>Once a frame: a device change that stood for the hysteresis shows, and the prompt line follows the device in hand.</summary>
    private void SyncControllerPrompts()
    {
        var controllers = Controllers;
        controllers.Update(ControllerNow);
        // The bottom line of every open window, else the plain play line; the near-target prompt waits for a scheme's target highlight.
        var context = !IsWorld || WorldModal ? PromptContext.Modal : PromptContext.Play;
        var text = controllers.Strip(context);
        // The fonts are read once a module and only when a strip shows (a start with the mouse in hand reads nothing).
        var fonts = text.Length == 0 ? null : TextFonts();
        if (text == _promptShown && ReferenceEquals(fonts, _promptFonts)) return;
        (_promptShown, _promptFonts) = (text, fonts);
        if (text.Length == 0) { if (_promptPanel is not null) _promptPanel.Visible = false; return; }
        EnsurePromptStrip();
        if (fonts is null) { _promptArt!.Set([]); _promptPanel!.Visible = false; return; }
        var (placed, _, _) = AreaTextFontLayout.Block(fonts.Text.Font, fonts.TextLineHeight, text);
        _promptArt!.Set([new(fonts.Text, fonts.TextLineHeight, placed.Lines, 0, 0, PromptColour)]);
        _promptPanel!.Visible = true;
    }

    private void EnsurePromptStrip()
    {
        if (_promptLayer is not null) return;
        _promptLayer = new CanvasLayer { Name = "ControllerPrompts", Layer = 90 };
        _promptPanel = new PanelContainer { Name = "PromptStrip", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _promptPanel.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _promptPanel.GrowHorizontal = Control.GrowDirection.Both;
        _promptPanel.GrowVertical = Control.GrowDirection.Begin;
        _promptPanel.OffsetBottom = -6;
        _promptPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(14 / 255f, 16 / 255f, 22 / 255f, .82f),
            ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 3, ContentMarginBottom = 3,
        });
        _promptArt = new AreaArtTextBlock { Name = "PromptText", FitContent = true };
        _promptPanel.AddChild(_promptArt); _promptLayer.AddChild(_promptPanel); AddChild(_promptLayer);
    }
}
