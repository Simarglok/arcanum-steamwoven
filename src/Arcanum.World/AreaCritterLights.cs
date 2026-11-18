using Arcanum.Data.Area;
using Arcanum.Data.Lighting;
using Arcanum.Data.Terrain;

namespace Arcanum.World;

/// <summary>
/// ARC-DEV-353: where a critter that left its T0 place stands now: the world cell it holds and the projected pixel shift of its continuous
/// anchor from its T0 anchor (the shift the moved body command carries, <see cref="AreaAiMovers.Commands"/>,
/// <see cref="AreaCompanionGroup.Commands"/>), before any walk frame changes its drawn rectangle.
/// </summary>
public readonly record struct AreaCritterPose(AreaObjectKey Key, long CellX, long CellY, int ShiftX, int ShiftY);

/// <summary>
/// ARC-DEV-353 (V7d second part, ledger LT10, LT12): a critter's light is its saved primary light (the original writes it from the worn
/// items when they change, EQ12) and it moves with the critter; the sector load builds it at the critter's T0 place, and every move of
/// the object moves its lights with it.
/// </summary>
public static class AreaCritterLights
{
    /// <summary>
    /// The critter's light where the critter stands: its saved light with the critter's cell, and the screen offsets that put the light the pose's
    /// shift away from where the sector load built it (<paramref name="pose"/> null: at its T0 place, as built).
    /// </summary>
    public static StationaryLight Place(ObjectLight light, AreaCritterPose? pose)
    {
        ArgumentNullException.ThrowIfNull(light);
        if (!light.Critter) throw new ArgumentException("Not a critter's light.", nameof(light));
        var flags = ObjectLightReader.FlagsAtLoad(light, 12);
        var (cellX, cellY, offsetX, offsetY) = Stand(light.X, light.Y, light.OffsetX, light.OffsetY, pose);
        var sector = new Arcanum.Data.Area.AreaSectorCoordinate(cellX >> 6, cellY >> 6).Packed;
        return new StationaryLight(sector, -1, cellX, cellY, offsetX, offsetY, flags, light.ArtId, light.Color, light.Object.ObjectIdSha256)
        { Owner = light.Object, Slot = light.Slot };
    }

    /// <summary>
    /// ARC-MAGIC-1229: where something that sits at <paramref name="baseX"/>, <paramref name="baseY"/> with the screen offsets of its T0 place stands once its critter has the
    /// <paramref name="pose"/> (null: still at its T0 place): the pose's cell and the offsets that put it the pose's shift away from the T0 anchor. The light of an eye candy on a
    /// critter (<see cref="AreaEyeCandyLights"/>) moves with the critter the same way as the critter's own light (LT10).
    /// </summary>
    /// <summary>
    /// ARC-AI-1605 (ledger AW18, VBG9): the screen offset a walking critter has left inside its committed cell: the projected distance (the grid's 40 and 20 pixel steps, as <see cref="Stand"/> and the companions' pose project it)
    /// from the cell's centre to its continuous anchor, which the original keeps in the object's pixel offset (fields 3 and 4) while a step is under way and zeroes at the end of the walk. (0, 0) for a critter at rest. The
    /// anchor and the cell are in the same grid coordinates (an anchor at the cell's centre is the integer cell), so no chart conversion is needed.
    /// </summary>
    public static (int X, int Y) StepOffset(Arcanum.Simulation.Movement.ExactScalar anchorU, Arcanum.Simulation.Movement.ExactScalar anchorV, Arcanum.Simulation.Movement.CellCoord committed)
    {
        double du = GridActorMath.Seconds(anchorU) - committed.X, dv = GridActorMath.Seconds(anchorV) - committed.Y;
        return ((int)Math.Round(40 * (dv - du), MidpointRounding.AwayFromZero), (int)Math.Round(20 * (du + dv), MidpointRounding.AwayFromZero));
    }

    public static (long CellX, long CellY, int OffsetX, int OffsetY) Stand(long baseX, long baseY, int baseOffsetX, int baseOffsetY, AreaCritterPose? pose)
    {
        if (pose is not { } moved) return (baseX, baseY, baseOffsetX, baseOffsetY);
        // The light stands at its tile's screen point plus its offsets; a different cell moves the tile by the projection of the
        // cell difference, which the shift already includes, so the offsets take only what remains.
        var tile = TerrainGeometry.Project(checked((int)(moved.CellX - baseX)), checked((int)(moved.CellY - baseY)));
        var origin = TerrainGeometry.Project(0, 0);
        return (moved.CellX, moved.CellY, checked(baseOffsetX + moved.ShiftX - (tile.X - origin.X)), checked(baseOffsetY + moved.ShiftY - (tile.Y - origin.Y)));
    }
}
