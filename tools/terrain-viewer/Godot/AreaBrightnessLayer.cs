using Arcanum.Presentation.Display;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-UI-1737 (ledger OH1): the "brightness" setting as the final colour multiply. The original scales its display's gamma ramp by
/// g = 1.0 + 0.1 × value, which brightens whatever the screen shows (the interface, the fades and the movies too); this layer is above every
/// other one and multiplies the finished picture by <see cref="BrightnessRules.Factor"/> the same way (<see cref="BrightnessRules.Apply"/>'s
/// arithmetic: the 8-bit channel × g, cut to a whole number, capped at white). Value 0 is the factor 1.0, which the original leaves alone: the
/// layer is hidden and the picture is today's byte for byte. A fade is a veil under this layer, so it ends at the player's brightness.
/// </summary>
public partial class AreaBrightnessLayer : CanvasLayer
{
    /// <summary>Above every viewer layer (the controller prompts are 90, the bug report and the HD pack box 20 and 22).</summary>
    public const int TopLayer = 127;

    private const string Shader = """
        shader_type canvas_item;
        render_mode unshaded, blend_disabled;
        uniform sampler2D screen_texture : hint_screen_texture, filter_nearest, repeat_disable;
        uniform float gain = 1.0;
        void fragment() {
            vec3 channel = floor(texture(screen_texture, SCREEN_UV).rgb * 255.0 + 0.5);
            // BrightnessRules.Apply: the channel times g, cut to a whole number (the guard keeps a product like 10 x 1.3 whole), capped at white.
            COLOR = vec4(min(floor(channel * gain + 0.001), vec3(255.0)) / 255.0, 1.0);
        }
        """;

    private readonly ColorRect _screen;
    private readonly ShaderMaterial _material;

    public AreaBrightnessLayer()
    {
        Name = "BrightnessLayer"; Layer = TopLayer; Visible = false;
        _material = new ShaderMaterial { Shader = new Shader { Code = Shader } };
        _screen = new ColorRect { Name = "BrightnessScreen", MouseFilter = Control.MouseFilterEnum.Ignore, Material = _material };
        _screen.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_screen);
    }

    /// <summary>The value the layer shows (0: hidden).</summary>
    public int Value { get; private set; }

    /// <summary>The multiplier the shader reads now (1 while hidden).</summary>
    public float Gain => Value == 0 ? 1f : (float)BrightnessRules.Factor(Value);

    /// <summary>Applies a setting of 0 to <see cref="BrightnessRules.Max"/>; 0 hides the layer.</summary>
    public void Apply(int value)
    {
        Value = Math.Clamp(value, 0, BrightnessRules.Max);
        _material.SetShaderParameter("gain", Gain);
        Visible = BrightnessRules.Active(Value);
    }
}
