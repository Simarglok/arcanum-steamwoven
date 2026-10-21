namespace Arcanum.Simulation.Magic;

/// <summary>
/// One target area as the original keeps it (record +0x70 + 0x18 × action, or a component's area): the 64-bit target mask (the
/// OR of the target names' values), the spell flags a target must have and must not have, the radius and the count (−1 or 0:
/// no limit). The data reader's areas map to it one to one.
/// </summary>
public readonly record struct SpellTargetArea(ulong Targets, uint RequiredSpellFlags = 0, uint ExcludedSpellFlags = 0, int Radius = 0, int Count = -1);

/// <summary>
/// One object the target functions read: its id (identity), object type (0 wall, 1 portal, 2 container, 3 scenery, 4 projectile,
/// 5..14 items, 15 PC, 16 NPC, 17 trap), location, object flags, spell flags, critter flags and flags 2, current hit points
/// (0x43D600), hit-point damage (field 0x1D), alignment (stat 19), poison (stat 24), material (field 0x1E, 4 flesh), weight
/// (field 0x59), portal flags (0x2E) and container flags (0x38) (1 locked, 4 magically held), the PC at the top of an NPC's
/// leader chain (0x45DDA0; none for others), the occupant of a scenery object (field 0x46) and an item's owner (field 0x58).
/// </summary>
public sealed record SpellTargetObject(string Id, int Type, SpellLocation Location, int ObjectFlags = 0, int SpellFlags = 0, int CritterFlags = 0,
    int CritterFlags2 = 0, int HitPoints = 1, int HitPointDamage = 0, int Alignment = 0, int Poison = 0, int Material = 0, int Weight = 0,
    int PortalFlags = 0, int ContainerFlags = 0, string? PcLeader = null, string? Occupant = null, string? Owner = null)
{
    public const int Portal = 1, Container = 2, Scenery = 3, Pc = 15, Npc = 16;
    public const int Destroyed = 0x1, Off = 0x2, DontDraw = 0x100, NoBlock = 0x400, Inventory = 0x1000, Invulnerable = 0x400000;
    public const int Undead = 0x4, Animal = 0x8, Injured = 0x780, Sleeping = 0x1000, Surrendered = 0x4000, Unrevivifiable = 0x800000,
        Unresurrectable = 0x1000000, Demon = 0x2000000, Mechanical = 0x20000000;
    public const int SafeOff = 0x800000, Summoned = 0x4000, Locked = 0x1, MagicallyHeld = 0x4, Flesh = 4;

    public bool IsCritter => Type is Pc or Npc;
    public bool IsItem => Type is >= 5 and <= 14;
    /// <summary>0x45D8D0: no hit points left (any object type).</summary>
    public bool IsDead => HitPoints <= 0;
}

/// <summary>
/// The context the target functions read (the original's 0x64-byte target context): the area; the self (+0x8, a run's parent)
/// and the source (+0x10) objects; the origin location (+0x18, the source's location or the run's); the candidate object and
/// tile under test (+0x20, +0x28); the action's target object and tile (+0x30, +0x38); another object whose party a
/// non-party area also spares (+0x40, a run's +0xE8 object); the last summoned object (+0x48) and the run's summoned list
/// (+0x58); the run's object list (+0x54: null when there is none, empty until an action with <c>Tgt_List</c> builds it).
/// </summary>
public sealed record SpellTargetContext(SpellTargetArea Area, SpellTargetObject? Self = null, SpellTargetObject? Source = null,
    SpellLocation Origin = default, SpellTargetObject? Candidate = null, SpellLocation CandidateTile = default, SpellTargetObject? Target = null,
    SpellLocation TargetTile = default, SpellTargetObject? Other = null, SpellTargetObject? Summoned = null,
    IReadOnlyList<SpellTargetObject>? SummonedList = null, IReadOnlyList<SpellTargetObject>? RunList = null);

/// <summary>
/// What the target functions ask of the world, for any map (the host fills it from the active map's objects): objects by id;
/// the objects in the square from <c>centre − radius</c> to <c>centre + radius</c> whose type bit (<c>1 &lt;&lt; type</c>) is in the
/// mask, in the original's query order (0x440B40: sector rows, sectors from the left, the object that entered a sector last
/// first; ledger TB13); the objects on a tile with such a mask (0x4407C0); whether a tile is blocked (0x4D7110 with flag 0);
/// whether the source has a path of at most 200 steps to a tile (0x41F3C0); the indoor/outdoor kind of a location's sector
/// (0x4D70B0, 0x5038C0); the direction 0..7 from one location to another (0x4B8D50, 5 for the same tile); a source's party
/// and followers (0x4413E0).
/// </summary>
public interface ISpellTargetWorld
{
    SpellTargetObject? Object(string id);

    IReadOnlyList<SpellTargetObject> ObjectsInSquare(SpellLocation centre, int radius, int typeMask);

    IReadOnlyList<SpellTargetObject> ObjectsAt(SpellLocation tile, int typeMask);

    bool TileBlocked(SpellLocation tile);

    bool PathExists(SpellTargetObject source, SpellLocation tile);

    int SectorKind(SpellLocation location);

    int Direction(SpellLocation from, SpellLocation to);

    IReadOnlyList<SpellTargetObject> Party(SpellTargetObject source);
}

/// <summary>The area test's answer and the failure reason it leaves for the targeting cursor (0x603D40: <c>0x100000</c> "not
/// damaged", <c>0x400 &lt;&lt; 32</c> "damaged"; 0 otherwise).</summary>
public readonly record struct SpellTargetTest(bool Passes, ulong Reason = 0);

/// <summary>One gathered target: an object (its tile none) or a tile (no object).</summary>
public readonly record struct SpellTargetEntry(SpellTargetObject? Object, SpellLocation Tile);

/// <summary>A gathered list in the original's order, and the run object list an action with <c>Tgt_List</c> built (the
/// gathered objects in reverse order), or null.</summary>
public sealed record SpellTargetCollection(IReadOnlyList<SpellTargetEntry> Entries, IReadOnlyList<SpellTargetObject>? BuiltRunList);

/// <summary>
/// ARC-MAGIC-007 (magic plan MT8): the original's target areas — the area test (0x4F2D20), the target collection (0x4F40B0) with
/// its list helpers (0x4F3F10, 0x4F3FD0, 0x4F4050, 0x4F2C60), a component's filter and re-aim in the run's per-target stage
/// (0x4510F0, 0x453D40) and the AI notice (0x453EE0, 0x453F20), as the 1.0.7.4 executable has them (contract
/// docs/world/spell-target-areas.md, ledger "Spell target areas", TA1–TA16). Pure functions over a context and a world port.
/// </summary>
public static class SpellTargetRules
{
    /// <summary>The original's target list holds 256 entries (0x5E3518; the invocation check's own list is as large) and its
    /// helpers do not check the bound; here further entries are dropped, in both modes (MG24, no-crash rule).</summary>
    public const int Capacity = 256;
    public const ulong NotInvulnerable = 0x2000UL << 32;

    /// <summary>
    /// The area test (0x4F2D20): whether the candidate object (or, without one, the candidate tile) satisfies the area. A mask
    /// with a self, object or radius bit (low 0x15) tests the candidate object; one without a candidate object passes to the
    /// tile tests only with the tile or list bit. The tile tests run with the tile bit (high 0x8000); a mask without it passes.
    /// The critter sub-tests run only with the critter bit (0x40); the not-dead (0x80000000) and non-party (high 0x10000000)
    /// tests also run without it (the not-dead test for any object, the non-party test for PCs and NPCs). MG25: the summoned
    /// test reads the spell flags (Steamwoven) or, as the original, the flags word the test read last (the critter flags with
    /// the critter bit, a door's or chest's flags after a lock test).
    /// </summary>
    [OriginalFunction(0x4F2D20, OriginalFunctionScope.Partial)]
    public static SpellTargetTest Test(SpellTargetContext context, ISpellTargetWorld world, MagicAnomalyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(profile);
        var mask = context.Area.Targets;
        if (mask == 0) return new(true);
        var lo = (uint)mask;
        var hi = (uint)(mask >> 32);
        if ((lo & 0x15) != 0)
        {
            if (context.Candidate is { } candidate) return TestObject(context, candidate, lo, hi, world, profile);
            if ((hi & 0x80008000) == 0) return new(false);
        }
        return new(TestTile(context, hi, world));
    }

    private static SpellTargetTest TestObject(SpellTargetContext c, SpellTargetObject o, uint lo, uint hi, ISpellTargetWorld world, MagicAnomalyProfile profile)
    {
        var fail = new SpellTargetTest(false);
        var type = o.Type;
        if ((o.ObjectFlags & (SpellTargetObject.Destroyed | SpellTargetObject.Off)) != 0 && !(o.IsCritter && (o.CritterFlags2 & SpellTargetObject.SafeOff) != 0))
            return fail;
        if ((o.ObjectFlags & SpellTargetObject.DontDraw) != 0 && !(o.IsCritter && (o.CritterFlags & SpellTargetObject.Sleeping) != 0)) return fail;
        var flags = o.SpellFlags;
        if ((flags & (int)c.Area.RequiredSpellFlags) != (int)c.Area.RequiredSpellFlags || (flags & (int)c.Area.ExcludedSpellFlags) != 0) return fail;
        var noSelf = (lo & 0x10000000) != 0;
        if ((lo & 0x9) != 0)
        {
            if (!noSelf && !Same(o, c.Self)) return fail;
        }
        else if (noSelf && (Same(o, c.Self) || Same(o, c.Source))) return fail;
        if ((lo & 0x2) != 0 && !noSelf && !Same(o, c.Source)) return fail;
        var critter = (lo & 0x40) != 0;
        if (critter)
        {
            if (!o.IsCritter) return fail;
            if ((lo & 0x20) != 0 && type != SpellTargetObject.Pc) return fail;
            if ((lo & 0x20000000) != 0 && type == SpellTargetObject.Pc) return fail;
            if ((lo & 0x100) != 0) { if (!o.IsDead) return fail; }
            else if ((lo & 0x80000000) != 0 && o.IsDead) return fail;
            var cf = flags = o.CritterFlags;
            if ((lo & 0x80) != 0) { if ((cf & SpellTargetObject.Animal) == 0) return fail; }
            else if ((lo & 0x40000000) != 0 && (cf & SpellTargetObject.Animal) != 0) return fail;
            if ((lo & 0x200) != 0 && (cf & SpellTargetObject.Undead) == 0) return fail;
            if ((lo & 0x400) != 0 && (cf & SpellTargetObject.Demon) == 0) return fail;
            if ((lo & 0x800) != 0 && (cf & SpellTargetObject.Mechanical) == 0) return fail;
            if ((lo & 0x1000) != 0) { if (o.Alignment < 0) return fail; }
            else if ((lo & 0x2000) != 0 && o.Alignment >= 0) return fail;
            if ((lo & 0x4000) != 0 && (cf & SpellTargetObject.Unrevivifiable) == 0) return fail;
            if ((lo & 0x8000) != 0 && (cf & SpellTargetObject.Unresurrectable) == 0) return fail;
            if ((hi & 0x1) != 0 && (cf & SpellTargetObject.Undead) != 0) return fail;
            if ((hi & 0x2) != 0 && (cf & SpellTargetObject.Demon) != 0) return fail;
            if ((hi & 0x4) != 0 && (cf & SpellTargetObject.Mechanical) != 0) return fail;
            if ((hi & 0x8) != 0) { if (o.Alignment >= 0) return fail; }
            else if ((hi & 0x10) != 0 && o.Alignment < 0) return fail;
            if ((hi & 0x20) != 0 && (cf & SpellTargetObject.Unrevivifiable) != 0) return fail;
            if ((hi & 0x40) != 0 && (cf & SpellTargetObject.Unresurrectable) != 0) return fail;
            if ((lo & 0x400000) != 0 && o.Poison <= 0) return fail;
            if ((hi & 0xC000000) != 0) { if (o.PcLeader is null && type != SpellTargetObject.Pc) return fail; }
            else if ((hi & 0x10000000) != 0 && (InParty(o, c.Self) || InParty(o, c.Other) || Same(o, c.Other))) return fail;
        }
        else
        {
            if ((lo & 0x10000) != 0) { if (type != SpellTargetObject.Portal && ((lo & 0x20000) == 0 || type != SpellTargetObject.Container)) return fail; }
            else if ((lo & 0x20000) != 0) { if (type != SpellTargetObject.Container) return fail; }
            else if ((lo & 0x80000) != 0) { if (type != 0) return fail; }
            else if ((hi & 0x200) != 0 && type == 0) return fail;
            if ((lo & 0x80000000) != 0 && o.IsDead) return fail;
            if ((hi & 0x10000000) != 0 && o.IsCritter && (InParty(o, c.Self) || InParty(o, c.Other) || Same(o, c.Other))) return fail;
        }
        if ((lo & 0x100000) != 0)
        {
            if (o.HitPointDamage <= 0 && (!o.IsCritter || (o.CritterFlags & SpellTargetObject.Injured) == 0)) return new(false, 0x100000);
        }
        else if ((hi & 0x400) != 0 && (o.HitPointDamage > 0 || (o.IsCritter && (o.CritterFlags & SpellTargetObject.Injured) != 0)))
            return new(false, 0x400UL << 32);
        if ((lo & 0x40000) != 0) { if (LockFlags(o, ref flags) is { } f && (f & SpellTargetObject.Locked) == 0) return fail; }
        else if ((hi & 0x80) != 0 && LockFlags(o, ref flags) is { } f && (f & SpellTargetObject.Locked) != 0) return fail;
        if ((hi & 0x100) != 0 && LockFlags(o, ref flags) is { } held && (held & SpellTargetObject.MagicallyHeld) != 0) return fail;
        if ((lo & 0x200000) != 0 && o.HitPointDamage <= 0 && (!o.IsCritter || o.Poison <= 0)) return fail;
        if ((lo & 0x1000000) != 0 && o.Material != SpellTargetObject.Flesh) return fail;
        if ((lo & 0x10) != 0)
        {
            var origin = c.TargetTile;
            if (origin.IsNone)
            {
                if (c.Source is null) return fail;
                origin = c.Source.Location;
            }
            if (SpellLocation.Distance(origin, o.Location) > c.Area.Radius) return fail;
        }
        var summoned = profile.SummonedTestReadsLastFlags ? flags : o.SpellFlags;
        if ((hi & 0x4000) != 0 && (summoned & SpellTargetObject.Summoned) == 0) return fail;
        if ((lo & 0x2000000) != 0) { if ((o.ObjectFlags & SpellTargetObject.Inventory) == 0) return fail; }
        else if ((hi & 0x1000) != 0 && (o.ObjectFlags & SpellTargetObject.Inventory) != 0) return fail;
        if ((hi & 0x2000) != 0 && (o.ObjectFlags & SpellTargetObject.Invulnerable) != 0) return fail;
        if ((lo & 0x4000000) != 0 && (!o.IsItem || o.Weight >= 5)) return fail;
        return new(true);
    }

    // A door's or chest's lock flags, which the original reads into its flags word; other objects have none.
    private static int? LockFlags(SpellTargetObject o, ref int flags) => o.Type switch
    {
        SpellTargetObject.Portal => flags = o.PortalFlags,
        SpellTargetObject.Container => flags = o.ContainerFlags,
        _ => null,
    };

    // The non-party test against one object of the context: the candidate's PC leader is that object or its PC leader, or the
    // candidate is that object's PC leader; against the other object (+0x40) the candidate must not be that object either (the
    // multiplayer party list is not modelled: single player).
    private static bool InParty(SpellTargetObject o, SpellTargetObject? by) =>
        by is not null && (o.PcLeader == by.Id || (o.PcLeader is not null && o.PcLeader == by.PcLeader) || (by.PcLeader is not null && o.Id == by.PcLeader));

    private static bool TestTile(SpellTargetContext c, uint hi, ISpellTargetWorld world)
    {
        if ((hi & 0x8000) == 0) return true;
        var tile = c.CandidateTile;
        if (tile.IsNone) return (hi & 0x80000000) != 0;
        if ((hi & 0x10000) != 0 && (c.Source is null || tile != (c.Candidate?.Location ?? SpellLocation.None))) return false;
        if ((hi & 0x20000) != 0 && c.Source is not null && !world.PathExists(c.Source, tile)) return false;
        if ((hi & 0x40000) != 0 && !Empty(tile, 0x3FFEF, world)) return false;
        if ((hi & 0x80000) != 0 && !Empty(tile, 0x2000B, world)) return false;
        if ((hi & 0x200000) != 0 && c.Area.Radius < 0) return false;
        return (hi & 0x1000000) == 0 || c.Origin.IsNone || world.SectorKind(c.Origin) == world.SectorKind(tile);
    }

    private static bool Empty(SpellLocation tile, int typeMask, ISpellTargetWorld world) =>
        !world.TileBlocked(tile) && world.ObjectsAt(tile, typeMask).All(o => o.IsItem || (o.ObjectFlags & SpellTargetObject.NoBlock) != 0);

    /// <summary>
    /// The target collection (0x4F40B0): the list an action's area gathers, in the original's order — self and source (unless
    /// no-self), the target object, the last summoned and the run's summoned list, the target tile, the source's tile, objects in
    /// a radius, tiles in a radius, the wall line, the candidate's tile, the "cone" (a square of the radius around the target
    /// tile or the source: objects with the self bit, else tiles), the run's object list, the source's party, an item source's
    /// owner. Objects and tiles the radius, wall and cone branches find must pass <see cref="Test"/>; the count limits those
    /// branches. Duplicates are skipped; at most <see cref="Capacity"/> entries (MG24).
    /// </summary>
    [OriginalFunction(0x4F40B0, OriginalFunctionScope.Partial)]
    public static SpellTargetCollection Collect(SpellTargetContext context, ISpellTargetWorld world, MagicAnomalyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(profile);
        var list = new List<SpellTargetEntry>();
        var area = context.Area;
        var lo = (uint)area.Targets;
        var hi = (uint)(area.Targets >> 32);
        var noSelf = (lo & 0x10000000) != 0;
        if ((lo & 0x1) != 0 && !noSelf) AddChecked(list, context.Self);
        if ((lo & 0x2) != 0 && !noSelf) AddChecked(list, context.Source);
        if ((lo & 0x4) != 0) Add(list, context.Target);
        if ((hi & 0x4000) != 0)
        {
            Add(list, context.Summoned);
            foreach (var summoned in context.SummonedList ?? []) Add(list, summoned);
        }
        if ((hi & 0x8000) != 0 && !context.TargetTile.IsNone) AddTile(list, context.TargetTile);
        if ((hi & 0x10000) != 0 && context.Source is not null) AddTile(list, context.Source.Location);
        var gather = new SpellTargetContext(area with { Targets = area.Targets & ~0x4UL }, context.Source, context.Source, context.Source?.Location ?? default);
        if ((lo & 0x10) != 0) GatherObjectsInRadius(list, context, gather, world, profile);
        if ((hi & 0x200000) != 0) GatherTiles(list, context, gather with { Area = gather.Area with { RequiredSpellFlags = 0, ExcludedSpellFlags = 0 } }, world, profile);
        var lineAndCone = gather with { Area = gather.Area with { RequiredSpellFlags = 0, ExcludedSpellFlags = 0 }, Other = context.Other };
        if ((hi & 0x400000) != 0) GatherWall(list, context, lineAndCone, world, profile);
        if ((hi & 0x800000) != 0 && context.Candidate is not null) AddTile(list, context.Candidate.Location);
        if ((hi & 0x2000000) != 0 && context.Source is not null) GatherCone(list, context, lineAndCone, world, profile);
        IReadOnlyList<SpellTargetObject>? built = null;
        if ((hi & 0x80000000) != 0 && context.RunList is { } runList)
        {
            if (runList.Count > 0) foreach (var o in runList) Add(list, o);
            else built = list.Where(e => e.Object is not null).Select(e => e.Object!).Reverse().ToArray();
        }
        if ((hi & 0x4000000) != 0 && context.Source is not null) foreach (var member in world.Party(context.Source)) Add(list, member);
        if ((hi & 0x20000000) != 0 && context.Source is { IsItem: true, Owner: { } owner }) Add(list, world.Object(owner));
        return new(list, built);
    }

    // 0x4F2C60: a scenery object with an occupant stands for the occupant (as an NPC); otherwise the object's own type. An occupant
    // the world does not know leaves the scenery object (the original takes the occupant's handle whatever it is).
    [OriginalFunction(0x4F2C60, OriginalFunctionScope.Partial)]
    private static (SpellTargetObject Object, int Kind) Visible(SpellTargetObject o, ISpellTargetWorld world) =>
        o.Type == SpellTargetObject.Scenery && o.Occupant is { } id && world.Object(id) is { } occupant ? (occupant, SpellTargetObject.Npc) : (o, o.Type);

    // The types a radius or cone gather asks for; every type but projectiles when no type bit is named and one of the given
    // object bits is set (the radius branch tests the object and radius bits, 0x14; the cone only the object bit, 0x4).
    private static (int Mask, bool AllTypes) TypeMask(uint lo, uint objectBits)
    {
        var mask = (lo & 0x40) != 0 ? 0x18008 : 0x8;
        if ((lo & 0x10000) != 0) mask |= 0x2;
        if ((lo & 0x20000) != 0) mask |= 0x4;
        if ((lo & 0x80000) != 0) mask |= 0x1;
        return mask == 0x8 && (lo & objectBits) != 0 ? (0x3FFEF, true) : (mask, false);
    }

    private static void GatherObjectsInRadius(List<SpellTargetEntry> list, SpellTargetContext c, SpellTargetContext gather, ISpellTargetWorld world, MagicAnomalyProfile profile)
    {
        var area = c.Area;
        var test = gather with { Area = gather.Area with { Targets = area.Targets & ~(0x8000UL << 32) }, TargetTile = c.TargetTile, Other = c.Other };
        var centre = c.Target is not null ? c.Target.Location : c.TargetTile;
        var (mask, all) = TypeMask((uint)area.Targets, 0x14);
        var count = area.Count;
        foreach (var found in world.ObjectsInSquare(centre, area.Radius, mask))
        {
            var (o, kind) = Visible(found, world);
            if (kind == SpellTargetObject.Scenery && !all) continue;
            if (!Test(test with { Candidate = o }, world, profile).Passes) continue;
            AddChecked(list, o);
            if (count > 0 && --count == 0) return;
        }
    }

    // The tile radius's and the wall's centre: the target tile, else the target object's location; with neither the original
    // reads a location through handle 0 and here the branch gathers nothing (MG26, both modes).
    private static SpellLocation Centre(SpellTargetContext c) => !c.TargetTile.IsNone ? c.TargetTile : c.Target?.Location ?? SpellLocation.None;

    // The original packs a tile from a 64-bit x and the low 32 bits of a 64-bit y: an x below 0 or past 2^32 spills into y.
    private static SpellLocation Tile(SpellLocation centre, long dx, long dy)
    {
        var packed = ((ulong)(uint)(centre.Y + dy) << 32) | (ulong)(centre.X + dx);
        return new((uint)packed, (uint)(packed >> 32));
    }

    private static void GatherTiles(List<SpellTargetEntry> list, SpellTargetContext c, SpellTargetContext test, ISpellTargetWorld world, MagicAnomalyProfile profile)
    {
        var centre = Centre(c);
        if (centre.IsNone) return;
        var r = c.Area.Radius;
        var count = c.Area.Count;
        for (var y = -r; y <= r; y++)
        {
            for (var x = -r; x <= r; x++)
            {
                var tile = Tile(centre, x, y);
                if (!Test(test with { CandidateTile = tile }, world, profile).Passes) continue;
                AddTile(list, tile);
                if (count > 0 && --count == 0) break;
            }
            if (c.Area.Count > 0 && count == 0) return;
        }
    }

    private static readonly (int X, int Y)[] Steps = [(1, -1), (0, -1), (-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0)];

    private static void GatherWall(List<SpellTargetEntry> list, SpellTargetContext c, SpellTargetContext test, ISpellTargetWorld world, MagicAnomalyProfile profile)
    {
        var centre = Centre(c);
        if (centre.IsNone) return;
        var direction = !c.Origin.IsNone && !c.TargetTile.IsNone ? world.Direction(c.Origin, c.TargetTile) : 0;
        var (dx, dy) = direction is >= 0 and <= 7 ? Steps[direction] : Steps[0];
        var count = c.Area.Count;
        for (var i = 0; i <= c.Area.Radius; i++)
        {
            foreach (var tile in new[] { Tile(centre, (long)i * dx, (long)i * dy), Tile(centre, -(long)i * dx, -(long)i * dy) })
            {
                if (!Test(test with { CandidateTile = tile }, world, profile).Passes) continue;
                AddTile(list, tile);
                if (count > 0 && --count == 0) return;
            }
        }
    }

    private static void GatherCone(List<SpellTargetEntry> list, SpellTargetContext c, SpellTargetContext test, ISpellTargetWorld world, MagicAnomalyProfile profile)
    {
        var centre = !c.TargetTile.IsNone ? c.TargetTile : c.Source!.Location;
        var r = c.Area.Radius;
        var lo = (uint)c.Area.Targets;
        if ((lo & 0x1) == 0)
        {
            for (var y = -r; y <= r; y++)
                for (var x = -r; x <= r; x++)
                {
                    var tile = Tile(centre, x, y);
                    if (Test(test with { CandidateTile = tile }, world, profile).Passes) AddTile(list, tile);
                }
            return;
        }
        var (mask, all) = TypeMask(lo, 0x4);
        var count = c.Area.Count;
        for (var y = -r; y <= r; y++)
        {
            for (var x = -r; x <= r; x++)
            {
                foreach (var found in world.ObjectsAt(Tile(centre, x, y), mask))
                {
                    var (o, kind) = Visible(found, world);
                    if (kind == SpellTargetObject.Scenery && !all) continue;
                    if (!Test(test with { Candidate = o }, world, profile).Passes) continue;
                    AddChecked(list, o);
                    if (count > 0 && --count == 0) break;
                }
                if (c.Area.Count > 0 && count == 0) break;
            }
            if (c.Area.Count > 0 && count == 0) return;
        }
    }

    // 0x4F3F10: an object, unless destroyed or switched off (a critter that is "safe off" still counts), once.
    [OriginalFunction(0x4F3F10, OriginalFunctionScope.Partial)]
    private static void AddChecked(List<SpellTargetEntry> list, SpellTargetObject? o)
    {
        if (o is null) return;
        if ((o.ObjectFlags & (SpellTargetObject.Destroyed | SpellTargetObject.Off)) != 0 && !(o.IsCritter && (o.CritterFlags2 & SpellTargetObject.SafeOff) != 0)) return;
        AddObjectEntry(list, o);
    }

    // 0x4F3FD0: an object, unless destroyed, once.
    [OriginalFunction(0x4F3FD0, OriginalFunctionScope.Partial)]
    private static void Add(List<SpellTargetEntry> list, SpellTargetObject? o)
    {
        if (o is null || (o.ObjectFlags & SpellTargetObject.Destroyed) != 0) return;
        AddObjectEntry(list, o);
    }

    private static void AddObjectEntry(List<SpellTargetEntry> list, SpellTargetObject o)
    {
        if (list.Count >= Capacity || list.Any(e => e.Object?.Id == o.Id)) return;
        list.Add(new(o, SpellLocation.None));
    }

    // 0x4F4050: a tile, unless an entry already has that location (an object entry's location is none).
    [OriginalFunction(0x4F4050, OriginalFunctionScope.Partial)]
    private static void AddTile(List<SpellTargetEntry> list, SpellLocation tile)
    {
        if (list.Count >= Capacity || list.Any(e => e.Tile == tile)) return;
        list.Add(new(null, tile));
    }

    /// <summary>
    /// The item-trigger gate of a component in the run's per-target stage (0x4510F0). The stage runs, per target and component:
    /// the component's own area test (<see cref="Test"/> against the target and its tile; a failed component is skipped); the
    /// re-aim (<see cref="ReAim"/>, which therefore happens even when this gate then refuses, and stays for the later
    /// components); this gate — when the record has item triggers and the component names some, they must meet the run's (a
    /// component with trigger 0x100 needs the run's 0x100); then the AI notice (<see cref="AiNotices"/>) and the component itself.
    /// </summary>
    [OriginalFunction(0x4510F0, OriginalFunctionScope.Partial)]
    public static bool TriggersAllow(bool recordHasTriggers, int componentTriggers, int runTriggers) =>
        !recordHasTriggers || componentTriggers == 0 || ((componentTriggers & runTriggers) != 0 && ((componentTriggers & 0x100) == 0 || (runTriggers & 0x100) != 0));

    /// <summary>
    /// A component's re-aim (0x453D40, its <c>Apply_AoE</c> mask): self makes the run's parent the target, source the run's source,
    /// summoned the last summoned object (each even when absent); then the tile bit sets the target tile — with the candidate
    /// tile bit ("offscreen", high 0x800000) to the current target's location when there is a target, otherwise to the run's
    /// target tile, or else its target object's location. Without a mask nothing changes. The original also caches the new target's
    /// type (0x5E75AC); the caller reads it from the target.
    /// </summary>
    [OriginalFunction(0x453D40, OriginalFunctionScope.Partial)]
    public static (SpellTargetObject? Target, SpellLocation Tile) ReAim(ulong apply, SpellTargetObject? target, SpellLocation tile, SpellTargetObject? parent,
        SpellTargetObject? source, SpellTargetObject? lastSummoned, SpellLocation runTile, SpellTargetObject? runTarget)
    {
        if (apply == 0) return (target, tile);
        if ((apply & 0x1) != 0) target = parent;
        if ((apply & 0x2) != 0) target = source;
        var hi = (uint)(apply >> 32);
        if ((hi & 0x4000) != 0) target = lastSummoned;
        if ((hi & 0x8000) != 0)
        {
            if ((hi & 0x800000) != 0) { if (target is not null) tile = target.Location; }
            else tile = !runTile.IsNone ? runTile : runTarget?.Location ?? SpellLocation.None;
        }
        return (target, tile);
    }

    /// <summary>
    /// The AI notice of an aggressive spell (0x453EE0, 0x453F20): for an aggressive record, a target and an action other than
    /// END, the target is told that the run's parent attacked it — once per target in a row (the original remembers the last one)
    /// — and that the run's other object (+0xE8) did, when there is one other than the parent. Answers the (attacker, target)
    /// pairs to tell and the target to remember.
    /// </summary>
    [OriginalFunction(0x453EE0, OriginalFunctionScope.Full)]
    [OriginalFunction(0x453F20, OriginalFunctionScope.Full)]
    public static (IReadOnlyList<(string? Attacker, string Target)> Notices, string? Remembered) AiNotices(bool aggressive, int action, string? target,
        string? parent, string? other, string? lastNotified)
    {
        if (!aggressive || target is null || action == 2 || target == lastNotified) return ([], lastNotified);
        var notices = new List<(string?, string)> { (parent, target) };
        if (other is not null && other != parent) notices.Add((other, target));
        return (notices, target);
    }

    private static bool Same(SpellTargetObject a, SpellTargetObject? b) => b is not null && a.Id == b.Id;
}
