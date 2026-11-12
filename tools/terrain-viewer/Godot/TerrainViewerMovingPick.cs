using Arcanum.Data.Area;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-INPUT-1356: what the pointer's pick reads of the moving bodies the canvas draws.
public partial class TerrainCanvas
{
    /// <summary>The moving bodies as the canvas draws them now (the last submission).</summary>
    internal IReadOnlyList<AreaDrawCommand>? AreaMovingDrawn => _areaMoving;

    /// <summary>Counts the changes of the moving bodies' drawing (a step, a pose, a fall), so the pointer's pick is made again when one moved.</summary>
    internal int AreaMovingDrawnStamp => _areaMovingStamp;
}

public partial class TerrainViewerApp
{
    /// <summary>ARC-INPUT-1356 (smoke): over the opaque pixels of the body as the canvas draws it now, how many the pick answers for the body, for another object
    /// (drawn in front) and for nothing. A body that moves is picked where it is drawn at the frame the pointer is handled.</summary>
    private (int Opaque, int Picked, int Other, int None, Vector2? FirstNone) MovingPickCount(AreaObjectKey key, int step = 3)
    {
        if (_canvas?.AreaMovingDrawn?.FirstOrDefault(c => c.Identity == key) is not { } drawn) return default;
        var transform = AreaDrawItem.RasterTransform(GetViewport()); var hud = _hud?.Rect; var frames = AreaFrames();
        var size = GetViewport().GetVisibleRect().Size;
        var left = Math.Max(0, (int)Math.Floor(transform.Origin.X + transform.X.X * drawn.Destination.X));
        var top = Math.Max(0, (int)Math.Floor(transform.Origin.Y + transform.Y.Y * drawn.Destination.Y));
        var right = Math.Min((int)size.X, (int)Math.Ceiling(transform.Origin.X + transform.X.X * (drawn.Destination.X + drawn.Destination.Width)));
        var bottom = Math.Min((int)size.Y, (int)Math.Ceiling(transform.Origin.Y + transform.Y.Y * (drawn.Destination.Y + drawn.Destination.Height)));
        int opaque = 0, picked = 0, other = 0, none = 0; Vector2? firstNone = null;
        for (var y = top; y < bottom; y += step)
        for (var x = left; x < right; x += step)
        {
            var p = new Vector2(x + .5f, y + .5f);
            if (hud?.HasPoint(p) == true || AreaAlphaAt(drawn, frames, transform, x, y) == 0) continue;
            opaque++;
            var hit = AreaObjectAtScreen(p);
            if (hit == key) picked++; else if (hit is null) { none++; firstNone ??= p; } else other++;
        }
        return (opaque, picked, other, none, firstNone);
    }
}

public partial class TerrainViewerApp
{
    /// <summary>
    /// ARC-INPUT-1356: what the pointer was over when the targeting cursor refused a press: the pick with and without the cursor's spared objects, the cell under
    /// the point, and the creatures drawn near it (their pixel distance to the point's rectangle test, the alpha under the point, fallen or stepping), so a report of a
    /// refused click on a moving creature says whether the point missed the body, hit a corpse or a spared body, or hit nothing at all.
    /// </summary>
    private string PickRefusalDetail(Vector2 screen)
    {
        try
        {
            if (HoverIndex() is not { } index || _readyArea is null) return "no pick";
            var transform = AreaDrawItem.RasterTransform(GetViewport()); var frames = AreaFrames();
            var sx = (int)Math.Floor(screen.X); var sy = (int)Math.Floor(screen.Y);
            string Name(AreaObjectKey? key) => key is { } k && index.Contains(k) ? $"{index.Row(k).Type}:{k.SourceIndex}" : "none";
            var moving = AreaMovingCommands() ?? [];
            var near = new List<string>();
            foreach (var command in moving.Concat(index.Bodies))
            {
                if (command.Identity is not { } key || !index.Contains(key) || index.Row(key).Type != AreaHoverIndex.NpcType) continue;
                if (!frames.ContainsKey(command.Frame)) continue;
                var left = transform.Origin.X + transform.X.X * command.Destination.X; var top = transform.Origin.Y + transform.Y.Y * command.Destination.Y;
                var right = left + transform.X.X * command.Destination.Width; var bottom = top + transform.Y.Y * command.Destination.Height;
                var dx = Math.Max(Math.Max(left - screen.X, screen.X - right), 0); var dy = Math.Max(Math.Max(top - screen.Y, screen.Y - bottom), 0);
                var distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance > 60) continue;
                var mover = _aiHost?.Scene.Npcs.FirstOrDefault(n => n.Row.Key == key) is { } npc ? _aiHost.Movers.Of(npc.Entity) : null;
                near.Add($"npc {key.SourceIndex} {(moving.Contains(command) ? "moving" : "t0")} dist={distance:0} alpha={(distance == 0 ? AreaAlphaAt(command, frames, transform, sx, sy) : -1)}"
                    + $" fallen={_fallenBodies.ContainsKey(key)} stepping={(mover is null ? "-" : mover.Stepping(_aiHost!.NowMs))}");
            }
            var chart = _canvas is null ? default : _canvas.GetGlobalTransformWithCanvas().AffineInverse() * screen;
            var cell = _canvas is null ? null : AreaCellPicking.Pick(new(chart.X, chart.Y));
            return $"at=({screen.X:0},{screen.Y:0}) pick={Name(AreaObjectAtScreen(screen, spared: SpellPickSpared()))} unspared={Name(AreaObjectAtScreen(screen))} cell={(cell is { } c ? c.X + "," + c.Y : "none")}"
                + $" near=[{string.Join("; ", near.Take(4))}]";
        }
        catch (Exception error)
        {
            return "detail failed " + error.GetType().Name;
        }
    }
}

