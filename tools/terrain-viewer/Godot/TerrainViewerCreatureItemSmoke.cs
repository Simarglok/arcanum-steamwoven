using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Magic;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-MAGIC-042 (T-m1, ledger TI4–TI6) native observation (stage "creature-item", only when named, map mode with --hero): the two kinds of creature item of the installed data are thrown from the pack at a tile near a living
    // NPC. The pet item (the Mechanical degree-7 product, script 1022, creature 28460) becomes a critter that joins the hero; the aggressor item (the Mechanical degree-6 product, script 29017, creature 27391) becomes a critter that
    // raises the attack event on everyone around it. The items are made on the hero's feet by the session (as a script's create object does), picked up through the inventory host, lifted onto the game cursor and thrown. NONE
    // when the map holds no living NPC with a clear throwing cell, or the module has no such item prototype. The critters are destroyed afterwards (the stage leaves the party and the map as it found them). The pet is a companion of the group (ARC-MAGIC-050): it is stood far from the hero and must walk back to it.
    // What the aggressor does after it exists (its fight) is printed, not judged.
    private async Task RunAreaCreatureItemSmokeAsync()
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _combatHost is null || _readyArea is not { } area || _inventoryHost is null
            || _worldActor.Session is not AreaGridSession grid || AreaLineOfFireNow() is not { } lineMap || PrototypeItems() is not { } items)
        { GD.Print($"ARC-MAGIC-042 native-smoke creature-item NONE map={_readyMapName} (no hero, combat values, attack map or prototype items)"); return; }
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-MAGIC-042 creature-item smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_CREATURE_ITEM_SMOKE_FAILED");
        }
        var runtime = CurrentAreaMap;
        var map = new AreaMapRuntime(area.Snapshot, grid.Snapshot, grid.Chart);
        static CellCoord World(AreaObjectRecord row) => new(checked((int)row.WorldX!.Value), checked((int)row.WorldY!.Value));
        // The two item prototypes: the item whose hit script is the named script (the number the audit gives is the basic number, the file is 20 more).
        AreaPrototypeItem? Find(int basic, int script) => new[] { items.ScriptItem(basic), items.Item(basic) }.FirstOrDefault(i => i is not null && i.Scripts.Any(s => s.Point == ScrPointHit && s.Script == script));
        var pet = Find(15086, 1022); var aggressor = Find(15136, 29017);
        if (pet is null || aggressor is null) { GD.Print($"ARC-MAGIC-042 native-smoke creature-item NONE map={_readyMapName} (no item prototype with the creature scripts: pet {pet is not null}, aggressor {aggressor is not null})"); return; }

        // The NPC the items are thrown near: living, with combat values, not a companion, with a throwing cell three to six cells away that has a clear line and that the hero can walk to.
        var npcs = area.Snapshot.Objects.Where(row => row.Type == 16 && row.Scope == AreaScope.Core && row.WorldX is not null && row.WorldY is not null && !IsAreaCompanion(row.Key))
            .Select(row => _combatHost.Target(session, runtime, row.Key))
            .Where(t => t is { Dead: false, Follower: false, Build.Combat: not null } && session.QueryLife(t.Build.Combat!.Target).View?.Status.IsDead != true)
            .Select(t => t!).OrderBy(t => t.Entity, StringComparer.Ordinal).ToArray();
        CellCoord? stand = null; AreaCombatTarget? victim = null;
        foreach (var candidate in npcs)
        {
            var at = World(candidate.Row);
            for (var d = 3; d <= 6 && stand is null; d++)
            for (var dy = -d; dy <= d && stand is null; dy++)
            for (var dx = -d; dx <= d && stand is null; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != d) continue;
                var hero = new CellCoord(at.X + dx, at.Y + dy);
                if (!lineMap.Contains(hero)) continue;
                var trace = LineOfFireRules.Trace(hero, 0, 0, at, lineMap, session.Rules.Combat);
                if (trace.LineFailed) continue;
                var chart = map.Chart.ToChart(new(hero.X, hero.Y));
                if (map.Chart.Contains(chart) && ApproachCell(grid, at.X, at.Y, 6, cell => cell == chart) == chart) { stand = hero; victim = candidate; }
            }
            if (stand is not null) break;
        }
        if (stand is not { } standAt || victim is not { } npc) { GD.Print($"ARC-MAGIC-042 native-smoke creature-item NONE map={_readyMapName} (no living NPC with a clear throwing cell among {npcs.Length})"); return; }
        var npcAt = World(npc.Row);
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        _aiForSmoke = true;
        var scaled = false;
        var heroBefore = session.QueryHeroLife()!.Record.State;
        var npcLife = npc.Build.Combat!.Target;
        var made = new List<string>();
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            AreaSmokePlace(map, map.Chart.ToChart(new(standAt.X, standAt.Y)));
            scaled = true; _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            PublishWorldSelection();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            void Frame()
            {
                _areaSmokeTime++;
                SpellSmokeTick();
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
            }
            session.Heal(session.Version, npcLife, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });

            // One throw: the item made on the hero's feet, into the pack, onto the cursor, thrown at the tile; the flight and the script's run are played out. Returns the item's entity and the run the landing made.
            (string Item, AreaThrownItemRun Run) Throw(AreaPrototypeItem prototype, CellCoord tile, string label)
            {
                session.AttachItemWeights(number => items.Item(number)?.Weight);
                session.AttachItemScripts(number => items.Item(number)?.Scripts);
                var created = session.CreateGroundItem(session.Version, prototype.Kind, new SessionMapId(runtime), session.QueryActor().Cell, AreaDropMapNow()!, DropOwner.PcSide);
                Require(created.Disposition == GameSessionDisposition.Changed && created.Item is not null, $"{label}: the item was created ({created.Disposition} {created.Failure})");
                var item = created.Item!;
                var taken = _inventoryHost.PickUp(session, item);
                Require(AreaInventoryHost.Moved(taken) && session.QueryPlacedItems().All(placed => placed.Entity != item), $"{label}: the hero picked the item up ({taken.Disposition} {taken.Failure} {taken.DeclinedBy})");
                SyncAreaInventory();
                Require(HoldItemOnGameCursor(item, "smoke") is null && GameCursorItemForSelfTest == item, $"{label}: the item is on the game cursor ({HeldThrowNoticeForSelfTest})");
                _projectileEndsForSelfTest.Clear(); _thrownLandingsForSelfTest.Clear(); _thrownItemRunsForSelfTest.Clear(); _lastHeldThrowForSelfTest = null;
                Require(ThrowGameCursorItem(tile), $"{label}: the throw started ({HeldThrowNoticeForSelfTest})");
                for (var frames = 0; frames < 1500 && _lastHeldThrowForSelfTest is null && HeroRunsHost() is { ThrowRun: not null }; frames++) Frame();
                Require(_lastHeldThrowForSelfTest is { Disposition: GameSessionDisposition.Changed }, $"{label}: the item was thrown ({_lastHeldThrowForSelfTest?.Disposition} {_lastHeldThrowForSelfTest?.Failure}, {HeldThrowNoticeForSelfTest})");
                for (var frames = 0; frames < 1500 && (_projectiles.Count > 0 || HeroRunsHost() is { ThrowRun: not null }); frames++) Frame();
                Require(_projectiles.Count == 0, $"{label}: the flight ended");
                var run = _thrownItemRunsForSelfTest.LastOrDefault(r => r.Item == item);
                Require(run is not null, $"{label}: the landing ran the item's script");
                return (item, run!);
            }

            // The pet: thrown at a tile of its own (no creature there), so it is a flight that ended at its destination (the hit point 7, ARC-COMBAT-1223; the range end runs the miss point 8).
            var petTile = new CellCoord(standAt.X + Math.Sign(npcAt.X - standAt.X) * 2, standAt.Y + Math.Sign(npcAt.Y - standAt.Y) * 2);
            var thrownPet = Throw(pet, petTile, "pet");
            var petRun = thrownPet.Run;
            GD.Print($"ARC-MAGIC-042 creature-item pet point={petRun.Point} outcome={petRun.Outcome} critter={petRun.Critter} followed={petRun.Followed} detail={petRun.Detail}");
            Require(petRun.Outcome == AreaThrownItemOutcome.Applied && petRun.Critter is { } petCritter, $"pet: the script ran and made a critter ({petRun.Outcome}: {petRun.Detail})");
            made.Add(petRun.Critter!);
            Require(petRun.Followed && session.QueryCompanion(petRun.Critter!).LedByPc, "pet: the critter follows the hero");
            Require(!session.QueryPlacedItems().Any(p => p.Entity == thrownPet.Item) && !session.QueryPcInventory().Any(i => i.Entity == thrownPet.Item), "pet: the item is gone");
            Require(session.QueryCreatedObject(petRun.Critter!) is { Kind: SessionCreatedKind.Critter } petMade && petMade.Prototype == 28460 + AreaPrototypeItems.PrototypeOffset,
                "pet: the critter is the script's prototype (28460 + 20)");
            // ARC-DEV-402 (TG9): the creation ended with the location notification of the cell the item lay on (the critter's own); the AI frame runs it (the tile's script, if any, runs with the critter as the triggerer).
            var petCell = session.QueryCreatedObject(petRun.Critter!)!.Cell;
            Require(_creationNoticesForSelfTest.Any(n => n.Entity == petRun.Critter && n.Cell.X == petCell.X && n.Cell.Y == petCell.Y), "pet: the creation queued its location notification on its own cell");
            Require(AreaAiHostNow() is { } hosted && (hosted.Scene.Npcs.Any(n => n.Entity == petRun.Critter) || hosted.Scene.Followers?.Npcs.Any(n => n.Entity == petRun.Critter) == true), "pet: the AI host runs the critter");
            // ARC-MAGIC-050: the pet is a companion of the shown map (its own body template), no NPC of the host: the group walks it after the hero.
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Frame();
            var petCompanion = _areaCompanions?.Companions.FirstOrDefault(c => c.Entity == petRun.Critter);
            Require(petCompanion is { Template.Created: true }, "pet: the created critter is a companion of the group with a body template of its own");
            Require(AreaAiHostNow() is { } petHost && petHost.RunCompanions.Contains(petRun.Critter!) && petHost.Scene.Npcs.All(n => n.Entity != petRun.Critter), "pet: the AI host runs it as a follower, not as an NPC");
            Require(AreaMovingCommands()?.Count(c => c.Identity == petCompanion!.Key) == 1, "pet: one moving command draws it");
            CellCoord PetCell() => new(checked((int)grid.Chart.ToWorld(petCompanion!.Grid.State.CommittedCell).X), checked((int)grid.Chart.ToWorld(petCompanion.Grid.State.CommittedCell).Y));
            // The pet is stood eight to ten cells from the hero (the follow goal starts beyond four cells), where the hero can walk to (the route's test), as a placement the session records; the group is rebuilt over it.
            // The actor session after the placement (the stage's first placement made a new one).
            var gridNow = (AreaGridSession)_worldActor.Session!;
            var heroAt = LiveCell(gridNow) ?? standAt;
            CellCoord? far = null;
            for (var d = 8; d <= 10 && far is null; d++)
            for (var dy = -d; dy <= d && far is null; dy++)
            for (var dx = -d; dx <= d && far is null; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != d) continue;
                var candidate = new CellCoord(heroAt.X + dx, heroAt.Y + dy);
                var chartCell = grid.Chart.ToChart(new(candidate.X, candidate.Y));
                if (grid.Chart.Contains(chartCell) && grid.Snapshot.IsAdmitted(grid.Chart.ToWorld(chartCell)) && ApproachCell(gridNow, heroAt.X, heroAt.Y, 12, c => c == chartCell) == chartCell) far = candidate;
            }
            Require(far is not null, "pet: a cell eight to ten cells from the hero to stand the pet on");
            Require(session.ApplyChanges(session.Version, null, [AreaCompanionPlacement.Edit(petCompanion!.Template, new SessionMapId(runtime), far!.Value, NeighborDirection.XPlus)]).Disposition == GameSessionDisposition.Changed, "pet: the placement was recorded");
            BindAreaCompanions();
            petCompanion = _areaCompanions?.Companions.FirstOrDefault(c => c.Entity == petRun.Critter);
            Require(petCompanion is { Template.Created: true }, "pet: the group was rebuilt with the pet");
            var petFirst = PetCell();
            Require(petFirst == far, $"pet: it stands on the recorded cell ({petFirst.X},{petFirst.Y} wanted {far!.Value.X},{far.Value.Y})");
            for (var frames = 0; frames < 800; frames++) Frame();
            var petLater = PetCell();
            Require(!_npcTilePending.Any(n => n.Npc == petRun.Critter), "pet: the creation notice was taken by the AI frame");
            var hero = LiveCell((AreaGridSession)_worldActor.Session!) ?? standAt;
            GD.Print($"ARC-MAGIC-050 creature-item pet-drive status={_areaCompanions?.Status()} held={AreaAiHostNow()?.Holds(petRun.Critter!)} danger={session.QueryDanger(petRun.Critter!).State} last={petCompanion!.LastUpdate.Action} goal={petCompanion.FollowGoal} time={petCompanion.Grid.State.Time.Value} pcTime={((AreaGridSession)_worldActor.Session!).State.Time.Value} heroCell={((AreaGridSession)_worldActor.Session!).State.CommittedCell} standAt={standAt.X},{standAt.Y}");
            Require(Math.Max(Math.Abs(petLater.X - hero.X), Math.Abs(petLater.Y - hero.Y)) <= 6, $"pet: the pet stands near the hero after the follow ({petLater.X},{petLater.Y} hero {hero.X},{hero.Y})");
            GD.Print($"ARC-MAGIC-042 creature-item pet-after critter={petRun.Critter} first={petFirst.X},{petFirst.Y} later={petLater.X},{petLater.Y} hero={hero.X},{hero.Y}"
                + $" led={session.QueryCompanion(petRun.Critter!).LedByPc} standing={session.QueryCompanion(petRun.Critter!).Standing}");

            // The aggressor: thrown at the NPC's tile (a strike, or a flight that ended near it); its critter raises the attack event on everyone in the vicinity: the NPC, and the hero (who only loses concealment).
            var thrownAggressor = Throw(aggressor, npcAt, "aggressor");
            var attackRun = thrownAggressor.Run;
            GD.Print($"ARC-MAGIC-042 creature-item aggressor point={attackRun.Point} outcome={attackRun.Outcome} critter={attackRun.Critter} attacks={string.Join(",", attackRun.Attacks.Select(a => $"{a.Attacker}>{a.Victim}:{a.Detail}"))} detail={attackRun.Detail}");
            Require(attackRun.Outcome == AreaThrownItemOutcome.Applied && attackRun.Critter is not null, $"aggressor: the script ran and made a critter ({attackRun.Outcome}: {attackRun.Detail})");
            made.Add(attackRun.Critter!);
            Require(!attackRun.Followed && !session.QueryCompanion(attackRun.Critter!).LedByPc, "aggressor: the critter does not follow");
            Require(attackRun.Attacks.Any(a => a.Victim == npc.Entity) && attackRun.Attacks.All(a => a.Attacker == attackRun.Critter), "aggressor: the critter raised the attack event on the NPC next to it");
            Require(session.QueryCreatedObject(attackRun.Critter!)?.Prototype == 27391 + AreaPrototypeItems.PrototypeOffset, "aggressor: the critter is the script's prototype (27391 + 20)");
            Require(_creationNoticesForSelfTest.Any(n => n.Entity == attackRun.Critter), "aggressor: the creation queued its location notification");
            for (var frames = 0; frames < 200; frames++) Frame();
            GD.Print($"ARC-MAGIC-042 native-smoke creature-item PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                RuntimeMap = runtime, Pet = petRun.Critter, PetPoint = petRun.Point, PetFollowed = petRun.Followed, Aggressor = attackRun.Critter, AggressorPoint = attackRun.Point,
                Attacks = attackRun.Attacks.Count, Victims = attackRun.Attacks.Select(a => a.Victim).ToArray(),
                Parity = "NOT_VERIFIED (rules read in the original executable; no original-runtime run)" }));
        }
        finally
        {
            foreach (var entity in made)
            {
                session.LeaveParty(session.Version, entity, new PartyLeave(true));
                session.DestroyCreatedObject(session.Version, entity);
            }
            SyncCreatedCritters(session, AreaAiHostNow()!);
            session.Heal(session.Version, npcLife, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
            RestoreHeroForSmoke(session, heroBefore);
            if (scaled) _areaSmokeTime = (_areaSmokeTime + 19) / 20;
            _gameCursorItem = null;
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            _aiForSmoke = false; RetireAiHost(); _aiHostStamp = null;
            PublishWorldSelection();
        }
    }

    private const int ScrPointHit = Arcanum.Scripting.Scr.ScrPoint.Hit;
}
