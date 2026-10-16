using Arcanum.Data.Terrain;
using Arcanum.TerrainViewer.Core;
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
        uniform ivec2 screen_start;
        uniform ivec2 screen_size;
        uniform float body_alpha = 1.0;
        uniform vec3 ambient = vec3(1.0);
        void fragment() {
            ivec2 p = ivec2(FRAGCOORD.xy) - screen_start;
            if (any(lessThan(p, ivec2(0))) || any(greaterThanEqual(p, screen_size))) discard;
            ivec2 x = ivec2(round(texelFetch(index_rows, ivec2(p.x, 0), 0).rg * 255.0));
            ivec2 y = ivec2(round(texelFetch(index_rows, ivec2(p.y, 1), 0).rg * 255.0));
            int sx = x.x + x.y * 256 - 1;
            int sy = y.x + y.y * 256 - 1;
            if (sx < 0 || sy < 0) discard;
            COLOR = texelFetch(TEXTURE, ivec2(sx, sy), 0) * vec4(ambient, body_alpha);
        }
        """;
    // Two immutable shader programs for process lifetime, not per-object copies.
    private static readonly Lazy<Shader> NormalShader = new(() => new Shader { Code = Code });
    private static readonly Lazy<Shader> AddShader = new(() => new Shader { Code = Code.Replace("blend_mix", "blend_add", StringComparison.Ordinal) });
    private ShaderMaterial? _material;
    private (bool Additive, int Alpha)? _materialState;
    private Color? _ambientState;
    private ImageTexture? _mapping;
    private AreaMappingKey? _key;
    private (Transform2D Transform, Rect2I Envelope, TerrainRect Source, TerrainRect Destination, bool X, bool Y)? _keyed;
    private AreaMappingCache? _ownMappings;
    private (Transform2D Transform, TerrainRect Source, TerrainRect Destination, bool X, bool Y, Vector2I Size)? _state;
    private Rect2 _primitive;
    private AreaActorGuide? _guide;
    public ImageTexture? FrameTexture { get; set; }
    public AreaDrawCommand? Command { get; set; }
    public ActorDiagnosticDrawCommand? Actor { get; set; }
    /// <summary>ARC-RENDER-010 ambient multiplier of this item's channel; white leaves source pixels unchanged.</summary>
    public Color Ambient { get; set; } = Colors.White;
    /// <summary>Shared index rows owned by the canvas. A standalone item (tests, probes)
    /// keeps a private cache holding only its current row.</summary>
    internal AreaMappingCache? Mappings { get; set; }

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
    internal void PrepareMapping(AreaDrawCommand command)
    {
        var viewport = GetViewport(); var transform = RasterTransform(viewport);
        // AREA chart items are in world coordinates; do not silently use a
        // second transform/projection for testing or movement.
        if (GlobalTransform != Transform2D.Identity) throw new InvalidOperationException("AREA_NEAREST_ITEM_TRANSFORM_DOMAIN");
        var source = Actor is null ? command.Source : new TerrainRect(0, 0, FrameTexture!.GetWidth(), FrameTexture.GetHeight());
        if (source.X < 0 || source.Y < 0 || (long)source.X + source.Width > FrameTexture!.GetWidth()
            || (long)source.Y + source.Height > FrameTexture.GetHeight())
            throw new InvalidOperationException("AREA_NEAREST_SOURCE_CROP_DOMAIN");
        var size = (Vector2I)viewport.GetVisibleRect().Size;
        var state = (transform, source, command.Destination, command.FlipX, command.FlipY, size);
        _material ??= new ShaderMaterial();
        if (_materialState != (command.Additive, command.Alpha) || Material != _material)
        {
            _material.Shader = command.Additive ? AddShader.Value : NormalShader.Value;
            Material = _material;
            _material.SetShaderParameter("body_alpha", command.Alpha / 255f);
            _materialState = (command.Additive, command.Alpha);
            _ambientState = null; // re-send the ambient uniform with every shader swap
        }
        if (_ambientState != Ambient)
        {
            _material.SetShaderParameter("ambient", new Vector3(Ambient.R, Ambient.G, Ambient.B));
            _ambientState = Ambient;
        }
        if (_state == state)
        {
            // Still in use: keep the shared row from being evicted by this submission.
            if (Mappings is { } current && _key is { } same) Bind(current.Acquire(same));
            return;
        }
        if (size.X > MaxViewportAxis || size.Y > MaxViewportAxis)
            throw new InvalidOperationException("AREA_NEAREST_VIEWPORT_DOMAIN");
        var envelope = ScreenEnvelope(transform, command.Destination, size);
        if (!envelope.Intersects(new Rect2I(Vector2I.Zero, size))) { ReleaseMapping(); return; }
        // A pure integer camera translation that moves the envelope by the same amount
        // keeps the exact row key (AreaNearestAxis.RowKey), so the key is reused.
        AreaMappingKey key;
        if (_key is { } previousKey && _keyed is { } k && k.Source == source && k.Destination == command.Destination
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
        _keyed = (transform, envelope, source, command.Destination, command.FlipX, command.FlipY);
        ImageTexture mapping;
        if (Mappings is { } shared) mapping = shared.Acquire(key);
        else
        {
            _ownMappings ??= new AreaMappingCache();
            _ownMappings.BeginSubmission(); mapping = _ownMappings.Acquire(key); _ownMappings.DropUnused();
        }
        Bind(mapping); _key = key;
        _material.SetShaderParameter("screen_start", envelope.Position);
        _material.SetShaderParameter("screen_size", envelope.Size);
        var inverse = transform.AffineInverse();
        var a = inverse * (Vector2)envelope.Position;
        var b = inverse * (Vector2)envelope.End;
        _primitive = new(a, b - a); _state = state;
    }

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
        if (!ReferenceEquals(mapping, _mapping)) _material!.SetShaderParameter("index_rows", mapping);
        _mapping = mapping;
    }

    /// <summary>Stops sampling the index rows. Shared rows stay owned by the cache;
    /// a standalone item's private row is disposed.</summary>
    public void ReleaseMapping()
    {
        _material?.SetShaderParameter("index_rows", default(Variant));
        _mapping = null; _state = null; _key = null; _keyed = null;
        _ownMappings?.Clear();
    }

    public override void _Draw()
    {
        if (Command is not { } command || FrameTexture is null) return;
        PrepareMapping(command);
        if (_mapping is not null) DrawTextureRect(FrameTexture, _primitive, false);
        if (Actor is not null && _guide is null) { _guide = new AreaActorGuide(); AddChild(_guide); }
        if (_guide is not null) { _guide.Actor = Actor; _guide.QueueRedraw(); }
    }

    public override void _ExitTree()
    {
        ReleaseMapping(); Material = null; _material?.Dispose(); _material = null; _materialState = null; _ambientState = null;
        _ownMappings?.Dispose(); _ownMappings = null;
    }
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
