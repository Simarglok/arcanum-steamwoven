using Arcanum.Data.Terrain;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>Integer metadata sampling, independent of interpolated texture UVs.</summary>
public partial class AreaDrawItem : Node2D
{
    public const long MaxMappingBytes = 16 * 1024 * 1024;
    private const int MaxViewportAxis = 8192;
    private const string Code = """
        shader_type canvas_item;
        render_mode unshaded, blend_mix;
        uniform sampler2D index_rows : filter_nearest, repeat_disable;
        // ARC-RENDER-3416: screen_start is the item's first screen pixel counted from the view's whole origin pixel (view_state texel 1),
        // so a whole-pixel camera move leaves it as it is.
        uniform ivec2 screen_start;
        uniform ivec2 screen_size;
        // ARC-RENDER-3416: what moves with the camera and the carried lights, one texture for all items of a view (AreaViewState):
        // texel 0 the raster (canvas origin and zoom), texel 1 the whole pixels of that origin, texel 2 the carried lattice.
        uniform sampler2D view_state : filter_nearest, repeat_disable;
        uniform float body_alpha = 1.0;
        uniform vec3 ambient = vec3(1.0);
        // ARC-RENDER-011 wall fade: alpha at the left and right drawn edges, linear across the frame width.
        uniform vec2 fade = vec2(1.0);
        uniform int fade_width = 1;
        uniform bool fade_flip = false;
        // ARC-RENDER-014: the fade is a function of the Legacy column. An HD crop (N·c, N·m) maps
        // its sampled index sx back to floor((sx - N·c) / N) = the Legacy column (Legacy: N = 1).
        uniform int fade_scale = 1;
        uniform int fade_start = 0;
        // ARC-RENDER-012 local light: 0 constant ambient, 1 horizontal two-colour gradient (walls),
        // 2 terrain lattice. view_raster() = canvas origin and zoom, item_rect = world destination.
        uniform int light_mode = 0;
        uniform vec3 ambient_right = vec3(1.0);
        uniform vec4 item_rect = vec4(0.0, 0.0, 1.0, 1.0);
        // ARC-RENDER-018 wading: destination rows at or below item_rect's bottom minus wade_rows take wade_alpha,
        // which replaces body_alpha and the wall fade there; 0 rows = not wading.
        uniform int wade_rows = 0;
        uniform bool corner_fade = false;
        uniform vec4 corners = vec4(1.0);
        uniform float wade_alpha = 1.0;
        uniform sampler2D light_lighter : filter_nearest, repeat_disable;
        uniform sampler2D light_darker : filter_nearest, repeat_disable;
        uniform ivec4 lattice = ivec4(0);
        // ARC-DEV-335, ARC-DEV-353: the carried lights (the hero's, the critters') as a second, small lattice that follows them, so it is
        // separate from the map's lattice; a critter's light may be a dark one (flag 0x2), so it has darker sums as well.
        uniform sampler2D carry_lighter : filter_nearest, repeat_disable;
        uniform sampler2D carry_darker : filter_nearest, repeat_disable;
        uniform bool carried = false;
        vec4 view_raster() { return texelFetch(view_state, ivec2(0, 0), 0); }
        vec3 lattice_node(ivec2 node, ivec4 carry_lattice) {
            vec3 plus = vec3(0.0);
            vec3 minus = vec3(0.0);
            ivec2 p = node - lattice.xy;
            if (!(any(lessThan(p, ivec2(0))) || any(greaterThanEqual(p, lattice.zw)))) {
                plus = texelFetch(light_lighter, p, 0).rgb;
                minus = texelFetch(light_darker, p, 0).rgb;
            }
            ivec2 q = node - carry_lattice.xy;
            if (!(any(lessThan(q, ivec2(0))) || any(greaterThanEqual(q, carry_lattice.zw)))) {
                plus = min(plus + texelFetch(carry_lighter, q, 0).rgb, vec3(1.0));
                minus = min(minus + texelFetch(carry_darker, q, 0).rgb, vec3(1.0));
            }
            return max(min(ambient + plus, vec3(1.0)) - minus, vec3(0.0));
        }
        vec3 light_tint(vec2 frag) {
            if (light_mode == 0) return ambient;
            vec4 raster = view_raster();
            vec2 world = (frag - raster.xy) / raster.zw;
            if (light_mode == 1) return mix(ambient, ambient_right, clamp((world.x - item_rect.x) / max(item_rect.z - 1.0, 1.0), 0.0, 1.0));
            vec2 g = world / vec2(40.0, 20.0);
            ivec2 n = ivec2(floor(g));
            vec2 f = g - floor(g);
            ivec4 carry = carried ? ivec4(texelFetch(view_state, ivec2(2, 0), 0)) : ivec4(0);
            return mix(mix(lattice_node(n, carry), lattice_node(n + ivec2(1, 0), carry), f.x),
                mix(lattice_node(n + ivec2(0, 1), carry), lattice_node(n + ivec2(1, 1), carry), f.x), f.y);
        }
        void fragment() {
            ivec2 p = ivec2(FRAGCOORD.xy) - ivec2(texelFetch(view_state, ivec2(1, 0), 0).xy) - screen_start;
            if (any(lessThan(p, ivec2(0))) || any(greaterThanEqual(p, screen_size))) discard;
            ivec2 x = ivec2(round(texelFetch(index_rows, ivec2(p.x, 0), 0).rg * 255.0));
            ivec2 y = ivec2(round(texelFetch(index_rows, ivec2(p.y, 1), 0).rg * 255.0));
            int sx = x.x + x.y * 256 - 1;
            int sy = y.x + y.y * 256 - 1;
            if (sx < 0 || sy < 0) discard;
            int legacy_column = (sx - fade_scale * fade_start) / fade_scale;
            int column = fade_flip ? fade_width - 1 - legacy_column : legacy_column;
            float wall = fade.x + float(column) * (fade.y - fade.x) / float(fade_width);
            float alpha = body_alpha * wall;
            if (corner_fade) {
                vec2 uv = (vec2(float(sx), float(sy)) + 0.5) / vec2(textureSize(TEXTURE, 0));
                float u = fade_flip ? 1.0 - uv.x : uv.x;
                alpha *= mix(mix(corners.x, corners.y, u), mix(corners.w, corners.z, u), uv.y);
            }
            if (wade_rows > 0 && (FRAGCOORD.y - view_raster().y) / view_raster().w >= item_rect.y + item_rect.w - float(wade_rows)) alpha = wade_alpha;
            vec4 texel = texelFetch(TEXTURE, ivec2(sx, sy), 0);
            COLOR = BODY_COLOR;
        }
        """;
    private const string BlendColor = "texel * vec4(light_tint(FRAGCOORD.xy), alpha)";
    // ARC-RENDER-018 multiply (blit 0x40): the destination times the source colour. A transparent texel multiplies by
    // white (HD edges blend toward it), and the output alpha is 1 so the destination alpha is kept. A multiply body
    // is unlit and has no alpha mode (alpha is 1), so neither changes its pixels.
    private const string MultiplyColor = "vec4(mix(vec3(1.0), texel.rgb * light_tint(FRAGCOORD.xy), texel.a * alpha), 1.0)";
    // Three immutable shader programs for process lifetime, not per-object copies.
    private static readonly Lazy<Shader> NormalShader = new(() => new Shader { Code = Code.Replace("BODY_COLOR", BlendColor, StringComparison.Ordinal) });
    private static readonly Lazy<Shader> AddShader = new(() => new Shader { Code = Code.Replace("BODY_COLOR", BlendColor, StringComparison.Ordinal)
        .Replace("blend_mix", "blend_add", StringComparison.Ordinal) });
    private static readonly Lazy<Shader> MultiplyShader = new(() => new Shader { Code = Code.Replace("BODY_COLOR", MultiplyColor, StringComparison.Ordinal)
        .Replace("blend_mix", "blend_mul", StringComparison.Ordinal) });
    // ARC-RES-1978: the relit variants (normal-mapped light term, AreaRelight). Built only when the relit look is first asked for; the three
    // Legacy programs above are never edited. The multiply body is not relit (it has no light of its own).
    private static string RelitCode(string blend) => Code.Replace("void fragment()", AreaRelight.Declarations + "\n        void fragment()", StringComparison.Ordinal)
        .Replace("BODY_COLOR", AreaRelight.BlendColor, StringComparison.Ordinal).Replace("blend_mix", blend, StringComparison.Ordinal);
    private static readonly Lazy<Shader> RelitNormalShader = new(() => { AreaRelight.EnsureRegistered(); return new Shader { Code = RelitCode("blend_mix") }; });
    private static readonly Lazy<Shader> RelitAddShader = new(() => { AreaRelight.EnsureRegistered(); return new Shader { Code = RelitCode("blend_add") }; });
    // ARC-RENDER-3416: everything the engine holds for the drawn command; the canvas moves it with the command from node to node.
    private AreaItemState _s = new();
    private AreaMappingCache? _ownMappings;
    private AreaViewState? _ownView;
    private AreaActorGuide? _guide;
    // ARC-RENDER-3362: what the last draw put on the canvas.
    private (ImageTexture Frame, Rect2 Primitive, bool Mapped, ActorDiagnosticDrawCommand? Actor, bool Guide)? _drawn;

    // Everything PrepareMapping reads. An item whose inputs are those of its last full preparation sends nothing to the engine again; only its index row is asked
    // for, so the shared cache keeps it. A frame holds thousands of items, and a walking critter changes only a few of them. ARC-RENDER-3416: the same holds
    // when only the transform differs by a whole-pixel camera move; the carried lattice's place is in the view state, so only its textures are read here.
    internal readonly record struct Prepared(ImageTexture Frame, int HdScale, bool Actor, TerrainRect Source, TerrainRect Destination, bool FlipX, bool FlipY,
        bool Additive, bool Multiply, int Alpha, int WadeRows, Color Ambient, Color AmbientRight, Color? Weight, AreaLatticeBinding? Lattice,
        ImageTexture? CarryLighter, ImageTexture? CarryDarker, int RelightKind, Vector4? Corners, AreaWallFade Fade, bool Relit, Transform2D Transform, Vector2I Viewport);

    // The uniform names as engine names, made once: a string argument made and freed a StringName at every call. Made on first use, so the class loads without an engine.
    private static class Uniform
    {
        internal static readonly StringName BodyAlpha = "body_alpha", WadeRows = "wade_rows", WadeAlpha = "wade_alpha", Ambient = "ambient", Fade = "fade",
            FadeWidth = "fade_width", FadeFlip = "fade_flip", FadeScale = "fade_scale", FadeStart = "fade_start", CornerFade = "corner_fade", Corners = "corners",
            RelightKind = "relight_kind", RelightScale = "relight_scale", RelightWeight = "relight_weight", LightMode = "light_mode", AmbientRight = "ambient_right",
            ViewState = "view_state", ItemRect = "item_rect", LightLighter = "light_lighter", LightDarker = "light_darker", Lattice = "lattice",
            CarryLighter = "carry_lighter", CarryDarker = "carry_darker", Carried = "carried", ScreenStart = "screen_start", ScreenSize = "screen_size",
            IndexRows = "index_rows";
    }

    public ImageTexture? FrameTexture { get; set; }
    /// <summary>ARC-RENDER-014: N when <see cref="FrameTexture"/> is an HD frame (exactly N·w × N·h), 1 for Legacy.
    /// The item samples the crop (N·c, N·m) with the Legacy destination, reflection and every other input unchanged.</summary>
    public int HdScale { get; set; } = 1;
    public AreaDrawCommand? Command { get; set; }
    public ActorDiagnosticDrawCommand? Actor { get; set; }
    /// <summary>ARC-DEV-064: draws the actor's frame and anchor cross (the canvas's debug-mode guides); off in play.</summary>
    public bool ShowGuide { get; set; }
    /// <summary>ARC-RENDER-010 ambient multiplier of this item's channel; white leaves source pixels unchanged.</summary>
    public Color Ambient { get; set; } = Colors.White;
    /// <summary>ARC-RENDER-011 wall check fade of this item; None draws it opaque.</summary>
    public AreaWallFade WallFade { get; set; }
    /// <summary>ARC-RENDER-022: a faded roof's four corner alphas (top left, top right, bottom right, bottom left, 0–1),
    /// interpolated over the frame in drawn orientation; null draws it without (ledger RF2).</summary>
    public Vector4? RoofCorners { get; set; }
    /// <summary>ARC-RENDER-012: right-end colour of a wall gradient; equal to <see cref="Ambient"/> otherwise.</summary>
    public Color AmbientRight { get; set; } = Colors.White;
    /// <summary>ARC-RENDER-012: the terrain light lattice, or null.</summary>
    internal AreaLatticeBinding? Lattice { get; set; }
    /// <summary>ARC-DEV-335: the carried lights' lattice, or null.</summary>
    internal AreaCarriedBinding? Carried { get; set; }
    /// <summary>ARC-RES-1978: what the relit look treats this item as (<see cref="AreaRelight.KindNone"/> .. KindCritter); read only while it is on.</summary>
    internal int RelightKind { get; set; }
    /// <summary>ARC-RENDER-2298: the ambient the relit night weight reads, the item's colour without the carried lights (null: <see cref="Ambient"/>); read only while the relit look is on.</summary>
    internal Color? WeightAmbient { get; set; }
    /// <summary>Shared index rows owned by the canvas. A standalone item (tests, probes)
    /// keeps a private cache holding only its current row.</summary>
    internal AreaMappingCache? Mappings { get; set; }
    /// <summary>ARC-RENDER-3416: the view state owned by the canvas, written once before its items are submitted. A standalone item keeps and writes its own.</summary>
    internal AreaViewState? View { get; set; }
    /// <summary>ARC-RENDER-3416: what the engine holds for this node's command.</summary>
    internal AreaItemState State => _s;
    /// <summary>ARC-RENDER-3416: takes the state another node held (the canvas permutes them, so each is held by one node).</summary>
    internal void Use(AreaItemState state)
    {
        if (ReferenceEquals(_s, state)) return;
        _s = state;
        if (!ReferenceEquals(Material, state.Material)) Material = state.Material;
    }

    internal static Transform2D RasterTransform(Viewport viewport)
    {
        var transform = viewport.GetCanvasTransform();
        if (viewport.Snap2DTransformsToPixel)
            transform.Origin = new(MathF.Ceiling(transform.Origin.X - .5f), MathF.Ceiling(transform.Origin.Y - .5f));
        if (transform.X.Y != 0 || transform.Y.X != 0 || transform.X.X <= 0 || transform.Y.Y <= 0
            || !float.IsFinite(transform.X.X) || !float.IsFinite(transform.Y.Y)
            || !float.IsFinite(transform.Origin.X) || !float.IsFinite(transform.Origin.Y))
            throw new InvalidOperationException("AREA_NEAREST_TRANSFORM_DOMAIN");
        return transform;
    }

    // The item's whole screen envelope, NOT clamped to the viewport: clamping would
    // make the index rows depend on the camera position. Fragments outside the
    // viewport are never rasterized, and the shader still excludes outside pixels.
    // Only an axis longer than AreaMappingCache.MaxRowLength (a very large sprite at
    // high zoom) is clamped to the viewport as before: still exact, just not reusable
    // across pans.
    private static Rect2I ScreenEnvelope(Transform2D t, TerrainRect d, Vector2I viewport)
    {
        var (left, right) = Axis((double)t.Origin.X, t.X.X, d.X, d.Width, viewport.X);
        var (top, bottom) = Axis((double)t.Origin.Y, t.Y.Y, d.Y, d.Height, viewport.Y);
        return new(left, top, checked(right - left), checked(bottom - top));
    }
    private static (int Start, int End) Axis(double origin, double zoom, int destination, int length, int viewport)
    {
        // Double envelope computation preserves the exact supplied float inputs.
        // One pixel of conservative primitive padding is NOT source ownership.
        var start = checked((int)Math.Floor(origin + zoom * destination) - 1);
        var end = checked((int)Math.Ceiling(origin + zoom * ((long)destination + length)) + 1);
        if ((long)end - start <= AreaMappingCache.MaxRowLength) return (start, end);
        var clamped = Math.Clamp(start, 0, viewport);
        return (clamped, Math.Clamp(end, clamped, viewport));
    }
    /// <summary>ARC-RENDER-3362: the canvas's submission of this item in a view with the raster <paramref name="transform"/> and the visible size
    /// <paramref name="size"/>: prepares it, shows it, and asks for a redraw only when its inputs or what it draws changed.</summary>
    internal void Submit(AreaDrawCommand command, Transform2D transform, Vector2I size)
    {
        var changed = PrepareMapping(command, transform, size);
        Show();
        if (changed || FrameTexture is null || _drawn != Drawn()) QueueRedraw();
    }

    private (ImageTexture, Rect2, bool, ActorDiagnosticDrawCommand?, bool) Drawn() => (FrameTexture!, _s.Primitive, _s.Mapping is not null, Actor, ShowGuide);

    /// <summary>Sends the item's raster mapping and uniforms for the view; false when nothing it reads changed since the last full preparation.</summary>
    internal bool PrepareMapping(AreaDrawCommand command, Transform2D transform, Vector2I size)
    {
        var relit = AreaRelight.Active && !command.Multiply;
        var view = View;
        if (view is null) { view = _ownView ??= new AreaViewState(); view.Write(transform, size, Carried?.Lattice ?? Vector4I.Zero); }
        var prepared = new Prepared(FrameTexture!, HdScale, Actor is not null, command.Source, command.Destination, command.FlipX, command.FlipY,
            command.Additive, command.Multiply, command.Alpha, command.WadeRows, Ambient, AmbientRight, WeightAmbient, Lattice, Carried?.Lighter, Carried?.Darker,
            RelightKind, RoofCorners, WallFade, relit, transform, size);
        var s = _s;
        if (s.Prepared is { } last && s.Key is { } held && ReferenceEquals(Material, s.Material)
            && (last == prepared || (last with { Transform = transform }) == prepared && Panned(s, last.Transform, transform, command.Destination, size)))
        {
            s.Prepared = prepared;
            // Still in use: keep the shared row from being evicted by this submission.
            if (Mappings is { } kept) Bind(kept.Acquire(held));
            return false;
        }
        // AREA chart items are in world coordinates; do not silently use a
        // second transform/projection for testing or movement.
        if (GlobalTransform != Transform2D.Identity) throw new InvalidOperationException("AREA_NEAREST_ITEM_TRANSFORM_DOMAIN");
        if (HdScale < 1 || FrameTexture!.GetWidth() % HdScale != 0 || FrameTexture.GetHeight() % HdScale != 0)
            throw new InvalidOperationException("AREA_HD_SCALE_DOMAIN");
        // The Legacy crop: the plan's for bodies and terrain, the whole Legacy frame for the actor.
        var legacy = Actor is null ? command.Source : new TerrainRect(0, 0, FrameTexture.GetWidth() / HdScale, FrameTexture.GetHeight() / HdScale);
        var source = HdScale == 1 ? legacy : HdCrop.Scale(legacy, HdScale);
        if (source.X < 0 || source.Y < 0 || (long)source.X + source.Width > FrameTexture!.GetWidth()
            || (long)source.Y + source.Height > FrameTexture.GetHeight())
            throw new InvalidOperationException("AREA_NEAREST_SOURCE_CROP_DOMAIN");
        var state = (transform, source, command.Destination, command.FlipX, command.FlipY, size);
        var material = s.Material ??= new ShaderMaterial();
        if (s.MaterialState != (command.Additive, command.Multiply, command.Alpha, command.WadeRows) || !ReferenceEquals(Material, material) || s.RelitState?.Relit != relit)
        {
            material.Shader = command.Multiply ? MultiplyShader.Value
                : relit ? (command.Additive ? RelitAddShader.Value : RelitNormalShader.Value)
                : command.Additive ? AddShader.Value : NormalShader.Value;
            s.RelitState = (relit, -1, -1);
            Material = material;
            material.SetShaderParameter(Uniform.ViewState, view.Texture);
            material.SetShaderParameter(Uniform.BodyAlpha, command.Alpha / 255f);
            material.SetShaderParameter(Uniform.WadeRows, command.WadeRows);
            material.SetShaderParameter(Uniform.WadeAlpha, AreaScenePlan.WadingAlpha / 255f);
            s.MaterialState = (command.Additive, command.Multiply, command.Alpha, command.WadeRows);
            s.AmbientState = null; s.WeightState = null; s.LightState = null; s.FadeState = null; s.CornerState = null; // re-send the per-item uniforms with every shader swap
        }
        if (s.AmbientState != Ambient)
        {
            material.SetShaderParameter(Uniform.Ambient, new Vector3(Ambient.R, Ambient.G, Ambient.B));
            s.AmbientState = Ambient;
        }
        var fadeState = (WallFade, legacy.Width, command.FlipX, HdScale, legacy.X);
        if (s.FadeState != fadeState)
        {
            var (left, right) = FadeAlphas(WallFade);
            material.SetShaderParameter(Uniform.Fade, new Vector2(left / 255f, right / 255f));
            material.SetShaderParameter(Uniform.FadeWidth, legacy.Width);
            material.SetShaderParameter(Uniform.FadeFlip, command.FlipX);
            material.SetShaderParameter(Uniform.FadeScale, HdScale);
            material.SetShaderParameter(Uniform.FadeStart, legacy.X);
            s.FadeState = fadeState;
        }
        var corners = (RoofCorners is not null, RoofCorners ?? Vector4.One);
        if (s.CornerState != corners)
        {
            material.SetShaderParameter(Uniform.CornerFade, corners.Item1);
            material.SetShaderParameter(Uniform.Corners, corners.Item2);
            s.CornerState = corners;
        }
        var mode = Lattice is not null || Carried is not null ? 2 : AmbientRight != Ambient ? 1 : 0;
        if (relit && (s.RelitState?.Kind != RelightKind || s.RelitState?.Scale != HdScale))
        {
            material.SetShaderParameter(Uniform.RelightKind, RelightKind);
            // ARC-RENDER-2060: an N-times HD frame has N times the texels over the same drawn size, so its luminance steps are 1/N as big: the gradient gain scales with N.
            material.SetShaderParameter(Uniform.RelightScale, HdScale);
            s.RelitState = (true, RelightKind, HdScale);
        }
        if (relit && s.WeightState != (WeightAmbient ?? Ambient))
        {
            var weight = WeightAmbient ?? Ambient;
            material.SetShaderParameter(Uniform.RelightWeight, new Vector3(weight.R, weight.G, weight.B));
            s.WeightState = weight;
        }
        // The raster is in the view state, so no item sends it for a camera move; the destination is read by the wall gradient and the water line only.
        var light = (mode, AmbientRight, Lattice, Carried?.Lighter, Carried?.Darker, mode == 1 || command.WadeRows > 0 ? command.Destination : default);
        if (s.LightState != light)
        {
            material.SetShaderParameter(Uniform.LightMode, mode);
            material.SetShaderParameter(Uniform.AmbientRight, new Vector3(AmbientRight.R, AmbientRight.G, AmbientRight.B));
            var d = command.Destination;
            material.SetShaderParameter(Uniform.ItemRect, new Vector4(d.X, d.Y, d.Width, d.Height));
            if (s.LightState is null || !ReferenceEquals(s.LightState.Value.Lattice, Lattice))
            {
                material.SetShaderParameter(Uniform.LightLighter, Lattice?.Lighter ?? default(Variant));
                material.SetShaderParameter(Uniform.LightDarker, Lattice?.Darker ?? default(Variant));
                material.SetShaderParameter(Uniform.Lattice, Lattice?.Lattice ?? Vector4I.Zero);
            }
            if (s.LightState is null || !ReferenceEquals(s.LightState.Value.CarryLighter, Carried?.Lighter) || !ReferenceEquals(s.LightState.Value.CarryDarker, Carried?.Darker))
            {
                material.SetShaderParameter(Uniform.CarryLighter, Carried?.Lighter ?? default(Variant));
                material.SetShaderParameter(Uniform.CarryDarker, Carried?.Darker ?? default(Variant));
                material.SetShaderParameter(Uniform.Carried, Carried is not null);
            }
            s.LightState = light;
        }
        if (s.Mapped == state)
        {
            // Still in use: keep the shared row from being evicted by this submission.
            if (Mappings is { } current && s.Key is { } same) Bind(current.Acquire(same));
            s.Prepared = prepared;
            return true;
        }
        if (size.X > MaxViewportAxis || size.Y > MaxViewportAxis)
            throw new InvalidOperationException("AREA_NEAREST_VIEWPORT_DOMAIN");
        var envelope = ScreenEnvelope(transform, command.Destination, size);
        if (!envelope.Intersects(new Rect2I(Vector2I.Zero, size))) { ReleaseMapping(); return true; }
        // A pure integer camera translation that moves the envelope by the same amount
        // keeps the exact row key (AreaNearestAxis.RowKey), so the key is reused.
        AreaMappingKey key;
        if (s.Key is { } previousKey && s.Keyed is { } k && k.Source == source && k.Destination == command.Destination
            && k.X == command.FlipX && k.Y == command.FlipY && k.Transform.X == transform.X && k.Transform.Y == transform.Y
            && IntegerShift(k.Transform.Origin, transform.Origin) is { } shift && envelope == new Rect2I(k.Envelope.Position + shift, k.Envelope.Size))
            key = previousKey;
        else
        {
            var d = command.Destination;
            var x = new AreaNearestAxis(transform.Origin.X, transform.X.X, d.X, d.Width, source.X, source.Width, command.FlipX);
            var y = new AreaNearestAxis(transform.Origin.Y, transform.Y.Y, d.Y, d.Height, source.Y, source.Height, command.FlipY);
            key = new AreaMappingKey(x.RowKey(envelope.Position.X), y.RowKey(envelope.Position.Y), envelope.Size.X, envelope.Size.Y);
        }
        s.Keyed = (transform, envelope, source, command.Destination, command.FlipX, command.FlipY);
        ImageTexture mapping;
        if (Mappings is { } shared) mapping = shared.Acquire(key);
        else
        {
            _ownMappings ??= new AreaMappingCache();
            _ownMappings.BeginSubmission(); mapping = _ownMappings.Acquire(key); _ownMappings.DropUnused();
        }
        Bind(mapping); s.Key = key;
        var screen = (Start: envelope.Position - AreaViewState.WholeOrigin(transform), envelope.Size);
        if (s.Screen != screen)
        {
            material.SetShaderParameter(Uniform.ScreenStart, screen.Start);
            material.SetShaderParameter(Uniform.ScreenSize, screen.Size);
            s.Screen = screen;
        }
        var inverse = transform.AffineInverse();
        var a = inverse * (Vector2)envelope.Position;
        var b = inverse * (Vector2)envelope.End;
        s.Primitive = new(a, b - a); s.Mapped = state; s.Prepared = prepared;
        return true;
    }

    // ARC-RENDER-3416: a whole-pixel camera move at the same zoom that moves the item's envelope by the same amount, still on screen: the row key
    // (AreaNearestAxis.RowKey), the start counted from the view's whole origin pixel and the world rectangle all stay, so the item sends nothing.
    private static bool Panned(AreaItemState s, Transform2D from, Transform2D to, TerrainRect destination, Vector2I viewport)
    {
        if (s.Keyed is not { } k || s.Mapped is not { } mapped || k.Transform != from || from.X != to.X || from.Y != to.Y
            || IntegerShift(from.Origin, to.Origin) is not { } shift) return false;
        var envelope = ScreenEnvelope(to, destination, viewport);
        if (envelope != new Rect2I(k.Envelope.Position + shift, k.Envelope.Size) || !envelope.Intersects(new Rect2I(Vector2I.Zero, viewport))) return false;
        s.Keyed = k with { Transform = to, Envelope = envelope };
        s.Mapped = mapped with { Transform = to };
        return true;
    }

    /// <summary>Alpha at the left and right drawn edges: the source's 64 for a whole wall, 128 on the faded side of a
    /// corner (<c>OWAF_TRANS_LEFT</c> fades the left side).</summary>
    internal static (int Left, int Right) FadeAlphas(AreaWallFade fade) => fade switch
    {
        AreaWallFade.None => (255, 255),
        AreaWallFade.Both => (AreaWallCheck.FadedAlpha, AreaWallCheck.FadedAlpha),
        AreaWallFade.Left => (AreaWallCheck.EdgeAlpha, 255),
        AreaWallFade.Right => (255, AreaWallCheck.EdgeAlpha),
        _ => throw new ArgumentOutOfRangeException(nameof(fade)),
    };

    // Both origins integral (always the case with pixel snapping) and below 2^24, so
    // they and their difference are exact integers; otherwise the key is recomputed.
    private static Vector2I? IntegerShift(Vector2 from, Vector2 to)
    {
        static bool Integral(float value) => MathF.Floor(value) == value && MathF.Abs(value) < 1 << 24;
        if (!Integral(from.X) || !Integral(from.Y) || !Integral(to.X) || !Integral(to.Y)) return null;
        return new((int)to.X - (int)from.X, (int)to.Y - (int)from.Y);
    }

    private void Bind(ImageTexture mapping)
    {
        if (!ReferenceEquals(mapping, _s.Mapping)) _s.Material!.SetShaderParameter(Uniform.IndexRows, mapping);
        _s.Mapping = mapping;
    }

    /// <summary>Stops sampling the index rows. Shared rows stay owned by the cache;
    /// a standalone item's private row is disposed.</summary>
    public void ReleaseMapping()
    {
        // An item that never had a material has no row to let go (and the names need an engine).
        if (_s.Material is not null) _s.Material.SetShaderParameter(Uniform.IndexRows, default(Variant));
        _s.Mapping = null; _s.Mapped = null; _s.Key = null; _s.Keyed = null; _s.Prepared = null; _drawn = null;
        _ownMappings?.Clear();
    }

    public override void _Draw()
    {
        _drawn = null;
        if (Command is not { } command || FrameTexture is null) return;
        // ARC-RENDER-3416: a canvas item is drawn for the view the canvas submitted, which its view state shows.
        if (View is { } view) PrepareMapping(command, view.Transform, view.Size);
        else
        {
            var viewport = GetViewport();
            PrepareMapping(command, RasterTransform(viewport), (Vector2I)viewport.GetVisibleRect().Size);
        }
        _drawn = Drawn();
        if (_s.Mapping is not null) DrawTextureRect(FrameTexture, _s.Primitive, false);
        if (Actor is not null && ShowGuide && _guide is null) { _guide = new AreaActorGuide(); AddChild(_guide); }
        if (_guide is not null) { _guide.Actor = ShowGuide ? Actor : null; _guide.QueueRedraw(); }
    }

    public override void _ExitTree()
    {
        ReleaseMapping(); Material = null; _s.Material?.Dispose(); _s = new();
        _ownMappings?.Dispose(); _ownMappings = null;
        _ownView?.Dispose(); _ownView = null;
    }
}

/// <summary>ARC-RENDER-012 terrain light lattice uploaded once per hour view: lighter/darker node sums and
/// (first column, first row, columns, rows); node (c, r) is at chart screen (40c, 20r).</summary>
internal sealed record AreaLatticeBinding(ImageTexture Lighter, ImageTexture Darker, Vector4I Lattice);

/// <summary>ARC-DEV-335, ARC-DEV-353: the carried lights' lattice (lighter and darker sums, in the same node frame as <see cref="AreaLatticeBinding"/>).</summary>
internal sealed record AreaCarriedBinding(ImageTexture Lighter, ImageTexture Darker, Vector4I Lattice);

/// <summary>ARC-RENDER-3416: what the engine holds for one drawn command: its material with every input sent, its index row and its world rectangle.
/// The canvas hands it to whichever node draws that command in the next submission, so a command that moved in the draw order sends nothing again.</summary>
internal sealed class AreaItemState
{
    internal ShaderMaterial? Material;
    internal (bool Additive, bool Multiply, int Alpha, int WadeRows)? MaterialState;
    internal Color? AmbientState;
    internal Color? WeightState;
    internal (AreaWallFade Fade, int Width, bool Flip, int Scale, int Start)? FadeState;
    internal (bool On, Vector4 Corners)? CornerState;
    internal (int Mode, Color Right, AreaLatticeBinding? Lattice, ImageTexture? CarryLighter, ImageTexture? CarryDarker, TerrainRect Rect)? LightState;
    internal (bool Relit, int Kind, int Scale)? RelitState;
    internal ImageTexture? Mapping;
    internal AreaMappingKey? Key;
    internal (Transform2D Transform, Rect2I Envelope, TerrainRect Source, TerrainRect Destination, bool X, bool Y)? Keyed;
    internal (Transform2D Transform, TerrainRect Source, TerrainRect Destination, bool X, bool Y, Vector2I Size)? Mapped;
    internal (Vector2I Start, Vector2I Size)? Screen;
    internal Rect2 Primitive;
    internal AreaDrawItem.Prepared? Prepared;
}

/// <summary>ARC-RENDER-3416: what every item of one view reads that changes with the camera or a carried light, as three texels of a float texture:
/// the raster (canvas origin and zoom), the whole pixels of that origin and the carried lattice (first column, first row, columns, rows).
/// A camera move or a moved carried light writes these once, instead of an input of every item.</summary>
internal sealed class AreaViewState : IDisposable
{
    private const int Floats = 12;
    private readonly float[] _values = new float[Floats];
    private readonly byte[] _bytes = new byte[Floats * sizeof(float)];
    private ImageTexture? _texture;

    /// <summary>The raster transform and visible size last written.</summary>
    internal Transform2D Transform { get; private set; } = Transform2D.Identity;
    internal Vector2I Size { get; private set; }
    internal ImageTexture Texture => _texture ?? throw new InvalidOperationException("AREA_VIEW_STATE_UNWRITTEN");

    /// <summary>The whole screen pixel at or before the raster's origin, which the items' screen starts are counted from.</summary>
    internal static Vector2I WholeOrigin(Transform2D transform) => new(checked((int)MathF.Floor(transform.Origin.X)), checked((int)MathF.Floor(transform.Origin.Y)));

    internal void Write(Transform2D transform, Vector2I size, Vector4I carry)
    {
        Transform = transform; Size = size;
        var whole = WholeOrigin(transform);
        ReadOnlySpan<float> next = [transform.Origin.X, transform.Origin.Y, transform.X.X, transform.Y.Y, whole.X, whole.Y, 0, 0, carry.X, carry.Y, carry.Z, carry.W];
        if (_texture is not null && next.SequenceEqual(_values)) return;
        next.CopyTo(_values);
        Buffer.BlockCopy(_values, 0, _bytes, 0, _bytes.Length);
        using var image = Image.CreateFromData(Floats / 4, 1, false, Image.Format.Rgbaf, _bytes);
        if (_texture is null) _texture = ImageTexture.CreateFromImage(image);
        else _texture.Update(image);
    }

    public void Dispose() { _texture?.Dispose(); _texture = null; }
}

// Guides are a diagnostic overlay, not pixels sampled by the source shader.
internal partial class AreaActorGuide : Node2D
{
    public ActorDiagnosticDrawCommand? Actor { get; set; }
    public override void _Draw()
    {
        if (Actor is not { } actor) return;
        var d = actor.Destination;
        DrawRect(new(d.X, d.Y, d.Width, d.Height), Colors.Magenta, false);
        DrawLine(new((float)actor.AnchorX - 4, (float)actor.AnchorY), new((float)actor.AnchorX + 4, (float)actor.AnchorY), Colors.Yellow);
        DrawLine(new((float)actor.AnchorX, (float)actor.AnchorY - 4), new((float)actor.AnchorX, (float)actor.AnchorY + 4), Colors.Yellow);
    }
}
