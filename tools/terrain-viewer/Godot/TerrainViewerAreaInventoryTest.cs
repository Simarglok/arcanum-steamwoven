using System.Reflection;
using Arcanum.Data.Area;
using Arcanum.Presentation.Hud;
using Arcanum.Session;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.TerrainViewer.Tests;
using Godot;
using Arcanum.World;

namespace Arcanum.TerrainViewer;

public static partial class TerrainViewerSelfTest
{
    /// <summary>ARC-UI-004 over the strict synthetic AREA owner in the production app: an invented seed makes one drawn
    /// body a ground item and another a dead NPC; pickup and loot run through the production click path and window.</summary>
    private static async Task AssertAreaInventoryAsync(Node host)
    {
        using var source = new AreaOwnerFixture(motionBodies: true);
        var read = typeof(TerrainLoadCoordinator).Assembly.GetType("Arcanum.World.AreaLoadOperation")!
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
            // ARC-UI-029: two more rows name the body's arrows and coins (a map's rows are distinct per entity).
            var spare = rows.Where(row => row.Key != item.Key && row.Key != body.Key).Take(3).ToArray();
            Require(spare.Length == 3, "three more synthetic rows");
            var listed = spare[0];
            static SessionItemFacts Facts(SessionItemType type, int name) => new(new(type, 4000 + name, name, 1), 1, 0, 0, null, []);
            var map = _readyArea.Snapshot.RuntimeMap;
            // The synthetic records share one object id, so the seed uses invented entity ids.
            InventoryForSelfTest(new AreaInventoryHost([new AreaInventoryMap(map,
                new([new("syn:body", SessionHolderKind.Npc, true, false, [], ["syn:listed", "syn:arrows", "syn:coins"])],
                    [new("syn:item", Facts(SessionItemType.Food, 1), new CellCoord(checked((int)item.WorldX!.Value), checked((int)item.WorldY!.Value))),
                     new("syn:listed", Facts(SessionItemType.Weapon, 2), null),
                     // ARC-UI-029: an arrow stack and a gold pile on the body ask how many.
                     new("syn:arrows", Facts(SessionItemType.Ammo, 3) with { Quantity = 12, AmmoType = 0 }, null),
                     new("syn:coins", Facts(SessionItemType.Gold, 4) with { Quantity = 7 }, null)]),
                new Dictionary<string, AreaObjectRecord> { ["syn:body"] = body, ["syn:item"] = item, ["syn:listed"] = listed, ["syn:arrows"] = spare[1], ["syn:coins"] = spare[2] })]));

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
            Require(ShownHeroRunsHost?.PickupRun is { } pickup && pickup.Bottom.Goal == Arcanum.Simulation.Goals.GoalType.PickupItem, "the far press starts the pickup goal 15: " + _inventoryStatus);
            // ARC-DEV-175: the goal walks the hero onto the item's tile through goal 8 (range 0); the table steps while the area clock is ticked.
            for (var i = 0; i < 20 && _worldRoute?.Target is null; i++) SelfTestAreaFrame();
            Require(_worldRoute?.Target is not null, "the pickup goal walks the hero: " + _inventoryStatus);
            PublishWorldSelection(); _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            var ground = GroundPoint(session.State.CommittedCell) ?? throw new InvalidOperationException("AREA inventory: no bare ground at the actor");
            _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = ground, GlobalPosition = ground });
            Require(ShownHeroRunsHost?.PickupRun is null && _areaSession.QueryPcInventory().Count == 0, "a ground press cancels the walk to pick up: " + _inventoryStatus);
            StopWorldRoute("ARC-UI-004 self-test");

            // Stand on the item's tile (goal 15 picks up from there, range 0): the press takes it and it leaves the world.
            var near = session.Chart.ToChart(new Arcanum.Data.Traversal.AreaStepCell(checked((int)item.WorldX!.Value), checked((int)item.WorldY!.Value)));
            ConfigureAreaActor(AreaRuntimes()[map], near, NeighborDirection.XPlus); OpenHoverGate(steps);
            point = await HoverPointAsync(host, itemCommand) ?? throw new InvalidOperationException("AREA inventory: item not visible next to it");
            HoverAtForSelfTest(point);
            Require(HoverActionForSelfTest == AreaHoverAction.PickUp, "item within reach: " + HoverActionForSelfTest);
            _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = point, GlobalPosition = point });
            for (var i = 0; i < 40 && _areaSession.QueryPcInventory().Count == 0; i++) SelfTestAreaFrame();
            Require(_areaSession.QueryPcInventory().Select(i => i.Entity).SequenceEqual(["syn:item"]), "the press picked the item up: " + _inventoryStatus);
            Require(_canvas!.AreaHidden?.Contains(item.Key) == true && AreaObjectAtScreen(point) != item.Key, "the taken item is neither drawn nor picked");
            Require(_inventoryStatus.StartsWith("picked up", StringComparison.Ordinal), "status " + _inventoryStatus);
            Require(GetSelfTestSnapshot().Hud.Contains(" | PC items 1 | picked up", StringComparison.Ordinal), "HUD inventory status");

            // The original Inventory key opens the window (modal) and closes it again; Escape closes it too.
            InventoryKey(Key.I);
            Require(InventoryOpen && WorldModal && WindowPanelsMatchStack && _inventoryPanel!.ViewForSelfTest!.Pc.Count == 1 && _inventoryPanel.Holder is null, "I opened the window");
            Require(_inventoryPanel!.PcTextForSelfTest.Contains("name unavailable] (food, weight 1)", StringComparison.Ordinal), "the PC's list names the item: " + _inventoryPanel.PcTextForSelfTest);
            HoverAtForSelfTest(point);
            Require(HoverActionForSelfTest == AreaHoverAction.None && HoverCursorForSelfTest == Godot.Input.CursorShape.Arrow, "the modal window hides the hover");
            InventoryKey(Key.I);
            Require(!InventoryOpen && !_worldGate.IsOpen, "I closed the window and quarantined the click guards");
            // ARC-DEV-111 (SA51, 0x573440): Escape is the main loop's key; the menu it opens closes the inventory family (the window here), and
            // Escape again (Continue) closes the menu.
            OpenHoverGate(steps); InventoryKey(Key.I); InventoryKey(Key.Escape);
            Require(!InventoryOpen && MainMenuOpen && _mainMenu!.Window == AreaMenuWindow.InPlay && WindowPanelsMatchStack, "Escape opened the menu, which closed the window");
            InventoryKey(Key.Escape);
            Require(!MainMenuOpen && !InventoryOpen, "Escape continued play");

            // The dead NPC opens to loot; its item goes to the PC through the window's take button.
            near = ApproachCell(session = (AreaGridSession)_worldActor.Session!, body.WorldX!.Value, body.WorldY!.Value, AreaInventoryHost.Reach)
                ?? throw new InvalidOperationException("AREA inventory: no cell next to the body");
            ConfigureAreaActor(AreaRuntimes()[map], near, NeighborDirection.XPlus); OpenHoverGate(steps);
            point = await HoverPointAsync(host, bodyCommand) ?? throw new InvalidOperationException("AREA inventory: body not visible");
            HoverAtForSelfTest(point);
            Require(HoverActionForSelfTest == AreaHoverAction.Loot, "dead NPC within reach: " + HoverActionForSelfTest);
            _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = point, GlobalPosition = point });
            for (var i = 0; i < 40 && !InventoryOpen; i++) SelfTestAreaFrame();
            Require(InventoryOpen && WindowPanelsMatchStack && _inventoryPanel!.Holder == "syn:body" && _inventoryPanel.TakeButtonsForSelfTest.Count == 3, "the press opened the body to loot");
            _inventoryPanel!.TakeButtonsForSelfTest[0].EmitSignal(BaseButton.SignalName.Pressed);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(_areaSession.QueryPcInventory().Count == 2 && _inventoryPanel.ViewForSelfTest!.Loot.Count == 2 && !_inventoryPanel.TakeAllForSelfTest.Disabled,
                "the take button moved the item: " + _inventoryPanel.MessageForSelfTest);
            Require(_inventoryPanel.MessageForSelfTest.StartsWith("took ", StringComparison.Ordinal), "window message " + _inventoryPanel.MessageForSelfTest);
            var quantity = await QuantityForSelfTestAsync(host);
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
            Require(!InventoryOpen && MainMenuOpen && WindowPanelsMatchStack && TargetsMatchStack, "Escape opened the menu, which closed the loot window (mode 10)");
            InventoryKey(Key.Escape);
            Require(!MainMenuOpen, "Escape continued play");
            return $"walk to pick up and its ground-click cancel, pickup from the item's tile, I/Escape toggle, loot by take button, {quantity}, heal use button, pick a lock button, repair button, wear and take-off buttons";
        }
        finally
        {
            HoverReleaseForSelfTest();
            if (InventoryOpen) EndInventory();
            ReleaseSimulatedPadForSelfTest();
            _worldFocusSimulationForSelfTest = simulatedFocus;
        }
    }

    /// <summary>
    /// ARC-UI-029 over the synthetic body (its arrows ×12 and coins ×7 after the weapon was taken): a take asks how many through the
    /// production keys and buttons, a cancel moves nothing, gold needs a hero (this session has none), and a drop of the taken arrows
    /// asks again and lays the chosen units on the actor's tile. Returns a summary.
    /// </summary>
    private async Task<string> QuantityForSelfTestAsync(Node host)
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("AREA inventory quantity: " + why); }
        async Task Frame() => await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        var panel = _inventoryPanel!;
        var session = _areaSession!;
        Button Take(string entity) => panel.TakeButtonsForSelfTest[panel.ViewForSelfTest!.Loot.ToList().FindIndex(line => line.Entity == entity)];
        Require(panel.ViewForSelfTest!.Loot.Any(line => line.Entity == "syn:coins" && line.Text.EndsWith(" ×7", StringComparison.Ordinal) && line.Units == 7),
            "the gold pile shows its coins");
        // The arrows: keypad 1 then 5 is clamped to 12, Backspace leaves 1, +1 makes 2, Enter takes 2.
        Take("syn:arrows").EmitSignal(BaseButton.SignalName.Pressed);
        await Frame();
        Require(panel.Asking && panel.QuantityForSelfTest == new QuantityBar(0, 12) && panel.TakeAllForSelfTest.Disabled && panel.GridAreaForSelfTest.Blocked,
            "the take asks with the rest of the window inert: " + _inventoryStatus);
        // ARC-DEV-110 (MIG16 S3): the question is interface mode 13 over the inventory window, which keeps its window.
        Require(TargetModeOpen(InterfaceMode.QuantityPicker) && TargetsMatchStack && InventoryOpen && WindowPanelsMatchStack && _interfaceWindows.Stack.Current == InterfaceMode.QuantityPicker
            && CursorMode == AreaCursorMode.Window, "the question is mode 13 over the inventory: " + StackText());
        InventoryKey(Key.Kp1); InventoryKey(Key.Kp5);
        Require(panel.QuantityForSelfTest!.Value.Count == 12, "15 is clamped to the stack: " + panel.QuantityTextForSelfTest);
        InventoryKey(Key.Backspace);
        panel.QuantityButtonsForSelfTest[1].EmitSignal(BaseButton.SignalName.Pressed);
        await Frame();
        Require(panel.QuantityForSelfTest!.Value.Count == 2, "Backspace then +1: " + panel.QuantityTextForSelfTest);
        InventoryKey(Key.Key1);
        Require(panel.QuantityForSelfTest!.Value.Count == 2, "a main-row digit does nothing (QW4)");
        InventoryKey(Key.Enter);
        var hero = LastQuantityMoveForSelfTest?.Item;
        Require(InventoryOpen && !panel.Asking && LastQuantityMoveForSelfTest is { Disposition: GameSessionDisposition.Changed } && session.QueryAmmo("syn:arrows")?.Units == 10
            && hero is not null && session.QueryAmmo(hero)?.Units == 2, "Enter took 2 arrows: " + _inventoryStatus);
        Require(!TargetModeOpen(InterfaceMode.QuantityPicker) && TargetsMatchStack && _interfaceWindows.Stack.Current != InterfaceMode.QuantityPicker && WindowPanelsMatchStack,
            "Accept left mode 13 and kept the inventory: " + StackText());
        // The coins: I cancels (the window stays; Escape would open the menu, which closes the window with the question); All then Accept asks the session, which has no hero to hold gold.
        Take("syn:coins").EmitSignal(BaseButton.SignalName.Pressed);
        await Frame();
        Require(panel.QuantityForSelfTest == new QuantityBar(0, 7), "the coins ask");
        InventoryKey(Key.I);
        Require(InventoryOpen && !panel.Asking && _inventoryStatus.StartsWith("nothing moved", StringComparison.Ordinal) && !panel.TakeAllForSelfTest.Disabled,
            "I cancelled the question only: " + _inventoryStatus);
        Require(!TargetModeOpen(InterfaceMode.QuantityPicker) && TargetsMatchStack && WindowPanelsMatchStack, "Cancel left mode 13: " + StackText());
        Take("syn:coins").EmitSignal(BaseButton.SignalName.Pressed);
        await Frame();
        panel.QuantityButtonsForSelfTest[0].EmitSignal(BaseButton.SignalName.Pressed);
        await Frame();
        Require(panel.QuantityForSelfTest!.Value.Count == 7, "All");
        panel.QuantityButtonsForSelfTest[3].EmitSignal(BaseButton.SignalName.Pressed);
        await Frame();
        Require(LastQuantityMoveForSelfTest is { Failure: GameSessionFailure.GoldUnavailable } && session.QueryNpcGold("syn:body") == 7,
            "no hero holds gold: " + _inventoryStatus);
        var gold = LastQuantityMoveForSelfTest!.Value.Failure;
        // The taken arrows: a drop asks; 1 lies on the actor's tile and 1 stays.
        var line = panel.ViewForSelfTest!.Pc.ToList().FindIndex(entry => entry.Entity == hero);
        Require(line >= 0 && panel.ViewForSelfTest.Pc[line].Units == 2, "the hero's stack is listed with its units");
        panel.DropButtonsForSelfTest[line].EmitSignal(BaseButton.SignalName.Pressed);
        await Frame();
        Require(panel.QuantityForSelfTest == new QuantityBar(0, 2), "the drop asks: " + _inventoryStatus);
        InventoryKey(Key.Kp1); InventoryKey(Key.KpEnter);
        var dropped = LastQuantityMoveForSelfTest?.Item;
        Require(LastQuantityMoveForSelfTest is { Disposition: GameSessionDisposition.Changed } && session.QueryAmmo(hero!)?.Units == 1 && dropped is not null
            && session.QueryPlacedItems().Any(item => item.Entity == dropped) && session.QueryAmmo(dropped)?.Units == 1, "one arrow dropped: " + _inventoryStatus);
        // The header's ammunition counts are the hero's (QueryHeroAmmo); this session has no hero, so they stay 0.
        Require(panel.HeaderForSelfTest.Contains("| arrows 0 | bullets 0 | charges 0 | fuel 0", StringComparison.Ordinal), "the header's counts: " + panel.HeaderForSelfTest);
        GD.Print($"ARC-UI-029 self-test quantity took=2 cancelled=1 gold={gold} dropped=1");
        return "quantity window (take 2 of 12 arrows by keypad and buttons, I cancel, gold without a hero, drop 1 of 2)";
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

    // ARC-DEV-175: a frame of the synthetic self-test: the area clock ticks 100 ms (the hero's pickup and loot runs step by it) and the world frame advances the grid and applies what the goals asked for.
    private void SelfTestAreaFrame()
    {
        TickAreaClock(_talkSmokeHostMs += 100);
        WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .1, true);
    }

    // The chart cell of the world cell a row stands on (a smoke stands the hero there).
    private CellCoord SmokeChartCellOf(AreaObjectRecord row) =>
        ((AreaGridSession)_worldActor.Session!).Chart.ToChart(new Arcanum.Data.Traversal.AreaStepCell(checked((int)row.WorldX!.Value), checked((int)row.WorldY!.Value)));

    /// <summary>
    /// ARC-UI-004 on the original exterior (native smoke): a ground item picked up on its tile, another reached by walk to
    /// pick up (the hero walks onto the tile, ARC-DEV-175), the dead NPCs looted with "take all" (ARC-SCRIPT-013: the amulet's insert-item script runs and prints its
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
            AreaSmokePlace(map, SmokeChartCellOf(first.Row));
            var point = await HoverPointAsync(this, commands[first.Row.Key]) ?? throw new InvalidOperationException("AREA_INVENTORY_SMOKE item not visible");
            HoverAtForSelfTest(point);
            Require(HoverActionForSelfTest == AreaHoverAction.PickUp, "hover " + HoverActionForSelfTest);
            AreaSmokeClick(point);
            for (var i = 0; i < 60 && _areaSession.QueryPcInventory().Count == start; i++) AreaSmokeFrame();
            Require(_areaSession.QueryPcInventory().Count == start + 1 && AreaObjectAtScreen(point) != first.Row.Key, "pickup on its tile: " + _inventoryStatus);

            // 2. From the spawn: a press on a farther item walks within reach and takes it on arrival.
            var second = groundKeys.Skip(1).OrderBy(t => Chebyshev(t.Row, (map.Chart.ToWorld(map.Chart.Spawn).X, map.Chart.ToWorld(map.Chart.Spawn).Y)))
                .First(t => Chebyshev(t.Row, (map.Chart.ToWorld(map.Chart.Spawn).X, map.Chart.ToWorld(map.Chart.Spawn).Y)) > AreaInventoryHost.Reach);
            AreaSmokePlace(map, map.Chart.Spawn);
            point = await HoverPointAsync(this, commands[second.Row.Key]) ?? throw new InvalidOperationException("AREA_INVENTORY_SMOKE far item not visible");
            AreaSmokeClick(point);
            Require(ShownHeroRunsHost?.PickupRun is not null, "walk to pick up started: " + _inventoryStatus);
            for (var i = 0; i < 600 && _areaSession.QueryPcInventory().Count == start + 1; i++) AreaSmokeFrame();
            Require(_areaSession.QueryPcInventory().Count == start + 2 && _inventoryHidden?.Contains(second.Row.Key) == true, "walk to pick up took it: " + _inventoryStatus);
            var walkFrames = _areaSmokeTime;

            // 3. Every dead NPC with items: the press opens the body, "take all" takes what the rules admit.
            var bodies = _areaSession.QueryMapInventory(new(map.RuntimeMap))!.Holders.Where(h => h.Dead && h.Items.Count > 0)
                .Select(h => _inventoryHost!.Target(_areaSession, map.RuntimeMap, KeyOf(h.Entity))!).ToArray();
            var looted = 0; var left = 0; var printed = 0; var lootArt = 0; var lootDragged = false;
            foreach (var body in bodies)
            {
                var scripted = _areaSession.QueryMapInventory(new(map.RuntimeMap))!.Holders.Single(h => h.Entity == body.Entity).Items
                    .Any(item => item.Facts.HasScript(SessionScriptPoint.InsertItem));
                AreaSmokePlace(map, ApproachCell((AreaGridSession)_worldActor.Session!, body.Row.WorldX!.Value, body.Row.WorldY!.Value, AreaInventoryHost.Reach)!.Value);
                point = await HoverPointAsync(this, commands[body.Row.Key]) ?? throw new InvalidOperationException("AREA_INVENTORY_SMOKE body not visible");
                var before = _areaSession.QueryPcInventory().Count;
                AreaSmokeClick(point);
                for (var i = 0; i < 60 && !InventoryOpen; i++) AreaSmokeFrame();
                Require(InventoryOpen && _inventoryPanel!.Holder == body.Entity, "the press opened the dead NPC");
                if (_inventoryArtContent is { LootArtReady: true })
                {
                    lootDragged |= await LootArtForSmoke(lootArt == 0, !scripted && !lootDragged);
                    lootArt++;
                }
                _inventoryPanel!.TakeAllForSelfTest.EmitSignal(BaseButton.SignalName.Pressed);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                looted += _areaSession.QueryPcInventory().Count - before; left += _inventoryPanel.ViewForSelfTest!.Loot.Count;
                // The printed line follows " — " in the message; its text is not logged.
                if (scripted) { Require(_inventoryStatus.Contains(" — ", StringComparison.Ordinal), "the amulet's script printed its line"); printed++; }
                if (_inventoryPanel.ArtMode && _inventoryPanel.GridAreaForSelfTest.LootLook)
                {
                    // ARC-UI-054: a message keeps the frame's scale; the round view shows the game view around the PC (IW33); on the first
                    // body a left release in it closes the window (IW34).
                    var grid = _inventoryPanel.GridAreaForSelfTest; var shown = _inventoryPanel.ViewForSelfTest!;
                    _inventoryPanel.Render(shown, null); var quiet = grid.FrameScale;
                    _inventoryPanel.Render(shown, "took 9 item(s) and a line long enough to reach past the window's right edge, cut there");
                    Require(grid.FrameScale == quiet, $"a message kept the loot frame's scale ({quiet} → {grid.FrameScale})");
                    Require(_inventoryPanel.RoundViewShown, "the loot window's round view shows the game view");
                    if (lootArt == 1)
                    {
                        grid.ReleaseAt(new Vector2(AreaInventoryHole.Corner(AreaInventoryMode.Loot).X + AreaInventoryHole.Side - 1, AreaInventoryHole.Corner(AreaInventoryMode.Loot).Y));
                        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                        Require(!InventoryOpen && !_inventoryPanel.RoundViewShown, "a release in the round view closed the loot window");
                        GD.Print("ARC-UI-054 round view release closed the loot window");
                    }
                }
                if (InventoryOpen) EndInventory();
            }
            // docs/world/inventory-runtime.md: four dead NPCs carry six items; the amulet's insert-item script runs (ARC-SCRIPT-013).
            Require((bodies.Length, looted, left, printed) == (4, 6, 0, 1), $"loot bodies {bodies.Length} taken {looted} left {left} printed {printed}");
            Require(_inventoryArtContent is not { LootArtReady: true } || lootArt == bodies.Length && lootDragged,
                $"every body's loot window showed the original look ({lootArt}/{bodies.Length}) and one item was dragged out ({lootDragged})");

            // 4. The Inventory key lists every taken item by its resolved name.
            AreaSmokePlace(map, map.Chart.Spawn);
            InventoryKey(Key.I);
            // ARC-UI-054: the own window's original look shows the round view too (IW33).
            Require(!_inventoryPanel!.ArtMode || _inventoryPanel.RoundViewShown, "the own window's round view shows the game view");
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
                view = await QuantityForSmoke();
                ArtForSmoke();
            }
            // ARC_AREA_INVENTORY_CAPTURE saves an in-app readback of the open window to the caller's ignored path.
            if (System.Environment.GetEnvironmentVariable("ARC_AREA_INVENTORY_CAPTURE") is { Length: > 0 } capture)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                if (image.SavePng(capture) != Error.Ok) throw new InvalidOperationException("AREA_INVENTORY_CAPTURE_FAILED");
            }
            var artShown = _inventoryPanel?.ArtMode ?? false;
            InventoryKey(Key.I);
            Require(!InventoryOpen, "I closed the window");
            AreaSmokePlace(map, map.Chart.Spawn); PublishWorldSelection(); _camera!.ResetWorld(WorldAnchor, WorldViewport, _worldPreferredZoom);
            GD.Print("ARC-UI-004 native-smoke inventory PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                GroundDrawn = groundKeys.Length, PickedNextTo = 1, WalkToPickUpFrames = walkFrames, DeadBodies = bodies.Length, Looted = looted, LeftOnBodies = left,
                PrintedLines = printed, WornCycled = wornCycled, Dragged = dragged, Grid = _areaSession.QueryHeroGrid().Items.Count,
                Pc = _areaSession.QueryPcInventory().Count, Named = view.Pc.Count,
                Parity = artShown ? "original art and layout (ARC-UI-051), item sizes (ARC-UI-055); fonts and paper-doll art NOT_VERIFIED" : "NOT_VERIFIED (diagnostic window, no original art)" }));
        }
        finally
        {
            HoverReleaseForSelfTest();
            if (InventoryOpen) EndInventory();
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
        }
    }

    /// <summary>ARC-UI-052: with the game's art a dead NPC's loot window shows the loot window's original look: the loot arts and the body's
    /// image read, every item of its grid drawn with its inventory art; and (<paramref name="drag"/>: once, on a body without a scripted
    /// item) one of its items taken by a press on the body's grid and a release on a free cell of the hero's grid.
    /// <c>ARC_AREA_LOOT_CAPTURE=PATH.png</c> saves a readback of the <paramref name="first"/> window. Returns whether an item was dragged.</summary>
    private async Task<bool> LootArtForSmoke(bool first, bool drag)
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("AREA_INVENTORY_SMOKE " + why); }
        var panel = _inventoryPanel!;
        var area = panel.GridAreaForSelfTest;
        var view = panel.ViewForSelfTest!;
        Require(panel.ArtMode && area.LootLook && area.Look is { } look && AreaLootArtLayout.ArtNumbers.All(look.Art.ContainsKey), "the loot window shows the original look");
        Require(view.HolderImage is { } image && area.Look!.Art.ContainsKey(image), $"the body's image {view.HolderImage?.ToString() ?? "none"} reads");
        var drawn = view.LootGrid.Count(item => area.Look!.ItemArt(item.Entity) is not null);
        Require(drawn == view.LootGrid.Count, $"every item of the body's grid has its art ({drawn}/{view.LootGrid.Count})");
        if (first && System.Environment.GetEnvironmentVariable("ARC_AREA_LOOT_CAPTURE") is { Length: > 0 } capture)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var shot = GetViewport().GetTexture().GetImage();
            if (shot.SavePng(capture) != Error.Ok) throw new InvalidOperationException("AREA_LOOT_CAPTURE_FAILED");
        }
        var dragged = "-";
        if (drag && view.LootGrid.FirstOrDefault(item => view.Loot.FirstOrDefault(line => line.Entity == item.Entity)?.StackKind is null
                && AreaLootArtLayout.ItemRect(item, area.LootScroll) is not null) is { } taken)
        {
            var rect = AreaLootArtLayout.ItemRect(taken, area.LootScroll)!.Value;
            var free = Enumerable.Range(0, Arcanum.Session.SessionHeroGrid.Slots).First(cell => AreaInventoryDrag.ItemAt(view.Grid, cell) is null);
            var before = _areaSession!.QueryPcInventory().Count;
            area.PressAt(new(rect.X + rect.Width / 2, rect.Y + rect.Height / 2));
            Require(area.Held is { Partner: true } held && held.Entity == taken.Entity, "the press on the body's grid holds its item");
            area.ReleaseAt(AreaInventoryGridArea.CellCentre(free));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(_areaSession.QueryPcInventory().Any(item => item.Entity == taken.Entity) && _areaSession.QueryPcInventory().Count == before + 1
                && !panel.ViewForSelfTest!.LootGrid.Any(item => item.Entity == taken.Entity), "the release on the hero's grid took it: " + _inventoryStatus);
            dragged = taken.Entity;
        }
        GD.Print($"ARC-UI-052 native-smoke loot-art PASS holder={view.Holder} image={view.HolderImage} grid={view.LootGrid.Count} drawn={drawn} not-in-grid={view.Loot.Count - view.LootGrid.Count} dragged={dragged}");
        return dragged != "-";
    }

    /// <summary>ARC-UI-051: with the game's art the player's own window shows the original look: every window art read, every grid and worn
    /// item's inventory art read, and the switch to the diagnostic view and back.</summary>
    private void ArtForSmoke()
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("AREA_INVENTORY_SMOKE " + why); }
        var panel = _inventoryPanel!;
        Require(panel.ArtMode && panel.GridAreaForSelfTest.Look is { } look && AreaInventoryArtLayout.ArtNumbers.All(look.Art.ContainsKey),
            "the original look shows with every window art");
        var view = panel.ViewForSelfTest!;
        var items = view.Grid.Select(item => item.Entity).Concat(view.Doll.Where(slot => slot.Entity is not null).Select(slot => slot.Entity!)).ToArray();
        var drawn = items.Count(item => panel.GridAreaForSelfTest.Look!.ItemArt(item) is not null);
        Require(items.Length > 0 && drawn == items.Length, $"every item's inventory art reads ({drawn}/{items.Length})");
        var lines = panel.GridAreaForSelfTest.Look!.Texts.Lines(view.Carry, view.Speed);
        Require(lines.All(line => line.Length > 0), "the weight, encumbrance and speed lines: " + string.Join(" | ", lines));
        GD.Print($"ARC-UI-051 native-smoke inventory-art PASS art={AreaInventoryArtLayout.ArtNumbers.Distinct().Count()} items={drawn}/{items.Length} lines=\"{string.Join(" | ", lines)}\"");
        // ARC-UI-055: every grid item takes its inventory art's cells (0x461780), and the kit holds at least one item larger than a cell.
        var sized = view.Grid.Select(item => (item, art: panel.GridAreaForSelfTest.Look!.ItemArt(item.Entity)!))
            .Select(pair => (pair.item, cells: Arcanum.Simulation.Items.InventoryGridRules.CellsOf(((uint)pair.art.GetWidth(), (uint)pair.art.GetHeight())))).ToArray();
        var wrong = sized.Where(pair => pair.cells != (pair.item.Width, pair.item.Height)).Select(pair => $"{pair.item.Entity} {pair.item.Width}x{pair.item.Height} art {pair.cells}").ToArray();
        Require(wrong.Length == 0, "grid items take their art's cells: " + string.Join("; ", wrong));
        Require(sized.Any(pair => pair.item.Width * pair.item.Height > 1), "an item larger than one cell");
        GD.Print($"ARC-UI-055 native-smoke item sizes PASS items={sized.Length} " +
            string.Join(' ', sized.Select(pair => $"{pair.item.Width}x{pair.item.Height}")));
    }

    /// <summary>ARC-UI-019: drags with the mouse's press and release on the window's grid area: a grid item to a free cell and back, the
    /// first worn item off into a free cell and back onto its slot. Returns the drags that moved and the window's view after them.</summary>
    private async Task<(int Dragged, AreaInventoryView View)> DragForSmoke(AreaInventoryView view)
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("AREA_INVENTORY_SMOKE " + why); }
        var area = _inventoryPanel!.GridAreaForSelfTest;
        var session = _areaSession!;
        int? Free() => Enumerable.Range(0, Arcanum.Session.SessionHeroGrid.Slots).Cast<int?>().FirstOrDefault(cell => AreaInventoryDrag.ItemAt(_inventoryPanel.ViewForSelfTest!.Grid, cell!.Value) is null);
        // The first cell where a rectangle of the given size lies inside the grid and meets no item (the pile's art may give it more than one cell).
        int? FreeRect(int width, int height) => Enumerable.Range(0, Arcanum.Session.SessionHeroGrid.Slots).Cast<int?>().FirstOrDefault(cell =>
            cell!.Value % 10 + width <= 10 && cell.Value / 10 + height <= Arcanum.Session.SessionHeroGrid.Rows && Enumerable.Range(0, width * height).All(part =>
                AreaInventoryDrag.ItemAt(_inventoryPanel.ViewForSelfTest!.Grid, cell.Value + part / width * 10 + part % width) is null));
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
        // ARC-UI-090: the hero's gold pile is a grid item too: it moves to a free cell and back with its coins untouched (dropping and
        // throwing the pile are ARC-UI-091's).
        if (view.Grid.FirstOrDefault(item => item.Entity == Arcanum.Session.SessionHeroGrid.GoldEntity) is { } pile && FreeRect(pile.Width, pile.Height) is { } spot)
        {
            var coins = session.QueryGold();
            await Drag(AreaInventoryGridArea.CellCentre(pile.Slot), AreaInventoryGridArea.CellCentre(spot));
            Require(SlotOf(pile.Entity) == spot && _inventoryStatus.StartsWith("moved ", StringComparison.Ordinal) && session.QueryGold() == coins, "gold pile move: " + _inventoryStatus);
            await Drag(AreaInventoryGridArea.CellCentre(spot), AreaInventoryGridArea.CellCentre(pile.Slot));
            Require(SlotOf(pile.Entity) == pile.Slot && session.QueryGold() == coins, "gold pile move back: " + _inventoryStatus);
            GD.Print($"ARC-UI-090 native-smoke gold-pile PASS coins={coins} cell={pile.Slot} via={spot} name={pile.Name}");
            dragged += 2;
            // ARC-UI-091 (QW1, QW5, IW16): a release on the drop icon asks how many coins, keypad 1 and Enter drop one on the hero's tile (a junk pile when
            // items lie there), and the coin is taken back from the ground (or the pile) into the hero's gold.
            if (coins > 1)
            {
                area.PressAt(AreaInventoryGridArea.CellCentre(pile.Slot));
                Require(area.Held is not null, "the press holds the gold pile");
                area.ReleaseAt(AreaInventoryGridArea.IconCentre(AreaInventoryIcon.Drop));
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Require(_inventoryPanel.QuantityForSelfTest == new QuantityBar(0, coins), "the gold drop asks: " + _inventoryStatus);
                InventoryKey(Key.Kp1); InventoryKey(Key.Enter);
                var coin = LastQuantityMoveForSelfTest?.Item;
                var lying = session.QueryPlacedItems().FirstOrDefault(item => item.Entity == coin);
                Require(LastQuantityMoveForSelfTest is { Disposition: GameSessionDisposition.Changed } && lying is { Kind.Type: SessionItemType.Gold } && lying.Cell == session.QueryActor().Cell
                    && session.QueryGold() == coins - 1 && SlotOf(pile.Entity) == pile.Slot, "one coin dropped on the hero's tile: " + _inventoryStatus);
                var junk = session.QueryJunkPiles().FirstOrDefault(p => p.Items.Any(item => item.Entity == coin));
                var returned = junk is null ? _inventoryHost!.PickUp(session, coin!) : _inventoryHost!.Loot(session, junk.Entity, coin!);
                Require(returned.Disposition == GameSessionDisposition.Changed && session.QueryGold() == coins, "the coin is taken back into the gold: " + returned.Failure);
                if (junk is not null) session.CloseJunkPile(session.Version, junk.Entity);
                _inventoryPanel.Render(InventoryView(null), null);
                GD.Print($"ARC-UI-091 native-smoke gold-drop PASS coins={coins} dropped=1 pile={junk is not null} cell={lying!.Cell}");
            }
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
        await StillPointerForSmoke();
        return (dragged, _inventoryPanel.ViewForSelfTest!);
    }

    /// <summary>ARC-UI-035: the still pointer over a grid item draws its item window in the bars' message window (0x573C74 → 0x57CCF0): the
    /// item's type icon and its name; resting on it again draws nothing new, and resting on a free cell keeps the description.</summary>
    private async Task StillPointerForSmoke()
    {
        static void Require(bool value, string why) { if (!value) { GD.Print("ARC-UI-035 native-smoke inventory-still FAIL " + why); throw new InvalidOperationException("AREA_INVENTORY_SMOKE " + why); } }
        var area = _inventoryPanel!.GridAreaForSelfTest;
        var view = _inventoryPanel.ViewForSelfTest!;
        if (view.Grid.FirstOrDefault() is not { } item) { GD.Print("ARC-UI-035 native-smoke inventory-still SKIP the grid is empty"); return; }
        var bars = _gameBarForSelfTest;
        _gameBarForSelfTest = true;
        try
        {
            for (var i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(_gameBarPanel is { Visible: true } && !_messageLog.PaneMode, "the bars show in the message mode");
            var described = _hotkeyDescriptionsForSelfTest;
            area.StillAt(AreaInventoryGridArea.CellCentre(item.Slot));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var log = _gameBarPanel!.MessageLog;
            Require(_hotkeyDescriptionsForSelfTest == described + 1 && log.ShownKindForSelfTest == AreaMessageKind.Item, $"the still pointer drew {item.Entity}'s item window: {log.ShownKindForSelfTest}");
            Require(log.IconForSelfTest is not null && log.DrawnTextsForSelfTest == ItemWindowTextsForSelfTest && ItemWindowLinesForSelfTest is { Weight.Length: > 0 }, $"the window shows the type icon, the name and the lines (icon {log.IconForSelfTest is not null}, texts {log.DrawnTextsForSelfTest})");
            area.StillAt(AreaInventoryGridArea.CellCentre(item.Slot));
            Require(_hotkeyDescriptionsForSelfTest == described + 1, "resting on the same item again draws nothing new");
            int? free = Enumerable.Range(0, Arcanum.Session.SessionHeroGrid.Slots).Cast<int?>().FirstOrDefault(cell => AreaInventoryDrag.ItemAt(view.Grid, cell!.Value) is null);
            if (free is { } cell) area.StillAt(AreaInventoryGridArea.CellCentre(cell));
            Require(log.ShownKindForSelfTest == AreaMessageKind.Item, "resting on a free cell keeps the description");
            GD.Print($"ARC-UI-035 native-smoke inventory-still PASS item={item.Entity} colour={log.FirstTextColourForSelfTest.ToHtml(false)} free={(free is null ? "-" : free)}");
        }
        finally
        {
            _messageLog.ClearHover();
            _gameBarForSelfTest = bars;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    /// <summary>ARC-UI-025: a right click (its release, IW5) on a worn item does nothing; a usable item (when the hero holds one) is used; a taken item dragged
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
            area.RightReleaseAt(AreaInventoryGridArea.SlotCentre(worn.Slot));
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(LastItemUseForSelfTest is { Gate: AreaItemUseGate.Wearable } && session.Version.Revision == revision, "a right release on a worn item does nothing: " + _inventoryStatus);
        }
        var usable = view.Pc.Where(line => line.Usable).Select(line => line.Entity).ToArray();
        string used = "-";
        if (usable.FirstOrDefault(entity => view.Grid.Any(item => item.Entity == entity)) is { } entity)
        {
            area.RightReleaseAt(AreaInventoryGridArea.CellCentre(view.Grid.First(item => item.Entity == entity).Slot));
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

    /// <summary>
    /// ARC-UI-029 on the original data: an ammunition stack of more than one unit of the hero's (when it holds none, part of an NPC's
    /// stack on a loaded map, a stack lying there, or, on the crash site which holds none, 5 arrows of the installed prototype given to
    /// an NPC there, is lent to it through the session first) is dropped through its Drop button; the quantity window asks,
    /// keypad 1 and Enter drop one unit on the hero's tile, and the unit is taken back. Returns the window's view after it.
    /// </summary>
    // The installed arrows' prototype file (basic 7038 + 20, ammunition type 0; ledger AM3).
    private const int ArrowPrototype = 7058;

    private async Task<AreaInventoryView> QuantityForSmoke()
    {
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-UI-029 native-smoke quantity FAIL " + why);
            throw new InvalidOperationException("AREA_INVENTORY_SMOKE quantity " + why);
        }
        var session = _areaSession!;
        var panel = _inventoryPanel!;
        var lent = "none";
        // The lent units stay with the hero for the later steps (the full smoke passes with them; giving them back would make the NPC a
        // new stack it never had when its whole stack was lent).
        if (session.QueryHeroAmmo().All(stack => stack.Units < 2))
        {
            // Lent from the loaded maps: two units of an NPC's stack, else a whole stack lying on the ground.
            var maps = new[] { CurrentAreaMap }.Concat(AreaRuntimes().Keys).Distinct().Select(map => session.QueryMapInventory(new(map))).Where(inventory => inventory is not null).ToArray();
            static bool Stack(SessionItemFacts facts) => facts.AmmoType is not null && facts.Quantity >= 2;
            var held = maps.SelectMany(inventory => inventory!.Holders.Where(holder => holder.Kind == SessionHolderKind.Npc)
                .SelectMany(holder => holder.Items.Where(item => Stack(item.Facts)).Select(item => (holder.Entity, Item: item)))).FirstOrDefault();
            var ground = maps.SelectMany(inventory => inventory!.Ground.Where(entry => Stack(entry.Item.Facts)).Select(entry => entry.Item)).FirstOrDefault();
            GD.Print($"ARC-UI-029 native-smoke quantity lend npc={held.Item?.Entity ?? "-"} ground={ground?.Entity ?? "-"}");
            InventoryMutationResult taken;
            if (held.Item is not null)
            {
                taken = session.TakeAmmoFromNpc(session.Version, held.Entity, held.Item.Entity, 2, held.Item.Facts.Kind, _inventoryHost!.InsertCheck);
                lent = held.Item.Entity;
            }
            else if (ground is not null) { taken = _inventoryHost!.PickUp(session, ground.Entity); lent = ground.Entity; }
            else
            {
                // The crash site holds no stack: an NPC of the map gets 5 arrows of the installed prototype (file 7058, type 0) first.
                var npc = maps.SelectMany(inventory => inventory!.Holders).FirstOrDefault(holder => holder.Kind == SessionHolderKind.Npc);
                Require(npc is not null && PrototypeItems()?.Item(ArrowPrototype) is { Kind.Type: SessionItemType.Ammo }, "an NPC and the arrow prototype");
                var arrows = PrototypeItems()!.Item(ArrowPrototype)!.Kind;
                var added = session.AddItemToNpc(session.Version, npc!.Entity, arrows, _inventoryHost!.InsertCheck, (0, 5));
                Require(added.Disposition == GameSessionDisposition.Changed, "an NPC's arrows: " + added.Failure);
                taken = session.TakeAmmoFromNpc(session.Version, npc.Entity, added.Item!, 5, arrows, _inventoryHost.InsertCheck);
                lent = $"{ArrowPrototype}x5";
            }
            Require(taken.Disposition == GameSessionDisposition.Changed, "lending: " + taken.Failure);
            panel.Render(InventoryView(null), null);
        }
        var stack = session.QueryHeroAmmo().First(entry => entry.Units >= 2);
        var line = panel.ViewForSelfTest!.Pc.ToList().FindIndex(entry => entry.Entity == stack.Entity);
        Require(line >= 0 && panel.ViewForSelfTest.Pc[line].Units == stack.Units, "the hero's stack is listed with its units");
        panel.DropButtonsForSelfTest[line].EmitSignal(BaseButton.SignalName.Pressed);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Require(panel.QuantityForSelfTest == new QuantityBar(0, stack.Units), "the drop asks: " + _inventoryStatus);
        // ARC-UI-1884 (QW3): with the bars up and the pane's art read, the original's pane in the bottom strip shows the question (background, five buttons,
        // the kind's icon, the count "0"); its buttons step the count as the window's do (up, down), and the diagnostic row is not drawn.
        var barForSelfTest = _gameBarForSelfTest;
        _gameBarForSelfTest = true;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (_gameBarPanel is { Visible: true } && Art(QuantityPaneRules.BackgroundArt) is not null)
        {
            var pane = QuantityPaneForSelfTest;
            Require(pane is not null && pane.BackgroundForSelfTest is not null, "the quantity pane draws its background art 298");
            Require(QuantityPaneRules.Buttons.All(button => Art(button.Art) is null || pane!.ButtonArtForSelfTest(button.Action) is not null), "the pane's buttons draw their art");
            GD.Print($"ARC-UI-1884 native-smoke quantity pane icon={(pane!.IconForSelfTest is { } icon ? $"{icon.GetWidth()}x{icon.GetHeight()}" : "none")} count={pane.CountTextForSelfTest}");
            // ARC_AREA_WINDOW_CAPTURE=PREFIX saves the viewport with the pane (PREFIX-quantity-pane.png) and with the diagnostic row it replaces (PREFIX-quantity-row.png); local, never in git.
            if (System.Environment.GetEnvironmentVariable("ARC_AREA_WINDOW_CAPTURE") is { Length: > 0 } capture)
            {
                async Task Shot(string name)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    var path = System.IO.Path.GetFullPath(capture) + "-quantity-" + name + ".png";
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                    using var image = GetViewport().GetTexture().GetImage();
                    if (image is null || image.SavePng(path) != Error.Ok) throw new InvalidOperationException("ARC-UI-1884 capture not saved: " + path);
                    GD.Print($"ARC-UI-1884 quantity capture {name} {image.GetWidth()}x{image.GetHeight()}");
                }
                QuantityPaneReleased(QuantityPaneAction.Up, true); QuantityPaneReleased(QuantityPaneAction.Up, true);
                await Shot("pane");
                _quantityPaneHiddenForCapture = true;
                await Shot("row");
                _quantityPaneHiddenForCapture = false;
                QuantityPaneReleased(QuantityPaneAction.Down, true); QuantityPaneReleased(QuantityPaneAction.Down, true);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            var pressed = QuantityPanePressesForSelfTest;
            QuantityPaneReleased(QuantityPaneAction.Up, true);
            Require(panel.QuantityForSelfTest == new QuantityBar(1, stack.Units) && QuantityPanePressesForSelfTest == pressed + 1, "the pane's up button adds one");
            QuantityPaneReleased(QuantityPaneAction.Down, true);
            Require(panel.QuantityForSelfTest == new QuantityBar(0, stack.Units), "the pane's down button takes it back");
            GD.Print("ARC-UI-1884 native-smoke quantity-pane PASS");
            // ARC-UI-1818: the opt-in generated-chrome part (ARC_AREA_WINDOW_ART=legacy|hd|generated) for the pane's background and five buttons.
            if (WindowArtSmokeRequested) await QuantityPaneArtSmokeAsync();
        }
        else GD.Print("ARC-UI-1884 native-smoke quantity-pane NONE (the interface bars or the pane's art are not up)");
        _gameBarForSelfTest = barForSelfTest;
        InventoryKey(Key.Kp1); InventoryKey(Key.Enter);
        var dropped = LastQuantityMoveForSelfTest?.Item;
        var placed = session.QueryPlacedItems().FirstOrDefault(item => item.Entity == dropped);
        Require(LastQuantityMoveForSelfTest is { Disposition: GameSessionDisposition.Changed } && placed is not null && placed.Cell == session.QueryActor().Cell
            && session.QueryAmmo(dropped!)?.Units == 1 && session.QueryAmmo(stack.Entity)?.Units == stack.Units - 1, "one unit dropped on the hero's tile: " + _inventoryStatus);
        Require(panel.HeaderForSelfTest.Contains(" | arrows ", StringComparison.Ordinal), "the header counts the ammunition: " + panel.HeaderForSelfTest);
        var pile = session.QueryJunkPiles().FirstOrDefault(p => p.Items.Any(item => item.Entity == dropped));
        var back = pile is null ? _inventoryHost!.PickUp(session, dropped!) : _inventoryHost!.Loot(session, pile.Entity, dropped!);
        Require(back.Disposition == GameSessionDisposition.Changed && session.QueryAmmo(stack.Entity)?.Units == stack.Units, "taken back into the stack: " + back.Failure);
        if (pile is not null) session.CloseJunkPile(session.Version, pile.Entity);
        panel.Render(InventoryView(null), null);
        // ARC-UI-086 (QW1, QW5, IW18, TT2): the stack is lifted onto the game cursor (the round view's map drop): the quantity window asks, keypad 1 and
        // Enter put one unit on the cursor and the window closes; the throw splits that unit off into a new stack that flies and lies hidden until it
        // lands; the smoke gives the flight up and takes the unit back.
        if (session.QueryHeroAmmo().FirstOrDefault(entry => entry.Units >= 2) is { } liftStack && _combatHost is not null && _worldActor.Session is AreaGridSession liftGrid)
        {
            LiftToGameCursor(liftStack.Entity, "smoke");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(panel.QuantityForSelfTest == new QuantityBar(0, liftStack.Units), "the lift asks how many: " + _inventoryStatus);
            InventoryKey(Key.Kp1); InventoryKey(Key.Enter);
            Require(GameCursorItemForSelfTest == liftStack.Entity && GameCursorCountForSelfTest == 1 && !InventoryOpen, $"one unit is on the cursor and the window closed ({GameCursorItemForSelfTest}, {GameCursorCountForSelfTest}, open {InventoryOpen}): {_inventoryStatus}");
            var cursor = _gameCursorItem!; _gameCursorItem = null;
            var from = HeroCellNow(liftGrid);
            var thrownAt = PerformHeroThrow(cursor, new(from.X + 3, from.Y));
            Require(thrownAt is { Disposition: GameSessionDisposition.Changed, Shots.Count: 1 }, $"the unit is thrown ({thrownAt?.Disposition} {thrownAt?.Failure}): {_heldThrowNotice}");
            var shot = thrownAt!.Shots[0];
            var flying = session.QueryThrownShot(shot)!.Value.Item;
            Require(session.QueryAmmo(liftStack.Entity)?.Units == liftStack.Units - 1 && session.QueryAmmo(flying)?.Units == 1 && flying != liftStack.Entity,
                $"one unit was split off into its own stack ({session.QueryAmmo(liftStack.Entity)?.Units} left, {flying} carries {session.QueryAmmo(flying)?.Units})");
            Require(session.QueryItemInFlight(flying), "the split stack is hidden in flight");
            ClearAreaProjectiles("smoke");
            session.EndShot(shot); // the projectile host's give-up has ended it already, or this does
            Require(!session.QueryItemInFlight(flying), "the flight is given up");
            var pickedUp = session.PickUpItem(session.Version, flying, _inventoryHost!.InsertCheck);
            Require(pickedUp.Disposition == GameSessionDisposition.Changed, $"the unit is picked up again ({pickedUp.Failure})");
            OpenInventory();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            panel.Render(InventoryView(null), null);
            GD.Print($"ARC-UI-086 native-smoke ammo-throw PASS stack={liftStack.Entity} units={liftStack.Units} thrown=1 split={flying}");
        }
        // ARC-UI-094 (QW1, QW5, IW18, TT2, HG1): the gold pile's lift as the stack's: the question asks for the coins, keypad 1 and Enter put one coin on the
        // cursor; the throw splits it off into a new pile that flies and lies hidden, lands where the smoke gives the flight up, and a pickup takes it back.
        if (session.QueryGold() >= 2 && _combatHost is not null && _worldActor.Session is AreaGridSession goldGrid)
        {
            if (!InventoryOpen) { OpenInventory(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
            var coins = session.QueryGold();
            LiftToGameCursor(SessionHeroGrid.GoldEntity, "smoke");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(panel.QuantityForSelfTest == new QuantityBar(0, coins), "the gold lift asks how many coins: " + _inventoryStatus);
            InventoryKey(Key.Kp1); InventoryKey(Key.Enter);
            Require(GameCursorItemForSelfTest == SessionHeroGrid.GoldEntity && GameCursorCountForSelfTest == 1 && !InventoryOpen, $"one coin is on the cursor and the window closed ({GameCursorItemForSelfTest}, {GameCursorCountForSelfTest}, open {InventoryOpen}): {_inventoryStatus}");
            var coinCursor = _gameCursorItem!; _gameCursorItem = null;
            var coinFrom = HeroCellNow(goldGrid);
            var coinThrow = PerformHeroThrow(coinCursor, new(coinFrom.X + 3, coinFrom.Y));
            Require(coinThrow is { Disposition: GameSessionDisposition.Changed, Shots.Count: 1 }, $"the coin is thrown ({coinThrow?.Disposition} {coinThrow?.Failure}): {_heldThrowNotice}");
            var coinShot = coinThrow!.Shots[0];
            var coinItem = session.QueryThrownShot(coinShot)!.Value.Item;
            Require(session.QueryGold() == coins - 1 && session.QueryItemInFlight(coinItem), $"one coin left the pile and flies as {coinItem} ({session.QueryGold()} left)");
            ClearAreaProjectiles("smoke");
            session.EndShot(coinShot);
            var coinBack = session.PickUpItem(session.Version, coinItem, _inventoryHost!.InsertCheck);
            Require(coinBack.Disposition == GameSessionDisposition.Changed && session.QueryGold() == coins, $"the coin is picked up into the gold ({coinBack.Failure}, {session.QueryGold()})");
            OpenInventory();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            panel.Render(InventoryView(null), null);
            GD.Print($"ARC-UI-094 native-smoke gold-throw PASS coins={coins} thrown=1 item={coinItem}");
        }
        await JunkPileStackSmoke();
        GD.Print($"ARC-UI-029 native-smoke quantity PASS units={stack.Units} dropped=1 lent={lent} pile={pile is not null} cell={placed!.Cell}");
        return panel.ViewForSelfTest!;
    }

    /// <summary>
    /// ARC-UI-1328 (QW1, QW5, NT1, AM4, JP5): part of a gold pile and of an ammunition stack taken out of a junk pile through the loot window: the hero drops
    /// two gold piles and part of its ammunition stack on its tile (a junk pile), the window opens on the pile, a Take button of the pile of 3 coins asks
    /// (mode 13), keypad 2 and Enter move 2 and leave 1 in the pile, a one-unit line moves at once, the ammunition stack's Take asks too and keeps the rest; what
    /// is left is taken by the buttons, the hero holds what it held before, and closing the window dissolves the pile.
    /// </summary>
    private async Task JunkPileStackSmoke()
    {
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-UI-1328 native-smoke junk-stack FAIL " + why);
            throw new InvalidOperationException("AREA_INVENTORY_SMOKE junk-stack " + why);
        }
        var session = _areaSession!;
        var panel = _inventoryPanel!;
        var coins = session.QueryGold();
        if (coins < 6 || session.QueryHeroAmmo().Cast<SessionAmmoRecord?>().FirstOrDefault(entry => entry!.Value.Units >= 3) is not { } ammo
            || session.QueryHeroAmmo().First(entry => entry.AmmoType == ammo.AmmoType).Entity != ammo.Entity || !HeroLocationKnown())
        {
            GD.Print($"ARC-UI-1328 native-smoke junk-stack SKIP coins={coins} ammo={session.QueryHeroAmmo().Count}");
            return;
        }
        if (!InventoryOpen) { OpenInventory(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        panel.Render(InventoryView(null), null);
        var ammoKind = panel.ViewForSelfTest!.Pc.FirstOrDefault(entry => entry.Entity == ammo.Entity)?.StackKind;
        Require(ammoKind is { Type: SessionItemType.Ammo }, "the hero's stack lists its kind");
        var held = ammo.Units;
        var goldKind = GoldPileKind();
        var coinsA = session.DropHeroGold(session.Version, 3, goldKind);
        var coinsB = session.DropHeroGold(session.Version, 2, goldKind);
        var arrows = session.DropHeroAmmo(session.Version, ammo.Entity, 2, ammoKind!.Value);
        Require(coinsA.Disposition == GameSessionDisposition.Changed && coinsB.Disposition == GameSessionDisposition.Changed && arrows.Disposition == GameSessionDisposition.Changed,
            $"the drops: {coinsA.Failure} {coinsB.Failure} {arrows.Failure}");
        var pile = session.QueryJunkPiles().FirstOrDefault(p => new[] { coinsA.Item, coinsB.Item, arrows.Item }.All(entity => p.Items.Any(item => item.Entity == entity)));
        string[] mine = [coinsA.Item!, coinsB.Item!, arrows.Item!];
        if (pile is null)
        {
            // The tile makes no junk pile of the three (a drop that did not fit): give the items back and leave the step.
            foreach (var entity in mine) if (!session.IsItemDestroyed(entity)) _inventoryHost!.PickUp(session, entity);
            GD.Print("ARC-UI-1328 native-smoke junk-stack SKIP the three drops make no junk pile");
            return;
        }
        EndInventory();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        OpenInventory(pile.Entity);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Require(InventoryOpen && panel.Holder == pile.Entity, "the window opened on the junk pile");
        Button Take(string entity) => panel.TakeButtonsForSelfTest[panel.ViewForSelfTest!.Loot.ToList().FindIndex(line => line.Entity == entity)];
        int Units(string entity) => panel.ViewForSelfTest!.Loot.FirstOrDefault(line => line.Entity == entity)?.Units ?? 0;
        Require(Units(coinsA.Item!) == 3 && Units(coinsB.Item!) == 2 && Units(arrows.Item!) == 2, "the pile lists the stacks with their units");
        var afterDrops = session.QueryGold();
        // Three coins: the question asks (mode 13), 2 move and 1 stays in the pile.
        Take(coinsA.Item!).EmitSignal(BaseButton.SignalName.Pressed);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Require(panel.Asking && panel.QuantityForSelfTest == new QuantityBar(0, 3) && TargetModeOpen(InterfaceMode.QuantityPicker), "the pile's coins ask: " + _inventoryStatus);
        InventoryKey(Key.Kp2); InventoryKey(Key.Enter);
        Require(LastQuantityMoveForSelfTest is { Disposition: GameSessionDisposition.Changed } && session.QueryGold() == afterDrops + 2 && Units(coinsA.Item!) == 1
            && session.QueryJunkPiles().Any(p => p.Entity == pile.Entity && p.Items.Any(item => item.Entity == coinsA.Item)) && !panel.Asking, "2 of 3 coins moved, 1 stays: " + _inventoryStatus);
        // The one coin left moves at once (no question); the pile stands with the window.
        Take(coinsA.Item!).EmitSignal(BaseButton.SignalName.Pressed);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Require(!panel.Asking && session.QueryGold() == afterDrops + 3 && session.IsItemDestroyed(coinsA.Item!) && Units(coinsA.Item!) == 0 && InventoryOpen, "the last coin moved at once: " + _inventoryStatus);
        // The other pile of 2 coins: the question asks and the whole pile moves (the pile's item is destroyed), then the arrows.
        Take(coinsB.Item!).EmitSignal(BaseButton.SignalName.Pressed);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Require(panel.Asking && panel.QuantityForSelfTest == new QuantityBar(0, 2), "the second pile asks: " + _inventoryStatus);
        InventoryKey(Key.Kp2); InventoryKey(Key.Enter);
        Require(session.QueryGold() == afterDrops + 5 && session.IsItemDestroyed(coinsB.Item!), "the whole second pile moved: " + _inventoryStatus);
        Require(session.QueryGold() == coins, $"the hero holds the coins it had again: {session.QueryGold()} of {coins}");
        // Two arrows: the question asks, 1 moves into the hero's stack and 1 stays in the pile.
        var heroUnits = session.QueryAmmo(ammo.Entity)!.Value.Units;
        Take(arrows.Item!).EmitSignal(BaseButton.SignalName.Pressed);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Require(panel.Asking && panel.QuantityForSelfTest == new QuantityBar(0, 2), "the pile's arrows ask: " + _inventoryStatus);
        InventoryKey(Key.Kp1); InventoryKey(Key.Enter);
        Require(session.QueryAmmo(ammo.Entity)?.Units == heroUnits + 1 && session.QueryAmmo(arrows.Item!)?.Units == 1 && Units(arrows.Item!) == 1, "1 of 2 arrows moved, 1 stays: " + _inventoryStatus);
        Take(arrows.Item!).EmitSignal(BaseButton.SignalName.Pressed);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Require(session.QueryAmmo(ammo.Entity)?.Units == held && session.IsItemDestroyed(arrows.Item!), "the last arrow moved at once: " + _inventoryStatus);
        EndInventory();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Require(session.QueryJunkPiles().All(p => p.Entity != pile.Entity || p.Items.Count > 0) && session.QueryGold() == coins, "the window closed on the pile");
        OpenInventory();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        panel.Render(InventoryView(null), null);
        GD.Print($"ARC-UI-1328 native-smoke junk-stack PASS pile={pile.Entity} coins={coins} arrows={held}");
    }
}
