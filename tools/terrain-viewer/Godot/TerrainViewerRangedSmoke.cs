using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;
using ScrPoint = Arcanum.Scripting.Scr.ScrPoint;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-COMBAT-015: the session's last attack result on an NPC and the walks stopped at a clear shot, for the ranged smoke.
    private SessionAttackResult? _lastCritterAttackForSelfTest;
    private int _clearShotStops;

    private const int LentBowRange = 10;
    // ARC-COMBAT-016: the projectile art the smoke lends its bow: scenery number 0, the art of every installed bow (combat census).
    private const int LentMissileArt = 0x40000000;

    // ARC-COMBAT-1766 (0x4B2870, ledger PF9, PS5): checks the weapon's end points the viewer asked for the hero's flight that just ended (<see cref="_shotEndScriptsForSelfTest"/>, cleared before the shot): a crossing end without a
    // strike (the range end or the destination) asks them once, with the hero as the shooter; the first of an ordinary shot is the hit point (7) at the destination or the miss point (8) at the range end, and the hero's thrown item
    // leaves those two to the thrown-item host (the guard against a double run). A strike, or the goal's own end (a flight that never took a step: crossed false), asks none here.
    private void RequireShotEndScripts(ProjectileStrikeKind end, bool crossed, bool thrown, string? wieldedWeapon, string label)
    {
        var expected = crossed && end is ProjectileStrikeKind.RangeEnd or ProjectileStrikeKind.DestinationEnd ? 1 : 0;
        var asked = _shotEndScriptsForSelfTest.ToArray();
        var ok = asked.Length == expected && asked.All(e => e.Attacker == SessionEventKinds.PlayerEntity && e.Thrown == thrown && (wieldedWeapon is null || e.Weapon == wieldedWeapon)
            && e.Destination == (end == ProjectileStrikeKind.DestinationEnd)
            && (thrown ? e.Points.All(point => point is not (ScrPoint.Hit or ScrPoint.Miss)) : e.Points.Count > 0 && e.Points[0] == (e.Destination ? ScrPoint.Hit : ScrPoint.Miss)));
        if (ok) return;
        GD.Print($"ARC-COMBAT-1766 ranged smoke FAIL: {label} end {end} asked {asked.Length} of {expected} end-script sets ({string.Join(",", asked.Select(e => $"{e.Attacker}:{e.Weapon}:thrown={e.Thrown}:{(e.Destination ? "dest" : "range")}:{string.Join("/", e.Points)}"))})");
        throw new InvalidOperationException("AREA_RANGED_SMOKE_FAILED");
    }

    // Native observation (ARC-COMBAT-015) with --hero, in map mode after the line-of-fire stage (or alone with
    // ARC_AREA_MAP_SMOKE_ONLY=ranged): the hero's ranged attack. No kit holds a bow, so the stage lends the wielded kit weapon a bow's
    // values (range 10, no ammunition: the shot's ammunition is the session's tests') and restores the weapon afterwards. Shot: the
    // hero stands 3..6 cells from a living NPC with combat values on a cell with a clear shot (the attack goal's reach test, 0x429960),
    // the attack starts without a walk, and the session's check carries the range penalty (5 per cell beyond half the Perception)
    // and the line's cost as cover. Approach: from 12..16 cells away the goal walks; Steamwoven stops it at the first cell with a
    // clear shot within the range (EX9), so the hit runs from 2..10 cells away (the Original setting would walk to the target's
    // side). The NPC is healed back afterwards. NONE on a map without such cells.
    private async Task RunAreaRangedSmokeAsync()
    {
        // ARC-WORLD-1149: this stage judges the shots' flight, strikes and wear, and wants a hit within 12 shots; at night a shot would meet the darkness penalty
        // (FR-LIGHT-011) and miss, so its checks read full light.
        _cellLightForSelfTest = Arcanum.Simulation.Combat.AttackRules.MaxLightLevel;
        try { await RunAreaRangedSmokeCoreAsync(); }
        finally { _cellLightForSelfTest = null; }
    }

    private async Task RunAreaRangedSmokeCoreAsync()
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _combatHost is null || _readyArea is not { } area
            || _worldActor.Session is not AreaGridSession grid || AreaLineOfFireNow() is not { } lineMap)
        { GD.Print($"ARC-COMBAT-015 native-smoke ranged NONE map={_readyMapName} (no hero or no attack map)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-COMBAT-015 ranged smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_RANGED_SMOKE_FAILED");
        }
        if (session.QueryEquipment().Worn.FirstOrDefault(w => w.Slot == EquipmentSlot.Weapon) is not { Item.Weapon: not null } wielded
            || session.QueryHeroKit().Where(k => k.Entity == wielded.Entity).Select(k => (SessionKitItem?)k).FirstOrDefault() is not { } kit)
        { GD.Print($"ARC-COMBAT-015 native-smoke ranged NONE map={_readyMapName} (the hero wields no kit weapon to lend a bow's values)"); return; }
        var map = new AreaMapRuntime(area.Snapshot, grid.Snapshot, grid.Chart);
        var runtime = CurrentAreaMap;
        // ARC-RENDER-039: the animation and drawn action of the last attack ShootOnce started.
        (int Animation, ActorDiagnosticAction Action) lastStart = default;
        var npcs = area.Snapshot.Objects.Where(row => row.Type == 16 && row.Scope == AreaScope.Core && row.WorldX is not null && row.WorldY is not null
                && !IsAreaCompanion(row.Key))
            .Select(row => _combatHost.Target(session, runtime, row.Key))
            .Where(t => t is { Dead: false, Follower: false, Build.Combat: not null } && session.QueryLife(t.Build.Combat!.Target).View?.Status.IsDead != true)
            .Select(t => t!).OrderBy(t => t.Entity, StringComparer.Ordinal).ToArray();
        static CellCoord World(AreaObjectRecord row) => new(checked((int)row.WorldX!.Value), checked((int)row.WorldY!.Value));
        var profile = session.Rules.Combat;
        // A cell the hero can stand on (ApproachCell's step test) at a distance in the band whose line can be built; within the bow's
        // range also with a clear shot.
        CellCoord? Shooting(AreaCombatTarget target, int near, int far) => Shootings(target, near, far).Cast<CellCoord?>().FirstOrDefault();
        IEnumerable<CellCoord> Shootings(AreaCombatTarget target, int near, int far)
        {
            var at = World(target.Row);
            for (var d = near; d <= far; d++)
            for (var dy = -d; dy <= d; dy++)
            for (var dx = -d; dx <= d; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != d) continue;
                var hero = new CellCoord(at.X + dx, at.Y + dy);
                if (!lineMap.Contains(hero)) continue;
                var trace = LineOfFireRules.Trace(hero, 0, 0, at, lineMap, profile);
                if (trace.LineFailed || d <= LentBowRange && !LineOfFireRules.InReach(d, LentBowRange, trace, trace.Blocker is { } b && b.Key == target.Row.Key ? b : target.Row))
                    continue;
                var chart = map.Chart.ToChart(new(hero.X, hero.Y));
                if (!map.Chart.Contains(chart) || ApproachCell(grid, at.X, at.Y, far, cell => cell == chart) != chart) continue;
                yield return hero;
            }
        }
        (AreaCombatTarget Target, CellCoord Hero)? shot = null;
        foreach (var target in npcs) if (Shooting(target, 3, 6) is { } cell) { shot = (target, cell); break; }
        if (shot is not { } pick)
        { GD.Print($"ARC-COMBAT-015 native-smoke ranged NONE map={_readyMapName} npcs={npcs.Length} (no clear shot 3..6 cells from an NPC)"); return; }

        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        var lent = false;
        bool Wear(EquipmentItem item)
        {
            if (session.Unequip(session.Version, EquipmentSlot.Weapon, _ => SessionInsertVerdict.Accepted, null, ItemTriggersNow()).Disposition == GameSessionDisposition.Rejected) return false;
            return session.Equip(session.Version, wielded.Entity, EquipmentSlot.Weapon, new(kit.Kind, [], false, item), (_, _) => true,
                _ => SessionInsertVerdict.Accepted, null, ItemTriggersNow()).Disposition == GameSessionDisposition.Changed;
        }
        try
        {
            var bow = wielded.Item with
            {
                Weapon = wielded.Item.Weapon! with
                {
                    Skill = CombatSkill.Bow, Range = LentBowRange, MagicRange = 0, AmmoType = null, AmmoConsumption = 0,
                    CriticalMissChart = CriticalMissChart.Bows,
                },
            };
            Require(lent = Wear(bow), "the kit weapon takes the lent bow's values");
            var hero = session.QueryHeroAttack()!;
            Require(hero is { Skill: CombatSkill.Bow, Range: LentBowRange, Supported: true, RangedSkill: true }, $"the hero shoots a bow ({hero.Skill} {hero.Range})");
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            if (!HeroInCombatMode) ToggleCombatMode();
            Require(HeroInCombatMode, "the hero is in combat mode");

            // Shot: in reach with a clear shot, no walk. A placement restarts the smoke clock in seconds; the frames count twentieths.
            AreaSmokePlace(map, map.Chart.ToChart(new(pick.Hero.X, pick.Hero.Y)));
            _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            PublishWorldSelection();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var at = World(pick.Target.Row);
            var distance = Math.Max(Math.Abs(at.X - pick.Hero.X), Math.Abs(at.Y - pick.Hero.Y));
            var trace = LineOfFireRules.Trace(pick.Hero, 0, 0, at, AreaLineOfFireNow()!, profile);
            var perception = session.QueryHeroStats(new(0))!.Value.Snapshot!.GetEffective(CharacterStat.Perception);
            var result = await ShootOnce(pick.Target, expectWalk: false);
            Require(result?.Resolution is { Refusal: AttackRefusal.None }, $"the shot was not refused ({result?.Failure} {result?.Resolution?.Refusal})");
            Require(result?.Resolution?.Difficulty is { } difficulty && difficulty.Range == 5 * Math.Max(0, distance - perception / 2) && difficulty.Cover == trace.Cost,
                $"the check carries the range penalty and the cover (distance {distance}, PE {perception}, cost {trace.Cost},"
                + $" range {result?.Resolution?.Difficulty?.Range}, cover {result?.Resolution?.Difficulty?.Cover})");
            var first = new { Distance = distance, Perception = perception, trace.Cost, Range = result!.Resolution!.Difficulty!.Range, result.Resolution.Outcome };

            // Approach: from farther away the walk stops at the first clear shot within the range (Steamwoven). A critical miss may
            // have dropped or broken the lent bow; a far cell the approach cannot route from is skipped for the next (at most five).
            object? second = null;
            var stillBow = session.QueryHeroAttack() is { Skill: CombatSkill.Bow };
            var tried = 0;
            foreach (var far in profile.RangedApproachSkipsClearShot || !stillBow ? [] : Shootings(pick.Target, 12, 16).Take(5).ToArray())
            {
                tried++;
                Heal();
                _clearShotStops = 0;
                AreaSmokePlace(map, map.Chart.ToChart(new(far.X, far.Y)));
                _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
                PublishWorldSelection();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                CancelAreaAttack(null);
                // ARC-DEV-270 (GS26, GS27): a Shift click is the attempt attack 19 with run flag 0x100: from this far cell the hero walks nowhere and the attack ends (the swing from where it stands is the hits above).
                if (tried == 1)
                {
                    var before = ((AreaGridSession)_worldActor.Session!).State.CommittedCell;
                    _attack = new(pick.Target, pick.Target.Build.Combat!, null) { NoWalk = true };
                    StartAttackGoal(_attack);
                    AttackSettleForSelfTest();
                    Require(_attack is null && !AttackApproaching && ((AreaGridSession)_worldActor.Session!).State.CommittedCell == before, "a Shift click from a far cell starts no walk and the attack ends there");
                    GD.Print($"ARC-DEV-270 shift-attack from far PASS notice={_attackNotice}");
                    CancelAreaAttack(null);
                }
                _attack = new(pick.Target, pick.Target.Build.Combat!, null);
                StartAttackGoal(_attack);
                AttackSettleForSelfTest();
                if (_attack is null || !AttackApproaching) { GD.Print($"ARC-COMBAT-015 ranged smoke approach skipped from={far.X},{far.Y}: {_attackNotice}"); continue; }
                var walked = await ShootOnce(pick.Target, expectWalk: true, started: true);
                var from = ((AreaGridSession)_worldActor.Session!).Chart.ToWorld(((AreaGridSession)_worldActor.Session!).State.CommittedCell);
                var shotFrom = Math.Max(Math.Abs(at.X - from.X), Math.Abs(at.Y - from.Y));
                Require(walked is { Failure: GameSessionFailure.None, Resolution.Refusal: AttackRefusal.None } && _clearShotStops > 0 && shotFrom is >= 2 and <= LentBowRange,
                    $"the walk stopped at a clear shot ({_clearShotStops}) and the hit ran from {shotFrom} cells ({walked?.Failure}, notice {_attackNotice})");
                second = new { From = new[] { far.X, far.Y }, ShotFrom = shotFrom, walked!.Resolution?.Difficulty?.Range };
                break;
            }
            Require(second is not null || tried == 0, $"an approach ran from one of {tried} far cells");
            var flights = session.QueryHeroAttack() is { Skill: CombatSkill.Bow } ? await Projectiles() : "NONE (a critical miss took the bow)";
            // ARC-DEV-1803: the trap leg's shooting cells are traced on the lines of the session this leg started with (their closures read it),
            // so they are read out here, before the saved-flight load disposes that session.
            var trapCells = session.QueryHeroAttack() is { Skill: CombatSkill.Bow } ? Shootings(pick.Target, 3, 6).Take(40).ToArray() : [];
            // ARC-DEV-288: while the lent bow is still wielded; the load replaces the session, so the sub-steps after it run on the loaded one (it holds the lent bow too).
            var savedFlight = session.QueryHeroAttack() is { Skill: CombatSkill.Bow } ? await SavedFlight() : "NONE (a critical miss took the bow)";
            // ARC-TECHNOLOGY-1635: a trap's shot of the default projectile art (the lent bow's), on the session the saved-flight leg left.
            var trapShots = session.QueryHeroAttack() is { Skill: CombatSkill.Bow } ? await TrapShotSmokeLegAsync(map, pick.Hero, trapCells) : "NONE (a critical miss took the bow)";
            session = _areaSession!;
            var pairs = session.QueryHeroAttack() is { Skill: CombatSkill.Bow } ? await ExpertShots() : "NONE (a critical miss took the bow)";
            var throws = session.QueryHeroAttack() is { Skill: CombatSkill.Bow } ? await Throws() : "NONE (a critical miss took the bow)";
            var boomerangs = session.QueryEquipment().Worn.Any(w => w.Slot == EquipmentSlot.Weapon && w.Entity == wielded.Entity) ? await Boomerangs()
                : "NONE (a critical miss took the lent weapon)";
            var cursorThrows = session.QueryEquipment().Worn.Any(w => w.Slot == EquipmentSlot.Weapon && w.Entity == wielded.Entity) ? await CursorThrows()
                : "NONE (a critical miss took the lent weapon)";
            ToggleCombatMode();
            GD.Print("ARC-COMBAT-015 native-smoke ranged PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Map = _readyMapName, Target = pick.Target.Entity, Hero = new[] { pick.Hero.X, pick.Hero.Y }, Shot = first,
                Approach = second ?? (object)(stillBow ? "NONE (no cell 12..16 away, or the Original setting)" : "NONE (the first shot's critical miss took the bow)"), Npcs = npcs.Length,
                Projectiles = flights, TrapShots = trapShots, ExpertShots = pairs, Throws = throws, Boomerangs = boomerangs, CursorThrows = cursorThrows, SavedFlight = savedFlight,
                Parity = "NOT_VERIFIED (rules read in the original executable; no original-runtime run)" }));
        }
        finally
        {
            CancelAreaAttack(null);
            PutBackGameCursorItem("the smoke ended");
            _lentMissileArtForSelfTest = null;
            _lentThrowWeightForSelfTest = null;
            ClearAreaProjectiles("the smoke ended");
            // A thrown kit weapon still on the ground goes back to the hand before the kit weapon is restored.
            if (lent && !session.QueryEquipment().Worn.Any(w => w.Slot == EquipmentSlot.Weapon)
                && session.PickUpItem(session.Version, wielded.Entity, _ => SessionInsertVerdict.Accepted).Disposition == GameSessionDisposition.Changed)
                session.Equip(session.Version, wielded.Entity, EquipmentSlot.Weapon, new(kit.Kind, [], false, wielded.Item), (_, _) => true, _ => SessionInsertVerdict.Accepted, null, ItemTriggersNow());
            if (lent && !Wear(wielded.Item)) GD.Print("ARC-COMBAT-015 ranged smoke could not restore the kit weapon");
            Heal();
            _areaSmokeTime = (_areaSmokeTime + 19) / 20;
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
        }

        // ARC-COMBAT-016: with the lent bow's projectile art each shot flies from the hero's cell: the hit decided at the shot deals
        // nothing until the arrow leaves the target's tile, its art is drawn on the way, and a missed arrow flies on past the target
        // (to its range's end, a stray critter or an obstruction). Shots from the first cell until a hit has landed and a miss has
        // flown (at most 12; a miss is reported, not required).
        async Task<object> Projectiles()
        {
            _lentMissileArtForSelfTest = LentMissileArt;
            Heal();
            AreaSmokePlace(map, map.Chart.ToChart(new(pick.Hero.X, pick.Hero.Y)));
            _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            PublishWorldSelection();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var life = pick.Target.Build.Combat!.Target;
            int Hp() => session.QueryLife(life).View!.Status.HitPoints;
            var hits = 0; var misses = new List<string>(); var drawn = 0; var maxSteps = 0L;
            for (var shots = 0; shots < 12 && (hits == 0 || misses.Count == 0); shots++)
            {
                Heal();
                _projectileEndsForSelfTest.Clear(); _shotEndScriptsForSelfTest.Clear();
                var unlaunchedArrow = _projectilesUnlaunchedForSelfTest;
                var result = await ShootOnce(pick.Target, expectWalk: false);
                Require(result?.Shot is { } && _projectiles.Count == 1, $"the shot flies ({result?.Failure} {result?.Resolution?.Outcome}, {_projectiles.Count} in flight)");
                var hit = result!.Resolution!.Outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit;
                var before = Hp();
                Require(!hit || result.Life is null, "a hit deals nothing at the shot");
                for (var frames = 0; frames < 400 && _projectiles.Count > 0; frames++)
                {
                    if (_projectileBodies.Count > 0) drawn++;
                    maxSteps = Math.Max(maxSteps, _projectiles.Count > 0 ? _projectiles[0].Flight.Steps : 0);
                    _areaSmokeTime++;
                    SpellSmokeTick();
                    WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
                }
                Require(_projectiles.Count == 0 && _projectileEndsForSelfTest.Count == 1, $"the flight ended ({_projectiles.Count} in flight, {_projectileEndsForSelfTest.Count} ends)");
                var end = _projectileEndsForSelfTest[0];
                // ARC-COMBAT-1766 (0x4B2870, PF9, PS5): an end without a strike runs the weapon's end points once (hit 7 at the destination, miss 8 at the range end); a strike runs none of them here.
                RequireShotEndScripts(end.Strike, unlaunchedArrow == _projectilesUnlaunchedForSelfTest, thrown: false, wieldedWeapon: null, $"arrow shot {shots}");
                if (hit)
                {
                    Require(end.Strike == ProjectileStrikeKind.Target && end.Struck == pick.Target.Entity && Hp() <= before,
                        $"the hit struck its target on arrival ({end.Strike} {end.Struck}, hp {before} -> {Hp()})");
                    hits++;
                }
                else
                {
                    Require(end.Strike != ProjectileStrikeKind.Target, "a miss never strikes its target");
                    misses.Add($"{end.Strike}:{end.Struck ?? "none"}");
                }
                Require(session.QueryShotsInFlight().Count == 0, "the session holds no shot after the flight");
            }
            Require(hits >= 1, $"a hit landed within 12 shots (misses {string.Join(",", misses)})");
            Require(drawn > 0 && maxSteps >= ProjectileRules.DefaultStepsPerTick, $"the arrow was drawn while it flew ({drawn} frames, {maxSteps} steps)");
            GD.Print($"ARC-COMBAT-016 native-smoke projectile PASS map={_readyMapName} target={pick.Target.Entity} misses={misses.Count} drawn={drawn} steps={maxSteps}");
            return new { Hits = hits, Misses = misses, DrawnFrames = drawn, Steps = maxSteps };
        }

        // ARC-DEV-288 (saved-runs.md §3.5, SV5, SV6): an arrow in flight is saved with F7 (its projectile, flight run and shot), the live flight ends, and after F8 the restored
        // arrow is the saved one, flies on from where it was and ends as the live one did (the same strike, the same target, the same hit points left). The load replaces the
        // session, so the stage's session is the loaded one from here on.
        async Task<object> SavedFlight()
        {
            _lentMissileArtForSelfTest = LentMissileArt;
            Heal();
            AreaSmokePlace(map, map.Chart.ToChart(new(pick.Hero.X, pick.Hero.Y)));
            _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            PublishWorldSelection();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var saves = System.IO.Directory.CreateTempSubdirectory("arc-dev-288-").FullName;
            _saveLibraryForSelfTest = new(saves);
            void Frame()
            {
                _areaSmokeTime++;
                SpellSmokeTick();
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
            }
            int Hp() => session.QueryLife(pick.Target.Build.Combat!.Target).View!.Status.HitPoints;
            try
            {
                // ARC-DEV-317: a shot whose flight ends within three frames (a miss scattered next to the hero, as the random stream decides) is shot again, up to five times.
                for (var tries = 0; ; tries++)
                {
                    _projectileEndsForSelfTest.Clear();
                    var result = await ShootOnce(pick.Target, expectWalk: false);
                    Require(result?.Shot is { } && _projectiles.Count == 1, $"the shot to save flies ({result?.Failure} {result?.Resolution?.Outcome}, {_projectiles.Count} in flight)");
                    for (var frames = 0; frames < 3 && _projectiles.Count > 0; frames++) Frame();
                    if (_projectiles.Count == 1 || tries >= 4) break;
                    for (var frames = 0; frames < 400 && HeroRunsHost() is { AttackRun: not null }; frames++) Frame();
                    CancelAreaAttack(null); Heal();
                }
                Require(_projectiles.Count == 1, "the arrow still flies a few frames on");
                var entity = _projectiles[0].Entity;
                CancelAreaAttack(null);
                QuickKey(Key.F7);
                Require(_quickSaveStatus?.StartsWith("F7 quick saved", StringComparison.Ordinal) == true, "F7 saved with an arrow in flight: " + _quickSaveStatus);
                var atSave = session.QueryProjectile(entity);
                var flights = session.Capture().Flights;
                Require(atSave?.Flight is not null && flights.Projectiles.SingleOrDefault()?.Entity == entity && flights.HeroShots.Count == 1, "the snapshot holds the arrow, its flight and its shot");
                for (var frames = 0; frames < 400 && _projectiles.Count > 0; frames++) Frame();
                Require(_projectiles.Count == 0 && _projectileEndsForSelfTest.Count == 1, $"the live flight ended ({_projectileEndsForSelfTest.Count} ends)");
                var live = _projectileEndsForSelfTest[0];
                var liveHp = Hp();
                _projectileEndsForSelfTest.Clear();
                var running = _areaSession;
                QuickKey(Key.F8);
                if (_mapHandoff is not null) await (_lastLoad ?? Task.CompletedTask);
                Require(!ReferenceEquals(running, _areaSession) && _areaSession is not null, "F8 loaded a new session: " + _quickSaveStatus);
                session = _areaSession!;
                Require(session.QueryProjectile(entity) == atSave, "after the load the arrow is the saved one, its flight field for field");
                Require(session.QueryGoals().RunsOf(entity).Count() == 1, "after the load the arrow's flight run is in the table");
                for (var frames = 0; frames < 400 && (_projectileEndsForSelfTest.Count == 0 || _projectiles.Count > 0); frames++) Frame();
                Require(_projectiles.Count == 0 && _projectileEndsForSelfTest.Count == 1, $"the restored flight ended ({_projectiles.Count} in flight, {_projectileEndsForSelfTest.Count} ends)");
                var restored = _projectileEndsForSelfTest[0];
                Require((restored.Strike, restored.Struck) == (live.Strike, live.Struck) && Hp() == liveHp,
                    $"the restored arrow ends as the live one ({restored.Strike}:{restored.Struck} hp {Hp()}, live {live.Strike}:{live.Struck} hp {liveHp})");
                Require(session.QueryShotsInFlight().Count == 0 && session.QueryProjectiles().Count == 0, "nothing is left in flight");
                GD.Print($"ARC-DEV-288 native-smoke saved-flight PASS map={_readyMapName} strike={live.Strike} struck={live.Struck ?? "none"} hp={liveHp}");
                return new { Strike = live.Strike.ToString(), live.Struck, Hp = liveHp, Steps = atSave!.Flight!.Steps };
            }
            finally
            {
                _saveLibraryForSelfTest = null; _quickKeys.Reset();
                try { System.IO.Directory.Delete(saves, recursive: true); } catch (System.IO.IOException) { }
            }
        }

        // ARC-COMBAT-018: with the hero's bow training raised to expert a shot flies as two projectiles on the same line, each ending on
        // its own: both strike the target of a hit (the second may find a corpse), both fly on past it after a miss. The bow's level
        // reaches the expert minimum with the hero's unspent points in the character editor when it must. Shots until a hit (at most
        // 12); the training, the points and the editor are set back afterwards.
        async Task<object> ExpertShots()
        {
            const Arcanum.Simulation.Skills.Skill Bow = Arcanum.Simulation.Skills.Skill.Bow;
            var bow = session.QueryHeroSkills()![Bow].Training;
            var opened = session.OpenHeroEditor().Disposition == GameSessionDisposition.Changed;
            var spent = 0;
            while (session.QueryHeroSkills()![Bow].Level < Arcanum.Simulation.Skills.SkillRules.MinimumLevel(SkillTraining.Expert)
                && spent < 40 && session.SpendHeroPoint(session.Version, AdvancementTrack.BasicSkill, (int)Bow).Disposition == GameSessionDisposition.Changed) spent++;
            foreach (var tier in new[] { SkillTraining.Apprentice, SkillTraining.Expert })
                if (session.QueryHeroSkills()![Bow].Training < tier) session.SetHeroSkillTraining(session.Version, Bow, tier);
            var ends = new List<string>();
            var hit = false;
            var level = session.QueryHeroSkills()![Bow].Level;
            var restored = false;
            try
            {
                for (var shots = 0; shots < 12 && !hit && session.QueryHeroAttack() is { Training: >= SkillTraining.Expert }; shots++)
                {
                    Heal();
                    _projectileEndsForSelfTest.Clear();
                    var result = await ShootOnce(pick.Target, expectWalk: false);
                    // A critical miss that took the bow ends the check; its arrows are given up.
                    if (session.QueryHeroAttack() is not { Skill: CombatSkill.Bow }) { ClearAreaProjectiles("the bow was lost"); break; }
                    Require(result?.Shots.Count == 2 && _projectiles.Count == 2 && _projectiles[0].Destination == _projectiles[1].Destination,
                        $"the expert shot flies as two projectiles on one line ({result?.Failure} {result?.Resolution?.Outcome}, {_projectiles.Count} in flight)");
                    hit = result!.Resolution!.Outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit;
                    for (var frames = 0; frames < 400 && _projectiles.Count > 0; frames++)
                    {
                        _areaSmokeTime++;
                        SpellSmokeTick();
                        WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
                    }
                    Require(_projectiles.Count == 0 && _projectileEndsForSelfTest.Count == 2 && session.QueryShotsInFlight().Count == 0,
                        $"both flights ended ({_projectiles.Count} in flight, {_projectileEndsForSelfTest.Count} ends)");
                    if (hit) Require(_projectileEndsForSelfTest.All(e => e.Strike == ProjectileStrikeKind.Target && e.Struck == pick.Target.Entity),
                        $"both arrows of a hit strike its target ({string.Join(",", _projectileEndsForSelfTest.Select(e => $"{e.Strike}:{e.Struck}"))})");
                    else Require(_projectileEndsForSelfTest.All(e => e.Strike != ProjectileStrikeKind.Target), "a miss's arrows never strike its target");
                    ends.Add(string.Join("+", _projectileEndsForSelfTest.Select(e => $"{e.Strike}:{e.Struck ?? "none"}")));
                }
            }
            finally
            {
                session.SetHeroSkillTraining(session.Version, Bow, bow);
                // Back down by the points spent here, one refund each.
                for (var left = spent; left > 0; left--)
                    if (session.RefundHeroPoint(session.Version, AdvancementTrack.BasicSkill, (int)Bow, SkillRefundClick.OnePoint).Disposition != GameSessionDisposition.Changed) break;
                if (opened) session.CloseHeroEditor();
                restored = session.QueryHeroSkills()![Bow].Training == bow;
            }
            Require(restored, "the hero's bow training is set back");
            if (ends.Count == 0 && session.QueryHeroAttack() is { Skill: CombatSkill.Bow } && level < Arcanum.Simulation.Skills.SkillRules.MinimumLevel(SkillTraining.Expert))
            {
                GD.Print($"ARC-COMBAT-018 native-smoke expert-bow NONE (bow level {level} with {spent} points spent allows no expert training)");
                return $"NONE (bow level {level} allows no expert training)";
            }
            if (ends.Count == 0)
            {
                GD.Print("ARC-COMBAT-018 native-smoke expert-bow NONE (a critical miss took the bow before an expert shot flew)");
                return "NONE (a critical miss took the bow first)";
            }
            GD.Print($"ARC-COMBAT-018 native-smoke expert-bow PASS map={_readyMapName} target={pick.Target.Entity} level={level} spent={spent} hit={hit}"
                + $" ends={string.Join(",", ends)}");
            return new { Shots = ends.Count, Hit = hit, Ends = ends };
        }

        // ARC-COMBAT-017: the lent bow becomes a throwing dagger (melee skill, "default throws", the installed 6072's weight 15) with the
        // same projectile art. Each throw leaves the hand at the shot (the item lies hidden on the hero's cell and is not drawn), flies,
        // and lands on the tile its projectile ends on (the struck target's, or the last one beyond the thrown range), drawn again and
        // without decay; it is picked up and wielded again for the next throw. Throws until one has hit (at most 12; a hit is reported,
        // not required).
        async Task<object> Throws()
        {
            var dagger = wielded.Item with
            {
                Weapon = wielded.Item.Weapon! with
                {
                    Skill = CombatSkill.Melee, Range = LentBowRange, MagicRange = 0, AmmoType = null, AmmoConsumption = 0,
                    CriticalMissChart = wielded.Item.Weapon.CriticalMissChart,
                },
            };
            Require(Wear(dagger), "the kit weapon takes the lent throwing dagger's values");
            _lentMissileArtForSelfTest = LentMissileArt;
            _lentThrowWeightForSelfTest = 15;
            var landings = new List<string>();
            var hit = false; var hiddenChecked = false; var scatters = new List<string>();
            // ARC-DEV-140 (B14, PF10, GT63): the lent dagger carries an item spell with the attacks-a-location trigger (0x800000, the grenade's), taken from the installed spell
            // records; the end of each throw that ends on a crossing (a strike, the range, the destination) must fire it at the tile it ended on. The thrown dagger lies on the map
            // when the event fires, so the cast host finds its lent spell facts and the executor starts the run; this checks the viewer's chain from the projectile's end to a started spell.
            for (var waits = 0; waits < 600 && SpellRecordsNow() is null; waits++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            int? lentSpell = null;
            if (SpellRecordsNow() is { } spellRecords)
                for (var n = 0; n < spellRecords.Length && lentSpell is null; n++)
                    if (spellRecords[n] is { } candidate && (candidate.Data.ItemTriggers & 0x800000) != 0) lentSpell = n;
            if (lentSpell is { } lentNumber)
                _lentItemSpellsForSelfTest = new(wielded.Entity, SessionSpellCaster.Hero, 14, [lentNumber, 10000, 10000, 10000, 10000], -1, 0);
            var shotEndEvents = 0;
            for (var throws = 0; throws < 12 && !hit; throws++)
            {
                Heal();
                AreaSmokePlace(map, map.Chart.ToChart(new(pick.Hero.X, pick.Hero.Y)));
                _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
                PublishWorldSelection();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                _projectileEndsForSelfTest.Clear(); _thrownLandingsForSelfTest.Clear(); _shotEndItemEventsForSelfTest.Clear(); _shotEndScriptsForSelfTest.Clear();
                var unlaunched = _projectilesUnlaunchedForSelfTest;
                var result = await ShootOnce(pick.Target, expectWalk: false);
                var heroCell = session.QueryActor().Cell;
                // ARC-COMBAT-018: a miss that scatters back onto the hero's own cell has no line to fly and lands at once.
                Require(result?.Shot is { } shot && (session.QueryThrownShot(shot) is { Item: var item } && item == wielded.Entity || result.Scatter == heroCell),
                    $"the dagger is thrown ({result?.Failure} {result?.Resolution?.Outcome}, shot {result?.Shot?.Id}, scatter {result?.Scatter})");
                Require(result!.Resolution!.Difficulty is not null && !session.QueryEquipment().Worn.Any(w => w.Slot == EquipmentSlot.Weapon),
                    "the dagger left the hand at the shot");
                if (_projectiles.Count == 1)
                {
                    SyncAreaGroundBodies();
                    Require(session.QueryItemInFlight(wielded.Entity) && session.QueryPlacedItems().Single(p => p.Entity == wielded.Entity).Cell == heroCell
                        && !_groundBodies.ContainsKey(PlacedKey(runtime, wielded.Entity)), "it lies hidden on the hero's cell while it flies");
                    hiddenChecked = true;
                }
                for (var frames = 0; frames < 1500 && _projectiles.Count > 0; frames++)
                {
                    _areaSmokeTime++;
                    SpellSmokeTick();
                    WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
                }
                Require(_projectiles.Count == 0 && _thrownLandingsForSelfTest.Count == 1 && _thrownLandingsForSelfTest[0].Disposition != GameSessionDisposition.Rejected,
                    $"the flight ended and the dagger landed ({_projectiles.Count} in flight, {_thrownLandingsForSelfTest.Count} landings)");
                var landing = _thrownLandingsForSelfTest[0];
                var placed = session.QueryPlacedItems().Single(p => p.Entity == wielded.Entity);
                Require(placed.Cell == landing.Tile && !session.QueryItemInFlight(wielded.Entity) && session.QueryShotsInFlight().Count == 0
                    && session.QueryItemDecays().All(d => d.Entity != wielded.Entity), $"it lies where the projectile ended, without decay ({placed.Cell} vs {landing.Tile})");
                var end = _projectileEndsForSelfTest.Single();
                // ARC-COMBAT-1766: the hero's thrown item leaves its hit and miss scripts (7, 8) to the thrown-item host (the guard against a double run): only its critical points are asked here.
                RequireShotEndScripts(end.Strike, unlaunched == _projectilesUnlaunchedForSelfTest, thrown: true, wieldedWeapon: wielded.Entity, $"throw {throws}");
                // A throw whose flight goal ended before its first tick (the scatter back onto the hero's own cell) ends through the end node, which fires no item event.
                if (lentSpell is { } expected && end.Strike != ProjectileStrikeKind.None && unlaunched == _projectilesUnlaunchedForSelfTest)
                {
                    Require(_shotEndItemEventsForSelfTest.Count == 1 && _shotEndItemEventsForSelfTest[0].Item == wielded.Entity && _shotEndItemEventsForSelfTest[0].Tile == landing.Tile
                        && _shotEndItemEventsForSelfTest[0].Spells.Contains(expected) && _shotEndItemEventsForSelfTest[0].Started >= 1, $"the throw's end fired and started the item's attacks-a-location spell {expected} at the landing tile ({_shotEndItemEventsForSelfTest.Count} events, end {end.Strike})");
                    shotEndEvents++;
                }
                hit = result.Resolution.Outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit;
                if (hit) Require(end.Strike == ProjectileStrikeKind.Target && landing.Tile == World(pick.Target.Row), $"a hit lands on its target's tile ({end.Strike} {landing.Tile})");
                // ARC-COMBAT-018: a missed throw flies to its scattered tile without a target and lands there unless it strikes on the way.
                else Require(result.Scatter is { } scatter && end.Strike != ProjectileStrikeKind.Target
                    && (end.Strike != ProjectileStrikeKind.DestinationEnd || landing.Tile == scatter), $"a miss scatters ({end.Strike} {landing.Tile} vs {result.Scatter})");
                if (!hit) scatters.Add($"{result.Scatter!.Value.X},{result.Scatter.Value.Y}:{end.Strike}");
                SyncAreaGroundBodies();
                landings.Add($"{end.Strike}:{landing.Tile.X},{landing.Tile.Y}:drawn={_groundBodies.ContainsKey(PlacedKey(runtime, wielded.Entity))}");
                // The viewer's wield passes the item-trigger host (ItemTriggersNow); a thrown dagger whose end spell (a Summon run) is still alive is not wielded without it.
                Require(session.PickUpItem(session.Version, wielded.Entity, _ => SessionInsertVerdict.Accepted).Disposition == GameSessionDisposition.Changed
                    && session.Equip(session.Version, wielded.Entity, EquipmentSlot.Weapon, new(kit.Kind, [], false, dagger), (_, _) => true,
                        _ => SessionInsertVerdict.Accepted, null, ItemTriggersNow()).Disposition == GameSessionDisposition.Changed, "the dagger is picked up and wielded again");
            }
            // The pre-made heroes barely train throwing: a hit is reported, not required (the session's tests strike with one).
            Require(hiddenChecked, $"a throw was seen in flight ({string.Join(",", landings)})");
            _lentThrowWeightForSelfTest = null; _lentItemSpellsForSelfTest = null;
            GD.Print(lentSpell is null ? "ARC-DEV-140 native-smoke shot-end-item-event NONE (no installed spell record carries the attacks-a-location trigger)"
                : $"ARC-DEV-140 native-smoke shot-end-item-event PASS map={_readyMapName} spell={lentSpell} events={shotEndEvents}");
            GD.Print($"ARC-COMBAT-017 native-smoke throw PASS map={_readyMapName} target={pick.Target.Entity} hit={hit} landings={string.Join(",", landings)}");
            if (scatters.Count > 0) GD.Print($"ARC-COMBAT-018 native-smoke scatter PASS map={_readyMapName} misses={string.Join(",", scatters)}");
            return new { Throws = landings.Count, Hit = hit, Landings = landings, Scatters = scatters };
        }

        // ARC-COMBAT-019: the lent weapon becomes a boomerang (the throwing skill, range 10, the bow's projectile art) and stays wielded.
        // Each throw marks the hero (a second attack is refused while it flies), turns once at its first end (its strike, or the end of
        // its range) toward the hero's tile, flies back and ends there, which clears the mark; then the auto attack starts a new attack
        // goal on the target. Throws until one has hit (at most 6; a hit is reported, not required).
        async Task<object> Boomerangs()
        {
            var boomerang = wielded.Item with
            {
                Weapon = wielded.Item.Weapon! with
                {
                    Skill = CombatSkill.Throwing, Range = LentBowRange, MagicRange = 0, AmmoType = null, AmmoConsumption = 0,
                },
            };
            Require(Wear(boomerang), "the kit weapon takes the lent boomerang's values");
            _lentMissileArtForSelfTest = LentMissileArt;
            Require(session.QueryHeroAttack() is { Skill: CombatSkill.Throwing, Supported: true }, "the hero throws a boomerang");
            var at = World(pick.Target.Row);
            var distance = Math.Max(Math.Abs(at.X - pick.Hero.X), Math.Abs(at.Y - pick.Hero.Y));
            var flights = new List<string>();
            var hit = false; var refused = false; var reattacks = 0;
            for (var throws = 0; throws < 6 && !hit; throws++)
            {
                Heal();
                AreaSmokePlace(map, map.Chart.ToChart(new(pick.Hero.X, pick.Hero.Y)));
                _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
                PublishWorldSelection();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                _projectileEndsForSelfTest.Clear(); _boomerangTurnsForSelfTest.Clear(); _shotEndScriptsForSelfTest.Clear();
                var unlaunchedBoomerang = _projectilesUnlaunchedForSelfTest;
                var result = await ShootOnce(pick.Target, expectWalk: false);
                // ARC-RENDER-039: a returning weapon attacks with the throw (WA4), drawn with the body's throw ART.
                Require(lastStart == (Arcanum.Simulation.Equipment.WeaponArtRules.ThrowAnimation, ActorDiagnosticAction.Throw),
                    $"the boomerang plays the throw ({lastStart.Animation} {lastStart.Action}, throw ART {_worldActor.Profile.Throw is not null})");
                // A critical miss may drop or break the lent boomerang: the check ends there.
                if (session.QueryHeroAttack() is not { Skill: CombatSkill.Throwing }) { ClearAreaProjectiles("the boomerang was lost"); break; }
                Require(result?.Shot is { } shot && session.QueryShotReturns(shot) && session.QueryBoomerangInFlight() && _projectiles.Count == 1
                    && _projectiles[0].Returns && session.QueryThrownShot(shot) is null,
                    $"the boomerang flies and marks the hero ({result?.Failure} {result?.Resolution?.Outcome}, {_projectiles.Count} in flight)");
                Require(session.QueryEquipment().Worn.Any(w => w.Slot == EquipmentSlot.Weapon && w.Entity == wielded.Entity), "it stays in the hand");
                if (!refused)
                {
                    var again = session.AttackCritter(session.Version, CritterAttack(new AttackGoal(pick.Target, pick.Target.Build.Combat!, null), distance, null) with { Flight = true });
                    Require(again.Failure == GameSessionFailure.WeaponInFlight, $"no attack starts while it is away ({again.Failure})");
                    refused = true;
                }
                for (var frames = 0; frames < 1500 && _projectiles.Count > 0; frames++)
                {
                    _areaSmokeTime++;
                    SpellSmokeTick();
                    WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
                }
                Require(_projectiles.Count == 0 && session.QueryShotsInFlight().Count == 0 && !session.QueryBoomerangInFlight(),
                    $"the boomerang came back ({_projectiles.Count} in flight, away {session.QueryBoomerangInFlight()})");
                Require(_boomerangTurnsForSelfTest.Count == 1 && _boomerangTurnsForSelfTest[0].To == pick.Hero,
                    $"it turned once toward the hero's tile ({string.Join(",", _boomerangTurnsForSelfTest.Select(t => $"{t.From}->{t.To}"))})");
                Require(_projectileEndsForSelfTest.Count == 2 && _projectileEndsForSelfTest[1].Strike == ProjectileStrikeKind.DestinationEnd,
                    $"its first end and its return ({string.Join(",", _projectileEndsForSelfTest.Select(e => $"{e.Strike}:{e.Struck}"))})");
                var first = _projectileEndsForSelfTest[0];
                // ARC-COMBAT-1766 (0x4B2870, 0x4B2690): the first end without a strike turns the boomerang back and then runs the end points; its return runs them once more, also when it turned on the hero's own tile (ARC-COMBAT-1971: the original returns through the tile it entered, then ends with the hit script).
                var turn = _boomerangTurnsForSelfTest[0];
                var firstScripts = first.Strike is ProjectileStrikeKind.RangeEnd or ProjectileStrikeKind.DestinationEnd ? 1 : 0;
                var returnScripts = 1;
                // A flight that never took a step ends through the goal's own end, which asks no scripts (ARC-DEV-140, GT53): the exact count holds only for crossing ends.
                if (unlaunchedBoomerang != _projectilesUnlaunchedForSelfTest) { returnScripts = 0; firstScripts = Math.Min(firstScripts, _shotEndScriptsForSelfTest.Count); }
                Require(_shotEndScriptsForSelfTest.Count == firstScripts + returnScripts
                    && _shotEndScriptsForSelfTest.All(e => e.Attacker == SessionEventKinds.PlayerEntity && e.Weapon == wielded.Entity && !e.Thrown && e.Points.Count > 0 && e.Points[0] is ScrPoint.Hit or ScrPoint.Miss)
                    && (returnScripts == 0 || _shotEndScriptsForSelfTest[^1].Destination && _shotEndScriptsForSelfTest[^1].Points[0] == ScrPoint.Hit),
                    $"the boomerang's ends ran the weapon's end points ({string.Join(",", _shotEndScriptsForSelfTest.Select(e => $"{(e.Destination ? "dest" : "range")}:{string.Join("/", e.Points)}"))} for first {first.Strike}, turn {turn.From}->{turn.To})");
                hit = result!.Resolution!.Outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit;
                if (hit) Require(first.Strike == ProjectileStrikeKind.Target && first.Struck == pick.Target.Entity, $"a hit turns at its target ({first.Strike} {first.Struck})");
                else Require(first.Strike != ProjectileStrikeKind.Target, "a miss never strikes its target");
                // 0x4A9AD0: back in the hand, the auto attack starts a new attack goal on the target (it may have fallen to the hit).
                if (_attack is { } again2 && again2.Entity == pick.Target.Entity) reattacks++;
                CancelAreaAttack(null);
                flights.Add($"{first.Strike}:{first.Struck ?? "none"}:{_boomerangTurnsForSelfTest[0].From.X},{_boomerangTurnsForSelfTest[0].From.Y}");
            }
            if (flights.Count == 0)
            {
                GD.Print("ARC-COMBAT-019 native-smoke boomerang NONE (a critical miss took the boomerang before its first flight)");
                return "NONE (a critical miss took the boomerang first)";
            }
            Require(reattacks > 0 || !_gameSettings.AutoAttack || _combatHost!.Target(session, runtime, pick.Target.Row.Key) is { Dead: true },
                $"the auto attack started again after a return ({reattacks} of {flights.Count})");
            GD.Print($"ARC-COMBAT-019 native-smoke boomerang PASS map={_readyMapName} target={pick.Target.Entity} hit={hit} reattacks={reattacks}"
                + $" flights={string.Join(",", flights)}");
            return new { Throws = flights.Count, Hit = hit, Reattacks = reattacks, Flights = flights };
        }

        // ARC-UI-063: the kit weapon, with its own values back, is lifted onto the game cursor: from the inventory window's round view in
        // its original look (IW18), or straight from the window without the art. A window opened while it is held puts it back (the item
        // never left the slot). Then it is thrown at the target's tile from the game cursor: it leaves the slot at the throw, flies on the
        // projectile host and lands where the flight ends; it is picked up and wielded again. Three throws; a hit is reported, not required.
        async Task<object> CursorThrows()
        {
            Require(Wear(wielded.Item), "the kit weapon has its own values back");
            var throws = new List<string>(); var lifts = new List<string>(); var cursorShown = false; var hit = false;
            async Task Lift()
            {
                OpenInventory();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Require(InventoryOpen, "the inventory window opened");
                var area = _inventoryPanel!.GridAreaForSelfTest;
                if (area.Look is not null && _inventoryPanel.ArtMode)
                {
                    area.PressAt(AreaInventoryGridArea.SlotCentre(EquipmentSlot.Weapon));
                    Require(area.Held?.Entity == wielded.Entity, $"the wielded weapon is held in the window ({area.Held?.Entity})");
                    var (x, y) = AreaInventoryHole.Corner(AreaInventoryMode.Own);
                    area.ReleaseAt(new(x + AreaInventoryHole.Side / 2f, y + AreaInventoryHole.Side / 2f));
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    lifts.Add("round-view");
                }
                else { LiftToGameCursor(wielded.Entity, "smoke"); lifts.Add("direct"); }
                Require(GameCursorItemForSelfTest == wielded.Entity && !InventoryOpen,
                    $"the weapon is on the game cursor and the window closed ({GameCursorItemForSelfTest}, open {InventoryOpen}, {_inventoryStatus})");
                Require(session.QueryEquipment().Worn.Any(w => w.Slot == EquipmentSlot.Weapon && w.Entity == wielded.Entity), "it stays wielded until the throw");
                SyncCursorArt();
                if (CursorShownForSelfTest is not null && CursorHeldShownForSelfTest == wielded.Entity) cursorShown = true;
            }
            await Lift();
            OpenInventory();
            ProcessGameCursorItem();
            Require(GameCursorItemForSelfTest is null && session.QueryEquipment().Worn.Any(w => w.Slot == EquipmentSlot.Weapon && w.Entity == wielded.Entity),
                $"a window opened puts it back ({HeldThrowNoticeForSelfTest})");
            EndInventory();
            for (var attempt = 0; attempt < 3; attempt++)
            {
                Heal();
                AreaSmokePlace(map, map.Chart.ToChart(new(pick.Hero.X, pick.Hero.Y)));
                _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
                PublishWorldSelection();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await Lift();
                _projectileEndsForSelfTest.Clear(); _thrownLandingsForSelfTest.Clear();
                _lastHeldThrowForSelfTest = null;
                var started = ThrowGameCursorItem(World(pick.Target.Row));
                // ARC-DEV-182: the throw is goal 37 of the table: its swing plays to the action frame, then the session throws.
                for (var frames = 0; frames < 1500 && started && _lastHeldThrowForSelfTest is null && HeroRunsHost() is { ThrowRun: not null }; frames++)
                {
                    _areaSmokeTime++;
                    SpellSmokeTick();
                    WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
                }
                var result = _lastHeldThrowForSelfTest;
                Require(started && result is { Disposition: GameSessionDisposition.Changed } && GameCursorItemForSelfTest is null
                    && !session.QueryEquipment().Worn.Any(w => w.Slot == EquipmentSlot.Weapon),
                    $"the weapon is thrown from the cursor ({result?.Disposition} {result?.Failure}, {HeldThrowNoticeForSelfTest})");
                for (var frames = 0; frames < 1500 && (_projectiles.Count > 0 || HeroRunsHost() is { ThrowRun: not null }); frames++)
                {
                    _areaSmokeTime++;
                    SpellSmokeTick();
                    WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
                }
                Require(_projectiles.Count == 0 && _thrownLandingsForSelfTest.Count == 1 && _thrownLandingsForSelfTest[0].Disposition != GameSessionDisposition.Rejected,
                    $"the flight ended and the weapon landed ({_projectiles.Count} in flight, {_thrownLandingsForSelfTest.Count} landings)");
                var landing = _thrownLandingsForSelfTest[0];
                Require(session.QueryPlacedItems().Single(p => p.Entity == wielded.Entity).Cell == landing.Tile && !session.QueryItemInFlight(wielded.Entity)
                    && session.QueryShotsInFlight().Count == 0, $"it lies where the flight ended ({landing.Tile})");
                var outcome = result!.Attack?.Resolution?.Outcome;
                hit |= outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit;
                throws.Add($"{outcome?.ToString() ?? "unrolled"}:{_projectileEndsForSelfTest.LastOrDefault().Strike}:{landing.Tile.X},{landing.Tile.Y}");
                Require(session.PickUpItem(session.Version, wielded.Entity, _ => SessionInsertVerdict.Accepted).Disposition == GameSessionDisposition.Changed
                    && session.Equip(session.Version, wielded.Entity, EquipmentSlot.Weapon, new(kit.Kind, [], false, wielded.Item), (_, _) => true,
                        _ => SessionInsertVerdict.Accepted, null, ItemTriggersNow()).Disposition == GameSessionDisposition.Changed, "the weapon is picked up and wielded again");
            }
            GD.Print($"ARC-UI-063 native-smoke cursor-throw PASS map={_readyMapName} target={pick.Target.Entity} lifts={string.Join(",", lifts)}"
                + $" cursor={(cursorShown ? "held-art" : "NONE (no cursor or item art)")} hit={hit} throws={string.Join(",", throws)}");
            return new { Lifts = lifts, CursorShown = cursorShown, Hit = hit, Throws = throws };
        }

        void Heal()
        {
            // ARC-DEV-1839: a critical miss of a shot hurts the hero (5..12 hit points against the hero's 30), and the legs make dozens of shots, so the hero is healed with the target; unhealed, the
            // misses of the throws and boomerang legs add up on a map battery's hero until one kills it and the next attack click is refused (the hero cannot act).
            if (session.QueryHeroLife() is { Status.IsDead: false }) session.Heal(session.Version, SessionLifeTarget.Hero, new LifeHeal { Full = true, Fatigue = 1000 });
            var healed = session.Heal(session.Version, pick.Target.Build.Combat!.Target, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
            if (healed.Disposition == GameSessionDisposition.Rejected) GD.Print($"ARC-COMBAT-015 ranged smoke heal refused failure={healed.Failure}");
            SyncAreaCombatBodies();
        }

        // One attack goal on the target, run frame by frame until its first hit (a walk first when expected).
        async Task<SessionAttackResult?> ShootOnce(AreaCombatTarget target, bool expectWalk, bool started = false)
        {
            _lastCritterAttackForSelfTest = null;
            if (!started)
            {
                CancelAreaAttack(null);
                _attack = new(target, target.Build.Combat!, null);
                StartAttackGoal(_attack);
            }
            AttackSettleForSelfTest();
            Require(_attack is not null && AttackApproaching == expectWalk && (expectWalk || _attack.Player is not null),
                $"the attack {(expectWalk ? "walks first" : "starts without a walk")}: {_attackNotice}");
            if (_attack!.Player is not null) lastStart = (_attack.Animation, _attack.Action);
            // ARC-DEV-237: the goal is kept by reference: a critical miss that stuns the hero (a run of the table since ARC-DEV-187) interrupts the attack run and clears _attack at once, after the hit was counted.
            var shot = _attack!;
            for (var frames = 0; frames < 4000 && _attack is not null && shot.Attacks == 0; frames++)
            {
                _areaSmokeTime++; SmokeTickHeroRuns();
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
            }
            var attacks = shot.Attacks;
            CancelAreaAttack(null);
            // A critical miss stunned the hero: the goal's own step needs the area clock, so the smoke cuts the stun run (its end node clears the bit) and the next shot can start.
            HeroRunsHost()?.LoseAll("SMOKE_STUN_CUT");
            Require(attacks == 1, $"one hit ran (attacks {attacks}, notice {_attackNotice})");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            return _lastCritterAttackForSelfTest;
        }
    }
}
