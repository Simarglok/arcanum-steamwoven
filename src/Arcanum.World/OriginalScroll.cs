using Arcanum.Simulation.Combat;

namespace Arcanum.World;

/// <summary>
/// ARC-UI-099 (MIG16 tail, SA44): the original's scroll direction codes and the screen-edge test, read in the retail 1.0.7.4 executable
/// (the main loop's edge routine 0x401F50, the arrow routine 0x402010 and the scroll function 0x40E0A0 they both call).
/// <para>
/// One scroll function takes a direction code 0 to 7: 0 up, 1 up and right, 2 right, 3 down and right, 4 down, 5 down and left, 6 left,
/// 7 up and left. The arrow keys give a code from the held-key bytes (an up arrow with a left arrow is 7, and so on; opposite arrows are
/// not tested). The edge routine reads the mouse position and gives a code only for the exact border pixels of the screen: column 0 is
/// the west edge, column width − 1 the east edge, row 0 the north edge and row height − 1 the south edge; a corner pixel is a diagonal.
/// There is no margin and no dwell time. The original's screen is 800×600 and the executable compares with 799 and 599; the viewer asks
/// for the border pixels of its own window, so the rule is the same at any size.
/// </para>
/// <para>
/// ARC-UI-100: the step and the rate of that scroll function. Each call moves the view by a constant number of pixels: horizontally
/// <c>X</c>, vertically <c>Y</c>, and on a diagonal <c>X + 4</c> and <c>Y + 2</c> (the jump table 0x40E58C). <c>X</c> and <c>Y</c> come
/// from a four-level speed table (0x40E940: 8/4, 20/10, 28/14 and 56/28 pixels). The level is set to 1 when the scroll module starts
/// (0x40DFCF) and its only setter (0x40E080) is never called, so the game always scrolls 20 pixels across and 10 pixels up or down per step
/// (24 and 12 on a diagonal; earlier notes had the level-0 figures). There is no acceleration. A step happens at most once per pass of the
/// main loop and at least <c>1000 / scrollfps</c> ms after the last one (<c>-scrollfps:N</c>, default 60, so 16 ms): a call that comes
/// sooner does nothing, and every step restarts the wait from its own time (the stamp is the time of the step, not a running sum), so a
/// slow frame loses steps. The arrow keys and the edge routine call the same function and share the one stamp.
/// </para>
/// <para>
/// ARC-UI-101: the distance limit. Before it moves the view the scroll function measures the view's centre against the player's tile (the
/// tile centre of the player object, which the move events 0x4EF4D2 and 0x4EF585 keep). With <c>d</c> = the player's Perception / 2 + 3
/// tiles (0x40E8B0, the stat read by 0x4B0490; no limit when <c>d</c> is 0: without a player or under <c>-scrolldist:0</c>) a step is free
/// while the centre after it lies inside a box of ±80·d across and ±40·d up and down around that tile centre. Outside the box a step
/// towards the player is still free (the rotation from the centre's tile before the step to the player's tile, 0x4B8D50, is the step's code or one of
/// its two neighbours; for code 0 the lower neighbour is never matched, a quirk of the original); any other step loses its outward part: a diagonal step with the centre past the side edges keeps only its vertical
/// part, one past the top or bottom edge keeps only its horizontal part, a straight step along an axis that is out of range is blocked, and a
/// step out of range on both axes is blocked. A blocked step moves nothing (the original also shows its blocked scroll cursor and puts the
/// pointer back on the border).
/// </para>
/// </summary>
public static class OriginalScroll
{
    /// <summary>The code for "no scroll" (the routine then only restores the plain cursor, 0x40E610).</summary>
    public const int None = -1;

    private static readonly (int X, int Y)[] Directions =
    [
        (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1),
    ];

    /// <summary>The direction code of the border pixel (<paramref name="x"/>, <paramref name="y"/>) of a screen of
    /// <paramref name="width"/> × <paramref name="height"/> pixels, or <see cref="None"/> (0x401F50 to 0x40200F). A position outside the
    /// screen is no border pixel.</summary>
    public static int EdgeCode(int x, int y, int width, int height)
    {
        if (width < 2 || height < 2 || x < 0 || y < 0 || x >= width || y >= height) return None;
        var column = x == 0 ? 6 : x == width - 1 ? 2 : -1;
        var row = y == 0 ? 0 : y == height - 1 ? 4 : -1;
        if (column < 0) return row;
        if (row < 0) return column;
        return (column, row) switch
        {
            (6, 0) => 7,
            (6, 4) => 5,
            (2, 0) => 1,
            _ => 3,
        };
    }

    /// <summary>The unit step on screen of a direction code (x to the right, y downwards), or (0, 0) for <see cref="None"/> or a code
    /// the function does not know (it only handles 0 to 7).</summary>
    public static (int X, int Y) Direction(int code) => code is >= 0 and <= 7 ? Directions[code] : (0, 0);

    /// <summary>The direction code of a held-arrow vector (x to the right, y downwards, each −1, 0 or 1), as the arrow routine builds it.
    /// An axis with opposite arrows held counts as no arrow on that axis (the viewer's reducer cancels them); the original does not
    /// test that case.</summary>
    public static int CodeOf(int x, int y)
    {
        x = Math.Sign(x); y = Math.Sign(y);
        for (var code = 0; code < Directions.Length; code++) if (Directions[code] == (x, y)) return code;
        return None;
    }

    /// <summary>The level of the speed table the original always runs at (0x40DFCF sets it to 1 at start; the setter is dead code).</summary>
    public const int DefaultSpeedLevel = 1;

    /// <summary>The wait between two steps in seconds at the default <c>-scrollfps:60</c> (0x40E890 stores 1000 / 60 = 16 ms).</summary>
    public const double StepIntervalSeconds = 0.016;

    private static readonly int[] HorizontalSteps = [8, 20, 28, 56];
    private static readonly int[] VerticalSteps = [4, 10, 14, 28];

    /// <summary>The pixels one step moves the view straight across at a speed level (0 to 3, else the default level).</summary>
    public static int HorizontalStep(int level = DefaultSpeedLevel) => HorizontalSteps[level is >= 0 and <= 3 ? level : DefaultSpeedLevel];

    /// <summary>The pixels one step moves the view straight up or down at a speed level.</summary>
    public static int VerticalStep(int level = DefaultSpeedLevel) => VerticalSteps[level is >= 0 and <= 3 ? level : DefaultSpeedLevel];

    /// <summary>The movement of the view in pixels for one step in a direction code (x to the right, y downwards, in the picture the view
    /// shows: a step to the east is (+X, 0), to the north (0, −Y), to the north-east (+(X + 4), −(Y + 2))), or (0, 0) for
    /// <see cref="None"/>. The executable stores the opposite sign (it subtracts from the screen offset); the picture moves as above.</summary>
    public static (int X, int Y) Step(int code, int level = DefaultSpeedLevel)
    {
        var (dx, dy) = Direction(code);
        var diagonal = dx != 0 && dy != 0;
        return (dx * (HorizontalStep(level) + (diagonal ? 4 : 0)), dy * (VerticalStep(level) + (diagonal ? 2 : 0)));
    }

    /// <summary>The scroll distance limit in tiles for a player with this Perception (0x40E8B0: the stat halved towards zero, plus 3).</summary>
    public static int LimitDistance(int perception) => perception / 2 + 3;

    /// <summary>The half extents of the free box around the player per tile of distance: the tile's width and height (0x40E274 and 0x40E28A).</summary>
    public const int BoxHalfWidthPerTile = 80, BoxHalfHeightPerTile = 40;

    /// <summary>
    /// The step the distance limit lets through (0x40E0A0 from its limit test to its apply or blocked exit). The view's centre and the player
    /// are points in one world picture (x to the right, y downwards); <paramref name="originX"/> is the picture's horizontal offset from the
    /// original's isometric frame (a tile's top-left corner is at ((y − x − 1)·40 + originX, (x + y)·20)), so the player's tile and the
    /// centre's tile come from <see cref="LineOfFireRules.Tile"/>. <paramref name="distance"/> 0 means no limit.
    /// </summary>
    public static ScrollLimitResult Limit(int code, double centerX, double centerY, double playerX, double playerY, double originX, int distance,
        int level = DefaultSpeedLevel)
    {
        var (x, y) = Step(code, level);
        if (code == None || distance <= 0) return new(false, code, x, y);
        var player = LineOfFireRules.Tile(Floor(playerX - originX), Floor(playerY), static _ => true);
        // The rotation starts at the tile of the view's centre BEFORE the step (0x40E295 converts the screen centre under the current offset).
        var current = LineOfFireRules.Tile(Floor(centerX - originX), Floor(centerY), static _ => true);
        if (player is not { } home || current is not { } centre) return new(false, code, x, y);
        // The player's tile centre (0x4B8680 plus 40 and 20) and the vector from it to the centre after the step.
        var dx = centerX + x - (40.0 * (home.Y - home.X) + originX);
        var dy = centerY + y - (20.0 * (home.X + home.Y) + 20);
        var overX = Math.Abs(dx) >= BoxHalfWidthPerTile * (double)distance;
        var overY = Math.Abs(dy) >= BoxHalfHeightPerTile * (double)distance;
        if (!overX && !overY) return new(false, code, x, y);
        var toward = LineOfFireRules.Rotation(centre, home);
        // The neighbours are (code - 1) and (code + 1) reduced with C's signed remainder (0x40E2D9): for code 0 the lower one is -1, which no
        // rotation equals, so only the rotations 0 and 1 count as towards the player there (an asymmetry of the original, kept: owner decision D9).
        var lower = code == 0 ? -1 : code - 1;
        if (toward == code || toward == ((code + 1) & 7) || toward == lower) return new(false, code, x, y);
        // Past the side edges (0x40E319): a diagonal keeps its vertical part (the horizontal part is zeroed, the vertical one keeps its +2),
        // a straight horizontal step is blocked.
        if (overX)
        {
            switch (code)
            {
                case 1 or 7: x = 0; code = 0; break;
                case 3 or 5: x = 0; code = 4; break;
                case 2 or 6: return new(true, code, 0, 0);
            }
        }
        // Past the top or bottom edge (0x40E380), on the code the first test left: a straight vertical step is blocked, a diagonal keeps its
        // horizontal part.
        if (overY)
        {
            switch (code)
            {
                case 0 or 4: return new(true, code, 0, 0);
                case 1 or 3: y = 0; code = 2; break;
                case 5 or 7: y = 0; code = 6; break;
            }
        }
        return new(false, code, x, y);
    }

    private static long Floor(double value) => (long)Math.Floor(value);

    /// <summary>The edge direction of the border pixel as a screen vector, or (0, 0).</summary>
    public static (int X, int Y) EdgeDirection(int x, int y, int width, int height) => Direction(EdgeCode(x, y, width, height));
}

/// <summary>
/// ARC-UI-100: the rate gate at the top of the scroll function (0x40E0A0): a call passes when at least the interval has gone by since the
/// last step that passed, and then the stamp is set to now. The clock here runs on the frames' own delta times.
/// </summary>
public sealed class OriginalScrollClock(double intervalSeconds = OriginalScroll.StepIntervalSeconds)
{
    private double _since = double.PositiveInfinity;

    /// <summary>Adds a frame's time; call once per frame before <see cref="TryStep"/>.</summary>
    public void Advance(double deltaSeconds)
    {
        if (double.IsFinite(deltaSeconds) && deltaSeconds > 0) _since += deltaSeconds;
    }

    /// <summary>True (once per interval) when a step may happen now; the wait then restarts from zero.</summary>
    public bool TryStep()
    {
        if (_since < intervalSeconds) return false;
        _since = 0;
        return true;
    }

    /// <summary>Forgets the last step: the next call passes at once (a scroll that starts after a pause).</summary>
    public void Reset() => _since = double.PositiveInfinity;
}

/// <summary>The step the distance limit lets through: <see cref="Blocked"/> (nothing moves) or the direction code left and the movement of
/// the view in pixels (x to the right, y downwards).</summary>
public readonly record struct ScrollLimitResult(bool Blocked, int Code, int X, int Y);
