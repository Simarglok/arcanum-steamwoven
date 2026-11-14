using Arcanum.Data.Lighting;
using Arcanum.Data.Terrain;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>
/// ARC-RES-1978 (experiment, FR-LIGHT-013 / FR-PRODUCT-007): a switchable "relit" look of the area draw. Off by default; while off nothing here
/// touches the draw (the Legacy shader programs and their uniforms are the ones the viewer always had). While on, the area item shaders are
/// replaced by variants that, per pixel, derive a normal from the sprite's own luminance (Sobel, alpha aware: the CPU twin is
/// <c>experiments/relight-lab</c> <c>NormalMapGenerator</c>, similar, not bit-exact) and add a normal-mapped diffuse term for the map's stationary lights and the carried
/// lights (the same sources the Legacy lattice uses), on top of the Legacy colour. It is a presentation-only variant: no geometry, clock,
/// RNG or gameplay luminance is read from or changed by it. The lights reach every item through global shader uniforms, so no per-item
/// uniform changes with the light list.
/// </summary>
internal static class AreaRelight
{
    /// <summary>Lights the shader reads (a 32 x 2 float texture). ARC-RES-2007 culling rule: of the carried, map and synthetic lights, the ones whose disk reaches the shown view are kept
    /// and the 32 whose centre is nearest the view's centre are uploaded (every light when no view is known); the rest are dropped.</summary>
    internal const int MaxLights = 32;

    /// <summary>How the relit term is combined with the Legacy colour (the three baked-light mitigations of the handoff).</summary>
    internal enum Mode
    {
        Off = 0,
        /// <summary>(a) the relit term added at the strength set, at any hour.</summary>
        Add = 1,
        /// <summary>(b) the same term weighted by how dark the item's ambient is: daylight stays Legacy, a dark scene gets the light.</summary>
        NightAdd = 2,
        /// <summary>(c) the baked shading is partly flattened (towards the local mean luminance), then the night-weighted term is added.</summary>
        DelightAdd = 3,
    }

    /// <summary>What an item is, for the normal strength (the shader's <c>relight_kind</c>): 0 not relit (masks, roofs, rings, highlight).</summary>
    internal const int KindNone = 0, KindGround = 1, KindObject = 2, KindCritter = 3;

    internal readonly record struct Light(float X, float Y, float Radius, Color Colour);

    private static bool _registered;
    private static ImageTexture? _lightTexture;
    private static Image? _lightImage;
    private static readonly List<Light> MapLights = [];
    private static readonly List<Light> CarriedLights = [];
    private static string _lastKey = "";
    private static Rect2? _view;

    private static Mode _developerMode = FromEnvironment();
    private static bool _look;

    /// <summary>The mode now. ARC-RENDER-2060: the developer override (the environment's <c>ARC_RELIGHT</c> = off, add, night, delight, or <see cref="SetMode"/> of the smokes
    /// and the palette) wins when it is not Off; else the player's look (<see cref="SetLook"/>, on exactly while HD graphics is on) is the night-weighted mode.</summary>
    internal static Mode Current => _developerMode != Mode.Off ? _developerMode : _look ? Mode.NightAdd : Mode.Off;
    internal static bool Active => Current != Mode.Off;
    /// <summary>The developer override alone (what a smoke saves and restores; <see cref="Current"/> also folds in the look).</summary>
    internal static Mode DeveloperMode => _developerMode;
    /// <summary>ARC-RENDER-2060: the player's relit look is on (a part of HD graphics, ARC-RENDER-2076; the caller decides).</summary>
    internal static bool Look => _look;
    /// <summary>ARC-RENDER-2060: the energy of the player's look: the night-weighted mode at 1.6 after the experiment ARC-RES-2007 (<c>ARC_RELIGHT_LOOK_STRENGTH</c> overrides it for tuning by eye).</summary>
    internal static float LookStrength { get; } = FloatFromEnvironment("ARC_RELIGHT_LOOK_STRENGTH", 1.6f);
    /// <summary>Overall energy of the relit term in a developer mode (0.6 is a mild one); the player's look uses <see cref="LookStrength"/>.</summary>
    internal static float Strength { get; set; } = FloatFromEnvironment("ARC_RELIGHT_STRENGTH", 0.6f);
    /// <summary>The energy the shader gets now.</summary>
    private static float EffectiveStrength => _developerMode != Mode.Off ? Strength : LookStrength;
    /// <summary>How much of the baked shading mode <see cref="Mode.DelightAdd"/> flattens (0..1).</summary>
    internal static float Delight { get; set; } = FloatFromEnvironment("ARC_RELIGHT_DELIGHT", 0.35f);
    /// <summary>Light height above the ground in screen pixels (Godot's <c>PointLight2D.height</c>).</summary>
    internal static float Height { get; set; } = FloatFromEnvironment("ARC_RELIGHT_HEIGHT", 48f);
    /// <summary>How strongly the luminance gradient bends the normal, per kind: ground is nearly flat, objects and critters bend more.</summary>
    internal static (float Ground, float Object, float Critter) NormalGain { get; set; } = (FloatFromEnvironment("ARC_RELIGHT_GROUND", 0.15f), FloatFromEnvironment("ARC_RELIGHT_OBJECT", 0.5f), FloatFromEnvironment("ARC_RELIGHT_CRITTER", 0.5f));
    /// <summary>Extra synthetic lights (the performance run's 5 or 20): placed by the caller, appended after the real ones.</summary>
    internal static List<Light> Extra { get; } = [];
    /// <summary>The lights of the last upload (for the smoke's report).</summary>
    internal static int UploadedCount { get; private set; }
    /// <summary>ARC-RES-2007: how many lights reached the view at the last upload before the 32 cap (the cap's overflow is this minus <see cref="UploadedCount"/>), and how many of those were carried.</summary>
    internal static int InViewCount { get; private set; }
    internal static int CarriedInViewCount { get; private set; }
    /// <summary>The shown view in world pixels (the area canvas sets it with each submission).</summary>
    internal static void SetView(Rect2 world) => _view = world;

    /// <summary>ARC-RENDER-2064: the light's disk (centre, radius) meets the shown view rectangle (true when no view is known): the cheap test before a wisp's life is asked.</summary>
    internal static bool ReachesView(Light l)
    {
        if (_view is not { } view) return true;
        var nx = Math.Clamp(l.X, view.Position.X, view.End.X); var ny = Math.Clamp(l.Y, view.Position.Y, view.End.Y);
        return (l.X - nx) * (l.X - nx) + (l.Y - ny) * (l.Y - ny) < l.Radius * l.Radius;
    }

    private static float FloatFromEnvironment(string name, float fallback) =>
        float.TryParse(OS.GetEnvironment(name), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : fallback;

    internal static Mode FromEnvironment() => (OS.GetEnvironment("ARC_RELIGHT") ?? "").ToLowerInvariant() switch
    {
        "add" or "1" => Mode.Add,
        "night" or "2" => Mode.NightAdd,
        "delight" or "3" => Mode.DelightAdd,
        _ => Mode.Off,
    };

    internal static void SetMode(Mode mode) => _developerMode = mode;

    /// <summary>ARC-RENDER-2060: turns the player's relit look on or off (the viewer calls it whenever the visual profile changes).</summary>
    internal static void SetLook(bool on) => _look = on;

    /// <summary>Steps off, add, night, delight, off ... (the diagnostic palette action).</summary>
    internal static Mode Cycle() { _developerMode = (Mode)(((int)_developerMode + 1) % 4); return _developerMode; }

    internal static void SetMapLights(IEnumerable<Light> lights) { MapLights.Clear(); MapLights.AddRange(lights); }
    internal static void SetCarriedLights(IEnumerable<Light> lights) { CarriedLights.Clear(); CarriedLights.AddRange(lights); }

    /// <summary>A placed light of a lighting field as a relit light: the rectangle's centre, half its width as the radius, the emitter's tint.</summary>
    internal static Light? FromPlacement(StationaryLight light, TerrainRect rect, AmbientPair pair)
    {
        // OFF emits nothing and DARK takes light away (the Legacy lattice handles it); neither adds a relit term.
        if ((light.Flags & (StationaryLightFlags.Off | StationaryLightFlags.Dark)) != 0) return null;
        var colour = StationaryLightField.Tint(light, pair);
        return new Light(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f, Math.Max(rect.Width, rect.Height) / 2f,
            new Color(colour.R / 255f, colour.G / 255f, colour.B / 255f));
    }

    /// <summary>Registers the global uniforms the relit shaders declare; a shader that uses them must not compile earlier.</summary>
    internal static void EnsureRegistered()
    {
        if (_registered) return;
        _lightImage = Image.CreateEmpty(MaxLights, 2, false, Image.Format.Rgbaf);
        _lightTexture = ImageTexture.CreateFromImage(_lightImage);
        RenderingServer.GlobalShaderParameterAdd("relight_lights", RenderingServer.GlobalShaderParameterType.Sampler2D, _lightTexture);
        RenderingServer.GlobalShaderParameterAdd("relight_params", RenderingServer.GlobalShaderParameterType.Vec4, new Vector4(0, 0, 0, 0));
        RenderingServer.GlobalShaderParameterAdd("relight_kinds", RenderingServer.GlobalShaderParameterType.Vec4, new Vector4(0, 0, 0, 0));
        _registered = true;
    }

    /// <summary>Writes the lights and the parameters the shaders read this frame (cheap: a 64-texel image and two vectors).</summary>
    internal static void Upload()
    {
        if (!Active) return;
        EnsureRegistered();
        IEnumerable<Light> candidates = CarriedLights.Concat(MapLights).Concat(Extra);
        var reach = candidates.ToArray();
        var centre = new Vector2(0, 0);
        if (_view is { } view)
        {
            centre = view.GetCenter();
            reach = reach.Where(ReachesView).ToArray();
        }
        InViewCount = reach.Length;
        CarriedInViewCount = reach.Count(l => CarriedLights.Contains(l));
        var all = (_view is null ? reach.AsEnumerable() : reach.OrderBy(l => (l.X - centre.X) * (l.X - centre.X) + (l.Y - centre.Y) * (l.Y - centre.Y))).Take(MaxLights).ToArray();
        var key = string.Join('|', all.Select(l => $"{l.X:0.#},{l.Y:0.#},{l.Radius:0.#},{l.Colour.R:0.##},{l.Colour.G:0.##},{l.Colour.B:0.##}"));
        if (key != _lastKey)
        {
            _lastKey = key;
            _lightImage!.Fill(new Color(0, 0, 0, 0));
            for (var i = 0; i < all.Length; i++)
            {
                _lightImage.SetPixel(i, 0, new Color(all[i].X, all[i].Y, Height, all[i].Radius));
                _lightImage.SetPixel(i, 1, new Color(all[i].Colour.R, all[i].Colour.G, all[i].Colour.B, 1f));
            }
            _lightTexture!.Update(_lightImage);
        }
        UploadedCount = all.Length;
        RenderingServer.GlobalShaderParameterSet("relight_params", new Vector4((float)Current, EffectiveStrength, Delight, Height));
        RenderingServer.GlobalShaderParameterSet("relight_kinds", new Vector4(NormalGain.Ground, NormalGain.Object, NormalGain.Critter, all.Length));
    }

    // The relit shader text is spliced into the Legacy program only for the relit variants; the Legacy programs are never edited.
    // h and a are 3x3 matrices indexed [x][y] of the neighbourhood: luminance (a transparent neighbour takes the centre's, so a silhouette
    // is not a cliff) and alpha.
    internal const string Declarations = """
        global uniform sampler2D relight_lights;
        global uniform vec4 relight_params;
        global uniform vec4 relight_kinds;
        uniform int relight_kind = 0;
        uniform int relight_scale = 1;
        float rl_lum(vec3 c) { return dot(c, vec3(0.299, 0.587, 0.114)); }
        void rl_taps(sampler2D tex, ivec2 p, vec4 centre, int stride, out mat3 h, out mat3 a) {
            ivec2 size = textureSize(tex, 0);
            float hc = rl_lum(centre.rgb);
            for (int x = 0; x < 3; x++) for (int y = 0; y < 3; y++) {
                ivec2 q = p + ivec2(x - 1, y - 1);
                if (x == 1 && y == 1) { h[x][y] = hc; a[x][y] = centre.a; }
                else if (any(lessThan(q, ivec2(0))) || any(greaterThanEqual(q, size))) { h[x][y] = hc; a[x][y] = 0.0; }
                else {
                    vec4 t = texelFetch(tex, q, 0);
                    h[x][y] = t.a < 0.5 ? hc : rl_lum(t.rgb);
                    a[x][y] = t.a;
                }
                // An N-times HD frame: the silhouette bevel looks N Legacy-pixel-widths less far than in the 1x look unless the alpha taps reach N texels.
                if (stride > 1 && !(x == 1 && y == 1)) {
                    ivec2 r = p + ivec2(x - 1, y - 1) * stride;
                    a[x][y] = (any(lessThan(r, ivec2(0))) || any(greaterThanEqual(r, size))) ? 0.0 : texelFetch(tex, r, 0).a;
                }
            }
        }
        vec3 rl_normal(mat3 h, mat3 a, float gain, float bevel) {
            float dx = (h[2][0] + 2.0 * h[2][1] + h[2][2]) - (h[0][0] + 2.0 * h[0][1] + h[0][2]);
            float dy = (h[0][2] + 2.0 * h[1][2] + h[2][2]) - (h[0][0] + 2.0 * h[1][0] + h[2][0]);
            // Outward bevel at a silhouette edge (a missing neighbour on the right pushes the normal to the right).
            float ex = ((a[0][0] + 2.0 * a[0][1] + a[0][2]) - (a[2][0] + 2.0 * a[2][1] + a[2][2])) * 0.25;
            float ey = ((a[0][0] + 2.0 * a[1][0] + a[2][0]) - (a[0][2] + 2.0 * a[1][2] + a[2][2])) * 0.25;
            return normalize(vec3(-dx * gain * 2.0 + ex * bevel, -dy * gain * 2.0 + ey * bevel, 1.0));
        }
        // The relit pixel: the Legacy colour (texel x the lattice tint) plus the normal-mapped diffuse term of the lights, per the mode.
        // ambient is the item's own Legacy ambient (a uniform of the program), the night weight's measure of darkness.
        vec4 relit_color(sampler2D tex, vec4 texel, ivec2 p, vec2 frag, float alpha) {
            vec3 tint = light_tint(frag);
            vec3 base = texel.rgb * tint;
            int mode = int(relight_params.x + 0.5);
            int count = int(relight_kinds.w + 0.5);
            if (mode == 0 || relight_kind == 0 || texel.a <= 0.0 || (count == 0 && mode != 3)) return vec4(base, texel.a * alpha);
            vec2 world = (frag - raster.xy) / raster.zw;
            float gain = (relight_kind == 1 ? relight_kinds.x : (relight_kind == 2 ? relight_kinds.y : relight_kinds.z)) * float(max(relight_scale, 1));
            float bevel = relight_kind == 1 ? 0.0 : 0.6;
            vec3 sum = vec3(0.0);
            mat3 hh; mat3 aa;
            bool have = false;
            vec3 n = vec3(0.0, 0.0, 1.0);
            // De-light flattens the baked shading everywhere (not only near a light), so the base has no step at a light's edge.
            if (mode == 3) { rl_taps(tex, p, texel, relight_scale, hh, aa); n = rl_normal(hh, aa, gain, bevel); have = true; }
            for (int i = 0; i < count; i++) {
                vec4 l0 = texelFetch(relight_lights, ivec2(i, 0), 0);
                vec3 c = texelFetch(relight_lights, ivec2(i, 1), 0).rgb;
                vec2 d = l0.xy - world;
                float att = max(1.0 - length(d) / max(l0.w, 1.0), 0.0);
                if (att <= 0.0) continue;
                if (!have) { rl_taps(tex, p, texel, relight_scale, hh, aa); n = rl_normal(hh, aa, gain, bevel); have = true; }
                vec3 l = normalize(vec3(d, l0.z));
                sum += c * att * att * max(dot(n, l), 0.0);
            }
            if (!have) return vec4(base, texel.a * alpha);
            float w = relight_params.y;
            // Night weighting: a bright ambient (day) leaves the Legacy pixel, a dark one takes the full term.
            if (mode >= 2) w *= clamp(1.0 - rl_lum(ambient), 0.0, 1.0);
            if (mode == 3) {
                // Partial de-light: pull the baked shading towards the local mean luminance (contrast reduced by the set share).
                float mean = 0.0;
                for (int x = 0; x < 3; x++) for (int y = 0; y < 3; y++) mean += hh[x][y];
                mean /= 9.0;
                float flat_scale = clamp(mean / max(hh[1][1], 0.02), 0.5, 2.0);
                base = texel.rgb * mix(1.0, flat_scale, relight_params.z) * tint;
            }
            return vec4(base + texel.rgb * sum * w, texel.a * alpha);
        }
        """;

    /// <summary>The Legacy blend body replaced by the relit one (Legacy: <c>texel * vec4(light_tint(FRAGCOORD.xy), alpha)</c>).</summary>
    internal const string BlendColor = "relit_color(TEXTURE, texel, ivec2(sx, sy), FRAGCOORD.xy, alpha)";
}
