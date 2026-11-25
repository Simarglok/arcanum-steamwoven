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
        // ARC-UI-3101: the rectangle drawn is the hole's square grown by this many canvas units on every side, so the copy lies under the ring (the background's own edge is smooth and covers it) and no pixel of the old surface shows in a gap.
        uniform float margin = 0.0;
        void fragment() {
            // The hole's own UV (0..1 over the square, beyond it in the margin) and the screen span of one such unit: the canvas-to-screen scale (and a flipped axis) without knowing the stretch.
            vec2 uv = (UV * (hole_side + 2.0 * margin) - vec2(margin)) / hole_side;
            vec2 span = vec2(dFdx(SCREEN_UV.x) / dFdx(uv.x), dFdy(SCREEN_UV.y) / dFdy(uv.y));
            // ARC-UI-3101 (#3276): the copy is the circle inscribed in its square (the lens's circle) grown by the margin; the square's corners show what is under them.
            vec2 round_q = uv - vec2(0.5);
            float limit = 0.5 + margin / hole_side;
            if (dot(round_q, round_q) > limit * limit) discard;
            vec2 delta = (source_origin - hole_origin) + uv * (source_side - hole_side);
            COLOR = vec4(texture(screen_texture, SCREEN_UV + delta / hole_side * span).rgb, 1.0);
        }
        """;
    /// <summary>ARC-UI-3101: how far the copy reaches under the ring beyond the lens circle, in screen pixels (the background's soft edge is 1 px wide).</summary>
    private const float OverlapPixels = 2f;
    private static readonly List<AreaRoundView> Live = [];
    private readonly ColorRect _hole;
    private Rect2 _square;
    private readonly ShaderMaterial _material;

    public AreaRoundView()
    {
        Name = "RoundViewLayer"; Layer = 3; Visible = false;
        _material = new ShaderMaterial { Shader = new Shader { Code = HoleShader } };
        _hole = new ColorRect { Name = "RoundView", MouseFilter = Control.MouseFilterEnum.Ignore, Material = _material };
        AddChild(_hole);
    }

    /// <summary>ARC-UI-2576: the squares (canvas units) of the round views that show now: the menu backdrop leaves them out, so the copy of the game view is not covered.</summary>
    public static IReadOnlyList<Rect2> VisibleRects()
    {
        lock (Live) return [.. Live.Where(view => view.Visible && view._square.Size.X > 0).Select(view => view._square)];
    }

    /// <summary>ARC-UI-2576: raised on the main thread when a round view was placed or hidden (the backdrop leaves the squares out in the same frame).</summary>
    public static event Action? RectsChanged;

    public override void _EnterTree() { lock (Live) { if (!Live.Contains(this)) Live.Add(this); } }

    public override void _ExitTree() { lock (Live) Live.Remove(this); }

    /// <summary>Shows the game view around <paramref name="holder"/> (its foot point in canvas units and the game view's zoom) in
    /// <paramref name="area"/>'s rectangle for its drawn mode, or hides it (null); the frame leaves the rectangle out while it shows.</summary>
    public void Update(AreaInventoryGridArea area, (Vector2 Foot, float Zoom)? holder)
    {
        ArgumentNullException.ThrowIfNull(area);
        area.RoundViewCut = holder is not null;
        var (x, y) = AreaInventoryHole.Corner(area.LookMode);
        Update(area.GlobalPosition + new Vector2(x, y) * area.FrameScale, AreaLensPlace.Side * area.FrameScale, holder);
    }

    /// <summary>ARC-UI-098: shows the game view around <paramref name="holder"/> in the square of <paramref name="side"/> screen pixels whose
    /// corner is <paramref name="origin"/> (the logbook's lens, ledger LB23), or hides it (null).</summary>
    public void Update(Vector2 origin, float side, (Vector2 Foot, float Zoom)? holder)
    {
        // ARC-UI-2576: with the Steamwoven layout the copy lies on layer 2, under the menu backdrop (layer 3), which leaves the lens's square out, so the copy reads the world and not the backdrop; the windows (layer 4)
        // draw the lens ring over it. Otherwise the layer stays 3, as it was (the copy reads the spell pane and the layer-3 parts).
        var layer = AreaGameBarPanel.SteamwovenOn ? 2 : 3;
        if (Layer != layer) Layer = layer;
        var wasVisible = Visible;
        Visible = holder is not null;
        if (holder is not { } shown) { if (wasVisible) RectsChanged?.Invoke(); return; }
        // ARC-UI-3101: the square the background leaves a circle out of is (origin, side); the copy is drawn OverlapPixels larger on every side, under the ring.
        _square = new Rect2(origin, new Vector2(side, side));
        _hole.Position = origin - new Vector2(OverlapPixels, OverlapPixels); _hole.Size = new(side + 2 * OverlapPixels, side + 2 * OverlapPixels);
        _material.SetShaderParameter("margin", OverlapPixels);
        var source = AreaInventoryHole.Source(shown.Foot.X, shown.Foot.Y, shown.Zoom);
        _material.SetShaderParameter("hole_origin", origin);
        _material.SetShaderParameter("hole_side", side);
        _material.SetShaderParameter("source_origin", new Vector2(source.X, source.Y));
        _material.SetShaderParameter("source_side", source.Side);
        RectsChanged?.Invoke();
    }
}
