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
        void fragment() {
            ivec2 p = ivec2(FRAGCOORD.xy) - screen_start;
            if (any(lessThan(p, ivec2(0))) || any(greaterThanEqual(p, screen_size))) discard;
            ivec2 x = ivec2(round(texelFetch(index_rows, ivec2(p.x, 0), 0).rg * 255.0));
            ivec2 y = ivec2(round(texelFetch(index_rows, ivec2(p.y, 1), 0).rg * 255.0));
            int sx = x.x + x.y * 256 - 1;
            int sy = y.x + y.y * 256 - 1;
            if (sx < 0 || sy < 0) discard;
            COLOR = texelFetch(TEXTURE, ivec2(sx, sy), 0) * vec4(1.0, 1.0, 1.0, body_alpha);
        }
        """;
    // Two immutable shader programs for process lifetime, not per-object copies.
    private static readonly Lazy<Shader> NormalShader = new(() => new Shader { Code = Code });
    private static readonly Lazy<Shader> AddShader = new(() => new Shader { Code = Code.Replace("blend_mix", "blend_add", StringComparison.Ordinal) });
    private ShaderMaterial? _material;
    private ImageTexture? _mapping;
    private (Transform2D Transform, TerrainRect Source, TerrainRect Destination, bool X, bool Y, Vector2I Size)? _state;
    private Rect2 _primitive;
    private AreaActorGuide? _guide;
    public long MappingBytes { get; private set; }
    public ImageTexture? FrameTexture { get; set; }
    public AreaDrawCommand? Command { get; set; }
    public ActorDiagnosticDrawCommand? Actor { get; set; }

    internal static Transform2D RasterTransform(Viewport viewport)
    {
        var transform = viewport.GetCanvasTransform();
        if (viewport.Snap2DTransformsToPixel)
            transform.Origin = new(MathF.Ceiling(transform.Origin.X - .5f), MathF.Ceiling(transform.Origin.Y - .5f));
        if (transform.X.Y != 0 || transform.Y.X != 0 || transform.X.X <= 0 || transform.Y.Y <= 0)
            throw new InvalidOperationException("AREA_NEAREST_TRANSFORM_DOMAIN");
        return transform;
    }

    private static Rect2I ScreenEnvelope(Viewport viewport, AreaDrawCommand command)
    {
        var size = viewport.GetVisibleRect().Size;
        if (size.X > MaxViewportAxis || size.Y > MaxViewportAxis)
            throw new InvalidOperationException("AREA_NEAREST_VIEWPORT_DOMAIN");
        var t = RasterTransform(viewport); var d = command.Destination;
        // Double envelope computation preserves the exact supplied float inputs.
        // One pixel of conservative primitive padding is NOT source ownership.
        var left = Math.Clamp((int)Math.Floor((double)t.Origin.X + (double)t.X.X * d.X) - 1, 0, (int)size.X);
        var top = Math.Clamp((int)Math.Floor((double)t.Origin.Y + (double)t.Y.Y * d.Y) - 1, 0, (int)size.Y);
        var right = Math.Clamp((int)Math.Ceiling((double)t.Origin.X + (double)t.X.X * ((long)d.X + d.Width)) + 1, left, (int)size.X);
        var bottom = Math.Clamp((int)Math.Ceiling((double)t.Origin.Y + (double)t.Y.Y * ((long)d.Y + d.Height)) + 1, top, (int)size.Y);
        return new(left, top, right - left, bottom - top);
    }

    internal static long EstimateMappingBytes(Viewport viewport, AreaDrawCommand command)
    {
        var envelope = ScreenEnvelope(viewport, command);
        return envelope.Size.X == 0 || envelope.Size.Y == 0 ? 0 : Math.Max(envelope.Size.X, envelope.Size.Y) * 8L;
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
        var state = (transform, source, command.Destination, command.FlipX, command.FlipY, (Vector2I)viewport.GetVisibleRect().Size);
        _material ??= new ShaderMaterial();
        _material.Shader = command.Additive ? AddShader.Value : NormalShader.Value;
        Material = _material;
        _material.SetShaderParameter("body_alpha", command.Alpha / 255f);
        if (_state == state) return;
        var envelope = ScreenEnvelope(viewport, command);
        if (envelope.Size.X == 0 || envelope.Size.Y == 0) { ReleaseMapping(); return; }
        var width = Math.Max(envelope.Size.X, envelope.Size.Y);
        var bytes = checked(width * 8);
        if (bytes > MaxMappingBytes) throw new InvalidOperationException("AREA_NEAREST_MAPPING_BUDGET");
        var d = command.Destination;
        var x = new AreaNearestAxis(transform.Origin.X, transform.X.X, d.X, d.Width, source.X, source.Width, command.FlipX);
        var y = new AreaNearestAxis(transform.Origin.Y, transform.Y.Y, d.Y, d.Height, source.Y, source.Height, command.FlipY);
        var rgba = new byte[bytes];
        void Encode(int at, int value)
        {
            var code = value + 1; rgba[at] = (byte)(code & 255); rgba[at + 1] = (byte)(code >> 8); rgba[at + 3] = 255;
        }
        for (var i = 0; i < envelope.Size.X; i++) Encode(i * 4, x.SourceIndex(envelope.Position.X + i));
        for (var i = 0; i < envelope.Size.Y; i++) Encode((width + i) * 4, y.SourceIndex(envelope.Position.Y + i));
        using var image = Image.CreateFromData(width, 2, false, Image.Format.Rgba8, rgba);
        if (_mapping is not null && _mapping.GetWidth() == width) _mapping.Update(image);
        else
        {
            ReleaseMapping(); _mapping = ImageTexture.CreateFromImage(image);
        }
        MappingBytes = bytes;
        _material.SetShaderParameter("index_rows", _mapping);
        _material.SetShaderParameter("screen_start", envelope.Position);
        _material.SetShaderParameter("screen_size", envelope.Size);
        var inverse = transform.AffineInverse();
        var a = inverse * (Vector2)envelope.Position;
        var b = inverse * (Vector2)envelope.End;
        _primitive = new(a, b - a); _state = state;
    }

    public void ReleaseMapping()
    {
        _material?.SetShaderParameter("index_rows", default(Variant));
        _mapping?.Dispose(); _mapping = null; _state = null; MappingBytes = 0;
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
        ReleaseMapping(); Material = null; _material?.Dispose(); _material = null;
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
