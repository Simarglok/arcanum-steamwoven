using Arcanum.Data.Area;
using Arcanum.Data.Combat;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    /// <summary>ARC-COMBAT-016: one projectile in flight on the shown map: the session's shot, what it was shot at (the struck object
    /// of the shot: the goal's target, or the line of fire's blocker), the destination (the goal target's tile at the shot), its flight
    /// and carried outcome, its faced art and the world time it left at.</summary>
    private sealed class AreaProjectile
    {
        public required SessionShot Shot { get; init; }
        // The session that holds the shot: a projectile of another session (a reload, a carried session) is given up.
        public required GameSessionOwner Owner { get; init; }
        public required int Map { get; init; }
        public required AreaObjectRecord Target { get; init; }
        public AttackGoal? Critter { get; init; }
        public AreaObjectAttackTarget? Object { get; init; }
        // The goal target's tile; ARC-COMBAT-019: a returning boomerang's is the hero's tile at its turn.
        public required CellCoord Destination { get; set; }
        public required uint? Art { get; init; }
        public required long StartMs { get; init; }
        public required ProjectileFlight Flight { get; set; }
        public required ProjectileShot State { get; set; }
        // ARC-COMBAT-017: the thrown item the projectile carries, which lands where it ends; null for a weapon kept in the hand.
        public SessionThrownShot? Thrown { get; init; }
        // ARC-COMBAT-018: a missed throw's scatter cleared its target: it flies free to its scattered tile and ends leaving it.
        public bool Targetless { get; init; }
        // ARC-COMBAT-019: a boomerang's projectile, which turns back to the hero at its first end (flag 0x1000).
        public bool Returns { get; init; }
        public long Ticks { get; set; }
        public int Frames { get; set; } = 1;
    }

    private readonly List<AreaProjectile> _projectiles = [];
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
        AttackGoal? critter, AreaObjectAttackTarget? obj, CellCoord? scatter)
    {
        var owner = _areaSession!;
        var session = (AreaGridSession)_worldActor.Session!;
        var hero = session.Chart.ToWorld(session.State.CommittedCell);
        var start = new CellCoord(checked((int)hero.X), checked((int)hero.Y));
        var destination = scatter ?? GoalTile(goal);
        var thrown = owner.QueryThrownShot(shot);
        if (ProjectileRules.Launch(start, destination) is not { } flight)
        {
            // A thrown item lands where it lies, on the hero's cell (a missed throw may scatter back onto it, ARC-COMBAT-018; the
            // original's flight of a line without a step was not read).
            if (thrown is { } item)
            {
                var landed = owner.LandThrownItem(owner.Version, shot, start);
                _thrownLandingsForSelfTest.Add((item.Item, start, landed.Disposition));
                _groundStamp = null;
                SyncAreaInventory();
            }
            else owner.EndShot(shot);
            _projectileEndsForSelfTest.Add((shot, scatter is null ? ProjectileStrikeKind.None : ProjectileStrikeKind.DestinationEnd, null));
            GD.Print($"ARC-COMBAT-016 projectile-unlaunched shot={shot.Id} from={start} to={destination}");
            return;
        }
        var direction = ProjectileRules.Direction(start, destination);
        var art = ProjectileRules.FacedArt(unchecked((uint)weapon.Art), direction);
        var hit = resolution.Outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit;
        _projectiles.Add(new AreaProjectile
        {
            Shot = shot, Owner = owner, Map = CurrentAreaMap, Target = target, Critter = critter, Object = obj, Destination = destination, Art = art,
            StartMs = WorldMilliseconds(), Flight = flight, State = new(hit, scatter is not null, thrown?.Range ?? weapon.Range), Thrown = thrown,
            Targetless = scatter is not null, Returns = owner.QueryShotReturns(shot),
        });
        _attackNotice = $"the shot flies ({(hit ? "it will hit" : scatter is null ? "it missed" : "it missed and scatters")})";
        GD.Print($"ARC-COMBAT-016 projectile-launch shot={shot.Id} target={target.Key.Kind}:{target.Key.SourceIndex} hit={hit} from={start.X},{start.Y}"
            + $" to={destination.X},{destination.Y} direction={direction} art={(art is { } a ? $"0x{a:X8}" : "none")}"
            + (thrown is { } t ? $" thrown={t.Item} range={t.Range}" : "") + (scatter is null ? "" : " scattered")
            + (owner.QueryShotReturns(shot) ? $" boomerang range={weapon.Range}" : ""));
        SyncAreaProjectileBodies();
    }

    /// <summary>
    /// ARC-COMBAT-016, per frame: each projectile takes the steps its ticks of 35 ms owe (four 10-pixel steps a tick, 0x42F6A0) and,
    /// on each tile it leaves, does what the original's 0x4B2870 does (<see cref="ProjectileRules.Cross{TObject}"/>): its target
    /// struck through the session with the carried hit, another living critter struck 1 in 3 (rolled from the session's sequence), an
    /// obstruction past the destination, or the end beyond its range. A flight off the map, or on a map no longer shown, ends.
    /// </summary>
    private void ProcessAreaProjectiles()
    {
        if (_projectiles.Count == 0) return;
        var owner = _areaSession;
        var now = WorldMilliseconds();
        var moved = false;
        var lineMap = owner is null || !AttackMapActive ? null : AreaLineOfFireNow();
        foreach (var projectile in _projectiles.ToArray())
        {
            if (owner is null || lineMap is null || projectile.Map != CurrentAreaMap || !ReferenceEquals(projectile.Owner, owner))
            {
                EndProjectile(projectile, ProjectileStrikeKind.None, null, "the map changed");
                continue;
            }
            var due = Math.Max(0, (now - projectile.StartMs) / ProjectileRules.TickMs);
            if (due <= projectile.Ticks) continue;
            var steps = ProjectileRules.StepsIn(due - projectile.Ticks, 0);
            projectile.Ticks = due;
            moved = true;
            for (var i = 0; i < steps && _projectiles.Contains(projectile); i++)
            {
                var (next, crossing) = ProjectileRules.Step(projectile.Flight, lineMap.Contains);
                if (next is null) { EndProjectile(projectile, ProjectileStrikeKind.None, null, "it left the map", land: true); break; }
                // The tile left is handled before the projectile moves into the next one (0x42F6A0); a projectile that ended or
                // turned back (ARC-COMBAT-019: it flies its new line) does not take this step.
                if (crossing is { } c && CrossTile(projectile, c, owner, lineMap)) continue;
                projectile.Flight = next;
            }
        }
        if (moved) { SyncAreaProjectileBodies(); PublishWorldSelection(); }
    }

    /// <summary>The tile left, as 0x4B2870 handles it; true when the projectile ended or turned back (its flight is not stepped on).</summary>
    private bool CrossTile(AreaProjectile projectile, ProjectileCrossing crossing, GameSessionOwner owner, AreaLineOfFire lineMap)
    {
        var map = new ProjectileMap(this, owner, projectile, lineMap);
        var crossed = ProjectileRules.Cross(projectile.State, crossing, projectile.Destination, projectile.Targetless ? null : projectile.Target, HeroRow, map,
            (min, max) => owner.Roll("combat.StrayStrike", min, max).Roll?.Value ?? throw new InvalidOperationException("AREA_STRAY_ROLL_REFUSED"));
        projectile.State = crossed.Shot;
        if (!crossed.Ends) return false;
        // ARC-COMBAT-019: a returning boomerang leaving the tile it flies back to is back in the hero's hand.
        if (crossed.Strike == ProjectileStrikeKind.DestinationEnd && projectile.State.Returning)
        {
            EndProjectile(projectile, crossed.Strike, null, "back in the hero's hand");
            BoomerangReturned(projectile);
            return true;
        }
        // ARC-COMBAT-019: a boomerang's first end without a strike turns it back (0x4B2690 with flag 0x1000).
        if (crossed.Strike is ProjectileStrikeKind.RangeEnd or ProjectileStrikeKind.DestinationEnd && projectile.Returns)
        {
            Ended(projectile, crossed.Strike, null, "turned back");
            TurnBack(projectile);
            return true;
        }
        if (crossed.Strike == ProjectileStrikeKind.RangeEnd) { EndProjectile(projectile, crossed.Strike, null, "beyond its range", land: true); return true; }
        // ARC-COMBAT-018: a shot without a target ends on leaving its destination (a missed throw lands on its scattered tile).
        if (crossed.Strike == ProjectileStrikeKind.DestinationEnd) { EndProjectile(projectile, crossed.Strike, null, "at its destination", land: true); return true; }
        Strike(projectile, crossed.Strike, crossed.Struck!);
        return true;
    }

    /// <summary>
    /// ARC-COMBAT-019 (0x4B2690 with attack flag 0x1000, 0x435A00): the boomerang's projectile turns back at its first end, from the
    /// tile it is on (the one being left) to the hero's tile now, and flies back striking nothing (flag 0x2000); its facing stays. The
    /// session's shot turns with it (a strike turned it already). On the hero's own tile it is back at once.
    /// </summary>
    private void TurnBack(AreaProjectile projectile)
    {
        var owner = projectile.Owner;
        owner.ReturnShot(projectile.Shot);
        if (_worldActor.Session is not AreaGridSession session) { EndProjectile(projectile, ProjectileStrikeKind.None, null, "no actor to return to"); return; }
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
            return;
        }
        projectile.Flight = flight;
        projectile.Destination = hero;
        if (!_projectiles.Contains(projectile)) _projectiles.Add(projectile);
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
        AttackGoal? goal = _combatHost!.Target(owner, CurrentAreaMap, projectile.Target.Key) is { Dead: false, Build.Combat: { } combat } npc
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

    /// <summary>The struck object takes the shot through the session: an NPC with combat values (<see cref="GameSessionOwner.LandShot"/>),
    /// a door, window, container, scenery or trap (<see cref="GameSessionOwner.LandShotOnObject"/>), a wall; a companion or an NPC
    /// without combat values is not ready (the shot ends without a strike). ARC-COMBAT-017: a thrown item then lands on the tile left
    /// (the original lands it just before the hit, PF8; nothing of the hit reads it).</summary>
    private void Strike(AreaProjectile projectile, ProjectileStrikeKind kind, AreaObjectRecord struck)
    {
        var owner = projectile.Owner;
        _projectiles.Remove(projectile);
        PlaceHeroForDrop();
        var map = CurrentAreaMap;
        if (!IsAreaCompanion(struck.Key) && (struck.Key == projectile.Target.Key && projectile.Critter is { } aimed ? aimed
                : _combatHost!.Target(owner, map, struck.Key) is { Dead: false, Build.Combat: { } combat } npc ? new AttackGoal(npc, combat, null) : null) is { } goal)
        {
            var result = owner.LandShot(owner.Version, projectile.Shot, CritterAttack(goal, 1, null));
            Ended(projectile, kind, goal.Entity, $"disposition={result.Disposition} failure={result.Failure} outcome={result.Resolution?.Outcome}"
                + $" damage={result.Life?.HitPointDamage}");
            // A refused strike leaves the shot in the session: it ends there too (a boomerang turns back).
            StruckEnd(projectile);
            if (result.Disposition != GameSessionDisposition.Rejected) FinishCritterHit(goal, result);
            return;
        }
        if (!IsAreaCompanion(struck.Key) && (struck.Key == projectile.Target.Key && projectile.Object is { } aimedObject ? aimedObject
                : ObjectTargets()?.Target(owner, struck.Key, AreaTrapped) is { Refusal: null } found && found.Kind is not DamageableObjectKind.Item ? found : null) is { } target)
        {
            if (ObjectTargets()?.Target(owner, target.Row.Key, AreaTrapped) is { Refusal: null } now) target = now;
            var result = owner.LandShotOnObject(owner.Version, projectile.Shot,
                target.Attack(1, null, AreaCombatHost.HeroWorn(owner, _areaKitDurability)) with { DropMap = AreaDropMapNow() });
            Ended(projectile, kind, target.Entity, $"disposition={result.Disposition} failure={result.Failure} outcome={result.Resolution?.Outcome}"
                + $" damage={result.Object?.Damage}");
            StruckEnd(projectile);
            if (result.Disposition != GameSessionDisposition.Rejected) FinishObjectHit(null, target, result);
            return;
        }
        if (struck.Type == 0)
        {
            var result = owner.LandShotOnObject(owner.Version, projectile.Shot,
                new SessionObjectAttack { Wall = true, HeroWorn = AreaCombatHost.HeroWorn(owner, _areaKitDurability), DropMap = AreaDropMapNow() });
            Ended(projectile, kind, $"wall:{struck.Key.SourceIndex}", $"disposition={result.Disposition} failure={result.Failure} prevented={result.Resolution?.Damage?.Prevented}");
            StruckEnd(projectile);
            if (result.Disposition != GameSessionDisposition.Rejected)
            {
                _attackNotice = "the shot struck a wall: " + AreaObjectTargets.Describe(result);
                FloatObjectAttack(result, $"wall:{struck.Key.SourceIndex}", null);
            }
            RefreshAreaStatus();
            return;
        }
        Ended(projectile, kind, null, $"struck {(IsAreaCompanion(struck.Key) ? "a companion" : $"an object of type {struck.Type}")}: not ready");
        StruckEnd(projectile);
        if (_projectiles.Contains(projectile) || !projectile.Returns)
            _attackNotice = "the shot struck " + (IsAreaCompanion(struck.Key) ? "a companion" : "an object") + ": striking it is not ready";
        RefreshAreaStatus();
    }

    /// <summary>ARC-COMBAT-019: after a strike a boomerang turns back (the original's end runs just before the hit, PF8); any other
    /// projectile ends on its tile.</summary>
    private void StruckEnd(AreaProjectile projectile)
    {
        if (projectile.Returns && !projectile.State.Returning) TurnBack(projectile);
        else EndOnTile(projectile);
    }

    /// <summary>ARC-COMBAT-017: a shot ends on the tile the projectile is on: a thrown item lands there (0x4B2690, flag 0x40) and the
    /// ground is drawn again; any other shot is gone (after a strike the session holds it no more).</summary>
    private void EndOnTile(AreaProjectile projectile)
    {
        var owner = projectile.Owner;
        if (projectile.Thrown is not { } thrown) { owner.EndShot(projectile.Shot); return; }
        var landed = owner.LandThrownItem(owner.Version, projectile.Shot, projectile.Flight.Tile);
        GD.Print($"ARC-COMBAT-017 thrown-landing shot={projectile.Shot.Id} item={thrown.Item} at={projectile.Flight.Tile.X},{projectile.Flight.Tile.Y}"
            + $" disposition={landed.Disposition} failure={landed.Failure}");
        _thrownLandingsForSelfTest.Add((thrown.Item, projectile.Flight.Tile, landed.Disposition));
        if (!ReferenceEquals(owner, _areaSession)) return;
        _groundStamp = null;
        SyncAreaInventory();
    }

    /// <summary>A flight ends without a strike: beyond its range or off the map a thrown item lands on its last tile
    /// (<paramref name="land"/>); a flight given up (a map change, a reload) leaves it on the hero's cell where it was thrown from.</summary>
    private void EndProjectile(AreaProjectile projectile, ProjectileStrikeKind kind, string? struck, string why, bool land = false)
    {
        _projectiles.Remove(projectile);
        if (land) EndOnTile(projectile);
        else projectile.Owner.EndShot(projectile.Shot);
        Ended(projectile, kind, struck, why);
        if (kind == ProjectileStrikeKind.RangeEnd) { _attackNotice = "the shot flew past"; RefreshAreaStatus(); }
        else if (kind == ProjectileStrikeKind.DestinationEnd && projectile.Returns) { _attackNotice = "the boomerang is back"; RefreshAreaStatus(); }
        else if (kind == ProjectileStrikeKind.DestinationEnd) { _attackNotice = "the throw missed and fell aside"; RefreshAreaStatus(); }
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
        foreach (var projectile in _projectiles.ToArray()) EndProjectile(projectile, ProjectileStrikeKind.None, null, why);
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
                bodies[key] = command with { Destination = new Arcanum.Data.Terrain.TerrainRect(checked(at.X + dx), checked(at.Y + dy), at.Width, at.Height) };
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
                if (row.Type is not (15 or 16) || row.WorldX != tile.X || row.WorldY != tile.Y || app.IsAreaCompanion(row.Key)) continue;
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
        if (ObjectTargets()?.Gone(owner).Contains(row.Key) == true) return null;
        return row.WorldX is { } wx && row.WorldY is { } wy ? new(checked((int)wx), checked((int)wy)) : null;
    }
}
