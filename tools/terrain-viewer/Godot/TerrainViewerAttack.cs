using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
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
        public AreaAttackPlayer? Player { get; set; }
        public int Octant { get; set; }
        /// <summary>ARC-RENDER-039: the playing animation (20 attack, 21 low attack, 14 throw) and the action that draws it (the attack
        /// for a repaired low attack).</summary>
        public int Animation { get; set; } = Arcanum.Simulation.Equipment.WeaponArtRules.AttackAnimation;
        public ActorDiagnosticAction Action { get; set; } = ActorDiagnosticAction.Attack;
        public int Attacks { get; set; }
        // The walks to the target this click made (Steamwoven's cap of three: a walk that arrived and still left the target out of reach is not tried again and again).
        public int Approaches { get; set; }
        // The last hit ended the goal (refused, or the resolver refused the attack): no repeat follows.
        public bool Stop { get; set; }
        // ARC-COMBAT-008: the last hit broke the object: no repeat follows.
        public bool Broke { get; set; }
        // ARC-COMBAT-015: the walk stopped at a clear shot (Steamwoven, EX9), and the cell last tested on the walk (ARC-DEV-181: the attack goal's walk asks the viewer at each cell it commits to).
        public bool ClearShotStop { get; set; }
        public CellCoord? TestedCell { get; set; }
    }

    /// <summary>An attack animation is playing (the exit gate's attempt-attack test).</summary>
    private bool AttackPlaying => _attack?.Player is { Playing: true } || ThrowPlaying;

    private bool AttackMapActive => _combatHost is not null && _areaSession is not null && _readyMode == TerrainViewerMode.Area
        && _combatHost.Covers(CurrentAreaMap);

    private void BindAreaCombat(AreaInventoryHost host)
    {
        CancelAreaAttack(null); CancelSpellCast(null); ClearAreaProjectiles("the map changed"); _combatHost = new AreaCombatHost(host, CombatPrototype, () => _areaSession); _attackNotice = null; _spellCasterStamp = null;
    }

    // ARC-WORLD-106: a made item's prototype fields for the NPC combat host, from the barter data (null while it loads).
    private Arcanum.Data.Combat.CombatFields? CombatPrototype(int prototype)
    {
        AdoptBarterData();
        return _barterData?.Data?.Prototype(prototype);
    }

    // ARC-DEV-119 (MIG9 S5c): the window move of one session keeps the map's AI host (the combat host it gets is the next window's, bound by AreaAiHostNow's rebind); its
    // casting runs end here, while the cast host they were charged on is still the shown one. Any other unbinding (a map change, a load, a leave) retires it.
    private void UnbindAreaCombat()
    {
        // The publish unbinds twice, the second time after the scene's teardown cleared _areaSession; a session that is not the host's shows in the stamp (AreaAiHostNow builds again).
        if (_aiKeepAcrossWindowMove && _aiHost is { } kept && (_areaSession is null || ReferenceEquals(kept.Session, _areaSession))) kept.ReleaseCasts();
        else { RetireAiHost(); _aiHostStamp = null; }
        CancelAreaAttack(null); CancelSpellCast(null); ClearAreaProjectiles("the map changed"); _combatHost = null; _attackNotice = null; _spellCasterStamp = null;
    }

    private static long Milliseconds(SimulationTime time) => (long)(time.Value.Numerator * 1000 / time.Value.Denominator);
    // ARC-DEV-125: the clock of the rules the hero's systems and the animations run on (the frame's world milliseconds in play).
    private long WorldMilliseconds() => Milliseconds(WorldTime);

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
        AttackMapActive && HeroInCombatMode && AreaObjectAtScreen(screen) is { } key ? AreaTargetOf(key) : null;

    /// <summary>ARC-DEV-076 (MIG3): the NPC drawn as <paramref name="key"/> by identity: a row of a held map, else a companion carried
    /// here whose home map is not held (asked by its entity, which reads that map once).</summary>
    private AreaCombatTarget? AreaTargetOf(AreaObjectKey key) =>
        _combatHost!.Target(_areaSession!, key)
        ?? (_areaCompanions?.Companions.FirstOrDefault(c => c.Key == key) is { } visitor ? _combatHost.Target(_areaSession!, visitor.Entity) : null);

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
    private void FinishObjectHit(AttackGoal? goal, AreaObjectAttackTarget target, SessionObjectAttackResult result, bool ranged = false)
    {
        var owner = _areaSession!;
        // A blocker the line of fire struck instead (ARC-COMBAT-012) does not end the goal on the clicked target when it breaks.
        if (goal is not null && target.Row.Key == goal.Row.Key)
            goal.Broke = result.Object?.Broken == true || result.Item?.Break is Arcanum.Simulation.Items.ItemBreakStep.Broken or Arcanum.Simulation.Items.ItemBreakStep.Deleted;
        _attackNotice = AreaObjectTargets.Describe(result);
        FloatObjectAttack(result, target.Entity, target.Row);
        GD.Print($"ARC-COMBAT-008 object-attack-hit target={target.Entity} disposition={result.Disposition} failure={result.Failure}"
            + $" outcome={result.Resolution?.Outcome} damage={result.Object?.Damage}/{result.Item?.Condition?.Damage} break={result.Object?.Break}/{result.Item?.Break}"
            + $" host={result.Host} step={result.StepFailure} wear={string.Join(",", result.Wear?.Steps.Select(s => $"{s.Kind}:{s.Entity}:{s.Amount}:{s.Skip}") ?? [])}"
            + $" crit={AreaCombatHost.Criticals(result.Resolution, result.Criticals)}");
        if (result.Life is { } self && self.Events.HasFlag(LifeEvents.Stunned)) HeroStunned(owner);
        // ARC-DEV-260: the object's trap and its BUST script (the hero is the breaker; a ranged attack sets the trap off with no triggerer), then the hits their effects dealt.
        RunObjectBreakSteps(target.Row, result.Host, true, ranged);
        LogObjectHits();
        SyncAreaInventory();
        RefreshAreaStatus();
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
    private AreaOccupancy? AreaPcOccupancy() => WithAiMovers(CompanionPcOccupancy());

    // ARC-WORLD-092: the AI's moved NPCs are added by WithAiMovers.
    private AreaOccupancy? CompanionPcOccupancy()
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
    /// script content (the status line then cannot say that a trap goes off).</summary>
    private bool AreaTrapped(AreaObjectRecord row) => _readyArea is { } area && UseScriptHost(area.Snapshot)?.IsTrap(row) == true;

    /// <summary>
    /// ARC-DEV-187 (MIG13 S13a): the hero's stun is its goal 57 on the table (the session starts it with the life step, which interrupts the hero's other runs; the hero host's step ends it and clears the bit): nothing to do here
    /// when the run exists; without one (no hero host, or the start was refused) the bit ends at once as before.
    /// </summary>
    private void HeroStunned(GameSessionOwner owner)
    {
        if (HeroRunsHost()?.StunRun is not null) { GD.Print("ARC-DEV-187 hero stun is a run of the table"); return; }
        EndStun(owner, SessionLifeTarget.Hero, "hero");
    }

    /// <summary>ARC-DEV-187: the stun goal's end node for the hero: the stunned bit is cleared (idempotent).</summary>
    internal void HeroStunEnded()
    {
        if (_areaSession is not { } owner) return;
        var cleared = owner.ClearStun(owner.Version, SessionLifeTarget.Hero);
        if (cleared.Disposition == GameSessionDisposition.Changed) GD.Print("ARC-DEV-187 hero stun ended by its goal");
    }

    // ARC-COMBAT-009: the original keeps a critter stunned while its stun animation (goal 57) runs; the viewer plays none, so it ends
    // the stun at once, as the session's contract has it for an animation that could not start (the duration is NOT READY).
    private static void EndStun(GameSessionOwner owner, SessionLifeTarget target, string who)
    {
        var cleared = owner.ClearStun(owner.Version, target);
        GD.Print($"ARC-COMBAT-009 stun-ended who={who} disposition={cleared.Disposition} (no stun animation in the viewer)");
    }

    /// <summary>
    /// ARC-DEV-181 (MIG13 S12a): the hero's attack is goal 18 of the goal table (goal 19 once the target is in reach), started by the object click as the talk, the use and the pickup are: <see cref="HeroRequests"/> decides the goal
    /// (GS26), the host starts it (GS27). The goal's nodes test the reach, walk the hero within it (the walk goal 3 to the planner's approach cell) and swing; the hit, the animation, the action points and the repeat are the ports below.
    /// </summary>
    private void StartAttackGoal(AttackGoal goal)
    {
        if (_worldActor.Session is not AreaGridSession || _areaSession is null || HeroRunsHost() is null) { CancelAreaAttack("no actor"); return; }
        goal.ClearShotStop = false; goal.TestedCell = null;
        var request = HeroRequests.Apply(new ObjectClickRequest(AttackTargetOf(goal), Alt: goal.Object is not null));
        GD.Print($"ARC-DEV-181 attack-click outcome={request.Outcome} goal={request.Click} target={goal.Entity}");
        if (request.Outcome != HeroRequestOutcome.Done) CancelAreaAttack(request.Click == Arcanum.Simulation.Goals.HeroClickGoal.Attack ? "the click was refused" : $"the click is {request.Click}");
    }

    /// <summary>The target string the attack goal holds: the target's session entity (the viewer's one attack names it).</summary>
    private static string AttackTargetOf(AttackGoal goal) => "atk:" + goal.Entity;

    /// <summary>The attack the goal's string names (the one attack the hero has), or null.</summary>
    private AttackGoal? AttackGoalOf(string target) => _attack is { } goal && AttackTargetOf(goal) == target ? goal : null;

    /// <summary>The hero walks to its attack target: the attack run holds a walk goal.</summary>
    internal bool AttackApproaching => HeroRunsHost()?.AttackWalking == true;

    // Self-test knobs (ARC-DEV-181): the line-of-fire smoke makes the goal's reach test pass although the line is blocked (as when the line changed after the test), so the hit meets the blocker.
    private bool _attackAlwaysInReachForSelfTest;

    /// <summary>ARC-DEV-181 (smokes): the click starts the goal, whose first step comes on the next table tick; the smoke ticks until the hero walks or swings, or the attack is over.</summary>
    internal void AttackSettleForSelfTest(int limit = 60)
    {
        for (var i = 0; i < limit && _attack is { Player: null } && !AttackApproaching; i++)
        {
            _areaSmokeTime++; SmokeTickHeroRuns();
            WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
        }
    }

    // ---- the ports of the attack goals (IAreaHeroRunsViewer) ----

    internal AiHeroAttackTarget? HeroAttackTarget(string target)
    {
        if (AttackGoalOf(target) is not { } goal || _areaSession is not { } owner || !AttackMapActive) return null;
        var (x, y) = LiveWorldCell(goal.Row);
        var tile = new CellCoord(checked((int)x), checked((int)y));
        // An object stays a target while the session draws it and the last hit did not break it (ARC-COMBAT-008).
        var now = goal.Combat is null ? null : _combatHost!.Target(owner, goal.Row.Key);
        var dead = goal.Combat is null ? goal.Broke || _objectTargets?.Target(owner, goal.Row.Key) is null
            : now?.Dead != false || owner.QueryLife(goal.Combat.Target).View?.Status.IsDead != false;
        return new(tile, dead);
    }

    internal bool HeroAttackPossible(string target)
    {
        if (AttackGoalOf(target) is null || _areaSession is not { } owner) return false;
        var hero = owner.QueryHeroAttack();
        if (hero is null) return AttackStopped("needs a hero (start with --hero)");
        if (!hero.Supported) return AttackStopped($"a {hero.Skill} weapon cannot attack yet");
        // ARC-COMBAT-019: no attack starts while the hero's boomerang is away (0x434B00).
        if (owner.QueryBoomerangInFlight()) return AttackStopped("the boomerang is still in flight");
        return true;
    }

    private bool AttackStopped(string why)
    {
        _attackNotice = "attack stopped: " + why;
        GD.Print("ARC-COMBAT-005 attack-cancel " + why);
        return false;
    }

    internal bool HeroInAttackReach(string target)
    {
        if (AttackGoalOf(target) is not { } goal || _areaSession is not { } owner || owner.QueryHeroAttack() is not { } hero || HeroRunsViewerTile() is not { } here) return false;
        return _attackAlwaysInReachForSelfTest || InAttackReach(goal.Row, here, hero);
    }

    private CellCoord? HeroRunsViewerTile()
    {
        if (_worldActor.Session is not AreaGridSession grid) return null;
        var state = grid.State;
        var cell = state.Segment is { } segment ? state.Phase == MovementPhase.Returning ? segment.From : segment.To : state.CommittedCell;
        return WorldTile(grid, cell);
    }

    internal CellCoord? HeroAttackApproachTile(string target)
    {
        if (AttackGoalOf(target) is not { } goal || _worldActor.Session is not AreaGridSession session || _areaSession?.QueryHeroAttack() is not { } hero) return null;
        var cell = session.Chart.ToWorld(session.State.CommittedCell);
        var distance = Chebyshev(goal.Row, (cell.X, cell.Y));
        var (x, y) = LiveWorldCell(goal.Row);
        if (goal.Approaches >= AttackApproaches) { _attackBlockedNotice = distance <= HeroReach(hero) ? "no clear shot at the target" : $"the target stays {distance} cells away"; return null; }
        // A new walk tests the clear shot afresh (the memo of the last walk's cell is stale after a repeat or a target that moved).
        goal.ClearShotStop = false; goal.TestedCell = null;
        if (AttackApproachCell(session, x, y) is not { } approach) { _attackBlockedNotice = distance <= HeroReach(hero) ? "no clear shot at the target and no approach" : $"the target is {distance} cells away and no route reaches it"; return null; }
        goal.Approaches++;
        SetObjectApproachGait();
        _attackNotice = distance <= HeroReach(hero) ? "walking to attack: no clear shot" : $"walking to attack: {distance} cells away";
        GD.Print($"ARC-COMBAT-005 attack-approach distance={distance} goal={approach}");
        RefreshAreaStatus();
        return WorldTile(session, approach);
    }

    private const int AttackApproaches = 3;

    // Why the attack could not begin (no ART for the body); null: it was not paid (the turn-based notice says).
    private string? _attackBeginNotice;

    // Why the last approach found no way (the attack's end says it).
    private string? _attackBlockedNotice;

    /// <summary>
    /// ARC-COMBAT-015 (EX9): on the ranged path, Steamwoven stops the walk to the target at the first cell the hero commits to from which the reach test passes (the started step still ends at its cell centre); the Original setting
    /// (<see cref="CombatAnomalyProfile.RangedApproachSkipsClearShot"/>) walks on to the approach's tile, as the original's callback does while the attack is possible. Asked by the attack's walk at each cell.
    /// </summary>
    internal bool HeroAttackWalkInReach(string target)
    {
        if (AttackGoalOf(target) is not { } goal || _areaSession is not { } owner || owner.Rules.Combat.RangedApproachSkipsClearShot
            || owner.QueryHeroAttack() is not { } hero || !RangedPath(hero) || _worldActor.Session is not AreaGridSession session) return false;
        var cell = session.State.CommittedCell;
        if (goal.TestedCell == cell) return goal.ClearShotStop;
        goal.TestedCell = cell;
        var world = session.Chart.ToWorld(cell);
        if (!InAttackReach(goal.Row, new(checked((int)world.X), checked((int)world.Y)), hero)) return false;
        goal.ClearShotStop = true;
        _clearShotStops++;
        GD.Print($"ARC-COMBAT-015 clear-shot stop at={world.X},{world.Y} target={goal.Entity}");
        return true;
    }

    internal Arcanum.Simulation.Ai.AiAttackSwing? HeroBeginAttack(string target)
    {
        if (AttackGoalOf(target) is not { } goal || _areaSession?.QueryHeroAttack() is not { } hero) return null;
        return BeginAttackAnimation(goal, hero);
    }

    internal void HeroAttackHit(string target)
    {
        if (AttackGoalOf(target) is { Player: not null } goal && _worldActor.Session is AreaGridSession session) RunAttackHit(goal, session);
    }

    /// <summary>The repeat test (0x429760, RT7) after the swing: a hero in combat mode with the auto-attack option outside a round, whose target is still there, attacks again; any other attack ends.</summary>
    internal bool HeroAttackRepeats(string target)
    {
        if (AttackGoalOf(target) is not { } goal || _areaSession is not { } owner) return false;
        _worldActor.AttackPose = null;
        goal.Player = null;
        var known = HeroAttackTarget(target);
        var repeats = !goal.Stop && RealTimeCombatRules.Repeats(new(HeroInCombatMode, true, _gameSettings.AutoAttack,
            owner.QueryCombatRound() is not null, known is null, known is { Dead: true }, false, false, false, owner.QueryBoomerangInFlight()));
        GD.Print($"ARC-COMBAT-005 attack-end target={goal.Entity} attacks={goal.Attacks} repeat={repeats}");
        if (!repeats) _attack = null;
        PublishWorldSelection(); RefreshAreaStatus();
        return repeats;
    }

    /// <summary>An attack goal ends the run (the node says why): the attack is cleared and the status line says it.</summary>
    internal void HeroAttackEnds(string target, Arcanum.Simulation.Ai.AiExecutorReason reason)
    {
        if (AttackGoalOf(target) is not { } goal || _areaSession?.QueryHeroAttack() is not { } hero) return;
        var distance = _worldActor.Session is AreaGridSession session && session.Chart.ToWorld(session.State.CommittedCell) is var cell ? Chebyshev(goal.Row, (cell.X, cell.Y)) : 0;
        var why = reason switch
        {
            Arcanum.Simulation.Ai.AiExecutorReason.AttackBlocked => _attackBlockedNotice ?? (distance <= HeroReach(hero) ? "no clear shot at the target" : $"the target stays {distance} cells away"),
            Arcanum.Simulation.Ai.AiExecutorReason.Unpaid => _attackBeginNotice ?? _turnBasedNotice,
            _ => null,
        };
        _attackBlockedNotice = null;
        ClearAttack(why);
    }

    /// <summary>The attack the hero has is over: nothing of it is left on the hero (its pose, its status).</summary>
    private void ClearAttack(string? why)
    {
        if (_attack is null) return;
        _attack = null;
        _worldActor.AttackPose = null;
        if (why is not null) { _attackNotice = "attack stopped: " + why; GD.Print("ARC-COMBAT-005 attack-cancel " + why); }
        if (_worldActor.Session is not null && _readyActorPlan is not null) PublishWorldSelection();
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

    /// <summary>Starts one attack animation: the clip from the attack ART (since ARC-RENDER-039 the low attack or the throw, as the
    /// original picks it) and the hero's Speed and weapon, the attack sound roll (a draw the original makes at every attack start), the
    /// facing toward the target.</summary>
    private Arcanum.Simulation.Ai.AiAttackSwing? BeginAttackAnimation(AttackGoal goal, SessionHeroAttack hero)
    {
        // ARC-RENDER-039: the attack, low attack or throw (WA4); a body without that ART does not attack (the original's goal fails, WA5).
        var animation = HeroAttackArt.Animation(hero.Skill, goal.Row, LyingAnimation(goal));
        if (HeroAttackArt.Clip(_worldActor.Profile, animation) is not var (action, art, rateTable))
        { _attackBeginNotice = $"the hero's body has no ART for attack animation {animation}"; return null; }
        // ARC-COMBAT-020: in a turn-based round the attack's start charges its action points (ledger CT2); refused, there is no attack.
        // ARC-UI-063: a "default throws" weapon starts the throw goal instead, which costs the throw's points (TT4).
        var throws = ShotFlight(hero) is not null && HeroThrow(goal) is not null;
        if (!ChargeHeroAction(throws ? _areaSession!.QueryHeroThrowCost() : TurnBasedRules.AttackCost(hero.WeaponSpeed), throws ? "throw" : "attack"))
        { _attackBeginNotice = null; return null; } // the unpaid attack kills the run (the goal says why through HeroAttackEnds)
        // ARC-RENDER-037: the rate is the art id's weapon slot's (the hero's body shows its weapon in combat mode, WA1): the attack ART's
        // (the throw's file drops the weapon, the art id keeps it).
        var weapon = (AttackArtWeapon)Arcanum.Simulation.Equipment.WeaponArtRules.Weapon(_worldActor.Profile.Attack!.Selector);
        var rate = RealTimeCombatRules.AttackFramesPerSecond(rateTable, weapon, Math.Clamp(hero.Speed, 1, RealTimeCombatRules.MaxStat),
            art.RawFramesPerSecond);
        var clip = new AttackClip(art.FramesPerGroup, art.ActionFrame, RealTimeCombatRules.AttackFramePauseMs(rate, hero.WeaponSpeed), weapon);
        var sound = _areaSession!.Roll("combat.AttackSound", 1, RealTimeCombatRules.AttackSoundSides);
        var session = (AreaGridSession)_worldActor.Session!;
        var (tx, ty) = LiveWorldCell(goal.Row);
        var targetCell = session.Chart.ToChart(new(tx, ty));
        goal.Octant = AreaCombatHost.Facing(session.State.CommittedCell, targetCell) is { } facing ? DiagnosticGridActor.Octant(facing)
            : DiagnosticGridActor.RenderState(session.State).Facing;
        goal.Player = new AreaAttackPlayer(clip, WorldMilliseconds());
        goal.Animation = animation; goal.Action = action;
        _worldActor.AttackPose = (action, goal.Octant, 0);
        PlayAttackStartSounds(sound.Roll?.Value);
        GD.Print($"ARC-COMBAT-005 attack-start target={goal.Entity} weapon={weapon} animation={animation} drawn={action} art=0x{art.Selector:x8} frames={clip.Frames} action={clip.ActionFrame} pause={clip.PauseMs} rate={rate}"
            + $" sound={sound.Roll?.Value.ToString() ?? sound.Failure.ToString()} aim={goal.Aim?.ToString() ?? "none"}");
        PublishWorldSelection();
        // The goal's swing (the table waits for the action frame, the hit step, then the rest of the animation): the same schedule the player follows.
        var moments = RealTimeCombatRules.Schedule(clip);
        var hit = moments.FirstOrDefault(m => m.Kind == AttackUpdateKind.Hit);
        var end = moments[^1];
        var actionMs = (int)Math.Max(1, hit.Kind == AttackUpdateKind.Hit ? hit.AtMs : end.AtMs);
        return new Arcanum.Simulation.Ai.AiAttackSwing(actionMs, (int)Math.Max(1, end.AtMs - actionMs));
    }

    /// <summary>Per frame, before the actor advances: the attack's guards (a window, the hero's state, combat mode) and its animation's pose. The walk, the hit and the repeat are the goal's.</summary>
    private void ProcessAreaAttack()
    {
        if (_attack is not { } goal) return;
        if (!AttackMapActive || _areaSession is not { } owner || _worldActor.Session is not AreaGridSession) { CancelAreaAttack("the map changed"); return; }
        if (WorldModal || ConversationOpen) { CancelAreaAttack("a dialog or window opened"); return; }
        if (owner.QueryHeroLife() is not { Status.IsActive: true }) { CancelAreaAttack("the hero cannot act"); return; }
        if (goal.Player is null && !HeroInCombatMode) { CancelAreaAttack("left combat mode"); return; }
        // The run is gone (a round's start sweep, an interrupt, the goal's own end): nothing of the attack is left.
        if (HeroRunsHost() is { AttackRun: null } ended)
        {
            ClearAttack((goal.Player is null ? "the walk to attack stopped" : "the swing was interrupted") + (ended.LastAttackEnd is { } how ? $" ({how})" : ""));
            return;
        }
        if (goal.Player is not { } player) return;
        var now = WorldMilliseconds();
        var changed = false;
        foreach (var (_, update) in player.Advance(now, () => owner.QueryHeroLife() is { Status.IsActive: true }))
        {
            changed = true;
            if (update.Kind == AttackUpdateKind.Ended || !player.Playing) break;
        }
        if (_attack == goal && player.Playing && changed) { _worldActor.AttackPose = (goal.Action, goal.Octant, player.Playback.Frame); PublishWorldSelection(); }
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
        target.WorldX is not null && target.WorldY is not null && AreaLineOfFireNow() is { } map && _areaSession is { } owner
            && LiveWorldCell(target) is var (x, y)
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
        // ARC-WORLD-092: the NPCs the AI walked stand on their cells now.
        if (_aiHost is { } ai)
            foreach (var npc in ai.Scene.Npcs)
                if (ai.Movers.IsMoved(npc.Entity)) live.TryAdd(ai.Movers.Of(npc.Entity)!.Cell, npc.Row);
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
        if (!IsAreaCompanion(blocker.Key) && _combatHost!.Target(owner, blocker.Key) is { Dead: false, Build.Combat: { } combat } npc)
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
            FloatObjectAttack(result, $"wall:{blocker.Key.SourceIndex}", null);
            GD.Print($"ARC-COMBAT-015 wall-hit disposition={result.Disposition} failure={result.Failure} outcome={result.Resolution?.Outcome}"
                + $" prevented={result.Resolution?.Damage?.Prevented} ammo={result.Ammunition?.UnitsAfter.ToString() ?? "none"} step={result.StepFailure}");
            if (result.Life is { } self && self.Events.HasFlag(LifeEvents.Stunned)) HeroStunned(owner);
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
        // ARC-COMBAT-016: with a projectile art the shot flies and strikes on arrival (ledger PF1).
        var flight = ShotFlight(owner.QueryHeroAttack());
        // ARC-COMBAT-017: a weapon that "default throws" leaves the hand and flies (ledger RH2).
        var result = owner.AttackCritter(owner.Version, CritterAttack(struck, distance, line) with
        {
            Flight = flight is not null, Throw = flight is null ? null : HeroThrow(goal),
        });
        goal.Attacks++;
        // ARC-WORLD-092: the attack event of a resolved attack: the victim (and the witnesses of the noise) react.
        if (result.Disposition != GameSessionDisposition.Rejected && result.Resolution?.Refusal == AttackRefusal.None) _aiHost?.HeroAttacked(struck.Entity);
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
            TargetWorn = _combatHost!.WornItems(owner, struck.Entity),
            // ARC-COMBAT-009: a critical drops the NPC's items where it stands (ARC-WORLD-092: where the AI walked it).
            TargetCell = struck.Row.WorldX is not null && struck.Row.WorldY is not null && LiveWorldCell(struck.Row) is var (x, y)
                ? new(checked((int)x), checked((int)y)) : null,
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
        FloatCritterAttack(result, HeroFloater, struck.Entity);
        GD.Print($"ARC-COMBAT-005 attack-hit target={struck.Entity} disposition={result.Disposition} failure={result.Failure}"
            + $" outcome={result.Resolution?.Outcome} cause={result.Resolution?.MissCause} damage={result.Life?.HitPointDamage} events={result.Life?.Events}"
            + $" hp={life?.Status.HitPoints}/{life?.Status.MaxHitPoints} xp={result.HitAward?.Award}/{result.KillAward?.Award} step={result.StepFailure}"
            + $" wear={string.Join(",", result.Wear?.Steps.Select(s => $"{s.Kind}:{s.Entity}:{s.Amount}:{s.Skip}") ?? [])}"
            + $" crit={AreaCombatHost.Criticals(result.Resolution, result.Criticals)}");
        // ARC-DEV-137 (MIG13 S6): a stunned NPC's stun is its goal 57; only the hero's (hit back, S13) still ends at once.
        if (result.Life is { } took && took.Events.HasFlag(LifeEvents.Stunned) && result.Recipient != DamageRecipient.Target)
            HeroStunned(owner);
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
        if (_conversationHost is { } talk)
            foreach (var award in talk.State.TakeAwards()) LevelAreaFollowers(award);
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

    // An NPC's loaded sheet and effects by identity (ARC-DEV-076, MIG3: the resolver's record, wherever it stands), or null without a record.
    private (SessionNpcAdvancement Loaded, Arcanum.Simulation.Character.AdvancementModifiers Modifiers)? AreaFollowerSheet(string entity) =>
        _combatHost is { } host && _areaEffectRules is { } effects && host.Fields(entity) is { } fields
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
        _combatHost is { } host && _areaEffectRules is { } effects && host.Fields(entity) is { } fields
            ? AreaAdvancement.Npc(entity, fields, effects)?.Loaded : null;

    /// <summary>Stops the attack goal (a new click, the stick, a dialog, the map or the hero's state); <paramref name="why"/>
    /// goes to the status line.</summary>
    private void CancelAreaAttack(string? why)
    {
        // The hero's attack run on the table ends with it (its walk's route stops).
        try { _heroRunsHost?.EndAttack("ROUTE_STOPPED_BY_ATTACK_END"); }
        catch (ObjectDisposedException) { /* a replaced session */ }
        ClearAttack(why);
        EndHeroThrow(why ?? "cancelled");
    }

    /// <summary>ARC-RENDER-039: the animation the attack's target is drawn in when the session knocked it out (the fall, 7) or killed
    /// it (its dying animation); null for a standing NPC or an object (its placed art's).</summary>
    private int? LyingAnimation(AttackGoal goal)
    {
        if (goal.Combat is null || _areaSession?.QueryLife(goal.Combat.Target).View is not { } life) return null;
        return life.Status.IsDead ? _deathAnimations.GetValueOrDefault((CurrentAreaMap, goal.Entity), AreaFallenPoses.FallDown)
            : life.Status.IsUnconscious ? AreaFallenPoses.FallDown : null;
    }

    // The fallen bodies of the shown map (an NPC the session killed or knocked out, drawn in its pose in place of its T0 body; the
    // frame its animation shows, ARC-RENDER-040), the poses read so far, the dying animation each kill chose, and the frame repository
    // with the poses added.
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
            // ARC-WORLD-111: the move ARTs drawn into the old overlay go with it.
            ResetBodyMoves();
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
        // ARC-WORLD-092: the NPCs the AI walked, after the companions (a fallen body where it stands).
        if (AiMoverCommands() is { Count: > 0 } walked)
            companions = [.. companions ?? [], .. walked.Where(c => companions?.Any(o => o.Identity == c.Identity) != true)];
        if ((_fallenBodies.Count == 0 && _portalBodies.Count == 0 && _groundBodies.Count == 0 && _projectileBodies.Count == 0 && _eyeCandyBodies.Count == 0
            && _spellMissileBodies.Count == 0 && _sceneryBodies.Count == 0)
            || _readyArea is null || _areaOverlay?.Area != _readyArea) return companions;
        var moving = companions?.ToList() ?? [];
        var taken = moving.Select(c => c.Identity).ToHashSet();
        moving.AddRange(_fallenBodies.Values.Where(c => !taken.Contains(c.Identity)));
        // ARC-WORLD-051: doors and windows off their T0 frame.
        moving.AddRange(_portalBodies.Values);
        // ARC-RENDER-035: animated scenery off its T0 frame (a busted scenery's ground body wins).
        moving.AddRange(_sceneryBodies.Values.Where(c => !_groundBodies.ContainsKey(c.Identity!.Value)));
        // ARC-RENDER-033: busted scenery and traps, and the items lying away from a T0 body.
        moving.AddRange(_groundBodies.Values.Where(c => !taken.Contains(c.Identity)));
        // ARC-COMBAT-016: projectiles in flight.
        moving.AddRange(_projectileBodies.Values);
        // ARC-MAGIC-017: spells' eye candy on their objects.
        moving.AddRange(_eyeCandyBodies.Values);
        // ARC-MAGIC-018: spell missiles in flight.
        moving.AddRange(_spellMissileBodies.Values);
        return moving;
    }

    /// <summary>The NPCs of the shown map the session killed or knocked out are drawn fallen: the dying animation the kill chose (the
    /// fall after a reload), or the fall-down animation while unconscious, played from the moment the view sees it and then held on its
    /// last frame (ARC-RENDER-040, <see cref="ObserveLying"/>). Recomputed when the session or the map changes or a played frame does;
    /// a pose that cannot be read leaves the T0 body and says why once.</summary>
    private void SyncAreaCombatBodies()
    {
        if (_readyArea is not { } area || _areaSession is not { } session || _combatHost is null || _inventoryHost is null
            || _inventoryHost.Maps.FirstOrDefault(m => m.RuntimeMap == area.Snapshot.RuntimeMap) is not { } map)
        {
            if (_fallenBodies.Count > 0) _fallenBodies = [];
            return;
        }
        var frames = (AreaFrameOverlay)AreaFrames();
        var now = WorldMilliseconds();
        var framesStamp = LyingFramesStamp(now);
        var stamp = (area, session, session.Version.Revision, framesStamp);
        if (Equals(stamp, _fallenStamp)) return;
        _fallenStamp = stamp;
        _lyingFramesDrawn = framesStamp;
        var overlay = _areaOverlay!.Value;
        var fallen = new Dictionary<AreaObjectKey, AreaDrawCommand>();
        // ARC-RENDER-040: the bodies lying when the map and session are first seen hold their last frame; the ones that stood up go.
        var firstSight = !Equals((area, session), _lyingBaseline);
        if (firstSight) { _lyingBaseline = (area, session); _lyingShows.Clear(); }
        var lying = new HashSet<(int Map, string Entity)>();
        foreach (var holder in map.Seed.Holders)
        {
            if (holder.Kind != SessionHolderKind.Npc || holder.Dead || session.QueryLifeRecord(holder.Entity) is null) continue;
            if (_combatHost.Build(session, map.RuntimeMap, holder.Entity).Combat is not { } combat || session.QueryLife(combat.Target).View is not { } life) continue;
            int? animation = life.Status.IsDead ? _deathAnimations.GetValueOrDefault((map.RuntimeMap, holder.Entity), AreaFallenPoses.FallDown)
                : life.Status.IsUnconscious ? AreaFallenPoses.FallDown : null;
            // ARC-DEV-137 (MIG13 S6): what the critter-state goals show wins: the knock-down's fall (also of a conscious NPC), the get-up, the dying animation the kill chose.
            var art = AiArtOf(map.RuntimeMap, holder.Entity);
            if (art is { Animation: AreaFallenPoses.FallDown or AreaFallenPoses.GetUp or >= AreaFallenPoses.Decapitation and <= AreaFallenPoses.SeveredLeg } shown) animation = shown.Animation;
            var row = map.Row(holder.Entity);
            if (animation is not { } anim || !overlay.Bodies.TryGetValue(row.Key, out var command) || !overlay.Visuals.TryGetValue(command.Visual, out var visual)) continue;
            if (!_fallenPoses.TryGetValue((map.RuntimeMap, holder.Entity, anim), out var load))
            {
                load = AreaFallenPoses.Load(visual, area.ArtSources.GetValueOrDefault(visual.Entry.Source), anim);
                _fallenPoses[(map.RuntimeMap, holder.Entity, anim)] = load;
                GD.Print($"ARC-COMBAT-005 fallen-pose entity={holder.Entity} animation={anim} status={load.Status} used={load.Pose?.Animation} detail={load.Detail}");
            }
            if (load.Pose is null) continue;
            var show = ObserveLying((map.RuntimeMap, holder.Entity), anim, life.Status.IsDead, session.QueryLifeRecord(holder.Entity)?.DeathTimeMs, load, now,
                firstSight);
            lying.Add((map.RuntimeMap, holder.Entity));
            // The frame the goal's step shows; a body no goal sets the art of keeps the show's own clock (a body first seen, a pose the goal's animation has left).
            var pose = show.Load.Frames[art is { } at && at.Animation == anim ? Math.Clamp(at.Frame, 0, show.Frames - 1) : show.FrameAt(now)];
            frames.Add(pose.Key, pose.Frame);
            fallen[row.Key] = AreaScenePlan.Repose(command, row, visual, pose.Key, pose.Frame.Width, pose.Frame.Height, pose.HotX, pose.HotY);
        }
        foreach (var key in _lyingShows.Keys.Where(k => k.Map == map.RuntimeMap && !lying.Contains(k)).ToArray()) _lyingShows.Remove(key);
        // The shows this sync started are in the stamp, so the next frame does not draw the same frames again.
        _lyingFramesDrawn = LyingFramesStamp(now);
        _fallenStamp = (area, session, session.Version.Revision, _lyingFramesDrawn);
        _fallenBodies = fallen;
    }

    /// <summary>ARC-DEV-137: the art the AI host's critter-state goals set for the NPC on the shown map (null without a host on it, or none set).</summary>
    private (int Animation, int Frame)? AiArtOf(int runtimeMap, string entity) =>
        _aiHost is { } host && host.Scene.RuntimeMap == runtimeMap ? host.ArtOf(entity) : null;

    /// <summary>ARC-DEV-137: the ART clip of an NPC's fall, get-up or dying animation (frames and rate) for the critter-state goals' timing; null for any other animation (the stun's 23 is not
    /// read yet) or when the pose does not read.</summary>
    private Arcanum.Simulation.Ai.AiAnimationClip? NpcAnimationClip(string entity, int animation)
    {
        if (animation is not (AreaFallenPoses.FallDown or AreaFallenPoses.GetUp or (>= AreaFallenPoses.Decapitation and <= AreaFallenPoses.SeveredLeg))) return null;
        if (_readyArea is not { } area || _areaOverlay is not { } overlay
            || _inventoryHost?.Maps.FirstOrDefault(m => m.RuntimeMap == area.Snapshot.RuntimeMap) is not { } map || !map.TryGetRow(entity, out var row)
            || !overlay.Bodies.TryGetValue(row.Key, out var command) || !overlay.Visuals.TryGetValue(command.Visual, out var visual)) return null;
        var key = (map.RuntimeMap, entity, animation);
        if (!_fallenPoses.TryGetValue(key, out var load))
        {
            load = AreaFallenPoses.Load(visual, area.ArtSources.GetValueOrDefault(visual.Entry.Source), animation);
            _fallenPoses[key] = load;
            GD.Print($"ARC-COMBAT-005 fallen-pose entity={entity} animation={animation} status={load.Status} used={load.Pose?.Animation} detail={load.Detail}");
        }
        return load.Pose is null ? null : new(load.Frames.Count, load.FramesPerSecond);
    }

    private string AttackStatus()
    {
        if (!AttackMapActive) return string.Empty;
        var state = _attack is null ? string.Empty : AttackApproaching ? " (walking to attack)" : _attack.Player is not null ? " (attacking)" : " (attack repeats)";
        return _attackNotice is null && _attack is null ? (HeroInCombatMode ? " | attack: left-click an NPC, Alt+click an object" : string.Empty)
            : $" | attack{state}: {_attackNotice ?? "-"}";
    }
}
