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

    /// <summary>One attack goal: its target, the aim, and either the walk to reach it or the attack animation playing (or the
    /// 5 ms pause before the auto-attack repeat).</summary>
    private sealed class AttackGoal(AreaCombatTarget target, SessionCritterCombat combat, HitLocation? aim)
    {
        public AreaCombatTarget Target { get; } = target;
        public SessionCritterCombat Combat { get; } = combat;
        public HitLocation? Aim { get; } = aim;
        public bool Approaching { get; set; }
        public AreaAttackPlayer? Player { get; set; }
        public long? RepeatAtMs { get; set; }
        public int Octant { get; set; }
        public int Attacks { get; set; }
        public int Approaches { get; set; }
        // The last hit ended the goal (refused, or the resolver refused the attack): no repeat follows.
        public bool Stop { get; set; }
    }

    /// <summary>An attack animation is playing (the exit gate's attempt-attack test).</summary>
    private bool AttackPlaying => _attack?.Player is { Playing: true };

    private bool AttackMapActive => _combatHost is not null && _areaSession is not null && _readyMode == TerrainViewerMode.CrashSiteArea
        && _combatHost.Covers(CurrentAreaMap);

    private void BindAreaCombat(AreaInventoryHost host) { CancelAreaAttack(null); _combatHost = new AreaCombatHost(host); _attackNotice = null; }

    private void UnbindAreaCombat() { CancelAreaAttack(null); _combatHost = null; _attackNotice = null; }

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
        if (CombatTargetAt(screen) is not { } target) return false;
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
        if (_attack is { } running && running.Target.Entity == target.Entity && !running.Stop) return true;
        CancelAreaAttack(null);
        _attack = new(target, combat, HeldAim());
        StartAttackGoal(_attack);
        return true;
    }

    /// <summary>The attack goal's first step: the target within the weapon's reach starts the animation; farther away the
    /// click route walks within reach first (the goal's move-near step).</summary>
    private void StartAttackGoal(AttackGoal goal)
    {
        if (_worldActor.Session is not AreaGridSession session || _areaSession is not { } owner) { CancelAreaAttack("no actor"); return; }
        var hero = owner.QueryHeroAttack();
        if (hero is null) { CancelAreaAttack("needs a hero (start with --hero)"); return; }
        if (!hero.Supported) { CancelAreaAttack($"a {hero.Skill} weapon cannot attack yet (melee only)"); return; }
        var cell = session.Chart.ToWorld(session.State.CommittedCell);
        var row = goal.Target.Row;
        var distance = Chebyshev(row, (cell.X, cell.Y));
        if (distance <= hero.Range)
        {
            StopWorldRoute("ROUTE_REPLACED_BY_ATTACK");
            goal.Approaching = false;
            if (session.State.Phase == MovementPhase.Settled) BeginAttackAnimation(goal, hero);
            return;
        }
        // A walk that arrived and still left the target out of reach is not tried again and again.
        if (goal.Approaches >= 3) { CancelAreaAttack($"the target stays {distance} cells away"); return; }
        if (WorldRoute(session) is { } route && ApproachCell(session, row.WorldX!.Value, row.WorldY!.Value, hero.Range) is { } approach)
        {
            goal.Approaches++;
            StopWorldRoute("ROUTE_REPLACED_BY_ATTACK");
            route.SetTarget(approach, WorldTime);
            goal.Approaching = true;
            SetObjectApproachGait();
            _worldRouteDiagnostic = DescribeWorldRoute(route); _worldRouteClick = $"{approach.X},{approach.Y} walk to attack";
            _attackNotice = $"walking to attack: {distance} cells away";
            GD.Print($"ARC-COMBAT-005 attack-approach distance={distance} goal={approach}");
            RefreshAreaStatus();
            return;
        }
        CancelAreaAttack($"the target is {distance} cells away and no standable cell within reach was found");
    }

    /// <summary>Starts one attack animation: the clip from the attack ART and the hero's Speed and weapon, the attack sound roll
    /// (a draw the original makes at every attack start), the facing toward the target.</summary>
    private void BeginAttackAnimation(AttackGoal goal, SessionHeroAttack hero)
    {
        var art = _worldActor.Profile.Attack!;
        var rate = RealTimeCombatRules.AttackFramesPerSecond(AttackAnimation.Attack, AttackArtWeapon.Unarmed, Math.Clamp(hero.Speed, 1, RealTimeCombatRules.MaxStat),
            art.RawFramesPerSecond);
        var clip = new AttackClip(art.FramesPerGroup, art.ActionFrame, RealTimeCombatRules.AttackFramePauseMs(rate, hero.WeaponSpeed), AttackArtWeapon.Unarmed);
        var sound = _areaSession!.Roll("combat.AttackSound", 1, RealTimeCombatRules.AttackSoundSides);
        var session = (AreaGridSession)_worldActor.Session!;
        var targetCell = session.Chart.ToChart(new(goal.Target.Row.WorldX!.Value, goal.Target.Row.WorldY!.Value));
        goal.Octant = AreaCombatHost.Facing(session.State.CommittedCell, targetCell) is { } facing ? DiagnosticGridActor.Octant(facing)
            : DiagnosticGridActor.RenderState(session.State).Facing;
        goal.Player = new AreaAttackPlayer(clip, WorldMilliseconds());
        goal.RepeatAtMs = null;
        _worldActor.AttackPose = (goal.Octant, 0);
        GD.Print($"ARC-COMBAT-005 attack-start target={goal.Target.Entity} frames={clip.Frames} action={clip.ActionFrame} pause={clip.PauseMs} rate={rate}"
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
            if (session.State.Phase != MovementPhase.Settled || _worldRoute is not { } route || route.Status == RouteFollowStatus.Following) return;
            if (route.Status != RouteFollowStatus.Arrived) { CancelAreaAttack("walk to attack stopped: " + (route.StopReason ?? route.Status.ToString())); return; }
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
        var target = _combatHost!.Target(owner, CurrentAreaMap, goal.Target.Row.Key);
        var dead = target?.Dead != false || owner.QueryLife(goal.Combat.Target).View?.Status.IsDead != false;
        var repeats = !goal.Stop && RealTimeCombatRules.Repeats(new(HeroInCombatMode, true, RealTimeCombatRules.AutoAttackDefault,
            owner.QueryCombatRound() is not null, target is null, dead, false, false, false, false));
        if (repeats) goal.RepeatAtMs = now + RealTimeCombatRules.RepeatDelayMs;
        else _attack = null;
        GD.Print($"ARC-COMBAT-005 attack-end target={goal.Target.Entity} attacks={goal.Attacks} repeat={repeats}");
        PublishWorldSelection(); RefreshAreaStatus();
    }

    /// <summary>The update that shows the action frame: the target still within reach is hit through the session, else the
    /// animation ends without a hit.</summary>
    private void RunAttackHit(AttackGoal goal, AreaGridSession session)
    {
        var owner = _areaSession!;
        var cell = session.Chart.ToWorld(session.State.CommittedCell);
        var distance = Chebyshev(goal.Target.Row, (cell.X, cell.Y));
        if (distance > (owner.QueryHeroAttack()?.Range ?? 1))
        {
            goal.Player!.Abort();
            _attackNotice = "the target is out of reach";
            return;
        }
        var result = owner.AttackCritter(owner.Version, new(goal.Combat) { Aim = goal.Aim, Distance = (int)distance });
        goal.Attacks++;
        // A refused attack, or one the resolver refused, ends the goal: repeating it would refuse again.
        goal.Stop = result.Disposition == GameSessionDisposition.Rejected || result.Resolution?.Refusal is { } refusal && refusal != AttackRefusal.None;
        var life = owner.QueryLife(goal.Combat.Target).View;
        _attackNotice = AreaCombatHost.Describe(result, life?.Status.HitPoints, life?.Status.MaxHitPoints);
        GD.Print($"ARC-COMBAT-005 attack-hit target={goal.Target.Entity} disposition={result.Disposition} failure={result.Failure}"
            + $" outcome={result.Resolution?.Outcome} cause={result.Resolution?.MissCause} damage={result.Life?.HitPointDamage} events={result.Life?.Events}"
            + $" hp={life?.Status.HitPoints}/{life?.Status.MaxHitPoints} xp={result.HitAward?.Award}/{result.KillAward?.Award} step={result.StepFailure}");
        LevelAreaFollowers(result.HitAward);
        LevelAreaFollowers(result.KillAward);
        if (result.Recipient == DamageRecipient.Target && result.Life is { } dealt && dealt.Events.HasFlag(LifeEvents.Died))
            _deathAnimations[(CurrentAreaMap, goal.Target.Entity)] = AreaFallenPoses.Animation(dealt.Death);
        SyncAreaCombatBodies();
        SyncAreaInventory();
        RefreshAreaStatus();
    }

    /// <summary>
    /// ARC-WORLD-036: passes an award's follower step to the hero's followers and the NPCs waiting for it on this map, with their
    /// stored fields and effects (<see cref="AreaAdvancement.LevelFollowers"/>).
    /// </summary>
    private void SyncAreaAwards()
    {
        if (_conversationHost?.State.TakeAwards() is not { Count: > 0 } awards) return;
        foreach (var award in awards) LevelAreaFollowers(award);
    }

    private void LevelAreaFollowers(SessionExperienceAward? award)
    {
        if (award?.Followers is null || _areaSession is null || _combatHost is null || _areaEffectRules is not { } effects) return;
        var map = CurrentAreaMap;
        // Fields come from the NPC's home map; a waiting NPC counts only while it stands on this map.
        var session = _areaSession;
        var raised = AreaAdvancement.LevelFollowers(session, award, entity =>
            _combatHost.HomeMap(entity) is { } home && AreaAdvancement.Nearby(session, entity, home, map) &&
            _combatHost.Fields(home, entity) is { } fields ? AreaAdvancement.Npc(entity, fields, effects) : null);
        foreach (var follower in raised)
            GD.Print($"ARC-WORLD-036 follower-level entity={follower.Record!.Entity} level={follower.Level!.Value.Level}"
                + $" points={follower.Level.Value.PointsGained} purchases={follower.Scheme?.Purchases.Length ?? 0} completed={follower.Scheme?.Completed}");
    }

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
    /// own command wins).</summary>
    private IReadOnlyList<AreaDrawCommand>? AreaMovingCommands()
    {
        var companions = AreaCompanionCommands();
        if (_fallenBodies.Count == 0 || _readyArea is null || _areaOverlay?.Area != _readyArea) return companions;
        var moving = companions?.ToList() ?? [];
        var taken = moving.Select(c => c.Identity).ToHashSet();
        moving.AddRange(_fallenBodies.Values.Where(c => !taken.Contains(c.Identity)));
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
        return _attackNotice is null && _attack is null ? (HeroInCombatMode ? " | attack: left-click an NPC" : string.Empty)
            : $" | attack{state}: {_attackNotice ?? "-"}";
    }
}
