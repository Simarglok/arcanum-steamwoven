namespace Arcanum.Simulation.Travel;

/// <summary>The draw of the original's random range (0x4CEF00, inclusive on both ends).</summary>
public interface IEncounterRolls
{
    int Roll(int min, int max);
}

/// <summary>
/// What the host knows of the place the roll is made for (ledger PO15–PO16): the roll location (the world-map marker while the party travels with the map open, else the hero's tile, 0x566CC0), the terrain
/// word's base and second types at its sector (-1 when the sector is outside the grid), whether the sector has a town map of its own (0x4BE380), and whether the rest window is open (0x57B4E0).
/// </summary>
public readonly record struct EncounterPlace(TravelLocation Location, int TerrainBase, int TerrainSecond, bool InTownMapSector, bool RestWindowOpen);

/// <summary>The hero's side of the roll: the character level (stat 17) the rows' level ranges are tested against, the game clock's hour, a global flag reader and the use counts of the rows.</summary>
public sealed record EncounterWho(int Hour, int HeroLevel, Func<int, bool> GlobalFlag, Func<int, int> Uses);

/// <summary>How far the roll went: every value before <see cref="Fired"/> is a stop of the original's roll (0x558DE0, 0x559010) that returns false.</summary>
public enum EncounterOutcome { NoTables, ChanceMissed, NoPower, TownMapSector, NoTerrain, TableOutOfRange, NoEligibleRow, NoMonsters, Fired }

/// <summary>
/// The result of one roll. <see cref="UsedRow"/> is the message number of the row whose use count rises by one (a row picked at all, also when its groups came to no monster); <see cref="Sizes"/> are the rolled
/// group sizes by slot; <see cref="West"/> is the ring orientation (the groups stand west of the location, else north of it). <see cref="Table"/> is the table index.
/// </summary>
public sealed record EncounterRoll(EncounterOutcome Outcome, int Table = -1, int? UsedRow = null, IReadOnlyList<int>? Sizes = null, IReadOnlyList<int>? Prototypes = null, bool West = false)
{
    public bool Fired => Outcome == EncounterOutcome.Fired;
}

/// <summary>
/// ARC-WORLD-1159 (E2-c, ledger PO13–PO19): the world-map random encounter roll of the retail 1.0.7.4 executable as pure functions over the tables and an injected draw: the chance (0x558DE0, 0x558F30), the power and
/// terrain values (0x558FB0, 0x5589E0), the table index and the weighted pick (0x559010, 0x559150, 0x5591B0), the group sizes (0x559200) and the creatures' spots (0x559260, 0x5594E0).
/// </summary>
public static class EncounterRules
{
    public const int DefaultChance = 5, RestChanceFactor = 5, DefaultPower = 2, TerrainTypes = 19, DayStartHour = 6, NightStartHour = 18, Ring = 6;
    public const long MinDelayMinutes = 300, MaxDelayMinutes = 700, MinuteMs = 60_000;

    // 0x5C79C4: the encounter terrain value of each terrain base type 0..18 (0: none).
    private static readonly int[] TerrainValues = [1, 3, 0, 2, 7, 6, 9, 2, 6, 0, 4, 2, 8, 5, 0, 0, 0, 0, 0];

    /// <summary>The delay of the next Random Encounters event, 300 to 700 game minutes (the draw is the caller's).</summary>
    public static long DelayMs(int minutes) => minutes * MinuteMs;

    /// <summary>0x558F30: the value of the first region of the chart whose circle covers the location (the Chebyshev distance within the radius), else none.</summary>
    [OriginalFunction(0x558F30, OriginalFunctionScope.Full)]
    public static int? RegionValue(IReadOnlyList<EncounterRegion> chart, TravelLocation location)
    {
        foreach (var region in chart)
            if (WorldTravelGeometry.Distance(new(region.X, region.Y), location) <= region.Radius) return region.Value;
        return null;
    }

    /// <summary>The roll's night test: night is the hour before 6 or after 17.</summary>
    public static bool IsNight(int hour) => hour < DayStartHour || hour >= NightStartHour;

    /// <summary>0x5589E0 and 0x558FB0: the encounter terrain value of a terrain word, 0 when either type is unknown (above 18) or has no value.</summary>
    [OriginalFunction(0x558FB0, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x5589E0, OriginalFunctionScope.Full)]
    public static int TerrainValue(int terrainBase, int terrainSecond)
    {
        if (terrainBase is < 0 or >= TerrainTypes || terrainSecond is < 0 or >= TerrainTypes) return 0;
        return TerrainValues[terrainBase] != 0 && TerrainValues[terrainSecond] != 0 ? TerrainValues[terrainBase] : 0;
    }

    /// <summary>The table index the roll picks without an override: the night flag plus twice the sum of the power and three times the terrain value, less 8.</summary>
    public static int TableIndex(int night, int power, int terrain) => night - 8 + (power + terrain * 3) * 2;

    /// <summary>0x559150: a row is eligible when the hero's level is within its range, its global flag (when it has one) is set and its use count is below its cap (when it has one).</summary>
    [OriginalFunction(0x559150, OriginalFunctionScope.Full)]
    public static bool Eligible(EncounterRow row, EncounterWho who) =>
        (short)row.MinLevel <= who.HeroLevel && who.HeroLevel <= (short)row.MaxLevel &&
        (row.GlobalFlag == -1 || who.GlobalFlag(row.GlobalFlag)) && (row.TriggerCap < 0 || who.Uses(row.Line) < row.TriggerCap);

    /// <summary>
    /// 0x558DE0 and 0x559010: the whole roll for a place, in the original's order of draws: the chance roll 1..100 (taken whenever the tables are loaded), then, only after it passed and the place allows a table
    /// (power, town map sector, terrain), the pick roll 1..sum of the eligible weights, then each group's size (a draw only when its range is not a single value).
    /// </summary>
    [OriginalFunction(0x558DE0, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x559010, OriginalFunctionScope.Full)]
    [OriginalFunction(0x5591B0, OriginalFunctionScope.Full)]
    [OriginalFunction(0x559200, OriginalFunctionScope.Full)]
    public static EncounterRoll Roll(EncounterTables tables, EncounterPlace place, EncounterWho who, IEncounterRolls rolls)
    {
        ArgumentNullException.ThrowIfNull(tables); ArgumentNullException.ThrowIfNull(who); ArgumentNullException.ThrowIfNull(rolls);
        if (tables.Status != EncounterTablesStatus.Loaded) return new(EncounterOutcome.NoTables);
        var chance = RegionValue(tables.Frequency, place.Location) ?? DefaultChance;
        if (place.RestWindowOpen) chance *= RestChanceFactor;
        if (rolls.Roll(1, 100) > chance) return new(EncounterOutcome.ChanceMissed);
        int power;
        if (RegionValue(tables.Power, place.Location) is { } found)
        {
            if (found == 0) return new(EncounterOutcome.NoPower);
            power = found;
        }
        else power = DefaultPower;
        var night = IsNight(who.Hour) ? 1 : 0;
        if (place.InTownMapSector) return new(EncounterOutcome.TownMapSector);
        var terrain = TerrainValue(place.TerrainBase, place.TerrainSecond);
        if (terrain == 0) return new(EncounterOutcome.NoTerrain);
        var table = RegionValue(tables.Override, place.Location) is { } over && over != -1 ? over : TableIndex(night, power, terrain);
        if (table < 0 || table >= tables.Tables.Count) return new(EncounterOutcome.TableOutOfRange, table);
        var rows = tables.Tables[table];
        var sum = 0;
        foreach (var row in rows) if (Eligible(row, who)) sum += row.Weight;
        if (sum <= 0) return new(EncounterOutcome.NoEligibleRow, table);
        // 0x559010 walks the rows with the roll: a row is taken when the roll is below its weight (strictly), else its weight is subtracted; running off the end takes the last eligible row.
        var pick = rolls.Roll(1, sum);
        var chosen = -1;
        for (var i = 0; i < rows.Count; i++)
        {
            if (!Eligible(rows[i], who)) continue;
            chosen = i;
            if (pick < rows[i].Weight) break;
            pick -= rows[i].Weight;
        }
        var picked = rows[chosen];
        var sizes = new int[picked.Groups.Count];
        var total = 0;
        for (var g = 0; g < sizes.Length; g++)
        {
            var group = picked.Groups[g];
            sizes[g] = group.Min == group.Max ? group.Min : rolls.Roll(group.Min, group.Max);
            total += sizes[g];
        }
        if (total == 0) return new(EncounterOutcome.NoMonsters, table, picked.Line);
        // 0x559260: a roll of 1..100 below 51 puts the groups west of the location, else north of it.
        var west = rolls.Roll(1, 100) < 51;
        return new(EncounterOutcome.Fired, table, picked.Line, sizes, picked.Groups.Select(g => g.Prototype).ToArray(), west);
    }

    /// <summary>
    /// 0x559260 and 0x5594E0: the tile of the n-th creature of a group. The groups stand on a ring base west (x − 6) or north (y − 6) of the location: the first creature is one tile on in y, later ones step outward
    /// in rings of three ((n − 1) / 3 + 1 in x, n mod 3 in y).
    /// </summary>
    [OriginalFunction(0x5594E0, OriginalFunctionScope.Full)]
    public static (long X, long Y) Spot(TravelLocation location, bool west, int n)
    {
        var baseX = west ? location.X - Ring : location.X;
        var baseY = west ? location.Y : location.Y - Ring;
        return n == 0 ? (baseX, baseY + 1) : (baseX + (n - 1) / 3 + 1, baseY + n % 3);
    }
}
