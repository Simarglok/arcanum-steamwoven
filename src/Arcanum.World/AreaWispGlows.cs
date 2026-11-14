using Arcanum.Data.Area;

namespace Arcanum.World;

/// <summary>
/// ARC-RENDER-2064 (FR-LIGHT-011, FR-LIGHT-013): the glow of one kind of wisp, the glowing-orb creatures. A presentation record of our own, keyed by the creature prototype: the original
/// lights nothing round a wisp (its brightness is its sprite only, ARC-RES-2061), so this is a deliberate Steamwoven difference that shows only in the relit look of the HD graphics
/// option; Legacy is unchanged. Colour is the sprite's brightest tone, radius is in screen pixels, strength scales the colour (1 = the sprite's tone as is), lift raises the light's
/// anchor from the creature's ground point to the orb's centre.
/// </summary>
public sealed record WispGlow(int Prototype, string Kind, byte Red, byte Green, byte Blue, float Radius, float Strength, int Lift);

/// <summary>A wisp that glows now: its row's key, the world cell and the screen offsets of its light anchor (before the lift), and the glow.</summary>
public readonly record struct AreaWispLight(AreaObjectKey Key, long CellX, long CellY, int OffsetX, int OffsetY, WispGlow Glow);

/// <summary>
/// ARC-RENDER-2064: the table of wisp kinds (<see cref="Table"/>) and where their lights stand. A wisp's light follows its creature the way a carried light does (LT10, LT12,
/// <see cref="AreaCritterLights.Stand"/>): at the creature's T0 cell until the AI host or the follower group moved it, then at its pose. It is not an object light of the map, writes nothing
/// to the saved object data and needs no schema step. Pure; no Godot, no session.
/// </summary>
public static class AreaWispGlows
{
    // Slots of the object's own screen offsets (the critter's light is built with them as well).
    private const int OffsetXSlot = 2, OffsetYSlot = 3;

    /// <summary>The wisp kinds, by creature prototype (see docs/research/relighting-experiment.md, "Wisps").</summary>
    public static IReadOnlyList<WispGlow> Table { get; } = Array.AsReadOnly(new WispGlow[]
    {
        // One creature species (art species 24, the 40x40 glow disc `wowuwxaa.art`) in four palettes, one prototype each (ARC-RENDER-2064, AC1). The tone is the disc's own most saturated bright
        // colour (the core is white in every palette); the radius is about three disc widths, the strength keeps the glow light and subtle.
        new(28471, "Love Lights (violet)", 0x99, 0x57, 0xD3, 130f, 0.9f, 20),
        new(28472, "Will-o-the-Wisp (amber)", 0xB0, 0x99, 0x49, 130f, 0.9f, 20),
        new(28473, "Volar's Wisp (cyan)", 0x57, 0xD3, 0xD3, 130f, 0.9f, 20),
        new(28474, "Death Lantern (red)", 0xE5, 0x3B, 0x3A, 130f, 0.9f, 20),
    });

    private static readonly Dictionary<int, WispGlow> ByPrototype = Table.ToDictionary(glow => glow.Prototype);

    /// <summary>The glow of a creature prototype, null when it is not a wisp.</summary>
    public static WispGlow? Of(int prototype) => ByPrototype.TryGetValue(prototype, out var glow) ? glow : null;

    /// <summary>The map rows that are wisps (a core-scope NPC-type object with a cell whose prototype is in the table, the rows the critter lights read too), in row order.</summary>
    public static IReadOnlyList<(AreaObjectRecord Row, WispGlow Glow)> Rows(IEnumerable<AreaObjectRecord> objects)
    {
        ArgumentNullException.ThrowIfNull(objects);
        var found = new List<(AreaObjectRecord, WispGlow)>();
        foreach (var row in objects)
            if (row.Type == AreaNpcRouteReader.NpcType && row.Scope == AreaScope.Core && row.WorldX is not null && row.WorldY is not null && Of(row.PrototypeNumber) is { } glow) found.Add((row, glow));
        return found;
    }

    /// <summary>Where a wisp's light stands: its row's cell and offsets at T0 (<paramref name="pose"/> null), or where the pose put it.</summary>
    public static AreaWispLight Place(AreaObjectRecord row, WispGlow glow, AreaCritterPose? pose)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(glow);
        var (cellX, cellY, offsetX, offsetY) = AreaCritterLights.Stand(row.WorldX ?? throw new ArgumentException("A row without a cell.", nameof(row)),
            row.WorldY ?? throw new ArgumentException("A row without a cell.", nameof(row)), Offset(row, OffsetXSlot), Offset(row, OffsetYSlot), pose);
        return new AreaWispLight(row.Key, cellX, cellY, offsetX, offsetY, glow);
    }

    private static int Offset(AreaObjectRecord row, int slot)
        => row.EffectiveFields.FirstOrDefault(field => field.Slot == slot && field.Presence == AreaPresence.Value)?.Number is { } value && value is >= int.MinValue and <= int.MaxValue ? (int)value : 0;
}
