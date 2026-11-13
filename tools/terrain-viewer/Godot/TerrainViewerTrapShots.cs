using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Traps;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-TECHNOLOGY-1635 (ledger TT15, docs/world/trap-state-runtime.md "Shot"): the missile of a native trap's shot (script 30002). The cast host applies the shot's dispatcher part at the trap (the amount, the two
// d100 rolls, the fate branch, the spell) and hands the missile to the projectile host here: one projectile of the default art (0x40000000, scenery art 0, faced along its line) leaves the shooter object's tile for the
// victim's location with the amount in the session's shot (the original's field 0x4F); where it meets the victim the strike goes through the cast host (the attack event, the victim's taking-damage script, the damage step);
// a victim that left the line is not struck and the missile flies on to its range (0x14), striking another critter only by the stray rule of 0x4B2870. The flight is saved with the session (the `trapShots` list of the
// flights section, schema 100).
public partial class TerrainViewerApp
{
    /// <summary>The default projectile art the ranged path takes for the attack flag 0x100 (0x4B39B0): scenery art 0.</summary>
    private const uint TrapShotArt = 0x40000000;

    /// <summary>The range that ends a trap shot's flight when nothing was struck: 0x14 tiles (the original's projectile range for a context with no weapon).</summary>
    private const int TrapShotRange = 20;

    // The launches and the ends so far, for the smoke: the shot, its shooter and victim, what struck it (null: nothing) and the session's answer.
    private readonly List<(long Shot, string Shooter, string Victim, CellCoord From, CellCoord To)> _trapShotLaunchesForSelfTest = [];
    private readonly List<(long Shot, ProjectileStrikeKind Strike, string? Struck, SessionTrapHit? Hit, int HitPoints)> _trapShotEndsForSelfTest = [];

    /// <summary>
    /// The cast host's launch of a trap's shot (<see cref="SessionSpellCaster.LaunchTrapShot"/>): the missile of the default art, faced in the direction of its line, leaves the shooter object's tile for the victim's tile.
    /// False when the victim has no row on the shown map or the projectile did not launch (the cast host then strikes at once). A shooter with no row (a wall of the data) flies under a stand-in row.
    /// </summary>
    private bool LaunchTrapShotFlight(SessionTrapShotLaunch launch)
    {
        if (_areaSession is not { } owner || _spellCastMap is not { } cast || owner.QueryTrapShot(launch.Shot) is null || !AttackMapActive || AreaLineOfFireNow() is null) return false;
        var victim = launch.Victim == SessionSpellCaster.Hero ? HeroRow : cast.Row(launch.Victim);
        if (victim is null) return false;
        var shooter = cast.Row(launch.Shooter) ?? TrapShooterStandIn;
        var direction = ProjectileRules.Direction(launch.From, launch.Destination);
        var faced = ProjectileRules.FacedArt(TrapShotArt, direction);
        var projectile = new AreaProjectile
        {
            Shot = launch.Shot, Owner = owner, Map = CurrentAreaMap, Target = victim, Shooter = shooter, Destination = launch.Destination, Art = faced,
            Flight = new(launch.From, launch.From, [(0, 0)], 1, launch.From, 0, 0, 0), State = new(true, false, TrapShotRange),
            Launched = new(launch.Victim, false, false, launch.Shooter, null),
        };
        if (!ProjectileHost().Launch(projectile, launch.From, launch.Victim == SessionSpellCaster.Hero ? null : launch.Victim))
        {
            GD.Print($"ARC-TECHNOLOGY-1635 trap-shot-not-launched shot={launch.Shot.Id} shooter={launch.Shooter}");
            return false;
        }
        _trapShotLaunchesForSelfTest.Add((launch.Shot.Id, launch.Shooter, launch.Victim, launch.From, launch.Destination));
        GD.Print($"ARC-TECHNOLOGY-1635 trap-shot-launch shot={launch.Shot.Id} entity={projectile.Entity} shooter={launch.Shooter} victim={launch.Victim} from={launch.From.X},{launch.From.Y}"
            + $" to={launch.Destination.X},{launch.Destination.Y} direction={direction} art={(faced is { } a ? $"0x{a:X8}" : "none")}");
        SyncAreaProjectileBodies();
        return true;
    }

    /// <summary>
    /// A trap's shot's projectile ended on a crossing (<see cref="CrossTile"/>): met the victim where it stands on the line (<see cref="ProjectileStrikeKind.Target"/>) or another living critter by the stray rule
    /// (<see cref="ProjectileStrikeKind.Stray"/>), the strike is the cast host's (<see cref="SessionSpellCaster.StrikeTrapShot"/>: the attack event of the shooter object, then the damage with the shooter as the damage source);
    /// an obstruction object or the end of the range ends the shot with no strike (an object struck by a trap's missile is not modelled: the amount would go through the object's damage step; recorded in the ledger).
    /// </summary>
    private ProjectileCrossResult TrapShotEnd(AreaProjectile projectile, ProjectileStrikeKind kind, AreaObjectRecord? struck)
    {
        var owner = projectile.Owner;
        _projectileHost?.Finish(projectile);
        var id = struck is null ? null : struck.Key == HeroRow.Key ? SessionSpellCaster.Hero : _spellCastMap?.IdOf(struck.Key);
        if (kind is ProjectileStrikeKind.Target or ProjectileStrikeKind.Stray && id is not null && ReferenceEquals(owner, _areaSession) && SpellCasterNow() is { } caster
            && ReferenceEquals(caster.Owner, owner) && caster.StrikeTrapShot(projectile.Shot, id) is { } result)
        {
            _trapShotEndsForSelfTest.Add((projectile.Shot.Id, kind, id, result.Hit, result.HitPointDamage));
            Ended(projectile, kind, id, $"a trap's shot: hit={result.Hit} amount={result.Amount} hit-points={result.HitPointDamage} shooter={result.Shooter}");
            FlushSpellLog(null);
            RefreshAreaStatus();
            return ProjectileCrossResult.Ended;
        }
        owner.EndShot(projectile.Shot);
        _trapShotEndsForSelfTest.Add((projectile.Shot.Id, kind, null, null, 0));
        Ended(projectile, kind, null, kind == ProjectileStrikeKind.Obstruction ? "a trap's shot met an object: its strike is not modelled" : "a trap's shot struck nothing");
        return ProjectileCrossResult.Ended;
    }

    // ARC-TECHNOLOGY-1635, a leg of the ranged stage (the lent bow's art is the default projectile art): a trap's shot (script 30002) from a scenery, portal or trap object of the map 3..8 cells from the hero, fired
    // through the cast host as a trap would. (1) The missile flies as a projectile of the shown host, drawn, the victim is unhurt while it flies and hurt by exactly the drawn amount where it meets the hero. (2) A hero that left the
    // line (a standable cell two cells off it, moved to once the missile flew) is not struck. (3) A shot in flight is saved with F7, the live flight ends, and after F8 the restored shot flies on and strikes as the live one did
    // (the same hit points left). The load replaces the session: the caller reads it again from the viewer. NONE (printed in the result, not a failure) when no object stands 3..8 cells from the hero.
    private async Task<object> TrapShotSmokeLegAsync(AreaMapRuntime map, CellCoord hero, IReadOnlyList<CellCoord> alternatives)
    {
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-TECHNOLOGY-1635 trap-shot smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_TRAP_SHOT_SMOKE_FAILED");
        }
        var session = _areaSession!;
        if (SpellCasterNow() is not { } caster || _spellCastMap is not { } cast || _readyArea is not { } area || AreaLineOfFireNow() is not { } lineMap) return "NONE (no cast host or line map)";
        var chosen = area.Snapshot.Objects.Where(r => r.Scope == AreaScope.Core && r.Type is 1 or 3 or 17 && r.WorldX is not null && r.WorldY is not null && cast.IdOf(r.Key) is not null)
            .Select(r => (Row: r, Cell: new CellCoord(checked((int)r.WorldX!.Value), checked((int)r.WorldY!.Value))))
            .Where(c => ProjectileRules.Distance(c.Cell, hero) is >= 3 and <= 8 && lineMap.Contains(c.Cell))
            .OrderByDescending(c => ProjectileRules.Distance(c.Cell, hero)).ThenBy(c => cast.IdOf(c.Row.Key), StringComparer.Ordinal).Take(1).ToArray();
        if (chosen.Length == 0) return "NONE (no scenery, portal or trap object 3..8 cells from the hero)";
        var (shooterRow, shooterCell) = chosen[0];
        var shooterId = cast.IdOf(shooterRow.Key)!;
        // The trap's counters: no spell, the shooter's tag 5, amounts 1..2 (the hero's hit points are healed back between the legs).
        var firing = new SessionTrapFiring(shooterId, SessionSpellCaster.Hero, TrapRules.NativeRun(30002, TrapRules.NonmagicalFlag)!.Value, unchecked((int)(5u << 16 | 2u << 8 | 1u)), 0)
        {
            Shooters = [new(shooterId, TrapEffectRules.ShooterScript, unchecked((int)(5u << 16)))],
        };
        int HeroHp() => session.QueryHeroLife()!.Status.HitPoints;
        void HealHero() => session.Heal(session.Version, SessionLifeTarget.Hero, new LifeHeal { Full = true, Fatigue = 1000 });
        void Frame()
        {
            _areaSmokeTime++;
            SpellSmokeTick();
            WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
        }
        async Task PlaceAsync(CellCoord cell)
        {
            AreaSmokePlace(map, map.Chart.ToChart(new(cell.X, cell.Y)));
            // The placement restarts the grid's observations: the smoke clock and the world's test time are set again as the ranged stage's other legs set them after their placement.
            _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            PublishWorldSelection();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        SessionTrapVictim Fire()
        {
            _trapShotLaunchesForSelfTest.Clear(); _trapShotEndsForSelfTest.Clear(); _projectileEndsForSelfTest.Clear();
            var fired = caster.ApplyTrapEffect(firing).Victims.Single();
            Require(fired.Hit == SessionTrapHit.InFlight && _trapShotLaunchesForSelfTest.Count == 1 && _projectiles.Count == 1 && session.QueryShotsInFlight().Count == 1,
                $"the shot flies as a projectile ({fired.Hit}, {_trapShotLaunchesForSelfTest.Count} launches, {_projectiles.Count} in flight)");
            return fired;
        }
        var saves = System.IO.Directory.CreateTempSubdirectory("arc-technology-1635-").FullName;
        try
        {
            HealHero();
            await PlaceAsync(hero);
            // (1) the strike where the missile meets the hero.
            var hp = HeroHp();
            var fired = Fire();
            Require(HeroHp() == hp && _trapShotEndsForSelfTest.Count == 0, "no damage and no strike while it flies");
            var drawn = 0;
            for (var frames = 0; frames < 800 && _projectiles.Count > 0; frames++) { Frame(); if (_projectileBodies.Count > 0) drawn++; }
            Require(_projectiles.Count == 0 && session.QueryShotsInFlight().Count == 0 && _trapShotEndsForSelfTest.Count == 1, $"the flight ended with a strike ({_projectiles.Count} in flight, {_trapShotEndsForSelfTest.Count} ends)");
            var end = _trapShotEndsForSelfTest[0];
            Require(end is { Strike: ProjectileStrikeKind.Target, Struck: SessionSpellCaster.Hero, Hit: SessionTrapHit.Damaged } && hp - HeroHp() == fired.Amount && drawn > 0,
                $"the missile struck the hero where it met it for the drawn amount (strike {end.Strike}:{end.Struck} {end.Hit}, {hp - HeroHp()} of {fired.Amount}, drawn in {drawn} frames)");
            GD.Print($"ARC-TECHNOLOGY-1635 trap-shot smoke strike shooter={shooterId} at={shooterCell.X},{shooterCell.Y} hero={hero.X},{hero.Y} amount={fired.Amount} drawn={drawn}");

            // (2) a hero that left the line is not struck.
            object leave = "NONE (no standable cell two cells off the line)";
            var along = new[] { (X: (double)hero.X - shooterCell.X, Y: (double)hero.Y - shooterCell.Y) }[0];
            var length = Math.Sqrt(along.X * along.X + along.Y * along.Y);
            if (alternatives.Where(c => length > 0 && Math.Abs(((double)c.X - shooterCell.X) * along.Y - ((double)c.Y - shooterCell.Y) * along.X) / length >= 2.0).Take(1).ToArray() is [var aside])
            {
                HealHero();
                hp = HeroHp();
                Fire();
                await PlaceAsync(aside);
                for (var frames = 0; frames < 1200 && _projectiles.Count > 0; frames++) Frame();
                Require(_projectiles.Count == 0 && session.QueryShotsInFlight().Count == 0 && _trapShotEndsForSelfTest.Count == 1, "the missile ended");
                var passed = _trapShotEndsForSelfTest[0];
                Require(passed.Struck != SessionSpellCaster.Hero && HeroHp() == hp, $"the hero that left the line was not struck ({passed.Strike}:{passed.Struck ?? "none"})");
                GD.Print($"ARC-TECHNOLOGY-1635 trap-shot smoke left-the-line aside={aside.X},{aside.Y} end={passed.Strike} struck={passed.Struck ?? "none"}");
                leave = new { Aside = new[] { aside.X, aside.Y }, End = passed.Strike.ToString(), Struck = passed.Struck ?? "none" };
                await PlaceAsync(hero);
            }

            // (3) a shot in flight is saved, and after the load strikes as the live one did.
            _saveLibraryForSelfTest = new(saves);
            HealHero();
            hp = HeroHp();
            for (var tries = 0; ; tries++)
            {
                fired = Fire();
                for (var frames = 0; frames < 2 && _projectiles.Count > 0; frames++) Frame();
                if (_projectiles.Count == 1 || tries >= 4) break;
                HealHero(); await PlaceAsync(hero);
            }
            Require(_projectiles.Count == 1, "the shot to save still flies");
            var entity = _projectiles[0].Entity;
            var shot = new SessionShot(_trapShotLaunchesForSelfTest[0].Shot);
            QuickKey(Key.F7);
            Require(_quickSaveStatus?.StartsWith("F7 quick saved", StringComparison.Ordinal) == true, "F7 saved with the shot in flight: " + _quickSaveStatus);
            var atSave = session.QueryProjectile(entity);
            var facts = session.QueryTrapShot(shot);
            Require(atSave?.Flight is not null && facts is { } && session.Capture().Flights.TrapShots.Count == 1, "the snapshot holds the shot, its projectile and its flight");
            for (var frames = 0; frames < 800 && _projectiles.Count > 0; frames++) Frame();
            Require(_projectiles.Count == 0 && _trapShotEndsForSelfTest.Count == 1, $"the live flight ended ({_trapShotEndsForSelfTest.Count} ends)");
            var live = _trapShotEndsForSelfTest[0];
            var liveHp = HeroHp();
            _trapShotEndsForSelfTest.Clear();
            var running = _areaSession;
            QuickKey(Key.F8);
            if (_mapHandoff is not null) await (_lastLoad ?? Task.CompletedTask);
            Require(!ReferenceEquals(running, _areaSession) && _areaSession is not null, "F8 loaded a new session: " + _quickSaveStatus);
            session = _areaSession!;
            Require(session.QueryTrapShot(shot) == facts && session.QueryProjectile(entity) == atSave, "after the load the shot and its projectile are the saved ones");
            for (var frames = 0; frames < 800 && (_trapShotEndsForSelfTest.Count == 0 || _projectiles.Count > 0); frames++) Frame();
            Require(_projectiles.Count == 0 && _trapShotEndsForSelfTest.Count == 1, $"the restored flight ended ({_projectiles.Count} in flight, {_trapShotEndsForSelfTest.Count} ends)");
            var restored = _trapShotEndsForSelfTest[0];
            Require((restored.Strike, restored.Struck, restored.Hit) == (live.Strike, live.Struck, live.Hit) && HeroHp() == liveHp,
                $"the restored shot ends as the live one ({restored.Strike}:{restored.Struck} hp {HeroHp()}, live {live.Strike}:{live.Struck} hp {liveHp})");
            Require(session.QueryShotsInFlight().Count == 0 && session.QueryProjectiles().Count == 0, "nothing is left in flight");
            GD.Print($"ARC-TECHNOLOGY-1635 trap-shot smoke saved-flight PASS strike={live.Strike} struck={live.Struck ?? "none"} hp={liveHp} amount={facts!.Value.Amount}");
            return new { Shooter = shooterId, At = new[] { shooterCell.X, shooterCell.Y }, Strike = end.Strike.ToString(), Amount = fired.Amount, Drawn = drawn, LeftTheLine = leave, Saved = live.Strike.ToString(), Hp = liveHp };
        }
        finally
        {
            _saveLibraryForSelfTest = null; _quickKeys.Reset();
            try { System.IO.Directory.Delete(saves, recursive: true); } catch (System.IO.IOException) { }
        }
    }
}
