namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-UI-3101: the round parts a window draws as <i>separate arts</i> over the kit's background (the sixteen spell college rings, the four skill group buttons and the eight technology discipline buttons of the character sheet's pages), the part of the issue
/// that the baked window surfaces (<c>disc_boxes</c>, <c>halo_boxes</c> of the surface tool) cannot reach. Each original art was drawn against a known panel: its rim carries that panel's colour in its edge pixels (the old shadow and
/// the dark anti-aliasing stair as opaque pixels), invisible over the old panel, a ragged dark band over a new background. (The lens rings 231, 232, 233 and 198 are not here: the surfaces already carry the ring, see the method study.) The part is a circle (the gold of its ring), so the cut is exact:
/// everything outside the circle of the ring's gold plus <see cref="Trim"/> is left out (anti-aliased at the device's pixel, whatever the texture's scale: the shader works in the art's own units), and the part gets the generated soft shadow down and to the right that
/// the surface rule gave the baked round parts (owner request 2026-11-25: 13 px offset, sigma 16 px, strength 0.7 of a master 3.6 px to the unit). Godot-free: the table, the constants and the two functions the shader mirrors.
/// </summary>
public static class AreaKitRoundParts
{
    /// <summary>One round part's art: its size in original units and the circle of its ring's gold: centre and outer radius, in units from the art's top-left corner (fitted to the gold pixels of all frames, the median).</summary>
    public readonly record struct Part(int Width, int Height, float Cx, float Cy, float Radius)
    {
        /// <summary>The radius of the cut: the gold's outer radius and the half unit of its dark outline.</summary>
        public float CutRadius => Radius + Trim;
    }

    /// <summary>The half unit of the old art's dark outline that the cut keeps outside the gold (the outline is the ring's own edge; the old shadow and the stair lie beyond it).</summary>
    public const float Trim = 0.4f;

    /// <summary>The shadow: 13, 13 px of the 3.6 px unit of the master (a master pixel of the surface tool), the blur sigma 16 px and the strength 0.7 (the numbers of <c>cast_disc_shadows</c>).</summary>
    public const float ShadowOffset = 13f / 3.6f, ShadowSigma = 16f / 3.6f, ShadowStrength = 0.7f;

    /// <summary>How far the shadow reaches beyond the disc towards the lower right, in units: the offset and three sigma (the node of a shadow is the disc's rectangle grown by this on every side).</summary>
    public static float ShadowReach => ShadowOffset + 3f * ShadowSigma;

    private static readonly Dictionary<int, Part> Parts = new()
    {
        // The sixteen college rings of the spells page (24 x 24).
        [11] = new(24, 24, 12.1f, 11.8f, 11.35f),
        [44] = new(24, 24, 12.1f, 11.85f, 11.45f),
        [38] = new(24, 24, 12.4f, 11.9f, 11.35f),
        [92] = new(24, 24, 11.85f, 11.75f, 11.3f),
        [50] = new(24, 24, 12.05f, 11.8f, 11.45f),
        [86] = new(24, 24, 11.75f, 11.85f, 11.4f),
        [56] = new(24, 24, 11.8f, 11.75f, 11.5f),
        [98] = new(24, 24, 11.5f, 11.8f, 11.4f),
        [62] = new(24, 24, 11.8f, 11.85f, 11.45f),
        [68] = new(24, 24, 12.45f, 11.85f, 11.35f),
        [74] = new(24, 24, 11.8f, 11.75f, 11.4f),
        [104] = new(24, 24, 12.0f, 11.85f, 11.45f),
        [110] = new(24, 24, 12.25f, 11.85f, 11.3f),
        [80] = new(24, 24, 11.75f, 11.75f, 11.5f),
        [116] = new(24, 24, 11.7f, 11.9f, 11.45f),
        [122] = new(24, 24, 11.85f, 11.8f, 11.45f),
        // The four skill group buttons (302 to 305, the hat is 304) and the eight technology disciplines (306 to 313).
        [302] = new(32, 32, 16.05f, 16.15f, 14.55f),
        [303] = new(33, 32, 16.45f, 16.0f, 14.5f),
        [304] = new(34, 32, 17.8f, 16.1f, 14.7f),
        [305] = new(32, 32, 15.65f, 16.2f, 14.6f),
        [306] = new(32, 31, 15.8f, 15.05f, 14.7f),
        [307] = new(32, 31, 15.4f, 14.95f, 14.8f),
        [308] = new(33, 31, 16.6f, 15.1f, 14.8f),
        [309] = new(32, 31, 15.8f, 14.95f, 14.9f),
        [310] = new(32, 31, 15.6f, 15.0f, 14.8f),
        [311] = new(32, 31, 16.05f, 14.9f, 14.8f),
        [312] = new(32, 31, 15.9f, 15.0f, 14.85f),
        [313] = new(32, 31, 15.55f, 14.9f, 14.8f),
        // ARC-UI-3347: the small brass-ringed buttons of the windows, boxes and strip panes (<c>ring_kit.RINGS</c>: the cut radius of the master less <see cref="Trim"/>): the note markers 145 to 148, the quantity pane's All 299, the cycle
        // buttons 757 and 758, the doll, bag and arrange buttons 338 to 340, the dialog box's accept and cancel 823 and 824. The character sheet's plus and minus (23, 24, 770, 771) are not here: they stand in the sheet's sockets.
        [145] = new(23, 23, 11.5f, 11.5f, 11.1f),
        [146] = new(23, 23, 11.5f, 11.5f, 11.1f),
        [147] = new(23, 23, 11.5f, 11.5f, 11.1f),
        [148] = new(23, 23, 11.5f, 11.5f, 11.1f),
        [299] = new(23, 23, 11.5f, 11.5f, 11.1f),
        [757] = new(23, 23, 11.5f, 11.5f, 11.1f),
        [758] = new(23, 23, 11.5f, 11.5f, 11.1f),
        [338] = new(28, 27, 13.4f, 12.1f, 12.8f),
        [339] = new(28, 27, 13.4f, 12.1f, 12.8f),
        [340] = new(28, 27, 13.4f, 12.1f, 12.8f),
        [823] = new(28, 29, 13.35f, 13.0f, 12.8f),
        [824] = new(28, 29, 13.35f, 13.0f, 12.8f),
    };

    /// <summary>The arts that have a round part, in order.</summary>
    public static IReadOnlyList<int> ArtNumbers { get; } = [.. Parts.Keys.Order()];

    /// <summary>The round part of <paramref name="art"/>, or null (not a round part: the art is drawn as it is).</summary>
    public static Part? Of(int art) => Parts.TryGetValue(art, out var part) ? part : null;

    /// <summary>
    /// The share of the point (<paramref name="x"/>, <paramref name="y"/>) of the art, in units from its top-left corner, that the cut keeps (0 outside the circle, 1 inside), the edge one device pixel wide centred on the circle
    /// (<paramref name="pixel"/> is the size of a device pixel in units). The shader <see cref="Shader"/> computes the same.
    /// </summary>
    public static float Coverage(Part part, float x, float y, float pixel)
    {
        var d = MathF.Sqrt((x - part.Cx) * (x - part.Cx) + (y - part.Cy) * (y - part.Cy)) - part.CutRadius;
        return Math.Clamp(.5f - d / Math.Max(pixel, 1e-4f), 0f, 1f);
    }

    /// <summary>
    /// The opacity of the shadow's black at the point (<paramref name="x"/>, <paramref name="y"/>) of the art's units: the disc moved by <see cref="ShadowOffset"/> down and right, blurred (the edge a logistic of the distance with the slope of a
    /// Gaussian of sigma <see cref="ShadowSigma"/>), times <see cref="ShadowStrength"/>, and nothing where the part itself is (a button covers it): the cut's complement.
    /// </summary>
    public static float ShadowAt(Part part, float x, float y, float pixel)
    {
        var moved = MathF.Sqrt((x - part.Cx - ShadowOffset) * (x - part.Cx - ShadowOffset) + (y - part.Cy - ShadowOffset) * (y - part.Cy - ShadowOffset)) - part.CutRadius;
        var edge = 1f / (1f + MathF.Exp(1.702f * moved / ShadowSigma));
        return ShadowStrength * edge * (1f - Coverage(part, x, y, pixel));
    }

    /// <summary>
    /// The canvas shader of a round part: <c>mode</c> 0 cuts the texture drawn over the node (the node is the art's rectangle, <c>unit_size</c> its size in units), <c>mode</c> 1 draws the shadow on a node of the art's rectangle grown by
    /// <see cref="ShadowReach"/> on every side (<c>origin</c> the art's corner inside it). The numbers are those of <see cref="Coverage"/> and <see cref="ShadowAt"/>. The blend is premultiplied alpha, as the character sheet's views are (<c>AreaArtTextDraw.Prepare</c>) and as the textures of their arts are made (<c>InterfaceHd.Texture(..., premultipliedAlpha: true)</c>): the cut scales the colour with the alpha, and the shadow's black is the colour 0 with its opacity. <c>straight</c> 1 (ARC-UI-3320: a kit face, a straight-alpha PNG) premultiplies the sampled colour first, so the face's own one-pixel edge blends as an art's does.
    /// </summary>
    public const string Shader = """
        shader_type canvas_item;
        render_mode blend_premul_alpha;
        uniform float mode = 0.0;
        uniform float straight = 0.0;
        uniform vec2 unit_size = vec2(1.0);
        uniform vec2 origin = vec2(0.0);
        uniform vec3 disc = vec3(0.0, 0.0, 1.0);
        uniform vec2 shadow = vec2(0.0);
        uniform vec2 shadow_shape = vec2(1.0, 0.0);
        void fragment() {
            vec2 p = UV * unit_size - origin;
            vec2 w = fwidth(p);
            float pixel = max(max(w.x, w.y), 0.0001);
            float d = length(p - disc.xy) - disc.z;
            float keep = clamp(0.5 - d / pixel, 0.0, 1.0);
            if (mode < 0.5) {
                if (straight > 0.5) COLOR.rgb *= COLOR.a;
                COLOR *= keep;
            } else {
                float moved = length(p - disc.xy - shadow) - disc.z;
                float edge = 1.0 / (1.0 + exp(1.702 * moved / shadow_shape.x));
                COLOR = vec4(0.0, 0.0, 0.0, shadow_shape.y * edge * (1.0 - keep));
            }
        }
        """;
}
