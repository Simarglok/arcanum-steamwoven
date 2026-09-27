using Godot;
using Arcanum.TerrainViewer.Core;

namespace Arcanum.TerrainViewer;

public partial class TerrainCanvas : Node2D
{
    private TerrainScenePlan? _plan;
    private TerrainTextureCache? _textures;

    public TerrainCanvas()
    {
        TextureFilter = TextureFilterEnum.Nearest;
    }

    public int CommandCount => _plan?.Commands.Count ?? 0;

    /// <summary>Stable, content-free geometry snapshot used by camera smoke checks.</summary>
    public string GeometrySignature => _plan is null
        ? string.Empty
        : string.Join(';', _plan.Commands.Select(command => $"{command.Ordinal}:{command.Source.X},{command.Source.Y},{command.Source.Width},{command.Source.Height}:{command.Destination.X},{command.Destination.Y},{command.Destination.Width},{command.Destination.Height}:{command.FlipX}"));

    public bool TryGetOpaqueLandmark(out TerrainCanvasLandmark landmark)
    {
        landmark = default;
        if (_plan is null || _textures is null) return false;
        foreach (var command in _plan.Commands)
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

    public void Publish(TerrainScenePlan plan, TerrainTextureCache textures)
    {
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _textures = textures ?? throw new ArgumentNullException(nameof(textures));
        QueueRedraw();
    }

    public void ClearScene()
    {
        _plan = null;
        _textures = null;
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
    }
}

public readonly record struct TerrainCanvasLandmark(int WorldX, int WorldY, byte R, byte G, byte B, byte A, int Ordinal);
