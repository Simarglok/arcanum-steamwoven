using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-COMBAT-005: the hero's attack in the AREA view, in real time and without AI (no NPC fights back). In combat mode a left
// click on a living NPC attacks it, as the original's click does (ledger FA1–FA6): Shift makes it the "attempt attack" goal
// (here the same attack), a follower of the hero is attacked only with left Alt held, a corpse is looted only with Alt, and
// holding , . or / aims at the head, an arm or a leg. The hero walks (running, as every object approach) within its weapon's
// reach first, then plays its bare-handed attack ART at the rate of its Speed and weapon; the frame that shows the action
// frame runs the hit through the session (resolution, damage, experience), and with the auto-attack setting (on, the
// original's default) the attack starts again 5 ms after it ends while the target lives. A click elsewhere, the stick, a
// dialog, a quick load, the hero leaving combat mode (refused while the animation plays: the exit gate's attempt-attack test)
// or losing the ability to act stop it; a refused hit ends it. Companions are not attacked yet (they move with their own
// actor). The status line names the outcome; a killed NPC can be looted (Alt in combat mode, a plain click out of it).
public partial class TerrainViewerApp
{
    private AreaCombatHost? _combatHost;
    // ARC-WORLD-036: the effect rules of the hero's start tables, for the followers' effects (null without --hero).
    private Func<int, Arcanum.Data.Characters.EffectRule?>? _areaEffectRules;
    private string? _attackNotice;
    private AttackGoal? _attack;
    // Left Alt as the key events report it (a follower is attacked only with left Alt, FA2).
    private bool _leftAltHeld;
    // Tests set the held aim key and Shift that a native click would read from the keyboard.
    private HitLocation? _attackAimForSelfTest;
    // ARC-COMBAT-012: the line-of-fire smoke lends the hero a reach beyond 1 (no kit holds a reach weapon); the struck object is in
    // the hero's real reach.
    private int? _attackReachForSelfTest;
    // ARC-COMBAT-012: the entity the last line of fire struck instead of the goal's target.
    private string? _lineOfFireStruck;

    private int HeroReach(SessionHeroAttack? hero) => _attackReachForSelfTest ?? hero?.Range ?? 1;

    /// <summary>One attack goal: its target, the aim, and either the walk to reach it or the attack animation playing (or the
    /// 5 ms pause before the auto-attack repeat).</summary>
    private sealed class AttackGoal(AreaObjectRecord row, string entity, HitLocation? aim)
    {
        public AttackGoal(AreaCombatTarget target, SessionCritterCombat combat, HitLocation? aim) : this(target.Row, target.Entity, aim)
        {
            Target = target; Combat = combat;
        }

        public AttackGoal(AreaObjectAttackTarget target, HitLocation? aim) : this(target.Row, target.Entity, aim) => Object = target;

        public AreaObjectRecord Row { get; } = row;
        public string Entity { get; } = entity;
        // ARC-COMBAT-005: an NPC target with its combat values; ARC-COMBAT-008: an object target instead.
        public AreaCombatTarget? Target { get; }
        public SessionCritterCombat? Combat { get; }
        public AreaObjectAttackTarget? Object { get; }
        public HitLocation? Aim { get; } = aim;
        public bool Approaching { get; set; }
        public AreaAttackPlayer? Player { get; set; }
        public long? RepeatAtMs { get; set; }
        public int Octant { get; set; }
        public int Attacks { get; set; }
        public int Approaches { get; set; }
        // The last hit ended the goal (refused, or the resolver refused the attack): no repeat follows.
        public bool Stop { get; set; }
        // ARC-COMBAT-008: the last hit broke the object: no repeat follows.
        public bool Broke { get; set; }
        // ARC-COMBAT-015: the walk stopped at a clear shot (Steamwoven, EX9), and the cell last tested on the walk.
        public bool ClearShotStop { get; set; }
        public CellCoord? TestedCell { get; set; }
    }

    /// <summary>An attack animation is playing (the exit gate's attempt-attack test).</summary>
    private bool AttackPlaying => _attack?.Player is { Playing: true };

    private bool AttackMapActive => _combatHost is not null && _areaSession is not null && _readyMode == TerrainViewerMode.CrashSiteArea
        && _combatHost.Covers(CurrentAreaMap);

    private void BindAreaCombat(AreaInventoryHost host)
    {
        CancelAreaAttack(null); CancelSpellCast(null); ClearAreaProjectiles("the map changed"); _combatHost = new AreaCombatHost(host); _attackNotice = null; _spellCasterStamp = null;
    }

    private void UnbindAreaCombat() { CancelAreaAttack(null); CancelSpellCast(null); ClearAreaProjectiles("the map changed"); _combatHost = null; _attackNotice = null; _spellCasterStamp = null; }

    private long WorldMilliseconds() { var time = WorldTime.Value; return (long)(time.Numerator * 1000 / time.Denominator); }

    /// <summary>Tracks left Alt from the key events (the mouse event only says that some Alt is held).</summary>
    private void TrackAttackModifiers(InputEventKey key)
    {
        if (key.Keycode == Key.Alt && key.Location != KeyLocation.Right) _leftAltHeld = key.Pressed;
    }

    // A key-up lost to a focus change cannot leave left Alt held: Alt must still be down.
    private bool LeftAltHeld => _leftAltHeld && Godot.Input.IsKeyPressed(Key.Alt);

    /// <summary>ARC-COMBAT-005: a companion of the shown map (it moves with its own actor); attacking one is not ready.</summary>
    private bool IsAreaCompanion(AreaObjectKey key) => _areaCompanions?.Companions.Any(c => c.Speaker.Row.Key == key) == true;

    private HitLocation? HeldAim()
    {
        if (_attackAimForSelfTest is { } aim) return aim;
        if (Godot.Input.IsPhysicalKeyPressed(Key.Comma)) return HitLocation.Head;
        if (Godot.Input.IsPhysicalKeyPressed(Key.Period)) return HitLocation.Arm;
        if (Godot.Input.IsPhysicalKeyPressed(Key.Slash)) return HitLocation.Leg;
        return null;
    }

    /// <summary>The NPC under the screen point while the hero is in combat mode (ARC-UI-005 pick), or null.</summary>
    private AreaCombatTarget? CombatTargetAt(Vector2 screen) =>
        AttackMapActive && HeroInCombatMode && AreaObjectAtScreen(screen) is { } key ? _combatHost!.Target(_areaSession!, CurrentAreaMap, key) : null;

    /// <summary>A guarded left press (see <c>WorldRouteClick</c>) in combat mode on an NPC: true when the press belongs to combat
    /// (an attack, a refused one, or a corpse clicked without Alt, which does nothing in the original).</summary>
    private bool TryAreaAttack(Vector2 screen, bool alt)
    {
        // ARC-COMBAT-008: an object is attacked only with Alt held (ledger OD14); without it the click uses the object as out of combat.
        if (CombatTargetAt(screen) is not { } target) return alt && TryAreaObjectAttack(screen);
        // FA6: a hero that cannot act (stoned, stunned, paralysed, unconscious, dead) does nothing with the click.
        if (_areaSession!.QueryHeroLife() is not { Status.IsActive: true })
        {
            _attackNotice = "the hero cannot act";
            RefreshAreaStatus();
            return true;
        }
        if (target.Dead)
        {
            // FA3: a corpse in combat mode is looted with Alt and otherwise ignored.
            if (alt) return false;
            _attackNotice = "a corpse in combat mode: Alt+click loots it";
            RefreshAreaStatus();
            return true;
        }
        if (target.Follower && !LeftAltHeld)
        {
            _attackNotice = "a follower is attacked only with left Alt held";
            RefreshAreaStatus();
            return true;
        }
        if (IsAreaCompanion(target.Row.Key))
        {
            _attackNotice = "attacking a companion is not ready";
            RefreshAreaStatus();
            return true;
        }
        if (target.Build.Combat is not { } combat)
        {
            _attackNotice = $"cannot attack: {target.Build.Failure}";
            GD.Print($"ARC-COMBAT-005 attack refused build={target.Build.Failure}");
            RefreshAreaStatus();
            return true;
        }
        CancelTalkApproach("an attack replaced it"); CancelAreaUseApproach("an attack replaced it"); CancelInventoryApproach("an attack replaced it");
        // The same target clicked again keeps its goal (the original adds no second attack goal on that target).
        if (_attack is { } running && running.Entity == target.Entity && !running.Stop) return true;
        CancelAreaAttack(null);
        _attack = new(target, combat, HeldAim());
        StartAttackGoal(_attack);
        return true;
    }

    // ARC-COMBAT-008: the attackable objects of the shown map, rebuilt when the map or its portals are bound again.
    private AreaObjectTargets? _objectTargets;
    private object? _objectTargetsStamp;

    private AreaObjectTargets? ObjectTargets()
    {
        // The exterior and the cave have no session portals (map mode binds them): their containers and scenery are targets all the same.
        if (!AttackMapActive || _readyArea is not { } area) return null;
        var stamp = (area, (object?)_areaPortals);
        if (!Equals(stamp, _objectTargetsStamp))
        {
            var inventory = _inventoryHost?.Maps.FirstOrDefault(m => m.RuntimeMap == area.Snapshot.RuntimeMap);
            _objectTargets = _areaPortals is { } portals ? AreaObjectTargets.From(area.Snapshot, portals, inventory)
                : new AreaObjectTargets(area.Snapshot.RuntimeMap, area.Snapshot.Objects, new Dictionary<AreaObjectKey, (string, SessionOpenableBaseline)>(), inventory);
            _objectTargetsStamp = stamp;
            GD.Print($"ARC-COMBAT-008 object-targets map={_readyMapName} objects={_objectTargets.Count}");
        }
        return _objectTargets;
    }

    /// <summary>ARC-COMBAT-008: a guarded left press with Alt in combat mode on a door, window, container or scenery: the original's click
    /// gives these the attack goal (ledger OD14; an item on the ground is picked up and a trap walked to, as without combat mode): true
    /// when the press belongs to combat.</summary>
    private bool TryAreaObjectAttack(Vector2 screen)
    {
        if (!AttackMapActive || !HeroInCombatMode || AreaObjectAtScreen(screen) is not { } key || ObjectTargets()?.Target(_areaSession!, key, AreaTrapped) is not { } target
            || target.Kind is Arcanum.Simulation.Combat.DamageableObjectKind.Item or Arcanum.Simulation.Combat.DamageableObjectKind.Trap)
            return false;
        if (_areaSession!.QueryHeroLife() is not { Status.IsActive: true })
        {
            _attackNotice = "the hero cannot act";
            RefreshAreaStatus();
            return true;
        }
        if (target.Refusal is not null)
        {
            _attackNotice = $"cannot attack: {target.Refusal}";
            GD.Print($"ARC-COMBAT-008 object-attack refused entity={target.Entity} detail={target.Refusal}");
            RefreshAreaStatus();
            return true;
        }
        CancelTalkApproach("an attack replaced it"); CancelAreaUseApproach("an attack replaced it"); CancelInventoryApproach("an attack replaced it");
        if (_attack is { } running && running.Entity == target.Entity && !running.Stop) return true;
        CancelAreaAttack(null);
        _attack = new(target, HeldAim());
        StartAttackGoal(_attack);
        return true;
    }

    /// <summary>ARC-COMBAT-008: the hit on an object at the action frame, through the session; the view then redraws what the hit
    /// destroyed, busted or switched off.</summary>
    private void RunObjectAttackHit(AttackGoal goal, AreaObjectAttackTarget target, int distance)
    {
        var owner = _areaSession!;
        // ARC-COMBAT-011: the object as it is now, its use script's trap included (a use may have changed the script).
        if (ObjectTargets()?.Target(owner, target.Row.Key, AreaTrapped) is { Refusal: null } now) target = now;
        PlaceHeroForDrop();
        // ARC-COMBAT-016: with a projectile art the shot flies and strikes on arrival (ledger PF1).
        var flight = ShotFlight(owner.QueryHeroAttack());
        var result = owner.AttackObject(owner.Version, target.Attack(distance, goal.Aim, AreaCombatHost.HeroWorn(owner, _areaKitDurability)) with
        {
            DropMap = AreaDropMapNow(), Flight = flight is not null,
            // ARC-COMBAT-017: a weapon that "default throws" leaves the hand and flies (ledger RH2).
            Throw = flight is null ? null : HeroThrow(goal),
        });
        goal.Attacks++;
        goal.Stop = result.Disposition == GameSessionDisposition.Rejected || result.Resolution?.Refusal is { } refusal && refusal != AttackRefusal.None;
        if (result.Shot is { } shot)
        {
            // ARC-COMBAT-018: a bow at expert shoots two projectiles on the same line.
            foreach (var projectile in result.Shots) LaunchProjectile(goal, projectile, flight!.Value, result.Resolution!, target.Row, null, target, null);
            // ARC-COMBAT-017: a throw is the throw goal, not the attack goal: nothing repeats it (ledger TW1).
            if (owner.QueryThrownShot(shot) is not null) goal.Stop = true;
            if (result.Life is null) { SyncAreaInventory(); RefreshAreaStatus(); return; }
        }
        FinishObjectHit(goal, target, result);
    }

    /// <summary>What follows an attack on a non-critter object (at once, or when a projectile struck it): the goal's break, the status
    /// line, a stun's end and the inventory.</summary>
    private void FinishObjectHit(AttackGoal? goal, AreaObjectAttackTarget target, SessionObjectAttackResult result)
    {
        var owner = _areaSession!;
        // A blocker the line of fire struck instead (ARC-COMBAT-012) does not end the goal on the clicked target when it breaks.
        if (goal is not null && target.Row.Key == goal.Row.Key)
            goal.Broke = result.Object?.Broken == true || result.Item?.Break is Arcanum.Simulation.Items.ItemBreakStep.Broken or Arcanum.Simulation.Items.ItemBreakStep.Deleted;
        _attackNotice = AreaObjectTargets.Describe(result);
        GD.Print($"ARC-COMBAT-008 object-attack-hit target={target.Entity} disposition={result.Disposition} failure={result.Failure}"
            + $" outcome={result.Resolution?.Outcome} damage={result.Object?.Damage}/{result.Item?.Condition?.Damage} break={result.Object?.Break}/{result.Item?.Break}"
            + $" host={result.Host} step={result.StepFailure} wear={string.Join(",", result.Wear?.Steps.Select(s => $"{s.Kind}:{s.Entity}:{s.Amount}:{s.Skip}") ?? [])}"
            + $" crit={AreaCombatHost.Criticals(result.Resolution, result.Criticals)}");
        if (result.Life is { } self && self.Events.HasFlag(LifeEvents.Stunned)) EndStun(owner, SessionLifeTarget.Hero, "hero");
        SyncAreaInventory();
        RefreshAreaStatus();
    }

    /// <summary>ARC-COMBAT-010: the session's actor placement follows the walking actor only at a save or a map change; a drop of the
    /// hero's item starts from it, so the attack places it on the committed cell first (as F7 does).</summary>
    private void PlaceHeroForDrop()
    {
        if (_worldActor.Session is not AreaGridSession grid || _areaSession is not { } owner) return;
        var cell = grid.Chart.ToWorld(grid.State.CommittedCell);
        var placed = owner.PlaceActor(owner.Version, new(new(CurrentAreaMap), new(checked((int)cell.X), checked((int)cell.Y)), grid.State.Facing));
        if (placed.Disposition == GameSessionDisposition.Rejected) GD.Print($"ARC-COMBAT-010 hero-placement refused failure={placed.Failure}");
    }

    /// <summary>ARC-COMBAT-010: the shown map as a drop reads it now: the step evidence with the doors' runtime passage, the map's
    /// rows less what the session destroyed or switched off, the dead as the session and the map data know them, and the hero's and
    /// the companions' live cells. Null without the actor.</summary>
    private AreaDropMap? AreaDropMapNow()
    {
        if (_worldActor.Session is not AreaGridSession session || _readyArea is not { } area || _areaSession is not { } owner) return null;
        var occupancy = AreaPcOccupancy() ?? AreaOccupancy.None;
        static CellCoord World(AreaGridSession s, CellCoord chart) { var w = s.Chart.ToWorld(chart); return new(checked((int)w.X), checked((int)w.Y)); }
        var live = occupancy.Occupied.Select(c => World(session, c)).Append(World(session, session.State.CommittedCell)).ToHashSet();
        var gone = ObjectTargets()?.Gone(owner) ?? new HashSet<AreaObjectKey>();
        var map = CurrentAreaMap;
        return new(session.Snapshot, area.Snapshot.Objects, occupancy.Moved, live, row => gone.Contains(row.Key),
            row => AreaCritterDead(owner, map, row), session.Portals);
    }

    // Whether a critter row is dead in the session or its map data (a drop's and a line of fire's dead test).
    private bool AreaCritterDead(GameSessionOwner owner, int map, AreaObjectRecord row) => AreaCritterDead(owner, map, row.Key);

    private bool AreaCritterDead(GameSessionOwner owner, int map, AreaObjectKey key) =>
        _combatHost?.Target(owner, map, key) is { } target &&
            (target.Dead || target.Build.Combat is { } combat && owner.QueryLife(combat.Target).View?.Status.IsDead == true);

    /// <summary>ARC-WORLD-073: whether a T0 critter record is dead in the session or its map data (the click route's critter checks).</summary>
    private bool AreaCritterDeadNow(AreaObjectKey key) => _areaSession is { } owner && AreaCritterDead(owner, CurrentAreaMap, key);

    /// <summary>ARC-WORLD-065: the shown map's objects the session destroyed or switched off (the same set while nothing changed);
    /// the original's object lists skip them, so they block no step (ledger FC1–FC3).</summary>
    private IReadOnlySet<AreaObjectKey> AreaRemovedObjects() =>
        _areaSession is { } session && ObjectTargets() is { } targets ? targets.Gone(session) : NoRemovedObjects;

    private static readonly IReadOnlySet<AreaObjectKey> NoRemovedObjects = new HashSet<AreaObjectKey>();
    private (IReadOnlySet<AreaObjectKey> Removed, AreaOccupancy Value)? _removedOnlyOccupancy;

    /// <summary>ARC-WORLD-016/065: the PC's live overlay: the companions' (which moves the removed objects too), or with no
    /// companions the removed objects alone (null when there are none).</summary>
    private AreaOccupancy? AreaPcOccupancy()
    {
        if (_areaCompanions is { } group) return group.OccupancyForPc();
        var removed = AreaRemovedObjects();
        if (removed.Count == 0) return null;
        if (_removedOnlyOccupancy is { } known && ReferenceEquals(known.Removed, removed)) return known.Value;
        var value = AreaOccupancy.None.WithRemoved(removed);
        _removedOnlyOccupancy = (removed, value);
        return value;
    }

    /// <summary>ARC-COMBAT-011: whether a row's use script is a trap (OD6), through the shown map's use-script host; false without
    /// script content (the status line then cannot say that a trap would fire).</summary>
    private bool AreaTrapped(AreaObjectRecord row) => _readyArea is { } area && UseScriptHost(area.Snapshot)?.IsTrap(row) == true;

    // ARC-COMBAT-009: the original keeps a critter stunned while its stun animation (goal 57) runs; the viewer plays none, so it ends
    // the stun at once, as the session's contract has it for an animation that could not start (the duration is NOT READY).
    private static void EndStun(GameSessionOwner owner, SessionLifeTarget target, string who)
    {
        var cleared = owner.ClearStun(owner.Version, target);
        GD.Print($"ARC-COMBAT-009 stun-ended who={who} disposition={cleared.Disposition} (no stun animation in the viewer)");
    }

    /// <summary>The attack goal's first step: the target within the weapon's reach (and on the ranged path with a clear shot,
    /// ARC-COMBAT-015) starts the animation; otherwise the click route walks toward it first (the goal's move-near step).</summary>
    private void StartAttackGoal(AttackGoal goal)
    {
        if (_worldActor.Session is not AreaGridSession session || _areaSession is not { } owner) { CancelAreaAttack("no actor"); return; }
        var hero = owner.QueryHeroAttack();
        if (hero is null) { CancelAreaAttack("needs a hero (start with --hero)"); return; }
        if (!hero.Supported) { CancelAreaAttack($"a {hero.Skill} weapon cannot attack yet"); return; }
        // ARC-COMBAT-019: no attack starts while the hero's boomerang is away (0x434B00).
        if (owner.QueryBoomerangInFlight()) { CancelAreaAttack("the boomerang is still in flight"); return; }
        var cell = session.Chart.ToWorld(session.State.CommittedCell);
        var row = goal.Row;
        var distance = Chebyshev(row, (cell.X, cell.Y));
        goal.ClearShotStop = false; goal.TestedCell = null;
        if (InAttackReach(row, new(checked((int)cell.X), checked((int)cell.Y)), hero))
        {
            StopWorldRoute("ROUTE_REPLACED_BY_ATTACK");
            goal.Approaching = false;
            if (session.State.Phase == MovementPhase.Settled) BeginAttackAnimation(goal, hero);
            return;
        }
        // A walk that arrived and still left the target out of reach is not tried again and again.
        if (goal.Approaches >= 3) { CancelAreaAttack(distance <= HeroReach(hero) ? "no clear shot at the target" : $"the target stays {distance} cells away"); return; }
        if (WorldRoute(session) is { } route && AttackApproachCell(session, row.WorldX!.Value, row.WorldY!.Value) is { } approach)
        {
            goal.Approaches++;
            StopWorldRoute("ROUTE_REPLACED_BY_ATTACK");
            route.SetTarget(approach, WorldTime);
            goal.Approaching = true;
            SetObjectApproachGait();
            _worldRouteDiagnostic = DescribeWorldRoute(route); _worldRouteClick = $"{approach.X},{approach.Y} walk to attack";
            _attackNotice = distance <= HeroReach(hero) ? "walking to attack: no clear shot" : $"walking to attack: {distance} cells away";
            GD.Print($"ARC-COMBAT-005 attack-approach distance={distance} goal={approach}");
            RefreshAreaStatus();
            return;
        }
        CancelAreaAttack(distance <= HeroReach(hero) ? "no clear shot at the target and no approach" : $"the target is {distance} cells away and no route reaches it");
    }

    /// <summary>
    /// ARC-WORLD-073: the cell the attack's approach walks to, as the original's (ledger AP1–AP4, 0x4280D0): the shared request's route to
    /// the target's own tile with flag 0x1 and the walk's first budget, one step short of it, over the PC's steps with living critters
    /// blocking in and out of combat mode (the approach never sets the request's flag 0x10); null when it has no route (the attack ends).
    /// The cell is reachable by construction; the weapon's reach is tested when the walk ends (the ranged walk goes to the same cell).
    /// </summary>
    internal static CellCoord? AttackApproachCell(AreaGridSession session, long worldX, long worldY)
    {
        var routes = new AreaRouteSource(session.Snapshot, session.Chart, session.Occupancy, () => session.Portals?.Invoke());
        var target = session.Chart.ToChart(new(worldX, worldY));
        return RoutePlanner.PlanApproach(routes, session.State.CommittedCell, target) is { Outcome: RouteOutcome.Found } plan ? plan.Target : null;
    }

    /// <summary>Starts one attack animation: the clip from the attack ART and the hero's Speed and weapon, the attack sound roll
    /// (a draw the original makes at every attack start), the facing toward the target.</summary>
    private void BeginAttackAnimation(AttackGoal goal, SessionHeroAttack hero)
    {
        // ARC-COMBAT-020: in a turn-based round the attack's start charges its action points (ledger CT2); refused, there is no attack.
        if (!ChargeHeroAction(TurnBasedRules.AttackCost(hero.WeaponSpeed), "attack")) { CancelAreaAttack(_turnBasedNotice); return; }
        var art = _worldActor.Profile.Attack!;
        var rate = RealTimeCombatRules.AttackFramesPerSecond(AttackAnimation.Attack, AttackArtWeapon.Unarmed, Math.Clamp(hero.Speed, 1, RealTimeCombatRules.MaxStat),
            art.RawFramesPerSecond);
        var clip = new AttackClip(art.FramesPerGroup, art.ActionFrame, RealTimeCombatRules.AttackFramePauseMs(rate, hero.WeaponSpeed), AttackArtWeapon.Unarmed);
        var sound = _areaSession!.Roll("combat.AttackSound", 1, RealTimeCombatRules.AttackSoundSides);
        var session = (AreaGridSession)_worldActor.Session!;
        var targetCell = session.Chart.ToChart(new(goal.Row.WorldX!.Value, goal.Row.WorldY!.Value));
        goal.Octant = AreaCombatHost.Facing(session.State.CommittedCell, targetCell) is { } facing ? DiagnosticGridActor.Octant(facing)
            : DiagnosticGridActor.RenderState(session.State).Facing;
        goal.Player = new AreaAttackPlayer(clip, WorldMilliseconds());
        goal.RepeatAtMs = null;
        _worldActor.AttackPose = (goal.Octant, 0);
        PlayAttackStartSounds(sound.Roll?.Value);
        GD.Print($"ARC-COMBAT-005 attack-start target={goal.Entity} frames={clip.Frames} action={clip.ActionFrame} pause={clip.PauseMs} rate={rate}"
            + $" sound={sound.Roll?.Value.ToString() ?? sound.Failure.ToString()} aim={goal.Aim?.ToString() ?? "none"}");
        PublishWorldSelection();
    }

    /// <summary>Per frame, before the actor advances: the walk to the target, the animation's due updates and the hit, and the
    /// auto-attack repeat.</summary>
    private void ProcessAreaAttack()
    {
        if (_attack is not { } goal) return;
        if (!AttackMapActive || _areaSession is not { } owner || _worldActor.Session is not AreaGridSession session) { CancelAreaAttack("the map changed"); return; }
        if (WorldModal || ConversationOpen) { CancelAreaAttack("a dialog or window opened"); return; }
        if (owner.QueryHeroLife() is not { Status.IsActive: true }) { CancelAreaAttack("the hero cannot act"); return; }
        if (goal.Player is null && !HeroInCombatMode) { CancelAreaAttack("left combat mode"); return; }
        if (goal.Approaching)
        {
            ApproachClearShot(goal, session, owner);
            if (session.State.Phase != MovementPhase.Settled || _worldRoute is not { } route || route.Status == RouteFollowStatus.Following) return;
            if (route.Status != RouteFollowStatus.Arrived && !goal.ClearShotStop)
            { CancelAreaAttack("walk to attack stopped: " + (route.StopReason ?? route.Status.ToString())); return; }
            StartAttackGoal(goal);
            return;
        }
        var now = WorldMilliseconds();
        if (goal.Player is null)
        {
            if (goal.RepeatAtMs is { } repeat && now < repeat) return;
            if (session.State.Phase != MovementPhase.Settled) return;
            StartAttackGoal(goal);
            return;
        }
        var changed = false;
        foreach (var (_, update) in goal.Player.Advance(now, () => owner.QueryHeroLife() is { Status.IsActive: true }))
        {
            changed = true;
            if (update.Kind == AttackUpdateKind.Hit) RunAttackHit(goal, session);
            if (update.Kind == AttackUpdateKind.Ended || !goal.Player.Playing) break;
        }
        if (_attack != goal) return;
        if (goal.Player.Playing)
        {
            if (changed) { _worldActor.AttackPose = (goal.Octant, goal.Player.Playback.Frame); PublishWorldSelection(); }
            return;
        }
        _worldActor.AttackPose = null;
        goal.Player = null;
        // An object stays a target while the session draws it and the last hit did not break it (ARC-COMBAT-008).
        var target = goal.Combat is null ? null : _combatHost!.Target(owner, CurrentAreaMap, goal.Row.Key);
        var dead = goal.Combat is null ? goal.Broke || _objectTargets?.Target(owner, goal.Row.Key) is null
            : target?.Dead != false || owner.QueryLife(goal.Combat.Target).View?.Status.IsDead != false;
        var repeats = !goal.Stop && RealTimeCombatRules.Repeats(new(HeroInCombatMode, true, _gameSettings.AutoAttack,
            owner.QueryCombatRound() is not null, goal.Combat is not null && target is null, dead, false, false, false, owner.QueryBoomerangInFlight()));
        if (repeats) goal.RepeatAtMs = now + RealTimeCombatRules.RepeatDelayMs;
        else _attack = null;
        GD.Print($"ARC-COMBAT-005 attack-end target={goal.Entity} attacks={goal.Attacks} repeat={repeats}");
        PublishWorldSelection(); RefreshAreaStatus();
    }

    /// <summary>The update that shows the action frame: the target still within reach is hit through the session, else the
    /// animation ends without a hit.</summary>
    private void RunAttackHit(AttackGoal goal, AreaGridSession session)
    {
        var owner = _areaSession!;
        var cell = session.Chart.ToWorld(session.State.CommittedCell);
        var distance = Chebyshev(goal.Row, (cell.X, cell.Y));
        if (distance > HeroReach(owner.QueryHeroAttack()))
        {
            goal.Player!.Abort();
            _attackNotice = "the target is out of reach";
            return;
        }
        // ARC-COMBAT-012: a reach above 1 takes the original's ranged path, whose line of fire may strike something else first.
        var trace = RangedPath(owner.QueryHeroAttack()) ? LoggedLineOfFire(session, goal.Row) : null;
        if (trace?.Blocker is { } blocker && blocker.Key != goal.Row.Key) { StrikeBlocker(goal, blocker, session); return; }
        if (goal.Object is { } struck) { RunObjectAttackHit(goal, struck, (int)distance); return; }
        HitCritter(goal, goal, (int)distance, ShotLine(trace, goal.Row));
    }

    /// <summary>ARC-COMBAT-012/015: the attack takes the original's ranged path (RA1: the throwing skill or a reach above 1).</summary>
    private bool RangedPath(SessionHeroAttack? hero) => HeroReach(hero) > 1 || hero?.Skill == CombatSkill.Throwing;

    /// <summary>ARC-COMBAT-012: the line of fire from the tile <paramref name="from"/> (world cell) to the target's (0x4ADE00), from the
    /// tile's centre (the hero stands settled, so no pixel offset); null without the shown map's line-of-fire view or target cell.</summary>
    // A target next to the hero is read as the Original setting reads it (owner decision 2026-10-28, LineOfFireRules.ReachTrace): the
    // reach test, the redirect and the check read the same line.
    private LineOfFireTrace<AreaObjectRecord>? TraceLineOfFire(CellCoord from, AreaObjectRecord target) =>
        target.WorldX is { } x && target.WorldY is { } y && AreaLineOfFireNow() is { } map && _areaSession is { } owner
            ? LineOfFireRules.ReachTrace(from, 0, 0, new(checked((int)x), checked((int)y)), map, owner.Rules.Combat) : null;

    /// <summary>ARC-COMBAT-012: the line of fire from the hero's committed cell to <paramref name="target"/>, logged; the original strikes
    /// its blocker instead of the target (ledger RA4).</summary>
    private LineOfFireTrace<AreaObjectRecord>? LoggedLineOfFire(AreaGridSession session, AreaObjectRecord target)
    {
        var hero = session.Chart.ToWorld(session.State.CommittedCell);
        if (TraceLineOfFire(new(checked((int)hero.X), checked((int)hero.Y)), target) is not { } trace) return null;
        GD.Print($"ARC-COMBAT-012 line-of-fire target={target.Key.Kind}:{target.Key.SourceIndex} cost={trace.Cost} failed={trace.LineFailed}"
            + $" tiles={trace.Tiles.Count} blocker={(trace.Blocker is { } b ? $"{b.Key.Kind}:{b.Key.SourceIndex} type={b.Type}" : "none")}");
        return trace;
    }

    /// <summary>ARC-COMBAT-015: what the shared check reads of a line traced to <paramref name="struck"/> (SC8): its cost and whether
    /// another object stops it.</summary>
    private static SessionLineOfFire? ShotLine(LineOfFireTrace<AreaObjectRecord>? trace, AreaObjectRecord struck) =>
        trace is null ? null : new SessionLineOfFire(Math.Min(trace.Cost, AttackResolver.MaxMagnitude), trace.Blocker is { } b && b.Key != struck.Key);

    /// <summary>
    /// ARC-COMBAT-015: the attack goal's reach test (0x429960, ledger EX8, RH1) from the world cell <paramref name="from"/>: the target
    /// within the weapon's reach and a clear shot (<see cref="LineOfFireRules.InReach{TObject}"/>: the line's cost below 26 and nothing
    /// but the target stopping it), for every weapon, a melee one next to its target too. A map without a line-of-fire view tests the
    /// reach alone.
    /// </summary>
    private bool InAttackReach(AreaObjectRecord target, CellCoord from, SessionHeroAttack hero)
    {
        var reach = HeroReach(hero);
        var distance = Chebyshev(target, (from.X, from.Y));
        if (distance > reach) return false;
        if (TraceLineOfFire(from, target) is not { } trace) return true;
        // The original compares object handles: the blocker is the target when it is the same row.
        return LineOfFireRules.InReach((int)distance, reach, trace, trace.Blocker is { } blocker && blocker.Key == target.Key ? blocker : target);
    }

    /// <summary>
    /// ARC-COMBAT-015 (EX9): on the ranged path, Steamwoven stops the walk to the target at the first cell the hero commits to from
    /// which the reach test passes (the started step still ends at its cell centre); the Original setting
    /// (<see cref="CombatAnomalyProfile.RangedApproachSkipsClearShot"/>) walks on to the approach's tile, as the original's callback
    /// does while the attack is possible.
    /// </summary>
    private void ApproachClearShot(AttackGoal goal, AreaGridSession session, GameSessionOwner owner)
    {
        if (goal.ClearShotStop || owner.Rules.Combat.RangedApproachSkipsClearShot || _worldRoute is not { Status: RouteFollowStatus.Following }
            || owner.QueryHeroAttack() is not { } hero || !RangedPath(hero)) return;
        var cell = session.State.CommittedCell;
        if (goal.TestedCell == cell) return;
        goal.TestedCell = cell;
        var world = session.Chart.ToWorld(cell);
        if (!InAttackReach(goal.Row, new(checked((int)world.X), checked((int)world.Y)), hero)) return;
        goal.ClearShotStop = true;
        StopWorldRoute("ROUTE_STOPPED_AT_CLEAR_SHOT");
        _clearShotStops++;
        GD.Print($"ARC-COMBAT-015 clear-shot stop at={world.X},{world.Y} target={goal.Entity}");
    }

    /// <summary>ARC-COMBAT-012: the shown map as the line of fire reads it now: the step evidence's tiles, the map's rows less what the
    /// session destroyed or switched off, the dead as the session and the map data know them, the doors' and windows' session state,
    /// and the companions on their live cells.</summary>
    private AreaLineOfFire? AreaLineOfFireNow()
    {
        if (_worldActor.Session is not AreaGridSession session || _readyArea is not { } area || _areaSession is not { } owner) return null;
        var occupancy = AreaPcOccupancy() ?? AreaOccupancy.None;
        var live = new Dictionary<CellCoord, AreaObjectRecord>();
        if (_areaCompanions is { } group)
            foreach (var companion in group.Companions)
            {
                var (x, y) = group.WorldCell(companion.Speaker);
                live.TryAdd(new(checked((int)x), checked((int)y)), companion.Speaker.Row);
            }
        var gone = ObjectTargets()?.Gone(owner) ?? new HashSet<AreaObjectKey>();
        var map = CurrentAreaMap;
        var portals = _areaPortals;
        return new(session.Snapshot, area.Snapshot.Objects, occupancy.Moved, live, row => gone.Contains(row.Key), row => AreaCritterDead(owner, map, row),
            row => portals is not null && portals.TryGet(row.Key, out var portal) && owner.QueryOpenable(portals.Openable(portal), portal.Baseline) is var state
                ? state.Open || (state.Flags & Arcanum.Simulation.Locks.LockRules.BustedBit) != 0 : null);
    }

    /// <summary>ARC-COMBAT-012: the shot strikes the object that stopped the line of fire: an NPC through the critter attack (its own
    /// rolls), a door, window, container, scenery or trap through the object attack, and since ARC-COMBAT-015 a wall, which takes the
    /// shot and nothing else (ledger RH5); striking a companion or an NPC without combat values is not ready, and the goal ends without
    /// a hit. The goal's reach test (RH1) admits only a clear line, so a blocker shows up here only when the line changed after it.</summary>
    private void StrikeBlocker(AttackGoal goal, AreaObjectRecord blocker, AreaGridSession session)
    {
        var owner = _areaSession!;
        var cell = session.Chart.ToWorld(session.State.CommittedCell);
        var distance = (int)Chebyshev(blocker, (cell.X, cell.Y));
        if (!IsAreaCompanion(blocker.Key) && _combatHost!.Target(owner, CurrentAreaMap, blocker.Key) is { Dead: false, Build.Combat: { } combat } npc)
        {
            GD.Print($"ARC-COMBAT-012 line-of-fire strikes npc={npc.Entity} instead of {goal.Entity}");
            _lineOfFireStruck = npc.Entity;
            // ARC-COMBAT-015: the shared check traces its own line, to the struck NPC (SC8).
            HitCritter(goal, new AttackGoal(npc, combat, goal.Aim), distance, ShotLine(LoggedLineOfFire(session, blocker), blocker));
            return;
        }
        if (ObjectTargets()?.Target(owner, blocker.Key, AreaTrapped) is { Refusal: null } struck
            && struck.Kind is not Arcanum.Simulation.Combat.DamageableObjectKind.Item)
        {
            GD.Print($"ARC-COMBAT-012 line-of-fire strikes object={struck.Entity} instead of {goal.Entity}");
            _lineOfFireStruck = struck.Entity;
            RunObjectAttackHit(goal, struck, distance);
            return;
        }
        if (blocker.Type == 0)
        {
            GD.Print($"ARC-COMBAT-015 line-of-fire strikes wall={blocker.Key.Kind}:{blocker.Key.SourceIndex} instead of {goal.Entity}");
            _lineOfFireStruck = $"wall:{blocker.Key.SourceIndex}";
            PlaceHeroForDrop();
            var flight = ShotFlight(owner.QueryHeroAttack());
            var result = owner.AttackObject(owner.Version, new SessionObjectAttack
            {
                Wall = true, Aim = goal.Aim, Distance = distance, HeroWorn = AreaCombatHost.HeroWorn(owner, _areaKitDurability), DropMap = AreaDropMapNow(),
                // ARC-COMBAT-016: the projectile flies to the wall and strikes it on arrival.
                Flight = flight is not null, Throw = flight is null ? null : HeroThrow(goal),
            });
            goal.Attacks++;
            goal.Stop = result.Disposition == GameSessionDisposition.Rejected || result.Resolution?.Refusal is { } refusal && refusal != AttackRefusal.None;
            if (result.Shot is { } shot)
            {
                foreach (var projectile in result.Shots) LaunchProjectile(goal, projectile, flight!.Value, result.Resolution!, blocker, null, null, null);
                // ARC-COMBAT-017: a throw is the throw goal, not the attack goal: nothing repeats it (ledger TW1).
                if (owner.QueryThrownShot(shot) is not null) goal.Stop = true;
                if (result.Life is null) { SyncAreaInventory(); RefreshAreaStatus(); return; }
            }
            _attackNotice = "the line of fire struck a wall: " + AreaObjectTargets.Describe(result);
            GD.Print($"ARC-COMBAT-015 wall-hit disposition={result.Disposition} failure={result.Failure} outcome={result.Resolution?.Outcome}"
                + $" prevented={result.Resolution?.Damage?.Prevented} ammo={result.Ammunition?.UnitsAfter.ToString() ?? "none"} step={result.StepFailure}");
            if (result.Life is { } self && self.Events.HasFlag(LifeEvents.Stunned)) EndStun(owner, SessionLifeTarget.Hero, "hero");
            SyncAreaInventory();
            RefreshAreaStatus();
            return;
        }
        goal.Stop = true;
        _attackNotice = $"the line of fire stops at {(IsAreaCompanion(blocker.Key) ? "a companion" : $"an object of type {blocker.Type}")}: striking it is not ready";
        GD.Print($"ARC-COMBAT-012 line-of-fire stopped type={blocker.Type} (not ready)");
        RefreshAreaStatus();
    }

    /// <summary>The hit on an NPC through the session: <paramref name="struck"/> is the NPC hit (the goal's own target, or the one the
    /// line of fire struck instead), <paramref name="goal"/> the goal whose count and end it sets.</summary>
    private void HitCritter(AttackGoal goal, AttackGoal struck, int distance, SessionLineOfFire? line)
    {
        var owner = _areaSession!;
        PlaceHeroForDrop();
        // ARC-COMBAT-016: with a projectile art the shot flies and strikes on arrival (ledger PF1).
        var flight = ShotFlight(owner.QueryHeroAttack());
        // ARC-COMBAT-017: a weapon that "default throws" leaves the hand and flies (ledger RH2).
        var result = owner.AttackCritter(owner.Version, CritterAttack(struck, distance, line) with
        {
            Flight = flight is not null, Throw = flight is null ? null : HeroThrow(goal),
        });
        goal.Attacks++;
        // A refused attack, or one the resolver refused, ends the goal: repeating it would refuse again.
        goal.Stop = result.Disposition == GameSessionDisposition.Rejected || result.Resolution?.Refusal is { } refusal && refusal != AttackRefusal.None;
        if (result.Shot is { } shot)
        {
            // ARC-COMBAT-018: a bow at expert shoots two projectiles on the same line; a missed throw flies to its scattered tile.
            foreach (var projectile in result.Shots) LaunchProjectile(goal, projectile, flight!.Value, result.Resolution!, struck.Row, struck, null, result.Scatter);
            // ARC-COMBAT-017: a throw is the throw goal, not the attack goal: nothing repeats it (ledger TW1).
            if (owner.QueryThrownShot(shot) is not null) goal.Stop = true;
            // A critical miss turned on the hero at the shot; anything else waits for the strike.
            if (result.Life is null) { _lastCritterAttackForSelfTest = result; SyncAreaInventory(); RefreshAreaStatus(); return; }
        }
        FinishCritterHit(struck, result);
    }

    /// <summary>ARC-COMBAT-005: the hero's attack on <paramref name="struck"/> as the session sees it now (its worn items, cell and the
    /// shown map for drops).</summary>
    private SessionAttack CritterAttack(AttackGoal struck, int distance, SessionLineOfFire? line)
    {
        var owner = _areaSession!;
        // ARC-COMBAT-008: the two sides' worn items with their durability values, so the attack wears them (ARC-COMBAT-007).
        return new(struck.Combat!)
        {
            Aim = struck.Aim, Distance = distance,
            // ARC-COMBAT-015: the bow and firearms skills read the line of fire's cost and blocker (SC8).
            LineOfFire = line,
            HeroWorn = AreaCombatHost.HeroWorn(owner, _areaKitDurability),
            TargetWorn = _combatHost!.WornItems(owner, CurrentAreaMap, struck.Entity),
            // ARC-COMBAT-009: a critical drops the NPC's items where it stands (its map data's cell; NPCs do not move here).
            TargetCell = struck.Row.WorldX is { } x && struck.Row.WorldY is { } y ? new(checked((int)x), checked((int)y)) : null,
            // ARC-COMBAT-010: the original's draws choose the cell a dropped item lands on, over the shown map.
            DropMap = AreaDropMapNow(),
        };
    }

    /// <summary>What follows an attack on an NPC (at once, or when a projectile struck it): the sounds, the status line, a stun's
    /// end, the followers' levels, the death pose and the drawn bodies and inventory.</summary>
    private void FinishCritterHit(AttackGoal struck, SessionAttackResult result)
    {
        var owner = _areaSession!;
        _lastCritterAttackForSelfTest = result;
        PlayAttackResultSounds(result, struck.Row, struck.Entity);
        var life = owner.QueryLife(struck.Combat!.Target).View;
        _attackNotice = AreaCombatHost.Describe(result, life?.Status.HitPoints, life?.Status.MaxHitPoints);
        GD.Print($"ARC-COMBAT-005 attack-hit target={struck.Entity} disposition={result.Disposition} failure={result.Failure}"
            + $" outcome={result.Resolution?.Outcome} cause={result.Resolution?.MissCause} damage={result.Life?.HitPointDamage} events={result.Life?.Events}"
            + $" hp={life?.Status.HitPoints}/{life?.Status.MaxHitPoints} xp={result.HitAward?.Award}/{result.KillAward?.Award} step={result.StepFailure}"
            + $" wear={string.Join(",", result.Wear?.Steps.Select(s => $"{s.Kind}:{s.Entity}:{s.Amount}:{s.Skip}") ?? [])}"
            + $" crit={AreaCombatHost.Criticals(result.Resolution, result.Criticals)}");
        if (result.Life is { } took && took.Events.HasFlag(LifeEvents.Stunned))
            EndStun(owner, result.Recipient == DamageRecipient.Target ? struck.Combat!.Target : SessionLifeTarget.Hero,
                result.Recipient == DamageRecipient.Target ? struck.Entity : "hero");
        LevelAreaFollowers(result.HitAward);
        LevelAreaFollowers(result.KillAward);
        if (result.Recipient == DamageRecipient.Target && result.Life is { } dealt && dealt.Events.HasFlag(LifeEvents.Died))
            _deathAnimations[(CurrentAreaMap, struck.Entity)] = AreaFallenPoses.Animation(dealt.Death);
        SyncAreaCombatBodies();
        SyncAreaInventory();
        RefreshAreaStatus();
    }

    /// <summary>
    /// ARC-WORLD-036: passes an award's follower step to the hero's followers and, by the rule setting (ARC-WORLD-047, owner decision
    /// D7), to its waiting followers, with their stored fields and effects (<see cref="AreaAdvancement.LevelFollowers"/>).
    /// </summary>
    private void SyncAreaAwards()
    {
        // ARC-UI-046: level-up entries queued while the texts were still loading.
        FlushLevelUps(TimeSpan.Zero);
        // ARC-WORLD-048: a visitor's talk queues its awards on its own host.
        foreach (var host in TalkHosts().ToArray())
        foreach (var award in host.State.TakeAwards()) LevelAreaFollowers(award);
    }

    private void LevelAreaFollowers(SessionExperienceAward? award)
    {
        // ARC-UI-046: the hero's level-up messages come first, as the source posts them before passing the step on.
        ShowLevelUps(award);
        if (award?.Followers is null || _areaSession is null || _combatHost is null || _areaEffectRules is null) return;
        var session = _areaSession;
        var report = AreaAdvancement.LevelFollowers(session, award, AreaFollowerSheet, AreaWaitingPlace);
        foreach (var follower in report.Raised)
            GD.Print($"ARC-WORLD-036 follower-level entity={follower.Record!.Entity} level={follower.Record.Sheet.Level}"
                + $" points={follower.Level?.PointsGained} purchases={follower.Scheme?.Purchases.Length ?? 0} completed={follower.Scheme?.Completed}");
        foreach (var entity in report.Deferred) GD.Print($"ARC-WORLD-047 level step kept for entity={entity} (sheet not readable here)");
        foreach (var missed in report.MissedApplied)
            GD.Print($"ARC-WORLD-047 missed level steps applied entity={missed.Record!.Entity} level={missed.Record.Sheet.Level}");
    }

    /// <summary>ARC-WORLD-047: a waiting follower's missed level steps, applied when its map data can be read (companions bind).</summary>
    private void ApplyMissedAreaLevels()
    {
        if (_areaSession is not { } session || _combatHost is null || _areaEffectRules is null) return;
        foreach (var applied in AreaAdvancement.ApplyMissed(session, AreaFollowerSheet))
            GD.Print($"ARC-WORLD-047 missed level steps applied entity={applied.Record!.Entity} level={applied.Record.Sheet.Level}");
    }

    // An NPC's loaded sheet and effects from the map whose objects hold it, or null when no covered map does.
    private (SessionNpcAdvancement Loaded, Arcanum.Simulation.Character.AdvancementModifiers Modifiers)? AreaFollowerSheet(string entity) =>
        _combatHost is { } host && _areaEffectRules is { } effects && host.HomeMap(entity) is { } home && host.Fields(home, entity) is { } fields
            ? AreaAdvancement.Npc(entity, fields, effects) : null;

    // Where a waiting follower stands: dead by the session (its life record or any map's seed, wherever it waits); among the loaded
    // objects when it is a companion drawn on the current map, with its tile offset from the PC.
    private AreaWaitingPlace AreaWaitingPlace(string entity)
    {
        var dead = _areaSession?.QueryNpcDead(entity) == true;
        if (_areaCompanions?.Companions.FirstOrDefault(c => c.Entity == entity) is not { } companion || _worldActor.Session is not AreaGridSession pc)
            return new(dead, null);
        var (at, from) = (companion.Grid.State.CommittedCell, pc.State.CommittedCell);
        return new(dead, (at.X - from.X, at.Y - from.Y));
    }

    /// <summary>ARC-SCRIPT-016: an NPC's advancement as its home map stores it, for a script's scheme change; null without the combat
    /// host or the effect rules (no <c>--hero</c>), or for an object that gives no admissible NPC sheet.</summary>
    private SessionNpcAdvancement? AreaNpcAdvancement(string entity) =>
        _combatHost is { } host && _areaEffectRules is { } effects && host.HomeMap(entity) is { } home && host.Fields(home, entity) is { } fields
            ? AreaAdvancement.Npc(entity, fields, effects)?.Loaded : null;

    /// <summary>Stops the attack goal (a new click, the stick, a dialog, the map or the hero's state); <paramref name="why"/>
    /// goes to the status line.</summary>
    private void CancelAreaAttack(string? why)
    {
        if (_attack is null) return;
        _attack = null;
        _worldActor.AttackPose = null;
        if (why is not null) { _attackNotice = "attack stopped: " + why; GD.Print("ARC-COMBAT-005 attack-cancel " + why); }
        if (_worldActor.Session is not null && _readyActorPlan is not null) PublishWorldSelection();
    }

    // The fallen bodies of the shown map (an NPC the session killed or knocked out, drawn in its pose's last frame in place of its
    // T0 body), the poses read so far, the dying animation each kill chose, and the frame repository with the poses added.
    private Dictionary<AreaObjectKey, AreaDrawCommand> _fallenBodies = [];
    private readonly Dictionary<(int Map, string Entity, int Animation), AreaFallenLoad> _fallenPoses = [];
    private readonly Dictionary<(int Map, string Entity), int> _deathAnimations = [];
    private (AreaLoadResult Area, AreaFrameOverlay Frames, Dictionary<AreaObjectKey, AreaDrawCommand> Bodies, Dictionary<AreaVisualKey, AreaVisualRecord> Visuals)? _areaOverlay;
    private object? _fallenStamp;

    /// <summary>The shown map's frames with the fallen poses read so far (one instance per published map).</summary>
    private IReadOnlyDictionary<AreaFrameKey, DecodedAreaFrame> AreaFrames()
    {
        var area = _readyArea ?? throw new InvalidOperationException("AREA_NOT_PUBLISHED");
        if (_areaOverlay is not { } overlay || !ReferenceEquals(overlay.Area, area))
        {
            _groundBodies = []; _groundOnly = []; _groundStamp = null; _extraVisuals = [];
            _areaOverlay = overlay = (area, new AreaFrameOverlay(area.Frames),
                area.Plan.Commands.Where(c => c.Identity is not null).GroupBy(c => c.Identity!.Value).ToDictionary(g => g.Key, g => g.First()),
                area.Snapshot.Visuals.ToDictionary(v => v.Key));
            _fallenBodies = []; _fallenStamp = null;
        }
        return overlay.Frames;
    }

    /// <summary>The poses added to the shown map's repository: they may be resident besides its own frames.</summary>
    private (int Frames, long Bytes) AreaOverlayExtra() => _areaOverlay is { } overlay && ReferenceEquals(overlay.Area, _readyArea)
        ? (overlay.Frames.AddedCount, overlay.Frames.AddedBytes) : (0, 0);

    /// <summary>The bodies drawn away from their T0 command: companions where they stand, then the fallen bodies (a companion's
    /// own command wins), then the portals on another frame.</summary>
    private IReadOnlyList<AreaDrawCommand>? AreaMovingCommands()
    {
        var companions = AreaCompanionCommands();
        if ((_fallenBodies.Count == 0 && _portalBodies.Count == 0 && _groundBodies.Count == 0 && _projectileBodies.Count == 0 && _eyeCandyBodies.Count == 0)
            || _readyArea is null || _areaOverlay?.Area != _readyArea) return companions;
        var moving = companions?.ToList() ?? [];
        var taken = moving.Select(c => c.Identity).ToHashSet();
        moving.AddRange(_fallenBodies.Values.Where(c => !taken.Contains(c.Identity)));
        // ARC-WORLD-051: doors and windows off their T0 frame.
        moving.AddRange(_portalBodies.Values);
        // ARC-RENDER-033: busted scenery and traps, and the items lying away from a T0 body.
        moving.AddRange(_groundBodies.Values.Where(c => !taken.Contains(c.Identity)));
        // ARC-COMBAT-016: projectiles in flight.
        moving.AddRange(_projectileBodies.Values);
        // ARC-MAGIC-017: spells' eye candy on their objects.
        moving.AddRange(_eyeCandyBodies.Values);
        return moving;
    }

    /// <summary>The NPCs of the shown map the session killed or knocked out are drawn fallen: the last frame of the dying
    /// animation the kill chose (the fall after a reload), or of the fall-down animation while unconscious. Recomputed when the
    /// session or the map changes; a pose that cannot be read leaves the T0 body and says why once.</summary>
    private void SyncAreaCombatBodies()
    {
        if (_readyArea is not { } area || _areaSession is not { } session || _combatHost is null || _inventoryHost is null
            || _inventoryHost.Maps.FirstOrDefault(m => m.RuntimeMap == area.Snapshot.RuntimeMap) is not { } map)
        {
            if (_fallenBodies.Count > 0) _fallenBodies = [];
            return;
        }
        var frames = (AreaFrameOverlay)AreaFrames();
        var stamp = (area, session, session.Version.Revision);
        if (Equals(stamp, _fallenStamp)) return;
        _fallenStamp = stamp;
        var overlay = _areaOverlay!.Value;
        var fallen = new Dictionary<AreaObjectKey, AreaDrawCommand>();
        foreach (var holder in map.Seed.Holders)
        {
            if (holder.Kind != SessionHolderKind.Npc || holder.Dead || session.QueryLifeRecord(holder.Entity) is null) continue;
            if (_combatHost.Build(map.RuntimeMap, holder.Entity).Combat is not { } combat || session.QueryLife(combat.Target).View is not { } life) continue;
            int? animation = life.Status.IsDead ? _deathAnimations.GetValueOrDefault((map.RuntimeMap, holder.Entity), AreaFallenPoses.FallDown)
                : life.Status.IsUnconscious ? AreaFallenPoses.FallDown : null;
            var row = map.Row(holder.Entity);
            if (animation is not { } anim || !overlay.Bodies.TryGetValue(row.Key, out var command) || !overlay.Visuals.TryGetValue(command.Visual, out var visual)) continue;
            if (!_fallenPoses.TryGetValue((map.RuntimeMap, holder.Entity, anim), out var load))
            {
                load = AreaFallenPoses.Load(visual, area.ArtSources.GetValueOrDefault(visual.Entry.Source), anim);
                _fallenPoses[(map.RuntimeMap, holder.Entity, anim)] = load;
                GD.Print($"ARC-COMBAT-005 fallen-pose entity={holder.Entity} animation={anim} status={load.Status} used={load.Pose?.Animation} detail={load.Detail}");
            }
            if (load.Pose is not { } pose) continue;
            frames.Add(pose.Key, pose.Frame);
            fallen[row.Key] = AreaScenePlan.Repose(command, row, visual, pose.Key, pose.Frame.Width, pose.Frame.Height, pose.HotX, pose.HotY);
        }
        _fallenBodies = fallen;
    }

    private string AttackStatus()
    {
        if (!AttackMapActive) return string.Empty;
        var state = _attack is null ? string.Empty : _attack.Approaching ? " (walking to attack)" : _attack.Player is not null ? " (attacking)" : " (attack repeats)";
        return _attackNotice is null && _attack is null ? (HeroInCombatMode ? " | attack: left-click an NPC, Alt+click an object" : string.Empty)
            : $" | attack{state}: {_attackNotice ?? "-"}";
    }
}
