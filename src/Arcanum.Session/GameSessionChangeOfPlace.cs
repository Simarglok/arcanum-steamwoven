using System.Collections.Immutable;
using Arcanum.Simulation.ChangeOfPlace;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Movement;

namespace Arcanum.Session;

/// <summary>Whether the host has another map's destination ready for the travellers (ARC-DEV-113, decision CD6 of the change-of-place note).</summary>
public enum ChangeOfPlaceReadiness
{
    /// <summary>The destination can take the travellers now; the change goes on in this call.</summary>
    Ready,
    /// <summary>The host is readying the destination over later frames (the viewer loads the map's scene); the change waits for
    /// <see cref="GameSessionOwner.ResumeChangeOfPlace"/>.</summary>
    Pending,
    /// <summary>The destination cannot be readied; the change stops before anything is written.</summary>
    Failed,
}

/// <summary>Where the hero stands after the payload, with its facing, and the host's edits for the bodies of the followers that travel
/// or stay (the viewer's companion placement rows). The session applies them with the hero's placement and the move list in one revision.</summary>
public sealed record ChangeOfPlaceArrival(CellCoord Cell, NeighborDirection Facing, IReadOnlyList<MapChangeEdit> Changes);

/// <summary>
/// ARC-DEV-113 (MIG11 slice S2): what the session asks the host while it runs a change of place. The session carries out its own steps
/// (the clock advance, the move list with the wait orders, the payload with the event carry, the identity kept) and calls
/// <see cref="Run"/> after its part of every step, so the host does the presentation and the scene work (fades, sound, movies, the
/// flush, close and open of the scene, the view, the player's rebinding, the pre-cache). Caller-serialized, called only from inside
/// <see cref="GameSessionOwner.ProcessChangeOfPlace"/> and <see cref="GameSessionOwner.ResumeChangeOfPlace"/>.
/// </summary>
public interface IChangeOfPlaceHost
{
    /// <summary>Whether the host can open the map (the map list's answer; the current map always can).</summary>
    bool DestinationListed(int map);

    /// <summary>The host's facts for the move list (unconscious followers, wait timers). The session sets the request's "no wait orders".</summary>
    SessionTransferFacts TransferFacts(ChangeOfPlaceRequest request);

    /// <summary>Another map only, before the travellers are written there: readies the destination (see <see cref="ChangeOfPlaceReadiness"/>);
    /// <paramref name="moveList"/> is the one the payload will apply.</summary>
    ChangeOfPlaceReadiness ReadyDestination(ChangeOfPlaceRequest request, SessionMapId destination, SessionMoveList moveList);

    /// <summary>The hero's arrival and the followers' body edits for the payload; <paramref name="moveList"/> says who travels.</summary>
    ChangeOfPlaceArrival Arrive(ChangeOfPlaceRequest request, SessionMapId destination, SessionMoveList moveList);

    /// <summary>The host's part of <paramref name="step"/>, after the session's. False stops the change there (as a failed step of the
    /// original's process leaves at once, RL30): no later step runs, so no fade-in.</summary>
    bool Run(ChangeOfPlaceStep step, ChangeOfPlaceRequest request);

    /// <summary>
    /// ARC-MAGIC-058 (MT9b): the player's change to another map, at <see cref="ChangeOfPlaceStep.CarryEvents"/>, before the host's part of the step: the spell runs
    /// that name an object that does not travel end (0x44F7A0, <see cref="Arcanum.Simulation.Magic.SpellRunTravelRules"/>); <paramref name="moveList"/> says who travels. A host with a spell
    /// cast host ends them through <see cref="SessionSpellCaster.EndRunsLeavingMap"/>; the default does nothing.
    /// </summary>
    void CarrySpellRuns(SessionMoveList moveList) { }
}

/// <summary>What one call of <see cref="GameSessionOwner.ProcessChangeOfPlace"/> or <see cref="GameSessionOwner.ResumeChangeOfPlace"/> did.</summary>
public enum ChangeOfPlaceRunStatus
{
    /// <summary>No request was pending (or none was waiting for its destination).</summary>
    Idle,
    /// <summary>The change ran every step of its plan; the slot is empty.</summary>
    Completed,
    /// <summary>The host is readying another map; the slot stays in progress (requests are refused) until the resume.</summary>
    AwaitingDestination,
    /// <summary>A step stopped the change (<see cref="ChangeOfPlaceRun.StoppedAt"/>); the slot is empty and no later step ran.</summary>
    Stopped,
    /// <summary>The plan refused the request (its destination is no longer listed); nothing ran, the slot is empty.</summary>
    Refused,
}

/// <summary>
/// The result of a change-of-place call: the request and its plan, the steps run so far in order, the step that stopped it, the session
/// failure (the payload's refusal, the clock's, or <see cref="GameSessionFailure.ChangeOfPlaceStopped"/> for a host stop and a destination
/// that could not be readied) and whether the host gives the map draw back (the ping's rule, CP2: after the change whatever its end, for
/// a local-player request that blanked the screen).
/// </summary>
public sealed record ChangeOfPlaceRun(
    ChangeOfPlaceRunStatus Status,
    ChangeOfPlaceRequest? Request,
    ChangeOfPlacePlan? Plan,
    ImmutableArray<ChangeOfPlaceStep> Ran,
    ChangeOfPlaceStep? StoppedAt,
    GameSessionFailure Failure,
    bool RestoreDraw)
{
    /// <summary>Nothing to do.</summary>
    public static ChangeOfPlaceRun Idle { get; } = new(ChangeOfPlaceRunStatus.Idle, null, null, [], null, GameSessionFailure.None, false);
}

public sealed partial class GameSessionOwner
{
    // The one request slot (0x601858 with its pending and in-progress flags). Not saved: a change is never in flight across a save.
    private ChangeOfPlaceSlot _changeOfPlace = ChangeOfPlaceSlot.Empty;
    // The change being run while the host readies another map: its plan, the next step and the move list built for it.
    private ChangeInFlight? _changeInFlight;

    private sealed class ChangeInFlight(ChangeOfPlaceRequest request, ChangeOfPlacePlan plan)
    {
        internal ChangeOfPlaceRequest Request { get; } = request;
        internal ChangeOfPlacePlan Plan { get; } = plan;
        internal int Next { get; set; }
        internal SessionMoveList? MoveList { get; set; }
        internal bool Waiting { get; set; }
        // How the change ended (set by EndChange); a resume the host made from inside ReadyDestination ends it before that call returns.
        internal ChangeOfPlaceRun? Result { get; set; }
        internal ImmutableArray<ChangeOfPlaceStep>.Builder Ran { get; } = ImmutableArray.CreateBuilder<ChangeOfPlaceStep>();
    }

    /// <summary>The request in the slot (pending or being processed), or null. The slot is not part of a snapshot.</summary>
    public ChangeOfPlaceRequest? QueryChangeOfPlace() { ThrowIfDisposed(); return _changeOfPlace.Pending; }

    /// <summary>True while a change runs over several calls (its destination is being readied).</summary>
    public bool ChangeOfPlaceInProgress { get { ThrowIfDisposed(); return _changeOfPlace.Processing; } }

    /// <summary>
    /// ARC-DEV-113: the hero's request for a change of place (a jump point, a script's teleport of the player; the original's 0x4D3380
    /// with the local player character as the object, so its request is refused only while another local-player request is pending and
    /// itself refuses every later one until it is processed). The rules of <see cref="ChangeOfPlaceRules.Submit"/> decide; the destination
    /// map −1 is the current map. The effects returned (<see cref="ChangeOfPlaceRequestEffects"/>) are the host's to show at once. A
    /// request writes nothing but the slot: no revision.
    /// </summary>
    public ChangeOfPlaceSubmission RequestHeroChangeOfPlace(ChangeOfPlaceCaller caller, int destinationMap, CellCoord destination,
        ChangeOfPlaceFlags flags, int advanceSeconds, Func<int, bool> destinationListed)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(destinationListed);
        var request = new ChangeOfPlaceRequest(SessionEventKinds.PlayerEntity, caller, destinationMap, destination, flags, advanceSeconds,
            IsPlayerCharacter: true, IsLocalPlayer: true);
        var submission = ChangeOfPlaceRules.Submit(_changeOfPlace, request, new(_actor.Map.RuntimeMap, destinationListed));
        _changeOfPlace = submission.Slot;
        EmptyCacheAfterSubmit(request, submission);
        return submission;
    }

    /// <summary>
    /// ARC-DEV-113: the host's frame call that processes the slot (the original's ping 0x4D3330; until the frame step of MIG12 calls
    /// it once per frame, the viewer calls it in the frame of its own request, decision CD7). An empty slot, or a change already waiting for its destination, does nothing. Otherwise the request's plan
    /// (<see cref="ChangeOfPlaceRules.Plan"/>) runs step by step: the session's part of a step, then the host's
    /// (<see cref="IChangeOfPlaceHost.Run"/>). The session's parts: <see cref="ChangeOfPlaceStep.AdvanceClocks"/> moves game time by the
    /// request's seconds (as an explicit wait, so a turn-based round does not hold it; since MIG12 S3 that is a jump of game and
    /// animation time together); <see cref="ChangeOfPlaceStep.BuildMoveList"/> builds the move list with its wait orders (the request's "no wait orders"
    /// honoured); the payload (<see cref="ChangeOfPlaceStep.RelocateObjects"/> or <see cref="ChangeOfPlaceStep.WriteTravellers"/>) places
    /// the hero with the host's arrival and applies the move list (wait orders, FM6's clearing, the carry of the travellers' events) in one
    /// revision, as <see cref="ApplyChanges"/> does. Another map's payload first asks the host to ready the destination
    /// (<see cref="IChangeOfPlaceHost.ReadyDestination"/>): a destination that cannot be readied stops the change before its payload is
    /// written, so the session stays on its map (GI7, decision CD6; a clock advance already made on the old map stays, as in the
    /// original); one being readied suspends the change until <see cref="ResumeChangeOfPlace"/>. The slot is emptied when the change ends,
    /// however it ends: a host that throws ends it too (stopped) before the exception goes on, so the slot is never left in progress.
    /// <para>
    /// ARC-DEV-114: a request of a mover that is not the hero (<see cref="RequestChangeOfPlace"/>) has no player character in its move list, so
    /// its plan opens no destination: the move list is the mover alone, the destination is not readied, and the payload is the host's placement
    /// edits with the mover's departure (<see cref="GameSessionOwner"/>'s <c>MoverPayload</c>). The hero's own change ends on the open map with the
    /// 3 × 3 pre-cache of the sector cache when the session holds one.
    /// </para>
    /// </summary>
    public ChangeOfPlaceRun ProcessChangeOfPlace(IChangeOfPlaceHost host)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(host);
        if (_changeInFlight is not null) return ChangeOfPlaceRun.Idle;
        var begin = ChangeOfPlaceRules.Begin(_changeOfPlace);
        if (begin.Request is not { } request) return ChangeOfPlaceRun.Idle;
        _changeOfPlace = begin.Slot;
        // The hero's request has a player character in its move list; a non-player mover (S3) has none, so the destination is not opened.
        var plan = ChangeOfPlaceRules.Plan(request, new(_actor.Map.RuntimeMap, host.DestinationListed), moveListHasPlayerCharacter: request.IsPlayerCharacter);
        var flight = new ChangeInFlight(request, plan);
        if (plan.Status != ChangeOfPlaceStatus.Planned) return EndChange(flight, ChangeOfPlaceRunStatus.Refused, null, GameSessionFailure.None);
        _changeInFlight = flight;
        return Guarded(flight, host, destinationReady: false);
    }

    /// <summary>
    /// ARC-DEV-113: the host's destination of a suspended change is <paramref name="ready"/> or not. Ready: the payload is written and the
    /// remaining steps run as in <see cref="ProcessChangeOfPlace"/>. Not ready: the change stops at the payload with none of it written.
    /// Without a suspended change it does nothing.
    /// </summary>
    public ChangeOfPlaceRun ResumeChangeOfPlace(IChangeOfPlaceHost host, bool ready)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(host);
        if (_changeInFlight is not { Waiting: true } flight) return ChangeOfPlaceRun.Idle;
        flight.Waiting = false;
        if (!ready) return EndChange(flight, ChangeOfPlaceRunStatus.Stopped, flight.Plan.Steps[flight.Next], GameSessionFailure.ChangeOfPlaceStopped);
        return Guarded(flight, host, destinationReady: true);
    }

    // A host's exception ends the change (stopped) and goes on: the slot is never left in progress.
    private ChangeOfPlaceRun Guarded(ChangeInFlight flight, IChangeOfPlaceHost host, bool destinationReady)
    {
        try { return RunChange(flight, host, destinationReady); }
        catch
        {
            if (ReferenceEquals(_changeInFlight, flight)) EndChange(flight, ChangeOfPlaceRunStatus.Stopped, flight.Plan.Steps[flight.Next], GameSessionFailure.ChangeOfPlaceStopped);
            throw;
        }
    }

    private ChangeOfPlaceRun RunChange(ChangeInFlight flight, IChangeOfPlaceHost host, bool destinationReady)
    {
        var request = flight.Request;
        var destination = new SessionMapId(flight.Plan.Destination);
        while (flight.Next < flight.Plan.Steps.Length)
        {
            var step = flight.Plan.Steps[flight.Next];
            switch (step)
            {
                case ChangeOfPlaceStep.AdvanceClocks:
                    if (request.AdvanceMilliseconds > 0)
                    {
                        var advanced = AdvanceClock(_clock.QueryClockSnapshot().Token, request.AdvanceMilliseconds, ClockAdvanceReason.ExplicitWait);
                        if (advanced.Disposition == ClockMutationDisposition.Rejected)
                            return EndChange(flight, ChangeOfPlaceRunStatus.Stopped, step, GameSessionFailure.ClockRejected);
                    }
                    break;
                case ChangeOfPlaceStep.BuildMoveList when !request.IsPlayerCharacter:
                    // A non-player mover (S3): the session models no NPC leader (TM10), so the list is the mover alone, with its inventory, which
                    // stays with it; a host that knows followers of the NPC cannot be served and stops the change before anything moves.
                    // A member of the hero's party sent away alone would stay in the party order with its place on another map (NOT READY: the party's
                    // own transfer assumes its members stand where the hero does), so it is refused here as a group is.
                    if (_party.Order.Contains(request.Entity) ||
                        (host.TransferFacts(request) ?? new()).FollowersOf?.Invoke(request.Entity) is { Count: > 0 })
                        return EndChange(flight, ChangeOfPlaceRunStatus.Stopped, step, GameSessionFailure.ChangeOfPlaceStopped);
                    flight.MoveList = new([], [request.Entity]);
                    break;
                case ChangeOfPlaceStep.BuildMoveList:
                    flight.MoveList = QueryTransferMoveList((host.TransferFacts(request) ?? new()) with
                    {
                        NoWaitOrders = (request.Flags & ChangeOfPlaceFlags.NoWaitOrders) != 0,
                    });
                    break;
                case ChangeOfPlaceStep.PreCache:
                    PreCacheArrival();
                    break;
                case ChangeOfPlaceStep.WriteTravellers when !destinationReady && request.IsPlayerCharacter:
                    // Waiting before the call: a host that readies the destination at once may resume from inside it.
                    flight.Waiting = true;
                    var readiness = host.ReadyDestination(request, destination, flight.MoveList ?? SessionMoveList.Empty);
                    if (flight.Result is { } resumed) return resumed;
                    if (readiness == ChangeOfPlaceReadiness.Pending)
                        return new(ChangeOfPlaceRunStatus.AwaitingDestination, request, flight.Plan, flight.Ran.ToImmutable(), null,
                            GameSessionFailure.None, false);
                    flight.Waiting = false;
                    if (readiness != ChangeOfPlaceReadiness.Ready)
                        return EndChange(flight, ChangeOfPlaceRunStatus.Stopped, step, GameSessionFailure.ChangeOfPlaceStopped);
                    if (Payload(flight, host, destination) is var written && written != GameSessionFailure.None)
                        return EndChange(flight, ChangeOfPlaceRunStatus.Stopped, step, written);
                    break;
                case ChangeOfPlaceStep.WriteTravellers:
                case ChangeOfPlaceStep.RelocateObjects:
                    if (Payload(flight, host, destination) is var placed && placed != GameSessionFailure.None)
                        return EndChange(flight, ChangeOfPlaceRunStatus.Stopped, step, placed);
                    break;
                case ChangeOfPlaceStep.CarryEvents:
                    // ARC-MAGIC-058: the runs that do not travel end here, on the old map (0x45C720 before the map close); the events and the runs that travel are written with the payload.
                    host.CarrySpellRuns(flight.MoveList ?? SessionMoveList.Empty);
                    break;
                // WaitOrders and the events of CarryEvents are written with the payload (one revision); KeepPlayerId keeps the hero, which the session
                // always resolves. The other steps are the host's alone.
            }
            if (!host.Run(step, request)) return EndChange(flight, ChangeOfPlaceRunStatus.Stopped, step, GameSessionFailure.ChangeOfPlaceStopped);
            flight.Ran.Add(step);
            flight.Next++;
        }
        return EndChange(flight, ChangeOfPlaceRunStatus.Completed, null, GameSessionFailure.None);
    }

    // The payload: the hero at the host's arrival on the destination, the host's body edits and the move list, in one revision.
    private GameSessionFailure Payload(ChangeInFlight flight, IChangeOfPlaceHost host, SessionMapId destination)
    {
        if (!flight.Request.IsPlayerCharacter) return MoverPayload(flight, host, destination);
        var moveList = flight.MoveList ?? SessionMoveList.Empty;
        if (host.Arrive(flight.Request, destination, moveList) is not { Changes: not null } arrival) return GameSessionFailure.ChangeOfPlaceStopped;
        var placed = ApplyChanges(Version, new ActorPlacement(destination, arrival.Cell, arrival.Facing), arrival.Changes, flight.MoveList);
        return placed.Disposition == GameSessionDisposition.Rejected ? placed.Failure : GameSessionFailure.None;
    }

    private ChangeOfPlaceRun EndChange(ChangeInFlight flight, ChangeOfPlaceRunStatus status, ChangeOfPlaceStep? stoppedAt, GameSessionFailure failure)
    {
        _changeInFlight = null;
        flight.Waiting = false;
        var end = ChangeOfPlaceRules.End(_changeOfPlace);
        _changeOfPlace = end.Slot;
        return flight.Result = new(status, flight.Request, flight.Plan, flight.Ran.ToImmutable(), stoppedAt, failure, end.RestoreDraw);
    }
}
