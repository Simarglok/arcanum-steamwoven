using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>What a window's debug block knows of its window: whether the diagnostic view is on, the window's message and where the window starts (screen px).</summary>
internal sealed record AreaDebugNote(bool Diagnostic, string? Message, float WindowLeft);

/// <summary>
/// ARC-UI-2615: the debug mode's block of the inventory, loot, barter and item service windows, and the owner of its key (Ctrl+Shift+J, between the original look and the diagnostic view). The block shows only
/// while the diagnostic view is on, so the original look is the player's own: the window's message and, in a test run alone, the key's note. It stands in the margin beside the window, on the bottom strip's top
/// edge; where there is no margin (4:3) it takes the screen's top left under the top strip and the diagnostic window stays below it (<see cref="TopReserve"/>). It never takes the pointer. Text is the message
/// window's size at least, in the original's units at the interface's scale.
/// </summary>
public partial class AreaDebugBlock : VBoxContainer
{
    /// <summary>The key that changes the look, with Ctrl and Shift.</summary>
    internal const Key ToggleKey = Key.J;
    internal const string KeyName = "Ctrl+Shift+J";

    /// <summary>The key's note is for a test run (an automated host, a self-test, a smoke), never the player; the app sets it.</summary>
    internal static bool KeyHint { get; set; }

    /// <summary>The text size in original units: the message window's fallback title size, no smaller than its 12-unit text.</summary>
    private const int TextFontSize = 13;
    private const int OutlineUnits = 4;
    /// <summary>The narrowest margin the block keeps to (units); with less it takes <see cref="TopWidth"/> at the top left.</summary>
    internal const float MarginWidth = 96;
    private const float TopWidth = 400, Inset = 4;

    private Func<AreaDebugNote?> _describe = null!;
    private Action _toggle = null!;
    private Label _key = null!;
    private Label _message = null!;

    /// <summary>How far down from the screen's top the block reaches where it stands at the top left (the window starts below it); 0 otherwise.</summary>
    internal float TopReserve { get; private set; }

    internal static AreaDebugBlock For(Func<AreaDebugNote?> describe, Action toggle)
    {
        var block = new AreaDebugBlock { Name = "DebugBlock", MouseFilter = MouseFilterEnum.Ignore, Visible = false, _describe = describe, _toggle = toggle };
        block._key = block.Line(Colors.White);
        block._message = block.Line(new Color(.6f, .8f, .65f));
        return block;
    }

    internal string KeyTextForSelfTest => _key.Visible ? _key.Text : string.Empty;
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
        var note = _describe() is { Diagnostic: true } shown ? shown : null;
        _key.Text = $"{KeyName}: original look";
        _key.Visible = KeyHint;
        _message.Text = note?.Message ?? string.Empty;
        _message.Visible = _message.Text.Length > 0;
        Visible = note is not null && (_key.Visible || _message.Visible);
        if (!Visible) { TopReserve = 0; return; }
        var view = GetViewport().GetVisibleRect().Size;
        var scale = AreaGameBarPanel.ZoneWindow(view, (int)Arcanum.World.AreaInventoryDrag.FrameWidth, (int)Arcanum.World.AreaInventoryDrag.FrameHeight) is { } zone
            ? (float)zone.Scale : AreaGameBarPanel.ScreenFactor(view);
        var margin = note!.WindowLeft / scale;
        var beside = margin >= MarginWidth;
        var width = beside ? margin - 2 * Inset : Math.Min(TopWidth, view.X / scale - 2 * Inset);
        Scale = new(scale, scale);
        // The width first: the wrapped lines give the height from it.
        Size = new(width, 0);
        Size = new(width, GetCombinedMinimumSize().Y);
        Position = new(Inset * scale, beside ? view.Y - AreaGameBarPanel.BottomInset - (Size.Y + Inset) * scale : AreaGameBarPanel.TopInset + Inset * scale);
        TopReserve = beside ? 0 : Position.Y + (Size.Y + Inset) * scale;
    }

    public override void _Input(InputEvent input)
    {
        if (input is not InputEventKey { Pressed: true, Echo: false, CtrlPressed: true, ShiftPressed: true, Keycode: ToggleKey } || _describe() is null) return;
        // Not while a text field is typed in (a bug report over the window).
        if (GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit) return;
        GetViewport().SetInputAsHandled();
        _toggle();
    }
}
