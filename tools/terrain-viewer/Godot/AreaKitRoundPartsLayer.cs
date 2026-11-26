using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-3101: the round parts a view draws as separate arts while the kit's background shows (<see cref="AreaKitRoundParts"/>): a child layer of the view that draws each part through the shader of the rules, the art cut to the circle of its ring and
/// under it the generated soft shadow (a socket dome has none, ARC-UI-3347). A view collects what it would have drawn with <c>DrawTextureRect</c> into a list in its own <c>_Draw</c> and gives it to <see cref="Show"/> at its end; <see cref="Clear"/> hides the layer where the kit does not show
/// (the view then draws its parts as before). The shadows lie under every part of the layer (a part never takes the shadow of its neighbour). The shader works in the art's own units, so the cut is one device pixel wide at any scale and for the Legacy
/// frame as for a pack frame; a texture whose size is not the table's art size is drawn as it is. ARC-UI-3320: a part may be a kit face (<see cref="KitItem"/>: the master's own size and straight alpha, already cut to the same circle); it is stretched to the art's rectangle and goes through the same cut and shadow.
/// </summary>
internal sealed partial class AreaKitRoundPartsLayer : Control
{
    /// <summary>One part to draw: its art number, its texture and its rectangle in the view's coordinates. <paramref name="Kit"/> (ARC-UI-3320): the texture is a kit face (straight alpha, linear sampling, the master's own size), not the art's frame.</summary>
    internal readonly record struct Item(int Art, Texture2D Texture, Rect2 Rect, bool Kit = false);

    private static Shader? _shader;
    private readonly Control _shadowHost = new() { Name = "Shadows", MouseFilter = MouseFilterEnum.Ignore };
    private readonly Control _partHost = new() { Name = "Parts", MouseFilter = MouseFilterEnum.Ignore };
    private readonly List<PartNode> _shadows = [], _parts = [];
    private IReadOnlyList<int> _shown = [];

    internal AreaKitRoundPartsLayer()
    {
        Name = "KitRoundParts";
        MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_shadowHost); AddChild(_partHost);
    }

    /// <summary>The arts the layer shows now, in order, for the self-test.</summary>
    internal IReadOnlyList<int> ShownForSelfTest => _shown;

    /// <summary>The cut and the shadow of a part for the self-test: the node's rectangle and its shader's disc, or null when the layer shows no such part.</summary>
    internal (Rect2 Part, Rect2 Shadow, Vector3 Disc)? NodesForSelfTest(int art)
    {
        for (var i = 0; i < _shown.Count; i++)
            if (_shown[i] == art)
                return (new Rect2(_parts[i].Position, _parts[i].Size), new Rect2(_shadows[i].Position, _shadows[i].Size), (Vector3)_parts[i].Material!.Get("shader_parameter/disc"));
        return null;
    }

    /// <summary>True when the art has a round part (the view draws it through the layer while the kit shows) and <paramref name="texture"/> has the table's size.</summary>
    internal static bool Applies(int art, Texture2D? texture) =>
        texture is not null && AreaKitRoundParts.Of(art) is { } part && texture.GetWidth() == part.Width && texture.GetHeight() == part.Height;

    /// <summary>
    /// ARC-UI-3320: the item of a kit face of <paramref name="art"/> at the art's place (<paramref name="place"/>, in the view's units): the rectangle is the table's art size, whatever the master's size (the shader works in the art's units and the
    /// face, already cut to the circle at its own size, is stretched to it), or null when the art is no round part.
    /// </summary>
    internal static Item? KitItem(int art, Texture2D face, (int X, int Y) place) => KitItem(art, face, (place.X, place.Y), 1f);

    /// <summary>
    /// ARC-UI-3347: as <see cref="KitItem(int, Texture2D, ValueTuple{int, int})"/> for a view that draws in screen pixels: the art's rectangle at <paramref name="place"/> (units) times <paramref name="scale"/>. A socket dome's rectangle moves to the
    /// socket's centre first (<see cref="AreaKitRoundParts.SocketShift"/>).
    /// </summary>
    internal static Item? KitItem(int art, Texture2D face, (float X, float Y) place, float scale)
    {
        if (AreaKitRoundParts.Of(art) is not { } part) return null;
        var (dx, dy) = AreaKitRoundParts.SocketShift(art, place.X, place.Y);
        return new Item(art, face, new Rect2((place.X + dx) * scale, (place.Y + dy) * scale, part.Width * scale, part.Height * scale), true);
    }

    /// <summary>Hides every part: the view draws its arts as before.</summary>
    internal void Clear()
    {
        if (_shown.Count == 0) return;
        _shown = [];
        _shadowHost.Visible = _partHost.Visible = false;
    }

    /// <summary>Shows exactly <paramref name="items"/> (each one a part <see cref="Applies"/> to).</summary>
    internal void Show(IReadOnlyList<Item> items)
    {
        if (items.Count == 0) { Clear(); return; }
        _shadowHost.Visible = _partHost.Visible = true;
        while (_parts.Count < items.Count)
        {
            var shadow = new PartNode(true); var part = new PartNode(false);
            _shadows.Add(shadow); _parts.Add(part);
            _shadowHost.AddChild(shadow); _partHost.AddChild(part);
        }
        for (var i = 0; i < _parts.Count; i++)
        {
            if (i >= items.Count) { _shadows[i].Visible = _parts[i].Visible = false; continue; }
            var (art, texture, rect, kit) = items[i];
            var part = AreaKitRoundParts.Of(art)!.Value;
            _parts[i].Place(part, texture, rect, kit);
            _shadows[i].Place(part, null, rect, false);
            _shadows[i].Visible = !AreaKitRoundParts.IsSocketDome(art);
        }
        _shown = [.. items.Select(item => item.Art)];
    }

    private static Shader Shared => _shader ??= new Shader { Code = AreaKitRoundParts.Shader };

    /// <summary>A part (the texture cut to the circle) or its shadow (the rectangle grown by the shadow's reach), one shader material each.</summary>
    private sealed partial class PartNode : Control
    {
        private readonly bool _shadow;
        private readonly ShaderMaterial _material = new() { Shader = Shared };
        private Texture2D? _texture;

        internal PartNode(bool shadow)
        {
            _shadow = shadow;
            MouseFilter = MouseFilterEnum.Ignore;
            Material = _material;
        }

        internal void Place(AreaKitRoundParts.Part part, Texture2D? texture, Rect2 rect, bool kit)
        {
            Visible = true;
            _texture = texture;
            // A kit face is a straight-alpha PNG at the master's size (about 3.5 texels to the unit): sampled linearly and premultiplied in the shader; an art's frame is premultiplied already (the view's own filter).
            TextureFilter = kit ? TextureFilterEnum.Linear : TextureFilterEnum.ParentNode;
            _material.SetShaderParameter("straight", kit ? 1f : 0f);
            var ratio = new Vector2(rect.Size.X / part.Width, rect.Size.Y / part.Height);
            _material.SetShaderParameter("disc", new Vector3(part.Cx, part.Cy, part.CutRadius));
            if (_shadow)
            {
                var reach = AreaKitRoundParts.ShadowReach;
                Position = rect.Position - new Vector2(reach, reach) * ratio;
                Size = rect.Size + new Vector2(reach, reach) * ratio * 2;
                _material.SetShaderParameter("mode", 1f);
                _material.SetShaderParameter("unit_size", new Vector2(part.Width + 2 * reach, part.Height + 2 * reach));
                _material.SetShaderParameter("origin", new Vector2(reach, reach));
                _material.SetShaderParameter("shadow", new Vector2(AreaKitRoundParts.ShadowOffset, AreaKitRoundParts.ShadowOffset));
                _material.SetShaderParameter("shadow_shape", new Vector2(AreaKitRoundParts.ShadowSigma, AreaKitRoundParts.ShadowStrength));
            }
            else
            {
                Position = rect.Position; Size = rect.Size;
                _material.SetShaderParameter("mode", 0f);
                _material.SetShaderParameter("unit_size", new Vector2(part.Width, part.Height));
                _material.SetShaderParameter("origin", Vector2.Zero);
            }
            QueueRedraw();
        }

        public override void _Draw()
        {
            if (_shadow) DrawRect(new Rect2(Vector2.Zero, Size), Colors.White);
            else if (_texture is not null) DrawTextureRect(_texture, new Rect2(Vector2.Zero, Size), false);
        }
    }
}
