using Arcanum.Data.Area;
using Arcanum.Data.Audio;
using Arcanum.Presentation.Audio;
using Arcanum.Simulation.Locks;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.Simulation.Skills;
using Arcanum.Simulation.Sound;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-WORLD-051: doors and windows at runtime in map mode. The state of each portal lives in the session (ARC-WORLD-050):
// the steps and the click route admit a portal by its current state, a step that crosses a closed portal it may pass opens
// it, and a left press on a portal uses it (walking within use range first): an open one closes, a closed one opens after
// the player's open attempt. The frame steps play at the ART's rate (1000 / fps ms, ledger LK10, LK11) with the portal's
// open, close or locked sound; the session records the final frame at once.
public partial class TerrainViewerApp
{
    private AreaPortals? _areaPortals;
    private object? _areaPortalsStamp;
    // A portal's frame steps still to be drawn: the frames in order and the time the first was shown.
    private readonly Dictionary<AreaObjectKey, (int[] Frames, ulong StartMs, long StepMs)> _portalAnimations = new();
    private Dictionary<AreaObjectKey, AreaDrawCommand> _portalBodies = [];
    private AreaObjectKey? _portalUseAfterRoute;
    // A use pressed while the actor is mid-step: it runs once the step has settled (never closing a door under the actor's step).
    private AreaObjectKey? _portalUseWhenSettled;
    private string _portalNotice = "";
    // ARC-SKILLS-006: lockpicks chosen in the inventory, waiting for a press on a door or window; and the lockpicks of the press
    // whose walk or settle is pending, so the pending use picks the lock instead.
    private string? _lockpickArmed;
    private string? _portalPickWith;

    /// <summary>Binds the shown map's portals to the session and the actor's steps (map mode with a session).</summary>
    private void BindAreaPortals()
    {
        if (_readyMapName is null || _readyArea is not { } area || _areaSession is not { } session || _worldActor.Session is not AreaGridSession grid)
        {
            _areaPortals = null; _areaPortalsStamp = null; _portalAnimations.Clear(); _portalBodies = []; _portalUseAfterRoute = null; _portalUseWhenSettled = null;
            return;
        }
        var stamp = (area, session);
        if (!Equals(stamp, _areaPortalsStamp))
        {
            _areaPortals = AreaPortals.From(area.Snapshot, area.Plan);
            _areaPortalsStamp = stamp; _portalAnimations.Clear(); _portalBodies = []; _portalUseAfterRoute = null; _portalUseWhenSettled = null;
            GD.Print($"ARC-WORLD-051 portals bound map={_readyMapName} portals={_areaPortals.All.Count} multi-frame={_areaPortals.All.Count(p => p.Frames.Count > 1)}");
        }
        var portals = _areaPortals!;
        grid.Portals = () => portals.Passages(session);
    }

    /// <summary>Per advance: the closed portals every started step crosses and may pass open, animated from frame 0.</summary>
    private void NoteMapPortals(GridAdvance advance)
    {
        if (_areaPortals is not { } portals || _areaSession is not { } session) return;
        foreach (var evidence in advance.AreaEvidence)
        {
            if ((evidence.Live?.Disposition ?? evidence.Assessment.Disposition) != Arcanum.Data.Traversal.CardinalTraversalDisposition.Open) continue;
            foreach (var portal in portals.OpenCrossed(session, evidence.Assessment))
            {
                Animate(portal, 0, portal.OpenFrame, opening: true);
                PlayPortalSound(portal, OpenableSoundAction.Open);
                GD.Print($"ARC-WORLD-051 portal opened by step entity={portal.Entity} frame={portal.OpenFrame}");
            }
        }
    }

    /// <summary>A guarded left press on a door or window: a use when it is within use range, else a walk to a cell in range
    /// and the use on arrival. Returns true when the click was on a portal.</summary>
    private bool TryAreaPortalUse(Vector2 screen)
    {
        // Armed lockpicks go to this press: on a portal they pick its lock, elsewhere the pick is cancelled.
        var pick = _lockpickArmed; _lockpickArmed = null;
        if (_areaPortals is not { } portals || _areaSession is null || _worldActor.Session is not AreaGridSession session
            || AreaObjectAtScreen(screen) is not { } hit || !portals.TryGet(hit, out var portal))
        {
            if (pick is not null) { _portalNotice = "pick a lock cancelled: not a door or window"; RefreshAreaStatus(); }
            return false;
        }
        CancelTalkApproach("a use click replaced it"); CancelInventoryApproach("a use click replaced it"); CancelAreaUseApproach("a use click replaced it");
        var row = _readyArea!.Snapshot.Objects.Single(o => o.Key == hit);
        _portalPickWith = pick;
        var range = pick is null ? AreaUseRule.UseRange : AreaPortals.PickRange;
        if (PortalInReach(session, row, range))
        {
            StopWorldRoute("ROUTE_REPLACED_BY_USE");
            if (session.State.Phase == MovementPhase.Settled) UsePortal(portal);
            else { _portalUseWhenSettled = hit; _portalNotice = $"{PortalName(portal)}: after this step"; RefreshAreaStatus(); }
            return true;
        }
        if (WorldRoute(session) is { } route && ApproachCell(session, row.WorldX!.Value, row.WorldY!.Value, range) is { } goal)
        {
            StopWorldRoute("ROUTE_REPLACED_BY_USE");
            route.SetTarget(goal, WorldTime); _portalUseAfterRoute = hit;
            _worldRouteDiagnostic = DescribeWorldRoute(route); _worldRouteClick = $"{goal.X},{goal.Y} walk to the door";
            _portalNotice = $"walking to {PortalName(portal)}";
        }
        else { _portalNotice = $"{PortalName(portal)}: no cell in reach"; _portalPickWith = null; }
        RefreshAreaStatus();
        return true;
    }

    /// <summary>Drops a walk to a door and a use waiting for the step to settle (another click, a reset or a reload replaced it),
    /// and armed lockpicks unless <paramref name="keepArmed"/> (a world press, which the armed lockpicks take).</summary>
    private void CancelPortalApproach(string why, bool keepArmed = false)
    {
        if (!keepArmed) _lockpickArmed = null;
        if (_portalUseAfterRoute is null && _portalUseWhenSettled is null) return;
        _portalUseAfterRoute = null; _portalUseWhenSettled = null; _portalPickWith = null; _portalNotice = "walk to the door cancelled: " + why;
    }

    // The walk-to-use guards (as walk to talk): no lifecycle, reset or reload, and input not quarantined or modal.
    private bool PortalUseAdmitted(out string why)
    {
        why = _worldLifecycleSession is not null || _worldResetPending || _worldReloadPending ? "reset/reload"
            : !_worldFocused || WorldModal || WorldUiFocus ? "input quarantined/modal" : "";
        return why.Length == 0;
    }

    private static bool PortalInReach(AreaGridSession session, AreaObjectRecord row, int range = AreaUseRule.UseRange)
    {
        var actor = session.Chart.ToWorld(session.State.CommittedCell);
        return Math.Max(Math.Abs(actor.X - row.WorldX!.Value), Math.Abs(actor.Y - row.WorldY!.Value)) <= range;
    }

    // The range of the pending use: the pick-lock goal's 1, else the use range.
    private int PendingPortalRange => _portalPickWith is null ? AreaUseRule.UseRange : AreaPortals.PickRange;

    /// <summary>ARC-SKILLS-006: the hero picks the portal's lock with the chosen lockpicks (the session's answer on the HUD). The
    /// original plays no sound; its witnesses (guards nearby who see or hear the picker) are not modelled.</summary>
    private void PickPortal(AreaPortal portal, string item)
    {
        var session = _areaSession!;
        var lockpick = _inventoryHost?.Lockpick(session, item, _areaKitLockpicks);
        if (lockpick is null) { _portalNotice = $"{PortalName(portal)}: the lockpicks are gone"; RefreshAreaStatus(); return; }
        var result = _areaPortals!.Pick(session, portal, lockpick);
        _portalNotice = result.Disposition == Arcanum.Session.GameSessionDisposition.Rejected ? $"{PortalName(portal)}: pick refused ({result.Failure})"
            : $"{PortalName(portal)}: {result.Outcome!.Value.Result switch
            {
                PickLockResult.Unlocked => "the lock opens", PickLockResult.Locked => "locked it", PickLockResult.Jammed => "the lock jams",
                _ => "the pick failed",
            }}{(result.FateUsed ? " (fate point)" : "")}";
        GD.Print($"ARC-SKILLS-006 pick entity={portal.Entity} disposition={result.Disposition} failure={result.Failure} success={result.Check?.Success} " +
            $"critical={result.Check?.Critical} result={result.Outcome?.Result} witnesses={result.Outcome?.WitnessesCalled} locked={result.State?.Locked}");
        RefreshAreaStatus();
    }

    private void UsePortal(AreaPortal portal)
    {
        if (_portalPickWith is { } pickWith) { _portalPickWith = null; PickPortal(portal, pickWith); return; }
        var session = _areaSession!; var portals = _areaPortals!;
        var from = DrawnFrame(portal, session);
        var use = portals.Use(session, portal);
        switch (use)
        {
            case AreaPortalUse.Opened: Animate(portal, from, portal.OpenFrame, opening: true); PlayPortalSound(portal, OpenableSoundAction.Open); break;
            case AreaPortalUse.Closed: Animate(portal, from, 0, opening: false); PlayPortalSound(portal, OpenableSoundAction.Close); break;
            case AreaPortalUse.Locked or AreaPortalUse.Jammed or AreaPortalUse.MagicallyHeld: PlayPortalSound(portal, OpenableSoundAction.Locked); break;
        }
        _portalNotice = $"{PortalName(portal)}: {use}";
        GD.Print($"ARC-WORLD-051 portal use entity={portal.Entity} result={use} frame={portals.Frame(session, portal)}");
        RefreshAreaStatus();
    }

    /// <summary>Per frame: a walk to a door uses it on arrival, the frame animations advance and the bodies of portals not on
    /// their T0 frame are drawn from their current frame.</summary>
    private void FollowAreaPortals()
    {
        if (_areaPortals is not { } portals || _areaSession is not { } session || _readyArea is not { } area) { if (_portalBodies.Count > 0) _portalBodies = []; return; }
        if (_worldActor.Session is AreaGridSession settled && settled.State.Phase == MovementPhase.Settled)
        {
            if (_portalUseWhenSettled is { } waiting)
            {
                _portalUseWhenSettled = null;
                if (!PortalUseAdmitted(out var why)) { _portalNotice = "door use dropped: " + why; RefreshAreaStatus(); }
                else if (portals.TryGet(waiting, out var door) && PortalInReach(settled, area.Snapshot.Objects.Single(o => o.Key == waiting), PendingPortalRange)) UsePortal(door);
            }
            if (_portalUseAfterRoute is { } key && ReferenceEquals(_worldRouteSession, settled) && _worldRoute is { } route && route.Status != RouteFollowStatus.Following)
            {
                _portalUseAfterRoute = null;
                if (!PortalUseAdmitted(out var why)) _portalNotice = "walk to the door dropped: " + why;
                else if (route.Status != RouteFollowStatus.Arrived) _portalNotice = "walk to the door stopped: " + (route.StopReason ?? route.Status.ToString());
                else if (portals.TryGet(key, out var target) && PortalInReach(settled, area.Snapshot.Objects.Single(o => o.Key == key), PendingPortalRange)) UsePortal(target);
                else _portalNotice = "walk to the door ended out of reach";
                RefreshAreaStatus();
            }
        }
        AreaFrames();
        var overlay = _areaOverlay ?? throw new InvalidOperationException("AREA_OVERLAY_REQUIRED");
        var bodies = new Dictionary<AreaObjectKey, AreaDrawCommand>();
        foreach (var portal in portals.All)
        {
            if (portal.Frames.Count < 2) continue;
            var frame = DrawnFrame(portal, session);
            if (frame == portal.Baseline.Frame || portal.Visual(frame) is not { } visual) continue;
            if (!overlay.Bodies.TryGetValue(portal.Key, out var command) || !overlay.Visuals.TryGetValue(command.Visual, out var body)) continue;
            bodies[portal.Key] = AreaScenePlan.PortalFrame(command, area.Snapshot.Objects.Single(o => o.Key == portal.Key), body, visual);
        }
        _portalBodies = bodies;
    }

    // The frame drawn now: an animation's current step, else the session's frame.
    private int DrawnFrame(AreaPortal portal, Arcanum.Session.GameSessionOwner session)
    {
        if (_portalAnimations.TryGetValue(portal.Key, out var animation))
        {
            var step = (long)(Time.GetTicksMsec() - animation.StartMs) / animation.StepMs;
            if (step < animation.Frames.Length) return animation.Frames[step];
            _portalAnimations.Remove(portal.Key);
        }
        return _areaPortals!.Frame(session, portal);
    }

    // The frames between two states, one per step of the open or close rule (the first step shows at once).
    private void Animate(AreaPortal portal, int from, int to, bool opening)
    {
        var frames = new List<int>(); var frame = from;
        for (var guard = 0; guard < 16 && frame != to; guard++)
        {
            if ((opening ? PortalFrameRules.OpenStep(portal.Art, frame) : PortalFrameRules.CloseStep(portal.Art, frame)) is not { } step) break;
            frames.Add(frame = step.Frame);
        }
        if (frames.Count == 0) { _portalAnimations.Remove(portal.Key); return; }
        _portalAnimations[portal.Key] = (frames.ToArray(), Time.GetTicksMsec(), portal.StepMs);
    }

    private void PlayPortalSound(AreaPortal portal, OpenableSoundAction action)
    {
        if (_readyArea?.Snapshot.Objects.SingleOrDefault(o => o.Key == portal.Key) is not { WorldX: { } x, WorldY: { } y } row
            || ObjectSoundReader.Read(row).Fields is not { } fields) return;
        if (SoundCueRules.Portal(fields.Type, fields.SoundEffect, action) is { } cue) PlayCueAt(cue, x, y, PositionalSize.Large, "portal");
    }

    private static string PortalName(AreaPortal portal) => portal.Art.Door ? "door" : "window";

    private string PortalStatus() => _areaPortals is null || _portalNotice.Length == 0 ? "" : " | " + _portalNotice;
}
