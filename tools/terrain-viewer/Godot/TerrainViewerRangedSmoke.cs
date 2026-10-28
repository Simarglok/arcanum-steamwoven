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
            ToggleCombatMode();
            GD.Print("ARC-COMBAT-015 native-smoke ranged PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Map = _readyMapName, Target = pick.Target.Entity, Hero = new[] { pick.Hero.X, pick.Hero.Y }, Shot = first,
                Approach = second ?? (object)(stillBow ? "NONE (no cell 12..16 away, or the Original setting)" : "NONE (the first shot's critical miss took the bow)"), Npcs = npcs.Length,
                Parity = "NOT_VERIFIED (rules read in the original executable; no original-runtime run)" }));
        }
        finally
        {
            CancelAreaAttack(null);
            if (lent && !Wear(wielded.Item)) GD.Print("ARC-COMBAT-015 ranged smoke could not restore the kit weapon");
            Heal();
            _areaSmokeTime = (_areaSmokeTime + 19) / 20;
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
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
