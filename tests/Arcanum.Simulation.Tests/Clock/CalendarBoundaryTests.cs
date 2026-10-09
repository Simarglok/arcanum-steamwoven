using Arcanum.Simulation.Clock;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Tests.Clock.ClockFixtures;

namespace Arcanum.Simulation.Tests.Clock;

[TestClass]
public sealed class CalendarBoundaryTests
{
    [TestMethod]
    [DataRow(21599999L, 0L, 21599999L, 5, 59, 59, 1, false)]
    [DataRow(21600000L, 0L, 21600000L, 6, 0, 0, 1, true)]
    [DataRow(21600001L, 0L, 21600001L, 6, 0, 0, 1, true)]
    [DataRow(64799999L, 0L, 64799999L, 17, 59, 59, 1, true)]
    [DataRow(64800000L, 0L, 64800000L, 18, 0, 0, 1, false)]
    [DataRow(64800001L, 0L, 64800001L, 18, 0, 0, 1, false)]
    [DataRow(86399999L, 0L, 86399999L, 23, 59, 59, 1, false)]
    [DataRow(86400000L, 1L, 0L, 0, 0, 0, 2, false)]
    [DataRow(86400001L, 1L, 1L, 0, 0, 0, 2, false)]
    public void NormalizedBoundaryThroughOrdinaryAdvance(long time, long day, long ms, int hour,
        int minute, int second, int date, bool isDay)
    {
        using var owner = Ready(Profile(0));
        var initial = owner.QueryClockSnapshot();
        var result = owner.AdvanceGameTime(initial.Token, time, ClockAdvanceReason.OrdinaryAdvance);
        Assert.AreEqual(ClockMutationDisposition.Changed, result.Disposition);
        Assert.AreEqual(ClockFailure.None, result.Failure);
        State(result.Snapshot, initial.OwnerIdentity, Profile(0), time, default, 1,
            day, ms, hour, minute, second, date, 1, 1885, isDay);
        Assert.AreEqual(result.Snapshot, owner.QueryClockSnapshot());
        State(initial, initial.OwnerIdentity, Profile(0), 0, default, 0, 0, 0, 0, 0, 0, 1, 1, 1885, false);
    }

    [TestMethod]
    [DataRow(0L, 0L, 1, 1, 1885L, 0L)]
    [DataRow(2505600000L, 29L, 30, 1, 1885L, 1L)]
    [DataRow(2592000000L, 30L, 1, 2, 1885L, 1L)]
    [DataRow(31017600000L, 359L, 30, 12, 1885L, 1L)]
    [DataRow(31104000000L, 360L, 1, 1, 1886L, 1L)]
    [DataRow(31190400000L, 361L, 2, 1, 1886L, 1L)]
    [DataRow(172800000L, 2L, 3, 1, 1885L, 1L)]
    public void OrdinaryCalendarAndMultiDayAdvance(long time, long day, int date, int month, long year, long revision)
    {
        using var owner = Ready(Profile(0));
        var initial = owner.QueryClockSnapshot();
        var result = owner.AdvanceGameTime(initial.Token, time, ClockAdvanceReason.OrdinaryAdvance);
        Assert.AreEqual(time == 0 ? ClockMutationDisposition.NoChange : ClockMutationDisposition.Changed, result.Disposition);
        Assert.AreEqual(ClockFailure.None, result.Failure);
        State(result.Snapshot, initial.OwnerIdentity, Profile(0), time, default, revision, day, 0,
            0, 0, 0, date, month, year, false);
        Assert.AreEqual(result.Snapshot, owner.QueryClockSnapshot());
    }

    [TestMethod]
    public void AdvanceOneMillisecondCrossesMidnightOnce()
    {
        using var owner = At(86399999);
        var before = owner.QueryClockSnapshot();
        State(before, before.OwnerIdentity, Profile(), 86399999, default, 0, 0, 86399999, 23, 59, 59, 1, 1, 1885, false);
        var result = owner.AdvanceGameTime(before.Token, 1, ClockAdvanceReason.OrdinaryAdvance);
        State(result.Snapshot, before.OwnerIdentity, Profile(), 86400000, default, 1, 1, 0, 0, 0, 0, 2, 1, 1885, false);
        Result(owner, result, ClockMutationDisposition.Changed, ClockFailure.None, result.Snapshot);
    }

    [TestMethod]
    public void MaximalPublicProfileCanReachLongMaxTimeWithoutNarrowingYear()
    {
        using var owner = Ready(Profile(0, int.MaxValue));
        var before = owner.QueryClockSnapshot();
        var result = owner.AdvanceGameTime(before.Token, long.MaxValue, ClockAdvanceReason.OrdinaryAdvance);
        State(result.Snapshot, before.OwnerIdentity, Profile(0, int.MaxValue), 9223372036854775807, default, 1,
            106751991167, 25975807, 7, 12, 55, 18, 10, 2444016955, true);
        Result(owner, result, ClockMutationDisposition.Changed, ClockFailure.None, result.Snapshot);
        Result(owner, owner.AdvanceGameTime(result.Snapshot.Token, 1, ClockAdvanceReason.OrdinaryAdvance),
            ClockMutationDisposition.Rejected, ClockFailure.TimeOverflow, result.Snapshot);
    }

    [TestMethod]
    public void SharedProductionCalendarReportsSignedYearCapacityWithoutClaimingPublicReachability()
    {
        Assert.AreEqual(ClockFailure.None, ClockCalendar.TryDerive(31103999999, long.MaxValue, out var last));
        Assert.AreEqual(new ClockCalendarFields(359, 86399999, 23, 59, 59, 30, 12, 9223372036854775807, false), last);
        Assert.AreEqual(ClockFailure.CalendarOverflow, ClockCalendar.TryDerive(31104000000, long.MaxValue, out var overflow));
        Assert.AreEqual(default(ClockCalendarFields), overflow);
        Assert.AreEqual(ClockFailure.InvalidState, ClockCalendar.TryDerive(-1, 0, out var invalid));
        Assert.AreEqual(default(ClockCalendarFields), invalid);
        Assert.AreEqual(ClockFailure.InvalidState, ClockCalendar.TryDerive(0, -1, out invalid));
        Assert.AreEqual(default(ClockCalendarFields), invalid);
        Assert.AreEqual(ClockFailure.None, ClockCalendar.TryDerive(0, 0, out var zero));
        Assert.AreEqual(new ClockCalendarFields(0, 0, 0, 0, 0, 1, 1, 0, false), zero);
    }
}
