using Arcanum.Session;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-DEV-145 (MIG13 S10a): the hero's request boundary in the viewer. A ground click is a WalkRequest, the right button's release a
// StopRequest (RR5: the level-3 interrupt and the turn toward the pointer), and both are applied by the world runtime's
// HeroRequestHost through this port over the viewer's own hosts (route, approach intents, attack, body). ARC-DEV-146 (S10b): the walk, the
// interrupt and the turn are runs of the goal table behind the same port (AreaHeroRunsHost); the route and the grid actor still take the steps.
public partial class TerrainViewerApp
{
    private HeroRequestHost? _heroRequestHost;
    private (AreaGridSession Session, CellCoord Cell)? _heroTurnAt;
    // A turn asked for while the hero is still finishing a step: it begins once the hero stands (the route stops at the next centre).
    private int? _heroTurnPending;
    private AreaGridSession? _heroTurnSession;
    private int _heroTurnStarts;
    private int _heroTurnTarget;
    // ARC-DEV-290 (MIG14 S9a): the session whose turns the viewer has seen (started or adopted); a turn run of another session found with no turn begun is one a load restored.
    private GameSessionOwner? _heroTurnOwner;

    private HeroRequestHost HeroRequests => _heroRequestHost ??= new(new ViewerHeroActions(this));

    /// <summary>The hero's goal 53 (the turn toward a tile) is a run of the table or waits for the hero to stand.</summary>
    internal bool HeroTurning => _heroTurnPending is not null || HeroRunsHost()?.TurnRun is not null; // through the shown session's host: a replaced session's is given up first

    /// <summary>
    /// A turn asked for while the hero finishes a step begins when it stands (the rotate starter, 0x433440, then starts goal 53 on the table). ARC-DEV-148: the goal's steps (30 ms per 45 degrees, GT102) are the table's,
    /// drawn through <see cref="ShowHeroFacing"/>; a hero that moves again, falls or changes map ends the goal (<see cref="HeroTurnHolds"/>), and a walk draws its own facing.
    /// </summary>
    private void ProcessHeroTurn()
    {
        if (_heroTurnPending is not { } target) return;
        if (_worldActor.Session is not AreaGridSession grid || HeroLyingDown) { _heroTurnPending = null; _heroTurnAt = null; return; }
        if (!ReferenceEquals(_heroTurnSession, grid) || _stickHeld || _attack is not null || _worldRoute is { Status: Arcanum.Simulation.Navigation.RouteFollowStatus.Following })
        {
            _heroTurnPending = null; return;
        }
        if (grid.State.Phase != MovementPhase.Settled) return;
        _heroTurnPending = null;
        // The rotate goal is a run of the table (0x433440 starts goal 53 through 0x44D4E0): a run of the hero that refuses it (a spell attempt) leaves the hero as it is; without a session there is no run.
        if (HeroRunsHost() is not { } runs || !runs.Turn(target)) return;
        _heroTurnStarts++; _heroTurnTarget = target;
        _heroTurnAt = (grid, grid.State.CommittedCell); _heroTurnOwner = _areaSession;
        GD.Print($"ARC-DEV-145 hero-turn goal=53 from={_worldActor.State.Facing} to={target}");
    }

    /// <summary>Goal 53 goes on while the hero stands settled where the turn began and can act. ARC-DEV-290: a turn a load restored begins where the loaded hero stands (the first step of a session the viewer has
    /// begun no turn in adopts it, whatever the replaced session left marked); a turn the viewer gave up (a right release) is not adopted again.</summary>
    private bool HeroTurnHolds()
    {
        // (A turn of the replaced session may still be marked: its run was given up with the disposed session, whose end node never ran.)
        if (_areaSession is { } session && !ReferenceEquals(_heroTurnOwner, session) && _worldActor.Session is AreaGridSession settled
            && settled.State.Phase == MovementPhase.Settled && !HeroLyingDown)
        {
            _heroTurnOwner = session; _heroTurnAt = (settled, settled.State.CommittedCell);
            GD.Print($"ARC-DEV-290 hero-turn adopted at={settled.State.CommittedCell}");
        }
        return HeroTurnHoldsHere();
    }

    private bool HeroTurnHoldsHere() =>
        !HeroLyingDown && _worldActor.Session is AreaGridSession grid && _heroTurnAt is { } at && ReferenceEquals(at.Session, grid)
        && at.Cell == grid.State.CommittedCell && grid.State.Phase == MovementPhase.Settled;

    /// <summary>Goal 53's step: the settled hero's art shows <paramref name="rotation"/> (a turn in place, drawn at once).</summary>
    private void ShowHeroFacing(int rotation)
    {
        if (_worldActor.Session is not AreaGridSession grid || grid.State.Phase != MovementPhase.Settled) return;
        _worldActor.TurnedFacing = (rotation, grid.State.Facing);
        PublishWorldSelection();
    }

    /// <summary>Goal 53's end node (0x42C650): a facing still different from the target snaps to it, drawn at once; a hero that does not stand settled draws its own.</summary>
    private void SnapHeroFacing(int rotation)
    {
        var turned = _heroTurnAt;
        _heroTurnPending = null;
        _heroTurnAt = null;
        // Only the hero the turn began with snaps: a run given up with a replaced session (a quick load, a map change) must not turn the hero that stands there now.
        if (_worldActor.Session is not AreaGridSession grid || turned is not { } at || !ReferenceEquals(at.Session, grid) || at.Cell != grid.State.CommittedCell
            || grid.State.Phase != MovementPhase.Settled || _worldActor.State.Facing == rotation) return;
        _worldActor.TurnedFacing = (rotation, grid.State.Facing);
        PublishWorldSelection();
    }

    private sealed class ViewerHeroActions(TerrainViewerApp app) : IHeroActions
    {
        public HeroFacts Facts()
        {
            var session = app._areaSession;
            // Chart cells: the pick and the route use the same chart as the hero's committed cell (the world cell is a translation of it).
            CellCoord? cell = app._worldActor.Session is AreaGridSession grid ? grid.State.CommittedCell : null;
            var active = session is not null && app.HeroActing(session);
            var round = session?.QueryCombatRound();
            var inRound = app.CombatModeActive && round is not null;
            // The hero's actions are viewer hosts, not runs of the table yet: the priority is the one of the goal each would be.
            // ARC-DEV-146: the hero's walk is a run of the table, whose goal has priority 2; a route that follows without a run (an approach, a far goal on the way) counts as before.
            var priority = app._spellGoal is not null ? 4
                : app._worldRoute is { Status: Arcanum.Simulation.Navigation.RouteFollowStatus.Following } || app.HeroTurning ? 2 : 0;
            priority = Math.Max(priority, app.HeroRunsHost()?.HighestPriority ?? 0); // the shown session's host (a replaced session's is given up first)
            return new(cell, app._worldActor.State.Facing, active, priority, inRound, inRound && round!.Actor.IsHero);
        }

        // ARC-DEV-169: the click decision's facts for a press on a dialog NPC (the viewer's other object clicks are not requests yet).
        public HeroClickFacts ClickFacts(string target, bool alt, bool shift)
        {
            // ARC-COMBAT-1943: the hero's Body of Air and polymorph spell flags go with every click's facts.
            var (air, polymorphed) = app.HeroClickSpells();
            return ClickFactsOf(target, alt, shift) with { HeroBodyOfAir = air, HeroPolymorphed = polymorphed };
        }

        private HeroClickFacts ClickFactsOf(string target, bool alt, bool shift)
        {
            var session = app._areaSession;
            // ARC-DEV-176: a door or window is a portal for the click rules (a use out of combat mode and in it).
            if (app._areaDoors?.PortalOf(target) is not null)
                return new(session is not null && app.HeroActing(session), false, false, false, app.HeroInCombatMode, HeroClickTarget.Portal, Alt: alt, Shift: shift);
            // ARC-DEV-181: the attack the viewer decided on (a creature, or with Alt an object or, ARC-COMBAT-1941, a wall) is an attack for the click rules, in combat mode.
            if (target.StartsWith("atk:", StringComparison.Ordinal))
                return new(session is not null && app.HeroActing(session), false, false, false, true, app._attack is { Wall: true } ? HeroClickTarget.Wall : app._attack is { Object: not null } ? HeroClickTarget.Scenery : HeroClickTarget.Critter, Alt: alt, Shift: shift);
            // A trigger object (an exit, a scripted object) is scenery for the click rules; anything else is a speaker.
            if (app.UseKeyOf(target) is { } key)
            {
                // ARC-DEV-175: a thing to pick up or loot is an item, a container (a chest, a junk pile) or a dead creature for the click rules.
                var inventory = IsInventoryTarget(target) && app.InventoryActive ? app.AreaInventoryTargetOf(key) : null;
                var kind = inventory?.Kind switch
                {
                    AreaInventoryTargetKind.GroundItem => HeroClickTarget.Item,
                    AreaInventoryTargetKind.DeadNpc => HeroClickTarget.Critter,
                    AreaInventoryTargetKind.JunkPile or AreaInventoryTargetKind.Container => HeroClickTarget.Container,
                    _ => HeroClickTarget.Scenery,
                };
                // ARC-DEV-304 (GT115): a body with the spoken-with-dead spell flag goes on to the talk, not the loot.
                var dead = inventory?.Kind == AreaInventoryTargetKind.DeadNpc;
                return new(session is not null && app.HeroActing(session), false, false, false, app.HeroInCombatMode, kind, TargetDead: dead,
                    TargetTalksDead: dead && inventory is { } body && app.SpokenWithDead(body.Entity), Alt: alt, Shift: shift);
            }
            var speaker = app.SpeakerOfTarget(target);
            var present = speaker is not null && app.SpeakerPresent(speaker);
            // (Stoned or stunned heroes are not active, which refuses the click first; the dead without the spoken-with-dead spell flag and the player characters are no speakers.)
            // ARC-DEV-304: a dead speaker that is present carries the flag, so the click goes on to the talk (GT115).
            var talksDead = present && speaker is not null && app.SpeakerDead(speaker);
            return new(session is not null && app.HeroActing(session), false, false, false, app.HeroInCombatMode,
                present ? HeroClickTarget.Critter : HeroClickTarget.Other, TargetDead: talksDead, TargetTalksDead: talksDead, Alt: alt, Shift: shift);
        }

        // ARC-INPUT-1946 (0x4445A0): the Alt click out of combat mode moves a loose item or a dead NPC's body within one tile onto the hero's tile (no goal, no action points).
        public HeroImmediateResult ImmediateAction(string target)
        {
            if (app._areaSession is not { } session || !IsInventoryTarget(target) || !app.InventoryActive || app.UseKeyOf(target) is not { } key
                || app.AreaInventoryTargetOf(key) is not { } found || app.ActorCell() is not { } hero) return HeroImmediateResult.None;
            var heroCell = new CellCoord((int)hero.X, (int)hero.Y);
            switch (found.Kind)
            {
                case AreaInventoryTargetKind.GroundItem:
                    return AreaHeroPull.PullItem(session, found.Entity, heroCell);
                case AreaInventoryTargetKind.DeadNpc:
                    return AreaHeroPull.PullBody(app._aiHost, found.Entity, heroCell);
                default:
                    return HeroImmediateResult.None;
            }
        }

        public bool StartTalk(string target, bool run, bool noWalk)
        {
            if (app.HeroRunsHost() is not { } host) return false;
            // A movement gate that refuses leaves the walk under way alone; otherwise the route the hero followed (an approach of its own, a far goal) gives way as for any other action, and the table interrupts the runs.
            if (!host.MayTalk) return false;
            app.StopWorldRoute("ROUTE_REPLACED_BY_TALK");
            app._mapFarGoal = null;
            return host.Talk(target, run, noWalk);
        }

        public bool StartPickup(string target, bool run, bool noWalk)
        {
            if (app.HeroRunsHost() is not { } host || !host.MayTalk) return false;
            app.StopWorldRoute("ROUTE_REPLACED_BY_PICKUP");
            app._mapFarGoal = null;
            return host.Pickup(target, run, noWalk);
        }

        public bool StartAttack(string target, bool run)
        {
            if (app.HeroRunsHost() is not { } host || !host.MayTalk) return false;
            app.StopWorldRoute("ROUTE_REPLACED_BY_ATTACK");
            app._mapFarGoal = null;
            return host.Attack(target, run);
        }

        public bool StartAttemptAttack(string target)
        {
            if (app.HeroRunsHost() is not { } host || !host.MayTalk) return false;
            app.StopWorldRoute("ROUTE_REPLACED_BY_ATTACK");
            app._mapFarGoal = null;
            return host.AttemptAttack(target);
        }

        public bool StartThrow(string item, CellCoord tile)
        {
            if (app.HeroRunsHost() is not { } host || !host.MayTalk) return false;
            // ARC-DEV-317: the press is the session's first throw the viewer has seen (a cursor emptied before the first node is not a restored throw), and the lifted count rides in the frame.
            app._throwOwner = app._areaSession;
            return host.Throw(item, tile, app._gameCursorItem is { } held && held.Item == item ? held.Count : null);
        }

        public bool StartLockpick(string portal, string item)
        {
            if (app.HeroRunsHost() is not { } host) return false;
            app.StopWorldRoute("ROUTE_REPLACED_BY_SKILL");
            return host.PickLock(portal, item);
        }

        public bool StartUseItem(string item, string target, bool run, bool noWalk)
        {
            if (app.HeroRunsHost() is not { } host || !host.MayTalk) return false;
            app.StopWorldRoute("ROUTE_REPLACED_BY_ITEM_USE");
            app._mapFarGoal = null;
            return host.UseItem(item, target, run, noWalk);
        }

        public bool StartUseItemOnTile(string item, CellCoord tile)
        {
            if (app.HeroRunsHost() is not { } host || !host.MayTalk) return false;
            app.StopWorldRoute("ROUTE_REPLACED_BY_ITEM_USE");
            app._mapFarGoal = null;
            return host.UseItemOnTile(item, tile);
        }

        public bool StartPickPocket(string victim, string item, bool plant)
        {
            if (app.HeroRunsHost() is not { } host) return false;
            app.StopWorldRoute("ROUTE_REPLACED_BY_SKILL");
            return host.PickPocket(app.PickPocketTargetOf(victim), item, plant);
        }

        public bool StartCast(int run, string? target)
        {
            if (app.HeroRunsHost() is not { } host) return false;
            return host.Cast(run, target);
        }

        public bool StartUse(string target, bool container, bool run, bool noWalk)
        {
            if (app.HeroRunsHost() is not { } host || !host.MayTalk) return false;
            app.StopWorldRoute("ROUTE_REPLACED_BY_USE");
            app._mapFarGoal = null;
            return host.Use(target, container, run, noWalk);
        }

        public HeroInputOptions Options() => new(app._alwaysRunOption, app._numLockRun, app._gameSettings.AutoAttack);

        // ARC-COMBAT-1949 (VC62): the start branch's reads of the hero's runs, the repeated attack queued beneath the main run and the click's flags on the existing main run.
        public HeroStartContext StartContext(HeroClickGoal goal, string? target, string? item = null) => app.HeroRunsHost()?.StartContext(goal, target, item) ?? default;
        public bool QueueGoal(HeroClickGoal goal, string target) => app.HeroRunsHost()?.QueueGoal(goal, target) == true;
        public void ApplyClickFlags(bool shift, bool nearOccupied) => app.HeroRunsHost()?.ApplyClickFlags(shift, nearOccupied);

        public bool SetWalkTarget(CellCoord tile, bool run, bool held)
        {
            if (app._worldActor.Session is not { } session || app.WorldRoute(session) is not { } route) return false;
            // The walk starter on the table first (GT14: the movement gate, then retarget in place or a new run after the level-3 interrupt); a refusal is no walk.
            var result = app.StartHeroWalkRun(tile, run, held);
            if (result == HeroWalkRunResult.Refused) return false;
            app.CancelAreaUseApproach("a ground click replaced it"); app.CancelTalkApproach("a ground click replaced it"); app.CancelInventoryApproach("a ground click replaced it");
            app._mapFarGoal = null; // a click replaces a far goal handed on across window moves (ARC-WORLD-042)
            if (result is null)
            {
                // No session, so no table (the synthetic fixtures): the route follower walks the goal itself, as before the table walked the hero.
                var accepted = route.SetTarget(tile, app.WorldTime);
                app.SetWorldGait(run ? MovementGait.Run : MovementGait.Walk, held ? "stick" : "ground click", newMove: !held || !app._stickHeld);
                return accepted;
            }
            // ARC-DEV-149 (S10b-2b): the table plans the walk and gives the route one step at a time; what the route followed before (an approach, a far goal) does not go on.
            // (A given step of the walk itself stays: a retarget replans at the next table step, and the step under way is never cancelled.)
            if (result != HeroWalkRunResult.Unchanged && route is { Status: Arcanum.Simulation.Navigation.RouteFollowStatus.Following, Given: false }) app.StopWorldRouteOnly("a ground click replaced it");
            // A held stick counts its run steps across its repeats (the press starts the counter, ARC-COMBAT-004).
            app.SetWorldGait(run ? MovementGait.Run : MovementGait.Walk, held ? "stick" : "ground click", newMove: !held || !app._stickHeld);
            return result != HeroWalkRunResult.Unchanged;
        }

        public void InterruptOrdinary(string why)
        {
            // The table's level-3 interrupt of the hero's runs (GS17): the walk run ends and its end node stops the route; a route with no run (an approach) stops below.
            app.HeroRunsHost()?.Interrupt(Arcanum.Simulation.Goals.GoalInterruptRules.OrdinaryActions, "ROUTE_STOPPED_BY_RIGHT_RELEASE");
            app.StopWorldRoute("ROUTE_STOPPED_BY_RIGHT_RELEASE");
            app.CancelTalkApproach(why); app.CancelAreaUseApproach(why); app.CancelInventoryApproach(why); app.CancelPortalApproach(why, keepArmed: true);
            app._mapFarGoal = null;
            app.CancelAreaAttack(why);
            app._heroTurnPending = null; app._heroTurnAt = null;
        }

        // ARC-DEV-147: the stick was released; the held walk ends where the hero is (the level-3 interrupt of its walk run: the end node stops the route, the step under way finishes).
        public bool EndHeldWalk() => app.HeroRunsHost()?.EndHeldWalk("ROUTE_STOPPED_BY_STICK") ?? false;

        public void StartTurn(int rotation)
        {
            if (app._worldActor.Session is not AreaGridSession grid) return;
            app._heroTurnPending = rotation; app._heroTurnSession = grid;
        }
    }
}
