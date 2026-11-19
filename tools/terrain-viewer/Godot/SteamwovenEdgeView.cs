using Arcanum.Presentation.Steamwoven;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-2890 (AC2): the generated end caps of the Steamwoven column: on each part of the column that borders the world (the strips, and the middle band while a menu window is open) a brass edge over the outermost
/// pixels and, outside it, a soft shadow thrown on the world, falling off with the square of the distance, clipped to the frame. Placeholder art drawn from numbers (the real caps are Codex art); no pointer events.
/// </summary>
internal sealed partial class SteamwovenEdgeView : Control
{
    private static readonly Color BrassDark = new(0.26f, 0.18f, 0.07f), BrassBright = new(0.86f, 0.69f, 0.31f), BrassMid = new(0.58f, 0.42f, 0.17f);
    private const float ShadowAlpha = 0.5f;
    private const int ShadowSlices = 8;

    private IReadOnlyList<PixelRect> _parts = [];
    private int _brass, _shadow;
    private PixelRect _frame;

    public SteamwovenEdgeView() => MouseFilter = MouseFilterEnum.Ignore;

    /// <summary>What the view shows now: the parts it caps, for the self-test.</summary>
    internal IReadOnlyList<PixelRect> PartsForSelfTest => _parts;

    public void Set(IReadOnlyList<PixelRect> parts, int brassWidth, int shadowWidth, PixelRect frame)
    {
        if (_brass == brassWidth && _shadow == shadowWidth && _frame == frame && _parts.SequenceEqual(parts)) return;
        (_parts, _brass, _shadow, _frame) = (parts, brassWidth, shadowWidth, frame);
        QueueRedraw();
    }

    public override void _Draw()
    {
        foreach (var part in _parts)
        {
            // The shadow on the world, outside the part, in slices that fall off quadratically from the edge.
            for (var i = 0; i < ShadowSlices; i++)
            {
                var near = _shadow * i / ShadowSlices;
                var far = _shadow * (i + 1) / ShadowSlices;
                if (far <= near) continue;
                var alphaNear = ShadowAlpha * Square(1f - (float)i / ShadowSlices);
                var alphaFar = ShadowAlpha * Square(1f - (float)(i + 1) / ShadowSlices);
                Slice(part.X - far, part.X - near, part, alphaFar, alphaNear);
                Slice(part.Right + near, part.Right + far, part, alphaNear, alphaFar);
            }
            Brass(part.X, 1, part);
            Brass(part.Right - _brass, -1, part);
        }
    }

    private static float Square(float value) => value * value;

    // A slice of shadow from x0 to x1 (clipped to the frame), black with alpha a0 at x0 and a1 at x1.
    private void Slice(int x0, int x1, PixelRect part, float a0, float a1)
    {
        var (left, right) = (Math.Max(x0, _frame.X), Math.Min(x1, _frame.Right));
        if (right <= left) return;
        // Clipping keeps the falloff of the whole slice: the alphas at the clipped ends interpolate along it.
        float At(int x) => a0 + (a1 - a0) * (x - x0) / (x1 - x0);
        var points = new Vector2[] { new(left, part.Y), new(right, part.Y), new(right, part.Bottom), new(left, part.Bottom) };
        var colors = new[] { new Color(0, 0, 0, At(left)), new Color(0, 0, 0, At(right)), new Color(0, 0, 0, At(right)), new Color(0, 0, 0, At(left)) };
        DrawPolygon(points, colors);
    }

    // The brass edge: dark outer line, bright highlight, mid brass (each a third of the width, at least a pixel), from the outer edge inward; direction +1 for a left edge starting at x, -1 for a right edge whose outer edge is its right side.
    private void Brass(int x, int direction, PixelRect part)
    {
        var third = Math.Max(1, _brass / 3);
        var widths = new[] { third, third, Math.Max(0, _brass - 2 * third) };
        var tones = new[] { BrassDark, BrassBright, BrassMid };
        var edge = direction > 0 ? x : x + _brass;
        for (var i = 0; i < widths.Length; i++)
        {
            if (widths[i] <= 0) continue;
            var left = direction > 0 ? edge : edge - widths[i];
            DrawRect(new Rect2(left, part.Y, widths[i], part.Height), tones[i]);
            edge += direction * widths[i];
        }
    }
}
