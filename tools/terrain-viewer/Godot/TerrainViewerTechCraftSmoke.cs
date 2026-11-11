using Arcanum.Data.Area;
using Arcanum.Data.Technology;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-DEV-362 (technology audit ARC-RES-104, slice T-a) native observation (stage "tech-craft", only when named, with --hero, best on the map "crash site cave"
    // whose bodies hold bullets): the whole technology chain on the player's data, through the production windows.
    //  1. Degrees: the hero is given points and buys Herbology degrees 1 and 2 and Gun Smithy degree 1 with the sheet tech page's + press.
    //  2. Herbology: the first two learned schematics (the food items that heal) are assembled in the schematic window from components the session creates by
    //     prototype; the products are new items of the pack, the components are gone; the hero, hurt to one hit point, uses one of each through the inventory
    //     window (the item-use path and the item-trigger host), and the hit points rise and the item is gone.
    //  3. Gun Smithy: the first learned schematic (a firearm) is assembled, worn in the weapon slot (the hero then attacks with the Firearms skill), given the
    //     bullets of the map, and shot at a living NPC from three to six cells away until a shot hits; the shots spend the bullets.
    //  4. F7 and F8: the degrees, the products, the worn gun and the bullets are the same after the load.
    // The hero's hit points and fatigue and the shot NPC's life are put back; the rest (points, degrees, items, the worn gun, the bullets taken) is the stage's own state: the stage runs alone (smoke.py) and its F8 replaces the session.
    private async Task RunAreaTechCraftSmokeAsync()
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _inventoryHost is null || _combatHost is null || _readyArea is not { } area
            || _worldActor.Session is not AreaGridSession grid || AreaLineOfFireNow() is not { } lineMap)
        { GD.Print($"{TechCraftCode} native-smoke tech-craft NONE map={_readyMapName} (needs --hero, an area session, its inventory and attack map)"); return; }
        var data = await TechSmokeDataAsync();
        for (var i = 0; i < 1800 && SpellRecordsNow() is null; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        TechSmokeRequire(SpellRecordsNow() is not null && ItemTriggersNow() is SessionItemTriggers, "the spell data and the item-trigger host of the map");
        var runtime = CurrentAreaMap;
        var map = new AreaMapRuntime(area.Snapshot, grid.Snapshot, grid.Chart);
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        var scaled = false;
        var saves = System.IO.Directory.CreateTempSubdirectory("arc-dev-362-").FullName;
        var libraryBefore = _saveLibraryForSelfTest;
        var heroBefore = session.QueryHeroLife()!.Record.State;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            if (InventoryOpen) EndInventory();
            AreaSmokePlace(map, grid.State.CommittedCell);
            scaled = true; _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            PublishWorldSelection();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            void Frame()
            {
                _areaSmokeTime++;
                SpellSmokeTick(); SmokeTickHeroRuns();
                WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .05, true, SimulationTime.At(_areaSmokeTime, 20));
            }

            // 1. Degrees.
            var intelligence = session.QueryHeroStats(new(0))!.Value.Snapshot!.GetEffective(Arcanum.Simulation.Character.CharacterStat.Intelligence);
            var before = Enumerable.Range(0, 8).Select(d => TechSmokeDegree(session, d)).ToArray();
            TechSmokeRequire(before[TechDisciplineHerbology] == 0 && before[TechDisciplineGunSmithy] == 0, $"the hero starts without degrees in Herbology and Gun Smithy ({string.Join(",", before)})");
            TechSmokeEnsurePoints(session, 3);
            OpenSheet();
            TechSmokeRequire(SheetOpen, "the sheet opens: " + _sheetStatus);
            TechSmokeRequire(TechSmokeBuyDegree(TechDisciplineHerbology) == 1, $"the tech page's + buys Herbology degree 1 (intelligence {intelligence})");
            TechSmokeRequire(TechSmokeBuyDegree(TechDisciplineHerbology) == 1, $"the tech page's + buys Herbology degree 2 (intelligence {intelligence}; degree 2 needs 8)");
            TechSmokeRequire(TechSmokeBuyDegree(TechDisciplineGunSmithy) == 1, $"the tech page's + buys Gun Smithy degree 1 (intelligence {intelligence})");
            EndSheet();
            TechSmokeRequire(!SheetOpen && TechSmokeDegree(session, TechDisciplineHerbology) == 2 && TechSmokeDegree(session, TechDisciplineGunSmithy) == 1, "the degrees stand after the sheet closed");
            var expertise = session.QueryHeroTechnology()!.Value;
            GD.Print($"{TechCraftCode} tech-craft degrees herbology={expertise[TechDisciplineHerbology].Degree}/{expertise[TechDisciplineHerbology].Expertise}% gun-smithy={expertise[TechDisciplineGunSmithy].Degree}/{expertise[TechDisciplineGunSmithy].Expertise}% intelligence={intelligence}");

            // ARC-UI-142: the schematic window's original look, clicked through its art buttons.
            await TechSmokeSchematicLookAsync();

            // 2. Herbology: assemble and use the first two learned products.
            var heals = new List<object>();
            for (var degree = 1; degree <= 2; degree++)
            {
                var record = SchematicCatalog.LearnedNumber(TechDisciplineHerbology, degree);
                var heldBefore = TechSmokeHeld(session);
                var craft = TechSmokeCraft(data, TechDisciplineHerbology, record);
                var products = craft.Made.ToList();
                var prototypes = products.Select(p => TechSmokePrototype(session, p)).Distinct().ToArray();
                TechSmokeRequire(prototypes.Length == 1 && prototypes[0] is not null, $"record {record}'s units are all one product ({string.Join(",", prototypes)})");
                GD.Print($"{TechCraftCode} tech-craft assembled record={record} units={products.Count} product={prototypes[0]} components={string.Join(",", craft.ComponentPrototypes)} message={craft.Message}");
                // The hero uses the first unit hurt to one hit point.
                var lost = TechSmokeHurtHero(session);
                OpenInventory();
                TechSmokeRequire(InventoryOpen, "the inventory window opens for the use");
                var logStart = SpellCasterNow()?.Log.Count ?? 0;
                var itemSpells = HeroItemSpells(products[0]);
                var spellRecords = SpellRecordsNow()!;
                foreach (var number in (itemSpells?.Spells ?? []).Where(n => n >= 0 && n < spellRecords.Length && spellRecords[n] is not null))
                    GD.Print($"{TechCraftCode} tech-craft spell={number} charges={itemSpells!.Charges} begin=" + string.Join(";", spellRecords[number]!.Of(SpellRunAction.Begin)
                        .Select(c => c.Type + (c.Damage is { } d ? $":{d.Minimum}-{d.Maximum}/type{d.DamageType}/flags{(int)d.Flags}" : ""))));
                UseHeroItem(products[0], AreaItemUseSource.RightClick, shift: false);
                var used = LastItemUseForSelfTest;
                TechSmokeRequire(used is { Gate: AreaItemUseGate.Use, Step: not null } && used.Value.Item == products[0], $"record {record}'s product is used: {used?.Gate} {used?.Step} {used?.Message}");
                SpellCastEvent[] HealLog() => [.. (SpellCasterNow()?.Log ?? []).Skip(logStart).Where(e => e.Kind == SpellCastEventKind.Healed && e.Object == SessionSpellCaster.Hero)];
                for (var frames = 0; frames < 400 && HealLog().Length == 0; frames++) Frame();
                for (var frames = 0; frames < 40; frames++) Frame();
                var healedEvents = HealLog();
                var healed = TechSmokeHeroHitPoints(session) - 1; var rested = TechSmokeHeroFatigue(session) - 1;
                if (InventoryOpen) EndInventory();
                // The spell's heal ran on the hero: degree 1's record 150 heals 20 hit points (type 0), degree 2's record 151 heals 20 fatigue (type 4).
                TechSmokeRequire(healedEvents.Length > 0, $"record {record}'s product runs its heal on the hero (step {used?.Step}, hit points {healed}, fatigue {rested}, log {healedEvents.Length})");
                TechSmokeRequire(lost.HitPoints >= 20 && lost.Fatigue >= 20 && (degree == 1 ? healed == 20 && rested == 0 : rested == 20 && healed == 0),
                    $"record {record}'s product heals what its record says, 20 (hit points hurt {lost.HitPoints} healed {healed}, fatigue hurt {lost.Fatigue} rested {rested})");
                TechSmokeRequire(!TechSmokeHeld(session).Contains(products[0]) && products.Skip(1).All(TechSmokeHeld(session).Contains), $"record {record}'s used unit is gone and the others stay ({TechSmokeHeld(session).Count} held, before {heldBefore.Count})");
                heals.Add(new { Record = record, Units = products.Count, Product = prototypes[0], HurtHitPoints = lost.HitPoints, HealedHitPoints = healed, HurtFatigue = lost.Fatigue, Rested = rested, Step = used!.Value.Step.ToString() });
                session.Heal(session.Version, SessionLifeTarget.Hero, new LifeHeal { Full = true, Fatigue = 1000 });
            }

            // 3. Gun Smithy: assemble the firearm, wear it and shoot with it.
            var gunRecord = SchematicCatalog.LearnedNumber(TechDisciplineGunSmithy, 1);
            var gun = TechSmokeCraft(data, TechDisciplineGunSmithy, gunRecord);
            TechSmokeRequire(gun.Made.Length == 1, $"the firearm record makes one gun ({gun.Made.Length})");
            var gunEntity = gun.Made[0];
            OpenInventory();
            TechSmokeRequire(InventoryOpen && _inventoryHost.Wearable(session, gunEntity, _areaKitWear) is { } wearable, "the made gun has wear values");
            var slot = AreaInventoryHost.WearSlots(_inventoryHost.Wearable(session, gunEntity, _areaKitWear)!)[0];
            WearItem(gunEntity, slot);
            EndInventory();
            TechSmokeRequire(session.QueryEquipment().Worn.Any(w => w.Entity == gunEntity && w.Slot == EquipmentSlot.Weapon), $"the gun is worn in the weapon slot ({slot}): {_inventoryStatus}");
            var heroAttack = session.QueryHeroAttack();
            TechSmokeRequire(heroAttack is { Skill: CombatSkill.Firearms, Supported: true }, $"the hero attacks with the Firearms skill ({heroAttack?.Skill} supported {heroAttack?.Supported})");
            var weapon = session.QueryEquipment().Worn.First(w => w.Entity == gunEntity).Item.Weapon!;
            var ammoType = weapon.AmmoType;
            TechSmokeRequire(ammoType is not null && weapon.AmmoConsumption > 0, $"the gun takes bullets (type {ammoType}, {weapon.AmmoConsumption} a shot)");
            GD.Print($"{TechCraftCode} tech-craft gun record={gunRecord} product={TechSmokePrototype(session, gunEntity)} range={heroAttack!.Range} ammo-type={ammoType} per-shot={weapon.AmmoConsumption}");

            // The bullets of the map: a stack on the ground or in a body.
            var inventory = session.QueryMapInventory(new(runtime))!;
            var stacks = inventory.Ground.Select(g => (Item: g.Item, Holder: (string?)null)).Concat(inventory.Holders.SelectMany(h => h.Items.Select(i => (Item: i, Holder: (string?)h.Entity))))
                .Where(s => s.Item.Facts.Kind.Type == SessionItemType.Ammo && s.Item.Facts.AmmoType == ammoType).ToArray();
            TechSmokeRequire(stacks.Length > 0, $"the map {_readyMapName} holds bullets of type {ammoType} (run this stage on the map \"crash site cave\")");
            var stack = stacks[0];
            var units = stack.Item.Facts.Quantity ?? 0;
            var taken = stack.Holder is null ? _inventoryHost.PickUp(session, stack.Item.Entity)
                : session.TakeAmmoFromNpc(session.Version, stack.Holder, stack.Item.Entity, units, stack.Item.Facts.Kind, _inventoryHost.InsertCheck);
            TechSmokeRequire(AreaInventoryHost.Moved(taken), $"the hero took the bullets ({units} from {stack.Holder ?? "the ground"}): {taken.Disposition} {taken.Failure}");
            var ammoBefore = session.QueryHeroAmmo().Where(a => a.AmmoType == ammoType).Sum(a => a.Units);
            const int MaxShots = 10;
            TechSmokeRequire(ammoBefore >= MaxShots * weapon.AmmoConsumption, $"the hero holds the bullets of {MaxShots} shots ({ammoBefore})");

            // The target: a living NPC with a clear line from a cell three to six cells away.
            static CellCoord World(AreaObjectRecord row) => new(checked((int)row.WorldX!.Value), checked((int)row.WorldY!.Value));
            var npcs = area.Snapshot.Objects.Where(row => row.Type == 16 && row.Scope == AreaScope.Core && row.WorldX is not null && row.WorldY is not null && !IsAreaCompanion(row.Key))
                .Select(row => _combatHost.Target(session, runtime, row.Key))
                .Where(t => t is { Dead: false, Follower: false, Build.Combat: not null } && session.QueryLife(t.Build.Combat!.Target).View?.Status.IsDead != true)
                .Select(t => t!).OrderBy(t => t.Entity, StringComparer.Ordinal).ToArray();
            (AreaCombatTarget Target, CellCoord Hero)? pick = null;
            foreach (var candidate in npcs)
            {
                var at = World(candidate.Row);
                for (var d = 3; d <= 6 && pick is null; d++)
                for (var dy = -d; dy <= d && pick is null; dy++)
                for (var dx = -d; dx <= d && pick is null; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != d) continue;
                    var cell = new CellCoord(at.X + dx, at.Y + dy);
                    if (!lineMap.Contains(cell) || LineOfFireRules.Trace(cell, 0, 0, at, lineMap, session.Rules.Combat).LineFailed) continue;
                    var chart = map.Chart.ToChart(new(cell.X, cell.Y));
                    if (map.Chart.Contains(chart) && ApproachCell(grid, at.X, at.Y, 6, c => c == chart) == chart) pick = (candidate, cell);
                }
                if (pick is not null) break;
            }
            TechSmokeRequire(pick is not null, $"a living NPC with a clear shot from 3 to 6 cells among {npcs.Length}");
            var target = pick!.Value.Target;
            var life = target.Build.Combat!.Target;
            int TargetHitPoints() => session.QueryLife(life).View!.Status.HitPoints;
            AreaSmokePlace(map, map.Chart.ToChart(new(pick.Value.Hero.X, pick.Value.Hero.Y)));
            _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            PublishWorldSelection();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!HeroInCombatMode) ToggleCombatMode();
            TechSmokeRequire(HeroInCombatMode, "the hero is in combat mode");

            var hits = 0; var shots = 0; var misses = new List<string>();
            for (; shots < MaxShots && hits == 0; shots++)
            {
                session.Heal(session.Version, life, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
                var hpBefore = TargetHitPoints();
                _lastCritterAttackForSelfTest = null; _projectileEndsForSelfTest.Clear();
                CancelAreaAttack(null);
                _attack = new(target, target.Build.Combat!, null);
                StartAttackGoal(_attack);
                AttackSettleForSelfTest();
                TechSmokeRequire(_attack is not null && !AttackApproaching, $"the attack starts without a walk: {_attackNotice}");
                var goal = _attack!;
                for (var frames = 0; frames < 4000 && _attack is not null && goal.Attacks == 0; frames++) Frame();
                var attacks = goal.Attacks;
                CancelAreaAttack(null);
                HeroRunsHost()?.LoseAll("SMOKE_STUN_CUT");
                TechSmokeRequire(attacks == 1, $"one shot ran (attacks {attacks}, notice {_attackNotice})");
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                var shot = _lastCritterAttackForSelfTest;
                TechSmokeRequire(shot?.Resolution is { Refusal: AttackRefusal.None }, $"the shot was not refused ({shot?.Failure} {shot?.Resolution?.Refusal})");
                var outcome = shot!.Resolution!.Outcome;
                for (var frames = 0; frames < 400 && (_projectiles.Count > 0 || frames < 40); frames++) Frame();
                if (outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit)
                {
                    hits++;
                    TechSmokeRequire(TargetHitPoints() < hpBefore || session.QueryLife(life).View?.Status.IsDead == true, $"the hit hurt the NPC (hit points {hpBefore} -> {TargetHitPoints()})");
                }
                else misses.Add(outcome.ToString());
            }
            TechSmokeRequire(hits >= 1, $"a firearm shot hit within 10 shots (misses {string.Join(",", misses)})");
            var ammoAfter = session.QueryHeroAmmo().Where(a => a.AmmoType == ammoType).Sum(a => a.Units);
            TechSmokeRequire(ammoAfter == ammoBefore - shots * weapon.AmmoConsumption, $"each shot spent {weapon.AmmoConsumption} bullet(s) ({ammoBefore} -> {ammoAfter} after {shots} shots)");
            if (HeroInCombatMode) ToggleCombatMode();
            session.Heal(session.Version, life, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });

            // 4. F7 and F8: the same degrees, items, worn gun and bullets after the load.
            string[] State(GameSessionOwner s) => [.. Enumerable.Range(0, 8).Select(d => "degree" + d + "=" + TechSmokeDegree(s, d)), .. TechSmokeHeldLines(s),
                "ammo=" + string.Join("+", s.QueryHeroAmmo().Select(a => $"{a.AmmoType}:{a.Units}")), "worn-weapon=" + s.QueryEquipment().Worn.Where(w => w.Slot == EquipmentSlot.Weapon).Select(w => w.Entity).FirstOrDefault()];
            var saved = State(session);
            _saveLibraryForSelfTest = new(saves);
            QuickKey(Key.F7);
            TechSmokeRequire(_quickSaveStatus?.StartsWith("F7 quick saved", StringComparison.Ordinal) == true, "F7 saved: " + _quickSaveStatus);
            for (var i = 0; i < 3; i++) AreaSmokeFrame();
            var running = _areaSession;
            QuickKey(Key.F8);
            TechSmokeRequire(_mapHandoff is not null || _quickSaveStatus?.StartsWith("F8 quick loaded", StringComparison.Ordinal) == true, "F8 loaded: " + _quickSaveStatus);
            if (_mapHandoff is not null) await (_lastLoad ?? Task.CompletedTask);
            for (var i = 0; i < 36000 && _mapHandoff is not null; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            TechSmokeRequire(_mapHandoff is null && !ReferenceEquals(running, _areaSession) && _areaSession is not null, "the load published a new session: " + _quickSaveStatus);
            var loaded = State(_areaSession!);
            TechSmokeRequire(loaded.SequenceEqual(saved), $"the load restored the degrees, items, gun and bullets: lost {string.Join(", ", saved.Except(loaded))}; new {string.Join(", ", loaded.Except(saved))}");
            GD.Print("ARC-DEV-362 native-smoke tech-craft PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Map = _readyMapName, Intelligence = intelligence, Herbology = heals, Gun = new { Record = gunRecord, Shots = shots, Hits = hits, Misses = misses.Count, Bullets = $"{ammoBefore}->{ammoAfter}" },
                Held = saved.Length, Parity = "NOT_VERIFIED (rules read in the original executable; no original-runtime run)" }));
        }
        finally
        {
            if (InventoryOpen) EndInventory();
            if (SheetOpen) EndSheet();
            if (SchematicsOpen) EndSchematics();
            CancelAreaAttack(null);
            if (_mapHandoff is not null) await (_lastLoad ?? Task.CompletedTask);
            if (HeroInCombatMode) ToggleCombatMode();
            _saveLibraryForSelfTest = libraryBefore; _quickKeys.Reset();
            try { System.IO.Directory.Delete(saves, recursive: true); } catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException) { }
            if (_areaSession is { } now) RestoreHeroForSmoke(now, heroBefore);
            if (scaled) _areaSmokeTime = (_areaSmokeTime + 19) / 20;
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            PublishWorldSelection();
        }
    }
}
