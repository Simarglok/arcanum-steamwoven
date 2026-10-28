using Arcanum.Simulation.Movement;

namespace Arcanum.Simulation.Combat;

/// <summary>
/// ARC-COMBAT-016: a projectile in flight (proto 5028 of the ranged path 0x4B39B0, ledger RH3 and PF1–PF12): its line between two
/// tiles' screen centres in 10-pixel steps, replayed from the start when the steps run out, with its pixel position, the tile it is
/// on and the steps it took (its art frame advances once per step). Immutable; <see cref="ProjectileRules.Step"/> moves it.
/// </summary>
/// <param name="Start">The tile the flight left from (the attacker's tile at the shot).</param>
/// <param name="Destination">The tile it flies at (the target's tile at the shot, also after a line-of-fire redirect).</param>
/// <param name="Samples">The line's 10-pixel steps (<see cref="LineOfFireRules.Samples"/>), at least one.</param>
/// <param name="Next">The index of the next step in <see cref="Samples"/>.</param>
/// <param name="Tile">The tile the projectile is on.</param>
/// <param name="X">The projectile's screen x relative to the start tile's centre.</param>
/// <param name="Y">The projectile's screen y relative to the start tile's centre.</param>
/// <param name="Steps">The steps taken.</param>
public sealed record ProjectileFlight(CellCoord Start, CellCoord Destination, IReadOnlyList<(int Dx, int Dy)> Samples, int Next, CellCoord Tile,
    long X, long Y, long Steps);

/// <summary>One tile change of a flight: the tile left, the tile entered and the tile distance from the start to the tile left
/// (0x4B96F0), which the range test reads.</summary>
public readonly record struct ProjectileCrossing(CellCoord Left, CellCoord Entered, int Distance);

/// <summary>What a projectile does when it leaves a tile (<see cref="ProjectileRules.Cross{TObject}"/>).</summary>
public enum ProjectileStrikeKind
{
    /// <summary>Nothing: it flies on.</summary>
    None,
    /// <summary>A shot that hit strikes its target, which stands on the tile left: the hit decided at the shot resolves now.</summary>
    Target,
    /// <summary>A shot that missed strikes another living critter on the tile left (1 in 3 each): a forced hit without a skill roll
    /// or a critical, at hit location 0 (the torso).</summary>
    Stray,
    /// <summary>A missed shot past its destination meets an object on the step to the next tile: a forced hit, as a stray.</summary>
    Obstruction,
    /// <summary>Nothing was struck and the tile left lies beyond the range from the start: the flight ends.</summary>
    RangeEnd,
    /// <summary>ARC-COMBAT-018: a shot without a target (a missed throw's scatter) leaves its destination: the flight ends there,
    /// before anything on the tile is struck (0x4B2870). ARC-COMBAT-019: so does a returning boomerang leaving the tile it flies
    /// back to.</summary>
    DestinationEnd,
}

/// <summary>A shot in flight as the original's projectile carries it (field 0x4D): whether it hit (flag 0x2) and whether it flies free
/// (flag 0x20: a missed shot that passed its destination, which then meets obstructions), and the range that ends it.
/// ARC-COMBAT-019: <see cref="Returning"/> is flag 0x2000, a boomerang flying back to its thrower (<see cref="ProjectileRules.TurnBack"/>).</summary>
public readonly record struct ProjectileShot(bool Hit, bool FreeFlight, int Range, bool Returning = false);

/// <summary>What leaving a tile did: the shot as it flies on (a missed shot leaving its destination flies free), what it struck
/// (the target, a stray critter or an obstruction, or the end of its range) and the struck object (null for none or the range end).</summary>
public readonly record struct ProjectileCrossingResult<TObject>(ProjectileShot Shot, ProjectileStrikeKind Strike, TObject? Struck) where TObject : class
{
    /// <summary>The flight ends: something was struck or the range ran out.</summary>
    public bool Ends => Strike != ProjectileStrikeKind.None;
}

/// <summary>
/// The map a projectile crosses (the host's): where the shot's target stands now, the living PCs and NPCs on a tile in the original's
/// list order (0x4407C0 with mask 0x18000, the dead skipped), and the object that stops one step of a free flight (the step cost
/// 0x43FDC0 with the projectile flags; null when nothing does).
/// </summary>
public interface IProjectileMap<TObject> where TObject : class
{
    /// <summary>The target's tile now, or null when it is gone.</summary>
    CellCoord? TargetTile { get; }

    /// <summary>The living PCs and NPCs on <paramref name="tile"/>, in the original's list order.</summary>
    IEnumerable<TObject> LivingCritters(CellCoord tile);

    /// <summary>The object that stops the step from <paramref name="from"/> in rotation 0..7 (<see cref="LineOfFireRules.Rotation"/>),
    /// or null.</summary>
    TObject? Obstruction(CellCoord from, int rotation);
}

/// <summary>
/// ARC-COMBAT-016: the original's projectile (read in the retail 1.0.7.4 executable by two readers, ledger PF1–PF12): the flight's
/// line and timing (the flight goal 0x24, tick 0x42F6A0), its facing (0x4B6A00, 0x4B6B10) and what it does on each tile it leaves
/// (0x4B2870). Pure functions; the host draws the flight, rolls the stray strikes and resolves what is struck.
/// </summary>
public static class ProjectileRules
{
    /// <summary>The flight's tick: the goal reschedules itself every 35 ms (delay code −3, the global 0x5DE6CC it sets).</summary>
    public const int TickMs = 35;
    /// <summary>The steps per tick when the weapon's eye-candy record gives none (goal field +0x98 is 0).</summary>
    public const int DefaultStepsPerTick = 4;
    /// <summary>The screen directions of a projectile's facing (0x4B6A00).</summary>
    public const int Directions = 32;
    /// <summary>The stray strike's die: a critter on the tile is struck when it rolls 1 (0x4B2870, random(1, 3) == 1).</summary>
    public const int StrayDie = 3;
    /// <summary>ARC-COMBAT-018: the projectiles of a bow shot at expert training or more (0x4B39B0).</summary>
    public const int BowExpertProjectiles = 2;

    /// <summary>
    /// ARC-COMBAT-018: how many projectiles a shot that flies leaves with (the ranged path 0x4B39B0, ledger PF2 and PS rows): two when
    /// the attack's skill is the bow and the attacker's bow training (0x4C60C0) is expert or more, else one. Both fly the same line from
    /// the attacker's tile (the offsets the path passes them, 0 and 10 of the table 0x5B57FC, are never read), carry the same decided
    /// outcome and each resolves its own strike; the ammunition was spent once, at the shot. A shot without a projectile art resolves
    /// at once and has none; the missed throw's scatter of the same path is <see cref="ThrowRules.ScatterTiles"/>.
    /// </summary>
    [OriginalFunction(0x4B39B0, OriginalFunctionScope.Partial)]
    public static int Projectiles(CombatSkill skill, SkillTraining bowTraining)
    {
        if (!Enum.IsDefined(skill)) throw new ArgumentOutOfRangeException(nameof(skill));
        if (!Enum.IsDefined(bowTraining)) throw new ArgumentOutOfRangeException(nameof(bowTraining));
        return skill == CombatSkill.Bow && bowTraining >= SkillTraining.Expert ? BowExpertProjectiles : 1;
    }

    /// <summary>
    /// 0x4B6A00: the screen direction 0..31 from one tile to another, 0 up the screen and counting clockwise: the angle of the screen
    /// delta between the two tiles (0x4B8680) in [0, 2π), times 32 / 2π truncated (a value outside 0..31 counts as 0), plus 8, modulo
    /// 32. The same tile is 0.
    /// </summary>
    [OriginalFunction(0x4B6A00, OriginalFunctionScope.Full)]
    public static int Direction(CellCoord from, CellCoord to)
    {
        if (from == to) return 0;
        var (fx, fy) = LineOfFireRules.Screen(from);
        var (tx, ty) = LineOfFireRules.Screen(to);
        var angle = Math.Atan2(ty - fy, tx - fx);
        if (angle < 0) angle += 2 * Math.PI;
        var sector = (int)(angle * 32.0 * (1 / (2 * Math.PI)));
        if (sector is < 0 or >= Directions) sector = 0;
        return (sector + 8) % Directions;
    }

    /// <summary>
    /// 0x4B6B10 for a scenery art (class 4, the class of every installed projectile art, combat census): the art faced in
    /// <paramref name="direction"/> 0..31 takes rotation direction mod 8 (bits 11..13) and its number (bits 19..27) raised by
    /// direction / 8, so a projectile has four consecutive scenery numbers of eight rotations each; a number past 511 leaves the
    /// number as it was (the original logs it). ARC-MAGIC-018: an eye-candy art (class 14, a spell's missile) the same, with its
    /// rotation in bits 9..11 (the rotation setter 0x502930's class 14 branch). Null for an art of another class (an item art takes
    /// rotation direction / 4; the rotation setter's other classes and its single-rotation test 0x504790 are not read).
    /// </summary>
    [OriginalFunction(0x4B6B10, OriginalFunctionScope.Partial)]
    public static uint? FacedArt(uint art, int direction)
    {
        if (direction is < 0 or >= Directions) throw new ArgumentOutOfRangeException(nameof(direction));
        var shift = (art >> 28) switch { SceneryArtClass => 11, EyeCandyArtClass => 9, _ => -1 };
        if (shift < 0) return null;
        var number = ((art >> 19) & 0x1FF) + (uint)(direction / 8);
        if (number < 0x200) art = (art & ~(0x1FFU << 19)) | (number << 19);
        return (art & ~(7U << shift)) | ((uint)(direction % 8) << shift);
    }

    /// <summary>The art class of a scenery art (id &gt;&gt; 28).</summary>
    public const uint SceneryArtClass = 4;
    /// <summary>ARC-MAGIC-018: the art class of an eye-candy art (a spell's missile).</summary>
    public const uint EyeCandyArtClass = 14;

    /// <summary>
    /// ARC-COMBAT-019 (the projectile's end 0x4B2690 with attack flag 0x1000, the turn 0x435A00, ledger BR rows): a boomerang's
    /// projectile at its first end (a strike, the end of its range, or the destination of a shot without a target) turns back: its
    /// flight goal flies on from the tile it is on (<paramref name="flight"/>'s tile, the one being left) to
    /// <paramref name="thrower"/>, the thrower's tile at the turn, on a new line (PF3's), and the shot returns (flag 0x2000) with its
    /// outcome, range and facing kept. Null when the two tiles are the same or no line is built: the boomerang is back at once
    /// (Steamwoven; the original's flight with a line of no step was not read).
    /// </summary>
    [OriginalFunction(0x435A00, OriginalFunctionScope.Partial)]
    public static (ProjectileFlight? Flight, ProjectileShot Shot) TurnBack(ProjectileFlight flight, ProjectileShot shot, CellCoord thrower)
    {
        ArgumentNullException.ThrowIfNull(flight);
        return (Launch(flight.Tile, thrower), shot with { Returning = true });
    }

    /// <summary>
    /// The flight from <paramref name="start"/> to <paramref name="destination"/> (0x435870 and its path node): the pixel line between
    /// the two tiles' screen centres in 10-pixel steps, the projectile on the start tile's centre. Null when the two tiles are the same
    /// or the line cannot be built (more than <see cref="LineOfFireRules.MaxLinePixels"/> pixels on its major axis).
    /// </summary>
    public static ProjectileFlight? Launch(CellCoord start, CellCoord destination)
    {
        if (start == destination) return null;
        var from = LineOfFireRules.Screen(start);
        var to = LineOfFireRules.Screen(destination);
        if (LineOfFireRules.Samples((from.X + LineOfFireRules.HalfWidth, from.Y + LineOfFireRules.HalfHeight),
                (to.X + LineOfFireRules.HalfWidth, to.Y + LineOfFireRules.HalfHeight)) is not { Count: > 0 } samples) return null;
        return new(start, destination, samples, 0, start, 0, 0, 0);
    }

    /// <summary>
    /// One step of the flight tick (0x42F6A0): the frame advances, the next sample is added to the pixel position (after the last one
    /// the samples start again from the first, so the projectile keeps its line), and the tile under the point is found. A tile change
    /// is the crossing the host handles before the projectile moves on (<see cref="Cross{TObject}"/>); a point off the map ends the
    /// flight (null flight: the original's goal fails there).
    /// </summary>
    public static (ProjectileFlight? Flight, ProjectileCrossing? Crossing) Step(ProjectileFlight flight, Func<CellCoord, bool> contains)
    {
        ArgumentNullException.ThrowIfNull(flight);
        ArgumentNullException.ThrowIfNull(contains);
        var index = flight.Next < flight.Samples.Count ? flight.Next : 0;
        var (dx, dy) = flight.Samples[index];
        long x = flight.X + dx, y = flight.Y + dy;
        var start = LineOfFireRules.Screen(flight.Start);
        if (LineOfFireRules.Tile(start.X + LineOfFireRules.HalfWidth + x, start.Y + LineOfFireRules.HalfHeight + y, contains) is not { } tile)
            return (null, null);
        var next = flight with { Next = index + 1, X = x, Y = y, Tile = tile, Steps = flight.Steps + 1 };
        return tile == flight.Tile ? (next, null) : (next, new ProjectileCrossing(flight.Tile, tile, Distance(flight.Start, flight.Tile)));
    }

    /// <summary>The steps a flight takes in <paramref name="ticks"/> ticks of <see cref="TickMs"/>: <paramref name="stepsPerTick"/> each,
    /// <see cref="DefaultStepsPerTick"/> when it is 0 or less.</summary>
    public static long StepsIn(long ticks, int stepsPerTick) => checked(Math.Max(0, ticks) * (stepsPerTick > 0 ? stepsPerTick : DefaultStepsPerTick));

    /// <summary>
    /// What the projectile does when it leaves <see cref="ProjectileCrossing.Left"/> (0x4B2870, called before it moves on; the start
    /// tile counts as left on the first crossing, the last tile it enters is never left): leaving its destination, a missed shot flies
    /// free from then on (flag 0x20; this crossing still reads the shot as it was). A shot that hit strikes its target when the target
    /// stands on the tile left now (wherever along the line; a target that left the line is not struck, and past the destination the
    /// shot flies free as a miss). A shot that missed meets, when it flies free, the object stopping the step toward the entered tile
    /// (an obstruction), else each living critter on the tile left other than the target and the attacker in list order, struck when
    /// <paramref name="roll"/>(1, 3) is 1 (the first that rolls it; nothing more is rolled). When nothing was struck and the tile left
    /// lies more than the range from the start, the flight ends. ARC-COMBAT-018: a shot without a <paramref name="target"/> (a missed
    /// throw's scatter) is a miss flying free from the start (else <see cref="ArgumentException"/>) and ends when it leaves its
    /// destination, before any of these tests. ARC-COMBAT-019: a returning boomerang (<see cref="ProjectileShot.Returning"/>, flag
    /// 0x2000) strikes nothing, meets no obstruction and has no range: it ends when it leaves its destination (the tile it flies back
    /// to), whatever its target, and flies on through every other tile.
    /// </summary>
    [OriginalFunction(0x4B2870, OriginalFunctionScope.Partial)]
    public static ProjectileCrossingResult<TObject> Cross<TObject>(ProjectileShot shot, ProjectileCrossing crossing, CellCoord destination,
        TObject? target, TObject attacker, IProjectileMap<TObject> map, Func<int, int, int> roll) where TObject : class
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(roll);
        // ARC-COMBAT-019: flag 0x2000 ends the shot at its destination and skips every other test (0x4B2A45–0x4B2AA1).
        if (shot.Returning) return new(shot, crossing.Left == destination ? ProjectileStrikeKind.DestinationEnd : ProjectileStrikeKind.None, null);
        // ARC-COMBAT-018: a shot without a target (a missed throw's scatter cleared it and set flag 0x20, 0x4B39B0) flies free and
        // ends on leaving its destination.
        if (target is null && (shot.Hit || !shot.FreeFlight)) throw new ArgumentException("A shot without a target is a miss flying free.", nameof(shot));
        if (crossing.Left == destination && target is null) return new(shot, ProjectileStrikeKind.DestinationEnd, null);
        // The stored flags change when the destination is left; the rest of this crossing reads the shot as it was.
        var next = crossing.Left == destination && target is not null ? shot with { Hit = false, FreeFlight = true } : shot;
        if (shot.Hit)
        {
            if (target is not null && map.TargetTile == crossing.Left) return new(next, ProjectileStrikeKind.Target, target);
        }
        else
        {
            if (shot.FreeFlight && map.Obstruction(crossing.Left, LineOfFireRules.Rotation(crossing.Left, crossing.Entered)) is { } blocker)
                return new(next, ProjectileStrikeKind.Obstruction, blocker);
            foreach (var critter in map.LivingCritters(crossing.Left))
            {
                if (ReferenceEquals(critter, target) || ReferenceEquals(critter, attacker)) continue;
                var value = roll(1, StrayDie);
                if (value is < 1 or > StrayDie) throw new ArgumentOutOfRangeException(nameof(roll));
                if (value == 1) return new(next, ProjectileStrikeKind.Stray, critter);
            }
        }
        return crossing.Distance > shot.Range ? new(next, ProjectileStrikeKind.RangeEnd, null) : new(next, ProjectileStrikeKind.None, null);
    }

    /// <summary>The tile distance between two tiles (0x4B96F0): the larger coordinate difference.</summary>
    public static int Distance(CellCoord a, CellCoord b) => (int)Math.Min(int.MaxValue, Math.Max(Math.Abs((long)a.X - b.X), Math.Abs((long)a.Y - b.Y)));
}
