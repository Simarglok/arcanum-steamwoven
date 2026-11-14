using Arcanum.Data.Area;
using Arcanum.Data.Combat;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-DEV-140 (MIG13 S8b): a projectile is a created object of the session that owns its run (goal 36), stepped by the frame step through the router; the host is per session. The projectile record
    // (AreaProjectile) lives in the Core project with the host; the strike, the item's landing and the map are this app's (IAreaProjectileViewer).
    private AreaProjectileHost? _projectileHost;
    // The steps the host took when the bodies were last drawn: a frame draws again only when a tick ran.
    private int _projectileDrawnSteps;
    // The line-of-fire map of the tick in progress (the viewer answers the host's map ask once per tick and the crossings read it).
    private AreaLineOfFire? _projectileLines;
    private IReadOnlyList<int?>? _areaKitMissileArt;
    private IReadOnlyList<int?>? _areaKitThrowWeight;
    // The smoke's lent projectile art (with its lent bow, ARC-COMBAT-016); null in play.
    private int? _lentMissileArtForSelfTest;
    // The smoke's lent throw weight (with its lent throwing dagger, ARC-COMBAT-017); null in play.
    private int? _lentThrowWeightForSelfTest;
    // The strikes and ends so far, for the smoke: the shot, what ended it and the struck entity.
    private readonly List<(SessionShot Shot, ProjectileStrikeKind Strike, string? Struck)> _projectileEndsForSelfTest = [];
    // The thrown items' landings so far, for the smoke: the item, the tile and the session's answer.
    private readonly List<(string Item, CellCoord Tile, GameSessionDisposition Disposition)> _thrownLandingsForSelfTest = [];
    // ARC-COMBAT-019: the boomerangs' turns so far, for the smoke: the shot, the tile it turned on and the tile it flies back to.
    private readonly List<(SessionShot Shot, CellCoord From, CellCoord To)> _boomerangTurnsForSelfTest = [];
    // ARC-DEV-140: the shots whose flight goal ended before its first tick (a throw at the thrower's own tile, a refused line), for the smoke: the goal's own end fires no item event (GT53).
    private int _projectilesUnlaunchedForSelfTest;
    // ARC-DEV-140 (B14): the item events a shot's end fired so far, for the smoke: the shot, the item, the tile, the item spells the executor fired (spell numbers) and how many of them started.
    private readonly List<(SessionShot Shot, string Item, CellCoord Tile, IReadOnlyList<int> Spells, int Started)> _shotEndItemEventsForSelfTest = [];

    /// <summary>The projectiles in flight now (the shown session's host, in the order they left); none without one (ARC-DEV-240: a host of a replaced session shows none).</summary>
    private IReadOnlyList<AreaProjectile> _projectiles => ShownProjectileHost?.Projectiles ?? [];

    /// <summary>The projectile host as far as it belongs to the shown session; null when there is none or when it still belongs to a replaced session (a quick load, a carried session: its session is disposed). The goal-step router goes through this; <see cref="ProjectileHost"/> replaces the stale host.</summary>
    private AreaProjectileHost? ShownProjectileHost => _projectileHost is { } host && ReferenceEquals(host.Session, _areaSession) ? host : null;

    /// <summary>The projectile host of the shown session: a new session (a reload, a carried session) gives up the old host's projectiles first.</summary>
    private AreaProjectileHost ProjectileHost()
    {
        if (_projectileHost is { } host && ReferenceEquals(host.Session, _areaSession)) return host;
        _projectileHost?.LoseAll("the session changed");
        _projectileDrawnSteps = 0;
        _projectileHost = new(_areaSession!, new ProjectileViewer(this));
        // ARC-DEV-288: a restored session's projectiles in flight join the new host before their runs step.
        AdoptRestoredProjectiles(_projectileHost);
        return _projectileHost;
    }

    /// <summary>ARC-DEV-288: the projectile host the goal-step router hands a projectile's run to: the shown one, or for a restored session that holds projectiles in flight the new host
    /// that adopts them (<see cref="ProjectileHost"/>), once the shown map is the session's (after the load's other rebinds, so the launch's rows resolve against them); null when there is no
    /// session, its map is not shown yet or nothing flies.</summary>
    private AreaProjectileHost? StepProjectileHost() =>
        ShownProjectileHost ?? (_areaSession is { } owner && owner.QueryActor().Map.RuntimeMap == CurrentAreaMap && owner.QueryProjectiles().Count > 0 ? ProjectileHost() : null);

    /// <summary>
    /// ARC-DEV-288 (saved-runs.md §3.5): the projectiles the restored session brought (its objects, runs and shots came back with the save) are rebuilt for the host from their saved
    /// flights: the rows of the launch are resolved again on the shown map by entity (the hero, an NPC or an item of the map, an attackable object), the aimed critter or object
    /// again from that row. A flight of another map, or one whose rows are not on the shown map, is given up as a flight that lost its map (its run killed, its shot ended).
    /// </summary>
    private void AdoptRestoredProjectiles(AreaProjectileHost host)
    {
        var owner = host.Session;
        foreach (var saved in owner.QueryProjectiles())
        {
            if (saved.Flight is not { } flight || host.Projectiles.Any(p => p.Entity == saved.Entity)) continue;
            var launch = flight.Launch;
            // A projectile whose run is gone (dropped at the restore) would hang in the air: it is given up below like one whose rows are gone.
            var running = owner.QueryGoals().RunsOf(saved.Entity).Any(run => !run.Done);
            var target = launch.Target is { } entity ? ProjectileRowOf(owner, entity) ?? (owner.QueryTrapShot(flight.Shot) is not null ? _spellCastMap?.Row(entity) : null) : null;
            AreaObjectRecord? shooter = null, thrower = null;
            var resolved = running && target is not null && saved.Map.RuntimeMap == CurrentAreaMap
                && (launch.Shooter is not { } s || (shooter = ProjectileRowOf(owner, s)) is not null || owner.QueryTrapShot(flight.Shot) is not null)
                && (launch.Thrower is not { } t || (thrower = ProjectileRowOf(owner, t)) is not null);
            // ARC-TECHNOLOGY-1635: a trap's shot flies under the shooter object's row when the shown map has it, else under the stand-in (the cast ids are not entities of the inventory).
            if (resolved && shooter is null && owner.QueryTrapShot(flight.Shot) is not null) shooter = _spellCastMap is { } cast && launch.Shooter is { } trapShooter ? cast.Row(trapShooter) ?? TrapShooterStandIn : TrapShooterStandIn;
            object? critter = resolved && launch.Critter && _combatHost?.Target(owner, target!.Key) is { Build.Combat: { } combat } npc ? new AttackGoal(npc, combat, null) : null;
            var obj = resolved && launch.Object ? ObjectTargets()?.Target(owner, target!.Key, AreaTrapped) : null;
            resolved &= (!launch.Critter || critter is not null) && (!launch.Object || obj is not null);
            if (resolved && AreaProjectile.Rebuild(saved, owner, target!, shooter, thrower, critter, obj) is { } projectile && host.Adopt(projectile))
            {
                GD.Print($"ARC-DEV-288 projectile-restored shot={flight.Shot.Id} entity={saved.Entity} target={launch.Target} at={saved.Cell.X},{saved.Cell.Y}"
                    + $" to={flight.Destination.X},{flight.Destination.Y} steps={flight.Steps} hit={flight.State.Hit}");
                continue;
            }
            // The given-up flight needs a projectile of its own to end: the saved one with the hero's row standing in for the rows that did not resolve.
            if (AreaProjectile.Rebuild(saved, owner, target ?? HeroRow, shooter, thrower, null, null) is { } lost) host.GiveUpRestored(lost, running ? "its launch's rows are not on the shown map" : "its run did not survive the restore");
            GD.Print($"ARC-DEV-288 projectile-not-restored shot={flight.Shot.Id} entity={saved.Entity} target={launch.Target ?? "none"}");
        }
        SyncAreaProjectileBodies();
    }

    /// <summary>ARC-DEV-288: the entity a save keeps for a projectile's row: the hero's <c>pc</c>, an NPC's or item's of the shown map, an attackable object's; null for a row the session
    /// names by no entity (a wall).</summary>
    private string? ProjectileRowEntity(AreaObjectRecord row)
    {
        if (row.Key == HeroRow.Key) return Arcanum.Simulation.Ai.AiSchedulerRules.Pc;
        if (_inventoryHost?.Maps.FirstOrDefault(m => m.RuntimeMap == CurrentAreaMap) is { } map && map.TryGetEntity(row.Key, out var entity)) return entity;
        return ObjectTargets()?.EntityOfKey(row.Key);
    }

    // The row of the shown map an entity the save kept names (ProjectileRowEntity's inverse), or null.
    private AreaObjectRecord? ProjectileRowOf(GameSessionOwner owner, string entity)
    {
        if (entity == Arcanum.Simulation.Ai.AiSchedulerRules.Pc) return HeroRow;
        if (_inventoryHost?.Maps.FirstOrDefault(m => m.RuntimeMap == CurrentAreaMap) is { } map && map.TryGetRow(entity, out var row)) return row;
        return ObjectTargets() is { } objects && objects.KeyOfEntity(entity) is { } key ? objects.Target(owner, key, AreaTrapped)?.Row : null;
    }

    /// <summary>The wielded weapon's projectile art (field 127; null for none, -1): the smoke's lent art, else the wielded kit item's.</summary>
    private int? HeroMissileArt()
    {
        if (_lentMissileArtForSelfTest is { } lent) return lent;
        if (WieldedKitNumber() is not { } number || _areaKitMissileArt is not { } kit || number > kit.Count) return null;
        return kit[number - 1] is { } art && art != CombatWeaponFields.NoMissileArt ? art : null;
    }

    /// <summary>ARC-COMBAT-017: the throw of the wielded weapon when it "default throws" (weapon flag 0x200, ledger RH2), with its stored
    /// weight: the smoke's lent one, else the wielded kit item's; null for a weapon that stays in the hand.</summary>
    private SessionThrow? HeroThrow(AttackGoal goal)
    {
        var weight = _lentThrowWeightForSelfTest ?? (WieldedKitNumber() is { } number && _areaKitThrowWeight is { } kit && number <= kit.Count ? kit[number - 1] : null);
        // ARC-COMBAT-018: the throw's target location is the goal target's tile, from which a missed throw scatters (TW1, RH2).
        return weight is { } w ? new(w) { Target = GoalTile(goal) } : null;
    }

    // The goal target's tile (its map data's cell; NPCs do not move here).
    private static CellCoord GoalTile(AttackGoal goal) => new(checked((int)goal.Row.WorldX!.Value), checked((int)goal.Row.WorldY!.Value));

    // The wielded kit item's number (1-based), or null when the hero wields nothing from its kit.
    private int? WieldedKitNumber() =>
        _areaSession?.QueryEquipment().Worn.FirstOrDefault(w => w.Slot == Arcanum.Simulation.Equipment.EquipmentSlot.Weapon) is { } wielded
        && wielded.Entity.StartsWith("kit:", StringComparison.Ordinal)
        && int.TryParse(wielded.Entity.AsSpan(4), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number)
        && number >= 1 ? number : null;

    /// <summary>ARC-COMBAT-016 (ledger PF1, RH3): a shot on the ranged path flies as a projectile when the weapon has a projectile art:
    /// that art and the weapon's range, read before the shot (a critical miss may drop or break the weapon); null when it hits at once
    /// on the action frame.</summary>
    private (int Art, int Range)? ShotFlight(SessionHeroAttack? hero) =>
        RangedPath(hero) && HeroMissileArt() is { } art ? (art, HeroReach(hero)) : null;

    /// <summary>
    /// ARC-COMBAT-016: the session's shot leaves the hero's tile for the goal target's tile (the destination stays the target's tile
    /// after a line-of-fire redirect, ledger PF2) with the art faced in its screen direction (0x4B6A00, 0x4B6B10); a shot that cannot
    /// fly (no line between the tiles) ends at once. ARC-COMBAT-017: a thrown item flies to its thrown range (PF9) and lands where the
    /// projectile ends. ARC-COMBAT-018: a missed throw flies to its <paramref name="scatter"/> tile without a target, free from the
    /// start, and lands where it ends.
    /// </summary>
    private void LaunchProjectile(AttackGoal goal, SessionShot shot, (int Art, int Range) weapon, AttackResolution resolution, AreaObjectRecord target,
        AttackGoal? critter, AreaObjectAttackTarget? obj, CellCoord? scatter) =>
        FlyShot(shot, scatter ?? GoalTile(goal), unchecked((uint)weapon.Art), false, weapon.Range,
            resolution.Outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit, target, critter, obj, scatter is not null);

    /// <summary>
    /// The flight of one shot from the hero's cell to <paramref name="destination"/> (<see cref="LaunchProjectile"/>'s; ARC-UI-063: the
    /// inventory throw's too). <paramref name="art"/> is faced in the flight's direction (an item art stays as it is, PF14: the rotation
    /// setter leaves it); an art without a facing rule (a kit item's art, <paramref name="unfacedArt"/>) flies unfaced. ARC-UI-063:
    /// <paramref name="sizeArc"/> draws a thrown item that is not a weapon on PF11's size arc. A <paramref name="targetless"/> shot (a
    /// scatter, or a throw at a location) flies free from the start and ends on leaving its destination. ARC-DEV-140: the projectile is
    /// a created object whose run, goal 36, builds the line itself; a shot whose destination is its own tile pops at once (no flight).
    /// </summary>
    private void FlyShot(SessionShot shot, CellCoord destination, uint? art, bool unfacedArt, int range, bool hit, AreaObjectRecord target,
        AttackGoal? critter, AreaObjectAttackTarget? obj, bool targetless, bool sizeArc = false)
    {
        var owner = _areaSession!;
        var session = (AreaGridSession)_worldActor.Session!;
        var hero = session.Chart.ToWorld(session.State.CommittedCell);
        var start = new CellCoord(checked((int)hero.X), checked((int)hero.Y));
        var thrown = owner.QueryThrownShot(shot);
        var direction = ProjectileRules.Direction(start, destination);
        var faced = art is { } raw ? ProjectileRules.FacedArt(raw, direction) ?? (unfacedArt ? raw : null) : null;
        var projectile = new AreaProjectile
        {
            Shot = shot, Owner = owner, Map = CurrentAreaMap, Target = target, Critter = critter, Object = obj, Destination = destination, Art = faced,
            Flight = new(start, start, [(0, 0)], 1, start, 0, 0, 0), State = new(hit, targetless, thrown?.Range ?? range), Thrown = thrown,
            Targetless = targetless, Returns = owner.QueryShotReturns(shot), SizeArc = sizeArc,
            Launched = new(ProjectileRowEntity(target), critter is not null, critter is null && obj is not null, null, null),
        };
        if (!ProjectileHost().Launch(projectile, start, critter?.Entity ?? obj?.Entity))
        {
            GD.Print($"ARC-DEV-140 projectile-not-launched shot={shot.Id}");
            return;
        }
        _attackNotice = $"the shot flies ({(hit ? "it will hit" : !targetless ? "it missed" : "it missed and scatters")})";
        GD.Print($"ARC-COMBAT-016 projectile-launch shot={shot.Id} entity={projectile.Entity} target={target.Key.Kind}:{target.Key.SourceIndex} hit={hit} from={start.X},{start.Y}"
            + $" to={destination.X},{destination.Y} direction={direction} art={(faced is { } a ? $"0x{a:X8}" : "none")}"
            + (thrown is { } t ? $" thrown={t.Item} range={t.Range}" : "") + (targetless ? " scattered" : "")
            + (owner.QueryShotReturns(shot) ? $" boomerang range={range}" : ""));
        SyncAreaProjectileBodies();
    }

    /// <summary>
    /// ARC-DEV-282 (PF1, PF2): the flight of an NPC's arrow or bullet from the shooter's tile to its target's (the hero's tile, or an NPC's): the shot is the session's
    /// (<see cref="GameSessionOwner.QueryNpcShot"/>: the check decided at the shot, the weapon's range), the art the wielded weapon's projectile art faced in the flight's direction,
    /// the strike the AI host's (<see cref="AreaAiHost.LandShot"/>): the target takes the carried hit, a miss flies on and may strike another living critter or an obstruction (PF3).
    /// False when the shooter, the target or the shot has no row on the shown map or the flight did not launch (the AI host then strikes the aimed target at once).
    /// </summary>
    private long? _npcShotLaunching;

    private bool AiLaunchShot(string shooter, string target, SessionShot shot, CellCoord from, CellCoord destination)
    {
        if (_inventoryHost?.Maps.FirstOrDefault(m => m.RuntimeMap == CurrentAreaMap) is not { } map) return false;
        var targetRow = HeroRow;
        if (target != Arcanum.Simulation.Ai.AiSchedulerRules.Pc && !map.TryGetRow(target, out targetRow!)) return false;
        return AiFlyShot(shooter, target, targetRow, null, shot, from, destination);
    }

    /// <summary>
    /// ARC-COMBAT-1140 (RA4, PF2): the flight of an NPC's arrow on the door, window, container, scenery, trap or wall that stopped its line of fire (<see cref="AreaAiScene.LaunchShotAtObject"/>): as
    /// <see cref="AiLaunchShot"/>, with the blocker as the shot's target (the shot was decided a hit on it) and the aimed target's tile as the destination; it strikes the blocker where it crosses its tile.
    /// </summary>
    private bool AiLaunchShotAtObject(string shooter, AreaObjectRecord blocker, SessionShot shot, CellCoord from, CellCoord destination) =>
        AiFlyShot(shooter, blocker.Type == 0 ? $"wall:{blocker.Key.SourceIndex}" : ObjectTargets()?.EntityOfKey(blocker.Key) ?? $"{blocker.Key.Kind}:{blocker.Key.SourceIndex}", blocker,
            blocker.Type == 0 || _areaSession is not { } owner ? null : ObjectTargets()?.Target(owner, blocker.Key, AreaTrapped), shot, from, destination);

    private bool AiFlyShot(string shooter, string target, AreaObjectRecord targetRow, AreaObjectAttackTarget? obj, SessionShot shot, CellCoord from, CellCoord destination)
    {
        if (_areaSession is not { } owner || _inventoryHost?.Maps.FirstOrDefault(m => m.RuntimeMap == CurrentAreaMap) is not { } map || !map.TryGetRow(shooter, out var shooterRow)
            || owner.QueryNpcShot(shot) is not { } facts) return false;
        var art = _combatHost?.WornItems(owner, CurrentAreaMap, shooter)?.GetValueOrDefault(Arcanum.Simulation.Equipment.EquipmentSlot.Weapon)?.MissileArt;
        var direction = ProjectileRules.Direction(from, destination);
        var faced = art is { } raw ? ProjectileRules.FacedArt(raw, direction) : null;
        var atObject = targetRow.Type != 15 && targetRow.Type != 16;
        var projectile = new AreaProjectile
        {
            Shot = shot, Owner = owner, Map = CurrentAreaMap, Target = targetRow, Object = obj, Shooter = shooterRow, Destination = destination, Art = faced,
            Flight = new(from, from, [(0, 0)], 1, from, 0, 0, 0), State = new(facts.Hit, false, facts.Range),
            Launched = new(atObject && obj is null ? null : target, false, obj is not null, shooter, null),
        };
        _npcShotLaunching = shot.Id;
        bool launched;
        try { launched = ProjectileHost().Launch(projectile, from, target == Arcanum.Simulation.Ai.AiSchedulerRules.Pc || atObject && obj is null ? null : target); }
        finally { _npcShotLaunching = null; }
        if (!launched)
        {
            GD.Print($"ARC-DEV-282 npc-shot-not-launched shot={shot.Id} shooter={shooter}");
            return false;
        }
        _npcShotLaunchesForSelfTest.Add((shot.Id, facts.Hit, shooter));
        GD.Print($"ARC-DEV-282 npc-shot-launch shot={shot.Id} entity={projectile.Entity} shooter={shooter} target={target} hit={facts.Hit} range={facts.Range} from={from.X},{from.Y}"
            + $" to={destination.X},{destination.Y} direction={direction} art={(faced is { } a ? $"0x{a:X8}" : "none")}");
        SyncAreaProjectileBodies();
        return true;
    }

    /// <summary>
    /// ARC-DEV-258 (MIG13 S14n, GT63): the flight of a generic item (a grenade) an NPC threw: from the thrower's tile to the throw's location, free from the start (no target, a stray strike
    /// on a critter on the way ends it there), drawn on the size arc of a thrown item that is no weapon, with the item's own art; it lands where it ends and the item's event (its spell) goes off there.
    /// ARC-COMBAT-1717 (PF1, PF6, PF7, PF10, TW3): a thrown weapon whose check the session decided at the throw (<see cref="GameSessionOwner.QueryNpcShot"/>) flies as the NPC's arrow does: to the struck critter's
    /// tile when it hit (the AI host strikes where the flight meets it, <see cref="AreaAiHost.LandShot"/>), free to the scatter tile when it missed (a stray critter or object on the way takes a forced hit), drawn with the
    /// item's own art unfaced, not on the size arc (the parent item is a weapon); the item lands where it ends and its event goes off there. False when the thrower has no row on the shown map, the item has no row and
    /// is no item the session made (ARC-DEV-315: a made item's art is its prototype's), the struck critter has none, or the flight did not launch (the AI host then lands the item at once, a decided hit striking first).
    /// </summary>
    private bool AiLaunchThrown(string thrower, SessionShot shot, CellCoord from, CellCoord destination)
    {
        if (_areaSession is not { } owner || _inventoryHost?.Maps.FirstOrDefault(m => m.RuntimeMap == CurrentAreaMap) is not { } map || !map.TryGetRow(thrower, out var row)
            || owner.QueryThrownShot(shot) is not { } thrown) return false;
        var direction = ProjectileRules.Direction(from, destination);
        // ARC-DEV-315: an item the session made has no row; it is a copy of its prototype, so its art is the prototype's current art.
        uint? art = map.TryGetRow(thrown.Item, out var itemRow) ? (Number(itemRow, CurrentArtSlot) is { } own ? own : null)
            : AreaMadeItems.Art(owner, thrown.Item, prototype => PrototypeItems()?.Item(prototype)?.Art, thrower);
        // ARC-SCRIPT-1779: the item's own art is the ground art a script's change art number wrote, if any.
        art = owner.QueryObjectArt(new SessionMapId(CurrentAreaMap), thrown.Item) ?? art;
        // ARC-COMBAT-1717: a thrown weapon carries the check decided at the throw; the hit's target is the struck critter (the hero, or an NPC of the shown map).
        var decided = owner.QueryNpcShot(shot);
        var weapon = decided is not null;
        // An unchecked flight of a weapon (a throw at a location) is no size arc either: its parent item is a weapon (PF11).
        var isWeapon = weapon || map.TryGetRow(thrown.Item, out var parent) && parent.Type == 5;
        var hit = decided is { Hit: true };
        var aimedId = hit ? decided!.Value.Target == SessionNpcShotFacts.HeroTarget ? Arcanum.Simulation.Ai.AiSchedulerRules.Pc : decided.Value.Target : null;
        var aimed = row;
        if (aimedId is not null && aimedId != Arcanum.Simulation.Ai.AiSchedulerRules.Pc && !map.TryGetRow(aimedId, out aimed!)) return false;
        if (aimedId == Arcanum.Simulation.Ai.AiSchedulerRules.Pc) aimed = HeroRow;
        // PF14: the weapon's projectile art is a scenery art faced in the flight's direction (the hero's thrown dagger flies so); without one the item's own art flies as it lies.
        if (weapon && decided!.Value.ThrownArt is { } missile) art = ProjectileRules.FacedArt(missile, direction) ?? art;
        var projectile = new AreaProjectile
        {
            Shot = shot, Owner = owner, Map = CurrentAreaMap, Target = aimed, Thrower = row, Shooter = weapon ? row : null, Destination = destination, Art = art,
            Flight = new(from, from, [(0, 0)], 1, from, 0, 0, 0), State = new(hit, !hit, thrown.Range), Thrown = thrown, Targetless = !hit, SizeArc = !isWeapon,
            Launched = new(aimedId ?? thrower, false, false, weapon ? thrower : null, thrower),
        };
        // A weapon's refused launch leaves its shot to the AI host (the hit strikes at once, the item lands); a generic item's ends it (the item stays on the thrower's cell).
        _npcShotLaunching = weapon ? shot.Id : null;
        bool launched;
        try { launched = ProjectileHost().Launch(projectile, from, hit && aimedId != Arcanum.Simulation.Ai.AiSchedulerRules.Pc ? aimedId : null); }
        finally { _npcShotLaunching = null; }
        if (!launched)
        {
            GD.Print($"ARC-DEV-258 npc-throw-not-launched shot={shot.Id} thrower={thrower}");
            return false;
        }
        GD.Print($"ARC-DEV-258 npc-throw-launch shot={shot.Id} thrower={thrower} item={thrown.Item} range={thrown.Range} from={from.X},{from.Y} to={destination.X},{destination.Y} direction={direction}"
            + $" art={(art is { } a ? $"0x{a:X8}" : "none")}" + (weapon ? $" weapon check={(hit ? "hit " + aimedId : "miss")}" : ""));
        _npcThrowLaunchesForSelfTest.Add((shot.Id, thrown.Item, hit, thrower, from, destination, owner.QueryItemInFlight(thrown.Item),
            owner.QueryMapInventory(new SessionMapId(CurrentAreaMap))?.Ground.FirstOrDefault(ground => ground.Item.Entity == thrown.Item)?.Cell));
        SyncAreaProjectileBodies();
        return true;
    }

    /// <summary>
    /// ARC-COMBAT-016, per frame: the projectiles' runs step in the frame step (the router hands them to the host: a tick of steps every 35 ms of the
    /// animation clock, so the flight stops with the Esc pause); the draw follows a tick.
    /// </summary>
    private void ProcessAreaProjectiles()
    {
        if (_projectileHost is not { } host || host.Steps == _projectileDrawnSteps) return;
        _projectileDrawnSteps = host.Steps;
        SyncAreaProjectileBodies();
        PublishWorldSelection();
    }

    // The end nodes of a projectile's run the session ended by itself (a map's close): the shown projectile host's (ARC-DEV-240).
    // ARC-DEV-288: a restored session's projectile whose run the session ended before its first step (a refused step at the restore, a map's close) is adopted here, so its shot ends too.
    private void ProjectileRunEnded(GoalRunEnd end) => StepProjectileHost()?.EndNodes(end);

    /// <summary>The viewer's side of the projectile host: the shown map's cells, the tile left (0x4B2870) and the flight goal's own end.</summary>
    private sealed class ProjectileViewer(TerrainViewerApp app) : IAreaProjectileViewer
    {
        public Func<CellCoord, bool>? LineMap(AreaProjectile projectile)
        {
            var owner = app._areaSession;
            var lineMap = owner is null || !app.AttackMapActive ? null : app.AreaLineOfFireNow();
            app._projectileLines = lineMap;
            return owner is null || lineMap is null || projectile.Map != app.CurrentAreaMap || !ReferenceEquals(projectile.Owner, owner) ? null : lineMap.Contains;
        }

        public ProjectileCrossResult Crossed(AreaProjectile projectile, ProjectileCrossing crossing) =>
            app.CrossTile(projectile, crossing, projectile.Owner, app._projectileLines!);

        public void GoalEnded(AreaProjectile projectile, bool land, string why) => app.ProjectileGoalEnded(projectile, land, why);
    }

    /// <summary>
    /// The flight goal's own end (0x42CAC0 → 0x4B2650 → 0x4B2690 without a target, a context or the item event, GT53, GT63): a point off the map, a shot
    /// whose destination is its own tile, a refused line. A thrown item is placed on the projectile's tile; a boomerang gets its second end (BR3) and takes
    /// its thrower's attack again. A flight given up (<paramref name="land"/> false: the map or the session changed) only ends the shot.
    /// </summary>
    private void ProjectileGoalEnded(AreaProjectile projectile, bool land, string why)
    {
        // ARC-DEV-282: a launch the host refused leaves the NPC's shot in the session, so the AI host can strike the aimed target at once.
        if (!land && _npcShotLaunching == projectile.Shot.Id) { Ended(projectile, ProjectileStrikeKind.None, null, why); return; }
        if (!land) { EndProjectile(projectile, ProjectileStrikeKind.None, null, why); return; }
        // The original's flight of a line without a step lands the item where it lies, on the hero's cell (a missed throw may scatter back onto it, ARC-COMBAT-018).
        if (projectile.Ticks == 0) _projectilesUnlaunchedForSelfTest++;
        if (projectile.Ticks == 0) GD.Print($"ARC-COMBAT-016 projectile-unlaunched shot={projectile.Shot.Id} at={projectile.Flight.Tile.X},{projectile.Flight.Tile.Y} to={projectile.Destination}");
        var offMap = projectile.Ticks > 0;
        EndProjectile(projectile, projectile.Targetless && !offMap ? ProjectileStrikeKind.DestinationEnd : ProjectileStrikeKind.None, null,
            why, land: true);
        if (projectile.Returns) BoomerangReturned(projectile);
    }

    /// <summary>The tile left, as 0x4B2870 handles it: what the flight did there (it flies on, it ended, or a boomerang turned back and flies its new line).</summary>
    private ProjectileCrossResult CrossTile(AreaProjectile projectile, ProjectileCrossing crossing, GameSessionOwner owner, AreaLineOfFire lineMap)
    {
        var map = new ProjectileMap(this, owner, projectile, lineMap);
        // ARC-UI-063 (PF11): the size arc is set at the start of every crossing, from the tile left (0x4B2870).
        if (projectile.SizeArc)
            projectile.Scale = ProjectileRules.ThrownScale(ProjectileRules.Distance(projectile.Flight.Start, projectile.Destination),
                ProjectileRules.Distance(crossing.Left, projectile.Destination));
        var crossed = ProjectileRules.Cross(projectile.State, crossing, projectile.Destination, projectile.Targetless ? null : projectile.Target, projectile.Thrower ?? projectile.Shooter ?? HeroRow, map,
            (min, max) => owner.Roll("combat.StrayStrike", min, max).Roll?.Value ?? throw new InvalidOperationException("AREA_STRAY_ROLL_REFUSED"));
        projectile.State = crossed.Shot;
        if (!crossed.Ends) return ProjectileCrossResult.Flies;
        // ARC-TECHNOLOGY-1635 (TT15): a native trap's shot strikes through the cast host and ends there (no boomerang, no thrown item, no NPC shooter).
        if (owner.QueryTrapShot(projectile.Shot) is not null) return TrapShotEnd(projectile, crossed.Strike, crossed.Struck);
        // ARC-COMBAT-019: a returning boomerang leaving the tile it flies back to is back in the hero's hand.
        if (crossed.Strike == ProjectileStrikeKind.DestinationEnd && projectile.State.Returning)
        {
            EndProjectile(projectile, crossed.Strike, null, "back in the hero's hand", fireEnd: true);
            BoomerangReturned(projectile);
            return ProjectileCrossResult.Ended;
        }
        // ARC-COMBAT-019: a boomerang's first end without a strike turns it back (0x4B2690 with flag 0x1000).
        if (crossed.Strike is ProjectileStrikeKind.RangeEnd or ProjectileStrikeKind.DestinationEnd && projectile.Returns)
        {
            var events = ShotEndEvents(projectile);
            var scripts = projectile.Owner.QueryShotEndScripts(projectile.Shot);
            Ended(projectile, crossed.Strike, null, "turned back");
            var turned = TurnBack(projectile);
            // ARC-COMBAT-1719: the boomerang's first end turns it back (0x4B2690), then the weapon's hit (7) or miss (8) script runs, then the item event.
            RunShotEndScripts(projectile, scripts, crossed.Strike == ProjectileStrikeKind.DestinationEnd);
            FireShotEnd(projectile, events);
            return turned ? ProjectileCrossResult.Turned : ProjectileCrossResult.Ended;
        }
        if (crossed.Strike == ProjectileStrikeKind.RangeEnd) { EndProjectile(projectile, crossed.Strike, null, "beyond its range", land: true, fireEnd: true); return ProjectileCrossResult.Ended; }
        // ARC-COMBAT-018: a shot without a target ends on leaving its destination (a missed throw lands on its scattered tile).
        if (crossed.Strike == ProjectileStrikeKind.DestinationEnd) { EndProjectile(projectile, crossed.Strike, null, "at its destination", land: true, fireEnd: true); return ProjectileCrossResult.Ended; }
        return Strike(projectile, crossed.Strike, crossed.Struck!);
    }

    /// <summary>
    /// ARC-COMBAT-019 (0x4B2690 with attack flag 0x1000, 0x435A00): the boomerang's projectile turns back at its first end, from the
    /// tile it is on (the one being left) to the hero's tile now, and flies back striking nothing (flag 0x2000); its facing stays. The
    /// session's shot turns with it (a strike turned it already). On the hero's own tile it is back at once. ARC-DEV-140: the goal takes the
    /// hero's tile as its destination and builds its line anew (BR4: the tick returns <see cref="ProjectileTickResult.Turned"/>). True: it flies on.
    /// </summary>
    private bool TurnBack(AreaProjectile projectile)
    {
        var owner = projectile.Owner;
        owner.ReturnShot(projectile.Shot);
        if (_worldActor.Session is not AreaGridSession session) { EndProjectile(projectile, ProjectileStrikeKind.None, null, "no actor to return to"); return false; }
        var hero = HeroCellNow(session);
        var from = projectile.Flight.Tile;
        var (flight, shot) = ProjectileRules.TurnBack(projectile.Flight, projectile.State, hero);
        projectile.State = shot;
        _boomerangTurnsForSelfTest.Add((projectile.Shot, from, hero));
        GD.Print($"ARC-COMBAT-019 boomerang-turn shot={projectile.Shot.Id} from={from.X},{from.Y} to={hero.X},{hero.Y} line={flight is not null}");
        if (flight is null)
        {
            EndProjectile(projectile, ProjectileStrikeKind.DestinationEnd, null, "turned on the hero's tile: back at once");
            BoomerangReturned(projectile);
            return false;
        }
        projectile.Flight = flight;
        projectile.Destination = hero;
        return true;
    }

    /// <summary>
    /// ARC-COMBAT-019 (the second end of 0x4B2690 → 0x4A9AD0 with the hero and the goal's target object): back in the hand, the hero
    /// takes up combat mode when it left it (0x4B3F50) and, in real time with the auto-attack setting (0x4B8230: the option, off in a turn-based
    /// round), attacks again what the shot was fired at (the goal's target slot: the clicked target, or the line of fire's blocker,
    /// not a critter a stray strike hit on the way) when no attack goal runs (0x434AE0 → 0x434B00, not aimed). A wall or a companion
    /// is not attacked again; the original's goal-slot tests (0x423300, 0x423470) and the turn-based round's join (0x4B6E70) are not
    /// modelled.
    /// </summary>
    private void BoomerangReturned(AreaProjectile projectile)
    {
        var owner = projectile.Owner;
        if (!ReferenceEquals(owner, _areaSession) || projectile.Map != CurrentAreaMap || owner.QueryHeroLife() is not { Status.IsActive: true }) return;
        if (!HeroInCombatMode) ToggleCombatMode();
        if (!HeroInCombatMode || owner.QueryCombatRound() is not null || !_gameSettings.AutoAttack || _attack is not null
            || IsAreaCompanion(projectile.Target.Key)) return;
        AttackGoal? goal = _combatHost!.Target(owner, projectile.Target.Key) is { Dead: false, Build.Combat: { } combat } npc
            ? new(npc, combat, null)
            : projectile.Object is not null && ObjectTargets()?.Target(owner, projectile.Target.Key, AreaTrapped) is { Refusal: null } obj ? new(obj, null) : null;
        GD.Print($"ARC-COMBAT-019 boomerang-back shot={projectile.Shot.Id} reattack={goal?.Entity ?? "none"}");
        if (goal is null) return;
        _attack = goal;
        StartAttackGoal(goal);
    }

    // The attacker as the rules compare it (the hero has no map row; nothing on a tile is this object).
    private static readonly AreaObjectRecord HeroRow = new(new("hero", 0, AreaSourceKind.Mobile, null, 0, 0, "hero", 0), 0, 15, 0, "", "", "", "",
        AreaScope.Core, null, null, null, Arcanum.Data.Area.AreaDisposition.BodyEligibleConditionally, [], [], true, true, true);

    // ARC-TECHNOLOGY-1635: the attacker of a trap's shot whose shooter object has no row of the shown map (a wall of the data, or a shot restored from a save): a row of no tile and no critter, so the stray rule
    // meets the hero and the companions as it meets them for any shooter that is no critter.
    private static readonly AreaObjectRecord TrapShooterStandIn = HeroRow with { Key = new("trap-shooter", 0, AreaSourceKind.Mobile, null, 0, 0, "trap-shooter", 0), Type = 1 };

    /// <summary>The struck object takes the shot through the session: an NPC with combat values (<see cref="GameSessionOwner.LandShot"/>),
    /// a door, window, container, scenery or trap (<see cref="GameSessionOwner.LandShotOnObject"/>), a wall; a companion or an NPC
    /// without combat values is not ready (the shot ends without a strike). ARC-COMBAT-017: a thrown item then lands on the tile left
    /// (the original lands it just before the hit, PF8; nothing of the hit reads it). ARC-DEV-140 (B14): the projectile's item then
    /// attacks the struck object's tile (0x4CBDB0, PF8, PF10): a grenade's own item spell goes off there. A boomerang turns back instead
    /// of ending.</summary>
    private ProjectileCrossResult Strike(AreaProjectile projectile, ProjectileStrikeKind kind, AreaObjectRecord struck)
    {
        var owner = projectile.Owner;
        var map = CurrentAreaMap;
        // The item event of the strike's end is asked while the shot is in flight (a struck thrown shot waits for its landing and asks nothing more).
        var events = ShotEndEvents(projectile);
        // ARC-DEV-258 (MIG13 S14n): an NPC's thrown item ends where it meets something: it lands on the tile left and its own event goes off there (GT63); the item's hit on the struck object is not ready.
        if (projectile.Thrower is not null && projectile.Shooter is null)
        {
            Ended(projectile, kind, struck.Key.Kind + ":" + struck.Key.SourceIndex, "an NPC's thrown item met an object: its hit is not ready");
            EndOnTile(projectile);
            FireShotEnd(projectile, events);
            return ProjectileCrossResult.Ended;
        }
        // ARC-DEV-282 (PF2, PF3): an NPC's arrow strikes through the AI host: the shot's target takes the carried hit, another critter it met on the way a forced hit; ARC-COMBAT-1140: an object or a wall
        // (a door, a container, scenery, a trap) it meets takes a forced hit too; a companion or an object the viewer has no target for (an item, a critter without the AI host) ends it.
        if (projectile.Shooter is { } shooter)
        {
            var struckHero = struck.Key == HeroRow.Key;
            var struckCritter = struckHero || struck.Type is 15 or 16;
            var struckId = struckHero ? Arcanum.Simulation.Ai.AiSchedulerRules.Pc : AreaConversationState.EntityId(struck);
            var shooterId = AreaConversationState.EntityId(shooter);
            _projectileHost?.Finish(projectile);
            // ARC-COMBAT-1717 (PF8, PF10): a thrown weapon lands on the tile left after its strike (the shot stays in the session for the landing) and its item event goes off there.
            var thrownWeapon = projectile.Thrown is not null;
            if (AreaAiHostNow() is not { } ai)
            {
                if (!thrownWeapon) owner.EndShot(projectile.Shot);
                Ended(projectile, kind, null, $"an NPC's shot met {(struckCritter ? "a critter" : $"an object of type {struck.Type}")} without the AI host: no strike");
                if (thrownWeapon) { EndOnTile(projectile); FireShotEnd(projectile, events); }
                return ProjectileCrossResult.Ended;
            }
            // ARC-COMBAT-1140 (PF8, RH5): an object or a wall the arrow meets takes it as a forced hit (the damage step of an object, its break and the trap and BUST script of it).
            if (!struckCritter)
            {
                var onObject = NpcShotOnObject(projectile, kind, struck, ai, shooterId);
                if (thrownWeapon) { EndOnTile(projectile); FireShotEnd(projectile, events); }
                return onObject;
            }
            var landed = ai.LandShot(shooterId, projectile.Shot, struckId);
            _npcShotStrikesForSelfTest.Add((projectile.Shot.Id, struckId, landed?.Disposition ?? GameSessionDisposition.Rejected, landed?.Life?.HitPointDamage));
            Ended(projectile, kind, struckId, $"an NPC's shot: disposition={landed?.Disposition} failure={landed?.Failure} outcome={landed?.Resolution?.Outcome} damage={landed?.Life?.HitPointDamage}");
            if (thrownWeapon) { EndOnTile(projectile); FireShotEnd(projectile, events); }
            return ProjectileCrossResult.Ended;
        }
        ProjectileCrossResult end;
        if (!IsAreaCompanion(struck.Key) && (struck.Key == projectile.Target.Key && projectile.Critter is AttackGoal aimed ? aimed
                : _combatHost!.Target(owner, map, struck.Key) is { Dead: false, Build.Combat: { } combat } npc ? new AttackGoal(npc, combat, null) : null) is { } goal)
        {
            var result = owner.LandShot(owner.Version, projectile.Shot, CritterAttack(goal, 1, null));
            Ended(projectile, kind, goal.Entity, $"disposition={result.Disposition} failure={result.Failure} outcome={result.Resolution?.Outcome}"
                + $" damage={result.Life?.HitPointDamage}");
            // A refused strike leaves the shot in the session: it ends there too (a boomerang turns back). ARC-MAGIC-042: a thrown item's hit script runs for a hit, its miss script for anything else.
            end = StruckEnd(projectile, result.Disposition != GameSessionDisposition.Rejected && result.Resolution?.Outcome is Arcanum.Simulation.Combat.AttackOutcome.Hit or Arcanum.Simulation.Combat.AttackOutcome.CriticalHit, goal.Entity);
            // ARC-DEV-292: the attack event of the aimed NPC was raised at the shot; an NPC the arrow met on the way gets its own at the strike (as an NPC's stray shot does, AreaAiHost.LandShot), before its pass.
            // ARC-AI-1516 (EX14): the stray strike's event carries the accidental bit.
            if (result.Disposition != GameSessionDisposition.Rejected && result.Resolution?.Refusal == AttackRefusal.None && !(struck.Key == projectile.Target.Key && projectile.Critter is AttackGoal)) _aiHost?.HeroStrayAttacked(goal.Entity);
            if (result.Disposition != GameSessionDisposition.Rejected) FinishCritterHit(goal, result, aimed: struck.Key == projectile.Target.Key && projectile.Critter is AttackGoal);
        }
        else if (!IsAreaCompanion(struck.Key) && (struck.Key == projectile.Target.Key && projectile.Object is { } aimedObject ? aimedObject
                : ObjectTargets()?.Target(owner, struck.Key, AreaTrapped) is { Refusal: null } found && found.Kind is not DamageableObjectKind.Item ? found : null) is { } target)
        {
            if (ObjectTargets()?.Target(owner, target.Row.Key, AreaTrapped) is { Refusal: null } now) target = now;
            var result = owner.LandShotOnObject(owner.Version, projectile.Shot,
                target.Attack(1, null, AreaCombatHost.HeroWorn(owner, _areaKitDurability)) with { DropMap = AreaDropMapNow(), TakingDamage = () => TakingDamageGoesOn(target.Entity, SessionSpellCaster.Hero), HitScripts = HeroHitScripts(target.Entity) });
            Ended(projectile, kind, target.Entity, $"disposition={result.Disposition} failure={result.Failure} outcome={result.Resolution?.Outcome}"
                + $" damage={result.Object?.Damage}");
            end = StruckEnd(projectile, result.Disposition != GameSessionDisposition.Rejected && result.Resolution?.Outcome is Arcanum.Simulation.Combat.AttackOutcome.Hit or Arcanum.Simulation.Combat.AttackOutcome.CriticalHit, target.Entity);
            if (result.Disposition != GameSessionDisposition.Rejected) FinishObjectHit(null, target, result, true);
        }
        else if (struck.Type == 0)
        {
            var result = owner.LandShotOnObject(owner.Version, projectile.Shot,
                new SessionObjectAttack { Wall = true, HeroWorn = AreaCombatHost.HeroWorn(owner, _areaKitDurability), DropMap = AreaDropMapNow() });
            Ended(projectile, kind, $"wall:{struck.Key.SourceIndex}", $"disposition={result.Disposition} failure={result.Failure} prevented={result.Resolution?.Damage?.Prevented}");
            end = StruckEnd(projectile);
            if (result.Disposition != GameSessionDisposition.Rejected)
            {
                _attackNotice = "the shot struck a wall: " + AreaObjectTargets.Describe(result);
                FloatObjectAttack(result, $"wall:{struck.Key.SourceIndex}", null);
            }
            RefreshAreaStatus();
        }
        else
        {
            Ended(projectile, kind, null, $"struck {(IsAreaCompanion(struck.Key) ? "a companion" : $"an object of type {struck.Type}")}: not ready");
            end = StruckEnd(projectile);
            if (end == ProjectileCrossResult.Turned || !projectile.Returns)
                _attackNotice = "the shot struck " + (IsAreaCompanion(struck.Key) ? "a companion" : "an object") + ": striking it is not ready";
            RefreshAreaStatus();
        }
        FireShotEnd(projectile, events);
        RunThrownItemScriptAfterStrike();
        return end;
    }

    /// <summary>
    /// ARC-COMBAT-1140 (ledger PF8, RH5, OD1–OD5): an NPC's arrow met a non-critter object: a door, window, container, scenery or trap takes the hit through the session (<see cref="AreaAiHost.LandShotOnObject"/>), a wall takes
    /// nothing, as the hero's strike; then the floaters, the status line and the object's trap and BUST script (the shooter is no triggerer or breaker here, as for a hit with no attacker: not modelled), the inventory a spill changed.
    /// An item on the ground, a companion's row or an object with no target ends the shot with no strike (as before).
    /// </summary>
    private ProjectileCrossResult NpcShotOnObject(AreaProjectile projectile, ProjectileStrikeKind kind, AreaObjectRecord struck, AreaAiHost ai, string shooterId)
    {
        var owner = projectile.Owner;
        if (struck.Type == 0)
        {
            var result = ai.LandShotOnObject(shooterId, projectile.Shot, null, wall: true);
            Ended(projectile, kind, $"wall:{struck.Key.SourceIndex}", $"an NPC's shot on a wall: disposition={result?.Disposition} failure={result?.Failure} prevented={result?.Resolution?.Damage?.Prevented}");
            _npcShotStrikesForSelfTest.Add((projectile.Shot.Id, $"wall:{struck.Key.SourceIndex}", result?.Disposition ?? GameSessionDisposition.Rejected, null));
            if (result is { Disposition: not GameSessionDisposition.Rejected }) NpcObjectHit(shooterId, struck, null, result);
            return ProjectileCrossResult.Ended;
        }
        if (IsAreaCompanion(struck.Key) || ObjectTargets()?.Target(owner, struck.Key, AreaTrapped) is not { Refusal: null, Object: { } objectTarget } target || target.Kind is DamageableObjectKind.Item)
        {
            // ARC-COMBAT-1717: a thrown weapon's shot stays for its landing (the caller's).
            if (projectile.Thrown is null) owner.EndShot(projectile.Shot);
            Ended(projectile, kind, null, $"an NPC's shot met an object of type {struck.Type} it cannot strike: no strike");
            return ProjectileCrossResult.Ended;
        }
        var landed = ai.LandShotOnObject(shooterId, projectile.Shot, objectTarget, wall: false);
        Ended(projectile, kind, target.Entity, $"an NPC's shot on an object: disposition={landed?.Disposition} failure={landed?.Failure} outcome={landed?.Resolution?.Outcome} damage={landed?.Object?.Damage}");
        _npcShotStrikesForSelfTest.Add((projectile.Shot.Id, target.Entity, landed?.Disposition ?? GameSessionDisposition.Rejected, landed?.Object is { } hit ? hit.Damage - hit.DamageBefore : null));
        if (landed is { Disposition: not GameSessionDisposition.Rejected }) NpcObjectHit(shooterId, struck, target, landed);
        return ProjectileCrossResult.Ended;
    }

    /// <summary>
    /// ARC-COMBAT-1140: what follows an NPC's shot that struck an object or a wall (the AI host's <see cref="AreaAiScene.ObjectStruck"/>, or the arrow's landing): the floaters over the object, the status line, the trap and BUST script
    /// of a break (no triggerer or breaker: the shooter is an NPC, not modelled), the redraw and the inventory a spill changed. <paramref name="target"/> is null for a wall.
    /// </summary>
    private void NpcObjectHit(string shooter, AreaObjectRecord row, AreaObjectAttackTarget? target, SessionObjectAttackResult result)
    {
        if (_aiForSmoke) _npcObjectStrikesForSelfTest.Add((shooter, target?.Entity, result.Object is { } hit ? hit.Damage - hit.DamageBefore : null));
        if (target is not null) FinishObjectHit(null, target, result, ranged: true, byHero: false, attacker: shooter);
        else
        {
            _attackNotice = "an NPC's shot struck a wall: " + AreaObjectTargets.Describe(result);
            FloatObjectAttack(result, $"wall:{row.Key.SourceIndex}", null, shooter);
        }
        // A critical miss hurt the shooter itself (its stun is its goal on the table; a fall is drawn from the bodies).
        if (result.Life is { } self)
        {
            _attackNotice = $"{AiName(shooter)}'s shot misfired: it takes {self.HitPointDamage} damage";
            if (self.Events.HasFlag(LifeEvents.Died)) _deathAnimations[(CurrentAreaMap, shooter)] = DeathAnimationOf(self.Death);
            SyncAreaCombatBodies();
            SyncAreaInventory();
        }
        RefreshAreaStatus();
    }

    /// <summary>ARC-COMBAT-019: after a strike a boomerang turns back (the original's end runs just before the hit, PF8); any other
    /// projectile ends on its tile.</summary>
    private ProjectileCrossResult StruckEnd(AreaProjectile projectile, bool hit = false, string? target = null)
    {
        if (projectile.Returns && !projectile.State.Returning) return TurnBack(projectile) ? ProjectileCrossResult.Turned : ProjectileCrossResult.Ended;
        EndOnTile(projectile, hit ? Arcanum.Scripting.Scr.ScrPoint.Hit : Arcanum.Scripting.Scr.ScrPoint.Miss, target, afterStrike: true);
        return ProjectileCrossResult.Ended;
    }

    /// <summary>ARC-COMBAT-017: a shot ends on the tile the projectile is on: a thrown item lands there (0x4B2690, flag 0x40) and the
    /// ground is drawn again; any other shot is gone (after a strike the session holds it no more).</summary>
    /// <summary>
    /// <paramref name="scriptPoint"/> (ARC-MAGIC-042, ledger TI4): the thrown item's script point (7 hit, 8 miss) to run where it landed; null for the goal's own end, which asks no item event (GT53). With <paramref name="afterStrike"/>
    /// the run waits for the end of the strike (<see cref="RunThrownItemScriptAfterStrike"/>: the victim's own hit handling goes first, the original resolves the hit before the item's script); otherwise it runs at once.
    /// </summary>
    private void EndOnTile(AreaProjectile projectile, int? scriptPoint = null, string? scriptTarget = null, bool afterStrike = false)
    {
        _projectileHost?.Finish(projectile);
        var owner = projectile.Owner;
        if (projectile.Thrown is not { } thrown) { owner.EndShot(projectile.Shot); return; }
        var landed = owner.LandThrownItem(owner.Version, projectile.Shot, projectile.Flight.Tile);
        GD.Print($"ARC-COMBAT-017 thrown-landing shot={projectile.Shot.Id} item={thrown.Item} at={projectile.Flight.Tile.X},{projectile.Flight.Tile.Y}"
            + $" disposition={landed.Disposition} failure={landed.Failure}");
        _thrownLandingsForSelfTest.Add((thrown.Item, projectile.Flight.Tile, landed.Disposition));
        if (!ReferenceEquals(owner, _areaSession)) return;
        _groundStamp = null;
        SyncAreaInventory();
        // ARC-MAGIC-042 (TI4): the item's hit or miss script runs where it landed (the hero's throw; a failed landing runs nothing).
        if (scriptPoint is not { } point || landed.Disposition == GameSessionDisposition.Rejected) return;
        var tile = projectile.Flight.Tile;
        if (afterStrike) _thrownScriptAfterStrike = () => RunThrownItemScript(projectile, thrown.Item, tile, point, scriptTarget);
        else RunThrownItemScript(projectile, thrown.Item, tile, point, scriptTarget);
    }

    private Action? _thrownScriptAfterStrike;

    /// <summary>Runs the thrown item's script a strike kept back (ARC-MAGIC-042): called at the end of <see cref="Strike"/>, after the victim's hit handling and the shot's end event.</summary>
    private void RunThrownItemScriptAfterStrike()
    {
        var run = _thrownScriptAfterStrike;
        _thrownScriptAfterStrike = null;
        run?.Invoke();
    }

    /// <summary>A flight ends without a strike: beyond its range or off the map a thrown item lands on its last tile
    /// (<paramref name="land"/>); a flight given up (a map change, a reload) leaves it on the hero's cell where it was thrown from.
    /// ARC-DEV-140 (B14): the crossing's ends (<paramref name="fireEnd"/>: the range end, the destination) fire the projectile's item event at the
    /// last tile (0x4CBDB0, PF9, PF10) after the item landed; the goal's own end and a flight given up fire none (GT53).</summary>
    private void EndProjectile(AreaProjectile projectile, ProjectileStrikeKind kind, string? struck, string why, bool land = false, bool fireEnd = false)
    {
        var events = fireEnd ? ShotEndEvents(projectile) : [];
        var scripts = fireEnd ? projectile.Owner.QueryShotEndScripts(projectile.Shot) : null;
        _projectileHost?.Finish(projectile);
        // ARC-COMBAT-1223 (PS5, PF9; 0x4B2870): the end at the destination (a shot without a target) runs the weapon's hit script (point 7), the range end its miss script (point 8).
        if (land) EndOnTile(projectile, !fireEnd ? null : kind == ProjectileStrikeKind.DestinationEnd ? Arcanum.Scripting.Scr.ScrPoint.Hit : Arcanum.Scripting.Scr.ScrPoint.Miss);
        else projectile.Owner.EndShot(projectile.Shot);
        // ARC-COMBAT-1719: the weapon's scripts of a shot that is not the hero's thrown item (its own are the thrown-item host's) and the critical points, then the item event.
        RunShotEndScripts(projectile, scripts, kind == ProjectileStrikeKind.DestinationEnd);
        Ended(projectile, kind, struck, why);
        if (kind == ProjectileStrikeKind.RangeEnd) { _attackNotice = "the shot flew past"; RefreshAreaStatus(); }
        else if (kind == ProjectileStrikeKind.DestinationEnd && projectile.Returns) { _attackNotice = "the boomerang is back"; RefreshAreaStatus(); }
        else if (kind == ProjectileStrikeKind.DestinationEnd) { _attackNotice = "the throw missed and fell aside"; RefreshAreaStatus(); }
        FireShotEnd(projectile, events);
    }

    /// <summary>ARC-DEV-140 (B14, ledger PF10, IC10, GT63): the item event of the end of <paramref name="projectile"/>'s flight at the tile it is on
    /// (<see cref="GameSessionOwner.QueryShotEndItemEvents"/>), asked while the shot is still in flight; none for a shot of a hand without an item.</summary>
    private static IReadOnlyList<SessionItemEvent> ShotEndEvents(AreaProjectile projectile) =>
        projectile.Owner.QueryShotEndItemEvents(projectile.Shot, projectile.Flight.Tile);

    /// <summary>
    /// ARC-COMBAT-1719 (0x4B2870, ledger PF9, PS5, CP4): the weapon's scripts at an end of a projectile's flight without a strike (<paramref name="facts"/>, asked before the end:
    /// <see cref="GameSessionOwner.QueryShotEndScripts"/>), through the shown map's use-script host (<see cref="AreaUseScriptHost.RunCombatPoint"/>) in the original's order (<see cref="AreaShotEndScripts.Points"/>): the shooter is the
    /// triggerer, the weapon the attachee and the shot's target (none for a scattered throw) the extra object; the answers cancel nothing. A flight of another session or map, or a shot with no weapon item, runs nothing; a run the host refuses
    /// is said and the end goes on.
    /// </summary>
    private void RunShotEndScripts(AreaProjectile projectile, SessionShotEndScripts? facts, bool destination)
    {
        if (facts is not { } ends || !ReferenceEquals(projectile.Owner, _areaSession) || projectile.Map != CurrentAreaMap) return;
        if (_areaSession is not { } owner || _readyArea is not { } area || UseScriptHost(area.Snapshot) is not { } host) return;
        static bool IsHero(string id) => id == SessionSpellCaster.Hero || id == Arcanum.Simulation.Ai.AiSchedulerRules.Pc;
        var hero = IsHero(ends.Attacker);
        // A carried item's row is read by identity from the map the hero took it on, a thrown one that lies on the ground from the map it came from; an NPC's weapon is a row of the shown map.
        var map = CurrentAreaMap;
        var carried = hero ? owner.QueryPcInventory().FirstOrDefault(held => held.Entity == ends.Weapon) : default;
        var isCarried = carried.Entity is not null;
        var placed = hero && !isCarried ? owner.QueryPlacedItems().FirstOrDefault(item => item.Entity == ends.Weapon) : null;
        var weaponMap = isCarried ? carried.Origin.RuntimeMap : placed is not null ? (placed.Origin ?? placed.Map).RuntimeMap : map;
        // 0x4B2870: the critical points ask that the parent item is a weapon (object type 5); an item the session does not list counts as the wielded weapon, an NPC's as a weapon.
        var isWeapon = !hero || (isCarried ? carried.Kind.Type : placed?.Kind.Type ?? SessionItemType.Weapon) == SessionItemType.Weapon;
        Arcanum.Scripting.Scr.ScrObjectRef Ref(string id) => IsHero(id) ? host.Pc : new(map, ScriptCodeOfCast(id));
        var weaponRef = new Arcanum.Scripting.Scr.ScrObjectRef(weaponMap, ends.Weapon);
        var attackerRef = Ref(ends.Attacker);
        var targetRef = ends.Target is { } target ? Ref(target) : (Arcanum.Scripting.Scr.ScrObjectRef?)null;
        var points = AreaShotEndScripts.Points(destination, ends.Critical, isWeapon, hero && ends.Thrown);
        foreach (var point in points)
        {
            var ran = Awarded(host, host.RunCombatPoint(point, weaponRef, attackerRef, targetRef, owner.Rules.Scripts));
            if (ran.Run is not null)
                GD.Print($"ARC-COMBAT-1719 shot-end-script shot={projectile.Shot.Id} point={point} attacker={ends.Attacker} weapon={ends.Weapon} target={ends.Target ?? "none"} outcome={ran.Outcome} run={ran.Run.Outcome} stop={ran.Run.Stop} detail={ran.Detail}");
        }
        if (points.Count > 0) RefreshAreaStatus();
    }

    /// <summary>ARC-DEV-140 (B14): fires the item event of a shot's end through the map's cast host (0x4CBDB0 → 0x4CB830, after the projectile's end placed
    /// the item): the item's own spells with the attacks-a-location trigger (0x800000) start as runs aimed at the tile, so a thrown grenade explodes where it
    /// strikes, lands or ends. A flight of another session or map fires nothing.</summary>
    private void FireShotEnd(AreaProjectile projectile, IReadOnlyList<SessionItemEvent> events)
    {
        if (events.Count == 0 || !ReferenceEquals(projectile.Owner, _areaSession) || projectile.Map != CurrentAreaMap || SpellCasterNow() is not { } caster) return;
        var fired = caster.FireItemEvents(events);
        var started = fired.Sum(f => f.Result.Spells.Count(s => s.Outcome == SessionItemSpellOutcome.Started));
        var item = events[0].Item ?? "";
        _shotEndItemEventsForSelfTest.Add((projectile.Shot, item, projectile.Flight.Tile, [.. fired.SelectMany(f => f.Result.Spells).Select(spell => spell.Firing.Spell)], started));
        GD.Print($"ARC-DEV-140 shot-end-item-event shot={projectile.Shot.Id} item={item} at={projectile.Flight.Tile.X},{projectile.Flight.Tile.Y} firings={fired.Count} started={started} outcomes={string.Join(",", fired.SelectMany(f => f.Result.Spells).Select(spell => $"{spell.Firing.Spell}:{spell.Outcome}:{spell.Result?.Disposition}/{spell.Result?.Failure}/{spell.Result?.Creation?.Status}/{string.Join("|", spell.Result?.Notes.Select(n => n.ToString()) ?? [])}"))}");
        FlushSpellLog(null);
        RefreshAreaStatus();
    }

    private void Ended(AreaProjectile projectile, ProjectileStrikeKind kind, string? struck, string detail)
    {
        _projectileEndsForSelfTest.Add((projectile.Shot, kind, struck));
        GD.Print($"ARC-COMBAT-016 projectile-end shot={projectile.Shot.Id} strike={kind} struck={struck ?? "none"} at={projectile.Flight.Tile.X},{projectile.Flight.Tile.Y}"
            + $" steps={projectile.Flight.Steps} {detail}");
        if (_projectiles.Count == 0) SyncAreaProjectileBodies();
    }

    /// <summary>Every projectile ends (a map change, a reload): the shots are given up.</summary>
    private void ClearAreaProjectiles(string why)
    {
        _projectileHost?.LoseAll(why);
        _knockbackHost?.LoseAll(why);
        _projectileBodies = [];
        // ARC-MAGIC-018: the spell missiles go the same way (a map change, a quick load, a replaced session).
        ClearSpellMissiles(why);
    }

    // The projectiles' draw commands, keyed by shot.
    private Dictionary<AreaObjectKey, AreaDrawCommand> _projectileBodies = [];

    /// <summary>
    /// ARC-COMBAT-016: each projectile drawn from its faced art (its frame advanced once per step) at its tile, moved by its pixel
    /// offset from the tile's centre along the line (no height: the original draws it on the line between the tiles' centres); an art
    /// that does not read is not drawn (said once by the extra art).
    /// </summary>
    private void SyncAreaProjectileBodies()
    {
        var bodies = new Dictionary<AreaObjectKey, AreaDrawCommand>();
        if (_readyArea is { } area && _projectiles.Count > 0)
        {
            var frames = (AreaFrameOverlay)AreaFrames();
            foreach (var projectile in _projectiles)
            {
                if (projectile.Art is not { } faced || ExtraArt() is not { } art) continue;
                var aid = projectile.Frames > 1 ? (faced & ~(31U << 14)) | ((uint)(projectile.Flight.Steps % projectile.Frames) << 14) : faced;
                var load = art.Load(aid);
                if (load.Frame is not { } frame)
                {
                    if (_groundReported.Add($"projectile:{aid}")) GD.Print($"ARC-COMBAT-016 projectile-art art=0x{aid:X8} status={load.Status} detail={load.Detail}");
                    continue;
                }
                projectile.Frames = Math.Clamp(frame.FramesPerGroup, 1, 32);
                frames.Add(frame.Key, frame.Frame);
                _extraVisuals[frame.Visual.Key] = frame.Visual;
                var key = new AreaObjectKey("projectile", area.Snapshot.RuntimeMap, AreaSourceKind.Mobile, null, 0, 0,
                    projectile.Shot.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), 0);
                var tile = projectile.Flight.Tile;
                var command = AreaScenePlan.Place(area.Plan.Chart, key, frame.Visual, tile.X, tile.Y, flat: false);
                var start = LineOfFireRules.Screen(projectile.Flight.Start);
                var here = LineOfFireRules.Screen(tile);
                var dx = checked((int)(start.X + projectile.Flight.X - here.X));
                var dy = checked((int)(start.Y + projectile.Flight.Y - here.Y));
                var at = command.Destination;
                var moved = command with { Destination = new Arcanum.Data.Terrain.TerrainRect(checked(at.X + dx), checked(at.Y + dy), at.Width, at.Height) };
                // ARC-UI-063 (PF11): a thrown non-weapon is drawn on its size arc, around its hotspot.
                bodies[key] = projectile.SizeArc ? AreaScenePlan.ScaledAboutHotspot(moved, frame.Visual, projectile.Scale) : moved;
            }
        }
        _projectileBodies = bodies;
        _canvas?.SetAreaMovingBodies(AreaMovingCommands());
    }

    /// <summary>The shown map as a projectile crosses it: the shot's target where it stands (an NPC at its map cell, a companion at
    /// its live cell, an object at its cell), the living PCs and NPCs on a tile (the hero, the companions and the map's living NPCs in
    /// row order), and the obstruction of a free flight's step (the line of fire's step cost, <see cref="LineOfFireRules.StepCost{T}"/>).</summary>
    private sealed class ProjectileMap(TerrainViewerApp app, GameSessionOwner owner, AreaProjectile projectile, AreaLineOfFire lineMap) : IProjectileMap<AreaObjectRecord>
    {
        public CellCoord? TargetTile => app.LiveCell(owner, projectile.Target);

        public IEnumerable<AreaObjectRecord> LivingCritters(CellCoord tile)
        {
            if (app._worldActor.Session is AreaGridSession session && app.HeroCellNow(session) == tile) yield return HeroRow;
            if (app._areaCompanions is { } group)
                foreach (var companion in group.Companions)
                {
                    var (x, y) = group.WorldCell(companion.Speaker);
                    if (x == tile.X && y == tile.Y) yield return Same(companion.Speaker.Row);
                }
            if (app._readyArea is not { } area) yield break;
            var map = app.CurrentAreaMap;
            foreach (var row in area.Snapshot.Objects)
            {
                // ARC-DEV-282: an NPC the AI walked stands on its own cell, not on its map data's.
                if (row.Type is not (15 or 16) || row.WorldX is null || row.WorldY is null || app.IsAreaCompanion(row.Key) || app.NpcCell(row) != tile) continue;
                if (!app.AreaCritterDead(owner, map, row)) yield return Same(row);
            }
        }

        // The rules compare objects by identity: the shot's target is the same object wherever its row comes from.
        private AreaObjectRecord Same(AreaObjectRecord row) => row.Key == projectile.Target.Key ? projectile.Target : row;

        public AreaObjectRecord? Obstruction(CellCoord from, int rotation) =>
            LineOfFireRules.StepCost(from, rotation, false, lineMap, owner.Rules.Combat).Blocker;
    }

    private CellCoord HeroCellNow(AreaGridSession session)
    {
        var cell = session.Chart.ToWorld(session.State.CommittedCell);
        return new(checked((int)cell.X), checked((int)cell.Y));
    }

    /// <summary>Where a row stands now: a companion at its live cell, anything else at its map cell (NPCs do not move here); null when
    /// it is gone (destroyed, switched off, a dead NPC still lies where it fell).</summary>
    private CellCoord? LiveCell(GameSessionOwner owner, AreaObjectRecord row)
    {
        if (_areaCompanions is { } group && group.Companions.FirstOrDefault(c => c.Speaker.Row.Key == row.Key) is { } companion)
        {
            var (x, y) = group.WorldCell(companion.Speaker);
            return new(checked((int)x), checked((int)y));
        }
        if (row.Key == HeroRow.Key && _worldActor.Session is AreaGridSession heroSession) return HeroCellNow(heroSession);
        if (ObjectTargets()?.Gone(owner).Contains(row.Key) == true) return null;
        if (row.Type is 15 or 16 && row.WorldX is not null && row.WorldY is not null) return NpcCell(row);
        return row.WorldX is { } wx && row.WorldY is { } wy ? new(checked((int)wx), checked((int)wy)) : null;
    }
}
