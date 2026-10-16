using Arcanum.Simulation.Clock;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

[TestClass]
public sealed class SessionLightingTests
{
    [TestMethod]
    public void RecordLightingAcceptsOnlyTheCurrentHourAndKeepsTheSessionRevision()
    {
        using var owner = Create();
        Assert.IsNull(owner.QueryLighting());
        var version = owner.Version;
        foreach (var bad in new SessionLightingState[] { new(0, 15), new(-1, 15), new(1, -1), new(1, 24), new(1, 16) })
        {
            Rejected(owner.RecordLighting(bad), owner, GameSessionFailure.InvalidLighting, 0);
            Assert.IsNull(owner.QueryLighting(), bad.ToString());
        }
        var recorded = owner.RecordLighting(new(3, 15));
        Assert.AreEqual(GameSessionDisposition.Changed, recorded.Disposition);
        Assert.AreEqual(version, recorded.Version);
        Assert.AreEqual(version, owner.Version, "lighting does not move the session revision");
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.RecordLighting(new(3, 15)).Disposition);
        Assert.AreEqual(new SessionLightingState(3, 15), owner.QueryLighting());

        // An advance within the hour keeps the record.
        var clock = owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, 3_599_999, ClockAdvanceReason.OrdinaryAdvance).Snapshot;
        Assert.AreEqual(15, clock.Hour);
        Assert.AreEqual(new SessionLightingState(3, 15), owner.QueryLighting());
        // Crossing the hour clears it, so a capture taken before the host records again still restores.
        clock = owner.AdvanceGameTime(clock.Token, 1, ClockAdvanceReason.ExplicitWait).Snapshot;
        Assert.AreEqual(16, clock.Hour);
        Assert.IsNull(owner.QueryLighting());
        var unrecorded = owner.Capture();
        Assert.IsNull(unrecorded.Lighting);
        using (var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(unrecorded)))) Assert.IsNull(restored.QueryLighting());
        Rejected(owner.RecordLighting(new(3, 15)), owner, GameSessionFailure.InvalidLighting, 0);
        Assert.IsNull(owner.QueryLighting(), "a refusal records nothing");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RecordLighting(new(3, 16)).Disposition);
        Assert.AreEqual(new SessionLightingState(3, 16), owner.Capture().Lighting);
        // A refused or no-op advance keeps the record.
        Assert.AreEqual(ClockMutationDisposition.Rejected, owner.AdvanceGameTime(clock.Token, -1, ClockAdvanceReason.OrdinaryAdvance).Disposition);
        Assert.AreEqual(ClockMutationDisposition.NoChange, owner.AdvanceGameTime(clock.Token, 0, ClockAdvanceReason.OrdinaryAdvance).Disposition);
        Assert.AreEqual(new SessionLightingState(3, 16), owner.QueryLighting());
    }

    [TestMethod]
    public void UnlitSessionRoundTripsAsNull()
    {
        using var owner = Create();
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.EndsWith(text, ",\"lighting\":null}");
        using var restored = Restore(Decode(text));
        Assert.IsNull(restored.QueryLighting());
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
    }

    [TestMethod]
    public void RestoreRefusesLightingOfAnotherHour()
    {
        var stale = Decode(GoldenJson.Replace("\"hour\":15}}", "\"hour\":14}}"));
        var result = GameSessionOwner.Restore(stale, RestoreOptions());
        Assert.IsNull(result.Owner);
        Assert.AreEqual(GameSessionFailure.InvalidLighting, result.Failure);

        // A paused saved clock restores paused, at the saved hour, with its lighting.
        var pausedText = GoldenJson.Replace("\"paused\":false,\"pauseReason\":\"None\"", "\"paused\":true,\"pauseReason\":\"Menu\"");
        using var paused = Restore(Decode(pausedText));
        Assert.AreEqual(new PauseDisposition(true, PauseReason.Menu), paused.QueryClockSnapshot().PauseDisposition);
        Assert.AreEqual(new SessionLightingState(1, 15), paused.QueryLighting());
    }
}
