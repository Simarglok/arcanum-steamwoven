using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>What a window's debug block shows: the key's note, the window's message (null: none) and where the window starts (screen px).</summary>
internal sealed record AreaDebugNote(string Key, string? Message, float WindowLeft);

/// <summary>
/// ARC-UI-2615: the debug mode's block of the inventory, loot, barter and item service windows, outside the window so that the window is the player's own: at the screen's left, standing on the bottom strip, the
/// key that changes between the original look and the diagnostic one, and under it the window's message. It lies in the margin beside the window; where there is none (4:3) it lies over the window. It never takes
/// the pointer. Text is the message window's size at least, in the original's units at the interface's scale.
/// </summary>
public partial class AreaDebugBlock : VBoxContainer
{
    /// <summary>The key that changes the look, with Ctrl and Shift.</summary>
    internal const Key ToggleKey = Key.J;
    internal const string KeyName = "Ctrl+Shift+J";

    /// <summary>The text size in original units: the message window's fallback title size, no smaller than its 12-unit text.</summary>
    private const int TextFontSize = 13;
    private const int OutlineUnits = 4;
    /// <summary>The narrowest margin the block keeps to (units); with less it takes <see cref="OverWindowWidth"/> over the window.</summary>
    private const float MarginWidth = 96, OverWindowWidth = 200, Inset = 4;

    private Func<AreaDebugNote?> _describe = null!;
    private Action _toggle = null!;
    private Label _key = null!;
    private Label _message = null!;

    internal static AreaDebugBlock For(Func<AreaDebugNote?> describe, Action toggle)
    {
        var block = new AreaDebugBlock { Name = "DebugBlock", MouseFilter = MouseFilterEnum.Ignore, Visible = false, _describe = describe, _toggle = toggle };
        block._key = block.Line(Colors.White);
        block._message = block.Line(new Color(.6f, .8f, .65f));
        return block;
    }

    internal string KeyTextForSelfTest => _key.Text;
    internal string MessageTextForSelfTest => _message.Visible ? _message.Text : string.Empty;

    private Label Line(Color colour)
    {
        var label = new Label { MouseFilter = MouseFilterEnum.Ignore, AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = colour };
        label.AddThemeFontSizeOverride("font_size", TextFontSize);
        label.AddThemeConstantOverride("outline_size", OutlineUnits);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        AddChild(label);
        return label;
    }

    public override void _Process(double delta)
    {
        if (_describe() is not { } note) { Visible = false; return; }
        Visible = true;
        _key.Text = note.Key;
        _message.Text = note.Message ?? string.Empty;
        _message.Visible = _message.Text.Length > 0;
        var view = GetViewport().GetVisibleRect().Size;
        var scale = AreaGameBarPanel.ZoneWindow(view, (int)Arcanum.World.AreaInventoryDrag.FrameWidth, (int)Arcanum.World.AreaInventoryDrag.FrameHeight) is { } zone
            ? (float)zone.Scale : AreaGameBarPanel.ScreenFactor(view);
        var margin = note.WindowLeft / scale;
        var width = (margin >= MarginWidth ? margin : OverWindowWidth) - 2 * Inset;
        Scale = new(scale, scale);
        // The width first: the wrapped lines give the height from it.
        Size = new(width, 0);
        Size = new(width, GetCombinedMinimumSize().Y);
        Position = new(Inset * scale, view.Y - AreaGameBarPanel.BottomInset - (Size.Y + Inset) * scale);
    }

    public override void _Input(InputEvent input)
    {
        if (input is not InputEventKey { Pressed: true, Echo: false, CtrlPressed: true, ShiftPressed: true, Keycode: ToggleKey } || _describe() is null) return;
        GetViewport().SetInputAsHandled();
        _toggle();
    }
}
