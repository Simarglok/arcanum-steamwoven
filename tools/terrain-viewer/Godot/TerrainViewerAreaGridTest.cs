using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Arcanum.Data.Area;
using Arcanum.Data.Terrain;
using Arcanum.Data.Traversal;
using Arcanum.Input;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.TerrainViewer.Tests;
using Arcanum.World;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public static partial class TerrainViewerSelfTest
{
    private static async Task AssertAreaGridAsync(Node host)
    {
        using var source = new AreaOwnerFixture(motionBodies: true);
        var read = typeof(TerrainLoadCoordinator).Assembly.GetType("Arcanum.TerrainViewer.Core.AreaLoadOperation")!
            .GetMethod("ReadArea", BindingFlags.NonPublic | BindingFlags.Static)!;
        var area = (AreaLoadResult)read.Invoke(null, [7L, source.Owner, CancellationToken.None, null])!;
        // Whole-core step evidence over the same strict publication; a second snapshot leaves tile names Unknown.
        var module = source.Streams[new(AreaSourceRole.Module)].Bytes;
        var steps = AreaOwnerFixture.Steps(area.Snapshot, module, 7);
        var unknown = AreaOwnerFixture.Steps(area.Snapshot, module, 7, tileNamesResolved: false);
        source.Owner.Dispose();
        var human = SyntheticActorFixture.Create(human: true, area: true);
        var result = human.Result with { Generation = 7, Area = area, AreaStepSnapshot = steps };
        var arguments = TerrainViewerArguments.Parse(["--mode", "crash-site-area", "--module", "synthetic-module",
            "--resource", "synthetic-terrain", "--actor-resource", "synthetic-actor", "--prototypes", "synthetic-prototypes",
            "--area-mobile", "synthetic-mobile", "--area-effects", "synthetic-effects", "--area-patch", "synthetic-patch", "--source-id", "synthetic"]);
        var viewport = new SubViewport { Size = new(800,600), TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        var app = new TerrainViewerApp { SelfTestBootstrap = true };
        viewport.AddChild(app); host.AddChild(viewport);
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        app.SetProcess(false); app.SetProcessInput(false);
        try
        {
            app.PublishSyntheticForSelfTest(arguments, result);
            var usage = source.Owner.Usage;
            app.WorldGridObservationMatrixForSelfTest();
            app.WorldPaletteChordMatrixForSelfTest();
            // ARC-RENDER-008 on the whole-core authority: blocker (5,1), Open (7,1), (-1,1) outside the admitted sectors.
            await app.WorldPassabilityOverlayMatrixForSelfTestAsync(new(5, 1), new(7, 1), new(-1, 1), "crash-site-area");
            var gridReport = await app.AreaGridMatrixForSelfTestAsync(arguments, result, unknown);
            var route = await app.AreaRouteMatrixForSelfTestAsync(viewport, result);
            var camera = await app.AreaCameraMatrixForSelfTestAsync(arguments, result);
            var report = new { Grid = gridReport, Route = route, Camera = camera };
            if (source.Owner.Usage != usage || source.Streams.Values.Any(stream => stream.DisposeCount != 1))
                throw new InvalidOperationException("AREA_GRID_PROVIDER_OWNERSHIP");
            var path = System.Environment.GetEnvironmentVariable("ARC_AREA_GRID_REPORT");
            if (!string.IsNullOrWhiteSpace(path)) File.WriteAllText(path, JsonSerializer.Serialize(report));
        }
        finally
        {
            app.ClearSyntheticForSelfTest();
            var final = app.GetSelfTestSnapshot();
            if (final.CacheCount != 0 || final.CreatedTextures != final.DisposedTextures)
                throw new InvalidOperationException("AREA_GRID_TEXTURE_OWNERSHIP");
            viewport.Free();
        }
        GD.Print("ARC-RENDER-007 AREA grid synthetic PASS production strict owner/publication/Godot callbacks/full SIMULATION frames/literal traces/held-turn-reinput/denials/atomic faults/native moving overlap; original AREA integration NOT_VERIFIED; physical NOT_VERIFIED");
    }
}

public partial class TerrainViewerApp
{
    internal async Task<object> AreaGridMatrixForSelfTestAsync(TerrainViewerArguments arguments,
        TerrainLoadResult result, AreaStepSnapshot unknown)
    {
        if (!SelfTestBootstrap || _readyArea is null || _readyMode != TerrainViewerMode.Area)
            throw new InvalidOperationException("AREA_GRID_TEST_READY_REQUIRED");
        // This matrix sets _worldFocused itself. Native window focus belongs to
        // the separate main-window probe; the rendered awaits below must not
        // let whichever application the desktop focuses quarantine the script.
        var simulatedFocus = _worldFocusSimulationForSelfTest;
        _worldFocusSimulationForSelfTest = true;
        try { return await AreaGridMatrixScriptAsync(arguments, result, unknown); }
        finally { _worldFocusSimulationForSelfTest = simulatedFocus; }
    }
    private async Task<object> AreaGridMatrixScriptAsync(TerrainViewerArguments arguments,
        TerrainLoadResult result, AreaStepSnapshot unknown)
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("AREA grid: " + why); }
        var none = new RawInputFrame(-1, default);
        var level = none;
        var events = new List<MovementEvent>();
        var evidence = new List<AreaGridStepEvidence>();
        var cases = new List<object>();
        var views = new List<object>();
        // ARC-INPUT-001: the selected pad's stick replays the former S/A/W/D key
        // edges callback for callback; frames carry its held level.
        void Frame(int time)
        {
            WorldFrameForSelfTest(none, new[] { level }, .1, true, SimulationTime.At(time));
            if (_worldActor.Session?.LastAdvance is not { } advance) return;
            events.AddRange(advance.Movement.Events.Where(e => !events.Any(old => old.Ordinal == e.Ordinal)));
            evidence.AddRange(advance.AreaEvidence.Where(e => !evidence.Any(old => old.Key == e.Key)));
        }
        void Keys(int time, bool down, params Key[] keys)
        {
            _worldTestTime = SimulationTime.At(time);
            foreach (var key in keys) SimulatedSectorKey(key, down);
            level = SimulatedPadLevel();
        }
        void Prepare(AreaStepSnapshot? snapshot = null, MovementLimits? limits = null)
        {
            if (_readyArea is null) PublishSyntheticForSelfTest(arguments, result);
            if (WorldModal) ToggleWorldSettings();
            GetViewport().GuiReleaseFocus(); RetireWorldLifecycle();
            _worldSettings = DiagnosticWorldPolicy.DefaultInputSettings;
            _worldActor.Configure(snapshot ?? result.AreaStepSnapshot!, SimulationTime.At(0), StepDuration.Of(8), limits);
            _worldGridInput = new(); _worldTestTime = SimulationTime.At(0); _worldFocused = true;
            SelectSimulatedPadForSelfTest(); level = StickLevel(default);
            Frame(1); Frame(2); Frame(3);
            Require(_worldGate.IsOpen, "two complete neutral frames");
            PublishWorldSelection(); _camera!.ResetWorld(WorldAnchor, WorldViewport, 2);
            events.Clear(); evidence.Clear();
        }
        string Trace() => string.Join("|", events.Select(e => $"{e.Kind}@{e.Time.Value.Numerator}/{e.Time.Value.Denominator}:{e.Key.From.X},{e.Key.From.Y}>{e.Key.To.X},{e.Key.To.Y}"));
        void Case(string name)
        {
            var state = _worldActor.Session!.State;
            cases.Add(new { Name = name, Trace = Trace(), state.Anchor, state.CommittedCell, state.Phase,
                Frame = _worldActor.CurrentFrame, state.Action, state.Facing, Evidence = evidence.ToArray() });
        }
        const string first = "StepStarted@10/1:6,1>7,1|ActionChanged@10/1:6,1>7,1";
        const string entry = "|CellCommitted@14/1:6,1>7,1";
        Prepare(); Keys(10,true,Key.S,Key.A); Frame(14); Frame(15);
        Require(Trace() == first + entry && _worldActor.Session!.State.Anchor.U == ExactScalar.Create(13,2), "held first entry");
        Frame(19); Frame(20);
        Require(Trace() == first + entry + "|CenterArrived@18/1:6,1>7,1|StepStarted@18/1:7,1>8,1"
            && _worldActor.CurrentFrame == 3 && _worldActor.IsPlaying, "held renewal/continuous 17FPS 10-frame clock");
        Keys(20,false,Key.S,Key.A); Frame(23); Frame(24);
        Require(_worldActor.Session!.State.CommittedCell == new CellCoord(7,1)
            && _worldActor.Session.State.Phase == MovementPhase.Settled, "second step early return");
        Case("held-renewal");

        Prepare(); Keys(10,true,Key.S,Key.A); Keys(16,false,Key.S,Key.A); Keys(16,true,Key.S,Key.D); Frame(19); Frame(20);
        Require(Trace() == first + entry + "|CenterArrived@18/1:6,1>7,1|StepStarted@18/1:7,1>7,2|FacingChanged@18/1:7,1>7,2"
            && _worldActor.CurrentFrame == 3 && _worldActor.State.Facing == 3 && _worldActor.State.Action == ActorDiagnosticAction.Walk,
            "turn at arrival keeps Walk clock, no artificial Idle event");
        Case("held-turn");

        Prepare(); Keys(10,true,Key.S,Key.A); Keys(12,false,Key.S,Key.A); Keys(13,true,Key.S,Key.A); Frame(14); Frame(15);
        Require(Trace() == first + "|StepCancelled@12/1:6,1>7,1|FacingChanged@12/1:6,1>7,1|CenterArrived@14/1:6,1>7,1|StepStarted@14/1:6,1>7,1|FacingChanged@14/1:6,1>7,1"
            && _worldActor.Session!.State.CommittedCell == new CellCoord(6,1) && _worldActor.CurrentFrame == 8,
            "reinput cannot unlatch return, renewal only at return center with continuous Walk");
        Case("return-reinput");

        foreach (var denial in new[] { "Blocked", "Unknown", "Unsupported" })
        {
            Prepare(denial == "Unknown" ? unknown : null);
            if (denial == "Unsupported")
            {
                // W+A is map YMinus: (6,1)->(6,0) is admitted; (6,-1) lies in an absent core sector and is
                // refused like a map edge, not rerouted.
                Keys(10,true,Key.W,Key.A); Frame(19); Frame(20);
                Require(events.Count(e => e.Kind == MovementEventKind.StepStarted) == 1
                    && _worldActor.Session!.State.CommittedCell == new CellCoord(6,0), "admitted edge, no enlargement past the core");
            }
            else { Keys(10,true,denial == "Blocked" ? [Key.W,Key.D] : [Key.S,Key.A]); Frame(11); Frame(12); }
            var rejected = events.Last(e => e.Kind == MovementEventKind.StepRejected);
            var denied = evidence.Single(e => e.Key == rejected.Key).Assessment;
            Require(denied.Coverage.ToString() == (denial == "Blocked" ? "Supported" : denial)
                && denied.Disposition?.ToString() == (denial == "Blocked" ? "Blocked" : null), denial + " distinct Data outcome");
            var state = _worldActor.Session!.State; var count = events.Count;
            Frame(30); Frame(31);
            Require(events.Count == count && _worldActor.Session.State.CommittedCell == state.CommittedCell
                && _worldActor.Session.State.Phase == MovementPhase.Settled && !_worldActor.IsPlaying,
                denial + " no held retries/stale steps/clock");
            Case(denial);
        }

        foreach (var fault in new[] { "queue-capacity", "replay-capacity", "late-callback" })
        {
            Prepare(limits: fault == "queue-capacity" ? new MovementLimits(inputs: 2)
                : fault == "replay-capacity" ? new MovementLimits(assessments: 1) : null);
            var retired = _worldActor.Session!;
            var previous = retired.State; var priorAdvance = retired.LastAdvance;
            var expected = fault == "queue-capacity" ? "GRID_OBSERVATION_CAPACITY"
                : fault == "replay-capacity" ? "GRID_REPLAY_CAPACITY_EXCEEDED" : "GRID_OBSERVATION_NONMONOTONIC";
            Exception? failure = null;
            try
            {
                Keys(10,true,Key.S,Key.A);
                if (fault == "queue-capacity") Keys(11,false,Key.S);
                else if (fault == "replay-capacity")
                {
                    Frame(30); previous = retired.State; priorAdvance = retired.LastAdvance;
                    Frame(31);
                }
                else Keys(9,false,Key.S);
            }
            catch (Exception error) when (error is InvalidOperationException or GridReplayCapacityException) { failure = error; }
            Require(failure?.Message == expected, fault + " exact fault cause");
            Require(ReferenceEquals(retired.State, previous) && ReferenceEquals(retired.LastAdvance, priorAdvance)
                && _worldActor.Session is null && _readyArea is null && _readyActor is null && _cache.Count == 0
                && _cache.CreatedCount == _cache.DisposedCount && !_worldGate.IsOpen,
                fault + " atomic previous state/evidence; production failure clears all scene/texture/queue ownership");
            Require(GetSelfTestSnapshot().Hud.Contains("Diagnostic mode area | failure stage=ActorPlan", StringComparison.Ordinal),
                fault + " failure retains AREA mode, not misleading WORLD readiness");
            cases.Add(new { Name = fault, Cause = failure!.Message, PreviousUnchanged = true, SceneCleared = true, FabricatedArrival = false });
        }

        // Literal world witnesses from the independent area_motion_oracle.py,
        // not produced placement/ordering/nearest data. The strict wire canopy
        // at cell6,1 has dest(-214,120,128,160) and ranks(97,-17). The actor is
        // behind it in map cell6 (second rank -20/-21) and ahead in cell7.
        // The independent oracle output is generated before native readback and
        // pinned by digest; every literal below must equal its value.
        var oraclePath = System.Environment.GetEnvironmentVariable("ARC_AREA_MOTION_ORACLE")
            ?? throw new InvalidOperationException("AREA_MOTION_ORACLE_REQUIRED");
        var oracleBytes = File.ReadAllBytes(oraclePath);
        Require(Convert.ToHexString(SHA256.HashData(oracleBytes)).ToLowerInvariant()
            == "f5b26e1784a392e4ed6b299090912e6c7b800a16ad005880c5b95fe018b40f9e", "motion oracle identity");
        var oracle = JsonDocument.Parse(oracleBytes).RootElement.Clone();
        static int[] Ints(JsonElement value) => value.EnumerateArray().Select(item => item.GetInt32()).ToArray();
        JsonElement OracleView(string name) => oracle.GetProperty("views").EnumerateArray().Single(view => view.GetProperty("name").GetString() == name);
        Require(Ints(oracle.GetProperty("zooms")).SequenceEqual([1,2,3]), "motion oracle zooms");
        int[] canopyRed = [231,37,19,255];
        async Task Render(string name, int x, int y, int[] actor, bool inFront)
        {
            var expected = inFront ? actor : canopyRed;
            var bound = OracleView(name);
            Require(Ints(bound.GetProperty("witness")).SequenceEqual([x,y]) && bound.GetProperty("inFront").GetBoolean() == inFront
                && Ints(bound.GetProperty("winner")).SequenceEqual(expected) && Ints(bound.GetProperty("actorColor")).SequenceEqual(actor),
                name + " literal witness differs from the independent oracle");
            var samples = new List<int[]>();
            foreach (var zoom in new[] { 1, 2, 3 })
            {
                _camera!.ResetWorld(WorldAnchor, WorldViewport, zoom);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                var transform = _canvas!.GetGlobalTransformWithCanvas();
                var point = transform * new Vector2(x + .5f,y + .5f);
                var sx = (int)MathF.Floor(point.X); var sy = (int)MathF.Floor(point.Y);
                using var image = GetViewport().GetTexture().GetImage();
                Require(image.GetSize() == new Vector2I(800,600) && sx >= 0 && sy >= 0 && sx < 800 && sy < 600, "actual native witness viewport");
                void Pixel(Image pixels, int[] rgba, string view)
                {
                    var c = pixels.GetPixel(sx,sy);
                    var actual = new[] { (int)Math.Round(c.R*255),(int)Math.Round(c.G*255),(int)Math.Round(c.B*255),(int)Math.Round(c.A*255) };
                    Require(actual.SequenceEqual(rgba), $"{name} zoom{zoom} {view} pixel {string.Join(',',actual)} expected {string.Join(',',rgba)}");
                }
                Pixel(image,expected,"winning");
                _canvas.SetStaticVisibilityForReadback(false);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using(var actorOnly = GetViewport().GetTexture().GetImage()) Pixel(actorOnly,actor,"actor-only");
                _canvas.SetStaticVisibilityForReadback(true); _canvas.SetActorVisibilityForReadback(false);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using(var canopyOnly = GetViewport().GetTexture().GetImage()) Pixel(canopyOnly,canopyRed,"canopy-only");
                _canvas.SetActorVisibilityForReadback(true);
                samples.Add([zoom,sx,sy]);
            }
            _camera!.ResetWorld(WorldAnchor, WorldViewport, 2);
            views.Add(new { Name = name, World = new[] { x,y }, Screens = samples, Winner = expected, NegativeControls = "PASS", ActualViewport = new[] {800,600} });
        }
        void Placement(string name, TerrainRect expected)
        {
            GD.Print($"ARC-RENDER-007 AREA grid actual {name} actor={_readyActor?.Destination}");
            var rect = Ints(OracleView(name).GetProperty("actorRect"));
            Require(new TerrainRect(rect[0],rect[1],rect[2],rect[3]) == expected, name + " literal placement differs from the independent oracle");
            Require(_readyActor?.Destination == expected, $"{name} simulation placement actual={_readyActor?.Destination} expected={expected}");
        }
        int[] idleRight = [24,218,236,255], walkLeft = [18,197,71,255], walkRight = [237,58,184,255];
        Prepare(); _hud!.Visible = false;
        var canopy = _readyArea!.Plan.Commands.Single(c => c.Identity?.Sector == 86570436012 && c.Identity?.SourceIndex == 1);
        GD.Print($"ARC-RENDER-007 AREA grid actual canopy={canopy.Destination} ranks=({canopy.FirstRank},{canopy.SecondRank})");
        Require(Ints(oracle.GetProperty("canopyRect")).SequenceEqual([-214,120,128,160]) && Ints(oracle.GetProperty("canopyRanks")).SequenceEqual([97,-17]),
            "canopy literal differs from the independent oracle");
        Require(canopy.Destination == new TerrainRect(-214,120,128,160) && canopy.FirstRank == 97 && canopy.SecondRank == -17,
            $"wire canopy literal placement actual={canopy.Destination} ranks=({canopy.FirstRank},{canopy.SecondRank})");
        Placement("Idle-behind", new TerrainRect(-164,157,9,7));
        await Render("Idle-behind",-157,158,idleRight,false);
        Keys(10,true,Key.S,Key.A); Frame(12); Frame(13); // complete12 => anchor6.25,1
        Placement("Walk-behind", new TerrainRect(-174,162,9,7));
        await Render("Walk-behind",-173,163,walkLeft,false);
        Keys(14,false,Key.S,Key.A); Frame(19); Frame(20);
        Require(_worldActor.Session!.State.CommittedCell == new CellCoord(7,1), "entry-equality finishes at data-derived cell7");
        Placement("Idle-front", new TerrainRect(-204,177,9,7));
        await Render("Idle-front",-197,178,idleRight,true);
        Keys(21,true,Key.S,Key.A); Frame(23); Frame(24); // complete23 => anchor7.25,1 in map cell7
        Placement("Walk-front", new TerrainRect(-214,182,9,7));
        await Render("Walk-front",-207,183,walkRight,true);
        Keys(24,false,Key.S,Key.A); Frame(28); Frame(29);
        Require(_worldActor.Session!.State.CommittedCell == new CellCoord(7,1)
            && _worldActor.Session.State.Phase == MovementPhase.Settled, "outbound7 early return settles at cell7");
        Keys(29,true,Key.W,Key.D); Frame(31); Frame(32); // complete31 => anchor6.75,1 in map cell6, mirrored
        Placement("Walk-return-behind", new TerrainRect(-194,172,9,7));
        // Mirrored source column1 (left green) lands at destination column7.
        await Render("Walk-return-behind",-187,173,walkLeft,false);
        Case("native-moving-overlap");
        _hud.Visible = true; ReleaseSimulatedPadForSelfTest();
        return new { Schema = "arcanum-area-grid-synthetic-v1", Task = "ARC-RENDER-007", Outcome = "PASS", result.Generation,
            Source = "public strict FILE/DAT/ART area fixture and its whole-core T0 step evidence", Commands = result.Area!.Plan.Commands.Count,
            ControlledProfile = "HumanFixed", MainViewport = GetWindow().GetVisibleRect().Size.ToString(), Cases = cases, Views = views,
            CallbackSafety = "literal D8 matrix PASS: between-frame/same-tick/focus/modal/disconnect/incomplete early/equality/late",
            OriginalAreaIntegration = "NOT_VERIFIED", Physical = "NOT_VERIFIED", FullDelivery = "NOT_READY" };
    }
}
