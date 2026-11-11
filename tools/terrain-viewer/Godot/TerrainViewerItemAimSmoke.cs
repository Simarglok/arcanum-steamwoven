using Arcanum.Data.Area;
using Arcanum.Session;
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
    // The item is a script trap device (prototype 15100: OIF_NEEDS_TARGET and OIF_NO_RANGED_USE, a trap device, no charges, IA16), whose own use is NOT READY (E6-b), so the stage observes the cursor and the walks only.
    //  1. The use icon with no Shift opens the item cursor (interface mode 16, mask object or tile, IA3, IA6) and the window closes; a right press cancels it (IA5).
    //  2. A left press on a tile three to eight cells away is the pick of the cursor's mask: the cursor closes, goal 41 (or goal 39 when an object stands there) walks the hero next to the target and the use's refusal shows.
    //  3. A left press on the NPC (an object target with OIF_NO_RANGED_USE, IA8) starts goal 39: the hero walks next to it and the use runs (the refusal shows), the device staying in the pack.
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
            ItemAimClick(Screen(tile!.Value), ctrl: false, shift: false);
            Require(!ItemTargetOpen && TargetsMatchStack, "the press closed the cursor (IA5)");
            var pressNotice = _itemAimLast;
            Require(pressNotice is not null && (pressNotice.Contains("goal 41", StringComparison.Ordinal) || pressNotice.Contains("goal 39", StringComparison.Ordinal)), $"the press started an item use goal: {pressNotice}");
            var tileGoal = pressNotice!.Contains("goal 41", StringComparison.Ordinal);
            Require(HeroRunsHost()?.ItemUseRun is { } run && run.Bottom.Goal == (tileGoal ? Arcanum.Simulation.Goals.GoalType.UseItemOnTile : Arcanum.Simulation.Goals.GoalType.UseItem), "the hero holds the item use run");
            RunUntil(() => _itemAimLast!.Contains("NOT READY", StringComparison.Ordinal), "the use ran at the end of the walk");
            Require(HeroRunsHost()?.ItemUseRun is null && TechSmokeHeld(session).Contains(trap), "the run ended and the device stays");
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
            RunUntil(() => _itemAimLast!.Contains("NOT READY", StringComparison.Ordinal), "goal 39 used the device at the NPC");
            Require(HeroRunsHost()?.ItemUseRun is null && TechSmokeHeld(session).Contains(trap), "goal 39 ended and the device stays");
            GD.Print("ARC-DEV-365 native-smoke item-aim PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = _readyMapName, Tile = tileGoal, Npc = npc.Id, Notice = _itemAimLast }));
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
