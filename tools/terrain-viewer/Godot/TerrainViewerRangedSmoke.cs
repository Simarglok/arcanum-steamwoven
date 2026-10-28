using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-COMBAT-015: the session's last attack result on an NPC and the walks stopped at a clear shot, for the ranged smoke.
    private SessionAttackResult? _lastCritterAttackForSelfTest;
    private int _clearShotStops;

    private const int LentBowRange = 10;
    // ARC-COMBAT-016: the projectile art the smoke lends its bow: scenery number 0, the art of every installed bow (combat census).
    private const int LentMissileArt = 0x40000000;

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
            if (session.Unequip(session.Version, EquipmentSlot.Weapon, _ => SessionInsertVerdict.Accepted).Disposition == GameSessionDisposition.Rejected) return false;
            return session.Equip(session.Version, wielded.Entity, EquipmentSlot.Weapon, new(kit.Kind, [], false, item), (_, _) => true,
                _ => SessionInsertVerdict.Accepted).Disposition == GameSessionDisposition.Changed;
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
                _attack = new(pick.Target, pick.Target.Build.Combat!, null);
                StartAttackGoal(_attack);
                if (_attack is not { Approaching: true }) { GD.Print($"ARC-COMBAT-015 ranged smoke approach skipped from={far.X},{far.Y}: {_attackNotice}"); continue; }
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
            var pairs = session.QueryHeroAttack() is { Skill: CombatSkill.Bow } ? await ExpertShots() : "NONE (a critical miss took the bow)";
            var throws = session.QueryHeroAttack() is { Skill: CombatSkill.Bow } ? await Throws() : "NONE (a critical miss took the bow)";
            ToggleCombatMode();
            GD.Print("ARC-COMBAT-015 native-smoke ranged PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Map = _readyMapName, Target = pick.Target.Entity, Hero = new[] { pick.Hero.X, pick.Hero.Y }, Shot = first,
                Approach = second ?? (object)(stillBow ? "NONE (no cell 12..16 away, or the Original setting)" : "NONE (the first shot's critical miss took the bow)"), Npcs = npcs.Length,
                Projectiles = flights, ExpertShots = pairs, Throws = throws,
                Parity = "NOT_VERIFIED (rules read in the original executable; no original-runtime run)" }));
        }
        finally
        {
            CancelAreaAttack(null);
            _lentMissileArtForSelfTest = null;
            _lentThrowWeightForSelfTest = null;
            ClearAreaProjectiles("the smoke ended");
            // A thrown kit weapon still on the ground goes back to the hand before the kit weapon is restored.
            if (lent && !session.QueryEquipment().Worn.Any(w => w.Slot == EquipmentSlot.Weapon)
                && session.PickUpItem(session.Version, wielded.Entity, _ => SessionInsertVerdict.Accepted).Disposition == GameSessionDisposition.Changed)
                session.Equip(session.Version, wielded.Entity, EquipmentSlot.Weapon, new(kit.Kind, [], false, wielded.Item), (_, _) => true, _ => SessionInsertVerdict.Accepted);
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
                _projectileEndsForSelfTest.Clear();
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
                    WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
                }
                Require(_projectiles.Count == 0 && _projectileEndsForSelfTest.Count == 1, $"the flight ended ({_projectiles.Count} in flight, {_projectileEndsForSelfTest.Count} ends)");
                var end = _projectileEndsForSelfTest[0];
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
            try
            {
                if (session.QueryHeroAttack() is not { Training: >= SkillTraining.Expert })
                {
                    GD.Print($"ARC-COMBAT-018 native-smoke expert-bow NONE (bow level {level} with {spent} points spent allows no expert training)");
                    return $"NONE (bow level {level} allows no expert training)";
                }
                for (var shots = 0; shots < 12 && !hit; shots++)
                {
                    Heal();
                    _projectileEndsForSelfTest.Clear();
                    var result = await ShootOnce(pick.Target, expectWalk: false);
                    if (session.QueryHeroAttack() is not { Skill: CombatSkill.Bow }) break;
                    Require(result?.Shots.Count == 2 && _projectiles.Count == 2 && _projectiles[0].Destination == _projectiles[1].Destination,
                        $"the expert shot flies as two projectiles on one line ({result?.Failure} {result?.Resolution?.Outcome}, {_projectiles.Count} in flight)");
                    hit = result!.Resolution!.Outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit;
                    for (var frames = 0; frames < 400 && _projectiles.Count > 0; frames++)
                    {
                        _areaSmokeTime++;
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
                // Back down to the editor's floor (the points spent here).
                for (var i = 0; spent > 0 && i < 200; i++)
                    if (session.RefundHeroPoint(session.Version, AdvancementTrack.BasicSkill, (int)Bow, SkillRefundClick.OnePoint).Disposition != GameSessionDisposition.Changed) break;
                if (opened) session.CloseHeroEditor();
            }
            Require(ends.Count > 0, "an expert shot flew");
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
            for (var throws = 0; throws < 12 && !hit; throws++)
            {
                Heal();
                AreaSmokePlace(map, map.Chart.ToChart(new(pick.Hero.X, pick.Hero.Y)));
                _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
                PublishWorldSelection();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                _projectileEndsForSelfTest.Clear(); _thrownLandingsForSelfTest.Clear();
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
                    WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
                }
                Require(_projectiles.Count == 0 && _thrownLandingsForSelfTest.Count == 1 && _thrownLandingsForSelfTest[0].Disposition != GameSessionDisposition.Rejected,
                    $"the flight ended and the dagger landed ({_projectiles.Count} in flight, {_thrownLandingsForSelfTest.Count} landings)");
                var landing = _thrownLandingsForSelfTest[0];
                var placed = session.QueryPlacedItems().Single(p => p.Entity == wielded.Entity);
                Require(placed.Cell == landing.Tile && !session.QueryItemInFlight(wielded.Entity) && session.QueryShotsInFlight().Count == 0
                    && session.QueryItemDecays().All(d => d.Entity != wielded.Entity), $"it lies where the projectile ended, without decay ({placed.Cell} vs {landing.Tile})");
                var end = _projectileEndsForSelfTest.Single();
                hit = result.Resolution.Outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit;
                if (hit) Require(end.Strike == ProjectileStrikeKind.Target && landing.Tile == World(pick.Target.Row), $"a hit lands on its target's tile ({end.Strike} {landing.Tile})");
                // ARC-COMBAT-018: a missed throw flies to its scattered tile without a target and lands there unless it strikes on the way.
                else Require(result.Scatter is { } scatter && end.Strike != ProjectileStrikeKind.Target
                    && (end.Strike != ProjectileStrikeKind.DestinationEnd || landing.Tile == scatter), $"a miss scatters ({end.Strike} {landing.Tile} vs {result.Scatter})");
                if (!hit) scatters.Add($"{result.Scatter!.Value.X},{result.Scatter.Value.Y}:{end.Strike}");
                SyncAreaGroundBodies();
                landings.Add($"{end.Strike}:{landing.Tile.X},{landing.Tile.Y}:drawn={_groundBodies.ContainsKey(PlacedKey(runtime, wielded.Entity))}");
                Require(session.PickUpItem(session.Version, wielded.Entity, _ => SessionInsertVerdict.Accepted).Disposition == GameSessionDisposition.Changed
                    && session.Equip(session.Version, wielded.Entity, EquipmentSlot.Weapon, new(kit.Kind, [], false, dagger), (_, _) => true,
                        _ => SessionInsertVerdict.Accepted).Disposition == GameSessionDisposition.Changed, "the dagger is picked up and wielded again");
            }
            // The pre-made heroes barely train throwing: a hit is reported, not required (the session's tests strike with one).
            Require(hiddenChecked, $"a throw was seen in flight ({string.Join(",", landings)})");
            _lentThrowWeightForSelfTest = null;
            GD.Print($"ARC-COMBAT-017 native-smoke throw PASS map={_readyMapName} target={pick.Target.Entity} hit={hit} landings={string.Join(",", landings)}");
            if (scatters.Count > 0) GD.Print($"ARC-COMBAT-018 native-smoke scatter PASS map={_readyMapName} misses={string.Join(",", scatters)}");
            return new { Throws = landings.Count, Hit = hit, Landings = landings, Scatters = scatters };
        }

        void Heal()
        {
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
            Require(_attack is not null && _attack.Approaching == expectWalk && (expectWalk || _attack.Player is not null),
                $"the attack {(expectWalk ? "walks first" : "starts without a walk")}: {_attackNotice}");
            for (var frames = 0; frames < 4000 && _attack is not null && _attack.Attacks == 0; frames++)
            {
                _areaSmokeTime++;
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
            }
            var attacks = _attack?.Attacks ?? 0;
            CancelAreaAttack(null);
            Require(attacks == 1, $"one hit ran (attacks {attacks}, notice {_attackNotice})");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            return _lastCritterAttackForSelfTest;
        }
    }
}
