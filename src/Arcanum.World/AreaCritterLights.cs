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
        long cellX = light.X, cellY = light.Y; int offsetX = light.OffsetX, offsetY = light.OffsetY;
        if (pose is { } moved)
        {
            cellX = moved.CellX; cellY = moved.CellY;
            // The light stands at its tile's screen point plus its offsets; a different cell moves the tile by the projection of the
            // cell difference, which the shift already includes, so the offsets take only what remains.
            var tile = TerrainGeometry.Project(checked((int)(cellX - light.X)), checked((int)(cellY - light.Y)));
            var origin = TerrainGeometry.Project(0, 0);
            offsetX = checked(light.OffsetX + moved.ShiftX - (tile.X - origin.X));
            offsetY = checked(light.OffsetY + moved.ShiftY - (tile.Y - origin.Y));
        }
        var sector = new Arcanum.Data.Area.AreaSectorCoordinate(cellX >> 6, cellY >> 6).Packed;
        return new StationaryLight(sector, -1, cellX, cellY, offsetX, offsetY, flags, light.ArtId, light.Color, light.Object.ObjectIdSha256)
        { Owner = light.Object, Slot = light.Slot };
    }
}
