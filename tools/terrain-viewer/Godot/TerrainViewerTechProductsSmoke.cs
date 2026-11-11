using Arcanum.Data.Area;
using Arcanum.Data.Technology;
using Arcanum.Session;
using Arcanum.Simulation.Character;
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
    internal const string TechProductsCode = "ARC-MAGIC-051";
    internal const int TechDisciplineChemistry = 1, TechDisciplineTherapeutics = 7;

    // ARC-MAGIC-051 (technology audit ARC-RES-125, slice T-s, first part) native observation (stage "tech-products", only when named, with --hero, on the map "crash site cave"): the
    // technology products the stage "tech-craft" leaves unused, assembled as the player does it (the same steps, TerrainViewerTechCraftHarness.cs) and used on the player's data.
    //  1. Degrees: the hero buys Therapeutics degrees 1 and 2 and Chemistry degree 1 with the sheet tech page's + press.
    //  2. Therapeutics: the first two learned records (food that adds stat effects: record 157, 158) are assembled and one unit of each eaten through the inventory window; the hero's durable
    //     effect list grows and the stat of record 158 (strength) rises by its +2; the unit is gone and the others stay.
    //  3. Chemistry: the first learned record (a poison food, record 171, aggressive) is assembled and used on a living NPC with the item cursor (the use icon, the press on the NPC: goal 39);
    //     the NPC takes the record's damage, the unit is gone.
    //  4. Found schematic: the module's found documents of those two disciplines are read through the inventory's Read button (each registers its schematic and is destroyed; a second copy of a known
    //     one is kept), and the Found tab of the schematic window assembles the first record the hero's expertise reaches.
    //  5. Charge cells: Chemistry degree 2 makes power cells; with intelligence bought to 17 and Electric degrees 1 to 6 the powered shield (degree 3) and the maintained armour (degree 6, record 168) are assembled and
    //     worn: the shield's 15 s call takes one cell, the armour's maintained run a cell a tick (ChargeNBranch). The cell weapon (degree 4, record 147) is not here: its record fires when its wearer is hit, a later step.
    //  6. F7 and F8: the hero's effect list, held and worn items, cells and degrees are the same after the load.
    // The NPC's life and the hero's hit points are put back; the rest (effects, points, degrees, items) is the stage's own state, not restored: the stage runs alone (smoke.py) and its F8 replaces the session.
    private async Task RunAreaTechProductsSmokeAsync()
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _inventoryHost is null || _combatHost is null || _readyArea is not { } area
            || _worldActor.Session is not AreaGridSession grid)
        { GD.Print($"{TechProductsCode} native-smoke tech-products NONE map={_readyMapName} (needs --hero, an area session, its inventory and attack host)"); return; }
        var data = await TechSmokeDataAsync();
        for (var i = 0; i < 1800 && SpellRecordsNow() is null; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        TechProductsRequire(SpellRecordsNow() is not null && ItemTriggersNow() is SessionItemTriggers && SpellCasterNow() is not null, "the spell data and the item-trigger host of the map");
        var caster = SpellCasterNow()!;
        var runtime = CurrentAreaMap;
        var map = new AreaMapRuntime(area.Snapshot, grid.Snapshot, grid.Chart);
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        var scaled = false;
        var saves = System.IO.Directory.CreateTempSubdirectory("arc-magic-051-").FullName;
        var libraryBefore = _saveLibraryForSelfTest;
        var heroBefore = session.QueryHeroLife()!.Record.State;
        SessionLifeTarget? npcLife = null;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            if (InventoryOpen) EndInventory();
            AreaSmokePlace(map, grid.State.CommittedCell);
            scaled = true; _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            PublishWorldSelection();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            void Frame() => TechSmokeFrame();
            int Stat(CharacterStat stat) => session.QueryHeroStats(new(0))!.Value.Snapshot!.GetEffective(stat);
            int Effects() => session.QueryHeroEffects()!.Effects.Count;

            // 1. Degrees.
            var intelligence = Stat(CharacterStat.Intelligence);
            TechProductsRequire(TechSmokeDegree(session, TechDisciplineTherapeutics) == 0 && TechSmokeDegree(session, TechDisciplineChemistry) == 0, "the hero starts without degrees in Therapeutics and Chemistry");
            TechSmokeEnsurePoints(session, 3);
            OpenSheet();
            TechProductsRequire(SheetOpen, "the sheet opens: " + _sheetStatus);
            TechProductsRequire(TechSmokeBuyDegree(TechDisciplineTherapeutics) == 1, $"the tech page's + buys Therapeutics degree 1 (intelligence {intelligence})");
            TechProductsRequire(TechSmokeBuyDegree(TechDisciplineTherapeutics) == 1, $"the tech page's + buys Therapeutics degree 2 (intelligence {intelligence})");
            TechProductsRequire(TechSmokeBuyDegree(TechDisciplineChemistry) == 1, $"the tech page's + buys Chemistry degree 1 (intelligence {intelligence})");
            EndSheet();
            TechProductsRequire(!SheetOpen, "the sheet closed");
            GD.Print($"{TechProductsCode} tech-products degrees therapeutics={TechSmokeDegree(session, TechDisciplineTherapeutics)} chemistry={TechSmokeDegree(session, TechDisciplineChemistry)} intelligence={intelligence}");

            // 2. Therapeutics: assemble each of the first two records and eat one unit.
            var potions = new List<object>();
            for (var degree = 1; degree <= 2; degree++)
            {
                var record = SchematicCatalog.LearnedNumber(TechDisciplineTherapeutics, degree);
                var craft = TechSmokeCraft(data, TechDisciplineTherapeutics, record);
                var products = craft.Made.ToList();
                var product = TechSmokePrototype(session, products[0]);
                GD.Print($"{TechProductsCode} tech-products assembled record={record} units={products.Count} product={product} components={string.Join(",", craft.ComponentPrototypes)}");
                var effectsBefore = Effects(); var strengthBefore = Stat(CharacterStat.Strength);
                OpenInventory();
                TechProductsRequire(InventoryOpen, "the inventory window opens for the use");
                UseHeroItem(products[0], AreaItemUseSource.RightClick, shift: false);
                var used = LastItemUseForSelfTest;
                TechProductsRequire(used is { Gate: AreaItemUseGate.Use, Step: not null } && used.Value.Item == products[0], $"record {record}'s product is used: {used?.Gate} {used?.Step} {used?.Message}");
                for (var frames = 0; frames < 400 && Effects() == effectsBefore; frames++) Frame();
                for (var frames = 0; frames < 40; frames++) Frame();
                if (InventoryOpen) EndInventory();
                var gained = Effects() - effectsBefore; var strength = Stat(CharacterStat.Strength) - strengthBefore;
                TechProductsRequire(gained > 0, $"record {record}'s potion adds effects to the hero (effects {effectsBefore} -> {Effects()}, step {used?.Step})");
                // Record 158 (degree 2) is strength +2.
                TechProductsRequire(degree == 1 || strength == 2, $"record {record}'s potion raises strength by 2 ({strengthBefore} -> {Stat(CharacterStat.Strength)})");
                TechProductsRequire(!TechSmokeHeld(session).Contains(products[0]) && products.Skip(1).All(TechSmokeHeld(session).Contains), $"record {record}'s used unit is gone and the others stay");
                potions.Add(new { Record = record, Units = products.Count, Product = product, EffectsGained = gained, StrengthDelta = strength, Step = used!.Value.Step.ToString() });
            }

            // 3. Chemistry: the poison food on a living NPC through the item cursor.
            var chemRecord = SchematicCatalog.LearnedNumber(TechDisciplineChemistry, 1);
            var poison = TechSmokeCraft(data, TechDisciplineChemistry, chemRecord);
            var poisonEntity = poison.Made[0];
            var poisonProduct = TechSmokePrototype(session, poisonEntity);
            static CellCoord World(AreaObjectRecord row) => new(checked((int)row.WorldX!.Value), checked((int)row.WorldY!.Value));
            var here = caster.Facts(SessionSpellCaster.Hero)?.Location is { } at && at != SpellLocation.None ? new CellCoord(checked((int)at.X), checked((int)at.Y)) : grid.State.CommittedCell is var c ? new CellCoord(c.X, c.Y) : default;
            var npc = area.Snapshot.Objects.Where(row => row.Type == 16 && row.Scope == AreaScope.Core && row.WorldX is not null && row.WorldY is not null && !IsAreaCompanion(row.Key))
                .Select(row => (Row: row, Id: SpellPickId(row.Key), Target: _combatHost.Target(session, runtime, row.Key)))
                .Where(o => o.Id is not null && o.Target is { Dead: false, Follower: false, Build.Combat: not null } && caster.Facts(o.Id)?.Location is { } there && there != SpellLocation.None)
                .OrderBy(o => Math.Max(Math.Abs(World(o.Row).X - here.X), Math.Abs(World(o.Row).Y - here.Y))).FirstOrDefault();
            TechProductsRequire(npc.Id is not null, "a living NPC with a cell to walk to");
            var npcId = npc.Id!;
            var life = npc.Target!.Build.Combat!.Target;
            npcLife = life;
            int NpcHitPoints() => session.QueryLife(life).View!.Status.HitPoints;
            var npcBefore = NpcHitPoints();
            var poisonSpells = HeroItemSpells(poisonEntity); var spellRecords = SpellRecordsNow()!;
            foreach (var number in (poisonSpells?.Spells ?? []).Where(n => n >= 0 && n < spellRecords.Length && spellRecords[n] is not null))
                GD.Print($"{TechProductsCode} tech-products spell={number} charges={poisonSpells!.Charges} begin=" + string.Join(";", spellRecords[number]!.Of(SpellRunAction.Begin)
                    .Select(c => c.Type + (c.Damage is { } d ? $":{d.Minimum}-{d.Maximum}/type{d.DamageType}/flags{(int)d.Flags}" : ""))));
            var recordDamage = poisonSpells?.Spells.Where(n => n >= 0 && n < spellRecords.Length && spellRecords[n] is not null)
                .SelectMany(n => spellRecords[n]!.Of(SpellRunAction.Begin)).Select(c => c.Damage?.Minimum ?? 0).Sum() ?? 0;
            OpenInventory();
            TechProductsRequire(InventoryOpen, "the inventory window opens for the poison");
            _itemAimLast = null;
            UseHeroItem(poisonEntity, AreaItemUseSource.UseIcon, shift: false);
            TechProductsRequire(_itemAiming is { } aim && aim.Item == poisonEntity, $"the use icon opened the item cursor for the poison: {_itemAimLast} {_inventoryStatus}");
            ApplyItemAim(_itemAiming!, npcId, SpellLocation.None, false, false);
            TechProductsRequire(!ItemTargetOpen, "the apply closed the cursor");
            for (var frames = 0; frames < 6000 && TechSmokeHeld(session).Contains(poisonEntity); frames++) Frame();
            for (var frames = 0; frames < 200; frames++) Frame();
            var poisoned = npcBefore - NpcHitPoints();
            GD.Print($"{TechProductsCode} tech-products poison record={chemRecord} product={poisonProduct} npc={npc.Row.Key} record-damage={recordDamage} hit-points={npcBefore}->{NpcHitPoints()} notice={_itemAimLast}");
            TechProductsRequire(!TechSmokeHeld(session).Contains(poisonEntity), $"the poison was used up ({_itemAimLast})");
            // The NPC's hit points are measured from before the use to the end of the walk and the run; an NPC that is not in a fight with the hero has no other source of damage in the cave.
            TechProductsRequire(poisoned > 0 && recordDamage >= npcBefore && session.QueryLife(life).View!.Status.IsDead, $"the poison killed the NPC (record damage {recordDamage}, hit points {npcBefore} -> {NpcHitPoints()})");

            // 4. A found schematic document: every document of the module for Therapeutics and Chemistry (the disciplines the stage bought degrees in) is read through the inventory's Read button (the schematic registers, the
            //    document is gone, a second copy of a known one stays), then the Found tab of the window lists the records and the first one the hero's expertise reaches is assembled.
            for (var i = 0; i < 1800 && _readingData is null; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); AdoptReadingData(); }
            TechProductsRequire(_readingData is { Status: AreaReadingDataStatus.Ready, Data: not null }, $"the reading data of the module ({_readingData?.Status} {_readingData?.Detail})");
            var documents = _readingData!.Data!.Prototypes.Where(p => p.Value.Subtype == (int)Arcanum.Data.Written.WrittenSubtype.Schematic && p.Value.TextStartLine >= SchematicCatalog.FirstFound)
                .Select(p => (Prototype: p.Key, Number: p.Value.TextStartLine, Discipline: data.Catalog.Discipline(p.Value.TextStartLine, data.Discipline) ?? -1))
                .Where(d => d.Discipline is TechDisciplineTherapeutics or TechDisciplineChemistry).OrderBy(d => d.Number).ToArray();
            GD.Print($"{TechProductsCode} tech-products found documents " + string.Join(";", documents.Select(d => $"{d.Prototype}={d.Number}/d{d.Discipline}")));
            TechProductsRequire(documents.Length > 0, "the module has found schematic documents for Therapeutics or Chemistry");
            var foundBefore = session.QueryHeroFoundSchematics()!.Value.Length;
            foreach (var document in documents)
            {
                var entity = TechSmokeGive(data, document.Prototype, new SessionItemKind(SessionItemType.Written, document.Prototype, 0, 0));
                OpenInventory();
                TechProductsRequire(InventoryOpen, "the inventory window opens for the document");
                _readingStatus = null;
                ReadItem(entity);
                TechProductsRequire(_readingStatus is not null || _inventoryStatus is not null, $"document {document.Prototype} was read");
                TechProductsRequire(session.QueryHeroFoundSchematics()!.Value.Contains(document.Number) && !TechSmokeHeld(session).Contains(entity),
                    $"reading document {document.Prototype} registered schematic {document.Number} and destroyed the document: {_readingStatus}");
                if (InventoryOpen) EndInventory();
            }
            TechProductsRequire(session.QueryHeroFoundSchematics()!.Value.Length == foundBefore + documents.Length, "each document registered one schematic");
            var again = TechSmokeGive(data, documents[0].Prototype, new SessionItemKind(SessionItemType.Written, documents[0].Prototype, 0, 0));
            OpenInventory();
            TechProductsRequire(InventoryOpen, "the inventory window opens for the second copy");
            _readingStatus = null;
            ReadItem(again);
            TechProductsRequire(_readingStatus is not null && TechSmokeHeld(session).Contains(again) && session.QueryHeroFoundSchematics()!.Value.Length == foundBefore + documents.Length, $"a second copy of a known schematic is kept and registers nothing: {_readingStatus}");
            if (InventoryOpen) EndInventory();
            TechSmokeCraftResult? foundCraft = null; var buttons = new List<string>();
            foreach (var document in documents)
            {
                var attempt = TechSmokeCraft(data, document.Discipline, document.Number, AreaSchematicTab.Found, probe: true);
                buttons.Add($"{document.Number}:{(attempt.Made.Length > 0 ? "assembled" : attempt.Message)}");
                if (attempt.Made.Length > 0) { foundCraft = attempt; break; }
            }
            GD.Print($"{TechProductsCode} tech-products found buttons " + string.Join(";", buttons));
            TechProductsRequire(foundCraft is not null, "the Found tab assembled a record the hero's expertise reaches: " + string.Join(";", buttons));

            // 5. Charge cells: Chemistry degree 2 makes the power cells, Electric degrees 1 to 6 (intelligence bought up to the 17 degree 6 asks) make the powered shield (degree 3) and the maintained armour (degree 6).
            const int CellAmmoType = 2, TechDisciplineElectric = 2;
            int Cells() => session.QueryHeroAmmo().Where(a => a.AmmoType == CellAmmoType).Sum(a => a.Units);
            var intelligenceNeed = 17 - Stat(CharacterStat.Intelligence);
            TechSmokeEnsurePoints(session, Math.Max(0, intelligenceNeed) + 7);
            OpenSheet();
            TechProductsRequire(SheetOpen, "the sheet opens for the electric degrees: " + _sheetStatus);
            for (var i = 0; i < intelligenceNeed; i++) PressStat(6, spend: true);
            TechProductsRequire(Stat(CharacterStat.Intelligence) >= 17, $"the stats page's + raised intelligence to 17 ({Stat(CharacterStat.Intelligence)})");
            TechProductsRequire(TechSmokeBuyDegree(TechDisciplineChemistry) == 1, "the tech page's + buys Chemistry degree 2");
            for (var degree = 1; degree <= 6; degree++) TechProductsRequire(TechSmokeBuyDegree(TechDisciplineElectric) == 1, $"the tech page's + buys Electric degree {degree}");
            EndSheet();
            var cellRecord = SchematicCatalog.LearnedNumber(TechDisciplineChemistry, 2);
            var cellCraft = TechSmokeCraft(data, TechDisciplineChemistry, cellRecord);
            var cellsMade = Cells();
            TechProductsRequire(cellsMade > 0 && session.QueryHeroAmmo().Any(a => a.AmmoType == CellAmmoType), $"the Chemistry degree 2 product is power cells ({cellsMade})");
            GD.Print($"{TechProductsCode} tech-products cells record={cellRecord} made={cellCraft.Made.Length} cells={cellsMade}");

            // The shield (the hero's own two-handed weapon is taken off first, it refuses a shield): worn, it asks for its cell every 15 s of the game clock.
            var shieldRecord = SchematicCatalog.LearnedNumber(TechDisciplineElectric, 3);
            var shield = TechSmokeCraft(data, TechDisciplineElectric, shieldRecord).Made[0];
            OpenInventory();
            TakeOffItem(EquipmentSlot.Weapon);
            TechProductsRequire(InventoryOpen && _inventoryHost.Wearable(session, shield, _areaKitWear) is not null, "the made shield has wear values");
            WearItem(shield, AreaInventoryHost.WearSlots(_inventoryHost.Wearable(session, shield, _areaKitWear)!)[0]);
            EndInventory();
            TechProductsRequire(session.QueryEquipment().Worn.Any(w => w.Entity == shield && w.Slot == EquipmentSlot.Shield), $"the powered shield is worn (it could not be before ARC-MAGIC-041): {_inventoryStatus}");
            var cellsWorn = Cells();
            GD.Print($"{TechProductsCode} tech-products shield calls clock={session.QueryClockSnapshot().GameTimestampMs} pending={string.Join(";", session.QueryScriptCalls().Select(c => $"{c.Call.Script}:{c.DeadlineMs}"))}");
            for (var step = 0; step < 4 && Cells() == cellsWorn; step++)
            {
                TechProductsRequire(session.JumpClocks(session.QueryClockSnapshot().Token, 8L * 16000).Disposition == Arcanum.Simulation.Clock.ClockMutationDisposition.Changed, "the game clock moved by 16 script seconds (8 ms of game clock to the script ms)");
                caster.ProcessEvents();
                for (var frames = 0; frames < 200 && Cells() == cellsWorn; frames++) Frame();
            }
            GD.Print($"{TechProductsCode} tech-products shield calls after clock={session.QueryClockSnapshot().GameTimestampMs} pending={string.Join(";", session.QueryScriptCalls().Select(c => $"{c.Call.Script}:{c.DeadlineMs}"))} refusals={AreaEvents.CallRefusals} last={AreaEvents.LastCallRefusal}");
            TechProductsRequire(Cells() == cellsWorn - 1, $"the worn shield's 15 s call took one cell ({cellsWorn} -> {Cells()})");
            GD.Print($"{TechProductsCode} tech-products shield record={shieldRecord} cells={cellsWorn}->{Cells()}");


            // The maintained armour (Electric degree 6, record 168): worn, its maintained run pays a cell a tick (5 s of the game clock) through ChargeNBranch.
            var armourRecord = SchematicCatalog.LearnedNumber(TechDisciplineElectric, 6);
            var armour = TechSmokeCraft(data, TechDisciplineElectric, armourRecord).Made[0];
            OpenInventory();
            TechProductsRequire(InventoryOpen && _inventoryHost.Wearable(session, armour, _areaKitWear) is not null, "the made armour has wear values");
            WearItem(armour, AreaInventoryHost.WearSlots(_inventoryHost.Wearable(session, armour, _areaKitWear)!)[0]);
            EndInventory();
            TechProductsRequire(session.QueryEquipment().Worn.Any(w => w.Entity == armour), $"the maintained armour is worn: {_inventoryStatus}");
            var cellsArmour = Cells();
            for (var step = 0; step < 4 && Cells() >= cellsArmour; step++)
            {
                TechProductsRequire(session.JumpClocks(session.QueryClockSnapshot().Token, 8L * 6000).Disposition == Arcanum.Simulation.Clock.ClockMutationDisposition.Changed, "the game clock moved by 6 script seconds");
                caster.ProcessEvents();
                for (var frames = 0; frames < 200 && Cells() >= cellsArmour; frames++) Frame();
            }
            TechProductsRequire(Cells() < cellsArmour, $"the worn maintained armour paid a cell a tick ({cellsArmour} -> {Cells()})");
            GD.Print($"{TechProductsCode} tech-products armour record={armourRecord} cells={cellsArmour}->{Cells()}");

            // 6. F7 and F8: the same effects and items after the load.
            string[] State(GameSessionOwner s) => [.. s.QueryHeroEffects()!.Effects.Select(e => "effect=" + e.Number).OrderBy(l => l, StringComparer.Ordinal), .. TechSmokeHeldLines(s), "cells=" + s.QueryHeroAmmo().Where(a => a.AmmoType == 2).Sum(a => a.Units), .. s.QueryEquipment().Worn.Select(w => $"worn={w.Slot}:{w.Entity}"),
                .. Enumerable.Range(0, 8).Select(d => "degree" + d + "=" + TechSmokeDegree(s, d))];
            var saved = State(session);
            _saveLibraryForSelfTest = new(saves);
            QuickKey(Key.F7);
            TechProductsRequire(_quickSaveStatus?.StartsWith("F7 quick saved", StringComparison.Ordinal) == true, "F7 saved: " + _quickSaveStatus);
            for (var i = 0; i < 3; i++) AreaSmokeFrame();
            var running = _areaSession;
            QuickKey(Key.F8);
            TechProductsRequire(_mapHandoff is not null || _quickSaveStatus?.StartsWith("F8 quick loaded", StringComparison.Ordinal) == true, "F8 loaded: " + _quickSaveStatus);
            if (_mapHandoff is not null) await (_lastLoad ?? Task.CompletedTask);
            for (var i = 0; i < 36000 && _mapHandoff is not null; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            TechProductsRequire(_mapHandoff is null && !ReferenceEquals(running, _areaSession) && _areaSession is not null, "the load published a new session: " + _quickSaveStatus);
            var loaded = State(_areaSession!);
            TechProductsRequire(loaded.SequenceEqual(saved), $"the load restored the effects, items and degrees: lost {string.Join(", ", saved.Except(loaded))}; new {string.Join(", ", loaded.Except(saved))}");
            GD.Print("ARC-MAGIC-051 native-smoke tech-products PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Map = _readyMapName, Intelligence = intelligence, Therapeutics = potions, Chemistry = new { Record = chemRecord, Product = poisonProduct, Damage = recordDamage }, Found = new { Documents = documents.Length, Assembled = foundCraft!.Record }, Cells = new { Shield = shieldRecord, Armour = armourRecord }, StateLines = saved.Length }));
        }
        finally
        {
            if (InventoryOpen) EndInventory();
            if (SheetOpen) EndSheet();
            if (SchematicsOpen) EndSchematics();
            CancelArmedLockpicks("the smoke ended");
            HeroRunsHost()?.LoseAll("SMOKE_TECH_PRODUCTS_END");
            if (_mapHandoff is not null) await (_lastLoad ?? Task.CompletedTask);
            _saveLibraryForSelfTest = libraryBefore; _quickKeys.Reset();
            try { System.IO.Directory.Delete(saves, recursive: true); } catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException) { }
            if (_areaSession is { } now)
            {
                if (npcLife is { } npcTarget) now.Heal(now.Version, npcTarget, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
                RestoreHeroForSmoke(now, heroBefore);
            }
            if (scaled) _areaSmokeTime = (_areaSmokeTime + 19) / 20;
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            PublishWorldSelection();
        }
    }

    private static void TechProductsRequire(bool value, string why)
    {
        if (value) return;
        GD.Print($"{TechProductsCode} tech-products smoke FAIL: " + why);
        throw new InvalidOperationException("AREA_TECH_PRODUCTS_SMOKE_FAILED");
    }
}
