namespace Arcanum.Simulation.Clock;

internal static class ClockCalendar
{
    // Shared by bootstrap and advance. Extended startYear is an arithmetic capacity seam,
    // not a supported public profile or owner construction range.
    internal static ClockFailure TryDerive(long timestampMs, long startYear, out ClockCalendarFields calendar)
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
