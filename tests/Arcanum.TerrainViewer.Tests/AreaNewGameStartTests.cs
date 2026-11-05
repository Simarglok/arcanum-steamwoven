using Arcanum.Session;
using Arcanum.Simulation.ChangeOfPlace;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

// ARC-DEV-115 (MIG11 S4, RL29, ledger OS5): the new game's teleport of the hero to the start map is the session's change of place with the
// start routine's flags (0x5412E0 read again: the request record's flag word is 0x45).
[TestClass]
public sealed class AreaNewGameStartTests
{
    private static GameSessionOwner Session(int map) =>
        AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(), new(31, 29), map).Owner!;

    [TestMethod]
    public void The_start_flags_are_both_movies_and_the_fade_in_and_the_landing_flag_is_the_blank_screen_alone()
    {
        Assert.AreEqual(0x45u, Convert.ToUInt32(AreaChangeOfPlace.NewGameFlags));
        Assert.AreEqual(0x20u, Convert.ToUInt32(AreaChangeOfPlace.LandingFlags));
        foreach (var flags in new[] { AreaChangeOfPlace.NewGameFlags, AreaChangeOfPlace.LandingFlags })
            Assert.AreEqual(ChangeOfPlaceFailure.None, ChangeOfPlaceRules.Validate(
                new ChangeOfPlaceRequest("pc", ChangeOfPlaceCaller.NewGame, 1, new(1, 1), flags, 0, true, true)));
    }

    [TestMethod]
    public void On_the_start_map_the_start_is_a_relocation_with_the_movies_and_the_fade_in_and_no_fade_out()
    {
        using var owner = Session(1);
        var facing = owner.QueryActor().Facing;
        Assert.IsNull(AreaNewGameStart.Run(owner, new(1), new(500, 600), null, out var run));
        Assert.AreEqual(ChangeOfPlaceRunStatus.Completed, run!.Status);
        Assert.AreEqual(ChangeOfPlaceCaller.NewGame, run.Request!.Caller);
        Assert.AreEqual(AreaChangeOfPlace.NewGameFlags | ChangeOfPlaceFlags.LocalPlayer, run.Request.StoredFlags);
        Assert.IsTrue(run.Ran.SequenceEqual([ChangeOfPlaceStep.PlayFirstMovie, ChangeOfPlaceStep.PlaySecondMovie, ChangeOfPlaceStep.WaitOrders,
            ChangeOfPlaceStep.BuildMoveList, ChangeOfPlaceStep.ResetWallCheck, ChangeOfPlaceStep.RelocateObjects, ChangeOfPlaceStep.KeepPlayerId,
            ChangeOfPlaceStep.PreCache, ChangeOfPlaceStep.FadeInScreen]), string.Join(",", run.Ran), "the fade-out is the routine's own, not a flag");
        Assert.AreEqual((1, 500, 600, facing), (owner.QueryActor().Map.RuntimeMap, owner.QueryActor().Cell.X, owner.QueryActor().Cell.Y, owner.QueryActor().Facing));
        Assert.IsNull(owner.QueryChangeOfPlace(), "the slot is empty again");
        Assert.IsFalse(owner.ChangeOfPlaceInProgress);
    }

    [TestMethod]
    public void A_session_on_another_map_is_written_to_the_start_map_and_takes_the_given_facing()
    {
        using var owner = Session(14);
        Assert.IsNull(AreaNewGameStart.Run(owner, new(1), new(500, 600), NeighborDirection.XPlus, out var run));
        Assert.AreEqual(ChangeOfPlaceRunStatus.Completed, run!.Status);
        CollectionAssert.Contains(run.Ran.ToArray(), ChangeOfPlaceStep.WriteTravellers);
        CollectionAssert.Contains(run.Ran.ToArray(), ChangeOfPlaceStep.OpenDestination);
        Assert.AreEqual((1, 500, 600, NeighborDirection.XPlus), (owner.QueryActor().Map.RuntimeMap, owner.QueryActor().Cell.X, owner.QueryActor().Cell.Y, owner.QueryActor().Facing));
    }

    [TestMethod]
    public void An_invalid_start_is_refused_before_the_slot_and_the_hero_stay_as_they_were()
    {
        using var owner = Session(1);
        var before = owner.QueryActor();
        var revision = owner.Version.Revision;
        var refused = AreaNewGameStart.Run(owner, new(1), new(-5, 600), null, out var run);
        StringAssert.Contains(refused, "not accepted");
        Assert.IsNull(run);
        Assert.AreEqual(before, owner.QueryActor());
        Assert.AreEqual(revision, owner.Version.Revision);
        Assert.IsNull(owner.QueryChangeOfPlace());
    }

    [TestMethod]
    public void A_pending_local_player_request_refuses_the_start_and_stays_in_the_slot()
    {
        using var owner = Session(1);
        Assert.IsTrue(owner.RequestHeroChangeOfPlace(ChangeOfPlaceCaller.JumpPoint, 1, new(40, 40), ChangeOfPlaceFlags.None, 0, map => map == 1).Accepted);
        var before = owner.QueryActor();
        var revision = owner.Version.Revision;
        var refused = AreaNewGameStart.Run(owner, new(1), new(500, 600), null, out var run);
        StringAssert.Contains(refused, "RefusedLocalPlayerPending");
        Assert.IsNull(run);
        Assert.AreEqual(ChangeOfPlaceCaller.JumpPoint, owner.QueryChangeOfPlace()!.Caller, "the earlier request keeps the slot");
        Assert.AreEqual((before, revision), (owner.QueryActor(), owner.Version.Revision));
    }
}
