using Arcanum.Data.Area;
using Arcanum.Data.Audio;
using Arcanum.Data.Traversal;
using Arcanum.Presentation.Audio;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Locks;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.Simulation.Skills;
using Arcanum.Simulation.Sound;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-WORLD-051: doors and windows at runtime in map mode. The state of each portal lives in the session (ARC-WORLD-050):
// the steps and the click route admit a portal by its current state, a step that crosses a closed portal it may pass opens
// it, and a left press on a portal uses it (walking within use range first): an open one closes, a closed one opens after
// the player's open attempt. ARC-DEV-138 (MIG13 S7): the change is the portal's own goal run (AreaDoorHost: 70 opens, 72 waits 1.5 s and tests the
// doorway, 71 closes), stepped on the animation clock, so the frames play at the ART's rate (1000 / fps ms, ledger LK10, LK11) with the open or
// close sound, a door closes by itself unless its portal flag keeps it open, and the Esc pause stops it; each step writes the session's frame.
public partial class TerrainViewerApp
{
    private AreaPortals? _areaPortals;
    private object? _areaPortalsStamp;
    private AreaDoorHost? _areaDoors;
    // The door host of the map or window shown before: the session's own ends of a door's run (a map's close) are drained after the new portals are bound, and belong to it.
    private AreaDoorHost? _retiredDoors;
    private int _retiredDoorsAge;
    private AreaGoalStepRouter? _goalStepRouter;
    // The frame step's goal-step host: the doors' runs to the door host, every other run to the AI host's steps.
    // ARC-DEV-290 (MIG14 S9a): the hero's runs a load restored are stepped before anything asked for the loaded session's hero host, so the router builds it. ARC-MAGIC-1280: so is the overlay host.
    private AreaGoalStepRouter GoalStepRouter => _goalStepRouter ??= new(() => _areaDoors, AiGoalSteps, StepMissileHost, StepProjectileHost, () => ShownKnockbackHost, () => ShownSceneryHost, () => ShownEyeCandyHost ?? RestoredEyeCandyHost(), () => ShownHeroRunsHost ?? (_areaSession is not null ? HeroRunsHost() : null));
    private Dictionary<AreaObjectKey, AreaDrawCommand> _portalBodies = [];
    // ARC-DEV-185 (MIG13 S12e): a lock pick is the hero's skill goal 33 → 67; the attempt it asked for waits here for the frame's next safe point.
    private (string Portal, string Item)? _lockpickPending;
    // ARC-DEV-185: a lock pick was started and not yet asked for or ended; its run ending without the ask says so on the HUD.
    private bool _lockpickTrying;
    private string _portalNotice = "";
    // ARC-SKILLS-006: lockpicks chosen in the inventory, waiting for a press on a door or window; and the lockpicks of the press
    // whose walk or settle is pending, so the pending use picks the lock instead.
    private string? _lockpickArmed;

    /// <summary>Binds the shown map's portals to the session and the actor's steps (map mode with a session).</summary>
    private void BindAreaPortals()
    {
        if (_readyMapName is null || _readyArea is not { } area || _areaSession is not { } session || _worldActor.Session is not AreaGridSession grid)
        {
            if (_areaDoors is not null) { _retiredDoors = _areaDoors; _retiredDoorsAge = 0; }
            _areaPortals = null; _areaPortalsStamp = null; _areaDoors = null; _portalBodies = []; _lockpickPending = null; _lockpickTrying = false;
            DisarmLockpicks();
            return;
        }
        var stamp = (area, session);
        if (!Equals(stamp, _areaPortalsStamp))
        {
            _areaPortals = AreaPortals.From(area.Snapshot, area.Plan);
            _areaPortalsStamp = stamp; _portalBodies = []; _lockpickPending = null; _lockpickTrying = false;
            if (_areaDoors is not null) { _retiredDoors = _areaDoors; _retiredDoorsAge = 0; }
            _areaDoors = new(session, _areaPortals, DoorCritterCells, PlayDoorSound) { HeroUse = HeroDoorUse, HeroScript = HeroDoorScript, HeroNotice = HeroDoorNotice, OpenerScript = OpenerDoorScript };
            DisarmLockpicks();
            GD.Print($"ARC-WORLD-051 portals bound map={_readyMapName} portals={_areaPortals.All.Count} multi-frame={_areaPortals.All.Count(p => p.Frames.Count > 1)}");
        }
        var portals = _areaPortals!;
        grid.Portals = () => portals.Passages(session);
        if (_aiHost is { } ai) { ai.DoorEnds = DoorRunEnded; ai.MissileEnds = MissileRunEnded; ai.ProjectileEnds = ProjectileRunEnded; ai.KnockbackEnds = KnockbackRunEnded; ai.HeroEnds = HeroRunEnded; }
    }

    // The end nodes of a door's run the session ended by itself (a map's close): the host that owned the door when the run began, which is the one shown before when a map or window change
    // came first, else the shown one. The AI host drains the session's ends; with the AI off the frame does (DrainDoorEnds).
    private void DoorRunEnded(GoalRunEnd end)
    {
        // Door ids repeat across maps (sector:N:index), so the retired host answers only for the frames right after the change (the drain follows it in the same frame) and for the same session.
        var host = _retiredDoors is { } old && old.Owns(end.Run.Owner) && ReferenceEquals(old.Session, _areaSession) ? old : _areaDoors;
        host?.EndNodes(end);
    }

    // With the AI off (AiEnabled false) nobody else drains the session's unclaimed ends, and a door's closing cut short by a map's close would stay half open; the non-door ends it
    // drains are NPC runs, and there is no AI host to run theirs. With the AI on but no host built yet the ends wait for the host.
    private void DrainDoorEnds()
    {
        if (AiEnabled || _areaSession is not { } session) return;
        foreach (var end in session.DrainGoalEnds())
        {
            if (AiExecutorRules.IsDoorRunGoal(end.Run.Bottom.Goal)) DoorRunEnded(end);
            else if (AiExecutorRules.IsMissileRunGoal(end.Run.Bottom.Goal)) MissileRunEnded(end);
            else if (AiExecutorRules.IsProjectileRunGoal(end.Run.Bottom.Goal)) ProjectileRunEnded(end);
            else if (AiExecutorRules.IsKnockbackRunGoal(end.Run.Bottom.Goal)) KnockbackRunEnded(end);
            else if (AreaHeroRunsHost.IsHeroRun(end.Run)) HeroRunEnded(end);
        }
    }

    // The world tiles of the living player characters and NPCs: the hero's committed cell and the AI host's NPCs and companions (a map without a host has the hero only).
    private IEnumerable<AreaStepCell> DoorCritterCells()
    {
        if (_worldActor.Session is AreaGridSession hero) yield return hero.Chart.ToWorld(hero.State.CommittedCell);
        if (_aiHost is { } ai)
            foreach (var tile in ai.LiveCritterTiles()) yield return new AreaStepCell(tile.X, tile.Y);
    }

    private void PlayDoorSound(AreaPortal portal, DoorSound sound) =>
        PlayPortalSound(portal, sound switch { DoorSound.Open => OpenableSoundAction.Open, DoorSound.Close => OpenableSoundAction.Close, _ => OpenableSoundAction.Locked });

    // The opener of the viewer's door starts (the hero's click and step): the hero's goal run is the slices S10 and S11's.
    private const string DoorOpener = "hero";

    /// <summary>Per advance: the closed portals every started step crosses and may pass start their opening run (the open sound plays at its begin). ARC-DEV-138: the original starts the
    /// same goals 11, 12 and 70 for a walker (GT49, Q5 answered); the hero's walk is not a goal run yet (S10), so the viewer starts the door's run for it and charges nothing.</summary>
    private void NoteMapPortals(GridAdvance advance)
    {
        if (_areaPortals is not { } portals || _areaSession is not { } session || _areaDoors is not { } doors) return;
        foreach (var evidence in advance.AreaEvidence)
        {
            if ((evidence.Live?.Disposition ?? evidence.Assessment.Disposition) != Arcanum.Data.Traversal.CardinalTraversalDisposition.Open) continue;
            foreach (var portal in portals.CrossedClosed(session, evidence.Assessment))
            {
                var started = doors.Open(portal, DoorOpener);
                // A start the table refuses must not leave the actor crossing a shut portal: the door opens at once instead.
                if (!started) doors.OpenNow(portal);
                GD.Print($"ARC-WORLD-051 portal opened by step entity={portal.Entity} frame={portal.OpenFrame} run={started}");
            }
        }
    }

    /// <summary>
    /// A guarded left press on a door or window. ARC-DEV-176 (MIG13 S11d, GS26 and GS27): the click is an <see cref="ObjectClickRequest"/>; the click rules make it the use goal 38, which walks the hero within use range
    /// (goal 8), turns into the door goals 11 or 49 and 12 or 50 at its node 6, and starts the door's own run (goals 70 and 71, S7). The hero's open attempt, its 2 action points (<see cref="HeroDoorUse"/>) and the
    /// refusals are the goals' (<see cref="AreaDoorHost"/> reports them). Armed lockpicks (the skill, S12) keep the old intent: a pick in range now, or after a walk. Returns true when the click was on a portal.
    /// </summary>
    private bool TryAreaPortalUse(Vector2 screen, bool alt = false)
    {
        // Armed lockpicks go to this press: on a portal they pick its lock, elsewhere the pick is cancelled.
        // The press pops the item target mode (SA51: every outcome after the dead and unconscious test pops).
        var pick = _lockpickArmed; DisarmLockpicks();
        if (_areaPortals is not { } portals || _areaSession is null || _worldActor.Session is not AreaGridSession session
            || AreaObjectAtScreen(screen) is not { } hit || !portals.TryGet(hit, out var portal))
        {
            // ARC-UI-1127 (E8-g): armed lockpicks on a container pick its lock.
            if (pick is not null && TryAreaContainerPick(screen, pick)) return true;
            if (pick is not null) { _portalNotice = "pick a lock cancelled: not a door, window or container"; RefreshAreaStatus(); }
            return false;
        }
        CancelTalkApproach("a use click replaced it"); CancelInventoryApproach("a use click replaced it"); CancelAreaUseApproach("a use click replaced it");
        if (pick is null)
        {
            var request = HeroRequests.Apply(new ObjectClickRequest(portal.Entity, Alt: alt && HeroInCombatMode));
            _portalNotice = request.Click != HeroClickGoal.UseObject ? $"{PortalName(portal)}: the click does nothing here ({request.Click})"
                : request.Outcome == HeroRequestOutcome.Done ? $"{PortalName(portal)}: using, goal 38 started" : $"{PortalName(portal)}: the use was refused";
            GD.Print($"ARC-DEV-176 portal-click outcome={request.Outcome} goal={request.Click}");
            RefreshAreaStatus();
            return true;
        }
        // ARC-DEV-185 (MIG13 S12e): armed lockpicks on a door or window are the skill goal 33 with skill 14: the hero walks within range 1 (goal 8), turns, and the pick is made at the frame's next safe point (goal 67).
        var lock_ = HeroRequests.Apply(new LockpickRequest(portal.Entity, pick));
        _portalNotice = lock_.Outcome == HeroRequestOutcome.Done ? $"{PortalName(portal)}: picking the lock" : $"{PortalName(portal)}: the pick was refused";
        _lockpickTrying = lock_.Outcome == HeroRequestOutcome.Done;
        GD.Print($"ARC-DEV-185 lockpick-click outcome={lock_.Outcome}");
        RefreshAreaStatus();
        return true;
    }

    /// <summary>Goal 67's node asked for the pick: it is made at the next safe point.</summary>
    internal void HeroPickLockAsked(string target, string item) => _lockpickPending = (target, item);

    /// <summary>ARC-WORLD-1469, ARC-WORLD-1668 (TT18, TT7): the use node of goal 12 for an NPC or a companion opener (0x428A10 → 0x441980, the opener the triggerer and the extra object): the door's trap meets the opener first (it is the trap's triggerer),
    /// then the door's own use script runs (<see cref="DoorUseScriptStops"/>); true when the use stops and the opener's run ends.</summary>
    private bool OpenerDoorScript(string opener, AreaPortal portal)
    {
        if (_aiHost is not { } host || _readyArea is not { } area) return false;
        var row = area.Snapshot.Objects.FirstOrDefault(o => o.Key == portal.Key);
        return row is not null && (host.TrapMetByUse(opener, row) || DoorUseScriptStops(area, row, new(CompanionHomeMap(opener) ?? CurrentAreaMap, opener)));
    }

    /// <summary>ARC-WORLD-1581, ARC-WORLD-1668 (TT18, TT7): the use node of goal 12 for the hero runs the door's use script (0x428A10 → 0x441980, the hero the triggerer and the extra object; the viewer asks it after the 2 action points of <see cref="HeroDoorUse"/>, which the node charges first): the door's trap meets the hero as a trapped container's does
    /// (<see cref="AreaTraps.Use"/>), then the door's own use script runs (<see cref="DoorUseScriptStops"/>); true when the use stops (spotted now, a native run went off, a refused meeting, a script that skips the default) and the hero's run ends with the door shut.</summary>
    private bool HeroDoorScript(AreaPortal portal)
    {
        if (_readyArea is not { } area || area.Snapshot.Objects.FirstOrDefault(o => o.Key == portal.Key) is not { } row) return false;
        if (AreaTrapsNow() is { } traps && traps.Use(row) is { } met)
        {
            GD.Print($"ARC-WORLD-1581 hero-door-trap entity={portal.Entity} outcome={met.Outcome} cancels={met.Cancels}");
            NoteTrapMeeting(met);
            if (met.Cancels) return true;
        }
        return UseScriptHost(area.Snapshot) is { } host && DoorUseScriptStops(area, row, host.Pc, hero: true);
    }

    /// <summary>ARC-WORLD-1668 (TT18): the door's use script (point 1) through the object entry point (<see cref="AreaUseScriptHost.RunUseScript"/>: the opener as the triggerer and the extra object; the trap meeting was asked before), its effects applied as a
    /// use's are (awards, floats, creations, the printed lines to the message window). ARC-WORLD-1805 (TT20): the hero's run takes a teleport of the hero (the installed doors that teleport do so unconditionally and answer "run default"): the script only records the request
    /// (0x447220 case 0x2C → 0x4D3380), so the transfer is queued here and the door opens as the answer says; it is applied at the frame's next safe point. An NPC's or a companion's teleport is not modelled (the run is then not applied and the use goes on), and so is a
    /// conversation a door's script asks for (no installed door or window use script starts one, census of ARC-WORLD-1805). True when the answer is "skip default" (<see cref="AreaUseScriptHost.UseStops"/>).</summary>
    private bool DoorUseScriptStops(AreaLoadResult area, AreaObjectRecord row, Arcanum.Scripting.Scr.ScrObjectRef opener, bool hero = false)
    {
        if (_areaSession is not { } session || UseScriptHost(area.Snapshot) is not { } host) return false;
        var transfers = hero ? _mapTransfers : null;
        var ran = Awarded(host, host.RunUseScript(AreaUseScriptHost.EntityId(row), opener, opener, session.Rules.Scripts, transfers is null ? null : teleport => transfers.DoorTeleportRefusal(row, teleport)));
        if (ran.Run is null && ran.Outcome == AreaScriptedUseOutcome.NoTeleport) return false;
        var stops = AreaUseScriptHost.UseStops(ran);
        GD.Print($"ARC-WORLD-1668 door-use-script entity={AreaUseScriptHost.EntityId(row)} opener={opener.Code} outcome={ran.Outcome} run={ran.Run?.Outcome.ToString() ?? "-"} stops={stops} detail={ran.Detail}");
        foreach (var entry in ScriptLineEntries(ran.Printed)) PostMessage(entry, "door script line");
        if (transfers is not null && ran.Outcome == AreaScriptedUseOutcome.TeleportRefused)
        { _areaUseStatus = $"door script teleport refused: {ran.Detail}"; RefreshAreaStatus(); }
        if (transfers is not null && ran.Outcome == AreaScriptedUseOutcome.Teleport && ran.Teleport is { } asked)
        {
            var queued = transfers.QueueDoorTeleport(row, ran.Run!.Script ?? 0, asked);
            GD.Print($"ARC-WORLD-1805 door-teleport entity={AreaUseScriptHost.EntityId(row)} outcome={queued.Outcome} to={queued.Site?.DestinationCatalogue} arrival={queued.Site?.Arrival} reason={queued.Reason}");
            if (queued.Outcome == AreaUseOutcome.Accepted) { _areaUseStatus = $"door script teleport queued to ({queued.Site!.Arrival.X},{queued.Site.Arrival.Y})"; RefreshAreaStatus(); }
        }
        return stops;
    }

    /// <summary>ARC-DEV-176: the use node of goal 12 for the hero (0x428A10): the open that got through the attempt costs 2 action points in a turn-based round (ledger CT3, GT40); false ends the run (nothing is charged for a refused open).</summary>
    private bool HeroDoorUse(AreaPortal portal)
    {
        if (ChargeHeroAction(TurnBasedRules.UseObjectCost, "use")) return true;
        _portalNotice = $"{PortalName(portal)}: {_turnBasedNotice}"; RefreshAreaStatus();
        return false;
    }

    /// <summary>ARC-DEV-176: what the hero's door goals did, for the status line and the locked sound (the original makes no sound for a refusal, GT39; the viewer keeps the locked sound, the player's only feedback).</summary>
    private void HeroDoorNotice(AreaPortal portal, AreaPortalUse use)
    {
        if (use is AreaPortalUse.Locked or AreaPortalUse.Jammed or AreaPortalUse.MagicallyHeld) PlayPortalSound(portal, OpenableSoundAction.Locked);
        _portalNotice = $"{PortalName(portal)}: {use}";
        GD.Print($"ARC-WORLD-051 portal use entity={portal.Entity} result={use} goal={_areaDoors?.GoalOf(portal)}");
        RefreshAreaStatus();
    }

    /// <summary>Drops armed lockpicks with the reason on the HUD (nothing when none are armed).</summary>
    private void CancelArmedLockpicks(string why)
    {
        if (_lockpickArmed is null && _itemAiming is null) return;
        // ARC-DEV-365: the item cursor shares interface mode 16 with armed lockpicks and leaves with them.
        var cursor = _itemAiming is not null;
        DisarmLockpicks(); _portalNotice = (cursor ? "item cursor cancelled: " : "pick a lock cancelled: ") + why;
    }

    /// <summary>ARC-DEV-110 (MIG16 S3): the armed lockpicks are interface mode 16 (item use) on the viewer's stack; popping it clears them
    /// (its leave step runs <c>_lockpickArmed = null</c>) and puts a held item back. Nothing when none are armed.</summary>
    private void DisarmLockpicks()
    {
        if (ItemTargetOpen) CloseTargetMode(InterfaceMode.ItemTarget);
    }

    /// <summary>Drops a walk to a door (another click, a reset or a reload replaced it), and armed lockpicks unless <paramref name="keepArmed"/> (a world press, which the armed lockpicks take).</summary>
    private void CancelPortalApproach(string why, bool keepArmed = false)
    {
        if (!keepArmed) DisarmLockpicks();
        // ARC-DEV-176: the hero's walk to a door is its use run (goal 38 and what it pushed).
        // (Only a use run on a door: a walk to a chest or a scripted object goes on under a press that is no door's.)
        var ended = ShownHeroRunsHost is { UseTarget: { } used } host && _areaDoors?.PortalOf(used) is not null && host.EndUse(why);
        // ARC-DEV-185: so is a lock pick's walk (the skill goal 33 on a door), and a pick not yet made.
        if (ShownHeroRunsHost is { SkillRun.Bottom.Target: { } skillTarget } skills && (_areaDoors?.PortalOf(skillTarget) is not null || IsInventoryTarget(skillTarget)) && skills.EndSkill(why)) ended = true;
        if (_lockpickPending is not null) { _lockpickPending = null; ended = true; }
        _lockpickTrying = false;
        if (ended) _portalNotice = "walk to the door or container cancelled: " + why;
    }

    // True when no hero skill run is left on the table (a replaced session counts as ended).
    private bool LockpickRunEnded()
    {
        try { return HeroRunsHost() is not { SkillRun: not null }; }
        catch (ObjectDisposedException) { return true; }
    }

    // The pending pick's guards (as walk to talk): no lifecycle, reset or reload, and input not quarantined or modal.
    private bool PortalUseAdmitted(out string why)
    {
        why = _worldLifecycleSession is not null || _worldResetPending || _worldReloadPending ? "reset/reload"
            : !_worldFocused || WorldModal || WorldUiFocus ? "input quarantined/modal" : "";
        return why.Length == 0;
    }

    /// <summary>ARC-SKILLS-006: the hero picks the portal's lock with the chosen lockpicks (the session's answer on the HUD). The
    /// original plays no sound; its witnesses (guards nearby who see or hear the picker) are not modelled.</summary>
    private void PickPortal(AreaPortal portal, string item)
    {
        var session = _areaSession!;
        var lockpick = _inventoryHost?.Lockpick(session, item, _areaKitLockpicks);
        if (lockpick is null) { _portalNotice = $"{PortalName(portal)}: the lockpicks are gone"; RefreshAreaStatus(); return; }
        ResumeLoadedRound();
        // ARC-WORLD-1149 (FR-LIGHT-011): the pick's darkness term reads the light at the portal's cell (0x4C8430, 0x4DCE10).
        var result = _areaPortals!.Pick(session, portal, lockpick, light: AreaCellLightLevel(portal.Cell.X, portal.Cell.Y), world: CombatWorldOrNull());
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

    /// <summary>The use of a portal: the hero's use goal 38 starts (the same request as a click), which walks, opens or closes and charges as the goals decide.</summary>
    private void UsePortal(AreaPortal portal)
    {
        var request = HeroRequests.Apply(new ObjectClickRequest(portal.Entity));
        _portalNotice = request.Outcome == HeroRequestOutcome.Done ? $"{PortalName(portal)}: using, goal 38 started" : $"{PortalName(portal)}: the use was refused";
        RefreshAreaStatus();
    }

    /// <summary>The frame's safe point of the lock pick: the pick goal 67 asked for is made, or the HUD says the run ended without it. ARC-DEV-321: also run before a save,
    /// so an attempt asked for in the frame before the save is made, not lost (the run has popped and the pending attempt is not saved).</summary>
    private void ProcessLockpickPending()
    {
        if (_lockpickPending is { } asked)
        {
            _lockpickPending = null; _lockpickTrying = false;
            if (!PortalUseAdmitted(out var why)) { _portalNotice = "pick a lock dropped: " + why; RefreshAreaStatus(); }
            else if (_areaDoors?.PortalOf(asked.Portal) is { } door) PickPortal(door, asked.Item);
            else if (ContainerOfSkillTarget(asked.Portal) is { } container) PickContainer(container, asked.Item);
            else { _portalNotice = "pick a lock dropped: the door or container is gone"; RefreshAreaStatus(); }
        }
        else if (_lockpickTrying && LockpickRunEnded())
        {
            // The skill run ended without asking for the pick (the door is gone, the hero could not turn or reach it, a cancel): the HUD says so.
            _lockpickTrying = false;
            _portalNotice = "pick a lock: the attempt did not happen"; RefreshAreaStatus();
        }
    }

    /// <summary>Per frame: a walk to a door uses it on arrival, the frame animations advance and the bodies of portals not on
    /// their T0 frame are drawn from their current frame.</summary>
    private void FollowAreaPortals()
    {
        if (_retiredDoors is not null && ++_retiredDoorsAge > 3) _retiredDoors = null;
        DrainDoorEnds();
        if (_areaPortals is not { } portals || _areaSession is not { } session || _readyArea is not { } area) { if (_portalBodies.Count > 0) _portalBodies = []; return; }
        // ARC-DEV-185: the lock pick goal 67 asked for lies here until the frame's safe point; a door that is no more (another map) drops it.
        ProcessLockpickPending(); ProcessItemUsePending();
        AreaFrames();
        var overlay = _areaOverlay ?? throw new InvalidOperationException("AREA_OVERLAY_REQUIRED");
        var bodies = new Dictionary<AreaObjectKey, AreaDrawCommand>();
        // ARC-SCRIPT-1778: the portals a script gave another art take its frames first.
        SyncPortalArt();
        foreach (var portal in portals.All)
        {
            var frame = DrawnFrame(portal, session);
            // A portal with the art a script wrote is drawn from that art whatever its frame (its T0 body is the old art's).
            if (portal.ArtId is not null)
            {
                if ((portal.Visual(frame) ?? portal.Visual(0)) is not { } newArt || !overlay.Bodies.TryGetValue(portal.Key, out var changed) || !overlay.Visuals.TryGetValue(changed.Visual, out var old)) continue;
                bodies[portal.Key] = AreaScenePlan.PortalArtFrame(changed, PortalRows(area)[portal.Key], old, newArt);
                continue;
            }
            if (portal.Frames.Count < 2) continue;
            if (frame == portal.Baseline.Frame || portal.Visual(frame) is not { } visual) continue;
            if (!overlay.Bodies.TryGetValue(portal.Key, out var command) || !overlay.Visuals.TryGetValue(command.Visual, out var body)) continue;
            bodies[portal.Key] = AreaScenePlan.PortalFrame(command, area.Snapshot.Objects.Single(o => o.Key == portal.Key), body, visual);
        }
        _portalBodies = bodies;
    }

    // The frame drawn now: the session's frame, which the door's run writes step by step (ARC-DEV-138).
    private int DrawnFrame(AreaPortal portal, Arcanum.Session.GameSessionOwner session) => _areaPortals!.Frame(session, portal);

    private void PlayPortalSound(AreaPortal portal, OpenableSoundAction action)
    {
        if (_readyArea?.Snapshot.Objects.SingleOrDefault(o => o.Key == portal.Key) is not { WorldX: { } x, WorldY: { } y } row
            || ObjectSoundReader.Read(row).Fields is not { } fields) return;
        if (SoundCueRules.Portal(fields.Type, fields.SoundEffect, action) is { } cue) PlayCueAt(cue, x, y, PositionalSize.Large, "portal");
    }

    private static string PortalName(AreaPortal portal) => portal.Art.Door ? "door" : "window";

    private string PortalStatus() => _areaPortals is null || _portalNotice.Length == 0 ? "" : " | " + _portalNotice;
}
