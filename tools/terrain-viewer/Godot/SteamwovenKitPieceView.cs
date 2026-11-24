using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-2461, ARC-UI-3143: one baked piece of an open window's kit background (SWI-BDR-3): the texture over its exact device rectangle, drawn 1:1 (Nearest when the baked pixels are the device pixels, else Linear).
/// A background leaves the squares of the round views (up to two) out, so the lens shows the game view under it. ARC-UI-3143: this is what remains of the board view of the retired generated chrome.
/// </summary>
internal sealed partial class SteamwovenKitPieceView : TextureRect
{
    // ARC-UI-2576: the squares of the round views (up to two) are left out, so the lens shows the game view under the piece.
    private const string HoleShader = """
        shader_type canvas_item;
        uniform vec2 view_size = vec2(1.0);
        uniform vec4 hole_a = vec4(0.0);
        uniform vec4 hole_b = vec4(0.0);
        bool inside(vec4 h, vec2 p) { return h.z > 0.0 && p.x >= h.x && p.x < h.x + h.z && p.y >= h.y && p.y < h.y + h.w; }
        void fragment() {
            vec2 p = UV * view_size;
            if (inside(hole_a, p) || inside(hole_b, p)) discard;
        }
        """;
    private ShaderMaterial? _holes;
    private (Rect2, Rect2, Rect2) _holeKey;

    public SteamwovenKitPieceView()
    {
        ExpandMode = ExpandModeEnum.IgnoreSize; StretchMode = StretchModeEnum.Scale;
        TextureFilter = TextureFilterEnum.Nearest;
        MouseFilter = MouseFilterEnum.Stop;
    }

    /// <summary>How many rectangles the piece leaves out now (0 to 2), for the self-test.</summary>
    public int HoleCount { get; private set; }

    /// <summary>The size of the texture shown, for the self-test.</summary>
    internal Vector2I PixelSizeForSelfTest => Texture is { } texture ? new Vector2I(texture.GetWidth(), texture.GetHeight()) : Vector2I.Zero;

    /// <summary>Leaves the given rectangles (in the piece's parent's coordinates, at most two) out of the piece; none restores it whole.</summary>
    public void SetHoles(IReadOnlyList<Rect2> rects)
    {
        var a = rects.Count > 0 ? rects[0] : default;
        var b = rects.Count > 1 ? rects[1] : default;
        HoleCount = Math.Min(2, rects.Count);
        if (rects.Count == 0 && _holes is null) return;
        var key = (a, b, new Rect2(Position, Size));
        if (_holes is not null && _holeKey == key && (Material is null) == (rects.Count == 0)) return;
        _holeKey = key;
        _holes ??= new ShaderMaterial { Shader = new Shader { Code = HoleShader } };
        Material = rects.Count == 0 ? null : _holes;
        if (rects.Count == 0) return;
        _holes.SetShaderParameter("view_size", Size);
        Vector4 Local(Rect2 r) => r.Size.X > 0 ? new Vector4(r.Position.X - Position.X, r.Position.Y - Position.Y, r.Size.X, r.Size.Y) : Vector4.Zero;
        _holes.SetShaderParameter("hole_a", Local(a));
        _holes.SetShaderParameter("hole_b", Local(b));
    }

    /// <summary>Shows a kit piece: the baked texture over <paramref name="rect"/>, nearest when exact (the baked pixels are the device pixels), else linear.</summary>
    public void ShowKit(Texture2D texture, Rect2I rect, bool exact)
    {
        Position = rect.Position; Size = rect.Size;
        Texture = texture;
        TextureFilter = exact ? TextureFilterEnum.Nearest : TextureFilterEnum.Linear;
    }
}
