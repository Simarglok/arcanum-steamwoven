using Godot;

namespace Arcanum.Experiments.DisplayUiLab;

public partial class ProceduralWorld : Node2D
{
    // This is deliberately offset from the initial camera center so a zoom assertion
    // can observe its viewport movement without conflating world and UI coordinates.
    public static readonly Vector2 MarkerWorldPosition = new(950, 510);

    public override void _Draw()
    {
        DrawRect(new Rect2(-1600, -1000, 3200, 2000), new Color("16263a"));
        for (var x = -800; x <= 1600; x += 80)
            DrawLine(new Vector2(x, -800), new Vector2(x + 800, 800), new Color("28445a"), 1);
        for (var y = -800; y <= 800; y += 80)
            DrawLine(new Vector2(-800, y), new Vector2(1600, y - 1200), new Color("28445a"), 1);
        DrawCircle(MarkerWorldPosition, 22, new Color("f4c95d"));
        DrawCircle(new Vector2(360, 300), 36, new Color("589f70"));
        DrawCircle(new Vector2(950, 510), 30, new Color("ad6a6c"));
    }
}
