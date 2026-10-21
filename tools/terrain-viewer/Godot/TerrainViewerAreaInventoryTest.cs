using System.Reflection;
using Arcanum.Data.Area;
using Arcanum.Session;
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
            _inventoryPanel.Render(new AreaInventoryView([new("syn:item", "bandage") { Healing = true }, new("syn:listed", "sword")], null, null, []), null);
            Require(_inventoryPanel.UseButtonsForSelfTest.Count == 1, "one use button for the one healing line");
            _inventoryPanel.UseButtonsForSelfTest[0].EmitSignal(BaseButton.SignalName.Pressed);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(_inventoryStatus == "not used: not a healing item the hero holds" && _inventoryPanel.UseButtonsForSelfTest.Count == 0,
                "the use button ran the app's heal: " + _inventoryStatus);
            InventoryKey(Key.Escape);
            Require(!InventoryOpen, "Escape closed the loot window");
            return $"walk to pick up and its ground-click cancel, pickup at reach {AreaInventoryHost.Reach}, I/Escape toggle, loot by take button, heal use button";
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
                PrintedLines = printed,
                Pc = _areaSession.QueryPcInventory().Count, Named = view.Pc.Count, Parity = "NOT_VERIFIED (diagnostic window, no grid or weight)" }));
        }
        finally
        {
            HoverReleaseForSelfTest();
            if (InventoryOpen) EndInventory();
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
        }
    }
}
