using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-DEV-282: the NPC arrows launched and the strikes the AI host landed, for the npc-shot stage.
    private readonly List<(long Shot, bool Hit, string Shooter)> _npcShotLaunchesForSelfTest = [];
    private readonly List<(long Shot, string Struck, GameSessionDisposition Disposition, int? Damage)> _npcShotStrikesForSelfTest = [];

    // ARC-DEV-282 native observation (stage "npc-shot", only when named, map mode with --hero): an NPC of the map that wields a bow or firearm with a projectile art (a real archer of the installed data, no lent weapon)
    // and the hero five cells from it with a clear line; the NPC is told of the hero's attack and fights. Its attack is decided at the swing's action frame, its arrow flies as a projectile of its own (the stage sees the
    // launch) and strikes: a shot decided as a hit lands the damage on the hero when the flight ends, a miss does not hurt the hero it was aimed at. The shot must end (nothing left in flight or in the session). NONE on a
    // map with no such NPC. The first shot found is judged; the rest are printed.
    private async Task RunAreaNpcShotSmokeAsync()
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _combatHost is null || _readyArea is not { } area || _inventoryHost is null
            || _worldActor.Session is not AreaGridSession grid)
        { GD.Print($"ARC-DEV-282 native-smoke npc-shot NONE map={_readyMapName} (no hero, combat values or attack map)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-DEV-282 npc-shot smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_NPC_SHOT_SMOKE_FAILED");
        }
        var runtime = CurrentAreaMap;
        if (_inventoryHost.Map(runtime) is not { } inventory) { GD.Print($"ARC-DEV-282 native-smoke npc-shot NONE map={_readyMapName} (no inventory)"); return; }
        var map = new AreaMapRuntime(area.Snapshot, grid.Snapshot, grid.Chart);
        var commands = area.Plan.Commands.Where(c => c.Identity is not null).ToDictionary(c => c.Identity!.Value);
        // The archers: free NPCs of the map whose worn weapon has a projectile art and reaches beyond one tile.
        var holders = session.QueryMapInventory(new(runtime))!.Holders.Where(h => h.Kind == SessionHolderKind.Npc && !h.Dead && session.QueryCompanion(h.Entity).Standing == CompanionStanding.Free).ToArray();
        var archers = new List<(string Entity, AreaObjectRecord Row, AreaCombatTarget Target)>();
        foreach (var holder in holders)
        {
            if (inventory.Row(holder.Entity) is not { Scope: AreaScope.Core } row || !commands.ContainsKey(row.Key)) continue;
            var build = _combatHost.Build(session, runtime, holder.Entity);
            var worn = _combatHost.WornItems(session, runtime, holder.Entity);
            if (build.Combat is not { } combat || worn is null || !worn.TryGetValue(EquipmentSlot.Weapon, out var weapon) || weapon.MissileArt is null
                || session.QueryLife(combat.Target).View is not { } life || life.Status.IsDead) continue;
            try
            {
                var values = combat.AttackValues(life.Status, life.Record.State, session.Rules.Combat, worn.Keys);
                if (values.Range > 1 && values.Supported && _combatHost.Target(session, runtime, row.Key) is { } target) archers.Add((holder.Entity, row, target));
            }
            catch (ArgumentException) { }
        }
        if (archers.Count == 0) { GD.Print($"ARC-DEV-282 native-smoke npc-shot NONE map={_readyMapName} (no NPC with a bow or firearm with a projectile art among {holders.Length} free NPCs)"); return; }
        ForgetNpcPlacesForSmoke(session, runtime);
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        _aiForSmoke = true;
        var heroBefore = session.QueryHeroLife()!.Record.State;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            (string Entity, AreaObjectRecord Row, AreaCombatTarget Target)? chosen = null; CellCoord? start = null;
            foreach (var archer in archers.Take(12))
            {
                var at = map.Chart.ToChart(new(archer.Row.WorldX!.Value, archer.Row.WorldY!.Value));
                start = await AreaSmokeFightableStartAsync(map, commands[archer.Row.Key], at, 6, 4, false);
                if (start is not null) { chosen = archer; break; }
            }
            if (chosen is not { } shooter || start is null) { GD.Print($"ARC-DEV-282 native-smoke npc-shot NONE map={_readyMapName} (no archer the hero can stand 4..6 cells from and see among {archers.Count})"); return; }
            var entity = shooter.Entity;
            AreaSmokePlace(map, start.Value);
            if (!HeroInCombatMode) ToggleCombatMode();
            _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
            PublishWorldSelection();
            var host = AreaAiHostNow();
            Require(host is not null && session.QueryAiTick(entity) is not null, "the AI host is built and the archer has its AI tick");
            session.Heal(session.Version, shooter.Target.Build.Combat!.Target, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
            host!.HeroAttacked(entity);
            _npcShotLaunchesForSelfTest.Clear(); _npcShotStrikesForSelfTest.Clear(); _projectileEndsForSelfTest.Clear();
            var heroHp = session.QueryHeroLife()!.Status.HitPoints;

            _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            var hostMs = 30_000_000L;
            TickAreaClock(hostMs);
            void Frame()
            {
                _areaSmokeTime++;
                TickAreaClock(hostMs += 50);
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
            }
            var frames = 0;
            for (; frames < 6000 && _npcShotLaunchesForSelfTest.Count == 0; frames++) Frame();
            Require(_npcShotLaunchesForSelfTest.Count > 0, $"the archer shot after {frames} frames: {host.TraceOf(entity)?.Perform} {host.TraceOf(entity)?.Hit} danger={session.QueryDanger(entity).State}");
            var first = _npcShotLaunchesForSelfTest[0];
            var heroAtShot = session.QueryHeroLife()!.Status.HitPoints;
            for (var f = 0; f < 3000 && (_projectiles.Count > 0 || _npcShotStrikesForSelfTest.Count == 0 && session.QueryShotsInFlight().Count > 0); f++) Frame();
            Require(_projectiles.Count == 0 && !session.QueryShotsInFlight().Contains(new SessionShot(first.Shot)), "the arrow's flight ended and the session holds no shot");
            var end = _projectileEndsForSelfTest.FirstOrDefault(e => e.Shot.Id == first.Shot);
            GD.Print($"ARC-DEV-282 npc-shot flight shot={first.Shot} hit={first.Hit} strike={end.Strike} struck={end.Struck ?? "none"} launches={_npcShotLaunchesForSelfTest.Count} strikes={_npcShotStrikesForSelfTest.Count}");
            // A shot decided as a hit that struck its target damages it; this one is aimed at the hero, so the damage is the hero's lost hit points (a stray strike before the target is printed, not judged).
            var lost = heroAtShot - session.QueryHeroLife()!.Status.HitPoints;
            if (first.Hit && _npcShotStrikesForSelfTest.Any(s => s.Shot == first.Shot && s.Struck == AiSchedulerRules.Pc))
                Require(_npcShotStrikesForSelfTest.First(s => s.Shot == first.Shot && s.Struck == AiSchedulerRules.Pc).Damage is > 0 || lost > 0, "a hit shot that reached the hero damaged it");
            if (!first.Hit) Require(!_npcShotStrikesForSelfTest.Any(s => s.Shot == first.Shot && s.Struck == AiSchedulerRules.Pc && s.Damage is > 0), "a missed shot did not hurt the hero it was aimed at");
            // ARC-COMBAT-1140: the second leg, an arrow of the same archer through an object of the map.
            var objectLeg = NpcShotObjectLeg(session, runtime, entity, Frame);
            GD.Print("ARC-DEV-282 native-smoke npc-shot PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                RuntimeMap = runtime, Archer = shooter.Row.PrototypeNumber, Shots = _npcShotLaunchesForSelfTest.Count, Hit = first.Hit, Strike = end.Strike.ToString(), Struck = end.Struck ?? "none", HeroLost = lost, ObjectLeg = objectLeg,
                Parity = "NOT_VERIFIED (rules read in the original executable; no original-runtime run)" }));
        }
        finally
        {
            RestoreHeroForSmoke(session, heroBefore);
            _aiForSmoke = false; RetireAiHost(); _aiHostStamp = null;
            _areaSmokeTime = (_areaSmokeTime + 19) / 20;
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            PublishWorldSelection();
        }
    }

    // ARC-COMBAT-1140 (PF3, PF8, RH5): a missed arrow of the real archer flies on past its destination and strikes the first object that stops its way (an obstruction). A door, container or scenery of the map that stops the line of fire
    // (the line map names it the blocker of the line from three cells before it to three beyond) is struck by the archer's session-decided miss, launched from the cell before it to the next cell (so the object lies beyond the destination):
    // the strike goes through the AI host to the session as a forced hit and the object's damage or condition changes. The aimed-at-an-object case (RA4) is covered by the AI host's unit tests. NONE (printed, not a failure) when the map has no such object.
    private string NpcShotObjectLeg(GameSessionOwner session, int runtime, string archer, Action frame)
    {
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-COMBAT-1140 npc-shot object leg FAIL: " + why);
            throw new InvalidOperationException("AREA_NPC_SHOT_OBJECT_LEG_FAILED");
        }
        if (ObjectTargets() is not { } targets || _readyArea is not { } area || AreaLineOfFireNow() is not { } lineMap || _combatHost is null || AreaAiHostNow() is null)
            return "NONE (no object targets, line map or AI host)";
        var combat = _combatHost.Build(session, runtime, archer).Combat;
        var worn = _combatHost.WornItems(session, runtime, archer);
        if (combat is null || worn is null) return "NONE (the archer has no combat values)";
        var candidates = area.Snapshot.Objects.Where(row => row.Scope == AreaScope.Core && row.WorldX is not null && row.WorldY is not null)
            .Select(row => targets.Target(session, row.Key, AreaTrapped))
            .Where(t => t is { Refusal: null, Object: { } o } && t.Kind is DamageableObjectKind.Portal or DamageableObjectKind.Container or DamageableObjectKind.Scenery
                && !o.Baseline.Invulnerable && !o.Baseline.Busted && !o.Baseline.JunkPile)
            .Select(t => (Target: t!, State: session.QueryObject(t!.Object!))).Where(c => c.State.HitPoints > 0).Take(600).ToArray();
        (AreaObjectAttackTarget Target, SessionObjectState State)? chosen = null; CellCoord from = default, to = default;
        foreach (var candidate in candidates)
        {
            var cell = new CellCoord(checked((int)candidate.Target.Row.WorldX!.Value), checked((int)candidate.Target.Row.WorldY!.Value));
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1) })
            {
                var a = new CellCoord(cell.X - 3 * dx, cell.Y - 3 * dy); var b = new CellCoord(cell.X + 3 * dx, cell.Y + 3 * dy);
                if (!lineMap.Contains(a) || !lineMap.Contains(b)) continue;
                if (LineOfFireRules.Trace(a, 0, 0, b, lineMap, session.Rules.Combat).Blocker is not { } blocker || blocker.Key != candidate.Target.Row.Key) continue;
                (chosen, from, to) = (candidate, a, new CellCoord(a.X + dx, a.Y + dy));
                break;
            }
            if (chosen is not null) break;
        }
        if (chosen is not { } picked) return $"NONE (no door, container or scenery of {candidates.Length} stops a straight line of fire at its own cell)";
        // A shot the session decided a miss (an object past the destination is struck by a missed shot only).
        SessionAttackResult? decided = null;
        for (var attempt = 0; attempt < 800 && !(decided?.Shot is { } maybe && session.QueryNpcShot(maybe) is { Hit: false }); attempt++)
        {
            foreach (var leaked in decided?.Shots ?? []) session.EndShot(leaked);
            decided = session.AttackByNpc(session.Version, new SessionNpcAttack(combat) { Distance = 6, LineOfFire = new(0, false), AttackerWorn = worn, Flight = true });
        }
        Require(decided?.Shot is { } missed && session.QueryNpcShot(missed) is { Hit: false }, "the session decided a missed flying shot of the archer");
        foreach (var extra in decided!.Shots.Skip(1)) session.EndShot(extra);
        var shot = decided.Shot!.Value;
        _npcShotStrikesForSelfTest.Clear(); _projectileEndsForSelfTest.Clear();
        Require(AiLaunchShot(archer, AiSchedulerRules.Pc, shot, from, to), "the arrow launched toward the object's cell");
        for (var f = 0; f < 3000 && (_projectiles.Count > 0 || session.QueryShotsInFlight().Contains(shot)); f++) frame();
        Require(_projectiles.Count == 0 && !session.QueryShotsInFlight().Contains(shot), "the arrow's flight ended and the session holds no shot");
        var after = session.QueryObject(picked.Target.Object!);
        var strike = _npcShotStrikesForSelfTest.FirstOrDefault(x => x.Shot == shot.Id);
        var end = _projectileEndsForSelfTest.FirstOrDefault(e => e.Shot.Id == shot.Id);
        GD.Print($"ARC-COMBAT-1140 npc-shot object leg target={picked.Target.Entity} kind={picked.Target.Kind} from={from.X},{from.Y} to={to.X},{to.Y} strike={end.Strike} struck={end.Struck ?? "none"}"
            + $" disposition={strike.Disposition} damage={strike.Damage} before={picked.State.Damage}/{picked.State.Condition} after={after.Damage}/{after.Condition} notice={_attackNotice}");
        Require(strike.Struck == picked.Target.Entity && strike.Disposition != GameSessionDisposition.Rejected, "the archer's arrow struck the object through the session");
        Require(after.Damage > picked.State.Damage || after.Condition != picked.State.Condition, "the arrow changed the object's damage or condition");
        return $"{picked.Target.Kind} {picked.Target.Entity} damage {picked.State.Damage}->{after.Damage} {after.Condition}";
    }
}
