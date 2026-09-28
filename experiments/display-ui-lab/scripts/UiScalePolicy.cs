using Godot;

namespace Arcanum.Experiments.DisplayUiLab;

internal static class UiScalePolicy
{
    private static readonly Color Foreground = new("f2f7ff");
    private static readonly Color PanelBackground = new("122235");
    private static readonly Color ButtonBackground = new("244563");
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
        control.AddThemeColorOverride("font_color", Foreground);
        if (control is Button button)
        {
            button.AddThemeStyleboxOverride("normal", ButtonStyle(ButtonBackground, preset));
            button.AddThemeStyleboxOverride("hover", ButtonStyle(new Color("315b80"), preset));
            button.AddThemeStyleboxOverride("focus", ButtonStyle(new Color("315b80"), preset, 2));
        }
    }

    internal static void ApplyContainer(Container container, UiScalePreset preset, int baseSeparation = 8)
        => container.AddThemeConstantOverride("separation", Mathf.RoundToInt(baseSeparation * Factor(preset)));

    internal static bool ConfiguredContrastPairsPass() =>
        ContrastRatio(Foreground, PanelBackground) >= 4.5f && ContrastRatio(Foreground, ButtonBackground) >= 4.5f;

    private static StyleBoxFlat ButtonStyle(Color background, UiScalePreset preset, int border = 1)
    {
        var padding = Mathf.RoundToInt(8 * Factor(preset));
        return new StyleBoxFlat
        {
            BgColor = background,
            BorderColor = new Color("92b5d4"),
            BorderWidthLeft = border,
            BorderWidthTop = border,
            BorderWidthRight = border,
            BorderWidthBottom = border,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            ContentMarginLeft = padding,
            ContentMarginTop = padding,
            ContentMarginRight = padding,
            ContentMarginBottom = padding,
        };
    }

    private static float ContrastRatio(Color first, Color second)
    {
        static float Linear(float channel) => channel <= 0.03928f ? channel / 12.92f : Mathf.Pow((channel + 0.055f) / 1.055f, 2.4f);
        static float Luminance(Color color) => 0.2126f * Linear(color.R) + 0.7152f * Linear(color.G) + 0.0722f * Linear(color.B);
        var light = Mathf.Max(Luminance(first), Luminance(second));
        var dark = Mathf.Min(Luminance(first), Luminance(second));
        return (light + 0.05f) / (dark + 0.05f);
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
