using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-COMBAT-2167 (stage "bed-blow", only when named, with --hero, on the Crash Site cave): the viewer glue of the hero's blow on an occupied bed (ARC-COMBAT-1942, 0x4B3170 SC16). A living cave NPC is seated in one of the cave's
    // beds with the occupancy's seat and the sleeping flags 0x1000 and 0x100 (what the host's standpoint seat writes; its bed record and latch are not), the hero stands next to the bed in combat mode and Alt-clicks it. The first blow takes the sleeper out of the bed
    // (the bed's occupied frame goes) and strikes the sleeper through the combat path with the check's invocation modifier -30 (no backstab); the next blows strike the empty bed as a scenery, whose damage changes. NONE
    // without a bed the hero can reach and see under the pointer, or without a living NPC with combat values. Numbers only; no original content in the output.
    private async Task RunAreaBedBlowSmokeAsync()
    {
        if (_areaSession is not { } session || session.QueryHero() is null || ObjectTargets() is not { } targets || _readyArea is not { } area || _worldActor.Session is not AreaGridSession grid
            || _combatHost is not { } combat || _mapTransfers is not { } transfers || session.QueryHeroAttack() is not { } heroAttack)
        { GD.Print($"ARC-COMBAT-2167 native-smoke bed-blow NONE map={_readyMapName} (needs --hero and an attack map)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-COMBAT-2167 bed-blow smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_BED_BLOW_SMOKE_FAILED");
        }
        var runtimeMap = CurrentAreaMap;
        var map = new AreaMapRuntime(area.Snapshot, grid.Snapshot, grid.Chart);
        var commands = area.Plan.Commands.Where(c => c.Identity is not null).GroupBy(c => c.Identity!.Value).ToDictionary(g => g.Key, g => g.First());
        var pickable = HoverIndex()?.Bodies.Select(body => body.Identity).ToHashSet() ?? [];
        var beds = transfers.Beds.Select(key => transfers.Object(key))
            .Where(row => row.Scope == AreaScope.Core && row.WorldX is not null && row.WorldY is not null && commands.ContainsKey(row.Key) && pickable.Contains(row.Key) && _canvas?.AreaRoofCovers(row.Key) != true
                && targets.Target(session, row.Key, AreaTrapped) is { Refusal: null, Object: { } o } && !o.Baseline.Busted && !o.Baseline.JunkPile && session.QueryObject(o).HitPoints > 0)
            .OrderBy(row => targets.Target(session, row.Key, AreaTrapped)!.Object!.Baseline.Invulnerable).ToArray();
        // The NPC with the most hit points that is no companion: one blow must leave it standing.
        var sleepers = session.QueryMapInventory(new(runtimeMap))?.Holders
            .Where(h => h.Kind == SessionHolderKind.Npc && !h.Dead && session.QueryCompanion(h.Entity).Standing == CompanionStanding.Free)
            .Select(h => (h.Entity, Build: combat.Build(session, runtimeMap, h.Entity)))
            .Where(c => c.Build.Combat is not null)
            .OrderByDescending(c => LifeRules.MaxHitPoints(c.Build.Combat!.Facts, 0)).ThenBy(c => c.Entity, StringComparer.Ordinal).ToArray() ?? [];
        if (beds.Length == 0 || sleepers.Length == 0)
        { GD.Print($"ARC-COMBAT-2167 native-smoke bed-blow NONE map={_readyMapName} beds={beds.Length} (reachable, drawn, attackable) npcs={sleepers.Length} (living, not companions, with combat values)"); return; }
        var sleeper = sleepers[0];
        var npc = sleeper.Entity;
        var occupancy = BedOccupancyForSelfTest;

        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime; var aiBefore = _aiForSmoke;
        var combatModeBefore = HeroInCombatMode;
        var seatedBed = default(AreaObjectKey?);
        var scaled = false;
        _worldFocusSimulationForSelfTest = true;
        _aiForSmoke = true;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            CalmNpcsForSmoke(session, [npc]);
            session.Heal(session.Version, SessionLifeTarget.Hero, new LifeHeal { Full = true, Fatigue = 1000 });
            session.Heal(session.Version, sleeper.Build.Combat!.Target, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
            if (!HeroInCombatMode) ToggleCombatMode();
            Require(HeroInCombatMode, "the hero is in combat mode");

            // The first bed with a cell the hero can stand on next to it, a clear swing from there and a point under the pointer.
            AreaObjectRecord? chosen = null; CellCoord standing = default; Vector2 point = default; var skipped = new List<string>();
            foreach (var bed in beds.Take(8))
            {
                CellCoord standable;
                try { standable = AreaSmokeStandable(map, map.Chart.ToChart(new(bed.WorldX!.Value, bed.WorldY!.Value)), 1); }
                catch (InvalidOperationException) { skipped.Add($"{bed.Key.SourceIndex}:no standable cell"); continue; }
                var world = map.Chart.ToWorld(standable);
                if (!RangedPath(heroAttack) && !InAttackReach(bed, new(checked((int)world.X), checked((int)world.Y)), heroAttack)) { skipped.Add($"{bed.Key.SourceIndex}:no clear swing"); continue; }
                _areaSmokeTime = (_areaSmokeTime + 19) / 20;
                AreaSmokePlace(map, standable);
                _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
                scaled = true;
                _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
                PublishWorldSelection();
                if (await HoverPointAsync(this, commands[bed.Key]) is not { } found) { skipped.Add($"{bed.Key.SourceIndex}:not under the pointer"); continue; }
                (chosen, standing, point) = (bed, standable, found);
                break;
            }
            if (chosen is null) { GD.Print($"ARC-COMBAT-2167 native-smoke bed-blow NONE map={_readyMapName} (every bed skipped: {string.Join("; ", skipped)})"); return; }
            var target = targets.Target(session, chosen.Key, AreaTrapped)!;
            var bedKey = chosen.Key;

            // The AI host reconciles the occupancy with the NPCs' sleeping state on its first bed question (an empty bed answers nobody), so the seat comes after it.
            var host = AreaAiHostNow();
            Require(host is not null, "the AI host is built");
            Require(host!.Tile(npc) is not null, "the AI host runs the sleeper");
            Require(host.HeroBlowOccupant(target.Entity) is null, "an empty bed gives no occupant");
            Require(occupancy.Seat(bedKey, npc), "the sleeper takes the bed");
            seatedBed = bedKey;
            Require(session.WriteObjectFlag(session.Version, npc, SessionObjectFlagField.Critter, 0x1000, true, false).Failure == GameSessionFailure.None
                && session.WriteObjectFlag(session.Version, npc, SessionObjectFlagField.Object, 0x100, true, false).Failure == GameSessionFailure.None, "the sleeper's flags are written");
            ProcessAreaSceneryAnimation();
            Require(_sceneryBodies.ContainsKey(bedKey), "the occupied bed is drawn at its next frame");
            if (await HoverPointAsync(this, commands[bedKey]) is not { } occupiedPoint) { Require(false, "the occupied bed is under the pointer"); return; }
            point = occupiedPoint;
            HoverAtForSelfTest(point);

            var hpBefore = session.QueryLife(sleeper.Build.Combat!.Target).View!.Record.State.HitPointDamage;
            var bedBefore = session.QueryObject(target.Object!);
            _lastCritterAttackForSelfTest = null;
            CancelAreaAttack(null);
            _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = point, GlobalPosition = point, AltPressed = true });
            Require(_attack is { Object: not null }, "the Alt-click on the occupied bed started an object attack: " + _attackNotice);
            AttackSettleForSelfTest();
            var goal = _attack!;
            var frames = 0;
            void Frame()
            {
                _areaSmokeTime++; SmokeTickHeroRuns();
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
            }
            // The first blow: the occupant is taken out and struck through the combat path.
            for (; frames < 4000 && _attack is not null && goal.Attacks == 0; frames++) Frame();
            Require(goal.Attacks > 0, "the hero's first blow landed on the bed's target: " + _attackNotice);
            var firstBlow = _lastCritterAttackForSelfTest;
            Require(firstBlow is not null, "the first blow struck a critter, not the bed: " + _attackNotice);
            Require(occupancy.Occupant(bedKey) is null, "the sleeper is out of the bed: " + occupancy.Occupant(bedKey));
            ProcessAreaSceneryAnimation();
            Require(!_sceneryBodies.ContainsKey(bedKey), "the emptied bed is drawn at its own frame again");
            var invocation = firstBlow!.Resolution?.Difficulty?.Invocation;
            Require(invocation == -30, $"the check carries the bed modifier -30 (SC16): {invocation} {firstBlow.Resolution?.Outcome}");
            var hit = firstBlow.Resolution?.Outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit;
            // The struck sleeper fights back from here on (the host is on): calm again, and the hero healed, so the second blow is not cut short by the fight.
            CalmNpcsForSmoke(session, [npc]);
            session.Heal(session.Version, SessionLifeTarget.Hero, new LifeHeal { Full = true, Fatigue = 1000 });
            var hpAfter = session.QueryLife(sleeper.Build.Combat!.Target).View!.Record.State.HitPointDamage;
            Require(!hit || hpAfter > hpBefore || firstBlow.Resolution?.Damage?.Prevented > 0, $"a hit hurt the sleeper: {hpBefore} -> {hpAfter}");
            Require(session.QueryObject(target.Object!).Damage == bedBefore.Damage, "the bed itself took nothing from the first blow");

            // The next blow: the goal keeps the bed, which is empty now, so the bed takes it as a scenery and no critter is struck again. A bed the data makes invulnerable (the cave's) takes no damage:
            // the blow still goes the object path, and the notice says so.
            for (; frames < 4000 && _attack is not null && goal.Attacks < 2; frames++) Frame();
            var bedAfter = session.QueryObject(target.Object!);
            Require(goal.Attacks >= 2, $"a second blow landed (attacks {goal.Attacks}, notice {_attackNotice})");
            Require(ReferenceEquals(_lastCritterAttackForSelfTest, firstBlow), "the second blow struck no critter: the bed was empty");
            Require(!goal.Stop && !string.IsNullOrEmpty(_attackNotice), $"the second blow went through the object path (notice '{_attackNotice}')");
            var invulnerable = target.Object!.Baseline.Invulnerable;
            Require(invulnerable || bedAfter.Damage > bedBefore.Damage || bedAfter.Condition != bedBefore.Condition, $"the next blow struck the empty bed (attacks {goal.Attacks}, notice {_attackNotice})");
            CancelAreaAttack(null);
            GD.Print("ARC-COMBAT-2167 native-smoke bed-blow PASS " + System.Text.Json.JsonSerializer.Serialize(new
            {
                Map = _readyMapName, Bed = target.Entity, Invocation = invocation, Outcome = firstBlow.Resolution?.Outcome.ToString(), Hit = hit, SleeperHp = new[] { hpBefore, hpAfter },
                BedDamage = new[] { bedBefore.Damage, bedAfter.Damage }, BedInvulnerable = invulnerable, Attacks = goal.Attacks, Frames = frames, Skipped = skipped.Count,
            }));
        }
        finally
        {
            CancelAreaAttack(null); _attackAimForSelfTest = null;
            // A failed run leaves the sleeper in the bed: the seat and the flags go out so the next stage finds the map as it was.
            if (seatedBed is { } leftover && occupancy.Occupant(leftover) == npc)
            {
                occupancy.Unseat(leftover, npc);
                session.WriteObjectFlag(session.Version, npc, SessionObjectFlagField.Critter, 0x1000, false, false);
                session.WriteObjectFlag(session.Version, npc, SessionObjectFlagField.Object, 0x100, false, false);
            }
            if (!combatModeBefore && HeroInCombatMode) ToggleCombatMode();
            HoverReleaseForSelfTest();
            if (scaled) _areaSmokeTime = (_areaSmokeTime + 19) / 20;
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            if (!aiBefore) { _aiForSmoke = false; RetireAiHost(); _aiHostStamp = null; }
        }
    }
}
