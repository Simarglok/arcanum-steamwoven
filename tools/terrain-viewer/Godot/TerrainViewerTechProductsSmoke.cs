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
    internal const int TechDisciplineChemistry = 1, TechDisciplineExplosives = 3, TechDisciplineTherapeutics = 7;

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
    //     worn: the shield's 15 s call takes one cell, the armour's maintained run a cell a tick (ChargeNBranch).
    //  6. NPC crafter: the window queued the way `su` queues it for the cave NPC (given a Herbology degree by the stage: the cave's NPCs have none) assembles a learned record from the NPC's own list; products to the NPC, hero's pack unchanged.
    //  7. Explosives: the degree-1 grenade (record 219) is assembled, thrown at a cave NPC from three to six cells (item event, run, damage on a cave NPC, grenade gone).
    //  7b. Cell weapon (ARC-MAGIC-060): the Electric degree-4 weapon (record 147, Target_Hit) is wielded with the armour off and swung at the cave NPC until it dies; the firing of a hit pays its cell(s) and shocks the NPC through the record
    //     (the original fires Target_Hit on the items of the one who damages an opponent, 0x4CBE70). The weapon comes off and the armour goes back on.
    //  8. F7 and F8: the hero's effect list, held and worn items, cells and degrees, and the NPC's list are the same after the load.
    // The hero's hit points and the cave NPCs' lives (all of them, after the grenade) are put back; the rest (effects, points, degrees, items) is the stage's own state, not restored: the stage runs alone (smoke.py) and its F8 replaces the session.
    private async Task RunAreaTechProductsSmokeAsync()
    {
        if (_areaSession is not { } session || session.QueryHero() is null || _inventoryHost is null || _combatHost is null || _readyArea is not { } area
            || _worldActor.Session is not AreaGridSession grid)
        { GD.Print($"{TechProductsCode} native-smoke tech-products NONE map={_readyMapName} (needs --hero, an area session, its inventory and attack host)"); return; }
        var data = await TechSmokeDataAsync();
        for (var i = 0; i < 1800 && SpellRecordsNow() is null; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        TechProductsRequire(SpellRecordsNow() is not null && ItemTriggersNow() is SessionItemTriggers && SpellCasterNow() is not null, "the spell data and the item-trigger host of the map");
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
            var here = SpellCasterNow()!.Facts(SessionSpellCaster.Hero)?.Location is { } at && at != SpellLocation.None ? new CellCoord(checked((int)at.X), checked((int)at.Y)) : grid.State.CommittedCell is var c ? new CellCoord(c.X, c.Y) : default;
            var npc = area.Snapshot.Objects.Where(row => row.Type == 16 && row.Scope == AreaScope.Core && row.WorldX is not null && row.WorldY is not null && !IsAreaCompanion(row.Key))
                .Select(row => (Row: row, Id: SpellPickId(row.Key), Target: _combatHost.Target(session, runtime, row.Key)))
                .Where(o => o.Id is not null && o.Target is { Dead: false, Follower: false, Build.Combat: not null } && SpellCasterNow()!.Facts(o.Id)?.Location is { } there && there != SpellLocation.None)
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
            TechSmokeEnsurePoints(session, Math.Max(0, intelligenceNeed) + 8);
            OpenSheet();
            TechProductsRequire(SheetOpen, "the sheet opens for the electric degrees: " + _sheetStatus);
            for (var i = 0; i < intelligenceNeed; i++) PressStat(6, spend: true);
            TechProductsRequire(Stat(CharacterStat.Intelligence) >= 17, $"the stats page's + raised intelligence to 17 ({Stat(CharacterStat.Intelligence)})");
            TechProductsRequire(TechSmokeBuyDegree(TechDisciplineChemistry) == 1, "the tech page's + buys Chemistry degree 2");
            TechProductsRequire(TechSmokeBuyDegree(TechDisciplineExplosives) == 1, "the tech page's + buys Explosives degree 1");
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
                SpellCasterNow()!.ProcessEvents();
                for (var frames = 0; frames < 200 && Cells() == cellsWorn; frames++) Frame();
            }
            GD.Print($"{TechProductsCode} tech-products shield calls after clock={session.QueryClockSnapshot().GameTimestampMs} pending={string.Join(";", session.QueryScriptCalls().Select(c => $"{c.Call.Script}:{c.DeadlineMs}"))} refusals={AreaEvents.CallRefusals} last={AreaEvents.LastCallRefusal}");
            TechProductsRequire(Cells() == cellsWorn - 1, $"the worn shield's 15 s call took one cell ({cellsWorn} -> {Cells()})");
            // The timer goes on: the next call, 15 script seconds later, takes a second cell (the wear question answered for the assembled shield).
            TechProductsRequire(session.JumpClocks(session.QueryClockSnapshot().Token, 8L * 16000).Disposition == Arcanum.Simulation.Clock.ClockMutationDisposition.Changed, "the game clock moved by 16 more script seconds");
            for (var frames = 0; frames < 200 && Cells() == cellsWorn - 1; frames++) Frame();
            TechProductsRequire(Cells() == cellsWorn - 2, $"the shield's next call took a second cell ({cellsWorn} -> {Cells()})");
            GD.Print($"{TechProductsCode} tech-products shield record={shieldRecord} cells={cellsWorn}->{Cells()}");


            // The maintained armour (Electric degree 6, record 168): worn, its maintained run pays a cell a tick (5 s of the game clock) through ChargeNBranch.
            // The shield comes off first: its timer must not be the one that takes the cell.
            OpenInventory();
            TakeOffItem(EquipmentSlot.Shield);
            EndInventory();
            TechProductsRequire(session.QueryEquipment().Worn.All(w => w.Slot != EquipmentSlot.Shield), "the shield is off");
            var armourRecord = SchematicCatalog.LearnedNumber(TechDisciplineElectric, 6);
            var armour = TechSmokeCraft(data, TechDisciplineElectric, armourRecord).Made[0];
            OpenInventory();
            TechProductsRequire(InventoryOpen && _inventoryHost.Wearable(session, armour, _areaKitWear) is not null, "the made armour has wear values");
            WearItem(armour, AreaInventoryHost.WearSlots(_inventoryHost.Wearable(session, armour, _areaKitWear)!)[0]);
            EndInventory();
            TechProductsRequire(session.QueryEquipment().Worn.Any(w => w.Entity == armour), $"the maintained armour is worn: {_inventoryStatus}");
            var cellsArmour = Cells();
            for (var step = 0; step < 1 && Cells() >= cellsArmour; step++)
            {
                TechProductsRequire(session.JumpClocks(session.QueryClockSnapshot().Token, 8L * 6000).Disposition == Arcanum.Simulation.Clock.ClockMutationDisposition.Changed, "the game clock moved by 6 script seconds");
                SpellCasterNow()!.ProcessEvents();
                for (var frames = 0; frames < 200 && Cells() >= cellsArmour; frames++) Frame();
            }
            TechProductsRequire(Cells() < cellsArmour, $"the worn maintained armour paid a cell a tick ({cellsArmour} -> {Cells()})");
            GD.Print($"{TechProductsCode} tech-products armour record={armourRecord} cells={cellsArmour}->{Cells()}");

            // 6. An NPC crafter, as `su` opens it: the window is queued the way the conversation's `su` queues it (QueueDialogSchematics ends in the same field) for the cave NPC, whose own sheet has no technology
            //    degree (the cave's NPCs have none), so the stage gives the crafter facts a Herbology degree 1 and intelligence 8 (the one made-up fact); the components are put in the NPC's list through the session,
            //    the window assembles from the NPC's list and the products go to the NPC, the hero's pack and the components in it unchanged.
            session.Heal(session.Version, life, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
            var caveFacts = AreaNpcCrafter(npcId);
            TechProductsRequire(caveFacts is not null, "the cave NPC has crafter facts");
            var cave = caveFacts!;
            var degrees = cave.Loaded.Sheet.TechDegrees.IsDefaultOrEmpty ? new int[8] : cave.Loaded.Sheet.TechDegrees.ToArray();
            TechProductsRequire(degrees.All(d => d == 0), $"the cave NPC has no degree of its own ({string.Join(",", degrees)})");
            degrees[TechDisciplineHerbology] = 1;
            var stats = cave.Loaded.Sheet.Stats.ToArray(); stats[(int)CharacterStat.Intelligence] = Math.Max(stats[(int)CharacterStat.Intelligence], 8);
            TechProductsRequire(session.QueryNpcAdvancement(npcId) is null, "the session keeps no advancement record of the cave NPC (it would override the stage's crafter facts)");
            var smith = new SessionNpcCrafter(cave.Loaded with { Sheet = cave.Loaded.Sheet with { TechDegrees = [.. degrees], Stats = [.. stats] } }, cave.Modifiers);
            var npcRecord = SchematicCatalog.LearnedNumber(TechDisciplineHerbology, 1);
            var npcRead = data.Catalog.Read(npcRecord).Record!;
            var npcComponents = new[] { TechSmokeCheapest(data, npcRead.Component1.Bases), TechSmokeCheapest(data, npcRead.Component2.Bases) };
            var heroLinesBefore = TechSmokeHeldLines(session);
            var npcListBefore = session.QueryNpcInventory(npcId).Select(i => i.Entity).ToHashSet(StringComparer.Ordinal);
            var given = npcComponents.Select(prototype =>
            {
                var added = session.AddItemToNpc(session.Version, npcId, data.Kind(prototype)!.Value, AreaInventoryHost.DiagnosticInsertCheck);
                TechProductsRequire(added.Disposition == GameSessionDisposition.Changed && added.Item is not null, $"the NPC was given prototype {prototype}: {added.Disposition} {added.Failure}");
                return added.Item!;
            }).ToArray();
            _schematicScriptOpen = (session, smith);
            for (var frames = 0; frames < 600 && !SchematicsOpen; frames++) { Frame(); TakeScriptSchematics(); }
            TechProductsRequire(SchematicsOpen && _schematicCrafter is not null, "the crafter's window opened from the queued su: " + _schematicsStatus);
            SelectSchematicDiscipline(TechDisciplineHerbology);
            TechProductsRequire(_schematicsPanel!.View is { Placeholder: false } npcView && npcView.Record == npcRecord && npcView.Crafter == npcId, $"the window shows record {npcRecord} of the NPC ({_schematicsPanel.View?.Record} {_schematicsPanel.View?.Crafter})");
            TechProductsRequire(_schematicsPanel.View!.Button == Arcanum.Simulation.Technology.AssemblyButton.CanAssemble, $"the NPC's list holds the components ({_schematicsPanel.View.Button})");
            AssembleSchematic();
            var npcMessage = _schematicMessage ?? "";
            TechProductsRequire(!npcMessage.StartsWith("not assembled", StringComparison.Ordinal), $"the NPC assembled record {npcRecord}: {npcMessage}");
            EndSchematics(byPlayer: true);
            var npcAfter = session.QueryNpcInventory(npcId).Select(i => i.Entity).ToHashSet(StringComparer.Ordinal);
            var npcMade = npcAfter.Except(npcListBefore).Except(given).ToArray();
            TechProductsRequire(npcMade.Length == npcRead.Quantity && given.All(e => !npcAfter.Contains(e)), $"the products ({npcMade.Length} of {npcRead.Quantity}) are in the NPC's list and the components are gone from it");
            TechProductsRequire(TechSmokeHeldLines(session).SequenceEqual(heroLinesBefore), "the hero's pack is unchanged by the NPC's assembly");
            GD.Print($"{TechProductsCode} tech-products npc-crafter record={npcRecord} made={npcMade.Length} message={npcMessage}");

            // 7. Explosives: the degree-1 grenade (record 219: fire and normal damage, knockback, a flash summon, a destroy) is assembled, lifted onto the game cursor and thrown at the cave NPC from three to six
            //    cells away with a clear line; its item event starts the record's run, the run's damage lands on a cave NPC (or is resisted) and the grenade is gone afterwards.
            var lineMap = AreaLineOfFireNow();
            TechProductsRequire(lineMap is not null, "the map's line-of-fire data");
            var caveNpcs = area.Snapshot.Objects.Where(r => r.Type == 16 && r.Scope == AreaScope.Core && !IsAreaCompanion(r.Key)).Select(r => SpellPickId(r.Key)).Where(id => id is not null).Select(id => id!).ToHashSet(StringComparer.Ordinal);
            CellCoord? throwFrom = null; var aimedAt = World(npc.Row);
            for (var d = 3; d <= 6 && throwFrom is null; d++)
            for (var dy = -d; dy <= d && throwFrom is null; dy++)
            for (var dx = -d; dx <= d && throwFrom is null; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != d) continue;
                var from = new CellCoord(aimedAt.X + dx, aimedAt.Y + dy);
                if (!lineMap!.Contains(from) || LineOfFireRules.Trace(from, 0, 0, aimedAt, lineMap, session.Rules.Combat).LineFailed) continue;
                var chart = map.Chart.ToChart(new(from.X, from.Y));
                if (map.Chart.Contains(chart) && ApproachCell(grid, aimedAt.X, aimedAt.Y, 6, c => c == chart) == chart) throwFrom = from;
            }
            TechProductsRequire(throwFrom is not null, "a clear line from three to six cells to the cave NPC");
            session.Heal(session.Version, life, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
            TechSmokeAmmoNpc = npcId;
            var grenadeRecord = SchematicCatalog.LearnedNumber(TechDisciplineExplosives, 1);
            AreaSmokePlace(map, map.Chart.ToChart(new(throwFrom!.Value.X, throwFrom.Value.Y)));
            _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            PublishWorldSelection();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            // The flight lands off the aimed tile by a fixed offset from where the hero stands (the grenade smoke prints it as the flight's, not judged): the first throw learns it, the next aims so that it lands on the NPC.
            var grenadeAim = aimedAt; var grenadeHurt = Array.Empty<string>(); var grenades = 0; var grenade = "";
            // ARC-WORLD-1149: the cave is dark, so a throw at the NPC would meet the darkness penalty and miss; this stage judges the grenade's run, not the hit chance.
            _cellLightForSelfTest = Arcanum.Simulation.Combat.AttackRules.MaxLightLevel;
            for (; grenades < 3 && grenadeHurt.Length == 0; grenades++)
            {
                session.Heal(session.Version, life, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
                session.Heal(session.Version, SessionLifeTarget.Hero, new LifeHeal { Full = true, Fatigue = 1000 });
                AreaSmokePlace(map, map.Chart.ToChart(new(throwFrom!.Value.X, throwFrom.Value.Y)));
                _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
                PublishWorldSelection();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                grenade = TechSmokeCraft(data, TechDisciplineExplosives, grenadeRecord).Made[0];
                SyncAreaInventory();
                var grenadeSpells = HeroItemSpells(grenade);
                foreach (var number in (grenadeSpells?.Spells ?? []).Where(n => n >= 0 && n < spellRecords.Length && spellRecords[n] is not null))
                    GD.Print($"{TechProductsCode} tech-products grenade spell={number} triggers=0x{spellRecords[number]!.Data.ItemTriggers:X} charges={grenadeSpells!.Charges} begin=" + string.Join(";", spellRecords[number]!.Of(SpellRunAction.Begin).Select(c => c.Type.ToString())));
                TechProductsRequire(HoldItemOnGameCursor(grenade, "smoke") is null && GameCursorItemForSelfTest == grenade, $"the grenade is on the game cursor ({HeldThrowNoticeForSelfTest})");
                _projectileEndsForSelfTest.Clear(); _thrownLandingsForSelfTest.Clear(); _shotEndItemEventsForSelfTest.Clear(); _lastHeldThrowForSelfTest = null;
                var grenadeLog = SpellCasterNow()!.Log.Count;
                TechProductsRequire(ThrowGameCursorItem(grenadeAim), $"the throw started ({HeldThrowNoticeForSelfTest})");
                for (var frames = 0; frames < 1500 && _lastHeldThrowForSelfTest is null && HeroRunsHost() is { ThrowRun: not null }; frames++) Frame();
                TechProductsRequire(_lastHeldThrowForSelfTest is { Disposition: GameSessionDisposition.Changed }, $"the grenade was thrown ({_lastHeldThrowForSelfTest?.Disposition} {_lastHeldThrowForSelfTest?.Failure}, {HeldThrowNoticeForSelfTest})");
                for (var frames = 0; frames < 1500 && (_projectiles.Count > 0 || HeroRunsHost() is { ThrowRun: not null }); frames++) Frame();
                TechProductsRequire(_projectiles.Count == 0, "the flight ended");
                for (var frames = 0; frames < 400 && SpellCasterNow()!.Log.Count == grenadeLog; frames++) Frame();
                for (var frames = 0; frames < 200; frames++) Frame();
                var grenadeEvents = _shotEndItemEventsForSelfTest.ToArray();
                grenadeHurt = [.. SpellCasterNow()!.Log.Skip(grenadeLog).Where(e => e.Kind is SpellCastEventKind.Damaged or SpellCastEventKind.Resisted && e.Object is not null && caveNpcs.Contains(e.Object)).Select(e => e.Object!).Distinct()];
                var landed = _thrownLandingsForSelfTest.Count > 0 ? _thrownLandingsForSelfTest[0].Tile : default;
                GD.Print($"{TechProductsCode} tech-products grenade record={grenadeRecord} throw={grenades} events={grenadeEvents.Length} started={string.Join(",", grenadeEvents.Select(e => e.Started))} hurt={grenadeHurt.Length} landed={landed.X},{landed.Y} aimed={grenadeAim.X},{grenadeAim.Y}");
                TechProductsRequire(grenadeEvents.Any(e => e.Started > 0), $"the grenade's item event started a spell run (events {grenadeEvents.Length})");
                TechProductsRequire(!session.QueryPlacedItems().Any(p => p.Entity == grenade) && !session.QueryPcInventory().Any(i => i.Entity == grenade) && session.IsItemDestroyed(grenade), "the grenade is gone after the explosion");
                if (grenadeHurt.Length == 0 && _thrownLandingsForSelfTest.Count > 0) grenadeAim = new CellCoord(aimedAt.X - (landed.X - grenadeAim.X), aimedAt.Y - (landed.Y - grenadeAim.Y));
            }
            _cellLightForSelfTest = null;
            TechProductsRequire(grenadeHurt.Length > 0, $"the grenade's run damaged (or was resisted by) a cave NPC within {grenades} throws");
            if (GameCursorItemForSelfTest == grenade) _gameCursorItem = null;
            ClearSpellMissiles("the smoke's grenade ended");
            foreach (var id in caveNpcs) if (_combatHost.Target(session, runtime, area.Snapshot.Objects.First(r => r.Type == 16 && SpellPickId(r.Key) == id).Key) is { Build.Combat: not null } hurtTarget)
                session.Heal(session.Version, hurtTarget.Build.Combat!.Target, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });

            // The cell weapon (Electric degree 4, record 147, event Target_Hit|Target_Attacker): the original fires Target_Hit on the items of the one that damages an opponent (0x4CBE70 walks the attacker's worn
            // items), so the wielded weapon pays its cell(s) through ChargeNBranch and shocks the struck NPC at each hit, once per hit. The armour comes off so that only the weapon's run takes cells.
            OpenInventory();
            TakeOffItem(EquipmentSlot.Armor);
            EndInventory();
            TechProductsRequire(session.QueryEquipment().Worn.All(w => w.Slot != EquipmentSlot.Armor), "the maintained armour is off");
            var weaponRecord = SchematicCatalog.LearnedNumber(TechDisciplineElectric, 4);
            var weapon = TechSmokeCraft(data, TechDisciplineElectric, weaponRecord).Made[0];
            OpenInventory();
            TechProductsRequire(InventoryOpen && _inventoryHost.Wearable(session, weapon, _areaKitWear) is not null, "the made cell weapon has wear values");
            WearItem(weapon, EquipmentSlot.Weapon);
            EndInventory();
            TechProductsRequire(session.QueryEquipment().Worn.Any(w => w.Entity == weapon && w.Slot == EquipmentSlot.Weapon), $"the cell weapon is wielded: {_inventoryStatus}");
            const int CellWeaponSpell = 147;
            GD.Print($"{TechProductsCode} tech-products cell-weapon spell={CellWeaponSpell} begin=" + string.Join(";", SpellRecordsNow()![CellWeaponSpell]!.Of(SpellRunAction.Begin).Select(c => c.Type + (c.Damage is { } d ? $":{d.Minimum}-{d.Maximum}/type{d.DamageType}" : ""))));
            var weaponSpells = HeroItemSpells(weapon);
            TechProductsRequire(weaponSpells is not null && weaponSpells.Spells.Contains(CellWeaponSpell) && SpellRecordsNow()![CellWeaponSpell] is { } weaponSpell && (weaponSpell.Data.ItemTriggers & 0x40000u) != 0,
                $"the made weapon (schematic {weaponRecord}) carries spell {CellWeaponSpell} with the Target_Hit event ({string.Join(",", weaponSpells?.Spells ?? [])})");
            session.Heal(session.Version, life, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
            session.Heal(session.Version, SessionLifeTarget.Hero, new LifeHeal { Full = true, Fatigue = 1000 });
            var swingTarget = _combatHost.Target(session, runtime, npc.Row.Key);
            TechProductsRequire(swingTarget is { Build.Combat: not null }, "the cave NPC is an attack target again");
            AreaSmokePlace(map, AreaSmokeStandable(map, map.Chart.ToChart(new(World(npc.Row).X, World(npc.Row).Y)), 1));
            _areaSmokeTime *= 20; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
            PublishWorldSelection();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!HeroInCombatMode) ToggleCombatMode();
            var cellsBeforeSwing = Cells(); var weaponLog = SpellCasterNow()!.Log.Count; var weaponLifeBefore = NpcHitPoints();
            CancelAreaAttack(null);
            _attack = new(swingTarget!, swingTarget!.Build.Combat!, null);
            StartAttackGoal(_attack);
            AttackSettleForSelfTest();
            var swings = new List<string>(); var weaponFirings = 0; var lastSeen = _lastCritterAttackForSelfTest;
            void Swung()
            {
                if (_lastCritterAttackForSelfTest is not { } now || ReferenceEquals(now, lastSeen)) return;
                lastSeen = now;
                var started = now.ItemSpells.Count(f => f.Item == weapon && (f.Mask & 0x40000u) != 0 && f.Result.Spells.Count > 0);
                weaponFirings += started;
                swings.Add($"{now.Resolution?.Outcome}:{started}");
            }
            for (var frames = 0; frames < 20000 && _attack is not null && !session.QueryLife(life).View!.Status.IsDead; frames++) { Frame(); Swung(); }
            for (var frames = 0; frames < 200; frames++) { Frame(); Swung(); }
            CancelAreaAttack(null);
            var shocks = SpellCasterNow()!.Log.Skip(weaponLog).Count(e => e.Kind == SpellCastEventKind.Damaged && e.Object == npcId);
            var cellsSpent = cellsBeforeSwing - Cells();
            GD.Print($"{TechProductsCode} tech-products cell-weapon record={weaponRecord} cells={cellsBeforeSwing}->{Cells()} shocks={shocks} swings={string.Join(",", swings)} weapon-firings={weaponFirings} npc-hit-points={weaponLifeBefore}->{NpcHitPoints()} notice={_attackNotice}");
            // The cave NPCs have a few hit points: the first hit may kill, so the record's electrical damage on the (then dead) NPC is not always logged; the cell paid before it is.
            // A cell-powered source pays the cost times (1 + power / complexity) (TC1, original bug): one cell or up to double for a technologist, per firing.
            TechProductsRequire(weaponFirings > 0 && cellsSpent >= weaponFirings && cellsSpent <= 2 * weaponFirings, $"each hit's firing of the cell weapon paid its cells ({cellsBeforeSwing} -> {Cells()}, {weaponFirings} firings, shocks {shocks}: {_attackNotice})");
            TechProductsRequire(NpcHitPoints() < weaponLifeBefore, $"the NPC lost hit points ({weaponLifeBefore} -> {NpcHitPoints()}, {cellsSpent} cells, {shocks} shocks logged)");
            if (HeroInCombatMode) ToggleCombatMode();
            // The hands and the body as the later steps expect them: the weapon off, the maintained armour on again.
            OpenInventory();
            TakeOffItem(EquipmentSlot.Weapon);
            WearItem(armour, EquipmentSlot.Armor);
            EndInventory();
            TechProductsRequire(session.QueryEquipment().Worn.Any(w => w.Entity == armour) && session.QueryEquipment().Worn.All(w => w.Slot != EquipmentSlot.Weapon), $"the weapon is off and the armour is on again: {_inventoryStatus}");

            // ARC-COMBAT-1223 (ledger PS5, PF10; 0x4B2870): a throw that MISSES its NPC flies on to its scattered tile and its end there fires the item's attacks-a-location event, so the grenade explodes
            // where it lands. The one exception is a scatter back onto the hero's own tile: the flight's line has no step (0x4201C0), the goal fails and ends without an item event (GT53), and the grenade
            // just lies there. The darkness penalty (ARC-WORLD-1149) makes the throws miss. The leg runs after the cell weapon's swings, which read the session's random sequence.
            _cellLightForSelfTest = 0;
            int missedFlown = 0, missedLying = 0, missedThrows = 0;
            for (; missedThrows < 12 && missedFlown == 0; missedThrows++)
            {
                session.Heal(session.Version, life, new LifeHeal { Full = true, Fatigue = 1000, Resurrect = true });
                session.Heal(session.Version, SessionLifeTarget.Hero, new LifeHeal { Full = true, Fatigue = 1000 });
                AreaSmokePlace(map, map.Chart.ToChart(new(throwFrom!.Value.X, throwFrom.Value.Y)));
                // (the clock must move on for the next observation; the scaling by 20 above would overflow the int after the third throw)
                _areaSmokeTime += 5000; _worldTestTime = SimulationTime.At(_areaSmokeTime, 20);
                PublishWorldSelection();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                var missGrenade = TechSmokeCraft(data, TechDisciplineExplosives, grenadeRecord).Made[0];
                SyncAreaInventory();
                TechProductsRequire(HoldItemOnGameCursor(missGrenade, "smoke") is null && GameCursorItemForSelfTest == missGrenade, $"the miss leg's grenade is on the game cursor ({HeldThrowNoticeForSelfTest})");
                _projectileEndsForSelfTest.Clear(); _thrownLandingsForSelfTest.Clear(); _shotEndItemEventsForSelfTest.Clear(); _lastHeldThrowForSelfTest = null;
                var unlaunchedBefore = _projectilesUnlaunchedForSelfTest;
                var missLog = SpellCasterNow()!.Log.Count;
                TechProductsRequire(ThrowGameCursorItem(aimedAt), $"the miss leg's throw started ({HeldThrowNoticeForSelfTest})");
                for (var frames = 0; frames < 1500 && _lastHeldThrowForSelfTest is null && HeroRunsHost() is { ThrowRun: not null }; frames++) Frame();
                TechProductsRequire(_lastHeldThrowForSelfTest is { Disposition: GameSessionDisposition.Changed }, $"the miss leg's grenade was thrown ({_lastHeldThrowForSelfTest?.Disposition} {_lastHeldThrowForSelfTest?.Failure})");
                for (var frames = 0; frames < 1500 && (_projectiles.Count > 0 || HeroRunsHost() is { ThrowRun: not null }); frames++) Frame();
                TechProductsRequire(_projectiles.Count == 0, "the miss leg's flight ended");
                for (var frames = 0; frames < 400 && SpellCasterNow()!.Log.Count == missLog && _shotEndItemEventsForSelfTest.Count == 0; frames++) Frame();
                for (var frames = 0; frames < 200; frames++) Frame();
                var outcome = _lastHeldThrowForSelfTest?.Attack?.Resolution?.Outcome;
                var missEvents = _shotEndItemEventsForSelfTest.ToArray();
                var lyingStill = session.QueryPlacedItems().Any(p => p.Entity == missGrenade);
                var noStep = _projectilesUnlaunchedForSelfTest != unlaunchedBefore;
                GD.Print($"{TechProductsCode} tech-products grenade-miss throw={missedThrows} outcome={outcome?.ToString() ?? "-"} events={missEvents.Length} started={string.Join(",", missEvents.Select(e => e.Started))} noStep={noStep} lying={lyingStill} destroyed={session.IsItemDestroyed(missGrenade)}");
                if (outcome is not { } decided || decided is AttackOutcome.Hit or AttackOutcome.CriticalHit) continue;
                if (noStep)
                {
                    TechProductsRequire(missEvents.Length == 0 && lyingStill && !session.IsItemDestroyed(missGrenade), "a scatter back onto the hero's own tile: no item event, the grenade lies there");
                    missedLying++;
                    continue;
                }
                TechProductsRequire(missEvents.Any(e => e.Started > 0), $"the missed grenade's item event started a spell run at its landing tile (events {missEvents.Length})");
                TechProductsRequire(!lyingStill && session.IsItemDestroyed(missGrenade), "the missed grenade exploded and is gone");
                missedFlown++;
            }
            _cellLightForSelfTest = null;
            TechProductsRequire(missedFlown > 0, $"a missed grenade exploded where it landed within {missedThrows} throws (lying {missedLying})");
            GD.Print($"{TechProductsCode} tech-products grenade-miss PASS exploded={missedFlown} lying={missedLying} throws={missedThrows}");

            // 8. F7 and F8: the same effects and items after the load.
            string[] State(GameSessionOwner s) => [.. s.QueryHeroEffects()!.Effects.Select(e => "effect=" + e.Number).OrderBy(l => l, StringComparer.Ordinal), .. TechSmokeHeldLines(s), "npc-items=" + string.Join("+", s.QueryNpcInventory(npcId).Select(i => i.Entity).OrderBy(e => e, StringComparer.Ordinal)), "cells=" + s.QueryHeroAmmo().Where(a => a.AmmoType == 2).Sum(a => a.Units), .. s.QueryEquipment().Worn.Select(w => $"worn={w.Slot}:{w.Entity}"),
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
                Map = _readyMapName, Intelligence = intelligence, Therapeutics = potions, Chemistry = new { Record = chemRecord, Product = poisonProduct, Damage = recordDamage }, Found = new { Documents = documents.Length, Assembled = foundCraft!.Record }, Cells = new { Shield = shieldRecord, Armour = armourRecord, Weapon = weaponRecord, Shocks = shocks }, NpcCrafter = npcRecord, Grenade = grenadeRecord, StateLines = saved.Length }));
        }
        finally
        {
            if (InventoryOpen) EndInventory();
            if (SheetOpen) EndSheet();
            if (SchematicsOpen) EndSchematics();
            TechSmokeAmmoNpc = null;
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
