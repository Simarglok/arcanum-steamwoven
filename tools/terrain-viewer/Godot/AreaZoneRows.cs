using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-2615: the rows the inventory, loot, barter and item service windows show around their 800 × 400 frame (the quantity row above it, the look switch and the message line below it). In the window zone of the
/// Steamwoven layout the zone is the middle band itself, so there is no place around the frame: while docked the rows lie over the frame's own edges, in the original's units scaled with the frame (the quantity row
/// along the top edge, one line with the switch and the message along the bottom edge), so the panel is the frame and nothing of it hangs under a strip. Undocked, the rows are back in the window's column.
/// </summary>
internal sealed class AreaZoneRows
{
    /// <summary>The rows' text size in the original's units (the logbook's switch is drawn at the same size), so 9 px at the least at unit scale 1.</summary>
    private const int RowFontSize = 9;
    private const int OutlineUnits = 3;

    private readonly VBoxContainer _contents;
    private readonly Control[] _home;
    private readonly Control[] _top;
    private readonly Control[] _bottom;
    private readonly Control _layer;
    private readonly VBoxContainer _column;
    private readonly HBoxContainer _line;
    private bool _docked;

    /// <summary>Call after <paramref name="contents"/> (the column of <paramref name="panel"/>) holds all its controls: their order is the one an undock restores.
    /// <paramref name="top"/> go along the frame's top edge, <paramref name="bottom"/> share the line along its bottom edge, left to right (a label takes the rest of it).</summary>
    public AreaZoneRows(PanelContainer panel, VBoxContainer contents, Control[] top, Control[] bottom)
    {
        _contents = contents;
        _home = contents.GetChildren().Cast<Control>().ToArray();
        _top = top;
        _bottom = bottom;
        // The panel container fits its children and resets their scale, so the scaled column hangs on a plain layer of its own.
        _layer = new Control { Name = "ZoneRows", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _column.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        _line = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _column.AddChild(_line);
        _layer.AddChild(_column);
        panel.AddChild(_layer);
    }

    /// <summary>True while the rows lie over the frame.</summary>
    public bool Docked => _docked;

    /// <summary>Puts the rows over the frame drawn at <paramref name="scale"/> (called every frame while the window is in the zone).</summary>
    public void Dock(float scale)
    {
        _column.Scale = new(scale, scale);
        _column.Size = new(AreaInventoryDrag.FrameWidth, AreaInventoryDrag.FrameHeight);
        if (_docked) return;
        _docked = true;
        KeepingFocus(() =>
        {
            _layer.Show();
            for (var index = 0; index < _top.Length; index++)
            {
                Move(_top[index], _column);
                _column.MoveChild(_top[index], index);
                // An empty part of the row takes no click from the frame under it.
                _top[index].MouseFilter = Control.MouseFilterEnum.Ignore;
            }
            foreach (var row in _bottom)
            {
                Move(row, _line);
                if (row is Label) row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            }
            foreach (var text in Texts())
            {
                text.AddThemeFontSizeOverride("font_size", RowFontSize);
                text.AddThemeConstantOverride("outline_size", OutlineUnits);
                text.AddThemeColorOverride("font_outline_color", Colors.Black);
            }
        });
    }

    /// <summary>Puts the rows back at their places in the window's column (the diagnostic window and the layouts outside the zone).</summary>
    public void Undock()
    {
        if (!_docked) return;
        _docked = false;
        KeepingFocus(() =>
        {
            foreach (var text in Texts())
            {
                text.RemoveThemeFontSizeOverride("font_size");
                text.RemoveThemeConstantOverride("outline_size");
                text.RemoveThemeColorOverride("font_outline_color");
            }
            foreach (var row in _top) row.MouseFilter = Control.MouseFilterEnum.Pass;
            foreach (var row in _bottom) if (row is Label) row.SizeFlagsHorizontal = Control.SizeFlags.Fill;
            for (var index = 0; index < _home.Length; index++)
            {
                Move(_home[index], _contents);
                _contents.MoveChild(_home[index], index);
            }
            _layer.Hide();
        });
    }

    private IEnumerable<Control> Texts() => _top.Concat(_bottom).SelectMany(row => row.GetChildren().OfType<Control>().Prepend(row)).Where(control => control is Label or Button);

    private static void Move(Control control, Node parent)
    {
        if (control.GetParent() == parent) return;
        control.GetParent().RemoveChild(control);
        parent.AddChild(control);
    }

    // A control loses the focus when it leaves its parent: the focused row control keeps it across a move.
    private void KeepingFocus(Action move)
    {
        var focus = _contents.IsInsideTree() ? _contents.GetViewport().GuiGetFocusOwner() : null;
        move();
        if (focus is not null && focus.IsVisibleInTree() && !focus.HasFocus()) focus.GrabFocus();
    }
}
