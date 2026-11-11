using Arcanum.Simulation.ChangeOfPlace;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Party;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;
using Step = Arcanum.Simulation.ChangeOfPlace.ChangeOfPlaceStep;

namespace Arcanum.Session.Tests;

// ARC-DEV-113 (MIG11 slice S2): the session owns the change-of-place slot and runs the plan of the hero's change (research CP1–CP8 of
// docs/architecture/change-of-place.md). Entities and maps are invented; the session has no hero (the PC is the actor).
[TestClass]
public sealed class ChangeOfPlaceStateTests
{
    private const string Virgil = "oid:virgil", Sogg = "oid:sogg";
    private static readonly CellCoord Here = new(110, 210), There = new(10, 20);

    // A host that records every call in order and answers as configured.
    private sealed class Host : IChangeOfPlaceHost
    {
        public List<string> Calls { get; } = [];
        public Func<int, bool> Listed { get; init; } = _ => true;
        public ChangeOfPlaceReadiness Readiness { get; init; } = ChangeOfPlaceReadiness.Ready;
        public Step? StopAt { get; init; }
        public SessionTransferFacts Facts { get; init; } = new();
        public IReadOnlyList<MapChangeEdit> Edits { get; init; } = [];
        public SessionMoveList? MoveList { get; private set; }
        public List<SessionMoveList> SpellRunCarries { get; } = [];
        public Action<Step>? OnRun { get; init; }
        public Func<ChangeOfPlaceReadiness>? OnReady { get; init; }

        public bool DestinationListed(int map) => Listed(map);
        public SessionTransferFacts TransferFacts(ChangeOfPlaceRequest request) { Calls.Add("facts"); return Facts; }
        public ChangeOfPlaceReadiness ReadyDestination(ChangeOfPlaceRequest request, SessionMapId destination, SessionMoveList moveList)
        {
            Calls.Add("ready " + destination.RuntimeMap);
            return OnReady?.Invoke() ?? Readiness;
        }

        public ChangeOfPlaceArrival Arrive(ChangeOfPlaceRequest request, SessionMapId destination, SessionMoveList moveList)
        {
            Calls.Add("arrive " + destination.RuntimeMap);
            MoveList = moveList;
            return new(request.Destination, NeighborDirection.YPlus, Edits);
        }

        public void CarrySpellRuns(SessionMoveList moveList) => SpellRunCarries.Add(moveList);

        public bool Run(Step step, ChangeOfPlaceRequest request)
        {
            Calls.Add(step.ToString());
            OnRun?.Invoke(step);
            return step != StopAt;
        }
    }

    private static ChangeOfPlaceSubmission Request(GameSessionOwner owner, int map, CellCoord cell, ChangeOfPlaceFlags flags = ChangeOfPlaceFlags.None,
        int seconds = 0, ChangeOfPlaceCaller caller = ChangeOfPlaceCaller.JumpPoint) =>
        owner.RequestHeroChangeOfPlace(caller, map, cell, flags, seconds, _ => true);

    private static GameSessionOwner Party(params string[] followers)
    {
        var owner = Create();
        foreach (var entity in followers) Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, entity).Disposition);
        return owner;
    }

    [TestMethod]
    public void A_jump_on_the_same_map_runs_its_plan_and_places_the_hero_in_one_revision()
    {
        using var owner = Create();
        var revision = owner.Version.Revision;
        var submitted = Request(owner, Exterior.RuntimeMap, Here);
        Assert.AreEqual((ChangeOfPlaceSubmitStatus.Stored, ChangeOfPlaceRequestEffects.None), (submitted.Status, submitted.Effects));
        Assert.AreEqual(revision, owner.Version.Revision, "a request writes the slot only");
        Assert.AreEqual(SessionEventKinds.PlayerEntity, owner.QueryChangeOfPlace()!.Entity);
        var host = new Host();
        var run = owner.ProcessChangeOfPlace(host);
        Assert.AreEqual((ChangeOfPlaceRunStatus.Completed, GameSessionFailure.None, false), (run.Status, run.Failure, run.RestoreDraw));
        CollectionAssert.AreEqual(new[] { Step.WaitOrders, Step.BuildMoveList, Step.ResetWallCheck, Step.RelocateObjects, Step.KeepPlayerId, Step.PreCache },
            run.Ran.ToArray());
        CollectionAssert.AreEqual(new[] { "WaitOrders", "facts", "BuildMoveList", "ResetWallCheck", "arrive 79", "RelocateObjects", "KeepPlayerId", "PreCache" },
            host.Calls, "the session's part of a step comes before the host's; a same-map change readies nothing");
        Assert.AreEqual(new ActorPlacement(Exterior, Here, NeighborDirection.YPlus), owner.QueryActor());
        Assert.AreEqual(revision + 1, owner.Version.Revision);
        Assert.IsNull(owner.QueryChangeOfPlace());
        Assert.IsFalse(owner.ChangeOfPlaceInProgress);
        Assert.AreSame(ChangeOfPlaceRun.Idle, owner.ProcessChangeOfPlace(host), "an empty slot does nothing");
    }

    [TestMethod]
    public void A_second_request_is_refused_while_the_heros_is_pending()
    {
        using var owner = Create();
        Assert.IsTrue(Request(owner, Exterior.RuntimeMap, Here).Accepted);
        var second = Request(owner, Cave.RuntimeMap, There);
        Assert.AreEqual(ChangeOfPlaceSubmitStatus.RefusedLocalPlayerPending, second.Status);
        Assert.AreEqual(Here, owner.QueryChangeOfPlace()!.Destination, "the first request keeps the slot (TR4)");
    }

    [TestMethod]
    public void An_unlisted_destination_is_refused_at_the_request_and_at_the_ping()
    {
        using var owner = Create();
        var unlisted = owner.RequestHeroChangeOfPlace(ChangeOfPlaceCaller.Script, Cave.RuntimeMap, There, ChangeOfPlaceFlags.BlankScreen, 0, _ => false);
        Assert.AreEqual((ChangeOfPlaceSubmitStatus.DestinationNotListed, ChangeOfPlaceRequestEffects.None), (unlisted.Status, unlisted.Effects));
        Assert.IsNull(owner.QueryChangeOfPlace());
        Assert.IsTrue(Request(owner, Cave.RuntimeMap, There).Accepted);
        var revision = owner.Version.Revision;
        var host = new Host { Listed = _ => false };
        var run = owner.ProcessChangeOfPlace(host);
        Assert.AreEqual((ChangeOfPlaceRunStatus.Refused, ChangeOfPlaceStatus.DestinationNotListed), (run.Status, run.Plan!.Status));
        Assert.AreEqual(0, host.Calls.Count);
        Assert.AreEqual((Start, revision), (owner.QueryActor(), owner.Version.Revision));
        Assert.IsNull(owner.QueryChangeOfPlace());
    }

    [TestMethod]
    public void Another_map_readies_the_destination_before_the_travellers_are_written_and_then_opens_it()
    {
        using var owner = Party(Virgil);
        Assert.IsTrue(Request(owner, Cave.RuntimeMap, There, caller: ChangeOfPlaceCaller.Script).Accepted);
        var host = new Host();
        var run = owner.ProcessChangeOfPlace(host);
        Assert.AreEqual(ChangeOfPlaceRunStatus.Completed, run.Status);
        CollectionAssert.AreEqual(new[]
        {
            "WaitOrders", "facts", "BuildMoveList", "ResetWallCheck", "ResetMagicTechAi", "CarryEvents", "ready 80", "arrive 80", "WriteTravellers",
            "KeepPlayerId", "FlushOldMap", "CloseOldMap", "OpenDestination", "MoveView", "RebindPlayer", "PreCache",
        }, host.Calls);
        Assert.AreEqual(new ActorPlacement(Cave, There, NeighborDirection.YPlus), owner.QueryActor());
        CollectionAssert.AreEqual(new[] { Virgil }, host.MoveList!.Travelling.ToArray(), "the follower travels with the hero");
    }

    // ARC-MAGIC-058 (MT9b, 0x45C720 → 0x44F7A0): the hero's change to another map hands the host the move list at CarryEvents, before the destination is readied, so the runs
    // that name an object that stays behind end on the old map; a jump on the same map carries nothing.
    [TestMethod]
    public void The_hero_s_change_to_another_map_asks_the_host_to_carry_the_spell_runs_with_the_move_list_and_a_same_map_jump_does_not()
    {
        using var owner = Party(Virgil, Sogg);
        Assert.IsTrue(Request(owner, Cave.RuntimeMap, There, caller: ChangeOfPlaceCaller.Script).Accepted);
        Host? self = null;
        var carriedAtStep = -1;
        var host = self = new Host { Facts = new SessionTransferFacts { NoFatigue = entity => entity == Sogg, WaitFacts = _ => null }, OnRun = step => { if (step == Step.CarryEvents) carriedAtStep = self!.SpellRunCarries.Count; } };
        Assert.AreEqual(ChangeOfPlaceRunStatus.Completed, owner.ProcessChangeOfPlace(host).Status);
        Assert.AreEqual(1, carriedAtStep, "asked before the host's part of the step");
        Assert.AreEqual(1, host.SpellRunCarries.Count);
        CollectionAssert.AreEqual(new[] { Virgil }, host.SpellRunCarries[0].Travelling.ToArray(), "the one that waits is not on the list");
        using var same = Create();
        Assert.IsTrue(Request(same, Exterior.RuntimeMap, Here).Accepted);
        var jump = new Host();
        Assert.AreEqual(ChangeOfPlaceRunStatus.Completed, same.ProcessChangeOfPlace(jump).Status);
        Assert.AreEqual(0, jump.SpellRunCarries.Count);
    }

    [TestMethod]
    public void A_destination_being_readied_suspends_the_change_until_the_resume()
    {
        using var owner = Party(Virgil);
        Assert.IsTrue(Request(owner, Cave.RuntimeMap, There).Accepted);
        var revision = owner.Version.Revision;
        var run = owner.ProcessChangeOfPlace(new Host { Readiness = ChangeOfPlaceReadiness.Pending });
        Assert.AreEqual((ChangeOfPlaceRunStatus.AwaitingDestination, false), (run.Status, run.RestoreDraw));
        Assert.AreEqual(Step.CarryEvents, run.Ran[^1]);
        Assert.AreEqual((Start, revision), (owner.QueryActor(), owner.Version.Revision), "nothing is written while the destination is readied");
        Assert.IsTrue(owner.ChangeOfPlaceInProgress);
        Assert.AreSame(ChangeOfPlaceRun.Idle, owner.ProcessChangeOfPlace(new Host()), "the ping is not re-entered");
        Assert.AreEqual(ChangeOfPlaceSubmitStatus.RefusedLocalPlayerPending, Request(owner, Exterior.RuntimeMap, Here).Status);
        var arrival = new Host();
        var resumed = owner.ResumeChangeOfPlace(arrival, ready: true);
        Assert.AreEqual(ChangeOfPlaceRunStatus.Completed, resumed.Status);
        CollectionAssert.AreEqual(new[] { "arrive 80", "WriteTravellers", "KeepPlayerId", "FlushOldMap", "CloseOldMap", "OpenDestination", "MoveView",
            "RebindPlayer", "PreCache" }, arrival.Calls, "the resume does not ready the destination again");
        Assert.AreEqual(Step.PreCache, resumed.Ran[^1]);
        Assert.AreEqual(Cave, owner.QueryActor().Map);
        Assert.AreEqual(revision + 1, owner.Version.Revision);
        Assert.IsFalse(owner.ChangeOfPlaceInProgress);
        Assert.AreSame(ChangeOfPlaceRun.Idle, owner.ResumeChangeOfPlace(arrival, ready: true), "nothing waits any more");
    }

    [TestMethod]
    public void A_destination_that_is_not_ready_leaves_the_session_on_its_map()
    {
        foreach (var failedAtOnce in new[] { true, false })
        {
            using var owner = Party(Virgil);
            Assert.IsTrue(Request(owner, Cave.RuntimeMap, There, ChangeOfPlaceFlags.FadeIn | ChangeOfPlaceFlags.BlankScreen).Accepted);
            var revision = owner.Version.Revision;
            Assert.AreSame(ChangeOfPlaceRun.Idle, owner.ResumeChangeOfPlace(new Host(), ready: false), "no change waits before the ping");
            ChangeOfPlaceRun run;
            if (failedAtOnce) run = owner.ProcessChangeOfPlace(new Host { Readiness = ChangeOfPlaceReadiness.Failed });
            else
            {
                Assert.AreEqual(ChangeOfPlaceRunStatus.AwaitingDestination, owner.ProcessChangeOfPlace(new Host { Readiness = ChangeOfPlaceReadiness.Pending }).Status);
                run = owner.ResumeChangeOfPlace(new Host(), ready: false);
            }
            Assert.AreEqual((ChangeOfPlaceRunStatus.Stopped, Step.WriteTravellers, GameSessionFailure.ChangeOfPlaceStopped),
                (run.Status, run.StoppedAt, run.Failure));
            Assert.IsTrue(run.RestoreDraw, "the ping gives the draw back after a failed local blanked change (CP2)");
            Assert.IsFalse(run.Ran.Contains(Step.FadeInScreen), "a change that did not run to its end does not fade in (CP7)");
            Assert.AreEqual((Start, revision), (owner.QueryActor(), owner.Version.Revision));
            Assert.IsNull(owner.QueryChangeOfPlace());
            Assert.IsFalse(owner.ChangeOfPlaceInProgress);
        }
    }

    [TestMethod]
    public void A_fade_and_teleport_advances_the_clock_on_the_old_map_and_fades_in_at_the_end()
    {
        using var owner = Create();
        var before = owner.QueryClockSnapshot().GameTimestampMs;
        const ChangeOfPlaceFlags fade = ChangeOfPlaceFlags.FadeOut | ChangeOfPlaceFlags.FadeIn | ChangeOfPlaceFlags.AdvanceClock
            | ChangeOfPlaceFlags.SoundEffect | ChangeOfPlaceFlags.FirstMovie | ChangeOfPlaceFlags.BlankScreen;
        var submitted = Request(owner, Cave.RuntimeMap, There, fade, seconds: 3600, caller: ChangeOfPlaceCaller.Script);
        Assert.AreEqual(ChangeOfPlaceRequestEffects.StartFadeOut | ChangeOfPlaceRequestEffects.BlankScreen, submitted.Effects, "CP1: at once for the hero");
        var host = new Host();
        var run = owner.ProcessChangeOfPlace(host);
        Assert.AreEqual((ChangeOfPlaceRunStatus.Completed, true), (run.Status, run.RestoreDraw));
        CollectionAssert.AreEqual(new[] { "FadeOutScreen", "PlaySound", "PlayFirstMovie", "AdvanceClocks", "WaitOrders" }, host.Calls.Take(5).ToArray());
        Assert.AreEqual(Step.FadeInScreen, run.Ran[^1]);
        Assert.AreEqual(before + 3_600_000, owner.QueryClockSnapshot().GameTimestampMs);
        Assert.AreEqual(Cave, owner.QueryActor().Map);
    }

    [TestMethod]
    public void A_host_step_that_fails_stops_the_change_there()
    {
        using var owner = Create();
        Assert.IsTrue(Request(owner, Cave.RuntimeMap, There, ChangeOfPlaceFlags.FadeIn).Accepted);
        var run = owner.ProcessChangeOfPlace(new Host { StopAt = Step.OpenDestination });
        Assert.AreEqual((ChangeOfPlaceRunStatus.Stopped, Step.OpenDestination, GameSessionFailure.ChangeOfPlaceStopped), (run.Status, run.StoppedAt, run.Failure));
        Assert.AreEqual(Step.CloseOldMap, run.Ran[^1]);
        Assert.AreEqual(Cave, owner.QueryActor().Map, "the payload came before the open, as in the original (RL30c)");
        Assert.IsNull(owner.QueryChangeOfPlace());
    }

    [TestMethod]
    public void A_payload_the_session_refuses_stops_the_change_with_nothing_written()
    {
        using var owner = Create();
        Assert.IsTrue(Request(owner, Exterior.RuntimeMap, Here).Accepted);
        var revision = owner.Version.Revision;
        var key = MapChangeKey.ForEntity("oid:rock", "obj.off");
        var host = new Host { Edits = [new(Exterior, key, 1), new(Exterior, key, 0)] };
        var run = owner.ProcessChangeOfPlace(host);
        Assert.AreEqual((ChangeOfPlaceRunStatus.Stopped, Step.RelocateObjects, GameSessionFailure.InvalidChangeKey), (run.Status, run.StoppedAt, run.Failure));
        Assert.IsFalse(host.Calls.Contains("RelocateObjects"), "the host's part of a refused step does not run");
        Assert.AreEqual((Start, revision), (owner.QueryActor(), owner.Version.Revision));
    }

    [TestMethod]
    public void The_wait_orders_follow_the_request_flag()
    {
        var facts = new SessionTransferFacts { NoFatigue = entity => entity == Sogg, WaitFacts = _ => null };
        using (var owner = Party(Virgil, Sogg))
        {
            Assert.IsTrue(Request(owner, Cave.RuntimeMap, There).Accepted);
            var host = new Host { Facts = facts };
            Assert.AreEqual(ChangeOfPlaceRunStatus.Completed, owner.ProcessChangeOfPlace(host).Status);
            CollectionAssert.AreEqual(new[] { Virgil }, host.MoveList!.Travelling.ToArray());
            Assert.AreEqual(Sogg, host.MoveList.Waiting.Single().Entity);
            Assert.AreEqual(CompanionStanding.Waiting, owner.QueryCompanion(Sogg).Standing, "the unconscious follower is ordered to wait");
        }
        using (var owner = Party(Virgil, Sogg))
        {
            Assert.IsTrue(Request(owner, Cave.RuntimeMap, There, ChangeOfPlaceFlags.NoWaitOrders).Accepted);
            var host = new Host { Facts = facts };
            var run = owner.ProcessChangeOfPlace(host);
            Assert.IsFalse(run.Ran.Contains(Step.WaitOrders));
            CollectionAssert.AreEqual(new[] { Virgil, Sogg }, host.MoveList!.Travelling.ToArray(), "flag 0x100: nobody waits");
        }
    }

    [TestMethod]
    public void The_slot_is_not_saved()
    {
        using var owner = Create();
        Assert.IsTrue(Request(owner, Cave.RuntimeMap, There).Accepted);
        using (var restored = Restore(owner.Capture()))
        {
            Assert.IsNull(restored.QueryChangeOfPlace());
            Assert.AreSame(ChangeOfPlaceRun.Idle, restored.ProcessChangeOfPlace(new Host()));
        }
        Assert.AreEqual(ChangeOfPlaceRunStatus.AwaitingDestination, owner.ProcessChangeOfPlace(new Host { Readiness = ChangeOfPlaceReadiness.Pending }).Status);
        using var waiting = Restore(owner.Capture());
        Assert.IsFalse(waiting.ChangeOfPlaceInProgress, "a change waiting for its destination is not saved either");
        Assert.AreEqual(Start, waiting.QueryActor());
    }

    [TestMethod]
    public void A_request_made_while_the_change_runs_is_refused()
    {
        using var owner = Create();
        Assert.IsTrue(Request(owner, Exterior.RuntimeMap, Here).Accepted);
        ChangeOfPlaceSubmission? nested = null;
        var run = owner.ProcessChangeOfPlace(new Host { OnRun = step => { if (step == Step.PreCache) nested = Request(owner, Cave.RuntimeMap, There); } });
        Assert.AreEqual(ChangeOfPlaceRunStatus.Completed, run.Status);
        Assert.AreEqual(ChangeOfPlaceSubmitStatus.RefusedLocalPlayerPending, nested!.Status);
        Assert.IsNull(owner.QueryChangeOfPlace(), "the nested request is not kept");
        Assert.AreEqual(Exterior, owner.QueryActor().Map);
    }

    [TestMethod]
    public void A_resumed_payload_the_session_refuses_stops_the_change_with_nothing_written()
    {
        using var owner = Create();
        Assert.IsTrue(Request(owner, Cave.RuntimeMap, There).Accepted);
        Assert.AreEqual(ChangeOfPlaceRunStatus.AwaitingDestination, owner.ProcessChangeOfPlace(new Host { Readiness = ChangeOfPlaceReadiness.Pending }).Status);
        var revision = owner.Version.Revision;
        var key = MapChangeKey.ForEntity("oid:rock", "obj.off");
        var run = owner.ResumeChangeOfPlace(new Host { Edits = [new(Cave, key, 1), new(Cave, key, 0)] }, ready: true);
        Assert.AreEqual((ChangeOfPlaceRunStatus.Stopped, Step.WriteTravellers, GameSessionFailure.InvalidChangeKey), (run.Status, run.StoppedAt, run.Failure));
        Assert.AreEqual((Start, revision), (owner.QueryActor(), owner.Version.Revision));
        Assert.IsFalse(owner.ChangeOfPlaceInProgress);
    }

    [TestMethod]
    public void A_clock_advance_made_on_the_old_map_stays_when_the_destination_fails()
    {
        using var owner = Create();
        var before = owner.QueryClockSnapshot().GameTimestampMs;
        Assert.IsTrue(Request(owner, Cave.RuntimeMap, There, ChangeOfPlaceFlags.AdvanceClock, seconds: 60).Accepted);
        var run = owner.ProcessChangeOfPlace(new Host { Readiness = ChangeOfPlaceReadiness.Failed });
        Assert.AreEqual((ChangeOfPlaceRunStatus.Stopped, Step.WriteTravellers), (run.Status, run.StoppedAt));
        Assert.AreEqual(before + 60_000, owner.QueryClockSnapshot().GameTimestampMs, "the advance comes before the payload, as in the original");
        Assert.AreEqual(Start, owner.QueryActor());
    }

    [TestMethod]
    public void A_host_that_resumes_from_inside_its_readying_ends_the_change_once()
    {
        using var owner = Create();
        Assert.IsTrue(Request(owner, Cave.RuntimeMap, There).Accepted);
        ChangeOfPlaceRun? inner = null;
        var host = new Host();
        var outer = owner.ProcessChangeOfPlace(new Host
        {
            OnReady = () => { inner = owner.ResumeChangeOfPlace(host, ready: true); return ChangeOfPlaceReadiness.Pending; },
        });
        Assert.AreEqual(ChangeOfPlaceRunStatus.Completed, inner!.Status);
        Assert.AreSame(inner, outer, "the outer call reports how the change ended");
        Assert.IsFalse(owner.ChangeOfPlaceInProgress);
        Assert.AreEqual(Cave, owner.QueryActor().Map);
    }

    [TestMethod]
    public void A_host_that_throws_frees_the_slot()
    {
        using var owner = Create();
        Assert.IsTrue(Request(owner, Cave.RuntimeMap, There).Accepted);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            owner.ProcessChangeOfPlace(new Host { OnRun = step => { if (step == Step.CloseOldMap) throw new InvalidOperationException("host"); } }));
        Assert.IsFalse(owner.ChangeOfPlaceInProgress);
        Assert.IsNull(owner.QueryChangeOfPlace());
        Assert.IsTrue(Request(owner, Exterior.RuntimeMap, Here).Accepted, "the next request is taken");
    }
}
