using Arcanum.Experiments.HdUiDensityLab.Core;
using Godot;

namespace Arcanum.Experiments.HdUiDensityLab;

/// <summary>Draws a <see cref="ScreenModel"/>: filled rectangles on whole device pixels (no anti-aliasing), text at the device
/// size with Godot's fallback font, and each vector lens as a <see cref="ColorRect"/> with the distance-field shader.</summary>
public partial class MockCanvas : Control
{
    private static readonly Color[][] Glass =
    [
        [new Color(0.55f, 0.75f, 0.85f), new Color(0.10f, 0.22f, 0.32f)],
        [new Color(0.60f, 0.85f, 0.60f), new Color(0.10f, 0.28f, 0.14f)],
        [new Color(0.95f, 0.78f, 0.45f), new Color(0.35f, 0.20f, 0.06f)],
    ];

    private readonly List<ColorRect> _lensNodes = [];
    private Shader? _lensShader;
    private ScreenModel? _model;

    public ScreenModel? Model
    {
        get => _model;
        set
        {
            _model = value;
            SyncLenses();
            QueueRedraw();
        }
    }

    public static Color ColorOf(Token token)
    {
        var (r, g, b) = Palette.Rgb(token);
        return Color.Color8(r, g, b);
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.Nearest;
        _lensShader = GD.Load<Shader>("res://scripts/LensRing.gdshader");
        SyncLenses();
    }

    public override void _Draw()
    {
        if (_model is null) return;
        foreach (var op in _model.Ops)
            DrawRect(new Rect2(op.Rect.X, op.Rect.Y, op.Rect.Width, op.Rect.Height), ColorOf(op.Token), filled: true, antialiased: false);
        var font = ThemeDB.FallbackFont;
        foreach (var text in _model.Texts)
        {
            var baseline = text.Y + Mathf.RoundToInt(font.GetAscent(text.SizePx));
            DrawString(font, new Vector2(text.X, baseline), text.Text, HorizontalAlignment.Left, -1, text.SizePx, ColorOf(text.Token));
        }
    }

    private void SyncLenses()
    {
        if (_lensShader is null) return;
        var lenses = _model?.Lenses ?? [];
        while (_lensNodes.Count < lenses.Count)
        {
            var node = new ColorRect { Material = new ShaderMaterial { Shader = _lensShader }, MouseFilter = MouseFilterEnum.Ignore };
            AddChild(node);
            _lensNodes.Add(node);
        }
        for (var i = 0; i < _lensNodes.Count; i++)
        {
            var node = _lensNodes[i];
            node.Visible = i < lenses.Count;
            if (!node.Visible) continue;
            var lens = lenses[i];
            var half = lens.OuterRadiusPx + 2;
            node.Position = new Vector2(lens.CenterX - half, lens.CenterY - half);
            node.Size = new Vector2(2 * half, 2 * half);
            var angle = Mathf.DegToRad(lens.LightAngleDegrees);
            var glass = Glass[Math.Clamp(lens.GlassIndex, 0, Glass.Length - 1)];
            var material = (ShaderMaterial)node.Material;
            material.SetShaderParameter("box_px", new Vector2(2 * half, 2 * half));
            material.SetShaderParameter("outer_r", (float)lens.OuterRadiusPx);
            material.SetShaderParameter("ring_w", (float)lens.RingPx);
            material.SetShaderParameter("bevel_w", (float)lens.BevelPx);
            material.SetShaderParameter("light_dir", new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)));
            material.SetShaderParameter("brass_light", ColorOf(Token.BrassLight));
            material.SetShaderParameter("brass_mid", ColorOf(Token.Brass));
            material.SetShaderParameter("brass_dark", ColorOf(Token.BrassDark));
            material.SetShaderParameter("glass_inner", glass[0]);
            material.SetShaderParameter("glass_outer", glass[1]);
        }
    }
}
