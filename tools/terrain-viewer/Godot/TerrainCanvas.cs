using Godot;
using Arcanum.TerrainViewer.Core;

namespace Arcanum.TerrainViewer;

public partial class TerrainCanvas : Node2D
{
    private TerrainScenePlan? _plan;
    private StaticSceneryScenePlan? _staticPlan;
    private ActorDiagnosticScenePlan? _actorPlan;
    private TerrainTextureCache? _textures;
    private bool _showStatics = true;
    private bool _showActor = true;
    internal bool ActorVisibleForReadback => _showActor;
    internal bool StaticVisibleForReadback => _showStatics;

    public TerrainCanvas()
    {
        TextureFilter = TextureFilterEnum.Nearest;
    }

    public int CommandCount => (_area?.Commands.Count ?? _plan?.Commands.Count ?? 0) + (_staticPlan?.Commands.Count ?? 0) + (_actorPlan is null ? 0 : 1);

    /// <summary>Stable, content-free geometry snapshot used by camera smoke checks.</summary>
    public string GeometrySignature => _plan is null
        ? string.Empty
        : string.Join(';', _plan.Commands.Select(command => $"terrain:{command.Ordinal}:{command.Footprint.X},{command.Footprint.Y},{command.Footprint.Width},{command.Footprint.Height}:{command.Source.X},{command.Source.Y},{command.Source.Width},{command.Source.Height}:{command.Destination.X},{command.Destination.Y},{command.Destination.Width},{command.Destination.Height}:{command.FlipX}")
            .Concat((_staticPlan?.Commands ?? Array.Empty<StaticSceneryDrawCommand>()).Select(command => $"static:{command.SourceIndex}:{command.TextureKey.ResourceIdentity.DatOrdinal},{command.TextureKey.Group},{command.TextureKey.Frame},{command.TextureKey.Palette}:{command.AnchorX},{command.AnchorY}:{command.Destination.X},{command.Destination.Y},{command.Destination.Width},{command.Destination.Height}:{command.FlipX}")));

    public bool TryGetOpaqueLandmark(out TerrainCanvasLandmark landmark)
        => TryGetOpaqueLandmark(null, out landmark);

    public bool TryGetOpaqueLandmark(Func<TerrainCanvasLandmark, bool>? accept, out TerrainCanvasLandmark landmark)
    {
        landmark = default;
        if (_plan is null || _textures is null) return false;
        // Later commands paint over earlier ones, so choose the final command
        // for a full-scene landmark that cannot be obscured by this canvas.
        foreach (var command in _plan.Commands.Reverse())
        {
            if (!_textures.TryFindOpaquePixel(command.TextureKey, command.Source, pixel =>
                {
                    var x = pixel.X - command.Source.X;
                    if (command.FlipX) x = command.Source.Width - 1 - x;
                    var candidate = new TerrainCanvasLandmark(command.Destination.X + x, command.Destination.Y + pixel.Y - command.Source.Y,
                        pixel.R, pixel.G, pixel.B, pixel.A, command.Ordinal);
                    return accept is null || accept(candidate);
                }, out var pixel)) continue;
            var offsetX = pixel.X - command.Source.X;
            if (command.FlipX) offsetX = command.Source.Width - 1 - offsetX;
            landmark = new TerrainCanvasLandmark(command.Destination.X + offsetX, command.Destination.Y + pixel.Y - command.Source.Y,
                pixel.R, pixel.G, pixel.B, pixel.A, command.Ordinal);
            return true;
        }
        return false;
    }

    public bool TryGetOpaqueStaticLandmark(out TerrainCanvasLandmark landmark)
        => TryGetOpaqueStaticLandmark(null, out landmark);

    /// <summary>
    /// Finds an opaque pixel from the reverse static draw order.  A caller may
    /// reject candidates that cannot survive the active camera sampling; this
    /// still returns an exact decoded static RGBA landmark, not a pass-delta.
    /// </summary>
    public bool TryGetOpaqueStaticLandmark(Func<TerrainCanvasLandmark, bool>? accept, out TerrainCanvasLandmark landmark)
    {
        landmark = default;
        if (_plan is null || _staticPlan is null || _textures is null) return false;
        // Reverse diagnostic draw order selects the last static command first,
        // so no later static command can obscure the chosen opaque pixel.
        foreach (var command in _staticPlan.Commands.Reverse())
        {
            if (!_textures.TryFindOpaquePixel(command.TextureKey, pixel =>
                {
                    var offsetX = command.FlipX ? command.Destination.Width - 1 - pixel.X : pixel.X;
                    var candidate = new TerrainCanvasLandmark(command.Destination.X + offsetX, command.Destination.Y + pixel.Y,
                        pixel.R, pixel.G, pixel.B, pixel.A, command.SourceIndex);
                    return accept is null || accept(candidate);
                }, out var pixel)) continue;
            var offsetX = command.FlipX ? command.Destination.Width - 1 - pixel.X : pixel.X;
            landmark = new TerrainCanvasLandmark(command.Destination.X + offsetX, command.Destination.Y + pixel.Y,
                pixel.R, pixel.G, pixel.B, pixel.A, command.SourceIndex);
            return true;
        }
        return false;
    }

    public void Publish(TerrainScenePlan plan, TerrainTextureCache textures)
    {
        ClearArea();
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _staticPlan = null;
        _actorPlan = null;
        _textures = textures ?? throw new ArgumentNullException(nameof(textures));
        QueueRedraw();
    }

    public void Publish(DiagnosticScenePlan plan, TerrainTextureCache textures)
    {
        ClearArea();
        _plan = plan?.Terrain ?? throw new ArgumentNullException(nameof(plan));
        _staticPlan = plan.Statics;
        _actorPlan = plan.Actor;
        _textures = textures ?? throw new ArgumentNullException(nameof(textures));
        QueueRedraw();
    }

    public void ClearScene()
    {
        ClearArea();
        _plan = null;
        _staticPlan = null;
        _actorPlan = null;
        _textures = null;
        QueueRedraw();
    }

    /// <summary>Native smoke-only full-viewport comparison seam.</summary>
    public void SetStaticVisibilityForReadback(bool visible)
    {
        _showStatics = visible;
        QueueRedraw();
    }

    public void SetActorVisibilityForReadback(bool visible)
    {
        _showActor = visible;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_plan is null || _textures is null) return;
        foreach (var command in _plan.Commands)
        {
            if (!_textures.TryGet(command.TextureKey, out var texture)) continue;
            var source = new Rect2(command.Source.X, command.Source.Y, command.Source.Width, command.Source.Height);
            if (command.FlipX)
            {
                DrawSetTransform(new Vector2(command.Destination.X + command.Destination.Width, command.Destination.Y), 0, new Vector2(-1, 1));
                DrawTextureRectRegion(texture, new Rect2(0, 0, command.Destination.Width, command.Destination.Height), source);
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            }
            else
            {
                DrawTextureRectRegion(texture, new Rect2(command.Destination.X, command.Destination.Y, command.Destination.Width, command.Destination.Height), source);
            }
        }
        DrawStatics();
        DrawActor();
    }

    private void DrawActor()
    {
        if (_showActor && _actorPlan is not null && _textures is not null)
        {
            var actor = _actorPlan.Current;
            if (_textures.TryGet(actor.TextureKey, out var texture))
            {
                var destination = new Rect2(actor.Destination.X, actor.Destination.Y, actor.Destination.Width, actor.Destination.Height);
                var source = new Rect2(0, 0, actor.Destination.Width, actor.Destination.Height);
                if (actor.FlipX)
                {
                    DrawSetTransform(new Vector2(actor.Destination.X + actor.Destination.Width, actor.Destination.Y), 0, new Vector2(-1, 1));
                    DrawTextureRectRegion(texture, new Rect2(0, 0, actor.Destination.Width, actor.Destination.Height), source);
                    DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                }
                else DrawTextureRectRegion(texture, destination, source);
                DrawRect(destination, Colors.Magenta, false, 1f);
                DrawLine(new Vector2((float)actor.AnchorX - 4, (float)actor.AnchorY), new Vector2((float)actor.AnchorX + 4, (float)actor.AnchorY), Colors.Yellow, 1f);
                DrawLine(new Vector2((float)actor.AnchorX, (float)actor.AnchorY - 4), new Vector2((float)actor.AnchorX, (float)actor.AnchorY + 4), Colors.Yellow, 1f);
            }
        }
    }

    private void DrawStatics()
    {
        if (_staticPlan is null || !_showStatics || _textures is null) return;
        foreach (var command in _staticPlan.Commands)
        {
            if (!_textures.TryGet(command.TextureKey, out var texture)) continue;
            var source = new Rect2(0, 0, command.Destination.Width, command.Destination.Height);
            if (command.FlipX)
            {
                DrawSetTransform(new Vector2(command.Destination.X + command.Destination.Width, command.Destination.Y), 0, new Vector2(-1, 1));
                DrawTextureRectRegion(texture, new Rect2(0, 0, command.Destination.Width, command.Destination.Height), source);
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            }
            else DrawTextureRectRegion(texture, new Rect2(command.Destination.X, command.Destination.Y, command.Destination.Width, command.Destination.Height), source);
        }
    }
}

public readonly record struct TerrainCanvasLandmark(int WorldX, int WorldY, byte R, byte G, byte B, byte A, int Ordinal);
