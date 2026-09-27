using Godot;

namespace Arcanum.Experiments.DisplayUiLab;

internal static class UiScalePolicy
{
    internal static float Factor(UiScalePreset preset) => (int)preset / 100.0f;

    internal static ResponsiveClass Classify(Vector2 viewport, UiScalePreset preset)
    {
        var factor = Factor(preset);
        var effective = viewport / factor;
        if (effective.X >= 1200 && effective.Y >= 680)
            return ResponsiveClass.Wide;
        return effective.X >= 800 && effective.Y >= 500
            ? ResponsiveClass.Medium
            : ResponsiveClass.Compact;
    }

    internal static float Target(float basePixels, UiScalePreset preset) => basePixels * Factor(preset);

    internal static void Apply(Control control, UiScalePreset preset, int baseFontSize = 16)
    {
        control.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(baseFontSize * Factor(preset)));
        control.AddThemeConstantOverride("separation", Mathf.RoundToInt(8 * Factor(preset)));
    }

    internal static StyleBoxFlat PanelStyle(Color background, UiScalePreset preset)
    {
        var padding = Mathf.RoundToInt(12 * Factor(preset));
        return new StyleBoxFlat
        {
            BgColor = background,
            BorderColor = new Color("92b5d4"),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
            ContentMarginLeft = padding,
            ContentMarginTop = padding,
            ContentMarginRight = padding,
            ContentMarginBottom = padding,
        };
    }
}
