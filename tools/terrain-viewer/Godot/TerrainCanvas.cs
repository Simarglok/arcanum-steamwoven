using Godot;
using Arcanum.TerrainViewer.Core;

namespace Arcanum.TerrainViewer;

public partial class TerrainCanvas : Node2D
{
    private TerrainScenePlan? _plan;
    private StaticSceneryScenePlan? _staticPlan;
    private TerrainTextureCache? _textures;
    private bool _showStatics = true;

    public TerrainCanvas()
    {
        TextureFilter = TextureFilterEnum.Nearest;
    }

    public int CommandCount => (_plan?.Commands.Count ?? 0) + (_staticPlan?.Commands.Count ?? 0);

    /// <summary>Stable, content-free geometry snapshot used by camera smoke checks.</summary>
    public string GeometrySignature => _plan is null
        ? string.Empty
        : string.Join(';', _plan.Commands.Select(command => $"terrain:{command.Ordinal}:{command.Footprint.X},{command.Footprint.Y},{command.Footprint.Width},{command.Footprint.Height}:{command.Source.X},{command.Source.Y},{command.Source.Width},{command.Source.Height}:{command.Destination.X},{command.Destination.Y},{command.Destination.Width},{command.Destination.Height}:{command.FlipX}")
            .Concat((_staticPlan?.Commands ?? Array.Empty<StaticSceneryDrawCommand>()).Select(command => $"static:{command.SourceIndex}:{command.TextureKey.ResourceIdentity.DatOrdinal},{command.TextureKey.Group},{command.TextureKey.Frame},{command.TextureKey.Palette}:{command.AnchorX},{command.AnchorY}:{command.Destination.X},{command.Destination.Y},{command.Destination.Width},{command.Destination.Height}:{command.FlipX}")));

    public bool TryGetOpaqueLandmark(out TerrainCanvasLandmark landmark)
    {
        landmark = default;
        if (_plan is null || _textures is null) return false;
        // Later commands paint over earlier ones, so choose the final command
        // for a full-scene landmark that cannot be obscured by this canvas.
        foreach (var command in _plan.Commands.Reverse())
        {
            if (!_textures.TryFindOpaquePixel(command.TextureKey, command.Source, out var pixel)) continue;
            var offsetX = pixel.X - command.Source.X;
            if (command.FlipX) offsetX = command.Source.Width - 1 - offsetX;
            landmark = new TerrainCanvasLandmark(command.Destination.X + offsetX, command.Destination.Y + pixel.Y - command.Source.Y,
                pixel.R, pixel.G, pixel.B, pixel.A, command.Ordinal);
            return true;
        }
        return false;
    }

    public bool TryGetOpaqueStaticLandmark(out TerrainCanvasLandmark landmark)
    {
        landmark = default;
        if (_plan is null || _staticPlan is null || _textures is null) return false;
        // Choose a static source pixel whose destination is outside terrain's
        // diagnostic bounds. Its full-scene readback cannot be confused with
        // an underlying terrain colour or alpha blend.
        var terrainBounds = _plan.WorldBounds;
        foreach (var command in _staticPlan.Commands)
        {
            if (!_textures.TryFindOpaquePixel(command.TextureKey, (x, y) =>
            {
                var worldX = command.Destination.X + (command.FlipX ? command.Destination.Width - 1 - x : x);
                var worldY = command.Destination.Y + y;
                return worldX < terrainBounds.X || worldX >= terrainBounds.X + terrainBounds.Width
                    || worldY < terrainBounds.Y || worldY >= terrainBounds.Y + terrainBounds.Height;
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
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _staticPlan = null;
        _textures = textures ?? throw new ArgumentNullException(nameof(textures));
        QueueRedraw();
    }

    public void Publish(DiagnosticScenePlan plan, TerrainTextureCache textures)
    {
        _plan = plan?.Terrain ?? throw new ArgumentNullException(nameof(plan));
        _staticPlan = plan.Statics;
        _textures = textures ?? throw new ArgumentNullException(nameof(textures));
        QueueRedraw();
    }

    public void ClearScene()
    {
        _plan = null;
        _staticPlan = null;
        _textures = null;
        QueueRedraw();
    }

    /// <summary>Native smoke-only full-viewport comparison seam.</summary>
    public void SetStaticVisibilityForReadback(bool visible)
    {
        _showStatics = visible;
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
        if (_staticPlan is null || !_showStatics) return;
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
