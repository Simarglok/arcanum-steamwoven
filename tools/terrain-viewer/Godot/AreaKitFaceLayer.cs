using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-3341: the flat small controls a window draws whole from the kit (<see cref="SteamwovenKitTable.WindowFaces"/>: the sheet's schematics tab, the schematic window's page arrows): a child layer of the view, sampled linearly and with no
/// material of its own, because the faces are straight-alpha masters of about 3.6 texels to the unit while the views draw their arts premultiplied and nearest. A view collects what it would have drawn with the art's own frame in its <c>_Draw</c>
/// and gives the list to <see cref="Show"/> at its end; each face is stretched to the art's rectangle, so the control's place and pointer area are the original's.
/// </summary>
internal sealed partial class AreaKitFaceLayer : Control
{
    /// <summary>One face to draw: the art it stands for, its texture and its rectangle in the view's units.</summary>
    internal readonly record struct Item(int Art, Texture2D Texture, Rect2 Rect);

    private Item[] _items = [];

    internal AreaKitFaceLayer()
    {
        Name = "KitFaces";
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.Linear;
    }

    /// <summary>The faces the layer shows now, in the order given, for the self-test.</summary>
    internal IReadOnlyList<Item> ShownForSelfTest => _items;

    /// <summary>Shows exactly <paramref name="items"/> (none: the layer draws nothing).</summary>
    internal void Show(IReadOnlyList<Item> items)
    {
        if (_items.SequenceEqual(items)) return;
        _items = [.. items];
        QueueRedraw();
    }

    public override void _Draw()
    {
        foreach (var item in _items) DrawTextureRect(item.Texture, item.Rect, false);
    }
}
