using Godot;

namespace Arcanum.Experiments.DisplayUiLab;

internal static class ResponsiveLayout
{
    internal static void SetRect(Control control, Rect2 rect)
    {
        control.Position = rect.Position;
        control.Size = rect.Size;
    }

    internal static Rect2 ClampToViewport(Rect2 candidate, Vector2 viewport)
    {
        var size = new Vector2(Mathf.Min(candidate.Size.X, viewport.X), Mathf.Min(candidate.Size.Y, viewport.Y));
        var position = new Vector2(
            Mathf.Clamp(candidate.Position.X, 0, Mathf.Max(0, viewport.X - size.X)),
            Mathf.Clamp(candidate.Position.Y, 0, Mathf.Max(0, viewport.Y - size.Y)));
        return new Rect2(position, size);
    }
}
