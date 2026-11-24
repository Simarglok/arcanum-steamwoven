using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>What a board is painted from: the material and rim, the exact device size, the strip scale and the unit position of its top left (so it continues a strip's grain), and the
/// strip frame whose wood it matches.</summary>
internal sealed record SteamwovenBoardSpec(
    ChromeMaterial Material, ChromeSides Rim, Rect2I Rect, double Scale, double OriginX, double OriginY,
    ChromeRecipe? ToneRecipe, byte[]? ToneLegacy, int ToneWidth, int ToneHeight)
{
    /// <summary>The identity that decides a repaint (the tone pixels by reference: the strip frame is the same object while it lives).</summary>
    public (ChromeMaterial, ChromeSides, Rect2I, double, double, double, object?) Key => (Material, Rim, Rect, Scale, OriginX, OriginY, ToneLegacy);
}

/// <summary>
/// ARC-UI-2452: one generated board (the sides of a panel, a menu's backdrop) of the Steamwoven prototype: painted off the main thread at the strip scale into one texture of exactly the
/// board's device size, drawn 1:1. While it is painted the node shows nothing (the rest of the HUD stays); <see cref="Pending"/> is true until the texture is in.
/// </summary>
internal sealed partial class SteamwovenBoardView : TextureRect
{
    private (ChromeMaterial, ChromeSides, Rect2I, double, double, double, object?)? _key;
    private int _generation;
    private int _pending;
    private bool _kit;

    // ARC-UI-2576: a backdrop leaves the squares of the round views out (up to two), so the lens shows the game view under it.
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

    /// <summary>How many rectangles the board leaves out now (0 to 2), for the self-test.</summary>
    public int HoleCount { get; private set; }

    /// <summary>Leaves the given rectangles (in the board's parent's coordinates, at most two) out of the board; none restores it whole.</summary>
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

    public SteamwovenBoardView()
    {
        ExpandMode = ExpandModeEnum.IgnoreSize; StretchMode = StretchModeEnum.Scale;
        TextureFilter = TextureFilterEnum.Nearest;
        MouseFilter = MouseFilterEnum.Stop;
    }

    /// <summary>Raised on the main thread after a paint was put in.</summary>
    public event Action? Painted;

    /// <summary>True while a paint is on a worker (the texture shown, if any, is the previous one).</summary>
    public bool Pending => Volatile.Read(ref _pending) > 0;

    /// <summary>The size and place of the texture last put in, for the self-test.</summary>
    internal Vector2I PixelSizeForSelfTest => Texture is { } texture ? new Vector2I(texture.GetWidth(), texture.GetHeight()) : Vector2I.Zero;

    /// <summary>ARC-UI-2461: shows a kit part instead of a generated paint: the baked texture over <paramref name="rect"/>, nearest when exact (the baked pixels are the device pixels), else linear. A paint under way is dropped.</summary>
    public void ShowKit(Texture2D texture, Rect2I rect, bool exact)
    {
        _generation++; _key = null; _kit = true;
        Position = rect.Position; Size = rect.Size;
        Texture = texture;
        TextureFilter = exact ? TextureFilterEnum.Nearest : TextureFilterEnum.Linear;
    }

    public void Request(SteamwovenBoardSpec spec)
    {
        TextureFilter = TextureFilterEnum.Nearest;
        // Back from a kit part: its texture goes at once, so it cannot stand in for the generated paint that is still under way.
        if (_kit) { _kit = false; Texture = null; }
        Position = spec.Rect.Position; Size = spec.Rect.Size;
        if (_key == spec.Key) return;
        _key = spec.Key;
        var generation = ++_generation;
        Interlocked.Increment(ref _pending);
        Task.Run(() =>
        {
            byte[]? pixels = null;
            try
            {
                pixels = InterfaceChromeRenderer.RenderBoard(spec.Material, spec.Rim, spec.Rect.Size.X, spec.Rect.Size.Y, spec.Scale, spec.OriginX, spec.OriginY,
                    spec.ToneRecipe, spec.ToneLegacy ?? [], spec.ToneWidth, spec.ToneHeight);
            }
            catch (Exception exception)
            {
                GD.Print("ARC-UI-2452 board paint failed: " + exception.Message);
            }
            Callable.From(() => { if (IsInstanceValid(this)) Apply(generation, spec.Rect.Size, pixels); }).CallDeferred();
        });
    }

    private void Apply(int generation, Vector2I size, byte[]? pixels)
    {
        Interlocked.Decrement(ref _pending);
        if (generation != _generation || !IsInsideTree()) return;
        // A failed paint is forgotten, so the next request paints again.
        if (pixels is null) { _key = null; return; }
        using var image = Image.CreateFromData(size.X, size.Y, false, Image.Format.Rgba8, pixels);
        Texture = ImageTexture.CreateFromImage(image);
        Painted?.Invoke();
    }
}
