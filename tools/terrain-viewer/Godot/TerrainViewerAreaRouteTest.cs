using Arcanum.Data.Traversal;
using Arcanum.Input;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    /// <summary>
    /// ARC-WORLD-007 stage 2 over the strict synthetic AREA fixture (blocker at 5,1): LMB clicks through the production
    /// unhandled-input seam and native GUI dispatch set route goals; the route drives the authoritative session at exact
    /// step ends; the stick, the palette and other quarantines stop it, and a started step always ends at a cell centre.
    /// </summary>
    internal async Task<object> AreaRouteMatrixForSelfTestAsync(SubViewport viewport, TerrainLoadResult result)
    {
        if (!SelfTestBootstrap || _readyArea is null || _readyMode != TerrainViewerMode.CrashSiteArea || result.AreaStepSnapshot is null)
            throw new InvalidOperationException("AREA_ROUTE_TEST_READY_REQUIRED");
        var simulatedFocus = _worldFocusSimulationForSelfTest;
        _worldFocusSimulationForSelfTest = true;
        try { return await AreaRouteMatrixScriptAsync(viewport, result.AreaStepSnapshot); }
        finally
        {
            _worldAdvanceObserverForSelfTest = null;
            if (WorldModal) ToggleWorldSettings();
            ReleaseSimulatedPadForSelfTest();
            _worldFocusSimulationForSelfTest = simulatedFocus;
        }
    }

    private async Task<object> AreaRouteMatrixScriptAsync(SubViewport viewport, AreaStepSnapshot steps)
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("AREA route: " + why); }
        var level = StickLevel(default);
        var events = new List<MovementEvent>();
        var cases = new List<object>();
        var names = new List<string>();
        var time = 0;
        _worldAdvanceObserverForSelfTest = advance => events.AddRange(advance.Movement.Events);
        void Frame() { time++; WorldFrameForSelfTest(new(-1, default), new[] { level }, .1, true, SimulationTime.At(time)); }
        void Run(int frames) { for (var i = 0; i < frames; i++) Frame(); }
        void Stick(bool down, params Key[] keys)
        {
            _worldTestTime = SimulationTime.At(time);
            foreach (var key in keys) SimulatedSectorKey(key, down);
            level = SimulatedPadLevel();
        }
        MovementState State() => _worldActor.Session!.State;
        RouteFollower Route() => _worldRoute ?? throw new InvalidOperationException("AREA route: follower missing");
        CellCoord[] Committed() => events.Where(e => e.Kind == MovementEventKind.CellCommitted).Select(e => e.Key.To).ToArray();
        bool Interrupted() => events.Any(e => e.Kind is MovementEventKind.StepCancelled or MovementEventKind.StepRejected);
        Vector2 Screen(CellCoord cell, double dx = 0, double dy = 0)
        {
            var center = AreaCellPicking.Center(cell);
            return _canvas!.GetGlobalTransformWithCanvas() * new Vector2((float)(center.X + dx), (float)(center.Y + dy));
        }
        void Click(CellCoord cell, double dx = 0, double dy = 0)
        {
            _worldTestTime = SimulationTime.At(time);
            _UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = Screen(cell, dx, dy) });
        }
        void Prepare()
        {
            if (WorldModal) ToggleWorldSettings();
            GetViewport().GuiReleaseFocus(); RetireWorldLifecycle();
            _worldSettings = DiagnosticWorldPolicy.DefaultInputSettings;
            _worldActor.Configure(steps, SimulationTime.At(0), StepDuration.Of(4));
            _worldGridInput = new(); _worldTestTime = SimulationTime.At(0); time = 0; _worldFocused = true;
            SelectSimulatedPadForSelfTest(); level = StickLevel(default);
            Run(3);
            Require(_worldGate.IsOpen, "two complete neutral frames open the gate");
            PublishWorldSelection(); _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
            events.Clear();
        }
        void Settled(CellCoord cell, string why) => Require(State().Phase == MovementPhase.Settled && State().CommittedCell == cell
            && State().LatestIntent == ActorIntent.Neutral, $"{why}: settled at {State().CommittedCell} {State().Phase} intent {State().LatestIntent}");
        void Case(string name) { names.Add(name); cases.Add(new { Name = name, Committed = Committed().Select(c => $"{c.X},{c.Y}").ToArray(),
            Final = $"{State().CommittedCell.X},{State().CommittedCell.Y}", Route = DescribeWorldRoute(Route()) }); }

        // Detour: the straight step west hits the blocker at (5,1); the click goal is still reached.
        Prepare(); Click(new(3, 1), dx: 25, dy: 5); // any point inside the diamond picks its cell
        Require(Route().Target == new CellCoord(3, 1) && Route().Status == RouteFollowStatus.Following, "click picked its diamond");
        Run(40);
        Require(Route().Status == RouteFollowStatus.Arrived, "detour arrived: " + DescribeWorldRoute(Route()));
        Settled(new(3, 1), "detour");
        Require(!Committed().Contains(new CellCoord(5, 1)) && !Interrupted(), "detour never enters the blocker or interrupts a step");
        Require(_readyActorInput is { } placed && placed.MapX == 3 && placed.MapY == 1, "presentation follows the authoritative session");
        Require(GetSelfTestSnapshot().Hud.Contains("LMB click-to-move route Arrived target=", StringComparison.Ordinal), "HUD route status");
        Case("detour");

        // A new click mid-step: the started step ends at its cell centre, then the new goal is walked.
        Prepare(); Click(new(10, 1)); Run(2);
        Require(State().Segment is { } first && first.To == new CellCoord(7, 1), "first step east in progress");
        Click(new(6, 4)); Run(40);
        Settled(new(6, 4), "retarget");
        Require(Committed().FirstOrDefault() == new CellCoord(7, 1) && !Interrupted(), "started step completed before the new goal");
        Case("retarget-mid-step");

        // The same goal again is kept, not replanned.
        Prepare(); Click(new(9, 1)); Run(2); var planned = Route().PlannedSegments;
        Click(new(9, 1));
        Require(Route().PlannedSegments == planned && Route().Status == RouteFollowStatus.Following, "same goal kept");
        Run(30); Settled(new(9, 1), "same goal");
        Case("same-goal");

        // The stick takes over at once, even in the route's own direction, and keeps its intent.
        Prepare(); Click(new(14, 1)); Run(6);
        var before = State().CommittedCell;
        Stick(true, Key.S, Key.A); Frame();
        Require(Route().Status == RouteFollowStatus.Stopped && Route().StopReason == "ROUTE_STOPPED_BY_STICK", "stick stops the route");
        Run(16);
        Require(State().CommittedCell.X >= before.X + 3 && State().LatestIntent == ActorIntent.Toward(NeighborDirection.XPlus),
            "the stick keeps walking: the stopped route never clears its intent");
        Stick(false, Key.S, Key.A); Run(12);
        Require(State().Phase == MovementPhase.Settled, "stick release settles");
        Case("stick-takeover");

        // The palette (a quarantine) stops the route; the started step ends at its cell centre.
        Prepare(); Click(new(14, 1)); Run(6);
        var stepping = State().Segment ?? throw new InvalidOperationException("AREA route: palette case needs a step in progress");
        ToggleWorldSettings();
        Require(WorldModal && Route().Status == RouteFollowStatus.Stopped && Route().StopReason == "ROUTE_STOPPED_QUARANTINE", "palette stops the route");
        Run(10);
        Settled(stepping.To, "palette");
        Require(!Interrupted(), "palette never cancels the started step");
        Click(new(3, 1));
        Require(Route().Target == new CellCoord(14, 1) && Route().Status == RouteFollowStatus.Stopped, "a click under the palette is ignored");
        ToggleWorldSettings(); Run(3);
        Require(!WorldModal && _worldGate.IsOpen && State().CommittedCell == stepping.To, "palette closed, no motion");
        Case("palette-stop");

        // An occupied goal stops with the planner's reason and no motion.
        Prepare(); Click(new(5, 1)); Run(6);
        Require(Route().Status == RouteFollowStatus.Stopped && Route().StopReason == "ROUTE_NOT_FOUND", "occupied goal reason");
        Settled(new(6, 1), "occupied goal");
        Require(!events.Any(e => e.Kind == MovementEventKind.StepStarted), "no step toward an occupied goal");
        Case("occupied-goal");

        // Native dispatch: a ground click reaches _UnhandledInput; a click on the HUD panel is GUI's.
        Prepare();
        var hud = _hud!.Rect;
        var ground = Screen(new(12, 6));
        Require(hud.Size.X > 0 && hud.Size.Y > 0 && !hud.HasPoint(ground) && viewport.GetVisibleRect().HasPoint(ground), $"ground point {ground} outside HUD {hud}");
        async Task Press(Vector2 at)
        {
            viewport.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at });
            viewport.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        await Press(ground);
        Require(Route().Target == new CellCoord(12, 6) && Route().Status == RouteFollowStatus.Following, "native ground click sets the goal");
        await Press(hud.GetCenter());
        Require(Route().Target == new CellCoord(12, 6), "a HUD click never sets a route goal");
        Run(40); Settled(new(12, 6), "native click");
        Case("native-dispatch");

        GD.Print("ARC-WORLD-007 AREA route synthetic PASS cases=" + string.Join(",", names));
        return new { Schema = "arcanum-area-route-synthetic-v1", Task = "ARC-WORLD-007", Outcome = "PASS", Cases = cases,
            Picking = "chart diamond centre (40(y-x)+40, 20(y+x)+20); actor foot projection 40 px left, retained",
            OriginalPathParity = "NOT_VERIFIED", Physical = "NOT_VERIFIED" };
    }
}
