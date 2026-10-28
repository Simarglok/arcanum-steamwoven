using System.Reflection;
using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.TerrainViewer.Tests;
using Godot;

namespace Arcanum.TerrainViewer;

public static partial class TerrainViewerSelfTest
{
    /// <summary>ARC-UI-004 over the strict synthetic AREA owner in the production app: an invented seed makes one drawn
    /// body a ground item and another a dead NPC; pickup and loot run through the production click path and window.</summary>
    private static async Task AssertAreaInventoryAsync(Node host)
    {
        using var source = new AreaOwnerFixture(motionBodies: true);
        var read = typeof(TerrainLoadCoordinator).Assembly.GetType("Arcanum.TerrainViewer.Core.AreaLoadOperation")!
            .GetMethod("ReadArea", BindingFlags.NonPublic | BindingFlags.Static)!;
        var area = (AreaLoadResult)read.Invoke(null, [7L, source.Owner, CancellationToken.None, null])!;
        var steps = AreaOwnerFixture.Steps(area.Snapshot, source.Streams[new(AreaSourceRole.Module)].Bytes, 7);
        source.Owner.Dispose();
        var result = SyntheticActorFixture.Create(human: true, area: true).Result with { Generation = 7, Area = area, AreaStepSnapshot = steps };
        var arguments = TerrainViewerArguments.Parse(["--mode", "crash-site-area", "--module", "synthetic-module",
            "--resource", "synthetic-terrain", "--actor-resource", "synthetic-actor", "--prototypes", "synthetic-prototypes",
            "--area-mobile", "synthetic-mobile", "--area-effects", "synthetic-effects", "--area-patch", "synthetic-patch", "--source-id", "synthetic"]);
        var viewport = new SubViewport { Size = new(800, 600), TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        var app = new TerrainViewerApp { SelfTestBootstrap = true };
        viewport.AddChild(app); host.AddChild(viewport);
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        app.SetProcess(false); app.SetProcessInput(false);
        string summary;
        try
        {
            app.PublishSyntheticForSelfTest(arguments, result);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            summary = await app.AreaInventoryForSelfTestAsync(host, steps);
        }
        finally
        {
            app.ClearSyntheticForSelfTest();
            viewport.Free();
        }
        GD.Print($"ARC-UI-004 self-test PASS synthetic pickup, loot and inventory window ({summary}); original items covered by the native smoke");
    }
}

public partial class TerrainViewerApp
{
    internal async Task<string> AreaInventoryForSelfTestAsync(Node host, Arcanum.Data.Traversal.AreaStepSnapshot steps)
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("AREA inventory: " + why); }
        if (!SelfTestBootstrap || _readyArea is null || _areaSession is null) throw new InvalidOperationException("AREA_INVENTORY_READY_REQUIRED");
        var simulatedFocus = _worldFocusSimulationForSelfTest;
        _worldFocusSimulationForSelfTest = true; _worldFocused = true;
        try
        {
            OpenHoverGate(steps);
            var index = HoverIndex()!;
            Require(index.Bodies.Count >= 2, "two pickable synthetic bodies");
            var rows = _readyArea.Snapshot.Objects;
            // Two bodies that are each the frontmost pick somewhere on screen: the last one is the item, the other the body.
            var shown = new List<AreaDrawCommand>();
            foreach (var candidate in index.Bodies.Reverse())
                if (shown.Count < 2 && await HoverPointAsync(host, candidate) is not null) shown.Add(candidate);
            Require(shown.Count == 2, "two visible synthetic bodies");
            var itemCommand = shown[0]; var bodyCommand = shown[1];
            var item = index.Row(itemCommand.Identity!.Value); var body = index.Row(bodyCommand.Identity!.Value);
            var listed = rows.First(row => row.Key != item.Key && row.Key != body.Key);
            static SessionItemFacts Facts(SessionItemType type, int name) => new(new(type, 4000 + name, name, 1), 1, 0, 0, null, []);
            var map = _readyArea.Snapshot.RuntimeMap;
            // The synthetic records share one object id, so the seed uses invented entity ids.
            InventoryForSelfTest(new AreaInventoryHost([new AreaInventoryMap(map,
                new([new("syn:body", SessionHolderKind.Npc, true, false, [], ["syn:listed"])],
                    [new("syn:item", Facts(SessionItemType.Food, 1), new CellCoord(checked((int)item.WorldX!.Value), checked((int)item.WorldY!.Value))),
                     new("syn:listed", Facts(SessionItemType.Weapon, 2), null)]),
                new Dictionary<string, AreaObjectRecord> { ["syn:body"] = body, ["syn:item"] = item, ["syn:listed"] = listed })]));

            // From afar (a reachable cell 2..6 cells from the item) the cursor promises a walk; a press sets the walk and
            // remembers the pickup; a ground press then cancels it.
            var session = (AreaGridSession)_worldActor.Session!;
            var routes = new AreaRouteSource(session.Snapshot, session.Chart);
            var itemChart = session.Chart.ToChart(new(item.WorldX!.Value, item.WorldY!.Value));
            var seen = new HashSet<CellCoord> { session.State.CommittedCell }; var queue = new Queue<CellCoord>([session.State.CommittedCell]);
            CellCoord? farCell = null;
            while (farCell is null && queue.TryDequeue(out var cell))
            {
                if (Math.Max(Math.Abs(cell.X - itemChart.X), Math.Abs(cell.Y - itemChart.Y)) is >= 2 and <= 6) { farCell = cell; break; }
                foreach (var direction in Enum.GetValues<NeighborDirection>())
                {
                    var next = Arcanum.Simulation.Navigation.RoutePlanner.Neighbor(cell, direction);
                    if (seen.Contains(next) || !routes.Contains(next) || routes.Assess(cell, direction) != Arcanum.Simulation.Navigation.RouteStepVerdict.Open) continue;
                    seen.Add(next); queue.Enqueue(next);
                }
            }
            ConfigureAreaActor(AreaRuntimes()[map], farCell ?? throw new InvalidOperationException("AREA inventory: no reachable cell 2..6 from the item"), NeighborDirection.XPlus);
            OpenHoverGate(steps); session = (AreaGridSession)_worldActor.Session!;
            var point = await HoverPointAsync(host, itemCommand) ?? throw new InvalidOperationException("AREA inventory: item not visible");
            HoverAtForSelfTest(point);
            Require(HoverActionForSelfTest == AreaHoverAction.WalkToPickUp && HoverCursorForSelfTest == Godot.Input.CursorShape.PointingHand,
                $"far item hover {HoverActionForSelfTest}");
            _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = point, GlobalPosition = point });
            Require(_inventoryAfterRoute is { Kind: AreaInventoryTargetKind.GroundItem } && _worldRoute?.Target is not null, "the far press walks to pick up: " + _inventoryStatus);
            PublishWorldSelection(); _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            var ground = GroundPoint(session.State.CommittedCell) ?? throw new InvalidOperationException("AREA inventory: no bare ground at the actor");
            _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = ground, GlobalPosition = ground });
            Require(_inventoryAfterRoute is null && _inventoryStatus.Contains("cancelled: a ground click", StringComparison.Ordinal) && _areaSession.QueryPcInventory().Count == 0,
                "a ground press cancels the walk to pick up: " + _inventoryStatus);
            StopWorldRoute("ARC-UI-004 self-test");

            // Stand next to the item: the press takes it at once and it leaves the world.
            var near = ApproachCell(session, item.WorldX!.Value, item.WorldY!.Value, AreaInventoryHost.Reach) ?? throw new InvalidOperationException("AREA inventory: no cell next to the item");
            ConfigureAreaActor(AreaRuntimes()[map], near, NeighborDirection.XPlus); OpenHoverGate(steps);
            point = await HoverPointAsync(host, itemCommand) ?? throw new InvalidOperationException("AREA inventory: item not visible next to it");
            HoverAtForSelfTest(point);
            Require(HoverActionForSelfTest == AreaHoverAction.PickUp, "item within reach: " + HoverActionForSelfTest);
            _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = point, GlobalPosition = point });
            Require(_areaSession.QueryPcInventory().Select(i => i.Entity).SequenceEqual(["syn:item"]), "the press picked the item up: " + _inventoryStatus);
            Require(_canvas!.AreaHidden?.Contains(item.Key) == true && AreaObjectAtScreen(point) != item.Key, "the taken item is neither drawn nor picked");
            Require(_inventoryStatus.StartsWith("picked up", StringComparison.Ordinal), "status " + _inventoryStatus);
            Require(GetSelfTestSnapshot().Hud.Contains(" | PC items 1 | picked up", StringComparison.Ordinal), "HUD inventory status");

            // The original Inventory key opens the window (modal) and closes it again; Escape closes it too.
            InventoryKey(Key.I);
            Require(InventoryOpen && WorldModal && _inventoryPanel!.ViewForSelfTest!.Pc.Count == 1 && _inventoryPanel.Holder is null, "I opened the window");
            Require(_inventoryPanel!.PcTextForSelfTest.Contains("name unavailable] (food, weight 1)", StringComparison.Ordinal), "the PC's list names the item: " + _inventoryPanel.PcTextForSelfTest);
            HoverAtForSelfTest(point);
            Require(HoverActionForSelfTest == AreaHoverAction.None && HoverCursorForSelfTest == Godot.Input.CursorShape.Arrow, "the modal window hides the hover");
            InventoryKey(Key.I);
            Require(!InventoryOpen && !_worldGate.IsOpen, "I closed the window and quarantined the click guards");
            OpenHoverGate(steps); InventoryKey(Key.I); InventoryKey(Key.Escape);
            Require(!InventoryOpen, "Escape closed the window");

            // The dead NPC opens to loot; its item goes to the PC through the window's take button.
            near = ApproachCell(session = (AreaGridSession)_worldActor.Session!, body.WorldX!.Value, body.WorldY!.Value, AreaInventoryHost.Reach)
                ?? throw new InvalidOperationException("AREA inventory: no cell next to the body");
            ConfigureAreaActor(AreaRuntimes()[map], near, NeighborDirection.XPlus); OpenHoverGate(steps);
            point = await HoverPointAsync(host, bodyCommand) ?? throw new InvalidOperationException("AREA inventory: body not visible");
            HoverAtForSelfTest(point);
            Require(HoverActionForSelfTest == AreaHoverAction.Loot, "dead NPC within reach: " + HoverActionForSelfTest);
            _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = point, GlobalPosition = point });
            Require(InventoryOpen && _inventoryPanel!.Holder == "syn:body" && _inventoryPanel.TakeButtonsForSelfTest.Count == 1, "the press opened the body to loot");
            _inventoryPanel!.TakeButtonsForSelfTest[0].EmitSignal(BaseButton.SignalName.Pressed);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(_areaSession.QueryPcInventory().Count == 2 && _inventoryPanel.ViewForSelfTest!.Loot.Count == 0 && _inventoryPanel.TakeAllForSelfTest.Disabled,
                "the take button moved the item: " + _inventoryPanel.MessageForSelfTest);
            Require(_inventoryPanel.MessageForSelfTest.StartsWith("took ", StringComparison.Ordinal), "window message " + _inventoryPanel.MessageForSelfTest);
            // ARC-SKILLS-005: a healing line gets a use button that runs the heal through the app; the synthetic line names no item
            // the host knows (and this session has no hero), so the app finds no healing item and reports it.
            // ARC-UI-025: the Use button runs the original's item use; the synthetic item has no use facts the host knows.
            _inventoryPanel.Render(new AreaInventoryView([new("syn:item", "bandage") { Healing = true, Usable = true }, new("syn:listed", "sword")], null, null, []), null);
            Require(_inventoryPanel.UseButtonsForSelfTest.Count == 1 && _inventoryPanel.DropButtonsForSelfTest.Count == 2, "one use button for the one usable line, a drop button per line");
            _inventoryPanel.UseButtonsForSelfTest[0].EmitSignal(BaseButton.SignalName.Pressed);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(_inventoryStatus.EndsWith("'s use facts do not read here", StringComparison.Ordinal) && _inventoryPanel.UseButtonsForSelfTest.Count == 0,
                "the use button ran the app's use: " + _inventoryStatus);
            // ARC-SKILLS-006: a lockpick line gets a Pick a lock button; lockpicks the host does not know are refused and the window stays.
            _inventoryPanel.Render(new AreaInventoryView([new("syn:picks", "lockpicks") { Lockpick = true }], null, null, []), null);
            Require(_inventoryPanel.PickButtonsForSelfTest.Count == 1 && _inventoryPanel.UseButtonsForSelfTest.Count == 0, "one pick button for the one lockpick line");
            _inventoryPanel.PickButtonsForSelfTest[0].EmitSignal(BaseButton.SignalName.Pressed);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(InventoryOpen && _lockpickArmed is null && _inventoryStatus == "not used: not lockpicks the hero holds", "unknown lockpicks: " + _inventoryStatus);
            // ARC-SKILLS-007: a damaged line gets a Repair button; an item without a known condition is not repaired.
            _inventoryPanel.Render(new AreaInventoryView([new("syn:worn", "sword") { Repairable = true }], null, null, []), null);
            Require(_inventoryPanel.RepairButtonsForSelfTest.Count == 1, "one repair button for the one damaged line");
            _inventoryPanel.RepairButtonsForSelfTest[0].EmitSignal(BaseButton.SignalName.Pressed);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(InventoryOpen && _inventoryStatus == "not repaired: no condition for this item", "unknown item: " + _inventoryStatus);
            // ARC-UI-015: a doll slot with an item gets a take-off button and a wearable line a wear button per slot; this session has no
            // hero, so the app's wear finds no wear values and its take-off no hero.
            _inventoryPanel.Render(new AreaInventoryView([new("syn:ring", "ring") { WearSlots = [EquipmentSlot.Ring1, EquipmentSlot.Ring2] }], null, null, [])
                { Doll = [new(EquipmentSlot.Shield, "syn:torch", "torch"), new(EquipmentSlot.Helmet, null, null)] }, null);
            Require(_inventoryPanel.WearButtonsForSelfTest.Count == 2 && _inventoryPanel.TakeOffButtonsForSelfTest.Count == 1
                && _inventoryPanel.DollTextForSelfTest.Contains("shield: torch", StringComparison.Ordinal), "two ring wear buttons, one take-off button: " + _inventoryPanel.DollTextForSelfTest);
            _inventoryPanel.WearButtonsForSelfTest[1].EmitSignal(BaseButton.SignalName.Pressed);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(InventoryOpen && _inventoryStatus.StartsWith("not worn: ", StringComparison.Ordinal) && _inventoryStatus.EndsWith("has no wear values here", StringComparison.Ordinal),
                "the wear button ran the app's wear: " + _inventoryStatus);
            _inventoryPanel.Render(new AreaInventoryView([], null, null, []) { Doll = [new(EquipmentSlot.Shield, "syn:torch", "torch")] }, null);
            _inventoryPanel.TakeOffButtonsForSelfTest[0].EmitSignal(BaseButton.SignalName.Pressed);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(InventoryOpen && _inventoryStatus == "not taken off: no hero", "the take-off button ran the app's take-off: " + _inventoryStatus);
            // ARC-UI-019: a press on a grid item holds it and its release on another cell asks the app for the move; this item is unknown here.
            _inventoryPanel.Render(new AreaInventoryView([], null, null, []) { Grid = [new("syn:box", 0, 2, 1, "box")] }, null);
            var area = _inventoryPanel.GridAreaForSelfTest;
            area.PressAt(AreaInventoryGridArea.CellCentre(1));
            Require(area.Held == new AreaInventoryHeld("syn:box", null, 1), "the press holds the box with its grab offset");
            area.ReleaseAt(AreaInventoryGridArea.CellCentre(16));
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(InventoryOpen && area.Held is null && _inventoryStatus.StartsWith("not moved: ", StringComparison.Ordinal) && _inventoryStatus.EndsWith("facts do not read here", StringComparison.Ordinal), "the release ran the app's move: " + _inventoryStatus);
            area.PressAt(AreaInventoryGridArea.CellCentre(0));
            area.ReleaseAt(new(-20, -20));
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(_inventoryStatus.EndsWith(" went back to its place", StringComparison.Ordinal), "a release outside puts it back: " + _inventoryStatus);
            InventoryKey(Key.Escape);
            Require(!InventoryOpen, "Escape closed the loot window");
            return $"walk to pick up and its ground-click cancel, pickup at reach {AreaInventoryHost.Reach}, I/Escape toggle, loot by take button, heal use button, pick a lock button, repair button, wear and take-off buttons";
        }
        finally
        {
            HoverReleaseForSelfTest();
            if (InventoryOpen) EndInventory();
            ReleaseSimulatedPadForSelfTest();
            _worldFocusSimulationForSelfTest = simulatedFocus;
        }
    }

    /// <summary>Replaces the viewer's inventory seeds (explicit test launch only) and attaches them to the area session.</summary>
    internal void InventoryForSelfTest(AreaInventoryHost host)
    {
        if (!SelfTestBootstrap && !_nativeSmoke) throw new InvalidOperationException("Explicit test launch required.");
        UnbindAreaInventory();
        AdoptAreaInventory(host);
        if (_inventoryHost is null) throw new InvalidOperationException("AREA_INVENTORY_SELF_TEST_SEED " + _inventoryStatus);
    }

    // One key press and release through the production keyboard route.
    private void InventoryKey(Key key)
    {
        InputWorld(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        InputWorld(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
    }

    /// <summary>
    /// ARC-UI-004 on the original exterior (native smoke): a ground item picked up next to it, another reached by walk to
    /// pick up, the dead NPCs looted with "take all" (ARC-SCRIPT-013: the amulet's insert-item script runs and prints its
    /// line, shown only on screen), and the Inventory key showing every taken
    /// item by its resolved name. Prints counts and outcomes only.
    /// </summary>
    private async Task RunAreaInventorySmokeAsync()
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("AREA_INVENTORY_SMOKE " + why); }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            Require(InventoryActive, _inventoryStatus);
            for (var i = 0; i < 600 && _hoverNames is null; i++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); AdoptHoverNames(); }
            Require(_hoverNames?.Status == AreaNamesStatus.Ready, "names " + _hoverNames?.Status);
            var map = _areaTransitions!.Current;
            var start = _areaSession!.QueryPcInventory().Count;
            var current = _areaSession.QueryMapInventory(new(map.RuntimeMap))!;
            var commands = _readyArea!.Plan.Commands.Where(c => c.Identity is not null).ToDictionary(c => c.Identity!.Value);
            var groundKeys = current.Ground.Select(g => _inventoryHost!.Target(_areaSession, map.RuntimeMap, KeyOf(g.Item.Entity))).OfType<AreaInventoryTarget>()
                .Where(t => commands.ContainsKey(t.Row.Key) && t.Row.Scope == AreaScope.Core).ToArray();
            Require(groundKeys.Length >= 2, $"ground items drawn: {groundKeys.Length}");
            AreaObjectKey KeyOf(string entity) => _inventoryHost!.Maps.Single(m => m.RuntimeMap == map.RuntimeMap).Row(entity).Key;

            // 1. Next to an item: one press takes it and it leaves the world.
            var first = groundKeys[0];
            AreaSmokePlace(map, ApproachCell((AreaGridSession)_worldActor.Session!, first.Row.WorldX!.Value, first.Row.WorldY!.Value, AreaInventoryHost.Reach)!.Value);
            var point = await HoverPointAsync(this, commands[first.Row.Key]) ?? throw new InvalidOperationException("AREA_INVENTORY_SMOKE item not visible");
            HoverAtForSelfTest(point);
            Require(HoverActionForSelfTest == AreaHoverAction.PickUp, "hover " + HoverActionForSelfTest);
            AreaSmokeClick(point);
            Require(_areaSession.QueryPcInventory().Count == start + 1 && AreaObjectAtScreen(point) != first.Row.Key, "pickup next to it: " + _inventoryStatus);

            // 2. From the spawn: a press on a farther item walks within reach and takes it on arrival.
            var second = groundKeys.Skip(1).OrderBy(t => Chebyshev(t.Row, (map.Chart.ToWorld(map.Chart.Spawn).X, map.Chart.ToWorld(map.Chart.Spawn).Y)))
                .First(t => Chebyshev(t.Row, (map.Chart.ToWorld(map.Chart.Spawn).X, map.Chart.ToWorld(map.Chart.Spawn).Y)) > AreaInventoryHost.Reach);
            AreaSmokePlace(map, map.Chart.Spawn);
            point = await HoverPointAsync(this, commands[second.Row.Key]) ?? throw new InvalidOperationException("AREA_INVENTORY_SMOKE far item not visible");
            AreaSmokeClick(point);
            Require(_inventoryAfterRoute is not null, "walk to pick up started: " + _inventoryStatus);
            for (var i = 0; i < 600 && _areaSession.QueryPcInventory().Count == start + 1; i++) AreaSmokeFrame();
            Require(_areaSession.QueryPcInventory().Count == start + 2 && _inventoryHidden?.Contains(second.Row.Key) == true, "walk to pick up took it: " + _inventoryStatus);
            var walkFrames = _areaSmokeTime;

            // 3. Every dead NPC with items: the press opens the body, "take all" takes what the rules admit.
            var bodies = _areaSession.QueryMapInventory(new(map.RuntimeMap))!.Holders.Where(h => h.Dead && h.Items.Count > 0)
                .Select(h => _inventoryHost!.Target(_areaSession, map.RuntimeMap, KeyOf(h.Entity))!).ToArray();
            var looted = 0; var left = 0; var printed = 0;
            foreach (var body in bodies)
            {
                var scripted = _areaSession.QueryMapInventory(new(map.RuntimeMap))!.Holders.Single(h => h.Entity == body.Entity).Items
                    .Any(item => item.Facts.HasScript(SessionScriptPoint.InsertItem));
                AreaSmokePlace(map, ApproachCell((AreaGridSession)_worldActor.Session!, body.Row.WorldX!.Value, body.Row.WorldY!.Value, AreaInventoryHost.Reach)!.Value);
                point = await HoverPointAsync(this, commands[body.Row.Key]) ?? throw new InvalidOperationException("AREA_INVENTORY_SMOKE body not visible");
                var before = _areaSession.QueryPcInventory().Count;
                AreaSmokeClick(point);
                Require(InventoryOpen && _inventoryPanel!.Holder == body.Entity, "the press opened the dead NPC");
                _inventoryPanel!.TakeAllForSelfTest.EmitSignal(BaseButton.SignalName.Pressed);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                looted += _areaSession.QueryPcInventory().Count - before; left += _inventoryPanel.ViewForSelfTest!.Loot.Count;
                // The printed line follows " — " in the message; its text is not logged.
                if (scripted) { Require(_inventoryStatus.Contains(" — ", StringComparison.Ordinal), "the amulet's script printed its line"); printed++; }
                EndInventory();
            }
            // docs/world/inventory-runtime.md: four dead NPCs carry six items; the amulet's insert-item script runs (ARC-SCRIPT-013).
            Require((bodies.Length, looted, left, printed) == (4, 6, 0, 1), $"loot bodies {bodies.Length} taken {looted} left {left} printed {printed}");

            // 4. The Inventory key lists every taken item by its resolved name.
            AreaSmokePlace(map, map.Chart.Spawn);
            InventoryKey(Key.I);
            var view = _inventoryPanel!.ViewForSelfTest!;
            Require(InventoryOpen && view.Pc.Count == _areaSession.QueryHeroKit().Count + start + 2 + looted && view.Pc.All(line => !line.Text.StartsWith('[')), "I lists the named items");
            // ARC-UI-015: with a hero, every worn item comes off through its take-off button and goes back on through its wear button.
            var wornCycled = 0;
            var dragged = 0;
            if (_areaSession.QueryHero() is not null)
            {
                Require(view.Doll.Count == 9 && view.Carry is not null && _inventoryPanel.HeaderForSelfTest.Contains("encumbrance level", StringComparison.Ordinal), "the doll and weight line");
                foreach (var slot in view.Doll.Where(slot => slot.Entity is not null).ToArray())
                {
                    var button = _inventoryPanel.TakeOffButtonsForSelfTest.First(b => b.Text.StartsWith($"Take off ({AreaInventoryHost.SlotName(slot.Slot)})", StringComparison.Ordinal));
                    button.EmitSignal(BaseButton.SignalName.Pressed);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    Require(_areaSession.QueryEquipment().Worn.All(w => w.Slot != slot.Slot) && _inventoryStatus.StartsWith("took off ", StringComparison.Ordinal), "take off: " + _inventoryStatus);
                    var target = _inventoryPanel.WearTargetsForSelfTest.ToList().IndexOf((slot.Entity!, slot.Slot));
                    Require(target >= 0, $"a wear button for {slot.Entity} in {slot.Slot}");
                    var wear = _inventoryPanel.WearButtonsForSelfTest[target];
                    wear.EmitSignal(BaseButton.SignalName.Pressed);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    Require(_areaSession.QueryEquipment().Worn.Any(w => w.Slot == slot.Slot && w.Entity == slot.Entity) && _inventoryStatus.StartsWith("wore ", StringComparison.Ordinal), "wear: " + _inventoryStatus);
                    wornCycled++;
                }
                view = _inventoryPanel.ViewForSelfTest!;
                (dragged, view) = await DragForSmoke(view);
                view = await DropAndUseForSmoke();
            }
            // ARC_AREA_INVENTORY_CAPTURE saves an in-app readback of the open window to the caller's ignored path.
            if (System.Environment.GetEnvironmentVariable("ARC_AREA_INVENTORY_CAPTURE") is { Length: > 0 } capture)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                if (image.SavePng(capture) != Error.Ok) throw new InvalidOperationException("AREA_INVENTORY_CAPTURE_FAILED");
            }
            InventoryKey(Key.I);
            Require(!InventoryOpen, "I closed the window");
            AreaSmokePlace(map, map.Chart.Spawn); PublishWorldSelection(); _camera!.ResetWorld(WorldAnchor, WorldViewport, _worldPreferredZoom);
            GD.Print("ARC-UI-004 native-smoke inventory PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                GroundDrawn = groundKeys.Length, PickedNextTo = 1, WalkToPickUpFrames = walkFrames, DeadBodies = bodies.Length, Looted = looted, LeftOnBodies = left,
                PrintedLines = printed, WornCycled = wornCycled, Dragged = dragged, Grid = _areaSession.QueryHeroGrid().Items.Count,
                Pc = _areaSession.QueryPcInventory().Count, Named = view.Pc.Count, Parity = "NOT_VERIFIED (diagnostic window, no original art)" }));
        }
        finally
        {
            HoverReleaseForSelfTest();
            if (InventoryOpen) EndInventory();
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
        }
    }

    /// <summary>ARC-UI-019: drags with the mouse's press and release on the window's grid area: a grid item to a free cell and back, the
    /// first worn item off into a free cell and back onto its slot. Returns the drags that moved and the window's view after them.</summary>
    private async Task<(int Dragged, AreaInventoryView View)> DragForSmoke(AreaInventoryView view)
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("AREA_INVENTORY_SMOKE " + why); }
        var area = _inventoryPanel!.GridAreaForSelfTest;
        var session = _areaSession!;
        int? Free() => Enumerable.Range(0, Arcanum.Session.SessionHeroGrid.Slots).Cast<int?>().FirstOrDefault(cell => AreaInventoryDrag.ItemAt(_inventoryPanel.ViewForSelfTest!.Grid, cell!.Value) is null);
        int SlotOf(string entity) => session.QueryHeroGrid().Items.FirstOrDefault(item => item.Entity == entity)?.Slot ?? -1;
        async Task Drag(Vector2 from, Vector2 to)
        {
            area.PressAt(from);
            Require(area.Held is not null, "the press holds an item");
            area.ReleaseAt(to);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        var dragged = 0;
        if (view.Grid.FirstOrDefault(item => item.Width == 1 && item.Height == 1) is { } small && Free() is { } free)
        {
            await Drag(AreaInventoryGridArea.CellCentre(small.Slot), AreaInventoryGridArea.CellCentre(free));
            Require(SlotOf(small.Entity) == free && _inventoryStatus.StartsWith("moved ", StringComparison.Ordinal), "grid move: " + _inventoryStatus);
            await Drag(AreaInventoryGridArea.CellCentre(free), AreaInventoryGridArea.CellCentre(small.Slot));
            Require(SlotOf(small.Entity) == small.Slot, "grid move back: " + _inventoryStatus);
            dragged += 2;
        }
        if (_inventoryPanel.ViewForSelfTest!.Doll.FirstOrDefault(slot => slot.Entity is not null) is { } worn && Free() is { } cell)
        {
            await Drag(AreaInventoryGridArea.SlotCentre(worn.Slot), AreaInventoryGridArea.CellCentre(cell));
            Require(SlotOf(worn.Entity!) == cell && session.QueryEquipment().Worn.All(item => item.Entity != worn.Entity), "take off into a cell: " + _inventoryStatus);
            await Drag(AreaInventoryGridArea.CellCentre(cell), AreaInventoryGridArea.SlotCentre(worn.Slot));
            Require(session.QueryEquipment().Worn.Any(item => item.Entity == worn.Entity && item.Slot == worn.Slot) && area.Held is null, "back onto its slot: " + _inventoryStatus);
            dragged += 2;
        }
        GD.Print($"ARC-UI-019 native-smoke drag PASS dragged={dragged} grid={session.QueryHeroGrid().Items.Count} unplaced={session.QueryHeroGrid().Unplaced.Count}");
        return (dragged, _inventoryPanel.ViewForSelfTest!);
    }

    /// <summary>ARC-UI-025: a right press on a worn item does nothing; a usable item (when the hero holds one) is used; a taken item dragged
    /// onto the drop icon lies on the hero's tile and is taken back. Returns the window's view after them.</summary>
    private async Task<AreaInventoryView> DropAndUseForSmoke()
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("AREA_INVENTORY_SMOKE " + why); }
        var area = _inventoryPanel!.GridAreaForSelfTest;
        var session = _areaSession!;
        var view = _inventoryPanel.ViewForSelfTest!;
        if (view.Doll.FirstOrDefault(slot => slot.Entity is not null) is { } worn)
        {
            var revision = session.Version.Revision;
            area.RightPressAt(AreaInventoryGridArea.SlotCentre(worn.Slot));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(LastItemUseForSelfTest is { Gate: AreaItemUseGate.Wearable } && session.Version.Revision == revision, "a right press on a worn item does nothing: " + _inventoryStatus);
        }
        var usable = view.Pc.Where(line => line.Usable).Select(line => line.Entity).ToArray();
        string used = "-";
        if (usable.FirstOrDefault(entity => view.Grid.Any(item => item.Entity == entity)) is { } entity)
        {
            area.RightPressAt(AreaInventoryGridArea.CellCentre(view.Grid.First(item => item.Entity == entity).Slot));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(LastItemUseForSelfTest is { Gate: AreaItemUseGate.Use, Step: not null } last && last.Item == entity, "a usable item is used: " + _inventoryStatus);
            used = LastItemUseForSelfTest!.Value.Step!.Value.ToString();
            if (ReadingOpen) EndReading();
            if (!InventoryOpen) InventoryKey(Key.I);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        var taken = session.QueryPcInventory().Select(item => item.Entity).ToHashSet(StringComparer.Ordinal);
        var drop = _inventoryPanel.ViewForSelfTest!.Grid.FirstOrDefault(item => taken.Contains(item.Entity) && session.QueryAmmo(item.Entity) is not { Units: > 1 }
            && _inventoryHost!.GridMoveItem(session, item.Entity, _areaKitGridMove) is { NoDrop: false, ScriptPoints.Count: 0 });
        Require(drop is not null, "a taken item to drop");
        area.PressAt(AreaInventoryGridArea.CellCentre(drop!.Slot));
        area.ReleaseAt(AreaInventoryGridArea.IconCentre(AreaInventoryIcon.Drop));
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var placed = session.QueryPlacedItems().FirstOrDefault(item => item.Entity == drop.Entity);
        Require(LastGroundDropForSelfTest is { Disposition: GameSessionDisposition.Changed } && placed is not null && placed.Cell == session.QueryActor().Cell
            && !session.QueryPcInventory().Any(item => item.Entity == drop.Entity), "the drop icon dropped the item on the hero's tile: " + _inventoryStatus);
        var pile = session.QueryJunkPiles().FirstOrDefault(p => p.Items.Any(item => item.Entity == drop.Entity));
        var back = pile is null ? _inventoryHost!.PickUp(session, drop.Entity) : _inventoryHost!.Loot(session, pile.Entity, drop.Entity);
        Require(back.Disposition == GameSessionDisposition.Changed && session.QueryPcInventory().Any(item => item.Entity == drop.Entity), "taken back: " + back.Failure);
        if (pile is not null) session.CloseJunkPile(session.Version, pile.Entity);
        _inventoryPanel.Render(InventoryView(null), null);
        GD.Print($"ARC-UI-025 native-smoke drop-use PASS used={used} usable={usable.Length} dropped=1 pile={pile is not null} cell={placed!.Cell}");
        return _inventoryPanel.ViewForSelfTest!;
    }
}
