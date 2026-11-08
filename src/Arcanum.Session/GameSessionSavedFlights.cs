using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;

namespace Arcanum.Session;

/// <summary>
/// ARC-DEV-288: how a projectile was launched, as the host rebuilds it after a load: the struck object's entity (<see cref="Target"/>, the hero's <c>pc</c> for a shot at him; null when the
/// launch named no row by entity, and such a flight is given up at the load), whether the shot aimed a critter (<see cref="Critter"/>) or a non-critter object (<see cref="Object"/>), and the
/// NPC that shot it (<see cref="Shooter"/>) or threw it (<see cref="Thrower"/>), null for the hero's.
/// </summary>
public sealed record SessionProjectileLaunch(string? Target, bool Critter, bool Object, string? Shooter, string? Thrower);

/// <summary>
/// ARC-DEV-288 (docs/architecture/saved-runs.md §3.5): what a save keeps of a projectile's flight, the original's projectile fields (SV5, SV6: the shot's flags in field 77, the weapon
/// in field 80, the flight's line and position in the object and its run): the shot it carries, how it was launched, the destination, the faced art, the line (<see cref="LineStart"/> to
/// <see cref="LineEnd"/>, rebuilt by <see cref="ProjectileRules.Resume"/>) with the next sample, the pixel position and the steps taken, and the carried shot flags
/// (<see cref="State"/>). The tile is the projectile's own cell.
/// </summary>
public sealed record SessionProjectileFlight(SessionShot Shot, SessionProjectileLaunch Launch, CellCoord Destination, uint? Art, CellCoord LineStart, CellCoord LineEnd, int Next,
    long X, long Y, long Steps, ProjectileShot State)
{
    /// <summary>A missed throw's scatter or a throw at a location: it flies free and ends leaving its destination.</summary>
    public bool Targetless { get; init; }
    /// <summary>A boomerang's projectile, which turns back at its first end.</summary>
    public bool Returns { get; init; }
    /// <summary>A thrown item that is no weapon, drawn on the size arc at <see cref="Scale"/> percent.</summary>
    public bool SizeArc { get; init; }
    public int Scale { get; init; } = 100;
    public int StepsPerTick { get; init; } = ProjectileRules.DefaultStepsPerTick;
    public int Frames { get; init; } = 1;
    public long Ticks { get; init; }
}

/// <summary>
/// ARC-DEV-288: the check a shot decided at the launch, as the projectile carries it to the strike (the original keeps only the shot's flags, field 77, SV6): the outcome, the miss
/// cause, the hit location, whether it was aimed, whether the weapon's magic was cancelled, the fate used and a critical self-hit. The check's rolls, difficulty and damage are not
/// carried: the strike rolls the damage then (<see cref="AttackResolver.LandHit"/>), so a restored shot's strike reports no check of its own.
/// </summary>
public sealed record SessionCarriedCheck(AttackRefusal Refusal, AttackOutcome Outcome, AttackMissCause MissCause, HitLocation HitLocation, bool Aimed, bool WeaponMagicCancelled,
    FateUse Fate, bool CriticalSelfHit)
{
    public static SessionCarriedCheck Of(AttackResolution check)
    {
        ArgumentNullException.ThrowIfNull(check);
        return new(check.Refusal, check.Outcome, check.MissCause, check.HitLocation, check.Aimed, check.WeaponMagicCancelled, check.Fate, check.CriticalSelfHit);
    }

    public AttackResolution Resolution() => new(Refusal, Outcome, MissCause, HitLocation, Aimed, WeaponMagicCancelled, null, null, null, Fate, CriticalSelfHit, null, []);
}

/// <summary>ARC-DEV-288: a shot of the hero in flight as a save keeps it: the weapon and skill it was shot with (the strike takes the hero's combatant as it is then), the carried check,
/// whether it hit, its target (an NPC's entity or a non-critter object's key), the thrown item, the projectile's item and the boomerang's state.</summary>
public sealed record SessionSavedHeroShot(SessionShot Shot, CombatWeapon Weapon, CombatSkill Skill, int SkillLevel, SkillTraining Training, int Reach, SessionCarriedCheck Check,
    bool Hit, string? Entity, string? Object)
{
    public SessionThrownShot? Thrown { get; init; }
    public string? WeaponItem { get; init; }
    public bool Struck { get; init; }
    public bool Boomerang { get; init; }
    public bool Returning { get; init; }
}

/// <summary>ARC-DEV-288: an NPC's thrown item in flight (a grenade) as a save keeps it.</summary>
public sealed record SessionSavedNpcThrow(SessionShot Shot, string Thrower, SessionThrownShot Thrown);

/// <summary>ARC-DEV-288: an NPC's arrow or bullet in flight as a save keeps it: the shooter, its attack values at the shot (the strike's attacker), the carried check, whether it hit and
/// its target (an NPC's entity, or <see cref="SessionNpcShotFacts.HeroTarget"/>).</summary>
public sealed record SessionSavedNpcArrow(SessionShot Shot, string Shooter, SessionNpcAttackValues Values, SessionCarriedCheck Check, bool Hit, string Target);

/// <summary>
/// ARC-DEV-288 (schema 80, the <c>flights</c> section; docs/architecture/saved-runs.md §3.5): the projectiles of the active map in flight with the shots they carry, and the two
/// counters. The original saves a projectile as an ordinary object made in play with its flight run (SV5, SV6) and goes on with it after the load; the session saves the
/// projectiles whose flight the host wrote and whose run the <c>goals</c> section holds, and the shots they carry. A game of an older schema has none. ARC-DEV-289 (schema 84): and the
/// spell missiles in flight (<see cref="Missiles"/>, each with its flight and one goal 28 run) with their counter.
/// </summary>
public sealed record SessionFlightState(int LastProjectile, IReadOnlyList<SessionProjectile> Projectiles, long NextShot, IReadOnlyList<SessionSavedHeroShot> HeroShots,
    IReadOnlyList<SessionSavedNpcThrow> NpcThrows, IReadOnlyList<SessionSavedNpcArrow> NpcArrows)
{
    public static SessionFlightState Empty { get; } = new(0, [], 0, [], [], []);

    /// <summary>ARC-DEV-289: the number of the last missile the session created (the <c>missile:n</c> counter).</summary>
    public int LastMissile { get; init; }
    /// <summary>ARC-DEV-289: the spell missiles in flight, ascending by number.</summary>
    public IReadOnlyList<SessionMissile> Missiles { get; init; } = [];

    public bool IsEmpty => Projectiles.Count == 0 && HeroShots.Count == 0 && NpcThrows.Count == 0 && NpcArrows.Count == 0 && Missiles.Count == 0;
}

public sealed partial class GameSessionOwner
{
    // ARC-DEV-288: a projectile a save keeps: the host wrote its flight, it carries a shot in flight and its one run is in the table, not done and not being stepped.
    private bool ProjectileIsSaved(SessionProjectile projectile) =>
        projectile.Flight is { } flight && ShotInFlight(flight.Shot)
        && _goals.Runs.Count(run => string.Equals(run.Owner, projectile.Entity, StringComparison.Ordinal)) == 1
        && _goals.Runs.Single(run => string.Equals(run.Owner, projectile.Entity, StringComparison.Ordinal)) is { Done: false } run
        && AiExecutorRules.IsProjectileRunGoal(run.Bottom.Goal) && _goals.SteppingSlot != run.Handle.Slot && GoalRunIsSavedKind(run);

    private bool ShotInFlight(SessionShot shot) => _shots.ContainsKey(shot) || _npcShots.ContainsKey(shot) || _npcArrows.ContainsKey(shot);

    // A projectile's run is saved with its projectile only (the goals capture asks this for an owner of the projectile origin).
    private bool ProjectileRunIsSaved(GoalRun run) =>
        _projectileObjects.TryGetValue(run.Owner, out var projectile) && ProjectileIsSaved(projectile);

    private SessionFlightState CaptureFlights()
    {
        var projectiles = _projectileObjects.Values.Where(ProjectileIsSaved).OrderBy(p => SessionProjectile.Number(p.Entity)).ToArray();
        var carried = projectiles.Select(p => p.Flight!.Shot).ToHashSet();
        var hero = _shots.Where(pair => carried.Contains(pair.Key)).OrderBy(pair => pair.Key.Id).Select(pair => new SessionSavedHeroShot(pair.Key, pair.Value.Facts.Weapon,
            pair.Value.Facts.Skill, pair.Value.Facts.SkillLevel, pair.Value.Facts.Training, pair.Value.Facts.Range, SessionCarriedCheck.Of(pair.Value.Check), pair.Value.Hit, pair.Value.Entity, pair.Value.Object)
        {
            Thrown = pair.Value.Thrown, WeaponItem = pair.Value.WeaponItem, Struck = pair.Value.Struck, Boomerang = pair.Value.Boomerang, Returning = pair.Value.Returning,
        }).ToArray();
        var throws = _npcShots.Where(pair => carried.Contains(pair.Key)).OrderBy(pair => pair.Key.Id).Select(pair => new SessionSavedNpcThrow(pair.Key, pair.Value.Thrower, pair.Value.Thrown)).ToArray();
        var arrows = _npcArrows.Where(pair => carried.Contains(pair.Key)).OrderBy(pair => pair.Key.Id)
            .Select(pair => new SessionSavedNpcArrow(pair.Key, pair.Value.Shooter, pair.Value.Values, SessionCarriedCheck.Of(pair.Value.Check), pair.Value.Hit, pair.Value.Target)).ToArray();
        return new(_lastProjectileNumber, projectiles, _nextShot, hero, throws, arrows)
        {
            LastMissile = _lastMissileNumber, Missiles = [.. _missileObjects.Values.Where(MissileIsSaved).OrderBy(m => SessionMissile.Number(m.Entity))],
        };
    }

    // Shapes the codec cannot know: the projectiles are on the saved map, numbered at most the counter, each with a flight that can resume and a shot of its own; every shot is carried by
    // exactly one projectile, numbered at most the shot counter; a hero's shot needs a hero; the entities are session entities. The runs are checked against the projectiles after the goals
    // import (CheckFlightRuns).
    private static GameSessionFailure ImportFlights(SessionFlightState saved, SessionMapId map, bool hero)
    {
        // The counters stay where the session's own increments can go on (CreateProjectile stops at int.MaxValue - 1; a shot id needs room for a full budget).
        if (saved.LastProjectile is < 0 or > int.MaxValue - 1 || saved.NextShot is < 0 or > long.MaxValue - MaxShotsInFlight) return GameSessionFailure.InvalidSnapshot;
        if (saved.Projectiles.Count > SessionProjectile.MaxProjectiles || saved.HeroShots.Count + saved.NpcThrows.Count + saved.NpcArrows.Count > MaxShotsInFlight)
            return GameSessionFailure.BudgetExceeded;
        if (!hero && saved.HeroShots.Count > 0) return GameSessionFailure.InvalidSnapshot;
        var shots = new HashSet<long>();
        foreach (var shot in saved.HeroShots.Select(s => s.Shot).Concat(saved.NpcThrows.Select(s => s.Shot)).Concat(saved.NpcArrows.Select(s => s.Shot)))
            if (shot.Id < 1 || shot.Id > saved.NextShot || !shots.Add(shot.Id)) return GameSessionFailure.InvalidSnapshot;
        foreach (var shot in saved.HeroShots)
        {
            if (shot.Entity is not null && shot.Object is not null) return GameSessionFailure.InvalidSnapshot;
            if (shot.Entity is { } entity && !SessionValidation.Entity(entity) || shot.Object is { Length: 0 } || shot.WeaponItem is { } item && !SessionValidation.Entity(item)) return GameSessionFailure.InvalidSnapshot;
            if (shot.Thrown is { } thrown && (!SessionValidation.Entity(thrown.Item) || thrown.Range < 0) || shot.Returning && !shot.Boomerang || shot.SkillLevel < 0 || shot.Reach < 0) return GameSessionFailure.InvalidSnapshot;
            if (!CarriedCheckFits(shot.Check, shot.Hit)) return GameSessionFailure.InvalidSnapshot;
        }
        foreach (var shot in saved.NpcThrows)
            if (!SessionValidation.Entity(shot.Thrower) || !SessionValidation.Entity(shot.Thrown.Item) || shot.Thrown.Range < 0) return GameSessionFailure.InvalidSnapshot;
        foreach (var shot in saved.NpcArrows)
            if (!SessionValidation.Entity(shot.Shooter) || shot.Target != SessionNpcShotFacts.HeroTarget && !SessionValidation.Entity(shot.Target) || shot.Values.SkillLevel < 0
                || !CarriedCheckFits(shot.Check, shot.Hit)) return GameSessionFailure.InvalidSnapshot;
        var carried = new HashSet<long>();
        var numbers = new HashSet<int>();
        foreach (var projectile in saved.Projectiles)
        {
            if (SessionProjectile.Number(projectile.Entity) is not { } number || number > saved.LastProjectile || !numbers.Add(number) || projectile.Map != map) return GameSessionFailure.InvalidSnapshot;
            if (!DropCellFits(projectile.Cell) || projectile.Flight is not { } flight || !shots.Contains(flight.Shot.Id) || !carried.Add(flight.Shot.Id)) return GameSessionFailure.InvalidSnapshot;
            var launch = flight.Launch;
            // The launch's kind is the shot's: a hero's shot names no shooter or thrower, an NPC's arrow its shooter, an NPC's throw its thrower.
            var arrow = saved.NpcArrows.FirstOrDefault(s => s.Shot == flight.Shot);
            var thrown = saved.NpcThrows.FirstOrDefault(s => s.Shot == flight.Shot);
            if (launch.Shooter != arrow?.Shooter || launch.Thrower != thrown?.Thrower) return GameSessionFailure.InvalidSnapshot;
            if (launch.Target is { } target && !SessionValidation.Entity(target) || launch.Shooter is { } shooter && !SessionValidation.Entity(shooter)
                || launch.Thrower is { } thrower && !SessionValidation.Entity(thrower) || launch.Critter && launch.Object) return GameSessionFailure.InvalidSnapshot;
            if (!DropCellFits(flight.Destination) || !DropCellFits(flight.LineStart) || !DropCellFits(flight.LineEnd) || flight.State.Range < 0 || flight.Scale is < 0 or > 100
                || flight.StepsPerTick is < 1 or > 1_000 || flight.Frames < 0 || flight.Ticks < 0
                || ProjectileRules.Resume(flight.LineStart, flight.LineEnd, flight.Next, projectile.Cell, flight.X, flight.Y, flight.Steps) is null) return GameSessionFailure.InvalidSnapshot;
        }
        return carried.Count == shots.Count ? GameSessionFailure.None : GameSessionFailure.InvalidSnapshot;
    }

    // A shot that hit carries a hit the strike can land (LandHit's own test); a miss carries any outcome but a refusal.
    private static bool CarriedCheckFits(SessionCarriedCheck check, bool hit) =>
        Enum.IsDefined(check.Refusal) && Enum.IsDefined(check.Outcome) && Enum.IsDefined(check.MissCause) && Enum.IsDefined(check.HitLocation) && Enum.IsDefined(check.Fate)
        && check.Refusal == AttackRefusal.None && check.Outcome != AttackOutcome.Refused
        && (!hit || check.Outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit && !(check.Aimed && check.HitLocation == HitLocation.Torso));

    // Every saved projectile has exactly one run of the goals section, a flight run (goal 36); every projectile run names a saved projectile (the goals import lets them through only so).
    private static GameSessionFailure CheckFlightRuns(SessionFlightState saved, GoalRunTable table)
    {
        var entities = saved.Projectiles.Select(p => p.Entity).ToHashSet(StringComparer.Ordinal);
        foreach (var run in table.Runs)
            if (run.Owner.StartsWith(SessionProjectile.EntityPrefix, StringComparison.Ordinal) && !entities.Contains(run.Owner)) return GameSessionFailure.InvalidSnapshot;
        foreach (var projectile in saved.Projectiles)
        {
            var runs = table.Runs.Where(run => string.Equals(run.Owner, projectile.Entity, StringComparison.Ordinal)).ToArray();
            if (runs.Length != 1 || !AiExecutorRules.IsProjectileRunGoal(runs[0].Bottom.Goal)) return GameSessionFailure.InvalidSnapshot;
        }
        return GameSessionFailure.None;
    }

    // The restore: the projectiles, the shots and the two counters (the owner is new: every store is empty).
    private void RestoreFlights(SessionFlightState saved)
    {
        foreach (var projectile in saved.Projectiles) _projectileObjects.Add(projectile.Entity, projectile);
        _lastProjectileNumber = saved.LastProjectile;
        _nextShot = saved.NextShot;
        foreach (var shot in saved.HeroShots)
            _shots.Add(shot.Shot, new PendingShot(new ShotFacts(shot.Weapon, shot.Skill, shot.SkillLevel, shot.Training, shot.Reach), shot.Check.Resolution(), shot.Hit, shot.Entity, shot.Object)
            {
                Thrown = shot.Thrown, WeaponItem = shot.WeaponItem, Struck = shot.Struck, Boomerang = shot.Boomerang, Returning = shot.Returning,
            });
        foreach (var shot in saved.NpcThrows) _npcShots.Add(shot.Shot, new NpcShot(shot.Thrower, shot.Thrown));
        foreach (var shot in saved.NpcArrows) _npcArrows.Add(shot.Shot, new NpcArrow(shot.Shooter, shot.Values, shot.Check.Resolution(), shot.Hit, shot.Target));
        // ARC-DEV-289: the spell missiles and their counter.
        foreach (var missile in saved.Missiles) _missileObjects.Add(missile.Entity, missile);
        _lastMissileNumber = saved.LastMissile;
    }
}
