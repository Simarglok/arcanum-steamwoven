using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-054: the round view's copy of the game view (IW33), on a layer under the window so the copy reads the world, not the window
/// (PROJECT: it reads what layers 1 and 2 drew too, floaters and the spell pane, where the original copies the game view alone). ARC-UI-064:
/// one component for the inventory panel and the barter panel; the frame's mode places the rectangle (<see cref="AreaInventoryHole"/>).
/// </summary>
public partial class AreaRoundView : CanvasLayer
{
    private const string HoleShader = """
        shader_type canvas_item;
        render_mode unshaded, blend_disabled;
        uniform sampler2D screen_texture : hint_screen_texture, filter_nearest, repeat_disable;
        // The hole's and the source square's corners and sides in canvas units (the same layer transform).
        uniform vec2 hole_origin;
        uniform float hole_side = 1.0;
        uniform vec2 source_origin;
        uniform float source_side = 1.0;
        void fragment() {
            // The screen span of one UV unit of the hole: the canvas-to-screen scale (and a flipped axis) without knowing the stretch.
            vec2 span = vec2(dFdx(SCREEN_UV.x) / dFdx(UV.x), dFdy(SCREEN_UV.y) / dFdy(UV.y));
            vec2 delta = (source_origin - hole_origin) + UV * (source_side - hole_side);
            COLOR = vec4(texture(screen_texture, SCREEN_UV + delta / hole_side * span).rgb, 1.0);
        }
        """;
    private static readonly List<AreaRoundView> Live = [];
    private readonly ColorRect _hole;
    private readonly ShaderMaterial _material;

    public AreaRoundView()
    {
        // ARC-UI-2576: layer 2, under the Steamwoven menu backdrop (layer 3, which leaves the lens's square out), so the copy reads the world and not the backdrop; the windows (layer 4) draw the lens ring over it.
        Name = "RoundViewLayer"; Layer = 2; Visible = false;
        lock (Live) Live.Add(this);
        _material = new ShaderMaterial { Shader = new Shader { Code = HoleShader } };
        _hole = new ColorRect { Name = "RoundView", MouseFilter = Control.MouseFilterEnum.Ignore, Material = _material };
        AddChild(_hole);
    }

    /// <summary>ARC-UI-2576: the squares (canvas units) of the round views that show now: the menu backdrop leaves them out, so the copy of the game view is not covered.</summary>
    public static IReadOnlyList<Rect2> VisibleRects()
    {
        lock (Live) return [.. Live.Where(view => view.Visible && view._hole.Size.X > 0).Select(view => new Rect2(view._hole.Position, view._hole.Size))];
    }

    public override void _ExitTree() { lock (Live) Live.Remove(this); }

    /// <summary>Shows the game view around <paramref name="holder"/> (its foot point in canvas units and the game view's zoom) in
    /// <paramref name="area"/>'s rectangle for its drawn mode, or hides it (null); the frame leaves the rectangle out while it shows.</summary>
    public void Update(AreaInventoryGridArea area, (Vector2 Foot, float Zoom)? holder)
    {
        ArgumentNullException.ThrowIfNull(area);
        area.RoundViewCut = holder is not null;
        var (x, y) = AreaInventoryHole.Corner(area.LookMode);
        Update(area.GlobalPosition + new Vector2(x, y) * area.FrameScale, AreaInventoryHole.Side * area.FrameScale, holder);
    }

    /// <summary>ARC-UI-098: shows the game view around <paramref name="holder"/> in the square of <paramref name="side"/> screen pixels whose
    /// corner is <paramref name="origin"/> (the logbook's lens, ledger LB23), or hides it (null).</summary>
    public void Update(Vector2 origin, float side, (Vector2 Foot, float Zoom)? holder)
    {
        Visible = holder is not null;
        if (holder is not { } shown) return;
        _hole.Position = origin; _hole.Size = new(side, side);
        var source = AreaInventoryHole.Source(shown.Foot.X, shown.Foot.Y, shown.Zoom);
        _material.SetShaderParameter("hole_origin", origin);
        _material.SetShaderParameter("hole_side", side);
        _material.SetShaderParameter("source_origin", new Vector2(source.X, source.Y));
        _material.SetShaderParameter("source_side", source.Side);
    }
}
