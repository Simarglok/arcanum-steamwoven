using Arcanum.Simulation.Clock;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Tests.Clock.ClockFixtures;

namespace Arcanum.Simulation.Tests.Clock;

[TestClass]
public sealed class MutationTests
{
    [TestMethod]
    public void ZeroPauseReasonResumeAndExplicitAdvanceHaveExactRevisions()
    {
        using var owner = Ready(Profile());
        var initial = owner.QueryClockSnapshot();
        State(initial, initial.OwnerIdentity, Profile(), 54000000, default, 0, 0, 54000000, 15, 0, 0, 1, 1, 1885, true);
        Result(owner, owner.AdvanceGameTime(initial.Token, 0, ClockAdvanceReason.OrdinaryAdvance),
            ClockMutationDisposition.NoChange, ClockFailure.None, initial);
        Result(owner, owner.AdvanceGameTime(initial.Token, -1, ClockAdvanceReason.OrdinaryAdvance),
            ClockMutationDisposition.Rejected, ClockFailure.InvalidDuration, initial);
        Result(owner, owner.SetPaused(initial.Token, default), ClockMutationDisposition.NoChange, ClockFailure.None, initial);
        var menu = owner.SetPaused(initial.Token, new(true, PauseReason.Menu));
        State(menu.Snapshot, initial.OwnerIdentity, Profile(), 54000000, new(true, PauseReason.Menu), 1,
            0, 54000000, 15, 0, 0, 1, 1, 1885, true);
        Result(owner, menu, ClockMutationDisposition.Changed, ClockFailure.None, menu.Snapshot);
        Result(owner, owner.SetPaused(menu.Snapshot.Token, new(true, PauseReason.Menu)),
            ClockMutationDisposition.NoChange, ClockFailure.None, menu.Snapshot);
        foreach (var duration in new[] { 0L, 100L, long.MaxValue })
            Result(owner, owner.AdvanceGameTime(menu.Snapshot.Token, duration, ClockAdvanceReason.OrdinaryAdvance),
                ClockMutationDisposition.NoChange, ClockFailure.None, menu.Snapshot);
        Result(owner, owner.AdvanceGameTime(menu.Snapshot.Token, -1, ClockAdvanceReason.OrdinaryAdvance),
            ClockMutationDisposition.Rejected, ClockFailure.InvalidDuration, menu.Snapshot);
        var tb = owner.SetPaused(menu.Snapshot.Token, new(true, PauseReason.TurnBased));
        State(tb.Snapshot, initial.OwnerIdentity, Profile(), 54000000, new(true, PauseReason.TurnBased), 2,
            0, 54000000, 15, 0, 0, 1, 1, 1885, true);
        Result(owner, tb, ClockMutationDisposition.Changed, ClockFailure.None, tb.Snapshot);
        var user = owner.SetPaused(tb.Snapshot.Token, new(true, PauseReason.User));
        State(user.Snapshot, initial.OwnerIdentity, Profile(), 54000000, new(true, PauseReason.User), 3,
            0, 54000000, 15, 0, 0, 1, 1, 1885, true);
        Result(owner, user, ClockMutationDisposition.Changed, ClockFailure.None, user.Snapshot);
        var resume = owner.SetPaused(user.Snapshot.Token, default);
        State(resume.Snapshot, initial.OwnerIdentity, Profile(), 54000000, default, 4,
            0, 54000000, 15, 0, 0, 1, 1, 1885, true);
        Result(owner, resume, ClockMutationDisposition.Changed, ClockFailure.None, resume.Snapshot);
        Result(owner, owner.SetPaused(resume.Snapshot.Token, default), ClockMutationDisposition.NoChange, ClockFailure.None, resume.Snapshot);
        var next = owner.AdvanceGameTime(resume.Snapshot.Token, 1, ClockAdvanceReason.OrdinaryAdvance);
        State(next.Snapshot, initial.OwnerIdentity, Profile(), 54000001, default, 5,
            0, 54000001, 15, 0, 0, 1, 1, 1885, true);
        Result(owner, next, ClockMutationDisposition.Changed, ClockFailure.None, next.Snapshot);
        State(initial, initial.OwnerIdentity, Profile(), 54000000, default, 0, 0, 54000000, 15, 0, 0, 1, 1, 1885, true);
    }

    [TestMethod]
    public void ResumeDirectlyFromMenuHasNoCatchup()
    {
        using var owner = At(54000000, 1, new(true, PauseReason.Menu));
        var before = owner.QueryClockSnapshot();
        var resume = owner.SetPaused(before.Token, default);
        State(resume.Snapshot, before.OwnerIdentity, Profile(), 54000000, default, 2,
            0, 54000000, 15, 0, 0, 1, 1, 1885, true);
        Result(owner, resume, ClockMutationDisposition.Changed, ClockFailure.None, resume.Snapshot);
    }

    [TestMethod]
    public void AllTokenFieldsGuardChangesAndNoopsEvenWhilePaused()
    {
        using var owner = Ready(Profile());
        using var foreign = Ready(Profile());
        var initial = owner.QueryClockSnapshot();
        var token = initial.Token;
        var p = Profile();
        var alteredProfiles = new[] { p with { ClockSchemaVersion = 2 }, p with { PolicyId = p.PolicyId + " " },
            p with { SourceRevision = p.SourceRevision.ToUpperInvariant() }, p with { SourceDataSha256 = new string('b', 64) },
            p with { StartupResourceSha256 = new string('b', 64) }, p with { StartYear = 1886 }, p with { StartHour = 0 },
            default(ProfileBinding) };
        var tokens = alteredProfiles.Select(profile => token with { ProfileBinding = profile }).Concat(new[] {
            default(ClockToken), token with { OwnerIdentity = null }, foreign.QueryClockSnapshot().Token,
            token with { OwnerIdentity = foreign.QueryClockSnapshot().OwnerIdentity },
            token with { Generation = 0 }, token with { Generation = 2 }, token with { Revision = -1 },
            token with { Revision = 1 } }).ToArray();
        foreach (var bad in tokens)
        {
            foreach (var duration in new[] { 0L, 1L })
                Result(owner, owner.AdvanceGameTime(bad, duration, ClockAdvanceReason.OrdinaryAdvance),
                    ClockMutationDisposition.Rejected, ClockFailure.StaleToken, initial);
            Result(owner, owner.SetPaused(bad, default), ClockMutationDisposition.Rejected, ClockFailure.StaleToken, initial);
            Result(owner, owner.SetPaused(bad, new(true, PauseReason.User)), ClockMutationDisposition.Rejected, ClockFailure.StaleToken, initial);
        }
        // An exact manually copied tuple is valid, but possession is not cryptographic authority.
        var copied = new ClockToken(token.OwnerIdentity, token.ProfileBinding, 1, 0);
        Result(owner, owner.AdvanceGameTime(copied, 0, ClockAdvanceReason.OrdinaryAdvance),
            ClockMutationDisposition.NoChange, ClockFailure.None, initial);
        var paused = owner.SetPaused(copied, new(true, PauseReason.Menu)).Snapshot;
        State(paused, initial.OwnerIdentity, Profile(), 54000000, new(true, PauseReason.Menu), 1,
            0, 54000000, 15, 0, 0, 1, 1, 1885, true);
        // A token with the future revision becomes exact after that semantic change;
        // it is not an authentication secret. Generate a different revision for this state.
        Assert.AreEqual(paused.Token, token with { Revision = 1 });
        Result(owner, owner.AdvanceGameTime(token with { Revision = 1 }, 0, ClockAdvanceReason.OrdinaryAdvance),
            ClockMutationDisposition.NoChange, ClockFailure.None, paused);
        foreach (var bad in tokens.Where(t => t != paused.Token).Append(token).Append(paused.Token with { Revision = 2 }))
        {
            foreach (var duration in new[] { 0L, 100L })
                Result(owner, owner.AdvanceGameTime(bad, duration, ClockAdvanceReason.OrdinaryAdvance),
                    ClockMutationDisposition.Rejected, ClockFailure.StaleToken, paused);
            Result(owner, owner.SetPaused(bad, paused.PauseDisposition), ClockMutationDisposition.Rejected, ClockFailure.StaleToken, paused);
            Result(owner, owner.SetPaused(bad, default), ClockMutationDisposition.Rejected, ClockFailure.StaleToken, paused);
        }
    }

    [TestMethod]
    public void MalformedRequestPrecedencePreservesCompleteValidState()
    {
        using var owner = Ready(Profile());
        foreach (var pause in new[] { default(PauseDisposition), new PauseDisposition(true, PauseReason.Menu) })
        {
            var before = owner.SetPaused(owner.QueryClockSnapshot().Token, pause).Snapshot;
            foreach (var token in new[] { before.Token, default(ClockToken) })
            {
                Result(owner, owner.AdvanceGameTime(token, -1, (ClockAdvanceReason)99),
                    ClockMutationDisposition.Rejected, ClockFailure.InvalidDuration, before);
                foreach (var duration in new[] { 0L, 1L })
                    Result(owner, owner.AdvanceGameTime(token, duration, (ClockAdvanceReason)99),
                        ClockMutationDisposition.Rejected, ClockFailure.InvalidReason, before);
                foreach (var badPause in new[] { new PauseDisposition(false, PauseReason.User), new PauseDisposition(false, PauseReason.Menu),
                    new PauseDisposition(false, PauseReason.TurnBased), new PauseDisposition(true, PauseReason.None),
                    new PauseDisposition(false, (PauseReason)99), new PauseDisposition(true, (PauseReason)99),
                    new PauseDisposition(true, (PauseReason)(-1)) })
                    Result(owner, owner.SetPaused(token, badPause), ClockMutationDisposition.Rejected, ClockFailure.InvalidReason, before);
            }
        }
    }

    [TestMethod]
    public void TimeCapacityAcceptsEdgeRejectsOverflowAndAllowsNoops()
    {
        using var owner = At(long.MaxValue - 1);
        var before = owner.QueryClockSnapshot();
        State(before, before.OwnerIdentity, Profile(), 9223372036854775806, default, 0,
            106751991167, 25975806, 7, 12, 55, 18, 10, 296535193, true);
        var edge = owner.AdvanceGameTime(before.Token, 1, ClockAdvanceReason.OrdinaryAdvance);
        State(edge.Snapshot, before.OwnerIdentity, Profile(), 9223372036854775807, default, 1,
            106751991167, 25975807, 7, 12, 55, 18, 10, 296535193, true);
        Result(owner, edge, ClockMutationDisposition.Changed, ClockFailure.None, edge.Snapshot);
        Result(owner, owner.AdvanceGameTime(edge.Snapshot.Token, 1, ClockAdvanceReason.OrdinaryAdvance),
            ClockMutationDisposition.Rejected, ClockFailure.TimeOverflow, edge.Snapshot);
        Result(owner, owner.AdvanceGameTime(edge.Snapshot.Token, long.MaxValue, ClockAdvanceReason.OrdinaryAdvance),
            ClockMutationDisposition.Rejected, ClockFailure.TimeOverflow, edge.Snapshot);
        Result(owner, owner.AdvanceGameTime(edge.Snapshot.Token, 0, ClockAdvanceReason.OrdinaryAdvance),
            ClockMutationDisposition.NoChange, ClockFailure.None, edge.Snapshot);
        var paused = owner.SetPaused(edge.Snapshot.Token, new(true, PauseReason.Menu)).Snapshot;
        State(paused, before.OwnerIdentity, Profile(), 9223372036854775807, new(true, PauseReason.Menu), 2,
            106751991167, 25975807, 7, 12, 55, 18, 10, 296535193, true);
        Result(owner, owner.AdvanceGameTime(paused.Token, long.MaxValue, ClockAdvanceReason.OrdinaryAdvance),
            ClockMutationDisposition.NoChange, ClockFailure.None, paused);
    }

    [TestMethod]
    public void RevisionCapacityStagesCompleteChangeThenRejectsWithoutPartialState()
    {
        using var owner = At(0, long.MaxValue - 1);
        var before = owner.QueryClockSnapshot();
        var edge = owner.AdvanceGameTime(before.Token, 86400000, ClockAdvanceReason.OrdinaryAdvance);
        State(edge.Snapshot, before.OwnerIdentity, Profile(), 86400000, default, 9223372036854775807,
            1, 0, 0, 0, 0, 2, 1, 1885, false);
        Result(owner, edge, ClockMutationDisposition.Changed, ClockFailure.None, edge.Snapshot);
        Result(owner, owner.AdvanceGameTime(edge.Snapshot.Token, 86400000, ClockAdvanceReason.OrdinaryAdvance),
            ClockMutationDisposition.Rejected, ClockFailure.RevisionExhausted, edge.Snapshot);
        Result(owner, owner.SetPaused(edge.Snapshot.Token, new(true, PauseReason.User)),
            ClockMutationDisposition.Rejected, ClockFailure.RevisionExhausted, edge.Snapshot);
        Result(owner, owner.AdvanceGameTime(edge.Snapshot.Token, 0, ClockAdvanceReason.OrdinaryAdvance),
            ClockMutationDisposition.NoChange, ClockFailure.None, edge.Snapshot);
        Result(owner, owner.SetPaused(edge.Snapshot.Token, default), ClockMutationDisposition.NoChange, ClockFailure.None, edge.Snapshot);
        using var pauseOwner = At(0, long.MaxValue - 1);
        var pauseEdge = pauseOwner.SetPaused(pauseOwner.QueryClockSnapshot().Token, new(true, PauseReason.User));
        State(pauseEdge.Snapshot, pauseEdge.Snapshot.OwnerIdentity, Profile(), 0, new(true, PauseReason.User),
            9223372036854775807, 0, 0, 0, 0, 0, 1, 1, 1885, false);
        Result(pauseOwner, pauseEdge, ClockMutationDisposition.Changed, ClockFailure.None, pauseEdge.Snapshot);
        Result(pauseOwner, pauseOwner.AdvanceGameTime(pauseEdge.Snapshot.Token, long.MaxValue, ClockAdvanceReason.OrdinaryAdvance),
            ClockMutationDisposition.NoChange, ClockFailure.None, pauseEdge.Snapshot);
        Result(pauseOwner, pauseOwner.SetPaused(pauseEdge.Snapshot.Token, new(true, PauseReason.User)),
            ClockMutationDisposition.NoChange, ClockFailure.None, pauseEdge.Snapshot);
        Result(pauseOwner, pauseOwner.SetPaused(pauseEdge.Snapshot.Token, new(true, PauseReason.Menu)),
            ClockMutationDisposition.Rejected, ClockFailure.RevisionExhausted, pauseEdge.Snapshot);
        Result(pauseOwner, pauseOwner.SetPaused(pauseEdge.Snapshot.Token, default),
            ClockMutationDisposition.Rejected, ClockFailure.RevisionExhausted, pauseEdge.Snapshot);
    }

    [TestMethod]
    public void AtBothCapacitiesValidationAndTimeFailurePrecedeRevisionFailure()
    {
        using var owner = At(long.MaxValue, long.MaxValue);
        var before = owner.QueryClockSnapshot();
        State(before, before.OwnerIdentity, Profile(), 9223372036854775807, default, 9223372036854775807,
            106751991167, 25975807, 7, 12, 55, 18, 10, 296535193, true);
        Result(owner, owner.AdvanceGameTime(before.Token, 0, ClockAdvanceReason.OrdinaryAdvance),
            ClockMutationDisposition.NoChange, ClockFailure.None, before);
        Result(owner, owner.SetPaused(before.Token, default), ClockMutationDisposition.NoChange, ClockFailure.None, before);
        Result(owner, owner.AdvanceGameTime(default, 0, ClockAdvanceReason.OrdinaryAdvance),
            ClockMutationDisposition.Rejected, ClockFailure.StaleToken, before);
        Result(owner, owner.AdvanceGameTime(default, -1, ClockAdvanceReason.OrdinaryAdvance),
            ClockMutationDisposition.Rejected, ClockFailure.InvalidDuration, before);
        Result(owner, owner.AdvanceGameTime(before.Token, 1, ClockAdvanceReason.OrdinaryAdvance),
            ClockMutationDisposition.Rejected, ClockFailure.TimeOverflow, before);
        Result(owner, owner.SetPaused(before.Token, new(true, PauseReason.User)),
            ClockMutationDisposition.Rejected, ClockFailure.RevisionExhausted, before);
    }
}
