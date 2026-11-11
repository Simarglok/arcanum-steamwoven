namespace Arcanum.Simulation.Clock;

/// <summary>The calendar of a game time: 30-day months, 12-month years from the profile's start year.</summary>
public static class ClockCalendar
{
    // Shared by bootstrap and advance, and since ARC-UI-008 by the logbook's dates. Extended startYear is an arithmetic
    // capacity seam, not a supported public profile or owner construction range.
    [OriginalFunction(0x45AB40, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x45AB20, OriginalFunctionScope.Full)]
    [OriginalFunction(0x45AB00, OriginalFunctionScope.Full)]
    [OriginalFunction(0x45AAC0, OriginalFunctionScope.Full)]
    [OriginalFunction(0x45ACF0, OriginalFunctionScope.Full)]
    [OriginalFunction(0x45AAE0, OriginalFunctionScope.Full)]
    [OriginalFunction(0x45AAA0, OriginalFunctionScope.Full)]
    [OriginalFunction(0x45A920, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x45A900, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x45A8E0, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x45A8B0, OriginalFunctionScope.Full)]
    [OriginalFunction(0x45A870, OriginalFunctionScope.Full)]
    public static ClockFailure TryDerive(long timestampMs, long startYear, out ClockCalendarFields calendar)
    {
        calendar = default;
        if (timestampMs < 0 || startYear < 0) return ClockFailure.InvalidState;
        var day = timestampMs / 86_400_000;
        var ms = timestampMs % 86_400_000;
        long year;
        try { year = checked(startYear + day / 360); }
        catch (OverflowException) { return ClockFailure.CalendarOverflow; }
        var hour = (int)(ms / 3_600_000);
        calendar = new(day, ms, hour, (int)(ms / 60_000 % 60), (int)(ms / 1_000 % 60),
            (int)(day % 30 + 1), (int)(day / 30 % 12 + 1), year, hour is >= 6 and < 18);
        return ClockFailure.None;
    }
}
