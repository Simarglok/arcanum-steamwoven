using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Locks;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    internal const string ItemAimCode = "ARC-DEV-365";

    // ARC-DEV-365 (technology audit slice T-g) native observation (stage "item-aim", only when named, with --hero, on a map with a living NPC): the item cursor and the item use goals on the player's data.
    // The item is a script trap device (prototype 15100: OIF_NEEDS_TARGET and OIF_NO_RANGED_USE, a trap device, no charges, IA16); since ARC-WORLD-125 (E6-c) its use arms a script trap: on a tile it makes a marker that carries the device's script
    // and spends the device, on an NPC it is refused (mes\trap.mes 2) and stays, on a tile that holds a trap object (the marker of an earlier device) it is refused (mes\trap.mes 3) and stays. ARC-WORLD-124 adds step 4, a timed bomb (item 15154) placed on a tile and its fuse.
    //  1. The use icon with no Shift opens the item cursor (interface mode 16, mask object or tile, IA3, IA6) and the window closes; a right press cancels it (IA5).
    //  2. A left press on a tile three to eight cells away is the pick of the cursor's mask: the cursor closes, goal 41 (or goal 39 when an object stands there) walks the hero next to the target and the use's refusal shows.
    //  3. A left press on the NPC (an object target with OIF_NO_RANGED_USE, IA8) starts goal 39: the hero walks next to it and the use runs (the refusal of an NPC target shows), the device staying in the pack.
    //  5. (ARC-WORLD-125) Two script trap devices used on the same free tile: the first arms a marker of its script's prototype (26000..26007), the second is refused with line 3 and stays.
    //  6. (ARC-UI-1127, E8-g) A healing kit with the target flag aimed at a hurt living NPC: goal 39 walks the hero next to it and the session's heal runs on it.
    //  7. (ARC-UI-1127) Lockpicks on the nearest locked container without an unlock script (needs one in the window: the town sector): skill goal 33 walks, turns and the session's pick runs; the open attempt follows it.
    //  8. (ARC-UI-1127) The cast host's Identify answer for an NPC opens the NPC's read-only sheet.
    private async Task RunAreaItemAimSmokeAsync()
    {
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print($"{ItemAimCode} item-aim smoke FAIL: " + why);
            throw new InvalidOperationException("AREA_ITEM_AIM_SMOKE_FAILED");
        }
        if (_areaSession is not { } session || session.QueryHero() is null || _inventoryHost is null || _readyArea is not { } area
            || _worldActor.Session is not AreaGridSession grid)
        { GD.Print($"{ItemAimCode} native-smoke item-aim NONE map={_readyMapName} (needs --hero and an area session with its inventory)"); return; }
        var data = await TechSmokeDataAsync();
        for (var i = 0; i < 1800 && SpellRecordsNow() is null; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var caster = SpellCasterNow();
        Require(SpellRecordsNow() is not null && caster is not null && ItemTileUseNow() is not null, "the spell data and the cast host of the map");
        var map = new AreaMapRuntime(area.Snapshot, grid.Snapshot, grid.Chart);
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        var scaled = false;
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
            Vector2 Screen(CellCoord world)
            {
                var chart = map.Chart.ToChart(new(world.X, world.Y));
                var centre = AreaCellPicking.Center(chart);
                return _canvas!.GetGlobalTransformWithCanvas() * new Vector2((float)centre.X, (float)centre.Y);
            }
            CellCoord? HeroCell() => caster!.Facts(SessionSpellCaster.Hero)?.Location is { } at && at != SpellLocation.None ? new(checked((int)at.X), checked((int)at.Y)) : null;

            var trap = TechSmokeGive(data, 15100);
            Require(_inventoryHost.UseFacts(session, trap, _areaKitUse) is { NeedsTarget: true, TrapDevice: true, NoRangedUse: true }, "prototype 15100 reads as a trap device that needs a target and has no ranged use");

            // 1. The cursor opens from the use icon and a right press cancels it.
            void OpenCursor()
            {
                OpenInventory();
                Require(InventoryOpen, "the inventory window opens for the use");
                UseHeroItem(trap, AreaItemUseSource.UseIcon, shift: false);
                Require(!InventoryOpen && _itemAiming is { } aim && aim.Item == trap, $"the use icon opened the item cursor and closed the window: {_itemAimLast} {_inventoryStatus}");
                Require(ItemAimingForSelfTest is { } open && open.Mask == (SpellUiRules.ObjectBit | SpellUiRules.TileBit), "the mask is object or tile (IA3)");
                Require(TargetModeOpen(InterfaceMode.ItemTarget) && TargetsMatchStack && WindowPanelsMatchStack && ItemTargetOpen, $"mode 16 is on the stack: {StackText()}");
            }
            OpenCursor();
            CancelArmedLockpicks("the smoke's right press");
            Require(!ItemTargetOpen && !TargetModeOpen(InterfaceMode.ItemTarget) && TargetsMatchStack && _itemAiming is null, $"the cancel closed mode 16: {StackText()}");
            Require(TechSmokeHeld(session).Contains(trap), "the cancelled use keeps the device");

            // 2. A press on a tile.
            void RunUntil(Func<bool> done, string why, int frames = 6000)
            {
                for (var i = 0; i < frames && !done(); i++) Frame();
                Require(done(), why + $" (last: {_itemAimLast})");
            }
            var hero = HeroCell();
            Require(hero is not null, "the hero's cell");
            CellCoord? tile = null;
            for (var d = 3; d <= 8 && tile is null; d++)
            for (var dx = -d; dx <= d && tile is null; dx++)
            for (var dy = -d; dy <= d && tile is null; dy++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != d) continue;
                var candidate = new CellCoord(hero!.Value.X + dx, hero.Value.Y + dy);
                var chart = map.Chart.ToChart(new(candidate.X, candidate.Y));
                if (map.Chart.Contains(chart) && ItemTileApproach(candidate, 1) is { } approach && approach != hero) tile = candidate;
            }
            Require(tile is not null, "a standable tile three to eight cells from the hero");
            OpenCursor();
            _itemAimLast = null;
            LastScriptTrapUse = null;
            ItemAimClick(Screen(tile!.Value), ctrl: false, shift: false);
            Require(!ItemTargetOpen && TargetsMatchStack, "the press closed the cursor (IA5)");
            var pressNotice = _itemAimLast;
            Require(pressNotice is not null && (pressNotice.Contains("goal 41", StringComparison.Ordinal) || pressNotice.Contains("goal 39", StringComparison.Ordinal)), $"the press started an item use goal: {pressNotice}");
            var tileGoal = pressNotice!.Contains("goal 41", StringComparison.Ordinal);
            Require(HeroRunsHost()?.ItemUseRun is { } run && run.Bottom.Goal == (tileGoal ? Arcanum.Simulation.Goals.GoalType.UseItemOnTile : Arcanum.Simulation.Goals.GoalType.UseItem), "the hero holds the item use run");
            RunUntil(() => LastScriptTrapUse is not null, "the use ran at the end of the walk");
            Require(HeroRunsHost()?.ItemUseRun is null, "the run ended");
            // A press on a bare tile (goal 41) arms a marker and spends the device; a press on an object (goal 39) is the object form: refused (the device stays) or armed on a portal or a container.
            Require(tileGoal ? LastScriptTrapUse is { Outcome: SessionScriptTrapOutcome.Armed or SessionScriptTrapOutcome.TileRefused } : LastScriptTrapUse is { Outcome: SessionScriptTrapOutcome.Armed or SessionScriptTrapOutcome.TargetRefused },
                $"the press's use answered: {LastScriptTrapUse?.Outcome} {_itemAimLast}");
            Require(TechSmokeHeld(session).Contains(trap) == (LastScriptTrapUse!.Outcome != SessionScriptTrapOutcome.Armed), "the device is spent exactly when the trap was armed");
            GD.Print($"ARC-WORLD-125 item-aim press outcome={LastScriptTrapUse.Outcome} marker={LastScriptTrapUse.Marker?.Prototype.ToString() ?? "-"} target={LastScriptTrapUse.Target ?? "-"}");
            if (!TechSmokeHeld(session).Contains(trap)) trap = TechSmokeGive(data, 15100);
            if (tileGoal)
            {
                var at = HeroCell()!.Value;
                Require(Math.Max(Math.Abs(at.X - tile.Value.X), Math.Abs(at.Y - tile.Value.Y)) <= 1, $"the hero stands next to the tile ({at.X},{at.Y} -> {tile.Value.X},{tile.Value.Y})");
            }
            GD.Print($"{ItemAimCode} item-aim press goal={(tileGoal ? 41 : 39)} notice={_itemAimLast}");

            // 3. A press on an NPC (goal 39).
            static CellCoord World(AreaObjectRecord row) => new(checked((int)row.WorldX!.Value), checked((int)row.WorldY!.Value));
            var here = HeroCell()!.Value;
            var npc = area.Snapshot.Objects.Where(row => row.Type == 16 && row.Scope == AreaScope.Core && row.WorldX is not null && row.WorldY is not null && !IsAreaCompanion(row.Key))
                .Select(row => (Row: row, Id: SpellPickId(row.Key)))
                .Where(o => o.Id is not null && caster!.Facts(o.Id)?.Location is { } there && there != SpellLocation.None)
                .OrderBy(o => Math.Max(Math.Abs(World(o.Row).X - here.X), Math.Abs(World(o.Row).Y - here.Y))).FirstOrDefault();
            Require(npc.Id is not null, "an NPC with a cell to walk to");
            OpenCursor();
            ApplyItemAim(_itemAiming!, npc.Id, SpellLocation.None, false, false);
            Require(!ItemTargetOpen && TargetsMatchStack, "the apply closed the cursor");
            var inRange = Math.Max(Math.Abs(World(npc.Row).X - here.X), Math.Abs(World(npc.Row).Y - here.Y)) <= 1;
            Require(inRange || HeroRunsHost()?.ItemUseRun is { Bottom.Goal: Arcanum.Simulation.Goals.GoalType.UseItem }, $"the NPC's press started goal 39: {_itemAimLast}");
            RunUntil(() => LastScriptTrapUse is { Outcome: SessionScriptTrapOutcome.TargetRefused }, "goal 39 used the device at the NPC: refused with mes\\trap.mes 2");
            Require(HeroRunsHost()?.ItemUseRun is null && TechSmokeHeld(session).Contains(trap), "goal 39 ended and the device stays");
            // 4. ARC-WORLD-124 (E6-b, ledger IA13, TP2, TP3): a timed bomb (item 15154, record 176) used on the tile of step 2 is destroyed, leaves its marker (prototype 4047) and sets the 88 s fuse; when the
            //    fuse ends (the session's game clock moved by it) the cast host's pass destroys the marker and casts the record at the tile with no source and no caster.
            var bomb = TechSmokeGive(data, 15154);
            Require(_inventoryHost.UseFacts(session, bomb, _areaKitUse) is { TrapDevice: true }, "prototype 15154 reads as a trap device");
            var spot = tile!.Value;
            var markers = session.QueryCreatedObjects(new SessionMapId(CurrentAreaMap)).Count;
            var placedLine = PlaceTrapDeviceUse(bomb, null, new SpellLocation(checked((uint)spot.X), checked((uint)spot.Y)));
            Require(LastTrapDeviceUse is { Outcome: SessionTrapDeviceOutcome.Placed, Marker.Prototype: 4047 } && !TechSmokeHeld(session).Contains(bomb), $"the bomb was placed and spent: {placedLine}");
            Require(session.QueryCreatedObjects(new SessionMapId(CurrentAreaMap)).Count == markers + 1 && session.QueryTrapDevices().Count == 1, "the marker and the fuse are in the session");
            var casts = new List<SessionTrapDeviceCast>();
            Require(session.JumpClocks(session.QueryClockSnapshot().Token, Arcanum.Simulation.Traps.TrapDeviceRules.FuseMs).Disposition == Arcanum.Simulation.Clock.ClockMutationDisposition.Changed, "the game clock moved by the fuse");
            caster!.ProcessEvents();
            RunUntil(() => { casts.AddRange(caster!.TakeTrapDeviceCasts()); return casts.Count > 0; }, $"the fuse ran (clock {session.QueryClockSnapshot().GameTimestampMs}, pending {string.Join(";", session.QueryTrapDevices().Select(e => $"{e.DeadlineMs}:{string.Join(",", e.Payload.Values)}"))}, created {session.QueryCreatedObjects(new SessionMapId(CurrentAreaMap)).Count})");
            Require(session.QueryTrapDevices().Count == 0 && session.QueryCreatedObjects(new SessionMapId(CurrentAreaMap)).Count == markers, "the fuse is spent and the marker destroyed");
            GD.Print($"ARC-WORLD-124 trap-bomb fuse spell={casts[0].Firing.Spell} outcome={casts[0].Outcome} run={casts[0].Run?.ToString() ?? "-"} tile={spot.X},{spot.Y}");
            Require(casts[0].Outcome == SpellCastAttemptOutcome.Started, $"the record's cast at the tile started: {casts[0].Outcome}");
            // 5. ARC-WORLD-125 (E6-c, ledger IA13, TP2): two script trap devices on the same tile. The first arms a marker (prototype 26000..26007) that carries the device's own script and spends the device; the second is
            //    refused with line 3 (a trap object stands there now) and stays. A tile whose map data holds a trap object refuses both.
            var free = new SpellLocation(checked((uint)(spot.X + 1)), checked((uint)spot.Y));
            var cellTwo = new CellCoord(spot.X + 1, spot.Y);
            var first = TechSmokeGive(data, 15100);
            PlaceTrapDeviceUse(first, null, free);
            var armed = LastScriptTrapUse;
            Require(armed is { Outcome: SessionScriptTrapOutcome.Armed or SessionScriptTrapOutcome.TileRefused }, $"the first device answered: {armed?.Outcome} {armed?.Failure}");
            if (armed!.Outcome == SessionScriptTrapOutcome.Armed)
            {
                var marker = armed.Marker!;
                Require(marker.Prototype is >= 26000 and <= 26007 && !TechSmokeHeld(session).Contains(first), $"a marker of the script's prototype was made and the device spent: {marker.Prototype}");
                Require(session.QueryPlacedTrapScript(marker.Map, marker.Entity) is { Script: not 0 }, "the marker carries the script");
                Require(session.QueryScriptTrapMarkers(marker.Map, cellTwo).Count == 1, "the session lists the marker on its tile");
            }
            var second = TechSmokeGive(data, 15100);
            PlaceTrapDeviceUse(second, null, free);
            Require(LastScriptTrapUse is { Outcome: SessionScriptTrapOutcome.TileRefused, Plan.RefusalLine: 3 } && TechSmokeHeld(session).Contains(second), $"the second device was refused with line 3 and stays: {LastScriptTrapUse?.Outcome}");
            GD.Print($"ARC-WORLD-125 script-trap first={armed.Outcome} second={LastScriptTrapUse!.Outcome} tile={cellTwo.X},{cellTwo.Y}");
            // 6. ARC-UI-1127 (E8-g, ledger IA14, HL1): a healing kit with OIF_NEEDS_TARGET aimed at the NPC of step 3 (the cursor is the kit's use: the heal skill on another critter): goal 39 walks the hero next to the NPC and the
            //    session's heal runs on it (a hurt NPC heals by the check's amount; the kit loses a use or is destroyed). The kit is the first installed healing item with the flag and no use or destroy script.
            for (var wait = 0; wait < 600 && _barterData is null; wait++) { AdoptBarterData(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
            var barter = _barterData?.Data;
            Require(barter is not null, "the item prototypes loaded (" + _barterData?.Status + ")");
            var kitNumber = Enumerable.Range(6049, 15213 - 6049 + 1).FirstOrDefault(n => barter!.Prototype(n) is { } p && AreaItemUseFacts.From("kit", p) is { HealingKit: true, NeedsTarget: true, UseScript: false }
                && AreaHealingItems.From("kit", p) is { } healing && !healing.ScriptPoints.Contains(SessionHealingItem.UseScript) && !healing.ScriptPoints.Contains(SessionHealingItem.DestroyScript));
            var healed = "NONE";
            // The patient: the nearest living NPC the heal can change (a dead or invulnerable one keeps its hit points).
            var patientNpc = area.Snapshot.Objects.Where(row => row.Type == 16 && row.Scope == AreaScope.Core && row.WorldX is not null && row.WorldY is not null && !IsAreaCompanion(row.Key))
                .Select(row => (Row: row, Id: SpellPickId(row.Key)))
                .Where(o => o.Id is not null && caster!.Facts(o.Id) is { } facts && facts.Location != SpellLocation.None && (facts.ObjectFlags & SpellTargetObject.Invulnerable) == 0
                    && caster.LifeTarget(o.Id) is { } lt && session.QueryLife(lt).View is { Status: { IsDead: false, MaxHitPoints: > 10 } })
                .OrderBy(o => Math.Max(Math.Abs(World(o.Row).X - here.X), Math.Abs(World(o.Row).Y - here.Y))).FirstOrDefault();
            Require(patientNpc.Id is not null, "a living NPC that the heal can change");
            if (kitNumber == 0) GD.Print($"{ItemAimCode} item-aim heal step NONE (no installed healing item with the target flag and no script)");
            else
            {
                var kitFields = barter!.Prototype(kitNumber)!;
                var kit = TechSmokeGive(data, kitNumber, new SessionItemKind(SessionItemType.Generic, kitNumber, kitFields.Object.Name, kitFields.Object.Description));
                Require(_inventoryHost.UseFacts(session, kit, _areaKitUse) is { HealingKit: true, NeedsTarget: true }, $"prototype {kitNumber} reads as a healing kit that needs a target");
                var patient = caster!.LifeTarget(patientNpc.Id!);
                Require(patient is not null, "the NPC has a life target on the cast host");
                var lifeBefore = session.QueryLife(patient!);
                var hurt = session.ApplyDamage(session.Version, patient!, new Arcanum.Simulation.Combat.LifeDamage { HitPoints = 10 });
                Require(hurt.Disposition == GameSessionDisposition.Changed, $"the NPC was hurt: {hurt.Disposition} {hurt.Failure} outcome={hurt.Outcome} life={lifeBefore.Failure}/{lifeBefore.View?.Status.HitPoints}/{lifeBefore.View?.Status.MaxHitPoints} flags={caster.Facts(patientNpc.Id!)?.ObjectFlags}");
                var hpBefore = session.QueryLife(patient!).View!.Status.HitPoints;
                OpenInventory();
                UseHeroItem(kit, AreaItemUseSource.UseIcon, shift: false);
                Require(!InventoryOpen && _itemAiming is { } kitAim && kitAim.Item == kit && ItemTargetOpen, $"the use icon opened the item cursor for the kit: {_itemAimLast} {_inventoryStatus}");
                _itemAimLast = null;
                ApplyItemAim(_itemAiming!, patientNpc.Id, SpellLocation.None, false, false);
                Require(!ItemTargetOpen && TargetsMatchStack, "the apply closed the cursor");
                Require(HeroRunsHost()?.ItemUseRun is { Bottom.Goal: Arcanum.Simulation.Goals.GoalType.UseItem } || (_itemAimLast?.Contains("goal 39", StringComparison.Ordinal) ?? false), $"the kit aimed at another critter walks with goal 39: {_itemAimLast}");
                RunUntil(() => LastHealForSelfTest is { } last && last.Target == patientNpc.Id, "goal 39 ended in the heal of the NPC");
                var heal = LastHealForSelfTest!.Value.Result;
                Require(heal.Disposition != GameSessionDisposition.Rejected && heal.Check is not null, $"the session ran the heal: {heal.Disposition} {heal.Failure}");
                var hpAfter = session.QueryLife(patient!).View!.Status.HitPoints;
                Require(!heal.Check!.Success || hpAfter >= hpBefore, $"a successful heal never lowers the NPC's hit points ({hpBefore} -> {hpAfter})");
                Require(HeroRunsHost()?.ItemUseRun is null && _itemAimLast is { Length: > 0 }, $"the use run ended and the HUD answered: {_itemAimLast}");
                healed = $"{(heal.Check.Success ? "healed" : "failed")} {hpBefore}->{hpAfter}";
                GD.Print($"ARC-UI-1127 item-aim heal kit={kitNumber} npc={patientNpc.Id} success={heal.Check.Success} hp={hpBefore}->{hpAfter} uses-left={heal.UsesLeft?.ToString() ?? "-"} destroyed={heal.ItemDestroyed} notice={_itemAimLast}");
            }
            // 7. ARC-UI-1127 (E8-g): lockpicks on a container: the nearest standing locked container the viewer can pick (no unlock script) is the skill goal 33's target (the loot goals' target string); the hero walks next to it, turns and
            //    the session's pick runs on its lock (a failure may retry); a picked container then opens through the window's admission.
            var picked = "NONE";
            if (_inventoryHost.Map(CurrentAreaMap) is { } lootMap)
            {
                var mapId = new SessionMapId(CurrentAreaMap);
                var lockedBox = area.Snapshot.Objects.Where(row => row.Scope == AreaScope.Core && row.Type == 2 && row.WorldX is not null && row.WorldY is not null && lootMap.TryGetEntity(row.Key, out _))
                    .Select(row => _inventoryHost.Target(session, CurrentAreaMap, row.Key)).OfType<AreaInventoryTarget>().Where(t => t.Kind == AreaInventoryTargetKind.Container)
                    .Where(t => !Arcanum.Data.Traversal.AreaStepInspector.HasUnlockScript(t.Row) && Arcanum.Data.Combat.CombatFieldReader.ReadBreakable(t.Row).Fields is { } box
                        && session.AttemptOpen(new(mapId, t.Entity), AreaObjectTargets.ContainerBaseline(t.Row, box), false) == OpenAttempt.Locked && LiveWorldCell(t.Row) is var (cx, cy) && ApproachCell(grid, cx, cy, 1) is not null)
                    .OrderBy(t => Math.Max(Math.Abs(World(t.Row).X - HeroCell()!.Value.X), Math.Abs(World(t.Row).Y - HeroCell()!.Value.Y))).FirstOrDefault();
                if (lockedBox is null) GD.Print($"{ItemAimCode} item-aim container-pick step NONE (no locked container with a standable cell next to it on map {_readyMapName})");
                else
                {
                    var picks = TechSmokeGive(data, 15101);
                    Require(_inventoryHost.Lockpick(session, picks, _areaKitLockpicks) is not null, "prototype 15101 reads as lockpicks");
                    HeroRunsHost()!.LoseAll("SMOKE_ITEM_AIM_PICK");
                    _lastContainerPick = null; _lockpickPending = null;
                    var started = HeroRequests.Apply(new LockpickRequest(InventoryTargetOf(lockedBox.Row.Key), picks));
                    Require(started.Outcome == HeroRequestOutcome.Done, $"the pick on the container started goal 33: {started.Outcome}");
                    Require(HeroRunsHost()?.SkillRun is { Bottom.Goal: Arcanum.Simulation.Goals.GoalType.UseSkill or Arcanum.Simulation.Goals.GoalType.AttemptPickLocks }, "the hero holds the skill run");
                    RunUntil(() => { ProcessLockpickPending(); return _lastContainerPick is not null; }, "the hero walked to the container and picked its lock");
                    var pick = _lastContainerPick!;
                    Require(pick.Disposition != GameSessionDisposition.Rejected && pick.Outcome is not null, $"the session ran the pick: {pick.Disposition} {pick.Failure}");
                    var opened = pick.Outcome!.Value.Result == Arcanum.Simulation.Skills.PickLockResult.Unlocked;
                    Require(opened == (session.AttemptOpen(new(mapId, lockedBox.Entity), AreaObjectTargets.ContainerBaseline(lockedBox.Row, Arcanum.Data.Combat.CombatFieldReader.ReadBreakable(lockedBox.Row).Fields!), false) == OpenAttempt.Opens),
                        $"the container's open attempt follows the pick ({pick.Outcome.Value.Result})");
                    picked = pick.Outcome.Value.Result.ToString();
                    GD.Print($"ARC-UI-1127 item-aim container-pick entity={lockedBox.Entity} result={picked} fate={pick.FateUsed}");
                }
            }
            // 8. ARC-UI-1127 (E8-g, ledger XR2): the cast host's Identify answer for an NPC (the event Read Aura, spell 7, logs for the hero as caster: SessionSpellCasterIdentifyTests) opens the read-only sheet of the NPC, the window
            //    the original opens. The hero's own Read Aura is refused by the spell's intelligence check on this smoke's hero (message 602), so the event is the cast host's entry as it would log it.
            var sheet = "NONE";
            PostSpellLog(null, [new SpellCastEvent(SpellCastEventKind.IdentifyCritter, 0, SpellTargetObject.Npc, patientNpc.Id!)]);
            for (var i = 0; i < 1200 && !(SheetOpen && _sheetNpc is not null); i++) { Frame(); TakeDialogNpcSheet(); }
            Require(SheetOpen && _sheetNpc is not null && LastIdentifySheetForSelfTest is { Opened: true } identified && identified.Entity == patientNpc.Id, $"the Identify answer opened the NPC's sheet: {_sheetNpcStatus}");
            Require(_sheetPanel!.ReadOnly, "the NPC's sheet is read only (no + or - buttons)");
            sheet = _sheetNpc!.View.Name;
            GD.Print($"ARC-UI-1127 item-aim identify sheet entity={patientNpc.Id} name={sheet} level={_sheetNpc.View.Level}");
            EndSheet();
            GD.Print("ARC-DEV-365 native-smoke item-aim PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = _readyMapName, Tile = tileGoal, Npc = npc.Id, Notice = _itemAimLast, Heal = healed, Pick = picked, Sheet = sheet }));
        }
        finally
        {
            if (InventoryOpen) EndInventory();
            CancelArmedLockpicks("the smoke ended");
            HeroRunsHost()?.LoseAll("SMOKE_ITEM_AIM_END");
            if (scaled) _areaSmokeTime = (_areaSmokeTime + 19) / 20;
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            PublishWorldSelection();
        }
    }
}
