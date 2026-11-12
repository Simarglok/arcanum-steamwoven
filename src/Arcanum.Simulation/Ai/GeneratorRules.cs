namespace Arcanum.Simulation.Ai;

/// <summary>
/// ARC-WORLD-1157 (E2-a, ledger PO1, PO3): the settings of a monster generator as the original packs them in the dword of the NPC's field 0x12D (the reader 0x4BA500): the five option bits
/// (27 to 31), the slot (19 to 26, an index into the 256-byte counter table), the cap (14 to 18, live creatures at once), the budget (7 to 13, creatures to create in all) and the count made so
/// far (0 to 6). The rate (0..7) is not in the dword: it is bits 21 to 23 of the NPC flag word (field 0x118, <see cref="GeneratorRules.RateOf"/>).
/// </summary>
public readonly record struct GeneratorSettings(int Options, int Slot, int Cap, int Budget, int Made)
{
    /// <summary>The counter table has one byte per slot.</summary>
    public const int SlotCount = 256;
    /// <summary>The largest cap (five bits) and budget (seven bits, also the largest count made).</summary>
    public const int MaxCap = 31, MaxBudget = 127;

    /// <summary>Option bit 0x01: may make creatures by day (hours 6 to 17).</summary>
    public const int ByDay = 0x01;
    /// <summary>Option bit 0x02: may make creatures by night (the other hours).</summary>
    public const int ByNight = 0x02;
    /// <summary>Option bit 0x04: only while the generator's drawn rectangle is off the view, and only at spots off the view (SZ6).</summary>
    public const int OffScreen = 0x04;
    /// <summary>Option bit 0x08: up to the room in one tick (a group); clear: one creature.</summary>
    public const int Group = 0x08;
    /// <summary>Option bit 0x10: the budget is ignored and the count made is not advanced.</summary>
    public const int Unlimited = 0x10;

    /// <summary>The settings in a stored dword (0x4BA500: the shifts and masks of the five fields; the rate and the table byte are read apart).</summary>
    [OriginalFunction(0x4BA500, OriginalFunctionScope.Partial)]
    public static GeneratorSettings Unpack(uint packed) =>
        new((int)(packed >> 27) & 0x1F, (int)(packed >> 19) & 0xFF, (int)(packed >> 14) & 0x1F, (int)(packed >> 7) & 0x7F, (int)(packed & 0x7F));

    /// <summary>The dword these settings pack to (0x4BA620's write-back; the live count goes to the table apart).</summary>
    [OriginalFunction(0x4BA620, OriginalFunctionScope.Partial)]
    public uint Pack() =>
        ((uint)(Options & 0x1F) << 27) | ((uint)(Slot & 0xFF) << 19) | ((uint)(Cap & 0x1F) << 14) | ((uint)(Budget & 0x7F) << 7) | (uint)(Made & 0x7F);

    /// <summary>Whether the budget counts (option bit 0x10 clear).</summary>
    public bool BudgetCounts => (Options & Unlimited) == 0;
}

/// <summary>
/// ARC-WORLD-1157 (E2-a, ledger PO1 to PO6): the monster generator's tick, read in the retail 1.0.7.4 executable (the spawner 0x4BA790, the day test 0x45ACF0, the rate table 0x5B5EC0). Pure
/// functions; the session holds the counter table and the count made, the host the view and the tiles.
/// </summary>
public static class GeneratorRules
{
    /// <summary>The game's day: hour 6 to 17 inclusive of the game clock's hour (0x45ACF0 over 0x45AAA0, <c>(clock / 3,600,000) mod 24</c>).</summary>
    public const long HourMs = 3_600_000, DayMs = 24 * HourMs;
    public const int FirstDayHour = 6, LastDayHour = 17;

    // The table at 0x5B5EC0, eight pairs {days, milliseconds} of the game clock indexed by the rate (PO6).
    private static readonly long[] RateDelays =
    [
        1_000, 30_000, 60_000, HourMs, DayMs, 7 * DayMs, 30 * DayMs, 360 * DayMs,
    ];

    /// <summary>The rate in a NPC flag word (bits 21 to 23).</summary>
    public static int RateOf(uint npcFlags) => (int)(npcFlags >> 21) & 7;

    /// <summary>The game clock's hour of a timestamp.</summary>
    /// <remarks>Executable: 0x45AAA0.</remarks>
    public static int HourOf(long gameMs) => (int)(gameMs / HourMs % 24);

    /// <summary>The day test: hour 6 to 17 is day.</summary>
    /// <remarks>Executable: 0x45ACF0 (the clock's own day flag is the same rule).</remarks>
    public static bool IsDay(long gameMs) => HourOf(gameMs) is >= FirstDayHour and <= LastDayHour;

    /// <summary>The delay of the generator's next tick, in game milliseconds, for a rate (PO6; the rate's three bits are masked as the original's table index is not range-checked).</summary>
    public static long NextDelayMs(int rate) => RateDelays[rate & 7];

    /// <summary>
    /// How many creatures the spawner asks the placement for (PO2, PO3), 0 when it makes nothing: the room is cap minus the live count and must be positive; the slot must not be
    /// blocked; the option bit of the hour (day: 0x01, night: 0x02) must be set; with option bit 0x04 the generator's drawn rectangle must be off the view (<paramref name="offView"/>); with the
    /// budget counting (bit 0x10 clear) the room is cut to what is left of the budget, and none left makes nothing; one creature unless option bit 0x08 asks for up to the room.
    /// </summary>
    /// <remarks>Executable: the spawner 0x4BA790.</remarks>
    public static int Request(GeneratorSettings settings, int live, bool blocked, long gameMs, bool offView)
    {
        var room = settings.Cap - live;
        if (room <= 0 || blocked) return 0;
        if ((settings.Options & (IsDay(gameMs) ? GeneratorSettings.ByDay : GeneratorSettings.ByNight)) == 0) return 0;
        if ((settings.Options & GeneratorSettings.OffScreen) != 0 && !offView) return 0;
        if (settings.BudgetCounts)
        {
            var left = settings.Budget - settings.Made;
            if (left < 1) return 0;
            room = Math.Min(room, left);
        }
        return (settings.Options & GeneratorSettings.Group) == 0 ? 1 : room;
    }
}
