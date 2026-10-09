using Arcanum.Simulation.Clock;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Clock;

internal static class ClockFixtures
{
    internal static ProfileBinding Profile(int hour = 15, int year = 1885) => new(1,
        "steamwoven-normalized-clock-v1", "a7ff41b300ef712f0e7d088183a3d08957110cdd",
        new string('a', 64), new string('c', 64), year, hour);

    internal static GameClockOwner Ready(ProfileBinding profile)
    {
        var result = GameClockOwner.Bootstrap(profile);
        Assert.AreEqual(ClockBootstrapDisposition.Ready, result.Disposition);
        Assert.AreEqual(ClockFailure.None, result.Failure);
        Assert.IsNotNull(result.Owner);
        Assert.IsNotNull(result.Snapshot);
        Assert.AreEqual(result.Snapshot.Value, result.Owner.QueryClockSnapshot());
        return result.Owner;
    }

    internal static GameClockOwner At(long time, long revision = 0, PauseDisposition pause = default)
    {
        var result = GameClockOwner.FromDescriptor(new(Profile(), time, pause, 1, revision));
        Assert.AreEqual(ClockBootstrapDisposition.Ready, result.Disposition);
        Assert.AreEqual(ClockFailure.None, result.Failure);
        Assert.IsNotNull(result.Owner);
        Assert.AreEqual(result.Snapshot, result.Owner.QueryClockSnapshot());
        return result.Owner;
    }

    // All expected fields supplied by independent literals, never a production derivation helper.
    internal static void State(ClockSnapshot actual, ClockOwnerIdentity identity, ProfileBinding profile,
        long time, PauseDisposition pause, long revision, long day, long ms, int hour, int minute,
        int second, int date, int month, long year, bool isDay)
    {
        var expected = new ClockSnapshot(1, identity, profile, time, pause, 1, revision,
            day, ms, hour, minute, second, date, month, year, isDay);
        Assert.AreEqual(expected, actual);
        Assert.AreEqual(new ClockToken(identity, profile, 1, revision), actual.Token);
    }

    internal static void Result(GameClockOwner owner, ClockMutationResult result,
        ClockMutationDisposition disposition, ClockFailure failure, ClockSnapshot expected)
    {
        Assert.AreEqual(disposition, result.Disposition);
        Assert.AreEqual(failure, result.Failure);
        Assert.AreEqual(expected, result.Snapshot);
        Assert.AreEqual(expected, owner.QueryClockSnapshot());
        Assert.AreEqual(expected.Token, result.Snapshot.Token);
    }

    internal static void Rejected(ClockBootstrapResult result, ClockFailure failure)
    {
        Assert.AreEqual(ClockBootstrapDisposition.Rejected, result.Disposition);
        Assert.AreEqual(failure, result.Failure);
        Assert.IsNull(result.Owner);
        Assert.IsNull(result.Snapshot);
    }
}
